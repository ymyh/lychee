namespace lychee.utils;

public sealed class ReadWriteLock<T>(T data) : IDisposable
{
    private readonly ReaderWriterLockSlim rwLock = new();

    private T data = data;

    ~ReadWriteLock()
    {
        Dispose();
    }

    public readonly struct ReadLockGuard(ReadWriteLock<T> rwl) : IDisposable
    {
        public T Data => rwl.data;

        public void Dispose()
        {
            rwl.rwLock.ExitReadLock();
        }
    }

    public readonly struct WriteLockGuard(ReadWriteLock<T> rwl) : IDisposable
    {
        public T Data
        {
            get => rwl.data;

            set => rwl.data = value;
        }

        public void Dispose()
        {
            rwl.rwLock.ExitWriteLock();
        }
    }

    public ReadLockGuard EnterReadLock()
    {
        rwLock.EnterReadLock();
        return new(this);
    }

    public WriteLockGuard EnterWriteLock()
    {
        rwLock.EnterWriteLock();
        return new(this);
    }

    public ReadLockGuard? TryEnterReadLock(TimeSpan timeout)
    {
        if (rwLock.TryEnterReadLock(timeout))
        {
            return new(this);
        }

        return null;
    }

    public ReadLockGuard? TryEnterReadLock(int millisecondsTimeout)
    {
        if (rwLock.TryEnterReadLock(millisecondsTimeout))
        {
            return new(this);
        }

        return null;
    }

    public WriteLockGuard? TryEnterWriteLock(TimeSpan timeout)
    {
        if (rwLock.TryEnterWriteLock(timeout))
        {
            return new(this);
        }

        return null;
    }

    public WriteLockGuard? TryEnterWriteLock(int millisecondsTimeout)
    {
        if (rwLock.TryEnterWriteLock(millisecondsTimeout))
        {
            return new(this);
        }

        return null;
    }

    public void Dispose()
    {
        rwLock.Dispose();
        GC.SuppressFinalize(this);
    }
}