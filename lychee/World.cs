using lychee.interfaces;

namespace lychee;

/// <summary>
/// The ECS world that manages entities, components, archetypes, and system schedules.
/// </summary>
/// <param name="typeRegistrar">The type registrar containing component and resource metadata.</param>
/// <param name="chunkSizeHint">A hint for the average size of archetype chunks in bytes, used for optimizing memory layout.</param>
public sealed class World(TypeRegistrar typeRegistrar, int chunkSizeHint) : IDisposable
{
#region Fields

    /// <summary>
    /// Manages the collection and execution order of all system schedules.
    /// </summary>
    public readonly SystemSchedules SystemSchedules = new();

    /// <summary>
    /// Manages entity creation, destruction, and ID allocation.
    /// </summary>
    public readonly EntityPool EntityPool = new();

    /// <summary>
    /// Manages archetypes which store entities grouped by their component composition.
    /// </summary>
    public readonly ArchetypeManager ArchetypeManager = new(typeRegistrar, chunkSizeHint);

    private readonly List<IEvent> events = [];

    private bool disposed = false;

#endregion

#region Internal methods

    internal void AddEvent(IEvent ev)
    {
        events.Add(ev);
    }

#endregion

#region Internal methods

    internal void Update(ISchedule? scheduleEnd = null)
    {
        if (SystemSchedules.Execute(scheduleEnd))
        {
            foreach (var ev in events)
            {
                ev.ExchangeFrontBack();
            }
        }
    }

    internal void RemoveAllEntities()
    {
        EntityPool.Clear();
        ArchetypeManager.ClearData();
    }

#endregion

#region IDisposable Member

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
