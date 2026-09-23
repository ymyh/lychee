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

    /// <summary>
    /// Indexes the nodes of <see cref="orderGraph"/> by their set, so that a set configured more than once is
    /// found instead of searched for. Kept in step with the graph: nodes are only ever added by
    /// <see cref="FindOrCreateNode"/>.
    /// </summary>
    private readonly Dictionary<SetInfo, DAGNode<SetInfo>> orderNodeDict = [];

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
    /// The constraint is only recorded here: whether it can be satisfied is decided when a schedule is built,
    /// so that configuration never depends on the order in which constraints happen to be declared.
    /// </summary>
    /// <typeparam name="TS1">The enum type of the first set.</typeparam>
    /// <typeparam name="TS2">The enum type of the second set.</typeparam>
    /// <param name="s1">The first set.</param>
    /// <param name="order">Whether <paramref name="s1"/> runs before or after <paramref name="s2"/>.</param>
    /// <param name="s2">The second set.</param>
    /// <exception cref="InvalidOperationException">Thrown when either set type has not been registered.</exception>
    public void ConfigureSetOrder<TS1, TS2>(TS1 s1, Order order, TS2 s2) where TS1 : Enum where TS2 : Enum
    {
        var info1 = GetRegisteredSetInfo(s1);
        var info2 = GetRegisteredSetInfo(s2);

        var node1 = FindOrCreateNode(info1);
        var node2 = FindOrCreateNode(info2);

        var (from, to) = order == Order.Before ? (node1, node2) : (node2, node1);

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
    /// <param name="predicate">The predicate, evaluated once per frame against the resource pool, and shared by
    /// every schedule that frame runs.</param>
    /// <exception cref="InvalidOperationException">Thrown when the set type has not been registered.</exception>
    public void ConfigureSetPredicate<T>(T set, Predicate predicate) where T : Enum
    {
        var info = GetRegisteredSetInfo(set);
        setPredicateDict[info] = predicate;
    }

    /// <summary>
    /// Nests one set inside another: the child inherits the parent's predicate, and constraints applied to the
    /// parent also cover the child. A set has at most one parent.
    /// Like set ordering, nesting is only recorded here and checked when a schedule is built.
    /// </summary>
    /// <typeparam name="TS1">The enum type of the parent set.</typeparam>
    /// <typeparam name="TS2">The enum type of the child set.</typeparam>
    /// <param name="parent">The set that contains <paramref name="child"/>.</param>
    /// <param name="child">The set contained in <paramref name="parent"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown when either set type has not been registered.</exception>
    public void ConfigureSetInSet<TS1, TS2>(TS1 parent, TS2 child) where TS1 : Enum where TS2 : Enum
    {
        var parentInfo = GetRegisteredSetInfo(parent);
        var childInfo = GetRegisteredSetInfo(child);

        if (parentDict.GetValueOrDefault(childInfo)?.Equals(parentInfo) == true)
        {
            return;
        }

        parentDict[childInfo] = parentInfo;
        Version++;
    }

    /// <summary>
    /// Evaluates every configured set predicate against the resource pool and caches the result, so the
    /// execution path can read them without invoking user code.
    /// A caller that is about to run only part of its systems can ask for less with
    /// <see cref="ComputePredicates(Enum[])"/>.
    /// </summary>
    public void ComputeAllPredicates()
    {
        foreach (var (info, func) in setPredicateDict)
        {
            SetPredicateResultDict[info] = func(resourcePool);
        }
    }

    /// <summary>
    /// Evaluates the predicates of the given sets and caches the results, so the execution path can read them
    /// without invoking user code. Ancestors are included, so a child set is evaluated with its parent's
    /// predicate even when only the child was named, and a set without a predicate is simply left out.
    /// </summary>
    /// <param name="sets">The sets to evaluate, together with their ancestors.</param>
    /// <exception cref="InvalidOperationException">Thrown when a set type has not been registered.</exception>
    public void ComputePredicates(Enum[] sets)
    {
        ComputePredicates(GetEffectiveSystemSets(sets));
    }

    /// <summary>
    /// Gets the parent set of the specified set, if any.
    /// </summary>
    public SetInfo? GetParent(SetInfo set)
    {
        return parentDict.GetValueOrDefault(set);
    }

#endregion

#region Internal Methods

    /// <summary>
    /// Evaluates the predicates of the given sets and caches the results. This is the resolved form of
    /// <see cref="ComputePredicates(Enum[])"/>: the sets arrive already expanded to include their ancestors, so
    /// nothing has to be resolved again, which is what a schedule wants when it asks for exactly the sets its
    /// own systems belong to. A set without a predicate is left out.
    /// </summary>
    /// <param name="sets">The sets to evaluate, resolved to set identities.</param>
    internal void ComputePredicates(SetInfo[] sets)
    {
        foreach (var set in sets)
        {
            if (setPredicateDict.TryGetValue(set, out var func))
            {
                SetPredicateResultDict[set] = func(resourcePool);
            }
        }
    }

    /// <summary>
    /// Checks the recorded constraints for contradictions. Configuration only records, so this is where an
    /// impossible combination is reported, once, when a schedule resolves its graph.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the recorded ordering contains a cycle, or when the nesting does.
    /// </exception>
    internal void Validate()
    {
        ThrowIfOrderContainsCycle();
        ThrowIfNestingContainsCycle();
    }

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

    /// <summary>
    /// Expands the sets declared on a system descriptor into the sets the system really belongs to: every
    /// declared set plus its ancestors, so a system in a child set also answers to the parent's ordering and
    /// predicate.
    /// </summary>
    /// <param name="directSets">The sets declared on a system descriptor.</param>
    /// <returns>The declared sets together with their ancestors.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a declared set type has not been registered. Without this check the set would silently get
    /// an identity that matches nothing, and every constraint on it would be dropped.
    /// </exception>
    internal SetInfo[] GetEffectiveSystemSets(Enum[] directSets)
    {
        var allSet = new HashSet<SetInfo>();

        foreach (var set in directSets)
        {
            var type = set.GetType();

            if (!IsRegistered(type))
            {
                throw new InvalidOperationException($"Set type '{type.Name}' has not been registered. Call AddSystemSet<{type.Name}>() first.");
            }

            // Nesting keeps a single parent, so the ancestry is a chain and walking upwards is enough.
            var current = new SetInfo(typeRegistrar.GetTypeId(type), GetSetName(set));

            while (current != null)
            {
                if (!allSet.Add(current))
                {
                    break;
                }

                current = parentDict.GetValueOrDefault(current);
            }
        }

        return [.. allSet];
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
    private static string GetSetName(Enum set)
    {
        return set.ToString();
    }

    private DAGNode<SetInfo> FindOrCreateNode(SetInfo info)
    {
        if (orderNodeDict.TryGetValue(info, out var existing))
        {
            return existing;
        }

        var node = new DAGNode<SetInfo>(info);
        orderGraph.AddNode(node);
        orderNodeDict[info] = node;

        return node;
    }

    /// <summary>
    /// Throws when the recorded set ordering cannot be linearised. Ordering chains that are independent from
    /// each other are legitimate, so this is a cycle test on the graph and not a "single root" test.
    /// </summary>
    private void ThrowIfOrderContainsCycle()
    {
        var inDegreeDict = new Dictionary<DAGNode<SetInfo>, int>(orderGraph.Count);
        var pendingQueue = new Queue<DAGNode<SetInfo>>();

        foreach (var node in orderGraph.Nodes)
        {
            var inDegree = node.Parents.Count;
            inDegreeDict[node] = inDegree;

            if (inDegree == 0)
            {
                pendingQueue.Enqueue(node);
            }
        }

        var visitedCount = 0;

        while (pendingQueue.Count > 0)
        {
            foreach (var child in pendingQueue.Dequeue().Children)
            {
                if (--inDegreeDict[child] == 0)
                {
                    pendingQueue.Enqueue(child);
                }
            }

            visitedCount++;
        }

        if (visitedCount != orderGraph.Count)
        {
            var cycleNames = orderGraph.Nodes.Where(node => inDegreeDict[node] > 0).Select(node => node.Data.Name);

            throw new InvalidOperationException($"Set ordering contains a cycle: {string.Join(" -> ", cycleNames)}");
        }
    }

    /// <summary>
    /// Throws when the recorded nesting cannot be walked. Nesting keeps a single parent, so following the
    /// parents upwards from every known set either ends or comes back to where it started.
    /// </summary>
    private void ThrowIfNestingContainsCycle()
    {
        // Nesting gives a set at most one parent, so walking upwards from a set is a single path and needs no
        // stack: seeing a set twice on the same path is the whole test.
        // A set already proven to reach an end cannot be part of a cycle, so reaching one ends the walk early
        // and everything below it is safe too. That is what keeps this linear in the number of sets.
        var safeSet = new HashSet<SetInfo>();
        var pathList = new List<SetInfo>();
        var pathSet = new HashSet<SetInfo>();

        foreach (var child in parentDict.Keys)
        {
            pathList.Clear();
            pathSet.Clear();

            var current = child;

            while (current != null && !safeSet.Contains(current))
            {
                pathList.Add(current);

                if (!pathSet.Add(current))
                {
                    throw new InvalidOperationException($"Set nesting contains a cycle: {string.Join(" -> ", pathList.Select(set => set.Name))}");
                }

                current = parentDict.GetValueOrDefault(current);
            }

            foreach (var set in pathList)
            {
                safeSet.Add(set);
            }
        }
    }

#endregion
}
