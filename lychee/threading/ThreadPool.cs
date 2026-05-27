using System.Threading.Channels;

namespace lychee.threading;

/// <summary>
/// A simple thread pool that dispatches tasks to multiple threads.
/// </summary>
public sealed class ThreadPool : IDisposable
{
    private readonly List<Thread> threads;

    private readonly Channel<Action<int>> sendTaskChannel;

    private int pendingCount;

    private readonly ManualResetEventSlim doneEvent = new(true);

    private bool disposed;

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

                        if (Interlocked.Decrement(ref pendingCount) == 0)
                        {
                            doneEvent.Set();
                        }
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        if (Interlocked.Decrement(ref pendingCount) == 0)
                        {
                            doneEvent.Set();
                        }
                    }
                }
            }) { IsBackground = true };

            threads.Add(thread);
            thread.Start();
        }
    }

    /// <summary>
    /// Dispatches a task to be executed on a worker thread.
    /// </summary>
    /// <param name="act">The task to dispatch. The parameter is the index of the worker thread executing it.</param>
    public void Dispatch(Action<int> act)
    {
        sendTaskChannel.Writer.WriteAsync(act);

        if (Interlocked.Increment(ref pendingCount) == 1)
        {
            doneEvent.Reset();
        }
    }

    /// <summary>
    /// Blocks the calling thread until all dispatched tasks have completed.
    /// </summary>
    public void Wait()
    {
        doneEvent.Wait();
    }

#region IDisposable Implementation

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

        doneEvent.Dispose();
        threads.Clear();
    }

#endregion
}
