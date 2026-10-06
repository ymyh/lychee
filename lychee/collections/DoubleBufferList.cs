using System.Runtime.InteropServices;

namespace lychee.collections;

/// <summary>
/// Write into the back list and read from the front list.
/// Writing or reading from the list is thread-safe, but exchanging two list is not thread-safe.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public sealed class DoubleBufferList<T>
{
    private List<T> front = [];

    private List<T> back = [];

    private readonly Lock backBufferLock = new();

    /// <summary>
    /// Add an item to the back buffer.
    /// This method is thread-safe.
    /// </summary>
    /// <param name="item">The item to add to the back buffer.</param>
    public void Enqueue(T item)
    {
        lock (backBufferLock)
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
