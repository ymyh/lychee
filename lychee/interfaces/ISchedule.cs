namespace lychee.interfaces;

public interface ISchedule
{
    public void AddSystem(ISystem system);

    public void Schedule(ResourcePool pool);
}
