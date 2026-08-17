using Collabot.Collattice.Api.Hosting.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Collabot.Collattice.Api.Tests.Infrastructure;

// CollatticeApiFactory variant for the webhook DELIVERY tests. Unlike
// WebhookTestFactory (which swaps the sink to capture enqueued events without delivery), this
// keeps the REAL production pipeline — the WebhookQueue, the typed HttpClient, HMAC signing,
// retry, persistence — and only swaps the dispatcher's HttpClient primary handler for a
// CapturingHttpMessageHandler. So the test exercises the actual send path (serialize-once + sign
// + headers) producing real bytes; the handler captures those bytes instead of hitting a socket.
//
// Two test modes:
//  - RunDispatcher = true (default): the WebhookDispatcherService hosted service runs and drains
//    the queue. Used by the "delivery happens after the 201" / dark-no-op tests, which assert on
//    the capturing handler or on a count-of-zero (no DB read race).
//  - RunDispatcher = false: the hosted dispatcher is removed so the test OWNS the queue and drives
//    WebhookDispatcherService.DeliverEventAsync deterministically against a scope's DbContext —
//    the TempCardSweepService.SweepAsync pattern. This avoids racing the running hosted service
//    against the shared in-memory SQLite connection (EF + a single shared connection is not safe
//    for concurrent cross-thread reads/writes), which is the right way to verify persistence.
//
// Webhooks config (Endpoint / Secret / MaxAttempts / a near-zero RetryBackoffBase) flows through
// the base ConfigOverrides path — both UseSetting (early) and ConfigureAppConfiguration (late),
// per the WAF eager-read seam.
public sealed class WebhookDeliveryFactory : CollatticeApiFactory
{
    public CapturingHttpMessageHandler Handler { get; } = new();

    public bool RunDispatcher { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Re-point the dispatcher's typed HttpClient at the capturing handler. The last
            // ConfigurePrimaryHttpMessageHandler registration for the named client wins, so the
            // real HttpWebhookSender (serialize-once + sign + headers) runs against our stub.
            services
                .AddHttpClient<IWebhookSender, HttpWebhookSender>()
                .ConfigurePrimaryHttpMessageHandler(() => Handler);

            // The base CollatticeApiFactory removes the hosted dispatcher (it races the shared
            // in-memory connection). Re-add it for the end-to-end delivery tests that assert
            // on the running dispatcher; the persistence tests leave it off (RunDispatcher = false)
            // and drive the deterministic DeliverEventAsync seam directly, fully owning the queue.
            if (RunDispatcher)
            {
                services.AddHostedService<WebhookDispatcherService>();
            }
        });
    }
}
