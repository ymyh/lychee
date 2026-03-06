using lychee;
using lychee.collections;
using lychee.interfaces;

namespace lychee_game;

public sealed class ManyToOne
{
    private readonly SparseMap<(Entity parent, List<Entity> children)> relationships = new();

    public void AddRelationship(Entity parent, Entity child)
    {
        if (!relationships.TryGetValue(parent.ID, out var relation))
        {
            relation.children = [];
            relationships.Add(parent.ID, relation);
        }

        relation.children.Add(child);
    }
}

public sealed class OneToOne
{
    private readonly SparseMap<(Entity, Entity)> relationships = new();

    public void AddRelationship(Entity head, Entity tail)
    {
        if (!relationships.TryGetValue(head.ID, out var relation))
        {
            relation.Item2 = tail;
            relationships.Add(head.ID, relation);
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
