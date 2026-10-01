using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PageAnalysisState
{
    public List<Aspose.Pdf.Tagged.AutoTagger.Block> result = null!;
    public List<Aspose.Pdf.Tagged.AutoTagger.Line> lines = null!;
    public TaggingRun run = null!;
    // The body text size is the most common line size; any distinct larger size is a
    // heading. Heading levels rank the distinct heading sizes largest→smallest.
    public double bodySize;
    // The usual distance between consecutive body lines; a clearly larger one ends a paragraph.
    public double pitch;
    // The right edge of the page's text.
    public double margin;
    // Per column (its right edge): whether its body lines are justified.
    public Dictionary<double, bool> justifiedColumns = new();
    // Tables come from ruling lines or aligned columns; their cell text is excluded from the
    // paragraph/heading flow and a single Table block is emitted at the table's position.
    public List<(Aspose.Pdf.Rectangle region, int rows, int cols)> tables = null!;
    public bool[] tableEmitted = null!;
    // Where each table ends: its lowest row edge (a reader sets its rows as tall as they stood, down to it).
    public double[] tableBottoms = null!;
    public int[] tableIds = null!;
    // The open paragraph: its lines (null when none is open).
    public List<Line>? paraLines;
    // The open list: its items, the item lines are joining, and where its labels start.
    public List<ListItem>? listItems;
    public ListItem? item;
    public double listLabelX;
    // The previous body line (reset by headings and tables): the reference for gaps and indents.
    public Line? prev;
    // The last heading line: a heading wrapped over lines continues from it.
    public Line? heading;
    // Per-block line list (parallel to result), used to spot in-line images and to place links.
    public List<List<Aspose.Pdf.Tagged.AutoTagger.Line>?> blockLines = null!;
    public Page page = default!;
    // The boxes the page's rules draw round content that is no table: small print in one is the box's text.
    public List<Aspose.Pdf.Rectangle> frames = [];
}
}
