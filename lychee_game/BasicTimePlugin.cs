using lychee_game.schedules;
using lychee;
using lychee.attributes;
using lychee.interfaces;

namespace lychee_game;

[AutoImplSystem]
public partial class InitBasicTimePluginSystem
{
    private static void Execute([Resource] Time time)
    {
        time.Start();
    }
}

[AutoImplSystem]
public partial class UpdateTimeResourceSystem
{
    private static void Execute([Resource] Time time)
    {
        time.Update();
    }
}

/// <summary>
/// Provide basic time support. Requires <see cref="BasicGamePlugin"/>. <br/>
/// Add a <see cref="Time"/> resource to the app. <br/>
/// Add a <see cref="InitBasicTimePluginSystem"/> to the <see cref="BasicGamePlugin.StartUp"/> schedule. <br/>
/// Add a <see cref="UpdateTimeResourceSystem"/> to the <see cref="BasicGamePlugin.Update"/> schedule.
/// </summary>
public sealed class BasicTimePlugin : IPlugin
{
    public readonly InitBasicTimePluginSystem InitBasicTimePluginSystem = new();

    public readonly UpdateTimeResourceSystem UpdateTimeResourceSystem = new();

    public void Install(App app)
    {
        if (!app.CheckInstalledPlugin<BasicGamePlugin>())
        {
            throw new PluginRequirementException("BasicGamePlugin is required");
        }

        app.AddResource(new Time());

        var startUp = (FireOnceSchedule)app.GetSchedule("StartUp")!;
        startUp.AddSystem(InitBasicTimePluginSystem);

        var update = (DefaultSchedule)app.GetSchedule("Update")!;
        update.AddSystem(UpdateTimeResourceSystem);
    }
}