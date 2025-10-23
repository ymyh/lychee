namespace lychee;

public sealed class World(TypeRegistry typeRegistry) : IDisposable
{
#region Fields

    public readonly SystemSchedules SystemSchedules = new();

    public readonly EntityPool EntityPool = new();

    public readonly ArchetypeManager ArchetypeManager = new(typeRegistry);

    internal readonly EventCenter EventCenter = new(typeRegistry);

#endregion

    ~World()
    {
        Dispose();
    }

#region Public methods

    /// <summary>
    /// Trigger all system schedules to execute once.
    /// </summary>
    public void Update()
    {
        SystemSchedules.Execute();
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
