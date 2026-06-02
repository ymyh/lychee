namespace lychee.interfaces;

/// <summary>
/// Contains metadata about a component type.
/// </summary>
/// <param name="Size">The size of the component in bytes.</param>
public readonly struct ComponentMeta(int size)
{
    public readonly int Size = size;
}

/// <summary>
/// Interface for components in the ECS framework. Components must be unmanaged.
/// </summary>
public interface IComponent
{
    /// <summary>
    /// Gets the metadata for this component.
    /// </summary>
    ComponentMeta GetComponentMeta();
}

/// <summary>
/// Marker interface for component bundles, which are groups of components that can be added or removed together from entities.
/// </summary>
public interface IComponentBundle;
