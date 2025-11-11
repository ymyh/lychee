using System.Threading.Channels;

namespace lychee.threading;

public sealed class ThreadPool : IDisposable
{
    private readonly List<Thread> threads;

    private readonly Channel<Action> sendTaskChannel;

    private readonly Channel<int> taskCompleteChannel;

    private int taskCount;

    public ThreadPool(int threadCount)
    {
        if (threadCount <= 1)
        {
            throw new ArgumentException("threadCount must be greater than 1");
        }

        threads = new(threadCount);
        sendTaskChannel = Channel.CreateBounded<Action>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
        taskCompleteChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        for (var i = 0; i < threadCount; i++)
        {
            var thread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        var act = await sendTaskChannel.Reader.ReadAsync();
                        act();
                        taskCompleteChannel.Writer.TryWrite(0);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                        break;
                    }
                }
            });

            threads.Add(thread);
            thread.Start();
        }
    }

    ~ThreadPool()
    {
        Dispose();
    }

    public void Dispatch(Action act)
    {
        taskCount++;
        sendTaskChannel.Writer.TryWrite(act);
    }

    public async Task AsTask()
    {
        while (taskCount > 0)
        {
            await taskCompleteChannel.Reader.ReadAsync();
            taskCount--;
        }
    }

    public void SpinWait()
    {
        while (taskCount > 0)
        {
            var task = taskCompleteChannel.Reader.ReadAsync();
            System.Threading.SpinWait.SpinUntil(() => task.IsCompleted);

            taskCount--;
        }
    }

    public void Dispose()
    {
        sendTaskChannel.Writer.Complete();
        taskCompleteChannel.Writer.Complete();
        threads.ForEach(x => x.Join());

        GC.SuppressFinalize(this);
    }
}