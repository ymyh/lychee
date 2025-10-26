using lychee.collections;

namespace lychee;

public sealed class Event<T>
{
    private readonly DoubleBufferQueue<T> queue = new();

    public void SendEvent(T ev)
    {
        queue.Enqueue(ev);
    }
}
