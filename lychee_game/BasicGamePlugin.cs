using lychee_game.schedules;
using lychee;
using lychee.interfaces;

namespace lychee_game;

public sealed class DefaultPluginDescriptor
{
    /// <summary>
    /// FixedUpdate schedule execution interval, in millisecond, default is 20.
    /// </summary>
    public int FixedUpdateInterval { get; set; } = 20;

    /// <summary>
    /// FixedUpdate schedule maximum catch up attempt count, default is 5.
    /// </summary>
    public int FixedUpdateCatchUpCount { get; set; } = 5;
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
///             <see cref="RenderTransparency"/>
///         </description>
///     </item>
///     <item>
///         <description>
///             <see cref="RenderUI"/>
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
    public FireOnceSchedule StartUp { get; private set; } = null!;

    public DefaultSchedule First { get; private set; } = null!;

    public FixedIntervalSchedule FixedUpdate { get; private set; } = null!;

    public DefaultSchedule Update { get; private set; } = null!;

    public DefaultSchedule PostUpdate { get; private set; } = null!;

    public DefaultSchedule Render { get; private set; } = null!;

    public DefaultSchedule RenderTransparency { get; private set; } = null!;

    public DefaultSchedule RenderUI { get; private set; } = null!;

    public DefaultSchedule Last { get; private set; } = null!;

    public BasicGamePlugin() : this(new())
    {
    }

    public void Install(App app)
    {
        StartUp = new(app, nameof(StartUp));
        app.AddSchedule(StartUp);

        First = new(app, nameof(First));
        app.AddSchedule(First);

        FixedUpdate = new(app, nameof(FixedUpdate))
        {
            FixedUpdateInterval = desc.FixedUpdateInterval,
            CatchUpCount = desc.FixedUpdateCatchUpCount
        };
        app.AddSchedule(FixedUpdate);

        Update = new(app, nameof(Update));
        app.AddSchedule(Update);

        PostUpdate = new(app, nameof(PostUpdate));
        app.AddSchedule(PostUpdate);

        Render = new(app, nameof(Render));
        app.AddSchedule(Render);

        RenderTransparency = new(app, nameof(RenderTransparency));
        app.AddSchedule(RenderTransparency);

        RenderUI = new(app, nameof(RenderUI));
        app.AddSchedule(RenderUI);

        Last = new(app, nameof(Last));
        app.AddSchedule(Last);
    }
}
