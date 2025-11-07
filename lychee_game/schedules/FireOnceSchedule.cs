using lychee;

namespace lychee_game.schedules;

/// <summary>
/// Execute once then idle until <see cref="Reset"/> is called.
/// </summary>
/// <param name="app">The application.</param>
/// <param name="commitPoint">The commit point.</param>
public sealed class FireOnceSchedule(App app, string name, BasicSchedule.CommitPointEnum commitPoint = BasicSchedule.CommitPointEnum.ScheduleEnd)
    : BasicSchedule(app, name, commitPoint)
{
    private bool fired;

    public override void Execute()
    {
        if (!fired)
        {
            fired = true;
            ExecuteImpl();
        }
    }

    /// <summary>
    /// Reset the fired status.
    /// </summary>
    public void Reset()
    {
        fired = false;
    }
}