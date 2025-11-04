using System.Diagnostics;

namespace lychee_game;

public sealed class Time
{
    private readonly Stopwatch stopwatch = new();

    private readonly Stopwatch stopwatchUpdate = new();

    public TimeSpan ElapsedTime => stopwatch.Elapsed;

    public TimeSpan ElapsedTimeFromLastUpdate { get; internal set; }

    public float TimeScale = 1.0f;

    internal void Start()
    {
        stopwatch.Start();
    }

    internal void Update()
    {
        ElapsedTimeFromLastUpdate = stopwatchUpdate.Elapsed;
        stopwatchUpdate.Restart();
    }
}
