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
/// </summary>
/// <param name="desc">The plugin descriptor.</param>
public sealed class BasicGamePlugin(DefaultPluginDescriptor desc) : IPlugin
{
    public FireOnceSchedule StartUp { get; private set; } = null!;

    public FixedIntervalSchedule FixedUpdate { get; private set; } = null!;

    public DefaultSchedule First { get; private set; } = null!;

    public DefaultSchedule Update { get; private set; } = null!;

    public DefaultSchedule PostUpdate { get; private set; } = null!;

    public DefaultSchedule Render { get; private set; } = null!;

    public DefaultSchedule Last { get; private set; } = null!;

    public BasicGamePlugin() : this(new())
    {
    }

    public void Install(App app)
    {
        StartUp = new(app);
        app.AddSchedule(StartUp, nameof(StartUp));

        First = new(app);
        app.AddSchedule(First, nameof(First));

        FixedUpdate = new(app)
        {
            FixedUpdateInterval = desc.FixedUpdateInterval,
            CatchUpCount = desc.FixedUpdateCatchUpCount
        };
        app.AddSchedule(FixedUpdate, nameof(FixedUpdate));

        Update = new(app);
        app.AddSchedule(Update, nameof(Update));

        PostUpdate = new(app);
        app.AddSchedule(PostUpdate, nameof(PostUpdate));

        Render = new(app);
        app.AddSchedule(Render, nameof(Render));

        Last = new(app);
        app.AddSchedule(Last, nameof(Last));
    }
}
