using System.Diagnostics;

namespace lychee_game;

public sealed class Time
{
    private readonly Stopwatch stopwatch = new();

    public TimeSpan ElapsedTime => stopwatch.Elapsed;

    public TimeSpan ElapsedTimeFromLastUpdate { get; }

    internal void Start()
    {
        stopwatch.Start();
    }
}
