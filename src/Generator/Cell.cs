using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a cell in a table row.
/// </summary>
public sealed class Cell
{
    /// <summary>Construct an empty cell with default formatting.</summary>
    public Cell() { }

    /// <summary>Construct a cell sized to <paramref name="rect"/>. The rectangle width is
    /// recorded as the cell's <see cref="Width"/>; height is currently ignored.</summary>
    public Cell(Rectangle rect)
    {
        if (rect is not null) Width = rect.Width;
    }

    /// <summary>The paragraphs (content) in this cell. Typically TextFragment instances.</summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Cell border.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Cell background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Cell padding (margin around content inside the cell).</summary>
    public MarginInfo? Margin { get; set; }

    /// <summary>Number of columns this cell spans. Default is 1.</summary>
    public int ColSpan { get; set; } = 1;

    /// <summary>True for the copies the layout pass appends to <see cref="Row.Cells"/>
    /// for the further grid columns a spanning cell covers. They exist so the row reads
    /// back as the grid it occupies; the layout itself must skip them, or the spanning
    /// cell would be laid out once per column it spans.</summary>
    internal bool SpanContinuation { get; set; }

    /// <summary>Set on a column-slice cell whose span is CUT by the slice edge on
    /// that side: the cut side draws no rule, and the fill and the other rules run
    /// to the box edge there (probed on the broken-header sheet: the cut spanned
    /// header fills 397..498 under a 396..498 box with top/bottom rules to 498 and
    /// no right rule).</summary>
    internal bool SpanCutLeft { get; set; }
    internal bool SpanCutRight { get; set; }

    /// <summary>Number of rows this cell spans. Default is 1.</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>If true, no border is drawn for this cell.</summary>
    public bool IsNoBorder { get; set; }

    /// <summary>Default text state for this cell. Auto-initialized so callers can
    /// mutate properties (e.g. HorizontalAlignment) without null-checking.</summary>
    public TextState? DefaultCellTextState { get; set; } = new TextState();

    /// <summary>Whether text in this cell may be word-wrapped. Wrapping is the DEFAULT
    /// (a plain cell whose text overruns its column breaks at a space), and the flag
    /// only PERMITS it: a cell wide enough for its text stays on one line either way.
    /// Turning it off makes the cell keep its text whole and let the cell's own clip
    /// crop it at the column edge ("col3 with large text string" in a 50 pt column
    /// renders as "col3 with").</summary>
    public bool IsWordWrapped { get; set; } = true;

    /// <summary>HTML NOWRAP: the cell's lines render whole — the layout pass never
    /// wraps them, even when the width estimate says they overflow the column.</summary>
    internal bool HtmlNoWrap { get; set; }

    /// <summary>Vertical alignment of the cell content. None = unset: plain
    /// rows seat content at the top, while a row-spanning cell centres its
    /// block; an EXPLICIT Top pins a spanning block to the span top.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>Horizontal alignment of the cell content.</summary>
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Width of the cell as laid out (set by the renderer; default 0).</summary>
    public double Width { get; internal set; }

    /// <summary>The cell's laid-out rectangle in page space, set by the renderer when
    /// the owning table is drawn (null until then). For a cell whose row is split
    /// across page slices this is the union of its slice rectangles.</summary>
    public Rectangle? Rect { get; internal set; }

    /// <summary>Background image painted behind cell content.</summary>
    public Image? BackgroundImage { get; set; }

    /// <summary>Path to a file used as the background image. Setting this assigns a
    /// new <see cref="Image"/> to <see cref="BackgroundImage"/> with the same file name.</summary>
    public string? BackgroundImageFile
    {
        get => BackgroundImage?.File;
        set
        {
            if (string.IsNullOrEmpty(value)) { BackgroundImage = null; return; }
            BackgroundImage = new Image { File = value };
        }
    }

    /// <summary>Whether the cell's formatting can be overridden by a contained fragment's
    /// text state. Stored only.</summary>
    public bool IsOverrideByFragment { get; set; }

    /// <summary>
    /// Convenience property: gets or sets the text of the first TextFragment in Paragraphs.
    /// Getting returns the text if the first paragraph is a TextFragment, otherwise empty string.
    /// Setting clears paragraphs and adds a new TextFragment with the given text.
    /// </summary>
    public string Text
    {
        get
        {
            if (Paragraphs.Count > 0 && Paragraphs[0] is TextFragment tf)
                return tf.Text;
            return string.Empty;
        }
        set
        {
            Paragraphs.Clear();
            Paragraphs.Add(new TextFragment(value));
        }
    }

    /// <summary>Create a shallow copy of this cell. The copy shares its content list
    /// with no other cell.</summary>
    public object Clone()
    {
        var copy = new Cell
        {
            Border = Border,
            BackgroundColor = BackgroundColor,
            Margin = Margin,
            ColSpan = ColSpan,
            RowSpan = RowSpan,
            IsNoBorder = IsNoBorder,
            DefaultCellTextState = DefaultCellTextState,
            IsWordWrapped = IsWordWrapped,
            VerticalAlignment = VerticalAlignment,
            Alignment = Alignment,
            Width = Width,
            BackgroundImage = BackgroundImage,
            IsOverrideByFragment = IsOverrideByFragment,
        };
        foreach (var p in Paragraphs)
            copy.Paragraphs.Add(p);
        return copy;
    }
}
