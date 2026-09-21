using lychee.collections;
using Predicate = System.Func<lychee.ResourcePool, bool>;

namespace lychee;

public sealed class SetInfo(int typeId, string name) : IEquatable<SetInfo>
{
    public readonly int TypeId = typeId;

    public readonly string Name = name;

    public override int GetHashCode()
    {
        return HashCode.Combine(TypeId.GetHashCode(), Name.GetHashCode());
    }

    public override bool Equals(object? obj)
    {
        return obj is SetInfo setInfo && setInfo.TypeId == TypeId &&  setInfo.Name == Name;
    }

#region IEquatable Implementation

    public bool Equals(SetInfo? other)
    {
        return other != null && other.TypeId == TypeId &&  other.Name == Name;
    }

#endregion
}

public sealed class SystemSets(TypeRegistrar typeRegistrar, ResourcePool resourcePool)
{
#region Internal Fields

    internal readonly Dictionary<SetInfo, bool> SetPredicateResultDict = [];

#endregion

#region Internal Properties

    /// <summary>
    /// Increases whenever a configuration that changes the structure of the set graph is applied, so a consumer
    /// holding a previously resolved result can tell that it is stale. Predicate configuration is not counted:
    /// it never affects the graph.
    /// </summary>
    internal int Version { get; private set; }

    /// <summary>
    /// Enumerates the configured set order constraints as <c>(Before, After)</c> pairs, which is the input the
    /// schedule needs to expand set ordering into system ordering.
    /// </summary>
    internal IEnumerable<(SetInfo Before, SetInfo After)> OrderEdges => orderGraph.Edges.Select(edge => (edge.From.Data, edge.To.Data));

#endregion

#region Private Fields

    private readonly HashSet<int> registeredTypeIdSet = [];

    private readonly DirectedAcyclicGraph<SetInfo> orderGraph = [];

    private readonly Dictionary<SetInfo, Predicate> setPredicateDict = [];

    private readonly Dictionary<SetInfo, SetInfo> parentDict = [];

#endregion

#region Public Methods

    /// <summary>
    /// Registers an enum type as a set dimension. Every value of the enum becomes usable as a set in
    /// <see cref="ConfigureSetOrder{TS1, TS2}"/>, <see cref="ConfigureSetInSet{TS1, TS2}"/> and
    /// <see cref="ConfigureSetPredicate{T}"/>. Registering the same type again has no effect.
    /// </summary>
    /// <typeparam name="T">The enum type whose values identify sets.</typeparam>
    public void AddSystemSet<T>() where T : Enum
    {
        var typeId = typeRegistrar.RegisterEnum<T>();

        if (registeredTypeIdSet.Add(typeId))
        {
            Version++;
        }
    }

    /// <summary>
    /// Constrains every system in one set to run before (or after) every system in another set.
    /// Declaring the same constraint again has no effect.
    /// </summary>
    /// <typeparam name="TS1">The enum type of the first set.</typeparam>
    /// <typeparam name="TS2">The enum type of the second set.</typeparam>
    /// <param name="s1">The first set.</param>
    /// <param name="order">Whether <paramref name="s1"/> runs before or after <paramref name="s2"/>.</param>
    /// <param name="s2">The second set.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either set type has not been registered, or when the constraint would create a cycle.
    /// </exception>
    public void ConfigureSetOrder<TS1, TS2>(TS1 s1, Order order, TS2 s2) where TS1 : Enum where TS2 : Enum
    {
        var info1 = GetRegisteredSetInfo(s1);
        var info2 = GetRegisteredSetInfo(s2);

        var node1 = FindOrCreateNode(info1);
        var node2 = FindOrCreateNode(info2);

        var (from, to) = order == Order.Before ? (node1, node2) : (node2, node1);

        // Independent ordering chains are legitimate, so a cycle is only a path that already leads back
        // from `to` to `from`. Checking the root count instead would reject unrelated constraints.
        if (HasPath(to, from))
        {
            throw new InvalidOperationException($"Cannot order set '{info1.Name}' {order.ToString().ToLower()} '{info2.Name}': would create a cycle");
        }

        if (orderGraph.TryAddEdge(from, to))
        {
            Version++;
        }
    }

    /// <summary>
    /// Attaches a predicate to a set. Systems belonging to the set (or to any of its children) only run while
    /// the predicate returns true.
    /// </summary>
    /// <typeparam name="T">The enum type of the set.</typeparam>
    /// <param name="set">The set to attach the predicate to.</param>
    /// <param name="predicate">The predicate, evaluated once per execution against the resource pool.</param>
    /// <exception cref="InvalidOperationException">Thrown when the set type has not been registered.</exception>
    public void ConfigureSetPredicate<T>(T set, Predicate predicate) where T : Enum
    {
        var info = GetRegisteredSetInfo(set);
        setPredicateDict[info] = predicate;
    }

    /// <summary>
    /// Nests one set inside another: the child inherits the parent's predicate, and constraints applied to the
    /// parent also cover the child. A set has at most one parent.
    /// </summary>
    /// <typeparam name="TS1">The enum type of the parent set.</typeparam>
    /// <typeparam name="TS2">The enum type of the child set.</typeparam>
    /// <param name="parent">The set that contains <paramref name="child"/>.</param>
    /// <param name="child">The set contained in <paramref name="parent"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either set type has not been registered, or when nesting would create a cycle.
    /// </exception>
    public void ConfigureSetInSet<TS1, TS2>(TS1 parent, TS2 child) where TS1 : Enum where TS2 : Enum
    {
        var parentInfo = GetRegisteredSetInfo(parent);
        var childInfo = GetRegisteredSetInfo(child);

        if (parentDict.GetValueOrDefault(childInfo)?.Equals(parentInfo) == true)
        {
            return;
        }

        ThrowIfNestingWouldCreateCycle(parentInfo, childInfo);

        parentDict[childInfo] = parentInfo;
        Version++;
    }

    /// <summary>
    /// Evaluates every configured set predicate against the resource pool and caches the result, so the
    /// execution path can read them without invoking user code.
    /// </summary>
    public void ComputeAllPredicates()
    {
        foreach (var (info, func) in setPredicateDict)
        {
            SetPredicateResultDict[info] = func(resourcePool);
        }
    }

    /// <summary>
    /// Gets the parent set of the specified set, if any.
    /// </summary>
    public SetInfo? GetParent(SetInfo set)
    {
        return parentDict.GetValueOrDefault(set);
    }

    /// <summary>
    /// Gets all sets that should execute before the specified set (transitive closure of orderGraph ancestors).
    /// </summary>
    public HashSet<SetInfo> GetSetsBefore(SetInfo set)
    {
        var node = orderGraph.FirstOrDefault(n => n.Data.Equals(set));
        if (node == null)
        {
            return [];
        }

        var result = new HashSet<SetInfo>();
        var queue = new Queue<DAGNode<SetInfo>>();

        foreach (var parent in node.Parents)
        {
            queue.Enqueue(parent);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (result.Add(current.Data))
            {
                foreach (var parent in current.Parents)
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all sets that should execute after the specified set (transitive closure of orderGraph descendants).
    /// </summary>
    public HashSet<SetInfo> GetSetsAfter(SetInfo set)
    {
        var node = orderGraph.FirstOrDefault(n => n.Data.Equals(set));
        if (node == null)
        {
            return [];
        }

        var result = new HashSet<SetInfo>();
        var queue = new Queue<DAGNode<SetInfo>>();

        foreach (var child in node.Children)
        {
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (result.Add(current.Data))
            {
                foreach (var child in current.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

#endregion

#region Internal Methods

    /// <summary>
    /// Returns whether the given enum type has been registered through <see cref="AddSystemSet{T}"/>.
    /// Sets backed by an unregistered type have no usable identity and must be rejected by consumers.
    /// </summary>
    /// <param name="enumType">The enum type backing a set.</param>
    /// <returns>True when the type is registered; otherwise, false.</returns>
    internal bool IsRegistered(Type enumType)
    {
        return registeredTypeIdSet.Contains(typeRegistrar.GetTypeId(enumType));
    }

#endregion

#region Private Methods

    private SetInfo GetRegisteredSetInfo<T>(T set) where T : Enum
    {
        var typeId = typeRegistrar.GetTypeId<T>();

        if (!registeredTypeIdSet.Contains(typeId))
        {
            throw new InvalidOperationException($"Set type '{typeof(T).Name}' has not been registered. Call AddSystemSet<{typeof(T).Name}>() first.");
        }

        return new(typeId, GetSetName(set));
    }

    /// <summary>
    /// Gets the name that identifies a set. Flag combinations and values without a declared member have no name
    /// of their own, and fall back to their string form rather than leaving the name null.
    /// </summary>
    private static string GetSetName<T>(T set) where T : Enum
    {
        return typeof(T).GetEnumName(set) ?? set.ToString();
    }

    private DAGNode<SetInfo> FindOrCreateNode(SetInfo info)
    {
        var existing = orderGraph.FirstOrDefault(n => n.Data.Equals(info));
        if (existing != null)
        {
            return existing;
        }

        var node = new DAGNode<SetInfo>(info);
        orderGraph.AddNode(node);

        return node;
    }

    /// <summary>
    /// Returns whether <paramref name="to"/> is reachable from <paramref name="from"/> by following children.
    /// </summary>
    private static bool HasPath(DAGNode<SetInfo> from, DAGNode<SetInfo> to)
    {
        if (from == to)
        {
            return true;
        }

        var pendingStack = new Stack<DAGNode<SetInfo>>();
        var visitedSet = new HashSet<DAGNode<SetInfo>>();

        pendingStack.Push(from);
        visitedSet.Add(from);

        while (pendingStack.Count > 0)
        {
            foreach (var child in pendingStack.Pop().Children)
            {
                if (child == to)
                {
                    return true;
                }

                if (visitedSet.Add(child))
                {
                    pendingStack.Push(child);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Throws when making <paramref name="parentInfo"/> the parent of <paramref name="childInfo"/> would close a
    /// cycle. Single-parent nesting makes the ancestry a chain, so following the parents upwards from the new
    /// parent is enough to prove there is none.
    /// </summary>
    private void ThrowIfNestingWouldCreateCycle(SetInfo parentInfo, SetInfo childInfo)
    {
        var chain = new List<string> { childInfo.Name };
        var current = parentInfo;

        while (true)
        {
            chain.Add(current.Name);

            if (current.Equals(childInfo))
            {
                throw new InvalidOperationException($"Cannot put set '{childInfo.Name}' in '{parentInfo.Name}': would create a cycle ({string.Join(" -> ", chain)})");
            }

            var grandParent = parentDict.GetValueOrDefault(current);

            if (grandParent == null)
            {
                return;
            }

            current = grandParent;
        }
    }

#endregion
}
