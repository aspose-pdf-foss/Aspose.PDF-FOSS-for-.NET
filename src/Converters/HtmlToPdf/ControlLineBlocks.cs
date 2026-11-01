using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The control line of the two base flows: a control stands in its line of text. The parser
// gives every control and text block of one markup line the same id; where such a line holds
// a control beside text (or a select), its blocks become one ControlLine that the render loop
// lays out at the line's first block. A line of controls alone is seated the same way in the
// UA serif flow, whose control boxes are probed; in the plain flow it keeps its standalone layout.
internal static partial class HtmlToPdfConverter
{
    internal sealed partial class Block
    {
        /// <summary>The markup line this block was parsed on, while that line holds a control (0 = none).</summary>
        public int CtlLineId;
        /// <summary>The collapsed space the markup left before and after this text on its control line.</summary>
        public bool CtlSpaceBefore, CtlSpaceAfter;
        /// <summary>The control line this block opens; its other blocks are <see cref="InCtlLine"/>.</summary>
        public ControlLine? CtlLine;
        public bool InCtlLine;
        /// <summary>The line whose seat this text records while it is laid out (set for that render only).</summary>
        public ControlLine? CtlSeat;
        /// <summary>A text that only holds its line box open for controls: seated, never drawn.</summary>
        public bool CtlStrut;
        /// <summary>A text box's size attribute: the columns it shows where no width is declared (0 = the UA 20).</summary>
        public int InputColumns;
        /// <summary>A picture of declared size standing on a control line, its top margin, and the
        /// text block of its alt attribute (drawn after its box when the picture cannot be loaded).</summary>
        public bool CtlLineImage;
        public double CtlImageMarginTopPt;
        public Block? CtlAltText;
        /// <summary>Set while its line is laid out: the picture's bytes, or the advance of its alt text.</summary>
        public byte[]? CtlImageBytes;
        public double CtlAltWidthPt;
        /// <summary>A push button's declared width in points (0 = its caption and chrome).</summary>
        public double ButtonWidthPt;
        /// <summary>A select's entries, in order.</summary>
        public List<string>? SelectOptions;
    }

    /// <summary>One markup line of controls and text, and where its first text was seated.</summary>
    internal sealed class ControlLine
    {
        public readonly List<Block> Items;
        public ControlLine(List<Block> items) => Items = items;

        /// <summary>How far a control taller than the text drops the line's first baseline.</summary>
        public double LeadDropPt;
        /// <summary>The advance of the controls seated before the line's first text.</summary>
        public double LeadPt;
        public bool Seated;
        public double FirstX, FirstBaseY, LastX, LastBaseY;
        public Page? FirstPage, LastPage;
        public string LastText = "";

        public void Reset(double leadDropPt, double leadPt)
        {
            LeadDropPt = leadDropPt;
            LeadPt = leadPt;
            Seated = false;
            FirstPage = LastPage = null;
            LastText = "";
        }
    }

    /// <summary>The line writer reports each line of the text being seated: where it starts and its baseline.</summary>
    private static void RecordControlLineSeat(ControlLine seat, BlockTextState bt, string text)
    {
        var baseY = bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y;
        if (!seat.Seated)
        {
            seat.FirstX = bt.lineXPos; seat.FirstBaseY = baseY; seat.FirstPage = bt.flow.page;
            seat.Seated = true;
        }
        seat.LastX = bt.lineXPos; seat.LastBaseY = baseY; seat.LastPage = bt.flow.page;
        seat.LastText = text;
    }

    private static bool IsLineControl(Block b) => b.IsCheckbox || b.IsRadio || b.IsInputField || b.IsButton || b.CtlLineImage;

    /// <summary>A text the line can carry: plain inline text with no box, float or alignment of its own.</summary>
    private static bool IsLineText(Block b) =>
        !b.IsHardBreak && !b.IsTable && !b.IsImage && !b.IsButton && !b.IsHorizontalRule && b.Pre is null
        && b.Text.Length > 0 && !b.NoAdvanceY && !b.FloatRight && !b.FloatLeft
        && !b.AlignCenter && !b.AlignCenterCss && !b.AlignCenterAttr && !b.AlignRight && !b.AlignJustify
        && b.BackgroundColor is null && b.BorderWidth <= 0 && b.ExplicitHeight <= 0
        && b.FsBox == 0 && b.BgSpan == 0 && !b.FsLegend;

    /// <summary>Block boxes the parser reads through like inline tags: their edges still end a control line.</summary>
    private static readonly HashSet<string> ControlLineEdgeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "form", "fieldset", "legend", "section", "article", "main", "header", "footer", "nav", "aside",
        "address", "center", "dl", "dt", "dd",
    };

    /// <summary>The open control line ends here: its pending text is flushed onto it first.</summary>
    private static void EndControlLine(ParseBlocksState pb)
    {
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, pb.styleStack.Peek());
        pb.ctlLineId = 0;
    }

    /// <summary>The line's id opens at its first control, before the flush that closes the text ahead of it.</summary>
    private static void OpenControlLine(ParseBlocksState pb)
    {
        if (pb.controlLines && pb.ctlLineId == 0) pb.ctlLineId = pb.nextCtlLineId++;
    }

    private static int InputSizeAttribute(Dictionary<string, string>? attrs) =>
        attrs is not null && attrs.TryGetValue("size", out var raw)
        && int.TryParse(UnescapeAttrValue(raw), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0 ? size : 0;

    /// <summary>A text flushed on an open control line joins it, remembering the space collapsed at its edges.</summary>
    private static void JoinControlLine(ParseBlocksState pb, Block blk, List<int> rawOf)
    {
        blk.CtlLineId = pb.ctlLineId;
        if (rawOf.Count == 0) return;
        blk.CtlSpaceBefore = rawOf[0] > 0;
        blk.CtlSpaceAfter = rawOf[^1] >= 0 && rawOf[^1] < pb.currentText.Length - 1;
    }

    /// <summary>An inline picture of declared size after a control joins the control's line.</summary>
    private static void JoinControlLineImage(ParseBlocksState pb, Block image, Token tok)
    {
        if (!image.IsImage || !image.ImageInline || image.ImageAbsPos || image.FloatLeft || image.FloatRight
            || image.ImageWidth <= 0 || image.ImageHeight <= 0) return;
        image.CtlLineId = pb.ctlLineId;
        image.CtlLineImage = true;
        string? style = null;
        tok.Attributes?.TryGetValue("style", out style);
        var top = SafeRegex.Match(style ?? "", @"(?<![-\w])margin-top\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
        if (top.Success && double.TryParse(top.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var topPx))
            image.CtlImageMarginTopPt = topPx * ClPxPt;
        if (string.IsNullOrWhiteSpace(image.ImageAlt)) return;
        var st = pb.styleStack.Peek();
        image.CtlAltText = new Block
        {
            Text = DecodeEntities(image.ImageAlt!).Trim(), FontSize = st.FontSize, FontRes = st.FontRes,
            FontFamily = st.FontFamily, ForeColor = st.ForeColor, LeftIndent = st.LeftIndent,
        };
    }

    /// <summary>A text area's own style gives its box where it does so in other than pixels: a percent
    /// width is that share of the content width, an absolute height (mm, pt, in) is its box.</summary>
    private static void ApplyDeclaredAreaBox(Block area, Dictionary<string, string>? attrs)
    {
        string? style = null;
        attrs?.TryGetValue("style", out style);
        if (string.IsNullOrEmpty(style)) return;
        var width = SafeRegex.Match(style!, @"(?<![-\w])width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        if (width.Success && double.TryParse(width.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent > 0)
        {
            area.InputWidthFrac = percent / 100;
        }
        var height = SafeRegex.Match(style!, @"(?<![-\w])height\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (height.Success && height.Groups[1].Value.IndexOf("px", StringComparison.OrdinalIgnoreCase) < 0
            && TryParseLength(height.Groups[1].Value.Trim()) is { } heightPt && heightPt > 0)
        {
            area.InputHeight = heightPt + 2 * ClWidgetOverhangPt;
        }
    }

    /// <summary>The caption a submit or reset input shows when it states none.</summary>
    private static string UaButtonCaption(string type) => type switch { "submit" => "Submit", "reset" => "Reset", _ => "" };

    /// <summary>A push button of the base flows: its caption, its name, and the width its style or the sheet declares.</summary>
    private static Block PushButtonBlock(ParseBlocksState pb, Dictionary<string, string>? attrs, string tag, string caption)
    {
        var st = pb.styleStack.Peek();
        string? style = null, name = null, id = null;
        attrs?.TryGetValue("style", out style);
        attrs?.TryGetValue("name", out name);
        attrs?.TryGetValue("id", out id);
        var (widthPx, _) = ParseInputSize(style);
        if (widthPx <= 0) (widthPx, _) = ParseInputSize(SheetControlBox(pb.css, tag, attrs));
        return new Block
        {
            IsButton = true,
            ButtonCaption = DecodeEntities(caption),
            ButtonWidthPt = widthPx * ClPxPt,
            InputName = !string.IsNullOrEmpty(name) ? name : string.IsNullOrEmpty(id) ? null : id,
            FontSize = st.FontSize,
            FontRes = st.FontRes,
            LeftIndent = st.LeftIndent,
            CtlLineId = pb.ctlLineId,
        };
    }

    /// <summary>A select of the base flows: a combo box of its entries showing the chosen one, or the first.</summary>
    private static Block ComboBlock(ParseBlocksState pb, string chosen)
    {
        var st = pb.styleStack.Peek();
        var entries = pb.selectOptions.FindAll(e => e.Length > 0);
        var value = chosen;
        if (value.Length == 0 && entries.Count > 0) value = entries[0];
        return new Block
        {
            IsInputField = true,
            IsSelectBox = true,
            InputValue = value,
            InputName = string.IsNullOrEmpty(pb.selectName) ? null : pb.selectName,
            InputWidth = pb.selectWidthPx,
            SelectOptions = entries,
            FontSize = st.FontSize,
            FontRes = st.FontRes,
            LeftIndent = st.LeftIndent,
            CtlLineId = pb.ctlLineId,
        };
    }

    /// <summary>Groups the parsed blocks into control lines: each stretch of one line id, cut at any
    /// text the line cannot carry, that holds a control beside text or a picture - or a select or push button, or with
    /// <paramref name="loneControls"/> any control.</summary>
    private static void BuildControlLines(List<Block> blocks, bool loneControls)
    {
        var i = 0;
        while (i < blocks.Count)
        {
            var b = blocks[i];
            var onLine = b.CtlLineId > 0 && (IsLineControl(b) || IsLineText(b));
            if (!onLine && !b.IsSelectBox && !b.IsButton) { i++; continue; }
            var j = i;
            while (onLine && j + 1 < blocks.Count && blocks[j + 1].CtlLineId == b.CtlLineId
                && (IsLineControl(blocks[j + 1]) || IsLineText(blocks[j + 1]))) j++;
            bool control = false, text = false, select = false, picture = false;
            for (var k = i; k <= j; k++)
            {
                if (!IsLineControl(blocks[k])) text = true;
                else if (blocks[k].CtlLineImage) picture = true;
                else { control = true; select |= blocks[k].IsSelectBox || blocks[k].IsButton; }
            }
            if (control && (text || select || picture || loneControls))
            {
                var items = new List<Block>(j - i + 2);
                // (a line of controls alone has no text to seat it: an undrawn one holds its line box)
                if (!text)
                    items.Add(new Block { Text = "\u00A0", CtlStrut = true, FontSize = b.FontSize, FontRes = b.FontRes, LeftIndent = b.LeftIndent });
                for (var k = i; k <= j; k++)
                {
                    items.Add(blocks[k]);
                    blocks[k].InCtlLine = k > i;
                }
                b.CtlLine = new ControlLine(items);
            }
            i = j + 1;
        }
    }
}
