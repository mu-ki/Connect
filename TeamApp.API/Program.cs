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
builder.Services.AddMemoryCache();
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

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
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
        EnsureConversationSchema(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
        throw;
    }
}

app.Run();

static void EnsureConversationSchema(AppDbContext context)
{
    const string sql = """
        ALTER TABLE "Users"
        ADD COLUMN IF NOT EXISTS "AvatarUrl" text NULL;

        ALTER TABLE "Users"
        ADD COLUMN IF NOT EXISTS "RefreshToken" text NULL;

        ALTER TABLE "Users"
        ADD COLUMN IF NOT EXISTS "RefreshTokenExpiry" timestamp with time zone NULL;

        ALTER TABLE "Channels"
        ADD COLUMN IF NOT EXISTS "ConversationType" text NOT NULL DEFAULT 'Group';

        ALTER TABLE "Channels"
        ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;

        CREATE TABLE IF NOT EXISTS "ConversationMembers" (
            "ChannelId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "JoinedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
            CONSTRAINT "PK_ConversationMembers" PRIMARY KEY ("ChannelId", "UserId"),
            CONSTRAINT "FK_ConversationMembers_Channels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES "Channels" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_ConversationMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_Channels_CreatedByUserId"
        ON "Channels" ("CreatedByUserId");

        CREATE INDEX IF NOT EXISTS "IX_ConversationMembers_UserId"
        ON "ConversationMembers" ("UserId");
        """;

    context.Database.ExecuteSqlRaw(sql);

    const string foreignKeySql = """
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'FK_Channels_Users_CreatedByUserId'
            ) THEN
                ALTER TABLE "Channels"
                ADD CONSTRAINT "FK_Channels_Users_CreatedByUserId"
                FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id")
                ON DELETE SET NULL;
            END IF;
        END $$;
        """;

    context.Database.ExecuteSqlRaw(foreignKeySql);

    const string backfillSql = """
        UPDATE "Channels"
        SET "ConversationType" = CASE
            WHEN "Name" LIKE 'DM\_%\_%' ESCAPE '\' THEN 'Direct'
            ELSE 'Group'
        END;

        INSERT INTO "ConversationMembers" ("ChannelId", "UserId", "JoinedAt")
        SELECT c."Id", u."Id", NOW()
        FROM "Channels" c
        JOIN "Users" u
          ON u."AdUpn" = split_part(c."Name", '_', 2)
          OR u."AdUpn" = split_part(c."Name", '_', 3)
        WHERE c."ConversationType" = 'Direct'
        ON CONFLICT ("ChannelId", "UserId") DO NOTHING;

        INSERT INTO "ConversationMembers" ("ChannelId", "UserId", "JoinedAt")
        SELECT c."Id", u."Id", NOW()
        FROM "Channels" c
        JOIN "Messages" m ON m."ChannelId" = c."Id"
        JOIN "Users" u ON u."Id" = m."SenderId"
        WHERE c."ConversationType" = 'Group'
        ON CONFLICT ("ChannelId", "UserId") DO NOTHING;
        """;

    context.Database.ExecuteSqlRaw(backfillSql);
}

