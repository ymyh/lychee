using lychee;
using lychee.collections;
using lychee.interfaces;

namespace lychee_game;

/// <summary>
/// Represents a many-to-one relationship between entities. Each parent entity can have multiple child entities, but each child can only have one parent.
/// </summary>
public sealed class ManyToOne
{
    private readonly SparseMap<(EntityRef parent, SparseMap<EntityRef> children)> relationships = [];

    public void AddRelationship(EntityRef parent, EntityRef child)
    {
        if (!relationships.TryGetValue(parent.ID, out var relation))
        {
            relation.children = [];
            relationships.AddOrUpdate(parent.ID, relation);
        }

        relation.children.Add(child.ID, child);
    }

    public void RemoveRelationship(EntityRef parent, EntityRef child)
    {
        if (relationships.TryGetValue(parent.ID, out var relation))
        {
            relation.children.Remove(child.ID);
        }
    }
}

/// <summary>
/// Represents a one-to-one relationship between two entities. Each entity can associate with at most one entity.
/// </summary>
public sealed class OneToOne
{
    private readonly SparseMap<(EntityRef, EntityRef)> relationships = [];

    public void AddRelationship(EntityRef head, EntityRef tail)
    {
        relationships[head.ID] = (head, tail);
    }

    public void RemoveRelationship(EntityRef head)
    {
        relationships.Remove(head.ID);
    }
}

public sealed class RelationshipPlugin : IPlugin
{
    public readonly OneToOne OneToOne = new();

    public readonly ManyToOne ManyToOne = new();

    public void Install(App app)
    {
        app.AddResource(OneToOne);
        app.AddResource(ManyToOne);
    }
}
