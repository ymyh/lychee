using lychee.interfaces;

namespace lychee;

/// <summary>
/// The source side of a relationship: a component stored on the entity that points at another entity.
/// The component itself is the authoritative data. The matching <see cref="IRelationshipTarget{TRelationship}"/>
/// on the target entity is maintained by the framework through component hooks.
/// </summary>
public interface IRelationship
{
    /// <summary>
    /// Gets the entity this relationship points at.
    /// </summary>
    EntityRef Target { get; }
}

/// <summary>
/// The reverse side of a relationship: the collection of entities that point at the owning entity.
/// The framework owns the collection; user code only reads it. An empty collection is removed
/// automatically, so the component being present means at least one entity is related.
/// </summary>
/// <typeparam name="TRelationship">The relationship component type this collection belongs to.</typeparam>
public interface IRelationshipTarget<TRelationship> : IDisposable
    where TRelationship : unmanaged, IComponent, IRelationship
{
    /// <summary>
    /// Gets the number of entities currently related through <typeparamref name="TRelationship"/>.
    /// </summary>
    int RelatedCount { get; }

    /// <summary>
    /// Gets the related entity at the given index, in insertion order.
    /// </summary>
    /// <param name="index">The zero-based index of the related entity.</param>
    /// <returns>The related entity reference.</returns>
    EntityRef GetRelated(int index);

    /// <summary>
    /// Appends a related entity to the collection.
    /// </summary>
    /// <param name="entity">The entity to append.</param>
    void AddRelated(EntityRef entity);

    /// <summary>
    /// Removes the first occurrence of a related entity, preserving the order of the remaining ones.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <returns>True if the entity was found and removed; otherwise, false.</returns>
    bool RemoveRelated(EntityRef entity);
}
