using System.Diagnostics;
using lychee.collections;

namespace lychee;

public sealed class Commander(App app)
{
    private Dictionary<Type, object> eventQueues = new();

    public void SendEvent<T>(T ev)
    {
        if (eventQueues.TryGetValue(typeof(T), out var obj))
        {
            Debug.Assert(obj is DoubleBufferQueue<T>);

            var queue = (DoubleBufferQueue<T>)obj;
            queue.Enqueue(ev);
        }
        else
        {
            var queue = app.ResourcePool.GetResource<DoubleBufferQueue<T>>();
            queue.Enqueue(ev);

            eventQueues.Add(typeof(T), queue);
        }
    }
}
