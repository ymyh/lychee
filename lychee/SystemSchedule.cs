using System.Reflection;
using lychee.attributes;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class SystemSchedules
{
    private readonly List<ISchedule> schedules = [];

    public void AddSchedule(ISchedule schedule)
    {
        var index = schedules.IndexOf(schedule);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule} already exists");
        }

        schedules.Add(schedule);
    }

    public void AddSchedule(ISchedule schedule, ISchedule addAfter)
    {
        var index = schedules.IndexOf(schedule);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule} already exists");
        }

        index = schedules.IndexOf(addAfter);
        if (index == -1)
        {
            throw new ArgumentException($"Schedule {addAfter} not found");
        }

        schedules.Insert(index + 1, schedule);
    }

    public void Execute()
    {
        foreach (var schedule in schedules)
        {
            schedule.Execute();
        }
    }
}

public sealed class SimpleSchedule : ISchedule
{
    /// <summary>
    /// Indicates when the schedule should commit the changes.
    /// </summary>
    public enum CommitPointEnum
    {
        /// <summary>
        /// Commits the changes at every synchronization point.
        /// </summary>
        Synchronization,

        /// <summary>
        /// Commits the changes at the end of the schedule execution.
        /// </summary>
        ScheduleEnd
    }

    private readonly Func<bool> shouldExecute;

    private readonly DirectedAcyclicGraph<SystemInfo> executionGraph = new();

    private FrozenDAGNode<SystemInfo>[][] frozenDAGNodes = [];

    private readonly App app;

    private readonly List<Task> tasks = [];

    private readonly List<EntityCommander> entityCommanders = [];

    public CommitPointEnum CommitPoint { get; set; }

    private bool isFrozen;

    private bool needConfigure = true;

#region Constructor

    public SimpleSchedule(App app, Func<bool> shouldExecute, CommitPointEnum commitPointEnum = CommitPointEnum.Synchronization)
    {
        this.app = app;
        this.shouldExecute = shouldExecute;
        CommitPoint = commitPointEnum;

        this.app.World.ArchetypeManager.ArchetypeCreated += () => { needConfigure = true; };

        executionGraph.AddNode(new());
    }

#endregion

#region Public methods

    public T AddSystem<[SystemConcept, SealedRequired] T>() where T : ISystem, new()
    {
        return AddSystem(new T(), new());
    }

    public T AddSystem<[SystemConcept, SealedRequired] T>(SystemDescriptor descriptor) where T : ISystem, new()
    {
        return AddSystem(new T(), descriptor);
    }

    public T AddSystem<[SystemConcept, SealedRequired] T>(T system) where T : ISystem
    {
        return AddSystem(system, new());
    }

    public T AddSystem<[SystemConcept, SealedRequired] T>(T system, SystemDescriptor descriptor) where T : ISystem
    {
        system.InitializeAG(app);

        var systemParamInfo = ExtractSystemParamInfo(system, descriptor);
        var node = new DAGNode<SystemInfo>(new(system, systemParamInfo, descriptor));
        DAGNode<SystemInfo> addAfterNode = null!;

        isFrozen = false;

        var list = executionGraph.AsList();
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

        executionGraph.AddNode(node);
        executionGraph.AddEdge(addAfterNode, node);

        return system;
    }

#endregion

#region Private methods

    private SystemParameterInfo[] ExtractSystemParamInfo(ISystem system, SystemDescriptor descriptor)
    {
        var sysType = system.GetType();
        var method = sysType.GetMethod("Execute", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var parameters = method.GetParameters();

        foreach (var param in parameters)
        {
            var type = param.ParameterType;

            if (param.ParameterType.IsByRef)
            {
                type = param.ParameterType.GetElementType()!;
            }

            if (type.GetInterface(typeof(IComponent).FullName!) != null)
            {
                app.TypeRegistry.RegisterComponent(type);

                if (!TypeUtils.ContainsField(type))
                {
                    throw new ArgumentException($"Type {param} as a component parameter is not supported, because it doesn't contains any field");
                }
            }
            else
            {
                app.TypeRegistry.Register(type);
            }
        }

        foreach (var type in descriptor.AllFilter)
        {
            app.TypeRegistry.RegisterComponent(type);
        }

        foreach (var type in descriptor.AnyFilter)
        {
            app.TypeRegistry.RegisterComponent(type);
        }

        foreach (var type in descriptor.NoneFilter)
        {
            app.TypeRegistry.RegisterComponent(type);
        }

        // Check this here because we need to make sure all types in descriptor are valid
        if (descriptor.NoneFilter.Length > 0)
        {
            if (parameters.Select(p => p.ParameterType).Intersect(descriptor.NoneFilter).Any())
            {
                throw new ArgumentException($"System {system} has component parameter that also in NoneFilter");
            }
        }

        return parameters.Select(p =>
        {
            var targetAttr = p.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(Resource));

            if (targetAttr != null)
            {
                return new SystemParameterInfo(p.ParameterType, (bool)targetAttr.ConstructorArguments[0].Value!);
            }

            return new(p.ParameterType, p.IsIn);
        }).ToArray();
    }

    private void Configure()
    {
        executionGraph.ForEach(x =>
        {
            if (x != executionGraph.Root)
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
        var intersected = systemA.Parameters.Intersect(systemB.Parameters,
            EqualityComparer<SystemParameterInfo>.Create((a, b) =>
            {
                var same = a.Type == b.Type;
                if (same && a.ReadOnly && b.ReadOnly)
                {
                    return false;
                }

                return same;
            }, info => HashCode.Combine(info.Type.GetHashCode(), info.ReadOnly))).ToArray();

        return intersected.Length == 0;
    }

#endregion

#region ISchedule Members

    public void Execute()
    {
        if (!shouldExecute())
        {
            return;
        }

        if (!isFrozen)
        {
            frozenDAGNodes = executionGraph.AsList().Skip(1).Freeze().AsExecutionGroup();
            isFrozen = true;
        }

        if (needConfigure)
        {
            Configure();
            needConfigure = false;
        }

        foreach (var group in frozenDAGNodes)
        {
            foreach (var frozenDagNode in group)
            {
                tasks.Add(Task.Run(() => { entityCommanders.Add(frozenDagNode.Data.System.ExecuteAG()); }));
            }

            Task.WaitAll(tasks);
            tasks.Clear();

            if (CommitPoint == CommitPointEnum.Synchronization)
            {
                Commit();
            }

            if (needConfigure)
            {
                Configure();
                needConfigure = false;
            }
        }

        if (CommitPoint == CommitPointEnum.ScheduleEnd)
        {
            Commit();
        }
    }

#endregion
}
