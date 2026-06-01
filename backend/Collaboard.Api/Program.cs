using System.Globalization;
using System.Reflection;
using Collaboard.Api;
using Collaboard.Api.Auth;
using Collaboard.Api.Configuration;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Hosting;
using Collaboard.Api.Installation;
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

// Installer-invoked subcommand: merge a freshly-shipped appsettings.json against the operator's
// on-disk file (preserve edits, refresh untouched defaults, add new keys). Runs before any host
// setup so this path never touches the database, the host, or the network (#235).
if (args.Length > 0 && args[0] == "--merge-appsettings")
{
    Environment.Exit(AppSettingsMergeCli.Run(args[1..], Console.Out, Console.Error));
}

var builder = WebApplication.CreateBuilder(args);

// WebApplication.CreateBuilder adds an env-var provider at builder-construction time. The
// re-add below ensures env vars sit at the top of the provider chain even if a future JSON
// source is added after construction (ConfigPrecedenceTests locks the ordering). Post-#235
// resolved precedence: env (Section__Key) > appsettings.json > hardcoded default — the
// .Local.json overlay channel was retired by #235.
builder.Configuration.AddEnvironmentVariables();

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

builder.Services.Configure<HostingSettings>(builder.Configuration.GetSection(HostingSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));

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
// fails loud and early here, naming the key and the offending value, rather than
// degrading to a cwd-relative guess that lands unpredictably under a hardened
// deployment (#233). Each actionable failure carries a copy-paste-ready remedy in
// both forms a user might use — environment variable and appsettings.json — so a
// manual-download user can fix it in one step (#233 follow-up, updated by #235).
static string ExampleDbConnectionString() =>
    OperatingSystem.IsWindows()
        ? @"Data Source=C:\collaboard\data\collaboard.db"
        : "Data Source=/var/lib/collaboard/collaboard.db";

static string ConfigRemedy()
{
    var example = ExampleDbConnectionString();
    var jsonValue = example.Replace(@"\", @"\\", StringComparison.Ordinal);

    return $$"""
        To fix this, set 'ConnectionStrings:Board' to an absolute path in either form:

          - Environment variable:
              ConnectionStrings__Board={{example}}

          - appsettings.json (next to the executable; edits survive upgrades via #235 smart-merge):
              {
                "ConnectionStrings": {
                  "Board": "{{jsonValue}}"
                }
              }
        """;
}

var connectionString = builder.Configuration.GetConnectionString("Board");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException
    (
        "Required configuration 'ConnectionStrings:Board' is not set. The application "
        + "does not derive a database path from the working or binary directory.\n\n"
        + ConfigRemedy()
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
            + "location does not depend on the process working directory.\n\n"
            + ConfigRemedy()
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
                + $"can write under this deployment's sandbox.\n\n{ConfigRemedy()}",
                ex
            );
        }
    }
}

builder.Services.AddDbContext<BoardDbContext>(options => options.UseSqlite(connectionString));

builder.Services.Configure<AttachmentSettings>(builder.Configuration.GetSection(AttachmentSettings.SectionName));
builder.Services.Configure<TempCardSweepSettings>(builder.Configuration.GetSection(TempCardSweepSettings.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BoardEventBroadcaster>();
builder.Services.AddScoped<IUserResolver, UserResolver>();
builder.Services.AddScoped<McpAuthService>();
builder.Services.AddHostedService<TempCardSweepService>();

// The request-body limits must track the configured REST upload cap — Kestrel and
// FormOptions reject oversize bodies with a 413 before the endpoint's friendlier 400
// runs, so binding both to MaxRestUploadBytes keeps the framework floor in lockstep
// with the application cap instead of duplicating a magic number.
var attachmentSettings = builder.Configuration
    .GetSection(AttachmentSettings.SectionName)
    .Get<AttachmentSettings>() ?? new AttachmentSettings();

builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = attachmentSettings.MaxRestUploadBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = attachmentSettings.MaxRestUploadBytes);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters =>
    {
        // #202: Default SDK behaviour swallows non-McpException tool failures
        // as "An error occurred invoking '<tool>'." with no detail — a typo'd
        // parameter name then drives multi-step false-alarm investigations.
        // McpErrorTranslator.WrapForCallTool catches the input-validation
        // shapes and rethrows as McpException so the wrapper renders
        // "<tool>': <Type — Message>". Server-internal failures (DB, EF,
        // downstream) deliberately fall through, preserving the body-less
        // wrapper response so infrastructure detail does not leak.
        filters.AddCallToolFilter(McpErrorTranslator.WrapForCallTool);
    });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
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
            var backupPath = $"{currentDbPath}.bak-{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
            File.Copy(currentDbPath, backupPath);
            app.Logger.LogInformation
            (
                "Database backed up to {BackupPath} before applying {Count} pending migration(s)",
                backupPath,
                pendingMigrations.Count()
            );
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

        db.Lanes.AddRange
        (
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Backlog", Position = 0 },
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "In Progress", Position = 1 },
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Done", Position = 2 },
            new Lane { Id = Guid.NewGuid(), BoardId = defaultBoard.Id, Name = "Archive", Position = int.MaxValue, IsArchiveLane = true }
        );

        db.Set<CardSize>().AddRange
        (
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

await app.RunAsync();
