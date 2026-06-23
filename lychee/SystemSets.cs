using lychee.collections;
using Predicate = System.Func<lychee.ResourcePool, bool>;

namespace lychee;

public sealed class SetInfo(int typeId, string name) : IEquatable<SetInfo>
{
    public readonly int TypeId = typeId;

    public readonly string Name = name;

    public SetInfo? Parent;

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
    internal readonly Dictionary<SetInfo, bool> SetPredicateResultDict = [];

    private readonly Dictionary<int, string[]> enumTypeIdToValues = [];

    private readonly DirectedAcyclicGraph<SetInfo> orderGraph = [];

    private readonly Dictionary<SetInfo, Predicate> setPredicateDict = [];

    private readonly Dictionary<SetInfo, SetInfo> parentDict = [];

    public void AddSystemSet<T>() where T : Enum
    {
        var typeId = typeRegistrar.GetTypeId<T>();
        enumTypeIdToValues.TryAdd(typeId, typeof(T).GetEnumNames());
    }

    public void ConfigureSetOrder<TS1, TS2>(TS1 s1, Order order, TS2 s2) where TS1 : Enum where TS2 : Enum
    {
        var info1 = GetRegisteredSetInfo(s1);
        var info2 = GetRegisteredSetInfo(s2);

        var node1 = FindOrCreateNode(info1);
        var node2 = FindOrCreateNode(info2);

        if (order == Order.Before)
        {
            orderGraph.AddEdge(node1, node2);
        }
        else
        {
            orderGraph.AddEdge(node2, node1);
        }

        if (!orderGraph.Valid)
        {
            orderGraph.RemoveEdge(order == Order.Before ? node1 : node2, order == Order.Before ? node2 : node1);
            throw new InvalidOperationException($"Cannot order set '{info1.Name}' {order.ToString().ToLower()} '{info2.Name}': would create a cycle");
        }
    }

    public void ConfigureSetPredicate<T>(T set, Predicate predicate) where T : Enum
    {
        var info = GetRegisteredSetInfo(set);
        setPredicateDict[info] = predicate;
    }

    public void ConfigureSetInSet<TS1, TS2>(TS1 parent, TS2 child) where TS1 : Enum where TS2 : Enum
    {
        var info1 = GetRegisteredSetInfo(parent);
        var info2 = GetRegisteredSetInfo(child);

        parentDict[info2] = info1;
    }

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
        if (node == null) return [];

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
        if (node == null) return [];

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

#region Private Methods

    private SetInfo GetRegisteredSetInfo<T>(T set) where T : Enum
    {
        var typeId = typeRegistrar.GetTypeId<T>();

        if (!enumTypeIdToValues.ContainsKey(typeId))
        {
            throw new InvalidOperationException($"Set type '{typeof(T).Name}' has not been registered. Call AddSystemSet<{typeof(T).Name}>() first.");
        }

        var name = typeof(T).GetEnumName(set)!;
        return new(typeId, name);
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

#endregion
}
