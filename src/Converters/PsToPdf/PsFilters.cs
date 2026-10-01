using System;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The decoding filters a PostScript program can wrap a data source in. Each maps
/// onto the library's existing stream filter, so there is one implementation of each
/// algorithm rather than a PostScript-side copy.
/// </summary>
internal static class PsFilters
{
    /// <summary>Decode a buffer with the named filter, returning it unchanged when
    /// the name is one of the encoding filters or is not recognised — a program that
    /// asks for an unsupported filter should still yield the page it painted.</summary>
    public static byte[] Decode(string name, byte[] data)
    {
        if (data is null) return new byte[0];
        try
        {
            return DecodeCore(name, data);
        }
        catch (Exception)
        {
            return data;
        }
    }

    /// <summary>How many bytes of <paramref name="data"/> the named filter's encoded
    /// run occupies, so a filter wrapped around a program's own source consumes its
    /// data and no more. A filter whose extent cannot be told from the bytes claims
    /// all of them, which is the old behaviour and the safe answer for a source that
    /// really does end with its data.</summary>
    public static int EncodedLength(string name, byte[] data)
    {
        if (data is null || data.Length == 0) return 0;
        return name switch
        {
            "ASCIIHexDecode" => TerminatorAt(data, (byte)'>', 1),
            "ASCII85Decode" => TerminatorAt(data, (byte)'>', 0) is var a && a > 0 ? a : data.Length,
            _ => data.Length,
        };
    }

    /// <summary>The offset just past the first terminator byte, or the whole buffer
    /// when there is none. <paramref name="extra"/> covers a two-byte terminator.</summary>
    private static int TerminatorAt(byte[] data, byte terminator, int extra)
    {
        for (var i = 0; i < data.Length; i++)
            if (data[i] == terminator)
                return Math.Min(data.Length, i + extra);
        return data.Length;
    }

    private static byte[] DecodeCore(string name, byte[] data) => name switch
    {
        "ASCIIHexDecode" => AsciiHexDecodeFilter.Decode(data),
        "ASCII85Decode" => Ascii85DecodeFilter.Decode(data),
        "RunLengthDecode" => RunLengthDecodeFilter.Decode(data),
        "FlateDecode" => FlateDecodeFilter.Decode(data, null),
        "LZWDecode" => LzwDecodeFilter.Decode(data, null),
        _ => data,
    };
}
