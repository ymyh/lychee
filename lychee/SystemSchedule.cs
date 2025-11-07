using lychee.interfaces;

namespace lychee;

public sealed class SystemSchedules
{
    private readonly List<ISchedule> schedules = [];

    private readonly Dictionary<string, ISchedule> scheduleDict = [];

    private int lastScheduleIndex = 0;

    private bool needClear;

    public void AddSchedule(ISchedule schedule)
    {
        var index = schedules.IndexOf(schedule);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule} already exists");
        }

        schedules.Add(schedule);
        scheduleDict.Add(schedule.Name, schedule);
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
        scheduleDict.Add(schedule.Name, schedule);
    }

    public void AddSchedule(ISchedule schedule, string addAfter)
    {
        var addAfterSchedule = GetSchedule(addAfter);
        if (addAfterSchedule == null)
        {
            throw new ArgumentException($"Schedule {addAfter} not found");
        }

        AddSchedule(schedule, addAfterSchedule);
    }

    public void ClearSchedules()
    {
        needClear = true;
    }

    public bool Execute(ISchedule? scheduleEnd = null)
    {
        for (var i = lastScheduleIndex; i < schedules.Count; i++)
        {
            if (schedules[i] == scheduleEnd)
            {
                break;
            }

            schedules[i].Execute();
            lastScheduleIndex = i;
        }

        if (lastScheduleIndex == schedules.Count - 1)
        {
            lastScheduleIndex = 0;

            if (needClear)
            {
                schedules.Clear();
                scheduleDict.Clear();

                needClear = false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Get a schedule by name.
    /// </summary>
    /// <param name="name">The name of the schedule.</param>
    /// <returns>The schedule with the given name, or null if not found.</returns>
    public ISchedule? GetSchedule(string name)
    {
        return scheduleDict.GetValueOrDefault(name);
    }
}
