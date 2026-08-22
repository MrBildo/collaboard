namespace Collabot.Collattice.Api.Events;

// The seam between the broadcaster's fan-out and webhook delivery. BoardEventBroadcaster
// holds one of these and enqueues the enriched event; a drainer (the
// WebhookDispatcherService) consumes it. Keeping the sink a separate singleton — rather
// than giving the broadcaster a queue field directly — keeps the broadcaster
// dependency-light on the SSE hot path and makes the in-memory queue the documented
// swap-point for a durable outbox later.
public interface IWebhookSink
{
    void Enqueue(BoardEvent boardEvent);
}
