using System.Threading.Channels;

namespace Collaboard.Api.Events;

public class BoardEventBroadcaster
{
    private readonly Dictionary<Guid, List<Channel<string>>> _boardSubscribers = [];
    private readonly List<Channel<string>> _globalSubscribers = [];
    private readonly Lock _lock = new();

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

    public void PublishGlobal(string eventType)
    {
        lock (_lock)
        {
            foreach (var (_, subscribers) in _boardSubscribers)
            {
                WriteToSubscribers(subscribers, eventType);
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

    private static void WriteToSubscribers(List<Channel<string>> subscribers, string eventType)
    {
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
}
