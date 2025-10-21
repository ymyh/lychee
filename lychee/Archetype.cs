﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager : IDisposable
{
    private readonly List<Archetype> archetypes = [];

    private readonly TypeRegistry typeRegistry;

    public delegate void ArchetypeCreatedHandler();

    /// <summary>
    /// Invoked when a new archetype is created.
    /// </summary>
    public event ArchetypeCreatedHandler? ArchetypeCreated;

    public ArchetypeManager(TypeRegistry typeRegistry)
    {
        this.typeRegistry = typeRegistry;
        GetOrCreateArchetype([]);
    }

#region Public methods

    public Archetype GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        var array = typeIdList.ToArray();
        Array.Sort(array);

        lock (archetypes)
        {
            foreach (var archetype in archetypes)
            {
                if (archetype.TypeIdList.SequenceEqual(array))
                {
                    return archetype;
                }
            }

            var id = archetypes.Count;
            var typeInfoList = array.Select(id => typeRegistry.GetTypeInfo(id).Item2).ToArray();
            archetypes.Add(new(id, array, typeInfoList));

            ArchetypeCreated?.Invoke();

            return archetypes[id];
        }
    }

    public Archetype GetOrCreateArchetype<T>()
    {
        var typeList = TypeUtils.GetTupleTypes<T>();
        var typeIds = typeList.Select(x => typeRegistry.RegisterComponent(x)).ToArray();

        return GetOrCreateArchetype(typeIds);
    }

    public Archetype GetOrCreateArchetype2<T>() where T : IComponentBundle
    {
        var type = typeof(T);
        var fields = type.GetFields();
        var typeIds = fields.Select(f => typeRegistry.RegisterComponent(f.FieldType)).ToArray();

        Array.Sort(typeIds);
        return GetOrCreateArchetype(typeIds);
    }

    public Archetype GetArchetype(int id)
    {
        lock (archetypes)
        {
            Debug.Assert(id >= 0 && id < archetypes.Count);
            return archetypes[id];
        }
    }

    public Archetype[] MatchArchetypesByPredicate(Type[] allFilter, Type[] anyFilter, Type[] noneFilter,
        int[] requires)
    {
        if (requires.Length == 0)
        {
            return [];
        }

        lock (archetypes)
        {
            return archetypes.Where(a =>
            {
                var ret = requires.Aggregate(true, (current, typeId) => current & a.TypeIdList.Contains(typeId));
                return allFilter.Select(type => typeRegistry.RegisterComponent(type))
                    .Aggregate(ret, (current, typeId) => current & a.TypeIdList.Contains(typeId));
            }).Where(a =>
            {
                var ret = anyFilter.Length == 0;

                foreach (var type in anyFilter)
                {
                    var typeId = typeRegistry.RegisterComponent(type);
                    ret |= a.TypeIdList.Contains(typeId);
                }

                return ret;
            }).Where(a =>
            {
                var ret = true;

                foreach (var type in noneFilter)
                {
                    var typeId = typeRegistry.RegisterComponent(type);
                    ret &= !a.TypeIdList.Contains(typeId);
                }

                return ret;
            }).ToArray();
        }
    }

#endregion

#region Internal methods

    internal void Commit()
    {
        foreach (var archetype in archetypes)
        {
            archetype.Commit();
        }
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var archetype in archetypes)
        {
            archetype.Dispose();
        }
    }

#endregion
}

public sealed class Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList) : IDisposable
{
#region Fields

    public readonly int ID = id;

    public readonly int[] TypeIdList = typeIdList;

    internal readonly Table Table = new(new(typeInfoList));

    private readonly SparseMap<Entity> entities = [];

    private readonly SparseMap<int> typeIdxMap = new(typeIdList.Select((id, index) => (id, index)));

    private readonly SparseMap<int[]> dstArchetypeCommCompIndices = new();

    private readonly ConcurrentStack<(int entityId, int chunkIdx, int idx)> holesInTable = new();

    private bool dirty;

#endregion

#region Public Methods

    public IEnumerable<(nint ptr, int size)> IterateTypeAmongChunk(int typeId)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.IterateOfTypeAmongChunk(typeIdx);
    }

    public Span<(int, Entity)> GetEntitiesSpan()
    {
        return entities.GetDenseAsSpan();
    }

    public int GetEntityIndex(Entity entity)
    {
        return entities.GetIndex(entity.ID);
    }

#endregion

#region Internal Methods

    internal void Commit()
    {
        if (!dirty || Table.Chunks.Count == 0)
        {
            return;
        }

        while (holesInTable.TryPop(out var hole))
        {
            var chunk = Table.Chunks[hole.chunkIdx];
            var from = chunk.Size + chunk.Reservation - 1;

            FillHole(hole.chunkIdx, from, hole.idx);

            if (chunk.Reservation > 0)
            {
                chunk.Reservation--;
            }
            else
            {
                entities.Remove(hole.entityId);
            }
        }

        Table.CommitReserved();
        dirty = false;
    }

    internal void CommitReservedEntity(Entity entity)
    {
        entities.Add(entity.ID, entity);
    }

    internal int GetTypeIndex(int typeId)
    {
        return typeIdxMap[typeId];
    }

    internal void MarkRemove(int entityId, int chunkIdx, int idx)
    {
        holesInTable.Push((entityId, chunkIdx, idx));
    }

    internal void MoveDataTo(Archetype archetype, int srcChunkIdx, int srcIdx, int dstChunkIdx, int dstIdx)
    {
        int[] commCompIndices;

        if (dstArchetypeCommCompIndices.TryGetValue(archetype.ID, out var compIndices))
        {
            commCompIndices = compIndices;
        }
        else
        {
            var commCompIds = TypeIdList.Intersect(archetype.TypeIdList).ToArray();
            commCompIndices = new int[commCompIds.Length];
            archetype.GetTypeIndices(commCompIds, commCompIndices);

            dstArchetypeCommCompIndices.Add(archetype.ID, commCompIndices);
        }

        foreach (var index in commCompIndices)
        {
            unsafe
            {
                var src = Table.GetPtr(index, srcChunkIdx, srcIdx);
                var dst = archetype.Table.GetPtr(index, dstChunkIdx, dstIdx);

                NativeMemory.Copy(src, dst, (nuint)Table.Layout.TypeInfoList[index].Size);
            }
        }

        dirty = true;
    }

    internal void PutComponentData<T>(int typeIdx, int chunkIdx, int idx, in T data) where T : unmanaged
    {
        unsafe
        {
            var dstPtr = Table.GetPtr(typeIdx, chunkIdx, idx);
            fixed (T* srcPtr = &data)
            {
                NativeMemory.Copy(srcPtr, dstPtr, (nuint)Table.Layout.TypeInfoList[typeIdx].Size);
            }
        }
    }

    internal (int chunkIdx, int idx) Reserve()
    {
        dirty = true;
        return Table.Reserve();
    }

    internal void RemoveEntity(Entity entity)
    {
        entities.Remove(entity.ID);
    }

#endregion

#region Private methods

    private void FillHole(int chunkIdx, int from, int to)
    {
        Debug.Assert(from != to);

        for (var i = 0; i < Table.Layout.TypeInfoList.Length; i++)
        {
            unsafe
            {
                var srcPtr = Table.GetPtr(i, chunkIdx, from);
                var dstPtr = Table.GetPtr(i, chunkIdx, to);

                NativeMemory.Copy(srcPtr, dstPtr, (nuint)Table.Layout.TypeInfoList[i].Size);
            }
        }

        if (from < Table.Chunks[chunkIdx].Size)
        {
            Table.Chunks[chunkIdx].Size--;
        }
    }

    private void GetTypeIndices(IEnumerable<int> typeIdList, Span<int> output)
    {
        var i = 0;
        foreach (var typeId in typeIdList)
        {
            output[i] = typeIdxMap[typeId];
            i++;
        }
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        entities.Dispose();
        dstArchetypeCommCompIndices.Dispose();
        typeIdxMap.Dispose();

        Table.Dispose();
    }

#endregion
}
