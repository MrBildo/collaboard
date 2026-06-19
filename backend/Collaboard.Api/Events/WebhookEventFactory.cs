using Collaboard.Api.Endpoints;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Events;

// The anti-drift core for webhook event emission (#320). Per the project convention —
// every card mutation has two callers (REST + MCP) sharing a helper core; drift between
// them is the top bug class — the eight converted sites must NOT each hand-build a
// BoardEvent. All three create sites call PublishCardCreatedAsync; all five move sites
// call PublishCardMovedAsync. So REST and MCP emit identical events by construction
// (the same remedy as CardSummaryBuilder / SearchHelper).
//
// Everything the wire envelope needs is resolved here, at emit-time, before enqueuing
// (D1 — fidelity-required): the fat CardSummary projection, the board slug, the lane
// name(s), a fresh ULID eventId, the occurredAt timestamp. The dispatcher (Phase 2)
// then needs no DB.
internal static class WebhookEventFactory
{
    // Centralized so a site can't emit "card.moved" while a test asserts "card_moved".
    // At two events these are fine as consts on the factory; the catalog-expansion PR
    // promotes them to a shared WebhookEventTypes class (#320 Promised Upgrade Path).
    public const string EventVersion = "1";
    public const string CardCreated = "card.created";
    public const string CardMoved = "card.moved";

    public static async Task PublishCardCreatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var summary = await BuildSummaryAsync(db, card, ct);
        var (boardSlug, laneName) = await ResolveBoardSlugAndLaneNameAsync(db, card.BoardId, card.LaneId, ct);

        var boardEvent = BuildEvent
        (
            CardCreated,
            card.BoardId,
            boardSlug,
            actor,
            new WebhookCardCreatedData(summary, laneName)
        );

        broadcaster.Publish(boardEvent);
    }

    // fromLane / fromPosition are captured by the call-site BEFORE the move mutates the
    // card (MoveCardToLaneAsync — or the PATCH site's inline mutation — renumbers both
    // lanes, so the source position is gone once it runs). By call time the card already
    // sits in the target lane at the target position. Used by the four single-card move
    // sites, which fan out to SSE + webhook together via broadcaster.Publish.
    public static async Task PublishCardMovedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        Lane fromLane,
        int fromPosition,
        Lane toLane,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCardMovedAsync(db, card, fromLane, fromPosition, toLane, actor, ct);
        broadcaster.Publish(boardEvent);
    }

    // Builds the card.moved event WITHOUT publishing — for the bulk path
    // (bulk_update_cards), where the webhook projection needs one event PER moved card but
    // the SSE projection coalesces to one board-bell per board (BulkExecution's existing
    // contract). The bulk tool enqueues these directly to the sink after the batch saves,
    // and rings the single SSE bell separately — so calling broadcaster.Publish per card
    // (which would ring N SSE bells) would break the coalesce safety property. (#320.)
    public static async Task<BoardEvent> BuildCardMovedAsync
    (
        BoardDbContext db,
        CardItem card,
        Lane fromLane,
        int fromPosition,
        Lane toLane,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var summary = await BuildSummaryAsync(db, card, ct);
        var boardSlug = await ResolveBoardSlugAsync(db, card.BoardId, ct);

        var data = new WebhookCardMovedData
        (
            summary,
            toLane.Name,
            new WebhookLaneRef(fromLane.Id, fromLane.Name, fromPosition),
            new WebhookLaneRef(toLane.Id, toLane.Name, card.Position)
        );

        return BuildEvent(CardMoved, card.BoardId, boardSlug, actor, data);
    }

    private static BoardEvent BuildEvent
    (
        string eventType,
        Guid boardId,
        string boardSlug,
        BoardUser actor,
        object data
    ) =>
        new
        (
            eventType,
            Ulid.NewUlid().ToString(),
            DateTimeOffset.UtcNow,
            EventVersion,
            boardId,
            boardSlug,
            new BoardEventActor(actor.Id, actor.Name, actor.Role.ToString()),
            data
        );

    private static async Task<CardSummary> BuildSummaryAsync(BoardDbContext db, CardItem card, CancellationToken ct)
    {
        var summaries = await CardSummaryBuilder.BuildAsync(db, [card], ct);
        return summaries[0];
    }

    private static async Task<string> ResolveBoardSlugAsync(BoardDbContext db, Guid boardId, CancellationToken ct) =>
        await db.Boards
            .Where(b => b.Id == boardId)
                .Select(b => b.Slug)
                    .FirstOrDefaultAsync(ct)
        ?? string.Empty;

    private static async Task<(string BoardSlug, string LaneName)> ResolveBoardSlugAndLaneNameAsync
    (
        BoardDbContext db,
        Guid boardId,
        Guid laneId,
        CancellationToken ct
    )
    {
        var boardSlug = await ResolveBoardSlugAsync(db, boardId, ct);
        var laneName = await db.Lanes
            .Where(l => l.Id == laneId)
                .Select(l => l.Name)
                    .FirstOrDefaultAsync(ct)
            ?? string.Empty;

        return (boardSlug, laneName);
    }
}
