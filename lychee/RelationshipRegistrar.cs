using System.Runtime.CompilerServices;
using lychee.interfaces;
using Microsoft.Extensions.Logging;

namespace lychee;

/// <summary>
/// Wires a relationship type pair into the framework: it registers the relationship and target hooks, writes
/// the erased <see cref="RelationshipInfo"/> into the world, and keeps the reverse collection in sync with the
/// source component as components are added, replaced and removed.
/// </summary>
internal static class RelationshipRegistrar
{
#region Internal Methods

    /// <summary>
    /// Registers a relationship pair. The relationship component is the source of truth; the target component
    /// is maintained by the hooks installed here.
    /// </summary>
    /// <typeparam name="TRelationship">The relationship component type, such as <c>ChildOf</c>.</typeparam>
    /// <typeparam name="TTarget">The reverse collection component type.</typeparam>
    /// <param name="app">The application the hooks are registered on.</param>
    /// <param name="linkedSpawn">Whether removing a target cascades to its related entities, and whether a
    /// linked clone copies the whole subtree.</param>
    /// <param name="remapTarget">Rewrites the target of a relationship component, used by linked cloning.</param>
    internal static unsafe void Register<TRelationship, TTarget>(
        App app,
        bool linkedSpawn,
        delegate* unmanaged<void*, EntityRef, void> remapTarget)
        where TRelationship : unmanaged, IComponent, IRelationship
        where TTarget : unmanaged, IComponent, IRelationshipTarget<TRelationship>
    {
        var typeRegistrar = app.TypeRegistrar;
        var relationshipTypeId = typeRegistrar.RegisterComponent<TRelationship>();
        var targetTypeId = typeRegistrar.RegisterComponent<TTarget>();

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TRelationship relationship) =>
            Attach<TRelationship, TTarget>(ref context, relationship.Target, context.Entity));

        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TRelationship relationship) =>
        {
            if (!context.TryGetPrevious<TRelationship>(out var previous) || previous.Target == relationship.Target)
            {
                return;
            }

            Detach<TRelationship, TTarget>(ref context, previous.Target, context.Entity);
            Attach<TRelationship, TTarget>(ref context, relationship.Target, context.Entity);
        });

        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TRelationship relationship) =>
            Detach<TRelationship, TTarget>(ref context, relationship.Target, context.Entity));

        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TTarget target) =>
        {
            if (linkedSpawn)
            {
                for (var i = 0; i < target.RelatedCount; i++)
                {
                    context.Commands.RemoveEntity(target.GetRelated(i));
                }
            }

            // TTarget owns native memory, so removing it must dispose the collection in place: the slot is
            // still readable by later OnRemove hooks in the same removal. Disposing a boxed copy would free
            // the allocation but leave the slot's pointer dangling.
            ref var mutableTarget = ref Unsafe.AsRef(in target);
            mutableTarget.Dispose();
        });

        app.World.Relationships.Register(new RelationshipInfo
        {
            RelationshipTypeId = relationshipTypeId,
            TargetTypeId = targetTypeId,
            LinkedSpawn = linkedSpawn,
            RelatedCount = RelatedCountImpl<TTarget, TRelationship>,
            GetRelated = GetRelatedImpl<TTarget, TRelationship>,
            GetTarget = GetTargetImpl<TRelationship>,
            RemapTarget = remapTarget,
        });
    }

#endregion

#region Private Static Methods

    private static unsafe void Attach<TRelationship, TTarget>(ref HookContext context, EntityRef target, EntityRef source)
        where TRelationship : unmanaged, IComponent, IRelationship
        where TTarget : unmanaged, IComponent, IRelationshipTarget<TRelationship>
    {
        if (target == source || !context.IsAlive(target) || IsAncestor<TRelationship>(ref context, source, target))
        {
            // Self-reference, a dangling target, or a cycle: drop the source component that would create it.
            context.App.Logger.LogWarning(
                "Relationship {Relationship} on entity {Entity} targets entity {Target}, which is invalid; the relationship was removed.",
                typeof(TRelationship).Name, source.ID, target.ID);

            context.Commands.RemoveComponent<TRelationship>(source);

            return;
        }

        if (context.Has<TTarget>(target))
        {
            // The collection already exists: append through a ref so it takes effect immediately.
            context.Get<TTarget>(target).AddRelated(source);
        }
        else
        {
            var fresh = default(TTarget);
            fresh.AddRelated(source);

            // The collection is created one command later, right after the command that triggered this hook.
            context.Commands.AddComponent(target, in fresh);
        }
    }

    private static void Detach<TRelationship, TTarget>(ref HookContext context, EntityRef target, EntityRef source)
        where TRelationship : unmanaged, IComponent, IRelationship
        where TTarget : unmanaged, IComponent, IRelationshipTarget<TRelationship>
    {
        if (!context.IsAlive(target) || !context.Has<TTarget>(target))
        {
            return;
        }

        ref var collection = ref context.Get<TTarget>(target);

        if (collection.RemoveRelated(source) && collection.RelatedCount == 0)
        {
            context.Commands.RemoveComponent<TTarget>(target);
        }
    }

    private static bool IsAncestor<TRelationship>(ref HookContext context, EntityRef source, EntityRef target)
        where TRelationship : unmanaged, IComponent, IRelationship
    {
        var current = target;

        while (context.TryGet<TRelationship>(current, out var relationship))
        {
            current = relationship.Target;

            if (current == source)
            {
                return true;
            }
        }

        return false;
    }

    private static unsafe int RelatedCountImpl<TTarget, TRelationship>(void* target)
        where TTarget : unmanaged, IComponent, IRelationshipTarget<TRelationship>
        where TRelationship : unmanaged, IComponent, IRelationship
    {
        return ((TTarget*)target)->RelatedCount;
    }

    private static unsafe EntityRef GetRelatedImpl<TTarget, TRelationship>(void* target, int index)
        where TTarget : unmanaged, IComponent, IRelationshipTarget<TRelationship>
        where TRelationship : unmanaged, IComponent, IRelationship
    {
        return ((TTarget*)target)->GetRelated(index);
    }

    private static unsafe EntityRef GetTargetImpl<TRelationship>(void* relationship)
        where TRelationship : unmanaged, IComponent, IRelationship
    {
        return ((TRelationship*)relationship)->Target;
    }

#endregion
}
