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
    public App(uint threadCount)
    {
        World = new(TypeRegistrar);
        ResourcePool = new(TypeRegistrar);
        ThreadPool = new((int)threadCount);
    }

    /// <summary>
    /// Creates an App with thread count of <see cref="Environment.ProcessorCount"/> / 2.
    /// </summary>
    public App() : this((uint)Environment.ProcessorCount / 2)
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

    /// <summary>
    /// Add a new resource with given value. Each type of resource can be added only once.
    /// </summary>
    /// <param name="resource">The resource to add.</param>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <returns>The resource just added.</returns>
    public T AddResource<T>(T resource)
    {
        return ResourcePool.AddResource(resource);
    }

    /// <summary>
    /// Add a new resource with default value. Each type of resource can be added only once.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <returns>The resource just added.</returns>
    public T AddResource<T>() where T : new()
    {
        return ResourcePool.AddResource<T>();
    }

    /// <summary>
    /// Gets the resource added before, target resource must be class.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <returns></returns>
    public T GetResource<T>() where T : class
    {
        return ResourcePool.GetResource<T>();
    }

    /// <summary>
    /// Gets the reference of the resource added before, target resource must be unmanaged.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public ref T GetResourceStructRef<T>() where T : unmanaged
    {
        return ref ResourcePool.GetResourceStructRef<T>();
    }

    /// <summary>
    /// Gets the reference of the resource added before, target resource must be class.
    /// </summary>
    /// <typeparam name="T">The resource type, must be class.</typeparam>
    /// <returns></returns>
    public ref T GetResourceClassRef<T>() where T : class
    {
        return ref ResourcePool.GetResourceClassRef<T>();
    }

    /// <summary>
    /// Gets the pointer of an unmanaged resource type.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <returns></returns>
    public byte[] GetResourcePtr<T>() where T : unmanaged
    {
        return ResourcePool.GetResourcePtr<T>();
    }

    /// <summary>
    /// Add a system schedule.
    /// </summary>
    /// <param name="schedule">The schedule to add.</param>
    /// <param name="name">The schedule name.</param>
    public void AddSchedule(ISchedule schedule, string name)
    {
        World.SystemSchedules.AddSchedule(schedule, name);
    }

    /// <summary>
    /// Add a system schedule after another system schedule.
    /// </summary>
    /// <param name="schedule">The schedule to add.</param>
    /// <param name="name">The schedule name.</param>
    /// <param name="addAfter">The schedule name to add after.</param>
    /// <exception cref="ArgumentException">Thrown when the schedule already exists or the addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, string name, string addAfter)
    {
        World.SystemSchedules.AddSchedule(schedule, name, addAfter);
    }

    /// <summary>
    /// Clear all system schedules.
    /// </summary>
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

    /// <summary>
    /// Gets a system schedule by name.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <returns></returns>
    public ISchedule? GetSchedule(string name)
    {
        return World.SystemSchedules.GetSchedule(name);
    }

    /// <summary>
    /// Gets a system schedule by name.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <returns></returns>
    public T? GetSchedule<T>(string name) where T : class, ISchedule
    {
        return World.SystemSchedules.GetSchedule<T>(name);
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
