namespace ONI_Together.Misc;

public static class BitUtils
{
    /// <summary>
    /// Maps the 6-bit hash extracted from a value's lowest set bit back to its bit
    /// index, using a 64-bit de Bruijn sequence (order 6) trick.
    ///
    /// TrailingZeroCount(v) isolates the lowest set bit as a power of two, folds it
    /// through the de Bruijn constant 0x022FDD63CC95386D via
    /// (lowestBit * 0x022FDD63CC95386D) >> 58 to produce a unique index 0..63, then
    /// looks up the original bit position here. Equivalent to
    /// <see href="https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.trailingzerocount?view=net-10.0">
    /// System.Numerics.BitOperations.TrailingZeroCount</see>,
    /// which is unavailable on the project's netstandard2.1 target.
    /// </summary>
    private static readonly int[] TrailingZeroIndex = new int[64]
    {
         0,  1,  2, 53,  3,  7, 54, 27,  4, 38, 41,  8, 34, 55, 48, 28,
        62,  5, 39, 46, 44, 42, 22,  9, 24, 35, 59, 56, 49, 18, 29, 11,
        63, 52,  6, 26, 37, 40, 33, 47, 61, 45, 43, 21, 23, 58, 17, 10,
        51, 25, 36, 32, 60, 20, 57, 16, 50, 31, 19, 15, 30, 14, 13, 12
    };

    /// <summary>
    /// Returns the index of the lowest set bit in <paramref name="value"/> (the number
    /// of trailing zero bits), or 64 when <paramref name="value"/> is 0.
    /// </summary>
    public static int TrailingZeroCount(ulong value)
    {
        if (value == 0) return 64;
        ulong lowestBit = value & (0UL - value);
        return TrailingZeroIndex[(int)((lowestBit * 0x022FDD63CC95386DUL) >> 58)];
    }
}