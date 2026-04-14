using lychee.interfaces;
using ThreadPool = lychee.threading.ThreadPool;

namespace lychee;

public sealed class AppDescriptor
{
    /// <summary>
    /// A hint for archetype chunk size in bytes. Larger chunk sizes can improve performance by reducing the number of chunks and
    /// increasing cache locality, but may also increase memory usage and fragmentation.
    /// The optimal value depends on the typical size of entities and components in your application. The default value is 16 KB.
    /// </summary>
    public int ChunkSizeHint { get; set; } = 16384;

    /// <summary>
    /// The number of threads to use in the default thread pool for parallel system execution. The default value is half of the available processor count.
    /// Adjusting this value can help balance performance and resource usage based on the workload and hardware capabilities of your application.
    /// </summary>
    public int ThreadCount { get; set; } = Environment.ProcessorCount / 2;

    /// <summary>
    /// The capacity of the thread pool's work queue. This limits how many tasks can be queued for execution before new tasks are rejected.
    /// A larger capacity allows for more queued tasks but may increase memory usage. The default value is 64.
    /// </summary>
    public int ThreadPoolQueueCapacity { get; set; } = 64;
}

/// <summary>
/// The ECS application.
/// </summary>
public sealed class App : IDisposable
{
#region Fields

    public TypeRegistrar TypeRegistrar { get; } = new();

    public ResourcePool ResourcePool { get; }

    public World World { get; }

    internal readonly ThreadPool ThreadPool;

    private readonly List<IDisposable> disposables = [];

    private bool disposed = false;

#endregion

#region Constructors

    /// <summary>
    /// Creates an App with specified thread pool setting.
    /// </summary>
    /// <param name="descriptor">The app descriptor.</param>
    public App(AppDescriptor descriptor)
    {
        World = new(TypeRegistrar, descriptor.ChunkSizeHint);
        ResourcePool = new(TypeRegistrar);
        ThreadPool = new(descriptor.ThreadCount, descriptor.ThreadPoolQueueCapacity);

        disposables.Add(World);
        disposables.Add(ResourcePool);
        disposables.Add(ThreadPool);
    }

    /// <summary>
    /// Creates an App with thread count of <see cref="Environment.ProcessorCount"/> / 2.
    /// </summary>
    public App() : this(new())
    {
    }

#endregion

#region Public methods

    /// <summary>
    /// Registers a new event type and adds it to both the resource pool and world.
    /// Events enable type-safe, decoupled communication between systems.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    public void AddEvent<T>()
    {
        var ev = new Event<T>();

        ResourcePool.AddResource(ev);
        World.AddEvent(ev);
    }

    /// <summary>
    /// Adds a resource instance to the resource pool. Each resource type can only be added once.
    /// </summary>
    /// <param name="resource">The resource instance to add.</param>
    /// <typeparam name="T">The resource type, must be a reference type.</typeparam>
    /// <returns>The resource instance that was added.</returns>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public T AddResource<T>(T resource) where T : class
    {
        return ResourcePool.AddResource(resource);
    }

    /// <summary>
    /// Adds a new resource instance with the default constructor to the resource pool.
    /// Each resource type can only be added once.
    /// </summary>
    /// <typeparam name="T">The resource type, must be a reference type with a default constructor.</typeparam>
    /// <returns>The newly created resource instance.</returns>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public T AddResource<T>() where T : class, new()
    {
        return ResourcePool.AddResource<T>();
    }

    /// <summary>
    /// Adds an unmanaged resource to the resource pool. The resource is stored in aligned native memory.
    /// Each resource type can only be added once.
    /// </summary>
    /// <param name="resource">The resource value to copy into native memory.</param>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public void AddResourceStruct<T>(T resource) where T : unmanaged
    {
        ResourcePool.AddResourceStruct(resource);
    }

    /// <summary>
    /// Adds a new unmanaged resource with the default value to the resource pool.
    /// Each resource type can only be added once.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public void AddResourceStruct<T>() where T : unmanaged
    {
        ResourcePool.AddResourceStruct<T>(new());
    }

    /// <summary>
    /// Retrieves a resource from the pool by type.
    /// </summary>
    /// <typeparam name="T">The resource type, must be a reference type.</typeparam>
    /// <returns>The resource instance.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
    public T GetResource<T>() where T : class
    {
        return ResourcePool.GetResource<T>();
    }

    /// <summary>
    /// Gets a mutable reference to a reference-type resource in the pool.
    /// </summary>
    /// <typeparam name="T">The resource type, must be a reference type.</typeparam>
    /// <returns>A reference to the resource.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
    public ref T GetResourceClassRef<T>() where T : class
    {
        return ref ResourcePool.GetResourceClassRef<T>();
    }

    /// <summary>
    /// Gets a mutable reference to an unmanaged resource stored in native memory.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <returns>A reference to the resource.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
    public ref T GetResourceStructRef<T>() where T : unmanaged
    {
        return ref ResourcePool.GetResourceStructRef<T>();
    }

    /// <summary>
    /// Gets a pointer to an unmanaged resource stored in native memory.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <returns>A pointer to the resource.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
    public unsafe T* GetResourcePtr<T>() where T : unmanaged
    {
        return ResourcePool.GetResourcePtr<T>();
    }

    /// <summary>
    /// Checks whether a resource of the specified type exists in the pool.
    /// </summary>
    /// <typeparam name="T">The resource type to check.</typeparam>
    /// <returns>True if the resource exists; otherwise, false.</returns>
    public bool HasResource<T>()
    {
        return ResourcePool.HasResource<T>();
    }

    /// <summary>
    /// Adds a system schedule with a unique name for execution ordering.
    /// </summary>
    /// <param name="schedule">The schedule instance to add.</param>
    public void AddSchedule(ISchedule schedule)
    {
        World.SystemSchedules.AddSchedule(schedule);
    }

    /// <summary>
    /// Adds a system schedule, inserting it after an existing schedule in the execution order.
    /// </summary>
    /// <param name="schedule">The schedule instance to add.</param>
    /// <param name="addAfter">The name of the schedule after which this schedule should execute.</param>
    /// <exception cref="ArgumentException">Thrown when the schedule name already exists or the addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, string addAfter)
    {
        World.SystemSchedules.AddSchedule(schedule, addAfter);
    }

    /// <summary>
    /// Removes all entities and their components from the world, keeping only the archetype definitions.
    /// This is useful for resetting the world state without affecting system schedules or resources.
    /// </summary>
    public void RemoveAllEntities()
    {
        World.RemoveAllEntities();
    }

    /// <summary>
    /// Removes all system schedules from the application.
    /// </summary>
    public void ClearSchedules()
    {
        World.SystemSchedules.ClearSchedules();
    }

    /// <summary>
    /// Creates a new ThreadPool with the specified thread count.
    /// The created pool will be automatically disposed when the App is disposed.
    /// </summary>
    /// <param name="threadCount">The number of threads in the pool.</param>
    /// <returns>A new ThreadPool instance.</returns>
    public ThreadPool CreateThreadPool(int threadCount)
    {
        var pool = new ThreadPool(threadCount);
        disposables.Add(pool);

        return pool;
    }

    /// <summary>
    /// Retrieves a system schedule by name.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <returns>The schedule if found; otherwise, null.</returns>
    public ISchedule? GetSchedule(string name)
    {
        return World.SystemSchedules.GetSchedule(name);
    }

    /// <summary>
    /// Retrieves a system schedule by name and casts it to the specified type.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <typeparam name="T">The schedule type, must be a reference type implementing ISchedule.</typeparam>
    /// <returns>The schedule if found and of the correct type; otherwise, null.</returns>
    public T? GetSchedule<T>(string name) where T : class, ISchedule
    {
        return World.SystemSchedules.GetSchedule<T>(name);
    }

    /// <summary>
    /// Installs a plugin into the application. Installing the same plugin type multiple times has no effect.
    /// The installed plugin is automatically registered as a resource for dependency injection.
    /// </summary>
    /// <param name="plugin">The plugin instance to install.</param>
    /// <typeparam name="T">The plugin type, must be a reference type implementing IPlugin.</typeparam>
    /// <returns>The installed plugin instance.</returns>
    public T InstallPlugin<T>(T plugin) where T : class, IPlugin
    {
        if (ResourcePool.HasResource<T>())
        {
            return plugin;
        }

        plugin.Install(this);
        ResourcePool.AddResource(plugin);

        return plugin;
    }

    /// <summary>
    /// Creates and installs a new plugin instance with the default constructor.
    /// Installing the same plugin type multiple times has no effect.
    /// </summary>
    /// <typeparam name="T">The plugin type, must have a default constructor.</typeparam>
    /// <returns>The newly created and installed plugin instance.</returns>
    public T InstallPlugin<T>() where T : class, IPlugin, new()
    {
        return InstallPlugin(new T());
    }

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
        World.Update(scheduleEnd);
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }
    }

#endregion
}
