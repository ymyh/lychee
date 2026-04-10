namespace lychee.interfaces;

/// <summary>
/// Marker interface for components in the ECS framework. Components must be unmanaged.
/// </summary>
public interface IComponent;

/// <summary>
/// Marker interface for component bundles, which are groups of components that can be added or removed together from entities.
/// </summary>
public interface IComponentBundle;
