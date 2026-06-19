namespace Collaboard.Api.Configuration;

// Outbound webhook delivery configuration (#320). One operator-configured endpoint per
// deployment, resolved through the standard config stack (env Section__Key >
// appsettings.json > hardcoded default). Dark-by-default: unset/empty Endpoint (or
// Enabled=false) means no dispatcher work and no outbound calls — the same kill-switch
// shape TempCardSweepSettings / UpdateCheckSettings use.
public class WebhookSettings
{
    public const string SectionName = "Webhooks";

    // The single outbound endpoint. Unset/empty = webhooks dark (no dispatcher work, no
    // outbound calls). Overridable via Webhooks__Endpoint.
    public string? Endpoint { get; init; }

    // Optional shared secret. Set => HMAC-SHA256 sign the raw body and send the
    // X-Collaboard-Signature header (sha256=...). Unset => unsigned. Overridable via
    // Webhooks__Secret. (Secret — never logged, never echoed in any response or event
    // payload.)
    public string? Secret { get; init; }

    // Master switch. Independent of Endpoint so a deployment can keep the endpoint
    // configured but pause delivery. Overridable via Webhooks__Enabled.
    public bool Enabled { get; init; } = true;

    // Per-POST timeout (the typed HttpClient.Timeout). A slow endpoint is treated as a
    // failed attempt, not waited on. Overridable via Webhooks__DeliveryTimeout.
    public TimeSpan DeliveryTimeout { get; init; } = TimeSpan.FromSeconds(5);

    // Max delivery attempts (initial + retries). Overridable via Webhooks__MaxAttempts.
    public int MaxAttempts { get; init; } = 3;

    // The first retry's backoff (attempt 1 is immediate; the wait before attempt 2). Subsequent
    // retries grow it ~4x with jitter (attempt 2 ~= this, attempt 3 ~= 4x this). The "config-
    // tunable" knob the delivery-semantics contract names; a test can set it near-zero to exercise
    // the retry loop without real wall-clock waits. Overridable via Webhooks__RetryBackoffBase.
    public TimeSpan RetryBackoffBase { get; init; } = TimeSpan.FromSeconds(2);
}
