namespace lychee_game;

public sealed class PluginRequirementException(string pluginName) : Exception($"{pluginName} is required");
