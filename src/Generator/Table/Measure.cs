using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>
    /// Simple word-wrap: splits text into lines that fit within the available width.
    /// Uses an approximate character width of 0.5 * fontSize for Helvetica.
    /// </summary>
    private static List<string> WrapText(string text, double fontSize, double availWidth,
        Func<string, double>? measure = null, bool overflowLongWords = false)
    {
        var lines = new List<string>();
        if (availWidth <= 0 || fontSize <= 0)
        {
            lines.Add(text);
            return lines;
        }

        // Measure with real Helvetica AFM widths instead of a flat 0.5 em
        // estimate — the old estimate let noticeably more characters per
        // line than GDI+ and under-counted page breaks for long cell text.
        // A caller that sized the column itself passes the very measure it used,
        // so the column and the wrap agree to the last bit.
        double MeasureWidth(string s, double sz) => measure is null ? MeasureWidthDefault(s, sz) : measure(s);
        var words = text.Split(' ');
        var spaceW = MeasureWidth(" ", fontSize);
        string currentLine = "";
        double currentWidth = 0;

        // A single word wider than the column splits at character level
        // ("Jurisdiction" in a squeezed 24 pt column renders as
        // "Juris/dictio/n"), filling each line to the width. A hyphen or en-dash
        // inside the word is a soft break opportunity tried FIRST ("B13-9876"
        // wraps to "B13-"/"9876"); only a segment still too wide char-splits.
        void StartWithWord(string word, double wordW)
        {
            // A column auto-fit to exactly this word's width must accept it — the
            // width comparison tolerates the last-bit error the pad add/subtract
            // round-trip introduces.
            if (wordW <= availWidth + 1e-6) { currentLine = word; currentWidth = wordW; return; }
            // HTML layout: a word too wide for its column spills past the cell edge —
            // the column was sized knowing that, and breaking it would show a split
            // a browser never shows.
            if (overflowLongWords) { currentLine = word; currentWidth = wordW; return; }
            if (word.IndexOf('-') > 0 || word.IndexOf('–') > 0)
            {
                var segs = new List<string>();
                var start = 0;
                for (var ci = 0; ci < word.Length; ci++)
                    if (word[ci] is '-' or '–' || ci == word.Length - 1)
                    {
                        segs.Add(word.Substring(start, ci - start + 1));
                        start = ci + 1;
                    }
                if (segs.Count > 1)
                {
                    currentLine = ""; currentWidth = 0;
                    foreach (var seg in segs)
                    {
                        var segW = MeasureWidth(seg, fontSize);
                        if (currentLine.Length == 0) { StartWithWord(seg, segW); continue; }
                        if (currentWidth + segW <= availWidth + 1e-6)
                        {
                            currentLine += seg;
                            currentWidth += segW;
                        }
                        else
                        {
                            lines.Add(currentLine);
                            StartWithWord(seg, segW);
                        }
                    }
                    return;
                }
            }
            var cur = ""; double cw = 0;
            foreach (var ch in word)
            {
                var chW = MeasureWidth(ch.ToString(), fontSize);
                if (cur.Length > 0 && cw + chW > availWidth + 1e-6)
                {
                    lines.Add(cur);
                    cur = ""; cw = 0;
                }
                cur += ch; cw += chW;
            }
            currentLine = cur;
            currentWidth = cw;
        }

        // "Has this line been opened yet" is NOT "is it still empty": a line opened by
        // the empty token that leads a run of spaces has length zero and still owns
        // every space after it. Testing emptiness dropped a paragraph's leading
        // indent, and with it the line the indent occupies when the word behind it is
        // too long to follow (cell paragraphs can be indented by the verbatim
        // string that wrote them, and the shipped template gives each indent its own
        // line).
        var lineStarted = false;
        foreach (var word in words)
        {
            var wordW = MeasureWidth(word, fontSize);
            if (!lineStarted)
            {
                StartWithWord(word, wordW);
                lineStarted = true;
                continue;
            }
            var withSpaceW = currentWidth + spaceW + wordW;
            if (withSpaceW <= availWidth + 1e-6)
            {
                currentLine += " " + word;
                currentWidth = withSpaceW;
            }
            else if (!TryZeroWidthSplit(word))
            {
                lines.Add(currentLine);
                StartWithWord(word, wordW);
            }
        }

        // U+200B is invisible and carries no advance, but it IS a legal wrap point. A
        // word that will not fit whole is retried at its zero-width spaces, so a line
        // packs the way a browser packs it instead of pushing the whole run down.
        bool TryZeroWidthSplit(string word)
        {
            if (word.IndexOf(ZeroWidthSpace) < 0) return false;
            var segs = new List<string>();
            var segStart = 0;
            for (var ci = 0; ci < word.Length; ci++)
                if (word[ci] == ZeroWidthSpace || ci == word.Length - 1)
                {
                    segs.Add(word.Substring(segStart, ci - segStart + 1));
                    segStart = ci + 1;
                }
            if (segs.Count < 2) return false;
            var needSpace = true;
            foreach (var seg in segs)
            {
                // A segment that is nothing but the break character carries no ink and
                // no box: breaking AT a zero-width space must not leave an empty line.
                if (seg.Trim(ZeroWidthSpace).Length == 0)
                {
                    if (currentLine.Length > 0) currentLine += seg;
                    continue;
                }
                var segW = MeasureWidth(seg, fontSize);
                if (currentLine.Length == 0) { StartWithWord(seg, segW); needSpace = false; continue; }
                var add = (needSpace ? spaceW : 0) + segW;
                if (currentWidth + add <= availWidth + 1e-6)
                {
                    currentLine += (needSpace ? " " : "") + seg;
                    currentWidth += add;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = ""; currentWidth = 0;
                    StartWithWord(seg, segW);
                }
                needSpace = false;
            }
            return true;
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine);

        if (lines.Count == 0)
            lines.Add("");

        return lines;
    }

    /// <summary>
    /// Measure a string's width in points using Arial glyph widths (1/1000
    /// text units scaled by font size). Arial is what GDI+ defaults to for
    /// HTML/table text when no explicit font is set, and it is ~8% wider
    /// than Helvetica on mixed lowercase text. Using Arial widths here
    /// puts our wrap breakpoints in line with the expected layout.
    /// Characters outside WinAnsi fall back to the font's default width.
    /// </summary>
    /// <summary>Widest single unbreakable word across a cell's paragraphs, at each
    /// paragraph's effective font size. Drives AutoFitToContent column sizing: the
    /// column must be at least this wide so no word is split, but multi-word content
    /// wraps within it.</summary>
    private double MaxWordWidth(Cell cell, Row row)
    {
        var cellFs = ResolveCellFontSize(cell, row);
        double max = 0;
        foreach (var p in cell.Paragraphs)
        {
            string? text;
            var fs = cellFs;
            if (p is Text.TextFragment tf) { text = tf.Text; fs = ResolveCellParagraphFontSize(tf, cellFs, cell, row); }
            else if (p is HtmlFragment h)
            {
                // Bold-serif HTML cell: the column sizes to the kerned Times New Roman
                // Bold advance at the HTML default size, not the Helvetica estimate.
                if (TryBoldOnlyHtml(h.HtmlContent, out var boldText) && BoldSerifTtf() is { } serifTtf)
                {
                    var bw = MeasureWidthKerned(boldText, HtmlCellFontSize, serifTtf);
                    if (bw > max) max = bw;
                    continue;
                }
                text = HtmlFragment.StripHtmlTags(h.HtmlContent ?? string.Empty);
            }
            else continue;
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                foreach (var word in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var wWidth = MeasureWidth(word, fs);
                    if (wWidth > max) max = wWidth;
                }
        }
        return max;
    }

    /// <summary>The generator's measure guard added onto every max-content auto-fit
    /// column: probed (columns land at
    /// text + padding + 0.01 to the third decimal; GetWidth reports 88.94 for two
    /// 44.46 pt cells).</summary>
    internal const double AutoFitMeasureGuardPt = 0.01;

    /// <summary>Widest full LINE of a cell's content — the max-content width used
    /// when a column-paginating auto-fit table sizes its columns to the unwrapped
    /// text (see the AutoFitToContent branch of ParseColumnWidths). With
    /// <paramref name="exact"/> the bare Helvetica AFM advance is used (the
    /// GetWidth measurement contract: 2 × "Cell N text" reports 88.94), without it
    /// the layout's inflation-guarded measure drives the wrap/pack decisions.</summary>
    private double MaxLineWidth(Cell cell, Row row, bool exact = false)
    {
        var cellFs = ResolveCellFontSize(cell, row);
        double max = 0;
        foreach (var p in cell.Paragraphs)
        {
            string? text;
            var fs = cellFs;
            if (p is Text.TextFragment tf)
            {
                text = tf.Text;
                fs = ResolveCellParagraphFontSize(tf, cellFs, cell, row);
                // With no size declared anywhere the fragment DRAWS at its own
                // 10 pt placeholder — measure at what draws, not the 12 pt
                // cell-resolution fallback (GetWidth 88.94 = 2 × 44.47 pins it).
                if (!tf.TextState.FontSizeTouched && cell.DefaultCellTextState is null
                    && row.DefaultCellTextState is null && DefaultCellTextState is null)
                    fs = tf.TextState.FontSize;
            }
            else if (p is HtmlFragment h) text = HtmlFragment.StripHtmlTags(h.HtmlContent ?? string.Empty);
            else continue;
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var lw = exact ? MeasureWidthExactAfm(line, fs) : MeasureWidth(line, fs);
                if (lw > max) max = lw;
            }
        }
        return max;
    }

    /// <summary>Bare Helvetica AFM advance sum — no layout inflation. The
    /// measurement contract behind <see cref="Table.GetWidth"/>'s auto-fit
    /// report (probed: columns land at AFM text + padding + 0.01
    /// to the third decimal).</summary>
    private static double MeasureWidthExactAfm(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            var w = code is >= 0 and <= 255 ? Standard14Fonts.GetWidth("Helvetica", code) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            total += w;
        }
        return total * fontSize / 1000.0;
    }

    /// <summary>The form-grid measure path's advance for a char the base face does
    /// not map: Verdana's OS/2 xAvgCharWidth (1229 units of the 2048 em). The
    /// name-row wrap point pins it (words 1..10 fit its 285pt box,
    /// word 11 overflows; only an advance in (0.510, 0.605] em satisfies both).</summary>
    private const double FormGridUnmappedAdvanceEm = 1229.0 / 2048.0;

    /// <summary>The installed variant's face name ("Verdana Bold Italic").</summary>
    private static string CellFaceName(string family, bool bold, bool italic) =>
        bold && italic ? family + " Bold Italic"
        : bold ? family + " Bold"
        : italic ? family + " Italic"
        : family;

    private static byte[]? CellFaceTtf(string family, bool bold, bool italic = false)
    {
        lock (_cellFaceTtfs)
        {
            if (_cellFaceTtfs.TryGetValue((family, bold, italic), out var cached)) return cached;
            byte[]? ttf = null;
            try { ttf = Aspose.Pdf.Text.FontRepository.GetTtfData(CellFaceName(family, bold, italic)); }
            catch { }
            if (ttf is null && (bold || italic))
                try { ttf = Aspose.Pdf.Text.FontRepository.GetTtfData(family); }
                catch { }
            _cellFaceTtfs[(family, bold, italic)] = ttf;
            return ttf;
        }
    }

    private static byte[]? BoldSerifTtf()
    {
        // The corpus runs fixtures 8-wide: the resolve must be atomic, or a second
        // thread can read half-written metrics (upm set, win ascent not) and lay a
        // cell out on a zero line box.
        lock (_serifInit) return BoldSerifTtfCore();
    }

    private static byte[]? BoldSerifTtfCore()
    {
        if (_serifTried) return _serifBoldTtf;
        _serifTried = true;
        try
        {
            var reg = Aspose.Pdf.Text.FontRepository.GetTtfData("Times New Roman");
            var bold = Aspose.Pdf.Text.FontRepository.GetTtfData("Times New Roman Bold");
            if (reg is not null && bold is not null)
            {
                var tp = new Aspose.Pdf.Text.TrueTypeParser(reg);
                tp.Parse();
                if (tp.UsWinAscent > 0 && tp.UnitsPerEm > 0)
                {
                    _serifUpm = tp.UnitsPerEm;
                    _serifHheaSum = tp.Ascent + Math.Abs(tp.Descent) + tp.LineGap;
                    _serifWinAsc = tp.UsWinAscent;
                    _serifWinDesc = tp.UsWinDescent;
                    (_serifRootBox, _serifBaseDrop) = SerifLineBox(HtmlCellFontSize);
                    _serifDescFrac = tp.UsWinDescent / _serifUpm;
                    _serifTtf = reg;
                    _serifBoldTtf = bold;
                }
            }
        }
        catch { /* faces unavailable: the legacy path stays */ }
        return _serifBoldTtf;
    }

    /// <summary>The regular serif face (resolved together with the bold one).</summary>
    private static byte[]? SerifTtf()
    {
        BoldSerifTtf();
        return _serifTtf;
    }

    /// <summary>Width of <paramref name="s"/> in points using the embedded font's real
    /// advances plus 'kern' pair adjustments — HTML-engine runs are kerned,
    /// and autofit columns size to the kerned width exactly.</summary>
    private static double MeasureWidthKerned(string s, double fontSize, byte[] ttf)
    {
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return MeasureWidthExact(s, fontSize);
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double total = 0;
        var prev = -1;
        foreach (var ch in s)
        {
            var gid = gp.GlyphIdOrLookAlike(ch);
            if (IsZeroAdvanceMark(ch)) continue;   // a combining mark occupies no width
            if (prev >= 0) total += gp.GetKernAdjustment(prev, gid);
            total += gp.GetAdvanceWidth(gid);
            prev = gid;
        }
        return total * fontSize / upm;
    }

    /// <summary>A combining mark — one that draws ON the character beside it (an enclosing
    /// circle round an option letter, an accent) — occupies NO advance of its own. A face
    /// that has no glyph for one would otherwise spend its whole missing-glyph advance on
    /// it, widening the run and the column that has to hold it.</summary>
    private static bool IsZeroAdvanceMark(char c) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
            is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.EnclosingMark;

    /// <summary>TJ adjustment array (thousandths of text space; positive pulls the following
    /// glyphs left) for the pair-kerning of <paramref name="s"/>, or null when no pair kerns.</summary>
    private static double[]? KernAdjustments(string s, byte[] ttf)
    {
        if (s.Length < 2) return null;
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return null;
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double[]? adj = null;
        var prev = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var gid = gp.GlyphIdOrLookAlike(s[i]);
            // A combining mark draws where the pen already is: pull the run back by the
            // whole advance the face would otherwise spend on it, so the character it
            // marks sits directly under it.
            if (IsZeroAdvanceMark(s[i]) && i < s.Length - 1)
            {
                adj ??= new double[s.Length - 1];
                adj[i] += gp.GetAdvanceWidth(gid) * 1000.0 / upm;
                continue;
            }
            if (prev >= 0)
            {
                var kern = gp.GetKernAdjustment(prev, gid);
                if (kern != 0)
                {
                    adj ??= new double[s.Length - 1];
                    adj[i - 1] += -kern * 1000.0 / upm;
                }
            }
            prev = gid;
        }
        return adj;
    }

    /// <summary>True when a CSS font-family resolves to a serif face the HTML
    /// engine substitutes with its embedded serif (Times New Roman) family.</summary>
    private static bool IsSerifCssFamily(string? family)
    {
        if (string.IsNullOrEmpty(family)) return false;
        return family.IndexOf("georgia", StringComparison.OrdinalIgnoreCase) >= 0
            || family.IndexOf("times", StringComparison.OrdinalIgnoreCase) >= 0
            || family.IndexOf("serif", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Greedy word wrap measured with the embedded face's kerned advances
    /// (the metrics the styled serif cell line renders with).</summary>
    private static List<string> WrapKernedLines(string s, double size, byte[] ttf, double avail)
    {
        var res = new List<string>();
        var cur = "";
        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var cand = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && avail > 0 && MeasureWidthKerned(cand, size, ttf) > avail + 1e-6)
            { res.Add(cur); cur = word; }
            else cur = cand;
        }
        if (cur.Length > 0) res.Add(cur);
        return res;
    }

    /// <summary>Points in one centimetre as the generator's width parser counts them.
    /// NOT 72/2.54 = 28.346: measured (a `"10 1cm 2cm 1.5cm"` grid draws
    /// its columns 10 / 28.7 / 57.4 / 43.05 pt wide), the generator's centimetre is a
    /// flat 28.7 pt — the same constant the XML generator uses.</summary>
    private const double GeneratorPointsPerCm = 28.7;

    /// <summary>Resolve one <see cref="Table.ColumnWidths"/> / <see cref="Table.DefaultColumnWidth"/>
    /// token to points. The generator reads a bare number and a <c>cm</c> suffix; every
    /// other suffix (mm, in, pt, px, pc) makes the generator layout throw a
    /// FormatException, so an unreadable token keeps the historical fallback rather than
    /// inventing a unit. A trailing '%' is the caller's business — it needs the band.</summary>
    private static bool TryParseWidthToken(string tok, out double points)
    {
        points = 0;
        if (tok.Length == 0) return false;
        var num = tok;
        var scale = 1.0;
        if (tok.EndsWith("cm", StringComparison.OrdinalIgnoreCase))
        {
            num = tok.Substring(0, tok.Length - 2);
            scale = GeneratorPointsPerCm;
        }
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && !double.TryParse(num.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out v))
            return false;
        points = v * scale;
        return true;
    }

    private static double MeasureWidth(string s, double fontSize) => MeasureWidthDefault(s, fontSize);

    private static double MeasureWidthDefault(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            int w;
            if (code >= 0 && code <= 255)
            {
                w = Standard14Fonts.GetWidth("Helvetica", code);
                if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            }
            else
            {
                w = Standard14Fonts.GetDefaultWidth("Helvetica");
            }
            total += w;
        }
        // Arial (the layout engine's default) is only marginally wider than
        // the Helvetica AFM widths used here, so apply a small ~5% inflation rather than a
        // heavy fudge — an over-large factor wraps cell text a word too early (e.g. a
        // "living facility" run that fits the column gets split across two lines).
        // ⚠ This is the HTML dialects' estimate, and their columns are calibrated
        // against it. The GENERATOR measures its own cells with the bare AFM advance
        // (<see cref="MeasureWidthExactAfm"/>) — probed by bracketing the wrap threshold
        // from both sides: "MMMM MMMM" is 69.42 pt at 10 pt and stays on
        // one line in a 70 pt column, breaking in a 69 pt one.
        return total * fontSize * 1.05 / 1000.0;
    }

    private static Aspose.Pdf.Text.GlyphOutlineParser? GetInlineGlyphParser(byte[] ttf)
    {
        if (_inlineGlyphParsers.TryGetValue(ttf, out var cached)) return cached;
        Aspose.Pdf.Text.GlyphOutlineParser? p = null;
        try { p = new Aspose.Pdf.Text.GlyphOutlineParser(ttf); } catch { }
        _inlineGlyphParsers[ttf] = p;
        return p;
    }

    /// <summary>Width of <paramref name="s"/> in points using an embedded font's real glyph
    /// advances (cmap → hmtx), for laying out a per-segment Type0 inline run.</summary>
    private static double MeasureWidthWithFont(string s, double fontSize, byte[] ttf,
        double unmappedEm = 0)
    {
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return MeasureWidthExact(s, fontSize);
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double total = 0;
        for (var i = 0; i < s.Length; i++)
        {
            // Codepoint walk: a plane-2 ideograph arrives as a surrogate pair and
            // must price as ONE glyph, not two notdef advances.
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            if (!gp.CMap.TryGetValue(cp, out var g))
            {
                // An unmapped char at the caller's own advance (the form-grid
                // measure path prices missing glyphs at the face's average char
                // width); zero keeps the legacy
                // notdef advance.
                if (unmappedEm > 0) { total += unmappedEm * upm; continue; }
                g = 0;
            }
            total += gp.GetAdvanceWidth(g);
        }
        return total * fontSize / upm;
    }

    /// <summary>Greedy character-level width wrap for CJK cell text (which has no ASCII spaces
    /// to break at), measured with the given fallback font. Every character is preserved —
    /// including spaces at a break — so the concatenated lines reconstruct the input exactly.
    /// A single character wider than the box is left on its own overflowing line.</summary>
    private static List<string> WrapCjkToWidth(string s, double fontSize, double availWidth, byte[] ttf)
    {
        var lines = new List<string>();
        if (availWidth <= 0) { lines.Add(s); return lines; }
        var cur = new System.Text.StringBuilder();
        double curW = 0;
        foreach (var ch in s)
        {
            double chW = MeasureWidthWithFont(ch.ToString(), fontSize, ttf);
            if (cur.Length > 0 && curW + chW > availWidth)
            {
                lines.Add(cur.ToString());
                cur.Clear();
                curW = 0;
            }
            cur.Append(ch);
            curW += chW;
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return lines;
    }

    /// <summary>Split text into wrap tokens, each keeping its trailing space, so word-wrapping
    /// an inline run preserves inter-word spacing (e.g. "a b " → ["a ", "b "]).</summary>
    private static IEnumerable<string> SplitKeepingSpaces(string s)
    {
        var start = 0;
        for (var i = 0; i < s.Length; i++)
            if (s[i] == ' ') { yield return s.Substring(start, i - start + 1); start = i + 1; }
        if (start < s.Length) yield return s.Substring(start);
    }

    /// <summary>Exact Standard-14 advance in the cell's own face — the measure the HTML
    /// layout pass sizes columns with, so a wrap made here falls where that pass expects.</summary>
    private static double MeasureFaceExact(string s, double fontSize, bool bold)
    {
        if (s.Length == 0) return 0;
        var face = bold ? "Helvetica-Bold" : "Helvetica";
        try
        {
            var f = Aspose.Pdf.Text.FontRepository.TryFindFont(face);
            if (f is not null) return f.MeasureString(s, fontSize);
        }
        catch { }
        return MeasureWidthExact(s, fontSize);
    }

    private static double MeasureWidthExact(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            var w = code is >= 0 and <= 255 ? Standard14Fonts.GetWidth("Helvetica", code) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            total += w;
        }
        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// Register a Helvetica font in the page resources and return the font resource name.
    /// </summary>
    /// <summary>Resolve (creating if needed) the page's /Resources /Font dictionary, used to
    /// register an embedded Type0 font for Arabic/Unicode cell text.</summary>
    internal static PdfDictionary ResolvePageFontDict(Page page)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) { resources = new PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) { fontDict = new PdfDictionary(); resources.Set("Font", fontDict); }
        return fontDict;
    }

    internal static string RegisterFont(Page page) => RegisterFont(page, "Helvetica");

    /// <summary>Register a standard Type1 base font (e.g. "Helvetica",
    /// "Helvetica-Bold") on the page's resource dictionary, reusing an existing
    /// matching entry, and return its resource name.</summary>
    internal static string RegisterFont(Page page, string baseFont)
    {
        // Resolve indirect /Resources and /Font in place; a bare cast would miss a
        // PdfReference and replace the real dictionary with an empty one, dropping
        // the page's existing fonts and XObjects (e.g. a background image).
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        // Reuse an already-registered entry for the same base font
        foreach (var key in fontDict.Keys)
        {
            if (page.Reader.Resolve(fontDict.Get(key)) is PdfDictionary existing)
            {
                var baseFontName = existing.GetName("BaseFont");
                if (baseFontName == baseFont)
                    return key;
            }
        }

        // Find a unique font name
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        // Cell text is written as WinAnsi bytes (see ContentStreamBuilder.ToWinAnsi);
        // without the matching /Encoding the CP1252 0x80-0x9F range (€, dashes,
        // curly quotes) is undefined in the font's default StandardEncoding.
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(name, font);
        return name;
    }
}
