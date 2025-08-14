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

public struct FreezedDAGNode<T>(T data, int batch)
{
    public T Data { get; set; } = data;
    public int Batch { get; set; } = batch;
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
            to.Parents.Add(from);
        }
        else
        {
            throw new ArgumentException("Can't add edge because at lease one of the nodes is not in the graph");
        }
    }

    public List<FreezedDAGNode<T>> FreezeAsList()
    {
        var inDegree = Nodes.ToDictionary(node => node, node => node.Parents.Count);
        var queue = new Queue<DAGNode<T>>(Nodes.Where(n => inDegree[n] == 0));
        var result = new List<FreezedDAGNode<T>>(Nodes.Count);
        var currentBatch = 0;

        while (queue.Count > 0)
        {
            var batchSize = queue.Count;
            for (var i = 0; i < batchSize; i++)
            {
                var node = queue.Dequeue();
                node.Batch = currentBatch;
                result.Add(new FreezedDAGNode<T>(node.Data, node.Batch));

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
            throw new InvalidOperationException("Graph contains a cycle!");
        }

        return result;
    }
}
