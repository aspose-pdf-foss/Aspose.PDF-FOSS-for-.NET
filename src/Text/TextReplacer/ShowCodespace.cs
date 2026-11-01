using System.Runtime.CompilerServices;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>How a composite font's show strings split into character codes: the codespace ranges of its
/// encoding CMap, each as (first code, last code, bytes per code). A CMap embedded in the file may give its
/// codes one byte each, or some one byte and others two (9.7.6.2); a replacement sliced and written by that
/// rule keeps every code whole. The named Identity and Unicode CMaps, and a CMap that cannot be read, take
/// two bytes per code.</summary>
internal sealed class ShowCodespace
{
    private const int ByteBits = 8;
    private const int HexDigitsPerByte = 2;
    private const int MaxCodeBytes = 4;
    private const string RangesStart = "begincodespacerange";
    private const string RangesEnd = "endcodespacerange";

    /// <summary>Bytes per code of the Identity CMaps, and of a composite font whose CMap cannot be read.</summary>
    internal const int DefaultCodeBytes = 2;

    internal static readonly ShowCodespace TwoByte = new(new[] { (0, 0xFFFF, DefaultCodeBytes) });

    /// <summary>Per font dictionary: a page's runs ask once per run, the CMap is read once.</summary>
    private static readonly ConditionalWeakTable<PdfDictionary, ShowCodespace> Known = new();

    /// <summary>Ranges ordered by bytes per code, the shortest first.</summary>
    private readonly (int first, int last, int width)[] _ranges;

    private ShowCodespace((int first, int last, int width)[] ranges) => _ranges = ranges;

    /// <summary>True when every code takes two bytes, as the Identity CMaps write them.</summary>
    internal bool IsTwoByte => _ranges.All(r => r.width == DefaultCodeBytes);

    /// <summary>The codespace of a composite font; null for a simple font, whose codes are one byte each.</summary>
    internal static ShowCodespace? Of(PdfDictionary? fontDict, PdfReader? reader)
    {
        if (fontDict is null || fontDict.GetName("Subtype") != "Type0") return null;
        lock (Known)
        {
            if (Known.TryGetValue(fontDict, out var known)) return known;
        }
        var encoding = fontDict.Get("Encoding");
        // Without the reader an embedded CMap cannot be read: two bytes, as before, and asked again later.
        if (encoding is PdfIndirectRef or PdfStream && reader is null) return TwoByte;
        ShowCodespace read;
        try
        {
            read = (reader?.Resolve(encoding) ?? encoding) is PdfStream cmap && reader is not null
                ? Parse(reader.DecodeStream(cmap)) ?? TwoByte
                : TwoByte;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            read = TwoByte;
        }
        lock (Known)
        {
            if (Known.TryGetValue(fontDict, out var known)) return known;
            Known.Add(fontDict, read);
        }
        return read;
    }

    /// <summary>The codespace ranges a CMap program declares; null when it declares none.</summary>
    private static ShowCodespace? Parse(byte[] program)
    {
        var text = Compat.Latin1.GetString(program);
        var ranges = new List<(int first, int last, int width)>();
        var at = text.IndexOf(RangesStart, StringComparison.Ordinal);
        while (at >= 0)
        {
            var end = text.IndexOf(RangesEnd, at, StringComparison.Ordinal);
            if (end < 0) break;
            var hex = new List<(int value, int digits)>();
            var i = at + RangesStart.Length;
            while ((i = text.IndexOf('<', i)) >= 0 && i < end)
            {
                var close = text.IndexOf('>', i);
                if (close < 0 || close > end) break;
                var digits = text.Substring(i + 1, close - i - 1).Trim();
                if (digits.Length is > 0 and <= MaxCodeBytes * HexDigitsPerByte
                    && int.TryParse(digits, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    hex.Add((value, digits.Length));
                i = close + 1;
            }
            for (var k = 0; k + 1 < hex.Count; k += 2)
                ranges.Add((hex[k].value, hex[k + 1].value, (hex[k].digits + 1) / HexDigitsPerByte));
            at = text.IndexOf(RangesStart, end, StringComparison.Ordinal);
        }
        if (ranges.Count == 0) return null;
        ranges.Sort((a, b) => a.width.CompareTo(b.width));
        return new ShowCodespace(ranges.ToArray());
    }

    /// <summary>The bytes of the code that starts at <paramref name="at"/>: the shortest range holding the bytes
    /// read at its width; a code in no range takes the shortest width (9.7.6.2).</summary>
    internal int CodeLength(byte[] bytes, int at)
    {
        foreach (var (first, last, width) in _ranges)
        {
            if (at + width > bytes.Length) continue;
            var code = 0;
            for (var k = 0; k < width; k++) code = (code << ByteBits) | bytes[at + k];
            if (code >= first && code <= last) return width;
        }
        return Math.Min(_ranges[0].width, bytes.Length - at);
    }

    /// <summary>The bytes <paramref name="code"/> is written in: the shortest range that holds it, the default
    /// when none does.</summary>
    internal int Width(int code)
    {
        foreach (var (first, last, width) in _ranges)
            if (code >= first && code <= last && (width >= MaxCodeBytes || code >> (width * ByteBits) == 0))
                return width;
        return DefaultCodeBytes;
    }
}
