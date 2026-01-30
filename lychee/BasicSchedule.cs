using System.Reflection;
using lychee.attributes;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

/// <summary>
/// A base class for schedule. Providing basic functionality like add and execute system. <br/>
/// It provides two modes to execute systems, see <see cref="ExecutionModeEnum"/>.
/// </summary>
public abstract class BasicSchedule : ISchedule
{
    /// <summary>
    /// Indicates when the schedule should commit the changes.
    /// </summary>
    public enum CommitPointEnum
    {
        /// <summary>
        /// Commits changes at every synchronization point.
        /// For SingleThread execution mode, it will commit changes after every system execution.
        /// </summary>
        Synchronization,

        /// <summary>
        /// Commits changes at the end of the schedule execution.
        /// </summary>
        ScheduleEnd
    }

    /// <summary>
    /// Indicates how the schedule should execute the systems.
    /// </summary>
    public enum ExecutionModeEnum
    {
        /// <summary>
        /// Executes all systems in a single thread.
        /// </summary>
        SingleThread,

        /// <summary>
        /// Executes all systems in multiple threads.
        /// </summary>
        MultiThread,
    }

    /// <summary>
    /// The execution graph of systems. You can easily see the execution order of systems through here.
    /// We don't recommend you modify this graph directly, you should use other APIs to do this.
    /// Make sure you know what you are doing before modifying this.
    /// </summary>
    public DirectedAcyclicGraph<SystemInfo> ExecutionGraph { get; } = new();

    private FrozenDAGNode<SystemInfo>[][] frozenDagNodes = [];

    private readonly App app;

    private readonly List<Commands> entityCommanders = [];

    public CommitPointEnum CommitPoint { get; set; }

    public ExecutionModeEnum ExecutionMode { get; set; }

    private bool isFrozen;

    private bool needConfigure = true;

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
    /// Add a system to schedule. The added system will call <see cref="ISystem.InitializeAG"/>.
    /// </summary>
    /// <typeparam name="T">The type of system to add.</typeparam>
    /// <returns>The system just added.</returns>
    public T AddSystem<[SystemConcept] T>() where T : ISystem, new()
    {
        return AddSystem(new T(), new());
    }

    /// <summary>
    /// Add a system to schedule, with descriptor. The added system will call <see cref="ISystem.InitializeAG"/>.
    /// </summary>
    /// <param name="descriptor">The system descriptor</param>
    /// <typeparam name="T">The type of system to add.</typeparam>
    /// <returns>The system just added.</returns>
    public T AddSystem<[SystemConcept] T>(SystemDescriptor descriptor) where T : ISystem, new()
    {
        return AddSystem(new T(), descriptor);
    }

    /// <summary>
    /// Add a system to schedule. The added system will call <see cref="ISystem.InitializeAG"/>.
    /// </summary>
    /// <param name="system">The system to add.</param>
    /// <typeparam name="T">The system type.</typeparam>
    /// <returns>The system just added.</returns>
    public T AddSystem<[SystemConcept] T>(T system) where T : ISystem
    {
        return AddSystem(system, new());
    }

    /// <summary>
    /// Add a system to schedule, with descriptor. The added system will call <see cref="ISystem.InitializeAG"/>.
    /// </summary>
    /// <param name="system">The system to add.</param>
    /// <param name="descriptor">The system descriptor</param>
    /// <typeparam name="T">The type of system to add.</typeparam>
    /// <returns>The system just added</returns>
    public T AddSystem<[SystemConcept] T>(T system, SystemDescriptor descriptor) where T : ISystem
    {
        DoAddSystem(system, descriptor);
        return system;
    }

    /// <summary>
    /// Add systems into schedule in given order.
    /// </summary>
    /// <param name="systems"></param>
    public void AddSystems(params ISystem[] systems)
    {
        ISystem addAfter = null!;
        foreach (var system in systems)
        {
            DoAddSystem(system, new() { AddAfter = addAfter });
            addAfter = system;
        }
    }

    /// <summary>
    /// Add systems into schedule in given order. Generic parameter T must be a value tuple containing systems.
    /// These target systems must have a default constructor.
    /// </summary>
    /// <typeparam name="T">Systems in a value tuple.</typeparam>
    /// <exception cref="ArgumentException">T is not a value tuple</exception>
    public void AddSystems<T>()
    {
        if (!TypeUtils.IsValueTuple<T>())
        {
            throw new ArgumentException($"Type {typeof(T).Name} is not a value tuple");
        }

        var types = TypeUtils.GetTupleTypes<T>();

        ISystem addAfter = null!;
        foreach (var type in types)
        {
            if (type.GetInterface("lychee.interfaces.ISystem") == null)
            {
                throw new ArgumentException($"Type {typeof(T).Name} is not a system type");
            }

            var ctor = type.GetConstructor([])!;
            var system = (ctor.Invoke([]) as ISystem)!;

            DoAddSystem(system, new() { AddAfter = addAfter });
            addAfter = system;
        }
    }

    public void ClearSystems()
    {
        ExecutionGraph.Clear();
        ExecutionGraph.AddNode(new());
    }

#endregion

#region Private methods

    private void DoAddSystem(ISystem system, SystemDescriptor descriptor)
    {
        system.InitializeAG(app);

        var node = new DAGNode<SystemInfo>(new(system, ExtractSystemParamInfo(system, descriptor), descriptor));
        DAGNode<SystemInfo> addAfterNode = null!;

        isFrozen = false;

        var list = ExecutionGraph.AsList();
        var currentGroup = -1;

        foreach (var n in list)
        {
            addAfterNode = n;

            if (descriptor.AddAfter != null)
            {
                if (n.Data.System == descriptor.AddAfter)
                {
                    currentGroup = n.Group;
                    descriptor.AddAfter = null;
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

    private SystemParameterInfo[] ExtractSystemParamInfo(ISystem system, SystemDescriptor descriptor)
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
        RegisterSystemComponent(parameters, descriptor);
        ValidateParameter(system, parameters);

        // Check this here because we need to make sure all types in descriptor are valid
        if (descriptor.NoneFilter.Length > 0)
        {
            if (parameters.Select(p => p.Type).Intersect(descriptor.NoneFilter).Any())
            {
                throw new ArgumentException($"System {system} has component parameter that also in NoneFilter");
            }
        }

        return parameters;
    }

    private void RegisterSystemComponent(SystemParameterInfo[] parameters, SystemDescriptor descriptor)
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
                    throw new ArgumentException($"Type {param} as a component parameter is not supported, because it doesn't contains any field");
                }
            }
            else
            {
                app.TypeRegistrar.Register(type);
            }
        }

        foreach (var type in descriptor.AllFilter)
        {
            app.TypeRegistrar.RegisterComponent(type);
        }

        foreach (var type in descriptor.AnyFilter)
        {
            app.TypeRegistrar.RegisterComponent(type);
        }

        foreach (var type in descriptor.NoneFilter)
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
    /// Executes all systems in the schedule.
    /// Inherited class should call this method in their Execute method.
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
/// Execute with no condition.
/// </summary>
/// <param name="app">The ECS application.</param>
/// <param name="executionMode">The execution mode, default is SingleThread.</param>
/// <param name="commitPoint">The commit point, default is Synchronization.</param>
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
