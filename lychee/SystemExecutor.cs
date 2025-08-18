using System.Runtime.InteropServices;
using lychee.interfaces;

namespace lychee;

public sealed class SystemScheduler(ResourcePool pool) : ISystemScheduler
{
    private readonly List<ISchedule> schedules = [];

    public Span<ISchedule> Schedules => CollectionsMarshal.AsSpan(schedules);

    public void AddSchedule(ISchedule schedule)
    {
        schedules.Add(schedule);
    }

    public void Execute()
    {
        foreach (var schedule in schedules)
        {
            schedule.Schedule(pool);
        }
    }
}
