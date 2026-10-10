using lychee.interfaces;

namespace lychee;

/// <summary>
/// A lightweight handle to an entity in the ECS framework.
/// It carries only the entity reference and the <see cref="Commands"/> it was issued from; its location is
/// resolved from the world on demand, so structural changes made through it only take effect at a commit point.
/// </summary>
public readonly struct Entity
{
#region Public Properties

    /// <summary>
    /// Gets the entity reference containing the entity ID and generation.
    /// </summary>
    public EntityRef Ref { get; }

    /// <summary>
    /// Gets the unique identifier of this entity.
    /// </summary>
    public int ID => Ref.ID;

#endregion

#region Internal Properties

    internal Commands Commands { get; }

#endregion

#region Constructors

    /// <summary>
    /// Initializes a new handle for an entity owned by the given commands.
    /// </summary>
    /// <param name="commands">The commands instance for deferred operations.</param>
    /// <param name="entityRef">The entity reference containing ID and generation.</param>
    internal Entity(Commands commands, EntityRef entityRef)
    {
        Commands = commands;
        Ref = entityRef;
    }

#endregion

#region Public Methods

    /// <summary>
    /// Records a copy of this entity with identical component data.
    /// The copy is applied at the next commit point.
    /// </summary>
    /// <param name="mode">Whether to copy the entity alone or its whole linked subtree.</param>
    /// <returns>The newly created copy entity handle.</returns>
    public Entity Copy(CloneMode mode = CloneMode.Shallow)
    {
        return Commands.CopyEntity(in this, mode);
    }

    /// <summary>
    /// Records removal of this entity. The entity is fully removed at the next commit point.
    /// </summary>
    public void Despawn()
    {
        Commands.RemoveEntity(in this);
    }

    /// <summary>
    /// Records adding a component to this entity. The entity moves to a new archetype at the next commit point.
    /// Adding a component type the entity already has is rejected by the archetype; use
    /// <see cref="ReplaceComponent{T}(in T)"/> to overwrite an existing component value instead.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="component">The component value to add.</param>
    public void AddComponent<T>(in T component) where T : unmanaged, IComponent
    {
        Commands.AddComponent(this, in component);
    }

    /// <summary>
    /// Records adding multiple components as a bundle to this entity.
    /// Adding a component type the entity already has is rejected by the archetype; this does not overwrite it.
    /// </summary>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <param name="components">The component bundle containing the components to add.</param>
    public void AddComponents<T>(in T components) where T : unmanaged, IComponentBundle
    {
        Commands.AddComponents(this, in components);
    }

    /// <summary>
    /// Records replacing the value of a component this entity already has, without moving archetypes.
    /// Does nothing when the entity is invalid, removed, or does not have the component.
    /// OnReplace hooks observe the previous and the new value.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="component">The new component value.</param>
    public void ReplaceComponent<T>(in T component) where T : unmanaged, IComponent
    {
        Commands.ReplaceComponent(this, in component);
    }

    /// <summary>
    /// Records removing a component from this entity. The entity moves to a new archetype at the next commit point.
    /// </summary>
    /// <typeparam name="T">The component type to remove, must be unmanaged and implement IComponent.</typeparam>
    public void RemoveComponent<T>() where T : unmanaged, IComponent
    {
        Commands.RemoveComponent<T>(this);
    }

    /// <summary>
    /// Records removing all components defined in a component bundle from this entity.
    /// </summary>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    public void RemoveComponents<T>() where T : unmanaged, IComponentBundle
    {
        Commands.RemoveComponents<T>(this);
    }

    /// <summary>
    /// Records removing all components defined in a tuple from this entity.
    /// </summary>
    /// <typeparam name="T">The tuple type containing the component types to remove, must be unmanaged.</typeparam>
    public void RemoveComponentsTuple<T>() where T : unmanaged
    {
        Commands.RemoveComponentsTuple<T>(this);
    }

    /// <summary>
    /// Records multiple component additions and removals on this entity as a single archetype migration.
    /// Remove operations must be called before Add operations within the configuration callback.
    /// </summary>
    /// <param name="configure">A callback that configures the alterations using the EntityAlter builder.</param>
    public void AlterComponents(Commands.EntityAlterContextDelegate configure)
    {
        Commands.AlterComponents(this, configure);
    }

    /// <summary>
    /// Gets a reference to a component of this entity as it exists in the world.
    /// Changes made through the returned reference do not trigger hooks.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A reference to the component.</returns>
    public ref T GetComponent<T>() where T : unmanaged, IComponent
    {
        return ref Commands.GetEntityComponent<T>(this);
    }

    /// <summary>
    /// Checks whether this entity has a specific component as it exists in the world.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if this entity has the component; otherwise, false.</returns>
    public bool WithComponent<T>() where T : unmanaged, IComponent
    {
        return Commands.WithComponent<T>(this);
    }

    /// <summary>
    /// Checks whether this entity does not have a specific component as it exists in the world.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if this entity does not have the component; otherwise, false.</returns>
    public bool WithoutComponent<T>() where T : unmanaged, IComponent
    {
        return Commands.WithoutComponent<T>(this);
    }

#endregion
}

/// <summary>
/// A stable reference to an entity, containing both the entity ID and generation.
/// The generation is used to detect references to destroyed entities that have been recycled.
/// </summary>
public struct EntityRef : IEquatable<EntityRef>
{
#region Properties & Fields

    /// <summary>
    /// Gets the unique identifier of the entity.
    /// </summary>
    public int ID { get; }

    internal int Generation;

#endregion

#region Constructors

    internal EntityRef(int id, int generation)
    {
        ID = id;
        Generation = generation;
    }

#endregion

#region Public Methods

    /// <summary>
    /// Determines whether two entity references are equal.
    /// </summary>
    /// <param name="a">The first entity reference.</param>
    /// <param name="b">The second entity reference.</param>
    /// <returns>True if both ID and generation match; otherwise, false.</returns>
    public static bool operator ==(EntityRef a, EntityRef b)
    {
        return a.Equals(b);
    }

    /// <summary>
    /// Determines whether two entity references are not equal.
    /// </summary>
    /// <param name="a">The first entity reference.</param>
    /// <param name="b">The second entity reference.</param>
    /// <returns>True if ID or generation differ; otherwise, false.</returns>
    public static bool operator !=(EntityRef a, EntityRef b)
    {
        return !a.Equals(b);
    }

    public override bool Equals(object? obj)
    {
        return obj is EntityRef other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generation, ID);
    }

#endregion

#region IEquatable Implementation

    public bool Equals(EntityRef other)
    {
        return ID == other.ID && Generation == other.Generation;
    }

#endregion
}

public struct EntityPos(int chunkIdx = 0, int idx = 0)
{
    internal readonly int ChunkIdx = chunkIdx;

    internal int Idx = idx;
}

/// <summary>
/// Stores information about an entity, including its archetype and position.
/// </summary>
public struct EntityInfo
{
    /// <summary>
    /// Gets the archetype this entity belongs to.
    /// </summary>
    public readonly Archetype Archetype;

    internal EntityPos Pos;

    internal EntityInfo(Archetype archetype, EntityPos pos)
    {
        Archetype = archetype;
        Pos = pos;
    }
}
