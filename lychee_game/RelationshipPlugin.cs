using lychee;
using lychee.collections;
using lychee.interfaces;

namespace lychee_game;

public sealed class Relationship
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

public sealed class RelationshipPlugin : IPlugin
{
    public Relationship Relationship = null!;

    public void Install(App app)
    {
        Relationship = app.AddResource(new Relationship());
    }
}