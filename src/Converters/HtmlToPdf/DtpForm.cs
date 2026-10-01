using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The positioned DTP-form dialect: a desktop-publishing HTML export (Avanquest
// WebEasy shape) whose BODY is a flat sequence of absolutely positioned
// elements — text divs, form text inputs and data-URI images — every one
// carrying a stylesheet id rule with pt coordinates. The engine lays the
// absolute canvas onto pages by slicing it into content bands, re-flowing
// wrapped div lines across the band boundary and repeating boundary-crossing
// images on both pages. All measures from the expected render of the
// credit-contract fixture.
internal static partial class HtmlToPdfConverter
{
    // Content origin: element x = cssLeft + 90, y = cssTop + 72 — the content
    // band paints as a white fill (90,72)-(pageW-90, pageH-72).
    private const double DtpSideMarginPt = 90.0;
    private const double DtpVertMarginPt = 72.0;
    // Div line box: pitch 1.125 em (11.25 at 10pt); the baseline seats at
    // halfLead + winAscent below the box top (81.09 for 10pt Arial at cssTop+72).
    private const double DtpLineFactor = 1.125;
    // A text input with no height rule strokes a 15.75 pt (21 px UA) box.
    private const double DtpInputDefaultHPt = 15.75;
    // Input value baselines sit at boxCentre + seat: regular values (drawn in
    // the standard sans) 3.71 below centre, bold values 0.81 higher — both
    // measured on 14/14.5/15.75-high boxes.
    private const double DtpInputSeatRegPt = 3.71;
    private const double DtpInputSeatBoldPt = 2.90;
    // Left-aligned input values inset 2.0 from the box edge (border 0.75 +
    // padding 1.5 rounded down by the renderer; measured exactly 2.0).
    private const double DtpInputPadPt = 2.0;
    // The input chrome strokes a 1 pt black rect regardless of the authored
    // (white) border: path inset 0.5 horizontally, outset 0.25 vertically.
    private const double DtpInputChromeInsetPt = 0.5;
    private const double DtpInputChromeOutsetPt = 0.25;
    // <u>/link underlines stroke 1 pt below the baseline, 1 pt wide.
    private const double DtpUnderlineDropPt = 1.0;
    // Pasted Word-HTML bullet paragraphs (MsoNormal + mso-list markers) pitch
    // differently from the surrounding 11.25 flow: 11.72 between wrapped lines
    // inside one paragraph, 12.08 entering a new paragraph (both measured).
    private const double DtpMsoWrapPitchPt = 11.72;
    private const double DtpMsoParaPitchPt = 12.08;

    private sealed class DtpIdRule
    {
        public double L, T, W, H;
        public bool HasH;
        public string? Align;
    }

    private sealed class DtpClassRule
    {
        public double SizePt = 10;
        public bool Bold, Italic;
        public string Family = "Arial";
        public (double R, double G, double B) Color = (0, 0, 0);
        public string? Align;
    }

    private sealed class DtpRun
    {
        public string Text = "";
        public bool Bold, Italic, Under;
        public double SizePt = 10;
        public string Face = "Arial";
        public (double R, double G, double B) Color = (0, 0, 0);
    }

    // One logical line (forced by an inner block boundary or <br>); wrapping
    // splits it into physical lines at draw time.
    private sealed class DtpLogicalLine
    {
        public List<DtpRun> Runs = new();
        public string Align = "left";
        public double FirstIndent;   // first physical line x offset (Mso margin+text-indent)
        public double HangIndent;    // continuation lines x offset (Mso margin-left)
        public bool Mso;             // a pasted-Word bullet paragraph (special pitches)
    }

    // ── rich text parsing ──────────────────────────────────────────────────

    /// <summary>Tokenize a positioned div's inner HTML into logical lines:
    /// inner div/p boundaries and &lt;br&gt; force breaks; strong/b/u/i/em/a
    /// set run styles; span inline styles override size/family; entities
    /// decode with nbsp preserved and other whitespace collapsed.</summary>
    private static List<DtpLogicalLine> DtpParseRichText(string inner, DtpClassRule baseClass, Dictionary<string, DtpClassRule> classRules, string defaultAlign)
    {
        var rt = new DtpRichTextState();
        rt.inner = inner;
        rt.baseClass = baseClass;
        rt.classRules = classRules;
        rt.defaultAlign = defaultAlign;
        rt.inner = Regex.Replace(rt.inner, @"<!--[\s\S]*?-->", " ");
        rt.lines = new List<DtpLogicalLine>();
        rt.cur = null;
        rt.bold = 0; rt.ital = 0; rt.under = 0; rt.link = 0;
        rt.alignStack = new Stack<string>();
        rt.alignStack.Push(rt.defaultAlign);
        rt.spanStack = new Stack<(double? Size, string? Face, (double, double, double)? Color)>();
        rt.msoFirstIndent = 0;
        rt.msoHangIndent = 0;
        rt.inMso = false;

        rt.idx = 0;
        rt.tokRx = new Regex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>'""]|'[^']*'|""[^""]*"")*)>");
        while (rt.idx < rt.inner.Length)
        {
            if (!DtpRichTextStep(rt)) break;
        }
        FlushDtpLine(rt);
        return rt.lines;
    }

    private static (double Size, string Face, (double, double, double) Color) DtpEffective(
        DtpClassRule baseClass, Stack<(double? Size, string? Face, (double, double, double)? Color)> spans)
    {
        double size = baseClass.SizePt;
        var face = DtpNormalizeFace(baseClass.Family);
        (double, double, double) color = baseClass.Color;
        double? sz = null;
        string? fc = null;
        (double, double, double)? cl = null;
        foreach (var s in spans)   // top-down: nearest frame wins per property
        {
            sz ??= s.Size;
            fc ??= s.Face;
            cl ??= s.Color;
        }
        return (sz ?? size, fc ?? face, cl ?? color);
    }

    private static string DtpNormalizeFace(string family)
    {
        var f = family.Trim().Trim('"', '\'').Trim();
        if (f.Equals("arial", StringComparison.OrdinalIgnoreCase)) return "Arial";
        if (f.Equals("symbol", StringComparison.OrdinalIgnoreCase)) return "Symbol";
        if (f.Equals("times new roman", StringComparison.OrdinalIgnoreCase)) return "Times New Roman";
        if (f.Equals("verdana", StringComparison.OrdinalIgnoreCase)) return "Verdana";
        if (f.Equals("courier new", StringComparison.OrdinalIgnoreCase)) return "Courier New";
        return f;
    }

    private static string DtpFaceName(DtpRun r)
    {
        if (r.Face is "Symbol") return "Symbol";
        var baseFace = r.Face.Length > 0 ? r.Face : "Arial";
        if (r.Bold && r.Italic) return baseFace + " Bold Italic";
        if (r.Bold) return baseFace + " Bold";
        if (r.Italic) return baseFace + " Italic";
        return baseFace;
    }

    private static double DtpMeasureRun(DtpRun r)
        => MeasureFaceText(DtpFaceName(r), r.Text, r.SizePt);

    /// <summary>Greedy word wrap of a logical line to the div width (indents
    /// reduce the first/continuation line budget). NBSP is not a break point;
    /// a trailing space hangs at the wrap.</summary>
    private static List<List<DtpRun>> DtpWrap(DtpLogicalLine ll, double width)
    {
        var result = new List<List<DtpRun>>();
        var line = new List<DtpRun>();
        double lineW = 0;
        var budget = width - ll.FirstIndent;

        void CloseLine()
        {
            result.Add(line);
            line = new List<DtpRun>();
            lineW = 0;
            budget = width - ll.HangIndent;
        }
        void Append(DtpRun proto, string text)
        {
            if (text.Length == 0) return;
            if (line.Count > 0 && ReferenceEquals(line[^1].Text, null)) { }
            if (line.Count > 0 && DtpSameStyle(line[^1], proto))
                line[^1].Text += text;
            else
            {
                var r2 = DtpCloneStyle(proto);
                r2.Text = text;
                line.Add(r2);
            }
            lineW += MeasureFaceText(DtpFaceName(proto), text, proto.SizePt);
        }

        foreach (var run in ll.Runs)
        {
            var t = run.Text;
            var i = 0;
            while (i < t.Length)
            {
                // token = a run of non-space chars (nbsp glued), or one space
                int j;
                if (t[i] == ' ')
                {
                    j = i + 1;
                    var w2 = MeasureFaceText(DtpFaceName(run), " ", run.SizePt);
                    // a space that would overflow hangs on the line
                    Append(run, " ");
                    if (lineW - w2 > budget + 1e-6) { }
                    i = j;
                    continue;
                }
                j = i;
                while (j < t.Length && t[j] != ' ') j++;
                var word = t[i..j];
                var wordW = MeasureFaceText(DtpFaceName(run), word, run.SizePt);
                if (line.Count > 0 && lineW + wordW > budget + 1e-6)
                {
                    // trailing space stays on the closed line (hangs)
                    CloseLine();
                }
                Append(run, word);
                i = j;
            }
        }
        if (line.Count > 0 || result.Count == 0) result.Add(line);
        return result;
    }

    private static bool DtpSameStyle(DtpRun a, DtpRun b)
        => a.Bold == b.Bold && a.Italic == b.Italic && a.Under == b.Under
           && a.SizePt.Equals(b.SizePt) && a.Face == b.Face && a.Color.Equals(b.Color);

    private static DtpRun DtpCloneStyle(DtpRun p) => new()
    {
        Bold = p.Bold, Italic = p.Italic, Under = p.Under,
        SizePt = p.SizePt, Face = p.Face, Color = p.Color,
    };

    /// <summary>Baseline seat below a line-box top: half-leading + winAscent
    /// (Arial 1854/434/2048 — the corpus face; 9.09 at 10 pt, matching the
    /// expected 81.09 first-baseline offset).</summary>
    private static double DtpSeat(double sizePt)
    {
        var lineH = DtpLineFactor * sizePt;
        var sum = (SlideTextAscEm + SlideTextDescEm) * sizePt;
        return (lineH - sum) / 2 + SlideTextAscEm * sizePt;
    }

    /// <summary>Map text into the F0xx PUA range a symbol-encoded cmap uses,
    /// when the direct codepoint has no mapping.</summary>
    private static string DtpToSymbolPua(Text.GlyphOutlineParser? parser, string text)
    {
        if (parser is null) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!parser.CMap.ContainsKey(ch) && parser.CMap.ContainsKey(0xF000 | ch))
                sb.Append((char)(0xF000 | ch));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static double? TryDtpPt(string css, string prop)
    {
        double value = 0;
        var m = Regex.Match(css, prop + @"\s*:\s*([\-\d.]+)\s*pt", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        value = DtpNum(m.Groups[1].Value);
        return value;
    }

    private static double DtpNum(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double? DtpStyleLen(string style, string pattern)
    {
        var m = Regex.Match(style, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var tok = m.Groups[1].Value;
        var isIn = tok.EndsWith("in", StringComparison.OrdinalIgnoreCase);
        var num = DtpNum(tok[..^2]);
        return isIn ? num * 72.0 : num;
    }

    private static (double, double, double) DtpHexColor(string hex6)
        => (System.Convert.ToInt32(hex6[..2], 16) / 255.0,
            System.Convert.ToInt32(hex6[2..4], 16) / 255.0,
            System.Convert.ToInt32(hex6[4..6], 16) / 255.0);

    private static string? DtpAttr(string tag, string name)
    {
        var m = Regex.Match(tag,
            name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value
            : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }
}
