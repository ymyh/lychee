using System.Threading.Channels;

namespace lychee.threading;

/// <summary>
/// A simple thread pool that dispatch tasks to multiple threads.
/// </summary>
public sealed class ThreadPool : IDisposable
{
    private readonly List<Thread> threads;

    private readonly Channel<Action<int>> sendTaskChannel;

    private readonly CountdownEvent countdownEvent = new(1);

    private bool disposed = false;

    public ThreadPool(int threadCount, int channelCapacity = 64)
    {
        if (threadCount < 1)
        {
            throw new ArgumentException("threadCount must be greater than 0");
        }

        if (channelCapacity < 1)
        {
            throw new ArgumentException("channelCapacity must be greater than 0");
        }

        threads = new(threadCount);
        sendTaskChannel = Channel.CreateBounded<Action<int>>(new BoundedChannelOptions(channelCapacity)
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
                        countdownEvent.Signal();
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        countdownEvent.Signal();
                    }
                }
            }) { IsBackground = true };

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

        countdownEvent.Wait();
        countdownEvent.Reset();
    }

#region IDisposable Member

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
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

#endregion
}
