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
    private sealed class FlowLayout
    {
        private readonly List<(byte[] content, double width, double height)> _overflowPages;
        private readonly double _marginLeft;
        private readonly double _marginRight;
        private readonly double _marginTop;
        private readonly double _marginBottom;
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

        // Footnotes queued for page-bottom rendering. Each footnote belongs to
        // a specific page slot (the slot the parent TextFragment.FootNote
        // declaration landed on). FinaliseFootnotes lays out each page's
        // footnotes upward from marginBottom and queues the resulting text
        // chunks into _pendingEmbeddedRenders -- so the existing
        // FinaliseEmbeddedRenders pass dispatches them to the right Page with
        // the right font.
        private readonly List<(int slot, Note note, string marker, double parentSize)> _pendingFootnotes = new();

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
            _startPageHeight = startPage.Height;
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

        /// <summary>Place a block image at the flow cursor: drawn at the current
        /// region's left edge, w×h points, cursor advanced below it. Drawn only
        /// while the flow is still on the live start page (the flows that reach
        /// this don't place images after overflowing).</summary>
        public void PlaceImageBlock(byte[] data, double w, double h)
        {
            EnsureRoom(h);
            var x = CurLeft;
            if (_overflowBuffer is null)
                _startPage.AddImage(data, new Rectangle(x, _curY - h, x + w, _curY));
            _curY -= h;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>Reserve a w×h block at the cursor for an annotation added
        /// through Paragraphs.Add (such annotations lay out like block
        /// paragraphs: stacked at the left content edge with no
        /// inter-paragraph gap) and queue it for page binding once overflow
        /// slots materialise.</summary>
        public void PlaceAnnotationBlock(Annotations.Annotation annot, double w, double h)
        {
            if (h > 0) EnsureRoom(h);
            var x = CurLeft;
            _pendingFlowAnnots.Add((_currentSlot, annot, new Rectangle(x, _curY - h, x + w, _curY)));
            _curY -= h;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

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
        }

        /// <summary>Where a paragraph started rendering in this flow (slot −1 =
        /// the start page, else the flow-relative overflow slot). Used by the
        /// deferred TOC emit to print the page a heading FINALLY landed on.</summary>
        public bool TryGetParagraphPosition(BaseParagraph p, out (int slot, double yTop) pos)
            => _paragraphPositions.TryGetValue(p, out pos);

        /// <summary>One styled run inside a flow paragraph: a piece of text
        /// with its own font/size/colour/underline, an optional superscript
        /// flag (footnote reference marks and band labels), the owning
        /// paragraph's extra left margin (heading Margin.Left — applies to the
        /// lines this run starts), and an optional fixed tab stop the NEXT
        /// run jumps to (heading label → text gap).</summary>
        internal sealed class StyledRun
        {
            public string Text = string.Empty;
            public double Size = 10;
            public Text.TextState State = new();
            public bool Sup;
            public double OwnerLeft;
            public double TabAfter;
            public Hyperlink? Link;
        }

        /// <summary>Queue a TextFragment.FootNote for page-bottom rendering on
        /// the current slot. Footnote content is laid out by FinaliseFootnotes
        /// after the main paragraph loop completes (when total content height
        /// is known and the page bottom band's size is fixed).</summary>
        public void QueueMarkedFootnote(Note note, string? marker = null, double parentSize = 10)
        {
            if (note is null) return;
            _pendingFootnotes.Add((_currentSlot, note, marker ?? NextFootnoteMarker(note), parentSize));
        }

        /// <summary>Reference-marker label for a note: its custom Text when set,
        /// otherwise the next sequential footnote number.</summary>
        public string NextFootnoteMarker(Note note) =>
            note.Text ?? (++_footnoteAutoNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Draw a superscript footnote marker on the current slot at an
        /// externally laid-out position (e.g. after a table cell's text run).
        /// <paramref name="baseline"/> is the parent line's text baseline.</summary>
        public void EmitFootnoteMarkerAt(Note note, string marker, double x,
            double baseline, double parentSize)
        {
            if (marker.Length == 0) return;
            var markerSize = parentSize * MarkerSizeRatio;
            var markerState = new Text.TextState
            {
                ForegroundColor = note.TextState?.ForegroundColor,
            };
            _pendingEmbeddedRenders.Add((_currentSlot, x, 0, marker, markerState, markerSize,
                baseline + MarkerBaselineRise("Helvetica", parentSize, parentSize, markerSize)));
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

        /// <summary>Baseline raise of a footnote marker over its parent line's
        /// baseline, in the row model where the marker's row bottom sits
        /// (rowPitch - markerSize) above the parent row bottom and each baseline
        /// sits descent(size) above its row bottom.</summary>
        private static double MarkerBaselineRise(string fontName, double parentSize,
            double rowPitch, double markerSize)
        {
            var d = DescentNorm(fontName);
            return (rowPitch - markerSize) + d * markerSize - d * parentSize;
        }

        private static double MeasureStyled(string text, StyledRun r, double size)
        {
            var st = r.State;
            var fd = st.FontData ?? st.Font?.SourceFontData;
            var baseFont = Text.TextBuilder.MapToStandard14Public(st);
            return Text.TextPaginator.CreateMeasurer(baseFont, size, fd)(text);
        }

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
                var size = r.Sup ? r.Size * 0.583 : r.Size;
                var t = r.Text ?? string.Empty;
                var i = 0;
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
                    if (tok != " " && cells.Count > 0 && x + w > width - lineLeft + 0.5)
                    {
                        if (cells[^1].Item2 == " ") cells.RemoveAt(cells.Count - 1);
                        lines.Add((lineLeft, cells));
                        cells = new List<(double, string, StyledRun)>();
                        lineLeft = r.OwnerLeft;
                        x = 0;
                    }
                    cells.Add((x, tok, r));
                    x += w;
                }
                if (r.TabAfter > 0 && x < r.TabAfter) x = r.TabAfter;
            }
            if (cells.Count > 0 || lines.Count == 0) lines.Add((lineLeft, cells));
            return lines;
        }

        /// <summary>Write a styled paragraph (heading with label / decorated
        /// segments, an inline-joined fragment chain, footnote reference
        /// marks) into the flow at the cursor. Line pitch is the line's
        /// dominant base size + <paramref name="lineSpacing"/>; the first
        /// line at a region top drops 0.8×size + spacing below the band top
        /// (the FloatingBox flow rule); later lines chain
        /// baselines. Emission goes through the deferred embedded-render
        /// queue so real fonts, colours, underline and superscript baselines
        /// all apply.</summary>
        public void WriteStyledParagraph(List<StyledRun> runs, double lineSpacing)
        {
            var lines = LayoutStyledLines(runs, CurWidth);
            foreach (var (left, cells) in lines)
            {
                double maxBase = 0;
                foreach (var (_, _, r) in cells)
                    if (!r.Sup && r.Size > maxBase) maxBase = r.Size;
                if (maxBase <= 0) maxBase = runs.Count > 0 ? runs[0].Size : 10;
                var lh = maxBase + lineSpacing;
                EnsureRoom(lh);
                // Line box = [cursor, cursor − pitch]; the queued Y is the box
                // bottom (descender line — the deferred TextBuilder write lifts
                // it by the font descent, landing the baseline exactly on
                // the line grid). Superscript runs raise the box.
                var boxBottom = _curY - lh;
                foreach (var (xr, text, r) in cells)
                {
                    if (text.Length == 0) continue;
                    var size = r.Sup ? r.Size * 0.583 : r.Size;
                    var y = boxBottom + (r.Sup ? 0.33 * maxBase : 0);
                    _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + left + xr, _curY,
                        text, r.State, size, y));
                }
                // ONE Link annotation per hyperlinked run per line — consecutive
                // word/space cells of the same run coalesce into a single rect.
                Hyperlink? runLink = null;
                double linkX0 = 0, linkX1 = 0;
                void FlushRunLink()
                {
                    if (runLink is not null && linkX1 > linkX0)
                        _pendingLinks.Add((_currentSlot,
                            new Rectangle(linkX0, boxBottom, linkX1, boxBottom + lh), runLink));
                    runLink = null;
                }
                foreach (var (xr, text, r) in cells)
                {
                    if (r.Link is null || text.Length == 0) { FlushRunLink(); continue; }
                    var x0 = CurLeft + left + xr;
                    var x1 = x0 + MeasureStyled(text, r, r.Sup ? r.Size * 0.583 : r.Size);
                    if (!ReferenceEquals(runLink, r.Link)) { FlushRunLink(); runLink = r.Link; linkX0 = x0; }
                    linkX1 = x1;
                }
                FlushRunLink();
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY = boxBottom;
            }
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        // ---- Footnote band ----
        // The band sits under the body. When ALL its lines fit on their home
        // page it is anchored to the PHYSICAL page bottom (the last line's box
        // ends at page height — the band top pins at 792 − 14·12 = 624 for a
        // 14-line band); when it overflows, the home page takes floor() lines
        // from just below the body (body bottom + 4.8 pt, the rule
        // offset) and each continuation page fills a fixed grid whose top
        // leaves exactly one parent-pitch body line below the top margin. A
        // 1 pt separator rule is drawn above the band on the home page and on
        // the FIRST continuation page only (none on later
        // continuation pages).
        private double _fnPitch, _fnSize, _fnParentPitch;
        private int _fnHomeSlot = int.MinValue;
        private double _fnHomeBandTopFromTop;
        private int _fnCursorSlot = int.MinValue;
        private int _fnCursorRow;
        private bool _fnSpilled;
        private readonly List<(int slot, int row, double x, string text, Text.TextState st, double size, bool sup)> _fnCells = new();
        private readonly Dictionary<int, int> _fnRowsBySlot = new();
        private readonly Dictionary<int, double> _slotBottomLimit = new();

        private double FnBandTopFromTop(int slot) => slot == _fnHomeSlot
            ? _fnHomeBandTopFromTop
            : _marginTop + _fnParentPitch + 4.8;

        private int FnCapacity(int slot) =>
            Math.Max(1, (int)((_startPageHeight - FnBandTopFromTop(slot)) / _fnPitch + 1e-6));

        public void QueueFootnote(Note note) => QueueFootnote(note, 0, 0);

        /// <summary>Queue a TextFragment.FootNote for band rendering. The
        /// parent fragment's size/leading give the body pitch that shapes the
        /// continuation-page grid. Lines are assigned to page slots
        /// immediately so the flow reserves the band region and later body
        /// writes page-break above it.</summary>
        public void QueueFootnote(Note note, double parentSize, double parentLineSpacing)
        {
            if (note is null || note.Paragraphs is not { Count: > 0 }) return;

            // Footnote text metrics come from the first paragraph fragment
            // (segment font/size promoted up, same as WriteTextFragment).
            Text.TextFragment? first = null;
            foreach (var para in note.Paragraphs)
                if (para is Text.TextFragment f0) { first = f0; break; }
            if (first is not null && first.Segments is { Count: > 0 } fsegs)
                foreach (var s in fsegs)
                {
                    if (first.TextState.Font?.SourceFontData is null && first.TextState.FontData is null
                        && s.TextState.Font?.SourceFontData is not null)
                        first.TextState.Font = s.TextState.Font;
                    if (first.TextState.FontData is null && s.TextState.FontData is not null)
                        first.TextState.FontData = s.TextState.FontData;
                    // FontSize defaults to a 10 pt placeholder, so promotion must
                    // key off FontSizeTouched, not <= 0.
                    if (!first.TextState.FontSizeTouched && s.TextState.FontSizeTouched)
                        first.TextState.FontSize = s.TextState.FontSize;
                    if (first.TextState.LineSpacing <= 0 && s.TextState.LineSpacing > 0)
                        first.TextState.LineSpacing = s.TextState.LineSpacing;
                }
            var fnSize = first is not null && first.TextState.FontSizeTouched ? (double)first.TextState.FontSize : 9;
            var fnLs = first is not null ? (double)first.TextState.LineSpacing : 0;

            if (_fnCursorSlot == int.MinValue)
            {
                _fnPitch = fnSize + fnLs;
                _fnSize = fnSize;
                _fnParentPitch = parentSize > 0 ? parentSize + parentLineSpacing : _fnPitch;
                _fnHomeSlot = _currentSlot;
                _fnHomeBandTopFromTop = (_startPageHeight - _curY) + 4.8;
                _fnCursorSlot = _currentSlot;
                _fnCursorRow = 0;
            }

            // The note's styled lines: superscript label then paragraph text.
            var runs = new List<StyledRun>();
            if (!string.IsNullOrEmpty(note.Text))
                runs.Add(new StyledRun
                {
                    Text = note.Text, Size = _fnSize, Sup = true,
                    State = first?.TextState ?? new Text.TextState(),
                });
            foreach (var para in note.Paragraphs)
                if (para is Text.TextFragment f && !string.IsNullOrEmpty(f.Text))
                    runs.Add(new StyledRun
                    {
                        Text = f.Text,
                        Size = f.TextState.FontSize > 0 ? f.TextState.FontSize : _fnSize,
                        State = f.TextState,
                    });
            var lines = LayoutStyledLines(runs, _startPage.Width - _marginLeft - _marginRight);

            foreach (var (left, cells) in lines)
            {
                while (_fnCursorRow >= FnCapacity(_fnCursorSlot))
                {
                    _fnCursorSlot++;
                    _fnCursorRow = 0;
                    _fnSpilled = true;
                }
                foreach (var (x, text, r) in cells)
                    if (text.Length > 0)
                        _fnCells.Add((_fnCursorSlot, _fnCursorRow, left + x, text, r.State,
                            r.Sup ? r.Size * 0.583 : r.Size, r.Sup));
                _fnRowsBySlot[_fnCursorSlot] = _fnCursorRow + 1;
                _fnCursorRow++;
            }

            // Reserve the band region so later body writes page-break above it.
            foreach (var kv in _fnRowsBySlot)
            {
                var slot = kv.Key;
                var bandTopYup = slot == _fnHomeSlot && !_fnSpilled
                    ? kv.Value * _fnPitch
                    : _startPageHeight - FnBandTopFromTop(slot);
                _slotBottomLimit[slot] = Math.Max(
                    _slotBottomLimit.TryGetValue(slot, out var prev) ? prev : double.MinValue,
                    bandTopYup);
            }
        }

        /// <summary>Emit the planned footnote band: materialise continuation
        /// slots the body never created, draw the separator rule on the home
        /// page and the first continuation page, then queue every band cell
        /// on its line grid (line k's box is [bandTop + k·pitch,
        /// bandTop + (k+1)·pitch] from the page top; the baseline sits one
        /// descent above the box bottom).</summary>
        public void FinaliseFootnotes()
        {
            if (_fnCells.Count == 0) return;

            var maxSlot = int.MinValue;
            foreach (var c in _fnCells) if (c.slot > maxSlot) maxSlot = c.slot;
            while (maxSlot >= _overflowPages.Count)
                _overflowPages.Add((System.Array.Empty<byte>(), _startPage.Width, _startPageHeight));

            double HomeBandTop()
            {
                if (!_fnSpilled)
                {
                    var rows = _fnRowsBySlot.TryGetValue(_fnHomeSlot, out var r) ? r : 0;
                    return _startPageHeight - rows * _fnPitch;
                }
                return _fnHomeBandTopFromTop;
            }
            double BandTop(int slot) => slot == _fnHomeSlot ? HomeBandTop() : FnBandTopFromTop(slot);

            var ruleSlots = new List<int> { _fnHomeSlot };
            if (_fnSpilled) ruleSlots.Add(_fnHomeSlot + 1);
            foreach (var slot in ruleSlots)
            {
                var yUp = _startPageHeight - BandTop(slot);
                var b = new Content.ContentStreamBuilder();
                b.SaveState();
                b.SetFillColor(0, 0, 0);
                b.Rectangle(_marginLeft, yUp, _startPage.Width - _marginLeft - _marginRight, 1.0);
                b.Fill();
                b.RestoreState();
                var bytes = b.Build();
                if (slot < 0) _startPage.AddContentStream(bytes);
                else if (slot < _overflowPages.Count)
                {
                    var (content, w, h) = _overflowPages[slot];
                    var merged = new byte[content.Length + bytes.Length];
                    Buffer.BlockCopy(content, 0, merged, 0, content.Length);
                    Buffer.BlockCopy(bytes, 0, merged, content.Length, bytes.Length);
                    _overflowPages[slot] = (merged, w, h);
                }
            }

            foreach (var (slot, row, x, text, st, size, sup) in _fnCells)
            {
                // The queued Y is the line box bottom (descender line) — the
                // deferred TextBuilder write lifts it by the font descent, which
                // is exactly the band grid (baseline = box bottom − descent).
                var posFromTop = BandTop(slot) + (row + 1) * _fnPitch - (sup ? 0.33 * _fnSize : 0);
                var pos = _startPageHeight - posFromTop;
                _pendingEmbeddedRenders.Add((slot, _marginLeft + x, pos + size,
                    text, st, size, pos));
            }
        }


        /// <summary>Layout each queued footnote at the bottom of its slot's
        /// page, growing upward from marginBottom. Each footnote's text wraps
        /// to contentWidth using TTF widths when available; the resulting
        /// chunks are queued into _pendingEmbeddedRenders so the existing
        /// embedded-render finaliser dispatches them to the right Page with
        /// the right embedded font. Footnote font defaults to the first
        /// paragraph's font / size; falls back to Helvetica 9pt.</summary>
        public void FinaliseMarkedFootnotes()
        {
            if (_pendingFootnotes.Count == 0) return;

            // Group by slot so each page gets one band.
            var bySlot = new Dictionary<int, List<(Note note, string marker, double parentSize)>>();
            foreach (var (slot, note, marker, parentSize) in _pendingFootnotes)
            {
                if (!bySlot.TryGetValue(slot, out var list))
                    bySlot[slot] = list = new List<(Note, string, double)>();
                list.Add((note, marker, parentSize));
            }

            var contentWidth = _startPage.Width - _marginLeft - _marginRight;
            if (contentWidth <= 0) return;
            var pageWidth = _startPage.Width;
            var pageHeight = _startPageHeight;

            // Process slots in order so spillover pages from slot N land
            // before slot N+1's content is finalised.
            var sortedSlots = new List<int>(bySlot.Keys);
            sortedSlots.Sort();

            foreach (var slot in sortedSlots)
            {
                var notes = bySlot[slot];
                // Collect all lines for this page's footnote band in natural
                // top-down reading order. The first line of each note carries its
                // superscript marker (rendered separately, body shifted right by
                // the marker's width). Band model: each line is a row of pitch
                // (FontSize + LineSpacing); a note built from a plain string
                // uses TextFragment defaults (Helvetica 10pt, black).
                var bandLines = new List<(string text, Text.TextState state, double size,
                    double pitch, string font, double width, string? marker,
                    double markerWidth, double markerSize, Note note)>();
                foreach (var (note, marker, parentSize) in notes)
                {
                    var noteFirstLine = true;
                    foreach (var para in note.Paragraphs)
                    {
                        if (para is not Text.TextFragment fn) continue;
                        // Promote the first segment's font/size up to the fragment
                        // when missing (same logic as WriteTextFragment).
                        if (fn.Segments is { Count: > 0 } segs)
                            foreach (var s in segs)
                            {
                                if (fn.TextState.Font?.SourceFontData is null
                                    && fn.TextState.FontData is null
                                    && s.TextState.Font?.SourceFontData is not null)
                                    fn.TextState.Font = s.TextState.Font;
                                if (fn.TextState.FontData is null && s.TextState.FontData is not null)
                                    fn.TextState.FontData = s.TextState.FontData;
                                if (fn.TextState.FontSize <= 0 && s.TextState.FontSize > 0)
                                    fn.TextState.FontSize = s.TextState.FontSize;
                            }
                        var fnSize = fn.TextState.FontSize > 0 ? fn.TextState.FontSize : 10;
                        var pitch = fnSize + (fn.TextState.LineSpacing > 0 ? fn.TextState.LineSpacing : 0);
                        var fnFont = Text.TextBuilder.MapToStandard14Public(fn.TextState);
                        var fnFontData = fn.TextState.FontData ?? fn.TextState.Font?.SourceFontData;
                        var measure = Text.TextPaginator.CreateMeasurer(fnFont, fnSize, fnFontData);
                        // The marker renders at half the PARENT fragment's size,
                        // not the note body's.
                        var markerSize = parentSize * MarkerSizeRatio;
                        var markerW = noteFirstLine && marker.Length > 0
                            ? Text.TextPaginator.CreateMeasurer(fnFont, markerSize, fnFontData)(marker)
                            : 0;
                        // The first line wraps in the width remaining after the
                        // marker; continuation lines return to the full width.
                        var lines = Text.TextPaginator.WrapToWidth(fn.Text ?? string.Empty,
                            fnFont, fnSize, contentWidth, fnFontData, markerW);
                        for (var li = 0; li < lines.Count; li++)
                        {
                            var withMarker = li == 0 && noteFirstLine;
                            bandLines.Add((lines[li], fn.TextState, fnSize, pitch, fnFont,
                                (withMarker ? markerW : 0) + measure(lines[li]),
                                withMarker ? marker : null, withMarker ? markerW : 0,
                                markerSize, note));
                        }
                        noteFirstLine = false;
                    }
                }

                if (bandLines.Count == 0) continue;

                // The band hangs BELOW the bottom margin line and grows downward
                // into the margin: body layout is unaffected and no page break
                // occurs, even deep into the margin. Row i's
                // bottom is marginBottom - sum(pitch_0..i); its baseline sits
                // descent(font) above the row bottom.
                double rowBottom = _marginBottom;
                foreach (var bl in bandLines)
                {
                    rowBottom -= bl.pitch;
                    var descent = DescentNorm(bl.font) * bl.size;
                    var baseline = rowBottom + descent;
                    if (bl.marker is { } mk)
                    {
                        // Marker row bottom sits (pitch - markerSize) above the
                        // line's row bottom; ForegroundColor tints only the marker.
                        var markerBaseline = rowBottom + (bl.pitch - bl.markerSize)
                                             + DescentNorm(bl.font) * bl.markerSize;
                        var markerState = new Text.TextState
                        {
                            Font = bl.state.Font,
                            FontData = bl.state.FontData,
                            ForegroundColor = bl.note.TextState?.ForegroundColor,
                        };
                        _pendingEmbeddedRenders.Add((slot, _marginLeft, 0, mk,
                            markerState, bl.markerSize, markerBaseline));
                    }
                    _pendingEmbeddedRenders.Add((slot, _marginLeft + bl.markerWidth, 0,
                        bl.text, bl.state, bl.size, baseline));
                }

                // Separator rule: 1pt above the margin floor, from the left margin
                // to the longest band line's right edge (capped at the content
                // right edge), styled by the page's NoteLineStyle (default: solid
                // black 1pt).
                double maxLineWidth = 0;
                foreach (var bl in bandLines)
                    maxLineWidth = Math.Max(maxLineWidth, bl.width);
                var ruleY = _marginBottom + 1;
                var ruleRight = Math.Min(_marginLeft + maxLineWidth, pageWidth - _marginRight);
                var style = _startPage.NoteLineStyle;
                var rb = new Content.ContentStreamBuilder();
                rb.SaveState();
                rb.SetLineWidth(style?.LineWidth > 0 ? style.LineWidth : 1);
                if (style?.Color is { } sc)
                    rb.SetStrokeColor(sc.R / 255.0, sc.G / 255.0, sc.B / 255.0);
                else
                    rb.SetStrokeGray(0);
                if (style?.DashArray is { Length: > 0 } da)
                {
                    var pattern = new double[da.Length];
                    for (var di = 0; di < da.Length; di++) pattern[di] = da[di];
                    rb.SetDashPattern(pattern, style.DashPhase);
                }
                rb.MoveTo(_marginLeft, ruleY).LineTo(ruleRight, ruleY).Stroke().RestoreState();
                _pendingRules.Add((slot, rb.Build()));
            }
        }

        /// <summary>Append each queued rule/graphics op-run to its materialised
        /// page. Same slot resolution as the other finalisers.</summary>
        public void FinaliseRules(IList<Page> overflowPageRefs)
        {
            foreach (var (slot, ops) in _pendingRules)
            {
                var target = slot < 0 ? _startPage :
                    slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;
                target?.AddContentStream(ops);
            }
        }

        /// <summary>Resolve every queued link annotation against the actual
        /// Page sequence and emit it. <paramref name="overflowPageRefs"/> is
        /// the list of Pages added (in order) from this layout's overflow
        /// queue; index N corresponds to slot N. Called by Document.Save once
        /// the outer loop has drained _overflowPages into Pages.</summary>
        public void FinaliseAnnotations(IList<Page> overflowPageRefs)
        {
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;

            foreach (var (slot, rect, hyperlink) in _pendingLinks)
            {
                var srcPage = PageOf(slot);
                if (srcPage is null) continue;
                EmitLinkOn(srcPage, rect, hyperlink, PageOf);
            }

            foreach (var (slot, annot, rect) in _pendingFlowAnnots)
            {
                var pg = PageOf(slot);
                if (pg is null) continue;
                BindFlowAnnotation(pg, annot, rect);
            }
        }

        /// <summary>Materialise every queued in-page HTML checkbox on the page its slot
        /// resolved to, and register it on the document's Form so it reports the page it
        /// actually flowed onto.</summary>
        public void FinaliseFormFields(IList<Page> overflowPageRefs, Document doc)
        {
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;

            foreach (var (slot, rect, checkedState) in _pendingFormFields)
            {
                var pg = PageOf(slot);
                if (pg is null) continue;
                var checkbox = new Aspose.Pdf.Forms.CheckboxField(pg, rect) { Checked = checkedState };
                doc.Form.Add(checkbox, pg.Number);
            }
        }

        // Bind a flow-placed annotation to its final page: translate the authored
        // geometry into the reserved rectangle, register the annotation, and give
        // it its flow appearance — Square strokes its colour, Line strokes /L,
        // Ink strokes its paths; Circle and Text stay INVISIBLE (they reserve
        // their block but paint nothing) via an explicitly empty /AP, so neither
        // the renderer's Square/Circle default nor the save-time icon
        // materialiser paints them.
        private static void BindFlowAnnotation(Page pg, Annotations.Annotation annot, Rectangle rect)
        {
            var old = annot.Rect ?? new Rectangle(0, 0, 0, 0);
            double dx = rect.LLX - old.LLX, dy = rect.LLY - old.LLY;
            annot.Rect = rect;
            switch (annot)
            {
                case Annotations.LineAnnotation line:
                    line.Starting = new Point(line.Starting.X + dx, line.Starting.Y + dy);
                    line.Ending = new Point(line.Ending.X + dx, line.Ending.Y + dy);
                    pg.Annotations.Add(line);
                    line.UpdateAppearances();
                    break;
                case Annotations.InkAnnotation ink:
                    var moved = new List<Point[]>();
                    foreach (var stroke in ink.InkList)
                    {
                        var s = new Point[stroke.Length];
                        for (var i = 0; i < stroke.Length; i++)
                            s[i] = new Point(stroke[i].X + dx, stroke[i].Y + dy);
                        moved.Add(s);
                    }
                    ink.InkList = moved;
                    pg.Annotations.Add(ink);
                    ink.UpdateAppearances();
                    break;
                case Annotations.SquareAnnotation sq:
                    pg.Annotations.Add(sq);
                    sq.UpdateAppearances();
                    break;
                case Annotations.CircleAnnotation or Annotations.TextAnnotation:
                    pg.Annotations.Add(annot);
                    annot.SetEmptyAppearance();
                    break;
                default:
                    pg.Annotations.Add(annot);
                    break;
            }
        }

        /// <summary>Render every queued embedded-font chunk through a
        /// TextBuilder bound to its target Page. Called from Document.Save
        /// after the overflow drain so each chunk's slot resolves to a real
        /// Page that TextBuilder can register the embedded font into.</summary>
        public void FinaliseEmbeddedRenders(IList<Page> overflowPageRefs)
        {
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;

            foreach (var (slot, x, y, text, textState, fontSize, baseline) in _pendingEmbeddedRenders)
            {
                var target = PageOf(slot);
                if (target is null) continue;
                var sub = new Text.TextFragment(text)
                {
                    Position = new Text.Position(x, baseline ?? (y - fontSize))
                };
                // Copy the embedded-font state across so TextBuilder picks the
                // right embedding path (FontData / TtfData / Font.SourceFontData).
                sub.TextState.FontSize = (float)fontSize;
                sub.TextState.FontData = textState.FontData;
                sub.TextState.Font = textState.Font;
                sub.TextState.ForegroundColor = textState.ForegroundColor;
                sub.TextState.IsBold = textState.IsBold;
                sub.TextState.IsItalic = textState.IsItalic;
                // Underline must survive the deferred hop — TextBuilder draws it
                // as a rectangle at save time from the fragment state.
                sub.TextState.Underline = textState.Underline;
                sub.TextState.IsStrikeOut = textState.IsStrikeOut;
                // Rotation too: the highlight and the glyph run both turn with it.
                sub.TextState.Rotation = textState.Rotation;
                // Carry the leading so a multi-line chunk renders on the same pitch
                // the paginator reserved (fontSize + LineSpacing), not a default 1.2x.
                sub.TextState.LineSpacing = textState.LineSpacing;
                // Carry the highlight so TextBuilder draws the per-line background rectangle
                // (the non-embedded path passes it through BuildWrappedTextStream; the embedded
                // path must copy it here or the highlight is silently dropped).
                sub.TextState.BackgroundColor = textState.BackgroundColor;
                // Marked-content tagging must survive the deferred hop: TextBuilder
                // wraps the emitted ops in BDC/EMC (merging same-tag/-MCID runs).
                sub.TextState.MarkedContentTag = textState.MarkedContentTag;
                sub.TextState.MarkedContentMcid = textState.MarkedContentMcid;
                var tb = new Text.TextBuilder(target);
                tb.AppendText(sub);
            }
        }

        /// <summary>Distribute the accumulated line-break notifications to the
        /// materialised Pages once the overflow drain has produced real Page
        /// objects for each slot.</summary>
        public void FinaliseNotifications(IList<Page> overflowPageRefs)
        {
            if (!_logNotifications || _notificationsBySlot.Count == 0) return;
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;
            foreach (var kv in _notificationsBySlot)
            {
                var page = PageOf(kv.Key);
                if (page is not null) page.NotificationLog += kv.Value.ToString();
            }
        }

        private void EmitLinkOn(Page srcPage, Rectangle rect, Hyperlink hyperlink,
            Func<int, Page?> pageOf)
        {
            if (hyperlink is LocalHyperlink lh)
            {
                int targetPageNumber = lh.TargetPageNumber;
                double destX = _marginLeft, destY = 0;
                if (targetPageNumber <= 0 && lh.Target is { } target
                    && _paragraphPositions.TryGetValue(target, out var pos)
                    && pageOf(pos.slot) is { } tp)
                {
                    targetPageNumber = tp.Number;
                    // The GoTo XYZ coordinate is in default user space --
                    // x = left margin, y = top of the target paragraph.
                    destY = pos.yTop;
                }
                if (targetPageNumber > 0)
                    srcPage.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(targetPageNumber, destX, destY, 0)));
            }
            else if (hyperlink is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
            {
                srcPage.Annotations.AddLinkAnnotation(rect, wh.Url);
            }
            else if (hyperlink is FileHyperlink fh && !string.IsNullOrEmpty(fh.FileName))
            {
                var launch = new LaunchAction(fh.FileName) { NewWindow = fh.NewWindow };
                srcPage.Annotations.AddLinkAnnotation(rect, launch);
            }
        }

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

        /// <summary>Bottom content margin (points) — the Y below which the flow page-breaks.</summary>
        public double BottomMargin => _marginBottom;

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

        /// <summary>Add vector content to the page a given slot will become. The slot may
        /// be the start page, the buffer still being written, or an overflow page already
        /// flushed to the queue — a frame drawn when its block closes has to reach back to
        /// the pages its content already ran down.</summary>
        public void AddContentToSlot(int slot, byte[] content)
        {
            if (content is null || content.Length == 0) return;
            if (_overflowBuffer is not null && slot == _currentSlot) { _overflowBuffer.Add(content); return; }
            if (slot < 0) { _startPage.AddContentStream(content); return; }
            if (slot >= _overflowPages.Count) return;
            var (existing, w, h) = _overflowPages[slot];
            var merged = new byte[existing.Length + content.Length];
            Buffer.BlockCopy(existing, 0, merged, 0, existing.Length);
            Buffer.BlockCopy(content, 0, merged, existing.Length, content.Length);
            _overflowPages[slot] = (merged, w, h);
        }

        /// <summary>Draw the sides of a framed block — a CSS border on a block element —
        /// across every page its content occupied. The box spans the write region's full
        /// width; the top rule is drawn only on the page the block opened on and the
        /// bottom rule only on the page it closed on, so a frame taller than the page runs
        /// off the bottom of one and continues at the next one's top margin, the way a
        /// browser prints it. Strokes sit INSIDE the box, so the outer edge is the region.
        /// </summary>
        public void DrawFrameBox(int startSlot, double startTop, double endBottom,
            double borderWidth, Color color)
        {
            if (borderWidth <= 0) return;
            var left = _marginLeft;
            var right = _startPage.Width - _marginRight;
            if (right - left <= 0) return;
            var half = borderWidth / 2;
            var pageTop = _startPageHeight - _marginTop;
            for (var slot = startSlot; slot <= _currentSlot; slot++)
            {
                var top = slot == startSlot ? startTop : pageTop;
                var bottom = slot == _currentSlot ? endBottom : _marginBottom;
                if (top - bottom <= 0) continue;
                var b = new Content.ContentStreamBuilder();
                b.SaveState();
                b.SetLineWidth(borderWidth);
                b.SetStrokeColor(color);
                b.MoveTo(left + half, top).LineTo(left + half, bottom).Stroke();
                b.MoveTo(right - half, top).LineTo(right - half, bottom).Stroke();
                if (slot == startSlot)
                    b.MoveTo(left, top - half).LineTo(right, top - half).Stroke();
                if (slot == _currentSlot)
                    b.MoveTo(left, bottom + half).LineTo(right, bottom + half).Stroke();
                b.RestoreState();
                AddContentToSlot(slot, b.Build());
            }
        }

        /// <summary>True once the flow has page-broken off the start page (subsequent
        /// content lives in an overflow buffer, not on a live Page yet).</summary>
        public bool HasOverflowed => _overflowBuffer is not null;

        /// <summary>Extra left indent (points) added to the write region for the next
        /// fragment — used for HTML block / list-item indentation. Reset by the caller
        /// between blocks.</summary>
        public double LeftIndent { get; set; }

        /// <summary>Left edge (in points) of the region the next line writes
        /// into — the current column when in column mode, else the page's left
        /// content margin, plus any block indent.</summary>
        private double CurLeft => (_colLefts is not null ? _colLefts[_curCol] : _marginLeft) + LeftIndent;

        /// <summary>Usable width (in points) of the current write region — the
        /// current column's width in column mode, else the full content width,
        /// reduced by any block indent.</summary>
        internal double CurWidth => (_colWidths is not null
            ? _colWidths[_curCol]
            : _startPage.Width - _marginLeft - _marginRight) - LeftIndent;

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
            _slotBottomY[_currentSlot] = _slotBottomY.TryGetValue(_currentSlot, out var prev)
                ? Math.Min(prev, y) : y;
        }

        private void FlowToNextRegion()
        {
            // A fresh column / page restarts body line placement from the top, so
            // the next line drops by its font size rather than chaining onto the
            // previous region's last baseline.
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            if (_colLefts is not null && _curCol < _colLefts.Length - 1)
            {
                _curCol++;
                _curY = _colBandTop;
            }
            else
            {
                StartNewPage();
            }
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

        /// <summary>Flush the last overflow page buffer to the shared overflow queue.</summary>
        public void Commit()
        {
            if (_overflowBuffer is { Count: > 0 })
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
                _overflowBuffer = null;
            }
        }

        /// <summary>Resume the flow on a spill page whose content is already built — e.g.
        /// the final page of a multi-page table. The supplied content seeds the next
        /// overflow slot's buffer and the cursor resumes at <paramref name="resumeY"/>, so
        /// following paragraphs append below it on the same page instead of opening a fresh
        /// one. Returns the slot index that page will occupy in the overflow queue.</summary>
        public int ContinueOnPrebuiltSpill(byte[] content, double resumeY)
        {
            // Flush any buffer in flight so it keeps its own slot before we seed a new one.
            if (_overflowBuffer is { Count: > 0 })
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
            _overflowBuffer = new List<byte[]> { content };
            _currentSlot = _overflowPages.Count; // slot this buffer will flush into
            _curY = resumeY;
            if (_colLefts is not null)
            {
                _curCol = 0;
                _colBandTop = _curY;
                _colDeepestY = _curY;
            }
            return _currentSlot;
        }

        /// <summary>Render a fragment whose segments carry differing styles as ONE
        /// line at the flow cursor: each segment is its own show in its own
        /// Standard-14 base font/size, x chained by the segment's REAL measured
        /// width (a bold run advances by its bold width). Returns false — caller
        /// falls back to the legacy writer — for shapes this single-line writer
        /// doesn't model: explicit newlines, text wider than the band (needs
        /// wrapping), embedded fonts, decorations, hyperlinks, or an overflow
        /// buffer in flight (continuation pages only materialise Helvetica).</summary>
        public bool TryWriteStyledSegmentsLine(Text.TextFragment tf)
        {
            if (_overflowBuffer is not null) return false;
            var runs = new List<(string text, string baseFont, double fs, Color? color)>();
            foreach (var seg in tf.Segments)
            {
                var text = seg.Text ?? string.Empty;
                if (text.Length == 0) continue;
                if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) return false;
                if (seg.Hyperlink is not null) return false;
                var st = seg.TextState;
                if (st.IsUnderline || st.IsStrikeOut) return false;
                if (st.FontData is not null || st.Font?.SourceFontData is not null) return false;
                var fs = st.FontSizeTouched ? (double)st.FontSize
                    : tf.TextState.FontSize > 0 ? tf.TextState.FontSize : (double)st.FontSize;
                if (fs <= 0) fs = 12;
                runs.Add((text, Text.TextBuilder.MapToStandard14Public(st), fs,
                    st.ForegroundColor ?? tf.TextState.ForegroundColor));
            }
            if (runs.Count == 0) return true; // nothing visible — consume as an empty write

            double totalWidth = 0, maxFs = 0;
            foreach (var r in runs)
            {
                totalWidth += MeasureLineWidth(r.text, r.baseFont, r.fs);
                if (r.fs > maxFs) maxFs = r.fs;
            }
            if (totalWidth > CurWidth + 0.5) return false; // would wrap — legacy writer

            var lineHeight = tf.TextState.LineSpacing > 0 ? maxFs + tf.TextState.LineSpacing : maxFs;
            EnsureRoom(lineHeight);

            // First baseline drops by the cap-height ascent from the band top —
            // the same placement BuildWrappedTextStream uses for plain fragments,
            // so a styled line sits exactly where its unstyled twin would.
            var capHeight = Text.Standard14Fonts.GetCapHeight(runs[0].baseFont);
            var ascent = capHeight > 0 ? capHeight / 1000.0 * maxFs : maxFs * 0.7;
            var baseline = _curY - ascent;

            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            var x = CurLeft;
            foreach (var r in runs)
            {
                var res = Table.RegisterFont(_startPage, r.baseFont);
                b.BeginText().SetFont(res, r.fs);
                if (r.color is { } c) b.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
                else b.SetFillColor(0, 0, 0);
                b.MoveTextPosition(x, baseline).ShowText(r.text).EndText();
                x += MeasureLineWidth(r.text, r.baseFont, r.fs);
            }
            b.RestoreState();
            WriteContent(b.Build());

            _curY -= lineHeight;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
        }

        /// <summary>Write one HTML block whose inline <c>&lt;b&gt;</c>/<c>&lt;strong&gt;</c>
        /// and <c>&lt;u&gt;</c> runs each set in their OWN style, as a single wrapped
        /// paragraph: the emphasised words draw bold and/or underlined while the rest of
        /// the block stays regular. Every run is measured in the face it actually draws
        /// in — <see cref="Text.TextBuilder"/> resolves a bold flag on a repository face
        /// to that family's Bold member, so the wrap and the run x positions are taken
        /// from the same metrics the glyphs get — and each piece is queued as its own
        /// deferred render. Returns false when the flow cannot take the fragment.</summary>
        public bool WriteEmphasisRuns(Text.TextFragment tf,
            IReadOnlyList<(int Start, int Length, bool Bold, bool Underline)> runs)
        {
            if (tf.HasExplicitPosition) return false;
            var text = tf.Text ?? string.Empty;
            if (text.Length == 0 || runs.Count == 0) return false;
            var contentWidth = CurWidth;
            if (contentWidth <= 0) return false;

            var fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            var regFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            var regData = tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData;
            // The bold member of the block's own family, resolved exactly the way
            // TextBuilder resolves a bold flag on a repository-embedded face. Absent
            // (a core face, or no Bold file installed) the regular metrics stand in,
            // which is what the writer will draw with too.
            Text.FontData? boldData = null;
            var family = tf.TextState.FontData?.FontName ?? tf.TextState.Font?.FontName
                ?? tf.TextState.FontName;
            if (!string.IsNullOrEmpty(family) && !Text.Standard14Fonts.IsCoreName(family)
                && !family.Contains("Bold", StringComparison.OrdinalIgnoreCase))
            {
                var styled = Text.FontRepository.FindFontData(family + " Bold");
                if (styled?.TtfData is not null
                    && styled.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true)
                    boldData = styled;
            }
            // Standard-14 stand-in for the bold runs when no repository Bold member
            // resolved: the bold variant of whatever core family the block maps to.
            var boldProbe = new Text.TextState
            {
                Font = tf.TextState.Font,
                FontData = tf.TextState.FontData,
                FontName = tf.TextState.FontName,
                IsBold = true,
                IsItalic = tf.TextState.IsItalic,
            };
            var boldFont = Text.TextBuilder.MapToStandard14Public(boldProbe);
            var measureReg = Text.TextPaginator.CreateMeasurer(regFont, fontSize, regData);
            var measureBold = Text.TextPaginator.CreateMeasurer(boldFont, fontSize,
                boldData ?? regData);
            double Measure(string s, bool bold) => bold ? measureBold(s) : measureReg(s);

            // Word / whitespace tokens that never straddle a run boundary, so every
            // token has exactly one style and one measurable width.
            var tokens = new List<(string Text, bool Bold, bool Under, bool Space, double W)>();
            foreach (var r in runs)
            {
                if (r.Start < 0 || r.Length <= 0 || r.Start + r.Length > text.Length) continue;
                var seg = text.Substring(r.Start, r.Length);
                var i = 0;
                while (i < seg.Length)
                {
                    var isSpace = seg[i] == ' ';
                    var j = i;
                    while (j < seg.Length && (seg[j] == ' ') == isSpace) j++;
                    var piece = seg.Substring(i, j - i);
                    tokens.Add((piece, r.Bold, r.Underline, isSpace, Measure(piece, r.Bold)));
                    i = j;
                }
            }
            if (tokens.Count == 0) return false;

            // Greedy wrap: a word that would overrun the content box starts a new line
            // and the break's own space is swallowed, as the plain wrap does.
            var lines = new List<List<(string Text, bool Bold, bool Under, double W)>>();
            var cur = new List<(string Text, bool Bold, bool Under, double W)>();
            var curW = 0.0;
            foreach (var t in tokens)
            {
                if (!t.Space && cur.Count > 0 && curW + t.W > contentWidth + WrapWidthSlackPt)
                {
                    while (cur.Count > 0 && cur[^1].Text.Trim().Length == 0)
                        cur.RemoveAt(cur.Count - 1);
                    if (cur.Count > 0) lines.Add(cur);
                    cur = new List<(string, bool, bool, double)>();
                    curW = 0;
                }
                if (t.Space && cur.Count == 0) continue;
                cur.Add((t.Text, t.Bold, t.Under, t.W));
                curW += t.W;
            }
            if (cur.Count > 0) lines.Add(cur);
            if (lines.Count == 0) return false;

            var lineHeight = tf.TextState.LineSpacing > 0
                ? fontSize + tf.TextState.LineSpacing : fontSize;
            EnsureRoom(lineHeight);
            // Runs go out as deferred renders; everything after them in this flow must
            // defer too so the page's content order stays paragraph order.
            _forceDeferredWrites = true;

            var idx = 0;
            while (idx < lines.Count)
            {
                var availableLines = Math.Max(1, (int)((_curY - EffectiveBottom) / lineHeight));
                var chunkSize = Math.Min(availableLines, lines.Count - idx);
                // Same first-baseline rule as the plain embedded chunk: chain onto the
                // previous body baseline when there is one, else drop by the font size.
                var firstBaseline = _lastBodyBaseline.HasValue
                    ? _lastBodyBaseline.Value - lineHeight
                    : _curY - (tf.TextState.LineSpacing > 0 && !tf.TextState.LineSpacingSynthetic
                        ? lineHeight : fontSize);
                for (var j = 0; j < chunkSize; j++)
                {
                    var baseline = firstBaseline - j * lineHeight;
                    var x = CurLeft;
                    var line = lines[idx + j];
                    var k = 0;
                    while (k < line.Count)
                    {
                        // Merge the neighbours that share a style into one show.
                        var m = k + 1;
                        while (m < line.Count && line[m].Bold == line[k].Bold
                               && line[m].Under == line[k].Under) m++;
                        var sb = new System.Text.StringBuilder();
                        var w = 0.0;
                        for (var p = k; p < m; p++) { sb.Append(line[p].Text); w += line[p].W; }
                        var runState = new Text.TextState
                        {
                            Font = tf.TextState.Font,
                            FontData = tf.TextState.FontData,
                            ForegroundColor = tf.TextState.ForegroundColor,
                            IsBold = line[k].Bold,
                            IsItalic = tf.TextState.IsItalic,
                            Underline = line[k].Under,
                        };
                        _pendingEmbeddedRenders.Add((_currentSlot, x, _curY,
                            sb.ToString(), runState, fontSize, baseline));
                        x += w;
                        k = m;
                    }
                }
                _lastBodyBaseline = firstBaseline - (chunkSize - 1) * lineHeight;
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY -= lineHeight * chunkSize;
                idx += chunkSize;
                if (idx < lines.Count) FlowToNextRegion();
            }
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
        }

        /// <summary>Rounding slack allowed before a word is judged not to fit the
        /// content box — the same half-point the plain styled-line writer uses.</summary>
        private const double WrapWidthSlackPt = 0.5;

        /// <summary>Break one wrapped line into word / single-space tokens and
        /// give each its x offset from the line start, full-justified to
        /// <paramref name="targetWidth"/>: the slack beyond the line's natural
        /// width is split equally over the interior spaces, and every token is
        /// shifted right by (spaces before it) x that share — so each stretch
        /// gap opens between a space glyph and the following word, space glyphs
        /// keep their natural width, and the last word's right edge lands on
        /// the target exactly. The line-break trailing space is dropped, and a
        /// line without interior spaces stays at its natural width.</summary>
        private static IEnumerable<(string token, double xOffset)> JustifyLineTokens(
            string line, Func<string, double> measure, double targetWidth)
        {
            var content = line.TrimEnd(' ');
            if (content.Length == 0) yield break;

            var tokens = new List<string>();
            var start = 0;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] != ' ') continue;
                if (i > start) tokens.Add(content.Substring(start, i - start));
                tokens.Add(" ");
                start = i + 1;
            }
            if (start < content.Length) tokens.Add(content.Substring(start));

            var widths = new double[tokens.Count];
            double naturalWidth = 0;
            var interiorSpaces = 0;
            for (var t = 0; t < tokens.Count; t++)
            {
                widths[t] = measure(tokens[t]);
                naturalWidth += widths[t];
                if (tokens[t] == " ") interiorSpaces++;
            }
            var extra = interiorSpaces > 0 && targetWidth > naturalWidth
                ? (targetWidth - naturalWidth) / interiorSpaces
                : 0.0;

            double cum = 0;
            var spacesBefore = 0;
            for (var t = 0; t < tokens.Count; t++)
            {
                yield return (tokens[t], cum + spacesBefore * extra);
                if (tokens[t] == " ") spacesBefore++;
                cum += widths[t];
            }
        }

        /// <summary>
        /// Write a TextFragment as wrapped, flowed text starting at the current Y
        /// cursor. Spans multiple pages via the overflow queue when needed. Returns
        /// false if the fragment is too complex for flow layout (embedded font or
        /// multi-segment) — caller should fall back to legacy fixed-position writer.
        /// </summary>
        /// <summary>True when every NON-EMPTY segment of <paramref name="tf"/> carries
        /// the same explicitly-touched font size — the shape whose segment size may be
        /// promoted to the fragment level.</summary>
        private static bool FragmentSegmentsShareOneTouchedSize(Text.TextFragment tf)
        {
            double size = -1;
            foreach (var s in tf.Segments)
            {
                if (string.IsNullOrEmpty(s.Text)) continue;
                if (!s.TextState.FontSizeTouched) return false;
                if (size < 0) size = s.TextState.FontSize;
                else if (Math.Abs(size - s.TextState.FontSize) > 0.01) return false;
            }
            return size > 0;
        }

        public bool WriteTextFragment(Text.TextFragment tf)
        {
            // Caller-specified Position overrides flow layout. Use HasExplicitPosition,
            // not "Position != null": the getter now auto-materialises a (0,0) Position,
            // so a fragment the caller never positioned must still flow here.
            if (tf.HasExplicitPosition) return false;
            // A fragment with tab stops lays its own line out — the marker runs
            // aligned to their stops with a leader drawn between them. Seat it on
            // this flow's next baseline and let the writer that knows how emit it.
            if (tf.TabStops is { Count: > 0 } && tf.Text.Contains("#$TAB", StringComparison.Ordinal))
            {
                // A tab-stopped line the caller never sized renders at the tabbed
                // default, not the fragment ctor's placeholder: the column pitch and
                // the run widths of a tabbed table are both calibrated to it.
                const double tabbedDefaultFs = 8;
                if (!tf.TextState.FontSizeTouched) tf.TextState.SetFontSizeQuiet(tabbedDefaultFs);
                var tabFs = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : tabbedDefaultFs;
                var tabLine = tabFs * 1.2;
                if (_curY - tabLine < _marginBottom) StartNewPage(flushEmpty: true);
                AdvanceY(tabLine);
                tf.Position = new Text.Position(_marginLeft, _curY);
                new Text.TextBuilder(_startPage).AppendText(tf);
                return true;
            }
            // Promote a segment-level font/size up to the fragment when the
            // fragment itself didn't set one. Generator-style tests build the
            // fragment with `new TextFragment()` then attach a TextSegment that
            // carries TextState.Font = FontRepository.FindFont("Arial") and a
            // FontSize -- the fragment-level TextState stays at the default
            // Helvetica/12 placeholder (TextFragmentState seeds Font with
            // FontInfo.DefaultHelvetica, which has no SourceFontData), hiding
            // the embedded font from both the paginator and TextBuilder. Treat
            // the fragment's Font as "not set" when it carries no SourceFontData,
            // so any segment that brings one wins.
            if (tf.Segments is { Count: > 0 } promoteSegs)
            {
                foreach (var s in promoteSegs)
                {
                    var fragHasEmbedded = tf.TextState.Font?.SourceFontData is not null
                                          || tf.TextState.FontData is not null;
                    if (!fragHasEmbedded && s.TextState.Font?.SourceFontData is not null)
                        tf.TextState.Font = s.TextState.Font;
                    if (tf.TextState.FontData is null && s.TextState.FontData is not null)
                        tf.TextState.FontData = s.TextState.FontData;
                    // FontSize defaults to a 10 pt placeholder, so promotion must key
                    // off FontSizeTouched, not <= 0 (same rule as QueueFootnote) — a
                    // segment-level 13 pt must not lose to the untouched fragment 10.
                    // Only the empty-ctor + single-styled-segment shape promotes: a
                    // fragment with its own ctor text plus a differently-sized added
                    // segment ("Aspose" + 5 pt "TM") keeps the fragment default for
                    // its untouched segments.
                    if (!tf.TextState.FontSizeTouched && s.TextState.FontSizeTouched
                        && FragmentSegmentsShareOneTouchedSize(tf))
                        tf.TextState.FontSize = s.TextState.FontSize;
                    // Line spacing lives on the segment in generator-style fragments
                    // (`seg.TextState.LineSpacing = ...`); promote it so the paginator
                    // sees the caller's leading rather than the fragment default.
                    if (tf.TextState.LineSpacing <= 0 && s.TextState.LineSpacing > 0)
                        tf.TextState.LineSpacing = s.TextState.LineSpacing;
                    if ((tf.TextState.Font?.SourceFontData ?? tf.TextState.FontData) is not null
                        && tf.TextState.FontSize > 0 && tf.TextState.LineSpacing > 0) break;
                }
            }
            // Embedded/CID fonts (FontData set directly, or via FontRepository.FindFont
            // populating TextState.Font.SourceFontData) need TextBuilder for correct
            // glyph encoding -- but TextBuilder is page-bound, and overflow pages
            // don't exist until after the outer Document.Save loop drains them. The
            // paginator lays the fragment out in Standard-14 metric space (close
            // enough for line-break decisions) and queues each per-page chunk into
            // _pendingEmbeddedRenders; FinaliseEmbeddedRenders runs after the drain
            // and uses a fresh TextBuilder against each target Page.
            var useEmbeddedFont = tf.TextState.FontData is not null
                                  || tf.TextState.Font?.SourceFontData is not null;
            // Invisible / clipping text rendering modes need the legacy writer, which
            // emits `Tr` operators; the paginator does not.
            if (tf.TextState.RenderingMode != 0) return false;
            // Per-segment explicit Position means the caller wants precise control;
            // otherwise tf.Text (concatenated from all segments via RefreshTextFromSegments)
            // is the paragraph's logical content and flow-wraps correctly even when the
            // fragment was constructed via `new TextFragment()` + `.Segments.Add(seg)`
            // (which produces Segments.Count == 2: a default empty segment + caller's).
            if (tf.Segments is { Count: > 1 })
            {
                foreach (var s in tf.Segments)
                    if (s.Position is not null) return false;
                // Segments carrying DIFFERING font/size/style (a bold 50pt word inside
                // a 30pt sentence): render them inline AT THE CURSOR as one chained
                // line when they fit — falling back to the legacy fixed-position
                // writer stamped every such fragment at the page top-left, so a
                // page full of "label: value" fragments collapsed into one
                // overlapping line at the top. Only genuinely complex shapes
                // (multi-line, wrapping, links, decorations) keep the fallback.
                if (Text.TextBuilder.SegmentStylesDiffer(tf, tf.TextState.FontSize))
                    return TryWriteStyledSegmentsLine(tf);
            }

            var baseFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            var fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            // In column mode this is the current column's width; otherwise the
            // full page content width. Wrapping uses the entry column's width;
            // the test columns are equal-width so a fragment that flows into the
            // next column keeps the same break points.
            var contentWidth = CurWidth;
            if (contentWidth <= 0) return false;

            // WordWrapMode.NoWrap → each \n-delimited input line becomes one
            // output line, regardless of width: a
            // long line stays on one rendered line that overflows the page
            // horizontally; only vertical pagination still applies. Default
            // (ByWords / Undefined / null) flows through the width-aware wrap.
            var noWrap = tf.TextState.FormattingOptions?.WrapMode
                         == Text.TextFormattingOptions.WordWrapMode.NoWrap;
            // First-line indent (paragraph indentation set via FormattingOptions):
            // the first wrapped line starts indented and is correspondingly narrower.
            var firstLineIndent = (double)(tf.TextState.FormattingOptions?.FirstLineIndent ?? 0f);
            // Subsequent-lines indent: every wrapped line after the paragraph's first
            // starts indented by this amount. Applies across page
            // breaks too — a chunk that does not start the paragraph indents all its
            // lines.
            var subsequentLinesIndent = (double)(tf.TextState.FormattingOptions?.SubsequentLinesIndent ?? 0f);
            var rawText = tf.Text ?? string.Empty;
            var charSpacing = tf.TextState.CharacterSpacing;
            var allLines = noWrap
                ? new List<string>(rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                : Text.TextPaginator.WrapToWidth(rawText, baseFont, fontSize, contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, firstLineIndent, charSpacing);
            // When notification logging is on, trace each wrapped line's width and
            // break reason (aligned 1:1 with allLines) so the loop below can record
            // where every line finished.
            var lineTrace = _logNotifications && !noWrap
                ? Text.TextPaginator.TraceLines(rawText, baseFont, fontSize, contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, firstLineIndent)
                : null;
            // lineHeight resolution order:
            //   1. fragment.TextState.LineSpacing -- explicit float override
            //      in points, set by callers like
            //      `seg.TextState.LineSpacing = fontSize + 3f`.
            //   2. FormattingOptions.LineSpacing == FullSize -- use the font's
            //      full vertical extent from the TTF (ascent - descent).
            //   3. fontSize * 1.2 -- default for FontSize / Undefined modes.
            var fontTtf = tf.TextState.FontData?.TtfData
                          ?? tf.TextState.Font?.SourceFontData?.TtfData;
            var fullSize = tf.TextState.FormattingOptions?.LineSpacing
                           == Text.TextFormattingOptions.LineSpacingMode.FullSize;
            double lineHeight;
            if (tf.TextState.LineSpacing > 0)
                // An explicit LineSpacing is extra leading added on
                // top of the glyph height: the line pitch is fontSize + LineSpacing
                // (a 10pt font with LineSpacing 13
                // lays out on a 23pt pitch, not 13). LineSpacing == 0 degenerates to the
                // default fontSize pitch below, so the rule is uniform.
                lineHeight = fontSize + tf.TextState.LineSpacing;
            else if (fullSize && fontTtf is { Length: > 12 })
                lineHeight = ComputeFullSizeLineHeight(fontTtf, fontSize);
            else
                // Default LineSpacingMode is FontSize: the line
                // advance equals the font size, not an inflated 1.2x leading.
                lineHeight = fontSize;
            EnsureRoom(lineHeight);

            // A fragment-level hyperlink applies to the fragment's first line.
            // Capture the slot + top-of-line before the write loop advances _curY.
            var fragHyperlink = tf.HyperlinkValue;
            var fragSlot = _currentSlot;
            var fragTop = _curY;

            // Per-segment hyperlinks: each TextSegment with a Hyperlink emits a
            // LinkAnnotation sized to the segment's run. Char offsets are into the
            // fragment's full text; the emission below maps them onto each wrapped
            // line (a hyperlink that wraps gets one rect per line it covers).
            var segHyperlinks = (List<(int charStart, int charEnd, Hyperlink hyperlink)>?)null;
            if (tf.Segments is { Count: > 0 } segs)
            {
                List<(int, int, Hyperlink)>? collected = null;
                var cursor = 0;
                foreach (var seg in segs)
                {
                    var len = seg.Text?.Length ?? 0;
                    if (len > 0 && seg.Hyperlink is { } h)
                        (collected ??= new()).Add((cursor, cursor + len, h));
                    cursor += len;
                }
                segHyperlinks = collected;
            }

            // FullJustify: each wrapped line — including the paragraph's last —
            // is stretched so its final word ends exactly at the region's right
            // edge. Every word AND every interior space goes out as
            // its own absolutely-positioned show (the line-break trailing space
            // is dropped), distributing the slack equally across the interior
            // spaces as gaps between a space glyph and the next word; space
            // glyphs keep their natural width. That show structure
            // matters beyond geometry: the absorber yields one fragment per
            // show, and justified-output tests index into that fragment list.
            var fullJustify = !noWrap
                && (tf.HorizontalAlignment == HorizontalAlignment.FullJustify
                    || tf.TextState.HorizontalAlignment == HorizontalAlignment.FullJustify);
            Func<string, double>? justifyMeasurer = null;

            // Center alignment: a single-line centred fragment (e.g. a "Next
            // Steps" title between two tables) is offset so its run sits at the
            // middle of the content band. Multi-line centred text keeps the
            // legacy left flow (no template currently pins it).
            var centerOffset = 0.0;
            if (!noWrap && allLines.Count == 1
                && (tf.HorizontalAlignment == HorizontalAlignment.Center
                    || tf.TextState.HorizontalAlignment == HorizontalAlignment.Center))
            {
                var lw = MeasureText(allLines[0], baseFont, fontSize);
                if (lw < CurWidth) centerOffset = (CurWidth - lw) / 2;
            }

            var idx = 0;
            double? nonEmbeddedLastBaseline = null;
            while (idx < allLines.Count)
            {
                var availableLines = Math.Max(1, (int)((_curY - EffectiveBottom) / lineHeight));
                var chunkSize = Math.Min(availableLines, allLines.Count - idx);
                var chunk = allLines.GetRange(idx, chunkSize);

                if (fullJustify)
                {
                    justifyMeasurer ??= Text.TextPaginator.CreateMeasurer(baseFont, fontSize,
                        tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                    // Baselines follow the deferred-render chain: first line at the
                    // top of a region drops by the font size, later lines sit one
                    // line height below the previous baseline (same rule as the
                    // embedded branch below).
                    // A CALLER-set LineSpacing adds its leading above the first
                    // line too (23 pt drop for 10 pt + 13);
                    // synthetic (layout-assigned) leading keeps the plain drop.
                    var firstLineBaseline = _lastBodyBaseline.HasValue
                        ? _lastBodyBaseline.Value - lineHeight
                        : _curY - (tf.TextState.LineSpacing > 0 && !tf.TextState.LineSpacingSynthetic
                            ? lineHeight : fontSize);
                    for (var j = 0; j < chunkSize; j++)
                    {
                        var lineBaseline = firstLineBaseline - j * lineHeight;
                        foreach (var (token, xOffset) in JustifyLineTokens(chunk[j], justifyMeasurer, CurWidth))
                            _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + xOffset, _curY,
                                token, tf.TextState, fontSize, lineBaseline));
                    }
                    _lastBodyBaseline = firstLineBaseline - (chunkSize - 1) * lineHeight;
                    // All later content in this flow must defer to keep the page's
                    // content-stream order equal to paragraph order (see field doc).
                    _forceDeferredWrites = true;
                    if (_overflowBuffer is not null)
                        _overflowBuffer.Add(Array.Empty<byte>());
                }
                else if (useEmbeddedFont || _forceDeferredWrites)
                {
                    // Queue the per-page chunk for deferred rendering. TextBuilder
                    // splits on \n internally and applies the leading set by
                    // SetLeading(lineHeight), so joining chunk lines with \n gets
                    // us multi-line rendering on the target page.
                    // The first line at the top of a region drops by the font size
                    // (the standard first-line placement); every following body line
                    // sits one of its own line heights below the previous baseline,
                    // so a size change between adjacent paragraphs is spaced by the
                    // lower line's metrics. Same-size runs are unaffected.
                    // Caller-set LineSpacing: leading above the first line too
                    // (see the fullJustify branch above).
                    var firstBaseline = _lastBodyBaseline.HasValue
                        ? _lastBodyBaseline.Value - lineHeight
                        : _curY - (tf.TextState.LineSpacing > 0 && !tf.TextState.LineSpacingSynthetic
                            ? lineHeight : fontSize);
                    _lastBodyBaseline = firstBaseline - (chunkSize - 1) * lineHeight;
                    _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + centerOffset, _curY,
                        string.Join("\n", chunk), tf.TextState, fontSize, firstBaseline));
                    // Mark the overflow buffer non-empty so StartNewPage / Commit
                    // flushes it -- otherwise an overflow-only embedded-render
                    // slot would never produce a Page, the deferred render would
                    // have no target, and the test would see Pages.Count
                    // unchanged from the start-page count. The placeholder is an
                    // empty byte array (concatenates to nothing in the final
                    // content stream).
                    if (_overflowBuffer is not null)
                        _overflowBuffer.Add(Array.Empty<byte>());
                }
                else
                {
                    // Register the fragment's MAPPED base font (Times/Courier/… — not
                    // unconditionally Helvetica) so a FontName the caller or the HTML
                    // UA-default flow set actually draws in that face. Overflow pages
                    // register their own F1 at commit and stay Helvetica.
                    var fontResName = _overflowBuffer is null
                        ? Table.RegisterFont(_startPage, baseFont)
                        : "F1";
                    var alphaGsName = tf.TextState.ForegroundColor is { } fg
                        ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, fg.AByte)
                        : null;
                    // TextState.BackgroundColor draws a filled highlight behind each
                    // wrapped line (its own /ca alpha, independent of the foreground's),
                    // emitted before the glyphs so the text sits on top.
                    var bgColor = tf.TextState.BackgroundColor;
                    var bgAlphaGsName = bgColor is { } bgc
                        ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, bgc.AByte)
                        : null;
                    var content = BuildWrappedTextStream(chunk, fontResName, fontSize,
                        CurLeft, _curY, lineHeight, tf.TextState.ForegroundColor,
                        tf.TextState.IsStrikeOut, tf.TextState.IsUnderline, baseFont, alphaGsName,
                        (idx == 0 ? firstLineIndent : 0) + centerOffset, subsequentLinesIndent, idx == 0,
                        bgColor, bgAlphaGsName, tf.TextState.Rotation);
                    WriteContent(content, tf.TextState);
                    // Mirror BuildWrappedTextStream's baseline math so a footnote
                    // marker can attach to the end of this chunk's last line.
                    var nbCap = Text.Standard14Fonts.GetCapHeight(baseFont);
                    var nbAscent = nbCap > 0 ? nbCap / 1000.0 * fontSize : fontSize * 0.7;
                    nonEmbeddedLastBaseline = _curY - nbAscent - (chunkSize - 1) * lineHeight;
                    // The non-embedded path positions baselines independently;
                    // don't let a following embedded paragraph chain onto a
                    // stale baseline from before it.
                    _lastBodyBaseline = null;
                }

                // Record where each line in this chunk finished. The line "slot"
                // baseline reported is one line-height below the
                // band top per line (curY is the band top for this chunk); the X is
                // the left margin plus the line's width including its trailing space.
                if (lineTrace is not null)
                {
                    for (var j = 0; j < chunkSize && idx + j < lineTrace.Count; j++)
                    {
                        var t = lineTrace[idx + j];
                        LogLine(_currentSlot, t.content, CurLeft + t.width,
                            _curY - lineHeight * (j + 1), t.reason);
                    }
                }

                _curY -= lineHeight * chunkSize;
                idx += chunkSize;
                if (idx < allLines.Count)
                    FlowToNextRegion();
            }
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            // Record how far down the body reached on this slot. In column mode the
            // footnote sits below the deepest column, so use _colDeepestY (the bottom
            // of the fullest column), not _curY (which may be near the top of a later,
            // shorter column).
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);

            // A FootNote on the fragment: emit its superscript reference marker
            // right after the last laid-out glyph and queue the note body for the
            // page-bottom band on this slot.
            if (tf.FootNote is { } note)
            {
                var marker = NextFootnoteMarker(note);
                var lastLine = allLines.Count > 0 ? allLines[^1] : string.Empty;
                var markerMeasurer = Text.TextPaginator.CreateMeasurer(baseFont, fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var markerBaseline = _lastBodyBaseline ?? nonEmbeddedLastBaseline;
                if (markerBaseline.HasValue && marker.Length > 0)
                {
                    var markerSize = fontSize * MarkerSizeRatio;
                    var markerState = new Text.TextState
                    {
                        Font = tf.TextState.Font,
                        FontData = tf.TextState.FontData,
                        ForegroundColor = note.TextState?.ForegroundColor,
                    };
                    _pendingEmbeddedRenders.Add((_currentSlot,
                        CurLeft + markerMeasurer(lastLine), 0, marker, markerState, markerSize,
                        markerBaseline.Value
                        + MarkerBaselineRise(baseFont, fontSize, lineHeight, markerSize)));
                }
                QueueMarkedFootnote(note, marker, fontSize);
            }

            if (fragHyperlink is not null && allLines.Count > 0)
                _pendingLinks.Add((fragSlot,
                    new Rectangle(CurLeft, fragTop - lineHeight, CurLeft + contentWidth, fragTop),
                    fragHyperlink));

            if (segHyperlinks is { Count: > 0 })
            {
                // Locate each wrapped line's character span within the fragment text so a
                // segment range [a,b) can be split across the lines it covers (wrapping
                // drops the break space, so lines are matched sequentially by content).
                var lineStart = new int[allLines.Count];
                var lineEnd = new int[allLines.Count];
                int scan = 0;
                for (int li = 0; li < allLines.Count; li++)
                {
                    var ln = allLines[li];
                    int at = ln.Length == 0 ? scan : rawText.IndexOf(ln, Math.Min(scan, rawText.Length), StringComparison.Ordinal);
                    if (at < 0) at = scan;
                    lineStart[li] = at;
                    lineEnd[li] = at + ln.Length;
                    scan = lineEnd[li];
                }
                foreach (var (a, b, h) in segHyperlinks)
                {
                    for (int li = 0; li < allLines.Count; li++)
                    {
                        var ln = allLines[li];
                        int ov0 = Math.Max(a, lineStart[li]);
                        int ov1 = Math.Min(b, lineEnd[li]);
                        if (ov1 <= ov0) continue;
                        var prefix = ln.Substring(0, ov0 - lineStart[li]);
                        var run = ln.Substring(ov0 - lineStart[li], ov1 - ov0);
                        var x0 = CurLeft + MeasureText(prefix, baseFont, fontSize);
                        var w = MeasureText(run, baseFont, fontSize);
                        var yTop = fragTop - lineHeight * li;
                        _pendingLinks.Add((fragSlot,
                            new Rectangle(x0, yTop - lineHeight, x0 + w, yTop), h));
                    }
                }
            }
            return true;
        }

        /// <summary>Compute the per-line vertical advance for
        /// <see cref="Text.TextFormattingOptions.LineSpacingMode.FullSize"/>.
        /// FullSize means the embedded font's full vertical extent (ascent
        /// minus descent, since descent is negative) scaled to the requested
        /// font size, so multi-script content with tall ascent glyphs (CJK
        /// fonts, Arial Unicode MS) advances by the right amount per line
        /// instead of the 1.2x-of-font-size default. Falls back to 1.2x if
        /// the TTF metrics can't be parsed.</summary>
        private static double ComputeFullSizeLineHeight(byte[] ttf, double fontSize)
        {
            try
            {
                // The full-size line pitch is the font's own recommended line
                // height (hhea ascender/descender/lineGap, or the OS/2 win
                // metrics), not the typographic ascent/descent used for the PDF
                // font descriptor. For fonts where the two differ -- e.g. CJK
                // faces whose typo metrics span only 1 em but whose line height
                // is taller -- the descriptor values understate the leading.
                var lineEm = Text.FontRepository.ReadTtfLineHeightEm(ttf);
                if (lineEm > 0) return lineEm * fontSize;
                var (ascent, descent, _, _) = Text.FontRepository.ReadTtfMetrics(ttf);
                if (ascent <= 0) return fontSize * 1.2;
                // ascent is positive; descent is negative. Total vertical
                // extent in 1/1000 em -> scale to points.
                var height = (ascent - descent) / 1000.0 * fontSize;
                return height > 0 ? height : fontSize * 1.2;
            }
            catch
            {
                return fontSize * 1.2;
            }
        }

        /// <summary>Measure the rendered width of <paramref name="text"/> in points
        /// using the same Standard-14 metrics that <see cref="Text.TextPaginator"/>
        /// uses for line-break calculations -- keeping the two in sync means
        /// per-segment link rectangles align with the wrap breakpoints that
        /// produced the rendered line.</summary>
        /// <summary>Queue one absolutely-placed text run on the current region — the
        /// SVG <c>&lt;text&gt;</c> path, whose glyphs are positioned by the SVG's own
        /// transform rather than by the line flow.</summary>
        public void WriteAbsoluteText(double x, double baselineY, string text,
            double fontSize, Text.Font? font)
        {
            if (string.IsNullOrEmpty(text)) return;
            _pendingEmbeddedRenders.Add((_currentSlot, x, baselineY, text,
                new Text.TextState { Font = font }, fontSize, baselineY));
        }

        private static double MeasureText(string text, string fontName, double fontSize)
        {
            double w = 0;
            foreach (var c in text)
            {
                var glyph = c < 256 ? c : '?';
                var cw = Text.Standard14Fonts.GetWidth(fontName, glyph);
                if (cw < 0) cw = 500;
                w += cw * fontSize / 1000.0;
            }
            return w;
        }

        private void WriteContent(byte[] content)
        {
            if (_overflowBuffer is null)
                _startPage.AddContentStream(content);
            else
                _overflowBuffer.Add(content);
        }

        /// <summary>Write a fragment's content honouring its marked-content
        /// tagging: a tagged fragment routes through the page's BDC/EMC wrapper
        /// (which merges directly consecutive same-tag/-MCID runs into one
        /// block). Overflow-buffered content keeps the plain path — the buffer
        /// is raw byte concatenation with no merge point.</summary>
        private void WriteContent(byte[] content, Text.TextState state)
        {
            if (_overflowBuffer is null && state.MarkedContentTag is { } tag)
                _startPage.AddMarkedContentStream(content, tag, state.MarkedContentMcid);
            else
                WriteContent(content);
        }

        /// <summary>Bottom limit (y-up) for body content on the current slot:
        /// the page's bottom margin, or the top of a reserved footnote band
        /// when one occupies this page.</summary>
        private double EffectiveBottom => Math.Max(_marginBottom,
            _slotBottomLimit.TryGetValue(_currentSlot, out var l) ? l : double.MinValue);

        private void EnsureRoom(double lineHeight)
        {
            if (_curY - lineHeight < EffectiveBottom)
                FlowToNextRegion();
        }

        private void StartNewPage(bool flushEmpty = false)
        {
            _lastBodyBaseline = null;
            // Flush the previous overflow page (if any) so each overflow-queue entry
            // corresponds to exactly one new Page — otherwise all overflow content
            // for this flow would collapse onto a single page. flushEmpty additionally
            // commits an empty buffer as a blank page (an explicit page break past a
            // paragraph that rendered nothing still occupies its own page); a null
            // buffer means we're still on the start page, so there's nothing to flush.
            if (_overflowBuffer is not null && (flushEmpty || _overflowBuffer.Count > 0))
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
            }
            _overflowBuffer = new List<byte[]>();
            // The new buffer will flush into the next available slot index in
            // _overflowPages -- record now so pending links / paragraph positions
            // logged before the flush still resolve to the right Page later.
            _currentSlot = _overflowPages.Count;
            _curY = _startPageHeight - _marginTop;
            // A fresh page restarts the column band at column 0 from the top.
            if (_colLefts is not null)
            {
                _curCol = 0;
                _colBandTop = _curY;
                _colDeepestY = _curY;
            }
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

        private static byte[] BuildWrappedTextStream(List<string> lines, string fontResName, double fontSize,
            double startX, double startY, double lineHeight, Color? foreground,
            bool strikeOut = false, bool underline = false, string? fontName = null,
            string? alphaGsName = null, double firstLineIndent = 0,
            double subsequentLinesIndent = 0, bool chunkStartsParagraph = true,
            Color? background = null, string? bgAlphaGsName = null,
            double rotation = 0)
        {
            // The left indent of this chunk's first rendered line: the paragraph's
            // own first line uses FirstLineIndent; a chunk that continues the
            // paragraph onto a new page is all "subsequent" lines.
            var firstIndent = chunkStartsParagraph ? firstLineIndent : subsequentLinesIndent;
            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            if (alphaGsName is not null)
                b.SetExtGState(alphaGsName);
            if (foreground is not null)
                b.SetFillColor(foreground.R / 255.0, foreground.G / 255.0, foreground.B / 255.0);
            // startY is the top of the text band. Drop the first baseline by the
            // font ascent (cap height) so the glyph tops align with the top margin
            // — the standard first-line placement. Subsequent lines advance
            // by lineHeight, so the whole block shifts down uniformly.
            var capHeight = fontName is not null ? Text.Standard14Fonts.GetCapHeight(fontName) : 0;
            var ascent = capHeight > 0 ? capHeight / 1000.0 * fontSize : fontSize * 0.7;
            var firstBaseline = startY - ascent;

            // Background highlight: a filled rectangle behind each wrapped line,
            // sized to the line's measured width and the font's em box (baseline +
            // descent up by one font size). Drawn before the glyphs, in its own
            // graphics state so the background's /ca alpha doesn't bleed into the
            // foreground fill that follows.
            if (background is { } bgcol)
            {
                var bgFontName = fontName ?? "Helvetica";
                var descentPt = Text.Standard14Fonts.GetDescent(bgFontName) / 1000.0 * fontSize; // negative
                b.SaveState();
                if (bgAlphaGsName is not null) b.SetExtGState(bgAlphaGsName);
                b.SetFillColor(bgcol.R / 255.0, bgcol.G / 255.0, bgcol.B / 255.0);
                for (var i = 0; i < lines.Count; i++)
                {
                    var lineW = MeasureLineWidth(lines[i], bgFontName, fontSize);
                    if (lineW <= 0) continue;
                    var lineY = firstBaseline - i * lineHeight;
                    var lineX = startX + (i == 0 ? firstIndent : subsequentLinesIndent);
                    b.Rectangle(lineX, lineY + descentPt, lineW, fontSize);
                    b.Fill();
                }
                b.RestoreState();
            }

            b.BeginText();
            b.SetFont(fontResName, fontSize);
            b.SetLeading(lineHeight);
            // The first line starts indented by firstLineIndent; line 2 shifts back
            // to startX via a relative Td (Td is relative to the current line start,
            // so a plain T* would otherwise carry the indent down to every line).
            // TextState.Rotation rotates the whole block around its first baseline
            // origin via the text matrix (Td/T* then advance in rotated text space).
            if (rotation != 0)
            {
                var rad = rotation * Math.PI / 180.0;
                var cos = Math.Round(Math.Cos(rad), 10);
                var sin = Math.Round(Math.Sin(rad), 10);
                b.SetTextMatrix(cos, sin, -sin, cos, startX + firstIndent, firstBaseline);
            }
            else
            {
                b.MoveTextPosition(startX + firstIndent, firstBaseline);
            }
            for (var i = 0; i < lines.Count; i++)
            {
                if (i == 1)
                {
                    // Shift from the first line's indent to the subsequent-lines
                    // indent (Td is relative to the current line start), then drop a
                    // line. Following lines keep that X via NextLine (T*).
                    var delta = subsequentLinesIndent - firstIndent;
                    if (delta != 0) b.MoveTextPosition(delta, -lineHeight);
                    else b.NextLine();
                }
                else if (i > 0) b.NextLine();
                b.ShowText(lines[i]);
            }
            b.EndText();

            // Emit strikeout / underline rectangles after the text. One per
            // wrapped line, sized to the line's measured width.
            if ((strikeOut || underline) && (fontName is not null))
            {
                double thickness = fontSize * 0.05;
                double soOffset = fontSize * 0.30;   // ~30% of em above baseline
                double ulOffset = -fontSize * 0.077; // ~7.7% below baseline
                for (var i = 0; i < lines.Count; i++)
                {
                    double lineW = MeasureLineWidth(lines[i], fontName, fontSize);
                    double lineY = firstBaseline - i * lineHeight;
                    double lineX = startX + (i == 0 ? firstIndent : subsequentLinesIndent);
                    if (strikeOut)
                    {
                        b.Rectangle(lineX, lineY + soOffset, lineW, thickness);
                        b.Fill();
                    }
                    if (underline)
                    {
                        b.Rectangle(lineX, lineY + ulOffset, lineW, thickness);
                        b.Fill();
                    }
                }
            }

            b.RestoreState();
            return b.Build();
        }

        private static double MeasureLineWidth(string line, string fontName, double fontSize)
        {
            if (Text.Standard14Fonts.IsStandard14(fontName))
            {
                double w = 0;
                foreach (var ch in line)
                {
                    var cw = Text.Standard14Fonts.GetWidth(fontName, ch < 256 ? ch : '?');
                    w += (cw >= 0 ? cw : 500) * fontSize / 1000.0;
                }
                return w;
            }
            return line.Length * fontSize * 0.5;
        }
    }
}
