using System.Diagnostics;
using lychee.interfaces;
using ThreadPool = lychee.threading.ThreadPool;

namespace lychee;

/// <summary>
/// The control of the application and some basic properties.
/// </summary>
public class AppControl
{
    public readonly Stopwatch Stopwatch = new();

    public int UpdateCount;

    public bool ShouldExit;
}

/// <summary>
/// The ECS application. <br/>
/// The application will contain a <see cref="AppControl"/> reesource and a default <see cref="Runner"/> when created.
/// </summary>
public sealed class App : IDisposable
{
#region Fields

    public readonly TypeRegistrar TypeRegistrar = new();

    public readonly ResourcePool ResourcePool;

    public readonly World World;

    internal readonly ThreadPool ThreadPool = new(4);

    /// <summary>
    /// Controls how the application should run. <br/>
    /// The default runner will update the world until <see cref="AppControl.ShouldExit"/> is set to true.
    /// </summary>
    public Action<ResourcePool> Runner { get; set; }

    public SystemSchedules SystemSchedules { get; }

#endregion

#region Constructors & Destructors

    public App()
    {
        World = new(TypeRegistrar);
        ResourcePool = new(TypeRegistrar);
        ResourcePool.AddResource(new AppControl());
        SystemSchedules = World.SystemSchedules;

        Runner = pool =>
        {
            var control = pool.GetResource<AppControl>();
            control.Stopwatch.Start();

            while (!control.ShouldExit)
            {
                World.Update();
                control.UpdateCount++;
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
        SystemSchedules.AddSchedule(schedule);
    }

    public void AddSchedule(ISchedule schedule, ISchedule addAfter)
    {
        SystemSchedules.AddSchedule(schedule, addAfter);
    }

    public void ClearSchedules()
    {
        SystemSchedules.ClearSchedules();
    }

    public void InstallPlugin(IPlugin plugin)
    {
        plugin.Install(this);
    }

    public void Run()
    {
        Runner(ResourcePool);
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
