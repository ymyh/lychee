using lychee.interfaces;

namespace lychee;

public sealed class App
{
    public App()
    {
        World = new World(TypeRegistry);
        Runner = () =>
        {
            while (!shouldExit)
            {
                World.Update();
            }
        };
    }

#region Fields

    public delegate void RunnerDelegate();

    public TypeRegistry TypeRegistry { get; } = new();

    public World World { get; }

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
