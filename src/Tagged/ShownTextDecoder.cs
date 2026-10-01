using System.Collections.Generic;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Tagged;

/// <summary>The text of a shown string. The content parser decodes shown bytes one per
/// character, which splits a 2-byte CID code (Identity-H) into a NUL plus a letter; strings
/// in such fonts go through the extraction decoder instead. One instance per parse: it
/// caches what it learns about each font resource.</summary>
internal sealed class ShownTextDecoder(PdfReader reader, Dictionary<string, PdfDictionary> fonts)
{
    private readonly Dictionary<string, (PdfDictionary Font, Dictionary<int, string>? ToUnicode)?> _twoByte = new();

    /// <summary>The text of <paramref name="bytes"/> shown in the font resource
    /// <paramref name="fontKey"/>; <paramref name="parsed"/> (the parser's reading) when the
    /// font is not a two-byte one.</summary>
    public string Decode(string? fontKey, byte[] bytes, string parsed)
    {
        if (fontKey is null || !fonts.TryGetValue(fontKey, out var fontDict)) return parsed;
        if (!_twoByte.TryGetValue(fontKey, out var cid))
        {
            Text.CidFontInfo? info = null;
            try { info = Text.CidFontInfo.TryBuild(fontDict, reader); } catch { /* not a CID font */ }
            cid = info is { IsTwoByteEncoding: true }
                ? (fontDict, Text.TextAbsorber.ParseToUnicodeFromDict(fontDict, reader))
                : null;
            _twoByte[fontKey] = cid;
        }
        return cid is { } c ? Text.TextAbsorber.DecodeStringPublic(bytes, c.ToUnicode, c.Font, reader, foldNbsp: false) : parsed;
    }
}
