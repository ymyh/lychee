using lychee.interfaces;
using ThreadPool = lychee.threading.ThreadPool;

namespace lychee;

/// <summary>
/// The ECS application. <br/>
/// </summary>
public sealed class App : IDisposable
{
#region Fields

    public readonly TypeRegistrar TypeRegistrar = new();

    public readonly ResourcePool ResourcePool;

    public readonly World World;

    public readonly ThreadPool ThreadPool;

    private readonly HashSet<Type> pluginInstalled = [];

#endregion

#region Constructors & Destructors

    public App(int threadCountHint = 0)
    {
        World = new(TypeRegistrar);
        ResourcePool = new(TypeRegistrar);

        if (threadCountHint == 0)
        {
            ThreadPool = new(Environment.ProcessorCount / 2);
        }
        else
        {
            ThreadPool = new(threadCountHint);
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

    public void AddResource<T>(T resource)
    {
        ResourcePool.AddResource(resource);
    }

    public T GetResource<T>()
    {
        return ResourcePool.GetResource<T>();
    }

    public ref T GetResourceRef<T>() where T : unmanaged
    {
        return ref ResourcePool.GetResourceRef<T>();
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
    /// Check if a plugin is installed.
    /// </summary>
    /// <typeparam name="T">The plugin type.</typeparam>
    /// <returns>True if the plugin is installed, otherwise false.</returns>
    public bool CheckInstalledPlugin<T>() where T : IPlugin
    {
        return pluginInstalled.Contains(typeof(T));
    }

    /// <summary>
    /// Update the application once.
    /// </summary>
    /// <param name="scheduleEnd">Trigger schedule execution up to this schedule. If null, all schedules will be executed.</param>
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

        GC.SuppressFinalize(this);
    }

#endregion
}
