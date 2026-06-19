using Collaboard.Api.Events;

namespace Collaboard.Api.Hosting.Webhooks;

// The HTTP send seam for webhook delivery (#320). Mirrors the UpdateCheck ILatestVersionSource
// shape — a typed HttpClient behind an interface — so the outbound POST is stubbable in tests
// (the Test Plan wants a stub HttpMessageHandler capturing exact bytes/headers without a real
// socket) and the dispatcher's drain/retry/persist logic stays separable from the wire send.
//
// One attempt = one POST. The sender serializes the event ONCE (D3 — sign the exact bytes sent),
// signs when a secret is configured, attaches the delivery headers, and reports the outcome. The
// dispatcher owns the retry loop, the persisted attempt log, and the loud final-failure drop.
public interface IWebhookSender
{
    Task<WebhookDeliveryResult> SendAsync(BoardEvent boardEvent, CancellationToken ct);
}

// The outcome of one delivery attempt. StatusCode is null when no response was received
// (timeout, DNS/TLS/connection error); Error carries the head-of-message actionable detail.
public sealed record WebhookDeliveryResult(bool Succeeded, int? StatusCode, string? Error);
