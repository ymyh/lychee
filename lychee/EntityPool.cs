using System.Diagnostics;

namespace lychee;

public sealed class EntityPool
{
    private int latestEntityId;

    private readonly List<Entity> entities = [];

    private readonly List<EntityInfo> entityInfoList = [];

    private readonly Stack<int> reusableEntitiesId = new();

    /// <summary>
    /// Create a new Entity
    /// </summary>
    /// <returns>The entity just created</returns>
    public Entity NewEntity()
    {
        if (reusableEntitiesId.TryPop(out var id))
        {
            var info = entityInfoList[id];

            // a new entity always belongs to default archetype, thus archetypeIdx also meaningless
            info.ArchetypeId = 0;
            info.ArchetypeIdx = 0;

            entityInfoList[id] = info;

            return entities[id];
        }

        id = latestEntityId;
        latestEntityId += 1;

        entities.Add(new Entity(id, 0));
        entityInfoList.Add(new EntityInfo());

        return entities[^1];
    }

    /// <summary>
    /// Remove entity by id
    /// </summary>
    /// <param name="id"></param>
    /// <exception cref="ArgumentOutOfRangeException">if id is invalid</exception>
    public void RemoveEntity(int id)
    {
        Debug.Assert(id >= 0 && id < entities.Count);

        var entity = entities[id];
        entity.Generation += 1;
        entities[id] = entity;

        reusableEntitiesId.Push(id);
    }

    public EntityInfo? GetEntityInfo(Entity entity)
    {
        var e = entities[entity.ID];

        if (e.Generation == entity.Generation)
        {
            return entityInfoList[e.ID];
        }

        return null;
    }
}
