using lychee.collections;
using lychee.interfaces;

namespace lychee;

/// <summary>
/// A thread-safe event queue that uses double buffering.
/// Events sent in the current update will be readable in the next update.
/// </summary>
/// <typeparam name="T">The type of event data.</typeparam>
public sealed class Event<T> : IEvent
{
    private readonly DoubleBufferQueue<T> queue = new();

    /// <summary>
    /// Sends an event. The event will be readable in the next update.
    /// </summary>
    /// <param name="ev">The event data to send.</param>
    public void SendEvent(T ev)
    {
        queue.Enqueue(ev);
    }

    /// <summary>
    /// Gets all events that were sent in the previous update.
    /// </summary>
    /// <returns>Events from the previous update.</returns>
    public IEnumerable<T> GetEnumerable()
    {
        return queue.GetEnumerable();
    }

    /// <summary>
    /// Swaps the front and back buffers and clears the new back buffer.
    /// This should be called once per update, typically at the beginning of update processing.
    /// </summary>
    public void ExchangeFrontBack()
    {
        queue.Exchange();
        queue.ClearBack();
    }
}
