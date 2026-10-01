using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Body grid table: one row laid out line at a time, splitting mid-row at the content limit.</summary>
    private static void RenderGridRow(GridTableState gd, List<GridCell> r)
    {
        var edgesOn = RowEdges(gd, r);
        var maxLines = 1;                       // an all-empty row still holds one line box
        foreach (var c in r) maxLines = Math.Max(maxLines, c.Lines.Count);
        var contentTop = gd.borderCenter + GridBorderPt / 2 + gd.pad;
        var segTop = gd.borderCenter - GridBorderPt / 2;     // border extent start on this page
        var lineTop = contentTop;
        var rowContentH = maxLines * gd.lineH;
        for (var li = 0; li < maxLines; li++)
        {
            if (lineTop + gd.lineH > gd.limit)
            {
                // split: side borders run to the page edge; the continuation
                // page resumes half a border below its top edge
                for (var e = 0; e <= gd.nCols; e++)
                    if (edgesOn[e]) VLine(gd, gd.edgeX[e], segTop, gd.limit + GridBorderPt / 2);
                FlushBorders(gd, gd.cursor.page);
                gd.cursor.page = gd.doc.Pages.Add(gd.pageWidth, gd.pageHeight);
                EnsureFonts(gd.cursor.page, gd.docFontDict);
                EnsureGridFonts(gd, gd.cursor.page);
                segTop = 0;
                lineTop = GridBorderPt / 2;
                contentTop = lineTop - li * gd.lineH;   // keeps lineTop = contentTop + li*lineH
            }
            foreach (var c in r)
            {
                if (li >= c.Lines.Count) continue;
                var ln = c.Lines[li];
                double lnW = 0;
                foreach (var (t, b, it) in ln) lnW += MeasureFaceText(RunFace(gd, b, it), t, gd.fontSize);
                var cx0 = gd.edgeX[c.Col] + GridBorderPt / 2 + gd.pad;
                var cx1 = gd.edgeX[c.Col + c.ColSpan] - GridBorderPt / 2 - gd.pad;
                var x = c.Align switch
                {
                    HorizontalAlignment.Center => cx0 + (cx1 - cx0 - lnW) / 2,
                    HorizontalAlignment.Right => cx1 - lnW,
                    _ => cx0,
                };
                foreach (var (t, b, it) in ln)
                {
                    var res = b ? "F9" : it ? "F10" : "F8";
                    EmitPositionedRun(gd.cursor.page, res, gd.fontSize, x, gd.pageHeight - (lineTop + gd.drop), t);
                    x += MeasureFaceText(RunFace(gd, b, it), t, gd.fontSize);
                }
            }
            lineTop += gd.lineH;
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
            var span = gd.edgeX[c.Col + c.ColSpan] - gd.edgeX[c.Col];
            var imgW = c.ImgPct > 0 ? c.ImgPct * (span - 2 * gd.pad - GridBorderPt / 2) : span - 2 * gd.pad - GridBorderPt;
            var imgH = imgW * natH / natW;
            var bx0 = gd.edgeX[c.Col] + GridBorderPt / 2 + gd.pad;
            var bx1 = gd.edgeX[c.Col + c.ColSpan] - GridBorderPt / 2 - gd.pad;
            var ix = bx0 + (bx1 - bx0 - imgW) / 2;
            var iyTop = contentTop + (rowContentH - imgH) / 2;
            gd.cursor.page.AddImage(png, new Rectangle(
                ix, gd.pageHeight - iyTop - imgH, ix + imgW, gd.pageHeight - iyTop));
        }
        var bottomCenter = lineTop + gd.pad + GridBorderPt / 2;
        for (var e = 0; e <= gd.nCols; e++)
            if (edgesOn[e]) VLine(gd, gd.edgeX[e], segTop, bottomCenter + GridBorderPt / 2);
        HLine(gd, bottomCenter);
        gd.borderCenter = bottomCenter;
    }

    /// <summary>Body grid table: every cell's runs wrapped char-by-char at the face's real advances.</summary>
    private static void WrapGridCells(GridTableState gd)
    {
        gd.lineH = MetricLineHeight(gd.fontSize, gd.lineSum);
        gd.drop = MetricBaselineDrop(gd.fontSize, gd.lineH, gd.fm);
        foreach (var r in gd.rows)
            foreach (var c in r)
            {
                if (c.Runs.Count == 0) continue;
                var cw = gd.edgeX[c.Col + c.ColSpan] - gd.edgeX[c.Col] - GridBorderPt - 2 * gd.pad;
                var line = new List<(string Text, bool Bold, bool Italic)>();
                double lw = 0;
                foreach (var (rt, rb, ri) in c.Runs)
                {
                    var rFace = RunFace(gd, rb, ri);
                    foreach (var ch in rt)
                    {
                        var adv = MeasureFaceText(rFace, ch.ToString(), gd.fontSize);
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
    }

    /// <summary>Body grid table: the column grid resolved from the first row's width percents.</summary>
    private static void SolveGridColumns(GridTableState gd)
    {
        gd.nCols = 0;
        foreach (var c0 in gd.rows[0]) gd.nCols += c0.ColSpan;
        gd.innerW = gd.contentWidth - GridBorderPt;
        gd.colW = new double[gd.nCols];
        {
            var ci = 0;
            foreach (var c0 in gd.rows[0])
            {
                for (var k = 0; k < c0.ColSpan; k++)
                    gd.colW[ci + k] = c0.WidthPct / 100.0 * gd.innerW / c0.ColSpan;
                ci += c0.ColSpan;
            }
            double sum0 = 0;
            for (var c = 0; c < gd.nCols - 1; c++) sum0 += gd.colW[c];
            gd.colW[gd.nCols - 1] = gd.innerW - sum0;
        }
        gd.edgeX = new double[gd.nCols + 1];
        gd.edgeX[0] = gd.marginLeft + GridBorderPt / 2;
        for (var c = 0; c < gd.nCols; c++) gd.edgeX[c + 1] = gd.edgeX[c] + gd.colW[c];
        foreach (var r in gd.rows)
        {
            var ci = 0;
            foreach (var c in r) { c.Col = ci; ci += c.ColSpan; }
        }
    }

    /// <summary>Body grid table: the rows and cells read off the table html.</summary>
    private static void ParseGridRows(GridTableState gd)
    {
        gd.rows = new List<List<GridCell>>();
        gd.row = null;
        gd.cell = null;
        gd.text = new StringBuilder();
        gd.boldDepth = 0;
        gd.italDepth = 0;
        foreach (var tok in Tokenize(gd.tableHtml))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (gd.cell is not null)
                {
                    // whitespace runs collapse; a pure-whitespace stretch between
                    // tags carries nothing into the cell
                    var t = Regex.Replace(tok.Value, @"\s+", " ");
                    if (t != " " || gd.text.Length > 0) gd.text.Append(t);
                }
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "td" or "th": CloseCell(gd); break;
                    case "tr": CloseRow(gd); break;
                    case "b" or "strong": FlushRun(gd); gd.boldDepth = Math.Max(0, gd.boldDepth - 1); break;
                    case "i" or "em": FlushRun(gd); gd.italDepth = Math.Max(0, gd.italDepth - 1); break;
                }
                continue;
            }
            switch (tag)
            {
                case "tr": CloseRow(gd); gd.row = new List<GridCell>(); break;
                case "td" or "th":
                    CloseCell(gd);
                    gd.row ??= new List<GridCell>();
                    gd.cell = new GridCell();
                    if (tok.Attributes is { } ca)
                    {
                        if (ca.TryGetValue("colspan", out var csv)
                            && int.TryParse(csv.Trim(), out var csn) && csn > 1)
                            gd.cell.ColSpan = csn;
                        if (ca.TryGetValue("width", out var wv) && wv.Trim().EndsWith('%')
                            && double.TryParse(wv.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float, gd.invc, out var pct))
                            gd.cell.WidthPct = pct;
                        if (ca.TryGetValue("align", out var av))
                            gd.cell.Align = av.Trim().ToLowerInvariant() switch
                            {
                                "center" => HorizontalAlignment.Center,
                                "right" => HorizontalAlignment.Right,
                                _ => HorizontalAlignment.Left,
                            };
                        if (ca.TryGetValue("style", out var st))
                        {
                            if (Regex.IsMatch(st, @"border-left\s*:\s*0", RegexOptions.IgnoreCase))
                                gd.cell.BorderLeftZero = true;
                            if (Regex.IsMatch(st, @"border-right\s*:\s*0", RegexOptions.IgnoreCase))
                                gd.cell.BorderRightZero = true;
                        }
                    }
                    break;
                case "b" or "strong": FlushRun(gd); gd.boldDepth++; break;
                case "i" or "em": FlushRun(gd); gd.italDepth++; break;
                case "br": break;   // a cell <br> concatenates (measured: the date cells draw as ONE line)
                case "img":
                    if (gd.cell is not null && tok.Attributes is { } ia)
                    {
                        if (ia.TryGetValue("src", out var src)
                            && Regex.Match(src, @"^data:image/png;base64,(.+)$",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } dm)
                            gd.cell.ImgB64 = dm.Groups[1].Value;
                        if (ia.TryGetValue("width", out var iw) && iw.Trim().EndsWith('%')
                            && double.TryParse(iw.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float, gd.invc, out var ipct))
                            gd.cell.ImgPct = ipct / 100.0;
                    }
                    break;
            }
        }
        CloseRow(gd);
    }

    /// <summary>Body grid table: cellpadding, font size and margins read off the table tag.</summary>
    private static void ReadGridTableAttributes(GridTableState gd)
    {
        gd.pad = PxPt;
        gd.fontSize = 11;
        gd.marTopPt = 0;
        gd.marBottomPt = 0;
        if (Regex.Match(gd.tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } tt)
        {
            var tag = tt.Value;
            var cp = Regex.Match(tag, @"cellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (cp.Success) gd.pad = double.Parse(cp.Groups[1].Value, gd.invc) * PxPt;
            var fs = Regex.Match(tag, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (fs.Success) gd.fontSize = double.Parse(fs.Groups[1].Value, gd.invc);
            var mt = Regex.Match(tag, @"margin-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mt.Success) gd.marTopPt = double.Parse(mt.Groups[1].Value, gd.invc) * PxPt;
            var mb = Regex.Match(tag, @"margin-bottom\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mb.Success) gd.marBottomPt = double.Parse(mb.Groups[1].Value, gd.invc) * PxPt;
        }
    }
}
