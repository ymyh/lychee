using lychee.interfaces;

namespace lychee;

/// <summary>
/// Manages the collection and execution of system schedules in the application.
/// Schedules are executed sequentially and can be dynamically added or cleared.
/// </summary>
public sealed class SystemSchedules
{
    private readonly List<ISchedule> schedules = [];

    private readonly Dictionary<string, ISchedule> scheduleDict = [];

    private int lastScheduleIndex;

    private bool needClear;

    /// <summary>
    /// Adds a schedule to the collection, optionally inserting it after a specified schedule.
    /// </summary>
    /// <param name="schedule">The schedule to add.</param>
    /// <param name="name">The unique name identifier for the schedule.</param>
    /// <param name="addAfter">The schedule after which to insert the new schedule; null to append to the end.</param>
    /// <exception cref="ArgumentException">Thrown when a schedule with the same name or instance already exists,
    /// or when the specified addAfter schedule is not found.</exception>
    public void AddSchedule(ISchedule schedule, string name, ISchedule? addAfter = null)
    {
        var index = schedules.IndexOf(schedule);
        if (index != -1)
        {
            throw new ArgumentException($"Schedule {schedule} already exists");
        }

        if (scheduleDict.ContainsKey(name))
        {
            throw new ArgumentException($"Schedule {name} already exists");
        }

        if (addAfter != null && schedules.IndexOf(addAfter) == -1)
        {
            index = schedules.IndexOf(addAfter);
            if (index == -1)
            {
                throw new ArgumentException($"Schedule {addAfter} does not exist");
            }

            schedules.Insert(index, schedule);
        }
        else
        {
            schedules.Add(schedule);
        }

        scheduleDict.Add(name, schedule);
    }

    /// <summary>
    /// Adds a schedule to the collection, inserting it after a schedule specified by name.
    /// </summary>
    /// <param name="schedule">The schedule to add.</param>
    /// <param name="name">The unique name identifier for the schedule.</param>
    /// <param name="addAfter">The name of the schedule after which to insert the new schedule.</param>
    /// <exception cref="ArgumentException">Thrown when a schedule with the same name already exists,
    /// or when the specified addAfter schedule name is not found.</exception>
    public void AddSchedule(ISchedule schedule, string name, string addAfter)
    {
        var addAfterSchedule = GetSchedule(addAfter);
        if (addAfterSchedule == null)
        {
            throw new ArgumentException($"Schedule {addAfter} not found");
        }

        AddSchedule(schedule, name, addAfterSchedule);
    }

    /// <summary>
    /// Requests that all schedules be cleared after the current execution cycle completes.
    /// </summary>
    /// <remarks>
    /// The clearing is deferred until the end of the current execution cycle to avoid
    /// modifying the collection during iteration.
    /// </remarks>
    public void ClearSchedules()
    {
        needClear = true;
    }

    /// <summary>
    /// Executes schedules starting from the last executed index, optionally stopping before a specified schedule.
    /// </summary>
    /// <param name="scheduleEnd">The schedule at which to stop execution (exclusive); null to execute all remaining schedules.</param>
    /// <returns>true if all schedules have been executed and the cycle is complete; false if execution was interrupted.</returns>
    /// <remarks>
    /// This method supports resumable execution across multiple calls. When the end of the schedule list is reached,
    /// the execution index is reset and any pending clear operation is performed.
    /// </remarks>
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
    /// Retrieves a schedule by its unique name.
    /// </summary>
    /// <param name="name">The unique name identifier of the schedule.</param>
    /// <returns>The schedule with the specified name, or null if not found.</returns>
    public ISchedule? GetSchedule(string name)
    {
        return scheduleDict.GetValueOrDefault(name);
    }

    /// <summary>
    /// Retrieves a schedule by its unique name and casts it to the specified type.
    /// </summary>
    /// <param name="name">The unique name identifier of the schedule.</param>
    /// <typeparam name="T">The type to cast the schedule to.</typeparam>
    /// <returns>The schedule cast to type T, or null if not found or the cast fails.</returns>
    public T? GetSchedule<T>(string name) where T : class, ISchedule
    {
        return scheduleDict.GetValueOrDefault(name) as T;
    }
}
