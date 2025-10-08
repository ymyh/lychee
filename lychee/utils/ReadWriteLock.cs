namespace lychee.utils;

public sealed class ReadWriteLock<T>(T data)
{
    private readonly ReaderWriterLockSlim locker = new();

    private readonly T data = data;

    public readonly struct ReadLockGuard(ReadWriteLock<T> locker) : IDisposable
    {
        public T Get()
        {
            return locker.data;
        }

        public void Dispose()
        {
            locker.locker.ExitReadLock();
        }
    }

    public readonly struct WriteLockGuard(ReadWriteLock<T> locker) : IDisposable
    {
        public T Get()
        {
            return locker.data;
        }

        public void Dispose()
        {
            locker.locker.ExitWriteLock();
        }
    }

    public ReadLockGuard EnterReadLock()
    {
        locker.EnterReadLock();
        return new(this);
    }

    public WriteLockGuard EnterWriteLock()
    {
        locker.EnterReadLock();
        return new(this);
    }

    public ReadLockGuard? TryEnterReadLock(TimeSpan timeout)
    {
        if (locker.TryEnterReadLock(timeout))
        {
            return new(this);
        }

        return null;
    }

    public ReadLockGuard? TryEnterReadLock(int millisecondsTimeout)
    {
        if (locker.TryEnterReadLock(millisecondsTimeout))
        {
            return new(this);
        }

        return null;
    }

    public WriteLockGuard? TryEnterWriteLock(TimeSpan timeout)
    {
        if (locker.TryEnterWriteLock(timeout))
        {
            return new(this);
        }

        return null;
    }

    public WriteLockGuard? TryEnterWriteLock(int millisecondsTimeout)
    {
        if (locker.TryEnterWriteLock(millisecondsTimeout))
        {
            return new(this);
        }

        return null;
    }
}
