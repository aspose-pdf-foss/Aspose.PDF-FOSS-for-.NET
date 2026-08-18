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
    /// Apply page-level Paragraphs, Headers, and Footers to each page's content stream.
    /// Called automatically before save.
    /// </summary>
    private void ApplyPageContent()
    {        // Form fields (combo/list/check/text boxes, radio groups) added to the
        // generator paragraph tree must be registered in the AcroForm before the
        // pages are written, so they round-trip as real fields.
        RegisterGeneratedFormFields();

        // Collect overflow pages to add after iteration
        var overflowPages = new List<(byte[] content, double width, double height)>();
        // Table images destined for an overflow page, keyed by that page's slot index in
        // overflowPages. Applied once the page is materialised (the page object doesn't
        // exist while the table is being built).
        var overflowImages = new Dictionary<int, List<(byte[] data, Rectangle rect)>>();
        // Per-page flow layouts whose deferred annotations need resolving against
        // the final Page sequence (slot indices into overflowPages). Each entry:
        // the flow + the slot range it owns in overflowPages.
        var pendingFlows = new List<(FlowLayout flow, int slotStart, int slotEnd)>();
        // TOC leaders + link annotations are emitted only after EVERY page has
        // laid out and the overflow pages are materialised: an entry's printed
        // page number must reflect the page its heading FINALLY landed on
        // (content pagination / IsInNewPage moves headings onto pages that do
        // not exist while the TOC page itself is being laid out).
        var pendingTocEmits = new List<(Page tocPage, string fontName, List<Page> contPages,
            System.Collections.Generic.List<(int slot, byte[] preLeader, double textEnd, double lastY,
                double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop,
                bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx,
                Rectangle linkRect, Heading heading, string lastLine, double lastX,
                System.Func<string, double>? measure)> entries)>();
        // Snapshot pages: if a paragraph handler (e.g. Image overflow)
        // appends a new Page mid-loop, the live collection mutates.
        var pagesSnapshot = Pages.ToList();
        // Per-level running counters for auto-sequenced headings authored
        // directly in page Paragraphs (e.g. Heading{ IsAutoSequence = true }).
        // Document-scoped so the sequence continues across pages.
        var headingAutoCounters = new Dictionary<int, int>();
        foreach (var page in pagesSnapshot)
        {
            // Flush operator collection to content stream
            page.Contents.FlushToPage();

            // Page.Background paints the whole page (MediaBox) behind every other
            // operator. Prepended so it sits under existing content and any
            // paragraphs/header/footer rendered below. The fill is wrapped in a
            // /Background marked-content block so re-applying a background replaces
            // the previous one instead of stacking, and Color.White means "remove
            // the background" (the documented semantics).
            if (page.ExplicitBackground is { } pageBg && !page.BackgroundApplied)
            {
                page.BackgroundApplied = true;
                page.RemoveTaggedBackground();
                var isWhite = pageBg.R == 255 && pageBg.G == 255 && pageBg.B == 255;
                if (!isWhite)
                {
                    var box = page.MediaBox;
                    var bgBuilder = new Content.ContentStreamBuilder();
                    bgBuilder.BeginMarkedContent(Page.BackgroundMarkerTag);
                    bgBuilder.SaveState();
                    bgBuilder.SetFillColor(pageBg.R / 255.0, pageBg.G / 255.0, pageBg.B / 255.0);
                    bgBuilder.Rectangle(box.LLX, box.LLY, box.Width, box.Height);
                    bgBuilder.Fill();
                    bgBuilder.RestoreState();
                    bgBuilder.EndMarkedContent();
                    page.PrependContentStream(bgBuilder.Build());
                }
            }

            // Materialise /AP appearances for annotations that lack one so they render
            // (the renderer draws Line/Polygon/Polyline only from their /AP) and expose
            // NormalAppearance after save.
            foreach (var annot in page.Annotations)
            {
                if (annot is Annotations.FreeTextAnnotation freeText)
                    freeText.GenerateAppearance();
                else if (annot is Annotations.LineAnnotation or Annotations.PolygonAnnotation
                                or Annotations.PolylineAnnotation or Annotations.SquareAnnotation
                                or Annotations.CircleAnnotation or Annotations.TextAnnotation
                         && annot.NormalAppearance is null)
                    annot.UpdateAppearances();
            }

            // Render page Header/Footer set through Page.Header / Page.Footer.
            // Independent of the paragraph layout below (a page may carry only a
            // header), so it runs before the LayoutApplied gate and guards itself.
            if (!page.HeaderFooterApplied && (page.Header is not null || page.Footer is not null))
            {
                page.HeaderFooterApplied = true;
                // Header paragraphs — text, HTML and Table alike — render on every
                // page that references them, whether laid out by the generator or
                // imported with its own content (their cells stay text-extractable
                // and any widgets/links bind to the page they land on).
                //
                // A FOOTER table is the one case that is gated: one HeaderFooter
                // instance shared across the already-inked static pages of an
                // imported document draws no footer table (a footer table stamped
                // onto every page of a loaded document is dropped). It still draws
                // on a generator-laid-out page, and a footer owned by a single page
                // draws normally; text/HTML footer fragments always render.
                bool FooterDrawsTables()
                {
                    if (page.Paragraphs.Count > 0 || page.TocInfo is not null
                        || (page.GetContentStreamBytes()?.Length ?? 0) == 0)
                        return true;
                    var footer = page.Footer;
                    var refs = 0;
                    for (var pi2 = 1; pi2 <= Pages.Count; pi2++)
                    {
                        if (ReferenceEquals(Pages[pi2].Footer, footer)) refs++;
                        if (refs > 1) return false;
                    }
                    return true;
                }
                page.Header?.RenderToPage(page, isHeader: true, page.Number, this);
                page.Footer?.RenderToPage(page, isHeader: false, page.Number, this,
                    FooterDrawsTables());
            }

            // A page laid out by an earlier ProcessParagraphs() call re-enters
            // layout when NEW paragraphs were queued since: the pass resumes at
            // the persisted LayoutCursorY, so the new content stacks below the
            // earlier content on the SAME page (five
            // one-per-call tables stay on page 1 — a re-processed paragraph is the
            // page's first, so IsInNewPage never forces a break either).
            if (page.LayoutApplied && page.Paragraphs.Count == 0) continue;

            // Apply TOC info + Paragraphs
            if (page.TocInfo is not null || page.Paragraphs.Count > 0)
            {
                // A landscape request made before explicit Width/Height assignments is
                // resolved here, when layout consumes the page geometry
                // (IsLandscape=true then Height=A4.Height still lays out wide).
                // On a page whose box was only INHERITED (no-size Insert), the request
                // resolves from the PageInfo A4 default instead: such a TOC page
                // renders 842×595 even inside a US-Letter document.
                if (page.PageInfo is { LandscapeRequested: true } && page.SizeInherited)
                    page.MediaBox = new Rectangle(0, 0, 842, 595);
                else
                    page.PageInfo?.ApplyRequestedOrientation();
                string? fontName = null;
                // page.PageInfo.Margin is always non-null (auto-initialised to a
                // zeroed MarginInfo) so ?? never fires. Treat zeros as "use
                // default 72 pt" — otherwise a fresh page with no explicit
                // margins lays content out with no top/bottom/left/right
                // breathing room at all.
                var m = page.PageInfo?.Margin;
                // Respect user-set margins verbatim (including explicit zeros); fall
                // back per side FIRST to the document-level PageInfo margin
                // (`doc.PageInfo.Margin.Left = 40` is honoured even when it is
                // set AFTER the pages were added — margins resolve at layout time,
                // not at Pages.Add time), THEN to the Generator defaults
                // (90 pt L/R, 72 pt T/B). With the matching default page size A4
                // (595x842), GoTo destinations land at x=90 y=770 = 842-72.
                var docMargin = PageInfo?.Margin;
                var marginTop    = m?.TopTouched    == true ? m!.Top    : docMargin?.TopTouched    == true ? docMargin!.Top    : 72;
                var marginBottom = m?.BottomTouched == true ? m!.Bottom : docMargin?.BottomTouched == true ? docMargin!.Bottom : 72;
                var marginLeft   = m?.LeftTouched   == true ? m!.Left   : docMargin?.LeftTouched   == true ? docMargin!.Left   : 90;
                var marginRight  = m?.RightTouched  == true ? m!.Right  : docMargin?.RightTouched  == true ? docMargin!.Right  : 90;
                // Shared Y cursor so consecutive paragraphs flow down the page instead
                // of piling on top of each other at the top margin. When cursor drops
                // below the bottom margin, FlushToNewPage() starts a fresh overflow page.
                // A page laid out in an earlier pass resumes below its existing content.
                // A CropBox set strictly inside the MediaBox re-anchors the flow:
                // paragraphs lay out in the VISIBLE page (a square CropBox
                // at the media bottom receives a full-bleed image at crop top, not
                // 284 pt below the A4 top edge).
                var layoutTopY = page.Height;
                if (page.RotateDegrees % 360 == 0)
                {
                    var cropForLayout = page.CropBox;
                    var mediaForLayout = page.MediaBox;
                    if (cropForLayout.URY < mediaForLayout.URY - 0.01
                        || cropForLayout.LLY > mediaForLayout.LLY + 0.01)
                        layoutTopY = cropForLayout.URY;
                }
                var curY = page.LayoutCursorY ?? (layoutTopY - marginTop);
                // A reopened page with IsAddParagraphsAfterLast has no persisted
                // layout cursor; resume below the lowest existing text so the new
                // paragraphs stack under the earlier content instead of over it.
                if (page.LayoutCursorY is null && page.IsAddParagraphsAfterLast)
                {
                    try
                    {
                        var resumeAbs = new Text.TextFragmentAbsorber();
                        resumeAbs.Visit(page);
                        double lowest = double.MaxValue;
                        foreach (Text.TextFragment fr in resumeAbs.TextFragments)
                            if (fr.Rectangle is { } fRect && fRect.LLY < lowest)
                                lowest = fRect.LLY;
                        if (lowest < double.MaxValue && lowest < curY)
                            curY = lowest;
                    }
                    catch { /* unreadable content: keep the top-margin cursor */ }
                }

                // TOC entry rendering: every heading whose TocPage is this page renders
                // as "<auto-number> <text> .... <destination page>" — laid out across
                // the configured columns, indented by heading level, with the heading
                // text wrapped to the column width and the page number right-aligned to
                // the column edge with a dot leader on the final line.
                //
                // Entries are drawn FROM THE PARAGRAPH FLOW below (RenderTocEntry runs
                // when the flow reaches each TOC-page heading), not in a separate
                // pre-pass: page content authored around the headings (e.g. spacer
                // fragments before the first entry) keeps its authored order in the
                // content stream and in extraction. Headings that
                // live on OTHER pages get their entries appended after this page's own
                // paragraphs, chaining the same cursor.
                var tocEntries = page.TocInfo is not null
                    ? CollectTocHeadings(page)
                    : new System.Collections.Generic.List<(Heading h, int pageIdx)>();
                var tocRendered = new System.Collections.Generic.HashSet<Heading>(ReferenceEqualityComparer.Instance);
                var tocCol = 0;
                // TOC continuation-page overflow: entries that no longer fit on the
                // current TOC page (their line boxes PLUS the entry's bottom margin
                // would cross the page's bottom margin) continue on pages INSERTED
                // right after the TOC page — this splits a 4×(10+300 pt)
                // TOC into two pages and shifts the content pages down. Because
                // that insertion changes the PAGE NUMBERS the leaders print, every
                // entry's rendering is buffered as (slot, pre-leader bytes, leader
                // parameters) and emitted only after the whole TOC laid out and
                // the continuation pages exist — leaders then resolve their
                // destination indices against the FINAL page sequence, and each
                // entry's text+leader blocks stay adjacent in stream order (the
                // raw-extraction line shape depends on that adjacency).
                var tocSlot = 0;
                var tocPending = new System.Collections.Generic.List<(int slot, byte[] preLeader,
                    double textEnd, double lastY, double entrySize, string entryFace,
                    Text.TabLeaderType leader, double rightStop,
                    bool showNumbers, bool underline, string prefix, double x0,
                    Page? destPage, int fallbackIdx, Rectangle linkRect, Heading heading,
                    string lastLine, double lastX, System.Func<string, double>? measure)>();
                double? tocTopY = null;
                // Hierarchical section counters for IsAutoSequence headings:
                // a level-N heading bumps counter[N] and resets the deeper ones,
                // printing "c1.c2.….cN " (e.g. 1, 1.1, 1.2, 2).
                var tocCounters = new int[12];
                // Script-matched CJK face for entries whose titles need one,
                // resolved once per TOC.
                byte[]? tocCjkTtf = null;
                var tocColCount = 1;
                double[] tocColLefts = System.Array.Empty<double>(), tocColWidths = System.Array.Empty<double>();
                if (tocEntries.Count > 0)
                {
                    // Column geometry: honour ColumnInfo.ColumnCount/widths/spacing
                    // for a multi-column TOC; otherwise a single column. The single-
                    // column geometry is kept identical to the legacy layout (right
                    // edge clamped to a 36 pt inset, 18 pt per indent level) so simple
                    // one-column TOCs are unaffected.
                    var ci = page.TocInfo!.ColumnInfo;
                    tocColCount = ci is { ColumnCount: > 1 } ? ci.ColumnCount : 1;
                    if (tocColCount > 1)
                        (tocColLefts, tocColWidths) = BuildColumnGeometry(
                            ci!, marginLeft, page.Width - marginLeft - marginRight);
                    else
                    {
                        tocColLefts = new[] { marginLeft };
                        // The entry band mirrors the page margins: the page number's
                        // right edge sits at Width − marginRight.
                        tocColWidths = new[] { page.Width - marginRight - marginLeft };
                    }
                }

                // Render TOC title if present. In a MULTI-COLUMN TOC the title
                // belongs to the FIRST column's flow: it centres within that
                // column's width and consumes an entry slot there, while the
                // other columns start at the pre-title top (the
                // second column's first entry aligns with the title row).
                if (page.TocInfo?.Title is { } tocTitle)
                {
                    fontName ??= Table.RegisterFont(page);
                    var titleSize = tocTitle.TextState.FontSize > 0 ? tocTitle.TextState.FontSize : 16;
                    // A bold title is emitted with the Helvetica-Bold base font; its
                    // glyphs are ~6% wider, which the centring estimate accounts for.
                    var titleBold = tocTitle.TextState.IsBold;
                    var titleFont = titleBold ? Table.RegisterFont(page, "Helvetica-Bold") : fontName;
                    // Measure with the REAL rendered widths (per-glyph Standard-14
                    // advances) of the matching AFM table: a length×avg-width guess
                    // over/under-shoots and shifts the title off centre.
                    var titleMetricFont = titleBold ? "Helvetica-Bold" : "Helvetica";
                    double TitleMeasure(string s)
                    {
                        double w = 0;
                        foreach (var tc in s)
                        {
                            var tcw = Text.Standard14Fonts.GetWidth(titleMetricFont, tc < 256 ? tc : '?');
                            if (tcw < 0) tcw = 500;
                            w += tcw * titleSize / 1000.0;
                        }
                        return w;
                    }
                    // The title band: the first column of a multi-column TOC,
                    // else the page's content width.
                    var titleBandLeft = tocColCount > 1 && tocColLefts.Length > 0 ? tocColLefts[0] : marginLeft;
                    var titleBandWidth = tocColCount > 1 && tocColWidths.Length > 0
                        ? tocColWidths[0]
                        : page.Width - marginLeft - marginRight;
                    // Wrap the title to the band: word-wrap first, and when a
                    // single word alone exceeds the band (e.g. a 120 pt
                    // "TableOfContents") fill CHARACTER-wise — such a title
                    // breaks mid-word across several lines rather
                    // than overflowing the page.
                    var titleLines = new List<string>();
                    foreach (var logical in tocTitle.Text.Replace("\r\n", "\n").Split('\n'))
                    {
                        var cur = new System.Text.StringBuilder();
                        foreach (var word in logical.Split(' '))
                        {
                            var trial = cur.Length == 0 ? word : cur + " " + word;
                            if (TitleMeasure(trial) <= titleBandWidth || cur.Length == 0)
                            {
                                if (cur.Length > 0) cur.Append(' ');
                                cur.Append(word);
                            }
                            else
                            {
                                titleLines.Add(cur.ToString());
                                cur.Clear();
                                cur.Append(word);
                            }
                            // Character-fill an over-wide run (single word or the
                            // current accumulation) into full lines.
                            while (TitleMeasure(cur.ToString()) > titleBandWidth && cur.Length > 1)
                            {
                                var fit = cur.Length - 1;
                                while (fit > 1 && TitleMeasure(cur.ToString(0, fit)) > titleBandWidth) fit--;
                                titleLines.Add(cur.ToString(0, fit));
                                cur.Remove(0, fit);
                            }
                        }
                        if (cur.Length > 0) titleLines.Add(cur.ToString());
                    }
                    if (titleLines.Count == 0) titleLines.Add(string.Empty);

                    // Columns other than the first anchor at the PRE-title top.
                    tocTopY = curY;
                    // Each title line occupies a 1-em line box below the cursor:
                    // baseline at (1 - |descent|) of the em, cursor advancing by
                    // the full em (a 12 pt title at cursor
                    // 770 draws its baseline at 760.48 and the next box starts
                    // at 758).
                    var titleAscFrac = (1000.0 + Text.Standard14Fonts.GetDescent("Helvetica")) / 1000.0;
                    var builder = new Content.ContentStreamBuilder();
                    foreach (var tl in titleLines)
                    {
                        var titleWidth = TitleMeasure(tl);
                        // Single-column: centre on the PAGE (the title
                        // x is independent of asymmetric margins). Multi-column:
                        // centre within the first column.
                        var titleX = tocColCount > 1
                            ? System.Math.Max(titleBandLeft, titleBandLeft + (titleBandWidth - titleWidth) / 2)
                            : System.Math.Max(marginLeft, (page.Width - titleWidth) / 2);
                        builder.BeginText()
                            .SetFont(titleFont, titleSize)
                            .SetFillColor(0, 0, 0)
                            .MoveTextPosition(titleX, curY - titleSize * titleAscFrac)
                            .ShowText(tl)
                            .EndText();
                        curY -= titleSize;
                    }
                    page.AddContentStream(builder.Build());
                }

                // Entries are set at the level format's size when the caller set
                // one, else the first explicitly-sized segment, else the 10 pt
                // LevelFormat default. The heading's OWN TextState is NOT in the
                // chain: a 24 pt heading's TOC line draws at the
                // plain 10 pt entry size (only the heading's in-content render
                // uses its TextState).
                const double defaultEntrySize = 10.0;
                static List<string> WrapEntry(string s, double maxW, double fs, string face = "Helvetica")
                {
                    var lines = new List<string>();
                    var cur = new System.Text.StringBuilder();
                    foreach (var word in s.Split(' '))
                    {
                        var trial = cur.Length == 0 ? word : cur + " " + word;
                        if (MeasureEntry(trial, fs, face) <= maxW || cur.Length == 0)
                        {
                            if (cur.Length > 0) cur.Append(' ');
                            cur.Append(word);
                        }
                        else
                        {
                            lines.Add(cur.ToString());
                            cur.Clear();
                            cur.Append(word);
                        }
                    }
                    if (cur.Length > 0) lines.Add(cur.ToString());
                    if (lines.Count == 0) lines.Add(string.Empty);
                    return lines;
                }

                // Render ONE TOC entry with its line box starting at startY; returns
                // the Y the next entry (or following paragraph) continues from.
                double RenderTocEntry(Heading h, int destIdx, double startY)
                {
                    fontName ??= Table.RegisterFont(page);
                    tocRendered.Add(h);
                    // Top of the entry area — column jumps restart from here.
                    tocTopY ??= startY;
                    var formatArray = page.TocInfo!.FormatArray;
                    var entryY = startY;

                    var level = h.Level > 0 ? h.Level : 1;
                    // TocInfo.FormatArray[level-1] carries this level's formatting
                    // (font size, subsequent-lines indent, margins) — every entry
                    // of a level is formatted from it. Falls back to the
                    // heading's own TextState / margins when no level format is set.
                    var fmt = formatArray is { Length: > 0 } fa && level - 1 < fa.Length
                        ? fa[level - 1] : null;
                    // The governing level format's TextState, bound once: it is null
                    // exactly when no level format governs this entry, so every read
                    // below resolves through that single null check.
                    var fmtState = fmt?.TextState;
                    // The entry's size and line spacing resolve through a fixed
                    // chain: the level format wins, then the first SEGMENT
                    // whose value was explicitly set (a segment font size set by the
                    // caller beats the heading's own TextState), then the heading.
                    double? segSize = null;
                    double segSpacing = 0;
                    foreach (Text.TextSegment hs in h.Segments)
                    {
                        if (segSize is null && hs.TextState.FontSizeTouched) segSize = hs.TextState.FontSize;
                        if (segSpacing == 0) segSpacing = hs.TextState.LineSpacing;
                        if (segSize is not null && segSpacing != 0) break;
                    }
                    var entrySize = fmtState is { FontSizeTouched: true } fts
                        ? (double)fts.FontSize
                        : segSize ?? defaultEntrySize;
                    // The level format's FontStyle picks the Standard-14 Helvetica
                    // variant the whole entry (text, leader and page number) is set in.
                    var entryFace = fmtState is null ? "Helvetica"
                        : EntryFace((fmtState.IsBold ? Text.FontStyles.Bold : 0)
                            | (fmtState.IsItalic ? Text.FontStyles.Italic : 0));
                    // The level's leader style; the TocInfo-wide LineDash applies only
                    // when no level format governs this entry.
                    var entryLeader = fmt?.LineDash
                        ?? page.TocInfo?.LineDash ?? Text.TabLeaderType.Dot;
                    // Subsequent (continuation) lines of a multi-line entry are
                    // indented by this level's SubsequentLinesIndent.
                    var subIndent = (double)(fmt?.SubsequentLinesIndent ?? 0);
                    // The line pitch is the entry size PLUS the resolved LineSpacing —
                    // plain entries pack one font-size apart, and an
                    // entry with TextState.LineSpacing=18 at 11 pt steps 29 pt per line
                    // (both its own wrapped lines and the gap to the next entry).
                    var lineSpacing = fmtState is { } fmtTs && fmtTs.LineSpacing != 0
                        ? (double)fmtTs.LineSpacing
                        : segSpacing != 0 ? segSpacing : (double)h.TextState.LineSpacing;
                    var lineH = entrySize + lineSpacing;
                    // Every line box's BOTTOM sits exactly one pitch below the previous
                    // line's bottom (the chain runs on rect bottoms, not baselines) —
                    // startY is the previous line's bottom, so this entry's first
                    // baseline is one pitch lower plus the font's descent.
                    var descFrac = -Text.Standard14Fonts.GetDescent("Helvetica") / 1000.0;
                    // The level format's (or heading's) Margin.Top is leading reserved
                    // ABOVE the entry — every entry (the first
                    // included) is pushed down by it, so consecutive entries sit lineH + Top apart.
                    entryY -= fmt?.Margin?.Top ?? h.Margin?.Top ?? 0;
                    entryY -= lineH - descFrac * entrySize;
                    var prefix = string.Empty;
                    if (h.IsAutoSequence)
                    {
                        if (level < tocCounters.Length)
                        {
                            tocCounters[level]++;
                            for (var k = level + 1; k < tocCounters.Length; k++) tocCounters[k] = 0;
                        }
                        // The section number prints for every auto-
                        // sequenced heading — a heading left at the DEFAULT style
                        // (None) still numbers in arabic ("1  Heading 1"),
                        // so None does NOT suppress.
                        // The number is followed by TWO spaces (the
                        // prefix fragment is "1  " — digit plus two space glyphs).
                        var parts = new List<string>();
                        for (var k = 1; k <= level && k < tocCounters.Length; k++)
                            parts.Add(tocCounters[k].ToString(System.Globalization.CultureInfo.InvariantCulture));
                        if (parts.Count > 0) prefix = string.Join(".", parts) + "  ";
                    }
                    // A heading authored through SEGMENTS (Heading.Text left empty)
                    // still titles its TOC entry — the entry text is the segment
                    // chain's concatenation.
                    var headingText = h.Text;
                    if (string.IsNullOrEmpty(headingText) && h.Segments.Count > 0)
                    {
                        var segText = new System.Text.StringBuilder();
                        foreach (Text.TextSegment hseg in h.Segments) segText.Append(hseg.Text);
                        headingText = segText.ToString();
                    }
                    var text = prefix + (headingText ?? string.Empty);
                    // A segment carrying its OWN font measures the entry — and its
                    // leader dots and page number — by that font's REAL advances:
                    // the column-fit scale is computed in the entry font's metrics,
                    // and positions are pinned to fractions of a point of those
                    // advances (a Standard-14 approximation lands dots a few
                    // hundredths of a point astray).
                    System.Func<string, double>? entryAdvance = null;
                    Text.Font? segFont0 = null;   // the collection indexer is 1-based
                    foreach (Text.TextSegment hseg0 in h.Segments) { segFont0 = hseg0.TextState.Font; break; }
                    if (segFont0?.SourceFontData?.TtfData is { Length: > 0 } segTtf)
                    {
                        try
                        {
                            var segParser = new Text.GlyphOutlineParser(segTtf);
                            if (segParser.CMap.Count > 0)
                            {
                                double upm = segParser.UnitsPerEm;
                                var sizeForMeasure = entrySize;
                                entryAdvance = s =>
                                {
                                    double units = 0;
                                    for (var ci = 0; ci < s.Length; ci++)
                                    {
                                        int cp = s[ci];
                                        if (char.IsHighSurrogate(s[ci]) && ci + 1 < s.Length
                                            && char.IsLowSurrogate(s[ci + 1]))
                                        { cp = char.ConvertToUtf32(s[ci], s[ci + 1]); ci++; }
                                        units += segParser.CMap.TryGetValue(cp, out var gid) && gid > 0
                                            ? segParser.GetAdvanceWidth(gid)
                                            : upm / 2.0;
                                    }
                                    return units / upm * sizeForMeasure;
                                };
                            }
                        }
                        catch { /* unparsable program: Standard-14 measure */ }
                    }
                    var pageNumStr = destIdx > 0
                        ? destIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty;
                    var pageNumWidth = MeasureEntry(pageNumStr, entrySize, entryFace);

                    double colLeft = tocColLefts[tocCol], colWidth = tocColWidths[tocCol];
                    // Entries are NOT indented by heading level — every level starts
                    // at the column left edge (plus any explicit level-
                    // format left margin).
                    var indent = colLeft + (double)(fmt?.Margin?.Left ?? 0);
                    var rightStop = colLeft + colWidth - (double)(fmt?.Margin?.Right ?? 0);
                    var pageNumX = rightStop - pageNumWidth;

                    // Wrap the entry, honouring explicit line breaks in the heading
                    // text (\r\n) first and width-wrapping each. Every rendered line
                    // after the entry's first is indented by SubsequentLinesIndent, so
                    // its available width is correspondingly narrower.
                    List<(string text, double x)> WrapEntryLines()
                    {
                        var outLines = new List<(string, double)>();
                        var logical = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                        foreach (var ll in logical)
                            foreach (var w in WrapEntry(ll, pageNumX - 6 - (outLines.Count == 0 ? indent : indent + subIndent), entrySize, entryFace))
                                outLines.Add((w, outLines.Count == 0 ? indent : indent + subIndent));
                        return outLines;
                    }
                    var wrapped = WrapEntryLines();

                    // Out of vertical room — the entry's line boxes plus its own
                    // bottom margin must fit above the page's bottom margin (an
                    // entry with Margin.Bottom=300 overflows even when its single
                    // line alone would fit). Move to the next column for a
                    // multi-column TOC; when the columns are exhausted, continue
                    // on the next CONTINUATION page (one is inserted
                    // after the TOC page — entries are never truncated).
                    var entryMarginBottom = (double)(fmt?.Margin?.Bottom ?? h.Margin?.Bottom ?? 0);
                    // Fit rule: the entry stays when its LAST line's ink bottom
                    // (baseline minus the font descent) plus the entry's own
                    // bottom margin clears the page bottom margin — a 45-entry
                    // A4-landscape page keeps a line whose bottom lands 1 pt
                    // above the margin; requiring a full
                    // line height of clearance breaks one entry early.
                    if (entryY - (wrapped.Count - 1) * lineH - descFrac * entrySize - entryMarginBottom < marginBottom)
                    {
                        if (tocCol + 1 >= tocColCount)
                        {
                            tocSlot++;
                            tocCol = 0;
                            // The continuation page has no title: its entry chain
                            // anchors at the page's top margin cursor, first entry
                            // one pitch below it (LLY = top − pitch, same rule as
                            // any other line).
                            tocTopY = page.Height - marginTop;
                            entryY = tocTopY.Value
                                - (fmt?.Margin?.Top ?? h.Margin?.Top ?? 0)
                                - lineH + descFrac * entrySize;
                        }
                        else
                        {
                            tocCol++;
                            entryY = tocTopY.Value - lineH + descFrac * entrySize;
                        }
                        colLeft = tocColLefts[tocCol];
                        colWidth = tocColWidths[tocCol];
                        indent = colLeft + (double)(fmt?.Margin?.Left ?? 0);
                        rightStop = colLeft + colWidth - (double)(fmt?.Margin?.Right ?? 0);
                        pageNumX = rightStop - pageNumWidth;
                        wrapped = WrapEntryLines();
                    }

                    // All entry pieces are positioned with Tm (absolute text matrix),
                    // not Td: column repositioning on one visual row goes out
                    // via Tm, and RAW extraction merges same-row Tm shows into
                    // one output line ("1  Heading 1.....2"), while Td-per-BT blocks
                    // stay one-line-per-show (table cells). This operator
                    // choice lets the extractor reassemble the entry as one
                    // line without loosening any extraction heuristics.
                    var b = new Content.ContentStreamBuilder();
                    var entryFontRes = entryFace == "Helvetica"
                        ? fontName : Table.RegisterFont(page, entryFace);
                    void TmShow(double x, double y, string s)
                    {
                        // CJK entry text (Japanese outline titles and the like) has no
                        // glyphs in the Standard-14 set — embed a script-matched CJK
                        // face and show the run as CID hex.
                        if (s.Length > 0 && ContainsCjkText(s))
                        {
                            tocCjkTtf ??= Text.CjkFallbackFont.ResolveEmbeddableBytes(s);
                            if (tocCjkTtf is { Length: > 0 })
                            {
                                var cjkDict = Table.ResolvePageFontDict(page);
                                var (cjkRes, cjkHex) = Text.Type0FontEmbedder.Embed(
                                    cjkDict, tocCjkTtf, "CJK", s.Replace('\t', ' '),
                                    stripSpacesInBaseFont: true);
                                b.BeginText().SetFont(cjkRes, entrySize).SetFillColor(0, 0, 0)
                                    .SetTextMatrix(1, 0, 0, 1, x, y).ShowTextHex(cjkHex).EndText();
                                return;
                            }
                        }
                        b.BeginText().SetFont(entryFontRes, entrySize).SetFillColor(0, 0, 0)
                            .SetTextMatrix(1, 0, 0, 1, x, y).ShowText(s).EndText();
                    }
                    // Every entry opens with an EMPTY text show at the
                    // line start (extraction reports an empty fragment before each
                    // entry's text), keeping fragment counts and order stable.
                    TmShow(wrapped[0].x, entryY, string.Empty);
                    // A leadered, numbered entry defers its FINAL line to the leader
                    // emission: that line is drawn horizontally scaled to the column
                    // (Tz), and the scale depends on the page-number width — which is
                    // only known after the final page sequence exists.
                    var deferFinal = entryLeader != Text.TabLeaderType.None
                        && page.TocInfo?.IsShowPageNumbers != false
                        && fmtState?.Underline != true;
                    for (var li = 0; li < wrapped.Count; li++)
                    {
                        var finalLine = li == wrapped.Count - 1;
                        // The first line carries the auto-number as its OWN show ("1  "
                        // digit + two spaces, the prefix fragment) with the
                        // heading text show starting exactly at the prefix's end.
                        if (li == 0 && prefix.Length > 0 && wrapped[0].text.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            TmShow(wrapped[0].x, entryY, prefix);
                            if (!(deferFinal && finalLine))
                                TmShow(wrapped[0].x + MeasureEntry(prefix, entrySize, entryFace), entryY,
                                    wrapped[0].text.Substring(prefix.Length));
                            continue;
                        }
                        if (deferFinal && finalLine) continue;
                        TmShow(wrapped[li].x, entryY - li * lineH, wrapped[li].text);
                    }

                    var lastY = entryY - (wrapped.Count - 1) * lineH;
                    var lastLineW = MeasureEntry(wrapped[^1].text, entrySize, entryFace);

                    // The entry's link box spans the whole entry: height is exactly
                    // the rendered-line count times the line PITCH (2×29 = 58 pt
                    // for a two-line 11 pt entry with 18 pt line
                    // spacing), top where the entry's box started, bottom at the last
                    // line's rect bottom, right edge on the column's right stop.
                    var linkTop = entryY - descFrac * entrySize + lineH;
                    var linkRect = new Rectangle(indent, linkTop - wrapped.Count * lineH,
                        colLeft + colWidth, linkTop);

                    // Emission is deferred (see tocPending above): the leader's page
                    // number must reflect the FINAL page sequence, which isn't known
                    // until every entry is laid out and the continuation pages are
                    // inserted. The pre-leader shows (empty + prefix + lines) are
                    // final bytes already.
                    tocPending.Add((tocSlot, b.Build(), wrapped[^1].x + lastLineW, lastY,
                        entrySize, entryFace, entryLeader, rightStop,
                        page.TocInfo?.IsShowPageNumbers != false,
                        fmtState?.Underline == true,
                        prefix, wrapped[0].x,
                        h.DestinationPage, destIdx, linkRect, h,
                        deferFinal ? wrapped[^1].text : string.Empty, wrapped[^1].x,
                        entryAdvance));

                    // The next entry (or paragraph) continues from this entry's last
                    // line-box BOTTOM (baseline minus descent) — the next entry then
                    // subtracts its OWN pitch, chaining rect bottoms one pitch apart.
                    // Margin.Bottom resolves like Margin.Top: level format first, then
                    // the heading's own margin (which can space entries 300 pt apart
                    // via heading.Margin.Bottom with no FormatArray set).
                    return lastY - descFrac * entrySize - entryMarginBottom;
                }

                var flow = new FlowLayout(page, overflowPages, marginLeft, marginRight, marginTop, marginBottom, curY, EnableNotificationLogging);
                var flowSlotStart = overflowPages.Count;
                var tb = new Text.TextBuilder(page);
                // Height of the current run of inline images (IsInLineParagraph). Inline
                // images share one line and the cursor only drops by the tallest of them
                // once the line ends (a block image or a flush at end-of-flow).
                double pendingInlineLineHeight = 0;
                // The same Table INSTANCE added to Paragraphs more than once lays out
                // a single time (re-adding is a no-op, not a
                // second copy of the table).
                var renderedTables = new HashSet<Table>(ReferenceEqualityComparer.Instance);
                var paraList = page.Paragraphs.ToList();
                for (var paraIdx = 0; paraIdx < paraList.Count; paraIdx++)
                {
                    var para = paraList[paraIdx];
                    // Close an open inline image line before any paragraph that isn't
                    // itself an inline image — the cursor drops by the tallest inline
                    // image so this paragraph starts on the next line.
                    if (pendingInlineLineHeight > 0
                        && !(para is Image inlineImg && inlineImg.IsInLineParagraph))
                    {
                        flow.AdvanceY(pendingInlineLineHeight);
                        pendingInlineLineHeight = 0;
                    }

                    // IsInNewPage forces this paragraph to start on a fresh overflow
                    // page, regardless of remaining room on the current one. Honors
                    // the BaseParagraph.IsInNewPage flag set on headings, paragraphs,
                    // and tables when the caller wants explicit pagination.
                    // ForceNewPage (eager) is required for renderers that bypass the
                    // Y cursor (Heading.Build draws at the supplied Y verbatim, so
                    // just resetting the cursor would leave the heading on the
                    // current page).
                    if (ParagraphIsInNewPage(para) && para != page.Paragraphs[0])
                        flow.ForceNewPage();

                    // Record this paragraph's starting position so a later
                    // LocalHyperlink that targets it (e.g. LocalHyperlink(head))
                    // can resolve to the right page + y after overflow pages
                    // have been added to the document.
                    flow.RecordPosition(para);

                    if (TryLayoutInlineJoinedRun(paraList, ref paraIdx, para, flow)) continue;

                    if (para is Text.TextFragment tf)
                    {
                        // NoCharacterAction.ReplaceFonts (explicit): substitute a glyph-covering
                        // face before layout when the fragment's font can't show its text —
                        // registered sources first, then host Arial, then a system CJK face.
                        if (tf.HasExplicitReplaceFonts &&
                            Text.FontRepository.SubstituteForMissingGlyphs(tf.Text, tf.TextState.Font) is { } replaceFace)
                            tf.TextState.Font = replaceFace;
                        // Arabic/RTL text: shape into contextual presentation forms and route
                        // through an Arabic-capable embedded font (the default Standard-14 font
                        // has no Arabic coverage and the renderer applies no OpenType shaping).
                        ShapeArabicForGenerator(tf);
                        // Replace page number macros
                        if (tf.Text.Contains("$p") || tf.Text.Contains("$P"))
                        {
                            tf.Text = tf.Text
                                .Replace("$p", page.Number.ToString())
                                .Replace("$P", PageCount.ToString());
                        }
                        // IsKeptWithNext + a following Table: when the pair no longer
                        // fits the space left on this page (but would fit a fresh one)
                        // both move together — the new page starts with
                        // the fragment at the content top (its top margin is consumed
                        // by the break).
                        if (tf.IsKeptWithNext && paraIdx + 1 < paraList.Count
                            && paraList[paraIdx + 1] is Table keptTable)
                        {
                            var keptH = keptTable.GetHeight();
                            var tfH = (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12)
                                + (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
                            if (flow.CurrentY - tfH - keptH < flow.BottomMargin
                                && tfH + keptH <= flow.ContentTop - flow.BottomMargin + 0.5)
                            {
                                flow.ForceNewPage();
                                // The fragment keeps its top margin on the new page
                                // (the kept title sits one Margin.Top below
                                // the content top, not flush against it).
                            }
                        }
                        // A fragment's own top margin is vertical space reserved above it;
                        // dropping the cursor by it (which paginates when it overflows the
                        // page) is what places a fragment with a large Margin.Top onto a
                        // later page instead of pinning it to the current one.
                        var tfTopMargin = tf.Margin?.Top ?? 0;
                        if (tfTopMargin > 0) flow.AdvanceY(tfTopMargin);
                        if (!flow.WriteTextFragment(tf))
                        {
                            // Flow layout declined (e.g. explicit Position or embedded font) —
                            // fall back to the legacy fixed-position writer. Assign a default
                            // layout position only when the caller didn't set one (the Position
                            // getter is never null now, so test HasExplicitPosition, not `??=`).
                            if (!tf.HasExplicitPosition)
                                tf.Position = new Text.Position(
                                    marginLeft, page.Height - marginTop - (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12));
                            tb.AppendText(tf);
                        }
                        var tfBottomMargin = tf.Margin?.Bottom ?? 0;
                        if (tfBottomMargin > 0) flow.AdvanceY(tfBottomMargin);
                        // FootNote marker + page-bottom band are emitted inside
                        // WriteTextFragment (the marker needs the last line's
                        // measured end position).
                    }
                    else if (para is HtmlFragment html)
                    {
                        LayoutHtmlFragmentParagraph(html, flow, page, tb, renderedTables, overflowPages, overflowImages, marginLeft, marginRight, marginTop, marginBottom);
                    }
                    else if (para is Table table)
                    {
                        LayoutTableParagraph(table, flow, page, renderedTables, overflowPages, overflowImages, marginLeft, marginTop);
                    }
                    else if (para is FloatingBox fbox)
                    {
                        LayoutFloatingBoxParagraph(fbox, flow, page, tocEntries, RenderTocEntry, headingAutoCounters, ref fontName, overflowPages, marginLeft, marginRight, marginBottom);
                    }
                    else if (para is Heading heading)
                    {
                        LayoutHeadingParagraph(heading, flow, page, tocEntries, RenderTocEntry, headingAutoCounters, ref fontName, marginLeft, marginRight);
                    }
                    else if (para is Image img)
                    {
                        LayoutImageParagraph(img, flow, page, ref pendingInlineLineHeight, marginLeft, marginRight, marginTop, marginBottom);
                    }
                    else if (para is Aspose.Pdf.Drawing.Graph graph)
                    {
                        LayoutGraphParagraph(graph, flow, page, marginLeft, marginTop, marginBottom);
                    }
                    else if (para is Annotations.Annotation annPara)
                    {
                        // An annotation added through Paragraphs.Add flows like a block
                        // paragraph: it reserves its Width×Height at the left content
                        // edge with no inter-paragraph gap (Line/Ink rectangles derive
                        // from their authored geometry via the ctor), and binds to the
                        // page its block landed on once overflow slots materialise.
                        flow.PlaceAnnotationBlock(annPara, annPara.Width, annPara.Height);
                    }
                }
                if (pendingInlineLineHeight > 0)
                {
                    flow.AdvanceY(pendingInlineLineHeight);
                    pendingInlineLineHeight = 0;
                }
                // TOC entries whose headings live on OTHER pages (they are not
                // paragraphs of this page, so the flow above never reached them):
                // append them after the page's own content, chaining the cursor.
                foreach (var (h, dIdx) in tocEntries)
                {
                    if (tocRendered.Contains(h)) continue;
                    var yAfter = RenderTocEntry(h, dIdx, flow.CurrentY);
                    flow.AdvanceY(flow.CurrentY - yAfter);
                }
                // Materialise the TOC: insert the continuation pages the overflow
                // asked for right AFTER the TOC page (an overlong TOC
                // splits across pages inserted in place, shifting the
                // content pages down), then emit every buffered entry — its
                // pre-leader shows first, then the leader built against the FINAL
                // page numbering (dots + destination number as one show flush at
                // the text end, right edge on the column stop, floor dot count,
                // slack spread as character spacing), then its link annotation.
                if (tocPending.Count > 0)
                {
                    var tocContPages = new List<Page>();
                    if (tocSlot > 0)
                    {
                        var tocPageIdx = Pages.IndexOf(page);
                        for (var ci = 1; ci <= tocSlot; ci++)
                        {
                            var cont = Pages.Insert(tocPageIdx + ci, page.Width, page.Height);
                            // The buffered entry bytes reference the TOC page's font
                            // resource name — register Helvetica on the continuation
                            // page and alias it under that exact name if the fresh
                            // page happened to assign a different one.
                            var contFontName = Table.RegisterFont(cont);
                            if (fontName is not null && contFontName != fontName)
                            {
                                var contRes = cont.Reader.ResolveDict(cont.Dict.Get("Resources"))!;
                                var contFonts = cont.Reader.ResolveDict(contRes.Get("Font"))!;
                                contFonts.Set(fontName, contFonts.Get(contFontName)!);
                            }
                            cont.LayoutApplied = true;
                            tocContPages.Add(cont);
                        }
                    }
                    pendingTocEmits.Add((page, fontName!, tocContPages, tocPending));
                }
                flow.Commit();
                // FinaliseFootnotes runs before slotEnd capture so its spillover
                // pages (added via _overflowPages) extend this flow's slot range.
                flow.FinaliseFootnotes();
                flow.FinaliseMarkedFootnotes();
                pendingFlows.Add((flow, flowSlotStart, overflowPages.Count));
                // Remember where this pass left the cursor: on the original page when
                // the flow never spilled, otherwise on the flow's final overflow slot
                // (persisted onto the Page object when the slot materialises below).
                if (flow.CurrentSlot < 0)
                    page.LayoutCursorY = flow.CurrentY;
                page.Paragraphs.Clear();
            }

            // Header/Footer are already rendered once per page by the self-guarding
            // RenderToPage block above (which uses page.Number for '#' substitution).
            // Re-applying them here stamped a second copy onto every freshly laid-out
            // page, so it is intentionally not repeated.

            // Apply a watermark set through Page.Watermark as an artifact. Use the
            // set-only PendingWatermark (not the Watermark getter, which now *detects*
            // an already-present watermark from the content) so re-saving a document
            // that already carries a watermark doesn't stamp a second copy.
            if (page.PendingWatermark is { Available: true, Image: { } wmImage })
                new WatermarkArtifact { SourceImage = wmImage }.AddToPage(page);

            page.LayoutApplied = true;
        }


        var overflowPageRefs = AddOverflowPages(overflowPages, overflowImages);
        FinaliseDeferredFlows(pendingFlows, overflowPageRefs);
        EmitDeferredTocLeaders(pendingTocEmits, pendingFlows, overflowPageRefs);
    }

    private List<Page> AddOverflowPages(
        List<(byte[] content, double width, double height)> overflowPages,
        Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages)
    {
        // Add overflow pages (from multi-page table layout) after iteration.
        // Track the Page created for each slot so deferred link annotations
        // (per-segment hyperlinks queued by FlowLayout) can resolve to the
        // page they actually landed on.
        var overflowPageRefs = new List<Page>(overflowPages.Count);
        for (var slot = 0; slot < overflowPages.Count; slot++)
        {
            var (content, width, height) = overflowPages[slot];
            var newPage = Pages.Add();
            newPage.MediaBox = new Rectangle(0, 0, width, height);
            Table.RegisterFont(newPage);
            newPage.AddContentStream(content);
            if (overflowImages.TryGetValue(slot, out var imgs))
                foreach (var (data, rect) in imgs)
                    newPage.AddImage(data, rect);
            overflowPageRefs.Add(newPage);
        }
        return overflowPageRefs;
    }

    private void FinaliseDeferredFlows(
        List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows,
        List<Page> overflowPageRefs)
    {
        // Resolve every flow's deferred link annotations + embedded-font renders
        // against the final page sequence -- each flow owns the slice of
        // overflowPageRefs captured at its commit time. Embedded renders go
        // first so TextBuilder gets a clean page before any annotation rects
        // overlay (incidental: AddContentStream order doesn't matter for the
        // saved PDF, but it keeps the dev mental model "render then annotate").
        foreach (var (flow, slotStart, slotEnd) in pendingFlows)
        {
            var pageRange = new List<Page>(slotEnd - slotStart);
            for (var i = slotStart; i < slotEnd; i++) pageRange.Add(overflowPageRefs[i]);
            // The flow's final cursor lives on its LAST overflow page: paragraphs
            // added to that page later resume below the spilled content.
            if (flow.CurrentSlot >= 0 && pageRange.Count > 0)
                pageRange[^1].LayoutCursorY = flow.CurrentY;
            // Pre-built table slices reference fonts embedded in the START page's
            // font dictionary (e.g. Type0 serif runs of an HTML-engine cell); a
            // freshly materialised overflow page only has the standard table font.
            // Merge the start page's font entries so those resource names resolve.
            foreach (var op in pageRange)
                MergePageFontResources(flow.CurrentPage, op);
            flow.FinaliseEmbeddedRenders(pageRange);
            flow.FinaliseNotifications(pageRange);
            flow.FinaliseAnnotations(pageRange);
            flow.FinaliseFormFields(pageRange, this);
            flow.FinaliseRules(pageRange);
            // A page watermark repeats on the overflow pages its content spilled onto.
            if (flow.CurrentPage.PendingWatermark is { Available: true, Image: { } fwmImage })
                foreach (var op in pageRange)
                    new WatermarkArtifact { SourceImage = fwmImage }.AddToPage(op);

            // A background artifact likewise repeats on every overflow page of the
            // flow (an overflowing page keeps its background image).
            foreach (var srcArt in flow.CurrentPage.Artifacts)
                if (srcArt is BackgroundArtifact bgArt)
                    foreach (var op in pageRange)
                        bgArt.RenderToPage(op);

            // A running Header/Footer likewise repeats on every overflow page of the flow, not
            // just the originating page (which was stamped in the main loop). Freshly-materialised
            // overflow pages carry no Header/Footer of their own, so render the source page's.
            var hfSource = flow.CurrentPage;
            if (hfSource.Header is not null || hfSource.Footer is not null)
                foreach (var op in pageRange)
                {
                    if (op.HeaderFooterApplied) continue;
                    op.HeaderFooterApplied = true;
                    hfSource.Header?.RenderToPage(op, isHeader: true, op.Number, this);
                    hfSource.Footer?.RenderToPage(op, isHeader: false, op.Number, this);
                }
        }
    }

    private void EmitDeferredTocLeaders(
        List<(Page tocPage, string fontName, List<Page> contPages,
        System.Collections.Generic.List<(int slot, byte[] preLeader, double textEnd, double lastY,
            double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop,
            bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx,
            Rectangle linkRect, Heading heading, string lastLine, double lastX,
            System.Func<string, double>? measure)> entries)> pendingTocEmits,
        List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows,
        List<Page> overflowPageRefs)
    {
        // Emit the deferred TOC leaders + link annotations against the FINAL
        // page sequence: only now is it known which page each heading actually
        // rendered on (content pagination and IsInNewPage move headings onto
        // overflow pages materialised above).
        foreach (var (tocPage, tocFontName, tocContPages, tocEntriesPending) in pendingTocEmits)
        {
            var tocPageIdxFinal = Pages.IndexOf(tocPage);

            // The page a heading FINALLY landed on: the flow that laid it out
            // recorded its slot; map the slot through that flow's overflow range.
            int FinalHeadingIdx(Heading h)
            {
                foreach (var (flow, slotStart, slotEnd) in pendingFlows)
                {
                    if (!flow.TryGetParagraphPosition(h, out var pos)) continue;
                    var hp = pos.slot < 0 ? flow.CurrentPage
                        : slotStart + pos.slot < slotEnd ? overflowPageRefs[slotStart + pos.slot] : null;
                    return hp is not null ? Pages.IndexOf(hp) : 0;
                }
                return 0;
            }

            foreach (var rec in tocEntriesPending)
            {
                var target = rec.slot == 0 ? tocPage : tocContPages[rec.slot - 1];
                if (rec.preLeader.Length > 0) target.AddContentStream(rec.preLeader);
                var destIdx = rec.destPage is not null ? Pages.IndexOf(rec.destPage) : 0;
                if (destIdx <= 0) destIdx = FinalHeadingIdx(rec.heading);
                if (destIdx <= 0)
                    destIdx = rec.fallbackIdx > tocPageIdxFinal
                        ? rec.fallbackIdx + tocContPages.Count
                        : rec.fallbackIdx;
                if (destIdx <= 0) continue;

                // IsCountTocPages=false: the printed numbers skip the TOC chain
                // itself (TOC page + its continuations) — the first content page
                // after a one-page TOC prints as "1" — while the GoTo link keeps
                // the physical index.
                var displayIdx = destIdx;
                if (tocPage.TocInfo?.IsCountTocPages == false)
                {
                    var chainEnd = tocPageIdxFinal + tocContPages.Count;
                    var sub = destIdx > chainEnd ? tocContPages.Count + 1
                        : destIdx >= tocPageIdxFinal ? destIdx - tocPageIdxFinal + 1 : 0;
                    if (destIdx - sub >= 1) displayIdx = destIdx - sub;
                }
                var pageNumStr = displayIdx.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Entries whose segment declared its own font measure through that
                // font's real advances (see the layout-side comment).
                double M(string s) => rec.measure?.Invoke(s)
                    ?? MeasureEntry(s, rec.entrySize, rec.entryFace);
                var pageNumWidth = M(pageNumStr);
                // The leader fill glyph: '.' for a Dot leader, '_' for a Solid one
                // (underscore advances abut, so the run reads as a continuous rule).
                var leaderChar = rec.leader == Text.TabLeaderType.Solid ? '_' : '.';
                var dotW = M(leaderChar.ToString());
                var leaderFontRes = rec.entryFace == "Helvetica"
                    ? tocFontName : Table.RegisterFont(target, rec.entryFace);
                var lb = new Content.ContentStreamBuilder();
                // The entry's underlined variant draws filled 0.5 pt rectangles
                // under each run (prefix at natural width, text to its end).
                if (rec.underline)
                {
                    var uy = rec.lastY - 0.207 * rec.entrySize + 0.207;
                    var prefixNat = MeasureEntry(rec.prefix, rec.entrySize, rec.entryFace);
                    if (prefixNat > 0)
                        lb.SaveState().SetFillColor(0, 0, 0)
                            .Rectangle(rec.x0, uy, prefixNat, 0.5).FillEvenOdd().RestoreState();
                    lb.SaveState().SetFillColor(0, 0, 0)
                        .Rectangle(rec.x0 + prefixNat, uy, rec.textEnd - rec.x0 - prefixNat, 0.5)
                        .FillEvenOdd().RestoreState();
                }

                // One text show at (x, lastY), horizontally scaled by tz percent.
                // CJK runs (no glyphs in the Standard-14 set) embed a script-matched
                // face and show as CID hex, exactly like the entry's earlier lines.
                void LeaderShow(double x, string s, double tz)
                {
                    if (s.Length == 0) return;
                    var scaled = System.Math.Abs(tz - 100) > 1e-9;
                    if (s.Length > 0 && ContainsCjkText(s)
                        && Text.CjkFallbackFont.ResolveEmbeddableBytes(s) is { Length: > 0 } cjkTtf)
                    {
                        var cjkDict = Table.ResolvePageFontDict(target);
                        var (cjkRes, cjkHex) = Text.Type0FontEmbedder.Embed(
                            cjkDict, cjkTtf, "CJK", s.Replace('\t', ' '),
                            stripSpacesInBaseFont: true);
                        lb.BeginText().SetFont(cjkRes, rec.entrySize).SetFillColor(0, 0, 0)
                            .SetTextMatrix(1, 0, 0, 1, x, rec.lastY);
                        if (scaled) lb.SetHorizontalScaling(tz);
                        lb.ShowTextHex(cjkHex);
                        // Tz is graphics state — reset before closing the block.
                        if (scaled) lb.SetHorizontalScaling(100);
                        lb.EndText();
                        return;
                    }
                    lb.BeginText().SetFont(leaderFontRes, rec.entrySize).SetFillColor(0, 0, 0)
                        .SetTextMatrix(1, 0, 0, 1, x, rec.lastY);
                    if (scaled) lb.SetHorizontalScaling(tz);
                    lb.ShowText(s);
                    if (scaled) lb.SetHorizontalScaling(100);
                    lb.EndText();
                }

                if (rec.lastLine.Length > 0 && dotW > 0)
                {
                    // The entry's final line is drawn HERE, horizontally scaled to fit
                    // the column: the numbering prefix paints unscaled at the line
                    // start, while the text, the leader dots and the page number
                    // carry one shared Tz. With
                    //   q = (colW − prefixW − textW − numW) / dotW,
                    //   D = round(q) − 1,
                    //   Tz = 100·colW / (prefixW + textW + D·dotW + numW),
                    // the dots+number show starts flush at the SCALED text end — so
                    // RAW extraction reads "…Heading 1....2" with no space, and a
                    // CJK entry's dots begin just right of where its natural width
                    // ends. The prefix sits in the denominator but paints unscaled
                    // (numbered lines end a hair short of the right stop).
                    var linePrefix = rec.prefix.Length > 0
                        && rec.lastLine.StartsWith(rec.prefix, StringComparison.Ordinal)
                        ? rec.prefix : string.Empty;
                    var lineText = rec.lastLine.Substring(linePrefix.Length);
                    var prefixW = M(linePrefix);
                    var textW = M(lineText);
                    var colW = rec.rightStop - rec.lastX;
                    var q = (colW - prefixW - textW - pageNumWidth) / dotW;
                    var dotCount = (int)System.Math.Round(q, System.MidpointRounding.AwayFromZero) - 1;
                    if (dotCount < 0) dotCount = 0;
                    var natural = prefixW + textW + dotCount * dotW + pageNumWidth;
                    var tz = natural > 0 ? 100.0 * colW / natural : 100.0;
                    LeaderShow(rec.lastX + prefixW, lineText, tz);
                    LeaderShow(rec.lastX + prefixW + textW * tz / 100.0,
                        new string(leaderChar, dotCount) + pageNumStr, tz);
                }
                else
                {
                    // Legacy right-aligned model for the paths the scaled draw does
                    // not cover: TabLeaderType.None keeps the page number alone on
                    // the column stop; IsShowPageNumbers=false closes the entry with
                    // an empty show at the text end (fragment parity); an underlined
                    // entry keeps its natural-width text (drawn with the entry) and
                    // right-aligns its leader.
                    var remainder = rec.rightStop - rec.textEnd - pageNumWidth;
                    var dotCount = dotW > 0 && remainder > 0
                        ? (int)System.Math.Round(remainder / dotW, System.MidpointRounding.AwayFromZero) - 1
                        : 0;
                    if (dotCount < 0) dotCount = 0;
                    if (rec.leader == Text.TabLeaderType.None) dotCount = 0;
                    var leaderText = rec.showNumbers
                        ? new string(leaderChar, dotCount) + pageNumStr : string.Empty;
                    var drawStart = rec.showNumbers
                        ? rec.rightStop - pageNumWidth - dotCount * dotW : rec.textEnd;
                    lb.BeginText().SetFont(leaderFontRes, rec.entrySize).SetFillColor(0, 0, 0)
                        .SetTextMatrix(1, 0, 0, 1, drawStart, rec.lastY)
                        .ShowText(leaderText)
                        .EndText();
                }
                target.AddContentStream(lb.Build());

                // The GoTo destination is the top-left of the target page
                // (0, page-height) mapped back through its rotation —
                // matching the heading-branch links, so validators that
                // expect the corner destination accept the TOC link too.
                var destPage = Pages.At(destIdx);
                var destRect = destPage.GetPageRect(true);
                var (destLeft, destTop) = destPage.RotationMatrix
                    .InverseTransformPoint(0, destRect.Height);
                target.Annotations.AddLinkAnnotation(rec.linkRect,
                    new Aspose.Pdf.Annotations.GoToAction(
                        new Aspose.Pdf.Annotations.XYZExplicitDestination(destIdx, destLeft, destTop, 0)));
            }
        }
    }
}
