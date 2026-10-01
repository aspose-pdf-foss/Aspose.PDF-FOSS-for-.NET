using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// THE FLOAT-BOX DOCUMENT: a standards-mode page whose stylesheet lays its blocks out by CSS
// floats - class rules that float sized divs and spans left and right (the float-built invoice:
// address blocks, bordered heading spans, a collapsed line-item table, footer panels; the
// WorkflowGen form: floated caption columns beside inline-block value boxes, text inputs,
// textareas and radio options, a bordered gridview). The reference converter lays it out as
// CSS 2.1 floats do (probed on synthetic fixtures): a float's containing block
// is its parent's content box, the first right float is the rightmost, a float that does not
// fit drops below the earlier float bottom that frees the width and then adds its margin-top,
// a float wider than its container anchors to its side and overflows the other, a widthless
// float shrinks to the larger of its floats' row and its line content, floats are formatting
// contexts that contain their floats while plain blocks are not; a floated span is a block of
// its own font's line; a block's line box is the strut of its own font (hhea ascent + descent +
// line gap, rounded to px) and its inlines sit on that baseline; a collapsed table with
// declared cell widths scales (declared + padding + border) columns into (table - border) and
// bands its rows on the cell font's strut; form controls are AcroForm widgets whose box is the
// CSS width by the line plus the UA chrome; a line-level block that misses the page band moves
// whole to the next sheet; the sheet is laid out on A4 first and then re-laid on
// max(A4, rightmost ink + 90), ink being a border, a fill or a text run.
internal static partial class HtmlToPdfConverter
{
    private const double FbPxPt = 0.75;
    private const double FbPageMarginX = 90.0;                // the UA sheet's side margins
    private const double FbPageMarginTop = 72.0;
    private const double FbPageMarginBottom = 72.0;
    private const double FbUaBodyMarginPx = 8.0;              // the UA body margin
    private const double FbDefaultFontPx = 16.0;              // the UA root size
    private const double FbA4WidthPt = 595.0;
    private const double FbA4HeightPt = 842.0;
    private const double FbMediumBorderPx = 3.0;              // `border: solid` with no width
    private const int FbMinFloatRules = 3;                    // the sheet lays out by floats
    private const double FbEpsilon = 1e-6;
    private const string FbSerifMeasureFace = "Times New Roman";
    private const string FbSerifBoldMeasureFace = "Times New Roman-Bold";
    private const string FbSansMeasureFace = "Arial";
    private const string FbSansBoldMeasureFace = "Arial Bold";
    private const string FbSerifFontName = "TimesNewRoman";
    private const string FbSerifBoldFontName = "TimesNewRoman-Bold";
    private const string FbSansFontName = "Arial";
    private const string FbSansBoldFontName = "ArialBold";
    private const string FbSerifRes = "F20";
    private const string FbSerifBoldRes = "F21";
    private const string FbSansRes = "F22";
    private const string FbSansBoldRes = "F23";
    // hhea metrics of the two faces (em fractions), the fallback when the program cannot be read
    private const double FbSerifHheaAsc = 1825.0 / 2048.0;
    private const double FbSerifHheaDesc = 443.0 / 2048.0;
    private const double FbSansHheaAsc = 1854.0 / 2048.0;
    private const double FbSansHheaDesc = 434.0 / 2048.0;
    private const double FbHheaLine = 2355.0 / 2048.0;

    /// <summary>Per-call working state of the float-box render. One instance per invocation; never shared.</summary>
    private sealed class FbState
    {
        public System.Globalization.CultureInfo invc = null!;
        public IReadOnlyDictionary<string, Dictionary<string, string>> css = null!;
        public HtmlNode body = null!;
        public double bodyMarginPt;                            // the body's margin (UA 6, or its own)
        public double pageW, pageH;
        public double bandTop;                                 // the first sheet's content top (72 + body margin)
        public double bandBottom;                              // page bottom margin edge
        public double laterPageStep;                           // the band a later page holds (from its 72 top)
        public FbBox root = null!;
        public Document doc = null!;
        public List<Page> pageObjs = new();
        public List<StringBuilder> pages = new();              // content streams per page
        public Dictionary<string, (double asc, double desc, double line)> faceMetrics = new();
        public Dictionary<string, (Forms.RadioButtonField field, int options)> radioGroups = new();
        public List<(string anc, string tag, string type, Dictionary<string, string> decls)> attrRules = new();
        public bool quirks;                                    // the quirks inline-styled claim (see FloatBoxReplaced.cs)
        public Core.PdfIndirectRef? iconRef;                   // the broken-image icon, built on first use
        public FbSheet? sheet;                                 // the styled-sheet claim's cascade and faces (see FloatBoxSheet.cs)
    }

    /// <summary>Render the float-box document, or null when none of the engine's claims holds:
    /// the float-laid sheet, the quirks inline-styled document, the styled sheet.</summary>
    private static Document? TryRenderFloatBoxDocument(ConvertState cv, HtmlLoadOptions? options)
    {
        var claim = FbClaims(cv) ? "floats" : FbQuirksClaims(cv) ? "quirks" : FbSheetClaims(cv, options) ? "sheet" : null;
        if (claim is null) return null;
        FbSheet? styled = null;
        if (claim == "sheet")
        {
            // (the sheet must SHIP a face the page's folder holds - a sheet whose programs are
            //  missing is another dialect's document)
            styled = FbReadSheet(cv.html, options, (FbA4WidthPt - 2 * FbPageMarginX) / FbPxPt, (FbA4HeightPt - FbPageMarginTop - FbPageMarginBottom) / FbPxPt);
            if (styled.Faces.Count == 0)
            {
                if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "1") Console.Error.WriteLine("[fb] sheet declined: no loadable face");
                return null;
            }
        }
        var css = cv.css ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var cleaned = Regex.Replace(cv.html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<!--[\s\S]*?-->", "");
        var dom = ParseDom(cleaned);
        var body = FbFindTag(dom, "body") ?? dom;
        var fb = new FbState
        {
            invc = System.Globalization.CultureInfo.InvariantCulture,
            css = css,
            body = body,
            pageH = FbA4HeightPt,
            quirks = claim == "quirks",
        };
        fb.sheet = styled;
        fb.bodyMarginPt = FbBodyMarginPt(fb, body);
        FbReadAttrRules(fb, cv.html);
        fb.bandBottom = fb.pageH - FbPageMarginBottom;
        fb.laterPageStep = fb.bandBottom - FbPageMarginTop;
        fb.bandTop = FbPageMarginTop + fb.bodyMarginPt;
        // pass 1 on A4: the rightmost ink sizes the sheet; pass 2 lays out on it
        FbLayoutDocument(fb, FbA4WidthPt);
        var ink = FbInkRight(fb.root);
        var sheet = Math.Max(FbA4WidthPt, ink + FbPageMarginX);
        if (sheet > FbA4WidthPt + FbEpsilon) FbLayoutDocument(fb, sheet);
        fb.pageW = sheet;
        var pageCount = FbPageOf(fb, Math.Max(fb.root.Y + fb.root.H, FbDeepBottom(fb.root)) - FbEpsilon).page + 1;
        fb.doc = new Document();
        for (var i = 0; i < pageCount; i++)
        {
            var page = fb.doc.Pages.Add(fb.pageW, fb.pageH);
            EnsureFonts(page);
            EnsureFont(page, FbSerifFontName, FbSerifRes);
            EnsureFont(page, FbSerifBoldFontName, FbSerifBoldRes);
            EnsureFont(page, FbSansFontName, FbSansRes);
            EnsureFont(page, FbSansBoldFontName, FbSansBoldRes);
            fb.pageObjs.Add(page);
            fb.pages.Add(new StringBuilder());
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "2") FbTraceBoxes(fb.root, 0);
        FbPaintTree(fb, fb.root);
        for (var i = 0; i < pageCount; i++)
            fb.pageObjs[i].AddContentStream(Encoding.ASCII.GetBytes(fb.pages[i].ToString()));
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "1")
            Console.Error.WriteLine($"[fb] ink={ink:0.###} sheet={sheet:0.###} pages={pageCount} claim={claim}");
        return fb.doc;
    }

    /// <summary>The quirks inline-styled claim (FloatBoxReplaced.cs), traced like the float one.</summary>
    private static bool FbQuirksClaims(ConvertState cv)
    {
        var why = FbQuirksDeclines(cv);
        if (why is not null && Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "1") Console.Error.WriteLine($"[fb] quirks declined: {why}");
        return why is null;
    }

    /// <summary>The styled-sheet claim (FloatBoxSheet.cs), traced like the float one.</summary>
    private static bool FbSheetClaims(ConvertState cv, HtmlLoadOptions? options)
    {
        var why = FbSheetDeclines(cv, options);
        if (why is not null && Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "1") Console.Error.WriteLine($"[fb] sheet declined: {why}");
        return why is null;
    }

    /// <summary>The claim: a standards-mode sheet with several class rules floating boxes,
    /// nothing positioned, no images, no explicit page margins.</summary>
    private static bool FbClaims(ConvertState cv)
    {
        var trace = Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "1";
        var why = FbDeclines(cv);
        if (trace && why is not null) Console.Error.WriteLine($"[fb] declined: {why}");
        return why is null;
    }

    /// <summary>Why the float-box arm declines a document, or null when it claims it.</summary>
    private static string? FbDeclines(ConvertState cv)
    {
        if (cv.marginsExplicit || cv.css is null || cv.css.Count == 0) return "margins or no sheet";
        // (a Transitional doctype that names its system identifier is standards mode - probed)
        if (ReadsInQuirksMode(cv.html) && !ReadsInLimitedQuirks(cv.html)) return "quirks";
        if (Regex.IsMatch(cv.html, @"<(img|iframe|svg|object|embed)\b", RegexOptions.IgnoreCase)) return "replaced content";
        if (Regex.IsMatch(cv.html, @"style\s*=\s*[""'][^""']*position\s*:\s*(absolute|fixed)", RegexOptions.IgnoreCase)) return "positioned inline";
        var floatRules = 0;
        foreach (var kv in cv.css)
        {
            if (kv.Value.TryGetValue("position", out var pos) && Regex.IsMatch(pos, @"absolute|fixed", RegexOptions.IgnoreCase)) return "positioned rule " + kv.Key;
            if (!kv.Value.TryGetValue("float", out var fl)) continue;
            var side = fl.Trim().ToLowerInvariant();
            if (side == "left" || side == "right") floatRules++;
        }
        return floatRules >= FbMinFloatRules ? null : "float rules " + floatRules;
    }

    /// <summary>The sheet's rules keyed on a type attribute (`input[type="radio"]`, with or without
    /// an ancestor part), which the sheet parser leaves out: read from the style blocks, screen-only
    /// media groups dropped, in source order.</summary>
    private static void FbReadAttrRules(FbState fb, string html)
    {
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = FlattenMediaBlocks(Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", ""));
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match d in StyleDeclRx.Matches(rule.Groups[2].Value))
                    decls[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
                if (decls.Count == 0) continue;
                foreach (var sel in rule.Groups[1].Value.Split(','))
                {
                    var m = FbAttrRuleRx.Match(sel.Trim());
                    if (m.Success) fb.attrRules.Add((m.Groups["anc"].Value, m.Groups["tag"].Value, m.Groups["type"].Value, decls));
                }
            }
        }
    }

    private static void FbTraceBoxes(FbBox box, int depth)
    {
        var n = box.Node;
        var name = n is null ? "?" : n.Tag + (n.Attrs is not null && n.Attrs.TryGetValue("id", out var id) ? "#" + id : "") + (n.Attrs is not null && n.Attrs.TryGetValue("class", out var cls) ? "." + cls.Replace(' ', '.') : "");
        var kind = box.IsFloat ? "float" : box.IsInline ? "inline" : box.IsTable ? "table" : "block";
        Console.Error.WriteLine($"[fb2] {new string(' ', depth * 2)}{name} {kind} x={box.X:0.###} y={box.Y:0.###} w={box.W:0.###} h={box.H:0.###} lines={box.Lines.Count} lh={box.St.LineHeightPx:0.##}px px={box.St.Px:0.##}"
            + (box.Lines.Count > 0 ? $" line0: y={box.Lines[0].Y:0.###} above={box.Lines[0].Above:0.###} below={box.Lines[0].Below:0.###}" : ""));
        foreach (var k in box.Kids) FbTraceBoxes(k, depth + 1);
    }

    private static HtmlNode? FbFindTag(HtmlNode node, string tag)
    {
        foreach (var c in node.Children)
        {
            if (c.Tag == tag) return c;
            if (c.Tag.Length > 0 && FbFindTag(c, tag) is { } found) return found;
        }
        return null;
    }

    /// <summary>The body's margin in pt: its own declared margin when it states one, else the UA 8 px.</summary>
    private static double FbBodyMarginPt(FbState fb, HtmlNode body)
    {
        var m = FbDecl(fb, body, "margin");
        if (!string.IsNullOrEmpty(m))
        {
            var first = m.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (first == "0" || ParsePxValue(first) > 0 || first.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                return ParsePxValue(first) * FbPxPt;
        }
        return FbUaBodyMarginPx * FbPxPt;
    }

    /// <summary>Lay the body out on a sheet of the width: the body is the root formatting context at
    /// (page margin + body margin) with the sheet's content width.</summary>
    private static void FbLayoutDocument(FbState fb, double sheetW)
    {
        var x = FbPageMarginX + fb.bodyMarginPt;
        var w = sheetW - 2 * FbPageMarginX - 2 * fb.bodyMarginPt;
        var st = FbStyleOf(fb, fb.body, FbRootStyle());
        var bfc = new FbBfc();
        var box = new FbBox { Node = fb.body, St = st, P = new FbBoxProps(), X = x, Y = fb.bandTop, W = w, IsBfc = true };
        var cursor = fb.bandTop;
        FbLayoutChildren(fb, box, fb.body, st, x, w, ref cursor, bfc);
        box.H = Math.Max(cursor, bfc.Bottom()) - fb.bandTop;
        fb.root = box;
    }

    /// <summary>The rightmost drawn edge of the tree: a bordered or painted box's right edge, a
    /// text run's advance end, or a table's box.</summary>
    private static double FbInkRight(FbBox box)
    {
        var right = 0.0;
        // (a replaced box counts its CONTENT right - probed: its right border is not ink)
        if (box.Img is not null) right = box.ContentX + box.ContentW;
        else if (box.P.HasVisibleBorder || box.P.BgRgb is not null || box.IsTable) right = box.X + box.W;
        foreach (var line in box.Lines)
            foreach (var it in line.Items)
                if (it.Text.Trim().Length > 0) right = Math.Max(right, line.X + it.X + it.Adv);
        foreach (var k in box.Kids) right = Math.Max(right, FbInkRight(k));
        return right;
    }

    /// <summary>The lowest box bottom in the tree (an overflowing float reaches past its parent).</summary>
    private static double FbDeepBottom(FbBox box)
    {
        var bottom = box.Y + box.H;
        foreach (var k in box.Kids) bottom = Math.Max(bottom, FbDeepBottom(k));
        return bottom;
    }

    /// <summary>The hhea metrics of a measuring face as em fractions (ascent, descent, ascent +
    /// descent + line gap), from the installed program; the probed Times / Arial values when the
    /// program cannot be read.</summary>
    private static (double asc, double desc, double line) FbFaceMetrics(FbState fb, string measureFace)
    {
        if (fb.faceMetrics.TryGetValue(measureFace, out var m)) return m;
        var serif = measureFace.StartsWith("Times", StringComparison.OrdinalIgnoreCase);
        m = serif ? (FbSerifHheaAsc, FbSerifHheaDesc, FbHheaLine) : (FbSansHheaAsc, FbSansHheaDesc, FbHheaLine);
        try
        {
            var ttf = Text.FontRepository.GetTtfData(measureFace);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.UnitsPerEm > 0 && tp.Ascent > 0)
                {
                    double upm = tp.UnitsPerEm, asc = tp.Ascent / upm, desc = Math.Abs(tp.Descent) / upm;
                    m = (asc, desc, asc + desc + Math.Max(0, tp.LineGap) / upm);
                }
            }
        }
        catch { /* keep the probed metrics */ }
        fb.faceMetrics[measureFace] = m;
        return m;
    }
}
