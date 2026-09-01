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

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>Reference-marker label for a note: its custom Text when set,
        /// otherwise the next sequential footnote number.</summary>
        public string NextFootnoteMarker(Note note) =>
            note.Text ?? (_currentParaOrdinal > 0 ? _currentParaOrdinal : ++_footnoteAutoNumber)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            var lineTop = baseline - DescentNorm("Helvetica") * parentSize + parentSize;
            _noteMarkLine[note] = (_currentSlot, lineTop);
            QueueNoteLink(note, x, lineTop,
                Text.TextPaginator.CreateMeasurer("Helvetica", markerSize, null)(marker), markerSize);
        }

        /// <summary>The x a note Link points at: the right content edge — the footer
        /// band's right margin when the page carries a footer with one, else the
        /// page's right margin.</summary>
        private double NoteLinkDestX()
        {
            var footer = _startPage.Footer;
            var right = footer?.Margin is { RightTouched: true } fm ? fm.Right : _marginRight;
            return _startPage.Width - right;
        }

        private readonly record struct BandGeometry(double Left, double Width, double Right,
            double AnchorBottom, double CapacityBottom, double MarginTop, bool HasFooter,
            double ContinuationBottom = 0);

        /// <summary>Where a band of <paramref name="bandHeight"/> stands on this flow's
        /// pages: its left edge and width, the bottom a whole band is anchored on,
        /// the floor a band the page cannot take whole stands on, and the footer's
        /// top margin (the gap between the band and its rule).</summary>
        private BandGeometry BandGeometryFor(double bandHeight)
        {
            var pageW = _startPage.Width;
            var footer = _startPage.Footer;
            if (footer is not null && (footer.Paragraphs.Count > 0 || !string.IsNullOrEmpty(footer.Text)))
            {
                var m = footer.Margin;
                var mL = m.LeftTouched ? m.Left : _marginLeft;
                var mR = m.RightTouched ? m.Right : _marginRight;
                var mT = m.TopTouched ? m.Top : 0;
                var mB = m.BottomTouched ? m.Bottom : 20;
                // A page's own band stands on the footer's lines plus twice each
                // line's leading (22 for a 12 pt footer at 10; 59 for 9 pt + 10
                // leading at 30; 50 for an unleaded empty 10 pt line over a 10 pt
                // page number at 30); a continuation page's band, holding only
                // another page's spill, stands on the footer's text lines alone
                // (39 for that 9 pt footer).
                var (textH, leading) = FooterLines(footer);
                var textTop = mB + textH;
                var homeBottom = textTop + 2 * leading;
                return new BandGeometry(mL, pageW - mR - mL, pageW - mR, homeBottom, homeBottom, mT, true, textTop);
            }
            return new BandGeometry(_marginLeft, pageW - _marginLeft - _marginRight, pageW - _marginRight,
                Math.Max(0, _marginBottom - bandHeight), 0, 0, false, 0);
        }

        private static double BandHeight(IEnumerable<BandLine> lines)
        {
            double h = 0;
            foreach (var l in lines) h += l.Pitch + l.MarginTop + l.MarginBottom;
            return h;
        }

        /// <summary>Lay a note into band lines at the band width: its mark leads the
        /// first paragraph; a paragraph with IsInLineParagraph joins the one before
        /// it; each line's pitch is its text size plus the note paragraph's leading
        /// (a line holding only the mark is as tall as the mark).</summary>
        private List<BandLine> LayoutNoteLines(NoteEntry e, BandGeometry g)
        {
            var lines = new List<BandLine>();
            var groups = new List<List<BaseParagraph>>();
            // An empty XML fragment in a note lays nothing — except the note's
            // last paragraph, which closes the note with one empty line of its
            // own size (a headnote document's notes stand one default line apart;
            // an empty fragment between two note paragraphs adds nothing).
            Text.TextFragment? closing = null;
            var paras = e.Note.Paragraphs;
            // The rule under a note runs the whole band only when the note OPENS
            // with a caller-built TextFragment; a note that opens with a picture or
            // a table is ruled to its widest line (probed 2026-08-26).
            var noteHead = paras.Count > 0 ? paras[0] : null;
            for (var pi = 0; pi < paras.Count; pi++)
            {
                if (paras[pi] is Text.TextFragment tf)
                {
                    if (tf.XmlEmptyShell)
                    {
                        if (pi == paras.Count - 1) closing = tf;
                        continue;
                    }
                    if (tf.IsInLineParagraph && groups.Count > 0) groups[^1].Add(tf);
                    else groups.Add(new List<BaseParagraph> { tf });
                    continue;
                }
                // A picture opens a line of its own unless it is inline-joined; a
                // table is always a block of its own.
                if (paras[pi] is Image noteImg)
                {
                    if (noteImg.IsInLineParagraph && groups.Count > 0) groups[^1].Add(noteImg);
                    else groups.Add(new List<BaseParagraph> { noteImg });
                    continue;
                }
                if (paras[pi] is Table noteTbl) groups.Add(new List<BaseParagraph> { noteTbl });
            }
            if (groups.Count == 0 && e.Marker.Length > 0) groups.Add(new List<BaseParagraph>());
            for (var gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                var head = group.Count > 0 ? group[0] : null;
                var runs = new List<StyledRun>();
                if (gi == 0 && e.Marker.Length > 0)
                    runs.Add(new StyledRun
                    {
                        Text = e.Marker, Size = e.ParentSize, NoteMark = true, Sup = true, Note = e.Note,
                        State = new Text.TextState { ForegroundColor = e.Note.TextState?.ForegroundColor },
                    });
                double groupLs = 0, groupFs = 0;
                // A table paragraph is the whole line: it renders as a grid from the
                // band cursor (indented past the mark when it opens the note) and the
                // line is as tall as the grid.
                if (group.Count == 1 && group[0] is Table bandTable)
                {
                    var markW = 0.0;
                    if (runs.Count > 0 && runs[0].NoteMark)
                        markW = MeasureStyled(runs[0].Text, runs[0], StyledRunSize(runs[0]));
                    var tblLeft = g.Left + markW;
                    bandTable.FlowLeftOffset = tblLeft;
                    bandTable.BuildMultiPage(_startPage, _startPageHeight - _marginTop,
                        0, 0, measureOnly: true);
                    var tblH = bandTable.LastRenderedHeight;
                    var tblLine = new BandLine
                    {
                        Pitch = tblH, TextHeight = 0, HasText = false,
                        NaturalWidth = markW, Note = e.Note,
                        NoteFirst = gi == 0, ParaFirst = true, LastOfParagraph = true,
                        Align = HorizontalAlignment.Left, Left = 0,
                        TableBlock = bandTable, TableLeft = tblLeft,
                    };
                    foreach (var r in runs) tblLine.Cells.Add((0, r.Text, r, StyledRunSize(r)));
                    lines.Add(tblLine);
                    continue;
                }
                foreach (var member in group)
                {
                    if (member is Image gImg)
                    {
                        // A picture in a note band: laid at the band cursor after
                        // whatever precedes it on the line, its own size (the flow
                        // image rule), and giving the line no height of its own
                        // while the line carries text.
                        if (LoadFlowImage(gImg, g.Width, 0, out var giData, out var giW, out var giH))
                            runs.Add(new StyledRun { ImageData = giData, ImageW = giW, ImageH = giH });
                        continue;
                    }
                    if (member is not Text.TextFragment tf) continue;
                    var parent = tf.TextState;
                    var pfs = parent.FontSizeTouched ? (double)parent.FontSize : 0;
                    if (parent.LineSpacing > groupLs) groupLs = parent.LineSpacing;
                    foreach (var seg in tf.Segments)
                    {
                        var st = seg.TextState;
                        if (st.LineSpacing > groupLs) groupLs = st.LineSpacing;
                        if (string.IsNullOrEmpty(seg.Text)) continue;
                        var size = st.FontSizeTouched ? (double)st.FontSize : pfs > 0 ? pfs : 10;
                        if (size > groupFs) groupFs = size;
                        var merged = new Text.TextState
                        {
                            ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                            Underline = st.Underline || parent.Underline,
                            IsBold = st.IsBold || parent.IsBold,
                            IsItalic = st.IsItalic || parent.IsItalic,
                        };
                        var font = st.Font?.SourceFontData is not null ? st.Font
                            : parent.Font?.SourceFontData is not null ? parent.Font : null;
                        if (font is not null) merged.Font = font;
                        if ((st.FontData ?? parent.FontData) is { } fd) merged.FontData = fd;
                        var name = st.FontName ?? parent.FontName;
                        if (!string.IsNullOrEmpty(name) && font is null) merged.FontName = name;
                        runs.Add(new StyledRun
                        {
                            Text = seg.Text, Size = size, State = merged, Sup = st.Superscript,
                            Link = seg.Hyperlink ?? tf.HyperlinkValue,
                        });
                    }
                }
                var headTf = head as Text.TextFragment;
                var align = headTf is null ? HorizontalAlignment.Left
                    : headTf.HorizontalAlignment != HorizontalAlignment.Left ? headTf.HorizontalAlignment
                    : headTf.TextState.HorizontalAlignment;
                // ⚠ TextFragment SHADOWS BaseParagraph.Margin with `new`, so reading it
                // through the base-typed head returns the fragment's UNSET base object
                // and a margined note paragraph loses its box.
                var hm = headTf is not null ? headTf.Margin : head?.Margin;
                double mTop = hm?.Top ?? 0, mBottom = hm?.Bottom ?? 0, mLeft = hm?.Left ?? 0, mRight = hm?.Right ?? 0;
                var paintFromRule = mTop != 0 || mBottom != 0 || mLeft != 0 || mRight != 0;
                var fullWidth = noteHead is Text.TextFragment { AutoNoteText: false };
                var laid = LayoutStyledLines(runs, Math.Max(1, g.Width - mLeft - mRight));
                for (var li = 0; li < laid.Count; li++)
                {
                    var (left, cells) = laid[li];
                    double maxBase = 0, markSize = 0, width = 0, maxImage = 0;
                    foreach (var (x, text, r) in cells)
                    {
                        var sz = StyledRunSize(r);
                        if (r.ImageData is not null)
                        {
                            maxImage = Math.Max(maxImage, r.ImageH);
                            width = Math.Max(width, x + r.ImageW);
                            continue;
                        }
                        if (r.NoteMark) markSize = Math.Max(markSize, sz);
                        else if (!r.Sup) maxBase = Math.Max(maxBase, r.Size);
                        if (text.Length > 0) width = Math.Max(width, x + MeasureStyled(text, r, sz));
                    }
                    var hasText = maxBase > 0;
                    var h = hasText ? maxBase
                        : maxImage > 0 ? maxImage
                        : li == 0 && groupFs > 0 ? groupFs : markSize > 0 ? markSize : 10;
                    var bl = new BandLine
                    {
                        Pitch = hasText ? h + groupLs : h, TextHeight = hasText ? h : 0, HasText = hasText,
                        NaturalWidth = width, Note = e.Note,
                        NoteFirst = gi == 0 && li == 0, ParaFirst = li == 0, LastOfParagraph = li == laid.Count - 1,
                        Align = align, Left = mLeft + left, FullWidthBox = fullWidth,
                        MarginTop = li == 0 ? mTop : 0, MarginBottom = li == laid.Count - 1 ? mBottom : 0,
                        ParaMarginTop = mTop, PaintFromRule = paintFromRule,
                    };
                    foreach (var (x, text, r) in cells) bl.Cells.Add((x, text, r, StyledRunSize(r)));
                    lines.Add(bl);
                }
            }
            if (closing is not null)
                lines.Add(new BandLine
                {
                    Pitch = closing.TextState.FontSizeTouched ? closing.TextState.FontSize : XmlDefaultFontSize,
                    Note = e.Note, ParaFirst = true, LastOfParagraph = true, Align = HorizontalAlignment.Left,
                });
            return lines;
        }

        private List<NoteEntry> NotesOnRel(int rel)
        {
            var list = new List<NoteEntry>();
            foreach (var n in _notes)
                if (n.Slot != EndNoteSlot && n.Slot + 1 == rel)
                {
                    n.Lines ??= LayoutNoteLines(n, BandGeometryFor(0));
                    list.Add(n);
                }
            return list;
        }

        private bool HasNotesBeyond(int rel)
        {
            foreach (var n in _notes) if (n.Slot == EndNoteSlot || n.Slot + 1 > rel) return true;
            return false;
        }

        /// <summary>Install a dissolved box's band plan: each planned page (relative
        /// to the current slot) gets its band lines, band top and the body limit
        /// above the band (the rule sits one point over that).</summary>
        internal void ApplyDissolvedPlan(DissolvedBandPlan plan)
        {
            var baseSlot = _currentSlot;
            foreach (var (rel, (lines, top)) in plan.Slots)
            {
                var slot = baseSlot + rel;
                _bandPlan[slot] = lines;
                _bandPlanTop[slot] = top;
                var g = BandGeometryFor(BandHeight(lines));
                var limit = top + g.MarginTop;
                _slotBottomLimit[slot] = Math.Max(
                    _slotBottomLimit.TryGetValue(slot, out var prev) ? prev : double.MinValue, limit);
            }
            if (plan.CloseAfter is { } ca) _closeAfter = (baseSlot + ca.rel, ca.child);
            Trace($"apply plan base={baseSlot} slots=[{string.Join(",", plan.Slots.Select(kv => kv.Key + ":" + kv.Value.lines.Count + "@" + kv.Value.top.ToString("F1")))}] close={plan.CloseAfter}");
        }

        /// <summary>Plan a dissolved FloatingBox's note bands page by page.
        /// <paramref name="runDry"/> lays the box into a fresh dry flow under a
        /// candidate plan and returns that flow. On each page the band takes the
        /// most lines it can: a band holding every line of the notes marked on the
        /// page stands on its anchor; one that cannot stands on the page's floor
        /// (the footer box, or the page bottom) and the page closes after the child
        /// that carried the last mark it holds. The body re-wraps above the band
        /// each time, so a candidate is kept only when the marks it was sized for
        /// still land on its page and at least one body line stays above it.</summary>
        internal DissolvedBandPlan PlanDissolvedBands(Func<DissolvedBandPlan, FlowLayout> runDry)
        {
            var plan = new DissolvedBandPlan();
            var spill = new List<BandLine>();
            for (var rel = 0; rel < 64; rel++)
            {
                var dry = runDry(plan);
                var here = dry.NotesOnRel(rel);
                if (here.Count == 0 && spill.Count == 0)
                {
                    if (!dry.HasNotesBeyond(rel)) break;
                    continue;
                }
                var all = new List<BandLine>(spill);
                foreach (var n in here) all.AddRange(n.Lines!);
                // One body line (the pitch the body was last laid on) stays above
                // the band on every page.
                var topLimit = dry._startPageHeight - dry._marginTop - dry._lastTextLinePitch;

                var chosenN = -1; double chosenTop = 0; var prefix = here.Count;
                for (var N = all.Count; N >= 1 && chosenN < 0; N--)
                {
                    var lines = all.GetRange(0, N);
                    var H = BandHeight(lines);
                    var g = dry.BandGeometryFor(H);
                    var capped = N < all.Count;
                    var t = (capped ? g.CapacityBottom : g.AnchorBottom) + H;
                    if (t > topLimit + 0.01) continue;
                    plan.Slots[rel] = (lines, t);
                    var d2 = runDry(plan);
                    var here2 = d2.NotesOnRel(rel);
                    if (here2.Count > here.Count) continue;
                    var ok = true;
                    var ownedLines = spill.Count;
                    for (var i = 0; i < here2.Count && ok; i++)
                    {
                        if (!ReferenceEquals(here2[i].Note, here[i].Note)) ok = false;
                        else ownedLines += here[i].Lines!.Count;
                    }
                    if (!ok || N > ownedLines) continue;
                    chosenN = N; chosenTop = t; prefix = here2.Count;
                }
                if (chosenN < 0)
                {
                    // Not even one line fits above the body: the page keeps no band
                    // and everything spills.
                    plan.Slots[rel] = (new List<BandLine>(), dry.BandGeometryFor(0).AnchorBottom);
                    if (here.Count > 0) plan.CloseAfter = (rel, here[^1].ChildIndex);
                    spill = all;
                    continue;
                }
                plan.Slots[rel] = (all.GetRange(0, chosenN), chosenTop);
                var owned = new List<BandLine>(spill);
                for (var i = 0; i < prefix; i++) owned.AddRange(here[i].Lines!);
                spill = owned.Count > chosenN ? owned.GetRange(chosenN, owned.Count - chosenN) : new List<BandLine>();
                if (spill.Count > 0 && prefix > 0)
                    plan.CloseAfter = (rel, here[prefix - 1].ChildIndex);
                if (spill.Count == 0 && prefix == here.Count && !dry.HasNotesBeyond(rel)) break;
            }
            return plan;
        }

        /// <summary>Lay every queued note into its page's band and queue the band's
        /// text, rules and links. Unplanned pages take all the lines of the notes
        /// marked on them (end notes collect on the flow's last page); a band taller
        /// than the page spills its tail to the next page. Continuation pages the
        /// body never reached are materialised as blank pages.</summary>
        public void FinaliseNoteBands()
        {
            if (_notes.Count == 0 && _bandPlan.Count == 0) return;
            var lastSlot = _currentSlot;
            var bySlot = new SortedDictionary<int, List<BandLine>>();
            foreach (var (slot, lines) in _bandPlan) bySlot[slot] = new List<BandLine>(lines);
            foreach (var e in _notes)
            {
                var slot = e.Slot == EndNoteSlot ? lastSlot : e.Slot;
                if (_bandPlan.ContainsKey(slot)) continue;
                e.Lines ??= LayoutNoteLines(e, BandGeometryFor(0));
                if (!bySlot.TryGetValue(slot, out var list)) bySlot[slot] = list = new List<BandLine>();
                list.AddRange(e.Lines);
            }
            // An unplanned band that would rise past the top margin spills its tail.
            var pageTop = _startPageHeight - _marginTop;
            var queue = new List<int>(bySlot.Keys);
            for (var qi = 0; qi < queue.Count; qi++)
            {
                var slot = queue[qi];
                if (_bandPlan.ContainsKey(slot)) continue;
                var lines = bySlot[slot];
                while (lines.Count > 1)
                {
                    var H = BandHeight(lines);
                    if (BandGeometryFor(H).AnchorBottom + H <= pageTop) break;
                    var last = lines[^1];
                    lines.RemoveAt(lines.Count - 1);
                    if (!bySlot.TryGetValue(slot + 1, out var next))
                    {
                        bySlot[slot + 1] = next = new List<BandLine>();
                        queue.Add(slot + 1);
                    }
                    next.Insert(0, last);
                }
            }
            var maxSlot = int.MinValue;
            foreach (var kv in bySlot) if (kv.Value.Count > 0 && kv.Key > maxSlot) maxSlot = kv.Key;
            while (maxSlot >= _overflowPages.Count)
                _overflowPages.Add((System.Array.Empty<byte>(), _startPage.Width, _startPageHeight));
            var prevHadBody = true;
            Trace($"finalise slots=[{string.Join(",", bySlot.Keys)}] bodies=[{string.Join(",", _slotBottomY.Keys)}] planned=[{string.Join(",", _bandPlanTop.Keys)}] overflow={_overflowPages.Count}");
            foreach (var (slot, lines) in bySlot)
            {
                Trace($"band slot={slot} lines={lines.Count} hasBody={SlotHasBody(slot)} planned={(_bandPlanTop.TryGetValue(slot, out var pt) ? pt.ToString("F1") : "-")} bodyBottom={(_slotBottomY.TryGetValue(slot, out var bb) ? bb.ToString("F1") : "-")}");
                if (lines.Count > 0)
                    DrawBand(slot, lines, _bandPlanTop.TryGetValue(slot, out var t) ? t : null, prevHadBody);
                prevHadBody = SlotHasBody(slot);
            }
        }

        /// <summary>Draw one page's band. A page with body text keeps the band at
        /// its planned / anchored top under a rule. A page the body never reached
        /// (a note's continuation) stands its band on the footer when it has one;
        /// without a footer the band fills a grid from one body line below the top
        /// margin, ruled only on the first such page after the body.</summary>
        private void DrawBand(int slot, List<BandLine> lines, double? plannedTop, bool prevHadBody)
        {
            var H = BandHeight(lines);
            var g = BandGeometryFor(H);
            var hasBody = SlotHasBody(slot);
            double top;
            if (hasBody) top = plannedTop ?? g.AnchorBottom + H;
            else if (g.HasFooter) top = g.ContinuationBottom + H;
            else top = _startPageHeight - _marginTop - _lastTextLinePitch;
            var ruleY = top + g.MarginTop + 1;
            Trace($"draw slot={slot} H={H:F1} top={top:F1} hasBody={hasBody} footer={g.HasFooter} anchor={g.AnchorBottom:F1} prevHadBody={prevHadBody}");
            if (hasBody || (!g.HasFooter && prevHadBody))
            {
                double maxW = 0; var full = false;
                foreach (var l in lines)
                {
                    maxW = Math.Max(maxW, l.Left + l.NaturalWidth);
                    full |= l.FullWidthBox;
                }
                var ruleRight = full ? g.Right : Math.Min(g.Left + maxW, g.Right);
                EmitNoteRule(slot, ruleY, _marginLeft, ruleRight);
            }
            // A margined paragraph is clipped to its box — which is placed
            // the rule gap plus the bottom margin above the paragraph's
            // natural slot, so its text (painted higher still) shows only what
            // reaches into the box. Pre-walk the cursor for each such box.
            var clipBoxes = new Dictionary<int, Rectangle>();
            {
                var Tw = top; var paraStart = -1; double paraBoxH = 0, paraLeft = 0;
                for (var li = 0; li < lines.Count; li++)
                {
                    var ln = lines[li];
                    if (ln.ParaFirst)
                    {
                        paraStart = li; paraBoxH = 0; paraLeft = ln.Left;
                        if (ln.NoteFirst)
                            foreach (var (cx, ct, cr, cs) in ln.Cells)
                                if (cr.NoteMark && ct.Length > 0) paraLeft = Math.Max(paraLeft, ln.Left + cx + MeasureStyled(ct, cr, cs));
                    }
                    Tw -= ln.MarginTop;
                    Tw -= ln.Pitch;
                    paraBoxH += ln.Pitch + HighlightBoxExtraEm * ln.TextHeight;
                    if (ln.LastOfParagraph)
                    {
                        if (ln.PaintFromRule)
                        {
                            var shift = 1 + ln.MarginBottom;
                            var box = new Rectangle(g.Left + paraLeft, Tw + shift, g.Right, Tw + shift + paraBoxH);
                            for (var k = paraStart; k <= li; k++) clipBoxes[k] = box;
                        }
                        Tw -= ln.MarginBottom;
                    }
                }
            }
            var T = top;
            double paraOffset = 0;
            for (var lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var line = lines[lineIdx];
                clipBoxes.TryGetValue(lineIdx, out var lineClip);
                if (line.ParaFirst) paraOffset = 0;
                if (line.NoteFirst) RecordNoteBandTop(line.Note, slot, T);
                // The mark stands on the band cursor; the paragraph's top margin is
                // charged under it. A margined paragraph paints its text from the
                // rule down instead (the measured placement), while its lines
                // still take their room in the band.
                var markerT = T;
                T -= line.MarginTop;
                var textT = line.PaintFromRule ? ruleY + 1 + line.ParaMarginTop - paraOffset : T;
                var xs = CellXs(line, Math.Max(1, g.Width - line.Left));
                Hyperlink? runLink = null; double linkX0 = 0, linkX1 = 0;
                void FlushLink()
                {
                    if (runLink is not null && linkX1 > linkX0)
                        _pendingLinks.Add((slot, new Rectangle(linkX0, textT - line.Pitch, linkX1, textT), runLink));
                    runLink = null;
                }
                for (var ci = 0; ci < line.Cells.Count; ci++)
                {
                    var (_, text, r, size) = line.Cells[ci];
                    if (r.ImageData is not null)
                    {
                        FlushLink();
                        var ix = g.Left + line.Left + xs[ci];
                        _pendingImages.Add((slot, r.ImageData,
                            new Rectangle(ix, markerT - r.ImageH, ix + r.ImageW, markerT)));
                        continue;
                    }
                    if (text.Length == 0) continue;
                    var x = g.Left + line.Left + xs[ci];
                    if (r.NoteMark)
                    {
                        FlushLink();
                        var my = markerT - size + Std14Seat(r.State, size);
                        _pendingEmbeddedRenders.Add((slot, x, my + size, text, r.State, size, my));
                        continue;
                    }
                    var y = textT - line.Pitch + (r.Sup ? 0.33 * line.TextHeight : 0) + Std14Seat(r.State, size);
                    _pendingEmbeddedRenders.Add((slot, x, y + size, text, r.State, size, y));
                    if (lineClip is not null) _pendingRenderClip[_pendingEmbeddedRenders.Count - 1] = lineClip;
                    var w = MeasureStyled(text, r, size);
                    if (r.Link is null) { FlushLink(); continue; }
                    if (!ReferenceEquals(runLink, r.Link)) { FlushLink(); runLink = r.Link; linkX0 = x; }
                    linkX1 = x + w;
                }
                FlushLink();
                if (line.TableBlock is { } bandTbl)
                    _pendingBandTables.Add((slot, bandTbl, line.TableLeft, T));
                T -= line.Pitch;
                paraOffset += line.Pitch;
                T -= line.MarginBottom;
            }
        }

        /// <summary>The separator rule above a band, styled by the page's
        /// NoteLineStyle (default: solid black 1 pt), queued on the band's slot.</summary>
        private void EmitNoteRule(int slot, double y, double x0, double x1)
        {
            if (x1 <= x0) return;
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
            rb.MoveTo(x0, y).LineTo(x1, y).Stroke().RestoreState();
            _pendingRules.Add((slot, rb.Build()));
        }

        /// <summary>Render every table a note band carries on the page its slot
        /// became. Deferred like the rules and the images: the grid registers its
        /// faces on the page it actually lands on, which does not exist while the
        /// band is being laid out.</summary>
        public void FinaliseBandTables(IList<Page> overflowPageRefs)
        {
            foreach (var (slot, table, left, top) in _pendingBandTables)
            {
                var target = slot < 0 ? _startPage :
                    slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;
                if (target is null) continue;
                table.FlowLeftOffset = left;
                // The band hangs below the bottom margin, so the grid is given the
                // whole page to render into and never breaks inside a band.
                var contents = table.BuildMultiPage(target, top, 0);
                if (contents.Count > 0) target.AddContentStream(contents[0]);
                if (table.LastImageDraws.Count > 0)
                    foreach (var (data, rect) in table.LastImageDraws[0])
                        try { target.AddImage(data, rect); }
                        catch (ArgumentException) { }
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
                if (targetPageNumber > 0 && targetPageNumber <= _finalPageCount)
                    srcPage.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(targetPageNumber, destX, destY, 0)));
                else if (targetPageNumber > 0)
                    // The target page never materialised: the Link is still placed,
                    // with no destination, rather than pointing past the page tree.
                    srcPage.Annotations.AddBareLinkAnnotation(rect);
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

        /// <summary>Emit a Link annotation for <paramref name="hyperlink"/> directly on
        /// <paramref name="srcPage"/> (an absolutely placed block that never defers).</summary>
        public void EmitLinkNow(Page srcPage, Rectangle rect, Hyperlink hyperlink)
            => EmitLinkOn(srcPage, rect, hyperlink, slot => slot < 0 ? _startPage : null);

        /// <summary>Flush the last overflow page buffer to the shared overflow queue.</summary>
        public void Commit()
        {
            if (_overflowBuffer is { Count: > 0 })
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
                _overflowBuffer = null;
            }
        }

    }
}
