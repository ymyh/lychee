using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace lychee.collections;

/// <summary>
/// Represents a double buffer queue. <br/>
/// Write into the back buffer and read from the front buffer.
/// </summary>
/// <typeparam name="T">The type of elements in the queue.</typeparam>
public sealed class DoubleBufferQueue<T>
{
    private List<T> front = [];

    private List<T> back = [];

    /// <summary>
    /// Add an item to the back buffer.
    /// This method is thread-safe.
    /// </summary>
    /// <param name="item">The item to add to the back buffer.</param>
    public void Enqueue(T item)
    {
        lock (back)
        {
            back.Add(item);
        }
    }

    /// <summary>
    /// Clear the back buffer.
    /// </summary>
    public void ClearBack()
    {
        back.Clear();
    }

    /// <summary>
    /// Exchange front and back buffer.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
    public void Exchange()
    {
        (front, back) = (back, front);
    }

    public Span<T> GetFrontSpan()
    {
        return CollectionsMarshal.AsSpan(front);
    }

    public IEnumerable<T> GetEnumerable()
    {
        return front;
    }
}
