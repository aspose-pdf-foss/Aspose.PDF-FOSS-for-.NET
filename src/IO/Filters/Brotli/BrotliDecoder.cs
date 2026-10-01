namespace Aspose.Pdf.IO.Filters.Brotli;

/// <summary>
/// A managed Brotli decoder (RFC 7932), the same on every platform and framework the library
/// runs on: the window bits, then meta-blocks -- metadata to skip, stored bytes, or compressed
/// commands of literals and copies that reach back into the output, into an attached dictionary
/// or into the static dictionary.
/// </summary>
internal static partial class BrotliDecoder
{
    /// <summary>What a caller may change about decoding.</summary>
    internal sealed class Options
    {
        /// <summary>Allow the large-window extension (window bits up to 30).</summary>
        public bool LargeWindow;

        /// <summary>Data that precedes the output, as far as copies are concerned, in order.</summary>
        public System.Collections.Generic.List<byte[]> Dictionary { get; } = new();
    }

    /// <summary>Decodes a whole Brotli stream; faults raise <see cref="BrotliDecodeException"/>.</summary>
    internal static byte[] Decode(byte[] input, Options? options = null)
    {
        var state = new State(new BrotliBitReader(input), options ?? new Options());
        state.Run();
        return state.Output.ToArray();
    }

    private const int NumLiteralCodes = 256;
    private const int NumCommandCodes = 704;
    private const int NumBlockLengthCodes = 26;
    private const int LiteralContextBits = 6;
    private const int DistanceContextBits = 2;
    private const int MaxDistanceBits = 24;
    private const int MaxLargeDistanceBits = 62;
    private const int WindowGap = 16;

    private static readonly int[] BlockLengthOffset =
    {
        1, 5, 9, 13, 17, 25, 33, 41, 49, 65, 81, 97, 113, 145, 177, 209, 241, 305, 369, 497, 753, 1265, 2289, 4337, 8433, 16625,
    };

    private static readonly int[] BlockLengthBits =
    {
        2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 7, 8, 9, 10, 11, 12, 13, 24,
    };

    private static readonly int[] InsertLengthOffset =
    {
        0, 1, 2, 3, 4, 5, 6, 8, 10, 14, 18, 26, 34, 50, 66, 98, 130, 194, 322, 578, 1090, 2114, 6210, 22594,
    };

    private static readonly int[] InsertLengthBits =
    {
        0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 12, 14, 24,
    };

    private static readonly int[] CopyLengthOffset =
    {
        2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 18, 22, 30, 38, 54, 70, 102, 134, 198, 326, 582, 1094, 2118,
    };

    private static readonly int[] CopyLengthBits =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 24,
    };

    // Distance codes 0..15: which of the last four distances, and what to add to it.
    private static readonly int[] DistanceShortIndex = { 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1 };
    private static readonly int[] DistanceShortDelta = { 0, 0, 0, 0, -1, 1, -2, 2, -3, 3, -1, 1, -2, 2, -3, 3 };

    /// <summary>The window bits the stream opens with; -1 for a value it may not use.</summary>
    private static int WindowBits(BrotliBitReader bits, bool largeWindow, out bool large)
    {
        large = false;
        if (bits.ReadBits(1) == 0) return 16;
        var n = bits.ReadBits(3);
        if (n != 0) return 17 + n;
        n = bits.ReadBits(3);
        if (n == 0) return 17;
        if (n != 1) return 8 + n;
        if (!largeWindow) return -1;
        large = true;
        if (bits.ReadBits(1) == 1) return -1;
        n = bits.ReadBits(6);
        return n is < 10 or > 30 ? -1 : n;
    }

    /// <summary>A number from 0 to 255 in the variable-length form meta-block headers use.</summary>
    private static int ReadVarLenUint8(BrotliBitReader bits)
    {
        if (bits.ReadBits(1) == 0) return 0;
        var n = bits.ReadBits(3);
        return n == 0 ? 1 : bits.ReadBits(n) + (1 << n);
    }

    private static int ReadBlockLength(BrotliBitReader bits, BrotliPrefixCode code)
    {
        var symbol = code.Read(bits);
        return BlockLengthOffset[symbol] + bits.ReadBits(BlockLengthBits[symbol]);
    }

    /// <summary>
    /// A context map (section 7.3): which prefix code each context uses, run-length coded for
    /// zeros and optionally move-to-front transformed.
    /// </summary>
    private static byte[] ReadContextMap(BrotliBitReader bits, int size, out int trees)
    {
        trees = ReadVarLenUint8(bits) + 1;
        var map = new byte[size];
        if (trees == 1) return map;
        var maxRunPrefix = bits.ReadBits(1) != 0 ? bits.ReadBits(4) + 1 : 0;
        var alphabet = trees + maxRunPrefix;
        var code = BrotliPrefixCode.ReadFrom(bits, alphabet, alphabet);
        var i = 0;
        while (i < size)
        {
            var symbol = code.Read(bits);
            if (symbol == 0)
            {
                map[i++] = 0;
            }
            else if (symbol <= maxRunPrefix)
            {
                var repeat = (1 << symbol) + bits.ReadBits(symbol);
                while (repeat-- != 0)
                {
                    if (i >= size) throw new BrotliDecodeException(BrotliErrors.CorruptedContextMap);
                    map[i++] = 0;
                }
            }
            else
            {
                map[i++] = (byte)(symbol - maxRunPrefix);
            }
        }
        if (bits.ReadBits(1) == 1) InverseMoveToFront(map);
        return map;
    }

    private static void InverseMoveToFront(byte[] map)
    {
        var mtf = new byte[256];
        for (var i = 0; i < 256; i++) mtf[i] = (byte)i;
        for (var i = 0; i < map.Length; i++)
        {
            var index = map[i];
            var value = mtf[index];
            map[i] = value;
            for (var j = index; j > 0; j--) mtf[j] = mtf[j - 1];
            mtf[0] = value;
        }
    }
}
