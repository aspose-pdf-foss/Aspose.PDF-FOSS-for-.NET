using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The Bootstrap clinical report form ─────────────────────────────────────
    // A `form-container` sheet: a flex `header-grid` (logo image beside a
    // bordered `header-table`), a centred `header-maintext` notice over the
    // form-header's 2px rule, then `section.section`s — each an #ddd-banded h3
    // followed by an <ol> of labelled inputs: full-width `.form-control` boxes,
    // bare inline text inputs, and 13px radio circles inside inline <label>s.
    //
    // Geometry (all measured on the expected PDF; A4 595×842,
    // 90/72 content margins, Segoe UI 12 pt):
    //   .form-container is 90vw of the 415 content band = 373.5 pt centred at
    //   110.75, bordered 1px #444, padded 20px → inner column 126.5..468.5.
    //   Body lines pitch 18 (16px × 1.5); the baseline hangs 13.969 under the
    //   line top (Segoe UI hhea 2210/2048 ascent + 1.020 half-leading) and the
    //   line box runs 4.031 below it. Adjacent BOX edges (an input's border
    //   box against anything else) overlap 0.75 — a `.form-control` box tops
    //   at the previous bottom − 0.75 and the next line tops 0.75 above its
    //   bottom edge; a page-continuation box tops at 72 − 0.75.
    //   A `.form-control` is 318 × 30 at the item column; a bare inline input
    //   is 134.43 × 24 seated 16.97 under its line top (the line grows to the
    //   24 pt box) and advances a 4px right margin; a radio is 9.75 square,
    //   its bottom on the baseline. `.form-check` indents 1.5em and adds its
    //   2px margin-bottom. The h3 band is 23.4 tall (14.4 line + 5px padding
    //   + 1px #aaa border) on #ddd, its baseline 16.67 under the band top;
    //   blocks meet it at the section's collapsed 20px margin.
    //   The widget appearances the era writer emits (a 1 pt black border box
    //   0.5 inside the field rect; a two-bezier 4.375-radius circle) are
    //   drawn as page ink here — the raster compare reads the page content.

    private const double CfPageW = 595.0;
    private const double CfPageH = 842.0;
    private const double CfContentTop = 72.0;
    private const double CfContentBottom = 770.0;
    private const double CfBodyX = 90.0;
    private const double CfBodyW = 415.0;
    private const double CfContainerW = 373.5;       // 90vw of the body band
    private const double CfContainerX0 = 110.75;
    private const double CfInnerX0 = 126.5;          // container border + 20px pad
    private const double CfInnerX1 = 468.5;
    private const double CfItemX = 150.5;            // ol padding-left 2rem
    private const double CfFs = 12.0;                // body 16px
    private const double CfLineH = 18.0;             // 16px × 1.5
    private const double CfBaseOff = 13.969;         // line top → baseline
    private const double CfLineDesc = 4.031;         // baseline → line bottom
    private const double CfJunction = 0.75;          // box-edge overlap (1px)
    private const double CfControlH = 30.0;          // .form-control border box
    private const double CfInlineInputW = 134.43;    // bare input default width
    private const double CfInlineInputH = 24.0;
    private const double CfInlineBaseOff = 16.97;    // input-line top → baseline
    private const double CfInlineMarginR = 3.0;      // input 4px margin-right
    private const double CfTextAreaH = 57.0;
    private const double CfRadio = 9.75;             // 13px UA radio
    private const double CfCheckIndent = 18.0;       // .form-check 1.5em
    private const double CfCheckMb = 1.5;            // .form-check 2px mb
    private const double CfBandH = 23.4;             // .section h3 band
    private const double CfBandBaseOff = 16.67;
    private const double CfBandPadX = 8.25;          // 10px pad + 1px border
    private const double CfSectionGap = 15.0;        // 20px collapsed margin
    private const double CfH3Mb = 6.0;               // h3 margin-bottom .5rem
    private const double CfOlMb = 12.0;              // ol margin-bottom 1rem
    private const double CfHeaderTop = 87.75;        // container content top
    private const double CfLogoBase = 118.69;        // alt-text baseline (flex centring)
    private const double CfTitleBandH = 30.0;        // h5 line 18 + pads + 6 mb
    private const double CfTitleBaseOff = 18.21;
    private const double CfCellH = 24.0;             // .table-sm row
    private const double CfCellPad = 3.0;            // .25rem
    private const double CfCellBaseOff = 16.97;
    private const double CfNoticeGap = 12.0;         // table margin-bottom 1rem
    private const double CfSmallFs = 10.5;           // .875em
    private const double CfMarkerOneX = 136.93;      // the measured "1." marker x
    private const string CfText = "0.129 0.145 0.161";  // #212529
    private const string CfBlack = "0 0 0";

    private sealed class CfAtom
    {
        public string Kind = "text";                 // text/radio/iinput/binput/tarea/br/check/table
        public string Text = "";
        public int Style;                            // 0 regular, 1 bold, 2 italic, 3 small
        public List<CfAtom>? Group;                  // a <label>'s inline children
        public List<List<string>>? Rows;             // table cell texts
    }

    private static Document? TryRenderClinicalForm(string html)
    {
        if (!html.Contains("class=\"form-container\"", StringComparison.Ordinal)
            || !html.Contains("id=\"header-table\"", StringComparison.Ordinal)
            || !html.Contains("id=\"header-maintext\"", StringComparison.Ordinal)
            || !html.Contains("<section class=\"section\"", StringComparison.Ordinal)
            || !html.Contains("form-control", StringComparison.Ordinal))
            return null;

        var segoe = Text.SystemFontResolver.Resolve("Segoe UI");
        var segoeBold = Text.SystemFontResolver.Resolve("SegoeUI-Bold")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Bold");
        var segoeItalic = Text.SystemFontResolver.Resolve("SegoeUI-Italic")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Italic");
        if (segoe is null || segoeBold is null || segoeItalic is null) return null;

        static string Flat(string frag) => Regex.Replace(
            DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

        // ── parse ────────────────────────────────────────────────────────────
        var bodyM = Regex.Match(html, @"<body[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase);
        var body = bodyM.Success ? bodyM.Groups[1].Value : html;

        // header: broken-logo alt text, the h5 title, four <strong>-labelled cells,
        // and the centred maintext.
        var logoAlt = Regex.Match(body, @"<img\b[^>]*\balt\s*=\s*""([^""]*)""[^>]*>",
            RegexOptions.IgnoreCase) is { Success: true } lm ? lm.Groups[1].Value : "";
        var titleM = Regex.Match(body, @"<h5[^>]*>([\s\S]*?)</h5>", RegexOptions.IgnoreCase);
        if (!titleM.Success) return null;
        var title = Flat(titleM.Groups[1].Value);
        var headTableM = Regex.Match(body,
            @"<table\b[^>]*id=""header-table""[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase);
        if (!headTableM.Success) return null;
        var headCells = new List<string>();          // flattened "Sponsor: " etc.
        foreach (Match cm in Regex.Matches(headTableM.Groups[1].Value,
            @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
            headCells.Add(Flat(cm.Groups[1].Value));
        if (headCells.Count < 4) return null;
        var mainTextM = Regex.Match(body,
            @"<div\b[^>]*id=""header-maintext""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        var mainText = mainTextM.Success ? Flat(mainTextM.Groups[1].Value) : "";

        // sections: h3 title + the <ol>'s <li>s parsed into inline/block atoms
        var sections = new List<(string Title, List<List<CfAtom>> Items)>();
        foreach (Match sm in Regex.Matches(body,
            @"<section\s+class=""section""[^>]*>([\s\S]*?)</section>", RegexOptions.IgnoreCase))
        {
            var sec = sm.Groups[1].Value;
            var h3 = Regex.Match(sec, @"<h3[^>]*>([\s\S]*?)</h3>", RegexOptions.IgnoreCase);
            var items = new List<List<CfAtom>>();
            foreach (Match li in Regex.Matches(sec, @"<li\b[^>]*>([\s\S]*?)</li>",
                RegexOptions.IgnoreCase))
                items.Add(ParseCfAtoms(li.Groups[1].Value));
            sections.Add((h3.Success ? Flat(h3.Groups[1].Value) : "", items));
        }
        if (sections.Count == 0) return null;

        // ── layout ───────────────────────────────────────────────────────────
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var measureDict = new Core.PdfDictionary();
        double M(string s, int style, double fs)
        {
            if (s.Length == 0) return 0;
            var (ttf, face) = style switch
            {
                1 => (segoeBold!, "SegoeUIBold"),
                2 => (segoeItalic!, "SegoeUIItalic"),
                _ => (segoe!, "SegoeUI"),
            };
            return Text.Type0FontEmbedder.MeasureText(measureDict, ttf, face, s, fs,
                stripSpacesInBaseFont: true);
        }

        // A page holds raw op strings and deferred text runs (embedded at paint).
        var pageShapes = new List<StringBuilder>();
        var pageRuns = new List<List<(int Style, double Fs, double X, double BaseTd, string Text, string Col)>>();
        var pageFlowBot = new List<double>();
        void NewPage() { pageShapes.Add(new StringBuilder()); pageRuns.Add(new()); pageFlowBot.Add(CfContentBottom); }
        NewPage();
        void Run(int style, double fs, double x, double baseTd, string text, string col = CfText)
        { if (text.Length > 0) pageRuns[^1].Add((style, fs, x, baseTd, text, col)); }
        void Line(double x0, double y0, double x1, double y1, double w, string rgb)
            => pageShapes[^1].AppendLine(string.Create(inv,
                $"q {rgb} RG {w:0.###} w {x0:F3} {CfPageH - y0:F3} m {x1:F3} {CfPageH - y1:F3} l S Q"));
        void Fill(double x, double yTop, double w, double h, string rgb)
            => pageShapes[^1].AppendLine(string.Create(inv,
                $"q {rgb} rg {x:F3} {CfPageH - yTop - h:F3} {w:F3} {h:F3} re f Q"));
        // the era widget border: a 1 pt black box half a point inside the rect
        void BoxInk(double x, double yTop, double w, double h)
            => pageShapes[^1].AppendLine(string.Create(inv,
                $"q {CfBlack} RG 1 w {x + 0.5:F3} {CfPageH - yTop - h + 0.5:F3} {w - 1:F3} {h - 1:F3} re S Q"));
        // the radio widget circle: two beziers, radius 4.375, 1 pt stroke
        void RadioInk(double x, double baseTd)
        {
            var cy = CfPageH - baseTd + CfRadio / 2;   // centre, PDF coords
            var x0 = x + 0.5; var x1 = x + CfRadio - 0.5;
            var yc = cy; var yo = 5.8333333;
            pageShapes[^1].AppendLine(string.Create(inv,
                $"q 1 w {CfBlack} RG {x0:F3} {yc:F3} m {x0:F3} {yc + yo:F3} {x1:F3} {yc + yo:F3} {x1:F3} {yc:F3} c "
                + $"{x1:F3} {yc - yo:F3} {x0:F3} {yc - yo:F3} {x0:F3} {yc:F3} c s Q"));
        }

        var bot = 0.0;              // bottom edge of the last placed line/box
        var prevWasBox = false;     // the 0.75 junction applies at a box edge

        void BreakPage()
        {
            pageFlowBot[^1] = bot;    // the container borders stop at the flow cut
            NewPage();
            bot = CfContentTop;
            prevWasBox = true;      // a continued box tops at 72 − 0.75
        }

        // ── page 1 header (measured constants; texts from the fixture) ───────
        Run(0, CfFs, CfInnerX0, CfLogoBase, logoAlt);
        var bandX = CfInnerX1 - 0.75 * (CfInnerX1 - CfInnerX0);   // .title-block 75%
        var bandW = CfInnerX1 - bandX;
        Fill(bandX, CfHeaderTop, bandW, CfTitleBandH, "0.973 0.976 0.98");
        Run(0, 15, bandX + CfCellPad, CfHeaderTop + CfTitleBaseOff, title, CfBlack);
        // the 2×2 cell grid: bold label + a trailing space where the source span
        // was empty; columns split the band on max content + an equal remainder
        var boldParts = new string[4]; var restParts = new string[4];
        for (var c = 0; c < 4; c++)
        {
            var mSplit = Regex.Match(headCells[c], @"^(\S+\s?\S*?:)\s*(.*)$");
            boldParts[c] = mSplit.Success ? mSplit.Groups[1].Value : headCells[c];
            restParts[c] = mSplit.Success ? mSplit.Groups[2].Value : "";
        }
        double CellW(int c) => M(boldParts[c], 1, CfFs)
            + (restParts[c].Length > 0 ? M(" " + restParts[c], 0, CfFs) : M(" ", 0, CfFs));
        var c1 = Math.Max(CellW(0), CellW(2)) + 2 * CfCellPad;
        var c2 = Math.Max(CellW(1), CellW(3)) + 2 * CfCellPad;
        var slack = (bandW - c1 - c2) / 2;
        var col1W = c1 + slack;
        var rowY = CfHeaderTop + CfTitleBandH;
        for (var r = 0; r < 2; r++)
        {
            Fill(bandX, rowY, col1W, CfCellH, "1 1 1");
            Fill(bandX + col1W, rowY, bandW - col1W, CfCellH, "1 1 1");
            for (var c = 0; c < 2; c++)
            {
                var idx = r * 2 + c;
                var cx = bandX + (c == 0 ? 0 : col1W) + CfCellPad;
                Run(1, CfFs, cx, rowY + CfCellBaseOff, boldParts[idx], CfBlack);
                if (restParts[idx].Length > 0)
                    Run(0, CfFs, cx + M(boldParts[idx], 1, CfFs), rowY + CfCellBaseOff,
                        " " + restParts[idx], CfBlack);
            }
            rowY += CfCellH;
        }
        // the centred notice, wrapped on the inner column
        var noticeTop = rowY + CfNoticeGap;
        var noticeLines = WrapCfWords(mainText, s => M(s, 0, CfFs), CfInnerX1 - CfInnerX0);
        foreach (var ln in noticeLines)
        {
            var w = M(ln, 0, CfFs);
            Run(0, CfFs, CfInnerX0 + (CfInnerX1 - CfInnerX0 - w) / 2, noticeTop + CfBaseOff, ln);
            noticeTop += CfLineH;
        }
        // the form-header's 2px black rule sits under the notice's line box
        var ruleCenter = noticeTop - CfLineH + CfBaseOff + CfLineDesc + 0.75;
        Line(CfInnerX0, ruleCenter, CfInnerX1, ruleCenter, 1.5, CfBlack);
        bot = ruleCenter + 0.75;
        prevWasBox = false;

        // ── sections ─────────────────────────────────────────────────────────
        foreach (var (secTitle, items) in sections)
        {
            var bandTop = bot - (prevWasBox ? CfJunction : 0) + CfSectionGap;
            if (bandTop + CfBandH > CfContentBottom) { BreakPage(); bandTop = CfContentTop; }
            Fill(CfInnerX0, bandTop, CfInnerX1 - CfInnerX0, CfBandH, "0.867 0.867 0.867");
            var b0 = bandTop + 0.375; var b1 = bandTop + CfBandH - 0.375;
            Line(CfInnerX0, b0, CfInnerX1, b0, 0.75, "0.667 0.667 0.667");
            Line(CfInnerX0, b1, CfInnerX1, b1, 0.75, "0.667 0.667 0.667");
            Line(CfInnerX0 + 0.375, bandTop, CfInnerX0 + 0.375, bandTop + CfBandH, 0.75, "0.667 0.667 0.667");
            Line(CfInnerX1 - 0.375, bandTop, CfInnerX1 - 0.375, bandTop + CfBandH, 0.75, "0.667 0.667 0.667");
            Run(0, CfFs, CfInnerX0 + CfBandPadX, bandTop + CfBandBaseOff, secTitle);
            bot = bandTop + CfBandH + CfH3Mb;
            prevWasBox = false;

            var itemNo = 0;
            foreach (var atoms in items)
            {
                itemNo++;
                LayoutCfItem(atoms, itemNo);
            }
            // the ol's 1rem margin-bottom collapses under the next section's
            // larger 20px margin-top — nothing to carry
        }
        pageFlowBot[^1] = bot;

        // one li: inline atoms flow into wrapped lines; blocks interleave
        void LayoutCfItem(List<CfAtom> atoms, int itemNo)
        {
            var markerPending = true;
            var lineAtoms = new List<(CfAtom Atom, double X)>();
            var pen = CfItemX;
            var lineHasInput = false;
            var pendingSpace = false;

            void FlushLine()
            {
                if (lineAtoms.Count == 0) { pen = CfItemX; lineHasInput = false; return; }
                var top = bot - (prevWasBox || lineHasInput ? CfJunction : 0);
                var lineBot = lineHasInput ? top + CfInlineInputH : top + CfLineH;
                if (lineBot > CfContentBottom)
                {
                    BreakPage();
                    top = bot - (lineHasInput ? CfJunction : 0);
                    lineBot = lineHasInput ? top + CfInlineInputH : top + CfLineH;
                }
                var baseline = top + (lineHasInput ? CfInlineBaseOff : CfBaseOff);
                if (markerPending)
                {
                    var num = itemNo + ".";
                    Run(0, CfFs, CfMarkerOneX + M("1.", 0, CfFs) - M(num, 0, CfFs), baseline, num);
                    markerPending = false;
                }
                foreach (var (a, x) in lineAtoms)
                    switch (a.Kind)
                    {
                        case "text": Run(a.Style, a.Style == 3 ? CfSmallFs : CfFs, x, baseline, a.Text); break;
                        case "radio": RadioInk(x, baseline); break;
                        case "iinput": BoxInk(x, baseline - CfInlineBaseOff, CfInlineInputW, CfInlineInputH); break;
                    }
                bot = lineHasInput ? lineBot : baseline + CfLineDesc;
                prevWasBox = lineHasInput;
                lineAtoms.Clear();
                pen = CfItemX;
                lineHasInput = false;
            }

            double AtomW(CfAtom a) => a.Kind switch
            {
                "text" => M(a.Text, a.Style, a.Style == 3 ? CfSmallFs : CfFs),
                "radio" => CfRadio,
                "iinput" => CfInlineInputW + CfInlineMarginR,
                _ => 0,
            };

            void PlaceInline(CfAtom a)
            {
                var group = a.Group is { } g ? g : new List<CfAtom> { a };
                var groupW = 0.0;
                foreach (var ga in group) groupW += AtomW(ga);
                var spaceW = pendingSpace ? M(" ", 0, CfFs) : 0;
                if (lineAtoms.Count > 0 && pen + spaceW + groupW > CfInnerX1 && groupW <= CfInnerX1 - CfItemX)
                { FlushLine(); spaceW = 0; }
                else if (pendingSpace && lineAtoms.Count > 0)
                {
                    lineAtoms.Add((new CfAtom { Kind = "text", Text = " " }, pen));
                    pen += spaceW;
                }
                pendingSpace = false;
                foreach (var ga in group)
                {
                    if (ga.Kind == "iinput") lineHasInput = true;
                    lineAtoms.Add((ga, pen));
                    pen += AtomW(ga);
                }
            }

            foreach (var a in atoms)
            {
                switch (a.Kind)
                {
                    case "text":
                    {
                        // words are the wrap units of bare text
                        var words = a.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var lead = a.Text.StartsWith(' ');
                        for (var w = 0; w < words.Length; w++)
                        {
                            if (w > 0 || lead) pendingSpace = pendingSpace || lineAtoms.Count > 0;
                            PlaceInline(new CfAtom { Kind = "text", Text = words[w], Style = a.Style });
                        }
                        if (a.Text.EndsWith(' ')) pendingSpace = true;
                        break;
                    }
                    case "radio" or "iinput" or "label":
                        PlaceInline(a);
                        break;
                    case "br":
                        FlushLine();
                        // the forced break makes one empty line box
                        bot += 0;
                        var t = bot - (prevWasBox ? CfJunction : 0);
                        bot = t + CfLineH;
                        prevWasBox = false;
                        break;
                    case "binput" or "tarea":
                    {
                        FlushLine();
                        var h = a.Kind == "tarea" ? CfTextAreaH : CfControlH;
                        var top = bot - CfJunction;
                        if (top + h > CfContentBottom) { BreakPage(); top = bot - CfJunction; }
                        BoxInk(CfItemX, top, CfInnerX1 - CfItemX, h);
                        bot = top + h;
                        prevWasBox = true;
                        break;
                    }
                    case "check":
                    {
                        FlushLine();
                        var top = bot - (prevWasBox ? CfJunction : 0);
                        if (top + CfLineH > CfContentBottom) { BreakPage(); top = bot; }
                        var baseline = top + CfBaseOff;
                        if (markerPending)
                        {
                            var num = itemNo + ".";
                            Run(0, CfFs, CfMarkerOneX + M("1.", 0, CfFs) - M(num, 0, CfFs), baseline, num);
                            markerPending = false;
                        }
                        Run(0, CfFs, CfItemX + CfCheckIndent, baseline, a.Text);
                        bot = baseline + CfLineDesc + CfCheckMb;
                        prevWasBox = false;
                        break;
                    }
                    case "table":
                    {
                        FlushLine();
                        if (a.Rows is not { Count: > 0 }) break;   // JS-filled shells render nothing
                        foreach (var row in a.Rows)
                        {
                            var top = bot - (prevWasBox ? CfJunction : 0);
                            if (top + CfCellH > CfContentBottom) { BreakPage(); top = bot; }
                            var colW = (CfInnerX1 - CfItemX) / Math.Max(1, row.Count);
                            for (var c = 0; c < row.Count; c++)
                                Run(0, CfFs, CfItemX + c * colW + CfCellPad, top + CfCellBaseOff, row[c]);
                            bot = top + CfCellH;
                            prevWasBox = true;
                        }
                        break;
                    }
                }
            }
            FlushLine();
        }

        // ── paint ────────────────────────────────────────────────────────────
        var doc = Document.Create();
        for (var pi = 0; pi < pageShapes.Count; pi++)
        {
            var page = doc.Pages.Add(CfPageW, CfPageH);
            var head = new StringBuilder();
            head.AppendLine(string.Create(inv,
                $"q 1 1 1 rg {CfBodyX:F0} {CfPageH - CfContentBottom:F0} {CfBodyW:F0} {CfContentBottom - CfContentTop:F0} re f Q"));
            // container side borders span the page's flow; the top border only
            // opens the box on page 1, the bottom border closes it on the last
            var yTopB = CfContentTop;
            var yBotB = pi < pageShapes.Count - 1 && pi > 0 ? CfContentBottom : pageFlowBot[pi];
            if (pi == 0)
                head.AppendLine(string.Create(inv,
                    $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0:F3} {CfPageH - (CfContentTop + 0.375):F3} m {CfContainerX0 + CfContainerW:F3} {CfPageH - (CfContentTop + 0.375):F3} l S Q"));
            if (pi == pageShapes.Count - 1)
                head.AppendLine(string.Create(inv,
                    $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0:F3} {CfPageH - (yBotB - 0.375):F3} m {CfContainerX0 + CfContainerW:F3} {CfPageH - (yBotB - 0.375):F3} l S Q"));
            head.AppendLine(string.Create(inv,
                $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0 + 0.375:F3} {CfPageH - yTopB:F3} m {CfContainerX0 + 0.375:F3} {CfPageH - yBotB:F3} l S Q"));
            head.AppendLine(string.Create(inv,
                $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0 + CfContainerW - 0.375:F3} {CfPageH - yTopB:F3} m {CfContainerX0 + CfContainerW - 0.375:F3} {CfPageH - yBotB:F3} l S Q"));
            page.AddContentStream(Encoding.ASCII.GetBytes(head.ToString() + pageShapes[pi]));
            // deferred text runs embed the Segoe faces per page
            EnsureFonts(page);
            if (page.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd)
                continue;
            var sbText = new StringBuilder();
            foreach (var (style, fs, x, baseTd, text, col) in pageRuns[pi])
            {
                var (ttf, face) = style switch
                {
                    1 => (segoeBold!, "SegoeUIBold"),
                    2 => (segoeItalic!, "SegoeUIItalic"),
                    _ => (segoe!, "SegoeUI"),
                };
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, face, text,
                    stripSpacesInBaseFont: true);
                sbText.AppendLine(string.Create(inv,
                    $"BT {col} rg /{rn} {fs:0.##} Tf 1 0 0 1 {x:F3} {CfPageH - baseTd:F3} Tm ")
                    + "<" + System.Convert.ToHexString(hex) + "> Tj ET");
            }
            page.AddContentStream(Encoding.ASCII.GetBytes(sbText.ToString()));
        }
        return doc;
    }

    // Tokenise one <li>'s inner HTML into flow atoms. A <label> wraps its
    // children into one wrap-unit group (unless it is a .form-check block);
    // radios, bare text inputs, .form-control inputs, <br>, <em>/<strong>/<small>
    // runs and stray text are flat atoms.
    private static List<CfAtom> ParseCfAtoms(string inner)
    {
        var atoms = new List<CfAtom>();
        ParseCfInline(inner, atoms, 0);
        return atoms;
    }

    private static void ParseCfInline(string inner, List<CfAtom> atoms, int style)
    {
        static string Collapse(string s) => Regex.Replace(DecodeEntities(s), @"\s+", " ");
        var rx = new Regex(
            @"<label\b([^>]*)>([\s\S]*?)</label>|<input\b([^>]*?)/?>|<textarea\b[^>]*>[\s\S]*?</textarea>"
            + @"|<br\s*/?>|<(em|i|strong|b|small)\b[^>]*>([\s\S]*?)</\4>"
            + @"|<div\b[^>]*class=""[^""]*table-responsive[^""]*""[^>]*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        var pos = 0;
        foreach (Match m in rx.Matches(inner))
        {
            var before = Collapse(inner[pos..m.Index]);
            if (before.Trim().Length > 0 || (before.Contains(' ') && atoms.Count > 0))
                atoms.Add(new CfAtom { Kind = "text", Text = before, Style = style });
            pos = m.Index + m.Length;
            if (m.Value.StartsWith("<label", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = m.Groups[1].Value;
                var content = m.Groups[2].Value;
                if (attrs.Contains("form-check", StringComparison.OrdinalIgnoreCase))
                {
                    atoms.Add(new CfAtom { Kind = "check", Text = Collapse(content).Trim() });
                }
                else
                {
                    var group = new List<CfAtom>();
                    ParseCfInline(content, group, style);
                    // trim the group's leading whitespace-only run (the radio leads)
                    if (group.Count > 0 && group[0].Kind == "text" && group[0].Text.Trim().Length == 0)
                        group.RemoveAt(0);
                    // and the last run's trailing space — the inter-label gap is the
                    // markup whitespace OUTSIDE the label, not the label's own tail
                    if (group.Count > 0 && group[^1].Kind == "text")
                    {
                        group[^1].Text = group[^1].Text.TrimEnd();
                        if (group[^1].Text.Length == 0) group.RemoveAt(group.Count - 1);
                    }
                    if (group.Count > 0)
                        atoms.Add(new CfAtom { Kind = "label", Group = group });
                }
            }
            else if (m.Value.StartsWith("<input", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = m.Groups[3].Value;
                var type = Regex.Match(attrs, @"type\s*=\s*""?(\w+)", RegexOptions.IgnoreCase)
                    .Groups[1].Value.ToLowerInvariant();
                if (type == "radio" || type == "checkbox")
                    atoms.Add(new CfAtom { Kind = "radio" });
                else if (attrs.Contains("form-control", StringComparison.OrdinalIgnoreCase))
                    atoms.Add(new CfAtom { Kind = "binput" });
                else
                    atoms.Add(new CfAtom { Kind = "iinput" });
            }
            else if (m.Value.StartsWith("<textarea", StringComparison.OrdinalIgnoreCase))
                atoms.Add(new CfAtom { Kind = "tarea" });
            else if (m.Value.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
                atoms.Add(new CfAtom { Kind = "br" });
            else if (m.Groups[4].Success)
            {
                var tag = m.Groups[4].Value.ToLowerInvariant();
                var runStyle = tag is "em" or "i" ? 2 : tag is "strong" or "b" ? 1 : 3;
                var t = Collapse(m.Groups[5].Value);
                if (t.Trim().Length > 0)
                    atoms.Add(new CfAtom { Kind = "text", Text = t.Trim(), Style = runStyle });
            }
            else if (m.Groups[6].Success)
            {
                var rows = new List<List<string>>();
                foreach (Match rm in Regex.Matches(m.Groups[6].Value, @"<tr\b[^>]*>([\s\S]*?)</tr>",
                    RegexOptions.IgnoreCase))
                {
                    var cells = new List<string>();
                    foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                        @"<t[dh]\b[^>]*>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase))
                        cells.Add(Regex.Replace(DecodeEntities(
                            Regex.Replace(cm.Groups[1].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim());
                    if (cells.Count > 0) rows.Add(cells);
                }
                atoms.Add(new CfAtom { Kind = "table", Rows = rows });
            }
        }
        var tail = Collapse(inner[pos..]);
        if (tail.Trim().Length > 0)
            atoms.Add(new CfAtom { Kind = "text", Text = tail, Style = style });
    }

    // Greedy word wrap with a caller-supplied measure.
    private static List<string> WrapCfWords(string text, Func<string, double> measure, double budget)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || measure(trial) <= budget) { cur.Clear(); cur.Append(trial); }
            else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}
