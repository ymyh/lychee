using lychee;
using lychee.interfaces;

namespace lychee_game;

public sealed class Relationship
{
    private Dictionary<Entity, List<Entity>> relationships = new();

    public void AddRelationship(Entity parent, Entity child)
    {
        if (!relationships.TryGetValue(parent, out var children))
        {
            children = [];
            relationships.Add(parent, children);
        }

        children.Add(child);
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
