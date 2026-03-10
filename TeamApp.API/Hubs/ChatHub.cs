using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeamApp.API.Data;
using TeamApp.API.Models;
using TeamApp.API.Services;

namespace TeamApp.API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _context;
    private readonly ConnectionTracker _tracker;

    public ChatHub(AppDbContext context, ConnectionTracker tracker)
    {
        _context = context;
        _tracker = tracker;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var upn = Context.User?.Identity?.Name;
        
        if (!string.IsNullOrEmpty(userId))
        {
            // Join a group specifically for this user to receive direct DMs
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
        }

        if (!string.IsNullOrEmpty(upn))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"UserUPN_{upn.ToLowerInvariant()}");

            bool isFirstConnection = _tracker.UserConnected(upn, Context.ConnectionId);
            if (isFirstConnection)
            {
                await Clients.Others.SendAsync("UserOnline", upn);
            }
        }
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var upn = Context.User?.Identity?.Name;
        
        if (!string.IsNullOrEmpty(upn))
        {
            bool isOffline = _tracker.UserDisconnected(upn, Context.ConnectionId);
            if (isOffline)
            {
                await Clients.Others.SendAsync("UserOffline", upn);
                
                // Update DB LastSeen
                var user = await _context.Users.FirstOrDefaultAsync(u => u.AdUpn == upn);
                if (user != null)
                {
                    user.LastSeen = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChannel(Guid channelId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
    }

    public async Task LeaveChannel(Guid channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
    }

    public async Task SendMessageToChannel(Guid channelId, string content)
    {
        var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId)) return;

        var message = new Message
        {
            Id = Guid.NewGuid(),
            Content = content,
            Timestamp = DateTime.UtcNow,
            SenderId = userId,
            ChannelId = channelId
        };
        
        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        var sender = await _context.Users.FindAsync(userId);
        var senderName = sender?.DisplayName ?? Context.User?.FindFirst("DisplayName")?.Value ?? "User";

        var messageDto = new
        {
            Id = message.Id,
            Content = message.Content,
            Timestamp = message.Timestamp,
            SenderName = senderName,
            SenderUpn = sender?.AdUpn ?? Context.User?.Identity?.Name
        };

        // Broadcast to everyone in that channel
        await Clients.Group($"Channel_{channelId}").SendAsync("ReceiveMessage", messageDto);
    }

    // WebRTC Signaling
    public async Task InitiateCall(string targetUpn, bool isVideo)
    {
        var callerUpn = Context.User?.Identity?.Name;
        var callerName = Context.User?.FindFirst("DisplayName")?.Value ?? callerUpn;
        
        // Notify the target user that a call is incoming
        await Clients.Group($"UserUPN_{targetUpn.ToLowerInvariant()}").SendAsync("IncomingCall", new { CallerUpn = callerUpn, CallerName = callerName, IsVideo = isVideo });
    }

    public async Task DeclineCall(string targetUpn)
    {
        var myUpn = Context.User?.Identity?.Name;
        await Clients.Group($"UserUPN_{targetUpn.ToLowerInvariant()}").SendAsync("CallDeclined", myUpn);
    }

    public async Task SendOffer(string targetUpn, string sdpOffer)
    {
        var callerUpn = Context.User?.Identity?.Name;
        await Clients.Group($"UserUPN_{targetUpn.ToLowerInvariant()}").SendAsync("ReceiveOffer", new { CallerUpn = callerUpn, Sdp = sdpOffer });
    }

    public async Task SendAnswer(string targetUpn, string sdpAnswer)
    {
        var responderUpn = Context.User?.Identity?.Name;
        await Clients.Group($"UserUPN_{targetUpn.ToLowerInvariant()}").SendAsync("ReceiveAnswer", new { ResponderUpn = responderUpn, Sdp = sdpAnswer });
    }

    public async Task SendIceCandidate(string targetUpn, string iceCandidate)
    {
        var senderUpn = Context.User?.Identity?.Name;
        await Clients.Group($"UserUPN_{targetUpn.ToLowerInvariant()}").SendAsync("ReceiveIceCandidate", new { SenderUpn = senderUpn, Candidate = iceCandidate });
    }
}
