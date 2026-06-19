using System.Collections.Concurrent;

namespace Collaboard.Api.Events;

// The in-memory webhook sink. Enqueue is called (after a successful SaveChanges) by the
// shared emit helper via BoardEventBroadcaster.Publish; the Phase 2 dispatcher drains via
// TryDequeue. In-memory means an API restart drops un-drained events — the deliberate v1
// reliability bar (#320), and the documented swap-point for a durable outbox: the
// IWebhookSink seam is what makes that a swap, not a rewrite. Card #320.
public sealed class WebhookQueue : IWebhookSink
{
    private readonly ConcurrentQueue<BoardEvent> _queue = new();

    public void Enqueue(BoardEvent boardEvent) => _queue.Enqueue(boardEvent);

    public bool TryDequeue(out BoardEvent? boardEvent) => _queue.TryDequeue(out boardEvent);

    public int Count => _queue.Count;
}
