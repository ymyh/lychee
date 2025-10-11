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

    public T AddSystem<[SystemConcept, SealedRequired] T>(T system) where T : ISystem
    {
        return AddSystem(system, new());
    }

    public T AddSystem<[SystemConcept, SealedRequired] T>(T system, SystemDescriptor descriptor) where T : ISystem
    {
        system.InitializeAG(app);

        var systemParamInfo = AnalyzeSystem(system, descriptor);
        var node = executionGraph.AddNode(new(new(system, systemParamInfo, descriptor)));

        isFrozen = false;

        if (executionGraph.Count == 1)
        {
            return system;
        }

        var list = executionGraph.AsList();
        foreach (var n in list)
        {
            if (!CanParallelWithSystem(n.Data, node.Data))
            {
                executionGraph.AddEdge(n, node);
            }
            else if (n.Parents.Count > 0)
            {
                executionGraph.AddEdge(n.Parents[0], node);
            }
        }

        return system;
    }

#endregion

#region Private methods

    private SystemParameterInfo[] AnalyzeSystem(ISystem system, SystemDescriptor descriptor)
    {
        var sysType = system.GetType();
        var method = sysType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!;
        var parameters = method.GetParameters();

        foreach (var param in parameters)
        {
            if (param.ParameterType.IsByRef)
            {
                app.TypeRegistry.RegisterComponent(param.ParameterType.GetElementType()!);
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

        return parameters.Select(p =>
        {
            var targetAttrs = p.CustomAttributes.Where(a => a.AttributeType == typeof(Resource));

            if (targetAttrs.Count() == 1)
            {
                return new(p.ParameterType, !(bool)targetAttrs.First().ConstructorArguments[0].Value!);
            }

            return new SystemParameterInfo(p.ParameterType, p.IsIn);
        }).ToArray();
    }

    private static bool CanParallelWithSystem(SystemInfo info, SystemInfo tryAddAfterInfo)
    {
        var intersected = info.Parameters.Intersect(tryAddAfterInfo.Parameters,
            EqualityComparer<SystemParameterInfo>.Create((a, b) =>
            {
                var same = a.Type == b.Type;
                if (same && a.ReadOnly && b.ReadOnly)
                {
                    return false;
                }

                return same;
            })).ToArray();

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
                    tasks.Add(Task.Run(() => { frozenDagNode.Data.System.ExecuteAG(); }));
                }

                Task.WaitAll(tasks);
                tasks.Clear();

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
