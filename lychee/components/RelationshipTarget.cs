using lychee.attributes;
using lychee.collections;
using lychee.interfaces;

namespace lychee.components;

/// <summary>
/// The framework-maintained reverse side of a relationship: the ordered collection of entities that
/// point at this entity through <typeparamref name="TRelationship"/>. It is read-only for user code;
/// the relationship hooks keep it in sync. When its last entry is removed the component is removed
/// with it, so having the component means having at least one related entity.
/// </summary>
/// <typeparam name="TRelationship">The relationship component type.</typeparam>
[Component]
public partial struct RelationshipTarget<TRelationship> : IRelationshipTarget<TRelationship>
    where TRelationship : unmanaged, IComponent, IRelationship
{
#region Private Fields

    private NativeList<EntityRef> entities;

#endregion

#region Public Properties

    /// <summary>
    /// Gets a read-only view over the related entities, in insertion order.
    /// </summary>
    public readonly ReadOnlySpan<EntityRef> Entities => entities.AsSpan();

#endregion

#region Public Methods

    /// <inheritdoc/>
    public readonly int RelatedCount => entities.Count;

    /// <inheritdoc/>
    public EntityRef GetRelated(int index) => entities[index];

    /// <inheritdoc/>
    public void AddRelated(EntityRef entity) => entities.Add(entity);

    /// <inheritdoc/>
    public bool RemoveRelated(EntityRef entity) => entities.Remove(entity);

#endregion

#region IDisposable Implementation

    /// <inheritdoc/>
    public void Dispose() => entities.Dispose();

#endregion
}
