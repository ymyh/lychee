using System.Diagnostics;
using lychee;
using lychee.attributes;
using lychee.interfaces;

namespace lychee_game;

public sealed class DefaultPluginDescriptor
{
    public int FixedUpdateInterval = 20;

    public int FixedUpdateCatchUpCount = 5;
}

[AutoImplSystem]
internal partial class InitTimeSystem
{
    private static void Execute([Resource] Time time)
    {
        time.Start();
    }
}

/// <summary>
///
/// </summary>
/// <param name="desc"></param>
public sealed class BasicGamePlugin(DefaultPluginDescriptor desc) : IPlugin
{
    public SimpleSchedule StartUp = null!;

    public SimpleSchedule FixedUpdate = null!;

    public SimpleSchedule First = null!;

    public SimpleSchedule PreUpdate = null!;

    public SimpleSchedule Update = null!;

    public SimpleSchedule PostUpdate = null!;

    public SimpleSchedule Render = null!;

    public SimpleSchedule Last = null!;

    public BasicGamePlugin() : this(new())
    {
    }

    public void Install(App app)
    {
        app.AddResource(new Time());
        app.AddResource(new GameControl());

        {
            var firstTime = true;
            StartUp = new(app, () =>
            {
                if (firstTime)
                {
                    firstTime = false;
                    return ExecuteStrategy.Exec;
                }

                return ExecuteStrategy.NoExec;
            });

            app.AddSchedule(StartUp);

            StartUp.AddSystem(new InitTimeSystem());
        }

        {
            First = new(app, () => ExecuteStrategy.Exec);
            app.AddSchedule(First);
        }

        {
            var firstTime = true;
            var stopwatch = new Stopwatch();
            var accErr = 0L;

            FixedUpdate = new(app, () =>
            {
                if (firstTime)
                {
                    stopwatch.Start();
                    firstTime = false;
                    return ExecuteStrategy.Exec;
                }

                var now = stopwatch.ElapsedMilliseconds + accErr;
                var exec = now >= desc.FixedUpdateInterval;

                if (exec)
                {
                    stopwatch.Restart();
                    now -= desc.FixedUpdateInterval;
                    accErr = now;

                    return now >= desc.FixedUpdateInterval ? ExecuteStrategy.ExecAgain : ExecuteStrategy.Exec;
                }

                return ExecuteStrategy.NoExec;
            });

            app.AddSchedule(FixedUpdate);
        }

        {
            PreUpdate = new(app, () => ExecuteStrategy.Exec);
            app.AddSchedule(PreUpdate);
        }

        {
            Update = new(app, () => ExecuteStrategy.Exec);
            app.AddSchedule(Update);
        }

        {
            PostUpdate = new(app, () => ExecuteStrategy.Exec);
            app.AddSchedule(PostUpdate);
        }

        {
            // Render = new(app, () =>
            // {
            //     var gameControl = app.ResourcePool.GetResource<GameControl>();
            //     SpinWait.SpinUntil(() => gameControl.Rendering);
            //
            //     return ExecuteStrategy.Exec;
            // });
            // app.AddSchedule(Render);
        }

        {
            Last = new(app, () => ExecuteStrategy.Exec);
            app.AddSchedule(Last);
        }
    }
}
