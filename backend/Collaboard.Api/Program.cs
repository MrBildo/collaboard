using System.Reflection;
using Collaboard.Api;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

// --version flag
if (args.Contains("--version"))
{
    var raw = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";
    Console.WriteLine($"Collaboard {raw.Split('+')[0]}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddCors();
var connectionString = builder.Configuration.GetConnectionString("Board") ?? "Data Source=./data/collaboard.db";

// Ensure the data directory exists before EF creates/opens the database
var dbPath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
var dbDir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

builder.Services.AddDbContext<BoardDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BoardEventBroadcaster>();
builder.Services.AddScoped<McpAuthService>();

builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();

    // Auto-backup SQLite DB before applying pending migrations
    var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
    if (pendingMigrations.Any())
    {
        var currentConnectionString = db.Database.GetConnectionString();
        var currentDbPath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(currentConnectionString).DataSource;
        if (File.Exists(currentDbPath))
        {
            var backupPath = $"{currentDbPath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(currentDbPath, backupPath);
            app.Logger.LogInformation("Database backed up to {BackupPath} before applying {Count} pending migration(s)",
                backupPath, pendingMigrations.Count());
        }
    }

    await db.Database.MigrateAsync();

    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = 'wal';");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");

    if (!await db.Users.AnyAsync())
    {
        var adminAuthKey = app.Configuration.GetValue<string>("Admin:AuthKey")
            ?? Ulid.NewUlid().ToString();

        db.Users.Add(new BoardUser
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            AuthKey = adminAuthKey,
            Role = UserRole.Administrator,
        });

        var defaultBoard = new Board
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            Slug = "default",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Boards.Add(defaultBoard);

        db.Lanes.AddRange(
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Backlog", Position = 0 },
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "In Progress", Position = 1 },
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Done", Position = 2 }
        );

        db.Set<CardSize>().AddRange(
            new CardSize { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "S", Ordinal = 0 },
            new CardSize { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "M", Ordinal = 1 },
            new CardSize { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "L", Ordinal = 2 },
            new CardSize { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "XL", Ordinal = 3 }
        );
        await db.SaveChangesAsync();
    }

    // Always log the admin auth key at startup
    var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Administrator);
    if (admin is not null)
    {
        app.Logger.LogInformation("Admin auth key: {AuthKey}", admin.AuthKey);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
}

// Serve the embedded SPA from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api/v1");

api.MapGet("/version", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store";
    var raw = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";
    var version = raw.Split('+')[0];
    return Results.Ok(new { version });
});

api.MapBoardEndpoints();
api.MapUserEndpoints();
api.MapLaneEndpoints();
api.MapSizeEndpoints();
api.MapCardEndpoints();
api.MapLabelEndpoints();
api.MapCommentEndpoints();
api.MapAttachmentEndpoints();

app.MapEventEndpoints();

app.MapMcp("/mcp");

app.MapDefaultEndpoints();

// SPA fallback — serve index.html for any unmatched routes (must be after API/MCP routes)
app.MapFallbackToFile("index.html");

// Complete all SSE channels on shutdown so streamed connections close promptly
app.Lifetime.ApplicationStopping.Register(() =>
    app.Services.GetRequiredService<BoardEventBroadcaster>().CompleteAll());

app.Run();

public partial class Program { }
