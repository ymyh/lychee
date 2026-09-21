using Microsoft.Extensions.Logging;

namespace lychee;

/// <summary>
/// Source-generated log messages emitted by schedules.
/// <see cref="LoggerMessageAttribute"/> moves both the level check and the message template out of the
/// call site, so a disabled log call allocates nothing on the scheduling hot path.
/// </summary>
internal static partial class ScheduleLog
{
#region Warning Messages

    /// <summary>
    /// Logged when two systems sharing an execution group access the same parameter without an ordering
    /// constraint between them. They are legal to run, but their relative order is not deterministic.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="schedule">The name of the schedule that owns both systems.</param>
    /// <param name="group">The execution group both systems ended up in.</param>
    /// <param name="systemA">The name of the first system.</param>
    /// <param name="systemB">The name of the second system.</param>
    /// <param name="parameter">The type name of the conflicting parameter.</param>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Schedule '{Schedule}' has ambiguous systems in group {Group}: {SystemA} and {SystemB} both access {Parameter}")]
    public static partial void AmbiguousSystems(ILogger logger, string schedule, int group, string systemA, string systemB, string parameter);

    /// <summary>
    /// Logged when expanding set constraints into system-to-system edges crosses the observation threshold.
    /// A large edge count points at two sets with many members being ordered against each other, which is the
    /// case that virtual set nodes handle in linear instead of quadratic space.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="schedule">The name of the schedule being built.</param>
    /// <param name="edgeCount">The number of edges produced by set expansion.</param>
    /// <param name="threshold">The observed threshold that was crossed.</param>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Schedule '{Schedule}' expanded set constraints into {EdgeCount} system edges, exceeding the threshold of {Threshold}; consider virtual set nodes")]
    public static partial void SetExpansionLarge(ILogger logger, string schedule, int edgeCount, int threshold);

#endregion
}
