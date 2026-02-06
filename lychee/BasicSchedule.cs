using System.Reflection;
using lychee.attributes;
using lychee.collections;
using lychee.components;
using lychee.interfaces;
using lychee.utils;

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
    /// The execution graph representing system dependencies and parallelization opportunities.
    /// You can inspect this graph to understand the execution order of systems.
    /// Direct modification is not recommended; use the provided APIs instead.
    /// </summary>
    public DirectedAcyclicGraph<SystemInfo> ExecutionGraph { get; } = new();

    private FrozenDAGNode<SystemInfo>[][] frozenDagNodes = [];

    private readonly App app;

    private readonly List<Commands> entityCommanders = [];

    /// <summary>
    /// Gets or sets when entity modifications are committed.
    /// </summary>
    public CommitPointEnum CommitPoint { get; set; }

    /// <summary>
    /// Gets or sets the execution mode for systems in this schedule.
    /// </summary>
    public ExecutionModeEnum ExecutionMode { get; set; }

    private bool isFrozen;

    private bool needConfigure = true;

#region Static Members

    private static readonly MethodInfo AddSystemsMethod = typeof(BasicSchedule).GetMethod(nameof(AddSystems), BindingFlags.Public | BindingFlags.Instance, [typeof(ISystem)])!;

#endregion

#region Constructor

    protected BasicSchedule(App app, ExecutionModeEnum executionMode = ExecutionModeEnum.SingleThread, CommitPointEnum commitPoint = CommitPointEnum.Synchronization)
    {
        this.app = app;
        CommitPoint = commitPoint;
        ExecutionMode = executionMode;

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
    /// <param name="addAfter">The system after which the new system should execute.</param>
    /// <typeparam name="T">The system type, must implement ISystem and have a default constructor.</typeparam>
    /// <returns>The newly created and added system instance.</returns>
    public T AddSystem<[SystemConcept] T>(ISystem addAfter) where T : ISystem, new()
    {
        return AddSystem(new T(), addAfter);
    }

    /// <summary>
    /// Adds an existing system instance to the schedule.
    /// The system will be initialized upon addition.
    /// </summary>
    /// <param name="system">The system instance to add.</param>
    /// <param name="addAfter">Optional. The system after which this system should execute.</param>
    /// <typeparam name="T">The system type, must implement ISystem.</typeparam>
    /// <returns>The system instance that was added.</returns>
    public T AddSystem<[SystemConcept] T>(T system, ISystem? addAfter = null) where T : ISystem
    {
        DoAddSystem(system, addAfter);
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
        if (!TypeUtils.IsValueTuple<T>())
        {
            throw new ArgumentException($"Type {typeof(T).Name} is not a value tuple");
        }

        var types = TypeUtils.GetTupleTypes<T>();

        foreach (var type in types)
        {
            if (TypeUtils.IsValueTuple(type))
            {
                AddSystemsMethod.MakeGenericMethod(type).Invoke(this, [addAfter]);
                continue;
            }

            if (type.GetInterface("lychee.interfaces.ISystem") == null)
            {
                throw new ArgumentException($"Type {typeof(T).Name} is not a system type");
            }

            var ctor = type.GetConstructor([])!;
            var system = (ctor.Invoke([]) as ISystem)!;

            DoAddSystem(system, addAfter);
            addAfter = system;
        }
    }

    /// <summary>
    /// Removes all systems from the schedule and resets the execution graph.
    /// </summary>
    public void ClearSystems()
    {
        ExecutionGraph.Clear();
        ExecutionGraph.AddNode(new());
    }

#endregion

#region Private methods

    private (Type[] all, Type[] any, Type[] none) GetSystemFilter(ISystem system)
    {
        var filterAttr = system.GetType().GetCustomAttribute<SystemFilter>();
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

    private void DoAddSystem(ISystem system, ISystem? addAfter = null)
    {
        system.InitializeAG(app);
        var (allFilter, anyFilter, noneFilter) = GetSystemFilter(system);

        var node = new DAGNode<SystemInfo>(new(system, ExtractSystemParamInfo(system, allFilter, anyFilter, noneFilter), new()
        {
            AllFilter = allFilter,
            AnyFilter = anyFilter,
            NoneFilter = noneFilter,
        }));
        DAGNode<SystemInfo> addAfterNode = null!;

        isFrozen = false;

        var list = ExecutionGraph.AsList();
        var currentGroup = -1;

        foreach (var n in list)
        {
            addAfterNode = n;

            if (addAfter != null)
            {
                if (n.Data.System == addAfter)
                {
                    currentGroup = n.Group;
                    addAfter = null;
                }

                continue;
            }

            if (n != list[0] && CanRunParallel(n.Data, node.Data) && n.Group > currentGroup)
            {
                if (n.Parents.Count > 0)
                {
                    addAfterNode = n.Parents[0];
                }
            }
        }

        ExecutionGraph.AddNode(node);
        ExecutionGraph.AddEdge(addAfterNode, node);
    }

    private SystemParameterInfo[] ExtractSystemParamInfo(ISystem system, Type[] allFilter, Type[] anyFilter, Type[] noneFilter)
    {
        var sysType = system.GetType();
        var method = sysType.GetMethod("Execute", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
        var parameters = method.GetParameters().Select(p =>
        {
            var resourceAttr = p.GetCustomAttribute<Resource>();

            if (resourceAttr != null)
            {
                return p.ParameterType.IsValueType ? new(p.ParameterType, !p.ParameterType.IsByRef || p.IsIn, true) : new SystemParameterInfo(p.ParameterType, resourceAttr.ReadOnly, true);
            }

            return new(p.ParameterType, p.IsIn, false);
        }).ToArray();

        CheckAutoImplSystemAttribute(system, sysType);
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
                    throw new ArgumentException($"Type {type} as a component parameter is not supported, because it is an emtpy struct");
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

    private static void CheckAutoImplSystemAttribute(ISystem system, Type systemType)
    {
        var attribute = systemType.GetCustomAttribute<AutoImplSystem>();

        if (attribute != null)
        {
            var groupSize = attribute.GroupSize;
            var threadCount = attribute.ThreadCount;

            if ((groupSize > 0 && threadCount == 0) || (groupSize == 0 && threadCount > 0))
            {
                throw new ArgumentException($"System {system} has a invalid AutoImplSystem attribute parameter, they must both be greater than 0 or both be 0");
            }
        }
    }

    private void Configure()
    {
        ExecutionGraph.ForEach(x =>
        {
            if (x != ExecutionGraph.Root)
            {
                x.Data.System.ConfigureAG(app, x.Data.Descriptor);
            }
        });
    }

    private void Commit()
    {
        entityCommanders.ForEach(x => x.Commit());
        entityCommanders.Clear();
    }

    private static bool CanRunParallel(SystemInfo systemA, SystemInfo systemB)
    {
        return systemA.Parameters.Intersect(systemB.Parameters,
            EqualityComparer<SystemParameterInfo>.Create((a, b) =>
            {
                var same = a.Type == b.Type;
                if (same && a.ReadOnly && b.ReadOnly)
                {
                    return false;
                }

                return same;
            }, info => HashCode.Combine(info.Type.GetHashCode(), info.ReadOnly))).Any();
    }

#endregion

    /// <summary>
    /// Executes all systems in the schedule according to the DAG and execution mode.
    /// Derived classes should call this method from their Execute implementation.
    /// </summary>
    protected void DoExecute()
    {
        if (!isFrozen)
        {
            frozenDagNodes = ExecutionGraph.AsList().Skip(1).Freeze().AsExecutionGroup();
            isFrozen = true;
        }

        foreach (var group in frozenDagNodes)
        {
            if (needConfigure)
            {
                Configure();
                needConfigure = false;
            }

            foreach (var frozenDagNode in group)
            {
                if (ExecutionMode == ExecutionModeEnum.SingleThread)
                {
                    entityCommanders.AddRange(frozenDagNode.Data.System.ExecuteAG());
                }
                else
                {
                    app.ThreadPool.Dispatch(_ => { entityCommanders.AddRange(frozenDagNode.Data.System.ExecuteAG()); });
                }
            }

            if (ExecutionMode == ExecutionModeEnum.MultiThread)
            {
                app.ThreadPool.AsTask().Wait();
            }

            if (CommitPoint == CommitPointEnum.Synchronization)
            {
                Commit();
            }
        }

        if (CommitPoint == CommitPointEnum.ScheduleEnd)
        {
            Commit();
        }
    }
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
    BasicSchedule.ExecutionModeEnum executionMode = BasicSchedule.ExecutionModeEnum.SingleThread,
    BasicSchedule.CommitPointEnum commitPoint = BasicSchedule.CommitPointEnum.Synchronization)
    : BasicSchedule(app, executionMode, commitPoint)
{
    public override void Execute()
    {
        DoExecute();
    }
}
