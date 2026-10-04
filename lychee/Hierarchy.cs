using lychee.collections;

namespace lychee;

/// <summary>
/// The kind of buffered hierarchy operation recorded by Commands and applied at commit time.
/// </summary>
internal enum HierarchyOpKind
{
    AddChild,
    RemoveChild,
    Despawn,
    DetachAllChildren,
}

/// <summary>
/// A buffered hierarchy operation. For <see cref="HierarchyOpKind.RemoveChild"/> and
/// <see cref="HierarchyOpKind.Despawn"/> only <see cref="Subject"/> is meaningful; for
/// <see cref="HierarchyOpKind.DetachAllChildren"/> <see cref="Subject"/> is the parent entity.
/// </summary>
internal readonly struct HierarchyOp(HierarchyOpKind kind, EntityRef subject, EntityRef parent)
{
    public readonly HierarchyOpKind Kind = kind;

    public readonly EntityRef Subject = subject;

    public readonly EntityRef Parent = parent;
}

/// <summary>
/// Maintains the parent-child hierarchy index of a world.
/// <see cref="lychee.components.ChildOf"/> is the single source of truth; this index is the
/// automatically maintained reverse lookup (equivalent to Bevy's <c>Children</c>).
/// All writes are buffered by <see cref="Commands"/> and applied here at commit time on a single
/// thread, so systems may read freely during execution.
/// </summary>
/// <remarks>
/// Children of a parent are iterated in attachment order. Re-parenting moves a child to the end
/// of its new parent's child list. Cycles are not detected (same as Bevy); iterators do not
/// guarantee termination on cyclic graphs.
/// </remarks>
public sealed class Hierarchy
{
    private struct HierarchyNode
    {
        // Own reference including generation, for iterators to yield.
        internal EntityRef Self;

        // Only meaningful when HasParent is true. The default EntityRef (0, 0) is a valid
        // entity and cannot serve as a sentinel.
        internal EntityRef Parent;

        internal bool HasParent;

        // Head and tail of the child list (tail insertion keeps iteration order equal to
        // attachment order). -1 means none.
        internal int FirstChildId;

        internal int LastChildId;

        // Sibling links within the parent's child list. -1 means none.
        internal int PrevSiblingId;

        internal int NextSiblingId;

        internal int ChildCount;
    }

    // Keyed by entity ID. Only entities that have a parent or have children get an entry.
    private readonly SparseMap<HierarchyNode> nodeMap = [];

    // Reused buffers for cascade collection, avoiding per-despawn allocations.
    private readonly HashSet<int> collectVisitedSet = [];

    private readonly Stack<int> collectStack = [];

#region Public Methods

    /// <summary>
    /// Tries to get the parent of the given entity.
    /// </summary>
    /// <param name="child">The child entity reference.</param>
    /// <param name="parent">When this method returns, contains the parent if found; otherwise, the default value.</param>
    /// <returns>True if the entity has a parent; otherwise, false.</returns>
    public bool TryGetParent(EntityRef child, out EntityRef parent)
    {
        if (nodeMap.TryGetValue(child.ID, out var node) && node.HasParent)
        {
            parent = node.Parent;
            return true;
        }

        parent = default;
        return false;
    }

    /// <summary>
    /// Gets the number of direct children of the given entity.
    /// </summary>
    /// <param name="parent">The parent entity reference.</param>
    /// <returns>The number of direct children; 0 if the entity has no children.</returns>
    public int GetChildCount(EntityRef parent)
    {
        return nodeMap.TryGetValue(parent.ID, out var node) ? node.ChildCount : 0;
    }

    /// <summary>
    /// Iterates over the direct children of the given entity in attachment order.
    /// </summary>
    /// <param name="parent">The parent entity reference.</param>
    /// <returns>An enumerable of child entity references.</returns>
    public IEnumerable<EntityRef> IterChildren(EntityRef parent)
    {
        if (!nodeMap.TryGetValue(parent.ID, out var node))
        {
            yield break;
        }

        for (var childId = node.FirstChildId; childId != -1;)
        {
            if (!nodeMap.TryGetValue(childId, out var childNode))
            {
                yield break;
            }

            yield return childNode.Self;
            childId = childNode.NextSiblingId;
        }
    }

    /// <summary>
    /// Performs the specified action on each direct child of the given entity in attachment order.
    /// Allocation-free alternative to <see cref="IterChildren"/> for hot paths.
    /// </summary>
    /// <param name="parent">The parent entity reference.</param>
    /// <param name="action">The action to perform on each child.</param>
    public void ForEachChild(EntityRef parent, Action<EntityRef> action)
    {
        if (!nodeMap.TryGetValue(parent.ID, out var node))
        {
            return;
        }

        for (var childId = node.FirstChildId; childId != -1;)
        {
            if (!nodeMap.TryGetValue(childId, out var childNode))
            {
                return;
            }

            action(childNode.Self);
            childId = childNode.NextSiblingId;
        }
    }

    /// <summary>
    /// Iterates over every entity that has at least one child.
    /// </summary>
    /// <returns>An enumerable of parent entity references.</returns>
    public IEnumerable<EntityRef> IterParents()
    {
        foreach (var (_, node) in nodeMap)
        {
            if (node.ChildCount > 0)
            {
                yield return node.Self;
            }
        }
    }

    /// <summary>
    /// Iterates over all descendants of the given entity in depth-first pre-order.
    /// Does not guarantee termination on cyclic graphs (cycles are not supported).
    /// </summary>
    /// <param name="root">The root entity reference.</param>
    /// <returns>An enumerable of descendant entity references.</returns>
    public IEnumerable<EntityRef> IterDescendants(EntityRef root)
    {
        if (!nodeMap.TryGetValue(root.ID, out var rootNode) || rootNode.FirstChildId == -1)
        {
            yield break;
        }

        var stack = new Stack<int>();
        stack.Push(rootNode.FirstChildId);

        while (stack.Count > 0)
        {
            if (!nodeMap.TryGetValue(stack.Pop(), out var node))
            {
                continue;
            }

            yield return node.Self;

            // Push the next sibling before the first child so that children are visited
            // before siblings, in attachment order within each level.
            if (node.NextSiblingId != -1)
            {
                stack.Push(node.NextSiblingId);
            }

            if (node.FirstChildId != -1)
            {
                stack.Push(node.FirstChildId);
            }
        }
    }

    /// <summary>
    /// Iterates over all ancestors of the given entity, from the direct parent up to the root.
    /// Does not guarantee termination on cyclic graphs (cycles are not supported).
    /// </summary>
    /// <param name="node">The entity reference to start from.</param>
    /// <returns>An enumerable of ancestor entity references.</returns>
    public IEnumerable<EntityRef> IterAncestors(EntityRef node)
    {
        var current = node;

        while (nodeMap.TryGetValue(current.ID, out var currentNode) && currentNode.HasParent)
        {
            yield return currentNode.Parent;
            current = currentNode.Parent;
        }
    }

    /// <summary>
    /// Gets the root ancestor of the given entity, or the entity itself if it has no parent.
    /// </summary>
    /// <param name="node">The entity reference to start from.</param>
    /// <returns>The topmost ancestor entity reference.</returns>
    public EntityRef RootAncestor(EntityRef node)
    {
        var current = node;

        while (nodeMap.TryGetValue(current.ID, out var currentNode) && currentNode.HasParent)
        {
            current = currentNode.Parent;
        }

        return current;
    }

#endregion

#region Internal Methods

    /// <summary>
    /// Attaches the child to the parent's child list. If the child already has a parent,
    /// it is detached from the old parent first (re-parenting).
    /// </summary>
    internal void AddChild(EntityRef parent, EntityRef child)
    {
        var parentNode = GetOrCreateNode(parent);
        var childNode = GetOrCreateNode(child);

        if (childNode.HasParent)
        {
            if (childNode.Parent.ID == parent.ID)
            {
                return;
            }

            DetachFromParent(ref childNode);
        }

        // Tail insertion keeps iteration order equal to attachment order.
        childNode.PrevSiblingId = parentNode.LastChildId;
        childNode.NextSiblingId = -1;

        if (parentNode.LastChildId != -1)
        {
            var lastChildNode = nodeMap[parentNode.LastChildId];
            lastChildNode.NextSiblingId = child.ID;
            nodeMap[parentNode.LastChildId] = lastChildNode;
        }
        else
        {
            parentNode.FirstChildId = child.ID;
        }

        parentNode.LastChildId = child.ID;
        parentNode.ChildCount++;

        childNode.Parent = parent;
        childNode.HasParent = true;

        nodeMap[parent.ID] = parentNode;
        nodeMap[child.ID] = childNode;
    }

    /// <summary>
    /// Detaches the child from its current parent. The child's own subtree is preserved.
    /// Tolerates entities that have no parent.
    /// </summary>
    internal void RemoveChild(EntityRef child)
    {
        if (!nodeMap.TryGetValue(child.ID, out var childNode))
        {
            return;
        }

        DetachFromParent(ref childNode);

        if (childNode.ChildCount == 0)
        {
            nodeMap.Remove(child.ID);
        }
        else
        {
            nodeMap[child.ID] = childNode;
        }
    }

    /// <summary>
    /// Detaches all direct children of the parent, turning them into roots.
    /// Grandchildren keep their own parents. Tolerates parents without children.
    /// </summary>
    internal void DetachAllChildren(EntityRef parent)
    {
        if (!nodeMap.TryGetValue(parent.ID, out var parentNode))
        {
            return;
        }

        for (var childId = parentNode.FirstChildId; childId != -1;)
        {
            if (!nodeMap.TryGetValue(childId, out var childNode))
            {
                break;
            }

            childId = childNode.NextSiblingId;

            childNode.HasParent = false;
            childNode.PrevSiblingId = -1;
            childNode.NextSiblingId = -1;

            if (childNode.ChildCount == 0)
            {
                nodeMap.Remove(childNode.Self.ID);
            }
            else
            {
                nodeMap[childNode.Self.ID] = childNode;
            }
        }

        parentNode.FirstChildId = -1;
        parentNode.LastChildId = -1;
        parentNode.ChildCount = 0;

        if (!parentNode.HasParent)
        {
            nodeMap.Remove(parent.ID);
        }
        else
        {
            nodeMap[parent.ID] = parentNode;
        }
    }

    /// <summary>
    /// Removes the entity from the index: detaches it from its parent and drops its node.
    /// Used for the root of a cascade despawn; callers must ensure its descendants are
    /// removed separately via <see cref="RemoveNodeOnly"/>.
    /// </summary>
    internal void RemoveFromIndex(EntityRef entity)
    {
        if (!nodeMap.TryGetValue(entity.ID, out var node))
        {
            return;
        }

        DetachFromParent(ref node);
        nodeMap.Remove(entity.ID);
    }

    /// <summary>
    /// Drops the entity's node without fixing any links. Only valid when every node linking
    /// to it is being removed as well (i.e. descendants of a cascade despawn).
    /// </summary>
    internal void RemoveNodeOnly(EntityRef entity)
    {
        nodeMap.Remove(entity.ID);
    }

    /// <summary>
    /// Collects the root and all its descendants into the results list. Protected against
    /// cycles by a visited set. The results list is not cleared by this method.
    /// </summary>
    internal void CollectDescendants(EntityRef root, List<EntityRef> results)
    {
        collectVisitedSet.Clear();
        collectStack.Clear();
        collectStack.Push(root.ID);

        while (collectStack.Count > 0)
        {
            var id = collectStack.Pop();

            if (!collectVisitedSet.Add(id))
            {
                continue;
            }

            if (!nodeMap.TryGetValue(id, out var node))
            {
                // The root may not participate in the hierarchy at all; it still needs
                // to be in the results so the caller processes it.
                if (id == root.ID)
                {
                    results.Add(root);
                }

                continue;
            }

            results.Add(node.Self);

            for (var childId = node.FirstChildId; childId != -1;)
            {
                collectStack.Push(childId);

                if (!nodeMap.TryGetValue(childId, out var childNode))
                {
                    break;
                }

                childId = childNode.NextSiblingId;
            }
        }
    }

    /// <summary>
    /// Removes all hierarchy data.
    /// </summary>
    internal void Clear()
    {
        nodeMap.Clear();
    }

#endregion

#region Private Methods

    private HierarchyNode GetOrCreateNode(EntityRef entityRef)
    {
        return nodeMap.TryGetValue(entityRef.ID, out var node)
            ? node
            : new HierarchyNode
            {
                Self = entityRef,
                FirstChildId = -1,
                LastChildId = -1,
                PrevSiblingId = -1,
                NextSiblingId = -1,
            };
    }

    // Unlinks the child from its parent's sibling list and clears its parent linkage.
    // The child node itself is kept; the caller decides whether to keep or drop it.
    private void DetachFromParent(ref HierarchyNode childNode)
    {
        if (!childNode.HasParent)
        {
            return;
        }

        if (nodeMap.TryGetValue(childNode.Parent.ID, out var parentNode))
        {
            if (childNode.PrevSiblingId != -1 && nodeMap.TryGetValue(childNode.PrevSiblingId, out var prevNode))
            {
                prevNode.NextSiblingId = childNode.NextSiblingId;
                nodeMap[childNode.PrevSiblingId] = prevNode;
            }
            else
            {
                parentNode.FirstChildId = childNode.NextSiblingId;
            }

            if (childNode.NextSiblingId != -1 && nodeMap.TryGetValue(childNode.NextSiblingId, out var nextNode))
            {
                nextNode.PrevSiblingId = childNode.PrevSiblingId;
                nodeMap[childNode.NextSiblingId] = nextNode;
            }
            else
            {
                parentNode.LastChildId = childNode.PrevSiblingId;
            }

            parentNode.ChildCount--;
            nodeMap[childNode.Parent.ID] = parentNode;

            // Keep the dense array compact: drop parent entries that no longer participate.
            if (parentNode is { ChildCount: 0, HasParent: false })
            {
                nodeMap.Remove(childNode.Parent.ID);
            }
        }

        childNode.HasParent = false;
        childNode.PrevSiblingId = -1;
        childNode.NextSiblingId = -1;
    }

#endregion
}
