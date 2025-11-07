using lychee.interfaces;

namespace lychee;

public sealed class World(TypeRegistrar typeRegistrar) : IDisposable
{
#region Fields

    internal readonly SystemSchedules SystemSchedules = new();

    public readonly EntityPool EntityPool = new();

    public readonly ArchetypeManager ArchetypeManager = new(typeRegistrar);

    private readonly List<IEvent> events = [];

#endregion

    ~World()
    {
        Dispose();
    }

#region Internal methods

    internal void AddEvent(IEvent ev)
    {
        events.Add(ev);
    }

#endregion

#region Public methods

    /// <summary>
    /// Trigger all system schedules to execute once.
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

        GC.SuppressFinalize(this);
    }

#endregion
}
