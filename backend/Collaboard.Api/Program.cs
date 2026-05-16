using System.Reflection;
using Collaboard.Api;
using Collaboard.Api.Configuration;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Hosting;
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

// Listen-address dual-pattern: `urls` / `ASPNETCORE_URLS` wins (Aspire dev, hosting-injected,
// operator override); otherwise the structured Hosting: settings build the bind URL. Runs
// before AddServiceDefaults so the host's URL story is settled before Aspire's hooks register.
var bindUrl = HostingBindResolver.Resolve(builder.Configuration);
if (bindUrl is not null)
{
    builder.WebHost.UseUrls(bindUrl);
}

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

builder.Services.Configure<HostingSettings>(
    builder.Configuration.GetSection(HostingSettings.SectionName));
builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));

var corsSettings = builder.Configuration
    .GetSection(CorsSettings.SectionName)
    .Get<CorsSettings>() ?? new CorsSettings();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicies.Default, policy =>
    {
        if (corsSettings.AllowedOrigins.Count == 0)
        {
            // Empty allow-list: no origins authorized. The middleware still runs and
            // simply emits no Access-Control-Allow-Origin header. Same-origin requests
            // are unaffected (browser doesn't preflight same-origin).
            return;
        }

        policy
            .WithOrigins([.. corsSettings.AllowedOrigins])
            .AllowAnyMethod()
            .AllowAnyHeader()           // X-User-Key is in there
            .AllowCredentials();        // cookies + auth headers permitted
    });
});

// The writable database location is a told input — never derived from the working
// directory, the binary directory, or $HOME. ConnectionStrings:Board is required
// configuration with no fallback; an absolute path is required. Misconfiguration
// fails loud and early here, naming the key and path, rather than degrading to a
// cwd-relative guess that lands unpredictably under a hardened deployment (#233).
var connectionString = builder.Configuration.GetConnectionString("Board");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException
    (
        "Required configuration 'ConnectionStrings:Board' is not set. Provide an "
        + "absolute SQLite connection string via appsettings.Local.json, the "
        + "ConnectionStrings__Board environment variable, or a command-line argument "
        + "(e.g. \"Data Source=/srv/collaboard/data/collaboard.db\"). The application "
        + "does not derive a database path from the working or binary directory."
    );
}

var dbPath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;

// SQLite special data sources (`:memory:`, empty/temp) carry no filesystem path —
// the in-memory test database relies on this. Real filesystem paths must be
// absolute and writable; anything else fails loud here, before EF opens the DB.
var isSpecialDataSource = string.IsNullOrEmpty(dbPath) || dbPath == ":memory:";
if (!isSpecialDataSource)
{
    if (!Path.IsPathRooted(dbPath))
    {
        throw new InvalidOperationException
        (
            $"Configuration 'ConnectionStrings:Board' resolves to a relative data "
            + $"source '{dbPath}'. An absolute path is required so the database "
            + "location does not depend on the process working directory. Set "
            + "'ConnectionStrings:Board' to an absolute path."
        );
    }

    var dbDir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dbDir))
    {
        try
        {
            Directory.CreateDirectory(dbDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException
            (
                $"The database directory '{dbDir}' (from 'ConnectionStrings:Board' "
                + $"= '{dbPath}') could not be created or is not writable: {ex.Message}. "
                + "Point 'ConnectionStrings:Board' at an absolute path the process "
                + "can write under this deployment's sandbox.",
                ex
            );
        }
    }
}

builder.Services.AddDbContext<BoardDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.Configure<AttachmentSettings>(builder.Configuration.GetSection("Attachments"));
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
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Done", Position = 2 },
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Archive", Position = int.MaxValue, IsArchiveLane = true }
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
else
{
    app.UseCors(CorsPolicies.Default);
}

// Serve the embedded SPA from wwwroot — gated on Hosting:ServeSpa so headless
// (hosted-separately) deployments 404 unmatched routes instead of the SPA shell.
var serveSpa = app.Configuration
    .GetValue($"{HostingSettings.SectionName}:{nameof(HostingSettings.ServeSpa)}", true);

if (serveSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

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
api.MapPruneEndpoints();
api.MapSearchEndpoints();

app.MapEventEndpoints();

app.MapMcp("/mcp");

app.MapDefaultEndpoints();

// SPA fallback — serve index.html for any unmatched routes (must be after API/MCP routes).
// Gated on Hosting:ServeSpa: headless deployments skip it so unmatched routes 404.
if (serveSpa)
{
    app.MapFallbackToFile("index.html");
}

// Complete all SSE channels on shutdown so streamed connections close promptly
app.Lifetime.ApplicationStopping.Register(() =>
    app.Services.GetRequiredService<BoardEventBroadcaster>().CompleteAll());

app.Run();

public partial class Program { }
