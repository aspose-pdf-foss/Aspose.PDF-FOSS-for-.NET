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

    internal static bool TryParseProcedureStepRows(string? html, out List<StepRow> rows,
        bool paraHasMargin = false, HtmlLoadOptions? options = null)
    {
        rows = new List<StepRow>();
        var s = html ?? "";
        ReadStepHeadingCss(s);
        // A form that links its stylesheet next to itself declares its p line box there
        // (16px/16px on these sheets). ONLY that is taken, and only the full-width
        // step-col-full generation consumes it: the narrow step-col family keeps its
        // fragment rhythm — headings, margins, box pads — even with the same sheet
        // on disk beside it.
        _stepLinkedParaLinePt = 0;
        if (options is not null && _stepParaLinePt <= 0)
        {
            var inlined = InlineLinkedStylesheets(s, options);
            var lpm = Regex.Match(inlined,
                @"(?<![-\w.#])p\s*\{[^}]*line-height\s*:\s*([\d.]+)\s*px[^}]*\}",
                RegexOptions.IgnoreCase);
            if (lpm.Success && double.TryParse(lpm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lpx) && lpx > 0)
                _stepLinkedParaLinePt = lpx * 0.75;
        }
        // The fragment's IsParagraphHasMargin honours the paragraph margin even when the
        // document's own p rule is out of reach behind a linked stylesheet: one line box
        // of the body's em, with the 1.12-em margin the sheet family declares. A fragment
        // that leaves the flag off keeps the flush rhythm this family is calibrated to.
        if (paraHasMargin && _stepParaMarginPt is null)
        {
            _stepParaMarginPt = 12.0 * 1.12;
            _stepParaLinePt = 12.0;
        }
        if (s.IndexOf("step-row", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("sr-content", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0) return false;
        // Every table in the document must be one this dialect knows how to place: a
        // smart-widget table of any of its kinds, or an ordinary author table the step
        // walker renders through the generic grid. A table of some other shape means the
        // document is not this form after all, and the generic flow should keep it.
        var tableTags = Regex.Matches(s, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var placeable = 0;
        foreach (Match tm in tableTags)
            if (Regex.IsMatch(tm.Value, @"class\s*=\s*['""]sw(dt|mt|l)-table[\s'""]", RegexOptions.IgnoreCase)
                || !Regex.IsMatch(tm.Value, @"class\s*=", RegexOptions.IgnoreCase))
                placeable++;
        if (tableTags.Count != placeable) return false;

        foreach (Match rm in Regex.Matches(s,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*step-row[^'""]*\1[^>]*>", RegexOptions.IgnoreCase))
        {
            var rowHtml = ExtractBalancedInnerAt(s, rm.Index);
            if (rowHtml is null) continue;
            var row = new StepRow
            {
                Clog = rm.Value.Contains("sr-step-clog", StringComparison.OrdinalIgnoreCase),
                Landscape = Regex.IsMatch(rm.Value, @"[\s'""]landscape[\s'""]", RegexOptions.IgnoreCase),
            };
            var bm = Regex.Match(rowHtml,
                @"<div\b[^>]*class\s*=\s*(['""])[^'""]*sr-bullet[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                RegexOptions.IgnoreCase);
            if (bm.Success && bm.Groups[2].Value.Length > 0) row.Bullet = DecodeEntities(bm.Groups[2].Value);
            // a struck-through step wraps its number: <div class='slashed-dv'>35.</div> —
            // the number sits on a grey fill with a slash through it
            if (row.Bullet is null)
            {
                var sm2 = Regex.Match(rowHtml,
                    @"<div\b[^>]*class\s*=\s*(['""])(?<cls>[^'""]*slashed-dv[^'""]*)\1[^>]*>\s*(?<num>[^<]*?)\s*</div>",
                    RegexOptions.IgnoreCase);
                if (sm2.Success && sm2.Groups["num"].Value.Length > 0)
                {
                    row.Bullet = DecodeEntities(sm2.Groups["num"].Value);
                    row.BulletSlashed = true;
                    row.BulletSlashWidthPt = sm2.Groups["cls"].Value
                        .Contains("width-25", StringComparison.OrdinalIgnoreCase) ? 18.75 : 20.25;
                }
            }
            // the indent is declared on the row, or on the bullet column it pads out
            // (read the bullet's OPEN TAG, not its captured body — a slashed bullet's
            // body is a nested div the body regex cannot take)
            var im = Regex.Match(rm.Value, @"indent-(\d)");
            if (!im.Success)
            {
                var bo = Regex.Match(rowHtml,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*sr-bullet[^'""]*\1[^>]*>", RegexOptions.IgnoreCase);
                im = Regex.Match(bo.Success ? bo.Value : "", @"indent-(\d)");
            }
            if (im.Success)
                row.IndentPt = im.Groups[1].Value switch
                {
                    "3" => 74 * 0.75, "4" => 137.84 * 0.75, "5" => 194.48 * 0.75,
                    "6" => 243.92 * 0.75, _ => 0,
                };

            // The content column is a fixed width the form declares per row: wide
            // for a landscape row, narrower otherwise, each less the indent the row
            // carries. It runs on past the sheet's right edge and is simply clipped
            // there. A row that stacks its acknowledge under the content instead of
            // beside it declares no width and takes what the sheet leaves.
            if (!Regex.IsMatch(rm.Value, @"step-col", RegexOptions.IgnoreCase))
            {
                var wide = Regex.IsMatch(rm.Value, @"[\s'""]landscape[\s'""]", RegexOptions.IgnoreCase);
                var wm = Regex.Match(rm.Value, @"indent-(\d)");
                row.ContentWidthPt = 0.75 * (wm.Success
                    ? wm.Groups[1].Value switch
                    {
                        "3" => wide ? 655.0 : 416.0,
                        "4" => wide ? 591.16 : 352.16,
                        "5" => wide ? 534.52 : 295.52,
                        "6" => wide ? 485.08 : 246.08,
                        _ => wide ? 729.0 : 490.0,
                    }
                    : wide ? 729.0 : 490.0);
            }

            var content = ExtractBalancedDivInner(rowHtml, "sr-content");
            if (content is null) continue;
            // a form that frames its own note boxes places them through that path
            // instead of the block-per-sheet pagination the other dialect needs
            row.Warn = _stepParaMarginPt is null && (Regex.IsMatch(content,
                @"class\s*=\s*(['""])[^'""]*step-warning[\s'""]", RegexOptions.IgnoreCase)
                || Regex.IsMatch(content, @"class\s*=\s*(['""])[^'""]*step-warning\1", RegexOptions.IgnoreCase));
            // the full-width generation's plain paragraphs take the linked sheet's
            // p line box (see _stepLinkedParaLinePt) — narrow step-col rows do not
            _stepRowColFull = rm.Value.Contains("step-col-full", StringComparison.OrdinalIgnoreCase);
            row.ColFull = _stepRowColFull;
            row.Items = WalkStepContent(content);

            // The row is a flex line, so it is as tall as its tallest column - and the
            // acknowledge column is a column like any other. A bare `sr-ack` holder is
            // banked so far right that nothing it carries falls on the sheet, but it
            // still sets a floor under the row. The measured anchors: the
            // checkbox blank sits 13.87 below the widget's top, the signature's a
            // little lower, the boolean's pair of option boxes 3.00 below it and 13.50
            // tall, and each widget then stacks its own labels underneath.
            var ackBox = ExtractBalancedDivInner(rowHtml, "sr-ack");
            if (ackBox is not null)
                foreach (Match wm3 in Regex.Matches(ackBox,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>",
                    RegexOptions.IgnoreCase))
                {
                    var wIn = ExtractBalancedInnerAt(ackBox, wm3.Index);
                    if (wIn is null) continue;
                    var labels = 0;
                    foreach (Match lm3 in Regex.Matches(wIn,
                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                        RegexOptions.IgnoreCase))
                        if (DecodeEntities(lm3.Groups[2].Value).Trim().Length > 0) labels++;
                    row.AckHeightPt += wm3.Groups[2].Value.ToLowerInvariant() switch
                    {
                        "signature" => 21.75 + 5.25 * labels,
                        "boolean" => 16.50 + 13.50 * labels,
                        _ => 18.75 + 5.25 * labels,
                    };
                }

            // The acknowledge column is drawn only when the row banks it to the sheet's
            // end: a bare `sr-ack` holder carries no widget the form puts on the page.
            var ack = ExtractBalancedDivInner(rowHtml, "justify-content-end");
            if (ack is not null && Regex.IsMatch(ack,
                @"<td\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1",
                RegexOptions.IgnoreCase))
            {
                // The col-full form generation banks its acknowledge widgets in a
                // two-row TABLE under the content: the first row holds each widget's
                // blanks (and the labels its own cell carries), the second stacks the
                // remaining labels on a baseline all widgets share.
                row.AckTable = true;
                row.AckHair = ack.Contains('\u200a') || ack.Contains("&#8202", StringComparison.Ordinal);
                var uiM = Regex.Match(ack,
                    @"<td\b[^>]*class\s*=\s*(['""])[^'""]*userinitials-wrap[^'""]*\1[^>]*>\s*([^<]+?)\s*</td>",
                    RegexOptions.IgnoreCase);
                if (uiM.Success && uiM.Groups[2].Value.Trim().Length > 0)
                    row.AckInitials = DecodeEntities(uiM.Groups[2].Value).Trim();
                foreach (Match trm in Regex.Matches(ack, @"<tr\b[^>]*>(.*?)</tr>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var isLabelRow = Regex.IsMatch(trm.Value,
                        @"^<tr\b[^>]*class\s*=\s*['""][^'""]*empty", RegexOptions.IgnoreCase);
                    var ti = 0;
                    foreach (Match tdm in Regex.Matches(trm.Groups[1].Value,
                        @"<td\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>(.*?)</td>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        var tdInner = tdm.Groups[3].Value;
                        if (!isLabelRow)
                        {
                            var w = new AckWidget { Kind = tdm.Groups[2].Value.ToLowerInvariant() };
                            if (w.Kind == "boolean")
                            {
                                foreach (Match om in Regex.Matches(tdInner,
                                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-opt[\s'""][^>]*>",
                                    RegexOptions.IgnoreCase))
                                {
                                    var oInner = ExtractBalancedInnerAt(tdInner, om.Index);
                                    if (oInner is null) continue;
                                    var isBox = Regex.IsMatch(oInner,
                                        @"class\s*=\s*(['""])[^'""]*abw-blank[^'""]*box[^'""]*\1",
                                        RegexOptions.IgnoreCase);
                                    // the blank's own body is the CHECK slot, the label its own div
                                    var blankBody = Regex.Match(oInner,
                                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-blank[^'""]*\1[^>]*>(?<b>[^<]*)</div>",
                                        RegexOptions.IgnoreCase);
                                    var isCheck = blankBody.Success && Regex.IsMatch(
                                        blankBody.Groups["b"].Value, @"&check;|✓");
                                    var lblM = Regex.Match(oInner,
                                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                                        RegexOptions.IgnoreCase);
                                    var optLabel = lblM.Success
                                        ? Regex.Replace(DecodeEntities(lblM.Groups[2].Value), @"\s+", " ").Trim()
                                        : null;
                                    w.Blanks.Add((49.95, isBox,
                                        optLabel is { Length: > 0 } ? optLabel : null, isCheck));
                                }
                            }
                            else
                            {
                                var cbBody = Regex.Match(tdInner,
                                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[cs]w-blank[^'""]*\1[^>]*>(?<b>[^<]*)</div>",
                                    RegexOptions.IgnoreCase);
                                w.Blanks.Add((104.25, false, null,
                                    cbBody.Success && Regex.IsMatch(cbBody.Groups["b"].Value, @"&check;|✓")));
                                foreach (Match lm in Regex.Matches(tdInner,
                                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[cs]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                                    RegexOptions.IgnoreCase))
                                {
                                    var lt = DecodeEntities(lm.Groups[2].Value).Trim();
                                    if (lt.Length > 0) w.TopLabels.Add(lt);
                                }
                            }
                            if (w.Blanks.Count > 0) row.Acks.Add(w);
                        }
                        else if (ti < row.Acks.Count)
                        {
                            foreach (Match lm in Regex.Matches(tdInner,
                                @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                                RegexOptions.IgnoreCase))
                            {
                                var lt = DecodeEntities(lm.Groups[2].Value).Trim();
                                if (lt.Length > 0) row.Acks[ti].Labels.Add(lt);
                            }
                        }
                        ti++;
                    }
                }
                row.HasAck = row.Acks.Count > 0;
            }
            else if (ack is not null)
            {
                foreach (Match wm2 in Regex.Matches(ack,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>",
                    RegexOptions.IgnoreCase))
                {
                    var wInner = ExtractBalancedInnerAt(ack, wm2.Index);
                    if (wInner is null) continue;
                    var w = new AckWidget();
                    var kind = wm2.Groups[2].Value.ToLowerInvariant();
                    w.Kind = kind;
                    // the generator writes a hair space before a checkbox blank; it is
                    // a real line box above the blank and deepens the widget's stack
                    w.Hair = Regex.IsMatch(wInner,
                        @"(?: |&#8202;?)\s*<div\b[^>]*acw-blank", RegexOptions.IgnoreCase);
                    if (kind == "boolean")
                    {
                        foreach (Match om in Regex.Matches(wInner,
                            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-opt[\s'""][^>]*>",
                            RegexOptions.IgnoreCase))
                        {
                            var oInner = ExtractBalancedInnerAt(wInner, om.Index);
                            if (oInner is null) continue;
                            var isBox = Regex.IsMatch(oInner,
                                @"class\s*=\s*(['""])[^'""]*abw-blank[^'""]*box[^'""]*\1", RegexOptions.IgnoreCase);
                            var optLabel = Regex.Replace(
                                DecodeEntities(HtmlFragment.StripHtmlTags(oInner)), @"\s+", " ").Trim();
                            w.Blanks.Add((isBox ? 50.2 : 49.0, isBox, optLabel.Length > 0 ? optLabel : null, false));
                        }
                    }
                    else
                    {
                        w.Blanks.Add((kind == "checkbox" ? 104.64 : 104.0, false, null, false));
                    }
                    foreach (Match lm2 in Regex.Matches(wInner,
                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                        RegexOptions.IgnoreCase))
                        w.Labels.Add(DecodeEntities(lm2.Groups[2].Value).Trim());
                    if (w.Blanks.Count > 0) row.Acks.Add(w);
                }
                row.HasAck = row.Acks.Count > 0;
            }
            // a numbered step stands on the sheet even when it carries no content: the
            // form still gives it its number and the height its acknowledge column needs
            if (row.Items.Count > 0 || row.Bullet is not null) rows.Add(row);
        }
        return rows.Count > 0;
    }

    /// <summary>Linear walk of step-content HTML into flowed items. Data-entry table
    /// cells recurse through the same walk, keeping only their line items.</summary>
    private static List<StepItem> WalkStepContent(string html)
    {
        var items = new List<StepItem>();
        var line = new StepLine();
        var boldDepth = 0;
        var inDetable = false;
        var pSawText = false;
        var pHadContent = false;
        double pendingPad = 0, gapNext = 0, headingPt = 0, headingLinePt = 0;
        var inChoice = false;
        var inPara = false;

        void Flush()
        {
            while (line.Segs.Count > 0 && line.Segs[^1].Text is { } t && t.Trim().Length == 0)
                line.Segs.RemoveAt(line.Segs.Count - 1);
            line.FontPt = headingPt;
            if (headingLinePt > 0) line.LinePt = headingLinePt;
            else if (inChoice && line.LinePt <= 0) line.LinePt = SwmOptionPitch;
            // a paragraph sets on the line box the document declares for it, grown to the
            // blank's own box where the line carries one
            else if (inPara && line.LinePt <= 0 && _stepParaLinePt > 0)
            {
                line.LinePt = _stepParaLinePt;
                // a blank is seated ON the baseline, so the box it needs is added under
                // the line rather than shared around it
                if (line.Segs.Exists(sg => sg.BlankPt > 0) && SwElementLinePt > _stepParaLinePt)
                {
                    line.AscentLinePt = _stepParaLinePt;
                    line.LinePt = SwElementLinePt;
                }
            }
            // …and a full-width row's paragraph takes the LINKED sheet's p line box
            // when the document's own styles declare none (see _stepLinkedParaLinePt)
            else if (inPara && line.LinePt <= 0 && _stepRowColFull && _stepLinkedParaLinePt > 0)
            {
                line.LinePt = _stepLinkedParaLinePt;
                if (line.Segs.Exists(sg => sg.BlankPt > 0) && SwElementLinePt > _stepLinkedParaLinePt)
                {
                    line.AscentLinePt = _stepLinkedParaLinePt;
                    line.LinePt = SwElementLinePt;
                }
            }
            if (line.Segs.Count > 0)
            {
                // a label that ends the line with nothing in it draws nothing and still
                // asks for its margin
                line.TrailPadPt = pendingPad;
                items.Add(new StepItem { Line = line, GapBefore = gapNext, KeepWithNext = inDetable });
                gapNext = 0;
            }
            line = new StepLine();
        }

        void EmitText(string raw)
        {
            var text = Regex.Replace(DecodeEntities(raw).Replace(' ', ' '), @"\s+", " ");
            foreach (var piece in Regex.Split(text, "([⃝☐◯⬤])"))
            {
                if (piece.Length == 0) continue;
                if (piece is "⃝" or "◯")
                { line.Segs.Add(new StepSeg { Radio = true }); pendingPad = 0; continue; }
                // the form face has no BLACK LARGE CIRCLE: the selected option's
                // glyph renders as the missing-glyph box, same ink as the checkbox
                if (piece is "☐" or "⬤")
                { line.Segs.Add(new StepSeg { Checkbox = true }); pendingPad = 0; continue; }
                if (piece.Trim().Length == 0)
                {
                    pSawText = true;
                    if (line.Segs.Count > 0 && !(line.Segs[^1].Text is { } pt && pt.EndsWith(' ')))
                        line.Segs.Add(new StepSeg { Text = " " });
                    continue;
                }
                line.Segs.Add(new StepSeg { Text = piece, Bold = boldDepth > 0, PadLeftPt = pendingPad });
                pendingPad = 0;
                pHadContent = true;
            }
        }

        var i = 0;
        var n = html.Length;
        while (i < n)
        {
            if (html[i] != '<')
            {
                var j = html.IndexOf('<', i);
                if (j < 0) j = n;
                EmitText(html[i..j]);
                i = j;
                continue;
            }
            var end = html.IndexOf('>', i);
            if (end < 0) break;
            var tagStr = html[i..(end + 1)];
            var nm = Regex.Match(tagStr, @"^</?\s*([A-Za-z][A-Za-z0-9]*)");
            if (!nm.Success) { i = end + 1; continue; }
            var tag = nm.Groups[1].Value.ToLowerInvariant();
            var isClose = tagStr[1] == '/';
            var selfClosed = tagStr.EndsWith("/>", StringComparison.Ordinal);
            var cls = Regex.Match(tagStr, @"class\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;
            var style = Regex.Match(tagStr, @"style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;

            // An author's own table in the step content — no widget wrapper around it —
            // still lays out as a grid rather than dissolving into the line stream.
            if (tag == "table" && !isClose)
            {
                Flush();
                inDetable = false;
                var tblEnd = SkipElement(html, i, "table");
                var tbl = ParseStepTable(html[i..tblEnd], StepWrapAlign(cls, style));
                if (tbl is not null)
                {
                    // the grid's own box opens where the line above it closes: the
                    // spacing it carries down its own edge is all that stands between
                    items.Add(new StepItem { Table = tbl, GapBefore = gapNext });
                    gapNext = 0;
                }
                i = tblEnd;
                continue;
            }

            double WidthPt()
            {
                var wm = Regex.Match(style, @"width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                return wm.Success
                    ? double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                    : 0;
            }

            if (tag == "br")
            {
                // A break always closes a line box; with nothing on the line - after a
                // block has just closed, say - it closes an empty one.
                var before = items.Count;
                Flush();
                if (items.Count == before)
                {
                    items.Add(new StepItem { Line = new StepLine(), GapBefore = gapNext, KeepWithNext = inDetable });
                    gapNext = 0;
                }
                i = end + 1;
                continue;
            }
            if (tag == "img") { i = end + 1; continue; }
            if (tag is "b" or "strong") { boldDepth += isClose ? -1 : 1; i = end + 1; continue; }
            if (isClose)
            {
                // a block that closes ends the line it was on, so a break that follows
                // it starts - and closes - an empty one
                if (tag == "div") Flush();
                if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                {
                    Flush();
                    gapNext += HeadingMetrics(tag).Margin;
                    headingPt = 0;
                    headingLinePt = 0;
                }
                if (tag == "p")
                {
                    Flush();
                    if (pSawText && !pHadContent)
                        items.Add(new StepItem
                        {
                            Line = new StepLine
                            {
                                Segs = { new StepSeg { Text = " " } },
                                EmptyPara = true,
                                // On the linked col-full rhythm an empty paragraph is
                                // ONE line box, flush; the legacy
                                // line-box-plus-margins pricing is the narrow
                                // family's calibration.
                                LinePt = _stepParaLinePt > 0 ? _stepParaLinePt
                                    : _stepRowColFull && _stepLinkedParaLinePt > 0
                                        ? _stepLinkedParaLinePt : 0,
                                ParaMarginPt = _stepParaMarginPt ?? 0,
                                BlockMargined = _stepParaMarginPt is not null
                                    || _stepParaLinePt <= 0
                                        && _stepRowColFull && _stepLinkedParaLinePt > 0,
                            },
                            GapBefore = gapNext,
                            KeepWithNext = inDetable,
                        });
                    if (pSawText && !pHadContent) gapNext = 0;
                    inPara = false;
                    pSawText = pHadContent = false;
                }
                i = end + 1;
                continue;
            }

            // A block that declares its own text alignment sets every line it holds that
            // way across the content column.
            // ⚠ an author's OWN block only - the form's table wraps carry text-align too and
            // must keep going through the table path
            if (!isClose && tag is "div" or "p" && !selfClosed && cls.Length == 0
                && Regex.IsMatch(style, @"text-align\s*:\s*(center|right)", RegexOptions.IgnoreCase))
            {
                Flush();
                var aInner = ExtractBalancedInnerAt(html, i, out var aPast);
                if (aInner is not null)
                {
                    var al = Regex.IsMatch(style, @"text-align\s*:\s*center", RegexOptions.IgnoreCase)
                        ? 1 : 2;
                    var aItems = WalkStepContent(aInner);
                    if (aItems.Count > 0)
                    {
                        aItems[0].GapBefore = gapNext;
                        gapNext = 0;
                        foreach (var ai in aItems) if (ai.Line is { } al2) al2.Align = al;
                        items.AddRange(aItems);
                    }
                    i = aPast;
                    continue;
                }
                i = end + 1;
                continue;
            }

            // A note, caution, ALARA or warning box: a framed block the form rules in its
            // own border width, holding a centred caption over its text. The caption sits
            // in a box of its own declared width, centred in the content column, and the
            // frame stands at least 80 css px tall.
            var nbm = Regex.Match(cls, @"step-(note|caution|alara|warning)(?![-\w])",
                RegexOptions.IgnoreCase);
            if (!isClose && nbm.Success && _stepParaMarginPt is not null)
            {
                Flush();
                var boxInner = ExtractBalancedInnerAt(html, i, out var boxPast);
                if (boxInner is not null)
                {
                    var kind = nbm.Groups[1].Value.ToLowerInvariant();
                    items.Add(new StepItem
                    {
                        BoxBorderPt = kind is "caution" or "warning" ? 3.75 : 0.75,
                        BoxDouble = kind == "caution",
                        BoxPadTopPt = _stepBoxPadPt is not null
                            && _stepBoxPadPt.TryGetValue(kind, out var bp) ? bp : 0.0,
                        GapBefore = gapNext,
                    });
                    gapNext = 0;
                    var boxItems = WalkStepContent(boxInner);
                    if (boxItems.Count > 0 && boxItems[0].Line is { } cap)
                    {
                        cap.CenterBoxPt = 0.75 * kind switch
                        {
                            "caution" => 83.0, "warning" => 88.0, "alara" => 66.0, _ => 55.0,
                        };
                        foreach (var cs in cap.Segs)
                            if (cs.Text is not null)
                            { cs.Bold = true; cs.Text = cs.Text.ToUpperInvariant(); }
                        boxItems[0].GapBefore = _stepParaMarginPt.Value;
                    }
                    items.AddRange(boxItems);
                    items.Add(new StepItem { BoxEnd = true });
                    i = boxPast;
                    continue;
                }
                i = end + 1;
                continue;
            }

            // every smart-widget table kind wraps its grid the same way (data entry,
            // M&TE matrix, list), so they all place through the one table path
            if (cls.Contains("swdt-tablewrap", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swmt-tablewrap", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swl-tablewrap", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                inDetable = false;
                var inner = ExtractBalancedInnerAt(html, i, out var past);
                if (inner is not null)
                {
                    var tbl = ParseStepTable(inner, StepWrapAlign(cls, style));
                    if (tbl is not null)
                    {
                        items.Add(new StepItem { Table = tbl, GapBefore = gapNext + 6 });
                        gapNext = 0;
                    }
                    i = past;
                    continue;
                }
                i = end + 1;
                continue;
            }
            // Each option of a multiple-choice widget is a block: it takes its own line
            // under the widget's label rather than running on beside it, and the widget
            // paces its lines wider than the form's own pitch (15.85 across a
            // label->option->option->option run).
            if (!isClose && cls.Contains("swm-option", StringComparison.OrdinalIgnoreCase))
            {
                line.LinePt = SwmOptionPitch;
                Flush();
                line.MarginTopPt = 2.25;      // .swm-option { margin-top: 3px }
                inChoice = true;
                i = end + 1;
                continue;
            }
            if (!isClose && cls.Contains("smart-widget-multiplechoice", StringComparison.OrdinalIgnoreCase))
                inChoice = true;
            if (cls.Contains("swdt-caption", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var inner = ExtractBalancedInnerAt(html, i, out var past);
                if (inner is not null)
                {
                    EmitText(inner);
                    Flush();
                    gapNext += 3.75;    // caption margin-bottom, 5 css px
                    i = past;
                    continue;
                }
                i = end + 1;
                continue;
            }
            // a heading is a block of its own, like a paragraph: it ends the line the
            // content before it was on, then sets at its own size with its own margins
            if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                Flush();
                var hm = HeadingMetrics(tag);
                gapNext += hm.Margin;
                headingPt = hm.Size;
                headingLinePt = hm.Line;
                pSawText = pHadContent = false;
                i = end + 1;
                continue;
            }
            if (tag == "p")
            {
                Flush();
                // A paragraph is a block of its own: it carries a margin above and below
                // that collapses with its neighbour's rather than adding to it, and none
                // at all against the ends of the content column.
                if (items.Count > 0 && _stepParaMarginPt is { } pMar)
                    gapNext = Math.Max(gapNext, pMar);
                inPara = true;
                pSawText = pHadContent = false;
                i = end + 1;
                continue;
            }
            if (cls.Contains("sws-element", StringComparison.OrdinalIgnoreCase))
            {
                // display:block blank: the underline takes its own line
                Flush();
                line.Segs.Add(new StepSeg { BlankPt = WidthPt() });
                Flush();
                i = selfClosed ? end + 1 : SkipElement(html, i, tag);
                continue;
            }
            if (cls.Contains("swe-element", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swt-element", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swn-element", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swd-element", StringComparison.OrdinalIgnoreCase))
            {
                line.Segs.Add(new StepSeg
                {
                    BlankPt = WidthPt(),
                    PadLeftPt = cls.Contains("swe-element", StringComparison.OrdinalIgnoreCase) ? 6 : 3,
                });
                pendingPad = 0;
                i = selfClosed ? end + 1 : SkipElement(html, i, tag);
                continue;
            }
            if (cls.Contains("swb-symbol", StringComparison.OrdinalIgnoreCase))
            {
                line.Segs.Add(new StepSeg { Radio = true });
                pendingPad = 0;
                i = selfClosed ? end + 1 : SkipElement(html, i, tag);
                continue;
            }
            if (Regex.IsMatch(cls, @"sw[a-z]+-label", RegexOptions.IgnoreCase))
            {
                pendingPad = Regex.IsMatch(cls, @"(^|\s)ml-0(\s|$)") ? 0
                    : Regex.IsMatch(cls, @"sw[bs]-label", RegexOptions.IgnoreCase) ? 6 : 3;
                i = end + 1;
                continue;
            }
            // A widget that places a grid labels it first, and the label travels with the
            // grid across a sheet - the data-entry and the M&TE widget both do this.
            if (Regex.IsMatch(cls, @"smart-widget-(de|mte-)table", RegexOptions.IgnoreCase))
            {
                Flush();
                inDetable = true;
                i = end + 1;
                continue;
            }
            if (cls.Contains("smart-widget-signature", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swes-block", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swe-block", StringComparison.OrdinalIgnoreCase))
                Flush();
            i = end + 1;
        }
        Flush();
        return items;
    }

    /// <summary>The line a multiple-choice widget paces its label and options on -
    /// wider than the form's own pitch. Measured across six consecutive gaps of a
    /// label->option->option->option run.</summary>
    internal const double SwmOptionPitch = 15.85;

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

    /// <summary>The layout side reads the parsed document's paragraph margin back when it
    /// seats the acknowledge table under the content.</summary>
    internal static double? StepParaMargin => _stepParaMarginPt;

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

    /// <summary>Every fill-in blank the form draws is an inline-block 15 css px tall with a
    /// 3 px margin under it, so a paragraph carrying one sets on an 18 px line box.</summary>
    private const double SwElementLinePt = 18 * 0.75;

    /// <summary>Read <c>hN { font-size: Npx; line-height: Npx }</c> out of the document's
    /// style blocks. A form that sizes its headings this way also resets their margin
    /// (<c>h1,…,h6 { margin: 0 }</c>) and their weight, so headings set at their declared
    /// size, on their declared line, with nothing above or below.</summary>
    private static void ReadStepHeadingCss(string html)
    {
        _stepHeadingCss = null;
        _stepBoxPadPt = null;
        foreach (Match bm in Regex.Matches(html,
            @"\.step-(note|caution|warning|alara)\b[^{]*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase))
        {
            var pt = Regex.Match(bm.Groups["body"].Value,
                @"padding-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (pt.Success)
                (_stepBoxPadPt ??= new())[bm.Groups[1].Value.ToLowerInvariant()] =
                    0.75 * double.Parse(pt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        Dictionary<string, (double, double, double)>? map = null;
        foreach (Match m in Regex.Matches(html,
            @"(?<tag>h[1-6])\s*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase))
        {
            var body = m.Groups["body"].Value;
            var fs = Regex.Match(body, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (!fs.Success) continue;
            var lh = Regex.Match(body, @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            var size = double.Parse(fs.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            var line = lh.Success
                ? double.Parse(lh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : size;
            (map ??= new())[m.Groups["tag"].Value.ToLowerInvariant()] = (size, line, 0.0);
        }
        if (map is not null) _stepHeadingCss = map;

        // A paragraph's margin is one em of the size the document gives it. A form that
        // declares nothing for `p` is left alone: those documents lay flush, and the
        // no-`p`-rule family is calibrated that way.
        _stepParaMarginPt = null;
        _stepParaLinePt = 0;
        var pm = Regex.Match(html,
            @"(?<![-\w])p\s*\{(?<body>[^}]*font-size\s*:\s*(?<px>[\d.]+)\s*px[^}]*)\}",
            RegexOptions.IgnoreCase);
        if (pm.Success && double.TryParse(pm.Groups["px"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pxv) && pxv > 0)
        {
            var paraPt = pxv * 0.75;
            _stepParaMarginPt = paraPt * 1.12;
            var plh = Regex.Match(pm.Groups["body"].Value,
                @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            _stepParaLinePt = plh.Success
                ? double.Parse(plh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : paraPt;
        }
    }

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

    /// <summary>Parse a data-entry <c>swdt-table</c>: fixed th widths, header texts,
    /// and td cells re-walked into line stacks.</summary>
    /// <summary>Where a table wrap seats its grid in the content column. The form says so
    /// either in the wrap's own class (<c>swmt-tablewrap right</c>) or inline
    /// (<c>style='text-align:right;'</c>); both spellings appear in one document.</summary>
    private static int StepWrapAlign(string cls, string style)
    {
        var m = Regex.Match(style, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(cls, @"(?:^|\s)(left|center|right)(?:\s|$)", RegexOptions.IgnoreCase);
        return m.Success
            ? m.Groups[1].Value.ToLowerInvariant() switch { "center" => 1, "right" => 2, _ => 0 }
            : 0;
    }

    private static StepTable? ParseStepTable(string wrapHtml, int align)
    {
        var tm = Regex.Match(wrapHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tm.Success) return null;
        var t = new StepTable { Align = align };
        // An author's own table sets at the size the form gives its tables — a step
        // below the body — but a table whose cells carry the form's own widgets keeps
        // the widgets' size, because it is their label runs that set the text.
        if (!Regex.IsMatch(tm.Value, @"class\s*=\s*['""]sw", RegexOptions.IgnoreCase))
        {
            t.FormRhythm = true;
            if (wrapHtml.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0)
                t.CellFontPt = 10.5;
        }
        var csm = Regex.Match(tm.Value, @"cellspacing\s*=\s*[""']?([\d.]+)",
            RegexOptions.IgnoreCase);
        if (csm.Success)
            t.CellSpacingPt = double.Parse(csm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        // ⚠ the declared width, NOT the max-width that may sit in front of it in the
        // same style - an unbounded `width` matches inside `max-width` and the grid then
        // fills the whole column
        var wm = Regex.Match(tm.Value, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        if (wm.Success)
        {
            t.WidthPt = double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            t.WidthDeclared = true;
        }

        foreach (Match trm in Regex.Matches(wrapHtml, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var tr = trm.Groups[1].Value;
            var ths = Regex.Matches(tr, @"<th\b([^>]*)>([\s\S]*?)</th\s*>", RegexOptions.IgnoreCase);
            if (ths.Count > 0 && t.Header.Count == 0)
            {
                foreach (Match th in ths)
                {
                    var wa = Regex.Match(th.Groups[1].Value, @"width\s*=\s*['""]?([\d.]+)px", RegexOptions.IgnoreCase);
                    t.ColPts.Add(wa.Success
                        ? double.Parse(wa.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                        : 48.75);
                    t.Header.Add(Regex.Replace(
                        DecodeEntities(HtmlFragment.StripHtmlTags(th.Groups[2].Value)), @"\s+", " ").Trim());
                }
                continue;
            }
            var tds = Regex.Matches(tr, @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (tds.Count == 0) continue;
            // A table that heads its columns with plain cells rather than <th> declares
            // the grid on its first row: take the widths from there.
            if (t.ColPts.Count == 0 && t.Header.Count == 0)
                foreach (Match td in tds)
                {
                    var cw = Regex.Match(td.Groups[1].Value,
                        @"width\s*[:=]\s*['""]?\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                    t.ColPts.Add(cw.Success
                        ? double.Parse(cw.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                        : 48.75);
                }
            var rowCells = new List<List<StepLine>>();
            var rowBg = new List<Color?>();
            foreach (Match td in tds)
            {
                var cellLines = new List<StepLine>();
                foreach (var it in WalkStepContent(td.Groups[2].Value))
                    if (it.Line is not null) cellLines.Add(it.Line);
                rowCells.Add(cellLines);
                var bgm = Regex.Match(td.Groups[1].Value,
                    @"background(?:-color)?\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
                rowBg.Add(bgm.Success ? ParseCssColor(bgm.Groups[1].Value) : null);
            }
            t.Rows.Add(rowCells);
            t.RowBg.Add(rowBg);
            // the row is at least as tall as the tallest min-height its cells declare
            var minH = 0.0;
            foreach (Match mh in Regex.Matches(tr,
                @"min-height\s*:\s*([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase))
                if (double.TryParse(mh.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var mhv))
                    minH = Math.Max(minH, mh.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                        ? mhv * 0.75 : mhv);
            t.RowMinPt.Add(minH);
        }
        if (t.ColPts.Count == 0 || t.Rows.Count == 0) return null;
        if (t.WidthPt <= 0) foreach (var c in t.ColPts) t.WidthPt += c;
        return t;
    }

    /// <summary>Index just past the matching close of the element opening at
    /// <paramref name="openIdx"/> (same-tag nesting honored).</summary>
    private static int SkipElement(string html, int openIdx, string tag)
    {
        var d = 0;
        foreach (Match m in Regex.Matches(html[openIdx..], @"<(/?)" + tag + @"\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (m.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                if (d == 0) return openIdx + m.Index + m.Length;
                continue;
            }
            d += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (d == 0) return openIdx + m.Index + m.Length;
        }
        return html.Length;
    }

    /// <summary>The inner HTML of the first <c>&lt;div&gt;</c> whose class contains
    /// <paramref name="classToken"/>, honoring nested div nesting.</summary>
    private static string? ExtractBalancedDivInner(string html, string classToken)
    {
        var open = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*" + Regex.Escape(classToken) + @"[^'""]*\1[^>]*>",
            RegexOptions.IgnoreCase);
        return open.Success ? ExtractBalancedInnerAt(html, open.Index) : null;
    }

    /// <summary>Balanced inner HTML of the div opening at <paramref name="openIdx"/>;
    /// the overload with <paramref name="pastEnd"/> also reports the index just past
    /// the close tag.</summary>
    private static string? ExtractBalancedInnerAt(string html, int openIdx)
        => ExtractBalancedInnerAt(html, openIdx, out _);

    private static string? ExtractBalancedInnerAt(string html, int openIdx, out int pastEnd)
    {
        pastEnd = html.Length;
        var open = Regex.Match(html[openIdx..], @"^<div\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return null;
        var i = openIdx + open.Length;
        var d = 1;
        foreach (Match t in Regex.Matches(html[i..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            d += t.Groups[1].Value.Length > 0 ? -1 : 1;
            if (d == 0)
            {
                pastEnd = i + t.Index + t.Length;
                return html.Substring(i, t.Index);
            }
        }
        return null;
    }

    /// <summary>Resolve knockout <c>data-bind="text: name"</c> spans against observable
    /// literals declared in the document's own scripts (<c>name = ko.observable('…')</c>,
    /// applied via <c>ko.applyBindings</c>): the bound span renders its observable's text.
    /// The enclosing heading splits at the span so the bound text keeps its own DOM-node
    /// run (and takes the browser heading size), matching how a scripted engine draws
    /// it. HTML without both binding halves passes through untouched.</summary>
    internal static string ApplyKnockoutTextBindings(string html)
    {
        if (string.IsNullOrEmpty(html)
            || html.IndexOf("data-bind", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("ko.observable", StringComparison.Ordinal) < 0
            || !Regex.IsMatch(html, @"ko\.applyBindings\s*\(")) return html ?? "";

        var lits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match sm in Regex.Matches(html, @"<script\b[^>]*>([\s\S]*?)</script\s*>",
                     RegexOptions.IgnoreCase))
            foreach (Match om in Regex.Matches(sm.Groups[1].Value,
                         @"(?:this\s*\.\s*)?(\w+)\s*=\s*ko\.observable\(\s*(['""])(.*?)\2\s*\)"))
                lits[om.Groups[1].Value] = om.Groups[3].Value;
        if (lits.Count == 0) return html;

        return Regex.Replace(html,
            @"<(h[1-6])([^>]*)>((?:(?!</\1>|<span)[\s\S])*?)<span[^>]*data-bind\s*=\s*(['""])\s*text\s*:\s*(\w+)\s*\4[^>]*>[\s\S]*?</span>\s*</\1>",
            m =>
            {
                if (!lits.TryGetValue(m.Groups[5].Value, out var lit)) return m.Value;
                // The browser heading size (h1 = 2 em of the 12 pt UA base, h2 = 1.5 em, …)
                // applies to both halves, so the bound run wraps where the scripted
                // engine's does.
                var uaPt = m.Groups[1].Value.ToLowerInvariant() switch
                {
                    "h1" => 24, "h2" => 18, "h3" => 14, "h4" => 12, "h5" => 10, _ => 9,
                };
                var open = $"<{m.Groups[1].Value} style=\"font-size:{uaPt}pt\"{m.Groups[2].Value}>";
                return $"{open}{m.Groups[3].Value}</{m.Groups[1].Value}>{open}{lit}</{m.Groups[1].Value}>";
            }, RegexOptions.IgnoreCase);
    }
}
