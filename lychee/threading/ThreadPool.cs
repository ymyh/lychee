using System.Threading.Channels;

namespace lychee.threading;

/// <summary>
/// A simple thread pool that dispatch tasks to multiple threads.
/// </summary>
public sealed class ThreadPool : IDisposable
{
    private readonly List<Thread> threads;

    private readonly Channel<Action<int>> sendTaskChannel;

    private readonly Channel<int> taskCompleteChannel;

    private int taskCount;

    public ThreadPool(int threadCount)
    {
        if (threadCount < 1)
        {
            throw new ArgumentException("threadCount must be greater than 0");
        }

        threads = new(threadCount);
        sendTaskChannel = Channel.CreateBounded<Action<int>>(new BoundedChannelOptions(64)
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
            var idx = i;
            var thread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        var act = await sendTaskChannel.Reader.ReadAsync();
                        act(idx);
                        taskCompleteChannel.Writer.TryWrite(0);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        break;
                    }
                }
            });

            threads.Add(thread);
            thread.Start();
        }
    }

    /// <summary>
    /// Dispatch a task to thread.
    /// </summary>
    /// <param name="act">The task to dispatch. The parameter of action is the index of thread.</param>
    public void Dispatch(Action<int> act)
    {
        taskCount++;
        sendTaskChannel.Writer.TryWrite(act);
    }

    /// <summary>
    /// Wait until all tasks are completed.
    /// </summary>
    /// <returns></returns>
    public async Task AsTask()
    {
        while (taskCount > 0)
        {
            await taskCompleteChannel.Reader.ReadAsync();
            taskCount--;
        }
    }

    /// <summary>
    /// Spin wait until all tasks are completed.
    /// </summary>
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
    }
}
