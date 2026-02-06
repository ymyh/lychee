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
    /// Executes system schedules up to the specified endpoint.
    /// </summary>
    /// <param name="scheduleEnd">The schedule at which to stop execution (exclusive); null to execute all schedules.</param>
    /// <remarks>
    /// This method supports resumable execution. If not all schedules are executed in one call,
    /// subsequent calls will continue from where execution left off. When all schedules complete,
    /// all registered events swap their front and back buffers.
    /// </remarks>
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
