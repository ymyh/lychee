using lychee.collections;

namespace lychee.Tests;

public class DirectedAcyclicGraphTests
{
#region AddNode

    [Fact]
    public void AddNode_SingleNode_AddsSuccessfully()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var node = new DAGNode<int>(42);

        graph.AddNode(node);

        Assert.Single(graph.Nodes);
        Assert.Equal(42, graph.Nodes[0].Data);
    }

    [Fact]
    public void AddNode_MultipleNodes_AllPresent()
    {
        var graph = new DirectedAcyclicGraph<int>();

        graph.AddNode(new DAGNode<int>(1));
        graph.AddNode(new DAGNode<int>(2));
        graph.AddNode(new DAGNode<int>(3));

        Assert.Equal(3, graph.Count);
    }

    [Fact]
    public void AddNode_ReturnsSameNode()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var node = new DAGNode<int>(42);

        var returned = graph.AddNode(node);

        Assert.Same(node, returned);
    }

#endregion

#region Root

    [Fact]
    public void Root_ReturnsFirstNode()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var node = new DAGNode<int>(42);
        graph.AddNode(node);

        Assert.Same(node, graph.Root);
    }

#endregion

#region AddEdge

    [Fact]
    public void AddEdge_ValidEdge_CreatesParentChildRelationship()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);

        Assert.Contains(b, a.Children);
        Assert.Contains(a, b.Parents);
    }

    [Fact]
    public void AddEdge_SameNode_ThrowsArgumentException()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));

        Assert.Throws<ArgumentException>(() => graph.AddEdge(a, a));
    }

    [Fact]
    public void AddEdge_DuplicateEdge_ThrowsArgumentException()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);

        Assert.Throws<ArgumentException>(() => graph.AddEdge(a, b));
    }

    [Fact]
    public void AddEdge_NodeNotInGraph_ThrowsArgumentException()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = new DAGNode<int>(2); // not added to graph

        Assert.Throws<ArgumentException>(() => graph.AddEdge(a, b));
    }

#endregion

#region RemoveEdge

    [Fact]
    public void RemoveEdge_ExistingEdge_RemovesRelationship()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);
        graph.RemoveEdge(a, b);

        Assert.DoesNotContain(b, a.Children);
        Assert.DoesNotContain(a, b.Parents);
    }

#endregion

#region Valid

    [Fact]
    public void Valid_EmptyGraph_ReturnsFalse()
    {
        var graph = new DirectedAcyclicGraph<int>();

        Assert.False(graph.Valid);
    }

    [Fact]
    public void Valid_SingleNode_ReturnsTrue()
    {
        var graph = new DirectedAcyclicGraph<int>();
        graph.AddNode(new DAGNode<int>(1));

        Assert.True(graph.Valid);
    }

    [Fact]
    public void Valid_LinearChain_ReturnsTrue()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));

        graph.AddEdge(a, b);
        graph.AddEdge(b, c);

        Assert.True(graph.Valid);
    }

    [Fact]
    public void Valid_DiamondGraph_ReturnsTrue()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));
        var d = graph.AddNode(new DAGNode<int>(4));

        graph.AddEdge(a, b);
        graph.AddEdge(a, c);
        graph.AddEdge(b, d);
        graph.AddEdge(c, d);

        Assert.True(graph.Valid);
    }

    [Fact]
    public void Valid_Cycle_ReturnsFalse()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));

        graph.AddEdge(a, b);
        graph.AddEdge(b, c);
        graph.AddEdge(c, a);

        Assert.False(graph.Valid);
    }

    [Fact]
    public void Valid_MultipleRoots_ReturnsFalse()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));

        graph.AddEdge(a, c);
        graph.AddEdge(b, c);

        Assert.False(graph.Valid);
    }

    [Fact]
    public void Valid_AfterRemovingCycleEdge_ReturnsTrue()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);
        graph.AddEdge(b, a);

        Assert.False(graph.Valid);

        graph.RemoveEdge(b, a);

        Assert.True(graph.Valid);
    }

#endregion

#region AsList

    [Fact]
    public void AsList_LinearChain_ReturnsCorrectOrder()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));

        graph.AddEdge(a, b);
        graph.AddEdge(b, c);

        var list = graph.AsList();

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0].Data);
        Assert.Equal(2, list[1].Data);
        Assert.Equal(3, list[2].Data);
    }

    [Fact]
    public void AsList_Diamond_ReturnsTopologicalOrder()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));
        var d = graph.AddNode(new DAGNode<int>(4));

        graph.AddEdge(a, b);
        graph.AddEdge(a, c);
        graph.AddEdge(b, d);
        graph.AddEdge(c, d);

        var list = graph.AsList();

        Assert.Equal(4, list.Count);
        Assert.Equal(1, list[0].Data); // a is first (root)
        Assert.Equal(4, list[^1].Data); // d is last (leaf)
    }

    [Fact]
    public void AsList_Cycle_ThrowsInvalidGraphException()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);
        graph.AddEdge(b, a);

        Assert.Throws<InvalidGraphException>(() => graph.AsList());
    }

    [Fact]
    public void AsList_MultipleRoots_ThrowsInvalidGraphException()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        Assert.Throws<InvalidGraphException>(() => graph.AsList());
    }

    [Fact]
    public void AsList_AssignsGroupIds()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));

        graph.AddEdge(a, b);
        graph.AddEdge(a, c);

        var list = graph.AsList();

        Assert.Equal(0, list[0].Group); // a
        Assert.Equal(1, list[1].Group); // b
        Assert.Equal(1, list[2].Group); // c (same group as b, both depend only on a)
    }

#endregion

#region ForEach

    [Fact]
    public void ForEach_VisitsAllNodes()
    {
        var graph = new DirectedAcyclicGraph<int>();
        graph.AddNode(new DAGNode<int>(1));
        graph.AddNode(new DAGNode<int>(2));
        graph.AddNode(new DAGNode<int>(3));

        var sum = 0;
        graph.ForEach(n => sum += n.Data);

        Assert.Equal(6, sum);
    }

#endregion

#region Clear

    [Fact]
    public void Clear_RemovesAllNodes()
    {
        var graph = new DirectedAcyclicGraph<int>();
        graph.AddNode(new DAGNode<int>(1));
        graph.AddNode(new DAGNode<int>(2));

        graph.Clear();

        Assert.Equal(0, graph.Count);
        Assert.Empty(graph.Nodes);
    }

#endregion

#region IEnumerable

    [Fact]
    public void GetEnumerator_IteratesAllNodes()
    {
        var graph = new DirectedAcyclicGraph<int>();
        graph.AddNode(new DAGNode<int>(1));
        graph.AddNode(new DAGNode<int>(2));

        var count = 0;
        foreach (var node in graph)
        {
            count++;
        }

        Assert.Equal(2, count);
    }

#endregion

#region DAGNode

    [Fact]
    public void DAGNode_DefaultConstructor_SetsDefaultData()
    {
        var node = new DAGNode<int>();

        Assert.Equal(0, node.Data);
        Assert.Empty(node.Parents);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void DAGNode_ParentsAndChildren_AreEmptyByDefault()
    {
        var node = new DAGNode<string>("test");

        Assert.Empty(node.Parents);
        Assert.Empty(node.Children);
    }

#endregion

#region Freeze and ExecutionGroup

    [Fact]
    public void Freeze_ReturnsFrozenNodes()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);

        var list = graph.AsList();
        var frozen = list.Freeze().ToArray();

        Assert.Equal(2, frozen.Length);
        Assert.Equal(1, frozen[0].Data);
        Assert.Equal(2, frozen[1].Data);
    }

    [Fact]
    public void AsExecutionGroup_GroupsParallelNodes()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));
        var c = graph.AddNode(new DAGNode<int>(3));

        graph.AddEdge(a, b);
        graph.AddEdge(a, c);

        var groups = graph.AsList().Freeze().AsExecutionGroup();

        Assert.Equal(2, groups.Length);
        Assert.Single(groups[0]); // a
        Assert.Equal(2, groups[1].Length); // b, c
    }

#endregion

#region Complex Scenarios

    [Fact]
    public void ComplexGraph_ValidTopologicalOrder()
    {
        // Build: a -> b -> d
        //        a -> c -> d
        //        a -> e
        var graph = new DirectedAcyclicGraph<string>();
        var a = graph.AddNode(new DAGNode<string>("a"));
        var b = graph.AddNode(new DAGNode<string>("b"));
        var c = graph.AddNode(new DAGNode<string>("c"));
        var d = graph.AddNode(new DAGNode<string>("d"));
        var e = graph.AddNode(new DAGNode<string>("e"));

        graph.AddEdge(a, b);
        graph.AddEdge(a, c);
        graph.AddEdge(a, e);
        graph.AddEdge(b, d);
        graph.AddEdge(c, d);

        var list = graph.AsList();

        Assert.Equal(5, list.Count);
        Assert.Equal("a", list[0].Data); // root

        // b and c must come before d
        var bIndex = list.FindIndex(n => n.Data == "b");
        var cIndex = list.FindIndex(n => n.Data == "c");
        var dIndex = list.FindIndex(n => n.Data == "d");
        var eIndex = list.FindIndex(n => n.Data == "e");

        Assert.True(bIndex < dIndex);
        Assert.True(cIndex < dIndex);
    }

    [Fact]
    public void RepeatedAddAndRemove_MaintainsValidity()
    {
        var graph = new DirectedAcyclicGraph<int>();
        var a = graph.AddNode(new DAGNode<int>(1));
        var b = graph.AddNode(new DAGNode<int>(2));

        graph.AddEdge(a, b);
        Assert.True(graph.Valid);

        graph.RemoveEdge(a, b);
        // After removing the only edge, a is the only root (b has no parents)
        // Actually, both a and b have no parents now → two roots → invalid
        Assert.False(graph.Valid);

        graph.AddEdge(a, b);
        Assert.True(graph.Valid);
    }

#endregion
}
