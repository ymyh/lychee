using System.Collections;

namespace lychee.collections;

/// <summary>
/// Exception thrown when a graph operation results in an invalid state, such as detecting a cycle.
/// </summary>
public sealed class InvalidGraphException(string reason) : Exception(reason);

/// <summary>
/// Represents a node in a directed acyclic graph (DAG).
/// </summary>
/// <typeparam name="T">The type of data stored in the node.</typeparam>
public sealed class DAGNode<T>(T data)
{
    public DAGNode() : this(default!)
    {
    }

    /// <summary>
    /// Gets or sets the data stored in this node.
    /// </summary>
    public T Data { get; set; } = data;

    /// <summary>
    /// Gets the list of parent nodes that have edges pointing to this node.
    /// </summary>
    public List<DAGNode<T>> Parents { get; } = [];

    /// <summary>
    /// Gets the list of child nodes that this node has edges pointing to.
    /// </summary>
    public List<DAGNode<T>> Children { get; } = [];

    internal int Group { get; set; }
}

/// <summary>
/// A read-only frozen representation of a DAG node for optimized performance.
/// </summary>
/// <typeparam name="T">The type of data stored in the node.</typeparam>
public struct FrozenDAGNode<T>(DAGNode<T> node)
{
    /// <summary>
    /// The data stored in this node.
    /// </summary>
    public T Data = node.Data;

    /// <summary>
    /// Gets the execution group ID. Nodes in the same group have no dependencies on each other and can be executed in parallel.
    /// </summary>
    public readonly int Group = node.Group;
}

/// <summary>
/// Represents a directed acyclic graph (DAG) with a single entry point and multiple possible exit points.
/// </summary>
/// <typeparam name="T">The type of data stored in the graph nodes.</typeparam>
public sealed class DirectedAcyclicGraph<T> : IEnumerable<DAGNode<T>>
{
    /// <summary>
    /// Gets the collection of all nodes in the graph.
    /// </summary>
    public List<DAGNode<T>> Nodes { get; } = [];

    /// <summary>
    /// Gets the root node (entry point) of the graph.
    /// </summary>
    public DAGNode<T> Root => Nodes[0];

    /// <summary>
    /// Gets the number of nodes in the graph.
    /// </summary>
    public int Count => Nodes.Count;

    /// <summary>
    /// Reusable queue for validation (cached to avoid per-call allocation).
    /// </summary>
    private readonly Queue<DAGNode<T>> validationQueue = [];

    /// <summary>
    /// Reusable in-degree dictionary for validation (cached to avoid per-call allocation).
    /// </summary>
    private readonly Dictionary<DAGNode<T>, int> validationInDegreeDict = [];

    /// <summary>
    /// Returns true if the graph is a valid DAG: has exactly one root node and contains no cycles.
    /// </summary>
    public bool Valid
    {
        get
        {
            if (Nodes.Count == 0)
            {
                return false;
            }

            var queue = validationQueue;
            var inDegreeDict = validationInDegreeDict;

            queue.Clear();
            inDegreeDict.Clear();

            DAGNode<T>? root = null;

            foreach (var node in Nodes)
            {
                var inDegree = node.Parents.Count;

                if (inDegree == 0)
                {
                    if (root != null)
                    {
                        return false;
                    }

                    root = node;
                }

                inDegreeDict[node] = inDegree;
            }

            if (root == null)
            {
                return false;
            }

            queue.Enqueue(root);
            var visited = 0;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                visited++;

                foreach (var child in current.Children)
                {
                    var degree = inDegreeDict[child] - 1;
                    inDegreeDict[child] = degree;

                    if (degree == 0)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return visited == Nodes.Count;
        }
    }

    /// <summary>
    /// Adds a node to the graph without connecting it to any other nodes.
    /// </summary>
    /// <param name="node">The node to add to the graph.</param>
    /// <returns>The added node.</returns>
    public DAGNode<T> AddNode(DAGNode<T> node)
    {
        Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Adds a directed edge from one node to another.
    /// </summary>
    /// <param name="from">The source node of the edge.</param>
    /// <param name="to">The destination node of the edge.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="from"/> or <paramref name="to"/> is not in the graph,
    /// when <paramref name="from"/> equals <paramref name="to"/>, or
    /// when the edge already exists.
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
    /// Removes a directed edge from one node to another.
    /// </summary>
    /// <param name="from">The source node of the edge.</param>
    /// <param name="to">The destination node of the edge.</param>
    public void RemoveEdge(DAGNode<T> from, DAGNode<T> to)
    {
        from.Children.Remove(to);
        to.Parents.Remove(from);
    }

    /// <summary>
    /// Performs topological sorting to return nodes in a valid execution order.
    /// For a more efficient structure, use <see cref="DirectedAcyclicGraphExtensions.Freeze{T}"/>.
    /// </summary>
    /// <returns>A topologically sorted list of nodes.</returns>
    /// <exception cref="InvalidGraphException">Thrown when the graph contains a cycle or more than one root node.</exception>
    public List<DAGNode<T>> AsList()
    {
        if (Nodes.Count(n => n.Parents.Count == 0) > 1)
        {
            throw new InvalidGraphException("Graph contains more than one root node");
        }

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
            throw new InvalidGraphException("Graph contains a cycle!");
        }

        return result;
    }

    /// <summary>
    /// Removes all nodes from the graph.
    /// </summary>
    public void Clear()
    {
        Nodes.Clear();
    }

    /// <summary>
    /// Executes the specified action on each node in the graph.
    /// </summary>
    /// <param name="action">The action to perform on each node.</param>
    public void ForEach(Action<DAGNode<T>> action)
    {
        foreach (var node in Nodes)
        {
            action(node);
        }
    }

#region IEnumerable Implementation

    public IEnumerator<DAGNode<T>> GetEnumerator()
    {
        return Nodes.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

#endregion
}

/// <summary>
/// Provides extension methods for working with directed acyclic graphs.
/// </summary>
public static class DirectedAcyclicGraphExtensions
{
    /// <summary>
    /// Provides extension methods for collections of DAG nodes.
    /// </summary>
    extension<T>(IEnumerable<DAGNode<T>> nodes)
    {
        /// <summary>
        /// Converts a collection of DAG nodes to their frozen counterparts for optimized performance.
        /// </summary>
        /// <returns>A collection of frozen DAG nodes.</returns>
        public IEnumerable<FrozenDAGNode<T>> Freeze()
        {
            return nodes.Select(x => new FrozenDAGNode<T>(x));
        }
    }

    /// <summary>
    /// Provides extension methods for collections of frozen DAG nodes.
    /// </summary>
    extension<T>(IEnumerable<FrozenDAGNode<T>> nodes)
    {
        /// <summary>
        /// Groups frozen nodes by their execution group ID for parallel execution planning.
        /// </summary>
        /// <returns>A 2D array where each inner array contains nodes that can be executed in parallel.</returns>
        public FrozenDAGNode<T>[][] AsExecutionGroup()
        {
            return nodes.GroupBy(x => new { x.Group }).Select(x => x.ToArray()).ToArray();
        }
    }
}
