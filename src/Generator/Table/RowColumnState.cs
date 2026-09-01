using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RowColumnState
{
    public Row row = null!;
    public int[]? gridToCell;
    public int origIdx;
    public Cell cell = null!;
    // Clamp ColSpan to the slice's remaining columns so chunked rendering
    // doesn't read past the end of the slice's colWidths.
    public int span;
    public double cellWidth;
    // The last column's BOX keeps the width the pitch gave it even where that
    // overhangs the content band; only its text was clamped (see
    // LastColBoxOverhang).
    public double cellBoxWidth;
    public MarginInfo? padding;
    public double dp;
    public double padLeft;
    public double padTop;
    public Rectangle sliceRect = null!;
    // HTML cellspacing: the cell's border box insets half a spacing from the
    // row band on ALL sides — adjacent rows (and side-by-side cells) keep
    // the page visible between their borders.
    public double bandInset;
    // Pitch mode (the border joined the column pitch): the strokes sit INSIDE
    // the box and the fill covers only what the drawn sides leave (a spanned
    // cell cut at a slice edge draws no rule there and its fill reaches the
    // box edge — probed on the broken-header sheet: 397..498 under a 396..498 box).
    public BorderInfo? pitchBorder;
    // Background — rounded when the cell's border is (the detail buttons).
    public Color? bgColor;
    // Text content — render the slice's line window for this cell. Inline cells carry only
    // blank placeholder lines here (their text is drawn by the inline pass below); skip them
    // so the line-based path doesn't emit empty show-text runs that read back as stray
    // (empty) text fragments.
    public bool cellIsInline;
    public System.Collections.Generic.List<Aspose.Pdf.Table.CellLine>? cellLines;
    // Generator dialect: the face's own descent seats the baseline and bounds the
    // per-cell text clip spliced in at this mark once the block is drawn.
    public bool generatorCell;
    public int clipMark;
    // The clip is the column's inner box less the cell's OWN padding (probed:
    // 5 pt padding on a 186 pt column clips 75..251; a −25 pt left margin
    // widens the box over the text it pulls out of the column).
    public double clipPadL;
    public double clipPadR;
    // Vertical alignment: when the slice is taller than the cell's content block
    // (e.g. a MinRowHeight-floored row), Center/Bottom shift the text down within
    // the cell. Top (the default) keeps the historical top-seated placement.
    public double padBot;
    public VerticalAlignment effVA;
    // CSS line-box cell (mixed per-line font sizes): lines stack by their own box
    // heights with ascent-based baselines. Falls back to the uniform grid when the
    // row is sliced across pages (LineStart > 0).
    public bool cssCell;
    // A cell border's stroke width insets the content from the border
    // edges (a 5 pt border seats text 5 pt further in/down,
    // including the per-side GraphInfo border case).
    public BorderInfo? insetBorder;
    public double borderInsetLeft;
    public double borderInsetTop;
    public double borderInsetBottom;
    public double vaOffset;
    // Text seats at the CELL's own effective top padding (EffectivePad —
    // zero margin components fall back to the default padding, so a
    // Margin(-25,0,0,0) cell still aligns with (0,5,0,5)-padded
    // neighbours while a Margin(0,8,0,3)/(0,8,0,0) pair keeps its
    // explicit 3/0 tops).
    // MIXED-size HTML-engine cell: slice.TopY already sits one border width
    // under the row edge, which IS the content top (measured: first
    // baseline = row edge - borderW - winAsc-drop, matching the full-width
    // horizontal inset) - charging borderInsetTop again seated the whole
    // stack half a border too low. Single-size engine cells keep the
    // calibrated legacy seat (their templates carry it).
    public bool engineCssStack;
    public double contentTop;
    // Every cell baseline sits a descender ABOVE the full-em drop
    // (box-bottom-minus-descender; 0.207 = the Helvetica AFM descender,
    // the cell's own face under the generator dialect).
    public double borderLiftFactor;
    public double cellDescentEm;
    public string? cellFace;
    public bool cellFragmentFace;
}
}
