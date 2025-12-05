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

    private readonly List<Commands> commandsList = [];

#endregion

#region Constructors & Destructors

    /// <param name="threadCount">The thread count of the thread pool. If 0, use <see cref="Environment.ProcessorCount"/> / 2 as the default value.</param>
    public App(int threadCount = 0)
    {
        World = new(TypeRegistrar);
        ResourcePool = new(TypeRegistrar);

        if (threadCount == 0)
        {
            ThreadPool = new(Environment.ProcessorCount / 2);
        }
        else
        {
            ThreadPool = new(threadCount);
        }
    }

    ~App()
    {
        Dispose();
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

    public Commands CreateCommands()
    {
        commandsList.Add(new(this));
        return commandsList[^1];
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
    public T InstallPlugin<T>(T plugin) where T : IPlugin, new()
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

        foreach (var commands in commandsList)
        {
            commands.Dispose();
        }

        GC.SuppressFinalize(this);
    }

#endregion
}
