using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>What one piece of an inline-only HTML fragment is.</summary>
    private enum HtmlInlineKind { Text, Break, Image, Radio, Checkbox }

    /// <summary>One styled piece of an inline-only HTML fragment: a text run with the
    /// style its enclosing elements resolved, a hard break, a picture or a form control.</summary>
    private sealed class HtmlInlineItem
    {
        public HtmlInlineKind Kind;
        public string Text = "";
        /// <summary>CSS family the run's elements declared; null = the fragment's base face.</summary>
        public string? Family;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        /// <summary>Font size in points; 0 = the fragment's base size.</summary>
        public double SizePt;
        public Color? Fore;
        public Color? Back;
        public string? Href;
        public byte[]? ImageData;
        public double ImageW;
        public double ImageH;
        /// <summary>vertical-align: middle on a picture (centred on the x-height).</summary>
        public bool ImageMiddle;
        /// <summary>The control's name attribute.</summary>
        public string? ControlName;
        public bool Checked;
    }

    /// <summary>One placed cell of an inline line: a run's text, a picture or a control at its pen offset.</summary>
    private sealed class HtmlInlineCell
    {
        public HtmlInlineItem Item = null!;
        public string Text = "";
        public double X;
        public double W;
    }

    /// <summary>One laid-out line of the inline flow.</summary>
    private sealed class HtmlInlineLine
    {
        public List<HtmlInlineCell> Cells = new();
        public double Pen;
        public bool HasContent => Cells.Count > 0;
        public double Above;
        public double Below;
    }

    /// <summary>Per-call working state of the inline-flow renderer.</summary>
    private sealed class HtmlInlineFlowState
    {
        public HtmlFragmentLayoutState hl = null!;
        public FlowLayout flow = null!;
        /// <summary>The markup this call lays out: the whole fragment, or one text piece of a
        /// fragment whose tables are laid out between its pieces.</summary>
        public string html = "";
        /// <summary>Whether this markup is the fragment's last piece: only its final anonymous
        /// block ends at its baseline; every other block ends at its full line box.</summary>
        public bool lastPiece;
        public List<HtmlInlineItem> items = null!;
        public string baseFamily = UaInlineBaseFamily;
        public double basePt = UaInlineBasePt;
        public UaFace strut = null!;
        public Dictionary<string, UaFace?> faces = new();
        public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
        public Content.ContentStreamBuilder csb = null!;
        public Color? defaultColor;
        public double width;
        public double left;
        /// <summary>The descent the fragment's box keeps under its last baseline (the first
        /// element's face at its size; nothing for bare text or a control).</summary>
        public double endExtra;
        public int faceCount;
        public int radioCount;
        /// <summary>The widest line laid out, for the fragment's reported rectangle.</summary>
        public double widestLine;
        /// <summary>The box a fragment-level hyperlink covers so far on the current page (one
        /// Link annotation per fragment and page).</summary>
        public Rectangle? linkBox;
    }

    /// <summary>The UA body face and size an in-page fragment sets in when it names none.</summary>
    private const string UaInlineBaseFamily = "Times New Roman";
    private const double UaInlineBasePt = 12.0;
    /// <summary>CSS reference pixel in points (96 px per inch).</summary>
    private const double CssPxToPt = UaFace.CssPxToPt;
    /// <summary>A radio control's widget box, its inline margins and the line box it stands
    /// in (probed: a 9.75 square 3.75 from the pen, its bottom on the baseline, 2.25 after it,
    /// on a 12 pt line).</summary>
    private const double RadioBoxPt = 9.75;
    private const double RadioLeftMarginPt = 3.75;
    private const double RadioRightMarginPt = 2.25;
    private const double RadioLineAbovePt = 12.0;
    /// <summary>A checkbox control's widget box and margins (probed: a 7.75 square 4 from
    /// the pen, 3.25 under the line top).</summary>
    private const double CheckboxBoxPt = 7.75;
    private const double CheckboxLeftMarginPt = 4.0;
    private const double CheckboxRightMarginPt = 2.25;
    private const double CheckboxTopMarginPt = 3.25;
    /// <summary>The colour of an <c>&lt;a href&gt;</c> run.</summary>
    private static readonly Color HtmlLinkColor = Color.FromArgb(0, 0, 255);
    /// <summary>A paragraph's UA margin (spent only when the fragment asks for paragraph margins).</summary>
    private const double UaParagraphMarginEm = 1.12;
    /// <summary>UA heading sizes and margins in em, index 1..6 (probed: h6 sets at 0.75 em).</summary>
    private static readonly double[] UaHeadingSizeEm = { 0, 2.0, 1.5, 1.17, 1.0, 0.83, 0.75 };
    private static readonly double[] UaHeadingMarginEm = { 0, 0.67, 0.75, 0.83, 1.12, 1.5, 1.67 };
    /// <summary>A list's UA padding (40 px), its box margin in em of the base size, the gap
    /// between a marker's right edge and the item text, and the bullet glyph.</summary>
    private const double UaListIndentPt = 30.0;
    private const double UaListMarginEm = 1.12;
    private const double UaListMarkerGapPt = 4.5;
    private const string UaBulletMarker = "•";
    /// <summary>Points per <c>&lt;font size=N&gt;</c> step, index 1..7.</summary>
    private static readonly double[] HtmlFontSizeStepsPt = { 0, 6.75, 9.75, 12, 13.5, 18, 24, 36 };
}
