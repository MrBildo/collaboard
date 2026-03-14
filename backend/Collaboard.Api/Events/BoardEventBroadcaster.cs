using System.Threading.Channels;

namespace Collaboard.Api.Events;

public class BoardEventBroadcaster
{
    private readonly List<Channel<string>> _subscribers = [];
    private readonly Lock _lock = new();

    public ChannelReader<string> Subscribe()
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<string> reader)
    {
        lock (_lock)
        {
            _subscribers.RemoveAll(ch => ch.Reader == reader);
        }
    }

    public void Publish(string eventType)
    {
        lock (_lock)
        {
            // Remove dead channels and write to live ones
            _subscribers.RemoveAll(ch =>
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
}
