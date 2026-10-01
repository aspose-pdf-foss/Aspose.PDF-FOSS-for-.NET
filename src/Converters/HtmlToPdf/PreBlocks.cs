using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A preformatted block: its lines verbatim (whitespace preserved, never wrapped),
    /// each on the face and size its markup gives it, paragraphs inside it carrying their own
    /// margins.</summary>
    internal sealed class PreBlock
    {
        public List<PreLine> Lines = new();
        public double WidestPt;       // the widest line's advance: the page grows to hold it
    }

    internal sealed class PreLine
    {
        public string Text = "";
        public string Face = "";
        public double FontPt;
        public bool Center;
        public bool Bold;             // the line's text sits inside <b>/<strong>: it draws in the face's bold
        public bool ParagraphStart;   // the first line of a <p>: its margin applies, collapsing
        public double MarginPt;       // the paragraph's top and bottom margin
        public bool ParagraphLine;    // the line belongs to a <p>
    }

    // Pre law (probed against the reference): the UA pre face is Courier New at 0.87 of the
    // inherited size (10.44 pt under the 12 pt default); a <font size=N> inside sets the size
    // absolutely. Every line box is the metric line of its own face (Lucida Console 8 pt:
    // 11 px = 8.25 pt) with the win-metric baseline seat. A <p> inside the pre is a block with
    // a 1.12 em margin that collapses with its neighbour's (and with the body inset at the
    // page top); a newline right before a block boundary opens no line of its own; the
    // newline after the <pre> tag is dropped. A <style> element's text is markup, not content.
    private const double PreFaceSizeFactor = 0.87;
    private const string PreDefaultFace = "Courier New";
    private const double PreParagraphMarginEm = 1.12;

    /// <summary>Every top-level &lt;pre&gt; outside a table becomes a preformatted block.</summary>
    private static void ScanPreBlocks(RowBlocksState rb)
    {
        // (the field-list dialect's pre is a flow block under the sheet's `pre` rule - Arial, 0.87 em,
        //  its own line box and margin - not a preformatted extract)
        if (rb.css is not null && rb.css.TryGetValue("pre", out var preRule)
            && preRule.TryGetValue("white-space", out var preWs) && preWs.Contains("pre-wrap", StringComparison.OrdinalIgnoreCase)
            && preRule.TryGetValue("font-family", out var preFam) && FirstFontFamily(preFam) is { } preFace && WinMetricsFor(preFace) is not null)
            return;
        foreach (Match m in Regex.Matches(rb.html, @"<pre\b[^>]*>([\s\S]*?)</pre\s*>", RegexOptions.IgnoreCase))
        {
            if (Overlaps(rb, m.Index, m.Index + m.Length)) continue;
            var before = rb.html[..m.Index];
            var opens = Regex.Matches(before, @"<table\b", RegexOptions.IgnoreCase).Count;
            var closes = Regex.Matches(before, @"</table\b", RegexOptions.IgnoreCase).Count;
            if (opens > closes) continue;
            var block = BuildPreBlock(m.Groups[1].Value);
            if (block is not null) rb.extracts.Add((m.Index, m.Index + m.Length, block));
        }
    }

    private sealed class PreParseState
    {
        public PreBlock block = new();
        public StringBuilder line = new();
        public bool lineOpen;                 // text or a newline has been seen on the current line
        public Stack<double> sizeStack = new();
        public double size;                   // the current inherited size (pt)
        public string? pFace; public double pSize; public bool pCenter; public bool inP;
        public bool pFirstLine;
        public int boldDepth;                 // open <b>/<strong> elements
        public bool lineBold;                 // text was appended to the current line while bold
        // The index into block.Lines where the CURRENTLY OPEN <p>'s own lines begin. A <p>
        // closed by its own </p> or superseded by a following sibling <p> keeps its declared
        // size; one closed only because an ANCESTOR ends (</font>, EOF) loses its declared
        // SIZE (not its face) and falls back to the enclosing <font size> (probed:
        // ALIGN, blank lines and PRE-nesting are all irrelevant — only that closure shape is).
        public int pOpenLineStart;
    }

    /// <summary>A <paramref name="st"/>'s open paragraph is ending without its own signal
    /// (no <c>&lt;/p&gt;</c>, no following sibling <c>&lt;p&gt;</c>) — an ancestor tag or EOF
    /// closed it instead. Its own declared size never took effect; only the face did.</summary>
    private static void DropUnclosedParagraphSize(PreParseState st, double fallbackSize)
    {
        if (!st.inP || st.pSize <= 0) return;
        for (var i = st.pOpenLineStart; i < st.block.Lines.Count; i++)
        {
            var ln = st.block.Lines[i];
            if (ln.FontPt != st.pSize) continue;
            ln.FontPt = fallbackSize;
            if (ln.ParagraphStart) ln.MarginPt = PreParagraphMarginEm * fallbackSize;
        }
    }

    /// <summary>Tokenise the pre's inner markup into lines, tracking &lt;font&gt; sizes and the
    /// paragraph style the lines fall under.</summary>
    private static Block? BuildPreBlock(string inner)
    {
        var st = new PreParseState { size = UaDefaultFontPt * PreFaceSizeFactor };
        // the newline right after the opening tag is not content
        if (inner.StartsWith("\r\n")) inner = inner[2..];
        else if (inner.StartsWith('\n')) inner = inner[1..];
        var pos = 0;
        foreach (Match t in Regex.Matches(inner, @"<!--[\s\S]*?-->|<(style|script)\b[^>]*>[\s\S]*?</\1\s*>|<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>", RegexOptions.IgnoreCase))
        {
            if (t.Index > pos) PreText(st, inner[pos..t.Index]);
            pos = t.Index + t.Length;
            if (t.Value.StartsWith("<!--") || t.Groups[1].Success) continue;
            PreTag(st, t.Groups[2].Value.Length > 0, t.Groups[3].Value.ToLowerInvariant(), t.Groups[4].Value);
        }
        if (pos < inner.Length) PreText(st, inner[pos..]);
        if (st.line.Length > 0) PreFlushLine(st);
        DropUnclosedParagraphSize(st, st.size);
        var block = st.block;
        if (block.Lines.Count == 0) return null;
        foreach (var ln in block.Lines)
            if (ln.Text.Length > 0)
                block.WidestPt = Math.Max(block.WidestPt, InstalledFaceAdvance(ln.Face, ln.Text, ln.FontPt));
        return new Block { Text = "", Pre = block };
    }

    private static readonly Dictionary<string, (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm, (double asc, double sum)? win)>
        _installedFaceCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An installed face by family name, through the system name-table scan when its
    /// file name differs from the family (lucon.ttf = "Lucida Console"), with the win metrics
    /// read off the face itself. Scoped to the verbatim arms: the shared PosFace keeps its
    /// file-name lookup, which the pdf-to-html exporter's run segmentation is calibrated on.</summary>
    private static (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm, (double asc, double sum)? win) InstalledFace(string name)
    {
        if (_installedFaceCache.TryGetValue(name, out var cached)) return cached;
        var pf = PosFace(name);
        var ttf = pf.ttf ?? Text.FontRepository.FindFontData(name)?.TtfData;
        var parser = pf.parser;
        var upm = pf.upm;
        var win = WinMetricsFor(name);
        try
        {
            if (ttf is not null && parser is null)
            {
                parser = new Text.GlyphOutlineParser(ttf);
                upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
            }
            if (ttf is not null && win is null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.UsWinAscent > 0 && tp.UnitsPerEm > 0)
                    win = ((double)tp.UsWinAscent / tp.UnitsPerEm, (double)(tp.UsWinAscent + tp.UsWinDescent) / tp.UnitsPerEm);
            }
        }
        catch { ttf = null; parser = null; win = null; }
        var entry = (ttf, parser, upm, win);
        _installedFaceCache[name] = entry;
        return entry;
    }

    /// <summary>A verbatim line's advance on its installed face (the face's rounded
    /// 1000-unit advances, half an em for a glyph it lacks).</summary>
    private static double InstalledFaceAdvance(string name, string text, double pt)
    {
        var face = InstalledFace(name);
        if (face.parser is null) return MeasureFaceText(name, text, pt);
        double w = 0;
        foreach (var ch in text)
            w += face.parser.CMap.TryGetValue(ch, out var g) && g != 0
                ? Math.Round(face.parser.GetAdvanceWidth(g) * 1000.0 / face.upm) * pt / 1000.0
                : 0.5 * pt;
        return w;
    }

    /// <summary>A C0 control code (other than tab and the newline this method is applied
    /// BEFORE splitting on) has no glyph in any installed face and draws as a tofu box; a
    /// mainframe report's stray form feed (0x0C) is content-stream noise, not a visible
    /// character.</summary>
    private static bool IsPreControlChar(char c) => c < ' ' && c != '\t' && c != '\n';

    private static void PreText(PreParseState st, string raw)
    {
        var text = DecodeEntities(raw).Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Any(IsPreControlChar))
            text = new string(text.Where(c => !IsPreControlChar(c)).ToArray());
        var parts = text.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            st.line.Append(parts[i]);
            st.lineOpen = true;
            if (st.boldDepth > 0 && parts[i].Trim().Length > 0) st.lineBold = true;
            if (i < parts.Length - 1) PreFlushLine(st);
        }
    }

    /// <summary>Close the current line as a line box of the current face; a newline that ends
    /// exactly at a block boundary is folded by the boundary itself.</summary>
    private static void PreFlushLine(PreParseState st)
    {
        var ln = new PreLine
        {
            Text = st.line.ToString(),
            Face = st.inP && st.pFace is not null ? st.pFace : PreDefaultFace,
            FontPt = st.inP && st.pSize > 0 ? st.pSize : st.size,
            Center = st.inP && st.pCenter,
            Bold = st.lineBold,
            ParagraphLine = st.inP,
            ParagraphStart = st.inP && st.pFirstLine,
        };
        ln.MarginPt = st.inP ? PreParagraphMarginEm * ln.FontPt : 0;
        st.block.Lines.Add(ln);
        st.line.Clear();
        st.lineOpen = false;
        st.lineBold = false;
        st.pFirstLine = false;
    }

    /// <summary>A block boundary: the pending line closes only when it holds text.</summary>
    private static void PreBoundary(PreParseState st)
    {
        if (st.line.Length > 0) PreFlushLine(st);
        st.lineOpen = false;
    }

    private static void PreTag(PreParseState st, bool closing, string tag, string attrText)
    {
        var attrs = ParseAttributes(attrText);
        switch (tag)
        {
            case "font":
                if (closing)
                {
                    DropUnclosedParagraphSize(st, st.size);
                    if (st.sizeStack.Count > 0) st.size = st.sizeStack.Pop();
                    break;
                }
                st.sizeStack.Push(st.size);
                if (attrs is not null && attrs.TryGetValue("size", out var sz)
                    && int.TryParse(sz.Trim().TrimStart('+'), out var sizeN))
                    st.size = HtmlFontSizeToPt(Math.Max(1, Math.Min(7, sizeN)));
                break;
            case "p":
                PreBoundary(st);
                if (closing) { st.inP = false; break; }
                st.inP = true;
                st.pFirstLine = true;
                st.pOpenLineStart = st.block.Lines.Count;
                st.pFace = null; st.pSize = 0; st.pCenter = false;
                if (attrs is not null)
                {
                    if (attrs.TryGetValue("align", out var al) && al.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                        st.pCenter = true;
                    if (attrs.TryGetValue("style", out var style)) ReadPreParagraphStyle(st, style);
                }
                break;
            case "div":
            case "body":
            case "html":
                // a whole document pasted inside the pre (Word/Office debris) draws inline,
                // on the same page, the same as any other boundary tag inside it
                PreBoundary(st);
                if (!closing) st.inP = false;
                break;
            case "br":
                PreFlushLine(st);
                break;
            case "b":
            case "strong":
                // (probed on the safety data sheet: `<u><b>SECTION 1 …</b></u>` draws in Courier New Bold)
                st.boldDepth = closing ? Math.Max(0, st.boldDepth - 1) : st.boldDepth + 1;
                break;
        }
    }

    /// <summary>The paragraph's inline style: its font-size, and its family from a
    /// <c>font-family</c> or a <c>font:</c> shorthand that names only the family.</summary>
    private static void ReadPreParagraphStyle(PreParseState st, string style)
    {
        foreach (var decl in style.Split(';'))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var name = decl[..colon].Trim().ToLowerInvariant();
            var value = decl[(colon + 1)..].Trim();
            if (name == "font-size" && TryParseLength(value) is { } pt && pt > 0) st.pSize = pt;
            else if (name == "font-family") st.pFace = value.Split(',')[0].Trim().Trim('"', '\'');
            else if (name == "font" && !Regex.IsMatch(value, @"\d"))
                st.pFace = value.Split(',')[0].Trim().Trim('"', '\'');
        }
    }

    /// <summary>Draw the pre: each line on its own face's metric line box, paragraphs spending
    /// their collapsed margins, the page breaking between lines.</summary>
    private static void LayoutPreBlock(ConvertState cv, PreBlock pre)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var yTop = cv.pageHeight - cv.flow.y;
        var pageTop = cv.marginTop + UaBodyMarginPt;
        var atPageTop = Math.Abs(yTop - pageTop) < 1e-3;
        var prevBottomMargin = 0.0;
        foreach (var ln in pre.Lines)
        {
            if (ln.ParagraphStart)
                yTop += atPageTop ? Math.Max(0, ln.MarginPt - UaBodyMarginPt) : Math.Max(prevBottomMargin, ln.MarginPt);
            // (the line box follows the face's WIN metrics: Lucida Console 8 pt = 11 px)
            // (a bold line draws in the face's bold, when the face has one installed)
            var faceName = ln.Face;
            var inst = InstalledFace(faceName);
            if (ln.Bold && InstalledFace(faceName + " Bold") is { ttf: not null } boldInst) { inst = boldInst; faceName += " Bold"; }
            var face = inst.ttf is not null ? inst : InstalledFace(PreDefaultFace);
            if (face.ttf is null || face.win is null) continue;
            var lineH = MetricLineHeight(ln.FontPt, face.win.Value.sum);
            var drop = MetricBaselineDrop(ln.FontPt, lineH, face.win.Value);
            if (yTop + lineH > cv.pageHeight - cv.marginBottom && !atPageTop)
            {
                cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                EnsureFonts(cv.flow.page, cv.docFontDict);
                yTop = cv.marginTop;
            }
            atPageTop = false;
            if (ln.Text.Length > 0
                && cv.flow.page.Dict.Get("Resources") is Core.PdfDictionary res
                && res.Get("Font") is Core.PdfDictionary fontDict)
            {
                var x = cv.marginLeft;
                if (ln.Center)
                    x += Math.Max(0, (cv.flow.contentWidth - InstalledFaceAdvance(ln.Face, ln.Text, ln.FontPt)) / 2);
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf, faceName, ln.Text, stripSpacesInBaseFont: true);
                cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(inv,
                    $"BT 0 0 0 rg /{rn} {ln.FontPt:F2} Tf 1 0 0 1 {x:F3} {cv.pageHeight - yTop - drop:F3} Tm <{Compat.ToHexString(hex)}> Tj ET\n")));
            }
            yTop += lineH;
            prevBottomMargin = ln.ParagraphLine ? ln.MarginPt : 0;
        }
        cv.flow.y = cv.pageHeight - yTop;
        cv.flow.lastWasHardBreak = false;
    }
}
