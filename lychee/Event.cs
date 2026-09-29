using lychee.collections;
using lychee.interfaces;

namespace lychee;

/// <summary>
/// Determines when events written during an update become readable.
/// The timing is chosen once, when the event is registered with <see cref="App.AddEvent{T}"/>.
/// </summary>
public enum EventPublishTiming
{
    /// <summary>
    /// Events are published at every commit point, i.e. whenever commands are applied, and become
    /// readable to the systems of the following layer of the same schedule.
    /// </summary>
    CommitPoint,

    /// <summary>
    /// Events are published at the end of every schedule and become readable to the systems of the
    /// following schedule.
    /// </summary>
    ScheduleEnd,

    /// <summary>
    /// Events are published at the end of the update and become readable in the next update.
    /// This is the default: every reader sees a whole update worth of events at once.
    /// </summary>
    UpdateEnd
}

/// <summary>
/// Writes into the back buffer of an <see cref="Event{T}"/>.
/// Obtained by declaring an <c>EventWriter&lt;T&gt;</c> parameter on a system's <c>Execute</c> method,
/// which resolves the <see cref="Event{T}"/> from the resource pool.
/// </summary>
/// <typeparam name="T">The type of event data.</typeparam>
/// <param name="ev">The event queue this writer writes into.</param>
public sealed class EventWriter<T>(Event<T> ev)
{
    /// <summary>
    /// Sends an event. It stays invisible to readers until the queue is published according to its
    /// <see cref="EventPublishTiming"/>.
    /// </summary>
    /// <param name="t">The event data to send.</param>
    public void Send(T t)
    {
        ev.SendEvent(t);
    }
}

/// <summary>
/// Reads events from the front buffer of an <see cref="Event{T}"/>, one at a time.
/// The cursor lives on the reader itself, so every system gets its own view of the events and never
/// consumes one on behalf of another system.
/// </summary>
/// <typeparam name="T">The type of event data.</typeparam>
/// <param name="ev">The event queue this reader reads from.</param>
public sealed class EventReader<T>(Event<T> ev)
{
    private int index;

    /// <summary>
    /// Reads the next event of the current batch, advancing the cursor.
    /// Returns false once the batch is exhausted; the cursor is not rewound, so a reader consumes
    /// each event at most once.
    /// </summary>
    /// <param name="t">The event data that was read, or the default value when nothing is left.</param>
    /// <returns>true when an event was read; false when the batch is exhausted.</returns>
    public bool Read(out T t)
    {
        var span = ev.GetFront();
        if (index < span.Length)
        {
            t = span[index++];
            return true;
        }

        t = default!;
        return false;
    }
}

/// <summary>
/// A thread-safe event queue that uses double buffering.
/// Events written while a batch is readable stay invisible until the queue is exchanged, which
/// happens according to the <see cref="EventPublishTiming"/> chosen at registration.
/// </summary>
/// <typeparam name="T">The type of event data.</typeparam>
public sealed class Event<T>() : IEvent
{
    private readonly DoubleBufferQueue<T> queue = new();

    /// <summary>
    /// Sends an event. The event stays in the back buffer until the queue is exchanged.
    /// </summary>
    /// <param name="ev">The event data to send.</param>
    internal void SendEvent(T ev)
    {
        queue.Enqueue(ev);
    }

    /// <summary>
    /// Swaps the front and back buffers and clears the new back buffer, making the events written
    /// since the previous swap readable and discarding the batch before that one.
    /// </summary>
    public void ExchangeFrontBack()
    {
        queue.Exchange();
        queue.ClearBack();
    }

    internal ReadOnlySpan<T> GetFront()
    {
        return queue.GetFrontSpan();
    }
}
