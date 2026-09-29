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

    /// <summary>
    /// Defines how a schedule reports systems that access the same data in conflicting ways without being
    /// ordered by the configuration.
    /// </summary>
    public enum AmbiguityDetectionEnum
    {
        /// <summary>
        /// Skips the check. <see cref="Ambiguities"/> stays empty.
        /// </summary>
        Ignore,

        /// <summary>
        /// Logs a warning for every such pair and collects it in <see cref="Ambiguities"/>.
        /// </summary>
        Warn,

        /// <summary>
        /// Throws as soon as building the schedule finds such a pair.
        /// </summary>
        Error
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

    /// <summary>
    /// Gets or sets how this schedule reports systems that conflict without an ordering constraint between
    /// them. Their order is then decided by the schedule itself, by placing them in different layers.
    /// </summary>
    public AmbiguityDetectionEnum AmbiguityDetection { get; set; } = AmbiguityDetectionEnum.Warn;

    /// <summary>
    /// The conflicting pairs no recorded constraint orders, as of the last build. Every pair listed here was
    /// separated by the schedule rather than by the configuration, so naming them with an explicit constraint
    /// is what fixes the order. Empty while <see cref="AmbiguityDetection"/> is
    /// <see cref="AmbiguityDetectionEnum.Ignore"/>.
    /// </summary>
    public IReadOnlyList<(ISystem A, ISystem B)> Ambiguities => ambiguitiesList;

#endregion

#region Private Fields

    /// <summary>
    /// The systems added so far, in declaration order. The execution graph is resolved from this list, so
    /// nothing here touches the graph.
    /// </summary>
    private readonly List<SystemInfo> pendingSystems = [];

    /// <summary>
    /// The conflicting pairs the last build reported, in the order they were found. Filled only by a build and
    /// cleared by <see cref="ClearSystems"/>.
    /// </summary>
    private readonly List<(ISystem A, ISystem B)> ambiguitiesList = [];

    private FrozenDAGNode<SystemInfo>[][] frozenDagNodes = [];

    private Commands[][] multiThreadResults = [];

    /// <summary>
    /// Every set the systems of this schedule belong to, resolved to include ancestors. A build fills it in, and
    /// it is what lets the execution evaluate exactly the set predicates that can filter this schedule's
    /// systems, and no others.
    /// </summary>
    private SetInfo[] usedSets = [];

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
        DoAddSystem(system, descriptor ?? new(), []);
        return system;
    }

    /// <summary>
    /// Adds multiple systems to the schedule in the order specified by a value tuple. Each element of the tuple
    /// is a group that runs entirely after the previous one, and a nested tuple is a single group: the systems
    /// inside it are left unordered, so they run in parallel unless they conflict.
    /// All systems must have a default constructor.
    /// </summary>
    /// <typeparam name="T">A value tuple containing system types.</typeparam>
    /// <param name="addAfter">Optional. The system after which the first group should execute.</param>
    /// <exception cref="ArgumentException">Thrown when T is not a value tuple or contains non-system types.</exception>
    /// <example>
    /// <code>
    /// // SysA runs first, then SysB and SysC run in parallel (if compatible), then SysD runs.
    /// schedule.AddSystems&lt;(SysA, (SysB, SysC), SysD)&gt;();
    /// </code>
    /// </example>
    public void AddSystems<T>(ISystem? addAfter = null)
    {
        var groupList = new List<SystemEntry[]>();

        foreach (var type in TypeUtils.GetTupleTypes(typeof(T)))
        {
            var systemList = new List<ISystem>();

            CollectGroup(type, typeof(T), systemList);
            groupList.Add([.. systemList.Select(system => new SystemEntry(system, null))]);
        }

        AddSystemGroups(addAfter, [.. groupList]);
    }

    /// <summary>
    /// Adds multiple systems to the schedule. Each argument is an array of systems representing
    /// a parallel group. Systems within the same array may execute in parallel if their access
    /// patterns allow. Every member of a group runs after every member of the previous group.
    /// A member may carry a <see cref="SystemDescriptor"/> by writing it as a system/descriptor pair, which is
    /// how a system added this way joins sets, picks up an <see cref="SystemDescriptor.AddAfter"/> dependency or
    /// gets a thread count.
    /// </summary>
    /// <param name="systemGroups">
    /// Variable number of system arrays. Each array represents a group of systems that can run in parallel,
    /// and each member is a plain system or a system together with its descriptor.
    /// </param>
    /// <example>
    /// <code>
    /// // SysA and SysB run in parallel (if compatible); SysC runs after both of them.
    /// schedule.AddSystems([new SysA(), new SysB()], [new SysC()]);
    ///
    /// // A group that needs descriptors lists every member as a system/descriptor pair.
    /// schedule.AddSystems(
    ///     [(new SysA(), null), (new SysB(), new SystemDescriptor { Sets = [Stage.Sim] })],
    ///     [(new SysC(), null)]);
    /// </code>
    /// </example>
    public void AddSystems(params SystemEntry[][] systemGroups)
    {
        AddSystemGroups(null, systemGroups);
    }

    /// <summary>
    /// Adds multiple systems to the schedule. Each argument is an array of systems representing
    /// a parallel group. Systems within the same array may execute in parallel if their access
    /// patterns allow. Every member of a group runs after every member of the previous group.
    /// Every system is added with the default descriptor; use the <see cref="SystemEntry"/> overload to give
    /// one a descriptor.
    /// </summary>
    /// <param name="systemGroups">
    /// Variable number of system arrays. Each array represents a group of systems that can run in parallel.
    /// </param>
    /// <example>
    /// <code>
    /// // SysA and SysB run in parallel (if compatible)
    /// // SysC runs after both SysA and SysB complete
    /// schedule.AddSystems([new SysA(), new SysB()], [new SysC()]);
    /// </code>
    /// </example>
    public void AddSystems(params ISystem[][] systemGroups)
    {
        // A null group is carried into the shared loop below, which is where it is reported.
        AddSystemGroups(null, [.. systemGroups.Select(group => group == null ? null : Array.ConvertAll(group, system => new SystemEntry(system, null)))]);
    }

    /// <summary>
    /// Adds the groups of the array form one after another, which is what makes every member of a group run
    /// after every member of the group before it.
    /// </summary>
    /// <param name="addAfter">The system the first group should execute after, if any.</param>
    /// <param name="systemGroups">The groups to add, in order. An empty group is skipped.</param>
    /// <exception cref="ArgumentException">Thrown when a group is null: that is a mistake, not an empty group.</exception>
    private void AddSystemGroups(ISystem? addAfter, SystemEntry[]?[] systemGroups)
    {
        var previousSystems = Array.Empty<ISystem>();

        for (var i = 0; i < systemGroups.Length; i++)
        {
            var group = systemGroups[i] ?? throw new ArgumentException($"System group at index {i} is null", nameof(systemGroups));

            if (group.Length == 0)
            {
                continue;
            }

            foreach (var entry in group)
            {
                DoAddSystem(entry.System, entry.Descriptor ?? new(), addAfter == null ? previousSystems : [.. previousSystems, addAfter]);
            }

            previousSystems = [.. group.Select(entry => entry.System)];
            addAfter = null;
        }
    }

    /// <summary>
    /// Removes all systems from the schedule and discards the resolved execution graph.
    /// The next execution resolves the schedule again, so nothing from the cleared state is executed.
    /// </summary>
    public void ClearSystems()
    {
        pendingSystems.Clear();
        ambiguitiesList.Clear();
        ExecutionGraph.Clear();
        frozenDagNodes = [];
        multiThreadResults = [];
        usedSets = [];
        builtVersion = -1;
    }

    /// <summary>
    /// Resolves the recorded systems and set constraints into a layered execution graph, in one pass.
    /// Execution calls this automatically when something changed; calling it explicitly right after
    /// configuration surfaces ordering problems (a cycle, an unregistered set) at configuration time instead
    /// of at run time. Calling it again without any change does nothing.
    /// Systems are packed into the fewest layers the conflicts allow, so only the constraints the caller
    /// actually declared decide which system runs before which; conflicting pairs left unordered are separated
    /// by the schedule itself and reported through <see cref="AmbiguityDetection"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a system declares a set type that was never registered, when the recorded set ordering or
    /// nesting contains a cycle, or when <see cref="AmbiguityDetection"/> is
    /// <see cref="AmbiguityDetectionEnum.Error"/> and a conflicting pair has no ordering constraint.
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

        var nodeList = new List<DAGNode<SystemInfo>>(pendingSystems.Count);
        var systemIndexDict = new Dictionary<ISystem, int>(ReferenceEqualityComparer.Instance);
        var membersDict = new Dictionary<SetInfo, List<DAGNode<SystemInfo>>>();

        // Expand the sets of every system and record which systems belong to which set.
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

        // Set ordering: every member of the earlier set runs before every member of the later one.
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

        // Dependencies declared while adding: SystemDescriptor.AddAfter, and the order of the groups of
        // AddSystems. Both are recorded as "the systems this one runs after", so one loop covers them.
        for (var i = 0; i < pendingSystems.Count; i++)
        {
            foreach (var previousSystem in pendingSystems[i].PreviousSystems)
            {
                if (systemIndexDict.TryGetValue(previousSystem, out var index))
                {
                    ExecutionGraph.TryAddEdge(nodeList[index], nodeList[i]);
                }
            }
        }

        // Report the conflicting pairs the recorded constraints leave unordered. Only explicit constraints
        // are in the graph at this point, so a conflicting pair sharing a layer is a pair that nothing but
        // this schedule decides about. Skipping the check means skipping the ordering it reads, not just
        // the reporting.
        ambiguitiesList.Clear();

        if (AmbiguityDetection != AmbiguityDetectionEnum.Ignore)
        {
            DetectAmbiguities(ResolveOrder());
        }

        // Give every system the earliest layer it can take: after all of its explicit predecessors, and never
        // sharing a layer with a system it conflicts with. Systems that only read the same data, or that
        // touch disjoint data, share a layer instead of being serialized by their declaration order.
        var layerDict = AssignConflictAwareLayers(nodeList);

        // Write the layering back as edges, so the conflict order travels the same way as every other
        // constraint and the layers below come out of the graph rather than from a private side table.
        AddConflictEdges(nodeList, layerDict);

        // Layering the graph is what gives every node its group, and it is also the check that the constraints
        // can be satisfied at all. The list it returns holds the same nodes in execution order, which the
        // grouping below does not need: nodeList already holds them in declaration order.
        ResolveOrder();

        frozenDagNodes = FreezeExecutionLayers(nodeList);

        var maxLayerSize = 0;

        foreach (var layer in frozenDagNodes)
        {
            maxLayerSize = Math.Max(maxLayerSize, layer.Length);
        }

        multiThreadResults = new Commands[maxLayerSize][];

        // The sets this schedule's systems belong to, already resolved to include ancestors, so the execution
        // can evaluate their predicates without walking the nesting again.
        usedSets = [.. membersDict.Keys];

        builtVersion = app.SystemSets.Version;
    }

    /// <summary>
    /// Whether the execution graph matches the current systems and set constraints.
    /// </summary>
    public bool IsBuilt => builtVersion == app.SystemSets.Version;

#endregion

#region Private methods

    /// <summary>
    /// Collects the systems of one group: a system type on its own, or every system of a nested tuple, which is
    /// a single group however deeply it is nested.
    /// </summary>
    /// <param name="type">The system type or value tuple to collect from.</param>
    /// <param name="tupleType">The tuple being walked, for the error message.</param>
    /// <param name="groupList">The list the systems found are appended to.</param>
    /// <exception cref="ArgumentException">Thrown when the tuple contains something that is not a system type.</exception>
    private static void CollectGroup(Type type, Type tupleType, List<ISystem> groupList)
    {
        if (type.IsAssignableTo(typeof(ISystem)))
        {
            groupList.Add(InstantiateSystem(type));
            return;
        }

        if (!TypeUtils.IsValueTuple(type))
        {
            throw new ArgumentException($"Type {type} in {tupleType} is not a system type");
        }

        foreach (var nestedType in TypeUtils.GetTupleTypes(type))
        {
            CollectGroup(nestedType, tupleType, groupList);
        }
    }

    private static ISystem InstantiateSystem(Type type)
    {
        return (ISystem)type.GetConstructor([])!.Invoke([])!;
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

    /// <summary>
    /// Records a system as declared, together with everything it was told to run after.
    /// </summary>
    /// <param name="system">The system to record.</param>
    /// <param name="descriptor">Its descriptor, which may add a dependency of its own.</param>
    /// <param name="previousSystems">The systems of the group it was declared in after, empty when there is none.</param>
    private void DoAddSystem(ISystem system, SystemDescriptor descriptor, ISystem[] previousSystems)
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
        }, descriptor.Sets, descriptor.AddAfter == null ? previousSystems : [.. previousSystems, descriptor.AddAfter]));

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
    /// Orders conflicting systems the way the layers were assigned, so the graph reproduces the layering that
    /// <see cref="AssignConflictAwareLayers"/> decided. A write conflicts with everything, a read only with
    /// writes.
    /// </summary>
    private void AddConflictEdges(List<DAGNode<SystemInfo>> nodeList, Dictionary<DAGNode<SystemInfo>, int> layerDict)
    {
        var lastWriterDict = new Dictionary<Type, int>();
        var readerDict = new Dictionary<Type, List<int>>();

        // Systems are visited layer by layer, so every edge added here points forward in the layering and can
        // never close a cycle. The sort is stable, which keeps systems of one layer in declaration order.
        var visitOrderList = Enumerable.Range(0, nodeList.Count)
            .OrderBy(index => layerDict[nodeList[index]])
            .ToList();

        foreach (var i in visitOrderList)
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
    /// Turns the laid-out graph into the layers the execution loop walks: one array of frozen nodes per layer,
    /// in layer order, each holding its systems in declaration order.
    /// Resolving the graph leaves every node carrying its layer as <see cref="DAGNode{T}.Group"/>, and
    /// <paramref name="nodeList"/> holds the nodes in declaration order, so walking it once and dropping each
    /// node into the bucket of its layer is all it takes. The result is the order a sort by layer and then by
    /// declaration index would give, without the sort: layers come out in order because the buckets are
    /// numbered by group, and a layer holds its systems in declaration order because that is the order they
    /// were put in. Nothing else decides the order inside a layer, so it has to come from somewhere
    /// reproducible, and declaration order is that somewhere.
    /// </summary>
    /// <param name="nodeList">The nodes of the resolved graph, in declaration order.</param>
    /// <returns>One array of frozen nodes per layer, in layer order. Empty when there are no systems.</returns>
    private static FrozenDAGNode<SystemInfo>[][] FreezeExecutionLayers(List<DAGNode<SystemInfo>> nodeList)
    {
        var maxGroup = -1;

        foreach (var node in nodeList)
        {
            maxGroup = Math.Max(maxGroup, node.Group);
        }

        // Layers are numbered from zero without gaps, so indexing by group orders them and keeps each whole.
        var layerList = new List<DAGNode<SystemInfo>>[maxGroup + 1];

        for (var i = 0; i <= maxGroup; i++)
        {
            layerList[i] = [];
        }

        foreach (var node in nodeList)
        {
            layerList[node.Group].Add(node);
        }

        var frozenLayerList = new FrozenDAGNode<SystemInfo>[layerList.Length][];

        for (var i = 0; i < layerList.Length; i++)
        {
            var layer = layerList[i];
            var frozenLayer = new FrozenDAGNode<SystemInfo>[layer.Count];

            for (var j = 0; j < layer.Count; j++)
            {
                frozenLayer[j] = new FrozenDAGNode<SystemInfo>(layer[j]);
            }

            frozenLayerList[i] = frozenLayer;
        }

        return frozenLayerList;
    }

    /// <summary>
    /// Packs the systems into layers: each takes the earliest layer that comes after everything it was
    /// explicitly ordered behind and that holds no system it conflicts with. Systems that only read the same
    /// data, or that touch disjoint data, therefore share a layer and run in parallel.
    /// The result is a deterministic function of the constraints and the declaration order, but it is a greedy
    /// packing: it keeps the layers few without promising the minimum.
    /// The graph must be acyclic; <see cref="ResolveOrder"/> has already checked that.
    /// </summary>
    /// <returns>The layer of every node, the first one being zero.</returns>
    private Dictionary<DAGNode<SystemInfo>, int> AssignConflictAwareLayers(List<DAGNode<SystemInfo>> nodeList)
    {
        var declarationIndexDict = new Dictionary<DAGNode<SystemInfo>, int>(nodeList.Count);

        for (var i = 0; i < nodeList.Count; i++)
        {
            declarationIndexDict[nodeList[i]] = i;
        }

        var layerDict = new Dictionary<DAGNode<SystemInfo>, int>(ExecutionGraph.Count);
        var inDegreeDict = new Dictionary<DAGNode<SystemInfo>, int>(ExecutionGraph.Count);
        var readyQueue = new PriorityQueue<DAGNode<SystemInfo>, int>();
        var writerLayerDict = new Dictionary<Type, HashSet<int>>();
        var readerLayerDict = new Dictionary<Type, HashSet<int>>();

        foreach (var node in ExecutionGraph.Nodes)
        {
            var inDegree = node.Parents.Count;
            inDegreeDict[node] = inDegree;

            if (inDegree == 0)
            {
                // Placing the earliest declared ready system first keeps the packing reproducible.
                readyQueue.Enqueue(node, declarationIndexDict[node]);
            }
        }

        while (readyQueue.Count > 0)
        {
            var node = readyQueue.Dequeue();
            var layer = 0;

            foreach (var parent in node.Parents)
            {
                layer = Math.Max(layer, layerDict[parent] + 1);
            }

            while (IsLayerBlocked(node.Data, layer, writerLayerDict, readerLayerDict))
            {
                layer++;
            }

            layerDict[node] = layer;
            RegisterLayerUsage(node.Data, layer, writerLayerDict, readerLayerDict);

            foreach (var child in node.Children)
            {
                inDegreeDict[child]--;

                if (inDegreeDict[child] == 0)
                {
                    readyQueue.Enqueue(child, declarationIndexDict[child]);
                }
            }
        }

        return layerDict;
    }

    /// <summary>
    /// Returns whether a layer already holds a system that would conflict with <paramref name="info"/>: a
    /// writer of a parameter blocks any use of it, a reader blocks only a write.
    /// </summary>
    private static bool IsLayerBlocked(SystemInfo info, int layer, Dictionary<Type, HashSet<int>> writerLayerDict, Dictionary<Type, HashSet<int>> readerLayerDict)
    {
        foreach (var parameter in info.Parameters)
        {
            if (GetOrAddLayerSet(writerLayerDict, parameter.Type).Contains(layer))
            {
                return true;
            }

            if (!parameter.ReadOnly && GetOrAddLayerSet(readerLayerDict, parameter.Type).Contains(layer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records the layer as used by <paramref name="info"/>, as a reader or as a writer of each of its
    /// parameters, so that later systems can tell whether a layer is still free for them.
    /// </summary>
    private static void RegisterLayerUsage(SystemInfo info, int layer, Dictionary<Type, HashSet<int>> writerLayerDict, Dictionary<Type, HashSet<int>> readerLayerDict)
    {
        foreach (var parameter in info.Parameters)
        {
            GetOrAddLayerSet(parameter.ReadOnly ? readerLayerDict : writerLayerDict, parameter.Type).Add(layer);
        }
    }

    /// <summary>
    /// Gets the layers a parameter type is already used in, creating the set on first use.
    /// </summary>
    private static HashSet<int> GetOrAddLayerSet(Dictionary<Type, HashSet<int>> layerSetDict, Type parameterType)
    {
        if (!layerSetDict.TryGetValue(parameterType, out var layerSet))
        {
            layerSet = [];
            layerSetDict[parameterType] = layerSet;
        }

        return layerSet;
    }

    /// <summary>
    /// Reports the conflicting pairs no recorded constraint orders, and collects them in
    /// <see cref="Ambiguities"/>. Only explicit constraints are in the graph when this runs, so two systems in
    /// one layer are unreachable from each other: nothing but this schedule decides which of them goes first.
    /// </summary>
    private void DetectAmbiguities(List<DAGNode<SystemInfo>> explicitOrderList)
    {
        // The nodes come out layer by layer, so a change of group starts the next layer.
        var layerGroupList = new List<(int Group, List<SystemInfo> Members)>();

        foreach (var node in explicitOrderList)
        {
            if (layerGroupList.Count == 0 || layerGroupList[^1].Group != node.Group)
            {
                layerGroupList.Add((node.Group, []));
            }

            layerGroupList[^1].Members.Add(node.Data);
        }

        foreach (var (group, members) in layerGroupList)
        {
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var parameterType = FindConflictingParameterType(members[i], members[j]);

                    if (parameterType == null)
                    {
                        continue;
                    }

                    if (AmbiguityDetection == AmbiguityDetectionEnum.Error)
                    {
                        throw new InvalidOperationException($"Schedule '{Name}' has ambiguous systems in group {group}: {DescribeSystem(members[i])} and {DescribeSystem(members[j])} both access {parameterType.Name} with no ordering constraint between them");
                    }

                    ScheduleLog.AmbiguousSystems(logger, Name, group, DescribeSystem(members[i]), DescribeSystem(members[j]), parameterType.Name);
                    ambiguitiesList.Add((members[i].System, members[j].System));
                }
            }
        }
    }

    /// <summary>
    /// Layers the graph and returns its nodes in execution order, replacing the generic graph failure with a
    /// message that names the systems that could not be ordered.
    /// </summary>
    private List<DAGNode<SystemInfo>> ResolveOrder()
    {
        try
        {
            return ExecutionGraph.AsList();
        }
        catch (InvalidGraphException)
        {
            throw new InvalidGraphException(DescribeUnsatisfiableOrdering());
        }
    }

    /// <summary>
    /// Describes the systems that cannot be ordered: they are part of an ordering cycle, or ordered after one.
    /// Naming them turns a generic graph failure into something that can be acted on.
    /// </summary>
    private string DescribeUnsatisfiableOrdering()
    {
        // A topological sort that cannot finish leaves behind exactly the nodes in a cycle and the nodes behind
        // them, which is the set worth naming.
        var inDegreeDict = new Dictionary<DAGNode<SystemInfo>, int>(ExecutionGraph.Count);
        var readyQueue = new Queue<DAGNode<SystemInfo>>();

        foreach (var node in ExecutionGraph.Nodes)
        {
            var inDegree = node.Parents.Count;
            inDegreeDict[node] = inDegree;

            if (inDegree == 0)
            {
                readyQueue.Enqueue(node);
            }
        }

        while (readyQueue.Count > 0)
        {
            foreach (var child in readyQueue.Dequeue().Children)
            {
                if (--inDegreeDict[child] == 0)
                {
                    readyQueue.Enqueue(child);
                }
            }
        }

        var stuckNames = ExecutionGraph.Nodes
            .Where(node => inDegreeDict[node] > 0)
            .Select(node => DescribeSystem(node.Data));

        return $"Schedule '{Name}' cannot be ordered with these constraints: {string.Join(", ", stuckNames)} form an ordering cycle or depend on one";
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
        ExecutionGraph.ForEach(x => x.Data.System.ConfigureAG(app, x.Data.FilterInfo));
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

        app.World.SwapEvents(EventPublishTiming.CommitPoint);
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
    /// Finds the component both systems touch in a way that forbids running them at the same time: a shared
    /// parameter counts unless both sides only read it, so read plus write is a conflict and write plus write is
    /// one too.
    /// </summary>
    /// <returns>The component type they conflict on, or null when they can share a layer.</returns>
    private static Type? FindConflictingParameterType(SystemInfo systemA, SystemInfo systemB)
    {
        foreach (var parameterA in systemA.Parameters)
        {
            foreach (var parameterB in systemB.Parameters)
            {
                if (parameterA.Type != parameterB.Type || (parameterA.ReadOnly && parameterB.ReadOnly))
                {
                    continue;
                }

                // Component parameters arrive as by-ref types, which is not how the component is written down.
                return parameterA.Type.IsByRef ? parameterA.Type.GetElementType()! : parameterA.Type;
            }
        }

        return null;
    }

#endregion

#region Protected Methods

    /// <summary>
    /// Executes all systems in the schedule according to the DAG and execution mode.
    /// Derived classes should call this method from their Execute implementation.
    /// </summary>
    protected void DoExecute()
    {
        // Before the build there is no knowing which sets this schedule's systems belong to, and a build that
        // fails has nothing worth evaluating predicates for, so the evaluation waits until the graph is resolved.
        Build();

        app.SystemSets.ComputePredicates(usedSets);

        if (needConfigure)
        {
            Configure();
            needConfigure = false;
        }

        foreach (var group in frozenDagNodes)
        {
            var multiThread = false;

            for (var i = 0; i < group.Length; i++)
            {
                var node = group[i];
                var idx = i;
                multiThreadResults[idx] = [];

                // A system runs unless a set it belongs to says otherwise. The sets were resolved by the build,
                // and their results by ComputePredicates, so this is only a lookup per set, done where the
                // decision is used rather than in a pass of its own.
                var predicate = true;

                foreach (var set in node.Data.EffectiveSets)
                {
                    if (app.SystemSets.SetPredicateResultDict.TryGetValue(set, out var setResult))
                    {
                        predicate &= setResult;
                    }
                }

                if (!predicate)
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
