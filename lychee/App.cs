using lychee.interfaces;

namespace lychee;

public sealed class App : IDisposable
{
#region Fields

    public delegate void RunnerDelegate();

    public readonly TypeRegistry TypeRegistry = new();

    public readonly ResourcePool ResourcePool;

    public readonly World World;
    public RunnerDelegate Runner { get; set; }

    private bool shouldExit;

#endregion

#region Constructors

    public App()
    {
        World = new(TypeRegistry);
        ResourcePool = new(TypeRegistry);

        Runner = () =>
        {
            while (!shouldExit)
            {
                World.Update();
            }
        };
    }

#endregion

#region public methods

    public void Exit()
    {
        shouldExit = true;
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
    }

#endregion

#region private methods

#endregion
}
