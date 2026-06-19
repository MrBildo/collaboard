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

    // The typed fan-out path (#320). Raised by the eight converted card-mutation
    // call-sites through the shared WebhookEventFactory. Two projections:
    //   1. SSE — DOWNSAMPLE to the existing thin signal. The wire stays byte-for-byte
    //      `event: board-updated\ndata: {}` (PublishToBoard writes the identical string
    //      the unconverted sites emit). The browser consumer sees no change — the
    //      safety property that protects the working SSE consumer.
    //   2. Webhook — full fidelity. Hand the enriched event to the sink, which the
    //      Phase 2 dispatcher drains. Dark deployments still enqueue; the dispatcher
    //      no-ops when no endpoint is configured.
    public void Publish(BoardEvent boardEvent)
    {
        ArgumentNullException.ThrowIfNull(boardEvent);

        PublishToBoard(boardEvent.BoardId, "board-updated");

        _webhookSink.Enqueue(boardEvent);
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
