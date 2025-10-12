using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

internal sealed class EntityTransferInfo(Archetype archetype, (TypeInfo info, int typeId)[] bundleInfo)
{
    public readonly Archetype Archetype = archetype;

    public readonly int[] TypeIndices = bundleInfo.Select(x => archetype.GetTypeIndex(x.typeId)).ToArray();

    public readonly List<Entity> targetEntities = [];

    public readonly (TypeInfo info, int typeId)[] BundleInfo = bundleInfo;
}

public sealed class EntityCommander(App app) : IDisposable
{
#region Fields

    internal readonly EntityPool EntityPool = app.World.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = app.World.ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry = app.TypeRegistry;

    internal readonly Dictionary<nint, SparseMap<EntityTransferInfo>> SrcArchetypeAddingTypeDict = new();

    internal readonly Dictionary<nint, SparseMap<EntityTransferInfo>> SrcArchetypeRemovingTypeDict = new();

    internal Archetype SrcArchetype = null!;

    internal EntityTransferInfo? TransferDstInfo;

    internal bool SrcArchetypeChanged;

#endregion

#region Public Methods

    public int CreateEntity()
    {
        var id = EntityPool.ReserveEntity();
        return id;
    }

    public void RemoveEntity(Entity entity)
    {
        EntityPool.MarkRemoveEntity(entity);
    }

    public void ChangeSrcArchetype(Archetype archetype)
    {
        if (TransferDstInfo != null)
        {
            Monitor.Exit(TransferDstInfo.Archetype);
        }

        SrcArchetypeChanged = true;
        SrcArchetype = archetype;
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var item in SrcArchetypeAddingTypeDict)
        {
            item.Value.Dispose();
        }
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommander self)
    {
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
                    var typeId = self.TypeRegistry.RegisterComponent<T>();
                    var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Append(typeId));

                    self.TransferDstInfo = new(dstArchetype, [new(new(), dstArchetype.GetTypeIndex(typeId))]);
                    map.Add(self.SrcArchetype.ID, self.TransferDstInfo);
                }

                Monitor.Enter(self.TransferDstInfo.Archetype);
            }

            Debug.Assert(self.TransferDstInfo != null);

            var (chunkIdx, idx) = self.TransferDstInfo.Archetype.Reserve();
            self.TransferDstInfo.Archetype.PutPartialData(self.TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);
            self.SrcArchetype.MoveDataTo(entityInfo, self.TransferDstInfo.Archetype, chunkIdx, idx);

            self.TransferDstInfo.targetEntities.Add(entity);
            entityInfo.ArchetypeId = self.TransferDstInfo.Archetype.ID;
            self.EntityPool.SetEntityInfo(entity, entityInfo);

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
                    if (T.StructInfo == null)
                    {
                        self.TypeRegistry.RegisterBundle<T>();
                    }

                    var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Concat(T.StructInfo!.Select(x => x.typeId)));

                    self.TransferDstInfo = new(dstArchetype, T.StructInfo);
                    map.Add(self.SrcArchetype.ID, self.TransferDstInfo);
                }

                Monitor.Enter(self.TransferDstInfo.Archetype);
            }

            var (chunkIdx, idx) = self.TransferDstInfo!.Archetype.Reserve();

            for (var i = 0; i < self.TransferDstInfo!.TypeIndices.Length; i++)
            {
                unsafe
                {
                    var info = T.StructInfo![i].info;
                    var ptr = self.TransferDstInfo.Archetype.Table.GetPtr(self.TransferDstInfo.TypeIndices[i], chunkIdx, idx);

                    fixed (T* bundlePtr = &bundle)
                    {
                        var componentPtr = (byte*)bundlePtr + info.Offset;
                        NativeMemory.Copy(componentPtr, ptr, (nuint)info.Size);
                    }
                }
            }

            self.SrcArchetype.MoveDataTo(entityInfo, self.TransferDstInfo.Archetype, chunkIdx, idx);
            self.TransferDstInfo.targetEntities.Add(entity);

            entityInfo.ArchetypeId = self.TransferDstInfo.Archetype.ID;
            self.EntityPool.SetEntityInfo(entity, entityInfo);

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

        internal void CommitChanges()
        {

        }
    }
}
