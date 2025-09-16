using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee;

public sealed class EntityPool
{
    private int latestEntityId;

    private readonly List<Entity> entities = [];

    private readonly List<EntityInfo> entityInfoList = [];

    private readonly ConcurrentStack<int> reusableEntitiesId = new();

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

        id = Interlocked.Increment(ref latestEntityId);

        // Maybe low performance
        lock (reusableEntitiesId)
        {
            entities.Add(new(id, 0));
            entityInfoList.Add(new());
        }

        return entities[^1];
    }

    /// <summary>
    /// Remove entity by id
    /// </summary>
    /// <param name="id"></param>
    public bool RemoveEntity(Entity entity)
    {
        var id = entity.ID;
        Debug.Assert(id >= 0 && id < entities.Count);

        if (entity.Generation != entities[id].Generation)
        {
            return false;
        }

        var span = CollectionsMarshal.AsSpan(entities);
        Interlocked.Increment(ref span[id].Generation);

        reusableEntitiesId.Push(id);

        return true;
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
