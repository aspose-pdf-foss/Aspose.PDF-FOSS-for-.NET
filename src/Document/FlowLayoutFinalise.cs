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
        /// <summary>Commit the overflow buffer in flight to its slot, so pages a
        /// table pre-builds are queued AFTER it (the table's first slice was
        /// injected into that buffer).</summary>
        internal void FlushInFlightBuffer()
        {
            if (_overflowBuffer is { Count: > 0 })
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
                _overflowBuffer = new List<byte[]>();
            }
        }

        /// <summary>Place a block image at the flow cursor: drawn at the current
        /// region's left edge, w×h points, cursor advanced below it.</summary>
        /// <remarks>Once the flow has overflowed there is no Page to draw on yet, so the
        /// placement is queued against the current slot and bound by
        /// <see cref="FinaliseImages"/> when that slot becomes a real page — the same
        /// deferral the inline images use. It used to be dropped instead, which is why a
        /// box whose pictures ran past the first page lost every one after it.</remarks>
        public void PlaceImageBlock(byte[] data, double w, double h)
        {
            EnsureRoom(h);
            var x = CurLeft;
            var rect = new Rectangle(x, _curY - h, x + w, _curY);
            if (_overflowBuffer is null) _startPage.AddImage(data, rect);
            else
            {
                _pendingImages.Add((_currentSlot, data, rect, false));
                // An overflow page is materialised from its CONTENT buffer, and a queued
                // image is not content — without a block here the buffer looks empty, the
                // slot never becomes a page, and the image binds nowhere.
                _overflowBuffer.Add(Array.Empty<byte>());
            }
            _curY -= h;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>Draw a picture a block laid at the cursor (a table cell's) on the region
        /// the flow is in: at once on the start page, queued against the slot once the flow
        /// has overflowed -- and, while the flow defers its text, in the deferred queue at its
        /// place, so a cell background written after it in the block does not cover it.</summary>
        public void PlaceBlockPicture(byte[] data, Rectangle rect)
        {
            if (_forceDeferredWrites)
            {
                _pendingRenderPictures[_pendingEmbeddedRenders.Count] = (data, rect);
                QueueDeferredPlaceholder();
                return;
            }
            if (_overflowBuffer is null) _startPage.AddImage(data, rect);
            else _pendingImages.Add((_currentSlot, data, rect, false));
        }

        /// <summary>Content a block laid at the cursor while the flow defers its text: it
        /// waits in the deferred queue at its place, so the page's stream keeps flow order.</summary>
        private void QueueDeferredContent(byte[] content)
        {
            _pendingRenderOps[_pendingEmbeddedRenders.Count] = content;
            QueueDeferredPlaceholder();
        }

        /// <summary>The queue entry a deferred block's content or picture stands behind: no
        /// text of its own, on the current slot, whose buffer it marks non-empty.</summary>
        private void QueueDeferredPlaceholder()
        {
            _pendingEmbeddedRenders.Add((_currentSlot, 0, _curY, string.Empty, new Text.TextState(), 0, null));
            if (_overflowBuffer is not null) _overflowBuffer.Add(Array.Empty<byte>());
        }

        /// <summary>Draw an image frame, and the paint that goes under it, on the
        /// region the flow is in: on the start page directly, else queued against
        /// the current slot and bound when that slot becomes a page. The caller
        /// has settled the rectangle and moves the cursor itself.</summary>
        public void PlaceImageFrame(byte[] data, Rectangle rect, byte[] paint, bool blackWhite)
        {
            if (_overflowBuffer is null)
            {
                if (paint.Length > 0) _startPage.AddContentStream(paint);
                _startPage.AddImage(data, rect, blackWhite);
                return;
            }
            // A queued image is not content; the paint (or an empty block) keeps
            // the slot's buffer non-empty so the slot becomes a page.
            _overflowBuffer.Add(paint);
            _pendingImages.Add((_currentSlot, data, rect, blackWhite));
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

        /// <summary>Reserve a field's block (its layout Width x Height, no gap
        /// after it) at the flow cursor's left edge and queue the widget for page
        /// binding once the overflow slots materialise: two stacked text boxes sit
        /// one directly under the other (measured 2026-08-23).</summary>
        public void PlaceFieldBlock(Forms.Field field, double w, double h)
        {
            if (h > 0) EnsureRoom(h);
            var x = CurLeft;
            _pendingFieldBlocks.Add((_currentSlot, field, new Rectangle(x, _curY - h, x + w, _curY)));
            _curY -= h;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>Append each queued rule/graphics op-run to its materialised
        /// page. Same slot resolution as the other finalisers.</summary>
        public void FinaliseRules(IList<Page> overflowPageRefs)
        {
            foreach (var (slot, ops) in _pendingRules)
            {
                var target = SlotPage(slot, overflowPageRefs);
                target?.AddContentStream(ops);
            }
        }

        /// <summary>Resolve every queued link annotation against the actual
        /// Page sequence and emit it. <paramref name="overflowPageRefs"/> is
        /// the list of Pages added (in order) from this layout's overflow
        /// queue; index N corresponds to slot N. Called by Document.Save once
        /// the outer loop has drained _overflowPages into Pages.</summary>
        public void FinaliseAnnotations(IList<Page> overflowPageRefs, int documentPageCount = int.MaxValue)
        {
            Page? PageOf(int slot) =>
                SlotPage(slot, overflowPageRefs);
            _finalPageCount = documentPageCount;

            foreach (var (slot, rect, hyperlink) in _pendingLinks)
            {
                var srcPage = PageOf(slot);
                if (srcPage is null) continue;
                EmitLinkOn(srcPage, rect, hyperlink, PageOf);
            }

            foreach (var (slot, rect, note) in _pendingNoteLinks)
            {
                var srcPage = PageOf(slot);
                if (srcPage is null || !_noteBandTop.TryGetValue(note, out var band)) continue;
                var destPage = PageOf(band.slot);
                if (destPage is null) continue;
                srcPage.Annotations.AddLinkAnnotation(rect,
                    new Aspose.Pdf.Annotations.GoToAction(
                        new Aspose.Pdf.Annotations.XYZExplicitDestination(destPage.Number,
                            NoteLinkDestX(), band.top, 0)));
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
            foreach (var (slot, field, rect) in _pendingFieldBlocks)
            {
                var fpg = SlotPage(slot, overflowPageRefs);
                if (fpg is null) continue;
                field.PlaceGeneratorWidget(fpg, rect);
            }
            Page? PageOf(int slot) =>
                SlotPage(slot, overflowPageRefs);

            foreach (var (slot, rect, checkedState) in _pendingFormFields)
            {
                var pg = PageOf(slot);
                if (pg is null) continue;
                var checkbox = new Aspose.Pdf.Forms.CheckboxField(pg, rect) { Checked = checkedState };
                doc.Form.Add(checkbox, pg.Number);
            }
            FinaliseInlineRadios(overflowPageRefs, doc);
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
                SlotPage(slot, overflowPageRefs);

            for (var ri = 0; ri < _pendingEmbeddedRenders.Count; ri++)
            {
                var (slot, x, y, text, textState, fontSize, baseline) = _pendingEmbeddedRenders[ri];
                var target = PageOf(slot);
                if (target is null) continue;
                // A block's content or picture that waited in the queue keeps its place in it.
                if (_pendingRenderOps.TryGetValue(ri, out var ops)) { target.AddContentStream(ops); continue; }
                if (_pendingRenderPictures.TryGetValue(ri, out var picture))
                {
                    try { target.AddImage(picture.Data, picture.Rect); }
                    catch (ArgumentException) { }
                    continue;
                }
                var sub = new Text.TextFragment(text)
                {
                    Position = new Text.Position(x, baseline ?? (y - fontSize))
                };
                if (_pendingRenderPitch.TryGetValue(ri, out var pitch))
                    sub.TextState.FlowLinePitch = pitch;
                var clipped = _pendingRenderClip.TryGetValue(ri, out var clip);
                // The reference paints a paragraph's underline BEFORE its clipped text
                // ([q rule Q] q re W n BT ... ET Q), the rule under the glyphs rather than
                // over their descenders; a test reads the rule's colour as the page's
                // second live operator. The rule goes in AFTER the text is written (its
                // extent is measured by the writer) at the slot recorded here.
                var ruleSlot = target.ContentStreamCount;
                if (clipped)
                {
                    var cb = new Content.ContentStreamBuilder();
                    cb.SaveState();
                    cb.Rectangle(clip!.LLX, clip.LLY, clip.Width, clip.Height).Clip();
                    target.AddContentStream(cb.Build());
                }
                // Copy the embedded-font state across so TextBuilder picks the
                // right embedding path (FontData / TtfData / Font.SourceFontData).
                sub.TextState.FontSize = (float)fontSize;
                sub.TextState.FontData = textState.FontData;
                sub.TextState.Font = textState.Font;
                // The Standard-14 face is carried by NAME — without it a styled
                // run (Times-Bold, Courier, …) would fall back to Helvetica.
                sub.TextState.FontName = textState.FontName;
                sub.TextState.ForegroundColor = textState.ForegroundColor;
                sub.TextState.IsBold = textState.IsBold;
                sub.TextState.IsItalic = textState.IsItalic;
                // A stroke asked for by the render mode, or by a synthetic bold pen, rides too:
                // the builder strokes a fill-then-stroke run with the pen it derives from the size.
                sub.TextState.RenderingMode = textState.FormattingOptions is { SyntheticBoldPen: > 0 }
                    && textState.RenderingMode == Text.TextRenderingMode.FillText
                    ? Text.TextRenderingMode.FillThenStrokeText : textState.RenderingMode;
                sub.TextState.StrokingColor = textState.StrokingColor;
                if (textState.FormattingOptions is { SyntheticBoldPen: > 0 } bold) sub.TextState.LineWidth = (float)bold.SyntheticBoldPen;
                else if (textState.LineWidth != 1.0) sub.TextState.LineWidth = textState.LineWidth;
                // Underline must survive the deferred hop — TextBuilder draws it
                // as a rectangle at save time from the fragment state.
                sub.TextState.Underline = textState.Underline;
                sub.TextState.IsStrikeOut = textState.IsStrikeOut;
                // Rotation too: the highlight and the glyph run both turn with it.
                sub.TextState.Rotation = textState.Rotation;
                sub.TextState.TextRise = textState.TextRise;
                sub.TextState.HorizontalScaling = textState.HorizontalScaling;
                // Carry the leading so a multi-line chunk renders on the same pitch
                // the paginator reserved (fontSize + LineSpacing), not a default 1.2x.
                sub.TextState.LineSpacing = textState.LineSpacing;
                // A synthetic slant or a skew shears the run's matrix as the flow's own writer shears it.
                if (textState.FormattingOptions is { } shear)
                {
                    sub.TextState.FormattingOptions.Slope = shear.Slope;
                    sub.TextState.FormattingOptions.Skew = shear.Skew;
                    sub.TextState.FormattingOptions.SyntheticItalicLean = shear.SyntheticItalicLean;
                }
                // The spacing a line was stretched (or set) with rides its glyphs too.
                sub.TextState.CharacterSpacing = textState.CharacterSpacing;
                sub.TextState.WordSpacing = textState.WordSpacing;
                if (_pendingRenderSpacing.TryGetValue(ri, out var spacing))
                {
                    sub.TextState.CharacterSpacing = (float)spacing.Tc;
                    sub.TextState.WordSpacing = (float)spacing.Tw;
                }
                // Carry the highlight so TextBuilder draws the per-line background rectangle
                // (the non-embedded path passes it through BuildWrappedTextStream; the embedded
                // path must copy it here or the highlight is silently dropped).
                sub.TextState.BackgroundColor = textState.BackgroundColor;
                // Marked-content tagging must survive the deferred hop: TextBuilder
                // wraps the emitted ops in BDC/EMC (merging same-tag/-MCID runs).
                sub.TextState.MarkedContentTag = textState.MarkedContentTag;
                sub.TextState.MarkedContentMcid = textState.MarkedContentMcid;
                var tb = new Text.TextBuilder(target);
                tb.AppendTextInline(sub);
                if (target.TakeUnderlineBytes(sub) is { } rule)
                    target.InsertContentStreamAt(ruleSlot, rule);
                if (clipped)
                {
                    var ce = new Content.ContentStreamBuilder();
                    ce.RestoreState();
                    target.AddContentStream(ce.Build());
                }
            }
        }

        /// <summary>Distribute the accumulated line-break notifications to the
        /// materialised Pages once the overflow drain has produced real Page
        /// objects for each slot.</summary>
        public void FinaliseNotifications(IList<Page> overflowPageRefs)
        {
            if (!_logNotifications || _notificationsBySlot.Count == 0) return;
            Page? PageOf(int slot) =>
                SlotPage(slot, overflowPageRefs);
            foreach (var kv in _notificationsBySlot)
            {
                var page = PageOf(kv.Key);
                if (page is not null) page.NotificationLog += kv.Value.ToString();
            }
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

        /// <summary>Paint every queued background box behind its own page's content.</summary>
        public void FlushBackgroundFills()
        {
            if (_pendingBackFills.Count == 0) return;
            var slots = new List<int>();
            foreach (var f in _pendingBackFills)
                if (!slots.Contains(f.Slot)) slots.Add(f.Slot);
            foreach (var slot in slots)
            {
                var cs = new Content.ContentStreamBuilder();
                foreach (var (s, rect, color) in _pendingBackFills)
                {
                    if (s != slot) continue;
                    cs.SaveState();
                    cs.SetFillColor(color);
                    cs.Rectangle(rect.LLX, rect.LLY, rect.Width, rect.Height);
                    cs.Fill();
                    cs.RestoreState();
                }
                PrependContentToSlot(slot, cs.Build());
            }
            _pendingBackFills.Clear();
        }

        /// <summary>Place every queued inline image on the page its slot became.
        /// Runs before the deferred text so overlapping glyphs paint on top.</summary>
        public void FinaliseImages(IList<Page> overflowPageRefs)
        {
            foreach (var (slot, data, rect, blackWhite) in _pendingImages)
            {
                var target = SlotPage(slot, overflowPageRefs);
                if (target is null) continue;
                try { target.AddImage(data, rect, blackWhite); }
                catch (ArgumentException) { }
            }
        }

    }
}
