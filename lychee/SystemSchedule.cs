using lychee.interfaces;

namespace lychee;

/// <summary>
/// Holds all system schedules.
/// </summary>
public sealed class SystemSchedules
{
    private readonly List<ISchedule> schedules = [];

    private readonly Dictionary<string, ISchedule> scheduleDict = [];

    private int lastScheduleIndex;

    private bool needClear;

    /// <summary>
    /// Add a schedule after another schedule.
    /// </summary>
    /// <param name="schedule">The schedule to add.</param>
    /// <param name="addAfter">The schedule after which to add the new schedule.</param>
    /// <exception cref="ArgumentException">Thrown when the schedule already exists or the addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, ISchedule? addAfter = null)
    {
        var index = schedules.IndexOf(schedule);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule} already exists");
        }

        if (addAfter != null && schedules.IndexOf(addAfter) == -1)
        {
            schedules.Add(schedule);
        }
        else
        {
            schedules.Insert(index + 1, schedule);
        }

        scheduleDict.Add(schedule.Name, schedule);
    }

    /// <summary>
    /// Add a schedule after another schedule by name.
    /// </summary>
    /// <param name="schedule">The schedule to add.</param>
    /// <param name="addAfter">The name of the schedule after which to add the new schedule.</param>
    /// <exception cref="ArgumentException">Thrown when the schedule already exists or the addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, string addAfter)
    {
        var addAfterSchedule = GetSchedule(addAfter);
        if (addAfterSchedule == null)
        {
            throw new ArgumentException($"Schedule {addAfter} not found");
        }

        AddSchedule(schedule, addAfterSchedule);
    }

    /// <summary>
    /// Clear all schedules.
    /// </summary>
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

    /// <summary>
    /// Get a schedule by name.
    /// </summary>
    /// <param name="name">The name of the schedule.</param>
    /// <typeparam name="T">The type of the schedule.</typeparam>
    /// <returns>The schedule with the given name, or null if not found.</returns>
    public T? GetSchedule<T>(string name) where T : class, ISchedule
    {
        return scheduleDict.GetValueOrDefault(name) as T;
    }
}
