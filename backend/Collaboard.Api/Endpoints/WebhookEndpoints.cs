using Collaboard.Api.Auth;
using Collaboard.Api.Configuration;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Collaboard.Api.Endpoints;

// Webhook observability endpoints (#320, #326). Read-only diagnostic data — delivery health, not
// board content. #326 D1 — promoted from strict-Administrator to admin-level (Administrator OR
// AgentAdministrator), uniform with the subscription CRUD surface: one capability, one gate, no
// per-transport drift.
internal static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder group)
    {
        // The operator's window into whether webhooks are firing: the persisted delivery
        // attempts, newest first, filterable to one board and/or one subscription (#326 — the
        // subscriptionId filter answers "is THIS webhook delivering?").
        group.MapGet("/webhooks/deliveries", async (BoardDbContext db, Guid? boardId, Guid? subscriptionId, int? offset, int? limit, CancellationToken ct) =>
        {
            var query = db.WebhookDeliveryAttempts.AsQueryable();

            if (boardId.HasValue)
            {
                query = query.Where(x => x.BoardId == boardId.Value);
            }

            if (subscriptionId.HasValue)
            {
                query = query.Where(x => x.SubscriptionId == subscriptionId.Value);
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
                        x.SubscriptionId,
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
        }).RequireAdminOrAgentAdmin();

        // The status endpoint: the global delivery posture + registry counts, so an operator can
        // answer "is delivery on, are private targets allowed, how many subscriptions exist?"
        // without a successful delivery already in the log. #326 — the v1 Endpoint/Secret-derived
        // endpointConfigured/signed booleans read the retiring config keys and would lie in the
        // registry world, so they are replaced by counts. Booleans + counts ONLY — never a secret
        // or a URL.
        group.MapGet("/webhooks/status", async (BoardDbContext db, IOptions<WebhookSettings> settings, CancellationToken ct) =>
        {
            var s = settings.Value;
            var subscriptionCount = await db.WebhookSubscriptions.CountAsync(ct);
            var enabledSubscriptionCount = await db.WebhookSubscriptions.CountAsync(x => x.Enabled, ct);

            return Results.Ok(new WebhookStatus
            (
                s.Enabled,
                s.AllowPrivateNetworkTargets,
                subscriptionCount,
                enabledSubscriptionCount
            ));
        }).RequireAdminOrAgentAdmin();

        // The webhook event catalog (#336): the full set of selectable event types with their display
        // metadata (label, description) grouped by family. The single server-side source of truth the
        // admin UI's subscription picker consumes — replacing the frontend's hand-maintained copy, so
        // the picker can never again drift from what the backend actually emits and accepts. Static
        // data (no DB); admin-level, uniform with the rest of the webhook surface (D1).
        group.MapGet("/webhooks/event-types", () => Results.Ok(WebhookEventCatalog.Groups))
            .RequireAdminOrAgentAdmin();

        return group;
    }
}

// The deliveries response item — projects WebhookDeliveryAttempt with Status as the enum NAME
// string (the REST API registers no JsonStringEnumConverter, so the entity's enum would otherwise
// serialize as its integer ordinal; the documented contract shows "Failed"/"Succeeded").
// SubscriptionId (#326) is nullable — v1/seed/ping-pre-deletion rows can carry null.
internal sealed record WebhookDeliveryItem
(
    Guid Id,
    Guid? SubscriptionId,
    string EventId,
    string EventType,
    Guid BoardId,
    int Attempt,
    string Status,
    int? HttpStatusCode,
    string? Error,
    DateTimeOffset AttemptedAtUtc
);

// The status response — global posture + registry counts (#326). Booleans + counts only; never the
// secret or any URL. enabled = the master kill-switch; allowPrivateNetworkTargets = the SSRF
// override; the counts are the registry size and how many are individually enabled.
internal sealed record WebhookStatus
(
    bool Enabled,
    bool AllowPrivateNetworkTargets,
    int SubscriptionCount,
    int EnabledSubscriptionCount
);
