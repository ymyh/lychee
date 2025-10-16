using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

internal sealed class EntityTransferInfo(Archetype archetype, (TypeInfo info, int typeIdx)[] bundleInfo)
{
    public readonly Archetype Archetype = archetype;

    public readonly int[] TypeIndices = bundleInfo.Select(x => x.typeIdx).ToArray();

    public readonly List<Entity> TargetEntities = [];

    public readonly (TypeInfo info, int typeId)[] BundleInfo = bundleInfo;
}

public sealed class EntityCommander(App app) : IDisposable
{
#region Fields

    internal readonly EntityPool EntityPool = app.World.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = app.World.ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry = app.TypeRegistry;

    internal readonly Dictionary<nint, SparseMap<EntityTransferInfo>> SrcArchetypeAddingTypeDict = new();

    internal readonly SparseMap<Dictionary<nint, EntityTransferInfo>> SrcArchetypeAddingTypeDict2 = new();

    internal readonly Dictionary<nint, SparseMap<EntityTransferInfo>> SrcArchetypeRemovingTypeDict = new();

    internal readonly SparseMap<Dictionary<nint, EntityTransferInfo>> SrcArchetypeRemovingTypeDict2 = new();

    internal readonly List<(Entity, Archetype)> RemovedEntities = [];

    internal Archetype SrcArchetype = app.World.ArchetypeManager.GetArchetype(0);

    internal EntityTransferInfo? TransferDstInfo;

    internal bool SrcArchetypeChanged = true;

#endregion

#region Public Methods

    public UnCommitedEntity CreateEntity()
    {
        return EntityPool.ReserveEntity();
    }

    public void RemoveEntity(Entity entity)
    {
        RemovedEntities.Add((entity, SrcArchetype));
        EntityPool.MarkRemoveEntity(entity);
    }

    public void RemoveEntity(UnCommitedEntity entity)
    {
        RemoveEntity(new Entity(entity.ID, 0));
    }

    public void ChangeSrcArchetype(Archetype archetype)
    {
        SrcArchetypeChanged = true;
        SrcArchetype = archetype;
    }

#endregion

#region Internal methods

    internal void CommitChanges()
    {
        foreach (var (_, map) in SrcArchetypeAddingTypeDict)
        {
            foreach (var (_, info) in map)
            {
                foreach (var entity in info.TargetEntities)
                {
                    EntityPool.CommitReservedEntity(entity.ID);
                    EntityPool.SetEntityInfo(entity, new(info.Archetype.ID));

                    info.Archetype.CommitReservedEntity(entity);
                }
            }
        }

        foreach (var (_, map) in SrcArchetypeRemovingTypeDict)
        {
            foreach (var (_, info) in map)
            {
                foreach (var entity in info.TargetEntities)
                {
                    info.Archetype.RemoveEntity(entity);
                }
            }
        }

        foreach (var (entity, archetype) in RemovedEntities)
        {
            archetype.RemoveEntity(entity);
        }

        RemovedEntities.Clear();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var (_, map) in SrcArchetypeAddingTypeDict)
        {
            map.Dispose();
        }
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommander self)
    {
        public void AddComponent<T>(int entityId, in T component) where T : unmanaged, IComponent
        {
            var entity = new Entity(entityId, 0);
            self.AddComponent(entity, in component);
        }

        public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.SrcArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommander, Entity, in T, bool>)&AddComponent<T>;
                }

                if (self.SrcArchetypeAddingTypeDict2.TryGetValue(self.SrcArchetype.ID, out var dict))
                {
                    if (dict.TryGetValue(ptr, out var info))
                    {
                        self.TransferDstInfo = info;
                    }
                }
                else
                {
                    dict = new();
                    self.SrcArchetypeAddingTypeDict2.Add(self.SrcArchetype.ID, dict);
                }

                if (self.TransferDstInfo == null)
                {
                    var typeId = self.TypeRegistry.RegisterComponent<T>();
                    var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Append(typeId));

                    self.TransferDstInfo = new(dstArchetype, [new(new(), dstArchetype.GetTypeIndex(typeId))]);
                    dict.Add(ptr, self.TransferDstInfo);
                }

                self.SrcArchetypeChanged = false;
            }

            Debug.Assert(self.TransferDstInfo != null);

            var (chunkIdx, idx) = self.TransferDstInfo.Archetype.Reserve();
            self.TransferDstInfo.Archetype.PutPartialData(self.TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);
            self.SrcArchetype.MoveDataTo(entityInfo, self.TransferDstInfo.Archetype, chunkIdx, idx);
            self.TransferDstInfo.TargetEntities.Add(entity);

            return true;
        }

        public bool AddComponents<T>(Entity entity, in T bundle) where T : unmanaged, IComponentBundle
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.SrcArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommander, Entity, in T, bool>)&AddComponents<T>;
                }

                if (self.SrcArchetypeAddingTypeDict.TryGetValue(ptr, out var map))
                {
                    map.TryGetValue(self.SrcArchetype.ID, out self.TransferDstInfo);
                }
                else
                {
                    map = new();
                    self.SrcArchetypeAddingTypeDict.Add(ptr, map);
                }

                if (self.TransferDstInfo == null)
                {
                    self.TypeRegistry.RegisterBundle<T>();

                    var bundleInfo = self.TypeRegistry.GetBundleInfo<T>();
                    var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Concat(bundleInfo.Select(x => x.typeId)));

                    self.TransferDstInfo = new(dstArchetype, bundleInfo);
                    map.Add(self.SrcArchetype.ID, self.TransferDstInfo);
                }

                self.SrcArchetypeChanged = false;
            }

            var (chunkIdx, idx) = self.TransferDstInfo!.Archetype.Reserve();

            for (var i = 0; i < self.TransferDstInfo!.TypeIndices.Length; i++)
            {
                unsafe
                {
                    var info = self.TransferDstInfo.BundleInfo[i].info;
                    var ptr = self.TransferDstInfo.Archetype.Table.GetPtr(self.TransferDstInfo.TypeIndices[i], chunkIdx, idx);

                    fixed (T* bundlePtr = &bundle)
                    {
                        var componentPtr = (byte*)bundlePtr + info.Offset;
                        NativeMemory.Copy(componentPtr, ptr, (nuint)info.Size);
                    }
                }
            }

            self.SrcArchetype.MoveDataTo(entityInfo, self.TransferDstInfo.Archetype, chunkIdx, idx);
            self.TransferDstInfo.TargetEntities.Add(entity);

            return true;
        }

        public bool RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.SrcArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommander, Entity, bool>)&RemoveComponent<T>;
                }

                if (self.SrcArchetypeRemovingTypeDict.TryGetValue(ptr, out var map))
                {
                    map.TryGetValue(self.SrcArchetype.ID, out self.TransferDstInfo);
                }
                else
                {
                    map = new();
                    self.SrcArchetypeRemovingTypeDict.Add(ptr, map);
                }
            }

            return true;
        }

        public bool RemoveComponents<T>(Entity entity) where T : unmanaged, IComponentBundle
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.SrcArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommander, Entity, bool>)&RemoveComponents<T>;
                }

                if (self.SrcArchetypeRemovingTypeDict.TryGetValue(ptr, out var map))
                {
                    map.TryGetValue(self.SrcArchetype.ID, out self.TransferDstInfo);
                }
                else
                {
                    map = new();
                    self.SrcArchetypeRemovingTypeDict.Add(ptr, map);
                }
            }

            return true;
        }
    }
}
