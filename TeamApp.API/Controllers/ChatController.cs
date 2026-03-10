using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TeamApp.API.Data;
using TeamApp.API.Models;
using TeamApp.API.Services;

namespace TeamApp.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAdAuthService _adAuthService;
    private readonly ConnectionTracker _tracker;

    public ChatController(AppDbContext context, IAdAuthService adAuthService, ConnectionTracker tracker)
    {
        _context = context;
        _adAuthService = adAuthService;
        _tracker = tracker;
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();

        var conversations = await _context.Channels
            .AsNoTracking()
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .Include(c => c.Messages)
            .Where(c => c.Members.Any(m => m.UserId == currentUserId.Value))
            .OrderByDescending(c => c.Messages.Max(m => (DateTime?)m.Timestamp) ?? DateTime.MinValue)
            .ThenBy(c => c.Name)
            .ToListAsync();

        return Ok(conversations.Select(c => ToConversationDto(c, currentUserId.Value)));
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> GetConversationMessages(Guid conversationId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();

        var canAccess = await _context.ConversationMembers
            .AnyAsync(cm => cm.ChannelId == conversationId && cm.UserId == currentUserId.Value);
        if (!canAccess) return Forbid();

        var messages = await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ChannelId == conversationId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                m.Id,
                m.Content,
                m.Timestamp,
                SenderName = m.Sender.DisplayName,
                SenderUpn = m.Sender.AdUpn
            })
            .ToListAsync();

        return Ok(messages);
    }

    [HttpPost("conversations/direct")]
    public async Task<IActionResult> GetOrCreateDirectConversation([FromBody] DirectConversationRequest req)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.TargetUpn)) return BadRequest("Target user is required.");

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null) return Unauthorized();

        var targetUser = await GetOrCreateUserAsync(req.TargetUpn, req.TargetDisplayName);

        if (targetUser.Id == currentUser.Id)
        {
            return BadRequest("You cannot start a direct conversation with yourself.");
        }

        var conversation = await _context.Channels
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c =>
                c.ConversationType == "Direct" &&
                c.Members.Count == 2 &&
                c.Members.Any(m => m.UserId == currentUser.Id) &&
                c.Members.Any(m => m.UserId == targetUser.Id));

        if (conversation == null)
        {
            conversation = new Channel
            {
                Id = Guid.NewGuid(),
                Name = $"{currentUser.DisplayName}, {targetUser.DisplayName}",
                IsPrivate = true,
                ConversationType = "Direct",
                CreatedByUserId = currentUser.Id,
                Members = new List<ConversationMember>
                {
                    new() { ChannelId = Guid.Empty, UserId = currentUser.Id },
                    new() { ChannelId = Guid.Empty, UserId = targetUser.Id }
                }
            };

            _context.Channels.Add(conversation);
            await _context.SaveChangesAsync();

            conversation = await _context.Channels
                .Include(c => c.Members)
                    .ThenInclude(m => m.User)
                .Include(c => c.Messages)
                .FirstAsync(c => c.Id == conversation.Id);
        }

        return Ok(ToConversationDto(conversation, currentUser.Id));
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Group name is required.");

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null) return Unauthorized();

        var requestedMembers = req.Members
            .Where(m => !string.IsNullOrWhiteSpace(m.AdUpn))
            .GroupBy(m => m.AdUpn.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .ToList();

        var members = new List<User> { currentUser };

        foreach (var requestedMember in requestedMembers)
        {
            var user = await GetOrCreateUserAsync(requestedMember.AdUpn, requestedMember.DisplayName);
            if (members.All(m => m.Id != user.Id))
            {
                members.Add(user);
            }
        }

        if (members.Count < 2)
        {
            return BadRequest("A group must contain at least two people.");
        }

        var group = new Channel
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            IsPrivate = true,
            ConversationType = "Group",
            CreatedByUserId = currentUser.Id,
            Members = members.Select(u => new ConversationMember
            {
                ChannelId = Guid.Empty,
                UserId = u.Id
            }).ToList()
        };

        _context.Channels.Add(group);
        await _context.SaveChangesAsync();

        group = await _context.Channels
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .Include(c => c.Messages)
            .FirstAsync(c => c.Id == group.Id);

        return Ok(ToConversationDto(group, currentUser.Id));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users
            .Select(u => new { u.AdUpn, u.DisplayName, u.AvatarUrl })
            .ToListAsync();
        return Ok(users);
    }

    [HttpGet("online")]
    public IActionResult GetOnlineUsers()
    {
        return Ok(_tracker.GetAllOnlineUsers());
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(new List<UserDto>());

        var adUsers = _adAuthService.SearchUsers(q).ToList();

        var upns = adUsers.Select(u => u.AdUpn).ToList();
        var dbUsers = await _context.Users.Where(u => upns.Contains(u.AdUpn)).ToDictionaryAsync(u => u.AdUpn);

        foreach (var user in adUsers)
        {
            user.IsOnline = _tracker.IsUserOnline(user.AdUpn);
            if (dbUsers.TryGetValue(user.AdUpn, out var dbUser))
            {
                user.LastSeen = dbUser.LastSeen;
            }
        }

        return Ok(adUsers);
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        return Ok(new
        {
            user.Id,
            user.DisplayName,
            user.Email,
            user.AvatarUrl
        });
    }

    [HttpPost("profile/avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile file, [FromServices] IWebHostEnvironment env)
    {
        if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return BadRequest("Only image files are allowed.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("File size cannot exceed 5MB.");

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        var avatarsFolder = Path.Combine(env.ContentRootPath, "Uploads", "avatars");
        Directory.CreateDirectory(avatarsFolder);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{userId}{ext}";
        var filePath = Path.Combine(avatarsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        user.AvatarUrl = $"/uploads/avatars/{fileName}";
        _context.Users.Update(user);
        await _context.SaveChangesAsync();

        return Ok(new { avatarUrl = user.AvatarUrl });
    }

    private Guid? GetCurrentUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdStr, out var userId) ? userId : null;
    }

    private async Task<User> GetOrCreateUserAsync(string upn, string? displayName)
    {
        var normalizedUpn = upn.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.AdUpn == normalizedUpn);
        if (user != null) return user;

        user = new User
        {
            Id = Guid.NewGuid(),
            AdUpn = normalizedUpn,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUpn : displayName.Trim(),
            Email = normalizedUpn
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private object ToConversationDto(Channel conversation, Guid currentUserId)
    {
        var orderedMembers = conversation.Members
            .Select(m => m.User)
            .Where(u => u != null)
            .OrderBy(u => u.DisplayName)
            .ToList();

        var otherMember = conversation.ConversationType == "Direct"
            ? orderedMembers.FirstOrDefault(u => u.Id != currentUserId)
            : null;

        return new
        {
            conversation.Id,
            conversation.Name,
            Type = conversation.ConversationType,
            IsGroup = conversation.ConversationType == "Group",
            MemberCount = orderedMembers.Count,
            Members = orderedMembers.Select(u => new
            {
                u.Id,
                u.AdUpn,
                u.DisplayName,
                u.AvatarUrl
            }),
            OtherParticipantUpn = otherMember?.AdUpn,
            OtherParticipantName = otherMember?.DisplayName,
            LastMessageAt = conversation.Messages.Max(m => (DateTime?)m.Timestamp)
        };
    }
}

public class DirectConversationRequest
{
    public string TargetUpn { get; set; } = string.Empty;
    public string? TargetDisplayName { get; set; }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public List<GroupMemberRequest> Members { get; set; } = new();
}

public class GroupMemberRequest
{
    public string AdUpn { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
