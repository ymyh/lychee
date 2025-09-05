namespace lychee.interfaces;

public interface ISchedule
{
    public string Name { get; }

    public void Configure();

    public void Execute();
}