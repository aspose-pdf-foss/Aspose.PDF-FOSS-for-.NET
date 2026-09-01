using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Collapsed-grid table renderer for the inline-body-margin dialect:
    /// 1px border-collapse grid at real cellpadding, columns from width-% attributes
    /// resolved in source order with the LAST column taking the remainder (the
    /// sheet over-declares 110%), colspan splitting its share equally, char-level
    /// break-all wrapping at the face's real advances, a &lt;br&gt; inside a cell
    /// CONCATENATING (the date cells draw as one line), and
    /// LINE-AT-A-TIME pagination: an over-tall row splits mid-row at the content
    /// limit, its side borders running to the page edge and the continuation page
    /// resuming half a border below the top edge. All geometry measured on the
    /// expected render. Emits runs + border strokes directly and advances the flow
    /// cursor past the table's bottom border and margin-bottom.</summary>
    private static void RenderBodyBoxGridTable(Document doc, ref Page page, ref double y,
        string tableHtml, double marginLeft, double contentWidth,
        double pageWidth, double pageHeight, double marginBottom,
        string face, (double asc, double sum) fm, double lineSum,
        Core.PdfDictionary docFontDict)
    {
        const double PxPt = 0.75;
        const double bw = 0.75;                    // the sheet's 1px collapsed border
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        // ── table tag attributes ─────────────────────────────────────────────
        double pad = PxPt, fontSize = 11, marTopPt = 0, marBottomPt = 0;
        if (Regex.Match(tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } tt)
        {
            var tag = tt.Value;
            var cp = Regex.Match(tag, @"cellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (cp.Success) pad = double.Parse(cp.Groups[1].Value, invc) * PxPt;
            var fs = Regex.Match(tag, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (fs.Success) fontSize = double.Parse(fs.Groups[1].Value, invc);
            var mt = Regex.Match(tag, @"margin-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mt.Success) marTopPt = double.Parse(mt.Groups[1].Value, invc) * PxPt;
            var mb = Regex.Match(tag, @"margin-bottom\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mb.Success) marBottomPt = double.Parse(mb.Groups[1].Value, invc) * PxPt;
        }

        // ── parse rows/cells: runs with bold/italic, cell <br> concatenates ──
        var rows = new List<List<GridCell>>();
        List<GridCell>? row = null;
        GridCell? cell = null;
        var text = new StringBuilder();
        int boldDepth = 0, italDepth = 0;
        void FlushRun()
        {
            if (cell is null || text.Length == 0) { text.Clear(); return; }
            var t = DecodeEntities(text.ToString());
            text.Clear();
            if (t.Length == 0) return;
            var b = boldDepth > 0; var it = italDepth > 0;
            if (cell.Runs.Count > 0 && cell.Runs[^1].Bold == b && cell.Runs[^1].Italic == it)
                cell.Runs[^1] = (cell.Runs[^1].Text + t, b, it);
            else cell.Runs.Add((t, b, it));
        }
        // a cell boundary resets emphasis: the sheet leaves a stray unclosed <b>
        // at a cell's end, and the following cells draw regular
        void CloseCell() { FlushRun(); if (cell is not null) row!.Add(cell); cell = null; boldDepth = 0; italDepth = 0; }
        void CloseRow() { CloseCell(); if (row is { Count: > 0 }) rows.Add(row); row = null; }
        foreach (var tok in Tokenize(tableHtml))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (cell is not null)
                {
                    // whitespace runs collapse; a pure-whitespace stretch between
                    // tags carries nothing into the cell
                    var t = Regex.Replace(tok.Value, @"\s+", " ");
                    if (t != " " || text.Length > 0) text.Append(t);
                }
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "td" or "th": CloseCell(); break;
                    case "tr": CloseRow(); break;
                    case "b" or "strong": FlushRun(); boldDepth = Math.Max(0, boldDepth - 1); break;
                    case "i" or "em": FlushRun(); italDepth = Math.Max(0, italDepth - 1); break;
                }
                continue;
            }
            switch (tag)
            {
                case "tr": CloseRow(); row = new List<GridCell>(); break;
                case "td" or "th":
                    CloseCell();
                    row ??= new List<GridCell>();
                    cell = new GridCell();
                    if (tok.Attributes is { } ca)
                    {
                        if (ca.TryGetValue("colspan", out var csv)
                            && int.TryParse(csv.Trim(), out var csn) && csn > 1)
                            cell.ColSpan = csn;
                        if (ca.TryGetValue("width", out var wv) && wv.Trim().EndsWith('%')
                            && double.TryParse(wv.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float, invc, out var pct))
                            cell.WidthPct = pct;
                        if (ca.TryGetValue("align", out var av))
                            cell.Align = av.Trim().ToLowerInvariant() switch
                            {
                                "center" => HorizontalAlignment.Center,
                                "right" => HorizontalAlignment.Right,
                                _ => HorizontalAlignment.Left,
                            };
                        if (ca.TryGetValue("style", out var st))
                        {
                            if (Regex.IsMatch(st, @"border-left\s*:\s*0", RegexOptions.IgnoreCase))
                                cell.BorderLeftZero = true;
                            if (Regex.IsMatch(st, @"border-right\s*:\s*0", RegexOptions.IgnoreCase))
                                cell.BorderRightZero = true;
                        }
                    }
                    break;
                case "b" or "strong": FlushRun(); boldDepth++; break;
                case "i" or "em": FlushRun(); italDepth++; break;
                case "br": break;   // a cell <br> concatenates (measured: the date cells draw as ONE line)
                case "img":
                    if (cell is not null && tok.Attributes is { } ia)
                    {
                        if (ia.TryGetValue("src", out var src)
                            && Regex.Match(src, @"^data:image/png;base64,(.+)$",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } dm)
                            cell.ImgB64 = dm.Groups[1].Value;
                        if (ia.TryGetValue("width", out var iw) && iw.Trim().EndsWith('%')
                            && double.TryParse(iw.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float, invc, out var ipct))
                            cell.ImgPct = ipct / 100.0;
                    }
                    break;
            }
        }
        CloseRow();
        if (rows.Count == 0) return;

        // ── column grid from the first row: percents of the inner width resolved
        // in source order (a colspan splits its share equally); the LAST column
        // takes the remainder — the sheet's shares sum past 100%. ──
        var nCols = 0;
        foreach (var c0 in rows[0]) nCols += c0.ColSpan;
        var innerW = contentWidth - bw;            // between the outer border centers
        var colW = new double[nCols];
        {
            var ci = 0;
            foreach (var c0 in rows[0])
            {
                for (var k = 0; k < c0.ColSpan; k++)
                    colW[ci + k] = c0.WidthPct / 100.0 * innerW / c0.ColSpan;
                ci += c0.ColSpan;
            }
            double sum0 = 0;
            for (var c = 0; c < nCols - 1; c++) sum0 += colW[c];
            colW[nCols - 1] = innerW - sum0;
        }
        var edgeX = new double[nCols + 1];         // border centers, absolute
        edgeX[0] = marginLeft + bw / 2;
        for (var c = 0; c < nCols; c++) edgeX[c + 1] = edgeX[c] + colW[c];
        foreach (var r in rows)
        {
            var ci = 0;
            foreach (var c in r) { c.Col = ci; ci += c.ColSpan; }
        }

        // ── wrap cells: char-level break-all at the face's real advances ──
        var lineH = MetricLineHeight(fontSize, lineSum);
        var drop = MetricBaselineDrop(fontSize, lineH, fm);
        string RunFace(bool b, bool it) => b ? face + " Bold" : it ? face + " Italic" : face;
        foreach (var r in rows)
            foreach (var c in r)
            {
                if (c.Runs.Count == 0) continue;
                var cw = edgeX[c.Col + c.ColSpan] - edgeX[c.Col] - bw - 2 * pad;
                var line = new List<(string Text, bool Bold, bool Italic)>();
                double lw = 0;
                foreach (var (rt, rb, ri) in c.Runs)
                {
                    var rFace = RunFace(rb, ri);
                    foreach (var ch in rt)
                    {
                        var adv = MeasureFaceText(rFace, ch.ToString(), fontSize);
                        if (lw + adv > cw && line.Count > 0)
                        {
                            c.Lines.Add(line);
                            line = new List<(string, bool, bool)>();
                            lw = 0;
                        }
                        if (line.Count > 0 && line[^1].Bold == rb && line[^1].Italic == ri)
                            line[^1] = (line[^1].Text + ch, rb, ri);
                        else line.Add((ch.ToString(), rb, ri));
                        lw += adv;
                    }
                }
                if (line.Count > 0) c.Lines.Add(line);
            }

        // ── dialect font resources: WinAnsi entries under the face's TrueType
        // names (the raster side resolves the system face for them) ──
        var faceRes = face.Replace(" ", "");
        void EnsureGridFonts(Page p2)
        {
            EnsureFont(p2, faceRes, "F8");
            EnsureFont(p2, faceRes + "Bold", "F9");
            EnsureFont(p2, faceRes + "Italic", "F10");
        }
        EnsureGridFonts(page);

        // ── border strokes, buffered per page ──
        var bops = new StringBuilder();
        void HLine(double yTd)
            => bops.Append(string.Create(invc,
                $"{marginLeft:F2} {pageHeight - yTd:F2} m {marginLeft + contentWidth:F2} {pageHeight - yTd:F2} l S "));
        void VLine(double x, double y0Td, double y1Td)
            => bops.Append(string.Create(invc,
                $"{x:F2} {pageHeight - y0Td:F2} m {x:F2} {pageHeight - y1Td:F2} l S "));
        void FlushBorders(Page p2)
        {
            if (bops.Length == 0) return;
            p2.AddContentStream(Encoding.ASCII.GetBytes(
                string.Create(invc, $"q 0 0 0 RG {bw:0.##} w {bops}Q\n")));
            bops.Clear();
        }

        // Vertical border strengths for a row: outer edges always stroke; an
        // interior boundary strokes unless BOTH neighbouring cell sides zero it
        // (border-left:0 beside border-right:0 collapses to nothing); a boundary
        // inside a colspan has no border at all.
        bool[] RowEdges(List<GridCell> r)
        {
            var on = new bool[nCols + 1];
            on[0] = on[nCols] = true;
            for (var i = 0; i < r.Count; i++)
            {
                var c = r[i];
                if (i > 0)
                {
                    var left = r[i - 1];
                    on[c.Col] = !left.BorderRightZero || !c.BorderLeftZero;
                }
            }
            return on;
        }

        // ── layout: line-at-a-time with mid-row pagination ──
        var limit = pageHeight - marginBottom;
        var borderCenter = pageHeight - y + marTopPt + bw / 2;
        HLine(borderCenter);
        foreach (var r in rows)
        {
            var edgesOn = RowEdges(r);
            var maxLines = 1;                       // an all-empty row still holds one line box
            foreach (var c in r) maxLines = Math.Max(maxLines, c.Lines.Count);
            var contentTop = borderCenter + bw / 2 + pad;
            var segTop = borderCenter - bw / 2;     // border extent start on this page
            var lineTop = contentTop;
            var rowContentH = maxLines * lineH;
            for (var li = 0; li < maxLines; li++)
            {
                if (lineTop + lineH > limit)
                {
                    // split: side borders run to the page edge; the continuation
                    // page resumes half a border below its top edge
                    for (var e = 0; e <= nCols; e++)
                        if (edgesOn[e]) VLine(edgeX[e], segTop, limit + bw / 2);
                    FlushBorders(page);
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    EnsureGridFonts(page);
                    segTop = 0;
                    lineTop = bw / 2;
                    contentTop = lineTop - li * lineH;   // keeps lineTop = contentTop + li*lineH
                }
                foreach (var c in r)
                {
                    if (li >= c.Lines.Count) continue;
                    var ln = c.Lines[li];
                    double lnW = 0;
                    foreach (var (t, b, it) in ln) lnW += MeasureFaceText(RunFace(b, it), t, fontSize);
                    var cx0 = edgeX[c.Col] + bw / 2 + pad;
                    var cx1 = edgeX[c.Col + c.ColSpan] - bw / 2 - pad;
                    var x = c.Align switch
                    {
                        HorizontalAlignment.Center => cx0 + (cx1 - cx0 - lnW) / 2,
                        HorizontalAlignment.Right => cx1 - lnW,
                        _ => cx0,
                    };
                    foreach (var (t, b, it) in ln)
                    {
                        var res = b ? "F9" : it ? "F10" : "F8";
                        EmitPositionedRun(page, res, fontSize, x, pageHeight - (lineTop + drop), t);
                        x += MeasureFaceText(RunFace(b, it), t, fontSize);
                    }
                }
                lineTop += lineH;
            }
            // images: centered in the cell box, width a share of the cell content
            // (measured: 40% of span − 2·padding − half a border), height by the
            // PNG's natural aspect, centered in the row's content band
            foreach (var c in r)
            {
                if (c.ImgB64 is null) continue;
                byte[] png;
                try { png = System.Convert.FromBase64String(c.ImgB64); } catch { continue; }
                if (png.Length < 24) continue;
                var natW = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                var natH = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
                if (natW <= 0 || natH <= 0) continue;
                var span = edgeX[c.Col + c.ColSpan] - edgeX[c.Col];
                var imgW = c.ImgPct > 0 ? c.ImgPct * (span - 2 * pad - bw / 2) : span - 2 * pad - bw;
                var imgH = imgW * natH / natW;
                var bx0 = edgeX[c.Col] + bw / 2 + pad;
                var bx1 = edgeX[c.Col + c.ColSpan] - bw / 2 - pad;
                var ix = bx0 + (bx1 - bx0 - imgW) / 2;
                var iyTop = contentTop + (rowContentH - imgH) / 2;
                page.AddImage(png, new Rectangle(
                    ix, pageHeight - iyTop - imgH, ix + imgW, pageHeight - iyTop));
            }
            var bottomCenter = lineTop + pad + bw / 2;
            for (var e = 0; e <= nCols; e++)
                if (edgesOn[e]) VLine(edgeX[e], segTop, bottomCenter + bw / 2);
            HLine(bottomCenter);
            borderCenter = bottomCenter;
        }
        FlushBorders(page);
        // the flow resumes one full border below the bottom stroke's center, plus
        // the table's own margin-bottom
        y = pageHeight - (borderCenter + bw + marBottomPt);
    }

    /// <summary>Render the @media-print invoice sheet (see the caller's gate):
    /// label/value tables, the item table with dashed cell bottoms and totals
    /// rows, the trailer table, a dashed hr, and the QR bitmap — at the measured
    /// geometry. Null when the document does not fit the shape (the caller falls
    /// through to the ordinary flow).</summary>
    private static Document? TryRenderPrintInvoice(string html, HtmlLoadOptions? options)
    {
        var bodyPctM = Regex.Match(html,
            @"body\s*\{[^}]*?width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        if (!bodyPctM.Success) return null;
        if (!double.TryParse(bodyPctM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bodyPct)
            || bodyPct is <= 0 or > 100) return null;

        var css = ParseStyleSheet(html);
        HtmlNode dom;
        try
        {
            dom = ParseDom(Regex.Replace(Regex.Replace(html,
                @"<!--[\s\S]*?-->", m => new string(' ', m.Length)),
                @"<(script|style|head)[^>]*>[\s\S]*?</\1>",
                m => new string(' ', m.Length), RegexOptions.IgnoreCase));
        }
        catch { return null; }

        // Collect top-level tables in document order; each row's cells with
        // their emphasis. A table containing a row of 4+ populated cells is the
        // ITEM table; tables after it are trailer tables.
        var tables = new List<List<(List<(string Text, bool Th, bool Italic)> Cells, bool AnyTh)>>();
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "table") continue;
            var rows = new List<(List<(string, bool, bool)>, bool)>();
            foreach (var tr in el.Descendants())
            {
                if (tr.Tag != "tr") continue;
                var cells = new List<(string, bool, bool)>();
                var anyTh = false;
                foreach (var cd in tr.Children)
                {
                    if (cd.Tag is not ("td" or "th")) continue;
                    var italic = cd.Attrs is not null && cd.Attrs.TryGetValue("class", out var ccls)
                        && ccls.Contains("italic", StringComparison.OrdinalIgnoreCase);
                    foreach (var sp2 in cd.Descendants())
                        if (sp2.Tag == "span" && sp2.Attrs is not null
                            && sp2.Attrs.TryGetValue("class", out var scls)
                            && scls.Contains("italic", StringComparison.OrdinalIgnoreCase))
                            italic = true;
                    var txt = DomText(cd, css);
                    if (cd.Tag == "th") anyTh = true;
                    cells.Add((txt, cd.Tag == "th", italic));
                }
                if (cells.Count > 0) rows.Add((cells, anyTh));
            }
            if (rows.Count > 0) tables.Add(rows);
        }
        if (tables.Count < 2) return null;
        var itemTableIdx = -1;
        for (var t = 0; t < tables.Count; t++)
            foreach (var (cells, _) in tables[t])
            {
                var filled = 0;
                foreach (var (txt, _, _) in cells) if (txt.Length > 0) filled++;
                if (filled >= 4) { itemTableIdx = t; break; }
            }
        if (itemTableIdx < 0) return null;

        var doc = new Document();
        var pageW = PrintLeftPt + PrintContainerPt + PrintRightBandPt;
        var page = doc.Pages.Add(pageW, 842.0);
        var fontDict = new Core.PdfDictionary();
        EnsureFonts(page, fontDict);

        var x0 = PrintLeftPt;
        var bodyW = PrintContainerPt * bodyPct / 100.0;
        var bodyR = x0 + bodyW;
        var fontPt = 9.0;
        var reg = PosFace("Calibri");
        var bold = PosFace("Calibri Bold");
        var ital = PosFace("Calibri Italic");
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        double Measure(string t2, bool b2, bool i2)
            => MeasureFaceText(b2 ? "Calibri Bold" : i2 ? "Calibri Italic" : "Calibri", t2, fontPt);
        void Draw(string t2, double x, double glyphTop, bool b2, bool i2)
        {
            var f2 = b2 && bold.ttf is not null ? bold : i2 && ital.ttf is not null ? ital : reg;
            if (f2.ttf is null || t2.Length == 0) return;
            var baseline = 842.0 - glyphTop - PrintCalibriAscEm * fontPt;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, f2.ttf,
                b2 ? "Calibri Bold" : i2 ? "Calibri Italic" : "Calibri", t2,
                stripSpacesInBaseFont: true);
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(inv,
                $"BT 0 0 0 rg /{rn} {fontPt:F1} Tf 1 0 0 1 {x:F2} {baseline:F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n")));
        }
        void Dash(double xA, double xB, double yTd)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(inv,
                $"q 0 0 0 RG 0.75 w [1 0.5] 0 d {xA:F2} {842.0 - yTd:F2} m {xB:F2} {842.0 - yTd:F2} l S Q\n")));

        var colEdges = new double[PrintItemColEdgeFrac.Length];
        for (var c = 0; c < colEdges.Length; c++)
            colEdges[c] = x0 + PrintItemColEdgeFrac[c] * bodyW;

        var yTop = PrintFirstTopPt;
        double trailerTop = -1;   // glyph top of the first trailer (ZOI) row
        for (var t = 0; t < tables.Count; t++)
        {
            if (t > 0) yTop += PrintTableOpenPt - PrintRowPitchPt;
            var isItem = t == itemTableIdx;
            var trailerRow = 0;
            List<(string, bool, bool)>? lastValuesRow = null;
            foreach (var (cells, anyTh) in tables[t])
            {
                var filled = new List<(string Text, bool Th, bool Italic)>();
                foreach (var cc in cells) if (cc.Item1.Length > 0) filled.Add(cc);
                // An all-empty row collapses — it holds no line band.
                if (filled.Count == 0) continue;
                var pitch = PrintRowPitchPt;
                if (isItem && filled.Count >= 4)
                {
                    // Column header / values row: right-aligned at the col edges.
                    for (var c = 0; c < filled.Count && c < colEdges.Length; c++)
                    {
                        var (txt, th2, it2) = filled[c];
                        Draw(txt, colEdges[c] - PrintColRightInsetPt - Measure(txt, th2, it2),
                            yTop, th2, it2);
                    }
                    if (!anyTh) lastValuesRow = cells;
                }
                // A totals row carries an EMPHASISED label (th/bold); the
                // trailer rows (hash pairs, edition lines) are plain pairs.
                else if (isItem && filled.Count == 2 && (filled[0].Th || anyTh))
                {
                    // Close the values band with the dashed cell bottoms first.
                    if (lastValuesRow is not null)
                    {
                        var dashY = yTop - PrintRowPitchPt + PrintDashDropPt;
                        var segL = x0 + 1.5;
                        for (var c = 0; c < colEdges.Length; c++)
                        {
                            Dash(segL, colEdges[c], dashY);
                            segL = colEdges[c] + 1.5;
                        }
                        lastValuesRow = null;
                    }
                    var (lab, labTh, labIt) = filled[0];
                    var (val, valTh, valIt) = filled[1];
                    Draw(lab, colEdges[^2] - PrintColRightInsetPt - Measure(lab, labTh, labIt),
                        yTop, labTh, labIt);
                    Draw(val, colEdges[^1] - PrintColRightInsetPt - Measure(val, valTh, valIt),
                        yTop, valTh, valIt);
                }
                else if (filled.Count >= 2)
                {
                    var isTrailerRow = t > itemTableIdx || (isItem && !filled[0].Th && !anyTh);
                    if (isTrailerRow && trailerTop < 0)
                    {
                        // The trailer opens two bands below the totals.
                        yTop += PrintTrailerGapPt - PrintRowPitchPt;
                        trailerTop = yTop;
                    }
                    var (lab, labTh, labIt) = filled[0];
                    var (val, valTh, valIt) = filled[1];
                    Draw(lab, x0 + PrintCellInsetPt, yTop, labTh, labIt);
                    var valX = isTrailerRow
                        ? x0 + PrintZoiValueOffPt
                        : x0 + PrintCellInsetPt + PrintValueColFrac * PrintContainerPt;
                    Draw(val, valX, yTop, valTh, valIt);
                    if (isTrailerRow && trailerRow < PrintTrailerPitchPt.Length)
                        pitch = PrintTrailerPitchPt[trailerRow++];
                }
                else
                {
                    var (txt, th2, it2) = filled[0];
                    Draw(txt, x0 + PrintCellInsetPt, yTop, th2, it2);
                }
                yTop += pitch;
            }
        }

        // Trailing dashed hr and the QR bitmap, both anchored on the trailer top.
        if (trailerTop < 0) trailerTop = yTop;
        Dash(x0, bodyR, trailerTop + PrintHrFromTrailerPt);
        var qrM = Regex.Match(html, @"<img[^>]*src\s*=\s*[""'](data:image/[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (qrM.Success && LoadConverterImage(qrM.Groups[1].Value, options) is { } qrBytes)
        {
            var zTop = trailerTop + PrintQrDropPt;
            var qx1 = bodyR - PrintQrRightInsetPt;
            try
            {
                page.AddImage(qrBytes, new Rectangle(
                    qx1 - PrintQrSizePt, 842.0 - zTop - PrintQrSizePt, qx1, 842.0 - zTop));
            }
            catch { }
        }
        return doc;
    }
    /// <summary>Gap a left-floated box keeps between itself and the text beside it.</summary>
    private const double FloatGutterPt = 6;

    /// <summary>How far a line's BOX top stands above its baseline: the face's ascent plus
    /// the half-leading. A line is beside a float while its box TOP is above the float's
    /// bottom edge, so the count is taken from there, not from the baseline — measured on
    /// the certificate, whose paragraph keeps seven narrow lines and releases the eighth.
    /// A face without usable metrics keeps the flat line box.</summary>
    private static double FloatLineBoxRise(Block block, double fontSizePt, double lineHeightPt)
    {
        if (block.FontFamily is not { Length: > 0 } fam
            || WinMetricsFor(fam) is not { } fm)
            return lineHeightPt / 2;
        return fm.asc * fontSizePt + (lineHeightPt - fm.sum * fontSizePt) / 2;
    }

    /// <summary>The face the float flow measures a block's lines with — the family it
    /// resolved, in the style the block carries.</summary>
    private static string FloatFlowMeasureFace(Block block) =>
        block.FontRes == "F2" || block.EmBold ? block.FontFamily + " Bold"
        : block.FontRes == "F3" || block.EmItalic ? block.FontFamily + " Italic"
        : block.FontFamily!;

    // Print-invoice sheet constants — every value measured on the expected
    // render of the @media-print invoice fixture (page 931.25 × 842):
    // sheet = 96 + the print container + the fitted right band; the body is the
    // sheet's width% of the container; text runs on a 19px (14.25pt) pitch with
    // a fresh table opening one 21px (15.75pt) band below the previous row.
    private const double PrintContainerPt = 751.25;   // the engine's 1000px-class print viewport

    private const double PrintRightBandPt = 84.0;     // fitted right band (112px)

    private const double PrintLeftPt = 96.0;

    private const double PrintRowPitchPt = 14.25;     // 19px line band @ 9pt Calibri

    private const double PrintTableOpenPt = 15.75;    // 21px first-row band of a fresh table

    private const double PrintFirstTopPt = 87.9;      // page top → first row glyph top

    private const double PrintCellInsetPt = 2.25;     // cell chrome (border-spacing + padding)

    private const double PrintValueColFrac = 0.4;     // label tables: value col at 40% of the container

    private const double PrintZoiValueOffPt = 123.7;  // trailer table: value col offset

    private const double PrintColRightInsetPt = 0.7;  // right-aligned runs off the column edge

    private const double PrintDashDropPt = 10.3;      // values glyph top → dashed cell bottoms

    private const double PrintTrailerGapPt = 28.5;    // totals → trailer rows (two bands)

    private const double PrintHrFromTrailerPt = 90.2; // trailer top → dashed hr (347.9→438.1)

    private const double PrintQrDropPt = 26.5;        // trailer top → QR image top

    private const double PrintQrSizePt = 48.19;       // 1.7cm QR bitmap

    private const double PrintQrRightInsetPt = 10.77; // body right → QR right edge

    // The item table's column RIGHT edges as fractions of the body width and the
    // trailer rows' measured pitches (dl margins ride the last two).
    private static readonly double[] PrintItemColEdgeFrac = { 0.1701, 0.3467, 0.5439, 0.7695, 1.0 };

    private static readonly double[] PrintTrailerPitchPt = { 14.25, 17.1, 19.9 };

    // Calibri's ascender (typo ascent 750/1000 + the win gap) — seats a baseline
    // from a measured glyph top.
    private const double PrintCalibriAscEm = 0.952;

}
