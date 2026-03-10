using Microsoft.EntityFrameworkCore;
using TeamApp.API.Data;
using TeamApp.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost",
        builder => builder
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddScoped<IAdAuthService, AdAuthService>();
builder.Services.AddSingleton<ConnectionTracker>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var signalRBuilder = builder.Services.AddSignalR();
var redisConnectionString = builder.Configuration.GetConnectionString("RedisBackplane");
if (!string.IsNullOrEmpty(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString);
}

var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_fallback_key_that_is_long_enough_for_hmac_sha256";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // 1) Prefer Authorization header if provided
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                // 2) Fallback to cookie-based token (HttpOnly cookie)
                if (context.Request.Cookies.TryGetValue("AccessToken", out var cookieToken) && !string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                    return Task.CompletedTask;
                }

                // 3) Support SignalR access_token query string for legacy clients
                var accessToken = context.Request.Query["access_token"].ToString();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chathub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Allow configuring a base path (e.g. /Connect) for IIS virtual applications.
// This can be set in appsettings.json (BasePath) or via the IIS-set ASPNETCORE_APPL_PATH env var.
var basePath = builder.Configuration["BasePath"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_APPL_PATH");
if (!string.IsNullOrEmpty(basePath))
{
    // Ensure base path starts with '/'.
    if (!basePath.StartsWith('/'))
    {
        basePath = "/" + basePath;
    }
    app.UsePathBase(basePath.TrimEnd('/'));
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("AllowLocalhost");

app.UseDefaultFiles();
app.UseStaticFiles();

// Serve the persistent Uploads folder (e.g. avatars) at /uploads/*
// This folder is NOT inside wwwroot so it survives run_app.bat rebuilds
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "Uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TeamApp.API.Hubs.ChatHub>("/chathub");
app.MapFallbackToFile("/index.html");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

app.Run();

