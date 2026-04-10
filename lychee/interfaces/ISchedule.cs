namespace lychee.interfaces;

/// <summary>
/// Defines a system schedule that can be executed by the World.
/// </summary>
public interface ISchedule
{
    /// <summary>
    /// Executes the schedule.
    /// </summary>
    public void Execute();

    /// <summary>
    /// The schedule name.
    /// </summary>
    public string Name { get; }
}
