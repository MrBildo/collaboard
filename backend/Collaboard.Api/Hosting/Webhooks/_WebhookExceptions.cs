namespace Collaboard.Api.Hosting.Webhooks;

// Thrown by WebhookSubscriptionStore (and SsrfGuard.ValidateForRegistrationAsync) when a
// subscription create/update is rejected for a caller-fixable reason — empty/invalid event
// selection, an unknown event type, a bad URL scheme, or an SSRF-blocked target at registration.
// The REST and MCP management surfaces (later slices) translate it to a 400 / tool-error; the
// foundation-slice store tests assert the throw. Distinct from WebhookSsrfBlockedException, which is
// the connect-time delivery block (recorded as a Failed attempt, never surfaced to a caller).
//
// sealed: a leaf error type carrying only a message; no subtype hierarchy is intended.
public sealed class WebhookValidationException : Exception
{
    public WebhookValidationException(string message)
        : base(message)
    {
    }

    public WebhookValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// Thrown by the SSRF connect-time guard (SsrfGuard's ConnectCallback) when a webhook target's
// resolved IP is in a blocked range. SocketsHttpHandler wraps a throwing ConnectCallback in
// HttpRequestException, which HttpWebhookSender's existing catch filter records as a Failed
// WebhookDeliveryAttempt — so a connect-blocked delivery is observable in the delivery log, not an
// uncaught crash (#326 S3a).
//
// sealed: a leaf error type carrying only a message; no subtype hierarchy is intended.
public sealed class WebhookSsrfBlockedException : Exception
{
    public WebhookSsrfBlockedException(string message)
        : base(message)
    {
    }

    public WebhookSsrfBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
