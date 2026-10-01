using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The table parser's working set, lifted out of BuildTableFromHtml: each
// method takes the parse state, the column model and the settled dialect
// scalars it reads. Bodies are verbatim.
    private static void AddBoxSeg(TableParseState ps, ChainBoxRun r)
    {
        var raw = ps.line.ToString();
        var start = Math.Min(r.StartLen, raw.Length);
        var boxText = CollapseWs(raw[start..]);
        if (boxText.Length == 0 && r.CircleFill is null) return;
        (ps.cellBoxSegs ??= new List<(int, ChainBoxRun, string, string)>())
            .Add((ps.lines.Count, r, CollapseWs(raw[..start]), boxText));
    }

    private static Text.Font? ResolveProbeFont(TableParseState ps, HtmlLoadOptions? options, bool widenProbe, string? fam, bool bold)
    {
        fam ??= ps.cellFamily;
        if (fam is null) return null;
        ps.probeFonts ??= new();
        if (ps.probeFonts.TryGetValue((fam, bold), out var cached)) return cached;
        Text.Font? r = null;
        // The UA checkbox grid measures its headers in the REAL bold program (the styled lookup
        // falls back to the regular face for a system family), as the render draws them.
        try
        {
            r = ps.uaControlGrid && bold ? Text.FontRepository.FindEmbeddableStyledFont(fam, Text.FontStyles.Bold) : null;
            r ??= Text.FontRepository.TryFindFont(fam, bold ? Text.FontStyles.Bold : Text.FontStyles.Regular, ignoreCase: true);
        }
        catch { }
        ps.probeFonts[(fam, bold)] = r;
        return r;
    }

    private static double MeasureLine(TableParseState ps, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, string s, bool bold = false, double pt = 0, string? fam = null)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        // The DataWorks control markers are glyphless — their widget widths
        // ride cellImgWidthPt, so the text measure must see them as nothing.
        if (dwFormCells && (s.IndexOf(Table.InlineInputChar) >= 0
            || s.IndexOf(Table.InlineCheckChar) >= 0
            || s.IndexOf(Table.InlineCheckboxGapChar) >= 0))
            s = s.Replace(Table.InlineInputChar.ToString(), "")
                .Replace(Table.InlineCheckChar.ToString(), "")
                .Replace(Table.InlineCheckboxGapChar.ToString(), "");
        if (string.IsNullOrEmpty(s)) return 0;
        var size = pt > 0 ? pt : cellFontSize;
        // An inline button's caption measures as text plus the button chrome —
        // face pads + outline outsets, em-scaled from the 12 pt probe (the
        // markers themselves have no glyphs).
        if (s.IndexOf(Table.InlineButtonChar) >= 0)
        {
            var bn = 0;
            foreach (var bc in s) if (bc == Table.InlineButtonChar) bn++;
            return MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, s.Replace(Table.InlineButtonChar.ToString(), "")
                    .Replace(Table.InlineButtonEndChar.ToString(), ""), bold, size, fam)
                + bn * Table.InlineButtonChromePt * size / 12.0;
        }
        // Probe lines mark superscript/subscript runs with sentinel chars; those
        // runs measure at 85% of the line's size (the CSS the filing dialect uses),
        // the way the rendered glyphs shrink.
        if (s.IndexOfAny(ProbeSentinels) >= 0)
        {
            double total = 0; var sup = false; var bDepth = 0; var st = 0;
            for (var i = 0; i <= s.Length; i++)
                if (i == s.Length || s[i] is >= '\uE000' and <= '\uE003')
                {
                    if (i > st) total += MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, s[st..i], bold || bDepth > 0, sup ? size * 0.85 : size, fam);
                    if (i < s.Length)
                        switch (s[i])
                        {
                            case '\uE000': bDepth++; break;
                            case '\uE001': bDepth = Math.Max(0, bDepth - 1); break;
                            default: sup = s[i] == '\uE002'; break;
                        }
                    st = i + 1;
                }
            return total;
        }
        try
        {
            var mf = ps.measureFont;
            if ((widenProbe || ps.uaControlGrid) && (bold || fam is not null))
                mf = ResolveProbeFont(ps, options, widenProbe, fam, bold) ?? ps.measureFont;
            // A system font resolved via FindFont has an empty PDF font dict (no /Widths),
            // so Font.MeasureString would default every glyph to 1 em. Read the real glyph
            // advances from the source TTF (hmtx) instead when available.
            if (mf?.SourceFontData?.TtfData is { Length: > 0 })
                return mf.SourceFontData.MeasureString(s, size);
            if (mf is not null) return mf.MeasureString(s, size);
            // No resolved font: the cell renders in a Standard-14 face (Helvetica, or
            // Helvetica-Bold for a <th>). Measure with that face's AFM advances so the
            // column width matches the rendered text — a flat 0.5-em average underestimates
            // real glyph widths (and a bold header most of all), wrapping a header that
            // should stay on one line (multi-word headers stay whole).
            var baseFont = bold ? "Helvetica-Bold" : "Helvetica";
            double total = 0;
            // An ideograph advances a FULL em (the CJK-advance law) —
            // the AFM table has no entry for it and would count it small.
            // Probe-only: the legacy render dialects are calibrated on the
            // AFM count and must not move.
            for (var ci = 0; ci < s.Length; ci++)
            {
                int cp = s[ci];
                if (char.IsHighSurrogate(s[ci]) && ci + 1 < s.Length && char.IsLowSurrogate(s[ci + 1]))
                {
                    cp = char.ConvertToUtf32(s[ci], s[ci + 1]);
                    ci++;
                }
                total += (widenProbe || fullWidthCjkMin) && IsFullWidthCp(cp) ? 1000
                    : cp > 0xFFFF ? 500
                    : Text.Standard14Fonts.GetWidth(baseFont, (char)cp);
            }
            if (total > 0) return total / 1000.0 * size;
        }
        catch { }
        // fallback: average glyph advance — a full em for full-width
        // codepoints (probe-only, see above)
        double est = 0;
        for (var ci = 0; ci < s.Length; ci++)
        {
            int cp = s[ci];
            if (char.IsHighSurrogate(s[ci]) && ci + 1 < s.Length && char.IsLowSurrogate(s[ci + 1]))
            {
                cp = char.ConvertToUtf32(s[ci], s[ci + 1]);
                ci++;
            }
            est += ((widenProbe || fullWidthCjkMin) && IsFullWidthCp(cp) ? 1.0 : 0.5) * size;
        }
        return est;
    }

    private static double MeasureSerifLine(double cellFontSize, string s, bool bold, double pt)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var size = pt > 0 ? pt : cellFontSize;
        var face = bold ? "Times-Bold" : "Times-Roman";
        double total = 0;
        foreach (var ch in s) total += Text.Standard14Fonts.GetWidth(face, ch);
        return total / 1000.0 * size;
    }

    /// <summary>The characters a line never breaks BEFORE (UAX #14's infix separators, closers and
    /// terminators): a label written <c>Direction :</c> is one unbreakable token, not two.</summary>
    internal const string NoBreakBeforeChars = ":;,.!?%)]}";

    /// <summary>The tokens a min-content measure takes the widest of: the space-separated words, with a
    /// word made only of <see cref="NoBreakBeforeChars"/> joined back onto the one before it (and its
    /// space), because no break opportunity stands there.</summary>
    private static List<string> MinContentWords(string s, bool joinNoBreakBefore)
    {
        var words = new List<string>(s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!joinNoBreakBefore) return words;
        for (var i = words.Count - 1; i > 0; i--)
            if (words[i].TrimEnd(NoBreakBeforeChars.ToCharArray()).Length == 0)
            {
                words[i - 1] = words[i - 1] + " " + words[i];
                words.RemoveAt(i);
            }
        return words;
    }

    private static double MeasureMinContent(TableParseState ps, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, string s, bool bold = false, double pt = 0, string? fam = null,
        bool breakDashes = false)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (dwFormCells && (s.IndexOf(Table.InlineInputChar) >= 0
            || s.IndexOf(Table.InlineCheckChar) >= 0
            || s.IndexOf(Table.InlineCheckboxGapChar) >= 0))
            s = s.Replace(Table.InlineInputChar.ToString(), "")
                .Replace(Table.InlineCheckChar.ToString(), "")
                .Replace(Table.InlineCheckboxGapChar.ToString(), "");
        if (string.IsNullOrEmpty(s)) return 0;
        double w = 0;
        foreach (var word in MinContentWords(s, ps.sheetTdBoxRule))
        {
            // The widen probe honours soft break opportunities after hyphens and
            // en-dashes, matching the min-content measure — an
            // unbreakable token ends after each dash (the segment keeps its
            // trailing dash), regardless of the surrounding character classes.
            // Slashes and non-breaking spaces are NOT opportunities.
            // The declared-percent grid asks for the same dash-aware floors
            // (breakDashes) — its min-content columns break at hyphens too.
            if ((widenProbe || breakDashes) && (word.IndexOf('-') > 0 || word.IndexOf('–') > 0))
            {
                var start = 0;
                for (var ci = 0; ci < word.Length; ci++)
                    if (word[ci] is '-' or '–' || ci == word.Length - 1)
                    {
                        w = Math.Max(w, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, word.Substring(start, ci - start + 1), bold, pt, fam));
                        start = ci + 1;
                    }
                continue;
            }
            // CJK line-breaking: an ideograph is a break opportunity on BOTH
            // sides, so a spaceless CJK run's min-content is one ideograph —
            // not the whole run (the browser's normal line-break rule).
            // Probe-only, like the full-em estimate above.
            if ((widenProbe || fullWidthCjkMin) && HasFullWidthCp(word))
            {
                foreach (var sg in CjkWordSegments(word))
                    w = Math.Max(w, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, sg, bold, pt, fam));
                continue;
            }
            w = Math.Max(w, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, word, bold, pt, fam));
        }
        return w;
    }

    private const double UaHrMarginEm = 0.5;          // ...and the hr's
    private const double UaH1MarginEm = 0.67;         // the browser's h1 margins, in the heading's size
    private const double UaH2MarginEm = 0.83;         // ...and h2's
    private const double UaH1FontEm = 2.0;            // the browser's h1 size when no sheet rule sizes it
    private const double UaH2FontEm = 1.5;
    private const double UaH3FontEm = 1.17;           // ...h3's, h4's, h5's and h6's
    private const double UaH4FontEm = 1.0;
    private const double UaH5FontEm = 0.83;
    private const double UaH6FontEm = 0.67;            // ...and h2's
    private const double UaHrRuleFontPt = 1.0;        // a 1pt "line" whose UA box is the 1.5 pt rule
    private const double UaHrRulePt = 1.5;            // the rule's box: two strokes
    private const double UaMarginOnlyFontPt = 0.01;   // a line of no height that carries a trailing block margin
    private const double UaHrStrokePt = 0.75;         // ...each one px
    private static readonly Color UaHrShadeColor = Color.FromRgbBytes(0x55, 0x55, 0x55);   // the lower, shaded stroke

    /// <summary>A block boundary in a UA-boxed cell leaves its margin pending; adjoining
    /// margins collapse to the larger.</summary>
    /// <summary>A UA heading's margin: its own style's, else the sheet's h1/h2 rule's, else the UA 0.67 / 0.83 em
    /// (in the heading's size), left pending like any block margin.</summary>
    private static void UaLeaveHeadingMargin(TableParseState ps, string tag, Token? tok, bool top)
    {
        var fs = ps.curFontPt > 0 ? ps.curFontPt : ps.uaBaseFontPt > 0 ? ps.uaBaseFontPt : DefaultBodyFontPt;
        var side = top ? "margin-top" : "margin-bottom";
        double? declared = null;
        if (tok?.Attributes is not null && tok.Attributes.TryGetValue("style", out var hSt) && hSt is not null)
            declared = CssBoxSidePt(hSt, "margin", side, top ? 0 : 2, fs);
        if (declared is null)
            foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { ps.uaSheet, ps.uaDocSheet })
                if (sheet is not null && sheet.TryGetValue(tag, out var rule) && CssBoxSidePt(rule, "margin", side, top ? 0 : 2, fs) is { } sheetPt)
                { declared = sheetPt; break; }
        ps.uaPendingMarginPt = Math.Max(ps.uaPendingMarginPt, declared ?? UaHeadingMarginEm(tag) * fs);
    }

    /// <summary>The browser's heading sizes, in em of the running size (h1 2, h2 1.5, h3 1.17, h4 1, h5 .83, h6 .67).</summary>
    private static double UaHeadingFontEm(string tag) => tag switch
    {
        "h1" => UaH1FontEm, "h2" => UaH2FontEm, "h3" => UaH3FontEm, "h4" => UaH4FontEm, "h5" => UaH5FontEm, _ => UaH6FontEm,
    };

    /// <summary>The browser's heading margins, in em of the heading's own size.</summary>
    private static double UaHeadingMarginEm(string tag) => tag switch
    {
        "h1" => UaH1MarginEm, "h2" => UaH2MarginEm, _ => UaBlockMarginEmOf(tag),
    };

    private static void UaLeaveBlockMargin(TableParseState ps, double em)
    {
        ps.uaPendingMarginPt = Math.Max(ps.uaPendingMarginPt, em * (ps.curFontPt > 0 ? ps.curFontPt : ps.uaBaseFontPt > 0 ? ps.uaBaseFontPt : DefaultBodyFontPt));
    }

    private static void PushLine(TableParseState ps, bool redlineCells, bool dwFormCells, bool widenProbe, bool keepIfBlank = false, bool joinNext = false)
    {
        // A box run still open at a line break closes its segment on this line
        // and re-opens at the start of the next (title plates wrap onto the
        // smaller ID line).
        if (ps.chainBoxOpen is { Count: > 0 })
            foreach (var br0 in ps.chainBoxOpen)
            {
                AddBoxSeg(ps, br0);
                br0.StartLen = 0;
            }
        // An anchor still open at a line break contributes its text so far to this
        // line and re-opens at the start of the next.
        if (ps.openAnchor is { } oa0)
        {
            var part = CollapseWs(ps.line.ToString()[oa0.Start..]);
            if (part.Length > 0) (ps.lineAnchors ??= new()).Add((part, oa0.Url));
            ps.openAnchor = (0, oa0.Url);
        }
        // The widen probe keeps &nbsp; (U+00A0) intact — it is NOT a break
        // opportunity, so nbsp-joined runs measure as one unbreakable token
        // (.NET's \s matches U+00A0, so the normal collapse would split them).
        var text = widenProbe
            ? Regex.Replace(ps.line.ToString(), "[^\\S ]+", " ").Trim(' ')
            : CollapseWs(ps.line.ToString());
        // A cell whose only content is a zero-width space holds a real line box all
        // the same — the browser gives that row a line's height. (The character
        // itself rides along in ordinary text: it is a soft break opportunity for
        // the wrap and is dropped when the line is drawn — see Table.StripZeroWidth.)
        var zwsOnly = text.Length > 0 && text.Trim(ZeroWidthSpace).Length == 0;
        if (zwsOnly) text = "";
        // A UA-boxed cell's pending block margin seats above the next line that SHOWS (a whitespace
        // run that collapses to nothing is no line, and must not swallow the margin).
        if (ps.uaCellBoxes && ps.uaPendingMarginPt > 0 && (text.Length > 0 || keepIfBlank || zwsOnly))
        {
            ps.lineMarginTop = Math.Max(ps.lineMarginTop, ps.uaPendingMarginPt);
            ps.uaPendingMarginPt = 0;
        }
        // Mixed bold runs on one line (form-grid): rebuild the run segments
        // against the raw buffer, keeping a single space at a run boundary the
        // source had whitespace at; discard unless they reconcile with the
        // collapsed line exactly.
        ApplyLineRunMarks(ps, text);
        ps.lineRunMarks = null;
        if ((ps.wordMailCells || ps.uaCellBoxes) && ps.curLineHeightPct > 0)
            (ps.lineHeightPctByIdx ??= new())[ps.lines.Count] = ps.curLineHeightPct;
        if (ps.uaLineAlign is { } uaAlign) (ps.lineAlignByIdx ??= new())[ps.lines.Count] = uaAlign;
        if (ps.uaHrRulePending) { (ps.hrRuleLines ??= new()).Add(ps.lines.Count); ps.uaHrRulePending = false; }
        if (ps.lineMarker is { } lineMarker && (text.Length > 0 || keepIfBlank))
        {
            (ps.lineMarkerByIdx ??= new())[ps.lines.Count] = lineMarker;
            ps.lineMarker = null;
        }
        // (a UA-boxed line whose runs change size stands on its largest run's box - measured on the
        //  royalty statement's 9 pt labels beside 10 pt values: the row pitches on the 10)
        if (ps.uaCellBoxes && text.Length > 0 && ps.lineMaxRunPt > ps.lineFontPt) ps.lineFontPt = ps.lineMaxRunPt;
        ps.lines.Add((text, text.Length > 0 || keepIfBlank || zwsOnly ? ps.lineFontPt : 0,
            ps.lineFamily, (keepIfBlank || zwsOnly) && text.Length == 0, joinNext, ps.lineAnchors,
            ps.lineHadText && ps.lineAllBold, ps.lineMarginTop,
            // A line inside an <li> that set no indent of its own (a <br>
            // continuation, the answer under a question caption) seats on the
            // item's standing list indent.
            ps.lineMarginLeft > 0 ? ps.lineMarginLeft : ps.liStandingIndentPt, ps.lineColor,
            ps.lineHadText && ps.lineAllItalic));
        if (redlineCells && ps.lineDecorUnion is { Count: > 0 } && text.Length > 0)
            (ps.lineDecorsByIdx ??= new())[ps.lines.Count - 1] = new List<(int Kind, Color? C)>(ps.lineDecorUnion);
        if (dwFormCells && ps.lineColorRuns is { Count: > 0 } && text.Length > 0)
            (ps.lineColorRunsByIdx ??= new())[ps.lines.Count - 1] = ps.lineColorRuns;
        ps.lineColorRuns = null;
        ps.lineDecorUnion = ps.cellDecorActive is { Count: > 0 }
            ? ps.cellDecorActive.ConvertAll(d => (d.Kind, d.C)) : null;
        if (ps.lineHadU) (ps.underlinedLines ??= new HashSet<int>()).Add(ps.lines.Count - 1);
        ps.lineHadU = ps.uDepth > 0;
        ps.lineAnchors = null;
        ps.line.Clear();
        ps.lineFontPt = 0; ps.lineFamily = null; ps.lineStyleSet = false; ps.lineMarginTop = 0; ps.lineMarginLeft = 0;
        ps.lineColor = null; ps.lineMaxRunPt = 0;
        ps.lineHadText = false; ps.lineAllBold = true; ps.lineAllItalic = true;
    }

    private static void CloseRow(TableParseState ps, TableColumnModel colModel, Table table, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool widenProbe, bool uaSerifMin, bool ptCellWidths, bool redlineCells, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad)
    {
        if (ps.cell is not null) CloseCell(ps, colModel, table, options, cellFontSize, dwFormCells, fullWidthCjkMin, breakAnywhereDoc, cellFontShorthand, chainBase, chainSpacingPt, chainUnbold, cssBaseFamily, cssBasePt, defaultCellFace, formGridStrutDropPt, hasBorder, inlineFaceRatio, overDeclaredDraw, padSide, uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, borderWidth, pad);
        if (ps.row is null) return;
        // A rowspan carried into this row means its cells don't start at
        // column 0 — such a row can never seed the whole-table column grid
        // (its explicit widths cover only the columns it holds).
        var rowUnderRowspan = ps.rowspanOcc.Count > 0;
        // Row-span occupancy ages one row per ACTUAL row close. CloseRow also runs
        // for the redundant boundary calls explicit-</TR> markup produces (the
        // </TR> close and the next <TR> open both land here); aging on those would
        // expire an occupancy after a single row and unshift the rows below it.
        for (var oi = ps.rowspanOcc.Count - 1; oi >= 0; oi--)
        {
            var (oc, os, orem) = ps.rowspanOcc[oi];
            if (orem <= 1) ps.rowspanOcc.RemoveAt(oi);
            else ps.rowspanOcc[oi] = (oc, os, orem - 1);
        }
        var cols = 0; foreach (var c in ps.row.Cells) cols += Math.Max(1, c.ColSpan);
        if (cols > colModel.maxCols) colModel.maxCols = cols;
        if (ps.rowPctSum > ps.rowPctDeclMax)
        {
            ps.rowPctDeclMax = ps.rowPctSum;
            ps.rowPxAtMax = ps.rowPxSum;
            ps.rowPxCellsAtMax = ps.rowPxCells;
        }
        ps.rowPctSum = 0; ps.rowPxSum = 0; ps.rowPxCells = 0;
        if (colModel.colGroupPt is null && colModel.colWidthsPt is null && ps.rowAllSingleExplicit
            && ps.rowWidths.Count > 1 && !rowUnderRowspan)
            colModel.colWidthsPt = new List<double>(ps.rowWidths);
        ps.rowWidths.Clear(); ps.rowAllSingleExplicit = true;
        colModel.colCursor = 0;
        if (ps.rowMinHeightPt > 0 && ps.rowMinHeightPt > ps.row.MinRowHeight)
        {
            ps.row.MinRowHeight = ps.rowMinHeightPt;
            ps.row.MinRowHeightIsContent = ps.rowMinHeightIsContent;
        }
        if (ps.row.Cells.Count > 0)
        {
            table.Rows.Add(ps.row);
            if (ps.rowHasCell)
            {
                if (ps.countingHeaderRows && !ps.rowHasTd) ps.headerRows++;
                else ps.countingHeaderRows = false;
            }
        }
        ps.rowHasTd = false; ps.rowHasCell = false;
        ps.row = null;
        ps.rowMinSum = 0;
    }
}
