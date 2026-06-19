using Collaboard.Api.Auth;
using Collaboard.Api.Configuration;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Collaboard.Api.Endpoints;

// Webhook observability endpoints (#320). Both admin-only and read-only — there is no
// webhook-management CRUD in v1 (the endpoint is config-only). Delivery health is
// operator-facing diagnostic data, not board content.
internal static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder group)
    {
        // The operator's window into whether webhooks are firing: the persisted delivery
        // attempts, newest first, filterable to one board.
        group.MapGet("/webhooks/deliveries", async (BoardDbContext db, Guid? boardId, int? offset, int? limit, CancellationToken ct) =>
        {
            var query = db.WebhookDeliveryAttempts.AsQueryable();

            if (boardId.HasValue)
            {
                query = query.Where(x => x.BoardId == boardId.Value);
            }

            var totalCount = await query.CountAsync(ct);

            var effectiveOffset = Math.Max(offset ?? 0, 0);
            var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);

            var items = await query
                .OrderByDescending(x => x.AttemptedAtUtc)
                .Skip(effectiveOffset)
                .Take(effectiveLimit)
                    .Select(x => new WebhookDeliveryItem
                    (
                        x.Id,
                        x.EventId,
                        x.EventType,
                        x.BoardId,
                        x.Attempt,
                        x.Status.ToString(),
                        x.HttpStatusCode,
                        x.Error,
                        x.AttemptedAtUtc
                    ))
                        .ToListAsync(ct);

            return Results.Ok(new PagedResult<WebhookDeliveryItem>(items, totalCount, effectiveOffset, effectiveLimit));
        }).RequireAdmin();

        // The D4 status endpoint: closes the empty-deliveries-log ambiguity the deliveries
        // endpoint structurally can't ("is it even on?" without a successful delivery already
        // wired). Booleans ONLY — never the secret, never the URL. Admin-only, consistent with
        // the deliveries endpoint.
        group.MapGet("/webhooks/status", (IOptions<WebhookSettings> settings) =>
        {
            var s = settings.Value;
            return Results.Ok(new WebhookStatus
            (
                s.Enabled,
                !string.IsNullOrWhiteSpace(s.Endpoint),
                !string.IsNullOrWhiteSpace(s.Secret)
            ));
        }).RequireAdmin();

        return group;
    }
}

// The deliveries response item — projects WebhookDeliveryAttempt with Status as the enum NAME
// string (the REST API registers no JsonStringEnumConverter, so the entity's enum would otherwise
// serialize as its integer ordinal; the documented contract shows "Failed"/"Succeeded").
internal sealed record WebhookDeliveryItem
(
    Guid Id,
    string EventId,
    string EventType,
    Guid BoardId,
    int Attempt,
    string Status,
    int? HttpStatusCode,
    string? Error,
    DateTimeOffset AttemptedAtUtc
);

// The status response — booleans only. Never carries the secret or the endpoint URL (the secret
// must never be echoed anywhere; the URL is operator-trust config, not status). #320 D4→(b).
internal sealed record WebhookStatus(bool Enabled, bool EndpointConfigured, bool Signed);
