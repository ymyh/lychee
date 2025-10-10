using lychee.interfaces;

namespace lychee;

public sealed class App : IDisposable
{
#region Fields

    public readonly TypeRegistry TypeRegistry = new();

    public readonly ResourcePool ResourcePool;

    public readonly World World;

    public Action Runner;

    private bool shouldExit;

#endregion

#region Constructors & Destructors

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

    ~App()
    {
        Dispose();
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
        GC.SuppressFinalize(this);
    }

#endregion
}
