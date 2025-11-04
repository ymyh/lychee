using lychee;

namespace lychee_game.schedules;

/// <summary>
/// Only execute once.
/// </summary>
/// <param name="app">The application.</param>
/// <param name="commitPoint">The commit point.</param>
public sealed class FireOnceSchedule(App app, BasicSchedule.CommitPointEnum commitPoint = BasicSchedule.CommitPointEnum.ScheduleEnd)
    : BasicSchedule(app, commitPoint)
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
}
