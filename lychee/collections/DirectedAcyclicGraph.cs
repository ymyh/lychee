namespace lychee.collections;

public sealed class DAGNode<T>
{
    public T Data { get; set; } = default!;

    public List<DAGNode<T>> Children { get; } = [];
}

public sealed class DirectedAcyclicGraph<T>
{
    public bool IsValid = false;
    public List<DAGNode<T>> Nodes { get; } = [];

    public void AddNode(DAGNode<T> node)
    {
        Nodes.Add(node);
    }

    public void AddEdge(DAGNode<T> from, DAGNode<T> to)
    {
        if (Nodes.Contains(from) && Nodes.Contains(to))
        {
            from.Children.Add(to);
        }
        else
        {
            throw new ArgumentException("Can't add edge because at lease one of the nodes is not in the graph");
        }
    }

    public bool Validate()
    {
        var visited = new Dictionary<DAGNode<T>, int>();

        foreach (var node in Nodes)
        {
            if (!visited.ContainsKey(node))
            {
                if (HasCycle(node, visited))
                {
                    IsValid = false;
                    return false;
                }
            }
        }

        IsValid = true;
        return true;
    }

    private static bool HasCycle(DAGNode<T> node, Dictionary<DAGNode<T>, int> visited)
    {
        visited[node] = 1;

        foreach (var child in node.Children)
        {
            if (!visited.TryGetValue(child, out var value))
            {
                if (HasCycle(child, visited))
                {
                    return true;
                }
            }
            else if (value == 1)
            {
                return true;
            }
        }

        visited[node] = 2;
        return false;
    }
}
