using lychee.interfaces;

namespace lychee;

/// <summary>
/// The ECS application.
/// </summary>
public sealed class App : IDisposable
{
#region Fields

    public readonly TypeRegistry TypeRegistry = new();

    public readonly ResourcePool ResourcePool;

    public readonly World World;

    /// <summary>
    /// Controls how the application should run. <br/>
    /// The default runner will update the world until <see cref="ShouldExit"/> is set to true.
    /// </summary>
    public Action Runner { get; set; }

    /// <summary>
    /// Indicates whether the application should exit.
    /// </summary>
    public bool ShouldExit { get; set; }

#endregion

#region Constructors & Destructors

    public App()
    {
        World = new(TypeRegistry);
        ResourcePool = new(TypeRegistry);

        Runner = () =>
        {
            while (!ShouldExit)
            {
                World.Update();
            }
        };
    }

    ~App()
    {
        Dispose();
    }

#endregion

#region Public methods

    public void AddEvent<T>()
    {
        ResourcePool.AddResource<Event<T>>(new());
    }

    public void InstallPlugin(IPlugin plugin)
    {
        plugin.Install(this);
    }

    public void Run()
    {
        Runner();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        World.Dispose();
        GC.SuppressFinalize(this);
    }

#endregion
}
