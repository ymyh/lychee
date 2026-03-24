using lychee.interfaces;

namespace lychee;

/// <summary>
/// Represents an entity in the ECS framework.
/// An entity is essentially an ID that can have components attached to it.
/// </summary>
public struct Entity(Commands commands, Archetype archetype)
{
    internal Archetype Archetype = archetype;

    /// <summary>
    /// Gets or sets the entity reference containing the entity ID and generation.
    /// </summary>
    public EntityRef Ref { get; set; }

    internal EntityPos Pos;

    /// <summary>
    /// Gets the unique identifier of this entity.
    /// </summary>
    public int ID => Ref.ID;

    internal int Generation => Ref.Generation;

    internal Commands Commands => commands;

    /// <summary>
    /// Initializes a new instance of the Entity struct with full state information.
    /// </summary>
    /// <param name="commands">The commands instance for deferred operations.</param>
    /// <param name="archetype">The archetype this entity belongs to.</param>
    /// <param name="entityRef">The entity reference containing ID and generation.</param>
    /// <param name="pos">The position of this entity within its archetype.</param>
    public Entity(Commands commands, Archetype archetype, EntityRef entityRef, EntityPos pos) : this(commands, archetype)
    {
        Ref = entityRef;
        Pos = pos;
    }

    /// <summary>
    /// Despawns this entity, marking it for removal.
    /// The entity will be fully removed when the commands are committed.
    /// </summary>
    public void Despawn()
    {
        commands.RemoveEntity(in this);
    }

    /// <summary>
    /// Adds a component to this entity.
    /// The entity will be moved to a new archetype matching its updated component composition.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="component">The component value to add.</param>
    public void AddComponent<T>(in T component) where T : unmanaged, IComponent
    {
        commands.AddComponent(ref this, in component);
    }

    /// <summary>
    /// Adds multiple components as a bundle to this entity.
    /// All components in the bundle will be added in a single operation.
    /// </summary>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <param name="components">The component bundle containing the components to add.</param>
    public void AddComponents<T>(in T components) where T : unmanaged, IComponentBundle
    {
        commands.AddComponents(ref this, in components);
    }

    /// <summary>
    /// Removes a component from this entity.
    /// The entity will be moved to a new archetype matching its updated component composition.
    /// </summary>
    /// <typeparam name="T">The component type to remove, must be unmanaged and implement IComponent.</typeparam>
    public void RemoveComponent<T>() where T : unmanaged, IComponent
    {
        commands.RemoveComponent<T>(ref this);
    }

    /// <summary>
    /// Removes all components defined in a component bundle from this entity.
    /// </summary>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    public void RemoveComponents<T>() where T : unmanaged, IComponentBundle
    {
        commands.RemoveComponents<T>(ref this);
    }

    /// <summary>
    /// Removes all components defined in a tuple from this entity.
    /// </summary>
    /// <typeparam name="T">The tuple type containing the component types to remove, must be unmanaged.</typeparam>
    public void RemoveComponentsTuple<T>() where T : unmanaged
    {
        commands.RemoveComponentsTuple<T>(ref this);
    }

    /// <summary>
    /// Performs multiple component additions and removals on this entity in a single archetype migration.
    /// Remove operations must be called before Add operations within the configuration callback.
    /// </summary>
    /// <param name="configure">A callback that configures the alterations using the EntityAlter builder.</param>
    public void AlterComponents(Commands.EntityAlterContextDelegate configure)
    {
        commands.AlterComponents(ref this, configure);
    }

    /// <summary>
    /// Gets a reference to a component of this entity.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A reference to the component.</returns>
    public ref T GetComponent<T>() where T : unmanaged, IComponent
    {
        return ref commands.GetEntityComponent<T>(Archetype, Pos);
    }

    /// <summary>
    /// Checks whether this entity has a specific component.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if this entity has the component; otherwise, false.</returns>
    public bool WithComponent<T>() where T : unmanaged, IComponent
    {
        return commands.WithComponent<T>(ref this);
    }

    /// <summary>
    /// Checks whether this entity does not have a specific component.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if this entity does not have the component; otherwise, false.</returns>
    public bool WithoutComponent<T>() where T : unmanaged, IComponent
    {
        return commands.WithoutComponent<T>(ref this);
    }
}

/// <summary>
/// A stable reference to an entity, containing both the entity ID and generation.
/// The generation is used to detect references to destroyed entities that have been recycled.
/// </summary>
public struct EntityRef : IEquatable<EntityRef>
{
    /// <summary>
    /// Gets the unique identifier of the entity.
    /// </summary>
    public int ID { get; }

    internal int Generation;

    internal EntityRef(int id, int generation)
    {
        ID = id;
        Generation = generation;
    }

    /// <summary>
    /// Determines whether this entity reference is equal to another.
    /// </summary>
    /// <param name="other">The entity reference to compare with.</param>
    /// <returns>True if both ID and generation match; otherwise, false.</returns>
    public bool Equals(EntityRef other)
    {
        return ID == other.ID && Generation == other.Generation;
    }

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

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is EntityRef other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Generation, ID);
    }
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
