namespace lychee.extensions;

public static class NumberExtensions
{
    extension(byte num)
    {
        /// <summary>
        /// Performs a saturating addition that clamps the result to <see cref="byte.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="other">The value to add.</param>
        /// <returns>The sum, clamped to the valid range of <see cref="byte"/>.</returns>
        public byte SaturatingAdd(byte other)
        {
            return (byte)(byte.MaxValue - other > num ? num + other : byte.MaxValue);
        }

        /// <summary>
        /// Performs a saturating subtraction that clamps the result to <see cref="byte.MinValue"/> on underflow.
        /// </summary>
        /// <param name="other">The value to subtract.</param>
        /// <returns>The difference, clamped to the valid range of <see cref="byte"/>.</returns>
        public byte SaturatingSub(byte other)
        {
            return (byte)(other <= num ? num - other : byte.MinValue);
        }
    }

    extension(short num)
    {
        /// <summary>
        /// Performs a saturating addition that clamps the result to <see cref="short.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="other">The value to add.</param>
        /// <returns>The sum, clamped to the valid range of <see cref="short"/>.</returns>
        public short SaturatingAdd(short other)
        {
            return (short)(short.MaxValue - other > num ? num + other : short.MaxValue);
        }

        /// <summary>
        /// Performs a saturating subtraction that clamps the result to <see cref="short.MinValue"/> on underflow.
        /// </summary>
        /// <param name="other">The value to subtract.</param>
        /// <returns>The difference, clamped to the valid range of <see cref="short"/>.</returns>
        public short SaturatingSub(short other)
        {
            return (short)(other <= num ? num - other : short.MinValue);
        }
    }

    extension(int num)
    {
        /// <summary>
        /// Performs a saturating addition that clamps the result to <see cref="int.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="other">The value to add.</param>
        /// <returns>The sum, clamped to the valid range of <see cref="int"/>.</returns>
        public int SaturatingAdd(int other)
        {
            return int.MaxValue - other > num ? num + other : int.MaxValue;
        }

        /// <summary>
        /// Performs a saturating subtraction that clamps the result to <see cref="int.MinValue"/> on underflow.
        /// </summary>
        /// <param name="other">The value to subtract.</param>
        /// <returns>The difference, clamped to the valid range of <see cref="int"/>.</returns>
        public int SaturatingSub(int other)
        {
            return other <= num ? num - other : int.MinValue;
        }
    }

    extension(uint num)
    {
        /// <summary>
        /// Performs a saturating addition that clamps the result to <see cref="uint.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="other">The value to add.</param>
        /// <returns>The sum, clamped to the valid range of <see cref="uint"/>.</returns>
        public uint SaturatingAdd(uint other)
        {
            return uint.MaxValue - other > num ? num + other : uint.MaxValue;
        }

        /// <summary>
        /// Performs a saturating subtraction that clamps the result to zero on underflow.
        /// </summary>
        /// <param name="other">The value to subtract.</param>
        /// <returns>The difference, clamped to the valid range of <see cref="uint"/>.</returns>
        public uint SaturatingSub(uint other)
        {
            return other <= num ? num - other : 0;
        }
    }

    extension(long num)
    {
        /// <summary>
        /// Performs a saturating addition that clamps the result to <see cref="long.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="other">The value to add.</param>
        /// <returns>The sum, clamped to the valid range of <see cref="long"/>.</returns>
        public long SaturatingAdd(long other)
        {
            return long.MaxValue - other > num ? num + other : long.MaxValue;
        }

        /// <summary>
        /// Performs a saturating subtraction that clamps the result to <see cref="long.MinValue"/> on underflow.
        /// </summary>
        /// <param name="other">The value to subtract.</param>
        /// <returns>The difference, clamped to the valid range of <see cref="long"/>.</returns>
        public long SaturatingSub(long other)
        {
            return other <= num ? num - other : long.MinValue;
        }
    }
}
