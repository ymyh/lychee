using lychee.interfaces;

namespace lychee.Tests;

#region Test Infrastructure

/// <summary>
/// Shared execution order recorder for system ordering tests.
/// </summary>
internal static class ExecutionRecorder
{
    private static readonly List<string> Order = [];

    public static void Record(string name)
    {
        Order.Add(name);
    }

    public static void Clear()
    {
        Order.Clear();
    }

    public static IReadOnlyList<string> GetOrder()
    {
        return Order;
    }
}

/// <summary>
/// Minimal ISystem that records execution and has no component parameters.
/// Systems with no parameters can potentially run in parallel with each other.
/// </summary>
internal sealed class RecordingSystem : ISystem
{
    private readonly string name;

    public RecordingSystem(string name)
    {
        this.name = name;
    }

    public string GetName() => name;

    public void InitializeAG(App app, SystemDescriptor descriptor) { }

    public void ConfigureAG(App app, SystemFilterInfo filterInfo) { }

    public Commands[] ExecuteAG()
    {
        ExecutionRecorder.Record(name);
        return [];
    }

    private static void Execute() { }
}

/// <summary>
/// A system that reads a specific component type (in parameter = readonly).
/// Two ReadSystems of different types can run in parallel.
/// </summary>
internal sealed class ReadSystem<T> : ISystem where T : unmanaged, IComponent
{
    private readonly string name;

    public ReadSystem(string name)
    {
        this.name = name;
    }

    public void InitializeAG(App app, SystemDescriptor descriptor) { }

    public void ConfigureAG(App app, SystemFilterInfo filterInfo) { }

    public Commands[] ExecuteAG()
    {
        ExecutionRecorder.Record(name);
        return [];
    }

    private static void Execute(in T component) { }
}

/// <summary>
/// A system that writes a specific component type (ref parameter = writable).
/// Two systems writing the same type cannot run in parallel.
/// </summary>
internal sealed class WriteSystem<T> : ISystem where T : unmanaged, IComponent
{
    private readonly string name;

    public WriteSystem(string name)
    {
        this.name = name;
    }

    public void InitializeAG(App app, SystemDescriptor descriptor) { }

    public void ConfigureAG(App app, SystemFilterInfo filterInfo) { }

    public Commands[] ExecuteAG()
    {
        ExecutionRecorder.Record(name);
        return [];
    }

    private static void Execute(ref T component) { }
}

/// <summary>
/// A system that can be skipped via Predicate.
/// </summary>
internal sealed class SkippableSystem : ISystem
{
    private readonly string name;
    private readonly bool shouldExecute;

    public SkippableSystem(string name, bool shouldExecute)
    {
        this.name = name;
        this.shouldExecute = shouldExecute;
    }

    public void InitializeAG(App app, SystemDescriptor descriptor) { }

    public void ConfigureAG(App app, SystemFilterInfo filterInfo) { }

    public Commands[] ExecuteAG()
    {
        ExecutionRecorder.Record(name);
        return [];
    }

    public bool Predicate(ResourcePool pool)
    {
        return shouldExecute;
    }

    private static void Execute() { }
}

internal enum TestSet
{
    SetA,
    SetB,
    SetC,
}

#endregion

public class SystemOrderTests : IDisposable
{
    private readonly App app;

    public SystemOrderTests()
    {
        app = new App();
    }

    public void Dispose()
    {
        ExecutionRecorder.Clear();
        app.Dispose();
    }

#region Single System

    [Fact]
    public void AddSystem_SingleSystem_Executes()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.Execute();

        Assert.Equal(["A"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystem_TwoSystems_MaintainsOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));

        schedule.Execute();

        Assert.Equal(["A", "B"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystem_ThreeSystems_MaintainsOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));
        schedule.AddSystem(new RecordingSystem("C"));

        schedule.Execute();

        Assert.Equal(["A", "B", "C"], ExecutionRecorder.GetOrder());
    }

#endregion

#region AddSystems Array Syntax — Sequential Groups

    [Fact]
    public void AddSystems_ArraySyntax_SingleElementGroups_StrictOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystems(addAfter: null, [new RecordingSystem("A")], [new RecordingSystem("B")], [new RecordingSystem("C")]);
        schedule.Execute();

        Assert.Equal(["A", "B", "C"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystems_ArraySyntax_ThreeGroups_StrictOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystems(addAfter: null, [new RecordingSystem("X")], [new RecordingSystem("Y")], [new RecordingSystem("Z")]);
        schedule.Execute();

        Assert.Equal(["X", "Y", "Z"], ExecutionRecorder.GetOrder());
    }

#endregion

#region AddSystems Array Syntax — Parallel Groups with different component types

    [Fact]
    public void AddSystems_DifferentComponentTypes_CanRunParallel()
    {
        var schedule = new DefaultSchedule(app, "Test");

        // ReadSystem<TestPosition> and ReadSystem<TestVelocity> have different types
        // so CanRunParallel should allow them to be in the same group
        schedule.AddSystems(addAfter: null,
            [new ReadSystem<TestPosition>("ReadPos"), new ReadSystem<TestVelocity>("ReadVel")]
        );

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        Assert.Equal(2, order.Count);
        Assert.Contains("ReadPos", order);
        Assert.Contains("ReadVel", order);
    }

    [Fact]
    public void AddSystems_SameComponentWrite_SequentialGroups()
    {
        var schedule = new DefaultSchedule(app, "Test");

        // Both write TestPosition — cannot run in parallel
        schedule.AddSystems(addAfter: null,
            [new WriteSystem<TestPosition>("Write1")],
            [new WriteSystem<TestPosition>("Write2")]
        );

        schedule.Execute();

        Assert.Equal(["Write1", "Write2"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystems_MixedReadWrite_CorrectGrouping()
    {
        var schedule = new DefaultSchedule(app, "Test");

        // ReadPos + ReadVel can be parallel (different types, both readonly)
        // WritePos must be after both (writes a type that ReadPos reads)
        schedule.AddSystems(addAfter: null,
            [new ReadSystem<TestPosition>("ReadPos"), new ReadSystem<TestVelocity>("ReadVel")],
            [new WriteSystem<TestPosition>("WritePos")]
        );

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var readPosIdx = order.ToList().IndexOf("ReadPos");
        var readVelIdx = order.ToList().IndexOf("ReadVel");
        var writePosIdx = order.ToList().IndexOf("WritePos");

        // ReadPos and ReadVel before WritePos
        Assert.True(readPosIdx < writePosIdx);
        Assert.True(readVelIdx < writePosIdx);
    }

#endregion

#region Predicate

    [Fact]
    public void Predicate_SkipsSystem_WhenFalse()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new SkippableSystem("Skipped", false));
        schedule.AddSystem(new RecordingSystem("B"));

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        Assert.Contains("A", order);
        Assert.DoesNotContain("Skipped", order);
        Assert.Contains("B", order);
    }

    [Fact]
    public void Predicate_ExecutesSystem_WhenTrue()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new SkippableSystem("Included", true));
        schedule.AddSystem(new RecordingSystem("B"));

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        Assert.Contains("A", order);
        Assert.Contains("Included", order);
        Assert.Contains("B", order);
    }

    [Fact]
    public void Predicate_MultipleSkipped_OnlyMatchingExecute()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new SkippableSystem("S1", false));
        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new SkippableSystem("S2", false));
        schedule.AddSystem(new RecordingSystem("B"));
        schedule.AddSystem(new SkippableSystem("S3", true));

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        Assert.DoesNotContain("S1", order);
        Assert.DoesNotContain("S2", order);
        Assert.Contains("S3", order);
        Assert.Contains("A", order);
        Assert.Contains("B", order);
    }

    [Fact]
    public void Predicate_AllSkipped_NothingExecutes()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new SkippableSystem("S1", false));
        schedule.AddSystem(new SkippableSystem("S2", false));

        schedule.Execute();

        Assert.Empty(ExecutionRecorder.GetOrder());
    }

#endregion

#region SystemSets API

    [Fact]
    public void SystemSets_AddSystemSet_RegistersCorrectly()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        // ConfigureSetOrder should not throw after registration
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
    }

    [Fact]
    public void SystemSets_ConfigureSetOrder_WithoutRegistration_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB));
    }

    [Fact]
    public void SystemSets_ConfigureSetPredicate_Works()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        var called = false;
        app.SystemSets.ConfigureSetPredicate(TestSet.SetA, pool =>
        {
            called = true;
            return true;
        });

        app.SystemSets.ComputeAllPredicates();

        Assert.True(called);
    }

    [Fact]
    public void SystemSets_ConfigureSetInSet_SetsParent()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetB);
    }

    [Fact]
    public void SystemSets_CycleDetection_Throws()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        Assert.Throws<InvalidOperationException>(() =>
            app.SystemSets.ConfigureSetOrder(TestSet.SetB, Order.Before, TestSet.SetA));
    }

    [Fact]
    public void SystemSets_PredicateResult_AppliedToExecution()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetPredicate(TestSet.SetA, _ => false);

        app.SystemSets.ComputeAllPredicates();

        // Find the correct SetInfo key from the dictionary
        var setAEntry = app.SystemSets.SetPredicateResultDict
            .FirstOrDefault(kv => kv.Key.Name == nameof(TestSet.SetA));

        Assert.False(setAEntry.Value);
    }

#endregion

#region Set-Based Ordering

    [Fact]
    public void SetOrder_Before_SystemExecutesInOrder()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        // Add B first, then A — but SetA should execute before SetB
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var aIdx = order.ToList().IndexOf("A");
        var bIdx = order.ToList().IndexOf("B");

        Assert.True(aIdx < bIdx, $"A({aIdx}) should execute before B({bIdx})");
    }

    [Fact]
    public void SetOrder_After_SystemExecutesInOrder()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.After, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var aIdx = order.ToList().IndexOf("A");
        var bIdx = order.ToList().IndexOf("B");

        Assert.True(bIdx < aIdx, $"B({bIdx}) should execute before A({aIdx})");
    }

    [Fact]
    public void SetOrder_MultipleSystemsInSameSet_MaintainRelativeOrder()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("B1"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("A1"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("A2"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B2"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var a1Idx = order.ToList().IndexOf("A1");
        var a2Idx = order.ToList().IndexOf("A2");
        var b1Idx = order.ToList().IndexOf("B1");
        var b2Idx = order.ToList().IndexOf("B2");

        // All A systems before all B systems
        Assert.True(a1Idx < b1Idx);
        Assert.True(a1Idx < b2Idx);
        Assert.True(a2Idx < b1Idx);
        Assert.True(a2Idx < b2Idx);
    }

    [Fact]
    public void SetOrder_ChainSets_ABC_InOrder()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetOrder(TestSet.SetB, Order.Before, TestSet.SetC);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("C"), new SystemDescriptor { Sets = [TestSet.SetC] });
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.Execute();

        Assert.Equal(["A", "B", "C"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void SetOrder_SystemNotInAnySet_ExecutesFreely()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("Free"));
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var aIdx = order.ToList().IndexOf("A");
        var bIdx = order.ToList().IndexOf("B");

        // A before B is enforced; Free can be anywhere
        Assert.True(aIdx < bIdx, $"A({aIdx}) should execute before B({bIdx})");
    }

    [Fact]
    public void SetOrder_MixedSetAndNonSet_Systems()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("Free1"));
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("Free2"));

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var aIdx = order.ToList().IndexOf("A");
        var bIdx = order.ToList().IndexOf("B");

        Assert.True(aIdx < bIdx, $"A({aIdx}) should execute before B({bIdx})");
    }

#endregion

#region Nested Set Ordering (Set In Set)

    [Fact]
    public void NestedSet_ParentSetBeforeChildSet()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        // SetC is a child of SetA; SetA is before SetB
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetC);
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        // SetC (child of SetA) should execute before SetB
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("C"), new SystemDescriptor { Sets = [TestSet.SetC] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var cIdx = order.ToList().IndexOf("C");
        var bIdx = order.ToList().IndexOf("B");

        Assert.True(cIdx < bIdx, $"C({cIdx}) should execute before B({bIdx}) because C is child of A which is before B");
    }

    [Fact]
    public void NestedSet_ChildSetInheritsParentPredicate()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        // SetC is a child of SetA
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetC);
        // SetA's predicate returns false
        app.SystemSets.ConfigureSetPredicate(TestSet.SetA, _ => false);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("C"), new SystemDescriptor { Sets = [TestSet.SetC] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        // Both A and C should be skipped because SetA's predicate is false
        // and C inherits from A
        Assert.DoesNotContain("A", order);
        Assert.DoesNotContain("C", order);
    }

    [Fact]
    public void NestedSet_ThreeLevelHierarchy()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        // A → B → C (chain of parent-child)
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetB);
        app.SystemSets.ConfigureSetInSet(TestSet.SetB, TestSet.SetC);

        // A before C (transitive through B)
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetC);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("C"), new SystemDescriptor { Sets = [TestSet.SetC] });
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var aIdx = order.ToList().IndexOf("A");
        var bIdx = order.ToList().IndexOf("B");
        var cIdx = order.ToList().IndexOf("C");

        Assert.True(aIdx < bIdx, $"A({aIdx}) should execute before B({bIdx})");
        Assert.True(bIdx < cIdx, $"B({bIdx}) should execute before C({cIdx})");
        Assert.True(aIdx < cIdx, $"A({aIdx}) should execute before C({cIdx})");
    }

    [Fact]
    public void NestedSet_MultipleSystemsInNestedSets()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        // SetB is child of SetA (predicate inheritance)
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetB);
        // SetA before SetC (explicit ordering)
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetC);

        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("C1"), new SystemDescriptor { Sets = [TestSet.SetC] });
        schedule.AddSystem(new RecordingSystem("B1"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("A1"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B2"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("C2"), new SystemDescriptor { Sets = [TestSet.SetC] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var a1Idx = order.ToList().IndexOf("A1");
        var c1Idx = order.ToList().IndexOf("C1");
        var c2Idx = order.ToList().IndexOf("C2");

        // A1 before C1, C2 (explicit SetA before SetC ordering)
        Assert.True(a1Idx < c1Idx, $"A1({a1Idx}) before C1({c1Idx})");
        Assert.True(a1Idx < c2Idx, $"A1({a1Idx}) before C2({c2Idx})");

        // B1, B2 should be present (no predicate blocking them)
        Assert.Contains("B1", order);
        Assert.Contains("B2", order);
    }

    [Fact]
    public void NestedSet_ChildSetInheritsParentOrdering()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        // SetB is before SetC
        app.SystemSets.ConfigureSetOrder(TestSet.SetB, Order.Before, TestSet.SetC);
        // SetA is parent of SetB
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        // SetC should execute after SetB (and thus after SetA's children)
        schedule.AddSystem(new RecordingSystem("C"), new SystemDescriptor { Sets = [TestSet.SetC] });
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        var bIdx = order.ToList().IndexOf("B");
        var cIdx = order.ToList().IndexOf("C");

        Assert.True(bIdx < cIdx, $"B({bIdx}) should execute before C({cIdx})");
    }

#endregion

#region Execution Graph Structure

    [Fact]
    public void ExecutionGraph_SingleSystem_HasOneNode()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));

        var list = schedule.ExecutionGraph.AsList();

        // Root + 1 system
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void ExecutionGraph_ThreeSystems_HasThreeNodes()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));
        schedule.AddSystem(new RecordingSystem("C"));

        var list = schedule.ExecutionGraph.AsList();

        // Root + 3 systems
        Assert.Equal(4, list.Count);
    }

    [Fact]
    public void ExecutionGraph_ClearSystems_ResetsGraph()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));

        schedule.ClearSystems();

        var list = schedule.ExecutionGraph.AsList();

        // Only root remains
        Assert.Single(list);
    }

#endregion

#region Multiple Schedule Execution

    [Fact]
    public void MultipleSchedules_ExecuteInOrder()
    {
        var schedule1 = new DefaultSchedule(app, "Schedule1");
        var schedule2 = new DefaultSchedule(app, "Schedule2");

        schedule1.AddSystem(new RecordingSystem("S1_A"));
        schedule2.AddSystem(new RecordingSystem("S2_A"));

        app.AddSchedule(schedule1);
        app.AddSchedule(schedule2);

        app.Update();

        var order = ExecutionRecorder.GetOrder();
        var s1Idx = order.ToList().IndexOf("S1_A");
        var s2Idx = order.ToList().IndexOf("S2_A");

        Assert.True(s1Idx < s2Idx, $"Schedule1({s1Idx}) should execute before Schedule2({s2Idx})");
    }

    [Fact]
    public void FirstSchedule_AlwaysExecutesBeforeUserSchedules()
    {
        var userSchedule = new DefaultSchedule(app, "UserSchedule");

        app.SystemSchedules.First.AddSystem(new RecordingSystem("First"));
        userSchedule.AddSystem(new RecordingSystem("User"));

        app.AddSchedule(userSchedule);

        app.Update();

        var order = ExecutionRecorder.GetOrder();
        var firstIdx = order.ToList().IndexOf("First");
        var userIdx = order.ToList().IndexOf("User");

        Assert.True(firstIdx < userIdx, $"First({firstIdx}) should execute before User({userIdx})");
    }

    [Fact]
    public void LastSchedule_AlwaysExecutesAfterUserSchedules()
    {
        var userSchedule = new DefaultSchedule(app, "UserSchedule");

        userSchedule.AddSystem(new RecordingSystem("User"));
        app.SystemSchedules.Last.AddSystem(new RecordingSystem("Last"));

        app.AddSchedule(userSchedule);

        app.Update();

        var order = ExecutionRecorder.GetOrder();
        var userIdx = order.ToList().IndexOf("User");
        var lastIdx = order.ToList().IndexOf("Last");

        Assert.True(userIdx < lastIdx, $"User({userIdx}) should execute before Last({lastIdx})");
    }

#endregion

#region Stress

    [Fact]
    public void Stress_ManySystems_MaintainsOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        for (var i = 0; i < 30; i++)
        {
            schedule.AddSystem(new RecordingSystem($"S{i:D2}"));
        }

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder();
        Assert.Equal(30, order.Count);

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal($"S{i:D2}", order[i]);
        }
    }

    [Fact]
    public void Stress_ManySchedules_AllExecute()
    {
        for (var i = 0; i < 10; i++)
        {
            var schedule = new DefaultSchedule(app, $"Schedule{i}");
            schedule.AddSystem(new RecordingSystem($"Sys{i}"));
            app.AddSchedule(schedule);
        }

        app.Update();

        var order = ExecutionRecorder.GetOrder();
        Assert.Equal(10, order.Count);

        for (var i = 0; i < 10; i++)
        {
            Assert.Contains($"Sys{i}", order);
        }
    }

    [Fact]
    public void Stress_MultipleUpdates_CumulativeExecution()
    {
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"));

        app.AddSchedule(schedule);

        app.Update();
        app.Update();
        app.Update();

        var order = ExecutionRecorder.GetOrder();
        Assert.Equal(3, order.Count);
        Assert.All(order, name => Assert.Equal("A", name));
    }

#endregion
}
