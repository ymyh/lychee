using System.Reflection;
using lychee.attributes;
using lychee.collections;
using lychee.components;
using lychee.interfaces;
using lychee.utils;
using Microsoft.Extensions.Logging;

namespace lychee;

/// <summary>
/// Base class for system schedules, providing core functionality for adding and executing systems.
/// Systems are organized in a directed acyclic graph (DAG) for automatic parallelization.
/// Supports both single-threaded and multi-threaded execution modes.
/// </summary>
public abstract class BasicSchedule : ISchedule
{
    /// <summary>
    /// Defines when entity modifications are committed to the world.
    /// </summary>
    public enum CommitPointEnum
    {
        /// <summary>
        /// Commits changes after each synchronization point.
        /// In SingleThread mode, this means after every system execution.
        /// In MultiThread mode, this means after each group of parallel systems completes.
        /// </summary>
        Synchronization,

        /// <summary>
        /// Commits all changes once at the end of schedule execution.
        /// </summary>
        ScheduleEnd
    }

    /// <summary>
    /// Defines how systems within the schedule are executed.
    /// </summary>
    public enum ExecutionModeEnum
    {
        /// <summary>
        /// Executes all systems sequentially on a single thread.
        /// </summary>
        SingleThread,

        /// <summary>
        /// Executes independent systems in parallel across multiple threads.
        /// Systems are automatically grouped based on their access patterns.
        /// </summary>
        MultiThread,
    }

#region Public Properties

    /// <summary>
    /// The execution graph representing system dependencies and parallelization opportunities.
    /// You can inspect this graph to understand the execution order of systems.
    /// Direct modification is not recommended; use the provided APIs instead.
    /// </summary>
    public DirectedAcyclicGraph<SystemInfo> ExecutionGraph { get; } = new();

    public string Name { get; }

    /// <summary>
    /// Gets or sets when entity modifications are committed.
    /// </summary>
    public CommitPointEnum CommitPoint { get; set; }

    /// <summary>
    /// Gets or sets the execution mode for systems in this schedule.
    /// </summary>
    public ExecutionModeEnum ExecutionMode { get; set; }

#endregion

#region Private Fields

    /// <summary>
    /// The systems added so far, in declaration order. The execution graph is resolved from this list, so
    /// nothing here touches the graph.
    /// </summary>
    private readonly List<SystemInfo> pendingSystems = [];

    private FrozenDAGNode<SystemInfo>[][] frozenDagNodes = [];

    private Commands[][] multiThreadResults = [];

    private readonly App app;

    private readonly ILogger logger;

    private readonly List<Commands> entityCommanders = [];

    /// <summary>
    /// The <see cref="SystemSets.Version"/> the execution graph was last resolved from, or -1 when the graph
    /// does not correspond to the recorded systems (never built, or systems were added/cleared since).
    /// </summary>
    private int builtVersion = -1;

    private bool needConfigure = true;

    /// <summary>
    /// How many edges the expansion of set ordering into system ordering may produce before the schedule says
    /// so. Two sets of 100 members already mean 10000 edges, which is the point where resolving through virtual
    /// set nodes (linear instead of quadratic) starts to pay off.
    /// </summary>
    private const int SetExpansionEdgeThreshold = 10000;

#endregion

#region Constructors

    protected BasicSchedule(App app, string name, ExecutionModeEnum executionMode = ExecutionModeEnum.SingleThread, CommitPointEnum commitPoint = CommitPointEnum.Synchronization)
    {
        this.app = app;
        Name = name;
        CommitPoint = commitPoint;
        ExecutionMode = executionMode;
        logger = app.LoggerFactory.CreateLogger($"lychee.schedule.{name}");

        this.app.World.ArchetypeManager.ArchetypeCreated += () => { needConfigure = true; };

        ExecutionGraph.AddNode(new());
    }

#endregion

#region ISchedule Members

    public abstract void Execute();

#endregion

#region Public methods

    /// <summary>
    /// Adds a new system instance to the schedule and initializes it.
    /// </summary>
    /// <typeparam name="T">The system type, must implement ISystem and have a default constructor.</typeparam>
    /// <returns>The newly created and added system instance.</returns>
    public T AddSystem<[SystemConcept] T>() where T : ISystem, new()
    {
        return AddSystem(new T());
    }

    /// <summary>
    /// Adds a new system instance to the schedule, positioning it after a specified system.
    /// The system will be initialized upon addition.
    /// </summary>
    /// <param name="descriptor">The system descriptor.</param>
    /// <typeparam name="T">The system type, must implement ISystem and have a default constructor.</typeparam>
    /// <returns>The newly created and added system instance.</returns>
    public T AddSystem<[SystemConcept] T>(SystemDescriptor descriptor) where T : ISystem, new()
    {
        return AddSystem(new T(), descriptor);
    }

    /// <summary>
    /// Adds an existing system instance to the schedule.
    /// The system will be initialized upon addition.
    /// </summary>
    /// <param name="system">The system instance to add.</param>
    /// <param name="descriptor">Optional. The system descriptor.</param>
    /// <typeparam name="T">The system type, must implement ISystem.</typeparam>
    /// <returns>The system instance that was added.</returns>
    public T AddSystem<[SystemConcept] T>(T system, SystemDescriptor? descriptor = null) where T : ISystem
    {
        DoAddSystem(system, descriptor ?? new());
        return system;
    }

    /// <summary>
    /// Adds multiple systems to the schedule in the order specified by a nested value tuple.
    /// Systems at the same nesting level may execute in parallel if their access patterns allow.
    /// All systems must have a default constructor.
    /// </summary>
    /// <typeparam name="T">A value tuple containing system types. Nested tuples create hierarchical ordering.</typeparam>
    /// <param name="addAfter">Optional. The system after which the first system should execute.</param>
    /// <exception cref="ArgumentException">Thrown when T is not a value tuple or contains non-system types.</exception>
    /// <example>
    /// <code>
    /// // SysA executes first
    /// // SysB and SysC execute in parallel (if compatible) after SysA
    /// // SysD executes after both SysB and SysC complete
    /// schedule.AddSystems&lt;(SysA, (SysB, SysC), SysD)&gt;();
    /// </code>
    /// </example>
    public void AddSystems<T>(ISystem? addAfter = null)
    {
        AddSystems(typeof(T), addAfter);
    }

    /// <summary>
    /// Adds multiple systems to the schedule. Each argument is an array of systems representing
    /// a parallel group. Systems within the same array may execute in parallel if their access
    /// patterns allow. Sequential arrays execute in order.
    /// </summary>
    /// <param name="systemGroups">
    /// Variable number of system arrays. Each array represents a group of systems that can run in parallel.
    /// </param>
    /// <example>
    /// <code>
    /// // SysA and SysB run in parallel (if compatible)
    /// // SysC runs after both SysA and SysB complete
    /// schedule.AddSystems(null, [new SysA(), new SysB()], [new SysC()]);
    /// </code>
    /// </example>
    public void AddSystems(params ISystem[][] systemGroups)
    {
        var groupIndex = 0;

        foreach (var group in systemGroups)
        {
            if (group.Length == 0)
            {
                continue;
            }

            foreach (var system in group)
            {
                DoAddSystem(system, new(), groupIndex);
            }

            groupIndex++;
        }
    }

    /// <summary>
    /// Adds multiple systems to the schedule. Each argument is an array of systems representing
    /// a parallel group. Systems within the same array may execute in parallel if their access
    /// patterns allow. Sequential arrays execute in order.
    /// </summary>
    /// <param name="addAfter">Optional. The system after which the first system should execute.</param>
    /// <param name="systemGroups">
    /// Variable number of system arrays. Each array represents a group of systems that can run in parallel.
    /// </param>
    /// <example>
    /// <code>
    /// // SysA and SysB run in parallel (if compatible)
    /// // SysC runs after both SysA and SysB complete
    /// schedule.AddSystems(null, [new SysA(), new SysB()], [new SysC()]);
    /// </code>
    /// </example>
    public void AddSystems(ISystem? addAfter, params ISystem[][] systemGroups)
    {
        var groupIndex = 0;

        foreach (var group in systemGroups)
        {
            if (group.Length == 0)
            {
                continue;
            }

            foreach (var system in group)
            {
                // The supplied system orders the first group; every later group is ordered by its group index.
                DoAddSystem(system, new() { AddAfter = groupIndex == 0 ? addAfter : null }, groupIndex);
            }

            groupIndex++;
        }
    }

    /// <summary>
    /// Removes all systems from the schedule and discards the resolved execution graph.
    /// The next execution resolves the schedule again, so nothing from the cleared state is executed.
    /// </summary>
    public void ClearSystems()
    {
        pendingSystems.Clear();
        ExecutionGraph.Clear();
        frozenDagNodes = [];
        multiThreadResults = [];
        builtVersion = -1;
    }

    /// <summary>
    /// Resolves the recorded systems and set constraints into a layered execution graph, in one pass.
    /// Execution calls this automatically when something changed; calling it explicitly right after
    /// configuration surfaces ordering problems (a cycle, an unregistered set) at configuration time instead
    /// of at run time. Calling it again without any change does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a system declares a set type that was never registered, or when the recorded set ordering or
    /// nesting contains a cycle.
    /// </exception>
    /// <exception cref="InvalidGraphException">Thrown when the declared constraints cannot all be satisfied.</exception>
    public void Build()
    {
        if (IsBuilt)
        {
            return;
        }

        // Configuration only records constraints, so this is the single place that decides whether they hold.
        app.SystemSets.Validate();

        ExecutionGraph.Clear();
        frozenDagNodes = [];
        multiThreadResults = [];

        // The graph has to keep exactly one entry point: it is Nodes[0], which both the layer stripping and the
        // configuration pass rely on as the virtual root every system without a predecessor hangs off.
        var rootNode = ExecutionGraph.AddNode(new());

        var nodeList = new List<DAGNode<SystemInfo>>(pendingSystems.Count);
        var systemIndexDict = new Dictionary<ISystem, int>(ReferenceEqualityComparer.Instance);
        var membersDict = new Dictionary<SetInfo, List<DAGNode<SystemInfo>>>();

        // 1) Expand the sets of every system and record which systems belong to which set.
        for (var i = 0; i < pendingSystems.Count; i++)
        {
            var info = pendingSystems[i];

            info.EffectiveSets = app.SystemSets.GetEffectiveSystemSets(info.DirectSets);

            var node = ExecutionGraph.AddNode(new(info));
            nodeList.Add(node);
            systemIndexDict.TryAdd(info.System, i);

            foreach (var set in info.EffectiveSets)
            {
                if (!membersDict.TryGetValue(set, out var members))
                {
                    members = [];
                    membersDict[set] = members;
                }

                members.Add(node);
            }
        }

        // 2) Set ordering: every member of the earlier set runs before every member of the later one.
        var expansionEdgeCount = 0;

        foreach (var (before, after) in app.SystemSets.OrderEdges)
        {
            if (membersDict.TryGetValue(before, out var beforeNodes) && membersDict.TryGetValue(after, out var afterNodes))
            {
                expansionEdgeCount += AddGroupEdges(beforeNodes, afterNodes);
            }
        }

        if (expansionEdgeCount > SetExpansionEdgeThreshold)
        {
            ScheduleLog.SetExpansionLarge(logger, Name, expansionEdgeCount, SetExpansionEdgeThreshold);
        }

        // 3) Explicit dependencies declared through SystemDescriptor.AddAfter.
        for (var i = 0; i < pendingSystems.Count; i++)
        {
            var addAfter = pendingSystems[i].AddAfter;

            if (addAfter != null && systemIndexDict.TryGetValue(addAfter, out var index))
            {
                ExecutionGraph.TryAddEdge(nodeList[index], nodeList[i]);
            }
        }

        // 3') Groups declared through the array form of AddSystems run one after another.
        AddGroupIndexEdges(nodeList);

        // 4) Systems that touch the same parameter in a conflicting way keep their declaration order.
        AddConflictEdges(nodeList);

        // 5) Layer the result. Every system without a predecessor hangs off the virtual root, so the graph
        //     always has exactly one entry point no matter how many independent systems it holds.
        foreach (var node in nodeList)
        {
            if (node.Parents.Count == 0)
            {
                ExecutionGraph.TryAddEdge(rootNode, node);
            }
        }

        List<DAGNode<SystemInfo>> orderedList;

        try
        {
            orderedList = ExecutionGraph.AsList();
        }
        catch (InvalidGraphException)
        {
            throw new InvalidGraphException(DescribeUnsatisfiableOrdering(rootNode));
        }

        var declarationIndexDict = new Dictionary<SystemInfo, int>(pendingSystems.Count);

        for (var i = 0; i < pendingSystems.Count; i++)
        {
            declarationIndexDict[pendingSystems[i]] = i;
        }

        // Systems in the same layer have no constraint between them; keep their declaration order so that the
        // same configuration always produces the same order, even though it is not promised.
        frozenDagNodes = orderedList
            .Skip(1)
            .OrderBy(node => node.Group)
            .ThenBy(node => declarationIndexDict[node.Data])
            .Freeze()
            .AsExecutionGroup();

        multiThreadResults = new Commands[frozenDagNodes.Select(x => x.Length).DefaultIfEmpty(0).Max()][];

        builtVersion = app.SystemSets.Version;
    }

    /// <summary>
    /// Whether the execution graph matches the current systems and set constraints.
    /// </summary>
    public bool IsBuilt => builtVersion == app.SystemSets.Version;

#endregion

#region Private methods

    private void AddSystems(Type tupleType, ISystem? addAfter)
    {
        var types = TypeUtils.GetTupleTypes(tupleType);

        foreach (var type in types)
        {
            if (type.IsAssignableTo(typeof(ISystem)))
            {
                var system = (type.GetConstructor([])!.Invoke([]) as ISystem)!;
                DoAddSystem(system, new() { AddAfter = addAfter });
                addAfter = system;
            }
            else if (TypeUtils.IsValueTuple(type))
            {
                AddSystems(type, addAfter);
            }
            else
            {
                throw new ArgumentException($"Type {type} in {tupleType} is not a system type");
            }
        }
    }

    private (Type[] all, Type[] any, Type[] none) GetSystemFilter(ISystem system)
    {
        var filterAttr = system.GetType().GetCustomAttribute<SystemFilterAttribute>();
        if (filterAttr != null)
        {
            if (filterAttr.All.Any(x => x == typeof(Disabled)) || filterAttr.Any.Any(x => x == typeof(Disabled)))
            {
                return (filterAttr.All, filterAttr.Any, filterAttr.None);
            }

            return (filterAttr.All, filterAttr.Any, filterAttr.None.Append(typeof(Disabled)).ToArray());
        }

        return ([], [], [typeof(Disabled)]);
    }

    private void DoAddSystem(ISystem system, SystemDescriptor descriptor, int groupIndex = -1)
    {
        if (CheckIfMultiThread(system))
        {
            if (descriptor.ThreadCount <= 0 || descriptor.GroupSize <= 0)
            {
                throw new ArgumentException("SystemDescriptor.ThreadCount and SystemDescriptor.GroupSize must not be 0 when system is multi-threaded mode on");
            }
        }

        system.InitializeAG(app, descriptor);

        var (allFilter, anyFilter, noneFilter) = GetSystemFilter(system);

        pendingSystems.Add(new(system, ExtractSystemParamInfo(system, allFilter, anyFilter, noneFilter), new()
        {
            AllFilter = allFilter,
            AnyFilter = anyFilter,
            NoneFilter = noneFilter,
        }, descriptor.Sets, descriptor.AddAfter, groupIndex));

        // The recorded systems no longer match the resolved graph; the next Build has to redo it.
        builtVersion = -1;
    }

    /// <summary>
    /// Adds an ordering edge from every node of one group to every node of another, skipping pairs that are the
    /// same node because a system can belong to both sets. Returns how many edges the expansion added.
    /// </summary>
    private int AddGroupEdges(List<DAGNode<SystemInfo>> fromNodes, List<DAGNode<SystemInfo>> toNodes)
    {
        var addedCount = 0;

        foreach (var fromNode in fromNodes)
        {
            foreach (var toNode in toNodes)
            {
                if (fromNode != toNode && ExecutionGraph.TryAddEdge(fromNode, toNode))
                {
                    addedCount++;
                }
            }
        }

        return addedCount;
    }

    /// <summary>
    /// Orders the groups declared through the array form of AddSystems: each group runs entirely after the
    /// previous one.
    /// </summary>
    private void AddGroupIndexEdges(List<DAGNode<SystemInfo>> nodeList)
    {
        var groupDict = new Dictionary<int, List<DAGNode<SystemInfo>>>();

        for (var i = 0; i < pendingSystems.Count; i++)
        {
            var groupIndex = pendingSystems[i].GroupIndex;

            if (groupIndex < 0)
            {
                continue;
            }

            if (!groupDict.TryGetValue(groupIndex, out var groupNodes))
            {
                groupNodes = [];
                groupDict[groupIndex] = groupNodes;
            }

            groupNodes.Add(nodeList[i]);
        }

        // Group indexes are handed out without gaps, so the chain stops at the first index that is missing.
        for (var groupIndex = 1; groupDict.ContainsKey(groupIndex); groupIndex++)
        {
            AddGroupEdges(groupDict[groupIndex - 1], groupDict[groupIndex]);
        }
    }

    /// <summary>
    /// Orders systems that access the same parameter in a conflicting way by declaration order, so the
    /// resolution stays deterministic when no explicit constraint covers them.
    /// A write conflicts with everything, a read only with writes.
    /// </summary>
    private void AddConflictEdges(List<DAGNode<SystemInfo>> nodeList)
    {
        var lastWriterDict = new Dictionary<Type, int>();
        var readerDict = new Dictionary<Type, List<int>>();

        for (var i = 0; i < pendingSystems.Count; i++)
        {
            foreach (var parameter in pendingSystems[i].Parameters)
            {
                if (!readerDict.TryGetValue(parameter.Type, out var readerList))
                {
                    readerList = [];
                    readerDict[parameter.Type] = readerList;
                }

                // A read and a write both have to come after the last writer, and the writer chain is already
                // ordered, so chaining onto the last writer is enough to be after every conflicting system.
                if (lastWriterDict.TryGetValue(parameter.Type, out var lastWriter))
                {
                    ExecutionGraph.TryAddEdge(nodeList[lastWriter], nodeList[i]);
                }

                if (parameter.ReadOnly)
                {
                    readerList.Add(i);
                    continue;
                }

                // A write also has to come after every read that happened since the last write.
                foreach (var reader in readerList)
                {
                    ExecutionGraph.TryAddEdge(nodeList[reader], nodeList[i]);
                }

                readerList.Clear();
                lastWriterDict[parameter.Type] = i;
            }
        }
    }

    /// <summary>
    /// Describes the systems the virtual root cannot reach: they are part of an ordering cycle, or ordered
    /// after one. Naming them turns a generic graph failure into something that can be acted on.
    /// </summary>
    private string DescribeUnsatisfiableOrdering(DAGNode<SystemInfo> rootNode)
    {
        var reachedSet = new HashSet<DAGNode<SystemInfo>>();
        var pendingStack = new Stack<DAGNode<SystemInfo>>();

        reachedSet.Add(rootNode);
        pendingStack.Push(rootNode);

        while (pendingStack.Count > 0)
        {
            foreach (var child in pendingStack.Pop().Children)
            {
                if (reachedSet.Add(child))
                {
                    pendingStack.Push(child);
                }
            }
        }

        var unreachableNames = ExecutionGraph.Nodes
            .Where(node => node.Data != null && !reachedSet.Contains(node))
            .Select(node => DescribeSystem(node.Data!));

        return $"Schedule '{Name}' cannot be ordered with these constraints: {string.Join(", ", unreachableNames)} form an ordering cycle or depend on one";
    }

    private static string DescribeSystem(SystemInfo info)
    {
        var sets = info.DirectSets.Length > 0 ? string.Join(", ", info.DirectSets) : "none";

        return $"{info.System.GetType().Name} (sets: {sets})";
    }

    private SystemParameterInfo[] ExtractSystemParamInfo(ISystem system, Type[] allFilter, Type[] anyFilter, Type[] noneFilter)
    {
        var sysType = system.GetType();
        var method = sysType.GetMethod("Execute", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
        var parameters = method.GetParameters().Select(p =>
        {
            var resourceAttr = p.GetCustomAttribute<ResourceAttribute>();

            if (resourceAttr != null)
            {
                return p.ParameterType.IsValueType ? new(p.ParameterType, !p.ParameterType.IsByRef || p.IsIn, true) : new SystemParameterInfo(p.ParameterType, resourceAttr.ReadOnly, true);
            }

            return new(p.ParameterType, p.IsIn, false);
        }).ToArray();

        RegisterSystemComponent(parameters, allFilter, anyFilter, noneFilter);
        ValidateParameter(system, parameters);

        // Check this here because we need to make sure all types in descriptor are valid
        if (noneFilter.Length > 0)
        {
            if (parameters.Select(p => p.Type).Intersect(noneFilter).Any())
            {
                throw new ArgumentException($"System {system} has component parameter that also in NoneFilter");
            }
        }

        return parameters;
    }

    private void RegisterSystemComponent(SystemParameterInfo[] parameters, Type[] allFilter, Type[] anyFilter, Type[] noneFilter)
    {
        foreach (var param in parameters)
        {
            var type = param.Type;

            if (param.Type.IsGenericType)
            {
                var t = param.Type.GetGenericTypeDefinition();

                if (t == typeof(Span<>) || t == typeof(ReadOnlySpan<>))
                {
                    type = param.Type.GetGenericArguments()[0];
                }
            }

            if (param.Type.IsByRef)
            {
                type = param.Type.GetElementType()!;
            }

            if (type.GetInterface(typeof(IComponent).FullName!) != null)
            {
                app.TypeRegistrar.RegisterComponent(type);

                if (TypeUtils.IsEmptyStruct(type))
                {
                    throw new ArgumentException($"Type {type} as a component parameter is not supported, because it is an empty struct");
                }
            }
            else
            {
                app.TypeRegistrar.Register(type);
            }
        }

        foreach (var type in allFilter)
        {
            app.TypeRegistrar.RegisterComponent(type);
        }

        foreach (var type in anyFilter)
        {
            app.TypeRegistrar.RegisterComponent(type);
        }

        foreach (var type in noneFilter)
        {
            app.TypeRegistrar.RegisterComponent(type);
        }
    }

    private void Configure()
    {
        ExecutionGraph.ForEach(x =>
        {
            if (x != ExecutionGraph.Root)
            {
                x.Data.System.ConfigureAG(app, x.Data.FilterInfo);
            }
        });
    }

    private void Commit()
    {
        entityCommanders.ForEach(x => x.Commit());
        entityCommanders.Clear();

        if (needConfigure)
        {
            Configure();
            needConfigure = false;
        }
    }

#endregion

#region Private Static Methods

    private static void ValidateParameter(ISystem system, SystemParameterInfo[] parameters)
    {
        var spanTypeCount = 0;
        var componentTypeCount = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];

            if (param.IsResource)
            {
                continue;
            }

            var type = param.Type;

            if (type.IsGenericType)
            {
                if (type == typeof(Span<>) || type == typeof(ReadOnlySpan<>))
                {
                    parameters[i] = new(param.Type.GetGenericArguments()[0], type == typeof(ReadOnlySpan<>), false);
                    spanTypeCount++;
                    continue;
                }
            }

            componentTypeCount++;
        }

        if (spanTypeCount > 0 && componentTypeCount > 0)
        {
            throw new ArgumentException($"System {system} has both component/entity span parameter and component/entity parameter, which is not supported");
        }
    }

    private static bool CheckIfMultiThread(ISystem system)
    {
        var attr = system.GetType().GetCustomAttribute<AutoImplSystemAttribute>();
        return attr?.MultiThreaded ?? false;
    }

    /// <summary>
    /// Returns whether two systems access the same parameter in a way that forbids running them at the same
    /// time: a shared parameter counts unless both sides only read it, so read plus write is a conflict and
    /// write plus write is one too.
    /// Used to report systems that ended up in the same layer without any ordering constraint between them,
    /// which means their relative order is left to the scheduler.
    /// </summary>
    private static bool HasConflict(SystemInfo systemA, SystemInfo systemB)
    {
        // Only the parameter type may be hashed: entries that conflict are the ones with the same type, and
        // they must land in the same bucket for the equality check below to ever see them.
        return systemA.Parameters.Intersect(systemB.Parameters,
            EqualityComparer<SystemParameterInfo>.Create((a, b) =>
            {
                var same = a.Type == b.Type;
                if (same && a.ReadOnly && b.ReadOnly)
                {
                    return false;
                }

                return same;
            }, info => info.Type.GetHashCode())).Any();
    }

#endregion

#region Protected Methods

    /// <summary>
    /// Executes all systems in the schedule according to the DAG and execution mode.
    /// Derived classes should call this method from their Execute implementation.
    /// </summary>
    protected void DoExecute()
    {
        app.SystemSets.ComputeAllPredicates();
        Build();

        if (needConfigure)
        {
            Configure();
            needConfigure = false;
        }

        foreach (var group in frozenDagNodes)
        {
            foreach (var frozenDagNode in group)
            {
                var predicate = frozenDagNode.Data.System.Predicate(app.ResourcePool);

                foreach (var set in frozenDagNode.Data.EffectiveSets)
                {
                    if (app.SystemSets.SetPredicateResultDict.TryGetValue(set, out var setResult))
                    {
                        predicate &= setResult;
                    }
                }

                frozenDagNode.Data.Predicate = predicate;
            }

            var multiThread = false;

            for (var i = 0; i < group.Length; i++)
            {
                var node = group[i];
                var idx = i;
                multiThreadResults[idx] = [];

                if (!node.Data.Predicate)
                {
                    continue;
                }

                if (ExecutionMode == ExecutionModeEnum.SingleThread || group.Length == 1)
                {
                    entityCommanders.AddRange(node.Data.System.ExecuteAG());

                    if (CommitPoint == CommitPointEnum.Synchronization)
                    {
                        Commit();
                    }
                }
                else
                {
                    multiThread = true;
                    app.ThreadPool.Dispatch(_ => { multiThreadResults[idx] = node.Data.System.ExecuteAG(); });
                }
            }

            if (multiThread)
            {
                app.ThreadPool.Wait();

                for (var i = 0; i < group.Length; i++)
                {
                    if (multiThreadResults[i].Length > 0)
                    {
                        entityCommanders.AddRange(multiThreadResults[i]);
                    }
                }

                if (CommitPoint == CommitPointEnum.Synchronization)
                {
                    Commit();
                }
            }
        }

        if (CommitPoint == CommitPointEnum.ScheduleEnd)
        {
            Commit();
        }
    }

#endregion
}

/// <summary>
/// A default schedule implementation that executes all systems without any preconditions.
/// Systems are executed according to the DAG and execution mode settings.
/// </summary>
/// <param name="app">The ECS application instance.</param>
/// <param name="executionMode">The execution mode for systems (default: SingleThread).</param>
/// <param name="commitPoint">The commit point for entity modifications (default: Synchronization).</param>
public sealed class DefaultSchedule(
    App app,
    string name,
    BasicSchedule.ExecutionModeEnum executionMode = BasicSchedule.ExecutionModeEnum.SingleThread,
    BasicSchedule.CommitPointEnum commitPoint = BasicSchedule.CommitPointEnum.Synchronization)
    : BasicSchedule(app, name, executionMode, commitPoint)
{
#region BasicSchedule Implementation

    public override void Execute()
    {
        DoExecute();
    }

#endregion
}
