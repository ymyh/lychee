using lychee.collections;
using lychee.interfaces;

namespace lychee;

public sealed class Event<T> : IEvent
{
    private readonly DoubleBufferQueue<T> queue = new();

    public void SendEvent(T ev)
    {
        queue.Enqueue(ev);
    }

    public IEnumerable<T> GetEnumerable()
    {
        return queue.GetEnumerable();
    }

    public void ExchangeFrontBack()
    {
        queue.Exchange();
        queue.ClearBack();
    }
}
