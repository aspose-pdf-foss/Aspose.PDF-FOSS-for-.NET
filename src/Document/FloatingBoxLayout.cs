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
    private void LayoutFloatingBoxParagraph(FloatingBox fbox, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, PageLayoutState pl, Dictionary<int, int> headingAutoCounters, ref string? fontName, List<(byte[] content, double width, double height)> overflowPages, double marginLeft, double marginRight, double marginBottom, double marginTop)
    {
        // A box holding one multi-column article: the article paints its
        // own sized, padded background and pours its paragraphs down each
        // column in turn. The declared width and height size the CONTENT,
        // so the painted box grows by the padding on every side; columns
        // split what is left after the CSS gap between them.
        if (fbox.PositioningMode == ParagraphPositioningMode.Default
            && fbox.Left == 0 && fbox.Top == 0
            && fbox.Paragraphs.Count == 1
            && fbox.Paragraphs[0] is HtmlFragment colFrag
            && Converters.HtmlToPdfConverter.TryParseColumnArticle(
                colFrag.HtmlContent, out var colArt))
        {
            const double caFs = 12.0, caLine = 13.5, caAbove = 10.7989;
            var caPad = colArt.PadPx * 0.75;
            var caContentW = colArt.WidthPx * 0.75;
            var caContentH = colArt.HeightPx * 0.75;
            var caLeft = marginLeft;
            var caTop = flow.CurrentY;                       // pdf y of the box top
            var caGap = caFs;                                // CSS 'normal' column gap = 1 em
            var caColW = (caContentW - caGap * (colArt.Columns - 1)) / colArt.Columns;
            var caFace = "Times-Roman";
            var caRes = Table.RegisterFont(flow.CurrentPage, caFace);

            double CaWidth(string t)
            {
                if (t.Length == 0) return 0;
                try
                {
                    return Text.FontRepository.TryFindFont(caFace)?.MeasureString(t, caFs)
                           ?? t.Length * caFs * 0.5;
                }
                catch { return t.Length * caFs * 0.5; }
            }

            // wrap every paragraph to the column width, remembering which
            // line ends its paragraph (those never stretch)
            var caLines = new List<(List<string> Words, bool Last)>();
            foreach (var text in colArt.Paragraphs)
            {
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var line = new List<string>();
                var w = 0.0;
                var spaceW = CaWidth(" ");
                foreach (var word in words)
                {
                    var ww = CaWidth(word);
                    var need = line.Count == 0 ? ww : w + spaceW + ww;
                    if (line.Count > 0 && need > caColW + 0.01)
                    {
                        caLines.Add((new List<string>(line), false));
                        line.Clear();
                        w = ww;
                    }
                    else w = need;
                    line.Add(word);
                }
                if (line.Count > 0) caLines.Add((new List<string>(line), true));
            }

            var caB = new Content.ContentStreamBuilder();
            caB.SaveState();
            if (colArt.Background is { } caBg)
                caB.SetFillColor(caBg)
                   .Rectangle(caLeft, caTop - (caContentH + 2 * caPad),
                              caContentW + 2 * caPad, caContentH + 2 * caPad)
                   .Fill();
            caB.SetFillGray(0);

            var caPerCol = (int)Math.Floor(caContentH / caLine);
            var caIdx = 0;
            for (var col = 0; col < colArt.Columns && caIdx < caLines.Count; col++)
            {
                var colLeft = caLeft + caPad + col * (caColW + caGap);
                var y = caTop - caPad - caAbove;              // first baseline
                for (var k = 0; k < caPerCol && caIdx < caLines.Count; k++, caIdx++)
                {
                    var (words, last) = caLines[caIdx];
                    var natural = 0.0;
                    foreach (var word in words) natural += CaWidth(word);
                    var gaps = words.Count - 1;
                    // a justified line stretches its gaps to the column edge;
                    // the line that ends a paragraph keeps its natural spaces
                    var gapW = gaps > 0 && colArt.Justify && !last
                        ? (caColW - natural) / gaps
                        : CaWidth(" ");
                    var x = colLeft;
                    foreach (var word in words)
                    {
                        caB.BeginText().SetFont(caRes, caFs)
                           .MoveTextPosition(x, y)
                           .ShowText(word).EndText();
                        x += CaWidth(word) + gapW;
                    }
                    y -= caLine;
                }
            }
            caB.RestoreState();
            flow.InjectContentAtCursor(caB.Build());
            flow.AdvanceY(caContentH + 2 * caPad);
            return;
        }
        // Flow-positioned, no-size FloatingBox is indistinguishable from the
        // ambient paragraph flow. Inline its child paragraphs into the shared
        // cursor so long content paginates via the surrounding FlowLayout.
        // Absolutely-positioned (Left/Top set) boxes still render through
        // AddFloatingBox since they don't participate in the flow.
        // A box that paints a background/border or carries a background
        // image is meant to render as a visible box (e.g. a coloured header
        // band), not be dissolved into the transparent paragraph flow — route
        // it through AddFloatingBox so its fill, border and child images draw.
        var fboxIsVisibleBox = fbox.BackgroundColor is not null
            || fbox.BackgroundImage is not null
            || (fbox.Border is not null && fbox.Border.HasAnySide);
        // A box that paints chrome but declares NO SIZE still dissolves: it
        // flows its children exactly as a chrome-less box does (top and left margins
        // ignored, the bottom one acting) and strokes the border round the region the
        // children ended up occupying — the content width, from the cursor where the
        // box opened down to where it closed. Routing it through the absolute renderer
        // instead drew a degenerate 1x1 border and charged the box's Margin.Top.
        var fboxChromeOnFlow = fboxIsVisibleBox && fbox.Width <= 0 && fbox.Height <= 0;
        if (fbox.PositioningMode == ParagraphPositioningMode.Default
            && fbox.Left == 0 && fbox.Top == 0 && (!fboxIsVisibleBox || fboxChromeOnFlow))
        {
            var chromeTop = flow.CurrentY;
            var chromePage = flow.CurrentPage;
            FlowLayout.DissolvedBandPlan? plan = null;
            if (!flow.IsDryRun && DissolvedBoxHasNotes(fbox))
            {
                var dryFontName = fontName;
                plan = flow.PlanDissolvedBands(candidate =>
                {
                    var dryPage = page.CreateDetachedSibling();
                    dryPage.Footer = page.Footer;
                    var dryFlow = flow.CreateDryRun(dryPage);
                    dryFlow.ApplyDissolvedPlan(candidate);
                    var dryCounters = new Dictionary<int, int>(headingAutoCounters);
                    var dryName = dryFontName;
                    LayoutDissolvedFloatingBox(fbox, dryFlow, dryPage, tocEntries, pl, dryCounters,
                        ref dryName, new List<(byte[] content, double width, double height)>(),
                        marginLeft, marginRight, marginBottom);
                    return dryFlow;
                });
                flow.ApplyDissolvedPlan(plan);
            }
            LayoutDissolvedFloatingBox(fbox, flow, page, tocEntries, pl, headingAutoCounters,
                ref fontName, overflowPages, marginLeft, marginRight, marginBottom);
            if (fboxChromeOnFlow && !flow.IsDryRun && ReferenceEquals(chromePage, flow.CurrentPage))
            {
                var chromeW = page.Width - marginRight - marginLeft;
                var chromeH = chromeTop - flow.CurrentY;
                if (chromeW > 0 && chromeH > 0)
                {
                    var cb = new Content.ContentStreamBuilder();
                    cb.SaveState();
                    if (fbox.BackgroundColor is { } chromeBg)
                        cb.SetFillColor(chromeBg).Rectangle(marginLeft, flow.CurrentY, chromeW, chromeH).Fill();
                    if (fbox.Border is { } chromeBorder && chromeBorder.HasAnySide)
                        FloatingBox.StrokeBorder(cb, chromeBorder, marginLeft, flow.CurrentY, chromeW, chromeH);
                    cb.RestoreState();
                    flow.InjectContentAtCursor(cb.Build());
                }
            }
            // Of the box's own margins only the bottom one acts: it is the gap
            // between the box's last line and the next paragraph. Top and Left
            // do not move a dissolved box (measured 2026-08-23 with each margin
            // in isolation).
            if (fbox.Margin?.Bottom > 0) flow.AdvanceY(fbox.Margin.Bottom);
        }
        else if (fbox.PositioningMode == ParagraphPositioningMode.Default
                 && fbox.Height <= 0 && !fboxIsVisibleBox
                 && fbox.Paragraphs.Count > 0
                 && fbox.Paragraphs.All(p => p is Text.TextFragment))
        {
            // A flow box with Left/Top offsets and no Height is a column of text
            // that starts Top below the cursor at margin + Left, wraps to its
            // Width and runs down to the page's bottom margin; the rest continues
            // on fresh pages at the content top, still at margin + Left (the Top
            // offset is not re-applied). Laid through the flow as one column.
            if (fbox.Top > 0) flow.AdvanceY(fbox.Top);
            var offLeft = marginLeft + fbox.Left;
            var offWidth = fbox.Width > 0 ? fbox.Width : page.Width - marginRight - offLeft;
            LayoutDissolvedFloatingBox(fbox, flow, page, tocEntries, pl, headingAutoCounters,
                ref fontName, overflowPages, marginLeft, marginRight, marginBottom,
                (new[] { offLeft }, new[] { offWidth }));
            if (fbox.Margin?.Bottom > 0) flow.AdvanceY(fbox.Margin.Bottom);
        }
        else if (fbox.PositioningMode == ParagraphPositioningMode.Default
                 && fbox.Left == 0 && fbox.Top == 0 && fbox.Height > 0 && fbox.Width > 0
                 && fbox.BackgroundImage is null
                 && fbox.ColumnInfo is { ColumnCount: > 1 }
                 && fbox.Paragraphs.All(p => p is Text.TextFragment))
        {
            // A sized, multi-column text box at the flow cursor: its background
            // and border paint Width x Height at the cursor, the paragraphs pour
            // down the columns (each column at box left + the widths and spacing
            // before it, cut at the page's right content edge) from Padding.Top
            // below the box top, and the flow resumes one Height below the box
            // top (measured 2026-08-23).
            var cbLeft = marginLeft;
            var cbTop = flow.CurrentY;
            var cbContent = new Content.ContentStreamBuilder();
            cbContent.SaveState();
            if (fbox.BackgroundColor is { } cbBg)
                cbContent.SetFillColor(cbBg).Rectangle(cbLeft, cbTop - fbox.Height, fbox.Width, fbox.Height).Fill();
            if (fbox.Border is { } cbBorder && cbBorder.HasAnySide)
                FloatingBox.StrokeBorder(cbContent, cbBorder, cbLeft, cbTop - fbox.Height, fbox.Width, fbox.Height);
            cbContent.RestoreState();
            flow.InjectContentAtCursor(cbContent.Build());
            var cbPad = fbox.Padding;
            if (cbPad?.Top > 0) flow.AdvanceY(cbPad.Top);
            var (cbLefts, cbWidths) = BuildColumnGeometry(fbox.ColumnInfo, cbLeft + (cbPad?.Left ?? 0),
                page.Width - marginRight - cbLeft - (cbPad?.Left ?? 0));
            LayoutDissolvedFloatingBox(fbox, flow, page, tocEntries, pl, headingAutoCounters,
                ref fontName, overflowPages, marginLeft, marginRight, marginBottom, (cbLefts, cbWidths));
            flow.MoveCursorTo(Math.Min(flow.CurrentY, cbTop - fbox.Height));
            if (fbox.Margin?.Bottom > 0) flow.AdvanceY(fbox.Margin.Bottom);
        }
        else if (fbox.PositioningMode != ParagraphPositioningMode.Default
                 || fbox.Left != 0 || fbox.Top != 0)
        {
            if (fbox.PositioningMode == ParagraphPositioningMode.Default)
            {
                // Default-positioned box with a Left/Top offset: the
                // offsets are relative to the page CONTENT area and the
                // box top anchors at the current flow position
                // (left margin + Left, top at the
                // cursor), so translate before the absolute render.
                var savedFbMode = fbox.PositioningMode;
                var savedFbTop = fbox.Top;
                var savedFbLeft = fbox.Left;
                fbox.PositioningMode = ParagraphPositioningMode.Absolute;
                fbox.Top = page.Height - flow.CurrentY + fbox.Top;
                fbox.Left = marginLeft + fbox.Left;
                fbox.PageBottomMargin = marginBottom;
                page.AddFloatingBox(fbox);
                EmitFloatingBoxLink(fbox, flow, page);
                fbox.PositioningMode = savedFbMode;
                fbox.Top = savedFbTop;
                fbox.Left = savedFbLeft;
                foreach (var spill in fbox.LastOverflowPages)
                    overflowPages.Add((spill, page.Width, page.Height));
            }
            else
            {
                // Absolute box — doesn't affect the flow cursor, but Left/Top
                // are relative to the page CONTENT area, not the page edge
                // (probed 2026-08-28: a 100x100 box at Left=300/Top=0 on the
                // default 90/72 margins strokes its border at x 389.95,
                // top y 770.05): translate by the page margins for the render.
                var savedAbsTop = fbox.Top;
                var savedAbsLeft = fbox.Left;
                fbox.Top = marginTop + fbox.Top;
                fbox.Left = marginLeft + fbox.Left;
                fbox.PageBottomMargin = marginBottom;
                page.AddFloatingBox(fbox);
                EmitFloatingBoxLink(fbox, flow, page);
                fbox.Top = savedAbsTop;
                fbox.Left = savedAbsLeft;
                foreach (var spill in fbox.LastOverflowPages)
                    overflowPages.Add((spill, page.Width, page.Height));
            }
        }
        else
        {
            // Flow-positioned visible box (background/border): render it at the
            // current cursor — not the page top — so a coloured header band
            // honours the page's top margin, then advance the flow past it.
            var targetPage = flow.CurrentPage;
            var savedMode = fbox.PositioningMode;
            var savedTop = fbox.Top;
            var savedLeft = fbox.Left;
            fbox.PositioningMode = ParagraphPositioningMode.Absolute;
            fbox.Top = targetPage.Height - flow.CurrentY;
            // The box seats at the page's LEFT CONTENT edge, not at x = 0: a
            // flow-positioned box starts where the flow does.
            fbox.Left = marginLeft + fbox.Left;
            fbox.PageBottomMargin = marginBottom;
            targetPage.AddFloatingBox(fbox);
            EmitFloatingBoxLink(fbox, flow, targetPage);
            fbox.PositioningMode = savedMode;
            fbox.Top = savedTop;
            fbox.Left = savedLeft;
            // Content the box could not hold spills onto fresh pages, where the box
            // re-seats with its chrome — without draining this the overflow rows were
            // silently dropped.
            foreach (var spill in fbox.LastOverflowPages)
                overflowPages.Add((spill, page.Width, page.Height));
            flow.AdvanceY(fbox.Height);
        }
    }

    /// <summary>A FloatingBox's paragraph Hyperlink is one Link annotation over the
    /// painted box.</summary>
    private static void EmitFloatingBoxLink(FloatingBox fbox, FlowLayout flow, Page page)
    {
        if (fbox.Hyperlink is null || flow.IsDryRun) return;
        // The box paints on the flow's start page; queue so the link resolves
        // against the final page sequence.
        if (ReferenceEquals(page, flow.CurrentPage) && !flow.HasOverflowed)
            flow.QueueLink(fbox.LastBoxRect, fbox.Hyperlink);
        else
            flow.EmitLinkNow(page, fbox.LastBoxRect, fbox.Hyperlink);
    }

    /// <summary>A fragment that carries a line-break segment ALONGSIDE real text. Only
    /// then does the break stand one builder-default line: a whole paragraph that is
    /// nothing but <c>Environment.NewLine</c> keeps the paragraph pitch the legacy
    /// writer gives it (probed — a box stacks bare newline PARAGRAPHS and
    /// each is charged its own pitch, while newline SEGMENTS inside
    /// a text-bearing fragment cost 10 pt each).</summary>
    private static bool HasBreakSegmentBesideText(Text.TextFragment tf)
    {
        var brk = false;
        var text = false;
        foreach (var seg in tf.Segments)
        {
            if (IsBreakSegment(seg)) { brk = true; continue; }
            if (!string.IsNullOrEmpty(seg.Text)) text = true;
        }
        return brk && text;
    }

    /// <summary>A segment that is nothing but a line break: it closes the line it sits
    /// on and stands one builder-default line of its own.</summary>
    private static bool IsBreakSegment(Text.TextSegment seg)
    {
        var t = seg.Text;
        if (string.IsNullOrEmpty(t) || t.Trim().Length != 0) return false;
        foreach (var ch in t) if (ch == (char)10 || ch == (char)13) return true;
        return false;
    }

    /// <summary>Whether a dissolved box carries a note whose band the planner must
    /// fit against the body.</summary>
    private static bool DissolvedBoxHasNotes(FloatingBox fbox)
    {
        foreach (var p in fbox.Paragraphs)
            if (p is Text.TextFragment { FootNote.Paragraphs.Count: > 0 }
                || p is Text.TextFragment { EndNote.Paragraphs.Count: > 0 })
                return true;
        return false;
    }

    /// <summary>Lay a dissolved (flow-positioned, no-size) FloatingBox's children
    /// into <paramref name="flow"/>. Run once for real; the band planner runs it
    /// into dry flows first when the box carries notes.</summary>
    private void LayoutDissolvedFloatingBox(FloatingBox fbox, FlowLayout flow, Page page,
        System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries,
        PageLayoutState pl, Dictionary<int, int> headingAutoCounters,
        ref string? fontName, List<(byte[] content, double width, double height)> overflowPages,
        double marginLeft, double marginRight, double marginBottom,
        (double[] lefts, double[] widths)? columnOverride = null)
    {
            // Multi-column box: lay the children out across N columns
            // (fill column 0 top-to-bottom, then column 1, ... then a
            // fresh page). Columns start at the page's left content
            // margin; the box's own Margin doesn't inset the flow.
            // A caller that sized/offset the box passes its own geometry
            // (a single narrow column for an offset box, padded columns
            // for a painted one).
            var columnCount = fbox.ColumnInfo?.ColumnCount ?? 0;
            var inColumns = false;
            if (columnOverride is { } co)
            {
                flow.BeginColumns(co.lefts, co.widths);
                inColumns = true;
            }
            else if (columnCount > 1)
            {
                var (lefts, widths) = BuildColumnGeometry(
                    fbox.ColumnInfo!, marginLeft,
                    page.Width - marginLeft - marginRight);
                if (lefts.Length > 1)
                {
                    flow.BeginColumns(lefts, widths);
                    inColumns = true;
                }
            }

            // Inline-joined styled paragraph accumulator: consecutive
            // IsInLineParagraph fragments/headings merge into ONE
            // flowing paragraph (joined with their
            // per-segment styles, footnote reference marks and heading
            // labels intact). A paragraph flushes when a
            // non-inline child starts the next one.
            var styRuns = new List<FlowLayout.StyledRun>();
            double styLs = 0;
            double styBaseSize = 0;
            var styNotes = new List<(Note note, string marker, double size)>();
            Color? styBackground = null;
            var styAlign = HorizontalAlignment.Left;
            var styLastChild = -1;

            static bool InlineOf(BaseParagraph p) =>
                p is Text.TextFragment ptf ? ptf.IsInLineParagraph : p.IsInLineParagraph;

            // A child needs the styled-run engine when the legacy
            // writers would drop its decorations: an explicit label,
            // an inline join (either side), per-segment colour /
            // underline / superscript, or a footnote.
            bool SegStyled(Text.TextState st) => st.Underline
                || st.ForegroundColor is not null || st.Superscript;

            void FlushStyled()
            {
                if (styRuns.Count > 0)
                    flow.WriteStyledParagraph(styRuns, styLs, styBackground, styAlign);
                foreach (var (n, marker, sz) in styNotes)
                    flow.QueueFootnote(n, marker, sz, styLastChild);
                var closeAfter = styNotes.Count > 0 && flow.ShouldCloseAfterChild(styLastChild);
                styRuns = new List<FlowLayout.StyledRun>();
                styNotes = new List<(Note, string, double)>();
                styLs = 0;
                styBaseSize = 0;
                styBackground = null;
                styAlign = HorizontalAlignment.Left;
                // The planner closes the page after the child whose note the band
                // could not take whole.
                if (closeAfter) flow.ForceNewPage();
            }

            void AppendFragmentRuns(Text.TextFragment tf)
            {
                var parent = tf.TextState;
                if (styRuns.Count == 0)
                    styAlign = tf.HorizontalAlignment != HorizontalAlignment.Left
                        ? tf.HorizontalAlignment : parent.HorizontalAlignment;
                if (parent.LineSpacing > styLs) styLs = parent.LineSpacing;
                styBackground ??= parent.BackgroundColor;
                // The fragment's own size: its largest segment size — a text-less
                // segment counts (an empty inline owner sized 12 carries a 6 pt
                // mark) — else the fragment's, else the builder default.
                double tfBase = 0;
                var hasOwnText = false;
                foreach (var seg in tf.Segments)
                {
                    if (!seg.TextState.Superscript && seg.TextState.FontSizeTouched)
                        tfBase = Math.Max(tfBase, seg.TextState.FontSize);
                    hasOwnText |= !string.IsNullOrEmpty(seg.Text);
                }
                var joinsLine = tf.IsInLineParagraph && styRuns.Count > 0;
                foreach (var seg in tf.Segments)
                {
                    var st = seg.TextState;
                    if (st.LineSpacing > styLs) styLs = st.LineSpacing;
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    if (IsBreakSegment(seg) && HasBreakSegmentBesideText(tf))
                    {
                        styRuns.Add(new FlowLayout.StyledRun
                        {
                            HardBreak = true, Text = string.Empty,
                            Size = Document.FlowLayout.XmlDefaultFontSize,
                        });
                        continue;
                    }
                    // A segment with no explicit size falls back to the
                    // document-builder default point size (10), not the
                    // Standard-14 12 — an untyped FloatingBox fragment
                    // renders at the builder default.
                    var size = st.FontSizeTouched ? (double)st.FontSize
                        : parent.FontSizeTouched ? parent.FontSize : 10;
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
                    if (st.FontData is not null) merged.FontData = st.FontData;
                    else if (parent.FontData is not null) merged.FontData = parent.FontData;
                    if (!st.Superscript && size > styBaseSize) styBaseSize = size;
                    styRuns.Add(new FlowLayout.StyledRun
                    {
                        Text = seg.Text, Size = size, State = merged,
                        Sup = st.Superscript, Link = seg.Hyperlink, InlineStart = joinsLine,
                    });
                    joinsLine = false;
                }
                if (tf.FootNote is { Paragraphs.Count: > 0 } fn)
                {
                    // The mark: half ITS fragment's size — its text's, else the
                    // fragment's own (the builder default 10 pt for an unsized
                    // one; an inline-joined fragment does not borrow the size of
                    // the paragraph it joins) — always in the Standard-14 sans.
                    var markSize = tfBase > 0 ? tfBase : parent.FontSizeTouched ? parent.FontSize : 10;
                    // A text-less inline fragment joins the previous line at its
                    // box top and is as tall as its own size plus leading.
                    var joinH = tf.IsInLineParagraph && !hasOwnText ? markSize + parent.LineSpacing : 0;
                    var marker = flow.NextFootnoteMarker(fn);
                    var markState = new Text.TextState { ForegroundColor = fn.TextState?.ForegroundColor };
                    if (marker.Length > 0)
                        styRuns.Add(new FlowLayout.StyledRun
                        {
                            Text = marker, Size = markSize, State = markState,
                            Sup = true, NoteMark = true, Note = fn, JoinHeight = joinH,
                        });
                    styNotes.Add((fn, marker, markSize));
                }
            }

            void AppendHeadingRuns(Heading h)
            {
                var parent = h.TextState;
                var ownerLeft = h.Margin?.Left ?? 0;
                if (parent.LineSpacing > styLs) styLs = parent.LineSpacing;
                if (styRuns.Count == 0)
                {
                    // The label renders only when the heading STARTS the
                    // paragraph — inline-joined headings show no number.
                    var label = h.UserLabel?.Text
                        ?? NextHeadingPrefix(headingAutoCounters, h).TrimEnd();
                    if (!string.IsNullOrEmpty(label))
                    {
                        var lblState = new Text.TextState
                        {
                            IsBold = h.UserLabel?.TextState.IsBold ?? false,
                        };
                        styRuns.Add(new FlowLayout.StyledRun
                        {
                            Text = label,
                            Size = parent.FontSize > 0 ? parent.FontSize : 10,
                            State = lblState, OwnerLeft = ownerLeft, TabAfter = 20,
                        });
                    }
                }
                foreach (var seg in h.Segments)
                {
                    var st = seg.TextState;
                    if (st.LineSpacing > styLs) styLs = st.LineSpacing;
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    var size = st.FontSizeTouched ? (double)st.FontSize
                        : parent.FontSizeTouched ? parent.FontSize : 10;
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
                    if (!st.Superscript && size > styBaseSize) styBaseSize = size;
                    styRuns.Add(new FlowLayout.StyledRun
                    {
                        Text = seg.Text, Size = size, State = merged,
                        Sup = st.Superscript, OwnerLeft = ownerLeft,
                    });
                }
            }

            var innerList = fbox.Paragraphs.ToList();
            for (var innerIdx = 0; innerIdx < innerList.Count; innerIdx++)
            {
                var inner = innerList[innerIdx];
                // IsInNewPage on a child forces the rest of the box onto a
                // fresh page (the surrounding flow paginates it), mirroring the
                // page-level paragraph rule. Flush the accumulated inline
                // paragraph first so it stays on the current page.
                if (innerIdx > 0 && ParagraphIsInNewPage(inner))
                {
                    FlushStyled();
                    flow.ForceNewPage();
                }
                // IsFirstParagraphInColumn pushes this paragraph to the
                // top of the next column. Never on the
                // very first child — column 0 is already its home.
                if (inColumns && innerIdx > 0 && inner.IsFirstParagraphInColumn)
                {
                    FlushStyled();
                    flow.ForceNextColumn();
                }
                var nextInline = innerIdx + 1 < innerList.Count
                    && InlineOf(innerList[innerIdx + 1]);

                if (inner is Heading innerHeading)
                {
                    // A TOC-page-authored heading inside the box is a TOC
                    // entry (same rule as the page-level branch); anything
                    // else renders as a content heading at the flow cursor
                    // — previously these were silently dropped and the box
                    // rendered blank.
                    if (ReferenceEquals(innerHeading.TocPage, page) || flow.IsDryRun)
                    {
                        FlushStyled();
                        if (flow.IsDryRun) continue;
                        var tIdx = tocEntries.FindIndex(e => ReferenceEquals(e.h, innerHeading));
                        if (tIdx >= 0)
                        {
                            var yA = RenderTocEntry(pl, innerHeading, tocEntries[tIdx].pageIdx, flow.CurrentY);
                            flow.AdvanceY(flow.CurrentY - yA);
                        }
                        continue;
                    }
                    var hStyled = innerHeading.UserLabel is not null
                        || InlineOf(innerHeading) || nextInline
                        || SegStyled(innerHeading.TextState)
                        || innerHeading.Segments.Any(s => SegStyled(s.TextState));
                    if (hStyled)
                    {
                        if (!InlineOf(innerHeading)) FlushStyled();
                        if (styRuns.Count == 0 && innerHeading.Margin is { Top: > 0 } hm)
                        {
                            if (flow.CurrentY - hm.Top - 12 < flow.BottomMargin)
                                flow.ForceNewPage();
                            flow.AdvanceY(hm.Top);
                        }
                        styLastChild = innerIdx;
                        AppendHeadingRuns(innerHeading);
                        if (!nextInline) FlushStyled();
                        continue;
                    }
                    FlushStyled();
                    // Heading top margin advances the flow; when the margin
                    // plus one heading line no longer fits, the heading
                    // moves to a fresh page and re-applies its margin there
                    // (as in the overflowing list case).
                    var hTopM = innerHeading.Margin?.Top ?? 0;
                    if (hTopM > 0)
                    {
                        if (flow.CurrentY - hTopM - 12 < flow.BottomMargin)
                            flow.ForceNewPage();
                        flow.AdvanceY(hTopM);
                    }
                    fontName ??= Table.RegisterFont(page);
                    var innerPrefix = NextHeadingPrefix(headingAutoCounters, innerHeading);
                    var (hContent, hHeight) = innerHeading.Build(
                        flow.CurrentPage, marginLeft + (innerHeading.Margin?.Left ?? 0),
                        flow.CurrentY, fontName, innerPrefix);
                    flow.InjectContentAtCursor(hContent);
                    flow.AdvanceY(hHeight);
                    continue;
                }
                if (inner is Text.TextFragment innerTf)
                {
                    var tfStyled = innerTf.IsInLineParagraph || nextInline
                        || innerTf.FootNote is { Paragraphs.Count: > 0 }
                        || SegStyled(innerTf.TextState)
                        || innerTf.Segments.Any(s => SegStyled(s.TextState))
                        // A fragment that MIXES a newline segment with real text needs
                        // the styled-run engine: the legacy writer prices that empty
                        // line at the fragment's own pitch instead of one default line,
                        // and drops the whole fragment outright when the segments also
                        // differ in size (its styled-line fallback carries no break).
                        || HasBreakSegmentBesideText(innerTf);
                    if (tfStyled)
                    {
                        if (!innerTf.IsInLineParagraph) FlushStyled();
                        if (styRuns.Count == 0)
                        {
                            flow.RecordPosition(innerTf);
                            if (innerTf.Margin is { Top: > 0 } styTopM) flow.AdvanceY(styTopM.Top);
                        }
                        styLastChild = innerIdx;
                        AppendFragmentRuns(innerTf);
                        if (!nextInline) FlushStyled();
                        continue;
                    }
                    FlushStyled();
                    flow.RecordPosition(innerTf);
                    // A child's own margins are room above and below it, as the
                    // page-level dispatcher reserves them (a headnote line with
                    // Margin.Top 6 pitches 20, not 14).
                    flow.ReserveTopMargin(innerTf.Margin?.Top ?? 0, innerTf);
                    flow.WriteTextFragment(innerTf);
                    var innerBottomM = innerTf.Margin?.Bottom ?? 0;
                    if (innerBottomM > 0) flow.AdvanceY(innerBottomM);
                    continue;
                }
                if (inner is Image lineImg && (nextInline || (InlineOf(lineImg) && styRuns.Count > 0)))
                {
                    // A picture that shares its line with text — either it is itself
                    // inline-joined, or the paragraph after it is — is a RUN of the
                    // styled paragraph: the text seats at the picture's right edge and
                    // the line advances by the text's pitch, the picture overhanging
                    // below it (probed on the era generator).
                    if (!InlineOf(lineImg)) FlushStyled();
                    if (LoadFlowImage(lineImg, page.Width - marginLeft - marginRight,
                            flow.ContentTop - marginBottom, out var liData, out var liW, out var liH))
                    {
                        styLastChild = innerIdx;
                        styRuns.Add(new FlowLayout.StyledRun
                        { ImageData = liData, ImageW = liW, ImageH = liH });
                    }
                    if (!nextInline) FlushStyled();
                    continue;
                }
                FlushStyled();
                if (inner is Aspose.Pdf.Drawing.Graph innerGraph)
                {
                    // A flow Graph in a dissolved box (a chapter rule):
                    // draw at the cursor and advance past it.
                    LayoutGraphParagraph(innerGraph, flow, page, marginLeft, 0, marginBottom);
                    continue;
                }
                if (inner is Image innerImage)
                {
                    // A block Image in a dissolved box draws at the flow
                    // cursor and advances the flow below it.
                    if (LoadFlowImage(innerImage, page.Width - marginLeft - marginRight,
                            flow.ContentTop - marginBottom, out var fbImgBytes, out var fbIw, out var fbIh))
                        flow.PlaceImageBlock(fbImgBytes, fbIw, fbIh);
                    continue;
                }
                if (inner is HtmlFragment innerHtml)
                {
                    // A dissolved box renders its HTML as blocks, not as one
                    // tag-stripped run: each block is its own paragraph, drawn in
                    // the browser default serif face at the browser default block
                    // size, and inline <a href> ranges become hyperlinked segments
                    // that the flow turns into Link annotations over their glyphs.
                    var innerBlocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
                        innerHtml.HtmlContent ?? "", HtmlUaBlockFontSize);
                    var innerWrote = false;
                    foreach (var ib in innerBlocks)
                    {
                        if (string.IsNullOrWhiteSpace(ib.Text)) continue;
                        var innerFrag = new Text.TextFragment(ib.Text);
                        innerFrag.TextState.FontName = HtmlUaSerifFontName;
                        innerFrag.TextState.FontSize =
                            (float)(ib.FontSize > 0 ? ib.FontSize : HtmlUaBlockFontSize);
                        if (ib.Anchors is { Count: > 0 })
                            ApplyHtmlAnchorSegments(innerFrag, ib.Text, ib.Anchors);
                        flow.WriteTextFragment(innerFrag);
                        innerWrote = true;
                    }
                    if (!innerWrote)
                    {
                        var innerPlain = HtmlFragment.StripHtmlTags(innerHtml.HtmlContent ?? "");
                        if (!string.IsNullOrWhiteSpace(innerPlain))
                            flow.WriteTextFragment(new Text.TextFragment(innerPlain));
                    }
                    continue;
                }
                if (inner is Table innerTable)
                {
                    // The box dissolves into the page flow, so its table
                    // anchors at the page's left content margin (a
                    // margin-less box still honours the page margins) and its
                    // continuation pages resume below the page's TOP content
                    // margin like any flow table (not at the bare page top).
                    innerTable.FlowLeftOffset = marginLeft;
                    var fbPage = flow.CurrentPage;
                    var fbSpillTop = fbPage.PageInfo?.Margin is { TopTouched: true } fbPm ? fbPm.Top
                        : PageInfo?.Margin is { TopTouched: true } fbDm ? fbDm.Top : 72;
                    // Bottom limit is the flow's own, not a flat 36: a page declaring a
                    // 72 pt bottom margin had its box's table run 36 pt past it. Taking
                    // it from the flow (rather than the caller's page margin) keeps a
                    // continuation slot's margins, exactly as the plain flow-table path
                    // and the report-band path already do.
                    var innerContents = innerTable.BuildMultiPage(fbPage, flow.CurrentY, flow.BottomMargin, fbSpillTop);
                    // Inject at the flow's CURRENT page position — after a
                    // page break the slice belongs to the overflow buffer,
                    // not the start page.
                    flow.InjectContentAtCursor(innerContents[0]);
                    var innerGraphs = innerTable.LastGraphDraws;
                    if (innerGraphs.Count > 0)
                        foreach (var gc in innerGraphs[0])
                            flow.InjectContentAtCursor(gc);
                    var innerImgs = innerTable.LastImageDraws;
                    if (!flow.HasOverflowed && innerImgs.Count > 0)
                        foreach (var (data, rect) in innerImgs[0])
                            flow.CurrentPage.AddImage(data, rect);
                    // A single-slice table consumes exactly its height so
                    // following children continue below it on this page.
                    if (innerContents.Count == 1)
                        flow.AdvanceY(innerTable.LastRenderedHeight);
                    else
                    {
                        // Intermediate spill pages are whole body pages and stand
                        // alone; the LAST one goes back to the flow so the box's
                        // remaining children pack BELOW the table on it. Parking the
                        // cursor under the bottom margin instead (the old reset) made
                        // every following child build from an exhausted page, so each
                        // returned an empty first slice plus a page of its own — the
                        // reference runs seven of these blocks onto one such page.
                        for (var pi = 1; pi < innerContents.Count - 1; pi++)
                        {
                            flow.RecordBodyOnSlot(overflowPages.Count, marginBottom);
                            overflowPages.Add((innerContents[pi], flow.CurrentPage.Width, flow.CurrentPage.Height));
                        }
                        var innerSlot = flow.ContinueOnPrebuiltSpill(
                            innerContents[innerContents.Count - 1], innerTable.LastPageEndY);
                        flow.RecordBodyOnSlot(innerSlot, innerTable.LastPageEndY);
                    }
                }
            }
            FlushStyled();

            if (inColumns)
                flow.EndColumns();
    }
}
