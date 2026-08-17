using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Collabot.Collattice.Api.Tests.Infrastructure;

// Adds the revision-race interceptor to the standard harness and changes nothing else. It lives in
// its own factory rather than the shared one because it commits a rival edit mid-save, which every
// other test in the suite has no reason to pay for and no reason to expect.
//
// The interceptor is attached to the DbContext options explicitly. Registering it as an IInterceptor
// in the application container is not enough — that was measured, and it never fired.
public class RevisionRaceFactory : CollatticeApiFactory
{
    public RevisionRaceInterceptor Interceptor => Services.GetRequiredService<RevisionRaceInterceptor>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services => services.AddSingleton<RevisionRaceInterceptor>());
    }

    protected override void ConfigureDbContext(IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
        options.AddInterceptors(serviceProvider.GetRequiredService<RevisionRaceInterceptor>());
}
