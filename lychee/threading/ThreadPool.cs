using System.Threading.Channels;

namespace lychee.threading;

/// <summary>
/// A simple thread pool that dispatch tasks to multiple threads.
/// </summary>
public sealed class ThreadPool : IDisposable
{
    private readonly List<Thread> threads;

    private readonly Channel<Action<int>> sendTaskChannel;

    private CountdownEvent countdownEvent = new(1);

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
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    finally
                    {
                        countdownEvent.Signal();
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
        countdownEvent.TryAddCount();
        sendTaskChannel.Writer.TryWrite(act);
    }

    /// <summary>
    /// Wait until all tasks are completed.
    /// </summary>
    public void Wait()
    {
        countdownEvent.Signal();

        if (countdownEvent.CurrentCount == 0)
        {
            countdownEvent.Reset();
            return;
        }

        try
        {
            countdownEvent.Wait();
            countdownEvent.Reset();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
    }

    public void Dispose()
    {
        sendTaskChannel.Writer.Complete();

        foreach (var thread in threads)
        {
            if (!thread.Join(1000))
            {
                thread.Interrupt();
            }
        }

        countdownEvent.Dispose();
        threads.Clear();
    }
}
