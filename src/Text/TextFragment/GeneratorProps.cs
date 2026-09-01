
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>Tab stops for this fragment (used with #$TAB markers in text).</summary>
    public TabStops? TabStops { get; set; }

    // Built by Document.BindXml: lay this fragment out with the classic
    // XML-generator line model (verbatim segment whitespace, line pitch = font
    // size + leading, per-run AFM-descent baselines, #$TAB stops). See
    // FlowLayout.WriteXmlModelFragment.
    internal bool XmlGeneratorModel;

    /// <summary>True for the paragraph a <see cref="Note"/> built from its string
    /// content: its box is only as wide as its text (a caller-built note paragraph
    /// claims the whole band width).</summary>
    internal bool AutoNoteText;

    /// <summary>True for an XML <c>&lt;TextFragment /&gt;</c> authored without any
    /// <c>&lt;TextSegment&gt;</c>: such a shell takes no room, where a fragment whose
    /// authored segment is empty still occupies one default-size line.</summary>
    internal bool XmlEmptyShell;

    // CSS line-box font metrics (fractions of em) for the HTML→PDF table-cell path.
    // When set (> 0), a generator table cell holding lines of MIXED font sizes lays this
    // fragment's line out as a CSS line box: height = line-height × em, baseline at
    // ascent × em + half-leading below the box top. Zero = legacy uniform-row layout.
    internal double CssAscent;

    internal double CssDescent;

    // CSS line-height (pt) carried into table-cell layout: wrapped cell lines
    // pitch at this height instead of the bare font size when set.
    internal double CssLineHeightPt;

    // True when CssLineHeightPt came from the CELL'S OWN inline `line-height`
    // declaration: the lifted table dialect then makes it each line's BOX height
    // (the css-box stack advance), not just the row pitch. Document-level pitches
    // stay row-level — the calibrated dialects depend on that.
    internal bool CssLineHeightFromCell;

    // Inline boxes drawn behind this fragment's first laid-out cell line (HTML
    // inline-block plates/pills, pre-measured by the converter with the metrics
    // that lay the line out); consumed by the generator table's render pass.
    internal List<InlineBoxDecoration>? InlineBoxes;

    // Radio options riding this fragment's text INLINE (an HTML form grid's
    // `◯ ◯Yes ◉ ◉No` row): one entry per Table.InlineRadioChar /
    // InlineRadioCheckedChar in Text, in order. The table render pass draws each
    // as a circle glyph in the line's run and places the option's widget there.
    internal System.Collections.Generic.List<Aspose.Pdf.Forms.RadioButtonOptionField>? InlineOptions;

    // Deliberate blank line (an explicit <br> inside a styled paragraph): keeps its
    // line box in the flow as vertical space even though it renders no text.
    internal bool CssKeepBlank;

    // pt-styled fragment: the cell paragraph's OWN horizontal margins (pt) —
    // an inset on the wrap box only (Margin.Left also carries the cell pad,
    // which the row layout's avail width has already spent).
    internal double HtmlWrapInsetPt;

    // …and the LEFT share of that inset (the paragraph's margin-left): the seat
    // indent for left-aligned lines; the remainder is the right-margin share
    // that insets a right-aligned line from the cell's padded right edge.
    internal double HtmlMarginLeftPt;

    // This cell line's text sat inside a <u> element: the table renderer strokes
    // an underline beneath the drawn run (over-declared grid dialect).
    internal bool HtmlUnderline;

    /// <summary>Cell lines render as CSS line boxes (1.2 em pitch) even when every
    /// line shares one size — set for cells sized by a stylesheet `font:` shorthand
    /// (the form-document dialect), whose reference pitch is the CSS line box.</summary>
    internal bool CssLineBoxAlways;

    // Form-grid baseline drop: the distance from this line's box top to its
    // baseline — max(strut drop, run drop), each half-leading + winAscent of
    // its box. Zero = legacy placement.
    internal double CssBaseDrop;

    /// <summary>
    /// When true, this text fragment renders on the same line as the previous
    /// in-line paragraph. Currently a state flag — layout wiring follows
    /// once Image / inline-flow rendering is implemented.
    /// </summary>
    public new bool IsInLineParagraph { get; set; }

    public new bool IsInNewPage { get; set; }

    /// <summary>
    /// Optional footnote attached to this fragment. Stored only; the
    /// layout engine does not currently render footnote references or
    /// the page-bottom note text.
    /// </summary>
    public Note? FootNote { get; set; }

    /// <summary>Internal read access to the hyperlink set via <see cref="Hyperlink"/>,
    /// used by the page layout pass to emit the corresponding link annotation.</summary>
    internal Hyperlink? HyperlinkValue => _hyperlink;

    /// <summary>Endnote attached to this fragment. Stored only.</summary>
    public Note? EndNote { get; set; }

    /// <summary>Number of wrapped lines computed during layout.
    /// 0 until layout runs.</summary>
    public int WrapLinesCount { get; set; }
}
