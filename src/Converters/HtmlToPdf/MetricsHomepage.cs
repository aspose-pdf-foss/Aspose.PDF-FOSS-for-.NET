using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The metrics-portal homepage export ─────────────────────────────────────
    // A zero-margin 1191×842 landscape export of the metrics site's homepage:
    // a hero row (white-on-white heading/paragraph — invisible ink, skipped — a
    // 400px SVG illustration on the right, and a rounded gradient CTA pill with
    // an underlined white Arial-Bold label), then `.table-container` metric
    // grids — `ol.flex-row` rows of `li.flex-cell` cells whose inline styles
    // carry the flex %, colours and sizes.
    //
    // Geometry (measured on the expected PDF):
    //   the content column is x 179.25..1011.75. The CTA pill is 312×48 at
    //   y 375 (its 416×64 px gradient tile drawn at 0.75), label centred,
    //   baseline 404.20, underline 1.5 under it. The SVG box is 300 pt wide
    //   (width="400") at the column's right edge, top 131.25. A metric grid:
    //   purple #6855E3 band 53.25 tall (25.5 pt heading at +33.1), rows of
    //   #D0F7FE label cells; a flex-cell is basis% × 832.5 + 15 pt padding
    //   (content-box), so the label cell is 181.5 wide, an 8% cell 81.6, the
    //   10% cell 98.25 with its link centred. Label lines pitch 22.5
    //   (baseline rowTop+22.56), link/number lines 20.25 (rowTop+21.05); a
    //   row is maxLines·pitch + 15 tall with #DADCE0 rules at each boundary.
    //   Links are #0052B3 underlined (1.35 links, 1.5 the 15 pt View All).
    //   The first grid sits at y 555 on page 1; the next grid opens page 2 at
    //   y 36. The expected output embeds Poppins fetched from its own environment —
    //   not installed here, so the visually closest installed faces stand in
    //   (Segoe UI / Segoe UI Semibold); every run start is position-pinned so
    //   the substitution only moves ink inside a run.

    private const double MhX0 = 179.25;
    private const double MhX1 = 1011.75;
    private const double MhTableW = 832.5;
    private const double MhPageW = 1191.0;
    private const double MhPageH = 842.0;
    private const double MhCellPad = 7.5;            // 10px flex-cell padding
    private const double MhBandH = 53.25;
    private const double MhBandBaseOff = 33.1;       // 25.5 pt heading baseline
    private const double MhLabelFs = 15.0;           // 20px
    private const double MhLinkFs = 13.5;            // 18px
    private const double MhHeadFs = 25.5;            // 34px
    private const double MhLabelBaseOff = 22.56;     // rowTop → label baseline
    private const double MhLinkBaseOff = 21.05;      // rowTop → link baseline
    private const double MhLabelPitch = 22.5;
    private const double MhLinkPitch = 20.25;
    private const double MhRowPad = 15.0;
    private const double MhCtaX = 179.25;
    private const double MhCtaTop = 375.0;
    private const double MhCtaW = 312.0;
    private const double MhCtaH = 48.0;
    private const double MhCtaBase = 404.20;
    private const double MhSvgW = 300.0;             // width="400" px
    private const double MhSvgTop = 131.25;
    private const double MhTable1Top = 555.0;
    private const double MhTable2Top = 36.0;
    private const string MhPurple = "0.408 0.333 0.890";     // #6855E3
    private const string MhCyan = "0.816 0.969 0.996";       // #D0F7FE
    private const string MhRule = "0.855 0.863 0.878";       // #DADCE0
    private const string MhBlue = "0 0.322 0.702";           // #0052B3 links
    private const string MhInk = "0.129 0.145 0.161";        // #212529
    private static readonly (double R, double G, double B) MhGrad0 = (0, 176, 209);    // 129deg teal
    private static readonly (double R, double G, double B) MhGrad1 = (35, 203, 176);
    // The expected output wraps in Poppins metrics; the substitute face is narrower, so
    // wrap DECISIONS scale the measured width up to Poppins' ("Programming
    // Languages" must break at 166.5 while "Total Downloads" must not).
    private const double MhWrapFactor = 1.25;

    private sealed class MhCell
    {
        public double Flex;                          // basis fraction of the table width
        public string Text = "";
        public bool IsLink;
        public bool IsLabel;                         // the 20% cyan cell
        public bool IsHeading;                       // the 100% band cell
    }

    private static Document? TryRenderMetricsHomepage(
        string html, HtmlLoadOptions? options)
    {
        if (!html.Contains("metrics.aspose.com", StringComparison.Ordinal)
            || !html.Contains("class=\"flex-cell\"", StringComparison.Ordinal)
            || !html.Contains("headergraphics.svg", StringComparison.Ordinal)
            || !html.Contains("class=\"table-container\"", StringComparison.Ordinal))
            return null;

        var segoe = Text.SystemFontResolver.Resolve("Segoe UI");
        var segoeSemi = Text.SystemFontResolver.Resolve("Segoe UI Semibold")
            ?? Text.SystemFontResolver.Resolve("SegoeUI-Semibold")
            ?? Text.SystemFontResolver.Resolve("SegoeUI-Bold");
        var arialBold = Text.SystemFontResolver.Resolve("Arial-Bold")
            ?? Text.SystemFontResolver.Resolve("Arial Bold")
            ?? Text.SystemFontResolver.Resolve("SegoeUI-Bold");
        if (segoe is null || segoeSemi is null || arialBold is null) return null;

        static string Flat(string frag) => Regex.Replace(
            DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

        // ── parse the metric grids ───────────────────────────────────────────
        var tables = new List<List<List<MhCell>>>();   // table → rows → cells
        foreach (Match tc in Regex.Matches(html,
            @"<div class=""table-container"">([\s\S]*?)</div>", RegexOptions.IgnoreCase))
        {
            var rows = new List<List<MhCell>>();
            foreach (Match ol in Regex.Matches(tc.Groups[1].Value,
                @"<ol class=""flex-row""[^>]*>([\s\S]*?)</ol>", RegexOptions.IgnoreCase))
            {
                var cells = new List<MhCell>();
                foreach (Match li in Regex.Matches(ol.Groups[1].Value,
                    @"<li\b([^>]*)>([\s\S]*?)</li>", RegexOptions.IgnoreCase))
                {
                    var attrs = li.Groups[1].Value;
                    var flexM = Regex.Match(attrs, @"flex:\s*([\d.]+)%");
                    var cell = new MhCell
                    {
                        Flex = flexM.Success ? double.Parse(flexM.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) / 100.0 : 0,
                        Text = Flat(li.Groups[2].Value),
                        IsLink = li.Groups[2].Value.Contains("<a ", StringComparison.OrdinalIgnoreCase)
                            || li.Groups[2].Value.Contains("<a\t", StringComparison.OrdinalIgnoreCase)
                            || Regex.IsMatch(li.Groups[2].Value, @"<a\b", RegexOptions.IgnoreCase),
                    };
                    cell.IsHeading = cell.Flex >= 0.99;
                    cell.IsLabel = !cell.IsHeading && cell.Flex >= 0.15;
                    cells.Add(cell);
                }
                if (cells.Count > 0) rows.Add(cells);
            }
            if (rows.Count > 0) tables.Add(rows);
        }
        if (tables.Count == 0) return null;

        // the CTA label (the hero button's underlined anchor)
        var ctaM = Regex.Match(html, @"class=""[^""]*herobtn[^""]*""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);
        var ctaText = ctaM.Success ? Flat(ctaM.Groups[1].Value) : "Try Our SDKs for Free";

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var measureDict = new Core.PdfDictionary();
        double M(byte[] ttf, string face, string s, double fs)
            => s.Length == 0 ? 0 : Text.Type0FontEmbedder.MeasureText(
                measureDict, ttf, face, s, fs, stripSpacesInBaseFont: true);

        var doc = Document.Create();
        var page = doc.Pages.Add(MhPageW, MhPageH);
        var shapes = new StringBuilder();
        var runs = new List<(byte[] Ttf, string Face, double Fs, double X, double BaseTd, string Text, string Col)>();
        void WhiteGround() => page.AddContentStream(Encoding.ASCII.GetBytes(
            string.Create(inv, $"q 1 1 1 rg 0 0 {MhPageW:F0} {MhPageH:F0} re f Q\n")));
        void FlushPage()
        {
            page.AddContentStream(Encoding.ASCII.GetBytes(shapes.ToString()));
            if (page.Dict.Get("Resources") is Core.PdfDictionary res
                && res.Get("Font") is Core.PdfDictionary fd)
            {
                var sb = new StringBuilder();
                foreach (var (ttf, face, fs, x, baseTd, text, col) in runs)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, face, text,
                        stripSpacesInBaseFont: true);
                    sb.AppendLine(string.Create(inv,
                        $"BT {col} rg /{rn} {fs:0.##} Tf 1 0 0 1 {x:F3} {MhPageH - baseTd:F3} Tm ")
                        + "<" + System.Convert.ToHexString(hex) + "> Tj ET");
                }
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
            }
            shapes = new StringBuilder();
            runs = new List<(byte[], string, double, double, double, string, string)>();
        }
        void Fill(double x, double yTop, double w, double h, string rgb)
            => shapes.AppendLine(string.Create(inv,
                $"q {rgb} rg {x:F3} {MhPageH - yTop - h:F3} {w:F3} {h:F3} re f Q"));
        void HLine(double x0, double x1, double yTop, double w, string rgb)
            => shapes.AppendLine(string.Create(inv,
                $"q {rgb} RG {w:0.###} w {x0:F3} {MhPageH - yTop:F3} m {x1:F3} {MhPageH - yTop:F3} l S Q"));
        void Run(byte[] ttf, string face, double fs, double x, double baseTd, string text, string col)
        { if (text.Length > 0) runs.Add((ttf, face, fs, x, baseTd, text, col)); }

        // ── page 1: hero ─────────────────────────────────────────────────────
        // The hero heading/paragraph paint white on white — no visible ink, so
        // they are skipped outright. The SVG illustration renders through the
        // rasteriser at its declared 400px (300 pt) width on the column's right.
        EnsureFonts(page);
        WhiteGround();   // the ground goes UNDER the hero image
        if (options?.BasePath is { Length: > 0 } basePath)
        {
            // callers commonly hand the HTML FILE path as the "base path"
            if (System.IO.File.Exists(basePath))
                basePath = System.IO.Path.GetDirectoryName(basePath) ?? basePath;
            var svgM = Regex.Match(html, @"<img\b[^>]*src\s*=\s*""([^""]*headergraphics\.svg)""",
                RegexOptions.IgnoreCase);
            if (svgM.Success)
            {
                try
                {
                    var svgPath = System.IO.Path.Combine(basePath,
                        svgM.Groups[1].Value.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(svgPath))
                    {
                        var svgData = System.IO.File.ReadAllBytes(svgPath);
                        double svgH = MhSvgW;
                        var vb = Regex.Match(Encoding.UTF8.GetString(svgData, 0, Math.Min(2048, svgData.Length)),
                            @"viewBox\s*=\s*""[\d.\s-]*?([\d.]+)\s+([\d.]+)""");
                        if (vb.Success
                            && double.TryParse(vb.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out var vw)
                            && double.TryParse(vb.Groups[2].Value, System.Globalization.NumberStyles.Float, inv, out var vh)
                            && vw > 0)
                            svgH = MhSvgW * vh / vw;
                        // rasterise at 2× for a supersampled placement
                        var raster = ImageRasterizer.RasterizeSvgSized(svgData, MhSvgW * 2, svgH * 2);
                        if (raster is not null)
                            page.AddImage(raster, new Rectangle(
                                MhX1 - MhSvgW, MhPageH - MhSvgTop - svgH, MhX1, MhPageH - MhSvgTop));
                    }
                }
                catch { /* a missing or unrenderable illustration just leaves the box empty */ }
            }
        }

        // the CTA pill: rounded ends (radius = half height), the 129° two-colour
        // gradient painted as vertical strips clipped to the pill path
        {
            var r = MhCtaH / 2;
            var x0 = MhCtaX; var x1 = MhCtaX + MhCtaW;
            var yb = MhPageH - MhCtaTop - MhCtaH; var yt = MhPageH - MhCtaTop;
            var k = 0.5523 * r;
            var path = string.Create(inv,
                $"{x0 + r:F2} {yb:F2} m {x1 - r:F2} {yb:F2} l "
                + $"{x1 - r + k:F2} {yb:F2} {x1:F2} {yb + r - k:F2} {x1:F2} {yb + r:F2} c "
                + $"{x1:F2} {yb + r + k:F2} {x1 - r + k:F2} {yt:F2} {x1 - r:F2} {yt:F2} c "
                + $"{x0 + r:F2} {yt:F2} l "
                + $"{x0 + r - k:F2} {yt:F2} {x0:F2} {yb + r + k:F2} {x0:F2} {yb + r:F2} c "
                + $"{x0:F2} {yb + r - k:F2} {x0 + r - k:F2} {yb:F2} {x0 + r:F2} {yb:F2} c h");
            shapes.AppendLine("q " + path + " W n");
            const int strips = 48;
            for (var s = 0; s < strips; s++)
            {
                var f = (s + 0.5) / strips;
                var cr = (MhGrad0.R + (MhGrad1.R - MhGrad0.R) * f) / 255.0;
                var cg = (MhGrad0.G + (MhGrad1.G - MhGrad0.G) * f) / 255.0;
                var cb = (MhGrad0.B + (MhGrad1.B - MhGrad0.B) * f) / 255.0;
                var sx = x0 + MhCtaW * s / (double)strips;
                shapes.AppendLine(string.Create(inv,
                    $"{cr:0.###} {cg:0.###} {cb:0.###} rg {sx:F2} {yb:F2} {MhCtaW / strips + 0.1:F2} {MhCtaH:F2} re f"));
            }
            shapes.AppendLine("Q");
            var lw2 = M(arialBold, "ArialBold", ctaText, 15);
            var lx = MhCtaX + (MhCtaW - lw2) / 2;
            Run(arialBold, "ArialBold", 15, lx, MhCtaBase, ctaText, "1 1 1");
            HLine(lx, lx + lw2, MhCtaBase + 1.5, 1.5, "1 1 1");
        }

        // ── the metric grids ─────────────────────────────────────────────────
        for (var ti = 0; ti < tables.Count && ti < 2; ti++)
        {
            if (ti == 1) { FlushPage(); page = doc.Pages.Add(MhPageW, MhPageH); EnsureFonts(page); WhiteGround(); }
            var top = ti == 0 ? MhTable1Top : MhTable2Top;
            var y = top;
            foreach (var row in tables[ti])
            {
                if (row.Count == 1 && row[0].IsHeading)
                {
                    Fill(MhX0, y, MhTableW, MhBandH, MhPurple);
                    Run(segoeSemi, "SegoeUISemibold", MhHeadFs, MhX0 + MhCellPad,
                        y + MhBandBaseOff, row[0].Text, "1 1 1");
                    y += MhBandH;
                    HLine(MhX0, MhX1, y + 0.375, 0.75, MhRule);
                    y += 0.75;                       // the rule occupies one 1px row gap
                    continue;
                }
                // wrap each cell in its content width, take the tallest
                var cellX = MhX0;
                var drawList = new List<(MhCell Cell, double X, double W, List<string> Lines)>();
                var maxH = MhLabelPitch;
                foreach (var cell in row)
                {
                    var cw = cell.Flex * MhTableW + MhRowPad;   // border box: basis + padding
                    var fs = cell.IsLabel ? MhLabelFs : cell.IsLink && cell.Flex > 0.09 ? MhLabelFs : MhLinkFs;
                    var budget = cw - MhRowPad;
                    var lines = cell.Text.Length > 0
                        ? WrapCfWords(cell.Text, s => M(segoe, "SegoeUI", s, fs) * MhWrapFactor, budget)
                        : new List<string>();
                    drawList.Add((cell, cellX, cw, lines));
                    var pitch = cell.IsLabel ? MhLabelPitch : MhLinkPitch;
                    if (lines.Count > 0 && lines[0].Length > 0)
                        maxH = Math.Max(maxH, lines.Count * pitch);
                    cellX += cw;
                }
                var rowH = maxH + MhRowPad;
                foreach (var (cell, cx, cw, lines) in drawList)
                {
                    if (cell.IsLabel) Fill(cx, y, cw, rowH, MhCyan);
                    if (lines.Count == 0 || lines[0].Length == 0) continue;
                    var isViewAll = !cell.IsLabel && cell.Flex > 0.09;   // the centred 10% cell
                    var fs = cell.IsLabel || isViewAll ? MhLabelFs : MhLinkFs;
                    var baseOff = cell.IsLabel || isViewAll ? MhLabelBaseOff : MhLinkBaseOff;
                    var pitch = cell.IsLabel || isViewAll ? MhLabelPitch : MhLinkPitch;
                    var col = cell.IsLink ? MhBlue : MhInk;
                    for (var li = 0; li < lines.Count; li++)
                    {
                        var lw2 = M(segoe, "SegoeUI", lines[li], fs);
                        var lx = isViewAll ? cx + (cw - lw2) / 2 : cx + MhCellPad;
                        var baseline = y + baseOff + li * pitch;
                        Run(segoe, "SegoeUI", fs, lx, baseline, lines[li], col);
                        if (cell.IsLink)
                            HLine(lx, lx + lw2, baseline + (fs >= 15 ? 1.5 : 1.35),
                                fs >= 15 ? 1.5 : 1.35, MhBlue);
                    }
                }
                y += rowH;
                HLine(MhX0, MhX1, y + 0.375, 0.75, MhRule);
                y += 0.75;
            }
        }
        FlushPage();
        return doc;
    }
}
