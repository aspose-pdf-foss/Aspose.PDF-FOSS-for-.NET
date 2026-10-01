using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{

    // The em-compensation dialect breaks a line to the next page when its
    // TOP plus a fixed line-box reserve overruns the content band:
    // a line at rel-top
    // 678.24 pt stays on a 698 pt band and 678.48 breaks, bracketing the
    // reserve in [19.52, 19.76) pt. The default dialect keeps its
    // baseline rule.
    private const double EmGridPageBreakReservePt = 19.6;
// STL positioned-page helpers: the invariant number parser and the page index of a run.
    private static double Num(StlPositionedState sp, string s) => double.Parse(s,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture);

    private static int PageOf(StlPositionedState sp, StlRun r) => (int)Math.Floor(Math.Max(0,
        (sp.emCompensationGrid ? r.TopPt + EmGridPageBreakReservePt : r.Baseline)
        - 1e-6) / sp.band);

    /// <summary>Render one positioned page: its background image, then every run seated at its em-grid or absolute position with its face, size and colour.</summary>
    private static bool RenderStlPage(StlPositionedState sp, int p)
    {
        sp.runs = sp.pagesRuns[p];
        sp.kMax = 0;
        foreach (var r in sp.runs)
            sp.kMax = Math.Max(sp.kMax, PageOf(sp, r));

        sp.outPages = new Page[sp.kMax + 1];
        for (var k = 0; k <= sp.kMax; k++)
        {
            var pg = sp.doc.Pages.Add(sp.pageW, sp.pageH);
            EnsureFonts(pg, sp.docFontDict);
            sp.outPages[k] = pg;
            if (sp.pagesImage[p] is { } bg)
            {
                // The page background keeps its box size at the 6pt content
                // inset; each output page shows its band slice of it (clipped to
                // the content band, so the raster pages along
                // with the text).
                var bgLeft = sp.ml + sp.stlContentPad;
                var top = sp.pageH - sp.mt - sp.stlContentPad + k * sp.band;
                var clip = Compat.Format(System.Globalization.CultureInfo.InvariantCulture,
                    $"q {bgLeft:F2} {sp.pageH - sp.mt - sp.stlContentPad - sp.band:F2} {bg.wPt:F2} {sp.band:F2} re W n\n");
                sp.outPages[k].AddContentStream(Encoding.ASCII.GetBytes(clip));
                try { sp.outPages[k].AddImage(bg.bytes, new Rectangle(bgLeft, top - bg.hPt, bgLeft + bg.wPt, top)); }
                catch { /* undecodable background: text-only re-import */ }
                sp.outPages[k].AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
            }
        }

        foreach (var r in sp.runs)
        {
            if (!RenderStlRun(sp, p, r)) break;
        }
        return true;
    }

    /// <summary>Parse one positioned page div: its background image and every positioned span into runs with their class properties resolved.</summary>
    private static bool ParseStlPage(StlPositionedState sp, string html, HtmlLoadOptions? options, int p)
    {
        sp.segStart = sp.pageDivs[p].Index;
        sp.segEnd = p + 1 < sp.pageDivs.Count ? sp.pageDivs[p + 1].Index : html.Length;
        sp.seg = html[sp.segStart..sp.segEnd];
        sp.runs = new List<StlRun>();

        sp.boxW = 0;
        sp.pdCls = Regex.Match(sp.pageDivs[p].Value, @"class=""(?<c>[^""]+)""");
        if (sp.pdCls.Success)
            foreach (var c in sp.pdCls.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (sp.classProps.TryGetValue(c, out var cpBox) && cpBox.WidthEm is { } weBox)
                {
                    sp.boxW = weBox * StlEmPt;
                    break;
                }

        foreach (Match dm in Regex.Matches(sp.seg,
            @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?""[^>]*>(?<body>.*?)</div>",
            RegexOptions.Singleline))
        {
            if (!ParseStlSpan(sp, dm)) break;
        }
        sp.pagesRuns.Add(sp.runs);

        sp.bg = null;
        sp.img = Regex.Match(sp.seg, @"<img src=""(?<src>[^""]+)""");
        if (sp.img.Success)
        {
            var bytes = LoadConverterImage(DecodeEntities(sp.img.Groups["src"].Value), options);
            if (bytes is not null && !IsSvgBytes(bytes))
            {
                double bw = 0, bh = 0;
                var pd = Regex.Match(sp.pageDivs[p].Value, @"class=""(?<c>[^""]+)""");
                if (pd.Success)
                    foreach (var c in pd.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        if (sp.classProps.TryGetValue(c, out var cp) && cp.WidthEm is { } we && cp.HeightEm is { } he)
                        {
                            bw = we * StlEmPt; bh = he * StlEmPt;
                            break;
                        }
                if (bw > 0 && bh > 0) sp.bg = (bytes, bw, bh);
            }
        }
        sp.pagesImage.Add(sp.bg);
        return true;
    }

    /// <summary>One positioned span: its left/top position, class properties, face, size, colour and text become a run of the page.</summary>
    private static bool ParseStlSpan(StlPositionedState sp, Match dm)
    {
        sp.leftPt = Num(sp, dm.Groups["l"].Value) * StlEmPt;
        sp.topPt = Num(sp, dm.Groups["t"].Value) * StlEmPt;
        sp.x = sp.leftPt;
        sp.sentinelAdv = 0.0;
        sp.lineHasBox = false;
        sp.lsBudget = 0.0;
        sp.gridBudget = sp.leftPt;

        sp.spanMatches = Regex.Matches(dm.Groups["body"].Value,
            @"<span class=""(?<scls>[^""]*)""(?:\s+style=""(?<sst>[^""]*)"")?[^>]*>(?<stext>.*?)</span>",
            RegexOptions.Singleline);
        for (var spanIdx = 0; spanIdx < sp.spanMatches.Count; spanIdx++)
        {
            if (!ParseStlSpanRun(sp, spanIdx)) break;
        }
        // All page CONTENT (lines and the background
        // raster alike) is offset by a constant 6pt (0.5em of the 12pt root) right and
        // down inside the margins; the page width then grows to
        // max(default, ML + 6 + line end + MR). The line ends after its trailing
        // space and word-spacing — only the sentinel nbsp hangs outside the
        // budget. (Stripping the whole trailing run sits 15 pt under the
        // correct 741/747 pt for the same file; keeping the
        // sentinel overshoots the sheet the other way.)
        if (sp.lineHasBox)
        {
            var lineEnd = sp.emCompensationGrid ? sp.gridBudget : sp.x - sp.sentinelAdv;
            sp.maxRight = Math.Max(sp.maxRight,
                (sp.boxW > 0 ? Math.Min(lineEnd, sp.boxW) : lineEnd) + sp.stlContentPad);
        }
        return true;
    }

    /// <summary>One run drawn on its page: the em-grid or absolute seat, the font resource and the text show.</summary>
    private static bool RenderStlRun(StlPositionedState sp, int p, StlRun r)
    {
        var rr = new StlRunRenderState();
        rr.sp = sp;
        rr.p = p;
        rr.r = r;
        rr.k = PageOf(rr.sp, rr.r);
        rr.pg = rr.sp.outPages[rr.k];
        rr.x = rr.sp.ml + rr.sp.stlContentPad + rr.r.LeftPt;
        rr.y = rr.sp.pageH - rr.sp.mt - rr.sp.stlContentPad - (rr.r.Baseline - rr.k * rr.sp.band);

        rr.faceName = rr.r.Family;
        rr.runUnion = null;
        if (CoveringStlFace(rr.sp.htmlFaces, rr.r.Family, rr.r.Text) is { Parser: not null } hface)
        {
            rr.faceTtf = hface.Ttf; rr.faceParser = hface.Parser; rr.faceUpm = hface.Upm;
        }
        else if ((rr.runUnion = UnionStlSegments(rr.sp.htmlFaces, rr.r.Family, rr.r.Text)) is not null)
        {
            // Sibling subsets jointly cover the line; each piece below embeds
            // its own program. The primary face carries the space advance.
            rr.faceTtf = rr.runUnion[0].face.Ttf;
            rr.faceParser = rr.runUnion[0].face.Parser;
            rr.faceUpm = rr.runUnion[0].face.Upm;
        }
        else
        {
            var face = PosFace(rr.r.Family);
            if (face.ttf is null) { rr.faceName = "Times New Roman"; face = PosFace(rr.faceName); }
            rr.faceTtf = face.ttf; rr.faceParser = face.parser; rr.faceUpm = face.upm;
        }
        if (rr.faceTtf is null) return true;

        rr.res = rr.pg.Dict.Get("Resources") as Core.PdfDictionary;
        rr.fontDict = rr.res?.Get("Font") as Core.PdfDictionary ?? rr.sp.docFontDict;

        rr.sb = new StringBuilder();
        rr.sb.Append("BT ");
        rr.cr = System.Convert.ToInt32(rr.r.Color.Substring(1, 2), 16) / 255.0;
        rr.cg = System.Convert.ToInt32(rr.r.Color.Substring(3, 2), 16) / 255.0;
        rr.cb = System.Convert.ToInt32(rr.r.Color.Substring(5, 2), 16) / 255.0;
        rr.sb.Append($"{rr.cr.ToString("0.###", rr.sp.inv)} {rr.cg.ToString("0.###", rr.sp.inv)} {rr.cb.ToString("0.###", rr.sp.inv)} rg ");

        rr.segments = new List<(string Text, char Sep)>();
        if (rr.r.WordSpacingPt != 0)
        {
            var segB = new StringBuilder();
            foreach (var ch in rr.r.Text)
            {
                if (ch == ' ') { rr.segments.Add((segB.ToString(), ' ')); segB.Clear(); continue; }
                if (segB.Length > 0 && StlIdeograph(segB[^1]) && StlIdeograph(ch))
                { rr.segments.Add((segB.ToString(), 'c')); segB.Clear(); }
                segB.Append(ch);
            }
            rr.segments.Add((segB.ToString(), '\0'));
        }
        else
            rr.segments.Add((rr.r.Text, '\0'));
        rr.segX = rr.x;
        for (var si = 0; si < rr.segments.Count; si++)
        {
            if (!RenderStlSegment(rr, si)) break;
        }
        rr.sb.AppendLine("ET");
        rr.pg.AddContentStream(Encoding.ASCII.GetBytes(rr.sb.ToString()));
        return true;
    }

    /// <summary>One inner span of a positioned line: its class properties, face, size, colour and text become a run at the walked pen, with the em-grid budget and sentinel advances.</summary>
    private static bool ParseStlSpanRun(StlPositionedState sp, int spanIdx)
    {
        var sm = sp.spanMatches[spanIdx];
        var isLastSpan = spanIdx == sp.spanMatches.Count - 1;
        var raw = DecodeEntities(Regex.Replace(sm.Groups["stext"].Value, "<[^>]+>", ""));
        if (raw.Length == 0) return true;
        sp.lineHasBox = true;

        var run = new StlRun { LeftPt = sp.x, TopPt = sp.topPt };
        double fsEm = 1.0; var fsSet = false;
        string? family = null, color = null;
        double? lsEm = null;
        foreach (var c in sm.Groups["scls"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!sp.classProps.TryGetValue(c, out var cp)) continue;
            if (!fsSet && cp.FontSizeEm is { } fe) { fsEm = fe; fsSet = true; }
            family ??= cp.Family;
            color ??= cp.Color;
            lsEm ??= cp.LetterSpacingEm;
        }
        run.FontSizePt = fsEm * StlEmPt;
        if (family is not null) run.Family = family;
        if (color is not null) run.Color = color;
        // letter-spacing / word-spacing em are relative to the span's own font size
        if (lsEm is { } l0) run.LetterSpacingPt = l0 * run.FontSizePt;
        var ws = Regex.Match(sm.Groups["sst"].Value ?? "", @"word-spacing:\s*(-?[\d.]+)em");
        if (ws.Success) run.WordSpacingPt = Num(sp, ws.Groups[1].Value) * run.FontSizePt;

        // The raw text (nbsp sentinel included) measures in the stl_
        // model — nbsp advances as the space glyph and takes a
        // letter-spacing slot but no word-spacing slot; the drawn text
        // drops the trailing sentinel, keeps interior spacing verbatim.
        var measureText = raw.Replace(' ', ' ');
        run.WidthPt = MeasureStlRun(run, raw, sp.htmlFaces);
        run.Text = measureText.TrimEnd();
        if (run.Text.Length > 0) sp.runs.Add(run);
        sp.x += run.WidthPt;
        sp.lsBudget += run.LetterSpacingPt * raw.Length;
        // ── The em-compensation dialect's own sheet budget ──
        // The budget sums, per span:
        //   · glyph advances of the visible text INCLUDING its trailing
        //     space, plus a space advance for the nbsp sentinel;
        //   · letter-spacing after every character EXCEPT the trailing space
        //     and the sentinel;
        //   · word-spacing on every space AND every HYPHEN — except that a
        //     span-final space with a further span behind it on the same
        //     line takes none (the sheet width only comes out right
        //     with that one space uncredited).
        // U+00A0 - the line-final sentinel the exporter appends.
        const char Nbsp = ' ';
        var visible = raw.TrimEnd(Nbsp);
        var sentinelChars = raw.Length - visible.Length;
        var gridAdv = MeasureStlAdvOnly(run, visible, sp.htmlFaces);
        if (sentinelChars > 0)
            gridAdv += MeasureStlAdvOnly(run, new string(' ', sentinelChars), sp.htmlFaces);
        var lsCarriers = visible.TrimEnd(' ').Length;
        var wsSlots = visible.Count(ch => ch == ' ' || ch == '-');
        if (!isLastSpan && visible.EndsWith(' ')) wsSlots--;
        // The IE-model layout charges word-spacing at every
        // boundary between two adjacent full-em CJK characters, exactly
        // as at a drawn space (a spread heading of 8 ideographs
        // and 2 spaces takes ws on all 8 slots — 6 ideograph pairs +
        // the 2 spaces).
        for (var ci2 = 1; ci2 < visible.Length; ci2++)
            if (StlIdeograph(visible[ci2 - 1]) && StlIdeograph(visible[ci2]))
                wsSlots++;
        sp.gridBudget += gridAdv + run.LetterSpacingPt * lsCarriers
            + run.WordSpacingPt * wsSlots;
        // The line-final sentinel &nbsp; dangles beyond the sheet's
        // width budget; the trailing space and its word-spacing stay in.
        // The sentinel advances as the space glyph plus letter-spacing and
        // takes no word-spacing slot (which MeasureStlRun adds for ' ').
        sp.sentinelAdv = raw.EndsWith('\u00A0')
            ? MeasureStlRun(run, " ", sp.htmlFaces) - run.WordSpacingPt
            : 0.0;
        return true;
    }
}
