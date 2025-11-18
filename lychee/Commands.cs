using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

using TransferInfoMap = SparseMap<Dictionary<nint, EntityTransferInfo>>;

internal struct ModifiedEntityInfo(Archetype archetype, int chunkIdx, int idx)
{
    public readonly Archetype Archetype = archetype;

    public readonly int ChunkIdx = chunkIdx;

    public readonly int Idx = idx;
}

internal sealed class EntityTransferInfo(Archetype archetype, (TypeInfo info, int typeId)[] bundleInfo)
{
    public readonly Archetype Archetype = archetype;

    public readonly int[] TypeIndices = bundleInfo.Select(x => archetype.GetTypeIndex(x.typeId)).ToArray();

    public readonly (TypeInfo info, int typeId)[] BundleInfo = bundleInfo;
}

public sealed class Commands(App app) : IDisposable
{
#region Fields

    private readonly EntityPool entityPool = app.World.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = app.World.ArchetypeManager;

    internal readonly TypeRegistrar TypeRegistrar = app.TypeRegistrar;

    internal readonly TransferInfoMap ArchetypeAddingTypeMap = new();

    internal readonly TransferInfoMap ArchetypeRemovingTypeMap = new();

    private readonly SparseMap<ModifiedEntityInfo> modifiedEntityInfoMap = new();

    private readonly SparseMap<Entity> removedEntityMap = new();

    internal EntityTransferInfo? TransferDstInfo;

    internal Archetype SrcArchetype = app.World.ArchetypeManager.GetArchetype(0);

    private bool srcArchetypeChanged = true;

#endregion

#region Public Methods

    public Entity CreateEntity()
    {
        var entity = entityPool.ReserveEntity();
        removedEntityMap.Remove(entity.ID);

        return entity;
    }

    public bool RemoveEntity(Entity entity)
    {
        if (removedEntityMap.ContainsKey(entity.ID))
        {
            return false;
        }

        if (entity.Generation != 0)
        {
            if (!entityPool.CheckEntityValid(entity))
            {
                return false;
            }

            var entityInfo = entityPool.GetEntityInfo(entity);

            if (entityInfo.ArchetypeId != SrcArchetype.ID)
            {
                SrcArchetype = ArchetypeManager.GetArchetype(entityInfo.ArchetypeId);
            }

            SrcArchetype.MarkRemove(entity.ID, entityInfo.ChunkIdx, entityInfo.Idx);
        }
        else
        {
            modifiedEntityInfoMap.TryGetValue(entity.ID, out var info);
            info.Archetype.MarkRemove(entity.ID, info.ChunkIdx, info.Idx);
        }

        modifiedEntityInfoMap.Remove(entity.ID);
        entityPool.MarkRemoveEntity(entity);
        removedEntityMap.Add(entity.ID, entity);

        return true;
    }

    public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var (srcChunkIdx, srcIdx) = GetPositionOfEntity(entity);

        if (srcArchetypeChanged)
        {
            this.AddComponentTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        TransferDstInfo.Archetype.PutComponentData(TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcChunkIdx, srcIdx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcChunkIdx, srcIdx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool AddComponents<T>(Entity entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var (srcChunkIdx, srcIdx) = GetPositionOfEntity(entity);

        if (srcArchetypeChanged)
        {
            this.AddComponentsTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo!.Archetype.Reserve();

        for (var i = 0; i < TransferDstInfo!.TypeIndices.Length; i++)
        {
            unsafe
            {
                var bundleInfo = TransferDstInfo.BundleInfo[i].info;
                var ptr = TransferDstInfo.Archetype.Table.GetPtr(TransferDstInfo.TypeIndices[i], chunkIdx, idx);

                fixed (T* bundlePtr = &bundle)
                {
                    var componentPtr = (byte*)bundlePtr + bundleInfo.Offset;
                    NativeMemory.Copy(componentPtr, ptr, (nuint)bundleInfo.Size);
                }
            }
        }

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcChunkIdx, srcIdx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcChunkIdx, srcIdx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var (srcChunkIdx, srcIdx) = GetPositionOfEntity(entity);

        if (srcArchetypeChanged)
        {
            this.RemoveComponentTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcChunkIdx, srcIdx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcChunkIdx, srcIdx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool RemoveComponents<T>(Entity entity) where T : unmanaged, IComponentBundle
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var (srcChunkIdx, srcIdx) = GetPositionOfEntity(entity);

        if (srcArchetypeChanged)
        {
            this.RemoveComponentsTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcChunkIdx, srcIdx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcChunkIdx, srcIdx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool RemoveComponentsTuple<T>(Entity entity) where T : unmanaged
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var (srcChunkIdx, srcIdx) = GetPositionOfEntity(entity);

        if (srcArchetypeChanged)
        {
            this.RemoveComponentsTupleTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcChunkIdx, srcIdx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcChunkIdx, srcIdx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    private (int srcChunkIdx, int srcIdx) GetPositionOfEntity(Entity entity)
    {
        var srcChunkIdx = 0;
        var srcIdx = 0;
        var oldSrcArchetype = SrcArchetype;

        if (modifiedEntityInfoMap.TryGetValue(entity.ID, out var info))
        {
            SrcArchetype = info.Archetype;
            srcChunkIdx = info.ChunkIdx;
            srcIdx = info.Idx;
        }
        else
        {
            if (entityPool.CheckEntityValid(entity))
            {
                var entityInfo = entityPool.GetEntityInfo(entity);
                SrcArchetype = ArchetypeManager.GetArchetype(entityInfo.ArchetypeId);
                (srcChunkIdx, srcIdx) = SrcArchetype.Table.GetChunkAndIndex(SrcArchetype.GetEntityIndex(entity));
            }
            else
            {
                SrcArchetype = ArchetypeManager.EmptyArchetype;
                srcArchetypeChanged = true;
            }
        }

        if (SrcArchetype != oldSrcArchetype)
        {
            srcArchetypeChanged = true;
        }

        return (srcChunkIdx, srcIdx);
    }

    public ref T GetEntityComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        if (modifiedEntityInfoMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity))
        {
            throw new ArgumentException($"Entity {entity.ID} is invalid or has already been modified");
        }

        var typeId = TypeRegistrar.GetTypeId<T>();
        var info = entityPool.GetEntityInfo(entity);
        var archetype = ArchetypeManager.GetArchetype(info.ArchetypeId);
        var (ptr, size) = archetype.GetChunkData(typeId, info.ChunkIdx);

        Debug.Assert((uint)info.Idx < (uint)size);

        unsafe
        {
            return ref *((T*)ptr + info.Idx);
        }
    }

    public void Trigger()
    {
    }

#endregion

#region Internal methods

    internal void Commit()
    {
        foreach (var (id, info) in modifiedEntityInfoMap)
        {
            var entity = entityPool.CommitReservedEntity(id, info.Archetype.ID, info.ChunkIdx, info.Idx);
            info.Archetype.CommitReservedEntity(entity);
        }

        foreach (var (_, entity) in removedEntityMap)
        {
            entityPool.CommitRemoveEntity(entity);
        }

        ArchetypeManager.Commit();
        removedEntityMap.Clear();
        modifiedEntityInfoMap.Clear();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ArchetypeAddingTypeMap.Dispose();
        ArchetypeRemovingTypeMap.Dispose();
        modifiedEntityInfoMap.Dispose();
        removedEntityMap.Dispose();
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(Commands self)
    {
        internal void AddComponentTransferInfo<T>() where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&AddComponentTransferInfo<T>;
            }

            if (self.ArchetypeAddingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeAddingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                var typeId = self.TypeRegistrar.RegisterComponent<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Append(typeId));

                self.TransferDstInfo = new(dstArchetype, [new(new(), typeId)]);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void AddComponentsTransferInfo<T>() where T : unmanaged, IComponentBundle
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&AddComponentsTransferInfo<T>;
            }

            if (self.ArchetypeAddingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeAddingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                self.TypeRegistrar.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistrar.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Concat(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, bundleInfo);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentTransferInfo<T>() where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&RemoveComponentTransferInfo<T>;
            }

            if (self.ArchetypeRemovingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                dict = new();
                self.TransferDstInfo = null;
                self.ArchetypeRemovingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                var typeId = self.TypeRegistrar.RegisterComponent<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Where(x => x != typeId));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentsTransferInfo<T>() where T : unmanaged, IComponentBundle
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&RemoveComponentsTransferInfo<T>;
            }

            if (self.ArchetypeRemovingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeRemovingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                self.TypeRegistrar.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistrar.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Except(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentsTupleTransferInfo<T>() where T : unmanaged
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&RemoveComponentsTupleTransferInfo<T>;
            }

            if (self.ArchetypeRemovingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeRemovingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                var typeIds = self.TypeRegistrar.GetComponentTypeIds<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Except(typeIds));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }
    }
}
