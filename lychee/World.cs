using lychee.interfaces;

namespace lychee;

/// <summary>
/// The central ECS world that manages entities, components, archetypes, and system schedules.
/// </summary>
/// <param name="typeRegistrar">The type registrar containing component and resource metadata.</param>
public sealed class World(TypeRegistrar typeRegistrar) : IDisposable
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
    /// Executes all system schedules up to the specified end point.
    /// If scheduleEnd is null or not found, all schedules are executed in order.
    /// Calling this method again continues execution from where it left off,
    /// looping back to the first schedule after the last one completes.
    /// </summary>
    /// <param name="scheduleEnd">
    /// The schedule at which to stop execution. If null, executes all schedules.
    /// Subsequent calls will resume from the next schedule in sequence.
    /// </param>
    public void Update(ISchedule? scheduleEnd = null)
    {
        if (SystemSchedules.Execute(scheduleEnd))
        {
            foreach (var ev in events)
            {
                ev.ExchangeFrontBack();
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
