using System.Threading.Channels;

namespace lychee.threading;

public sealed class ThreadPool
{
    private readonly List<Thread> threads;

    private readonly List<Channel<Action>> sendTaskChannels;

    private int taskCount;

    private int currentChannel;

    public ThreadPool(int threadCount)
    {
        threads = new(threadCount);
        sendTaskChannels = new(threadCount);

        for (var i = 0; i < threadCount; i++)
        {
            var sendTaskChannel = Channel.CreateBounded<Action>(64);
            var taskCompleteChannel = Channel.CreateBounded<int>(64);

            var thread = new Thread(async () =>
            {
                var act = await sendTaskChannel.Reader.ReadAsync();
                Interlocked.Increment(ref taskCount);
                act();
                Interlocked.Decrement(ref taskCount);
                taskCompleteChannel.Writer.TryWrite(0);
            });

            sendTaskChannels.Add(sendTaskChannel);
            threads.Add(thread);

            thread.Start();
        }
    }

    ~ThreadPool()
    {
        foreach (var channel in sendTaskChannels)
        {
            channel.Writer.Complete();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
    }

    public void Dispatch(Action act)
    {
        if (currentChannel == sendTaskChannels.Count)
        {
            currentChannel = 0;
        }

        sendTaskChannels[currentChannel].Writer.WriteAsync(act);
        currentChannel++;
    }

    public void Wait()
    {
        SpinWait.SpinUntil(() => taskCount == 0, 16);
    }
}
