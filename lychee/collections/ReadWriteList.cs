using System.Diagnostics;
using lychee.threading;

namespace lychee.collections;

public sealed class ReadWriteList<T>
{
    public readonly struct ReadList<T>(ReadWriteList<T> list, ReadWriteLock<T[]>.ReadLockGuard guard) : IDisposable
    {
        public T this[int index]
        {
            get
            {
                Debug.Assert((uint)index < (uint)guard.Data.Length);
                return guard.Data[index];
            }
        }

        public bool Contains(T value)
        {
            return guard.Data.Contains(value);
        }

        public int IndexOf(T value)
        {
            return guard.Data.IndexOf(value);
        }

        public void Dispose()
        {
            guard.Dispose();
        }
    }

    public readonly struct WriteList<T>(ReadWriteList<T> list, ReadWriteLock<T[]>.WriteLockGuard guard) : IDisposable
    {
        public T this[int index]
        {
            get
            {
                Debug.Assert((uint)index < (uint)guard.Data.Length);
                return guard.Data[index];
            }

            set
            {
                Debug.Assert((uint)index < (uint)guard.Data.Length);
                guard.Data[index] = value;
            }
        }

        public void Add(T value)
        {
            // 快速路径：容量足够
            var index = Interlocked.Increment(ref list.size) - 1;

            if (index >= list.capacity)
            {
                // 回退 size，以免占用无效位置
                Interlocked.Decrement(ref list.size);

                // 扩容（一次性同步）
                lock (list)
                {
                    if (list.size >= list.capacity)
                    {
                        EnsureCapacity(list.capacity == 0 ? 16 : list.capacity * 2);
                    }
                }

                // 重新获取位置
                index = Interlocked.Increment(ref list.size) - 1;
            }

            guard.Data[index] = value;
        }

        public bool Contains(T value)
        {
            return guard.Data.Contains(value);
        }

        public int IndexOf(T value)
        {
            return guard.Data.IndexOf(value);
        }

        private void EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative");
            }

            if (capacity < list.capacity)
            {
                return;
            }

            list.capacity = Math.Max(list.capacity * 2, capacity);

            if (guard.Data.Length != 0)
            {
                var newArray = new T[capacity];

                guard.Data.CopyTo(newArray);
                guard.Data = newArray;
            }
            else
            {
                guard.Data = new T[capacity];
            }
        }

        public void Dispose()
        {
            guard.Dispose();
        }
    }

    private ReadWriteLock<T[]> array = new([]);

    private volatile int size;

    private int capacity;

    public bool IsFull => size == capacity;

    public ReadList<T> GetReadList()
    {
        var guard = array.EnterReadLock();
        return new(this, guard);
    }

    public WriteList<T> GetWriteList()
    {
        var guard = array.EnterWriteLock();
        return new(this, guard);
    }
}
