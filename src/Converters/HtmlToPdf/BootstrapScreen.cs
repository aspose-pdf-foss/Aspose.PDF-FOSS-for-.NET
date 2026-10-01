using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The Bootstrap-screen dialect: a themed Bootstrap page (a BODY rule carrying a
// pixel font size, a unitless line-height and a page background, plus the
// .container/.table framework classes) rendered at HtmlMediaType.Screen. The
// reference lays it out on symmetric 90 pt side margins with the body background
// painted over the content box, the container inset by its 15px padding, real
// line-height line boxes (UNROUNDED — 13px · 1.42857 = 13.928 pt, where the
// px-rounding the other metric dialects apply would land a half-pixel off), and
// win-metric half-leading baselines. Tables draw as bordered grids whose columns
// share the full width in proportion to their widest cell; buttons draw as their
// CSS boxes with the surrounding text seated by vertical-align:middle (box middle
// = text baseline − half the face's x-height); glyphicons substitute the system
// symbol faces. Every constant below is either read from the
// stylesheet or measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    /// <summary>One inline piece of a Bootstrap paragraph.</summary>
    private sealed class BsRun
    {
        public string? Text;                       // plain text (null for icon/button)
        public int IconCp;                         // glyphicon code point (0 = none)
        public bool InLink;                        // inside <a> — link colour
        public BsButton? Button;
    }

    private sealed class BsButton
    {
        public int IconCp;
        public string Label = "";
        public bool Large;                         // .btn-lg
        public bool IsButtonTag;                   // <button> draws the UA outline
        public Color Fill = Color.FromArgb(236, 236, 236);
        public Color Border = Color.FromArgb(145, 150, 156);
        public Color Fg = Color.FromArgb(51, 51, 50);
    }

    // The content box: 90 pt side margins, 72 pt top/bottom (the body
    // background fill spans exactly (90,72)-(pageW-90,pageH-72)).
    private const double BsMarginX = 90.0;
    private const double BsMarginY = 72.0;
    // .glyphicon { position: relative; top: 1px } — every icon sits 0.75 pt below
    // its line's baseline.
    private const double BsIconTopPt = 0.75;
    // An unresolved private-use glyphicon (E003/E045 have no glyph in the system
    // symbol faces) still advances three quarters of an em (measured: the bold
    // button label starts 7.31 pt past the icon pen at 9.75 pt).
    private const double BsNotdefIconAdvEm = 0.75;
    // A <button> element's UA chrome: a 1 px black outline drawn OUTSIDE the CSS
    // box — 2 pt to the sides, 1.5 pt above/below (measured).
    private const double BsButtonOutlineX = 2.0;
    private const double BsButtonOutlineY = 1.5;

    // The MVC-template (navbar + jumbotron) arm — all measured values:
    // the 50px navbar band, the brand baseline 23.4 under the band top, the
    // jumbotron's 36pt (48px) top pad, its 27pt/29.7 h1 with the 15px bottom
    // margin, the 15.75/22.05 lead, the 34.5 btn-lg box, the 11.3 gap to the
    // 25.5 plain-button box, the 30px bottom pad, and the 30px jumbotron
    // margin-bottom; hr rules carry 20px margins.
    private const double BsNavbarHPt = 38.2;
    private const double BsBrandBaselinePt = 23.4;
    private const double JmbPadTopPt = 36.0;
    private const double JmbPadBotPt = 22.5;
    private const double JmbPadXPt = 11.25;
    private const double JmbH1FsPt = 27.0;
    private const double JmbH1LineHPt = 29.7;
    private const double JmbH1MarBPt = 11.25;
    private const double JmbLeadFsPt = 15.75;
    private const double JmbLeadLineHPt = 22.05;
    private const double JmbLeadMarBPt = 11.25;
    private const double JmbBtnLgHPt = 34.5;
    private const double JmbBtnHPt = 25.5;
    private const double JmbBtnGapPt = 11.3;
    private const double JmbMarBPt = 22.5;
    private const double BsHrMarginPt = 15.0;

    // Glyphicons Halflings :before content (the stylesheet's own mapping — the
    // parser drops :before rules, so the three icons this sheet uses are pinned).
    private static int BsGlyphiconCp(string classes)
        => classes.Contains("glyphicon-envelope") ? 0x2709
         : classes.Contains("glyphicon-search") ? 0xE003
         : classes.Contains("glyphicon-print") ? 0xE045
         : 0;

    /// <summary>Render a themed Bootstrap screen document, or null when the page
    /// does not carry the dialect's fingerprint.</summary>
    private static Document? TryRenderBootstrapScreen(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        var bs = new BootstrapScreenState();
        // ── fingerprint: themed Bootstrap body + framework classes ──
        if (!css.TryGetValue("body", out var body)) return null;
        if (!body.TryGetValue("background-color", out var bodyBgV)
            || ParseCssColor(bodyBgV) is not { } bodyBg) return null;
        if (!body.TryGetValue("font-size", out var bodyFsV)
            || !bodyFsV.TrimEnd().EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || TryParseLength(bodyFsV) is not { } bodyFs) return null;
        if (!body.TryGetValue("line-height", out var bodyLhV)
            || !double.TryParse(bodyLhV.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lineFactor)
            || lineFactor is <= 1.0 or >= 2.0) return null;
        if (!css.ContainsKey(".container") || !css.ContainsKey(".table")) return null;
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']container[""']", RegexOptions.IgnoreCase))
            return null;
        bs.body = body;
        bs.bodyBg = bodyBg;
        bs.bodyFs = bodyFs;
        bs.lineFactor = lineFactor;

        if (!TryResolveBootstrapStyles(bs, css)) return null;

        if (!TryParseBootstrapHeader(bs, css, html)) return null;

        bs.blocks = ParseBootstrapBlocks(bs.bodyHtml, bs, css);
        if (bs.blocks.Count == 0) return null;

        bs.doc = new Document();
        bs.page = bs.doc.Pages.Add(pageWidth, pageHeight);
        bs.boldFace = bs.face + "-Bold";
        EnsureFont(bs.page, bs.face.Replace(" ", ""), "FA");
        EnsureFont(bs.page, bs.face.Replace(" ", "") + "-Bold", "FB");

        bs.contentX = BsMarginX + bs.containerPad;
        bs.contentW = pageWidth - 2 * BsMarginX - 2 * bs.containerPad;
        bs.invc = System.Globalization.CultureInfo.InvariantCulture;

        // body background over the content box
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q {bodyBg.R / 255.0:0.###} {bodyBg.G / 255.0:0.###} {bodyBg.B / 255.0:0.###} rg " +
            $"{BsMarginX:F2} {BsMarginY:F2} {pageWidth - 2 * BsMarginX:F2} {pageHeight - 2 * BsMarginY:F2} re f Q\n")));

        bs.yTd = BsMarginY;
        bs.lastPMarB = 0.0;
        if (bs.jumboDoc)
        {
            RenderJumbotron(bs, pageWidth, pageHeight);
        }
        foreach (var blk in bs.blocks)
        {
            if (!RenderBootstrapBlock(bs, pageWidth, pageHeight, blk)) break;
        }
        return bs.doc;
    }

    private abstract class BsBlock { }
    private sealed class BsRule : BsBlock { }
    private sealed class BsHeading : BsBlock { public string Text = ""; }
    private sealed class BsParagraph : BsBlock { public List<BsRun> Runs = new(); }
    private sealed class BsTable : BsBlock
    {
        public List<List<(string Text, bool Th)>> Rows = new();
    }

    private static List<BsBlock> ParseBootstrapBlocks(string bodyHtml, BootstrapScreenState bs, IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        var bb = new BootstrapBlocksState();
        bb.bodyHtml = bodyHtml;
        bb.bs = bs;
        bb.css = css;
        bb.blocks = new List<BsBlock>();
        bb.p = null;
        bb.table = null;
        bb.row = null;
        bb.text = new StringBuilder();
        bb.textTarget = null;                 // "h2" | "p" | "cell" | "btn"
        bb.cellTh = false;
        bb.btn = null;
        bb.linkDepth = 0;

        foreach (var tok in Tokenize(bb.bodyHtml))
        {
            ParseBootstrapToken(bb, tok);
        }
        return bb.blocks;
    }

    /// <summary>The bordered Bootstrap table: white box, #c3c6c9 collapsed grid
    /// (thead bottom 2px), columns sharing the full width in proportion to their
    /// widest cell. Returns the y (top-down) below the table box.</summary>
    private static double RenderBootstrapTable(Page page, BsTable t, double yTd,
        double contentX, double contentW, double pageHeight,
        string face, string boldFace, (double asc, double sum) fm,
        double fontSize, double lineH, double drop, double pad,
        Color borderCol, Color tableBg, Color textCol,
        BootstrapScreenState bs)
    {
        const double bw = 0.75;                    // 1px grid border
        const double thBw = 1.5;                   // thead's 2px bottom border
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        var nCols = 0;
        foreach (var r in t.Rows) nCols = Math.Max(nCols, r.Count);
        if (nCols == 0) return yTd;

        // Columns: each takes its widest cell's text plus the cell chrome, and
        // the leftover width distributes in the same proportion (the browser's
        // auto-layout fill under width:100%).
        var natural = new double[nCols];
        foreach (var r in t.Rows)
            for (var c = 0; c < r.Count; c++)
                natural[c] = Math.Max(natural[c],
                    MeasureFaceText(r[c].Th ? boldFace : face, r[c].Text, fontSize));
        double natSum = 0;
        foreach (var w in natural) natSum += w;
        var chrome = nCols * (2 * pad + bw);
        var inner = contentW - bw;                 // between the outer border centers
        var scale = natSum > 0 ? (inner - chrome) / natSum : 0;
        var edge = new double[nCols + 1];
        edge[0] = contentX + bw / 2;
        for (var c = 0; c < nCols; c++)
            edge[c + 1] = edge[c] + 2 * pad + bw + natural[c] * scale;
        edge[nCols] = contentX + contentW - bw / 2;

        // Pass 1: row boundaries (each row is one line box plus padding; the
        // thead closes with its 2px border).
        var boundaries = new List<double> { yTd + bw / 2 };
        foreach (var r in t.Rows)
        {
            var isHead = r.Count > 0 && r[0].Th;
            var halfBelow = isHead ? thBw / 2 : bw / 2;
            boundaries.Add(boundaries[^1] + bw / 2 + pad + lineH + pad + halfBelow);
        }
        var bottomEdge = boundaries[^1] + bw;      // the box closes one full border below

        // Paint order: white table box, then the grid strokes, then the text.
        page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
            $"q {tableBg.R / 255.0:0.###} {tableBg.G / 255.0:0.###} {tableBg.B / 255.0:0.###} rg " +
            $"{contentX:F2} {pageHeight - bottomEdge:F2} {contentW:F2} {bottomEdge - yTd:F2} re f Q\n")));

        var sb = new StringBuilder();
        void H(double y, double w2)
            => sb.Append(Compat.Format(invc,
                $"{w2:0.##} w {contentX:F2} {pageHeight - y:F2} m {contentX + contentW:F2} {pageHeight - y:F2} l S "));
        void V(double x, double y0, double y1)
            => sb.Append(Compat.Format(invc,
                $"{bw:0.##} w {x:F2} {pageHeight - y0:F2} m {x:F2} {pageHeight - y1:F2} l S "));
        H(boundaries[0], bw);
        for (var ri = 0; ri < t.Rows.Count; ri++)
        {
            var isHead = t.Rows[ri].Count > 0 && t.Rows[ri][0].Th;
            for (var c = 0; c <= nCols; c++)
                V(edge[c], boundaries[ri] - bw / 2, boundaries[ri + 1] + bw / 2);
            H(boundaries[ri + 1], isHead ? thBw : bw);
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
            $"q {borderCol.R / 255.0:0.###} {borderCol.G / 255.0:0.###} {borderCol.B / 255.0:0.###} RG {sb}Q\n")));

        for (var ri = 0; ri < t.Rows.Count; ri++)
        {
            var baseline = boundaries[ri] + bw / 2 + pad + drop;
            var r = t.Rows[ri];
            for (var c = 0; c < r.Count; c++)
                EmitRun(bs, pageHeight, r[c].Th ? "FB" : "FA", fontSize,
                    edge[c] + bw / 2 + pad, baseline, r[c].Text, textCol);
        }
        return bottomEdge;
    }
}
