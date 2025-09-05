namespace lychee.interfaces;

/// <summary>
/// Interface for system
/// </summary>
public interface ISystem
{
    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing.<br/>
    /// Auto configure the system, called when initializing or Archetype change.
    /// </summary>
    public void ConfigureAG();

    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing.<br/>
    /// Execute the system from <see cref="ISchedule"/>.
    /// </summary>
    public void ExecuteAG();
}