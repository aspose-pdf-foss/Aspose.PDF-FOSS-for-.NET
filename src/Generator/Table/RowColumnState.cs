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
    // The running pen: where this column starts, advanced as the cell is drawn. The
    // per-column loop in RenderRowSlice seeds it and reads it back.
    public double cellX;
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
    // the running height of the inline rows drawn so far in this cell
    public double inlineStack;
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
    // The extraction inputs, captured from the method parameters.
    public int col;
    public ContentStreamBuilder builder = null!;
    public RowSlice slice = null!;
    public double[] colWidths = null!;
    public string fontName = null!;
    public int[] cellMap = null!;
    public List<(Rectangle rect, Hyperlink link)>? links;
    public List<(byte[] data, Rectangle rect)>? imageSink;
    public List<(ReservedBlock block, ReservedPart part, Rectangle rect)>? blockSink;
    public List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink;
    public List<byte[]>? graphSink;
    public List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink;
    public Page? page;
    public List<(Note note, double x, double baseline, double size)>? footnoteSink;
    public int firstLine;
    public int lastLine;
    public bool hasOption;
    public bool anyNonLeft;
    public bool anyType0;
    public bool anyBoxes;
    public bool anyLinks;
    public bool xmlMixedSizes;
    // The cell mixes alignments (e.g. a centred title above a left HtmlFragment)
    // or carries an embedded-font (Arabic/Type0) line, an inline-box
    // decoration, so each line is positioned
    // absolutely. Lines can differ in width, hence per-line placement.
    public double padRight;
    public double cssCum;
    // DataWorks text cells walk each line by its OWN 1.125-em
    // box (the label columns pitch 13.5 while a sibling option
    // column steps 14.76 — the widget pitch never propagates
    // into the labels).
    public double dwTextWalk;
    public double xmlCum;
    public Table.CellLine line = null!;
    // A css-box line advances the stack by its OWN box height —
    // including deliberate blank lines, which occupy space silently.
    public double cssTop;
    // (advance the dw walk for every line, blanks included)
    public double dwLineOff;
    // XML own-size pitch: consume the line's height whether
    // or not it draws (a blank spacer line still advances).
    public double xmlTop;
    public double w;
    // A right-anchored BOX line (the overall-signal pill) keeps the
    // UA cell pad off the border box, the same white the full-width
    // bars below reserve — the pill must never sit flush against
    // the frame.
    public double boxRightPad;
    // A line carrying its paragraph's own margins centers on
    // the margin-inset content box, not the full padded cell.
    public double lineX;
    public double lineTop;
    // Baseline: ascent + half-leading below the box top for css-box
    // lines; the legacy full-em drop otherwise, lifted a descender
    // for explicitly-bordered cells (see borderLiftFactor).
    public double collapsedSeat;
    public double lineBase;
    public string plResolvedFont = null!;
    public Table.CellLine first = null!;
    public double textX;
    // HTML-engine metrics: the baseline sits a descender ABOVE the
    // block bottom (box = n·size), i.e. lifted by 0.207·size vs the
    // legacy full-em drop (0.207 = the Helvetica AFM descender).
    // Explicitly-bordered cells get the same lift.
    public double engineLift;
    // A caller-declared leading lies ABOVE the glyphs, so it moves
    // only the FIRST baseline down: every later line already rides
    // the row's uniform LineHeight, which the leading grew.
    public double textY;
    // A truly empty <td> collapses in a browser and shows NO text —
    // unlike an &nbsp; cell, whose text is U+00A0 and survives this
    // test. Emitting the empty run anyway reads back as a spurious
    // text fragment, shifting every fragment
    // index after it. Lifted render only; the legacy dialects round
    // trip their leading empty segment through it.
    public bool cellHasInk;
}
}
