using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAdAuthService adAuthService, AppDbContext context, IConfiguration config, ConnectionTracker tracker, ILogger<AuthController> logger)
    {
        _adAuthService = adAuthService;
        _context = context;
        _config = config;
        _tracker = tracker;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
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

            // 3. Generate and persist tokens
            var accessToken = GenerateAccessToken(user, out var accessExpiry);
            var refreshToken = GenerateRefreshToken();
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            SetAuthCookies(accessToken, accessExpiry, refreshToken, user.RefreshTokenExpiry.Value);

            // Return user info only; tokens are stored in secure cookies.
            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during login for {Username}", request.Username);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue("RefreshToken", out var refreshToken) || string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);
        if (user == null || user.RefreshTokenExpiry == null || user.RefreshTokenExpiry < DateTime.UtcNow)
        {
            return Unauthorized();
        }

        // Keep the same refresh token (avoid logout if the browser doesn't accept updated cookies).
        // Just extend its expiry on each refresh.
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        var newAccessToken = GenerateAccessToken(user, out var accessExpiry);

        _context.Users.Update(user);
        await _context.SaveChangesAsync();

        SetAuthCookies(newAccessToken, accessExpiry, refreshToken, user.RefreshTokenExpiry.Value);
        return Ok(user);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue("RefreshToken", out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);
            if (user != null)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiry = null;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
        }

        ClearAuthCookies();
        return Ok();
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

    private string GenerateAccessToken(User user, out DateTime expires)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_config["Jwt:Key"] ?? "super_secret_fallback_key_that_is_long_enough_for_hmac_sha256");
        expires = DateTime.UtcNow.AddMinutes(15);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.AdUpn),
                new Claim("DisplayName", user.DisplayName)
            }),
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        RandomNumberGenerator.Fill(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private void SetAuthCookies(string accessToken, DateTime accessExpiry, string refreshToken, DateTime refreshExpiry)
    {
        // Ensure cookies are sent for cross-site POSTs (refresh requests) when using HTTPS.
        // If we are running behind a reverse-proxy / load balancer, Request.IsHttps can be false,
        // so also treat X-Forwarded-Proto=https as HTTPS.
        var isHttps = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

        var sameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax;
        var secure = isHttps;

        var accessCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Expires = accessExpiry,
            Path = "/"
        };

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Expires = refreshExpiry,
            Path = "/"
        };

        Response.Cookies.Append("AccessToken", accessToken, accessCookieOptions);
        Response.Cookies.Append("RefreshToken", refreshToken, refreshCookieOptions);
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete("AccessToken", new CookieOptions { Path = "/" });
        Response.Cookies.Delete("RefreshToken", new CookieOptions { Path = "/" });
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
