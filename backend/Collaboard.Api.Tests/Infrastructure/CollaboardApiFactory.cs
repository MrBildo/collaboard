using Collaboard.Api.Hosting.Webhooks;
using Collaboard.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Collaboard.Api.Tests.Infrastructure;

public class CollaboardApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public const string TestAdminAuthKey = "test-admin-auth-key-12345678";
    public string AdminAuthKey { get; private set; } = string.Empty;
    public Guid DefaultBoardId { get; private set; }

    // In-memory config overrides applied after the baseline test config. Lets a test
    // flip Hosting:ServeSpa, populate Cors:AllowedOrigins, set ASPNETCORE_ENVIRONMENT,
    // etc. Null (the IClassFixture default) preserves today's behavior.
    public IReadOnlyDictionary<string, string?>? ConfigOverrides { get; init; }

    // Forces the host environment. Null keeps WebApplicationFactory's default
    // (Development). "Production" exercises the non-dev CORS named-policy branch.
    public string? EnvironmentName { get; init; }

    public static CollaboardApiFactory WithConfig(IReadOnlyDictionary<string, string?> overrides) =>
        new() { ConfigOverrides = overrides };

    public static CollaboardApiFactory WithConfig
    (
        string environmentName,
        IReadOnlyDictionary<string, string?> overrides
    ) =>
        new() { EnvironmentName = environmentName, ConfigOverrides = overrides };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (EnvironmentName is not null)
        {
            builder.UseEnvironment(EnvironmentName);
        }

        // ConnectionStrings:Board is required configuration with no fallback
        // (#233 G-3b); Program.cs hard-fails at host-build time if it is unset.
        // That eager read happens before ConfigureServices swaps in the shared
        // in-memory connection, so the value must be visible to
        // builder.Configuration — i.e. injected via UseSetting, not
        // ConfigureAppConfiguration (the WAF eager-read seam). ":memory:" is
        // the special data source Program.cs's guard exempts from path
        // validation and directory creation; the actual test database is the
        // shared SqliteConnection swapped in below.
        builder.UseSetting("ConnectionStrings:Board", "Data Source=:memory:");

        // UseSetting feeds the web-host builder configuration, which
        // WebApplication.CreateBuilder incorporates BEFORE Program.cs reads
        // builder.Configuration. ConfigureAppConfiguration delegates run later and
        // are not visible to eager builder.Configuration reads (e.g. the AddCors
        // policy snapshot). Overrides must go through UseSetting to exercise the
        // same early-resolution path production uses via Cors__AllowedOrigins__N
        // environment variables.
        if (ConfigOverrides is not null)
        {
            foreach (var (key, value) in ConfigOverrides)
            {
                builder.UseSetting(key, value);
            }
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AuthKey"] = TestAdminAuthKey,
            });

            if (ConfigOverrides is not null)
            {
                config.AddInMemoryCollection(ConfigOverrides);
            }
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<BoardDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<BoardDbContext>(options =>
                options.UseSqlite(_connection));

            // Remove the webhook dispatcher hosted service by default. It queries WebhookSubscriptions
            // on every drained card event (Webhooks:Enabled defaults true, #326 — IsConfigured no
            // longer gates on a configured endpoint), and the WAF's single shared in-memory SQLite
            // connection is not concurrency-safe across threads — that background query races a test
            // thread's card SaveChanges under suite load (the S50 "a BackgroundService must do no DB
            // work the test thread can race" discipline, extended from startup to per-drain work).
            // The webhook DELIVERY tests opt back in via WebhookDeliveryFactory (RunDispatcher).
            var dispatcher = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(WebhookDispatcherService));
            if (dispatcher is not null)
            {
                services.Remove(dispatcher);
            }
        });
    }

    public async Task InitializeAsync()
    {
        // Force host creation which triggers Program.cs seed logic
        _ = CreateClient();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Role == UserRole.Administrator);
        AdminAuthKey = admin.AuthKey;

        var board = await db.Set<Board>().FirstAsync();
        DefaultBoardId = board.Id;
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
