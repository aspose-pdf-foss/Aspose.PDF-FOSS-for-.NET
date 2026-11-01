using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>The standard face whose codes are dingbats rather than letters; no host carries it.</summary>
    internal const string DingbatsFaceName = "ZapfDingbats";

    /// <summary>Shared by both renderers: true when the font is the standard dingbats face (a subset
    /// tag aside).</summary>
    internal static bool IsDingbatsFace(PdfDictionary fontDict, IO.PdfReader reader)
    {
        var baseFont = (reader.Resolve(fontDict.Get("BaseFont")) as PdfName)?.Value;
        if (baseFont is { Length: > 7 } && baseFont[6] == '+') baseFont = baseFont.Substring(7);
        return baseFont == DingbatsFaceName;
    }

    /// <summary>Shared by both renderers: for the standard dingbats face, the code of the face's
    /// own encoding behind each code of the font. An encoding that names a code (/Differences
    /// [1 /a35 /space]) shows the named dingbat there, and nothing for a name the face does not
    /// have; a code it leaves alone is the face's own. Null when the encoding names no code.</summary>
    internal static byte[]? BuildDingbatCodes(PdfDictionary fontDict, IO.PdfReader reader)
    {
        if (reader.ResolveDict(fontDict.Get("Encoding")) is not { } enc
            || reader.Resolve(enc.Get("Differences")) is not PdfArray diffs)
            return null;
        byte[]? map = null;
        var code = 0;
        foreach (var item in diffs)
        {
            if (item is PdfInteger start) { code = (int)start.Value; continue; }
            if (item is not PdfName name) continue;
            if (code >= 0 && code < SimpleFontCodeCount)
            {
                if (map is null)
                {
                    map = new byte[SimpleFontCodeCount];
                    for (var i = 0; i < map.Length; i++) map[i] = (byte)i;
                }
                map[code] = ZapfDingbatsGlyphNames.StandardCode(name.Value);
            }
            code++;
        }
        return map;
    }

    /// <summary>The characters a run in the standard dingbats face shows, one per code, or null
    /// for any other face: the substitute face is addressed by character.</summary>
    private static string? DingbatText(RenderContext ctx, string? fontName, byte[] rawBytes)
    {
        if (fontName is null || ctx.FontDicts is null || !ctx.FontDicts.TryGetValue(fontName, out var fd)
            || !IsDingbatsFace(fd, ctx.Reader))
            return null;
        if (!ctx.DingbatCodeMaps.TryGetValue(fd, out var codes))
            ctx.DingbatCodeMaps[fd] = codes = BuildDingbatCodes(fd, ctx.Reader);
        var chars = new char[rawBytes.Length];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = TextAbsorber.DingbatCharacter(codes is null ? rawBytes[i] : codes[rawBytes[i]]);
        return new string(chars);
    }
}
