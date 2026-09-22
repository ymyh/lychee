using lychee.collections;
using lychee.interfaces;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// An <see cref="ILoggerFactory"/> that keeps every formatted message, so a test can assert a schedule really
/// reached the log instead of trusting that it called something.
/// </summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        return new RecordingLogger(Messages);
    }

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class RecordingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}

/// <summary>
/// A flags enum, used to cover set values that have no declared name of their own.
/// </summary>
[Flags]
internal enum FlagsTestSet
{
    None = 0,
    SetA = 1,
    SetB = 2,
}

/// <summary>
/// A parameterless system, for the tuple form of <c>AddSystems</c>, which constructs the systems itself.
/// The tests using it only look at how many systems ended up in each layer, so one type is enough.
/// </summary>
internal sealed class TupleFormSystem : ISystem
{
    public void InitializeAG(App app, SystemDescriptor descriptor) { }

    public void ConfigureAG(App app, SystemFilterInfo filterInfo) { }

    public Commands[] ExecuteAG()
    {
        return [];
    }

    private static void Execute() { }
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

        schedule.AddSystems([new RecordingSystem("A")], [new RecordingSystem("B")], [new RecordingSystem("C")]);
        schedule.Execute();

        Assert.Equal(["A", "B", "C"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystems_SeparateCalls_DoNotOrderEachOther()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var a = new RecordingSystem("A");
        var b = new RecordingSystem("B");
        var c = new RecordingSystem("C");
        var d = new RecordingSystem("D");

        // Two independent calls. Each one orders its own groups and nothing else, so the calls must not be
        // chained onto each other just because they both start counting at their first group.
        schedule.AddSystems([a], [b]);
        schedule.AddSystems([c], [d]);

        schedule.Build();

        Assert.Equal(NodeOf(schedule, a).Group, NodeOf(schedule, c).Group);
        Assert.Equal(NodeOf(schedule, b).Group, NodeOf(schedule, d).Group);
        Assert.True(NodeOf(schedule, a).Group < NodeOf(schedule, b).Group);
    }

    [Fact]
    public void AddSystems_ArraySyntax_ThreeGroups_StrictOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystems([new RecordingSystem("X")], [new RecordingSystem("Y")], [new RecordingSystem("Z")]);
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
        schedule.AddSystems(
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
        schedule.AddSystems(
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
        schedule.AddSystems(
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

#region AddSystems Tuple Syntax

    /// <summary>
    /// Reads how many systems each layer holds, in layer order, so a test can assert on the shape of the layering
    /// without telling the identical systems apart.
    /// </summary>
    private static int[] LayerSizes(DefaultSchedule schedule)
    {
        return schedule.ExecutionGraph.AsList()
            .GroupBy(node => node.Group)
            .Select(group => group.Count())
            .ToArray();
    }

    [Fact]
    public void AddSystems_TupleForm_FlatTupleRunsSequentially()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystems<(TupleFormSystem, TupleFormSystem, TupleFormSystem)>();

        schedule.Build();

        Assert.Equal([1, 1, 1], LayerSizes(schedule));
    }

    [Fact]
    public void AddSystems_TupleForm_NestedTupleRunsAsOneGroup()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystems<(TupleFormSystem, (TupleFormSystem, TupleFormSystem), TupleFormSystem)>();

        schedule.Build();

        // The two systems of the nested tuple have nothing ordering them, so they share a layer, while the outer
        // elements still run one after another.
        Assert.Equal([1, 2, 1], LayerSizes(schedule));
    }

#endregion

#region AddSystems with Descriptors

    [Fact]
    public void AddSystems_EntryForm_GroupsRunInOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystems(
            [(new RecordingSystem("A"), null), (new RecordingSystem("B"), null)],
            [(new RecordingSystem("C"), null)]);
        schedule.Execute();

        // Every member of the second group runs after both members of the first one.
        Assert.Equal(["A", "B", "C"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystems_EntryForm_DescriptorSetsAreApplied()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        // SetB is declared first, so only the descriptor's sets can put InA ahead of InB.
        schedule.AddSystems(
        [
            (new RecordingSystem("InB"), new SystemDescriptor { Sets = [TestSet.SetB] }),
            (new RecordingSystem("InA"), new SystemDescriptor { Sets = [TestSet.SetA] }),
        ]);
        schedule.Execute();

        Assert.Equal(["InA", "InB"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystems_EntryForm_DescriptorAddAfterIsApplied()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var writer = new RecordingSystem("W");

        // The reader comes first in the group and still has to run after the writer.
        schedule.AddSystems(
            [(new RecordingSystem("R"), new SystemDescriptor { AddAfter = writer }), (writer, null)]);
        schedule.Execute();

        Assert.Equal(["W", "R"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void AddSystems_NullGroup_Throws()
    {
        var schedule = new DefaultSchedule(app, "Test");

        // A null group is a mistake rather than an empty one, so it is reported instead of being skipped.
        var exception = Assert.Throws<ArgumentException>(() => schedule.AddSystems(null!, [new RecordingSystem("A")]));

        Assert.Contains("null", exception.Message);
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

        var parentInfo = MakeSetInfo(TestSet.SetA);
        var childInfo = MakeSetInfo(TestSet.SetB);

        Assert.Equal(parentInfo, app.SystemSets.GetParent(childInfo));
        Assert.Null(app.SystemSets.GetParent(parentInfo));
    }

    [Fact]
    public void SystemSets_CycleDetection_ThrowsAtValidationNotAtConfiguration()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        // Configuration only records: a contradictory pair is accepted here...
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetOrder(TestSet.SetB, Order.Before, TestSet.SetA);

        // ...and reported when the constraints are resolved.
        var exception = Assert.Throws<InvalidOperationException>(() => app.SystemSets.Validate());

        Assert.Contains(nameof(TestSet.SetA), exception.Message);
        Assert.Contains(nameof(TestSet.SetB), exception.Message);
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

#region SystemSets Configuration Internals

    [Fact]
    public void SystemSets_IsRegistered_TracksRegistration()
    {
        Assert.False(app.SystemSets.IsRegistered(typeof(TestSet)));
        Assert.False(app.SystemSets.IsRegistered(typeof(FlagsTestSet)));

        app.SystemSets.AddSystemSet<TestSet>();

        Assert.True(app.SystemSets.IsRegistered(typeof(TestSet)));
        Assert.False(app.SystemSets.IsRegistered(typeof(FlagsTestSet)));
    }

    [Fact]
    public void SystemSets_Version_IncreasesOnGraphChange()
    {
        var before = app.SystemSets.Version;

        app.SystemSets.AddSystemSet<TestSet>();
        var afterRegister = app.SystemSets.Version;

        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        var afterOrder = app.SystemSets.Version;

        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetC);
        var afterNesting = app.SystemSets.Version;

        Assert.True(afterRegister > before);
        Assert.True(afterOrder > afterRegister);
        Assert.True(afterNesting > afterOrder);
    }

    [Fact]
    public void SystemSets_Version_UnchangedWhenConfigurationRepeats()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetC);

        var version = app.SystemSets.Version;

        // Repeating the exact same configuration must not report a change.
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetC);

        Assert.Equal(version, app.SystemSets.Version);
    }

    [Fact]
    public void SystemSets_Version_UnchangedByPredicateConfiguration()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        var version = app.SystemSets.Version;

        app.SystemSets.ConfigureSetPredicate(TestSet.SetA, _ => true);
        app.SystemSets.ComputeAllPredicates();

        // Predicates are re-evaluated every execution, so they never invalidate the set graph.
        Assert.Equal(version, app.SystemSets.Version);
    }

    [Fact]
    public void SystemSets_ConfigureSetOrder_DuplicateConstraint_DoesNotThrow()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        Assert.Single(app.SystemSets.OrderEdges);
    }

    [Fact]
    public void SystemSets_OrderEdges_ReflectsBothDirections()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetOrder(TestSet.SetC, Order.After, TestSet.SetB);

        var edges = app.SystemSets.OrderEdges.ToList();

        var setA = MakeSetInfo(TestSet.SetA);
        var setB = MakeSetInfo(TestSet.SetB);
        var setC = MakeSetInfo(TestSet.SetC);

        // `Order.After` must be stored as the same "before" edge, not as a reverse edge.
        Assert.Equal(2, edges.Count);
        Assert.Contains((setA, setB), edges);
        Assert.Contains((setB, setC), edges);
    }

    [Fact]
    public void SystemSets_ConfigureSetInSet_SelfNesting_IsReportedAtValidation()
    {
        app.SystemSets.AddSystemSet<TestSet>();

        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetA);

        var exception = Assert.Throws<InvalidOperationException>(() => app.SystemSets.Validate());

        Assert.Contains(nameof(TestSet.SetA), exception.Message);
    }

    [Fact]
    public void SystemSets_ConfigureSetInSet_DirectCycle_IsReportedAtValidation()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetB);
        app.SystemSets.ConfigureSetInSet(TestSet.SetB, TestSet.SetA);

        var exception = Assert.Throws<InvalidOperationException>(() => app.SystemSets.Validate());

        // The message must name the sets taking part in the cycle, and the whole chain.
        Assert.Contains(nameof(TestSet.SetA), exception.Message);
        Assert.Contains(nameof(TestSet.SetB), exception.Message);
    }

    [Fact]
    public void SystemSets_ConfigureSetInSet_IndirectCycle_IsReportedAtValidation()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.ConfigureSetInSet(TestSet.SetA, TestSet.SetB);
        app.SystemSets.ConfigureSetInSet(TestSet.SetB, TestSet.SetC);
        app.SystemSets.ConfigureSetInSet(TestSet.SetC, TestSet.SetA);

        Assert.Throws<InvalidOperationException>(() => app.SystemSets.Validate());
    }

    [Fact]
    public void SystemSets_FlagsCombinationValue_IsUsableAsSet()
    {
        app.SystemSets.AddSystemSet<FlagsTestSet>();

        // A flag combination has no declared name of its own; it must still be usable instead of
        // crashing later on a null name.
        app.SystemSets.ConfigureSetPredicate(FlagsTestSet.SetA | FlagsTestSet.SetB, _ => true);
        app.SystemSets.ComputeAllPredicates();

        Assert.Single(app.SystemSets.SetPredicateResultDict);
    }

    [Fact]
    public void SystemSets_ConfigureSetOrder_DisjointConstraints_AreIndependent()
    {
        app.SystemSets.AddSystemSet<TestSet>();
        app.SystemSets.AddSystemSet<FlagsTestSet>();

        // Two independent ordering chains must coexist: neither implies anything about the other,
        // and neither is a cycle.
        app.SystemSets.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.SystemSets.ConfigureSetOrder(FlagsTestSet.SetA, Order.Before, FlagsTestSet.SetB);

        app.SystemSets.Validate();

        Assert.Equal(2, app.SystemSets.OrderEdges.Count());
    }

    [Fact]
    public void SetIdentity_SameNamedMembersOfDifferentEnums_DoNotCollide()
    {
        app.AddSystemSet<TestSet>();
        app.AddSystemSet<FlagsTestSet>();

        // TestSet.SetA and FlagsTestSet.SetA share a member name, so they only stay apart because a set identity
        // carries the enum type id as well.
        app.ConfigureSetOrder(FlagsTestSet.SetA, Order.Before, FlagsTestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");
        var inTestSet = new RecordingSystem("A");
        var inFlagsSet = new RecordingSystem("B");

        schedule.AddSystem(inTestSet, new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(inFlagsSet, new SystemDescriptor { Sets = [FlagsTestSet.SetB] });

        schedule.Build();

        // The constraint covers the FlagsTestSet pair only, so the two systems are unrelated and share a layer.
        Assert.Equal(NodeOf(schedule, inTestSet).Group, NodeOf(schedule, inFlagsSet).Group);
    }

#endregion

#region Helpers

    private SetInfo MakeSetInfo(TestSet set)
    {
        return new(app.TypeRegistrar.GetTypeId<TestSet>(), set.ToString());
    }

    /// <summary>
    /// Finds the resolved node of a system, so a test can assert on the layer it landed in.
    /// </summary>
    private static DAGNode<SystemInfo> NodeOf(DefaultSchedule schedule, ISystem system)
    {
        return schedule.ExecutionGraph.AsList().First(node => ReferenceEquals(node.Data.System, system));
    }

#endregion

#region App Set Facades

    [Fact]
    public void App_AddSystemSet_RegistersSetType()
    {
        // This public entry point used to throw: it registered the enum through Marshal.SizeOf.
        app.AddSystemSet<TestSet>();

        Assert.True(app.SystemSets.IsRegistered(typeof(TestSet)));

        // The registration must be usable through the facades.
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
    }

    [Fact]
    public void App_ConfigureSetOrder_TakesEffectForLaterSystems()
    {
        app.AddSystemSet<TestSet>();
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");

        // Added in the opposite order to what the set constraint requires.
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder().ToList();

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
    }

    [Fact]
    public void App_ConfigureSetPredicate_SkipsSystemsInSet()
    {
        app.AddSystemSet<TestSet>();
        app.ConfigureSetPredicate(TestSet.SetB, _ => false);

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        schedule.Execute();

        Assert.Equal(["A"], ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void App_ConfigureSetInSet_ChildInheritsParentPredicate()
    {
        app.AddSystemSet<TestSet>();
        app.ConfigureSetInSet(TestSet.SetA, TestSet.SetC);
        app.ConfigureSetPredicate(TestSet.SetA, _ => false);

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("C"), new SystemDescriptor { Sets = [TestSet.SetC] });

        schedule.Execute();

        Assert.Empty(ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void App_SystemSets_IsNotExposedPublicly()
    {
        // Set configuration is meant to go through the App facades, so the field must not be public.
        // A public-only lookup returning null is the assertion.
        Assert.Null(typeof(App).GetField("SystemSets"));
    }

#endregion

#region Deferred Resolution

    [Fact]
    public void SetOrder_ConfiguredAfterSystemsWereAdded_StillApplies()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });

        // Both the registration and the constraint come after the systems: resolution happens later, so the
        // order must still be honoured.
        app.AddSystemSet<TestSet>();
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        schedule.Execute();

        var order = ExecutionRecorder.GetOrder().ToList();

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
    }

    [Fact]
    public void SetOrder_ConfiguredAfterFirstExecution_RebuildsAndApplies()
    {
        app.AddSystemSet<TestSet>();

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });

        schedule.Execute();
        ExecutionRecorder.Clear();

        // Changing set configuration after the graph was resolved invalidates it.
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        schedule.Execute();

        var order = ExecutionRecorder.GetOrder().ToList();

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
    }

    [Fact]
    public void Build_ConfigurationChangeAfterBuild_IsResolvedAgain()
    {
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"));

        schedule.Build();
        Assert.True(schedule.IsBuilt);

        schedule.AddSystem(new RecordingSystem("B"));
        Assert.False(schedule.IsBuilt);

        schedule.Build();

        Assert.True(schedule.IsBuilt);
        Assert.Equal(2, schedule.ExecutionGraph.Count);
    }

    [Fact]
    public void Build_UnregisteredSet_Throws()
    {
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });

        var exception = Assert.Throws<InvalidOperationException>(() => schedule.Build());

        Assert.Contains(nameof(TestSet), exception.Message);
    }

    [Fact]
    public void Build_MutualAddAfter_ReportsTheCycleWithSetNames()
    {
        app.AddSystemSet<TestSet>();

        var schedule = new DefaultSchedule(app, "Test");
        var a = new RecordingSystem("A");
        var b = new RecordingSystem("B");

        schedule.AddSystem(a, new SystemDescriptor { Sets = [TestSet.SetA], AddAfter = b });
        schedule.AddSystem(b, new SystemDescriptor { Sets = [TestSet.SetB], AddAfter = a });

        var exception = Assert.Throws<InvalidGraphException>(() => schedule.Build());

        // The message has to name what takes part in the cycle, otherwise there is nothing to act on.
        Assert.Contains(nameof(TestSet.SetA), exception.Message);
        Assert.Contains(nameof(TestSet.SetB), exception.Message);
    }

    [Fact]
    public void Build_SetOrderCycleBetweenUsedSets_ReportsTheSets()
    {
        app.AddSystemSet<TestSet>();
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.ConfigureSetOrder(TestSet.SetB, Order.Before, TestSet.SetA);

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"), new SystemDescriptor { Sets = [TestSet.SetA] });
        schedule.AddSystem(new RecordingSystem("B"), new SystemDescriptor { Sets = [TestSet.SetB] });

        var exception = Assert.Throws<InvalidOperationException>(() => schedule.Build());

        Assert.Contains(nameof(TestSet.SetA), exception.Message);
        Assert.Contains(nameof(TestSet.SetB), exception.Message);
    }

    [Fact]
    public void Build_SetOrderCycleWithoutMembers_IsStillReported()
    {
        app.AddSystemSet<TestSet>();
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);
        app.ConfigureSetOrder(TestSet.SetB, Order.Before, TestSet.SetA);

        // Nothing belongs to these sets, so the cycle constrains nothing at all. It is still a contradiction
        // in the configuration and must not be accepted just because it happens to be inert.
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"));

        Assert.Throws<InvalidOperationException>(() => schedule.Build());
    }

    [Fact]
    public void ClearSystems_ThenExecute_DoesNotRunTheOldSystems()
    {
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"));
        schedule.Execute();
        ExecutionRecorder.Clear();

        schedule.ClearSystems();
        schedule.Execute();

        Assert.Empty(ExecutionRecorder.GetOrder());
    }

    [Fact]
    public void Conflict_TwoWritersOfTheSameComponent_RunInDifferentLayers()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var first = new WriteSystem<TestPosition>("W1");
        var second = new WriteSystem<TestPosition>("W2");

        schedule.AddSystem(first);
        schedule.AddSystem(second);
        schedule.Build();

        Assert.True(NodeOf(schedule, first).Group < NodeOf(schedule, second).Group);
    }

    [Fact]
    public void NoConflict_TwoParameterlessSystems_ShareALayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var first = new RecordingSystem("A");
        var second = new RecordingSystem("B");

        schedule.AddSystem(first);
        schedule.AddSystem(second);
        schedule.Build();

        Assert.Equal(NodeOf(schedule, first).Group, NodeOf(schedule, second).Group);
    }

    [Fact]
    public void Conflict_WriteAfterRead_RunsInALaterLayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var reader = new ReadSystem<TestPosition>("R");
        var writer = new WriteSystem<TestPosition>("W");

        schedule.AddSystem(reader);
        schedule.AddSystem(writer);
        schedule.Build();

        Assert.True(NodeOf(schedule, reader).Group < NodeOf(schedule, writer).Group);
    }

    [Fact]
    public void Conflict_ReadAfterWrite_RunsInALaterLayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var writer = new WriteSystem<TestPosition>("W");
        var reader = new ReadSystem<TestPosition>("R");

        schedule.AddSystem(writer);
        schedule.AddSystem(reader);
        schedule.Build();

        Assert.True(NodeOf(schedule, writer).Group < NodeOf(schedule, reader).Group);
    }

    [Fact]
    public void NoConflict_TwoReadersOfTheSameComponent_ShareALayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var first = new ReadSystem<TestPosition>("R1");
        var second = new ReadSystem<TestPosition>("R2");

        schedule.AddSystem(first);
        schedule.AddSystem(second);
        schedule.Build();

        Assert.Equal(NodeOf(schedule, first).Group, NodeOf(schedule, second).Group);
    }

    [Fact]
    public void AddSystems_ArraySyntax_EveryMemberOfAGroupPrecedesTheNextGroup()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var a1 = new RecordingSystem("A1");
        var a2 = new RecordingSystem("A2");
        var b1 = new RecordingSystem("B1");

        schedule.AddSystems([a1, a2], [b1]);
        schedule.Build();

        var a1Node = NodeOf(schedule, a1);
        var a2Node = NodeOf(schedule, a2);
        var b1Node = NodeOf(schedule, b1);

        Assert.Equal(a1Node.Group, a2Node.Group);
        Assert.True(a1Node.Group < b1Node.Group);
        Assert.True(a2Node.Group < b1Node.Group);
    }

    [Fact]
    public void Build_SameLayerKeepsDeclarationOrder()
    {
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));
        schedule.AddSystem(new RecordingSystem("C"));

        schedule.Execute();

        // No constraint and no conflict, so they share a layer; the order inside a layer is not promised but
        // must stay stable, and declaration order is the one thing a reader can predict.
        Assert.Equal(["A", "B", "C"], ExecutionRecorder.GetOrder());
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

        // The graph is a resolution product, so it only exists after a build.
        schedule.Build();

        var list = schedule.ExecutionGraph.AsList();

        Assert.Single(list);
    }

    [Fact]
    public void ExecutionGraph_ThreeSystems_HasThreeNodes()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));
        schedule.AddSystem(new RecordingSystem("C"));

        schedule.Build();

        var list = schedule.ExecutionGraph.AsList();

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void ExecutionGraph_ClearSystems_ResetsGraph()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));

        schedule.Build();
        schedule.ClearSystems();

        // Clearing drops the resolved graph entirely; the next build recreates the root.
        Assert.Equal(0, schedule.ExecutionGraph.Count);
        Assert.False(schedule.IsBuilt);
    }

    [Fact]
    public void ExecutionGraph_EmptySchedule_IsEmpty()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.Build();

        // Nothing was added, so there is nothing to order — no placeholder node to carry around.
        Assert.Equal(0, schedule.ExecutionGraph.Count);
        Assert.True(schedule.IsBuilt);
    }

    [Fact]
    public void Build_RepeatedWithoutChanges_KeepsTheSameGraph()
    {
        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystem(new RecordingSystem("A"));
        schedule.AddSystem(new RecordingSystem("B"));

        schedule.Build();
        var firstNodes = schedule.ExecutionGraph.Nodes.ToList();

        schedule.Build();

        Assert.Equal(firstNodes, schedule.ExecutionGraph.Nodes);
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

#region Conflict Aware Layering

    [Fact]
    public void Build_SystemsThatOnlyRead_MergeIntoSameLayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var w1 = new WriteSystem<TestPosition>("W1");
        var r1 = new ReadSystem<TestPosition>("R1");
        var w2 = new WriteSystem<TestPosition>("W2");
        var r2 = new ReadSystem<TestPosition>("R2");

        schedule.AddSystem(w1);
        schedule.AddSystem(r1);
        schedule.AddSystem(w2);
        schedule.AddSystem(r2);

        schedule.Build();

        // R1 and R2 only read, so nothing needs them apart, not even the writer declared between them.
        Assert.Equal(NodeOf(schedule, r1).Group, NodeOf(schedule, r2).Group);
        Assert.True(NodeOf(schedule, w1).Group < NodeOf(schedule, r1).Group);
        Assert.True(NodeOf(schedule, w2).Group > NodeOf(schedule, r2).Group);
    }

    [Fact]
    public void Build_SystemWithoutConflicts_JoinsTheFirstLayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var writer = new WriteSystem<TestPosition>("W");
        var untouched = new WriteSystem<TestHealth>("H");

        schedule.AddSystem(writer);
        schedule.AddSystem(untouched);

        schedule.Build();

        Assert.Equal(NodeOf(schedule, writer).Group, NodeOf(schedule, untouched).Group);
    }

    [Fact]
    public void Build_ConflictingSystems_NeverShareALayer()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var first = new WriteSystem<TestPosition>("W1");
        var second = new WriteSystem<TestPosition>("W2");

        schedule.AddSystem(first);
        schedule.AddSystem(second);

        schedule.Build();

        Assert.NotEqual(NodeOf(schedule, first).Group, NodeOf(schedule, second).Group);
    }

    [Fact]
    public void AddAfter_PinsTheOrderOfAConflictingPair()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var w1 = new WriteSystem<TestPosition>("W1");
        var r1 = new ReadSystem<TestPosition>("R1");
        var w2 = new WriteSystem<TestPosition>("W2");
        var r2 = new ReadSystem<TestPosition>("R2");

        schedule.AddSystem(w1);
        schedule.AddSystem(r1);
        schedule.AddSystem(w2);
        // Declaring the constraint is what keeps R2 out of R1's layer: without it the packing is free to lift
        // R2 up next to R1, which would make it read the value from before W2 instead of after it.
        schedule.AddSystem(r2, new SystemDescriptor { AddAfter = w2 });

        schedule.Build();

        Assert.True(NodeOf(schedule, r2).Group > NodeOf(schedule, w2).Group);
    }

    [Fact]
    public void SetOrder_OppositeToDeclarationOrder_IsHonored()
    {
        app.AddSystemSet<TestSet>();
        app.ConfigureSetOrder(TestSet.SetA, Order.Before, TestSet.SetB);

        var schedule = new DefaultSchedule(app, "Test");
        var a = new WriteSystem<TestPosition>("A");
        var b = new WriteSystem<TestPosition>("B");

        // The declared order says B first, the set constraint says A first: the explicit constraint wins,
        // instead of the two of them meeting in a cycle.
        schedule.AddSystem(b, new SystemDescriptor { Sets = [TestSet.SetB] });
        schedule.AddSystem(a, new SystemDescriptor { Sets = [TestSet.SetA] });

        schedule.Build();

        Assert.True(NodeOf(schedule, a).Group < NodeOf(schedule, b).Group);
    }

    [Fact]
    public void AddAfterPointingAtALaterDeclaration_IsHonored()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var a = new WriteSystem<TestPosition>("A");
        var b = new WriteSystem<TestPosition>("B");

        schedule.AddSystem(a, new SystemDescriptor { AddAfter = b });
        schedule.AddSystem(b);

        schedule.Build();

        Assert.True(NodeOf(schedule, b).Group < NodeOf(schedule, a).Group);
    }

#endregion

#region Conflict Diagnosis

    [Fact]
    public void Ambiguities_UnorderedConflictingPair_IsReported()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var writer = new WriteSystem<TestPosition>("W");
        var reader = new ReadSystem<TestPosition>("R");

        schedule.AddSystem(writer);
        schedule.AddSystem(reader);

        schedule.Build();

        var pair = Assert.Single(schedule.Ambiguities);
        Assert.Same(writer, pair.A);
        Assert.Same(reader, pair.B);
    }

    [Fact]
    public void Ambiguities_ExplicitlyOrderedPair_IsNotReported()
    {
        var schedule = new DefaultSchedule(app, "Test");
        var writer = new WriteSystem<TestPosition>("W");
        var reader = new ReadSystem<TestPosition>("R");

        schedule.AddSystem(writer);
        schedule.AddSystem(reader, new SystemDescriptor { AddAfter = writer });

        schedule.Build();

        Assert.Empty(schedule.Ambiguities);
    }

    [Fact]
    public void Ambiguities_NonConflictingSystems_AreNotReported()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new ReadSystem<TestPosition>("R1"));
        schedule.AddSystem(new ReadSystem<TestPosition>("R2"));

        schedule.Build();

        Assert.Empty(schedule.Ambiguities);
    }

    [Fact]
    public void AmbiguityDetection_Ignore_ReportsNothing()
    {
        var schedule = new DefaultSchedule(app, "Test") { AmbiguityDetection = BasicSchedule.AmbiguityDetectionEnum.Ignore };

        schedule.AddSystem(new WriteSystem<TestPosition>("W"));
        schedule.AddSystem(new ReadSystem<TestPosition>("R"));

        schedule.Build();

        Assert.Empty(schedule.Ambiguities);
    }

    [Fact]
    public void AmbiguityDetection_Error_Throws()
    {
        var schedule = new DefaultSchedule(app, "Test") { AmbiguityDetection = BasicSchedule.AmbiguityDetectionEnum.Error };

        schedule.AddSystem(new WriteSystem<TestPosition>("W"));
        schedule.AddSystem(new ReadSystem<TestPosition>("R"));

        var exception = Assert.Throws<InvalidOperationException>(() => schedule.Build());

        Assert.Contains("Test", exception.Message);
        Assert.Contains(nameof(TestPosition), exception.Message);
    }

    [Fact]
    public void AmbiguityDetection_Warn_WritesToTheLogger()
    {
        var loggerFactory = new RecordingLoggerFactory();

        using var loggedApp = new App(new AppDescriptor { LoggerFactory = loggerFactory });
        var schedule = new DefaultSchedule(loggedApp, "Test");

        schedule.AddSystem(new WriteSystem<TestPosition>("W"));
        schedule.AddSystem(new ReadSystem<TestPosition>("R"));

        schedule.Build();

        var message = Assert.Single(loggerFactory.Messages);
        Assert.Contains("Test", message);
        Assert.Contains(nameof(TestPosition), message);
    }

    [Fact]
    public void Ambiguities_ClearSystems_Clears()
    {
        var schedule = new DefaultSchedule(app, "Test");

        schedule.AddSystem(new WriteSystem<TestPosition>("W"));
        schedule.AddSystem(new ReadSystem<TestPosition>("R"));
        schedule.Build();
        Assert.NotEmpty(schedule.Ambiguities);

        schedule.ClearSystems();

        Assert.Empty(schedule.Ambiguities);
    }

#endregion
}
