namespace lychee.interfaces;

/// <summary>
/// Represents a plugin that can be installed into an application.
/// Usually you shouldn't access any fields or properties of the plugin before installed.
/// </summary>
public interface IPlugin
{
    void Install(App app);
}
