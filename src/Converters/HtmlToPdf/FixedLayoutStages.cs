using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Every div is drawn onto its band: the sheet it lands on, its runs and the images it carries.</summary>
    private static Document RenderFixedLayoutPages(HtmlLoadOptions? options, List<FixedPageDiv> divs, double pageH, double bandH, double pageW, FixedSheetModel model)
    {
        Document? doc = default;
        doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // ALL source pages stack into one continuous flow that is sliced
        // into sheet-height bands ACROSS page boundaries (a sheet can show
        // the seam: one page's footer and the next page's heading mid-sheet). Sheets
        // are created on demand in flow order; a page box shorter than the band
        // shares its sheet with the next page's top.
        var cum = 0.0;          // this div's top edge in continuous flow coordinates
        var pagesMade = 0;
        foreach (var div in divs)
        {
            cum = FixedContainerPushedTop(div, cum, bandH, model);
            var k0 = (int)Math.Floor((cum + 0.01) / bandH);
            var k1 = Math.Max(k0, (int)Math.Floor((cum + div.SrcH - 0.01) / bandH));
            for (var band = k0; band <= k1; band++)
            {
                pagesMade = RenderFixedLayoutBand(options, doc, docFontDict, inv, div, band, cum, pageH, bandH, pageW, pagesMade, model);
            }
            cum += div.SrcH;
        }
        return doc;
    }

    /// <summary>The flow top of <paramref name="div"/> after the widow rule's PAGE PUSH: a container
    /// whose in-flow content does not fit what is left of the band it opens on moves whole - image,
    /// layer and every positioned line - to the next sheet's top margin, and the flow cursor moves
    /// with it, so every later container shifts by the band that was left (probed on the raster
    /// export: source page 8 opens with 34 pt of band left for a 79.2 pt layer, and every line of
    /// pages 8-11 sits 34 lower than the plain 792-per-container advance). A dialect without a widow
    /// rule, or a container that read no in-flow content, keeps its natural top.</summary>
    private static double FixedContainerPushedTop(FixedPageDiv div, double cum, double bandH, FixedSheetModel model)
    {
        if (model.FitBottomPt <= 0 || div.InFlowContentHeightPt <= 0) return cum;
        var band = (int)Math.Floor((cum + 0.01) / bandH);
        var y = model.ContentTopPt + cum - band * bandH;
        // A top at or below the band's bottom edge already continues on the next sheet.
        if (y >= model.FitBottomPt) { band++; y -= bandH; }
        var remaining = model.FitBottomPt - y;
        if (div.InFlowContentHeightPt - remaining < FixedPushTolerancePt) return cum;
        return (band + 1) * bandH + (model.WidowTopPt - model.ContentTopPt);
    }

    /// <summary>The sheet the bands are drawn on: its height, the band it holds and the width its widest element needs.</summary>
    private static (double pageH, double bandH, double pageW)? SolveFixedLayoutSheet(HtmlLoadOptions? options, List<FixedPageDiv> divs, bool emGridMarkup, FixedSheetModel model)
    {
        double pageH = default;
        double bandH = default;
        double pageW = default;
        pageW = 0;
        var pageInfo = options?.PageInfo;
        pageH = pageInfo?.Height is > 0 ? pageInfo.Height : 841.89;
        if (pageInfo?.LandscapeRequested == true && pageInfo.Width > pageH)
            pageH = pageInfo.Width;
        // The BAND pitch derives from exact A4 (841.89); PageInfo's rounded 842
        // default gives a pitch 0.11pt long, which walks the content ~12pt
        // off by sheet 95 of a long document. The sheet's PAGE BOX, however, is
        // the rounded 842 — the rasterized page is 842pt tall while the bands
        // step at the exact-A4 pitch.
        var bandBaseH = Math.Abs(pageH - 841.89) < 0.5 ? 841.89 : pageH;
        if (Math.Abs(pageH - 841.89) < 0.5) pageH = 842.0;
        bandH = model.BandPitchPt > 0
            ? model.BandPitchPt
            : bandBaseH - model.ContentTopPt - model.ContentBottomPt;
        if (bandH <= 0) return null;
        // A dialect that measures its own pitch pins the band to the sheet's top margin;
        // the clip's bottom edge is then whatever the sheet has left under it.
        if (model.BandPitchPt > 0) model.ContentBottomPt = pageH - model.ContentTopPt - bandH;

        // Sheet width: 96 + the widest laid-out element + the dialect's right pad, over the
        // whole document.
        double maxRight = MeasureFixedSheetDivs(divs, model, emGridMarkup);
        if (maxRight <= 0)
            foreach (var div in divs) maxRight = Math.Max(maxRight, div.SrcW);
        pageW = FixedMarginLeftPt + maxRight + model.RightPadPt;
        return (pageH, bandH, pageW);
    }

    /// <summary>One band of a div: the sheet it opens or reuses, and the runs and images it draws on it.</summary>
    /// <returns>The number of sheets made so far, after any this band had to open.</returns>
    private static int RenderFixedLayoutBand(HtmlLoadOptions? options, Document doc, Core.PdfDictionary docFontDict, System.Globalization.CultureInfo inv, FixedPageDiv div, int band, double cum, double pageH, double bandH, double pageW, int pagesMade, FixedSheetModel model)
    {
        while (pagesMade <= band)
        {
            var np = doc.Pages.Add(pageW, pageH);
            EnsureFonts(np, docFontDict);
            pagesMade++;
        }
        var page = doc.Pages[band + 1];
        var yOff = model.ContentTopPt - (band * bandH - cum);   // top-down source y → sheet y

        // Clip everything on this sheet to the content band; the q stays open
        // across the content streams below and is closed at the end.
        page.AddContentStream(Encoding.ASCII.GetBytes(
            $"q 0 {model.ContentBottomPt.ToString("F2", inv)} {pageW.ToString("F2", inv)} {bandH.ToString("F2", inv)} re W n\n"));

        if (div.Background is not null)
        {
            try
            {
                // A raster page export's PNG draws at its OWN size; the other dialects' backgrounds
                // fill the container box they were authored for.
                var bgW = div.BackgroundW > 0 ? div.BackgroundW : div.SrcW;
                var bgH = div.BackgroundH > 0 ? div.BackgroundH : div.SrcH;
                page.AddImage(div.Background, new Aspose.Pdf.Rectangle(
                    FixedMarginLeftPt, pageH - yOff - bgH, FixedMarginLeftPt + bgW, pageH - yOff));
            }
            catch { /* undecodable background — text still imports */ }
        }

        // The page SVG replays from its sidecar (ObjectUrl) or, in the
        // self-contained dialect, from the inline markup itself.
        string? replaySvgText = null;
        var replaySvgDir = "";
        if (div.ObjectUrl is not null)
        {
            var svgBytes = LoadConverterImage(div.ObjectUrl, options);
            if (svgBytes is not null)
            {
                var slash = div.ObjectUrl.LastIndexOf('/');
                if (slash > 0) replaySvgDir = div.ObjectUrl[..(slash + 1)];
                replaySvgText = Encoding.UTF8.GetString(svgBytes);
            }
        }
        else if (div.InlineSvgText is not null)
        {
            replaySvgText = div.InlineSvgText;
        }
        if (replaySvgText is not null)
        {
            // Map the SVG's viewBox onto the page box: x right, y DOWN from
            // the sheet's content origin.
            var vb2 = Regex.Match(replaySvgText,
                @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
            double vw = 0, vh = 0;
            if (vb2.Success)
            {
                vw = double.Parse(vb2.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture);
                vh = double.Parse(vb2.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (vw > 0 && vh > 0)
            {
                var placement = new[]
                {
                    div.SrcW / vw, 0, 0, -div.SrcH / vh,
                    FixedMarginLeftPt, pageH - yOff,
                };
                try { ReplaySvgObject(page, replaySvgText, placement, replaySvgDir, options); }
                catch { /* a partial page graphic still beats none */ }
            }
        }

        var sb = new StringBuilder();
        foreach (var s in div.Spans)
        {
            if (!RenderFixedLayoutSpan(docFontDict, inv, div, s, page, sb, yOff, band, cum, bandH, pageH, model)) break;
        }
        if (sb.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

        // Close the band clip's q.
        page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
        return pagesMade;
    }

    /// <summary>One positioned span on the band: its face, its seat and the text it shows.</summary>
    private static bool RenderFixedLayoutSpan(Core.PdfDictionary docFontDict, System.Globalization.CultureInfo inv, FixedPageDiv div, FixedSpan s, Page page, StringBuilder sb, double yOff, double band, double cum, double bandH, double pageH, FixedSheetModel model)
    {
        if (s.Color is null) return true;   // selection-only text; the raster carries the pixels
        // Skip spans clearly outside this band (the clip still guards stragglers).
        // s.Top is div-local; bands live in continuous-flow coordinates. A dialect with a
        // widow rule seats each LINE on its own band, so it does its own skipping.
        var spanTop = cum + s.Top;
        if (model.FitBottomPt <= 0
            && (spanTop + s.FontSize * 1.4 < band * bandH || spanTop > (band + 1) * bandH))
            return true;
        var res = page.Dict.Get("Resources") as Core.PdfDictionary;
        var fontDict = res?.Get("Font") as Core.PdfDictionary ?? docFontDict;

        sb.Append("BT ");
        sb.Append($"{s.Color.Value.r.ToString("F3", inv)} {s.Color.Value.g.ToString("F3", inv)} {s.Color.Value.b.ToString("F3", inv)} rg ");
        if (s.LetterSpacing != 0)
            sb.Append($"{s.LetterSpacing.ToString("F3", inv)} Tc ");

        var spanLines = s.Lines;
        for (var li = 0; li < spanLines.Length; li++)
        {
            var lineText = spanLines[li];
            if (lineText.Trim().Length == 0) continue;
            if (FixedLineSeat(s, model, li, cum, bandH, band) is not { } lineTop) continue;
            var y = pageH - (lineTop + (s.BaselineDropPt > 0 ? s.BaselineDropPt : s.FontSize));
            var runX = FixedMarginLeftPt + s.Left;

            // Split into runs by resolved face: the document's own @font-face
            // program where it has the glyph, else the mapped system face,
            // else the script fallback — per codepoint.
            var i = 0;
            while (i < lineText.Length)
            {
                int cp0 = lineText[i];
                if (char.IsHighSurrogate(lineText[i]) && i + 1 < lineText.Length && char.IsLowSurrogate(lineText[i + 1]))
                    cp0 = char.ConvertToUtf32(lineText[i], lineText[i + 1]);
                var (runOwn, runSys) = s.FaceFor(cp0);
                var runSb = new StringBuilder();
                double runW = 0;
                while (i < lineText.Length)
                {
                    int cp = lineText[i];
                    var cpLen = 1;
                    if (char.IsHighSurrogate(lineText[i]) && i + 1 < lineText.Length && char.IsLowSurrogate(lineText[i + 1]))
                    {
                        cp = char.ConvertToUtf32(lineText[i], lineText[i + 1]);
                        cpLen = 2;
                    }
                    var (own, sys) = s.FaceFor(cp);
                    if ((!ReferenceEquals(own, runOwn)
                         || !sys.Equals(runSys, StringComparison.OrdinalIgnoreCase))
                        && lineText[i] != ' ') break;
                    var piece = lineText.Substring(i, cpLen);
                    runSb.Append(piece);
                    runW += MeasureSpanLine(s, piece);
                    i += cpLen;
                    // Word-spacing cannot ride the content stream: Tw applies
                    // only to single-byte code 32, and these runs are shown
                    // through composite (Type0) fonts, so a space inside a run
                    // advances by its bare glyph width however the span is
                    // styled. The run therefore ENDS at the space, and the next
                    // one is positioned at the accumulated x - which carries the
                    // word-spacing, because runW is measured with it. Without
                    // this the drawn line is wider than the measured one by the
                    // whole word-spacing budget (a 24-space line at
                    // -0.024em ran 5.7 pt long, and the sheet with it).
                    if (s.WordSpacing != 0 && piece == " ") break;
                }
                var runText = runSb.ToString();
                var runTtf = runOwn?.Ttf ?? PosFace(runSys).ttf;
                var runBase = runOwn is not null ? "DocFace" + runOwn.Id : runSys;
                if (runTtf is not null)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, runTtf,
                        runBase, runText, stripSpacesInBaseFont: true);
                    sb.Append($"/{rn} {s.FontSize.ToString("F2", inv)} Tf ");
                    sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
                    sb.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ");
                }
                else
                {
                    sb.Append($"/F1 {s.FontSize.ToString("F2", inv)} Tf ");
                    sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
                    sb.Append($"({EscapePdfString(runText)}) Tj ");
                }
                runX += runW;
            }
        }
        if (s.LetterSpacing != 0) sb.Append("0 Tc ");
        sb.AppendLine("ET");
        return true;
    }
}
