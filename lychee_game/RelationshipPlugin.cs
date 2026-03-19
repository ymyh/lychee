using lychee;
using lychee.collections;
using lychee.interfaces;

namespace lychee_game;

public sealed class ManyToOne
{
    private readonly SparseMap<(EntityRef parent, List<EntityRef> children)> relationships = new();

    public void AddRelationship(EntityRef parent, EntityRef child)
    {
        if (!relationships.TryGetValue(parent.ID, out var relation))
        {
            relation.children = [];
            relationships.AddOrUpdate(parent.ID, relation);
        }

        relation.children.Add(child);
    }
}

public sealed class OneToOne
{
    private readonly SparseMap<(EntityRef, EntityRef)> relationships = new();

    public void AddRelationship(EntityRef head, EntityRef tail)
    {
        if (!relationships.TryGetValue(head.ID, out var relation))
        {
            relation.Item2 = tail;
            relationships.AddOrUpdate(head.ID, relation);
        }

        relation.Item2 = tail;
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
