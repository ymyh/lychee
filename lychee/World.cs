using lychee.interfaces;

namespace lychee;

/// <summary>
/// An ECS world that holds all entities, components, systems and events.
/// </summary>
/// <param name="typeRegistrar">The <see cref="TypeRegistrar"/> from <see cref="App"/>.</param>
public sealed class World(TypeRegistrar typeRegistrar) : IDisposable
{
#region Fields

    public readonly SystemSchedules SystemSchedules = new();

    public readonly EntityPool EntityPool = new();

    public readonly ArchetypeManager ArchetypeManager = new(typeRegistrar);

    private readonly List<IEvent> events = [];

#endregion

#region Internal methods

    internal void AddEvent(IEvent ev)
    {
        events.Add(ev);
    }

#endregion

#region Public methods

    /// <summary>
    /// Trigger all system schedules to execute once.
    /// <param name="scheduleEnd">Trigger schedule execute up to before this schedule. If null or not found, all schedules will be executed.
    /// Call this method again will continue from the last schedule until all schedules are executed and then begin a new loop.
    /// </param>
    /// </summary>
    public void Update(ISchedule? scheduleEnd = null)
    {
        if (SystemSchedules.Execute(scheduleEnd))
        {
            foreach (var ev in events)
            {
                ev.PrepareForNextUpdate();
            }
        }
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ArchetypeManager.Dispose();
        EntityPool.Dispose();
    }

#endregion
}
