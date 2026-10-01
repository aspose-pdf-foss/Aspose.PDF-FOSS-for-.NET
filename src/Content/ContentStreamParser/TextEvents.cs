using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
    private void FireTextShown(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        var ts = new TextShownState();
        ts.bytes = bytes;
        ts.toUnicode = toUnicode;
        ts.text = DecodeBytes(ts.bytes, ts.toUnicode);
        OnTextShown?.Invoke(ts.text, ts.bytes, _state);

        ts.fontSize = _state.FontSize;
        ts.charSpacing = _state.CharSpacing;
        ts.wordSpacing = _state.WordSpacing;
        ts.hScaling = _state.HorizontalScaling / 100.0;
        ts.totalWidth = 0;
        if (OnTextCodeAdvances is not null) ts.codeEnds = new List<(int, double)>();

        if (_currentCidInfo is not null && _currentCidInfo.LegacyCodepage != 0)
        {
            DecodeLegacyCodepageShownText(ts);
        }
        else if (_currentCidInfo is not null && _currentCidInfo.IsTwoByteEncoding)
        {
            DecodeTwoByteShownText(ts);
        }
        else
        {
            DecodeSimpleShownText(ts);
        }

        if (ts.codeEnds is not null) OnTextCodeAdvances?.Invoke(ts.codeEnds, _state);
        if (_currentCidInfo is not null && _currentCidInfo.IsVertical)
            _state.AdvanceTextPosition(0, -ts.totalWidth);
        else
            _state.AdvanceTextPosition(ts.totalWidth, 0);
        OnTextShownEnd?.Invoke(_state);
    }

    /// <summary>
    /// Build a code→Unicode map from a simple font's /Encoding (base + /Differences) when
    /// it carries no /ToUnicode. Subset TrueType fonts often number glyphs 1,2,3… and map
    /// them to names via /Differences; without this the bytes decode to control chars
    /// (U+0001…) and the renderer's Unicode-keyed cmap fallback can't find the glyph.
    /// </summary>
    private static Dictionary<int, string>? BuildEncodingToUnicode(PdfDictionary fontDict, IO.PdfReader reader)
    {
        // Only simple fonts carry a byte→name /Encoding; CID fonts use CMaps.
        var enc = reader.Resolve(fontDict.Get("Encoding"));
        if (enc is not PdfDictionary && enc is not PdfName) return null;
        var names = Devices.SoftwarePageRenderer.ResolveEncoding(fontDict, reader);
        var map = new Dictionary<int, string>();
        for (var code = 0; code < 256; code++)
        {
            var name = names[code];
            if (name is null || name == ".notdef") continue;
            var uni = Text.TextAbsorber.ResolveGlyphName(name);
            if (!string.IsNullOrEmpty(uni)) map[code] = uni;
        }
        return map.Count > 0 ? map : null;
    }

    private static string DecodeBytes(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        if (toUnicode is not null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in bytes)
            {
                if (toUnicode.TryGetValue(b, out var mapped))
                    sb.Append(mapped);
                else
                    sb.Append((char)b);
            }
            return sb.ToString();
        }
        return Compat.Latin1.GetString(bytes);
    }
}
