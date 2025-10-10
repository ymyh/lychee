namespace lychee;

public sealed class World(TypeRegistry typeRegistry) : IDisposable
{
#region Fields

    public readonly SystemSchedules SystemSchedules = new();

    public readonly EntityPool EntityPool = new();

    public readonly ArchetypeManager ArchetypeManager = new(typeRegistry);

#endregion

    ~World()
    {
        Dispose();
    }

#region Public methods

    public void Update()
    {
        SystemSchedules.Execute();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ArchetypeManager.Dispose();
        GC.SuppressFinalize(this);
    }

#endregion
}
