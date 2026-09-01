using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
    private void FireTextShown(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        var text = DecodeBytes(bytes, toUnicode);
        OnTextShown?.Invoke(text, bytes, _state);

        // Advance the text matrix by the total string displacement.
        // Per PDF spec §9.4.4: tx = ((w0 - Tj/1000) * Tf + Tc + Tw) for each character.
        // For CID fonts bytes pair into 2-byte codes, and widths are CID-keyed — walking the
        // decoded Unicode string would use wrong width entries and miscount character boundaries.
        var fontSize = _state.FontSize;
        var charSpacing = _state.CharSpacing;
        var wordSpacing = _state.WordSpacing;
        var hScaling = _state.HorizontalScaling / 100.0;
        double totalWidth = 0;

        if (_currentCidInfo is not null && _currentCidInfo.LegacyCodepage != 0)
        {
            // Non-embedded predefined national CMap (GBK-EUC-H, …). The /W table is
            // keyed by the Adobe CID we never resolve (so GetWidth returns /DW, often
            // 500), but the renderer draws these full-width; advancing the cursor by
            // 500 would compress every CJK line to half width. Walk the mixed-width
            // run and use nominal full-width (1000) / half-width (500) — matching what
            // DrawLegacyCjkText advances by — so glyphs and cursor stay in lockstep.
            // Vertical writing-mode (-V) advances down the page, one em per full-width
            // glyph, and isn't affected by horizontal scaling.
            var vert = _currentCidInfo.IsVertical;
            var hs = vert ? 1.0 : hScaling;
            var i = 0;
            while (i < bytes.Length)
            {
                var step = _currentCidInfo.LegacyByteLength(bytes[i]);
                if (step == 2 && i + 1 >= bytes.Length) step = 1;
                var code = step == 2 ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
                var w = Text.CjkFallbackFont.AdvanceEm(_currentCidInfo, _currentMetrics, code, step);
                totalWidth += (w / 1000.0 * fontSize + charSpacing) * hs;
                if (!vert && step == 1 && bytes[i] == 32)
                    totalWidth += wordSpacing * hScaling;
                i += step;
            }
        }
        else if (_currentCidInfo is not null && _currentCidInfo.IsTwoByteEncoding)
        {
            var vertical = _currentCidInfo.IsVertical;
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var cid = (bytes[i] << 8) | bytes[i + 1];
                if (vertical)
                {
                    // Vertical writing: the cursor travels by the VERTICAL displacement
                    // w1 (/W2 per-CID, else /DW2 default -1000) — not the horizontal
                    // width, which is 500 for half-width forms and would halve the pitch.
                    var c = _currentCidInfo.CodeToCid(cid);
                    var w0 = _currentMetrics?.GetWidth(c) ?? 1000;
                    var (w1y, _, _) = _currentCidInfo.VerticalMetrics(c, w0);
                    totalWidth += -w1y / 1000.0 * fontSize + charSpacing;
                    continue;
                }
                // The /W table is keyed by Adobe CIDs. A Unicode CMap (Uni*-UTF16/UCS2)
                // shows codepoints, so map the code to the collection's real CID for
                // the width lookup (the renderers advance the same way); other
                // encodings keep the raw-code key as before.
                var wKey = cid;
                if (_currentCidInfo.IsUnicodeEncoding && _currentCidInfo.Ordering is not null
                    && _currentCidInfo.Ordering != "Identity"
                    && Text.AdobeCidTables.UnicodeToCid(_currentCidInfo.Ordering, cid) is int realCid)
                    wKey = realCid;
                var w = _currentMetrics?.GetWidth(wKey) ?? 1000;
                totalWidth += (w / 1000.0 * fontSize + charSpacing) * hScaling;
                // Word spacing: PDF 32000 §9.3.3 — Tw applies ONLY to the single-byte
                // code 32; a 2-byte <0020> in a UTF16/UCS2 CMap never takes it (a
                // Korean invoice's "-4 Tw <0020>" word gaps must stay the full
                // half-width advance).
            }
        }
        else
        {
            // A simple font advances one width per BYTE — the /Widths array is keyed
            // by the raw byte values (that's how the bytes were paired with widths at
            // embed time). Subset TT fonts with /ToUnicode map byte X to char Y but
            // /Widths still uses X as the key, so a Unicode-char lookup falls through
            // to the MissingWidth/default and the cursor advances wrong (visible as
            // huge letter-spacing on subset-font text). When a /ToUnicode entry
            // expands one code to SEVERAL chars (an Arabic lam-alef ligature, "fi"),
            // the decoded text is longer than the byte string — walking it would add
            // one advance per CHAR, opening a gap after every ligature. Walk the
            // bytes; the decoded text only supplies the char for the fallback width
            // lookup when the mapping is 1:1.
            bool oneToOne = bytes.Length == text.Length;
            for (var i = 0; i < bytes.Length; i++)
            {
                var ch = oneToOne ? text[i] : (char)bytes[i];
                int w = _currentMetrics?.GetWidth(bytes[i]) ?? 0;
                if (w == 0) w = _currentMetrics?.GetWidth(ch) ?? 500;
                totalWidth += (w / 1000.0 * fontSize + charSpacing) * hScaling;
                if (ch == ' ' || bytes[i] == 0x20)
                    totalWidth += wordSpacing * hScaling;
            }
        }

        if (_currentCidInfo is not null && _currentCidInfo.IsVertical)
            _state.AdvanceTextPosition(0, -totalWidth);
        else
            _state.AdvanceTextPosition(totalWidth, 0);
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
        return System.Text.Encoding.Latin1.GetString(bytes);
    }
}
