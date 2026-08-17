using System.Threading.Channels;

namespace Collaboard.Api.Events;

public class BoardEventBroadcaster(IWebhookSink webhookSink)
{
    private readonly Dictionary<Guid, List<Channel<string>>> _boardSubscribers = [];
    private readonly Lock _lock = new();
    private readonly IWebhookSink _webhookSink = webhookSink
        ?? throw new ArgumentNullException(nameof(webhookSink));

    public ChannelReader<string> Subscribe(Guid boardId)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_lock)
        {
            if (!_boardSubscribers.TryGetValue(boardId, out var subscribers))
            {
                subscribers = [];
                _boardSubscribers[boardId] = subscribers;
            }

            subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public void Unsubscribe(Guid boardId, ChannelReader<string> reader)
    {
        lock (_lock)
        {
            if (_boardSubscribers.TryGetValue(boardId, out var subscribers))
            {
                subscribers.RemoveAll(ch => ch.Reader == reader);
                if (subscribers.Count == 0)
                {
                    _boardSubscribers.Remove(boardId);
                }
            }
        }
    }

    public void PublishBoardUpdated(Guid boardId) => PublishToBoard(boardId, "board-updated");

    // The typed fan-out path. Raised by the eight converted card-mutation
    // call-sites through the shared WebhookEventFactory. Two projections:
    //   1. SSE — DOWNSAMPLE to the existing thin signal. The wire stays byte-for-byte
    //      `event: board-updated\ndata: {}` (PublishToBoard writes the identical string
    //      the unconverted sites emit). The browser consumer sees no change — the
    //      safety property that protects the working SSE consumer.
    //   2. Webhook — full fidelity. Hand the enriched event to the sink, which the
    //      dispatcher drains. Dark deployments still enqueue; the dispatcher
    //      no-ops when no endpoint is configured.
    public void Publish(BoardEvent boardEvent)
    {
        ArgumentNullException.ThrowIfNull(boardEvent);

        PublishToBoard(boardEvent.BoardId, "board-updated");

        _webhookSink.Enqueue(boardEvent);
    }

    // The multi-axis co-fire path. A single user action (PATCH /cards, update_card)
    // can change several axes at once — content, lane, labels — and must raise a webhook
    // event PER changed axis while ringing EXACTLY ONE SSE bell. Calling Publish per event
    // would ring N bells and break the browser-safety coalesce property (the SSE wire must
    // stay byte-for-byte the single `board-updated` signal these sites emit today). So: ring
    // the board bell once (identical string), then enqueue every event to the webhook sink.
    // The bell rings even when `events` is empty — preserving the prior "every such mutation
    // rings one bell" SSE behaviour for an all-no-op edit.
    public void PublishCoalesced(Guid boardId, IReadOnlyList<BoardEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        PublishToBoard(boardId, "board-updated");

        foreach (var boardEvent in events)
        {
            _webhookSink.Enqueue(boardEvent);
        }
    }

    // Broadcasts to all board-scoped subscribers (every connected client regardless of board)
    public void PublishGlobal(string eventType)
    {
        lock (_lock)
        {
            List<Guid> emptyBoards = [];
            foreach (var (boardId, subscribers) in _boardSubscribers)
            {
                WriteToSubscribers(subscribers, eventType);
                if (subscribers.Count == 0)
                {
                    emptyBoards.Add(boardId);
                }
            }

            foreach (var boardId in emptyBoards)
            {
                _boardSubscribers.Remove(boardId);
            }
        }
    }

    private void PublishToBoard(Guid boardId, string eventType)
    {
        lock (_lock)
        {
            if (_boardSubscribers.TryGetValue(boardId, out var subscribers))
            {
                WriteToSubscribers(subscribers, eventType);
                if (subscribers.Count == 0)
                {
                    _boardSubscribers.Remove(boardId);
                }
            }
        }
    }

    public void CompleteAll()
    {
        lock (_lock)
        {
            foreach (var (_, subscribers) in _boardSubscribers)
            {
                foreach (var ch in subscribers)
                {
                    ch.Writer.TryComplete();
                }
            }

            _boardSubscribers.Clear();
        }
    }

    private static void WriteToSubscribers(List<Channel<string>> subscribers, string eventType) =>
        subscribers.RemoveAll(ch =>
        {
            if (!ch.Writer.TryWrite(eventType))
            {
                ch.Writer.TryComplete();
                return true;
            }

            return false;
        });
}
