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

    [HttpGet("channels")]
    public async Task<IActionResult> GetChannels()
    {
        // Simple implementation returning all channels for now
        var channels = await _context.Channels.ToListAsync();
        return Ok(channels);
    }

    [HttpGet("channels/{channelId}/messages")]
    public async Task<IActionResult> GetChannelMessages(Guid channelId)
    {
        var messages = await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.Timestamp) // usually you want pagination
            .Take(50)
            .Select(m => new {
                m.Id,
                m.Content,
                m.Timestamp,
                SenderName = m.Sender.DisplayName,
                SenderUpn = m.Sender.AdUpn
            })
            .ToListAsync();
            
        return Ok(messages.OrderBy(m => m.Timestamp));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users
            .Select(u => new { u.AdUpn, u.DisplayName, u.AvatarUrl })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel([FromBody] CreateChannelRequest req)
    {
        var channel = new Channel { Id = Guid.NewGuid(), Name = req.Name };
        _context.Channels.Add(channel);
        await _context.SaveChangesAsync();
        return Ok(channel);
    }

    [HttpPost("dm")]
    public async Task<IActionResult> GetOrCreateDirectMessage([FromBody] DmRequest req)
    {
        var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        // Ensure target user is recorded in local DB
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.AdUpn == req.TargetUpn);
        if (targetUser == null)
        {
            targetUser = new User
            {
                Id = Guid.NewGuid(),
                AdUpn = req.TargetUpn,
                DisplayName = req.TargetDisplayName ?? req.TargetUpn,
                Email = req.TargetUpn
            };
            _context.Users.Add(targetUser);
            await _context.SaveChangesAsync();
        }

        // Deterministic channel name based on UPNs
        var myUpn = User.Identity?.Name ?? "";
        var sortedNames = new[] { targetUser.AdUpn, myUpn }.OrderBy(n => n).ToArray();
        var dmChannelName = $"DM_{sortedNames[0]}_{sortedNames[1]}";

        var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Name == dmChannelName);
        if (channel == null)
        {
            channel = new Channel { Id = Guid.NewGuid(), Name = dmChannelName };
            _context.Channels.Add(channel);
            await _context.SaveChangesAsync();
        }

        return Ok(channel);
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
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
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

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return NotFound();

        // Save to ContentRoot/Uploads/avatars/ — persists across wwwroot rebuilds
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
}

public class CreateChannelRequest
{
    public string Name { get; set; } = string.Empty;
}

public class DmRequest
{
    public string TargetUpn { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
}
