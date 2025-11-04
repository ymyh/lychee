using lychee_game.schedules;
using lychee;
using lychee.attributes;
using lychee.interfaces;

namespace lychee_game;

[AutoImplSystem]
public partial class InitBasicGamePluginSystem
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

public sealed class DefaultPluginDescriptor
{
    public int FixedUpdateInterval = 20;

    public int FixedUpdateCatchUpCount = 5;
}

/// <summary>
/// A plugin that provides basic game features. <br/>
/// It provides the following schedules:
/// <list type="bullet">
///     <item>
///         <description>
///             <see cref="StartUp"/> (Contains a <see cref="InitBasicGamePluginSystem"/>)
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="First"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="FixedUpdate"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="PreUpdate"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="Update"/> (Contains a <see cref="UpdateTimeResourceSystem"/>)
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="PostUpdate"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="Render"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="Last"/>
///         </description>
///     </item>
/// </list>
/// It also provides the following resources:
/// <list type="bullet">
///     <item>
///         <description>
///             <see cref="Time"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="GameControl"/>
///         </description>
///     </item>
/// </list>
/// </summary>
/// <param name="desc">The plugin descriptor.</param>
public sealed class BasicGamePlugin(DefaultPluginDescriptor desc) : IPlugin
{
    public FireOnceSchedule StartUp = null!;

    public FixedIntervalSchedule FixedUpdate = null!;

    public DefaultSchedule First = null!;

    public DefaultSchedule PreUpdate = null!;

    public DefaultSchedule Update = null!;

    public DefaultSchedule PostUpdate = null!;

    public DefaultSchedule Render = null!;

    public DefaultSchedule Last = null!;

    public BasicGamePlugin() : this(new())
    {
    }

    public void Install(App app)
    {
        app.AddResource(new Time());
        app.AddResource(new GameControl());

        {
            StartUp = new(app);
            app.AddSchedule(StartUp);

            StartUp.AddSystem(new InitBasicGamePluginSystem());
        }

        {
            First = new(app);
            app.AddSchedule(First);
        }

        {
            FixedUpdate = new(app, BasicSchedule.CommitPointEnum.Synchronization, desc.FixedUpdateInterval, desc.FixedUpdateCatchUpCount);
            app.AddSchedule(FixedUpdate);
        }

        {
            PreUpdate = new(app);
            app.AddSchedule(PreUpdate);
        }

        {
            Update = new(app);
            app.AddSchedule(Update);

            Update.AddSystem(new UpdateTimeResourceSystem());
        }

        {
            PostUpdate = new(app);
            app.AddSchedule(PostUpdate);
        }

        {
            Render = new(app);
            app.AddSchedule(Render);
        }

        {
            Last = new(app);
            app.AddSchedule(Last);
        }
    }
}
