using System.Reflection;
using lychee.attributes;
using lychee.collections;
using lychee.interfaces;

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

    public void AddSchedule(ISchedule schedule, ISchedule addAfterSchedule)
    {
        var index = schedules.IndexOf(schedule);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule} already exists");
        }

        index = schedules.IndexOf(addAfterSchedule);
        if (index == -1)
        {
            throw new ArgumentException($"Schedule {addAfterSchedule} not found");
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
    private readonly App app;

    private readonly Func<bool> shouldExecute;

    private readonly DirectedAcyclicGraph<SystemInfo> executionGraph = new();

    private FrozenDAGNode<SystemInfo>[][] frozenDAGNodes = [];

    private readonly List<Task> tasks = [];

    private readonly List<EntityCommander> entityCommanders = [];

    private bool isFrozen;

    private bool needConfigure;

#region Constructor

    public SimpleSchedule(App app, Func<bool> shouldExecute)
    {
        this.app = app;
        this.shouldExecute = shouldExecute;

        this.app.World.ArchetypeManager.ArchetypeCreated += () => { needConfigure = true; };
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

        if (executionGraph.Count == 0)
        {
            executionGraph.AddNode(node);
            return system;
        }

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

            if (CanRunParallel(n.Data, node.Data) && n.Group > currentGroup)
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
            if (param.ParameterType.IsByRef && param.ParameterType.GetElementType()!.GetInterface(typeof(IComponent).FullName!) != null)
            {
                app.TypeRegistry.RegisterComponent(param.ParameterType.GetElementType()!);
            }
            else if (param.ParameterType.GetInterface(typeof(IComponent).FullName!) != null)
            {
                app.TypeRegistry.RegisterComponent(param.ParameterType);
            }
            else
            {
                app.TypeRegistry.Register(param.ParameterType);
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
                return new SystemParameterInfo(p.ParameterType, !(bool)targetAttr.ConstructorArguments[0].Value!);
            }

            return new(p.ParameterType, p.IsIn);
        }).ToArray();
    }

    private static bool CanRunParallel(SystemInfo sysA, SystemInfo sysB)
    {
        var intersected = sysA.Parameters.Intersect(sysB.Parameters,
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

    private void Configure()
    {
        executionGraph.ForEach(x => x.Data.System.ConfigureAG(app, x.Data.Descriptor));
    }

#endregion

#region ISchedule Members

    public void Execute()
    {
        if (shouldExecute())
        {
            if (!isFrozen)
            {
                frozenDAGNodes = executionGraph.AsList().Freeze().AsExecutionGroup();
                isFrozen = true;
            }

            foreach (var group in frozenDAGNodes)
            {
                foreach (var frozenDagNode in group)
                {
                    tasks.Add(Task.Run(() => { entityCommanders.Add(frozenDagNode.Data.System.ExecuteAG()); }));
                }

                Task.WaitAll(tasks);
                tasks.Clear();

                entityCommanders.ForEach(x => x.CommitChanges());
                entityCommanders.Clear();

                if (needConfigure)
                {
                    Configure();
                    needConfigure = false;
                }
            }
        }
    }

#endregion
}
