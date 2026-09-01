using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One right-aligned line of the procedure-form header band.</summary>
    internal sealed class ProcBandLine
    {
        public string Text = "";
        public bool Bold;
        // Explicit CSS line-box height (pt); 0 = the band's own 1.12 em pitch.
        public double HeightPt;
    }

    /// <summary>Detect the procedure-form header-band dialect: a
    /// <c>global-header</c> whose <c>header-right-dv</c> rows and
    /// <c>sectionHeader</c> paragraphs stack right-aligned against the band's
    /// right margin. Each explicit-height row steps by its own CSS height; the
    /// remaining lines step on the band pitch. Only the line carrying
    /// <c>&lt;strong&gt;</c> is bold — emphasis stays per line, not per fragment.</summary>
    internal static bool TryParseProcedureBandLines(string? html, out List<ProcBandLine> lines)
    {
        lines = new List<ProcBandLine>();
        var s = html ?? "";
        if (s.IndexOf("global-header", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("header-right-dv", StringComparison.OrdinalIgnoreCase) < 0) return false;
        foreach (Match m in Regex.Matches(s,
            @"<(?<t>div|p)\b[^>]*class\s*=\s*(?<q>['""])(?<c>[^'""]*(?:header-right-dv|sectionHeader)[^'""]*)\k<q>[^>]*>(?<inner>[\s\S]*?)</\k<t>\s*>",
            RegexOptions.IgnoreCase))
        {
            var inner = m.Groups["inner"].Value;
            var text = Regex.Replace(DecodeEntities(HtmlFragment.StripHtmlTags(inner)), @"\s+", " ").Trim();
            if (text.Length == 0) continue;
            var open = m.Value[..(m.Value.IndexOf('>') + 1)];
            var hm = Regex.Match(open, @"height\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            lines.Add(new ProcBandLine
            {
                Text = text,
                Bold = Regex.IsMatch(inner, @"<(b|strong)[\s>]", RegexOptions.IgnoreCase),
                HeightPt = hm.Success
                    ? double.Parse(hm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : 0,
            });
        }
        return lines.Count > 1;
    }

    /// <summary>One drawn piece of a procedure-step content line: a text run, an
    /// underlined fill-in blank of a CSS-declared width, or a radio/checkbox glyph.</summary>
    internal sealed class StepSeg
    {
        public string? Text;
        public bool Bold;
        public double BlankPt;
        public bool Radio;
        public bool Checkbox;
        public double PadLeftPt;
    }

    internal sealed class StepLine
    {
        public List<StepSeg> Segs = new();
        /// <summary>The line stands for an empty paragraph: it takes a line box AND the
        /// paragraph's own margins above and below it.</summary>
        public bool EmptyPara;
        /// <summary>The block model already carries this line's paragraph margins in its
        /// item gap, so the line box is all the height it takes.</summary>
        public bool BlockMargined;
        /// <summary>The box the baseline is split from, where the line's own box was grown
        /// under the baseline by something seated on it. 0 to split the line's own box.</summary>
        public double AscentLinePt;
        /// <summary>Size the line sets in when it is not the form's own — a heading sets
        /// at its own size. 0 to take the form's.</summary>
        public double FontPt;
        /// <summary>The line box the line sits in, when the markup declares one of its own
        /// (a heading's <c>line-height</c>). 0 to pace at the form's own pitch.</summary>
        public double LinePt;
        /// <summary>How the block sets its lines across the content column: 0 from the left,
        /// 1 centred, 2 flush right.</summary>
        public int Align;
        /// <summary>Width of the box the line is set in when that box is centred in the
        /// content column and holds the line at its own left edge - how a note box seats
        /// its caption. 0 for a line that simply starts where the content does.</summary>
        public double CenterBoxPt;
        /// <summary>Margin an empty label at the end of the line still asks for. It draws
        /// nothing, but the line is that much wider for it — which is what decides how
        /// narrow a column holding the line may be.</summary>
        public double TrailPadPt;
        /// <summary>Margin the block carries above its first line box — an option of a
        /// choice widget stands that far under the label.</summary>
        public double MarginTopPt;
        /// <summary>The margin the document declares for a paragraph, carried on an
        /// <see cref="EmptyPara"/> line so a cell can price the block it stands for.
        /// 0 where the document declares none.</summary>
        public double ParaMarginPt;
    }

    /// <summary>A data-entry table inside a step's content column: fixed CSS column
    /// widths, header texts, and cells that are themselves little line stacks.</summary>
    internal sealed class StepTable
    {
        public double WidthPt;
        /// <summary>The table declared that width itself. Where it did not,
        /// <see cref="WidthPt"/> is only the sum of the columns and there is no width to
        /// fit the columns into.</summary>
        public bool WidthDeclared;
        /// <summary>The <c>cellspacing</c> the table declares, in points: with separate
        /// borders each cell stands its own box and the rows are spaced apart.</summary>
        public double CellSpacingPt;
        /// <summary>Where the wrap seats the grid in the content column: 0 its left
        /// edge, 1 its centre, 2 its right edge.</summary>
        public int Align;
        public List<double> ColPts = new();
        public List<string> Header = new();
        public List<List<List<StepLine>>> Rows = new();
        /// <summary>Per row, per cell: the fill the markup paints behind it, null where
        /// the cell is left blank. Parallel to <see cref="Rows"/>.</summary>
        public List<List<Color?>> RowBg = new();
        /// <summary>Size the cells set in. A smart-widget grid carries the form's own
        /// 12 pt; an author's table sets a step smaller, at the size the form gives its
        /// table headings.</summary>
        public double CellFontPt = 12.0;
        /// <summary>Per row, the floor its own cells declare through
        /// <c>min-height</c> — zero where the markup declares none. Parallel to
        /// <see cref="Rows"/>.</summary>
        public List<double> RowMinPt = new();
        /// <summary>The table is the author's own, so its cells stack on the form's line
        /// rhythm rather than the tighter one the widget grids were built to.</summary>
        public bool FormRhythm;
    }

    /// <summary>One flowed item of a step row: a content line or a data-entry table,
    /// with any extra vertical gap its own CSS margins put before it.</summary>
    internal sealed class StepItem
    {
        public StepLine? Line;
        public StepTable? Table;
        public double GapBefore;
        // A data-entry caption/label line travels with its table across pages.
        public bool KeepWithNext;
        /// <summary>Opens a framed note box: the rule width the form draws it in.</summary>
        public double BoxBorderPt;
        /// <summary>The frame is drawn as a double rule rather than a solid one.</summary>
        public bool BoxDouble;
        /// <summary>The padding-top the document's own sheet declares for this box kind,
        /// in points; 0 leaves the renderer's 2 css px default.</summary>
        public double BoxPadTopPt;
        /// <summary>Closes the framed box opened earlier in the row.</summary>
        public bool BoxEnd;
    }

    internal sealed class StepRow
    {
        public string? Bullet;
        /// <summary>slashed-dv: the number sits on a grey fill with a slash through it —
        /// a struck-through (skipped) step.</summary>
        public bool BulletSlashed;
        /// <summary>The slashed fill's width; the wrapper may narrow it (width-25).</summary>
        public double BulletSlashWidthPt;
        /// <summary>Text of the userinitials-wrap cell, when the form filled it in.</summary>
        public string? AckInitials;
        /// <summary>The row declares the landscape column.</summary>
        public bool Landscape;
        /// <summary>The full-width (<c>step-col-full</c>) generation — content spans the
        /// column and the acknowledge widgets bank under it on the linked-sheet rhythm.</summary>
        public bool ColFull;
        public double IndentPt;
        // The width the form gives this row's content column, less whatever its
        // indent takes. Zero where the row lets the column take the sheet.
        public double ContentWidthPt;
        public bool Clog;
        // step-warning wrapper: a 5 css px black box around the row's content.
        public bool Warn;
        /// <summary>The acknowledge widgets sit in a two-row table under the content
        /// (the col-full form generation) rather than in a flex column beside it.</summary>
        public bool AckTable;
        /// <summary>The widget row opens with a hair-space text line, which seats the
        /// blanks three points lower in the cluster.</summary>
        public bool AckHair;
        public List<StepItem> Items = new();
        /// <summary>How tall the acknowledge column stands, whether or not any of it falls
        /// on the sheet. The row is a flex line and cannot be shorter than this.</summary>
        public double AckHeightPt;
        public bool HasAck;
        public string? AckLabel;
        public List<AckWidget> Acks = new();
    }

    /// <summary>One acknowledge widget on the row's justified right end: fill-in
    /// blanks (a plain underline or a bordered box, each with an optional inline
    /// option label) and the widget's small stacked labels.</summary>
    internal sealed class AckWidget
    {
        public List<(double W, bool Box, string? OptLabel, bool Check)> Blanks = new();
        public List<string> Labels = new();
        /// <summary>checkbox / signature / boolean — the table-shaped cluster places
        /// each kind's blanks and labels on its own vertical anchors.</summary>
        public string Kind = "";
        /// <summary>Labels the widget's own cell carries beside its blank; the shared
        /// second label row goes to <see cref="Labels"/>.</summary>
        public List<string> TopLabels = new();
        /// <summary>The hair space this generation writes before a checkbox blank —
        /// an inline text line box of its own ABOVE the blank, which deepens the
        /// widget's stack accordingly.</summary>
        public bool Hair;
    }

    /// <summary>Detect the procedure-step form dialect: <c>step-row</c> blocks whose
    /// <c>sr-bullet</c> column numbers a <c>sr-content</c> column of smart-widget
    /// lines — labels, underlined fill-in blanks (<c>*-element</c> spans with CSS
    /// widths), radio/checkbox symbols, centered data-entry tables — with an
    /// optional acknowledge box on the row's justified right end. A missing widget
    /// figure image reserves no space. Tables outside the data-entry widget keep
    /// the document on the generic paths.</summary>
    /// <summary>A multi-column article block: a sized, padded box that pours its
    /// paragraphs down one column before starting the next.</summary>
    internal sealed class ColumnArticle
    {
        public int Columns = 1;
        public double WidthPx;
        public double HeightPx;
        public double PadPx;
        public Color? Background;
        public bool Justify;
        public bool FillAuto;
        public List<string> Paragraphs = new();
    }

    /// <summary>Detect the CSS multi-column article: one element declaring
    /// <c>column-count</c> together with its own width and height, holding only
    /// paragraphs. The declared width and height size the CONTENT box — padding
    /// adds to them, as the CSS box model has it.</summary>
    internal static bool TryParseColumnArticle(string? html, out ColumnArticle art)
    {
        art = new ColumnArticle();
        var s = html ?? "";
        var m = Regex.Match(s,
            @"<(?<t>article|div|section)\b[^>]*style\s*=\s*(?<q>['""])(?<st>[^'""]*column-count[^'""]*)\k<q>[^>]*>(?<inner>[\s\S]*?)</\k<t>\s*>",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var st = m.Groups["st"].Value;

        double Px(string prop)
        {
            var pm = Regex.Match(st, @"(?<![-\w])" + prop + @"\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            return pm.Success ? double.Parse(pm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        }

        var cm = Regex.Match(st, @"column-count\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        art.Columns = cm.Success ? int.Parse(cm.Groups[1].Value) : 1;
        art.WidthPx = Px("width");
        art.HeightPx = Px("height");
        art.PadPx = Px("padding");
        art.Justify = Regex.IsMatch(st, @"text-align\s*:\s*justify", RegexOptions.IgnoreCase);
        art.FillAuto = Regex.IsMatch(st, @"column-fill\s*:\s*auto", RegexOptions.IgnoreCase);
        var bm = Regex.Match(st, @"background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (bm.Success) art.Background = ParseCssColor(bm.Groups[1].Value.Trim());

        foreach (Match pm in Regex.Matches(m.Groups["inner"].Value,
                     @"<p\b[^>]*>([\s\S]*?)</p\s*>", RegexOptions.IgnoreCase))
        {
            var text = Regex.Replace(
                DecodeEntities(HtmlFragment.StripHtmlTags(pm.Groups[1].Value)), @"\s+", " ").Trim();
            if (text.Length > 0) art.Paragraphs.Add(text);
        }
        return art.Columns > 1 && art.WidthPx > 0 && art.HeightPx > 0 && art.Paragraphs.Count > 0;
    }

    /// <summary>One styled piece of a margin-flow paragraph.</summary>
    internal sealed class FlowRun
    {
        public string Text = "";
        // CSS font-family declared by the run's OWN element; null = the fragment's
        // strut font (the family does NOT inherit into child elements).
        public string? Family;
        public bool Bold;
        public bool Italic;
        public Color? Fore;
        public Color? Back;
        public bool HardBreak;
    }

    internal sealed class FlowPara
    {
        public List<FlowRun> Runs = new();
    }

    /// <summary>Split letter-shaped HTML into paragraphs of styled runs for the
    /// margin-flow renderer. Weight, slant and colour inherit down the element
    /// tree; <b>font-family does not</b> — text takes the family declared by the
    /// element that directly contains it, and any nested element falls back to the
    /// fragment's own font. Under that rule a
    /// <c>&lt;strong&gt;</c> or a colour <c>&lt;span&gt;</c> inside an Arial span
    /// still sets in the fragment's face.</summary>
    internal static List<FlowPara> ParseFlowParagraphs(string? html)
    {
        var paras = new List<FlowPara>();
        var s = html ?? "";
        var cur = new FlowPara();

        void EndPara()
        {
            if (cur.Runs.Count > 0) paras.Add(cur);
            cur = new FlowPara();
        }

        // element state stack: family is per-element, the rest inherit
        var stack = new List<(string? Family, bool Bold, bool Italic, Color? Fore, Color? Back)>
        {
            (null, false, false, null, null),
        };

        var i = 0;
        var n = s.Length;
        while (i < n)
        {
            if (s[i] != '<')
            {
                var j = s.IndexOf('<', i);
                if (j < 0) j = n;
                var raw = DecodeEntities(s[i..j]);
                // collapse runs of ordinary whitespace; a no-break space is content
                var text = Regex.Replace(raw, @"[ \t\r\n\f]+", " ");
                if (text.Length > 0)
                {
                    var st = stack[^1];
                    cur.Runs.Add(new FlowRun
                    {
                        Text = text, Family = st.Family, Bold = st.Bold,
                        Italic = st.Italic, Fore = st.Fore, Back = st.Back,
                    });
                }
                i = j;
                continue;
            }
            var end = s.IndexOf('>', i);
            if (end < 0) break;
            var tagStr = s[i..(end + 1)];
            i = end + 1;
            var nm = Regex.Match(tagStr, @"^</?\s*([A-Za-z][A-Za-z0-9]*)");
            if (!nm.Success) continue;
            var tag = nm.Groups[1].Value.ToLowerInvariant();
            var isClose = tagStr[1] == '/';
            var selfClosed = tagStr.EndsWith("/>", StringComparison.Ordinal);

            if (tag == "br") { cur.Runs.Add(new FlowRun { HardBreak = true }); continue; }
            if (tag is "style" or "script")
            {
                if (!isClose)
                {
                    var close = Regex.Match(s[i..], @"</\s*" + tag + @"\s*>", RegexOptions.IgnoreCase);
                    i = close.Success ? i + close.Index + close.Length : n;
                }
                continue;
            }
            if (tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                EndPara();
                if (isClose && stack.Count > 1) stack.RemoveAt(stack.Count - 1);
                else if (!isClose && !selfClosed) stack.Add(ElementState(stack[^1], tagStr, tag));
                continue;
            }
            if (tag is "span" or "strong" or "b" or "em" or "i" or "u" or "font" or "a" or "sub" or "sup")
            {
                if (isClose) { if (stack.Count > 1) stack.RemoveAt(stack.Count - 1); }
                else if (!selfClosed) stack.Add(ElementState(stack[^1], tagStr, tag));
                continue;
            }
        }
        EndPara();
        return paras;
    }

    /// <summary>The style an element imposes on the text directly inside it: family
    /// from its own declaration only, everything else inherited from its parent.</summary>
    private static (string? Family, bool Bold, bool Italic, Color? Fore, Color? Back) ElementState(
        (string? Family, bool Bold, bool Italic, Color? Fore, Color? Back) parent, string tagStr, string tag)
    {
        var style = Regex.Match(tagStr, @"style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;
        string? family = null;
        var fm = Regex.Match(style, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            var first = fm.Groups[1].Value.Split(',')[0].Trim().Trim('\'', '"');
            if (first.Length > 0) family = first;
        }
        else
        {
            var fa = Regex.Match(tagStr, @"\bface\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase);
            if (fa.Success) family = fa.Groups[2].Value.Split(',')[0].Trim();
        }

        var bold = parent.Bold || tag is "strong" or "b"
                   || Regex.IsMatch(style, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase);
        var italic = parent.Italic || tag is "em" or "i"
                     || Regex.IsMatch(style, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase);

        var fore = parent.Fore;
        var cm = Regex.Match(style, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (cm.Success && ParseCssColor(cm.Groups[1].Value.Trim()) is { } fc) fore = fc;

        var back = parent.Back;
        var bm = Regex.Match(style, @"background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (bm.Success && ParseCssColor(bm.Groups[1].Value.Trim()) is { } bc) back = bc;

        return (family, bold, italic, fore, back);
    }

    /// <summary>The Standard-14 face to draw a CSS family in, at the requested
    /// weight and slant. Named families map onto their metric twins (Arial and the
    /// sans-serif generic to Helvetica, Times New Roman and the serif generic to
    /// Times), so a document written for the system fonts sets in the same widths.</summary>
    internal static string Std14Face(string? family, bool bold, bool italic)
    {
        var f = (family ?? "").ToLowerInvariant();
        if (f.Contains("courier") || f.Contains("mono") || f.Contains("consol"))
            return bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold"
                : italic ? "Courier-Oblique" : "Courier";
        if (f.Contains("times") || f.Contains("serif") && !f.Contains("sans"))
            return bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold"
                : italic ? "Times-Italic" : "Times-Roman";
        return bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold"
            : italic ? "Helvetica-Oblique" : "Helvetica";
    }

    /// <summary>The face's OS/2 vertical metrics per em, as the system fonts these
    /// Standard-14 twins stand in for declare them: usWinAscent, usWinDescent and
    /// the line gap. They set the "normal" line height, which CSS rounds to whole
    /// pixels before splitting the leftover leading evenly above and below.</summary>
    private static (double Asc, double Desc, double Gap) FaceVMetrics(string face)
    {
        if (face.StartsWith("Times", StringComparison.Ordinal)) return (1825 / 2048.0, 443 / 2048.0, 87 / 2048.0);
        if (face.StartsWith("Courier", StringComparison.Ordinal)) return (1705 / 2048.0, 615 / 2048.0, 0);
        return (1854 / 2048.0, 434 / 2048.0, 67 / 2048.0);   // Helvetica stands in for Arial
    }

    /// <summary>The face's "normal" line height at this size: the em box plus its
    /// line gap, rounded to a whole CSS pixel.</summary>
    internal static double FaceLineHeight(string face, double size)
    {
        var (a, d, g) = FaceVMetrics(face);
        return 0.75 * Math.Round(size / 0.75 * (a + d + g), MidpointRounding.AwayFromZero);
    }

    /// <summary>Height of the face's content area above the baseline (no leading).</summary>
    internal static double FaceAscent(string face, double size) => size * FaceVMetrics(face).Asc;

    /// <summary>Depth of the face's content area below the baseline (no leading).</summary>
    internal static double FaceDescent(string face, double size) => size * FaceVMetrics(face).Desc;

    /// <summary>Distance from a line box's top to its baseline: half the leading
    /// plus the content ascent.</summary>
    internal static double FaceAbove(string face, double size)
    {
        var (a, d, _) = FaceVMetrics(face);
        var half = (FaceLineHeight(face, size) - size * (a + d)) / 2;
        return half + size * a;
    }

    /// <summary>Distance from a line box's baseline to its bottom.</summary>
    internal static double FaceBelow(string face, double size)
    {
        var (a, d, _) = FaceVMetrics(face);
        var half = (FaceLineHeight(face, size) - size * (a + d)) / 2;
        return half + size * d;
    }

    /// <summary>One flowed piece of the centered filing-letter dialect: a text
    /// line, a blank line from a hard break, or the letterhead image.</summary>
    internal sealed class FilingItem
    {
        public string? Text;
        public bool Blank;
        public string? ImgSrc;
        public double ExtraGap;
        public bool AlignLeft;
        public double IndentPt;
    }

    /// <summary>Detect the legacy filing-letter dialect: a centered Times body on a
    /// 4 em line rhythm (html/body text-align center, body line-height 4em), opened
    /// by a letterhead image. Every hard break holds a full-pitch blank line; the
    /// letter's paragraph wrappers carry their 1 em margins even where invalid
    /// nesting would make a browser hoist the blocks out.</summary>
    internal static bool TryParseFilingLetter(string? html, out List<FilingItem> items)
    {
        items = new List<FilingItem>();
        var s = html ?? "";
        if (!Regex.IsMatch(s, @"line-height\s*:\s*4em", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(s, @"html\s*,\s*body\s*\{[^}]*text-align\s*:\s*center", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(s, @"font-family\s*:\s*'?Times", RegexOptions.IgnoreCase)
            || s.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0) return false;

        var body = Regex.Match(s, @"<body[^>]*>([\s\S]*?)</body\s*>", RegexOptions.IgnoreCase) is { Success: true } bm
            ? bm.Groups[1].Value : s;
        body = Regex.Replace(body, @"<style\b[^>]*>[\s\S]*?</style\s*>", "", RegexOptions.IgnoreCase);

        var parsed = items;
        var cur = "";
        double gapNext = 0, indent = 0;
        var leftDepth = 0;
        var indentDepth = 0;
        var depth = 0;

        void EndLine(bool blankIfEmpty)
        {
            var t = cur.Trim();
            cur = "";
            if (t.Length > 0)
            {
                parsed.Add(new FilingItem
                {
                    Text = t, ExtraGap = gapNext, AlignLeft = leftDepth > 0, IndentPt = indent,
                });
                gapNext = 0;
            }
            else if (blankIfEmpty)
            {
                parsed.Add(new FilingItem { Blank = true, ExtraGap = gapNext });
                gapNext = 0;
            }
        }

        var i = 0;
        var n = body.Length;
        while (i < n)
        {
            if (body[i] != '<')
            {
                var j = body.IndexOf('<', i);
                if (j < 0) j = n;
                var txt = Regex.Replace(DecodeEntities(body[i..j]).Replace('\u00A0', ' '), @"\s+", " ");
                if (txt.Trim().Length > 0 || cur.Length > 0 && txt.Length > 0)
                    cur += cur.Length == 0 ? txt.TrimStart() : txt;
                i = j;
                continue;
            }
            var end = body.IndexOf('>', i);
            if (end < 0) break;
            var tagStr = body[i..(end + 1)];
            i = end + 1;
            var nm = Regex.Match(tagStr, @"^</?\s*([A-Za-z][A-Za-z0-9]*)");
            if (!nm.Success) continue;
            var tag = nm.Groups[1].Value.ToLowerInvariant();
            var isClose = tagStr[1] == '/';
            var style = Regex.Match(tagStr, @"style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;

            if (tag == "br") { EndLine(blankIfEmpty: true); continue; }
            if (tag == "img" && !isClose)
            {
                EndLine(blankIfEmpty: false);
                var sm = Regex.Match(tagStr, @"\bsrc\s*=\s*['""]?([^'""\s>]+)", RegexOptions.IgnoreCase);
                if (sm.Success)
                {
                    parsed.Add(new FilingItem { ImgSrc = sm.Groups[1].Value, ExtraGap = gapNext });
                    gapNext = 0;
                }
                continue;
            }
            if (tag is "div" or "p" or "table" or "thead" or "tbody" or "tr")
            {
                EndLine(blankIfEmpty: false);
                if (tag == "p" && !isClose
                    && Regex.IsMatch(style, @"margin-left\s*:\s*1cm", RegexOptions.IgnoreCase))
                    indentDepth = depth + 1;
                if (tag == "div" || tag == "p")
                {
                    if (!isClose)
                    {
                        depth++;
                        if (Regex.IsMatch(style, @"text-align\s*:\s*left", RegexOptions.IgnoreCase) && leftDepth == 0)
                            leftDepth = depth;
                    }
                    else
                    {
                        if (leftDepth == depth) leftDepth = 0;
                        if (indentDepth == depth) indentDepth = 0;
                        depth--;
                    }
                }
                indent = indentDepth > 0 ? 28.35 : 0;
                continue;
            }
            if (tag is "td" or "th") { if (!isClose) cur += cur.Length > 0 ? "  " : ""; continue; }
        }
        EndLine(blankIfEmpty: false);
        // the letter opens image-first and speaks in centered lines
        var hasImg = false;
        foreach (var it in parsed) { if (it.ImgSrc is not null) { hasImg = true; break; } }
        return hasImg && parsed.Count > 6;
    }

    /// <summary>Heading metrics for the step dialect, read from the document's OWN
    /// stylesheet when it declares them (these forms size their headings in CSS pixels and
    /// reset the margin), else the user-agent defaults. Set per parse.
    /// ⚠ Per PARSE, so it must be per THREAD: two documents laid out at once would
    /// otherwise read each other's stylesheet.</summary>
    [ThreadStatic]
    private static Dictionary<string, (double Size, double Line, double Margin)>? _stepHeadingCss;

    /// <summary>The margin a paragraph carries above and below, 1.12 em of the size the
    /// document gives it - a three-paragraph step paces at 26.94 on a 13.50 line box,
    /// and a heading-then-paragraph step measures 38.94.
    /// Null where the document declares no rule for <c>p</c> at all - those forms lay their
    /// paragraphs out flush.</summary>
    [ThreadStatic]
    private static double? _stepParaMarginPt;

    /// <summary>padding-top the document's sheet declares per framed-box kind, in points.</summary>
    [ThreadStatic]
    private static Dictionary<string, double>? _stepBoxPadPt;

    /// <summary>The line box a paragraph sets in, from the <c>line-height</c> the document
    /// declares for <c>p</c>. 0 to take the form's own pitch.</summary>
    [ThreadStatic]
    private static double _stepParaLinePt;

    /// <summary>The p line box declared in the form's LINKED stylesheet, honoured only for
    /// the full-width (<c>step-col-full</c>) generation's plain paragraphs — the narrow
    /// step-col family keeps ITS fragment rhythm even with the same sheet on disk,
    /// so nothing else is taken from a linked sheet.</summary>
    [ThreadStatic]
    private static double _stepLinkedParaLinePt;

    /// <summary>Whether the row whose content is being walked is a <c>step-col-full</c>
    /// row. Set per row by <see cref="TryParseProcedureStepRows"/>.</summary>
    [ThreadStatic]
    private static bool _stepRowColFull;

    /// <summary>A heading's size, the line box it sits in, and the margin above and below.
    /// The document's own sheet wins; the fallback is the user-agent's (a multiple of the
    /// body's em, with a margin in the heading's own em).</summary>
    private static (double Size, double Line, double Margin) HeadingMetrics(string tag)
    {
        if (_stepHeadingCss is not null && _stepHeadingCss.TryGetValue(tag, out var css)) return css;
        return tag switch
        {
            "h1" => (24.0, 24.0, 0.67 * 24.0),
            "h2" => (18.0, 18.0, 0.83 * 18.0),
            "h3" => (14.04, 14.04, 1.00 * 14.04),
            "h4" => (12.0, 12.0, 1.33 * 12.0),
            "h5" => (9.96, 9.96, 1.67 * 9.96),
            _ => (8.04, 8.04, 2.33 * 8.04),
        };
    }

}
