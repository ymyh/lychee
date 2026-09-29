using lychee.interfaces;

namespace lychee;

/// <summary>
/// The ECS world that manages entities, components, archetypes, and system schedules.
/// </summary>
/// <param name="typeRegistrar">The type registrar containing component and resource metadata.</param>
/// <param name="chunkSizeHint">A hint for the average size of archetype chunks in bytes, used for optimizing memory layout.</param>
public sealed class World(TypeRegistrar typeRegistrar, int chunkSizeHint) : IDisposable
{
#region Public Fields

    /// <summary>
    /// Manages entity creation, destruction, and ID allocation.
    /// </summary>
    public readonly EntityPool EntityPool = new();

    /// <summary>
    /// Manages archetypes which store entities grouped by their component composition.
    /// </summary>
    public readonly ArchetypeManager ArchetypeManager = new(typeRegistrar, chunkSizeHint);

#endregion

#region Private Fields

    private readonly List<IEvent> commitPointEvents = [];

    private readonly List<IEvent> scheduleEndEvents = [];

    private readonly List<IEvent> updateEndEvents = [];

    private bool disposed;

#endregion

#region Internal Methods

    // An event belongs to exactly one bucket. It has to be that way because swapping twice with nothing
    // read in between drops the batch the first swap published, which is what happens when an event is
    // published both earlier and at the end of the update.
    internal void AddEvent(IEvent ev, EventPublishTiming timing)
    {
        switch (timing)
        {
            case EventPublishTiming.CommitPoint:
                commitPointEvents.Add(ev);
                break;
            case EventPublishTiming.ScheduleEnd:
                scheduleEndEvents.Add(ev);
                break;
            case EventPublishTiming.UpdateEnd:
                updateEndEvents.Add(ev);
                break;
        }
    }

    internal void RemoveAllEntities()
    {
        EntityPool.Clear();
        ArchetypeManager.ClearData();
    }

    internal void SwapEvents(EventPublishTiming timing)
    {
        List<IEvent> events;

        switch (timing)
        {
            case EventPublishTiming.CommitPoint:
                events = commitPointEvents;
                break;
            case EventPublishTiming.ScheduleEnd:
                events = scheduleEndEvents;
                break;
            case EventPublishTiming.UpdateEnd:
                events = updateEndEvents;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(timing), timing, null);
        }

        foreach (var ev in events)
        {
            ev.ExchangeFrontBack();
        }
    }

#endregion

#region IDisposable Implementation

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ArchetypeManager.Dispose();
    }

#endregion
}
