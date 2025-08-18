namespace lychee.interfaces;

public interface ISystemScheduler
{
    public Span<ISchedule> Schedules { get; }

    public void AddSchedule(ISchedule schedule);

    public void Execute();
}
