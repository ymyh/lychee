using System.Data;

namespace lychee.collections;

public sealed class DAGNode<T>(T data)
{
    public DAGNode() : this(default!)
    {
    }

    public T Data { get; set; } = data;

    public int Batch { get; set; }

    public List<DAGNode<T>> Parents { get; } = [];

    public List<DAGNode<T>> Children { get; } = [];
}

public struct FrozenDAGNode<T>(T data, int batch)
{
    public T Data { get; set; } = data;

    /// <summary>
    /// Nodes in same batch can be executed in parallel
    /// </summary>
    public int Batch { get; set; } = batch;
}

public sealed class DirectedAcyclicGraph<T>
{
    public List<DAGNode<T>> Nodes { get; } = [];

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
    /// Perform topological sorting to freeze the graph as a list of frozen nodes
    /// </summary>
    /// <returns></returns>
    /// <exception cref="ConstraintException">If graph contains a cycle</exception>
    public List<FrozenDAGNode<T>> FreezeAsList()
    {
        var inDegree = Nodes.ToDictionary(node => node, node => node.Parents.Count);
        var queue = new Queue<DAGNode<T>>(Nodes.Where(n => inDegree[n] == 0));
        var result = new List<FrozenDAGNode<T>>(Nodes.Count);
        var currentBatch = 0;

        while (queue.Count > 0)
        {
            var batchSize = queue.Count;
            for (var i = 0; i < batchSize; i++)
            {
                var node = queue.Dequeue();
                node.Batch = currentBatch;
                result.Add(new(node.Data, node.Batch));

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
}