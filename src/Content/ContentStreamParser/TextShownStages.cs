using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
    /// <summary>One decode arm of the text-shown event.</summary>
    private void DecodeTwoByteShownText(TextShownState ts)
    {
        var vertical = _currentCidInfo!.IsVertical;
        for (var i = 0; i + 1 < ts.bytes.Length; i += 2)
        {
            var cid = (ts.bytes[i] << 8) | ts.bytes[i + 1];
            if (vertical)
            {
                // Vertical writing: the cursor travels by the VERTICAL displacement
                // w1 (/W2 per-CID, else /DW2 default -1000) — not the horizontal
                // width, which is 500 for half-width forms and would halve the pitch.
                var c = _currentCidInfo.CodeToCid(cid);
                var w0 = _currentMetrics?.GetWidth(c) ?? 1000;
                var (w1y, _, _) = _currentCidInfo.VerticalMetrics(c, w0);
                ts.totalWidth += -w1y / 1000.0 * ts.fontSize + ts.charSpacing;
                ts.codeEnds?.Add((i + 2, ts.totalWidth));
                continue;
            }
            // The /W table is keyed by CIDs. An embedded CMap maps each code to its CID
            // first, as the renderers do when they draw the glyph - keyed by the raw code,
            // a subset whose CMap packs Latin letters into low CIDs advanced every glyph
            // by the /DW default and opened a gap after each shown string. A Unicode CMap
            // (Uni*-UTF16/UCS2) shows codepoints, so map the code to the collection's
            // real CID for the width lookup.
            var wKey = _currentCidInfo.CodeToCid(cid);
            if (_currentCidInfo.IsUnicodeEncoding && _currentCidInfo.Ordering is not null
                && _currentCidInfo.Ordering != "Identity"
                && Text.AdobeCidTables.UnicodeToCid(_currentCidInfo.Ordering, cid) is int realCid)
                wKey = realCid;
            var w = _currentMetrics?.GetWidth(wKey) ?? 1000;
            ts.totalWidth += (w / 1000.0 * ts.fontSize + ts.charSpacing) * ts.hScaling;
            // Word spacing: PDF 32000 §9.3.3 — Tw applies ONLY to the single-byte
            // code 32; a 2-byte <0020> in a UTF16/UCS2 CMap never takes it (a
            // Korean invoice's "-4 Tw <0020>" word gaps must stay the full
            // half-width advance).
            ts.codeEnds?.Add((i + 2, ts.totalWidth));
        }
    }

    /// <summary>One decode arm of the text-shown event.</summary>
    private void DecodeLegacyCodepageShownText(TextShownState ts)
    {
        // Non-embedded predefined national CMap (GBK-EUC-H, …). The /W table is
        // keyed by the Adobe CID we never resolve (so GetWidth returns /DW, often
        // 500), but the renderer draws these full-width; advancing the cursor by
        // 500 would compress every CJK line to half width. Walk the mixed-width
        // run and use nominal full-width (1000) / half-width (500) — matching what
        // DrawLegacyCjkText advances by — so glyphs and cursor stay in lockstep.
        // Vertical writing-mode (-V) advances down the page, one em per full-width
        // glyph, and isn't affected by horizontal scaling.
        var vert = _currentCidInfo!.IsVertical;
        var hs = vert ? 1.0 : ts.hScaling;
        var i = 0;
        while (i < ts.bytes.Length)
        {
            var step = _currentCidInfo.LegacyByteLength(ts.bytes[i]);
            if (step == 2 && i + 1 >= ts.bytes.Length) step = 1;
            var code = step == 2 ? ((ts.bytes[i] << 8) | ts.bytes[i + 1]) : ts.bytes[i];
            var w = Text.CjkFallbackFont.AdvanceEm(_currentCidInfo, _currentMetrics, code, step);
            ts.totalWidth += (w / 1000.0 * ts.fontSize + ts.charSpacing) * hs;
            if (!vert && step == 1 && ts.bytes[i] == 32)
                ts.totalWidth += ts.wordSpacing * ts.hScaling;
            i += step;
            ts.codeEnds?.Add((i, ts.totalWidth));
        }
    }

    /// <summary>x</summary>
    private void DecodeSimpleShownText(TextShownState ts)
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
        bool oneToOne = ts.bytes.Length == ts.text.Length;
        for (var i = 0; i < ts.bytes.Length; i++)
        {
            var ch = oneToOne ? ts.text[i] : (char)ts.bytes[i];
            int w = _currentMetrics?.GetWidth(ts.bytes[i]) ?? 0;
            if (w == 0) w = _currentMetrics?.GetWidth(ch) ?? 500;
            ts.totalWidth += (w / 1000.0 * ts.fontSize + ts.charSpacing) * ts.hScaling;
            if (ch == ' ' || ts.bytes[i] == 0x20)
                ts.totalWidth += ts.wordSpacing * ts.hScaling;
            ts.codeEnds?.Add((i + 1, ts.totalWidth));
        }
    }
}
