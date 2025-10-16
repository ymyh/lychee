using System.Data;

namespace lychee.collections;

/// <summary>
/// Represents a node in DAG
/// </summary>
/// <param name="data"></param>
/// <typeparam name="T"></typeparam>
public sealed class DAGNode<T>(T data)
{
    public DAGNode() : this(default!)
    {
    }

    public T Data { get; set; } = data;

    public List<DAGNode<T>> Parents { get; } = [];

    public List<DAGNode<T>> Children { get; } = [];

    internal int Group { get; set; }
}

/// <summary>
/// Frozen DAG node, for better performance
/// </summary>
/// <param name="node">The node to freeze</param>
/// <typeparam name="T"></typeparam>
public struct FrozenDAGNode<T>(DAGNode<T> node)
{
    public T Data = node.Data;

    /// <summary>
    /// Nodes in same group means they are irrelative to each other
    /// </summary>
    public readonly int Group = node.Group;
}

public sealed class DirectedAcyclicGraph<T>
{
    public List<DAGNode<T>> Nodes { get; } = [];

    public int Count => Nodes.Count;

    /// <summary>
    /// Add a node into graph without connecting to other node(s)
    /// </summary>
    /// <param name="node">The node to add</param>
    /// <returns>The added node</returns>
    public DAGNode<T> AddNode(DAGNode<T> node)
    {
        Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Add an edge between two nodes
    /// </summary>
    /// <param name="from">The node to add edge from</param>
    /// <param name="to">The node to add edge to</param>
    /// <exception cref="ArgumentException">
    /// If <b>from</b> or <b>to</b> is not in the graph <br/>
    /// If <b>from</b> is the same as <b>to</b> <br/>
    /// If <b>from</b> already has a child <b>to</b>
    /// </exception>
    public void AddEdge(DAGNode<T> from, DAGNode<T> to)
    {
        if (from == to)
        {
            throw new ArgumentException("Can't add edge because `from` is the same as `to`");
        }

        if (Nodes.Contains(from) && Nodes.Contains(to))
        {
            if (from.Children.Contains(to))
            {
                throw new ArgumentException("Can't add edge because `from` already has a child `to`");
            }

            from.Children.Add(to);
            to.Parents.Add(from);
        }
        else
        {
            throw new ArgumentException("Can't add edge because at lease one of the nodes is not in the graph");
        }
    }

    /// <summary>
    /// Perform topological sorting to make the graph as a list. If you want a more efficient structure, see <see cref="DirectedAcyclicGraphExtensions.Freeze{T}"/>
    /// </summary>
    /// <returns></returns>
    /// <exception cref="ConstraintException">If graph contains a cycle</exception>
    public List<DAGNode<T>> AsList()
    {
        var inDegree = Nodes.ToDictionary(node => node, node => node.Parents.Count);
        var queue = new Queue<DAGNode<T>>(Nodes.Where(n => inDegree[n] == 0));
        var result = new List<DAGNode<T>>(Nodes.Count);
        var currentBatch = 0;

        while (queue.Count > 0)
        {
            var batchSize = queue.Count;
            for (var i = 0; i < batchSize; i++)
            {
                var node = queue.Dequeue();
                node.Group = currentBatch;
                result.Add(node);

                foreach (var child in node.Children)
                {
                    inDegree[child]--;
                    if (inDegree[child] == 0)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            currentBatch++;
        }

        if (result.Count != Nodes.Count)
        {
            throw new ConstraintException("Graph contains a cycle!");
        }

        return result;
    }

    public void ForEach(Action<DAGNode<T>> action)
    {
        ForEachInner(Nodes, action);
        return;

        static void ForEachInner(List<DAGNode<T>> nodes, Action<DAGNode<T>> action)
        {
            foreach (var node in nodes)
            {
                action(node);
                ForEachInner(node.Children, action);
            }
        }
    }
}

public static class DirectedAcyclicGraphExtensions
{
    extension<T>(IEnumerable<DAGNode<T>> nodes)
    {
        public IEnumerable<FrozenDAGNode<T>> Freeze()
        {
            return nodes.Select(x => new FrozenDAGNode<T>(x));
        }
    }

    extension<T>(IEnumerable<FrozenDAGNode<T>> nodes)
    {
        public FrozenDAGNode<T>[][] AsExecutionGroup()
        {
            return nodes.GroupBy(x => new { x.Group }).Select(x => x.ToArray()).ToArray();
        }
    }
}