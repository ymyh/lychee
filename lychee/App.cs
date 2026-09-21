using lychee.interfaces;
using lychee.systems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>
    /// The logger factory the framework uses to report diagnostics such as ambiguous scheduling or oversized
    /// set expansion. It defaults to <see cref="NullLoggerFactory.Instance"/>, which discards every message at
    /// zero cost, so logging stays opt-in. Assign a real factory to observe the diagnostics.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;
}

/// <summary>
/// The ECS application.
/// </summary>
public sealed class App : IDisposable
{
#region Public Properties

    public TypeRegistrar TypeRegistrar { get; } = new();

    public ResourcePool ResourcePool { get; }

    public World World { get; }

    public SystemSchedules SystemSchedules { get; }

#region Internal Fields

    internal readonly ThreadPool ThreadPool;

    internal readonly SystemSets SystemSets;

#endregion

#region Internal Properties

    internal ILoggerFactory LoggerFactory { get; }

#endregion

#endregion

#region Private Fields

    private readonly List<IDisposable> disposables = [];

    private bool disposed;

#endregion

#region Constructors

    /// <summary>
    /// Creates an App with specified thread pool setting.
    /// </summary>
    /// <param name="descriptor">The app descriptor.</param>
    public App(AppDescriptor descriptor)
    {
        // Assigned first: the default schedule built below creates its logger from this factory.
        LoggerFactory = descriptor.LoggerFactory;

        World = new(TypeRegistrar, descriptor.ChunkSizeHint);
        SystemSchedules = new(this);
        ResourcePool = new(TypeRegistrar);
        ThreadPool = new(descriptor.ThreadCount, descriptor.ThreadPoolQueueCapacity);
        SystemSets = new(TypeRegistrar, ResourcePool);

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

#region Public Methods

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
    /// Checks whether a resource of the specified type exists in the pool.
    /// </summary>
    /// <param name="typeName">The type name of the resource to check.</param>
    /// <returns>True if the resource exists; otherwise, false.</returns>
    public bool HasResource(string typeName)
    {
        return ResourcePool.HasResource(Type.GetType(typeName) ?? throw new ArgumentException($"Type {typeName} not found"));
    }

    /// <summary>
    /// Adds a system schedule with a unique name for execution ordering.
    /// </summary>
    /// <param name="schedule">The schedule instance to add.</param>
    public void AddSchedule(ISchedule schedule)
    {
        SystemSchedules.AddSchedule(schedule);
    }

    /// <summary>
    /// Adds a system schedule, inserting it after an existing schedule in the execution order.
    /// </summary>
    /// <param name="schedule">The schedule instance to add.</param>
    /// <param name="addAfter">The name of the schedule after which this schedule should execute.</param>
    /// <exception cref="ArgumentException">Thrown when the schedule name already exists or the addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, string addAfter)
    {
        SystemSchedules.AddSchedule(schedule, addAfter);
    }

    /// <summary>
    /// Adds a system schedule, inserting it after an existing schedule in the execution order.
    /// </summary>
    /// <param name="schedule">The schedule instance to add.</param>
    /// <param name="addAfter">The schedule after which this schedule should execute.</param>
    /// <exception cref="ArgumentException">Thrown when the schedule name already exists or the addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, ISchedule addAfter)
    {
        AddSchedule(schedule, addAfter.Name);
    }

    /// <summary>
    /// Registers a new state and adds a cleanup system to the "Last" schedule.
    /// Entities with a <see cref="lychee.components.StateScoped{T}"/> matching the previous state
    /// are automatically despawned when the state changes.
    /// </summary>
    /// <param name="initial">The initial state value.</param>
    /// <typeparam name="T">The state type, typically an enum.</typeparam>
    /// <returns>The state resource for use in systems.</returns>
    public State<T> AddState<T>(T initial) where T : unmanaged, Enum
    {
        var state = new State<T>(initial);
        ResourcePool.AddResource(state);
        SystemSchedules.Last.AddSystem<StateCleanupSystem<T>>();

        return state;
    }

    /// <summary>
    /// Registers an enum type as a set dimension, so its values can be used with the other set configuration
    /// methods. Registering the same type again has no effect.
    /// </summary>
    /// <typeparam name="T">The enum type whose values identify sets.</typeparam>
    public void AddSystemSet<T>() where T : Enum
    {
        SystemSets.AddSystemSet<T>();
    }

    /// <summary>
    /// Constrains every system in one set to run before (or after) every system in another set.
    /// Declaring the same constraint again has no effect.
    /// </summary>
    /// <typeparam name="TS1">The enum type of the first set.</typeparam>
    /// <typeparam name="TS2">The enum type of the second set.</typeparam>
    /// <param name="s1">The first set.</param>
    /// <param name="order">Whether <paramref name="s1"/> runs before or after <paramref name="s2"/>.</param>
    /// <param name="s2">The second set.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either set type has not been registered, or when the constraint would create a cycle.
    /// </exception>
    public void ConfigureSetOrder<TS1, TS2>(TS1 s1, Order order, TS2 s2) where TS1 : Enum where TS2 : Enum
    {
        SystemSets.ConfigureSetOrder(s1, order, s2);
    }

    /// <summary>
    /// Attaches a predicate to a set. Systems belonging to the set (or to any of its children) only run while
    /// the predicate returns true.
    /// </summary>
    /// <typeparam name="T">The enum type of the set.</typeparam>
    /// <param name="set">The set to attach the predicate to.</param>
    /// <param name="predicate">The predicate, evaluated once per execution against the resource pool.</param>
    /// <exception cref="InvalidOperationException">Thrown when the set type has not been registered.</exception>
    public void ConfigureSetPredicate<T>(T set, Func<ResourcePool, bool> predicate) where T : Enum
    {
        SystemSets.ConfigureSetPredicate(set, predicate);
    }

    /// <summary>
    /// Nests one set inside another: the child inherits the parent's predicate, and constraints applied to the
    /// parent also cover the child. A set has at most one parent.
    /// </summary>
    /// <typeparam name="TS1">The enum type of the parent set.</typeparam>
    /// <typeparam name="TS2">The enum type of the child set.</typeparam>
    /// <param name="parent">The set that contains <paramref name="child"/>.</param>
    /// <param name="child">The set contained in <paramref name="parent"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either set type has not been registered, or when nesting would create a cycle.
    /// </exception>
    public void ConfigureSetInSet<TS1, TS2>(TS1 parent, TS2 child) where TS1 : Enum where TS2 : Enum
    {
        SystemSets.ConfigureSetInSet(parent, child);
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
        SystemSchedules.ClearSchedules();
    }

    /// <summary>
    /// Retrieves a system schedule by name.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <returns>The schedule if found; otherwise, null.</returns>
    public ISchedule? GetSchedule(string name)
    {
        return SystemSchedules.GetSchedule(name);
    }

    /// <summary>
    /// Retrieves a system schedule by name and casts it to the specified type.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <typeparam name="T">The schedule type, must be a reference type implementing ISchedule.</typeparam>
    /// <returns>The schedule if found and of the correct type; otherwise, null.</returns>
    public T? GetSchedule<T>(string name) where T : class, ISchedule
    {
        return SystemSchedules.GetSchedule<T>(name);
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
        if (SystemSchedules.Execute(scheduleEnd))
        {
           World.SwapEvents();
        }
    }

#endregion

#region IDisposable Implementation

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
