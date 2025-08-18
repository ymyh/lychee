using lychee.interfaces;

namespace lychee;

public sealed class App
{
#region Constructors

    public App()
    {
        World = new World(TypeRegistry, ResourcePool);
        Runner = () =>
        {
            while (!shouldExit)
            {
                World.Update();
            }
        };
    }

#endregion

#region Fields

    public delegate void RunnerDelegate();

    public readonly TypeRegistry TypeRegistry = new();

    public readonly ResourcePool ResourcePool = new();

    public readonly World World;

    public RunnerDelegate Runner { get; set; }

    private bool shouldExit;

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

#region private methods

#endregion
}
