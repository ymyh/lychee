using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

using TransferInfoMap = SparseMap<Dictionary<nint, EntityTransferInfo>>;

internal struct ModifiedEntityInfo(Archetype archetype, int chunkIdx, int idx)
{
    public Archetype Archetype = archetype;

    public int ChunkIdx = chunkIdx;

    public int Idx = idx;
}

internal sealed class EntityTransferInfo(Archetype archetype, (TypeInfo info, int typeId)[] bundleInfo)
{
    public readonly Archetype Archetype = archetype;

    public readonly int[] TypeIndices = bundleInfo.Select(x => archetype.GetTypeIndex(x.typeId)).ToArray();

    public readonly (TypeInfo info, int typeId)[] BundleInfo = bundleInfo;
}

public sealed class EntityCommander : IDisposable
{
#region Fields

    internal readonly EntityPool EntityPool;

    internal readonly ArchetypeManager ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry;

    internal readonly TransferInfoMap ArchetypeAddingTypeMap = new();

    internal readonly TransferInfoMap ArchetypeRemovingTypeMap = new();

    internal readonly SparseMap<ModifiedEntityInfo> ModifiedEntityInfoMap = new();

    internal EntityTransferInfo? TransferDstInfo;

    private static Archetype emptyArchetype = null!;

    /// <summary>
    /// <b> WARNING: Do not set this property manually. </b>
    /// </summary>
    public Archetype SrcArchetype
    {
        get;

        set
        {
            srcArchetypeChanged = field != value;
            field = value;
        }
    }

    private bool srcArchetypeChanged = true;

#endregion

#region Constructors

    public EntityCommander(App app)
    {
        EntityPool = app.World.EntityPool;
        ArchetypeManager = app.World.ArchetypeManager;
        TypeRegistry = app.TypeRegistry;
        SrcArchetype = app.World.ArchetypeManager.GetArchetype(0);

        emptyArchetype = app.World.ArchetypeManager.GetArchetype(0);
    }

#endregion

#region Public Methods

    public Entity CreateEntity()
    {
        return EntityPool.ReserveEntity();
    }

    public bool RemoveEntity(Entity entity)
    {
        if (entity.Generation != 0)
        {
            if (!EntityPool.CheckEntityValid(entity))
            {
                return false;
            }

            // EntityPool.MarkRemoveEntity(entity);
            EntityPool.GetEntityInfo(entity, out var entityInfo);

            if (entityInfo.ArchetypeId != SrcArchetype.ID)
            {
                SrcArchetype = ArchetypeManager.GetArchetype(entityInfo.ArchetypeId);
            }

            SrcArchetype.MarkRemove(entity.ID, entityInfo.ChunkIdx, entityInfo.Idx);
        }
        else
        {
            ModifiedEntityInfoMap.TryGetValue(entity.ID, out var info);
            info.Archetype.MarkRemove(entity.ID, info.ChunkIdx, info.Idx);
        }

        ModifiedEntityInfoMap.Remove(entity.ID);
        EntityPool.MarkRemoveEntity(entity);

        return true;
    }

    public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
    {
        if (entity.Generation != 0 && !EntityPool.CheckEntityValid(entity))
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
        ModifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool AddComponents<T>(Entity entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        if (entity.Generation != 0 && !EntityPool.CheckEntityValid(entity))
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
        ModifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        if (entity.Generation != 0 && !EntityPool.CheckEntityValid(entity))
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
        ModifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    public bool RemoveComponents<T>(Entity entity) where T : unmanaged, IComponentBundle
    {
        if (entity.Generation != 0 && !EntityPool.CheckEntityValid(entity))
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
        ModifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, chunkIdx, idx));

        return true;
    }

    private (int srcChunkIdx, int srcIdx) GetPositionOfEntity(Entity entity)
    {
        var srcChunkIdx = 0;
        var srcIdx = 0;

        if (ModifiedEntityInfoMap.TryGetValue(entity.ID, out var info))
        {
            SrcArchetype = info.Archetype;
            srcChunkIdx = info.ChunkIdx;
            srcIdx = info.Idx;
        }
        else
        {
            if (EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                SrcArchetype = ArchetypeManager.GetArchetype(entityInfo.ArchetypeId);
                (srcChunkIdx, srcIdx) = SrcArchetype.Table.GetChunkAndIndex(SrcArchetype.GetEntityIndex(entity));
            }
            else
            {
                SrcArchetype = emptyArchetype;
                srcArchetypeChanged = true;
            }
        }

        return (srcChunkIdx, srcIdx);
    }

#endregion

#region Internal methods

    internal void Commit()
    {
        foreach (var (id, info) in ModifiedEntityInfoMap)
        {
            var entity = EntityPool.CommitReservedEntity(id, info.Archetype.ID, info.ChunkIdx, info.Idx);
            info.Archetype.CommitReservedEntity(entity);
        }

        ArchetypeManager.Commit();
        ModifiedEntityInfoMap.Clear();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ArchetypeAddingTypeMap.Dispose();
        ArchetypeRemovingTypeMap.Dispose();
        ModifiedEntityInfoMap.Dispose();
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommander self)
    {
        internal void AddComponentTransferInfo<T>() where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <EntityCommander, void>)&AddComponentTransferInfo<T>;
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
                var typeId = self.TypeRegistry.RegisterComponent<T>();
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
                ptr = (nint)(delegate* <EntityCommander, void>)&AddComponentsTransferInfo<T>;
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
                self.TypeRegistry.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistry.GetBundleInfo<T>();
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
                ptr = (nint)(delegate* <EntityCommander, void>)&AddComponentTransferInfo<T>;
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
                var typeId = self.TypeRegistry.RegisterComponent<T>();
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
                ptr = (nint)(delegate* <EntityCommander, void>)&AddComponentsTransferInfo<T>;
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
                self.TypeRegistry.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistry.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Concat(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }
    }
}
