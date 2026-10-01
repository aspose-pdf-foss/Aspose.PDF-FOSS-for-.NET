namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Writes one deflate block from its LZ77 tokens, choosing per block whichever of stored,
/// fixed-Huffman or dynamic-Huffman coding costs the fewest bits.
/// </summary>
internal static class DeflateBlockWriter
{
    private const int BlockTypeStored = 0;
    private const int BlockTypeFixed = 1;
    private const int BlockTypeDynamic = 2;
    private const int BlockHeaderBits = 3;
    private const int MaxStoredBlock = 65535;
    private const int StoredBlockOverheadBits = BlockHeaderBits + 7 + 32;   // header, worst-case alignment, LEN and NLEN
    private const int HlitBits = 5, HdistBits = 5, HclenBits = 4, CodeLengthLengthBits = 3;
    private const int MinHlit = 257, MinHdist = 1, MinHclen = 4;
    private const int RepeatPrevious = 16, ShortZeroRun = 17, LongZeroRun = 18;
    private const int RepeatPreviousMin = 3, RepeatPreviousMax = 6;
    private const int ShortZeroRunMin = 3, ShortZeroRunMax = 10;
    private const int LongZeroRunMin = 11, LongZeroRunMax = 138;
    private const int RepeatPreviousExtraBits = 2, ShortZeroRunExtraBits = 3, LongZeroRunExtraBits = 7;

    private static readonly byte[] FixedLiteralLengths = BuildFixedLiteralLengths();
    private static readonly ushort[] FixedLiteralCodes = DeflateHuffman.AssignCodes(FixedLiteralLengths);
    private static readonly byte[] FixedDistanceLengths = BuildFixedDistanceLengths();
    private static readonly ushort[] FixedDistanceCodes = DeflateHuffman.AssignCodes(FixedDistanceLengths);
    private static readonly byte[] LengthCodeOf = BuildLengthCodeOf();

    /// <summary>A run of code lengths as the code-length alphabet spells it.</summary>
    private readonly record struct LengthSymbol(byte Symbol, byte Extra, byte ExtraBits);

    public static void Write(DeflateBitWriter writer, ushort[] tokenLength, ushort[] tokenValue, int tokenCount,
        byte[] data, int blockStart, int blockBytes, bool final)
    {
        var literalFreq = new int[DeflateTables.LiteralLengthAlphabet];
        var distanceFreq = new int[DeflateTables.DistanceAlphabet];
        CountFrequencies(tokenLength, tokenValue, tokenCount, literalFreq, distanceFreq);

        var literalLengths = DeflateHuffman.BuildLengths(literalFreq, DeflateTables.MaxCodeBits);
        var distanceLengths = DeflateHuffman.BuildLengths(distanceFreq, DeflateTables.MaxCodeBits);
        var hlit = Math.Max(MinHlit, LastUsed(literalLengths) + 1);
        var hdist = Math.Max(MinHdist, LastUsed(distanceLengths) + 1);
        var runs = EncodeLengthRuns(literalLengths, hlit, distanceLengths, hdist);
        var codeLengthFreq = new int[DeflateTables.CodeLengthAlphabet];
        foreach (var run in runs) codeLengthFreq[run.Symbol]++;
        var codeLengthLengths = DeflateHuffman.BuildLengths(codeLengthFreq, DeflateTables.MaxCodeLengthCodeBits);
        var hclen = Math.Max(MinHclen, LastUsedInOrder(codeLengthLengths) + 1);

        var payloadExtra = ExtraBitsCost(literalFreq, distanceFreq);
        var fixedCost = BlockHeaderBits + payloadExtra + CodedCost(literalFreq, FixedLiteralLengths) + CodedCost(distanceFreq, FixedDistanceLengths);
        var dynamicCost = BlockHeaderBits + HlitBits + HdistBits + HclenBits + hclen * CodeLengthLengthBits
            + RunsCost(runs, codeLengthLengths) + payloadExtra
            + CodedCost(literalFreq, literalLengths) + CodedCost(distanceFreq, distanceLengths);
        var storedCost = StoredCost(blockBytes);

        if (storedCost <= fixedCost && storedCost <= dynamicCost)
        {
            WriteStored(writer, data, blockStart, blockBytes, final);
            return;
        }
        writer.WriteBits(final ? 1u : 0u, 1);
        if (fixedCost <= dynamicCost)
        {
            writer.WriteBits(BlockTypeFixed, 2);
            WriteTokens(writer, tokenLength, tokenValue, tokenCount, FixedLiteralCodes, FixedLiteralLengths, FixedDistanceCodes, FixedDistanceLengths);
            return;
        }
        writer.WriteBits(BlockTypeDynamic, 2);
        WriteDynamicHeader(writer, hlit, hdist, hclen, codeLengthLengths, runs);
        WriteTokens(writer, tokenLength, tokenValue, tokenCount,
            DeflateHuffman.AssignCodes(literalLengths), literalLengths,
            DeflateHuffman.AssignCodes(distanceLengths), distanceLengths);
    }

    /// <summary>Stored blocks of at most 65535 bytes; an empty range still writes one block.</summary>
    public static void WriteStored(DeflateBitWriter writer, byte[] data, int offset, int count, bool final)
    {
        var remaining = count;
        do
        {
            var chunk = Math.Min(remaining, MaxStoredBlock);
            var last = chunk == remaining;
            writer.WriteBits(final && last ? 1u : 0u, 1);
            writer.WriteBits(BlockTypeStored, 2);
            writer.AlignToByte();
            writer.WriteBits((uint)chunk, 16);
            writer.WriteBits((uint)~chunk & 0xFFFF, 16);
            writer.WriteBytes(data, offset, chunk);
            offset += chunk;
            remaining -= chunk;
        } while (remaining > 0);
    }

    private static void CountFrequencies(ushort[] tokenLength, ushort[] tokenValue, int tokenCount,
        int[] literalFreq, int[] distanceFreq)
    {
        for (var i = 0; i < tokenCount; i++)
        {
            if (tokenLength[i] == 0)
            {
                literalFreq[tokenValue[i]]++;
                continue;
            }
            literalFreq[DeflateTables.LengthCodeBase + LengthCodeOf[tokenLength[i]]]++;
            distanceFreq[DistanceCodeOf(tokenValue[i])]++;
        }
        literalFreq[DeflateTables.EndOfBlock]++;
    }

    private static void WriteTokens(DeflateBitWriter writer, ushort[] tokenLength, ushort[] tokenValue, int tokenCount,
        ushort[] literalCodes, byte[] literalLengths, ushort[] distanceCodes, byte[] distanceLengths)
    {
        for (var i = 0; i < tokenCount; i++)
        {
            if (tokenLength[i] == 0)
            {
                writer.WriteCode(literalCodes[tokenValue[i]], literalLengths[tokenValue[i]]);
                continue;
            }
            int lengthCode = LengthCodeOf[tokenLength[i]];
            var lengthSymbol = DeflateTables.LengthCodeBase + lengthCode;
            writer.WriteCode(literalCodes[lengthSymbol], literalLengths[lengthSymbol]);
            var lengthExtraBits = DeflateTables.LengthExtraBits[lengthCode];
            if (lengthExtraBits > 0) writer.WriteBits((uint)(tokenLength[i] - DeflateTables.LengthBase[lengthCode]), lengthExtraBits);
            var distanceCode = DistanceCodeOf(tokenValue[i]);
            writer.WriteCode(distanceCodes[distanceCode], distanceLengths[distanceCode]);
            var distanceExtraBits = DeflateTables.DistanceExtraBits[distanceCode];
            if (distanceExtraBits > 0) writer.WriteBits((uint)(tokenValue[i] - DeflateTables.DistanceBase[distanceCode]), distanceExtraBits);
        }
        writer.WriteCode(literalCodes[DeflateTables.EndOfBlock], literalLengths[DeflateTables.EndOfBlock]);
    }

    private static void WriteDynamicHeader(DeflateBitWriter writer, int hlit, int hdist, int hclen,
        byte[] codeLengthLengths, List<LengthSymbol> runs)
    {
        writer.WriteBits((uint)(hlit - MinHlit), HlitBits);
        writer.WriteBits((uint)(hdist - MinHdist), HdistBits);
        writer.WriteBits((uint)(hclen - MinHclen), HclenBits);
        for (var i = 0; i < hclen; i++)
            writer.WriteBits(codeLengthLengths[DeflateTables.CodeLengthOrder[i]], CodeLengthLengthBits);
        var codeLengthCodes = DeflateHuffman.AssignCodes(codeLengthLengths);
        foreach (var run in runs)
        {
            writer.WriteCode(codeLengthCodes[run.Symbol], codeLengthLengths[run.Symbol]);
            if (run.ExtraBits > 0) writer.WriteBits(run.Extra, run.ExtraBits);
        }
    }

    /// <summary>The literal/length and distance code lengths as one sequence of
    /// code-length-alphabet symbols: literal lengths, zero runs and repeats.</summary>
    private static List<LengthSymbol> EncodeLengthRuns(byte[] literalLengths, int hlit, byte[] distanceLengths, int hdist)
    {
        var all = new byte[hlit + hdist];
        Array.Copy(literalLengths, all, hlit);
        Array.Copy(distanceLengths, 0, all, hlit, hdist);
        var runs = new List<LengthSymbol>();
        var i = 0;
        while (i < all.Length)
        {
            var length = all[i];
            var run = 1;
            while (i + run < all.Length && all[i + run] == length) run++;
            i += run;
            if (length == 0) EncodeZeroRun(runs, run);
            else EncodeLengthRun(runs, length, run);
        }
        return runs;
    }

    private static void EncodeZeroRun(List<LengthSymbol> runs, int run)
    {
        while (run > 0)
        {
            if (run >= LongZeroRunMin)
            {
                var n = Math.Min(run, LongZeroRunMax);
                runs.Add(new LengthSymbol(LongZeroRun, (byte)(n - LongZeroRunMin), LongZeroRunExtraBits));
                run -= n;
            }
            else if (run >= ShortZeroRunMin)
            {
                runs.Add(new LengthSymbol(ShortZeroRun, (byte)(run - ShortZeroRunMin), ShortZeroRunExtraBits));
                run = 0;
            }
            else
            {
                runs.Add(new LengthSymbol(0, 0, 0));
                run--;
            }
        }
    }

    private static void EncodeLengthRun(List<LengthSymbol> runs, byte length, int run)
    {
        runs.Add(new LengthSymbol(length, 0, 0));
        run--;
        while (run >= RepeatPreviousMin)
        {
            var n = Math.Min(run, RepeatPreviousMax);
            runs.Add(new LengthSymbol(RepeatPrevious, (byte)(n - RepeatPreviousMin), RepeatPreviousExtraBits));
            run -= n;
        }
        for (; run > 0; run--) runs.Add(new LengthSymbol(length, 0, 0));
    }

    private static long CodedCost(int[] freq, byte[] lengths)
    {
        long bits = 0;
        for (var i = 0; i < freq.Length; i++) bits += (long)freq[i] * lengths[i];
        return bits;
    }

    private static long ExtraBitsCost(int[] literalFreq, int[] distanceFreq)
    {
        long bits = 0;
        for (var code = 0; code < DeflateTables.LengthBase.Length; code++)
            bits += (long)literalFreq[DeflateTables.LengthCodeBase + code] * DeflateTables.LengthExtraBits[code];
        for (var code = 0; code < DeflateTables.DistanceAlphabet; code++)
            bits += (long)distanceFreq[code] * DeflateTables.DistanceExtraBits[code];
        return bits;
    }

    private static long RunsCost(List<LengthSymbol> runs, byte[] codeLengthLengths)
    {
        long bits = 0;
        foreach (var run in runs) bits += codeLengthLengths[run.Symbol] + run.ExtraBits;
        return bits;
    }

    private static long StoredCost(int blockBytes)
    {
        var chunks = Math.Max(1, (blockBytes + MaxStoredBlock - 1) / MaxStoredBlock);
        return (long)chunks * StoredBlockOverheadBits + 8L * blockBytes;
    }

    private static int LastUsed(byte[] lengths)
    {
        for (var i = lengths.Length - 1; i >= 0; i--)
            if (lengths[i] != 0) return i;
        return -1;
    }

    private static int LastUsedInOrder(byte[] codeLengthLengths)
    {
        for (var i = DeflateTables.CodeLengthOrder.Length - 1; i >= 0; i--)
            if (codeLengthLengths[DeflateTables.CodeLengthOrder[i]] != 0) return i;
        return -1;
    }

    private static int DistanceCodeOf(int distance)
    {
        var bases = DeflateTables.DistanceBase;
        var low = 0;
        var high = bases.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (bases[mid] <= distance) low = mid;
            else high = mid - 1;
        }
        return low;
    }

    private static byte[] BuildLengthCodeOf()
    {
        var table = new byte[DeflateTables.MaxMatch + 1];
        for (var code = 0; code < DeflateTables.LengthBase.Length; code++)
        {
            var first = DeflateTables.LengthBase[code];
            var count = 1 << DeflateTables.LengthExtraBits[code];
            for (var length = first; length < first + count && length <= DeflateTables.MaxMatch; length++)
                table[length] = (byte)code;
        }
        table[DeflateTables.MaxMatch] = (byte)(DeflateTables.LengthBase.Length - 1);
        return table;
    }

    // The fixed code is defined over 288 symbols (RFC 1951 §3.2.6), two of which are never
    // emitted; the canonical assignment must still count them, or every 9-bit code lands
    // four places too low.
    private const int FixedLiteralAlphabet = 288;

    private static byte[] BuildFixedLiteralLengths()
    {
        var lengths = new byte[FixedLiteralAlphabet];
        for (var i = 0; i < lengths.Length; i++)
            lengths[i] = (byte)(i < 144 ? 8 : i < 256 ? 9 : i < 280 ? 7 : 8);
        return lengths;
    }

    private static byte[] BuildFixedDistanceLengths()
    {
        var lengths = new byte[DeflateTables.DistanceAlphabet];
        for (var i = 0; i < lengths.Length; i++) lengths[i] = 5;
        return lengths;
    }
}
