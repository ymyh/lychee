using lychee_game.schedules;
using lychee;
using lychee.interfaces;

namespace lychee_game;

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
///             <see cref="StartUp"/>
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
///             <see cref="Update"/>
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
/// </summary>
/// <param name="desc">The plugin descriptor.</param>
public sealed class BasicGamePlugin(DefaultPluginDescriptor desc) : IPlugin
{
    public FireOnceSchedule StartUp = null!;

    public FixedIntervalSchedule FixedUpdate = null!;

    public DefaultSchedule First = null!;

    public DefaultSchedule Update = null!;

    public DefaultSchedule PostUpdate = null!;

    public DefaultSchedule Render = null!;

    public DefaultSchedule Last = null!;

    public BasicGamePlugin() : this(new())
    {
    }

    public void Install(App app)
    {
        StartUp = new(app, nameof(StartUp));
        app.AddSchedule(StartUp);

        First = new(app, nameof(First));
        app.AddSchedule(First);

        FixedUpdate = new(app, nameof(FixedUpdate), BasicSchedule.CommitPointEnum.Synchronization, desc.FixedUpdateInterval, desc.FixedUpdateCatchUpCount);
        app.AddSchedule(FixedUpdate);

        Update = new(app, nameof(Update));
        app.AddSchedule(Update);

        PostUpdate = new(app, nameof(PostUpdate));
        app.AddSchedule(PostUpdate);

        Render = new(app, nameof(Render));
        app.AddSchedule(Render);

        Last = new(app, nameof(Last));
        app.AddSchedule(Last);
    }
}
