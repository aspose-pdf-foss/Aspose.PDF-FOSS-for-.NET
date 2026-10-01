using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The ink an unframed, unpainted declared-width table draws: laid out in its declared box, its
    // text lines and bands end where their alignment seats them, a broken image at its bevel box.
    // The sheet a UA document grows for such a table ends one page margin past that ink, not past
    // the box (probed on the safety data sheet: the 768 px borderless header's centred title ends
    // 489.31 into the box and the page is 675.31 = 96 + 489.31 + 90, not the 762 the box would make).

    /// <summary>The furthest ink over EVERY unpainted table declaring the width the sheet is being grown
    /// for, each laid out in that box; 0 when there is none, or when one of them paints past its text
    /// (MEASURED, the evaluation form: four 7in tables, the widest line - 497.07 into the third one's box -
    /// ends the sheet, not the first table's).</summary>
    private static double DeclaredTablesInkRightPt(ConvertState cv, HtmlLoadOptions? options)
    {
        if (cv.blocks is null || cv.declaredTableW <= 0) return 0;
        var ink = 0.0;
        foreach (var b in cv.blocks)
        {
            if (b.TableHtml is not { Length: > 0 } th) continue;
            var tag = Regex.Match(th, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
            if (!tag.Success || Math.Abs(DeclaredTableWidthPt(tag.Value) - cv.declaredTableW) >= 1e-6) continue;
            if (TableIsPainted(th)) return 0;
            ink = Math.Max(ink, MetricTableInkRightPt(cv, options, th, cv.declaredTableW));
        }
        return ink;
    }

    /// <summary>The markup without its `display: none` spans: hidden content paints nothing, so an image
    /// inside one cannot make a table painted (the evaluation form's edit-icon spans).</summary>
    private static string StripHiddenSpans(string html)
        => Regex.Replace(html, @"<span\b[^>]*display\s*:\s*none[^>]*>.*?</span\s*>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Whether every cell width the table's cells declare is a width ATTRIBUTE (none by style),
    /// with at least one such attribute on the grid.</summary>
    private static bool AttributeColumnsOnly(string tableHtml)
    {
        var tag = Regex.Match(tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        // (a table stating any width of its own - a percent included - is another law's)
        if (tag.Success && Regex.IsMatch(tag.Value, @"\bwidth\s*[=:]", RegexOptions.IgnoreCase)) return false;
        var any = false;
        foreach (Match cm in Regex.Matches(tableHtml, @"<t[dh]\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (Regex.IsMatch(DivStyleOf(cm.Value), @"(?<![-\w])width\s*:", RegexOptions.IgnoreCase)) return false;
            var wa = Regex.Match(cm.Value, @"\bwidth\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (!wa.Success) continue;
            // (a percent share is not a pixel hint)
            if (wa.Groups[1].Value.Contains('%')) return false;
            any = true;
        }
        return any;
    }

    /// <summary>Whether a table paints past its text: a fill or frame on it or any cell, a rule, or
    /// an image the engine can draw (a REMOTE image draws its broken box, which the ink walk sees).</summary>
    private static bool TableIsPainted(string tableHtml)
    {
        // (a nested grid's paint is its own: the outer table is judged on its own tags and cells,
        // and the nested grids contribute their painted boxes to the ink walk - measured on the
        // enterprise summary: an unpainted 800 px table round three framed 200 px grids pages
        // 736.75 = 96 + 2 x 200 + 150.75 + 90, not the 786 its box would make)
        var own = StripHiddenSpans(ExtractNestedTables(tableHtml).result);
        return Regex.IsMatch(own, @"\bbgcolor\s*=|(?<![-\w])background(?:-color|-image)?\s*:\s*(?!transparent|none)"
                + @"|\bborder\s*=\s*[""']?[1-9]|(?<![-\w])border(?:-(?:top|left|right|bottom))?\s*:\s*(?!none|0)"
                + @"|(?<![-\w])border-style\s*:\s*(?!none)|<hr\b|<input\b|<textarea\b|<select\b",
            RegexOptions.IgnoreCase)
        || Regex.IsMatch(own, @"<img\b(?![^>]*\bsrc\s*=\s*[""']?https?:)", RegexOptions.IgnoreCase);
    }

    /// <summary>The box a nested grid paints, from the host cell's content edge: its declared width
    /// plus the rule it strokes; 0 when it paints nothing or declares no width.</summary>
    private static double NestedGridInkBoxPt(string sub)
    {
        var tag = Regex.Match(sub, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tag.Success) return 0;
        if (TableIsPainted(sub))
        {
            var w = DeclaredTableWidthPt(tag.Value);
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
                Console.Error.WriteLine($"[nestedink] tag='{(tag.Value.Length > 90 ? tag.Value[..90] : tag.Value)}' painted=True declared={w:0.##}");
            return w > 0 ? w + GridPaintOutsetPt(sub) : 0;
        }
        // (an unpainted grid's ink is its own nested grids', one cell chrome in from its edge -
        // the enterprise summary's 200 px wrappers, cellspacing and cellpadding 0, round framed
        // 200 px grids)
        var (s, p, bw, _) = WrapperAttrChrome(tag.Value);
        var inner = 0.0;
        var deeperSubs = ExtractNestedTables(sub).subTables;
        foreach (var deeper in deeperSubs)
            inner = Math.Max(inner, NestedGridInkBoxPt(deeper));
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
            Console.Error.WriteLine($"[nestedink] tag='{(tag.Value.Length > 90 ? tag.Value[..90] : tag.Value)}' painted=False deeper={deeperSubs.Count} inner={inner:0.##} chrome={s + p + bw:0.##}");
        return inner > 0 ? s + p + bw + inner : 0;
    }

    private const double MetricDefaultRulePt = 0.75;

    /// <summary>What a painted grid's ink stands past its declared box: one rule for a frame; both
    /// side borders of its rule element when a rule is all it paints (probed on the Words letter:
    /// the ruled 126 pt grid inks 127.5 - its `hr` spans the box plus a border a side).</summary>
    private static double GridPaintOutsetPt(string sub)
    {
        var own = ExtractNestedTables(sub).result;
        var ruled = Regex.IsMatch(own, @"<hr\b", RegexOptions.IgnoreCase);
        var framed = Regex.IsMatch(own, @"\bborder\s*=\s*[""']?[1-9]|(?<![-\w])border(?:-(?:top|left|right|bottom))?\s*:\s*(?!none|0)"
            + @"|(?<![-\w])border-style\s*:\s*(?!none)", RegexOptions.IgnoreCase);
        return ruled && !framed ? 2 * MetricDefaultRulePt : MetricDefaultRulePt;
    }

    /// <summary>The furthest ink an unpainted nested grid draws, from its box's left edge: the grid
    /// parsed in its host cell's typography, solved in the box and walked like the table itself
    /// (probed on the Words letter without its rule: the block's furthest line, 110.46 into its
    /// 126 pt grid, ends the sheet at 96 + 344.25 + 110.46 + 90).</summary>
    private static double NestedGridTextInkPt(MetricTableState host, MetricCell hostCell, string sub, double boxW)
    {
        if (!host.stdSerif || boxW <= 0) return 0;
        MetricTableState mt;
        try
        {
            mt = ParseMetricTable(host.doc, sub, host.css, 0, boxW, host.pageWidth, host.pageHeight, host.marginTop,
                host.marginBottom, GridTypoCell(hostCell).Face ?? host.face, CellFm(host.fm, GridTypoCell(hostCell)),
                host.docFontDict, host.stdSerif, GridTypoCell(hostCell).FontSize ?? host.mps.fontSize, wrapperStacks: true,
                symInsetPt: 0, host.rtl, host.paragraphCells, host.serifReportCells, host.loadOptions, host.siblingCellRules,
                uaBlockCells: host.mps.uaBlockCells, uaFormCells: host.mps.uaFormCells, hostCellPadPt: host.p);
            mt.cursor = new FlowPosition { y = host.pageHeight };
            mt.declaredBoxLayout = true;
            if (!SolveMetricTable(mt)) return 0;
        }
        catch { return 0; }
        return MetricSolvedInkRightPt(mt, boxW) - mt.tableX;
    }

    /// <summary>The rightmost ink the table draws when laid out in a box of the given width, measured
    /// from the box's left edge; 0 when nothing can be measured.</summary>
    private static double MetricTableInkRightPt(ConvertState cv, HtmlLoadOptions? options, string tableHtml, double boxW)
    {
        var profile = cv.profile;
        if (!profile.uaStdSerif) return 0;
        // (in the face the flow draws: the body's own where its tag or rule states one)
        var face = profile.metricFace is { Length: > 0 } flowFace && WinMetricsFor(flowFace) is not null ? flowFace : UaSerifFaceName;
        if (WinMetricsFor(face) is not { } fm) return 0;
        MetricTableState mt;
        try
        {
            mt = ParseMetricTable(cv.doc ?? Document.Create(), tableHtml, cv.css, 0, boxW, cv.pageWidth, cv.pageHeight,
                cv.marginTop, cv.marginBottom, face, fm, new Core.PdfDictionary(), stdSerif: true, baseFontSize: UaDefaultFontPt,
                wrapperStacks: !profile.deadExternalCss, symInsetPt: profile.bodyZeroMargin ? 0.0 : UaBodyMarginPt,
                rtl: profile.rtlDoc,
                paragraphCells: profile.emailNewsletterDoc || profile.ssrsReportDoc || profile.uaBlockCells,
                serifReportCells: profile.ssrsReportDoc || profile.uaBlockCells,
                loadOptions: options, siblingCellRules: profile.docSiblingCellRules,
                uaBlockCells: profile.uaBlockCells, uaFormCells: profile.uaFormCells);
            mt.cursor = new FlowPosition { y = cv.pageHeight };
            mt.declaredBoxLayout = true;
            if (!SolveMetricTable(mt)) return 0;
        }
        catch { return 0; }
        return MetricSolvedInkRightPt(mt, boxW) - mt.tableX;
    }

    /// <summary>The min-content width of a top-level grid on the UA flow: the sum over its columns of the
    /// widest unbreakable content any non-spanning cell holds with that cell's paddings and side rules,
    /// plus the border spacing (MEASURED, the change-control page's 11-column grid: 580.252 = per-column
    /// widest kerned run + th 5.5 / td 4.55 chrome, the th-md floors at 71.25). 0 when nothing can be measured.</summary>
    private static double MetricTableMinContentPt(ConvertState cv, HtmlLoadOptions? options, string tableHtml, double boxW)
    {
        var profile = cv.profile;
        if (!profile.uaStdSerif) return 0;
        var face = profile.metricFace is { Length: > 0 } flowFace && WinMetricsFor(flowFace) is not null ? flowFace : UaSerifFaceName;
        if (WinMetricsFor(face) is not { } fm) return 0;
        MetricTableState mt;
        try
        {
            mt = ParseMetricTable(cv.doc ?? Document.Create(), tableHtml, cv.css, 0, boxW, cv.pageWidth, cv.pageHeight,
                cv.marginTop, cv.marginBottom, face, fm, new Core.PdfDictionary(), stdSerif: true,
                baseFontSize: profile.bodyOwnFontPt > 0 ? profile.bodyOwnFontPt : UaDefaultFontPt,
                wrapperStacks: !profile.deadExternalCss, symInsetPt: profile.bodyZeroMargin ? 0.0 : UaBodyMarginPt,
                rtl: profile.rtlDoc, paragraphCells: profile.uaBlockCells, serifReportCells: profile.uaBlockCells,
                loadOptions: options, siblingCellRules: profile.docSiblingCellRules,
                uaBlockCells: profile.uaBlockCells, uaFormCells: profile.uaFormCells);
            mt.cursor = new FlowPosition { y = cv.pageHeight };
            CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
            if (mt.rows.Count == 0) return 0;
            OrderMetricRows(mt);
            CountMetricColumns(mt);
        }
        catch { return 0; }
        var total = (mt.nCols + 1) * mt.s;
        for (var c = 0; c < mt.nCols; c++)
        {
            var colBox = 0.0;
            foreach (var r in mt.rows)
                if (c < r.Count && r[c].ColSpan <= 1 && !r[c].Phantom)
                    colBox = Math.Max(colBox, MetricCellMinContentPt(mt, r[c]) + 2 * mt.p + r[c].BorderLeftW + r[c].BorderRightW);
            total += colBox;
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
            Console.Error.WriteLine($"[tablemin] cols={mt.nCols} s={mt.s} p={mt.p} min={total:0.###}");
        return total;
    }

    /// <summary>The rightmost ink a solved grid's cells draw, in the grid's own coordinates.</summary>
    private static double MetricSolvedInkRightPt(MetricTableState mt, double boxW)
    {
        var right = 0.0;
        foreach (var r in mt.rows)
        {
            var colX = mt.tableX + mt.s + mt.mps.tablePadLeftPt;
            var skip = 0;
            for (var c = 0; c < mt.nCols; c++)
            {
                if (skip > 0) { skip--; continue; }
                var cellBoxW = mt.colW[c] + 2 * mt.p;
                if (c < r.Count)
                {
                    var mc = r[c];
                    for (var k = 1; k < mc.ColSpan && c + k < mt.nCols; k++) { cellBoxW += mt.s + mt.colW[c + k] + 2 * mt.p; skip++; }
                    if (!mc.Phantom) right = Math.Max(right, MetricCellInkRightPt(mt, mc, colX, cellBoxW));
                    if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
                        Console.Error.WriteLine($"[tableink-cell] c={c} colX={colX:0.##} boxW={cellBoxW:0.##} subs={mc.SubTables?.Count ?? 0} segs={mc.DivSegs?.Count ?? 0} text='{(mc.Text.Length > 10 ? mc.Text[..10] : mc.Text)}' right={(mc.Phantom ? -1 : MetricCellInkRightPt(mt, mc, colX, cellBoxW)):0.##}");
                }
                colX += cellBoxW + mt.s;
            }
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
            Console.Error.WriteLine($"[tableink] box={boxW:0.##} tableX={mt.tableX:0.##} right={right:0.##}");
        return right;
    }

    /// <summary>The rightmost ink one solved cell draws at its seat: each text line or band at its
    /// alignment, a broken image's bevel box.</summary>
    private static double MetricCellInkRightPt(MetricTableState mt, MetricCell mc, double colX, double boxW)
    {
        var p = mt.p;
        var padL = mc.PadLeft >= 0 ? mc.PadLeft : p;
        var padR = mc.PadRight >= 0 ? mc.PadRight : p;
        var right = 0.0;
        if (mc.ImgPlaceholder && mc.ImgWPt > 0)
        {
            var gutter = mc.ImgRemoteBroken ? RemoteBrokenImageGutterPt : 0;
            right = colX + mc.BorderLeftW + padL + gutter + (mc.ImgRemoteBroken ? BrokenImageBevelPt : 0) + mc.ImgWPt + BrokenImageBevelPt / 2;
        }
        var cellFs = mc.FontSize ?? mt.mps.fontSize;
        if (mc.SubTables is { Count: > 0 } inkSubs)
            foreach (var sub in inkSubs)
            {
                if (NestedGridInkBoxPt(sub) is > 0 and var subBox)
                    right = Math.Max(right, colX + mc.BorderLeftW + padL + subBox);
                // (an unpainted grid's text is ink too: laid out in the cell's box, its furthest line
                // ends the sheet)
                else if (NestedGridTextInkPt(mt, mc, sub, boxW - mc.BorderLeftW - padL - padR) is > 0 and var subInk)
                    right = Math.Max(right, colX + mc.BorderLeftW + padL + subInk);
            }
        if (mc.DivSegs is { Count: > 0 } segs)
        {
            foreach (var sg in segs)
            {
                if (sg.Text.Length == 0 || sg.NestedTable >= 0) continue;
                var probe = new MetricCell { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize, Italic = sg.Italic };
                var fs = sg.FontSize ?? mt.mps.fontSize;
                var shear = SyntheticItalicShearFor(probe.Face, probe.Bold, probe.Italic);
                if (shear > 0) probe.Italic = false;
                var overhang = ShearOverhangPt(shear, CellFm(mt.fm, probe), fs);
                var faceN = CellFaceName(mt.face, mt.boldFace, probe);
                var wrapW = boxW - padL - padR - sg.PadLeft - sg.PadRight - mc.BorderLeftW;
                var lines = sg.Runs is not null ? MeasuredWordWrapStyled(sg.Text, wrapW, mt.face, mt.boldFace, probe, fs, sg.Runs)
                    : MeasuredWordWrap(sg.Text, wrapW, faceN, fs);
                var offs = sg.Runs is not null ? LineStartOffsets(sg.Text, lines) : null;
                for (var i = 0; i < lines.Length; i++)
                {
                    var lw = (sg.Runs is not null ? MeasureStyledLine(mt.face, mt.boldFace, probe, fs, lines[i], offs![i], sg.Runs)
                        : MeasureFaceText(faceN, lines[i], fs)) + overhang;
                    var lx = sg.AlignRight || (!sg.AlignCenter && mc.Align == HorizontalAlignment.Right) ? colX + boxW - padR - sg.PadRight - lw
                        : sg.AlignCenter || mc.Align == HorizontalAlignment.Center ? colX + padL + sg.PadLeft + (wrapW - lw) / 2
                        : colX + mc.BorderLeftW + padL + sg.PadLeft;
                    right = Math.Max(right, lx + lw);
                }
            }
            return right;
        }
        var offsC = mc.Runs is not null ? LineStartOffsets(mc.Text, mc.Lines) : null;
        for (var i = 0; i < mc.Lines.Length; i++)
        {
            var ln = mc.Lines[i];
            if (ln.Length == 0) continue;
            var lw = mc.Runs is not null ? MeasureStyledLine(mt.face, mt.boldFace, mc, cellFs, ln, offsC![i], mc.Runs)
                : MeasureFaceText(CellFaceName(mt.face, mt.boldFace, mc), ln, cellFs);
            var lx = mc.Align switch
            {
                HorizontalAlignment.Right => colX + boxW - p - lw,
                HorizontalAlignment.Center => colX + (boxW - lw) / 2,
                _ => colX + mc.BorderLeftW + padL,
            };
            right = Math.Max(right, lx + lw);
        }
        return right;
    }
    /// <summary>A UA grid's caption: one line centred over the grid's box at the table's base size,
    /// its line box spent before the first row (probed on the state analysis: the caption's line
    /// stands 13.9 over the header row's text).</summary>
    private static void PaintMetricCaption(MetricTableState mt)
    {
        if (mt.captionText is not { Length: > 0 } cap || !mt.stdSerif || mt.cursor.page is null) return;
        var fs = mt.baseFontSize;
        var probe = new MetricCell { FontSize = fs };
        var faceN = CellFaceName(mt.face, mt.boldFace, probe);
        var res = ResOfFlatOn(mt.mps, mt.flatRes, mt.face, mt.boldFace, mt.cursor.page, new MetricCell { Face = mt.face, FontSize = fs });
        var lineH = (HheaLineSumFor(mt.face) ?? mt.fm.sum) * fs;
        var boxW = (mt.nCols + 1) * mt.s;
        foreach (var w in mt.colW) boxW += w + 2 * mt.p;
        var capW = MeasureFaceText(faceN, cap, fs);
        var x = Math.Max(mt.tableX, mt.tableX + (boxW - capW) / 2);
        EmitCellLineRuns(mt.cursor.page, res, fs, x, mt.cursor.y - MetricBaselineDrop(fs, lineH, mt.fm), cap, faceN);
        mt.cursor.y -= lineH;
    }
}
