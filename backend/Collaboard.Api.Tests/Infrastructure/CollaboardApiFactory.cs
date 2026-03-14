using Collaboard.Api;
using Collaboard.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Collaboard.Api.Tests.Infrastructure;

public class CollaboardApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public const string TestApiKey = "test-api-key-for-integration";
    public string AdminAuthKey { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ApiKey"] = TestApiKey,
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BoardDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<BoardDbContext>(options =>
                options.UseSqlite(_connection));

        });
    }

    public async Task InitializeAsync()
    {
        // Force host creation which triggers Program.cs seed logic
        _ = CreateClient();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Role == UserRole.Administrator);
        AdminAuthKey = admin.AuthKey;
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
