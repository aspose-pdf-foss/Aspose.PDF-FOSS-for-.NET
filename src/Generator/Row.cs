using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed class Row
{
    /// <summary>The cells in this row.</summary>
    public Cells Cells { get; set; } = new();

    /// <summary>Row border.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Default cell border for cells in this row.</summary>
    public BorderInfo? DefaultCellBorder { get; set; }

    /// <summary>Fixed row height. If > 0, the row height is exactly this value.</summary>
    public double FixedRowHeight { get; set; }

    /// <summary>Minimum row height.</summary>
    public double MinRowHeight { get; set; }

    /// <summary><see cref="MinRowHeight"/> is the CSS CONTENT box a fixed-height child
    /// claims, so the cell's own padding rides on top of it — as opposed to the legacy
    /// <c>height="N"</c> floor, which is the whole row height.</summary>
    internal bool MinRowHeightIsContent { get; set; }

    /// <summary>Row background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Default text state for cells in this row. Auto-initialized so callers can
    /// mutate properties without null-checking.</summary>
    public TextState? DefaultCellTextState { get; set; } = new TextState();

    /// <summary>Default cell padding for cells in this row.</summary>
    public MarginInfo? DefaultCellPadding { get; set; }

    /// <summary>Vertical alignment applied to each cell's content. Consumed
    /// by <see cref="Cell.VerticalAlignment"/> when the cell itself doesn't
    /// override it. None = unset (top-seated for plain rows; a row-spanning
    /// cell centres its block instead — an EXPLICIT Top there pins the block
    /// to the span top, which is why unset must stay distinguishable).</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>
    /// Indicates whether this row will be rendered on a new page during multi-page table layout.
    /// Set automatically by the table layout engine during <see cref="Table.Build(Page)"/> or document save.
    /// A CALLER may also set it to demand that the row opens a page; the layout honours
    /// that demand and then overwrites the property with where the row actually landed.
    /// </summary>
    public bool IsInNewPage
    {
        get => _isInNewPage;
        set { _isInNewPage = value; IsInNewPageAuthored = value; }
    }

    private bool _isInNewPage;

    /// <summary>True while <see cref="IsInNewPage"/> still holds a CALLER's demand rather
    /// than the layout's report. The layout is run more than once per save (a measure
    /// pass, then the draw), so without this the second pass would read the first pass's
    /// report back as a demand and break a page under every row that merely happened to
    /// start one.</summary>
    internal bool IsInNewPageAuthored { get; private set; }

    /// <summary>Record where the row landed WITHOUT turning the report into a demand.</summary>
    internal void ReportInNewPage(bool value)
    {
        _isInNewPage = value;
        IsInNewPageAuthored = false;
    }

    /// <summary>
    /// Whether this row is allowed to break across pages when its cells
    /// don't fit the remaining space on the current page. When false, the
    /// row is moved entirely to the next page.
    /// Stored only; the table layout engine does not currently split rows
    /// across pages.
    /// </summary>
    public bool IsRowBroken { get; set; }

    /// <summary>Set once the layout pass has expanded this row's <see cref="Cells"/> to
    /// the grid it covers (see Table.ApplyLaidOutCellGrid). Keeps a second pass over an
    /// already-published row from inserting the span continuations twice.</summary>
    internal bool CellGridPublished { get; set; }

    /// <summary>Shallow copy: a new Row whose cells reference the same
    /// <see cref="Cell"/> instances and whose scalar properties carry the
    /// same values. The Cells collection itself is independent (cloning
    /// the row and adding to one Row's Cells does not affect the other).</summary>
    public object Clone()
    {
        var clone = new Row
        {
            Border = Border,
            DefaultCellBorder = DefaultCellBorder,
            FixedRowHeight = FixedRowHeight,
            MinRowHeight = MinRowHeight,
            BackgroundColor = BackgroundColor,
            DefaultCellTextState = DefaultCellTextState,
            DefaultCellPadding = DefaultCellPadding,
            VerticalAlignment = VerticalAlignment,
            IsInNewPage = IsInNewPage,
            IsRowBroken = IsRowBroken,
        };
        foreach (var cell in Cells)
            clone.Cells.Add(cell);
        return clone;
    }
}
