namespace Aspose.Pdf.IO.Filters;

/// <summary>The RFC 1951 alphabets shared by the inflater and the deflater.</summary>
internal static class DeflateTables
{
    public const int MinMatch = 3;
    public const int MaxMatch = 258;
    public const int WindowSize = 32768;
    public const int EndOfBlock = 256;
    public const int LengthCodeBase = 257;
    public const int LiteralLengthAlphabet = 286;
    public const int DistanceAlphabet = 30;
    public const int CodeLengthAlphabet = 19;
    public const int MaxCodeBits = 15;
    public const int MaxCodeLengthCodeBits = 7;

    public static readonly int[] LengthBase = {
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
    };

    public static readonly int[] LengthExtraBits = {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
    };

    public static readonly int[] DistanceBase = {
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
        8193, 12289, 16385, 24577
    };

    public static readonly int[] DistanceExtraBits = {
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
    };

    // The code-length-alphabet symbols appear in this permuted order in the bit stream.
    public static readonly int[] CodeLengthOrder = {
        16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
    };
}
