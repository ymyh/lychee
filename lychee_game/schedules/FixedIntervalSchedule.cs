using System.Diagnostics;
using lychee;

namespace lychee_game.schedules;

/// <summary>
/// Executes at a fixed time interval. It will try to catch up if the interval is missed.
/// </summary>
/// <param name="app">The application.</param>
/// <param name="commitPoint">The commit point.</param>
/// <param name="fixedUpdateInterval">The interval in milliseconds.</param>
/// <param name="catchUpCount">The catch-up attempt count in each execute.</param>
public sealed class FixedIntervalSchedule(
    App app,
    string name,
    BasicSchedule.CommitPointEnum commitPoint = BasicSchedule.CommitPointEnum.Synchronization,
    int fixedUpdateInterval = 20,
    int catchUpCount = 5)
    : BasicSchedule(app, name, commitPoint)
{
    private long accErr = fixedUpdateInterval;

    private readonly Stopwatch stopwatch = new();

    public int FixedUpdateInterval { get; set; } = fixedUpdateInterval;

    public int CatchUpCount { get; set; } = catchUpCount;

    public override void Execute()
    {
        var now = stopwatch.ElapsedMilliseconds + accErr;

        if (now >= FixedUpdateInterval)
        {
            now -= FixedUpdateInterval;
            accErr = now;
            stopwatch.Restart();

            ExecuteImpl();
        }

        var i = 0;
        while (now >= FixedUpdateInterval && (i < CatchUpCount || CatchUpCount < 0))
        {
            now -= FixedUpdateInterval;
            accErr = now;
            i++;

            ExecuteImpl();
        }
    }
}
