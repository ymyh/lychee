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

    private readonly DirectedAcyclicGraph<ISystem> executionGraph = new();

    private List<FrozenDAGNode<ISystem>> frozenDAGNodes = [];

    public ISystem AddSystem(ISystem system)
    {
        var node = executionGraph.AddNode(new(system));
        return system;
    }

    public void Execute()
    {
        if (ShouldExecute(resourcePool))
        {
            frozenDAGNodes = executionGraph.FreezeAsList();
            throw new NotImplementedException();
        }
    }

    private void AnalyzeSystem(ISystem system)
    {
        var type = system.GetType();
        var method = type.GetMethod("Execute");
        var parameters = method!.GetParameters();

        foreach (var param in parameters)
        {
            if (param.ParameterType.IsByRef)
            {
                typeRegistry.GetOrRegister(param.ParameterType.GetElementType()!);
            }
            else
            {
                typeRegistry.GetOrRegister(param.ParameterType);
            }
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
    }
}
