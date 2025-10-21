namespace lychee.extensions;

public static class NumberExtensions
{
    extension(byte num)
    {
        public byte SaturatingAdd(byte other)
        {
            return (byte)(byte.MaxValue - other > num ? num + other : byte.MaxValue);
        }

        public byte SaturatingSub(byte other)
        {
            return (byte)(other <= num ? num - other : byte.MinValue);
        }
    }

    extension(short num)
    {
        public short SaturatingAdd(short other)
        {
            return (short)(short.MaxValue - other > num ? num + other : short.MaxValue);
        }

        public short SaturatingSub(short other)
        {
            return (short)(other <= num ? num - other : short.MinValue);
        }
    }

    extension(int num)
    {
        public int SaturatingAdd(int other)
        {
            return int.MaxValue - other > num ? num + other : int.MaxValue;
        }

        public int SaturatingSub(int other)
        {
            return other <= num ? num - other : int.MinValue;
        }
    }

    extension(uint num)
    {
        public uint SaturatingAdd(uint other)
        {
            return uint.MaxValue - other > num ? num + other : uint.MaxValue;
        }

        public uint SaturatingSub(uint other)
        {
            return other <= num ? num - other : 0;
        }
    }

    extension(long num)
    {
        public long SaturatingAdd(long other)
        {
            return long.MaxValue - other > num ? num + other : long.MaxValue;
        }

        public long SaturatingSub(long other)
        {
            return other <= num ? num - other : long.MinValue;
        }
    }
}