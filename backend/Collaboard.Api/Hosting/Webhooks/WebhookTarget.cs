namespace Collaboard.Api.Hosting.Webhooks;

// What one delivery needs to dial: the target URL and the optional per-subscription HMAC secret.
// Decouples the sender from the WebhookSubscription entity — the dispatcher builds one of these per
// matched subscription and hands it to IWebhookSender (#326). v1's sender read these from
// Webhooks:Endpoint / :Secret; v2 carries them per-subscription.
public sealed record WebhookTarget(string Url, string? Secret);
