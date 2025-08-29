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

public sealed class DefaultSchedule(string name, TypeRegistry typeRegistry, Func<bool> shouldExecute) : ISchedule
{
    public string Name { get; } = name;

    private readonly Func<bool> shouldExecute = shouldExecute;

    private readonly DirectedAcyclicGraph<SystemInfo> executionGraph = new();

    private FrozenDAGNode<SystemInfo>[] frozenDAGNodes = [];

    private bool isFrozen;

    public T AddSystem<[SystemConcept] [SealedRequired] T>(T system) where T : ISystem
    {
        return AddSystem(system, new SystemDescriptor());
    }

    public T AddSystem<[SystemConcept] [SealedRequired] T>(T system, SystemDescriptor descriptor) where T : ISystem
    {
        var systemParamInfo = AnalyzeSystem(system, descriptor);
        var node = executionGraph.AddNode(new(new(system, systemParamInfo)));

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

    public void Execute()
    {
        if (shouldExecute())
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

    private SystemParameterInfo[] AnalyzeSystem(ISystem system, SystemDescriptor descriptor)
    {
        var sysType = system.GetType();
        var method = sysType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
        var parameters = method!.GetParameters();

        foreach (var param in parameters)
        {
            typeRegistry.GetOrRegister(param.ParameterType.IsByRef
                ? param.ParameterType.GetElementType()!
                : param.ParameterType);
        }

        foreach (var type in descriptor.AllFilter)
        {
            typeRegistry.GetOrRegister(type);
        }

        foreach (var type in descriptor.AnyFilter)
        {
            typeRegistry.GetOrRegister(type);
        }

        foreach (var type in descriptor.NoneFilter)
        {
            typeRegistry.GetOrRegister(type);
        }

        return parameters.Where(x =>
                x.CustomAttributes.All(a =>
                    a.AttributeType != typeof(ResReadOnly) && a.AttributeType != typeof(ResMut))).Select(x =>
                new SystemParameterInfo(x.ParameterType,
                    x.CustomAttributes.Any(a => a.AttributeType == typeof(ReadOnly))))
            .ToArray();
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

        if (intersected.Length > 0)
        {
            return false;
        }

        return true;
    }
}
