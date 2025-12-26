using lychee.interfaces;
using ThreadPool = lychee.threading.ThreadPool;

namespace lychee;

/// <summary>
/// The ECS application.
/// </summary>
public sealed class App : IDisposable
{
#region Fields

    public readonly TypeRegistrar TypeRegistrar = new();

    public readonly ResourcePool ResourcePool;

    public readonly World World;

    internal readonly ThreadPool ThreadPool;

    private readonly HashSet<Type> pluginInstalled = [];

    private readonly List<IDisposable> disposables = [];

#endregion

#region Constructors & Destructors

    /// <summary>
    /// Creates an App with specified thread count.
    /// </summary>
    /// <param name="threadCount">The thread count of the thread pool.</param>
    public App(int threadCount)
    {
        World = new(TypeRegistrar);
        ResourcePool = new(TypeRegistrar);
        ThreadPool = new(threadCount);
    }

    /// <summary>
    /// Creates an App with thread count <see cref="Environment.ProcessorCount"/> / 2.
    /// </summary>
    public App() : this(Environment.ProcessorCount / 2)
    {
    }

#endregion

#region Public methods

    public void AddEvent<T>()
    {
        var ev = new Event<T>();

        ResourcePool.AddResource(ev);
        World.AddEvent(ev);
    }

    public T AddResource<T>(T resource)
    {
        return ResourcePool.AddResource(resource);
    }

    public T AddResource<T>() where T : new()
    {
        return ResourcePool.AddResource<T>();
    }

    public T GetResource<T>()
    {
        return ResourcePool.GetResource<T>();
    }

    public ref T GetResourceStructRef<T>() where T : unmanaged
    {
        return ref ResourcePool.GetResourceStructRef<T>();
    }

    public ref T GetResourceClassRef<T>() where T : class
    {
        return ref ResourcePool.GetResourceClassRef<T>();
    }

    public byte[] GetResourcePtr<T>() where T : unmanaged
    {
        return ResourcePool.GetResourcePtr<T>();
    }

    public void AddSchedule(ISchedule schedule)
    {
        World.SystemSchedules.AddSchedule(schedule);
    }

    public void AddSchedule(ISchedule schedule, ISchedule addAfter)
    {
        World.SystemSchedules.AddSchedule(schedule, addAfter);
    }

    public void ClearSchedules()
    {
        World.SystemSchedules.ClearSchedules();
    }

    /// <summary>
    /// Creates a <see cref="Commands"/>.
    /// </summary>
    /// <returns></returns>
    public Commands CreateCommands()
    {
        var commands = new Commands(this);
        disposables.Add(commands);

        return commands;
    }

    /// <summary>
    /// Creates a <see cref="ThreadPool"/> that will call Dispose when <see cref="App"/> destoried.
    /// </summary>
    /// <returns></returns>
    public ThreadPool CreateThreadPool(int threadCount)
    {
        var pool = new ThreadPool(threadCount);
        disposables.Add(pool);

        return pool;
    }

    public ISchedule? GetSchedule(string name)
    {
        return World.SystemSchedules.GetSchedule(name);
    }

    /// <summary>
    /// Install a plugin to the application. Install same plugin takes no effect.
    /// </summary>
    /// <param name="plugin">The plugin to install.</param>
    /// <typeparam name="T">The plugin type.</typeparam>
    /// <returns></returns>
    public T InstallPlugin<T>(T plugin) where T : IPlugin
    {
        var type = plugin.GetType();
        if (pluginInstalled.Contains(type))
        {
            return plugin;
        }

        plugin.Install(this);
        pluginInstalled.Add(plugin.GetType());

        return plugin;
    }

    /// <summary>
    /// Install a plugin to the application. Install same plugin takes no effect.
    /// </summary>
    /// <typeparam name="T">The plugin type.</typeparam>
    /// <returns></returns>
    public T InstallPlugin<T>() where T : IPlugin, new()
    {
        return InstallPlugin(new T());
    }

    /// <summary>
    /// Check if a plugin is installed.
    /// </summary>
    /// <typeparam name="T">The plugin type.</typeparam>
    /// <returns>True if the plugin is installed, otherwise false.</returns>
    public bool CheckPluginInstalled<T>() where T : IPlugin
    {
        return pluginInstalled.Contains(typeof(T));
    }

    /// <summary>
    /// Update the application once.
    /// </summary>
    /// <param name="scheduleEnd">Trigger schedule execute up to before this schedule. If null or not found, all schedules will be executed.
    /// Call this method again will continue from the last schedule until all schedules are executed and then begin a new loop.
    /// </param>
    public void Update(ISchedule? scheduleEnd = null)
    {
        World.Update(scheduleEnd);
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ThreadPool.Dispose();
        World.Dispose();

        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }
    }

#endregion
}
