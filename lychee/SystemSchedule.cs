using System.Reflection;
using lychee.attributes;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

public interface ISchedule
{
    public string Name { get; }

    public void Execute();
}

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
}

public sealed class DefaultSchedule(string name, TypeRegistry typeRegistry, ResourcePool resourcePool) : ISchedule
{
    public string Name { get; } = name;

    public Func<ResourcePool, bool> ShouldExecute = _ => true;

    private readonly DirectedAcyclicGraph<SystemInfo> executionGraph = new();

    private FrozenDAGNode<SystemInfo>[] frozenDAGNodes = [];

    private bool isFrozen;

    public T AddSystem<[SystemConcept] [SealedRequired] T>(T system) where T : ISystem
    {
        var systemParamInfo = AnalyzeSystem(system);
        var node = executionGraph.AddNode(new(new(system, systemParamInfo)));

        isFrozen = false;

        if (executionGraph.Count == 1)
        {
            return system;
        }

        var list = executionGraph.AsList();
        foreach (var n in list)
        {
            if (CanAddAfterSystem(n.Data, node.Data))
            {
                executionGraph.AddEdge(n, node);
            }
        }

        return system;
    }

    public void Execute()
    {
        if (ShouldExecute(resourcePool))
        {
            if (!isFrozen)
            {
                frozenDAGNodes = executionGraph.AsList().Freeze();
                isFrozen = true;
            }

            foreach (var frozenDagNode in frozenDAGNodes)
            {
                frozenDagNode.Data.System.ExecuteAG();
            }
        }
    }

    private SystemParameterInfo[] AnalyzeSystem(ISystem system)
    {
        var type = system.GetType();
        var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
        var parameters = method!.GetParameters();

        foreach (var param in parameters)
        {
            typeRegistry.GetOrRegister(param.ParameterType.IsByRef
                ? param.ParameterType.GetElementType()!
                : param.ParameterType);
        }

        foreach (var component in system.AllFilter)
        {
            typeRegistry.GetOrRegister(component.GetType());
        }

        foreach (var component in system.AnyFilter)
        {
            typeRegistry.GetOrRegister(component.GetType());
        }

        foreach (var component in system.NoneFilter)
        {
            typeRegistry.GetOrRegister(component.GetType());
        }

        return parameters.Select(x =>
                new SystemParameterInfo(x.ParameterType,
                    x.CustomAttributes.Any(a => a.AttributeType == typeof(ReadOnly))))
            .ToArray();
    }

    private static bool CanAddAfterSystem(SystemInfo info, SystemInfo tryAddAfterInfo)
    {
        var intersected = info.Parameters.Intersect(tryAddAfterInfo.Parameters,
            EqualityComparer<SystemParameterInfo>.Create((a, b) =>
            {
                var same = a!.Type == b!.Type;
                if (same && a.ReadOnly && b.ReadOnly)
                {
                    return false;
                }

                return same;
            })).ToArray();

        if (intersected.Length > 0)
        {
            return false;
        }

        return true;
    }
}
