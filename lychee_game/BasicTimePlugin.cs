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

public sealed class BasicTimePlugin : IPlugin
{
    public void Install(App app)
    {
        if (!app.CheckInstalledPlugin<BasicGamePlugin>())
        {
            throw new PluginRequirementException("BasicGamePlugin is required");
        }

        app.AddResource(new Time());

        var startUp = (FireOnceSchedule)app.GetSchedule("StartUp")!;
        startUp.AddSystem(new InitBasicTimePluginSystem());

        var update = (DefaultSchedule)app.GetSchedule("Update")!;
        update.AddSystem(new UpdateTimeResourceSystem());
    }
}
