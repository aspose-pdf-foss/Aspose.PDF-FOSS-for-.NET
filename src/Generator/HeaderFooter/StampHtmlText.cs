using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
    /// <summary>Resolves the text, size, face and CSS placement an HTML fragment stamps with; false when the fragment produced nothing to draw.</summary>
    private bool ResolveHtmlFragmentText(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        // With IsEmbedFonts the fragment's CSS font-family must bind the real
        // face (typically registered through a FolderFontSource) and render
        // through the embedded Type0 path — the Standard-14 fallback has no
        // font program, so it can never be embedded or subset.
        if (htmlFrag.HtmlLoadOptions is { IsEmbedFontsSet: true, IsEmbedFonts: true })
            ts.embedFont = ResolveDeclaredFont(htmlFrag.HtmlContent ?? "");
        ts.hc = htmlFrag.HtmlContent ?? "";
        // The escaped-newline footer fragment: markup authored as ONE source
        // line whose newlines are literal "\n" two-character sequences. The
        // reference typesets it on the serif default (its \n-poisoned CSS all
        // drops), draws the "\n"s as text, and hangs the stack from the page's
        // bottom-margin line pushed by the footer's own (negative) top margin —
        // see Table.DrawEscapedNewlineFooterHtml for the measured laws.
        if (!ResolveFooterTableLines(hf, ts, htmlFrag)) return false;
        // An HTML <table> in a header/footer fragment renders as real columns
        // (rows × cells) rather than the flat tag-stripped text stack: build a
        // generator Table and lay it out bottom-aligned to the footer band.
        if (!ResolveHtmlTableText(hf, ts, htmlFrag)) return false;
        // Procedure-form header band: right-aligned lines against the band's
        // right margin, bold only where the line itself carries it, explicit
        // CSS row heights stepping the stack (the remaining lines keep the
        // band's 1.12 em pitch).
        if (!ResolveProcedureBandText(hf, ts, htmlFrag)) return false;
        if (!ResolveHeaderBandText(hf, ts, htmlFrag)) return false;
        ts.cssLeftIndent = Converters.HtmlToPdfConverter.CssBlockLeftIndentPt(
            ts.hc, htmlFrag.HtmlLoadOptions) + ts.hfBandShift;
        if (!ResolveInlineEmphasisFontText(hf, ts, htmlFrag)) return false;
        if (!ResolveHtmlBlockText(hf, ts, htmlFrag)) return false;
        ts.text = HtmlFragment.StripHtmlTags(ApplyTextTransforms(ts.hc));
        ts.blkStyle = System.Text.RegularExpressions.Regex.Match(ts.hc,
            @"<(?:div|p)\b[^>]*style\s*=\s*(['""])(?<s>[^'""]*)\1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (ts.blkStyle.Success)
        {
            var st = ts.blkStyle.Groups["s"].Value;
            var fsm = System.Text.RegularExpressions.Regex.Match(st,
                @"font-size\s*:\s*([\d.]+)\s*px",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fsm.Success && double.TryParse(fsm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fpx) && fpx > 0)
                ts.fs = (float)(fpx * 0.75);
            var alm = System.Text.RegularExpressions.Regex.Match(st,
                @"text-align\s*:\s*(center|right)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (alm.Success)
                ts.cssAlign = alm.Groups[1].Value.Equals("center", StringComparison.OrdinalIgnoreCase)
                    ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        }
        return true;
    }

    /// <summary>An underline's centre below the baseline and its thickness, in em (measured on
    /// the 24 pt band fragment: the rule spans 0.05..0.15 em under the baseline).</summary>
    private const double BandUnderlineDropEm = 0.1;

    private const double BandUnderlineThicknessEm = 0.1;

    /// <summary>The single-font inline-emphasis fragment — one <c>&lt;font face size&gt;</c>
    /// holding text with b/u/i runs — draws its runs in the named face's real TrueType styles
    /// at the legacy size the attribute names, on a CSS line box seated under the band's top
    /// plus the fragment's own Margin.Top (probed: an Arial 36 pt header baseline 43.105
    /// under the sheet top with a 10 pt margin; the footer's box hangs from the page's
    /// bottom content margin the same way: baseline 511.105 on a 540 pt page), an
    /// underlined run ruled 0.1 em thick 0.1 em under its baseline; false when it drew.
    /// A face without TrueType data draws the Standard-14 twins at the legacy seat.</summary>
    private bool ResolveInlineEmphasisFontText(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        if (ts.embedFont is not null) return true;
        if (Converters.HtmlToPdfConverter.TryParseInlineEmphasisFont(ts.hc) is not (var face, var pt, var runs)
            || pt <= 0 || runs.Count == 0)
            return true;
        var x = hf.x + ts.cssLeftIndent;
        var b = new ContentStreamBuilder();
        b.SaveState();
        if (UaFace.TryLoad(face, false, false, BandFaceResourceName) is { } regular)
        {
            var bandTop = hf.isHeader ? hf.pageHeight - hf.mTop : hf.bodyBottom;
            var baseline = bandTop - (htmlFrag.Margin.TopTouched ? htmlFrag.Margin.Top : 0) - regular.Above(pt);
            var fontDict = Table.ResolvePageFontDict(hf.page);
            var styleIndex = 0;
            foreach (var (text, bold, underline, italic) in runs)
            {
                if (text.Length == 0) continue;
                var runFace = (bold || italic ? UaFace.TryLoad(face, bold, italic, BandFaceResourceName + (++styleIndex)) : null) ?? regular;
                var shown = runFace.EncodableText(text);
                var (res, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(fontDict, runFace.Ttf, runFace.Name, shown,
                    stripSpacesInBaseFont: true, resNameHint: runFace.ResName);
                b.BeginText().SetFont(res, pt).MoveTextPosition(x, baseline);
                if (runFace.KernAdjustments(shown) is { } adj) b.ShowTextHexKerned(hex, adj);
                else b.ShowTextHex(hex);
                b.EndText();
                var w = runFace.Width(shown, pt);
                if (underline)
                    b.SetLineWidth(pt * BandUnderlineThicknessEm)
                        .MoveTo(x, baseline - pt * BandUnderlineDropEm)
                        .LineTo(x + w, baseline - pt * BandUnderlineDropEm)
                        .Stroke();
                x += w;
            }
            b.RestoreState();
            hf.page.AddContentStream(b.Build());
            hf.lastTextY = baseline;
            hf.lastTextEndX = x;
            hf.y = baseline - regular.Below(pt);
            hf.firstParagraph = false;
            return false;
        }
        DrawStandard14EmphasisRuns(hf, htmlFrag, b, face, pt, runs, x);
        return false;
    }

    /// <summary>The resource name the band's UA face embeds under.</summary>
    private const string BandFaceResourceName = "FUaBand";

    /// <summary>The legacy band seat for a face with no TrueType data: Standard-14 twins,
    /// the baseline the band cursor pushed by the fragment's own margin.</summary>
    private void DrawStandard14EmphasisRuns(StampParagraphsState hf, HtmlFragment htmlFrag, ContentStreamBuilder b,
        string face, double pt, List<(string text, bool bold, bool underline, bool italic)> runs, double x)
    {
        var family = face.Contains("Times", StringComparison.OrdinalIgnoreCase) ? "Times"
            : face.Contains("Courier", StringComparison.OrdinalIgnoreCase) ? "Courier" : "Helvetica";
        var baseline = hf.isHeader
            ? hf.y - (htmlFrag.Margin.TopTouched ? htmlFrag.Margin.Top : 0)
            : hf.y + (htmlFrag.Margin.BottomTouched ? htmlFrag.Margin.Bottom : 0);
        foreach (var (text, bold, underline, italic) in runs)
        {
            if (text.Length == 0) continue;
            var fn = Standard14StyleName(family, bold, italic);
            b.BeginText().SetFont(EnsureFontResource(hf.page, fn), pt)
                .MoveTextPosition(x, baseline).ShowText(text).EndText();
            double w;
            try { w = FontRepository.TryFindFont(fn)?.MeasureString(text, pt) ?? EstimateWidth(text, pt); }
            catch { w = EstimateWidth(text, pt); }
            if (underline)
                b.SetLineWidth(pt * BandUnderlineThicknessEm)
                    .MoveTo(x, baseline - pt * BandUnderlineDropEm)
                    .LineTo(x + w, baseline - pt * BandUnderlineDropEm)
                    .Stroke();
            x += w;
        }
        b.RestoreState();
        hf.page.AddContentStream(b.Build());
        hf.lastTextY = baseline;
        hf.lastTextEndX = x;
        hf.y -= pt * 1.2;
        hf.firstParagraph = false;
    }

    /// <summary>The Standard-14 face name for a family in a bold/italic style.</summary>
    private static string Standard14StyleName(string family, bool bold, bool italic) => family switch
    {
        "Times" => bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold" : italic ? "Times-Italic" : "Times-Roman",
        "Courier" => bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold" : italic ? "Courier-Oblique" : "Courier",
        _ => bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica",
    };

    /// <summary>The fragment's block list picks the face and size the text stamps with; false when the blocks were drawn directly.</summary>
    private bool ResolveHtmlBlockText(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        ts.hfBlocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
            ApplyTextTransforms(ts.hc),
            ts.hfBandSmall ? 9.75 : hf.fontSize > 10 ? hf.fontSize : 12.0);
        ts.hfTextBlocks = ts.hfBlocks.FindAll(b => !string.IsNullOrWhiteSpace(b.Text));
        if (ts.embedFont is null
            && (ts.hfTextBlocks.Count > 1 || (IsClipExtraContent && ts.hfTextBlocks.Count == 1)))
        {
            // Inline emphasis carried by every line of the fragment (the
            // <p><u><strong>… header idiom) — resolved at fragment level.
            var hfBold = System.Text.RegularExpressions.Regex.IsMatch(ts.hc, @"<(b|strong)[\s>]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var hfUnderline = System.Text.RegularExpressions.Regex.IsMatch(ts.hc, @"<u[\s>]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var hfFont = hfBold ? "Helvetica-Bold" : "Helvetica";
            var hfRes = EnsureFontResource(hf.page, hfFont);
            // Body content edges this band must respect (the page's content
            // margins through the same fall-through as mLeft).
            var bodyTopMargin = Margin.TopTouched && Margin.Top > 0 ? Margin.Top
                : hf.page.PageInfo?.Margin is { TopTouched: true } ptm ? ptm.Top
                : hf.document?.PageInfo?.Margin is { TopTouched: true } dtm ? dtm.Top
                : 90;
            var bodyBottomMargin = hf.page.PageInfo?.Margin is { BottomTouched: true } pbm ? pbm.Bottom
                : hf.document?.PageInfo?.Margin is { BottomTouched: true } dbm ? dbm.Bottom
                : 72;
            var hfB = new ContentStreamBuilder();
            hfB.SaveState();
            var lineIdx = 0;
            foreach (var blk in ts.hfTextBlocks)
            {
                var bfs = blk.FontSize > 0 ? blk.FontSize : hf.fontSize;
                // The HTML paragraph band steps on a 1.12 em pitch.
                var pitch = bfs * 1.12;
                double baseline;
                if (hf.isHeader)
                {
                    baseline = hf.page.Height - hf.mTop - bfs - lineIdx * pitch - ts.hfBandOffset;
                    // Clip: a header line whose descender/underline would touch
                    // the body's first line (cap top = top margin − cap ascent)
                    // is extra content.
                    if (IsClipExtraContent
                        && (hf.page.Height - baseline) + bfs * 0.2 > bodyTopMargin - bfs * 0.72)
                        break;
                }
                else
                {
                    // Footer band: with clipping the band tucks under the body's
                    // bottom content margin; otherwise keep the legacy bottom-up
                    // stack from the footer margin.
                    baseline = IsClipExtraContent
                        ? bodyBottomMargin - bfs - lineIdx * pitch
                        : hf.mBottom + hf.fontSize + lineIdx * pitch;
                    if (baseline < 0) break;
                }
                // a row that declares its own background paints the band behind
                // its line, spanning its enclosing column's share of the band
                if (blk.BackgroundColor is { } hbg && ts.hfBandW > 0)
                    hfB.SetFillColor(hbg.R / 255.0, hbg.G / 255.0, hbg.B / 255.0)
                       .Rectangle(hf.x + ts.cssLeftIndent, baseline - RptBandDescentPt,
                           ts.hfBandW * (blk.WidthFrac > 0 ? blk.WidthFrac : 1.0), RptRowPitchPt)
                       .Fill()
                       .SetFillColor(0, 0, 0);
                hfB.BeginText()
                    .SetFont(hfRes, bfs)
                    .MoveTextPosition(hf.x + ts.cssLeftIndent + blk.LeftIndent, baseline)
                    .ShowText(blk.Text)
                    .EndText();
                if (hfUnderline)
                {
                    double ulW;
                    try
                    {
                        ulW = FontRepository.TryFindFont(hfFont)?.MeasureString(blk.Text, bfs)
                              ?? EstimateWidth(blk.Text, bfs);
                    }
                    catch { ulW = EstimateWidth(blk.Text, bfs); }
                    hfB.SetLineWidth(bfs * 0.07)
                        .MoveTo(hf.x + ts.cssLeftIndent + blk.LeftIndent, baseline - bfs * 0.12)
                        .LineTo(hf.x + ts.cssLeftIndent + blk.LeftIndent + ulW, baseline - bfs * 0.12)
                        .Stroke();
                }
                lineIdx++;
            }
            hfB.RestoreState();
            hf.page.AddContentStream(hfB.Build());
            if (hf.isHeader) hf.y -= lineIdx * hf.fontSize * 1.12;
            return false;
        }
        return true;
    }

    /// <summary>A header band's face, size and offsets from its CSS; false when the band drew itself.</summary>
    private bool ResolveHeaderBandText(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        ts.hfBandOffset = 0.0;
        ts.hfBandShift = 0.0;
        ts.hfBandSmall = false;
        ts.hfBandW = 0.0;
        if (hf.isHeader && ts.embedFont is null)
        {
            var h5M = System.Text.RegularExpressions.Regex.Match(ts.hc,
                @"(?s)<h5[^>]*>(.*?)</h5>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var bodyWM = System.Text.RegularExpressions.Regex.Match(ts.hc,
                @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))[^'""]*\1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var spans = h5M.Success
                ? System.Text.RegularExpressions.Regex.Matches(h5M.Groups[1].Value,
                    @"(?s)<span\b[^>]*style\s*=\s*(['""])(?<st>[^'""]*width\s*:\s*[\d.]+%[^'""]*)\1[^>]*>(?<t>.*?)</span>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                : null;
            if (!LayoutHeaderBandSpans(hf, ts, spans, bodyWM)) return false;
        }
        return true;
    }

    /// <summary>A procedure band (numbered lines) stamps its own lines; false when it drew them.</summary>
    private bool ResolveProcedureBandText(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        if (ts.embedFont is null
            && Converters.HtmlToPdfConverter.TryParseProcedureBandLines(ts.hc) is { } pbLines)
        {
            var pbFs = hf.fontSize > 10 ? hf.fontSize : 12.0;
            // the band's own container may declare a right padding in the
            // fragment's LINKED sheet (reachable through its load options) —
            // the right-aligned lines anchor that much further in
            var pbRight = hf.page.Width - (Margin.RightTouched ? Margin.Right : hf.mLeft)
                - Converters.HtmlToPdfConverter.BandPaddingRightPt(ts.hc, htmlFrag.HtmlLoadOptions);
            var pbBaseline = hf.page.Height - hf.mTop - pbFs;
            double pbAdv = 0;
            var pbB = new ContentStreamBuilder();
            pbB.SaveState();
            foreach (var pl in pbLines)
            {
                var pf = pl.Bold ? "Helvetica-Bold" : "Helvetica";
                var pres = EnsureFontResource(hf.page, pf);
                double pw;
                try
                {
                    pw = FontRepository.TryFindFont(pf)?.MeasureString(pl.Text, pbFs)
                         ?? EstimateWidth(pl.Text, pbFs);
                }
                catch { pw = EstimateWidth(pl.Text, pbFs); }
                pbB.BeginText().SetFont(pres, pbFs)
                    .MoveTextPosition(pbRight - pw, pbBaseline)
                    .ShowText(pl.Text).EndText();
                var pbStep = pl.HeightPt > 0 ? pl.HeightPt : pbFs * 1.12;
                pbBaseline -= pbStep;
                pbAdv += pbStep;
            }
            pbB.RestoreState();
            hf.page.AddContentStream(pbB.Build());
            if (hf.isHeader) hf.y -= pbAdv;
            return false;
        }
        return true;
    }

    /// <summary>An HTML table in the band renders as a table; false when nothing is left to stamp as text.</summary>
    private bool ResolveHtmlTableText(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        if (Converters.HtmlToPdfConverter.ContainsTable(ts.hc))
        {
            // A header/footer table is authored with its frame on the cells
            // themselves (`<td style="BORDER-TOP: black 1pt solid; …">`), so the
            // per-cell border sides are read here as they are in the band dialect.
            // A table that states a CSS width gets the BAND to resolve it against. Handed a
            // zero, its `width: N%` cells have no box at all and fall back to their content, so
            // the complaint report's centre header cell came out a third of its share and broke
            // "New York State Office of Child and Family Services" over two lines where the
            // reference keeps it on one. The band is the fragment's declared body width, else the
            // page between its margins. ⚠ Only a CSS width takes this: a table sizing itself from
            // a `width=` ATTRIBUTE is scaled by the width solver's own attribute path instead
            // (TableWidthSolver's absolute-width scaler), and a table declaring neither is
            // shrink-to-fit — probed, and both keep the band they had.
            var bandAvail = Converters.HtmlToPdfConverter.DeclaresCssTableWidth(ts.hc)
                ? BandBoxWidthPt(hf, ts.hc) - 2 * BandSideInsetPt : 0;
            // …and the face its own sheet names, so the row is measured and drawn in the family
            // the fragment declares instead of the Standard-14 default.
            var (htmlTbl, _) = Converters.HtmlToPdfConverter.BuildTableFromHtml(
                ts.hc, bandAvail, htmlFrag.HtmlLoadOptions, null, null,
                authoredCellChrome: true, defaultCellFace: BandSheetFamily(ts.hc));
            if (htmlTbl is not null)
            {
                // On a /Rotate page the footer content is drawn through a visual→raw
                // matrix (the table is laid out in the page's rotation-adjusted VISUAL
                // space, then mapped into raw content space so it appears upright).
                var rotCm = VisualToRawRotationCm(hf.page);

                // The generator centres a table in `page.Width - 2*FlowLeftOffset`; a
                // left-aligned footer table at Margin.Left would shrink to half width
                // (over-wrapping its cells). For the rotated path, lay the table out
                // one-sided (start at a small offset so the usable width ≈ the band
                // from the left margin to the right edge) and translate it to Margin.Left
                // in the visual frame; the unrotated path keeps the original placement.
                double flo = hf.x + (bandAvail > 0 ? BandSideInsetPt : 0), translateX = 0;
                if (rotCm is not null)
                {
                    var desiredUsable = Math.Max(50.0, hf.page.Width - hf.x - 36);
                    flo = (hf.page.Width - desiredUsable) / 2;
                    translateX = hf.x - flo;
                }
                htmlTbl.FlowLeftOffset = flo;

                // First pass measures the single-page height (from the page top so the
                // table doesn't trip the page-break logic); then render so the table's
                // bottom sits at the footer's bottom margin. A far-below bottom margin
                // keeps the whole table on this page (no spill slice the footer drops).
                htmlTbl.BuildMultiPage(hf.page, hf.page.Height, 0, measureOnly: true);
                var startY = hf.isHeader ? hf.y : hf.mBottom + htmlTbl.LastRenderedHeight;
                var contents = htmlTbl.BuildMultiPage(hf.page, startY, -hf.page.Height);

                if (rotCm is null)
                {
                    if (contents.Count > 0) hf.page.AddContentStream(contents[0]);
                    // Cell images are collected by the layout pass, not written into
                    // its content stream — without this blit a logo in a header
                    // table's first cell is laid out and then silently dropped.
                    if (htmlTbl.LastImageDraws.Count > 0)
                        foreach (var (data, rect) in htmlTbl.LastImageDraws[0])
                            try { hf.page.AddImage(data, rect); }
                            catch (ArgumentException) { /* unsupported format: skip */ }
                    if (htmlTbl.LastGraphDraws.Count > 0)
                        foreach (var gc in htmlTbl.LastGraphDraws[0])
                            hf.page.AddContentStream(gc);
                }
                else
                {
                    var wrap = new System.Text.StringBuilder("q\n").Append(rotCm).Append('\n');
                    if (Math.Abs(translateX) > 0.001)
                        wrap.Append("1 0 0 1 ")
                            .Append(translateX.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))
                            .Append(" 0 cm\n");
                    if (contents.Count > 0)
                        wrap.Append(System.Text.Encoding.ASCII.GetString(contents[0])).Append('\n');
                    if (htmlTbl.LastGraphDraws.Count > 0)
                        foreach (var gc in htmlTbl.LastGraphDraws[0])
                            wrap.Append(System.Text.Encoding.ASCII.GetString(gc)).Append('\n');
                    wrap.Append("Q\n");
                    hf.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(wrap.ToString()));
                }
                hf.y = startY - htmlTbl.LastRenderedHeight;
                StampHeadingsAfterTable(hf, ts, startY - htmlTbl.LastRenderedHeight);
            }
            return false;
        }
        return true;
    }

    // The heading ladder under a header fragment's table, PROBED through the reference engine
    // by stamping synthetic fragments and reading back the drawn
    // baselines. One variable at a time - 1/2/3 table rows, 1/2/3 headings, 12 cm and 17.59 cm
    // bodies - and the ladder is invariant to all of them:
    //     rows=1  last row baseline 818.55 -> 800.17, 783.67
    //     rows=2  last row baseline 804.30 -> 785.92, 769.42
    //     rows=3  last row baseline 790.05 -> 771.67, 755.17
    // i.e. the first heading drops 18.38 from the LAST ROW'S BASELINE, then 16.50 per heading.
    //
    // Both figures are the ORDINARY flow, not a band rule of their own: the headings follow the
    // table in the block flow with their margins zeroed by the corpus sheet, so the first one
    // seats its line box on the table's bottom edge and each next one is a whole line further
    // down. Decomposing the probe against the face's line box says the same thing -
    //     drop  = Below(cell face, cell size) + cell padding + cell spacing + Above(head face, head size)
    //     pitch = LineHeight(head face, head size)
    // and for the reference's Arial h3 (14.04 pt) LineHeight is 16.50 exactly, while the
    // complaint report's own render puts its table's bottom edge at 25.750 and the reference's
    // first heading baseline at 38.867 - a drop of 13.117, which is Above(Arial, 14.04) to five
    // decimals. So the ladder hangs off the table's BOX BOTTOM and is read from the band face,
    // which also carries it to a face or a heading size the probe never covered.
    // (h4 gives 15.74/13.50 on the same probe; h2 is not comparable because the corpus sheet
    // zeroes the margins of h3..h6 only, so an h2 keeps its own.)

    /// <summary>Table box bottom to the first heading baseline when the band names no face to
    /// read metrics from: Above(Arial, 14.04), the value the reference itself draws.</summary>
    private const double HdrHeadingAbovePt = 13.117;

    /// <summary>Heading baseline to heading baseline (probed) when the band names no face:
    /// LineHeight(Arial, 14.04).</summary>
    private const double HdrHeadingPitchPt = 16.50;

    /// <summary>How far inside its declared box a band grid's text seats: the border-spacing
    /// the browser keeps OUTSIDE every cell of a separated grid - on the grid's own outer edge
    /// as much as between its cells - plus the UA stylesheet's own cell padding. The generator
    /// grid carries NEITHER horizontally (its columns are calibrated off measured footprints,
    /// and its UA chrome takes only the vertical pair), so a band that declares its width lays
    /// out that far inside the box it declared instead. Its first cell's text then starts
    /// there and its last cell's ends the same distance short of the far edge, which is where
    /// every run of a band actually seats: measured on the complaint report, 92.250 and
    /// 586.353 in a 90..588.614 box. ⚠ The interior gaps come out one spacing narrower than
    /// the browser's, there being nothing between two columns to carry it - no band run seats
    /// on an interior edge.</summary>
    private const double BandSideInsetPt = Converters.HtmlToPdfConverter.UaCellSpacingPt
        + Converters.HtmlToPdfConverter.UaCellPadPt;

    /// <summary>The band a stamped fragment lays out in: the width its own body declares, else
    /// the page between the flow's left offset and its mirror on the right.</summary>
    private double BandBoxWidthPt(StampParagraphsState hf, string html)
    {
        var bodyWM = System.Text.RegularExpressions.Regex.Match(html,
            @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt|px))[^'""]*\1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var w = bodyWM.Success ? PhysPt(bodyWM.Groups[2].Value) : 0;
        return w > 0 ? w : hf.page.Width - 2 * hf.x;
    }

    /// <summary>The face a band fragment's own stylesheet gives its text: the family its
    /// universal or body rule names. The band paths below drew the Standard-14 twins whatever
    /// the fragment said, so a sheet declaring Arial stamped Helvetica - different glyphs, a
    /// different wrap, and a centred line whose measured width and drawn width disagreed.
    /// Null when the fragment names no family, or names one with no TrueType data.</summary>
    private static string? BandSheetFamily(string html)
    {
        var css = Converters.HtmlToPdfConverter.ParseStyleSheet(html);
        foreach (var key in new[] { "*", "body", "html" })
            if (css.TryGetValue(key, out var rule)
                && rule.TryGetValue("font-family", out var fam)
                && Converters.HtmlToPdfConverter.FirstFontFamily(fam) is { Length: > 0 } first)
                return first;
        return null;
    }

    /// <summary>The headings a fragment carries OUTSIDE its tables, in document order.</summary>
    private static List<string> HeadingsOutsideTables(string html)
    {
        var loose = System.Text.RegularExpressions.Regex.Replace(html,
            @"(?s)<table\b.*?</table>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var found = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(loose, @"(?s)<h([1-6])\b[^>]*>(.*?)</h\1>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var txt = HtmlFragment.StripHtmlTags(m.Groups[2].Value).Trim();
            if (txt.Length > 0) found.Add(txt);
        }
        return found;
    }

    /// <summary>A header fragment's headings that sit OUTSIDE its table. The table resolver
    /// builds the table alone and returns, so an authored heading beside it - the complaint
    /// report's centred "Division of Child Care Services" pair - was dropped entirely. The
    /// reference draws them centred under the table, on the same heading ladder the band
    /// dialect measures; a fragment that declares its own body width centres on that band
    /// rather than on the page's content box.</summary>
    private void StampHeadingsAfterTable(StampParagraphsState hf, StampTextState ts, double tableBottom)
    {
        if (!hf.isHeader) return;
        var headings = HeadingsOutsideTables(ts.hc);
        if (headings.Count == 0) return;

        var bodyWM = System.Text.RegularExpressions.Regex.Match(ts.hc,
            @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))[^'""]*\1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var bandW = bodyWM.Success ? PhysPt(bodyWM.Groups[2].Value) : 0;
        if (bandW <= 0) bandW = hf.page.Width - 2 * hf.x;

        // The fragment's own face draws them; the Standard-14 twin is the fallback. The face
        // decides the centring too: a heading measured in one face and drawn in another lands
        // off centre by the difference (measured on the complaint report: 4 pt).
        var sheetFace = BandSheetFamily(ts.hc) is { } fam
            ? UaFace.TryLoad(fam, true, false, BandFaceResourceName + "H") : null;
        var res = sheetFace is null ? EnsureFontResource(hf.page, "Helvetica-Bold") : "";
        var fontDict = sheetFace is null ? null : Table.ResolvePageFontDict(hf.page);
        var b = new ContentStreamBuilder();
        b.SaveState();
        // The first heading seats its line box on the table's bottom edge, each next one a whole
        // line below it.
        var pitch = sheetFace?.LineHeight(RptH3FontPt) ?? HdrHeadingPitchPt;
        var baseline = tableBottom - (sheetFace?.Above(RptH3FontPt) ?? HdrHeadingAbovePt) + pitch;
        foreach (var txt in headings)
        {
            baseline -= pitch;
            if (sheetFace is { } face)
            {
                var shown = face.EncodableText(txt);
                var (fres, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(fontDict!, face.Ttf, face.Name,
                    shown, stripSpacesInBaseFont: true, resNameHint: face.ResName);
                b.BeginText().SetFont(fres, RptH3FontPt)
                    .MoveTextPosition(hf.x + (bandW - face.Width(shown, RptH3FontPt)) / 2, baseline);
                if (face.KernAdjustments(shown) is { } adj) b.ShowTextHexKerned(hex, adj);
                else b.ShowTextHex(hex);
                b.EndText();
                continue;
            }
            var tw = MeasureReportText(txt, RptH3FontPt, bold: true);
            b.BeginText().SetFont(res, RptH3FontPt)
                .MoveTextPosition(hf.x + (bandW - tw) / 2, baseline)
                .ShowText(txt).EndText();
        }
        b.RestoreState();
        hf.page.AddContentStream(b.Build());
        hf.y = baseline;
    }

    /// <summary>A footer HTML table with explicit line breaks stamps line by line; false when it drew everything itself.</summary>
    private bool ResolveFooterTableLines(StampParagraphsState hf, StampTextState ts, HtmlFragment htmlFrag)
    {
        if (!hf.isHeader && ts.hc.Contains("\\n", StringComparison.Ordinal)
            && Converters.HtmlToPdfConverter.ContainsTable(ts.hc))
        {
            var escMarginBottom = hf.page.PageInfo?.Margin is { BottomTouched: true } epbm ? epbm.Bottom
                : hf.document?.PageInfo?.Margin is { BottomTouched: true } edbm ? edbm.Bottom
                : 72;
            var escTop = escMarginBottom - (Margin.TopTouched ? Margin.Top : 0);
            // The band's right edge is the RAW media-box width even on a
            // rotated page (measured: a rotated page's visual frame is 792 wide, yet
            // the table ends exactly at the raw 612) — the footer
            // geometry is computed on raw dims and drawn through the rotation.
            var escRight = hf.page.MediaBox.Width - (Margin.RightTouched ? Margin.Right : 0);
            if (Table.DrawEscapedNewlineFooterHtml(hf.page, ts.hc, hf.x, escRight, escTop) is ({ } escBytes, var escH))
            {
                // The dialect lays out in the page's rotation-adjusted VISUAL
                // frame; a /Rotate page needs the same visual→raw mapping the
                // generic footer-table path applies.
                if (VisualToRawRotationCm(hf.page) is { } escRot)
                {
                    var escWrap = new System.Text.StringBuilder("q\n").Append(escRot).Append('\n')
                        .Append(System.Text.Encoding.ASCII.GetString(escBytes)).Append("\nQ\n");
                    escBytes = System.Text.Encoding.ASCII.GetBytes(escWrap.ToString());
                }
                hf.page.AddContentStream(escBytes);
                hf.y = escTop - escH;
                return false;
            }
        }
        return true;
    }

    /// <summary>A CSS physical length (cm/mm/in/pt) in points; 0 when the text is not one.</summary>
    private static double PhysPt(string v)
    {
        var m2 = System.Text.RegularExpressions.Regex.Match(v,
            @"([\d.]+)\s*(cm|mm|in|pt)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m2.Success) return 0;
        var n = double.Parse(m2.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        return m2.Groups[2].Value.ToLowerInvariant() switch
        {
            "cm" => n * 28.346457, "mm" => n * 2.8346457, "in" => n * 72.0, _ => n,
        };
    }

    /// <summary>Lays out a header band's percentage-width spans as boxes; false when the band drew itself.</summary>
    private bool LayoutHeaderBandSpans(StampParagraphsState hf, StampTextState ts, System.Text.RegularExpressions.MatchCollection? spans, System.Text.RegularExpressions.Match bodyWM)
    {
        if (spans is { Count: >= 2 } && bodyWM.Success
            && PhysPt(bodyWM.Groups[2].Value) is > 0 and var bandW)
        {
            var pageMarginCss = 0.0;
            var atPage = System.Text.RegularExpressions.Regex.Match(ts.hc,
                @"(?s)@page\s*\{(?<b>[^}]*)\}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (atPage.Success)
            {
                var mDecl = System.Text.RegularExpressions.Regex.Match(atPage.Groups["b"].Value,
                    @"(?<![-\w])margin(-left)?\s*:\s*([^;}]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mDecl.Success) pageMarginCss = PhysPt(mDecl.Groups[2].Value);
            }
            var bandL = RptBandLeftPt + pageMarginCss;
            var h5Fs = RptH5FontPt;
            var spaceW = RptSpaceEm * h5Fs;        // inter-inline-block whitespace
            var boldRes = EnsureFontResource(hf.page, "Helvetica-Bold");
            var bandB = new ContentStreamBuilder();
            bandB.SaveState();
            double MeasureBold(string t, double fs2) => MeasureReportText(t, fs2, bold: true);
            var h5Base = hf.mTop + RptH5BasePt;
            var bx = bandL;
            foreach (System.Text.RegularExpressions.Match sp in spans)
            {
                var st = sp.Groups["st"].Value;
                var pw = System.Text.RegularExpressions.Regex.Match(st, @"width\s*:\s*([\d.]+)%");
                var boxW = pw.Success ? double.Parse(pw.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) / 100.0 * bandW : 0;
                var txt = HtmlFragment.StripHtmlTags(sp.Groups["t"].Value).Trim();
                if (txt.Length > 0)
                {
                    var tw = MeasureBold(txt, h5Fs);
                    var tx = System.Text.RegularExpressions.Regex.IsMatch(st, @"text-align\s*:\s*center",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        ? bx + (boxW - tw) / 2
                        : System.Text.RegularExpressions.Regex.IsMatch(st, @"text-align\s*:\s*right",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            ? bx + boxW - tw
                            : bx;
                    bandB.BeginText().SetFont(boldRes, h5Fs)
                        .MoveTextPosition(tx, hf.page.Height - h5Base)
                        .ShowText(txt).EndText();
                }
                bx += boxW + spaceW;
            }
            // centred <h3> headings on the band's own ladder
            var h3Fs = RptH3FontPt;
            var lastBase = h5Base;
            var firstH3 = true;
            foreach (System.Text.RegularExpressions.Match h3 in
                System.Text.RegularExpressions.Regex.Matches(ts.hc, @"(?s)<h3\b[^>]*>(.*?)</h3>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var txt = HtmlFragment.StripHtmlTags(h3.Groups[1].Value).Trim();
                if (txt.Length == 0) continue;
                lastBase += firstH3 ? RptH5ToH3Pt : RptH3PitchPt;
                firstH3 = false;
                var tw = MeasureBold(txt, h3Fs);
                bandB.BeginText().SetFont(boldRes, h3Fs)
                    .MoveTextPosition(bandL + (bandW - tw) / 2, hf.page.Height - lastBase)
                    .ShowText(txt).EndText();
            }
            bandB.RestoreState();
            hf.page.AddContentStream(bandB.Build());
            // the DATA REGION below the headings: nested percentage columns
            // of bold right-aligned labels, bands, checkboxes and framed
            // fieldsets, all at the band's left in the sheet's small size.
            // The first row's baseline sits 18.26 under the last heading's.
            var regionHtml = System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Match(ts.hc,
                    @"(?s)<body\b[^>]*>(.*?)</body>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    is { Success: true } rgb ? rgb.Groups[1].Value : ts.hc,
                @"(?s)<h5[^>]*>.*?</h5>|<h3\b[^>]*>.*?</h3>|<script\b.*?</script>|<!--.*?-->", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var regB = new ContentStreamBuilder();
            regB.SaveState();
            RenderReportRegion(hf.page, regB, regionHtml, bandL, bandW,
                lastBase + RptH3ToRegionPt, false,
                boldRes, EnsureFontResource(hf.page, "Helvetica"));
            regB.RestoreState();
            hf.page.AddContentStream(regB.Build());
            return false;
        }
        return true;
    }
}
