using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee;

/// <summary>
/// Holds all entities in the world
/// </summary>
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
            lock (entities)
            {
                var info = entityInfoList[id];

                // a new entity always belongs to default archetype, thus archetypeIdx also meaningless
                info.ArchetypeId = 0;
                info.ArchetypeIdx = 0;

                entityInfoList[id] = info;

                return entities[id];
            }
        }

        id = Interlocked.Increment(ref latestEntityId);

        lock (entities)
        {
            entities.Add(new(id, 0));
            entityInfoList.Add(new());

            return entities[^1];
        }
    }

    /// <summary>
    /// Remove entity by id
    /// </summary>
    /// <param name="entity"></param>
    public bool RemoveEntity(Entity entity)
    {
        var id = entity.ID;
        lock (entities)
        {
            Debug.Assert(id >= 0 && id < entities.Count);

            if (entity.Generation != entities[id].Generation)
            {
                return false;
            }

            var span = CollectionsMarshal.AsSpan(entities);
            span[id].Generation++;

            reusableEntitiesId.Push(id);
        }

        return true;
    }

    /// <summary>
    /// Get entity info by entity
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    public bool GetEntityInfo(Entity entity, out EntityInfo info)
    {
        lock (entities)
        {
            var e = entities[entity.ID];

            if (e.Generation == entity.Generation)
            {
                info = entityInfoList[e.ID];
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Set entity info with given entity
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    public bool SetEntityInfo(Entity entity, EntityInfo info)
    {
        lock (entities)
        {
            if (entities[entity.ID].Generation == entity.Generation)
            {
                entityInfoList[entity.ID] = info;
                return true;
            }
        }

        return false;
    }
}
