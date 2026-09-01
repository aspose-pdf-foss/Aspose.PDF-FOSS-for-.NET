using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private double RenderLayoutTable(Table lt, double originX, double boxW, double startY,
        FlowLayout flow, double marginLeft, HashSet<Table> renderedTables, bool measureOnly = false)
    {
        var originLeft = originX >= 0 ? originX : marginLeft;
        // The layout table's own cell padding sits above and below every
        // block it places — the markup's `cellpadding` nests, so each level
        // of table inset adds its own.
        var lpadTop = lt.DefaultCellPadding?.Top ?? 0;
        var lpadBottom = lt.DefaultCellPadding?.Bottom ?? 0;
        var y = startY;
        foreach (var lrow in lt.Rows)
        {
            var cellCount = lrow.Cells.Count;
            if (cellCount == 0) continue;
            var widths = new double[cellCount];
            var declared = 0.0;
            var undeclared = 0;
            for (var c = 0; c < cellCount; c++)
            {
                var w = lrow.Cells.At(c).Width;
                widths[c] = w > 0 ? w : 0;
                if (w > 0) declared += w; else undeclared++;
            }
            if (undeclared > 0)
            {
                var share = Math.Max(0, boxW - declared) / undeclared;
                for (var c = 0; c < cellCount; c++)
                    if (widths[c] <= 0) widths[c] = share;
            }

            // how tall is this row? measure before placing, so a row
            // that no longer fits can move to a fresh page whole
            if (originX < 0 && !measureOnly)
            {
                var need = 0.0;
                for (var c = 0; c < cellCount; c++)
                {
                    var mcell = lrow.Cells.At(c);
                    var mh = 0.0;
                    foreach (var mp in mcell.Paragraphs)
                    {
                        if (mp is Table mt)
                        {
                            // a cell holding a table of tables is as tall as
                            // placing it would make it — measure it the same way
                            if (HasNestedTables(mt))
                            {
                                mh += RenderLayoutTable(mt, originLeft, widths[c], y, flow, marginLeft, renderedTables, measureOnly: true);
                                continue;
                            }
                            mt.FlowLeftOffset = originLeft;
                            mt.BuildMultiPage(flow.CurrentPage, y, flow.BottomMargin, measureOnly: true);
                            mh += mt.LastRenderedHeight;
                        }
                        else if (mp is Text.TextFragment mtf)
                            mh += Converters.HtmlToPdfConverter.FaceLineHeight("Helvetica",
                                mtf.TextState.FontSize > 0 ? mtf.TextState.FontSize : 12);
                    }
                    need = Math.Max(need, mh);
                }
                if (need > 0) need += lpadTop + lpadBottom;
                if (need > 0 && y - need < flow.BottomMargin
                    && need <= flow.ContentTop - flow.BottomMargin)
                {
                    flow.ForceNewPage();
                    y = flow.CurrentY;
                }
            }

            var rowTop = y - lpadTop;
            var rowAdvance = 0.0;
            var cx = originLeft;
            for (var c = 0; c < cellCount; c++)
            {
                var lcell = lrow.Cells.At(c);
                var cy = rowTop;
                foreach (var lp in lcell.Paragraphs)
                {
                    if (lp is Table lin)
                    {
                        if (!measureOnly) renderedTables.Add(lin);
                        lin.HtmlEngineMetrics = true;
                        lin.HtmlLayoutWrap = true;
                        // a table that itself only places other tables keeps placing them
                        if (HasNestedTables(lin))
                        {
                            cy -= RenderLayoutTable(lin, cx, widths[c], cy, flow, marginLeft, renderedTables, measureOnly);   // nested: no paging
                            continue;
                        }
                        // a floated cell resolves its percentage a second
                        // time: the region is half the cell, hung on its
                        // right edge, and the table may overflow past it
                        var region = lcell.Alignment == HorizontalAlignment.Right
                            ? widths[c] / 2 : widths[c];
                        Converters.HtmlToPdfConverter.ApplyAutoWidths(lin, region, fill: !lin.HtmlAutoWidth);
                        Converters.HtmlToPdfConverter.ApplyAutoRowHeights(lin);
                        var lx = lcell.Alignment == HorizontalAlignment.Right
                            ? cx + widths[c] - region : cx;
                        lin.FlowLeftOffset = lx;
                        var lcontents = lin.BuildMultiPage(flow.CurrentPage, cy, flow.BottomMargin,
                            measureOnly: measureOnly);
                        if (!measureOnly)
                        {
                            if (lcontents.Count > 0) flow.InjectContentAtCursor(lcontents[0]);
                            if (lin.LastGraphDraws.Count > 0)
                                foreach (var gc in lin.LastGraphDraws[0])
                                    flow.InjectContentAtCursor(gc);
                            if (!flow.HasOverflowed && lin.LastImageDraws.Count > 0)
                                foreach (var (data, rect) in lin.LastImageDraws[0])
                                    flow.CurrentPage.AddImage(data, rect);
                        }
                        cy -= lin.LastRenderedHeight;
                    }
                    else if (lp is Text.TextFragment ltf
                             && !string.IsNullOrWhiteSpace(ltf.Text))
                    {
                        var lfs = ltf.TextState.FontSize > 0 ? ltf.TextState.FontSize : 12;
                        var lface = ltf.TextState.IsBold ? "Helvetica-Bold" : "Helvetica";
                        if (!measureOnly)
                        {
                            var lres = Table.RegisterFont(flow.CurrentPage, lface);
                            var lb = new Content.ContentStreamBuilder();
                            lb.SaveState();
                            lb.BeginText().SetFont(lres, lfs)
                              .MoveTextPosition(cx, cy - lfs)
                              .ShowText(ltf.Text!).EndText();
                            lb.RestoreState();
                            flow.InjectContentAtCursor(lb.Build());
                        }
                        cy -= Converters.HtmlToPdfConverter.FaceLineHeight(lface, lfs);
                    }
                    else if (lp is Text.TextFragment lws)
                        // A blank (or &nbsp;-only) cell is still a line box:
                        // it takes its own font's line height, not a nominal one.
                        cy -= Converters.HtmlToPdfConverter.FaceLineHeight("Helvetica",
                            lws.TextState.FontSize > 0 ? lws.TextState.FontSize : 12);
                }
                rowAdvance = Math.Max(rowAdvance, rowTop - cy);
                cx += widths[c];
            }
            y -= lpadTop + rowAdvance + lpadBottom;
        }
        return startY - y;
    }

    // Render a real HTML <table> as a generator Table at the flow cursor,
    // paginating like a page-level Table paragraph (same logic as the
    // `para is Table` branch below).
    private void RenderHtmlTable(Table t, FlowLayout flow, Page page, double marginLeft, double marginTop,
        List<(byte[] content, double width, double height)> overflowPages,
        Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages)
    {
        var tablePage = flow.CurrentPage;
        t.FlowLeftOffset = marginLeft;
        var spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;

        // Page-break-before: if the whole table doesn't fit in the space left
        // on the current page but would fit on a fresh one, move it to the next
        // page (keeps a table together — the common HTML expectation). Measure
        // its single-page height from the content top first.
        t.BuildMultiPage(tablePage, flow.ContentTop, flow.BottomMargin, measureOnly: true);
        var tableH = t.LastRenderedHeight;
        var avail = flow.CurrentY - flow.BottomMargin;
        var pageBudget = flow.ContentTop - flow.BottomMargin;
        // …but the form-grid dialect SPLITS a section table
        // instead (the band row stays on the page foot,
        // the header/data rows continue overleaf).
        if (tableH > avail + 0.5 && tableH <= pageBudget + 0.5
            && flow.CurrentY < flow.ContentTop - 0.5
            && !t.HonorCellTtfFaces)
            flow.ForceNewPage();

        var pageContents = t.BuildMultiPage(tablePage, flow.CurrentY, flow.BottomMargin, spillTopMargin,
            contentFlow: true);
        var tableImages = t.LastImageDraws;
        var tableGraphs = t.LastGraphDraws;
        // Inject the first slice at the flow's CURRENT page position (the start
        // page, or the current overflow buffer once the flow has page-broken) —
        // NOT directly on the start page, which is where the cursor no longer is.
        flow.InjectContentAtCursor(pageContents[0]);
        if (tableGraphs.Count > 0)
            foreach (var gc in tableGraphs[0])
                flow.InjectContentAtCursor(gc);
        // Cell images: drawn on the live start page (only correct before the flow
        // overflows — overflowed cell images are rare and out of scope here).
        if (!flow.HasOverflowed && tableImages.Count > 0)
            foreach (var (data, rect) in tableImages[0])
                tablePage.AddImage(data, rect);
        if (pageContents.Count == 1)
        {
            flow.AdvanceY(t.LastRenderedHeight);
        }
        else
        {
            for (var pi = 1; pi < pageContents.Count - 1; pi++)
            {
                if (pi < tableImages.Count && tableImages[pi].Count > 0)
                    overflowImages[overflowPages.Count] = tableImages[pi];
                overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
            }
            var lastIdx = pageContents.Count - 1;
            var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], t.LastPageEndY);
            if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                overflowImages[lastSlot] = tableImages[lastIdx];
        }
    }

    private void RenderUaSerifTable(string chunk, double uaBoxPt, FlowLayout flow, Page page,
        double marginLeft)
    {
        const System.Text.RegularExpressions.RegexOptions UaRx =
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Singleline;
        var twm = System.Text.RegularExpressions.Regex.Match(chunk,
            @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+)(?![\d%])", UaRx);
        var tableW = twm.Success
            ? double.Parse(twm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : uaBoxPt;
        var zeroPad = System.Text.RegularExpressions.Regex.IsMatch(chunk,
            @"<table\b[^>]*\bcellpadding\s*=\s*[""']?0[""']?", UaRx);
        var pad = zeroPad ? 0.0 : UaTdPadPt;

        // columns: declared pt widths from the colgroup, else
        // percentage widths off the first row's cells
        var colWs = new List<double>();
        foreach (System.Text.RegularExpressions.Match cm in
            System.Text.RegularExpressions.Regex.Matches(chunk,
                @"<col\b[^>]*width\s*:\s*([\d.]+)pt", UaRx))
            colWs.Add(double.Parse(cm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture));
        if (colWs.Count == 0)
            foreach (System.Text.RegularExpressions.Match cm in
                System.Text.RegularExpressions.Regex.Matches(chunk,
                    @"<td\b[^>]*width\s*:\s*([\d.]+)%", UaRx))
                colWs.Add(tableW * double.Parse(cm.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) / 100.0);
        if (colWs.Count == 0) return;

        // faces: css-named family with real metrics, the
        // fallback face for glyphs the primary lacks, and the
        // UA serif default drawn as the base-14 Times
        var faces = new Dictionary<string,
            (byte[] Ttf, string Name, Text.GlyphOutlineParser Gp, Text.TrueTypeParser Tp)>();
        (byte[], string, Text.GlyphOutlineParser, Text.TrueTypeParser)? Face(string family)
        {
            var key = family.ToLowerInvariant();
            if (faces.TryGetValue(key, out var have)) return have;
            var name = System.Globalization.CultureInfo.InvariantCulture
                .TextInfo.ToTitleCase(key);
            // the repository's ttf data drops the legacy kern
            // table wrap measurement relies on — read the system
            // file itself when the face is a known one
            var file = key switch
            {
                "calibri" => "calibri.ttf",
                "times new roman" => "times.ttf",
                "microsoft sans serif" => "micross.ttf",
                _ => null,
            };
            byte[]? ttf = null;
            if (file is not null)
                try
                {
                    ttf = System.IO.File.ReadAllBytes(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file));
                }
                catch { ttf = null; }
            if (ttf is null)
                try { ttf = Text.FontRepository.GetTtfData(name); }
                catch { return null; }
            if (ttf is null) return null;
            try
            {
                var tp2 = new Text.TrueTypeParser(ttf);
                tp2.Parse();
                var got = (ttf, name, new Text.GlyphOutlineParser(ttf), tp2);
                faces[key] = got;
                return got;
            }
            catch { return null; }
        }
        // css line box: whole-css-px rounded hhea line height
        double CssBox(Text.TrueTypeParser t, double s) =>
            0.75 * Math.Floor((t.Ascent + Math.Abs(t.Descent) + t.LineGap)
                * (s * 96.0 / 72.0) / t.UnitsPerEm + 0.5);
        double SeatIn(Text.TrueTypeParser t, double s, double box) =>
            t.UsWinAscent * s / t.UnitsPerEm
            + (box - (t.UsWinAscent + t.UsWinDescent) * s / t.UnitsPerEm) / 2;
        // the paste's U+FFFD stays as-is; it draws
        // through the system fallback face, which carries the
        // replacement-character glyph
        const char UaFallbackChar = '�';
        const string UaFallbackFamily = "Microsoft Sans Serif";

        double GlyphAdv(Text.GlyphOutlineParser gp, int gid, double s) =>
            gp.GetAdvanceWidth(gid) * s / (gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0);
        double StyledWidth(string t, Text.GlyphOutlineParser gp,
            Text.GlyphOutlineParser? gpFb, double s)
        {
            double w = 0;
            var prev = -1;
            foreach (var c in t)
            {
                if (c == UaFallbackChar && gpFb is not null)
                {
                    var gf = gpFb.CMap.TryGetValue(c, out var g2) ? g2 : 0;
                    w += GlyphAdv(gpFb, gf, s);
                    prev = -1;
                    continue;
                }
                var gid = gp.CMap.TryGetValue(c, out var g) ? g : 0;
                if (prev >= 0)
                    w += gp.GetKernAdjustment(prev, gid) * s
                         / (gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0);
                w += GlyphAdv(gp, gid, s);
                prev = gid;
            }
            return w;
        }

        var fontDict = Table.ResolvePageFontDict(flow.CurrentPage);
        var uaTimes2 = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
        double TimesWidth(string t)
        {
            try
            {
                return Text.FontRepository.TryFindFont("Times-Roman")
                    ?.MeasureString(t, UaSerifPt) ?? t.Length * UaSerifPt * 0.5;
            }
            catch { return t.Length * UaSerifPt * 0.5; }
        }

        var tb = new Content.ContentStreamBuilder();
        tb.SaveState();
        var topD = page.Height - flow.CurrentY;   // top-down cursor
        var totalH = 0.0;

        foreach (System.Text.RegularExpressions.Match rm in
            System.Text.RegularExpressions.Regex.Matches(chunk,
                @"<tr(?<a>[^>]*)>(?<in>.*?)</tr>", UaRx))
        {
            var declH = 0.0;
            var dhm = System.Text.RegularExpressions.Regex.Match(
                rm.Groups["a"].Value, @"height\s*:\s*([\d.]+)pt", UaRx);
            if (dhm.Success)
                declH = double.Parse(dhm.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);

            var cells = new List<(string Text, double Fs, string? Family,
                Color? Bg, Color? EdgeColor, bool[] Solid,
                List<string> Lines, List<double> Boxes)>();
            foreach (System.Text.RegularExpressions.Match dm in
                System.Text.RegularExpressions.Regex.Matches(rm.Groups["in"].Value,
                    @"<td(?<a>[^>]*)>(?<in>.*?)</td>", UaRx))
            {
                var attrs = dm.Groups["a"].Value;
                var text = System.Text.RegularExpressions.Regex.Replace(
                    HtmlFragment.StripHtmlTags(dm.Groups["in"].Value),
                    @"\s+", " ").Trim();
                var fs = UaSerifPt;
                var fsm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"font-size\s*:\s*([\d.]+)pt", UaRx);
                if (fsm.Success)
                    fs = double.Parse(fsm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                string? family = null;
                var ffm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"font-family\s*:\s*([^;""']+)", UaRx);
                if (ffm.Success) family = ffm.Groups[1].Value.Trim();
                Color? bg = null;
                var bgm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"background[^;]*?(#[0-9a-f]{6})", UaRx);
                if (bgm.Success)
                    bg = Converters.HtmlToPdfConverter.ParseCssColor(bgm.Groups[1].Value);
                // border-style lists top right bottom left
                var solid = new bool[4];
                var bsm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"border-style\s*:\s*([^;]+)", UaRx);
                if (bsm.Success)
                {
                    var toks = bsm.Groups[1].Value.Trim().Split(' ',
                        StringSplitOptions.RemoveEmptyEntries);
                    for (var e = 0; e < 4; e++)
                        solid[e] = string.Equals(
                            toks[Math.Min(e, toks.Length - 1)], "solid",
                            StringComparison.OrdinalIgnoreCase);
                    if (toks.Length == 2) { solid[2] = solid[0]; solid[3] = solid[1]; }
                }
                Color? edgeColor = null;
                var bcm = System.Text.RegularExpressions.Regex.Match(attrs,
                    @"border-color\s*:\s*(#[0-9a-f]{6})", UaRx);
                if (bcm.Success)
                    edgeColor = Converters.HtmlToPdfConverter.ParseCssColor(bcm.Groups[1].Value);
                cells.Add((text, fs, family, bg, edgeColor, solid,
                    new List<string>(), new List<double>()));
            }
            if (cells.Count == 0) continue;

            // wrap each cell and size its line boxes
            var rowContentH = 0.0;
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var cell = cells[ci];
                var colW = colWs[Math.Min(ci, colWs.Count - 1)];
                var leftInset = cell.Solid[3] ? UaTableEdgePt : 0.0;
                var availW = colW - leftInset - 2 * pad;
                var styled = cell.Family is not null && Face(cell.Family) is not null;
                var fb = Face(UaFallbackFamily);
                // lines break by the real face's
                // metrics even where we draw the base-14 serif —
                // measure with the system TTF when it resolves
                var measureFace = styled
                    ? Face(cell.Family!)
                    : Face("Times New Roman");
                double WordW(string w)
                {
                    if (measureFace is { } mf)
                        return StyledWidth(w, mf.Item3, fb?.Item3, cell.Fs);
                    double mw = 0;
                    foreach (System.Text.RegularExpressions.Match sm in
                        System.Text.RegularExpressions.Regex.Matches(w,
                            $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                        mw += sm.Value[0] == UaFallbackChar && fb is not null
                            ? StyledWidth(sm.Value, fb.Value.Item3, null, cell.Fs)
                            : TimesWidth(sm.Value);
                    return mw;
                }
                var cur = "";
                foreach (var w in cell.Text.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    var probe = cur.Length == 0 ? w : cur + " " + w;
                    if (cur.Length > 0 && WordW(probe) > availW)
                    { cell.Lines.Add(cur); cur = w; }
                    else cur = probe;
                }
                if (cur.Length > 0) cell.Lines.Add(cur);
                foreach (var ln in cell.Lines)
                {
                    double box;
                    if (styled)
                    {
                        var f = Face(cell.Family!)!.Value;
                        box = CssBox(f.Item4, cell.Fs);
                        if (ln.IndexOf(UaFallbackChar) >= 0 && fb is not null)
                            box = Math.Max(box, CssBox(fb.Value.Item4, cell.Fs));
                    }
                    else box = UaSerifPitchPt;
                    cell.Boxes.Add(box);
                }
                var sum = 0.0;
                foreach (var b in cell.Boxes) sum += b;
                rowContentH = Math.Max(rowContentH, sum);
                cells[ci] = cell;
            }

            var edged = false;
            foreach (var c in cells) if (c.Solid[0] || c.Solid[2]) edged = true;
            var rowH = Math.Max(
                declH > 0 ? declH + (edged ? 2 * UaTableEdgePt : 0) : 0,
                rowContentH + 2 * pad);

            // paint: fills, then edges, then text
            var cellL = marginLeft;
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var cell = cells[ci];
                var colW = colWs[Math.Min(ci, colWs.Count - 1)];
                var cellR = cellL + colW;
                if (cell.Bg is { } bgc)
                    tb.SetFillColor(bgc.R / 255.0, bgc.G / 255.0, bgc.B / 255.0)
                      .Rectangle(cellL, page.Height - (topD + rowH), colW, rowH)
                      .Fill();
                if (cell.Solid[0])
                {
                    var ec = cell.EdgeColor ?? Color.Black;
                    tb.SetStrokeColor(ec.R / 255.0, ec.G / 255.0, ec.B / 255.0)
                      .SetLineWidth(UaTableEdgePt)
                      .MoveTo(cellL, page.Height - (topD + UaTableEdgePt / 2))
                      .LineTo(cellR, page.Height - (topD + UaTableEdgePt / 2))
                      .Stroke();
                }
                if (cell.Solid[2])
                    tb.SetStrokeColor(0, 0, 0).SetLineWidth(UaTableEdgePt)
                      .MoveTo(cellL, page.Height - (topD + rowH - UaTableEdgePt / 2))
                      .LineTo(cellR, page.Height - (topD + rowH - UaTableEdgePt / 2))
                      .Stroke();
                if (cell.Solid[3])
                    tb.SetStrokeColor(0, 0, 0).SetLineWidth(UaTableEdgePt)
                      .MoveTo(cellL + UaTableEdgePt / 2, page.Height - topD)
                      .LineTo(cellL + UaTableEdgePt / 2, page.Height - (topD + rowH))
                      .Stroke();

                if (cell.Lines.Count > 0)
                {
                    var styled = cell.Family is not null && Face(cell.Family) is not null;
                    var fb = Face(UaFallbackFamily);
                    // the fill colour above still governs — text is black
                    tb.SetFillColor(0, 0, 0);
                    var sum = 0.0;
                    foreach (var b in cell.Boxes) sum += b;
                    var contentTop = topD + (rowH - sum) / 2;
                    double pitch, seat1;
                    if (styled)
                    {
                        var f = Face(cell.Family!)!.Value;
                        pitch = CssBox(f.Item4, cell.Fs);
                        seat1 = SeatIn(f.Item4, cell.Fs, cell.Boxes[0]);
                    }
                    else
                    {
                        pitch = UaSerifPitchPt;
                        seat1 = UaSerifSeatPt + (cell.Boxes[0] - UaSerifPitchPt) / 2;
                    }
                    var textX = cellL + (cell.Solid[3] ? UaTableEdgePt : 0) + pad;
                    for (var li = 0; li < cell.Lines.Count; li++)
                    {
                        var baseD = contentTop + seat1 + li * pitch;
                        var py = page.Height - baseD;
                        var ln = cell.Lines[li];
                        if (styled)
                        {
                            var f = Face(cell.Family!)!.Value;
                            var x = textX;
                            // split at fallback-glyph boundaries
                            foreach (System.Text.RegularExpressions.Match sm in
                                System.Text.RegularExpressions.Regex.Matches(ln,
                                    $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                            {
                                var seg = sm.Value;
                                var isFb = seg[0] == UaFallbackChar && fb is not null;
                                var (ttf, name, gp, _) = isFb ? fb!.Value : f;
                                var (res, hex) = Text.Type0FontEmbedder.Embed(
                                    fontDict, ttf, name, seg,
                                    stripSpacesInBaseFont: true);
                                tb.BeginText().SetFont(res, cell.Fs)
                                  .MoveTextPosition(x, py);
                                if (StepKernAdjustments(seg, gp) is { } adj)
                                    tb.ShowTextHexKerned(hex, adj);
                                else tb.ShowTextHex(hex);
                                tb.EndText();
                                x += StyledWidth(seg, gp,
                                    isFb ? null : fb?.Item3, cell.Fs);
                            }
                        }
                        else
                        {
                            // base-14 Times for the serif default; the
                            // fallback glyph alone goes through its
                            // embedded face
                            var x = textX;
                            foreach (System.Text.RegularExpressions.Match sm in
                                System.Text.RegularExpressions.Regex.Matches(ln,
                                    $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                            {
                                var seg = sm.Value;
                                if (seg[0] == UaFallbackChar && fb is not null)
                                {
                                    var (ttf, name, gp, _) = fb.Value;
                                    var (res, hex) = Text.Type0FontEmbedder.Embed(
                                        fontDict, ttf, name, seg,
                                        stripSpacesInBaseFont: true);
                                    tb.BeginText().SetFont(res, cell.Fs)
                                      .MoveTextPosition(x, py)
                                      .ShowTextHex(hex).EndText();
                                    x += StyledWidth(seg, gp, null, cell.Fs);
                                }
                                else
                                {
                                    tb.BeginText().SetFont(uaTimes2, cell.Fs)
                                      .MoveTextPosition(x, py)
                                      .ShowText(seg).EndText();
                                    x += TimesWidth(seg);
                                }
                            }
                        }
                    }
                }
                cellL = cellR;
            }
            topD += rowH;
            totalH += rowH;
        }
        tb.RestoreState();
        flow.InjectContentAtCursor(tb.Build());
        flow.AdvanceY(totalH);
    }
}
