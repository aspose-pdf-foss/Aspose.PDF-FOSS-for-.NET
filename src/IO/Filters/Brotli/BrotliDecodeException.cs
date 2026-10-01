namespace Aspose.Pdf.IO.Filters.Brotli;

/// <summary>
/// A Brotli stream that cannot be decoded, with the error code the widely used reference
/// decoders report for the same fault, so a caller can say which rule the stream broke.
/// </summary>
internal sealed class BrotliDecodeException : System.IO.IOException
{
    internal BrotliDecodeException(int code) : base("Brotli decoding failed, error code " + code) => Code = code;

    /// <summary>The fault, one of the <see cref="BrotliErrors"/> codes.</summary>
    internal int Code { get; }
}

/// <summary>The decoding faults, numbered as Brotli decoders conventionally number them.</summary>
internal static class BrotliErrors
{
    internal const int CorruptedCodeLengthTable = -2;
    internal const int CorruptedContextMap = -3;
    internal const int CorruptedHuffmanCodeHistogram = -4;
    internal const int CorruptedPaddingBits = -5;
    internal const int CorruptedReservedBit = -6;
    internal const int DuplicateSimpleHuffmanSymbol = -7;
    internal const int ExuberantNibble = -8;
    internal const int InvalidBackwardReference = -9;
    internal const int InvalidMetablockLength = -10;
    internal const int InvalidWindowBits = -11;
    internal const int NegativeDistance = -12;
    internal const int ReadAfterEnd = -13;
    internal const int SymbolOutOfRange = -15;
    internal const int UnusedBytesAfterEnd = -17;
    internal const int UnusedHuffmanSpace = -18;
}
