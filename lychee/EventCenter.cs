using lychee.collections;

namespace lychee;

public sealed class EventCenter(TypeRegistry typeRegistry) : IDisposable
{
    private readonly SparseMap<object> allEventQueues = [];

    internal DoubleBufferQueue<T> GetOrCreateQueue<T>()
    {
        var typeId = typeRegistry.Register<T>();
        allEventQueues.TryGetValue(typeId, out var queue);

        if (queue == null)
        {
            queue = new DoubleBufferQueue<T>();
            allEventQueues.Add(typeId, queue);
        }

        return (DoubleBufferQueue<T>)queue;
    }

#region IDispose member

    public void Dispose()
    {
        allEventQueues.Dispose();
    }

#endregion
}
