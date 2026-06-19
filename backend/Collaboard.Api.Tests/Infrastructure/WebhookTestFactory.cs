using Collaboard.Api.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Collaboard.Api.Tests.Infrastructure;

// CollaboardApiFactory variant that swaps the production WebhookQueue for a
// CapturingWebhookSink, exposed via Sink so a test can read the events the seam
// enqueued. Phase 1 has no dispatcher/HTTP — the sink IS the observable. (#320.)
public sealed class WebhookTestFactory : CollaboardApiFactory
{
    public CapturingWebhookSink Sink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // The broadcaster's typed publish path and BulkCardTools both resolve
            // IWebhookSink; replacing that single registration routes every enqueued event
            // into the capturing sink. WebhookQueue stays registered but unused here — the
            // dispatcher that drains it is Phase 2.
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IWebhookSink));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IWebhookSink>(Sink);
        });
    }
}
