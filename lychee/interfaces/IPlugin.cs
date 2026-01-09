namespace lychee.interfaces;

/// <summary>
/// Represents a plugin that can be installed into an application.
/// Usually you shouldn't access any fields or properties of the plugin before installing it into an application.
/// </summary>
public interface IPlugin
{
    void Install(App app);
}
