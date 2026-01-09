using lychee;

namespace lychee_game.schedules;

/// <summary>
/// Execute when predicate returns true.
/// </summary>
/// <param name="app">The application.</param>
/// <param name="predicate">The predicate.</param>
/// <param name="commitPoint">The commit point.</param>
public sealed class ConditionalSchedule(
    App app,
    Func<bool> predicate,
    BasicSchedule.ExecutionModeEnum executionMode = BasicSchedule.ExecutionModeEnum.SingleThread,
    BasicSchedule.CommitPointEnum commitPoint = BasicSchedule.CommitPointEnum.Synchronization)
    : BasicSchedule(app, executionMode, commitPoint)
{
    public override void Execute()
    {
        if (predicate())
        {
            DoExecute();
        }
    }
}
