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
    /// <summary>A fragment's inline boxes are registered against the lines they sit on.</summary>
    private void PlanInlineBoxes(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc)
    {
        if (paragraph is TextFragment { InlineBoxes: { Count: > 0 } fragBoxes }
            && pc.lines.Count > pp.linesBeforeFrag)
        {
            var deco = pc.lines[pp.linesBeforeFrag];
            deco.Boxes = fragBoxes;
            // The boxes' laid-out extent IS the line's width (the box model
            // owns the pen) — alignment must flush the boxes, not the flat
            // text they replaced.
            double boxExtent = 0;
            var boxText = false;
            foreach (var fb in fragBoxes)
            {
                boxExtent = Math.Max(boxExtent, fb.XOff + fb.Width);
                if (fb.Text is not null) boxText = true;
            }
            if (boxText && boxExtent > 0) deco.KernedWidth = boxExtent;
            if (pp.fragLineH > pp.fragFontSize * 1.2 + 0.01 && deco.BoxH <= 0)
            {
                var decoPadT = 0.0;
                foreach (var fb in fragBoxes) decoPadT = Math.Max(decoPadT, fb.PadTop);
                deco.BoxH = pp.fragLineH;
                deco.BaseOff = decoPadT + pp.fragFontSize;
            }
        }
    }

    /// <summary>Each newline-separated segment of the text becomes a wrapped line group.</summary>
    private void PlanTextSegments(RowPlanParagraphState pp, RowPlanColumnState pc)
    {
        foreach (var segment in pp.text!.Split('\n'))
        {
            if (segment.Length == 0) continue;
            var estWidth = pp.genMeas is null ? MeasureWidth(segment, pp.fragFontSize) : pp.genMeas(segment);
            // The lifted render sizes columns off the same estimate the wrap
            // uses, so a line that EXACTLY fills its column must not break on
            // sub-point rounding (a trailing "…" spilling to its own line).
            var wrapSlack = NestedTableRender ? 1.5 : 0.0;
            // Max-content auto-fit columns are sized to their full unwrapped
            // text by construction — nothing wraps in them (the exact column
            // vs the ~5 % estimate would otherwise split what the generator
            // draws on one line).
            // A SPANNED cell is the exception: it never sized a column (its
            // content is laid out across the span), so it wraps there like any
            // other over-wide cell — the five spanned headers of the
            // repeating-column sheet lay out on two to four lines each.
            if (!pc.cell.HtmlNoWrap && !(AutoFitMaxContentCells && pc.cell.ColSpan <= 1)
                && pc.cell.IsWordWrapped && estWidth > pc.availWidth + wrapSlack)
            {
                foreach (var l in WrapText(segment, pp.fragFontSize, pc.availWidth + wrapSlack, pp.genMeas,
                             overflowLongWords: HtmlLayoutWrap))
                    pc.lines.Add(new CellLine { Text = StripZeroWidth(l), FontSize = pp.fragFontSize, ForegroundColor = pp.color, Bold = pp.fragBold, Hyperlink = pp.fragLink, LinkRuns = TakeAnchorRuns(pp, l, s => MeasureWidth(s, pp.fragFontSize)), Align = pp.lineAlign, CssAsc = pp.fragCssAsc, CssForce = pp.fragCssForce, CssDesc = pp.fragCssDesc, BoxH = pp.runBoxH > 0 ? pp.runBoxH : pp.htmlCssBoxPx > 0 ? Math.Round(pp.htmlCssBoxPx * 1.15) * 0.75 : pp.ownBoxH, BaseOff = pp.runBoxH > 0 ? CssRunBaseOff(pp.runBoxH, pp.fragFontSize, pp.fragCssAsc, pp.fragCssDesc) : pp.htmlCssBoxPx > 0 || pp.ownBoxH > 0 ? pp.fragFontSize : 0 });
            }
            else
            {
                pc.lines.Add(new CellLine { Text = StripZeroWidth(segment), FontSize = pp.fragFontSize, ForegroundColor = pp.color, Bold = pp.fragBold, Hyperlink = pp.fragLink, LinkRuns = TakeAnchorRuns(pp, segment, s => MeasureWidth(s, pp.fragFontSize)), Align = pp.lineAlign, CssAsc = pp.fragCssAsc, CssForce = pp.fragCssForce, CssDesc = pp.fragCssDesc, BoxH = pp.runBoxH > 0 ? pp.runBoxH : pp.htmlCssBoxPx > 0 ? Math.Round(pp.htmlCssBoxPx * 1.15) * 0.75 : pp.ownBoxH, BaseOff = pp.runBoxH > 0 ? CssRunBaseOff(pp.runBoxH, pp.fragFontSize, pp.fragCssAsc, pp.fragCssDesc) : pp.htmlCssBoxPx > 0 || pp.ownBoxH > 0 ? pp.fragFontSize : 0 });
            }
        }
    }

    /// <summary>The generator cell model lays the fragment on its own grid; true when handled here.</summary>
    private bool PlanGeneratorCellText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        if (GeneratorCellModel && paragraph is TextFragment gtf
            && ResolveGeneratorCellFace(gtf, pc.cell, row) is { } genFaceName
            && !ContainsCjk(pp.text!)
            && !Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(pp.text)
            && (CellFaceTtf(genFaceName, pp.fragBold, pp.fragItalic)
                ?? (pp.fragBold || pp.fragItalic ? null : gtf.TextState.Font?.SourceFontData?.TtfData)) is { } genTtf)
        {
            var genRealVariant = CellFaceTtf(genFaceName, pp.fragBold, pp.fragItalic) is not null;
            double MeasGen(string t) => MeasureWidthWithFont(t, pp.fragFontSize, genTtf);
            // A single WORD wider than the column needs the legacy
            // character-break/hyphen wrap — this branch wraps on spaces
            // only and would clip the token to one line.
            var genOverWide = false;
            if (pc.availWidth > 0)
                foreach (var gw in pp.text!.Split(' ', '\n'))
                    if (gw.Length > 0 && MeasGen(gw) > pc.availWidth) { genOverWide = true; break; }
            if (!genOverWide)
            {
            foreach (var rawSeg in pp.text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (rawSeg.Length == 0) continue;
                var genLines = pc.cell.IsWordWrapped && MeasGen(rawSeg) > pc.availWidth
                    ? WrapLinesWithFont(rawSeg, pp.fragFontSize, genTtf, pc.availWidth)
                    : new List<string> { rawSeg };
                foreach (var l in genLines)
                    pc.lines.Add(new CellLine
                    {
                        Text = l,
                        FontSize = pp.fragFontSize,
                        ForegroundColor = pp.color,
                        Hyperlink = pp.fragLink,
                        LinkRuns = TakeAnchorRuns(pp, l, MeasGen),
                        Align = pp.lineAlign,
                        Type0Ttf = genTtf,
                        Type0FontName = genRealVariant
                            ? CellFaceName(genFaceName, pp.fragBold, pp.fragItalic) : genFaceName,
                        KernedWidth = MeasGen(l),
                    });
            }
            return true;
            }
        }
        return false;
    }

    /// <summary>A fragment honouring the cell's TrueType face lays through its embedded program; true when handled here.</summary>
    private bool PlanCellTtfFaceText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        if (HonorCellTtfFaces && paragraph is TextFragment ftf
            && ftf.TextState.Font?.FontName is { Length: > 0 } cellFaceName
            && !ContainsCjk(pp.text!)
            && !Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(pp.text)
            && CellFaceTtf(cellFaceName, pp.fragBold, pp.fragItalic) is { } cellFaceTtf)
        {
            // Mixed bold runs on ONE line (the owner band's 'Owner Team:
            // <b>bv Designers</b>'): one CellLine whose StyleRuns the render
            // draws sequentially, each in its own face variant.
            if (pp.fragGridRuns is { Count: > 1 })
            {
                var runCells = new List<(string Text, byte[] Ttf, string Name)>();
                var runsW = 0.0;
                var runsOk = true;
                foreach (var (runText, runBold) in pp.fragGridRuns)
                {
                    var runTtf = CellFaceTtf(cellFaceName, runBold || pp.fragBold, pp.fragItalic);
                    if (runTtf is null) { runsOk = false; break; }
                    runCells.Add((runText, runTtf,
                        CellFaceName(cellFaceName, runBold || pp.fragBold, pp.fragItalic)));
                    runsW += MeasureWidthKerned(runText, pp.fragFontSize, runTtf);
                }
                if (runsOk)
                {
                    pc.lines.Add(new CellLine
                    {
                        Text = pp.text!,
                        FontSize = pp.fragFontSize,
                        ForegroundColor = pp.color,
                        Align = pp.lineAlign,
                        CssAsc = pp.fragCssAsc, CssForce = pp.fragCssForce,
                        CssDesc = pp.fragCssDesc,
                        Type0Ttf = cellFaceTtf,
                        Type0FontName = CellFaceName(cellFaceName, pp.fragBold, pp.fragItalic),
                        StyleRuns = runCells,
                        Decors = ftf.HtmlDecors,
                        InputBoxes = ftf.InlineInputBoxes,
                        ColorRuns = ftf.HtmlColorRuns,
                        OwnLinePt = ftf.CssLineHeightPt,
                        KernTj = true,
                        KernedWidth = runsW,
                        // Form-grid lines are CSS boxes: their own line box and
                        // the measured baseline seat within it.
                        BoxH = FormGridCells && pp.fragLineH > 0 ? pp.fragLineH : 0,
                        BaseOff = FormGridCells ? ftf.CssBaseDrop : 0,
                    });
                    return true;
                }
            }
            double MeasFace(string s) => MeasureWidthKerned(s, pp.fragFontSize, cellFaceTtf);
            // pt-styled fragment: the paragraph's own margins inset the
            // wrap box ('Prijs p/ stuk' wraps inside them).
            var faceWrapW = pc.cell.HtmlNoWrap
                // A nowrap cell keeps its whole line and overflows the
                // column (the results row draws past its box).
                ? double.MaxValue
                : HtmlWrapInsetsCellMargins && ftf.HtmlWrapInsetPt > 0
                ? Math.Max(8.0, pc.availWidth - ftf.HtmlWrapInsetPt)
                : pc.availWidth;
            foreach (var segment in pp.text!.Split('\n'))
            {
                if (segment.Length == 0) continue;
                foreach (var l in WrapKernedLines(segment, pp.fragFontSize, cellFaceTtf, faceWrapW))
                    pc.lines.Add(new CellLine
                    {
                        Text = l,
                        FontSize = pp.fragFontSize,
                        ForegroundColor = pp.color,
                        Hyperlink = pp.fragLink,
                        LinkRuns = TakeAnchorRuns(pp, l, MeasFace),
                        // The paragraph's own margins seat the line inside
                        // the padded cell box: margin-left indents a
                        // left-aligned line, margin-right insets a
                        // right-aligned one.
                        Decors = ftf.HtmlDecors,
                        InputBoxes = ftf.InlineInputBoxes,
                        ColorRuns = ftf.HtmlColorRuns,
                        OwnLinePt = ftf.CssLineHeightPt,
                        LeftIndent = HtmlWrapInsetsCellMargins ? ftf.HtmlMarginLeftPt : 0,
                        RightInsetPt = HtmlWrapInsetsCellMargins
                            ? Math.Max(0, ftf.HtmlWrapInsetPt - ftf.HtmlMarginLeftPt) : 0,
                        Align = pp.lineAlign,
                        CssAsc = pp.fragCssAsc, CssForce = pp.fragCssForce,
                        CssDesc = pp.fragCssDesc,
                        Type0Ttf = cellFaceTtf,
                        Type0FontName = CellFaceName(cellFaceName, pp.fragBold, pp.fragItalic),
                        KernTj = true,
                        KernedWidth = MeasFace(l),
                        BoxH = FormGridCells && pp.fragLineH > 0 ? pp.fragLineH : 0,
                        BaseOff = FormGridCells ? ftf.CssBaseDrop : 0,
                    });
            }
            return true;
        }
        return false;
    }

    /// <summary>A fragment honouring the cell's font face lays through that face; true when handled here.</summary>
    private bool PlanCellFontFaceText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc)
    {
        if (HonorCellFontFaces && paragraph is TextFragment stf
            && IsSerifCssFamily(stf.TextState.Font?.FontName)
            && (pp.fragBold ? BoldSerifTtf() : SerifTtf()) is { } cellSerifTtf)
        {
            // The serif faces don't cover the ballot boxes (☐ U+2610 / ☒ U+2612)
            // vote forms mark their choices with — draw them as the covered white
            // square so each box leaves its outline on the page (the WinAnsi path
            // shows '?', an uncovered Type0 run nothing at all).
            if (pp.text!.IndexOf('☐') >= 0 || pp.text.IndexOf('☒') >= 0)
                pp.text = pp.text.Replace('☐', '□').Replace('☒', '□');
            double MeasCell(string s) => MeasureWidthKerned(s, pp.fragFontSize, cellSerifTtf);
            foreach (var segment in pp.text.Split('\n'))
            {
                if (segment.Length == 0) continue;
                foreach (var l in WrapKernedLines(segment, pp.fragFontSize, cellSerifTtf, pc.availWidth))
                    pc.lines.Add(new CellLine
                    {
                        Text = l,
                        FontSize = pp.fragFontSize,
                        ForegroundColor = pp.color,
                        Hyperlink = pp.fragLink,
                        LinkRuns = TakeAnchorRuns(pp, l, MeasCell),
                        Align = pp.lineAlign,
                        CssAsc = pp.fragCssAsc, CssForce = pp.fragCssForce,
                        CssDesc = pp.fragCssDesc,
                        Type0Ttf = cellSerifTtf,
                        Type0FontName = pp.fragBold ? "Times New Roman Bold" : "Times New Roman",
                        KernTj = true,
                        KernedWidth = MeasCell(l),
                    });
            }
            return true;
        }
        return false;
    }

    /// <summary>A bold fragment with CSS ascent metrics lays at those metrics; true when handled here.</summary>
    private bool PlanBoldCssText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        if (paragraph is TextFragment btf && btf.TextState.IsBold && pp.fragCssAsc > 0
            // …unless the real-face cell pipeline owns this table: it
            // draws bold variants itself, with the cell's decorations
            // and margin insets aboard.
            && !HonorCellTtfFaces
            && IsSerifCssFamily(btf.TextState.Font?.FontName)
            && BoldSerifTtf() is { } serifBoldTtf)
        {
            // Band dialect: the paragraph's explicit margin-top becomes a
            // silent spacer box above its first line (the css-box stack
            // consumes BoxH for empty-text lines without drawing them).
            var bMargin = HonorCellFontFaces ? ParagraphMargin(paragraph) : null;
            if (bMargin is { Top: > 0 })
                pc.lines.Add(new CellLine { Text = "", FontSize = pp.fragFontSize, BoxH = bMargin.Top, Align = pp.lineAlign });
            double MeasSerif(string s) => MeasureWidthKerned(s, pp.fragFontSize, serifBoldTtf);
            foreach (var segment in pp.text!.Split('\n'))
            {
                if (segment.Length == 0) continue;
                foreach (var l in WrapKernedLines(segment, pp.fragFontSize, serifBoldTtf, pc.availWidth))
                    pc.lines.Add(new CellLine
                    {
                        Text = l,
                        FontSize = pp.fragFontSize,
                        ForegroundColor = pp.color,
                        Hyperlink = pp.fragLink,
                        LinkRuns = TakeAnchorRuns(pp, l, MeasSerif),
                        Align = pp.lineAlign,
                        CssAsc = pp.fragCssAsc, CssForce = pp.fragCssForce,
                        CssDesc = pp.fragCssDesc,
                        KernTj = true,
                        KernedWidth = MeasSerif(l),
                        // Band tables: route through the per-line render path
                        // (anyType0) so the serif runs draw even in a uniform
                        // left-aligned row — the plain text-object path ignores
                        // Runs and would fall back to Helvetica.
                        Type0Ttf = HonorCellFontFaces ? serifBoldTtf : null,
                        Type0FontName = HonorCellFontFaces ? "Times New Roman Bold" : null,
                        Runs = new List<HtmlRun>
                        {
                            new HtmlRun { Text = l, X = 0, Size = pp.fragFontSize, Bold = true },
                        },
                    });
            }
            return true;
        }
        return false;
    }

    /// <summary>CJK text lays through the CJK fallback chain; true when handled here.</summary>
    private bool PlanCjkText(RowPlanParagraphState pp, RowPlanColumnState pc)
    {
        if (ContainsCjk(pp.text!) || NeedsCjkChain(pp.text!, null))
        {
            var cjkTtf = Aspose.Pdf.Text.CjkFallbackFont.ResolveEmbeddableBytes(pp.text!);
            var needsChain = NeedsCjkChain(pp.text!, cjkTtf);
            if (cjkTtf is not null && CjkCoveredBy(pp.text!, cjkTtf) && !needsChain)
            {
                foreach (var segment in pp.text!.Split('\n'))
                {
                    if (segment.Length == 0) continue;
                    // Char-level width wrap (CJK has no ASCII spaces to break at),
                    // measured with the fallback font, so long CJK cell text stays
                    // inside the column. Every character — including spaces — is kept,
                    // so mixed CJK+ASCII like "繋がって or つながって" still reconstructs
                    // fully across the wrapped lines. Each line draws as one Type0/CID run.
                    foreach (var lineText in WrapCjkToWidth(segment, pp.fragFontSize, pc.availWidth, cjkTtf))
                    {
                        pc.lines.Add(new CellLine
                        {
                            Text = lineText, FontSize = pp.fragFontSize, ForegroundColor = pp.color, Align = pp.lineAlign,
                            Type0Ttf = cjkTtf, Type0FontName = "MSGothic",
                            Underline = pp.fragUnderline,
                        });
                    }
                }
                return true;
            }
            // Codepoints no single face carries (CJK radicals, plane-2
            // ideographs, technical symbols): draw through the per-codepoint
            // coverage chain — each run in the first face that covers it,
            // the way the substitution chain runs (SimSun / Segoe UI Symbol /
            // SimSun-ExtB on one line). Text this arm does not take keeps
            // the legacy fall-through below.
            if (needsChain
                && (cjkTtf is not null
                    || Aspose.Pdf.Text.CjkFallbackFont.ChainFaces().Count > 0))
            {
                var chainFallback = Aspose.Pdf.Text.CjkFallbackFont.ChainFaces();
                foreach (var segment in pp.text!.Split('\n'))
                {
                    if (segment.Length == 0) continue;
                    foreach (var lineText in WrapChainToWidth(segment, pp.fragFontSize,
                        pc.availWidth, cjkTtf, "MSGothic"))
                    {
                        var runs = SegmentByCoverageChain(lineText, cjkTtf, "MSGothic");
                        var lineTtf = runs is { Count: > 0 } ? runs[0].Ttf
                            : cjkTtf ?? (chainFallback.Count > 0 ? chainFallback[0].Bytes : null);
                        var lineFace = runs is { Count: > 0 } ? runs[0].Name
                            : cjkTtf is not null ? "MSGothic"
                            : chainFallback.Count > 0 ? chainFallback[0].Name : "MSGothic";
                        if (lineTtf is null) continue;
                        pc.lines.Add(new CellLine
                        {
                            Text = lineText, FontSize = pp.fragFontSize, ForegroundColor = pp.color, Align = pp.lineAlign,
                            Type0Ttf = lineTtf, Type0FontName = lineFace,
                            StyleRuns = runs,
                            Underline = pp.fragUnderline,
                        });
                    }
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>Text fully covered by the fragment's embedded TrueType lays through that face; true when handled here.</summary>
    private bool PlanEmbeddedTtfText(RowPlanParagraphState pp, RowPlanColumnState pc)
    {
        if (pp.fragEmbeddedTtf is not null && CjkCoveredBy(pp.text!, pp.fragEmbeddedTtf))
        {
            foreach (var segment in pp.text!.Split('\n'))
            {
                if (segment.Length == 0) continue;
                // Over-declared grid document: the fragment's own face does
                // not exempt the text from its column box — a 17% header
                // column wraps its label like any other. The
                // legacy dialects keep their unwrapped single-line draw.
                if (HtmlOverDeclaredDraw)
                {
                    foreach (var lineText in WrapCjkToWidth(segment, pp.fragFontSize, pc.availWidth, pp.fragEmbeddedTtf))
                        pc.lines.Add(new CellLine
                        {
                            Text = lineText, FontSize = pp.fragFontSize, ForegroundColor = pp.color, Align = pp.lineAlign,
                            Type0Ttf = pp.fragEmbeddedTtf, Type0FontName = pp.fragEmbeddedName, Type0SplitTokens = true,
                            Underline = pp.fragUnderline,
                        });
                    continue;
                }
                pc.lines.Add(new CellLine
                {
                    Text = segment, FontSize = pp.fragFontSize, ForegroundColor = pp.color, Align = pp.lineAlign,
                    Type0Ttf = pp.fragEmbeddedTtf, Type0FontName = pp.fragEmbeddedName, Type0SplitTokens = true,
                });
            }
            return true;
        }
        return false;
    }

    /// <summary>An over-declared HTML draw lays its text at the declared metrics; true when handled here.</summary>
    private bool PlanOverDeclaredHtml(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc)
    {
        if (HtmlOverDeclaredDraw
            && (paragraph as TextFragment)?.InlineOptions is not { Count: > 0 }
            && pp.text!.IndexOf(InlineButtonChar) < 0
            && Aspose.Pdf.Text.CjkFallbackFont.SerifLatinFace(pp.fragBold) is { } latinFace)
        {
            foreach (var segment in pp.text.Split('\n'))
            {
                if (segment.Length == 0) continue;
                foreach (var lineText in WrapChainToWidth(segment, pp.fragFontSize,
                    pc.availWidth, latinFace.Bytes, latinFace.Name))
                {
                    var runs = SegmentByCoverageChain(lineText, latinFace.Bytes, latinFace.Name);
                    pc.lines.Add(new CellLine
                    {
                        Text = lineText, FontSize = pp.fragFontSize, ForegroundColor = pp.color, Align = pp.lineAlign,
                        Type0Ttf = runs is { Count: > 0 } ? runs[0].Ttf : latinFace.Bytes,
                        Type0FontName = runs is { Count: > 0 } ? runs[0].Name : latinFace.Name,
                        StyleRuns = runs,
                        Underline = pp.fragUnderline,
                    });
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>Arabic text is shaped and laid right to left through its embedded face; true when handled here.</summary>
    private bool PlanArabicText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        if (Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(pp.text))
        {
            // The form-grid dialect's Arabic sets in the SERIF fallback
            // face (the dialect's Verdana carries no Arabic
            // program), and WRAPS at the cell box — the long RTL name runs
            // over two lines there. Other dialects keep the one-line Arial
            // path byte-stable.
            var arabicFont = Aspose.Pdf.Text.FontRepository.TryFindFont(
                FormGridCells ? "Times New Roman" : "Arial");
            var arabicTtf = arabicFont?.SourceFontData?.TtfData;
            if (arabicTtf is not null)
            {
                if (PlanArabicShapedText(pp, paragraph, pc, row, arabicFont, arabicTtf)) return true;
            }
        }
        return false;
    }

    /// <summary>An empty paragraph still takes its line pitch; true when nothing more is laid.</summary>
    private bool PlanEmptyText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        if (pp.text!.Length == 0)
        {
            var blank = new CellLine
            {
                Text = "", FontSize = pp.fragFontSize, ForegroundColor = pp.color, Align = pp.lineAlign,
                CssAsc = pp.fragKeepBlank ? pp.fragCssAsc : 0, CssDesc = pp.fragKeepBlank ? pp.fragCssDesc : 0,
                CssForce = pp.fragCssForce,
                // A kept blank line is a real line box under CSS run boxes — the
                // row of a cell holding nothing but an invisible character is
                // still one line tall.
                BoxH = pp.runBoxH, BaseOff = pp.runBoxH > 0 ? pp.runBoxH : 0,
                // A DELIBERATE blank (an explicit <br> line box) is markup-real:
                // the exact-stack and slice pricers must count it — the draw
                // walk advances it like any line.
                HtmlEngine = pp.fragKeepBlank,
            };
            // A blank line can still CARRY inline boxes (a standalone badge
            // circle in an otherwise-empty cell).
            if (paragraph is TextFragment { InlineBoxes: { Count: > 0 } blankBoxes })
            {
                blank.Boxes = blankBoxes;
                if (pp.fragLineH > blank.FontSize && blank.BoxH <= 0)
                    blank.BoxH = pp.fragLineH;
            }
            pc.lines.Add(blank);
            return true;
        }
        return false;
    }

    /// <summary>An HTML fragment paragraph supplies the cell text, its block metrics and its faces; true when the fragment was drawn directly.</summary>
    private bool PlanHtmlFragmentSource(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, RowPlanState rp, Row row)
    {
        if (paragraph is HtmlFragment html)
        {
            // A BOXED div pair — an outer div styled with a border (and
            // optionally a background-color) wrapping an inner div that
            // declares its font-size inline (unitless = pt) — renders as
            // CELL chrome: the fill and thin border cover the whole cell,
            // the text sets bold at the declared size, and the row
            // occupies the CSS normal line box plus the border above and
            // below (probed: fs 14 header cells pitch ≈ 14×1.2 + 2×0.7).
            var boxedDiv = Regex.Match(html.HtmlContent ?? "",
                @"<div\s[^>]*style\s*=\s*""[^""]*border\s*:[^""]*""[^>]*>\s*<div\s[^>]*style\s*=\s*""[^""]*font-size\s*:\s*([\d.]+)\s*[;""]",
                RegexOptions.IgnoreCase);
            if (boxedDiv.Success
                && double.TryParse(boxedDiv.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var boxedFs) && boxedFs > 0)
            {
                pp.fragFontSize = boxedFs;
                pp.htmlBoxedDivLineH = boxedFs * CssNormalLineHeight + 2 * HtmlThinBorderPt;
                pp.fragBold = Regex.IsMatch(html.HtmlContent ?? "",
                    @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase);
                var bg = Regex.Match(html.HtmlContent ?? "",
                    @"background-color\s*:\s*([-\w#]+)", RegexOptions.IgnoreCase);
                if (bg.Success && pc.cell.BackgroundColor is null)
                {
                    var sys = System.Drawing.Color.FromName(bg.Groups[1].Value);
                    if (sys.IsKnownColor || bg.Groups[1].Value.StartsWith('#'))
                        pc.cell.BackgroundColor = Color.FromRgb(sys);
                }
                pc.cell.Border ??= new BorderInfo(BorderSide.All,
                    (float)HtmlThinBorderPt, Color.Black);
                pp.text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                pp.color = pc.textState?.ForegroundColor;
                pp.lineAlign = ParseHtmlAlignment(html.HtmlContent);
            }
            else
            {
            if (PlanHtmlFragmentBlocks(pp, pc, rp, row, html)) return true;
            }
        }
        return false;
    }

    /// <summary>A text fragment paragraph supplies the cell text and its own font, size and colour.</summary>
    private void PlanTextFragmentSource(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        if (paragraph is TextFragment tf)
        {
            pp.text = tf.Text;
            pp.fragFontSize = ResolveCellParagraphFontSize(tf, pc.defaultFontSize, pc.cell, row);
            // Callers commonly style the SEGMENT rather than the fragment (the
            // fragment is built empty and the segment carries font, size and
            // colour), so a colour declared there is the fragment's colour —
            // the same fallback the font size already takes.
            pp.color = tf.TextState.ForegroundColor
                    ?? FirstSegmentForegroundColor(tf)
                    ?? pc.textState?.ForegroundColor;
            pp.fragBold = tf.TextState.IsBold || DeclaredCellBold(pc.cell, row);
            if (!pp.fragBold)
                foreach (var fseg in tf.Segments)
                    if (fseg.TextState.IsBold && !string.IsNullOrEmpty(fseg.Text))
                    { pp.fragBold = true; break; }
            pp.fragItalic = tf.TextState.IsItalic;
            pp.fragGridRuns = tf.FormGridRuns;
            pp.fragLink = tf.HyperlinkValue;
            pp.fragAnchors = tf.HtmlAnchors;
            pp.fragUnderline = tf.HtmlUnderline;
            pp.fragEmbeddedTtf = tf.TextState.Font?.SourceFontData?.TtfData;
            pp.fragEmbeddedName = tf.TextState.Font?.FontName;
            // A segment opting into NoCharacterAction.UseCustomReplacementFont NAMES the
            // face that carries the characters its own font lacks. Its declared face (a
            // Latin one, for CJK text) covers none of them, so the replacement font is the
            // face this text is actually written in. Without honouring it the CJK arm
            // below falls through to the generic installed substitute and the saved
            // document names a font the caller never asked for.
            if (pp.fragEmbeddedTtf is null || !CjkCoveredBy(pp.text, pp.fragEmbeddedTtf))
                if (DeclaredReplacementFace(tf) is { } repl)
                {
                    pp.fragEmbeddedTtf = repl.Ttf;
                    pp.fragEmbeddedName = repl.Name;
                }
            pp.fragCssAsc = tf.CssAscent; pp.fragCssDesc = tf.CssDescent;
            pp.fragKeepBlank = tf.CssKeepBlank;
            pp.fragCssForce = tf.CssLineBoxAlways;
            if (tf.TextState.HorizontalAlignment != HorizontalAlignment.Left &&
                tf.TextState.HorizontalAlignment != HorizontalAlignment.None)
                pp.lineAlign = tf.TextState.HorizontalAlignment;
        }
    }

    /// <summary>An HTML fragment without a boxed div lays its blocks as the cell's text lines; true when the blocks were drawn directly.</summary>
    private bool PlanHtmlFragmentBlocks(RowPlanParagraphState pp, RowPlanColumnState pc, RowPlanState rp, Row row, HtmlFragment html)
    {
        // HTML-engine cell: markup in the b/strong/small/div/br family lays
        // out as serif CSS line boxes (pixel-quantized leading, mixed bold/
        // small runs per line, kerned TJ output — see ParseHtmlEngineCell).
        // A fragment carrying its OWN TextState sizes the HTML from it (the
        // face does not follow — HTML text takes its family from CSS, so an
        // Arial TextState still sets in the UA serif). IsBreakWords lets a
        // word wider than the column break inside itself.
        // The generator dialect hands EVERY HtmlFragment to the HTML
        // engine, markup or not: a plain-text fragment sets in the UA
        // serif at 12 pt on 13.5 pt line boxes (probed 2026-08-23), and
        // its block ends ON its last baseline — the row below starts
        // there, the last line's descent hanging into it.
        var genHtmlSize = html.TextState?.FontSize > 0 ? html.TextState.FontSize : HtmlCellFontSize;
        var genPlainHtml = GeneratorCellModel
            && (html.HtmlContent?.IndexOf('<') ?? 0) < 0;
        if (ParseHtmlEngineCell(html.HtmlContent, pc.availWidth, genHtmlSize,
                html.IsBreakWords, plainText: GeneratorCellModel) is { } engineLines)
        {
            // Only the PLAIN-TEXT dialect reshapes the block: its last
            // line ends ON its baseline and its lines join the exact
            // stack. Markup-family cells keep their calibrated boxes.
            if (genPlainHtml && engineLines.Count > 0)
            {
                engineLines[^1].BoxH = SerifLineBox(genHtmlSize).Drop;
                foreach (var gel in engineLines) gel.GenEngineExact = true;
            }
            foreach (var el in engineLines)
            {
                el.ForegroundColor = pc.textState?.ForegroundColor;
                el.Align = ParseHtmlAlignment(html.HtmlContent);
                // The engine's lines are CSS line BOXES: the row's grid has
                // to step at the box, not at the bare font size, or the row
                // is priced short and its slice overruns the page.
                var elH = el.BoxH > 0 ? el.BoxH : el.FontSize;
                Consider(rp, elH, elH);
                pc.lines.Add(el);
            }
            return true;
        }
        // List cell: block text followed by <ul>/<ol> items. Items render
        // left-aligned with a hanging bullet — the item text indents by
        // the list margin-start (CSS default 40px), continuation lines
        // keep the indent, and the bullet hangs to the left of the first
        // line. The fragment's own stylesheet font-size (px) sizes every
        // line.
        if (BuildHtmlListCellLines(html.HtmlContent, pc.availWidth, pp.fragFontSize,
                pc.textState?.ForegroundColor, pc.cellAlign) is { } listLines)
        {
            foreach (var ll in listLines)
            {
                Consider(rp, ll.FontSize, ll.FontSize);
                pc.lines.Add(ll);
            }
            return true;
        }
        pp.text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
        pp.color = pc.textState?.ForegroundColor;
        pp.lineAlign = ParseHtmlAlignment(html.HtmlContent);
        // A block-level (div/p) text-align rule in the fragment's own
        // stylesheet wins over alignment hits from unrelated selectors
        // elsewhere in the sheet.
        var blockRule = Regex.Match(html.HtmlContent ?? "",
            @"(?:^|[,{}\s])(?:div|p)\s*(?:,[^{]*)?\{[^}]*text-align\s*:\s*(left|right|center)",
            RegexOptions.IgnoreCase);
        if (blockRule.Success)
            pp.lineAlign = blockRule.Groups[1].Value.ToLowerInvariant() switch
            {
                "right" => HorizontalAlignment.Right,
                "center" => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left,
            };
        var cssPx = Regex.Match(html.HtmlContent ?? "", @"font-size\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        if (blockRule.Success && cssPx.Success
            && double.TryParse(cssPx.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var cssPxV) && cssPxV > 0)
            pp.htmlCssBoxPx = cssPxV;
        // The markup's own <a href> runs annotate exactly like the ones a
        // TextFragment carries in HtmlAnchors — the tag-stripped text keeps the
        // anchor's characters, so each run is located on its laid-out line.
        pp.fragAnchors = ParseCellHtmlAnchors(html.HtmlContent);
        return false;
    }

    /// <summary>Arabic text shaped through its embedded face is laid right to left in the cell; true when handled here.</summary>
    private bool PlanArabicShapedText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row, Aspose.Pdf.Text.Font? arabicFont, byte[] arabicTtf)
    {
        // Form-grid measure path: the wrap runs on the BASE
        // face's advances with every non-WinAnsi char at the '?'
        // fallback width (the draw then substitutes the serif face) —
        // the shaped serif line is far narrower than the measure, so
        // the name row breaks two words earlier than a shaped-width
        // wrap would.
        var fgBaseTtf = FormGridCells
            ? CellFaceTtf((paragraph as TextFragment)?.TextState.Font?.FontName
                is { Length: > 0 } fgFam ? fgFam : "Verdana", false)
            : null;
        double MeasFallback(string s)
        {
            // Raw text against the base face, unmapped Arabic chars at
            // Verdana's average char width (OS/2 xAvgCharWidth 1229 of
            // the 2048 em) — the measure path: the wrap is
            // decided BEFORE the draw substitutes the serif face, so
            // the name row breaks after 'حياة' though the shaped serif
            // line is far narrower than the box.
            return fgBaseTtf is null
                ? MeasureWidthWithFont(
                    Aspose.Pdf.Text.ArabicTextShaper.Shape(s), pp.fragFontSize, arabicTtf)
                : MeasureWidthWithFont(s, pp.fragFontSize, fgBaseTtf,
                    unmappedEm: FormGridUnmappedAdvanceEm);
        }
        IEnumerable<string> arSegs;
        if (FormGridCells && pc.availWidth > 0)
        {
            var arLines = new List<string>();
            var arCur = "";
            foreach (var arW in pp.text!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var arCand = arCur.Length == 0 ? arW : arCur + " " + arW;
                if (arCur.Length > 0 && MeasFallback(arCand) > pc.availWidth)
                { arLines.Add(arCur); arCur = arW; }
                else arCur = arCand;
            }
            if (arCur.Length > 0) arLines.Add(arCur);
            arSegs = arLines;
        }
        else arSegs = new[] { pp.text! };
        foreach (var arSeg in arSegs)
        {
            if (!PlanArabicSegment(pp, paragraph, pc, arabicFont, arabicTtf, fgBaseTtf, arSeg)) break;
        }
        return true;
    }

    /// <summary>Shapes one Arabic segment, measures it in its face and adds its line to the plan.</summary>
    private bool PlanArabicSegment(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Aspose.Pdf.Text.Font? arabicFont, byte[] arabicTtf, byte[]? fgBaseTtf, string arSeg)
    {
        // LTR-base visual ordering (form-grid): an R-block spans from
        // an Arabic char through any interior neutrals to the last
        // Arabic char it can reach, and shapes (reversing) as one RTL
        // run; neutrals after the last block keep their logical place
        // (the name row draws its '?' tail after the
        // leftmost Arabic word, not reversed ahead of it).
        string ShapeLtrBase(string logical)
        {
            if (!FormGridCells) return Aspose.Pdf.Text.ArabicTextShaper.Shape(logical);
            bool IsR(char c) => c >= '؀' && c <= 'ۿ'
                || c >= 'ﭐ' && c <= '﷿'
                || c >= 'ﹰ' && c <= '﻿';
            var vsb = new System.Text.StringBuilder(logical.Length);
            var i = 0;
            while (i < logical.Length)
            {
                if (!IsR(logical[i])) { vsb.Append(logical[i]); i++; continue; }
                var j = i;
                var k = i;
                while (k < logical.Length)
                {
                    if (IsR(logical[k])) { j = k; k++; continue; }
                    var m = k;
                    while (m < logical.Length && !IsR(logical[m])
                           && !char.IsLetterOrDigit(logical[m])) m++;
                    if (m < logical.Length && IsR(logical[m])) { k = m; continue; }
                    break;
                }
                vsb.Append(Aspose.Pdf.Text.ArabicTextShaper.Shape(logical[i..(j + 1)]));
                i = j + 1;
            }
            return vsb.ToString();
        }
        var arShaped = ShapeLtrBase(arSeg);
        // Draw the shaped visual line as mixed runs: Arabic
        // segments in the serif fallback, everything else (the
        // '?' mojibake, dots, spaces) in the cell's own base
        // face — the run alternation the page must carry.
        List<(string Text, byte[] Ttf, string Name)>? arRuns = null;
        var arKernW = 0.0;
        if (FormGridCells && fgBaseTtf is not null)
        {
            arRuns = new();
            var segStart = 0;
            bool SegArabic(char c) => c >= '؀' && c <= 'ۿ'
                    || c >= 'ﭐ' && c <= '﷿'
                    || c >= 'ﹰ' && c <= '﻿';
            for (var si = 1; si <= arShaped.Length; si++)
            {
                if (si < arShaped.Length
                    && SegArabic(arShaped[si]) == SegArabic(arShaped[segStart])) continue;
                var segText = arShaped[segStart..si];
                var segAr = SegArabic(arShaped[segStart]);
                arRuns.Add((segText, segAr ? arabicTtf : fgBaseTtf,
                    segAr ? arabicFont!.FontName : "Verdana"));
                arKernW += MeasureWidthWithFont(segText, pp.fragFontSize,
                    segAr ? arabicTtf : fgBaseTtf);
                segStart = si;
            }
        }
        pc.lines.Add(new CellLine
        {
            Text = arShaped,
            FontSize = pp.fragFontSize,
            ForegroundColor = pp.color,
            Align = pp.lineAlign,
            Type0Ttf = arabicTtf,
            Type0FontName = arabicFont!.FontName,
            StyleRuns = arRuns,
            KernedWidth = arRuns is not null ? arKernW : 0,
            // Form-grid Arabic lines are CSS boxes like their Latin
            // siblings: own line box, measured baseline seat.
            BoxH = FormGridCells && (paragraph as TextFragment)?.CssLineHeightPt > 0
                ? (paragraph as TextFragment)!.CssLineHeightPt : 0,
            BaseOff = FormGridCells
                ? (paragraph as TextFragment)?.CssBaseDrop ?? 0 : 0,
        });
        return true;
    }
}
