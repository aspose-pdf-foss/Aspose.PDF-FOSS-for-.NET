using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
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
        if (htmlFrag.HtmlLoadOptions?.IsEmbedFonts == true)
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
            && Converters.HtmlToPdfConverter.TryParseProcedureBandLines(ts.hc, out var pbLines))
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
            var htmlTbl = Converters.HtmlToPdfConverter.BuildTableFromHtml(
                ts.hc, 0, out _, htmlFrag.HtmlLoadOptions, null, null,
                authoredCellChrome: true);
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
                double flo = hf.x, translateX = 0;
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
            }
            return false;
        }
        return true;
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
            if (Table.DrawEscapedNewlineFooterHtml(hf.page, ts.hc, hf.x, escRight, escTop,
                    out var escH) is { } escBytes)
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
