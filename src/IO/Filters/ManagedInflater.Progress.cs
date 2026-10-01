namespace Aspose.Pdf.IO.Filters;

/// <summary>Why a deflate or zlib stream could not be read, in the terms zlib reports it.</summary>
internal enum InflateFault
{
    None,
    /// <summary>The input ended before the stream did; more input may complete it.</summary>
    Truncated,
    UnknownCompressionMethod,
    InvalidWindowSize,
    IncorrectHeaderCheck,
    InvalidBlockType,
    InvalidStoredBlockLengths,
    InvalidBitLengthRepeat,
    InvalidLiteralLengthCode,
    InvalidDistanceCode,
    InvalidDistanceTooFarBack,
    IncorrectDataCheck,
}

/// <summary>Where reading a stream got to.</summary>
internal enum InflateState
{
    /// <summary>The whole stream was read, its check value included.</summary>
    Done,
    /// <summary>All the input was read and the stream goes on.</summary>
    NeedsInput,
    /// <summary>The zlib header asks for a preset dictionary; <see cref="InflateProgress.DictionaryId"/> names it.</summary>
    NeedsDictionary,
    /// <summary>The stream is malformed; <see cref="InflateProgress.Fault"/> says how.</summary>
    Failed,
}

/// <summary>What reading a stream from its start produced: the bytes decoded, how many input bytes
/// were read to get them, and where it stopped.</summary>
internal sealed class InflateProgress
{
    public InflateState State { get; init; }
    public InflateFault Fault { get; init; }
    public byte[] Output { get; init; } = Array.Empty<byte>();
    public int Consumed { get; init; }
    public uint DictionaryId { get; init; }

    /// <summary>zlib's own wording for the fault; null when there is none.</summary>
    public string? Message => Fault switch
    {
        InflateFault.UnknownCompressionMethod => "unknown compression method",
        InflateFault.InvalidWindowSize => "invalid window size",
        InflateFault.IncorrectHeaderCheck => "incorrect header check",
        InflateFault.InvalidBlockType => "invalid block type",
        InflateFault.InvalidStoredBlockLengths => "invalid stored block lengths",
        InflateFault.InvalidBitLengthRepeat => "invalid bit length repeat",
        InflateFault.InvalidLiteralLengthCode => "invalid literal/length code",
        InflateFault.InvalidDistanceCode => "invalid distance code",
        InflateFault.InvalidDistanceTooFarBack => "invalid distance too far back",
        InflateFault.IncorrectDataCheck => "incorrect data check",
        _ => null,
    };
}

internal static partial class ManagedInflater
{
    private const int CompressionMethodDeflate = 8;
    private const int MaxWindowBits = 15;
    private const int WindowBitsBias = 8;
    private const int HeaderCheckDivisor = 31;
    private const int PresetDictionaryFlag = 0x20;
    private const int CheckValueBytes = 4;
    private const string FaultKey = "InflateFault";

    /// <summary>A malformed-stream exception carrying the fault that stopped it.</summary>
    private static InvalidDataException Faulted(InflateFault fault, string message)
    {
        var e = new InvalidDataException(message);
        e.Data[FaultKey] = fault;
        return e;
    }

    /// <summary>
    /// Reads the first <paramref name="count"/> bytes of a stream from its start, as far as they
    /// go, for a caller that feeds a stream piece by piece and asks again with more. Input is read
    /// a byte at a time, only as needed, so <see cref="InflateProgress.Consumed"/> is exact: at
    /// the end of a zlib stream it stops after the check value, leaving whatever follows unread.
    /// With the zlib wrapper the header is checked and the trailing Adler-32 compared; a header
    /// asking for a preset dictionary stops there unless <paramref name="dictionary"/> is given,
    /// which then primes the window.
    /// </summary>
    internal static InflateProgress Inflate(byte[] input, int count, bool zlibWrapper, byte[]? dictionary)
    {
        var data = input.Length == count ? input : input.AsSpan(0, count).ToArray();
        var start = 0;
        if (zlibWrapper)
        {
            var header = ReadZlibHeader(data, dictionary, out start);
            if (header is not null) return header;
        }

        var reader = new BitReader(data, start);
        var output = new OutputBuffer(data.Length);
        var primed = zlibWrapper && dictionary is not null && (data[1] & PresetDictionaryFlag) != 0 ? dictionary : null;
        if (primed is not null)
        {
            output.EnsureCapacity(primed.Length);
            Array.Copy(primed, output.Data, primed.Length);
            output.Length = primed.Length;
        }
        byte[] Produced() => primed is null ? output.ToArray() : output.Data.AsSpan(primed.Length, output.Length - primed.Length).ToArray();

        try
        {
            InflateBlocks(reader, output);
        }
        catch (InvalidDataException e)
        {
            return Stopped(e.Data[FaultKey] is InflateFault stopped ? stopped : InflateFault.InvalidLiteralLengthCode, Produced(), reader.Position);
        }
        catch (IndexOutOfRangeException)
        {
            return Stopped(InflateFault.InvalidBitLengthRepeat, Produced(), reader.Position);
        }

        var decoded = Produced();
        if (!zlibWrapper) return new InflateProgress { State = InflateState.Done, Output = decoded, Consumed = reader.Position };
        var trailer = reader.Position;
        if (trailer + CheckValueBytes > data.Length)
            return new InflateProgress { State = InflateState.NeedsInput, Output = decoded, Consumed = data.Length };
        var expected = (uint)(data[trailer] << 24 | data[trailer + 1] << 16 | data[trailer + 2] << 8 | data[trailer + 3]);
        var fault = expected == ManagedDeflater.Adler32(decoded, 0, decoded.Length) ? InflateFault.None : InflateFault.IncorrectDataCheck;
        return new InflateProgress
        {
            State = fault == InflateFault.None ? InflateState.Done : InflateState.Failed,
            Fault = fault,
            Output = decoded,
            Consumed = trailer + CheckValueBytes,
        };
    }

    /// <summary>The zlib header checked a byte at a time; null when the blocks may be read from
    /// <paramref name="start"/>, otherwise where reading stopped.</summary>
    private static InflateProgress? ReadZlibHeader(byte[] data, byte[]? dictionary, out int start)
    {
        start = 0;
        if (data.Length < 1) return new InflateProgress { State = InflateState.NeedsInput };
        int cmf = data[0];
        if ((cmf & 0x0F) != CompressionMethodDeflate) return Stopped(InflateFault.UnknownCompressionMethod, Array.Empty<byte>(), 1);
        if ((cmf >> 4) + WindowBitsBias > MaxWindowBits) return Stopped(InflateFault.InvalidWindowSize, Array.Empty<byte>(), 1);
        if (data.Length < 2) return new InflateProgress { State = InflateState.NeedsInput, Consumed = 1 };
        if ((cmf * 256 + data[1]) % HeaderCheckDivisor != 0) return Stopped(InflateFault.IncorrectHeaderCheck, Array.Empty<byte>(), 2);
        start = 2;
        if ((data[1] & PresetDictionaryFlag) == 0) return null;
        if (data.Length < start + CheckValueBytes) return new InflateProgress { State = InflateState.NeedsInput, Consumed = data.Length };
        var id = (uint)(data[2] << 24 | data[3] << 16 | data[4] << 8 | data[5]);
        start += CheckValueBytes;
        return dictionary is null
            ? new InflateProgress { State = InflateState.NeedsDictionary, Consumed = start, DictionaryId = id }
            : null;
    }

    /// <summary>
    /// A whole stream, read the way a decompressing stream reader reads it to its end: a stream
    /// cut short yields the bytes it holds, a malformed one (a bad header or code, a check value
    /// that does not match, a preset dictionary nobody supplied) throws
    /// <see cref="InvalidDataException"/> with zlib's wording. Without <paramref name="zlibWrapper"/>
    /// the input is raw deflate and whatever follows its final block is left unread.
    /// </summary>
    internal static byte[] InflateToEnd(byte[] input, int offset, int count, bool zlibWrapper)
    {
        var progress = Inflate(Slice(input, offset, count), count, zlibWrapper, null);
        return progress.State switch
        {
            InflateState.Done or InflateState.NeedsInput => progress.Output,
            InflateState.NeedsDictionary => throw Faulted(InflateFault.None, "zlib: preset dictionary not supported"),
            _ => throw Faulted(progress.Fault, "zlib: " + progress.Message),
        };
    }

    /// <summary>
    /// What a damaged stream still yields, read as zlib or as raw deflate from <paramref name="offset"/>:
    /// the bytes decoded before a fault, less the partly filled 4096-byte chunk a chunked reader
    /// loses when the fault surfaces (see <see cref="InflateZlib"/>); null when nothing is left.
    /// </summary>
    internal static byte[]? Salvage(byte[] input, int offset, bool zlibWrapper)
    {
        var count = input.Length - offset;
        if (count <= 0) return null;
        var progress = Inflate(Slice(input, offset, count), count, zlibWrapper, null);
        var output = progress.Output;
        if (progress.State is InflateState.Failed or InflateState.NeedsDictionary)
            output = output.AsSpan(0, output.Length & ~(SalvageChunk - 1)).ToArray();
        return output.Length > 0 ? output : null;
    }

    private const int SalvageChunk = 4096;

    private static byte[] Slice(byte[] input, int offset, int count)
        => offset == 0 && count == input.Length ? input : input.AsSpan(offset, count).ToArray();

    private static InflateProgress Stopped(InflateFault fault, byte[] output, int consumed) => new()
    {
        State = fault == InflateFault.Truncated ? InflateState.NeedsInput : InflateState.Failed,
        Fault = fault == InflateFault.Truncated ? InflateFault.None : fault,
        Output = output,
        Consumed = consumed,
    };
}
