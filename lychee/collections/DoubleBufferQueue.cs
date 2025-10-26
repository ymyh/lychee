using System.Diagnostics.CodeAnalysis;

namespace lychee.collections;

public sealed class DoubleBufferQueue<T>
{
    private List<T> front = [];

    private List<T> back = [];

    public void Enqueue(T item)
    {
        lock (back)
        {
            back.Add(item);
        }
    }

    public IEnumerable<T> GetEnumerable()
    {
        return front;
    }

    /// <summary>
    /// Exchange front and back buffer. <br/>
    /// Never call this method in parallel.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
    public void Exchange()
    {
        (front, back) = (back, front);
    }

    public void ClearBack()
    {
        back.Clear();
    }
}
