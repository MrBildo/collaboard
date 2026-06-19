using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Collaboard.Api.Configuration;
using Collaboard.Api.Events;
using Microsoft.Extensions.Options;

namespace Collaboard.Api.Hosting.Webhooks;

// The typed-HttpClient realization of IWebhookSender (#320). Serializes the enriched event ONCE
// with the project's REST JSON config (camelCase / Web defaults — a consumer that already reads
// Collaboard's REST shapes sees identical field names), signs THOSE bytes when a secret is set
// (D3 — never re-serialize for the signature), and POSTs with the five delivery headers. The
// per-POST timeout is the typed client's Timeout (configured in Program.cs from
// Webhooks:DeliveryTimeout) so a slow endpoint is a failed attempt, not a wait.
internal sealed class HttpWebhookSender
(
    HttpClient httpClient,
    IOptions<WebhookSettings> settings
) : IWebhookSender
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly WebhookSettings _settings = settings.Value;

    public async Task<WebhookDeliveryResult> SendAsync(BoardEvent boardEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(boardEvent);

        // Serialize ONCE — these exact bytes are what we sign and what we send (D3).
        var body = JsonSerializer.SerializeToUtf8Bytes(boardEvent, JsonSerializerOptions.Web);

        try
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.Endpoint)
            {
                Content = content,
            };

            // X-Collaboard-Event lets a consumer route without parsing the body. The delivery-id
            // header carries the envelope eventId (in v1 the delivery-id IS the event-id), so every
            // retry of one event carries the same dedup value.
            request.Headers.TryAddWithoutValidation("X-Collaboard-Event", boardEvent.EventType);
            request.Headers.TryAddWithoutValidation("X-Collaboard-Delivery-Id", boardEvent.EventId);

            if (!string.IsNullOrWhiteSpace(_settings.Secret))
            {
                request.Headers.TryAddWithoutValidation
                (
                    "X-Collaboard-Signature",
                    WebhookSigner.Sign(body, _settings.Secret)
                );
            }

            using var response = await _httpClient.SendAsync(request, ct);

            var statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new WebhookDeliveryResult(true, statusCode, null);
            }

            // A non-2xx is a failed attempt; the reason phrase is a usable head-of-message signal.
            var error = $"{statusCode.ToString(CultureInfo.InvariantCulture)} {response.ReasonPhrase}".Trim();
            return new WebhookDeliveryResult(false, statusCode, error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown cancellation — propagate so the dispatcher loop exits cleanly; the
            // dispatcher does not record an attempt for a shutdown-cancelled send.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // No response: timeout (TaskCanceledException from HttpClient.Timeout, ct not the
            // trigger), or DNS/TLS/connection-refused (HttpRequestException). Head-of-message
            // carries the actionable error class.
            return new WebhookDeliveryResult(false, null, ex.Message);
        }
    }
}
