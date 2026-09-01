using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary>
    /// Flow-layout helper that tracks a Y cursor across sequential paragraphs on a
    /// page. When content overflows the page's content region, a new overflow page
    /// is queued (via the shared overflowPages list that Document.ApplyPageContent
    /// drains after the main loop) and the cursor is reset to the top margin. This
    /// allows TextFragment / HtmlFragment / Heading / Table paragraphs to flow down
    /// the page instead of stacking at the same Y coordinate.
    /// </summary>
    private sealed partial class FlowLayout
    {
        private readonly List<(byte[] content, double width, double height)> _overflowPages;
        private readonly double _marginLeft;
        private readonly double _marginRight;
        // Top/bottom margins of the CURRENT page: a per-page OnBeforePageGenerate
        // handler may re-margin a generated page before its content is laid.
        private double _marginTop;
        private double _marginBottom;

        /// <summary>Called when the flow breaks to a new overflow slot (its index),
        /// before any content is laid on it; returns the slot's top and bottom
        /// margins (OnBeforePageGenerate fires on the new page first,
        /// so a handler that sets <c>page.PageInfo.Margin.Top</c> moves that page's
        /// content top).</summary>
        internal Func<int, (double top, double bottom)>? OnPageBreak;
        private readonly Dictionary<int, (double top, double bottom)> _slotMargins = new();

        private readonly double _startPageHeight;
        private readonly Page _startPage;
        private double _curY;

        // Multi-column layout state. When _colLefts is non-null the flow lays
        // text out across N columns: it fills column 0 from the band top down to
        // marginBottom, then column 1, ... then column N-1, and only then starts
        // a fresh page (resetting to column 0). Single-column flow leaves these
        // null so every CurLeft/CurWidth/FlowToNextRegion call degenerates to the
        // original full-width, one-region-per-page behaviour (byte-for-byte).
        private double[]? _colLefts;
        private double[]? _colWidths;
        private int _curCol;
        private double _colBandTop;     // Y the next column resets to (top of the band)
        private double _colDeepestY;    // lowest Y any column reached, for resume-after-box

        // Lowest Y the body reached on each slot. FinaliseFootnotes anchors a page's
        // footnote band at the body bottom (not the top margin), so a footnote whose
        // body fills the page spills to a continuation page instead of overlapping.
        private readonly Dictionary<int, double> _slotBottomY = new();

        // When we overflow the start page, we accumulate content blocks for the
        // *current* overflow page here. On the next overflow we flush the buffer
        // into the overflow queue (one queue entry = one new page) and start a
        // fresh buffer. Without this, each WriteTextFragment chunk would become
        // its own tiny page with just a line or two.
        private List<byte[]>? _overflowBuffer;

        // Slot index of the currently-active write target. -1 means _startPage;
        // 0+ means the i-th overflow page that the current _overflowBuffer will
        // flush into. Tracking this lets link annotations and paragraph-position
        // records survive into pages that don't exist yet (overflow buffers are
        // flushed in the outer loop after this layout finishes).
        private int _currentSlot = -1;

        // Where each rendered paragraph started, so a LocalHyperlink with
        // Target = another paragraph (e.g. LocalHyperlink(head)) can resolve to
        // the right slot + y in the saved document. Resolved against the
        // slot→Page map after the outer overflow drain.
        private readonly Dictionary<BaseParagraph, (int slot, double yTop)> _paragraphPositions = new();

        // Deferred link annotations -- target slot, rect, and hyperlink. Resolved
        // and emitted by FinaliseAnnotations once overflow slots map to real
        // Pages.
        private readonly List<(int slot, Rectangle rect, Hyperlink hyperlink)> _pendingLinks = new();

        // Annotations added through Paragraphs.Add — placed as blocks by
        // PlaceAnnotationBlock, bound to their final page (geometry translated
        // into the reserved rect) by FinaliseAnnotations.
        private readonly List<(int slot, Annotations.Annotation annot, Rectangle rect)> _pendingFlowAnnots = new();

        // A note's reference marker is a Link to its band: the marker's own box on
        // the body line, going to (content right edge, the note's first band line
        // top) on the band's page. Resolved once the bands are laid out.
        private readonly List<(int slot, Rectangle rect, Note note)> _pendingNoteLinks = new();
        private readonly Dictionary<Note, (int slot, double top)> _noteBandTop = new();

        // Deferred AcroForm checkboxes emitted from in-page HTML (<input type=checkbox>).
        // Bound to their final page — and registered on the document Form — by
        // FinaliseFormFields once overflow slots map to real Pages, so a checkbox that
        // flows onto a later page reports that page (not the start page).
        private readonly List<(int slot, Rectangle rect, bool checkedState)> _pendingFormFields = new();

        // Deferred renders for fragments using embedded/CID fonts. The paginator
        // does its line-break maths in Standard-14 metric space (approximate
        // but enough for "does this chunk fit"), then queues the per-page
        // chunk text + start coordinates + the original fragment's TextState.
        // After Pages.Add() has populated overflow slots, FinaliseEmbeddedRenders
        // builds a TextBuilder per target page and re-runs each chunk through
        // it -- the TextBuilder path handles TrueType/CIDFont registration in
        // the page's /Resources and emits the right glyph encoding.
        private readonly List<(int slot, double x, double y, string text,
            Text.TextState textState, double fontSize, double? baseline)> _pendingEmbeddedRenders = new();

        // Running baseline of the last body line emitted on the current region,
        // used to give the full-size / explicit line-spacing modes the
        // "leading above the line" rule: the next line's baseline
        // sits one of *its own* line heights below the previous baseline, so a
        // size change between adjacent paragraphs spaces by the lower
        // paragraph's metrics (not the upper one's). Null at the top of every
        // region/page so the first line there drops by its font size — the
        // standard first-line placement. Footnotes bypass this (they queue
        // an explicit baseline of their own).
        private double? _lastBodyBaseline;

        // Once a full-justified fragment has queued its per-token deferred
        // renders, every later fragment in this flow must defer too: deferred
        // content is appended to the page AFTER the immediate writes, so an
        // immediate write following a justified paragraph would land BEFORE it
        // in the content stream and shuffle the absorber's fragment order
        // (which justified-output tests index into). Stays false in flows
        // without full-justified text, keeping their output byte-identical.
        private bool _forceDeferredWrites;

        // Every footnote / endnote of the flow, in reading order, with the slot
        // its reference marker landed on. FinaliseNoteBands lays each page's
        // notes into a band at the page foot (see "Note bands" below).
        private readonly List<NoteEntry> _notes = new();

        // Where the last note mark of a note was drawn: (slot, text top of its
        // line) — the band planner's cap reads it.
        private readonly Dictionary<Note, (int slot, double top)> _noteMarkLine = new();

        // Pitch (size + leading) of the last text line any writer laid: the
        // body pitch a note mark on a text-less line still answers with.
        private double _lastTextLinePitch = 10;

        /// <summary>True for the throwaway flow the dissolved-box planner lays
        /// into: it draws on a detached page and renders nothing that outlives it.</summary>
        internal bool IsDryRun { get; init; }

        // Sequential footnote number handed to notes without a custom Text label.
        private int _footnoteAutoNumber;

        // Separator rules and other raw graphics queued per slot, appended to the
        // materialised page's content by FinaliseRules.
        private readonly List<(int slot, byte[] ops)> _pendingRules = new();

        // Line-break notification log, keyed by slot, accumulated only when
        // notification logging was enabled on the document. Distributed to the
        // materialised Pages by FinaliseNotifications.
        private readonly bool _logNotifications;
        private readonly Dictionary<int, System.Text.StringBuilder> _notificationsBySlot = new();

        public FlowLayout(Page startPage, List<(byte[] content, double width, double height)> overflowPages,
            double marginLeft, double marginRight, double marginTop, double marginBottom, double startY,
            bool logNotifications = false)
        {
            _startPage = startPage;
            _overflowPages = overflowPages;
            _marginLeft = marginLeft;
            _marginRight = marginRight;
            _marginTop = marginTop;
            _marginBottom = marginBottom;
            // The MEDIA frame's height (see Page.LayoutFrameHeight): a /Rotate
            // page's paragraphs seat against the media edges and paint upright in
            // them; Page.Height answers the rotated DISPLAY frame, which put the
            // whole layout past the media edge, where it clipped away.
            _startPageHeight = startPage.LayoutFrameHeight;
            _curY = startY;
            _logNotifications = logNotifications;
        }

        /// <summary>Record a finished line in the notification log for the given slot.</summary>
        private void LogLine(int slot, string content, double x, double y, char reason)
        {
            var reasonText = reason switch
            {
                'N' => "new line marker detected",
                'M' => "line reached right margin",
                _ => "end of the line",
            };
            if (!_notificationsBySlot.TryGetValue(slot, out var sb))
                _notificationsBySlot[slot] = sb = new System.Text.StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("The line '").Append(content).Append("' was finished at {X=")
              .Append(x.ToString("F1", ci)).Append(", Y=").Append(y.ToString("F1", ci))
              .Append("} because ").Append(reasonText).Append(".\n");
        }

        // Form fields placed through Paragraphs: the block each reserved, bound to
        // the page its slot becomes by FinaliseFormFields.
        private readonly List<(int slot, Forms.Field field, Rectangle rect)> _pendingFieldBlocks = new();

        /// <summary>Reserve a square box at the flow cursor for an AcroForm checkbox and
        /// queue it (with the current overflow slot) for page binding once the overflow
        /// slots materialise. Modelled on <see cref="PlaceAnnotationBlock"/>.</summary>
        public void QueueCheckbox(double size, double leftIndent, bool checkedState)
        {
            EnsureRoom(size);
            var x = CurLeft + leftIndent;
            _pendingFormFields.Add((_currentSlot,
                new Rectangle(x, _curY - size, x + size, _curY), checkedState));
            _curY -= size + 2;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>Record where a paragraph started rendering. Looked up later
        /// when another paragraph's LocalHyperlink targets this one.</summary>
        public void RecordPosition(BaseParagraph p)
        {
            if (p is null) return;
            _paragraphPositions[p] = (_currentSlot, _curY);
            _paraOrdinalBySlot.TryGetValue(_currentSlot, out var ordinal);
            _currentParaOrdinal = ordinal + 1;
            _paraOrdinalBySlot[_currentSlot] = _currentParaOrdinal;
        }

        // A note's default reference mark is the ORDINAL of its paragraph on the
        // page the paragraph starts on (1-based), not a running note counter:
        // notes on the 4th and 6th paragraphs of page 1 read "4" and "6", the
        // note on the 12th paragraph of page 2 reads "12".
        private readonly Dictionary<int, int> _paraOrdinalBySlot = new();
        private int _currentParaOrdinal;

        /// <summary>Queue a TextFragment.FootNote for page-bottom rendering on
        /// the current slot. Footnote content is laid out by FinaliseFootnotes
        /// after the main paragraph loop completes (when total content height
        /// is known and the page bottom band's size is fixed).</summary>
        public void QueueMarkedFootnote(Note note, string? marker = null, double parentSize = 10)
        {
            if (note is null) return;
            var (slot, top) = _noteMarkLine.TryGetValue(note, out var ml) ? ml : (_currentSlot, _curY);
            _notes.Add(new NoteEntry
            {
                Note = note, Marker = marker ?? NextFootnoteMarker(note), ParentSize = parentSize,
                Slot = slot, MarkerTop = top, MarkerPitch = _lastTextLinePitch,
            });
        }

        /// <summary>Queue a dissolved FloatingBox child's FootNote: the note bands on
        /// the page its mark landed on; <paramref name="childIndex"/> names the box
        /// child that carried the mark (the planner closes a page after it when the
        /// band cannot take the whole note).</summary>
        /// <summary>Queue a footnote for the band of the page its mark landed on.</summary>
        public void QueueFootnote(Note note, string marker, double parentSize, int childIndex)
        {
            if (note is null) return;
            var (slot, top) = _noteMarkLine.TryGetValue(note, out var ml) ? ml : (_currentSlot, _curY);
            _notes.Add(new NoteEntry
            {
                Note = note, Marker = marker, ParentSize = parentSize,
                Slot = slot, MarkerTop = top, MarkerPitch = _lastTextLinePitch, ChildIndex = childIndex,
            });
        }

        /// <summary>Queue a TextFragment.EndNote for the band at the bottom of the
        /// flow's last page.</summary>
        public void QueueEndNote(Note note, string marker, double parentSize)
        {
            if (note is null) return;
            _notes.Add(new NoteEntry
            {
                Note = note, Marker = marker, ParentSize = parentSize, Slot = EndNoteSlot,
                MarkerTop = _curY, MarkerPitch = _lastTextLinePitch,
            });
        }

        /// <summary>Queue the Link annotation a note marker carries: the marker's
        /// box (<paramref name="lineTop"/> down by the marker size, the marker's
        /// advance wide) on the current slot. Coordinates round to 1/100 pt so the
        /// saved /Rect reads back as the same values.</summary>
        private void QueueNoteLink(Note note, double x, double lineTop, double markerWidth, double markerSize)
        {
            static double R(double v) => Math.Round(v, 2);
            _pendingNoteLinks.Add((_currentSlot,
                new Rectangle(R(x), R(lineTop - markerSize), R(x + markerWidth), R(lineTop)), note));
        }

        /// <summary>Record where a note's band starts (its first band line's top on
        /// <paramref name="slot"/>) — the target of the marker's Link.</summary>
        private void RecordNoteBandTop(Note note, int slot, double top)
        {
            if (!_noteBandTop.ContainsKey(note)) _noteBandTop[note] = (slot, top);
        }

        // The superscript reference marker renders at half the parent run's size
        // (a 10pt body takes a 5pt marker, 20pt -> 10pt).
        internal const double MarkerSizeRatio = 0.5;

        /// <summary>Positive per-em descent fraction for a Standard-14 face
        /// (Helvetica: 0.207). The marker baseline maths is expressed in the
        /// row model: rowBottom = baseline - descent(size).</summary>
        private static double DescentNorm(string fontName)
        {
            var d = Text.Standard14Fonts.GetDescent(fontName);
            return d < 0 ? -d / 1000.0 : d / 1000.0;
        }

        /// <summary>The tail of <paramref name="text"/> from the start of wrapped line
        /// <paramref name="idx"/> (lines are matched in sequence; null when a line
        /// cannot be located).</summary>
        private static string? RemainderFrom(string text, List<string> lines, int idx)
        {
            var pos = 0;
            for (var k = 0; k < idx; k++)
            {
                var at = lines[k].Length == 0 ? pos : text.IndexOf(lines[k], pos, StringComparison.Ordinal);
                if (at < 0) return null;
                pos = at + lines[k].Length;
            }
            var start = lines[idx].Length == 0 ? pos : text.IndexOf(lines[idx], pos, StringComparison.Ordinal);
            return start < 0 ? null : text.Substring(start);
        }

        /// <summary>Whether the text holds a character outside Latin-1.</summary>
        private static bool HasNonLatin1(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text) if (c > 0xFF) return true;
            return false;
        }

        /// <summary>Rounding guard on the wrap test: a word stays on a line only
        /// when it reaches no further than this past the line's width (the
        /// reference wraps a word that overshoots the margin by a third of a
        /// point, so this is noise-sized, not a tolerance).</summary>
        private const double WrapEpsilon = 0.01;

        /// <summary>Wrap a run list into positioned lines at the given width.
        /// Superscript runs measure at their reduced size; a run's TabAfter
        /// jumps the cursor to a fixed offset from the line start (heading
        /// number tab); the OwnerLeft of the run that starts each line gives
        /// that line its left margin (a wrapped heading continuation returns
        /// to the margin of the heading whose text starts the line).</summary>
        internal static List<(double left, List<(double x, string text, StyledRun run)> cells)>
            LayoutStyledLines(List<StyledRun> runs, double width)
        {
            var lines = new List<(double, List<(double, string, StyledRun)>)>();
            var cells = new List<(double, string, StyledRun)>();
            double lineLeft = runs.Count > 0 ? runs[0].OwnerLeft : 0;
            double x = 0;
            foreach (var r in runs)
            {
                var size = StyledRunSize(r);
                if (r.HardBreak)
                {
                    cells.Add((x, string.Empty, r));
                    lines.Add((lineLeft, cells));
                    cells = new List<(double, string, StyledRun)>();
                    lineLeft = r.OwnerLeft;
                    x = 0;
                    continue;
                }
                if (r.ImageData is not null)
                {
                    // A picture is one unbreakable cell of its own width.
                    if (cells.Count > 0 && x + r.ImageW > width - lineLeft + WrapEpsilon)
                    {
                        if (cells[^1].Item2 == " ") cells.RemoveAt(cells.Count - 1);
                        lines.Add((lineLeft, cells));
                        cells = new List<(double, string, StyledRun)>();
                        lineLeft = r.OwnerLeft;
                        x = 0;
                    }
                    cells.Add((x, string.Empty, r));
                    x += r.ImageW;
                    continue;
                }
                var t = r.Text ?? string.Empty;
                var i = 0;
                var firstWord = true;
                while (i < t.Length)
                {
                    string tok;
                    if (t[i] == ' ') { tok = " "; i++; }
                    else
                    {
                        var j = i;
                        while (j < t.Length && t[j] != ' ') j++;
                        tok = t.Substring(i, j - i);
                        i = j;
                    }
                    var w = MeasureStyled(tok, r, size);
                    if (tok != " " && cells.Count > 0 && x + w > width - lineLeft + WrapEpsilon)
                    {
                        if (r.InlineStart && firstWord)
                        {
                            // An inline-joined fragment's first word cannot leave
                            // the line it joins: the line's remainder is the
                            // fragment's first-line width, and a word wider than
                            // that breaks by characters, as any over-wide word
                            // does at a line start ("See H" / "opkins v. Andaya").
                            var fit = 0;
                            while (fit < tok.Length
                                   && x + MeasureStyled(tok.Substring(0, fit + 1), r, size) <= width - lineLeft + WrapEpsilon)
                                fit++;
                            if (fit > 0)
                            {
                                cells.Add((x, tok.Substring(0, fit), r));
                                tok = tok.Substring(fit);
                                w = MeasureStyled(tok, r, size);
                            }
                        }
                        if (cells[^1].Item2 == " ") cells.RemoveAt(cells.Count - 1);
                        lines.Add((lineLeft, cells));
                        cells = new List<(double, string, StyledRun)>();
                        lineLeft = r.OwnerLeft;
                        x = 0;
                    }
                    cells.Add((x, tok, r));
                    x += w;
                    if (tok != " ") firstWord = false;
                }
                if (r.TabAfter > 0 && x < r.TabAfter) x = r.TabAfter;
            }
            if (cells.Count > 0 || lines.Count == 0) lines.Add((lineLeft, cells));
            return lines;
        }

        /// <summary>Height of a line's highlight box in em: the glyph box plus a
        /// tenth above it (the box a TextState.BackgroundColor paints behind each
        /// line; consecutive lines' boxes merge into one rectangle).</summary>
        private const double HighlightBoxEm = 1.1;

        /// <summary>What a paragraph's box adds per line over the line pitch, in em
        /// of the line's text size (a 10 pt line's box is 11.6 pt tall).</summary>
        private const double HighlightBoxExtraEm = 0.16;

        // ---- Note bands ----
        // Every footnote and endnote renders in a band at the foot of a page.
        // The band's lines are laid at the band width: the footer's inner width
        // when the page carries a footer, else the page content width. With a
        // footer the band stands on the footer's text top; without one it hangs
        // from the bottom margin down (a band taller than the margin stands on
        // the physical page bottom). A 1 pt rule sits one footer-top-margin
        // above the band on every page that carries body text. In a line, a
        // note mark hangs from the line's text top at half its parent size; the
        // text's box bottom is one pitch (size + leading) below the text top.
        // A dissolved FloatingBox plans its pages' bands up front
        // (PlanDissolvedBands): the body stops above the band, and a note the
        // band cannot take whole spills its tail to the next page's band.

        private const int EndNoteSlot = int.MaxValue;

        // Diagnostic trace of the band engine (ASPOSE_PDF_NOTE_TRACE=1 → stderr).
        private static readonly bool NoteTrace =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPOSE_PDF_NOTE_TRACE"));
        private static void Trace(string msg)
        {
            if (NoteTrace) Console.Error.WriteLine("[notes] " + msg);
        }

        private sealed class NoteEntry
        {
            public Note Note = null!;
            public string Marker = "";
            public double ParentSize;
            public int Slot;             // the mark's slot; EndNoteSlot for an end note
            public double MarkerTop;     // text top of the line carrying the mark
            public double MarkerPitch;   // pitch of the last text line before it
            public int ChildIndex = -1;  // dissolved-box child that carried the mark
            public List<BandLine>? Lines;
        }

        internal sealed class BandLine
        {
            public List<(double x, string text, StyledRun run, double size)> Cells = new();
            public double Pitch;          // line advance: text height + leading, or the mark alone
            public double TextHeight;     // the line's text size (0 on a mark-only line)
            public bool HasText;
            public double NaturalWidth;   // ink extent from the line's left
            public Note Note = null!;
            public bool NoteFirst;        // the note's first line (carries the mark; the link target)
            public bool ParaFirst;
            public bool LastOfParagraph;
            public HorizontalAlignment Align;
            public double Left;           // paragraph left margin inside the band
            public bool FullWidthBox;     // a caller-built paragraph claims the band width
            public double MarginTop, MarginBottom, ParaMarginTop;
            public bool PaintFromRule;    // a margined paragraph paints from the rule down
            // A Table note paragraph: the line IS the table, drawn from the band
            // cursor at TableLeft and as tall as the grid it renders.
            public Table? TableBlock;
            public double TableLeft;
        }

        /// <summary>The per-page band plan a dissolved FloatingBox lays under: the
        /// lines each page (relative to the box's first page) carries and the band's
        /// top, plus the child after which a page closes when its band spilled.</summary>
        internal sealed class DissolvedBandPlan
        {
            public Dictionary<int, (List<BandLine> lines, double top)> Slots = new();
            public (int rel, int child)? CloseAfter;
        }

        // Bottom limit (y-up) of body content per slot: the top of that page's
        // planned band plus the footer's top margin.
        private readonly Dictionary<int, double> _slotBottomLimit = new();
        private readonly Dictionary<int, List<BandLine>> _bandPlan = new();
        private readonly Dictionary<int, double> _bandPlanTop = new();
        private (int slot, int child)? _closeAfter;

        /// <summary>Lift from a line's box bottom to the baseline of a run drawn
        /// in <paramref name="st"/>: the Standard-14 descent for a metric-only face
        /// (the deferred writer seats those at the position it is given), nothing
        /// for an embedded face (the writer adds that face's own descent).</summary>
        private static double Std14Seat(Text.TextState st, double size)
        {
            if ((st.FontData?.TtfData ?? st.Font?.SourceFontData?.TtfData) is { Length: > 0 }) return 0;
            return DescentNorm(Text.TextBuilder.MapToStandard14Public(st)) * size;
        }

        /// <summary>A throwaway flow with this flow's geometry and cursor, drawing on
        /// <paramref name="dryPage"/>: the dissolved-box planner lays the box into it
        /// to see where marks and page breaks land under a candidate band plan.</summary>
        internal FlowLayout CreateDryRun(Page dryPage)
        {
            var dry = new FlowLayout(dryPage, new List<(byte[] content, double width, double height)>(),
                _marginLeft, _marginRight, _marginTop, _marginBottom, _curY) { IsDryRun = true };
            dry._footnoteAutoNumber = _footnoteAutoNumber;
            dry._currentParaOrdinal = _currentParaOrdinal;
            dry._lastTextLinePitch = _lastTextLinePitch;
            return dry;
        }

        /// <summary>True when the plan closes the current page after the box child
        /// <paramref name="child"/> (its note's band could not take the whole note).</summary>
        internal bool ShouldCloseAfterChild(int child)
            => _closeAfter is { } c && c.child == child && c.slot == _currentSlot;

        /// <summary>Whether a slot carries body content: the start page always does
        /// (whatever the caller placed on it — a table, an image — lives there even
        /// when no flow writer recorded a line); an overflow page only when a
        /// writer reached it (a page the band alone created has none).</summary>
        private bool SlotHasBody(int slot) => slot < 0 || _slotBottomY.ContainsKey(slot);

        private readonly List<(int slot, Table table, double left, double top)> _pendingBandTables = new();

        // The document's page count once every overflow page exists; links queued
        // during layout resolve against it (a target beyond it gets no destination).
        private int _finalPageCount = int.MaxValue;

        public double CurrentY => _curY;

        /// <summary>Overflow slot the flow ended on; −1 = still on the original page.</summary>
        public int CurrentSlot => _currentSlot;
        public Page CurrentPage => _startPage;

        public void AdvanceY(double delta)
        {
            _curY -= delta;
            // An explicit cursor jump (a paragraph margin, a table's consumed height,
            // an inline-image line, a rule…) breaks the run of consecutive body lines
            // that _lastBodyBaseline chains together. Clearing it forces the next
            // deferred embedded-font fragment to anchor its first baseline to the new
            // _curY instead of chaining off a stale baseline from before the jump —
            // otherwise a TextFragment placed after a Table renders at the pre-table
            // baseline and overlaps it (e.g. a generator invoice's sentence colliding
            // with the info table above it).
            _lastBodyBaseline = null;
        }

        /// <summary>Advance the cursor by one EMPTY line box, page-breaking first when
        /// the box does not fit. A run of <c>&lt;br&gt;</c>s that crosses the page
        /// bottom keeps its remainder at the top of the next page, exactly as a run
        /// of text lines would — a plain <see cref="AdvanceY"/> would drive the
        /// cursor past the margin and the overshoot would be lost at the break.</summary>
        public void AdvanceLineBox(double height)
        {
            if (height <= 0) return;
            EnsureRoom(height);
            _curY -= height;
            _lastBodyBaseline = null;
        }

        /// <summary>Bottom content margin (points) — the Y below which the flow page-breaks.</summary>
        public double BottomMargin => _marginBottom;
        /// <summary>The current page's top margin (content top = page height − this).</summary>
        public double ContentTopMargin => _marginTop;

        /// <summary>Top of the content area on the current page (points).</summary>
        public double ContentTop => _startPageHeight - _marginTop;

        /// <summary>Inject a pre-built content stream (e.g. a Table slice rendered by
        /// BuildMultiPage at <see cref="CurrentY"/>) at the flow's CURRENT page position.
        /// While still on the start page this appends to it directly; once the flow has
        /// page-broken into an overflow buffer it appends to that buffer instead, so the
        /// content lands on the page the cursor is actually on (not the original start page).
        /// The caller advances <see cref="CurrentY"/> afterwards.</summary>
        public void InjectContentAtCursor(byte[] content)
        {
            if (content is null || content.Length == 0) return;
            if (_overflowBuffer is null) _startPage.AddContentStream(content);
            else _overflowBuffer.Add(content);
        }

        /// <summary>Queue a Link annotation over <paramref name="rect"/> on the page
        /// the cursor is currently on (a paragraph-level Hyperlink on a block such
        /// as a Graph). Resolved against the real Page sequence by
        /// <see cref="FinaliseAnnotations"/>.</summary>
        public void QueueLink(Rectangle rect, Hyperlink hyperlink)
        {
            _pendingLinks.Add((_currentSlot, rect, hyperlink));
        }


        // ---- CSS background boxes ----
        // A background box is painted BEFORE the content it encloses, so it is
        // queued here while the blocks flow and prepended to each page's content
        // once the paragraph that declared them has finished.
        private readonly List<(int Slot, Rectangle Rect, Color Color)> _pendingBackFills = new();

        /// <summary>True once the flow has page-broken off the start page (subsequent
        /// content lives in an overflow buffer, not on a live Page yet).</summary>
        public bool HasOverflowed => _overflowBuffer is not null;

        /// <summary>Extra left indent (points) added to the write region for the next
        /// fragment — used for HTML block / list-item indentation. Reset by the caller
        /// between blocks.</summary>
        public double LeftIndent { get; set; }

        /// <summary>CSS <c>orphans</c>: the least number of a block's own lines that may
        /// be left at the foot of a page. A block with fewer lines than this still fits
        /// wherever it fits; one with more moves whole rather than leaving a stub behind,
        /// which is why a two-line paragraph never splits at the browser default of 2.
        /// 0 or 1 = split wherever the page runs out. Set by the caller for the flows
        /// that lay out authored CSS.</summary>
        public int MinLinesPerPage { get; set; }

        /// <summary>Left edge of the current write region in page points.</summary>
        public double CurrentLeft => CurLeft;

        /// <summary>Jump the cursor to an absolute Y (a positioned paragraph's
        /// bottom edge). Like <see cref="AdvanceY"/> this breaks the chained body
        /// baseline so the next line seats from the new cursor.</summary>
        public void MoveCursorTo(double y)
        {
            _curY = y;
            _lastBodyBaseline = null;
            if (_colLefts is not null) _colDeepestY = Math.Min(_colDeepestY, _curY);
        }

        /// <summary>Begin multi-column layout. <paramref name="lefts"/> and
        /// <paramref name="widths"/> describe each column's absolute left edge and
        /// usable width. Text written after this fills column 0 top-to-bottom, then
        /// column 1, etc.; only after the last column does the flow page-break.
        /// Columns start at the current Y cursor (the band top).</summary>
        public void BeginColumns(double[] lefts, double[] widths)
        {
            _colLefts = lefts;
            _colWidths = widths;
            _curCol = 0;
            _colBandTop = _curY;
            _colDeepestY = _curY;
        }

        /// <summary>End multi-column layout and resume full-width flow just below
        /// the deepest point any column reached, so content after the box renders
        /// under it rather than overlapping.</summary>
        public void EndColumns()
        {
            if (_colLefts is null) return;
            _colLefts = null;
            _colWidths = null;
            _curCol = 0;
            _curY = _colDeepestY;
        }

        /// <summary>Force the next write into the next column (honours
        /// <see cref="BaseParagraph.IsFirstParagraphInColumn"/>). Past the last
        /// column this starts a fresh page of columns.</summary>
        public void ForceNextColumn()
        {
            if (_colLefts is null) return;
            FlowToNextRegion();
        }

        /// <summary>Advance to the next write region: the next column if one
        /// remains in column mode, otherwise a fresh page. Outside column mode
        /// this is exactly <see cref="StartNewPage"/>.</summary>
        /// <summary>Note that body content on the current slot reached at least as
        /// far down as <paramref name="y"/>. FinaliseFootnotes uses the minimum (the
        /// deepest point) as the top of that page's footnote band.</summary>
        private void RecordSlotBottom(double y)
        {
            Trace($"body slot={_currentSlot} y={y:F1} dry={IsDryRun}");
            _slotBottomY[_currentSlot] = _slotBottomY.TryGetValue(_currentSlot, out var prev)
                ? Math.Min(prev, y) : y;
        }

        /// <summary>Record body on an overflow slot the flow's writers never
        /// reached — a page a multi-page table filled on its own. The band of
        /// that page then hangs under the table's last row instead of treating
        /// the page as a note continuation.</summary>
        public void RecordBodyOnSlot(int slot, double bottomY)
        {
            Trace($"body(table) slot={slot} y={bottomY:F1} dry={IsDryRun}");
            _slotBottomY[slot] = _slotBottomY.TryGetValue(slot, out var prev) ? Math.Min(prev, bottomY) : bottomY;
        }

        /// <summary>
        /// Force the next paragraph to start on a fresh overflow page. Used after
        /// a Table — we don't currently know the Table's final Y, so we treat it
        /// as consuming the rest of the current page.
        /// </summary>
        public void ResetToTopOfNextPage() => _curY = _marginBottom - 1; // guarantees next EnsureRoom triggers new page

        /// <summary>Eagerly start a fresh overflow page. Used when the next
        /// paragraph must render on a new page regardless of whether the
        /// downstream render path consults the Y cursor (e.g. Heading.Build
        /// draws at the supplied Y without an internal pagination check, so
        /// just resetting the cursor isn't enough). Commits the current page even
        /// when empty so consecutive forced breaks (e.g. several IsInNewPage
        /// FloatingBoxes that render nothing) each produce their own blank page
        /// instead of collapsing onto one.</summary>
        public void ForceNewPage() => StartNewPage(flushEmpty: true);

        /// <summary>Start a fresh page for the next block unless the flow is already
        /// at the top of an empty one (a block that does not fit below the cursor).</summary>
        public void BreakPageIfContent() => StartNewPage();

        /// <summary>Default body font size of the XML-generator model (a BindXml
        /// fragment without a FontSize).</summary>
        internal const double XmlDefaultFontSize = 10;

        /// <summary>Default #$TAB stops sit at multiples of this many space-widths
        /// of the active font/size (8 × 0.278 em at every
        /// size).</summary>
        private const int XmlDefaultTabStopSpaces = 8;

        /// <summary>Rounding slack allowed before a word is judged not to fit the
        /// content box — the same half-point the plain styled-line writer uses.</summary>
        private const double WrapWidthSlackPt = 0.5;

        /// <summary>First baseline of a FullSize-spaced paragraph: its line box is the
        /// FULL line height (not the bare font size) hanging from the band top, and the
        /// baseline sits the font's own descent above the box bottom — which is exactly
        /// what the writer's descent lift does to the layout baseline returned here.
        /// Measured: a 10 pt Arial Unicode MS block under a 770 band
        /// top puts its first glyphs on 759.31, its hhea ascender being 10.688 pt at that
        /// size. Null when the mode or the metrics don't apply, and the caller keeps the
        /// ordinary seat.</summary>
        private double? FullSizeFirstBaseline(byte[]? ttf, double lineHeight)
        {
            if (ttf is not { Length: > 12 }) return null;
            return Text.FontRepository.ReadTtfLineAscentEm(ttf) > 0 ? _curY - lineHeight : null;
        }

        /// <summary>Ascent of the generic font descriptor, in em — the last-resort
        /// vertical extent when a face reports no metrics of its own.</summary>
        private const double GenericAscentEm = 0.900;

        /// <summary>…and its depth below the baseline, in em.</summary>
        private const double GenericDescentEm = 0.210;

        /// <summary>Bottom limit (y-up) for body content on the current slot:
        /// the page's bottom margin, or the top of a reserved footnote band
        /// when one occupies this page.</summary>
        private double EffectiveBottom => Math.Max(_marginBottom,
            _slotBottomLimit.TryGetValue(_currentSlot, out var l) ? l : double.MinValue);

        /// <summary>Break to the next region unless <paramref name="height"/> of the
        /// current one is still free — a box that must not be split announces its own
        /// height before it opens.</summary>
        public void EnsureRoomFor(double height) => EnsureRoom(height);

        private void EnsureRoom(double lineHeight)
        {
            if (_curY - lineHeight < EffectiveBottom)
                FlowToNextRegion();
        }

        private static byte[] ConcatBlocks(List<byte[]> blocks)
        {
            var total = 0;
            foreach (var b in blocks) total += b.Length;
            var result = new byte[total];
            var offset = 0;
            foreach (var b in blocks)
            {
                Buffer.BlockCopy(b, 0, result, offset, b.Length);
                offset += b.Length;
            }
            return result;
        }

        // ---- Generator inline line model ----
        // A paragraph built from styled runs — the segments of one fragment, an
        // IsInLineParagraph chain of fragments and images, note reference marks —
        // lays out on lines whose TOP is the cursor:
        //   • every run of fragment F seats on F's own box: box bottom = line top −
        //     F's pitch on this line (its largest segment size + leading), baseline
        //     = box bottom + the face descent (12 pt and 20 pt segments of one
        //     fragment share the 20 pt box; a 12 / 20 / 8 pt inline chain seats
        //     each member on its own box from the same line top);
        //   • an image run stands on the line top (its box hangs below it) and only
        //     advances the pen;
        //   • the line advances by the pitch of the LAST text fragment on it (the
        //     12 / 20 / 8 chain continues 8 below), or by the tallest image when the
        //     line holds no text;
        //   • Right / Center alignment shifts the whole line by its slack;
        //   • a note mark (half the parent size, in the default face) stands on a
        //     box whose bottom sits (pitch − mark size) above the parent box bottom.
        internal sealed class InlineRun
        {
            public string Text = string.Empty;
            public double Size;
            /// <summary>Font size plus the owning fragment's leading.</summary>
            public double Pitch;
            /// <summary>Index of the owning fragment in the chain.</summary>
            public int Group;
            public Text.TextState State = new();
            public Hyperlink? Link;
            public bool Underline;
            public bool Strike;
            public bool NoteMarker;
            public Note? Note;
            public byte[]? ImageData;
            public double ImageW, ImageH;
        }

        private readonly List<(int slot, byte[] data, Rectangle rect)> _pendingImages = new();

        /// <summary>Reserved line pitch of a multi-line deferred chunk, keyed by its
        /// index in the deferred render queue.</summary>
        private readonly Dictionary<int, double> _pendingRenderPitch = new();
        // Renders clipped to a box (a margined note paragraph), by render index.
        private readonly Dictionary<int, Rectangle> _pendingRenderClip = new();

        /// <summary>Share of the face descent the decoration origin sits below the
        /// baseline — equivalently descent/10 above the line box bottom (Helvetica
        /// 10 pt: baseline − 1.863; Times: − 1.953; Courier 12: − 1.696; Arial
        /// 12: − 2.279).</summary>
        internal const double DecorationOriginDescentShare = 0.9;

        /// <summary>Rise of the strike-through rule above the decoration origin,
        /// in em (4.3 pt at 10 pt, 8.6 at 20, 3.01 at 7, 5.16 at 12).</summary>
        internal const double StrikeoutRiseEm = 0.43;

        /// <summary>Thickness of the underline and strike-through rules, in em
        /// (0.5 pt at 10 pt, 1 at 20, 0.35 at 7).</summary>
        internal const double DecorationThicknessEm = 0.05;

        /// <summary>Face descent of a run, in em: the hhea descender (per-mille
        /// truncated, as the Type0 descriptor writes it) for an embedded face, the
        /// AFM descent for a Standard-14 one.</summary>
        private static double RunDescentEm(Text.TextState st)
        {
            var ttf = st.FontData?.TtfData ?? st.Font?.SourceFontData?.TtfData;
            if (ttf is { Length: > 12 })
            {
                var d = Text.TextBuilder.HheaDescentPerMille(ttf);
                if (d != 0) return Math.Abs(d) / 1000.0;
            }
            return DescentNorm(Text.TextBuilder.MapToStandard14Public(st));
        }

        private static bool RunIsEmbedded(Text.TextState st) => HasFaceProgram(st);

        /// <summary>Whether a run will be written through a REAL face program. The
        /// deferred writer lifts such a run by that face's own descent, so the flow
        /// hands it the line box BOTTOM; a Standard-14 run is handed its baseline.
        /// A Standard-14 name resolves to a <see cref="Text.FontData"/> carrying the
        /// NAME and no program (FontRepository answers "Helvetica" with one, and the
        /// XML round trip writes the resolved name back), so testing the font OBJECT
        /// for null seated every round-tripped paragraph one descent too low.</summary>
        internal static bool HasFaceProgram(Text.TextState st) =>
            (st.FontData?.TtfData ?? st.Font?.SourceFontData?.TtfData) is { Length: > 0 };

        private static double InlineMeasure(string text, InlineRun r)
        {
            if (r.ImageData is not null) return r.ImageW;
            if (text.Length == 0) return 0;
            var st = r.NoteMarker ? new Text.TextState() : r.State;
            return MeasureStyled(text, new StyledRun { State = st }, r.Size);
        }

    }
}
