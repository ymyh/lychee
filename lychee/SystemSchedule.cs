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
        var index = schedules.FindIndex(x => x.Name == schedule.Name);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule.Name} already exists");
        }

        schedules.Add(schedule);
    }

    public void AddSchedule(ISchedule schedule, string addAfterScheduleName)
    {
        var index = schedules.FindIndex(x => x.Name == addAfterScheduleName);
        if (index == -1)
        {
            throw new ArgumentException($"Schedule {addAfterScheduleName} not found");
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

    public void Configure()
    {
        foreach (var schedule in schedules)
        {
            schedule.Configure();
        }
    }
}

public sealed class DefaultSchedule(string name, App app, Func<bool> shouldExecute) : ISchedule
{
    public string Name { get; } = name;

    private readonly DirectedAcyclicGraph<SystemInfo> executionGraph = new();

    private FrozenDAGNode<SystemInfo>[][] frozenDAGNodes = [];

    private List<Task> tasks = [];

    private bool isFrozen;

    public T AddSystem<[SystemConcept] [SealedRequired] T>(T system) where T : ISystem
    {
        return AddSystem(system, new());
    }

    public T AddSystem<[SystemConcept] [SealedRequired] T>(T system, SystemDescriptor descriptor) where T : ISystem
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

    private SystemParameterInfo[] AnalyzeSystem(ISystem system, SystemDescriptor descriptor)
    {
        var sysType = system.GetType();
        var method = sysType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!;
        var parameters = method.GetParameters();

        foreach (var param in parameters)
        {
            app.World.TypeRegistry.RegisterComponent(param.ParameterType.IsByRef
                ? param.ParameterType.GetElementType()!
                : param.ParameterType);
        }

        foreach (var type in descriptor.AllFilter)
        {
            app.World.TypeRegistry.RegisterComponent(type);
        }

        foreach (var type in descriptor.AnyFilter)
        {
            app.World.TypeRegistry.RegisterComponent(type);
        }

        foreach (var type in descriptor.NoneFilter)
        {
            app.World.TypeRegistry.RegisterComponent(type);
        }

        return parameters.Select(p =>
        {
            var targetAttrs = p.CustomAttributes.Where(a =>
                a.AttributeType == typeof(ResReadOnly) || a.AttributeType == typeof(ResMut)).ToArray();

            return targetAttrs.Length switch
            {
                > 1 => throw new CustomAttributeFormatException(
                    $"Parameter {p.Name} has both ResReadOnly and ResMut attribute"),
                1 => new(p.ParameterType, targetAttrs[0].AttributeType == typeof(ResReadOnly)),
                _ => new SystemParameterInfo(p.ParameterType, p.IsIn)
            };
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
                    tasks.Add(Task.Run(() =>
                    {
                        frozenDagNode.Data.System.ExecuteAG();
                    }));
                }

                Task.WaitAll(tasks);
                tasks.Clear();
            }
        }
    }

    public void Configure()
    {
        executionGraph.AsList().ForEach(x => x.Data.System.ConfigureAG(app, x.Data.Descriptor));
    }

#endregion
}
