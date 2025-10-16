namespace lychee.interfaces;

/// <summary>
/// <b> Warning: Don't implement this interface manually unless you know what are you doing. </b> <br/>
/// Interface for system, auto implemented by <see cref="lychee.attributes.AutoImplSystem"/>.
/// </summary>
public interface ISystem
{
    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing.<br/>
    /// Initialize the system, called when initializing.
    /// </summary>
    public void InitializeAG(App app);

    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing.<br/>
    /// Configure the system, called when archetype changed.
    /// </summary>
    public void ConfigureAG(App app, SystemDescriptor descriptor);

    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing.<br/>
    /// Execute the system from <see cref="ISchedule"/>.
    /// </summary>
    public EntityCommander ExecuteAG();
}