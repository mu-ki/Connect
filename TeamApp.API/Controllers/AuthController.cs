using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TeamApp.API.Services;
using TeamApp.API.Models;
using TeamApp.API.Data;
using Microsoft.EntityFrameworkCore;

namespace TeamApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAdAuthService _adAuthService;
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly ConnectionTracker _tracker;

    public AuthController(IAdAuthService adAuthService, AppDbContext context, IConfiguration config, ConnectionTracker tracker)
    {
        _adAuthService = adAuthService;
        _context = context;
        _config = config;
        _tracker = tracker;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // 1. Validate against Active Directory
        bool isValid = _adAuthService.ValidateCredentials(request.Username, request.Password);
        if (!isValid) return Unauthorized("Invalid Active Directory credentials.");

        // 2. Fetch or Create user in local PostgreSQL database
        var user = await _context.Users.FirstOrDefaultAsync(u => u.AdUpn == request.Username);
        
        var adUser = _adAuthService.GetUserByUpn(request.Username);
        string newDisplayName = adUser?.DisplayName ?? request.Username.Split('@')[0];

        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                AdUpn = request.Username,
                DisplayName = newDisplayName,
                Email = request.Username
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else if (user.DisplayName != newDisplayName)
        {
            // Sync any updated display name
            user.DisplayName = newDisplayName;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        // 3. Generate JWT Token
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_config["Jwt:Key"] ?? "super_secret_fallback_key_that_is_long_enough_for_hmac_sha256");
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.AdUpn),
                new Claim("DisplayName", user.DisplayName)
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return Ok(new { Token = tokenHandler.WriteToken(token), User = user });
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(new List<UserDto>());
        
        var adUsers = _adAuthService.SearchUsers(q).ToList();
        
        // Enrich with IsOnline from tracker and LastSeen from DB
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

    [HttpGet("online")]
    public IActionResult GetOnlineUsers()
    {
        // For simplicity, we just need a way to get the keys of active connections.
        // We can add a method to ConnectionTracker.
        var onlineUsers = _tracker.GetAllOnlineUsers();
        return Ok(onlineUsers);
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
