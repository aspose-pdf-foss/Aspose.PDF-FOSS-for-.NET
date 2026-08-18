using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a header or footer that can be applied to PDF pages.
/// Supports page number substitution: use '#' in the text to insert the 1-based page number.
/// </summary>
public sealed class HeaderFooter
{
    /// <summary>The text content. Use '#' as a placeholder for the page number.</summary>
    public string Text { get; set; } = "";

    /// <summary>Text formatting state (font, size, color).</summary>
    public TextState TextState { get; set; } = new()
    {
        FontName = "Helvetica",
        FontSize = 10,
        ForegroundColor = Color.Black,
    };

    /// <summary>Margins controlling the position of the header/footer text.
    /// Untouched sides resolve to path-specific defaults at stamp time: the
    /// plain-text stamp keeps the legacy 20 pt band, while the Paragraphs
    /// render uses left = the page's content left margin and header top = 0
    /// (a header paragraph starts at the physical page top).</summary>
    public MarginInfo Margin { get; set; } = new();

    /// <summary>Horizontal alignment of the text.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Center;

    /// <summary>
    /// Collection of paragraph objects (TextFragment, HtmlFragment, etc.) to render in the header/footer.
    /// When populated, these are used instead of <see cref="Text"/>.
    /// </summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Whether content overflowing the header/footer area is clipped. Stored only.</summary>
    public bool IsClipExtraContent { get; set; }

    /// <summary>
    /// Create a HeaderFooter from a text string.
    /// </summary>
    /// <param name="text">Text content. Use '#' for page number substitution.</param>
    public static HeaderFooter FromText(string text)
    {
        return new HeaderFooter { Text = text };
    }

    /// <summary>
    /// Shallow clone — copies <see cref="Text"/>/<see cref="TextState"/>/
    /// <see cref="Margin"/>/<see cref="HorizontalAlignment"/> plus shallow-copied
    /// references to every entry in <see cref="Paragraphs"/>. Same-content-on-every-page
    /// usage requires this; otherwise cloned headers/footers would render blank.
    /// Returns <see cref="object"/> to keep the published reflection shape.
    /// </summary>
    public object Clone()
    {
        var copy = new HeaderFooter
        {
            Text = Text,
            TextState = TextState,
            Margin = Margin,
            HorizontalAlignment = HorizontalAlignment,
            IsClipExtraContent = IsClipExtraContent,
        };
        foreach (var p in Paragraphs)
            copy.Paragraphs.Add(p);
        return copy;
    }

    /// <summary>
    /// Apply this header/footer as a header (positioned at the top) to the given pages.
    /// </summary>
    /// <param name="pages">Pages to stamp.</param>
    /// <param name="substitutePageNumbers">
    /// When true, '#' in the text is replaced with the 1-based page number.
    /// </param>
    public void ApplyAsHeader(Page[] pages, bool substitutePageNumbers = true)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var text = substitutePageNumbers ? Text.Replace("#", (i + 1).ToString()) : Text;
            StampText(page, text, isHeader: true);
        }
    }

    /// <summary>
    /// Apply this header/footer as a footer (positioned at the bottom) to the given pages.
    /// </summary>
    /// <param name="pages">Pages to stamp.</param>
    /// <param name="substitutePageNumbers">
    /// When true, '#' in the text is replaced with the 1-based page number.
    /// </param>
    public void ApplyAsFooter(Page[] pages, bool substitutePageNumbers = true)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var text = substitutePageNumbers ? Text.Replace("#", (i + 1).ToString()) : Text;
            StampText(page, text, isHeader: false);
        }
    }

    /// <summary>Render this header/footer onto a page that referenced it through
    /// <see cref="Page.Header"/> / <see cref="Page.Footer"/>. Substitutes '#' in
    /// <see cref="Text"/> with the 1-based page number, then emits the text or
    /// paragraph content at the top (header) or bottom (footer) margin.
    /// <paramref name="paragraphContent"/> gates TABLE paragraphs only:
    /// a header/footer table belongs to the page generator and draws only on
    /// generator-laid-out pages — a footer table assigned to the static pages
    /// of an imported document stays undrawn, while text/HTML fragments (and
    /// the plain <see cref="Text"/> stamp) render everywhere.</summary>
    internal void RenderToPage(Page page, bool isHeader, int pageNumber, Document? document = null,
        bool paragraphContent = true)
    {
        // An imported page can leave a persistent CTM active at the end of its
        // content (e.g. a top-level y-flip); bracket it so the header/footer
        // draws in default page space.
        page.IsolateExistingContent();
        var text = Text.Replace("#", pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text = ApplyLabelMacros(text, document, pageNumber);
        StampText(page, text, isHeader, document, pageNumber, paragraphContent);
    }

    /// <summary>Resolve the page-label macros <c>$p</c> (this page's label) and
    /// <c>$P</c> (the last-page label of this page's label range — the section
    /// total) against the document's /PageLabels. No-op when no document is
    /// supplied. On a document with no labels these degrade to the page number
    /// and the total page count respectively.</summary>
    private static string ApplyLabelMacros(string text, Document? document, int pageNumber)
    {
        if (document is null || string.IsNullOrEmpty(text)) return text;
        if (!text.Contains("$p") && !text.Contains("$P")) return text;
        var idx0 = pageNumber - 1;
        var labels = document.PageLabels;
        var pageCount = document.Pages.Count;
        return text
            .Replace("$P", labels.GetRangeLastLabel(idx0, pageCount))
            .Replace("$p", labels.FormatLabel(idx0));
    }

    private void StampText(Page page, string textContent, bool isHeader,
        Document? document = null, int pageNumber = 0, bool tableContent = true)
    {
        // If we have Paragraphs, render them instead of plain text
        if (Paragraphs.Count > 0)
        {
            StampParagraphs(page, isHeader, document, pageNumber, tableContent);
            return;
        }

        var fontName = TextState.FontName ?? "Helvetica";
        var fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        var fontResName = EnsureFontResource(page, fontName);

        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var textWidth = textContent.Length * fontSize * 0.5;

        // The plain-text stamp keeps its historical 20 pt default band for
        // untouched margins (the Paragraphs path resolves differently).
        var mLeft = Margin.LeftTouched ? Margin.Left : 20;
        var mRight = Margin.RightTouched ? Margin.Right : 20;
        var mTop = Margin.TopTouched ? Margin.Top : 20;
        var mBottom = Margin.BottomTouched ? Margin.Bottom : 20;

        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Left => mLeft,
            HorizontalAlignment.Right => pageWidth - mRight - textWidth,
            _ => (pageWidth - textWidth) / 2,
        };

        var y = isHeader
            ? pageHeight - mTop - fontSize
            : mBottom;

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        var fg = TextState.ForegroundColor;
        if (fg is not null)
            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);

        builder.BeginText()
            .SetFont(fontResName, fontSize)
            .MoveTextPosition(x, y)
            .ShowText(textContent)
            .EndText()
            .RestoreState();

        page.AddContentStream(builder.Build());
    }

    /// <summary>Apply CSS <c>text-transform</c> declared on inline elements to their
    /// text nodes (uppercase/lowercase), so the flattened tag-stripped render keeps
    /// the authored casing ("Planning and Zoning" styled uppercase reads back as
    /// "PLANNING AND ZONING").</summary>
    private static string ApplyTextTransforms(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html,
            @"(?is)<(\w+)\b[^>]*style\s*=\s*(['""])[^'""]*?text-transform\s*:\s*(uppercase|lowercase)[^'""]*\2[^>]*>(.*?)</\1>",
            m =>
            {
                var upper = m.Groups[3].Value.Equals("uppercase", StringComparison.OrdinalIgnoreCase);
                return System.Text.RegularExpressions.Regex.Replace(
                    m.Groups[4].Value, @"(?<=^|>)[^<>]+(?=<|$)",
                    t => upper ? t.Value.ToUpperInvariant() : t.Value.ToLowerInvariant());
            });
    }

    /// <summary>The Generator's default page content margin, which an untouched
    /// header/footer band inherits on the left and right.</summary>
    private const double DefaultBandMargin = 90;

    private void StampParagraphs(Page page, bool isHeader,
        Document? document = null, int pageNumber = 0, bool tableContent = true)
    {
        var pageHeight = page.Height;
        var fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        // Untouched margins resolve to the header-band geometry:
        // left = the page's content left margin (page PageInfo → document
        // PageInfo → the 90 pt Generator default), header top = 0 — a header
        // paragraph's first baseline hangs just below the physical page top.
        var mTop = Margin.TopTouched ? Margin.Top : 0;
        var mBottom = Margin.BottomTouched ? Margin.Bottom : 20;
        var mLeft = Margin.LeftTouched ? Margin.Left
            : page.PageInfo?.Margin is { LeftTouched: true } pm ? pm.Left
            : document?.PageInfo?.Margin is { LeftTouched: true } dm ? dm.Left
            : 90;
        var y = isHeader
            ? pageHeight - mTop - fontSize
            : mBottom + fontSize;
        var x = mLeft;
        // Nothing has been placed in the band yet: the first paragraph seats against
        // the band's own top edge.
        var firstParagraph = true;
        // Baseline and end-X of the last rendered text paragraph, so a following
        // TextFragment with IsInLineParagraph continues on the SAME line directly
        // after it (such fragments render inline, with no gap).
        double lastTextY = double.NaN, lastTextEndX = double.NaN;

        // Surface every LocalHyperlink / WebHyperlink nested in this header
        // or footer's Paragraphs tree as a LinkAnnotation on the page. The
        // header's text rendering itself is handled per-paragraph-type below,
        // but the annotations need to be emitted regardless of which paragraph
        // types are renderable -- a TextFragment buried inside a Table cell
        // would otherwise drop its hyperlink on the floor.
        EmitNestedHyperlinks(page, Paragraphs, x, y, fontSize);

        foreach (var para in Paragraphs)
        {
            // ── Image ──────────────────────────────────────────────────
            // Images in HeaderFooter.Paragraphs (e.g. logos in a page header
            // or footer) need to drop an Image XObject into the page resources
            // and emit a Do reference. The same code path Document.cs uses for
            // page.Paragraphs Image entries, scoped to a HeaderFooter slot.
            // Headers grow downward from y; footers grow upward from the page
            // bottom so the image stays in the visible footer band even with
            // the default 20-pt margins.
            if (para is Image img)
            {
                byte[]? imgData = null;
                if (img.ImageStream is not null)
                {
                    var pos = img.ImageStream.CanSeek ? img.ImageStream.Position : -1L;
                    // Rewind when seekable: callers commonly hand us a stream after
                    // reading dimensions with `new Bitmap(stream)`, which leaves the
                    // position at end-of-stream. Without this the image silently disappears.
                    if (img.ImageStream.CanSeek) img.ImageStream.Position = 0;
                    using var imgMem = new System.IO.MemoryStream();
                    img.ImageStream.CopyTo(imgMem);
                    imgData = imgMem.ToArray();
                    if (pos >= 0) img.ImageStream.Position = pos;
                }
                else if (!string.IsNullOrEmpty(img.File) && System.IO.File.Exists(img.File))
                {
                    imgData = System.IO.File.ReadAllBytes(img.File);
                }
                if (imgData is null || imgData.Length == 0) continue;

                // SVG sources must be rasterised first — Page.AddImage only
                // accepts raster formats. The viewport size comes back with it so the
                // drawing can keep its aspect ratio inside the box below.
                double svgViewW = 0, svgViewH = 0;
                if (Table.IsSvg(img, imgData))
                {
                    var raster = ImageRasterizer.RasterizeSvg(imgData, out var vw, out var vh);
                    if (raster is not null) { imgData = raster; svgViewW = vw; svgViewH = vh; }
                }

                var imgW = img.FixWidth > 0 ? img.FixWidth
                    : page.Width - mLeft - (Margin.RightTouched ? Margin.Right : 20);
                double imgH;
                if (img.FixHeight > 0)
                    imgH = img.FixHeight;
                else
                {
                    // No explicit height: preserve the image's aspect ratio rather than
                    // defaulting to a square (a wide footer bar would otherwise render as a
                    // huge block covering the page).
                    try
                    {
                        var probe = new ImageStamp(new System.IO.MemoryStream(imgData));
                        imgH = probe.PixelWidth > 0 ? imgW * probe.PixelHeight / (double)probe.PixelWidth : imgW;
                    }
                    catch { imgH = imgW; }
                }
                // A VECTOR source keeps its viewport aspect ratio inside the declared
                // box (SVG's default `xMidYMid meet`): it is fitted, not stretched, and
                // centred on both axes — a 10:1 logo in a 50×50 box draws as a 50×5 band
                // halfway down it.
                var boxW = imgW;
                var boxH = imgH;
                double boxOffX = 0, boxOffY = 0;
                if (svgViewW > 0 && svgViewH > 0 && boxW > 0 && boxH > 0)
                {
                    var fit = Math.Min(boxW / svgViewW, boxH / svgViewH);
                    imgW = svgViewW * fit;
                    imgH = svgViewH * fit;
                    boxOffX = (boxW - imgW) / 2;
                    boxOffY = (boxH - imgH) / 2;
                }

                // An image honours its own horizontal alignment inside the band
                // (the band's left margin, its right edge, or centred between them);
                // the band's own x is the fallback.
                var imgRight = Margin.RightTouched ? Margin.Right
                    : page.PageInfo?.Margin is { RightTouched: true } prm ? prm.Right
                    : document?.PageInfo?.Margin is { RightTouched: true } drm ? drm.Right
                    : DefaultBandMargin;
                var boxX = img.HorizontalAlignment switch
                {
                    HorizontalAlignment.Right => page.Width - imgRight - boxW,
                    HorizontalAlignment.Center => (page.Width - boxW) / 2,
                    HorizontalAlignment.Left => mLeft,
                    _ => x,
                };
                var imgX = boxX + boxOffX;
                Rectangle rect;
                if (isHeader)
                {
                    // Header: image top edge at current y, growing downward. The FIRST
                    // paragraph's image starts at the band top itself — the font-size
                    // drop in y is a text baseline allowance, and an image has no baseline.
                    var boxTop = (firstParagraph ? pageHeight - mTop : y) - boxOffY;
                    rect = new Rectangle(imgX, boxTop - imgH, imgX + imgW, boxTop);
                    y = boxTop - imgH - boxOffY - 4;
                }
                else
                {
                    // Footer: image bottom edge at the bottom margin, growing upward.
                    var bottom = mBottom + boxOffY;
                    rect = new Rectangle(imgX, bottom, imgX + imgW, bottom + imgH);
                    y = bottom + imgH + boxOffY + 4;
                }
                firstParagraph = false;
                try { page.AddImage(imgData, rect); }
                catch (ArgumentException) { continue; }
                continue;
            }

            // ── FloatingBox ────────────────────────────────────────────
            // A header/footer box honours its Top/Left (positioned at page
            // coordinates) and renders its background plus nested paragraphs
            // (e.g. a Table). Its Left is relative to the page's left content
            // margin, so offset by that
            // margin (the 90 pt Generator default when untouched) while
            // rendering, then restore the caller's values.
            if (para is FloatingBox fb)
            {
                var hm = page.PageInfo?.Margin;
                var leftMargin = hm?.LeftTouched == true ? hm.Left : 90;
                var savedLeft = fb.Left;
                var savedMode = fb.PositioningMode;
                // A fixed-height header box crams its content: a nested
                // table's rows must fit inside the box height. Generator
                // tables default to 2 pt vertical cell padding, which would push
                // later rows below a small box, so tighten a nested table's cell
                // padding to zero (only when the caller left it at the default)
                // while rendering this box, then restore it. Scoped to a header /
                // footer FloatingBox so ordinary body tables are unaffected.
                var tightened = new List<Table>();
                if (fb.Height > 0)
                {
                    foreach (var inner in fb.Paragraphs)
                    {
                        if (inner is Table t && t.DefaultCellPadding is null)
                        {
                            t.DefaultCellPadding = new MarginInfo(0, 0, 0, 0);
                            tightened.Add(t);
                        }
                    }
                }
                fb.Left = savedLeft + leftMargin;
                fb.PositioningMode = ParagraphPositioningMode.Absolute;
                page.AddFloatingBox(fb);
                fb.Left = savedLeft;
                fb.PositioningMode = savedMode;
                foreach (var t in tightened) t.DefaultCellPadding = null;
                continue;
            }

            // ── Table ──────────────────────────────────────────────────
            // A bare table in the header/footer renders at the running y.
            if (para is Table tbl)
            {
                // Header/footer TABLES belong to the page generator: on a
                // static (imported, never laid-out) page the table stays
                // undrawn while sibling text/HTML fragments still render.
                if (!tableContent) continue;
                // A header table anchors at the top margin itself — the running y
                // already carries the first text line's font-size inset, which
                // applies to text baselines, not to a table's top edge. The
                // header's left margin becomes the table's flow offset.
                var tableTop = isHeader ? pageHeight - mTop : y;
                // Per-page working clones of interactive fields in a footer table:
                // the SAME footer renders on every page, and each page must carry
                // its own field + widget (one AcroForm field per page, all at the
                // same footer rectangle).
                List<(Cell cell, int idx, Aspose.Pdf.Forms.CheckboxField proto, Aspose.Pdf.Forms.CheckboxField clone)>? fieldSwaps = null;
                var footerBottomBound = 36.0;
                if (!isHeader)
                {
                    var hasFields = false;
                    foreach (var r in tbl.Rows)
                    {
                        foreach (var c in r.Cells)
                        {
                            foreach (var p in c.Paragraphs)
                                if (p is Aspose.Pdf.Forms.CheckboxField) { hasFields = true; break; }
                            if (hasFields) break;
                        }
                        if (hasFields) break;
                    }
                    if (hasFields)
                    {
                        // Footer tables carrying form fields are BOTTOM-anchored: the
                        // table's bottom edge sits on the footer band line —
                        // Margin.Bottom when set, else the default band bottom of 60
                        // (widget rect (90,60)-(100,70) with a 14 pt caption baseline
                        // at 60.6).
                        var bandBottom = Margin.BottomTouched ? Margin.Bottom : 60.0;
                        tableTop = bandBottom + tbl.GetHeight(page);
                        if (document is not null)
                        {
                            foreach (var r in tbl.Rows)
                                foreach (var c in r.Cells)
                                    for (var pidx = 0; pidx < c.Paragraphs.Count; pidx++)
                                        if (c.Paragraphs[pidx] is Aspose.Pdf.Forms.CheckboxField proto)
                                        {
                                            var clone = new Aspose.Pdf.Forms.CheckboxField(document)
                                            {
                                                Width = proto.Width,
                                                Height = proto.Height,
                                                Style = proto.Style,
                                            };
                                            (fieldSwaps ??= new()).Add((c, pidx, proto, clone));
                                            c.Paragraphs[pidx] = clone;
                                        }
                        }
                    }
                    else
                    {
                        // A plain FOOTER table hangs BELOW the page's content
                        // bottom-margin line and grows downward into the margin (its
                        // top edge sits at the page margin, not at the footer's own
                        // Margin.Bottom), so pass an extended bottom bound to keep
                        // the whole table on this page.
                        tableTop = page.PageInfo?.Margin is { BottomTouched: true } pbm ? pbm.Bottom
                            : document?.PageInfo?.Margin is { BottomTouched: true } dbm ? dbm.Bottom
                            : 72;
                        footerBottomBound = -pageHeight;
                    }
                }
                if (tbl.FlowLeftOffset == 0) tbl.FlowLeftOffset = mLeft;
                if (!isHeader) tbl.SuppressBaselineLift = true;
                var contents = tbl.BuildMultiPage(page, tableTop, isHeader ? 36 : footerBottomBound);
                if (fieldSwaps is not null)
                {
                    foreach (var (c, idx, proto, clone) in fieldSwaps)
                    {
                        c.Paragraphs[idx] = proto;
                        // Register the placed clone in the AcroForm for THIS page (the
                        // widget rect was set by the table's layout pass).
                        document!.Form.Add(clone, page.Number);
                    }
                }
                if (contents.Count > 0) page.AddContentStream(contents[0]);
                // Blit cell images collected for this page — only the flow
                // dispatcher applied LastImageDraws, so a header-table logo
                // (e.g. an SVG rasterised into a cell) was silently dropped.
                if (tbl.LastImageDraws.Count > 0)
                    foreach (var (data, rect) in tbl.LastImageDraws[0])
                        try { page.AddImage(data, rect); }
                        catch (ArgumentException) { /* unsupported format: skip */ }
                if (tbl.LastGraphDraws.Count > 0)
                    foreach (var g in tbl.LastGraphDraws[0])
                        page.AddContentStream(g);
                y -= tbl.LastRenderedHeight;
                continue;
            }

            string? text = null;
            var fn = TextState.FontName ?? "Helvetica";
            var fs = fontSize;
            Text.Font? embedFont = null;
            // Left offset this paragraph's own CSS puts on its outermost block
            // container (an HtmlFragment styled `#header { margin-left: 200px }`
            // renders indented by that box offset, not at the header band's left edge).
            var cssLeftIndent = 0.0;
            // Size and alignment the fragment's outermost block declares inline
            // (a footer div styled `font-size: 12px; text-align: Center` sets its
            // flattened band line that way).
            var cssAlign = HorizontalAlignment.None;

            if (para is TextFragment tf)
            {
                text = ApplyLabelMacros(tf.Text ?? "", document, pageNumber);
                if (tf.TextState.FontName is not null) fn = tf.TextState.FontName;
                if (tf.TextState.FontSize > 0) fs = tf.TextState.FontSize;
            }
            else if (para is HtmlFragment htmlFrag)
            {
                // With IsEmbedFonts the fragment's CSS font-family must bind the real
                // face (typically registered through a FolderFontSource) and render
                // through the embedded Type0 path — the Standard-14 fallback has no
                // font program, so it can never be embedded or subset.
                if (htmlFrag.HtmlLoadOptions?.IsEmbedFonts == true)
                    embedFont = ResolveDeclaredFont(htmlFrag.HtmlContent ?? "");
                var hc = htmlFrag.HtmlContent ?? "";
                // An HTML <table> in a header/footer fragment renders as real columns
                // (rows × cells) rather than the flat tag-stripped text stack: build a
                // generator Table and lay it out bottom-aligned to the footer band.
                if (Converters.HtmlToPdfConverter.ContainsTable(hc))
                {
                    // A header/footer table is authored with its frame on the cells
                    // themselves (`<td style="BORDER-TOP: black 1pt solid; …">`), so the
                    // per-cell border sides are read here as they are in the band dialect.
                    var htmlTbl = Converters.HtmlToPdfConverter.BuildTableFromHtml(
                        hc, 0, out _, htmlFrag.HtmlLoadOptions, null, null,
                        authoredCellChrome: true);
                    if (htmlTbl is not null)
                    {
                        // On a /Rotate page the footer content is drawn through a visual→raw
                        // matrix (the table is laid out in the page's rotation-adjusted VISUAL
                        // space, then mapped into raw content space so it appears upright).
                        var rotCm = VisualToRawRotationCm(page);

                        // The generator centres a table in `page.Width - 2*FlowLeftOffset`; a
                        // left-aligned footer table at Margin.Left would shrink to half width
                        // (over-wrapping its cells). For the rotated path, lay the table out
                        // one-sided (start at a small offset so the usable width ≈ the band
                        // from the left margin to the right edge) and translate it to Margin.Left
                        // in the visual frame; the unrotated path keeps the original placement.
                        double flo = x, translateX = 0;
                        if (rotCm is not null)
                        {
                            var desiredUsable = Math.Max(50.0, page.Width - x - 36);
                            flo = (page.Width - desiredUsable) / 2;
                            translateX = x - flo;
                        }
                        htmlTbl.FlowLeftOffset = flo;

                        // First pass measures the single-page height (from the page top so the
                        // table doesn't trip the page-break logic); then render so the table's
                        // bottom sits at the footer's bottom margin. A far-below bottom margin
                        // keeps the whole table on this page (no spill slice the footer drops).
                        htmlTbl.BuildMultiPage(page, page.Height, 0, measureOnly: true);
                        var startY = isHeader ? y : mBottom + htmlTbl.LastRenderedHeight;
                        var contents = htmlTbl.BuildMultiPage(page, startY, -page.Height);

                        if (rotCm is null)
                        {
                            if (contents.Count > 0) page.AddContentStream(contents[0]);
                            // Cell images are collected by the layout pass, not written into
                            // its content stream — without this blit a logo in a header
                            // table's first cell is laid out and then silently dropped.
                            if (htmlTbl.LastImageDraws.Count > 0)
                                foreach (var (data, rect) in htmlTbl.LastImageDraws[0])
                                    try { page.AddImage(data, rect); }
                                    catch (ArgumentException) { /* unsupported format: skip */ }
                            if (htmlTbl.LastGraphDraws.Count > 0)
                                foreach (var gc in htmlTbl.LastGraphDraws[0])
                                    page.AddContentStream(gc);
                        }
                        else
                        {
                            var wrap = new System.Text.StringBuilder("q\n").Append(rotCm).Append('\n');
                            if (Math.Abs(translateX) > 0.001)
                                wrap.Append("1 0 0 1 ")
                                    .Append(translateX.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))
                                    .Append(" 0 cm\n");
                            if (contents.Count > 0)
                                wrap.Append(System.Text.Encoding.ASCII.GetString(contents[0])).Append('\n');
                            if (htmlTbl.LastGraphDraws.Count > 0)
                                foreach (var gc in htmlTbl.LastGraphDraws[0])
                                    wrap.Append(System.Text.Encoding.ASCII.GetString(gc)).Append('\n');
                            wrap.Append("Q\n");
                            page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(wrap.ToString()));
                        }
                        y = startY - htmlTbl.LastRenderedHeight;
                    }
                    continue;
                }
                // Procedure-form header band: right-aligned lines against the band's
                // right margin, bold only where the line itself carries it, explicit
                // CSS row heights stepping the stack (the remaining lines keep the
                // band's 1.12 em pitch).
                if (embedFont is null
                    && Converters.HtmlToPdfConverter.TryParseProcedureBandLines(hc, out var pbLines))
                {
                    var pbFs = fontSize > 10 ? fontSize : 12.0;
                    // the band's own container may declare a right padding in the
                    // fragment's LINKED sheet (reachable through its load options) —
                    // the right-aligned lines anchor that much further in
                    var pbRight = page.Width - (Margin.RightTouched ? Margin.Right : mLeft)
                        - Converters.HtmlToPdfConverter.BandPaddingRightPt(hc, htmlFrag.HtmlLoadOptions);
                    var pbBaseline = page.Height - mTop - pbFs;
                    double pbAdv = 0;
                    var pbB = new ContentStreamBuilder();
                    pbB.SaveState();
                    foreach (var pl in pbLines)
                    {
                        var pf = pl.Bold ? "Helvetica-Bold" : "Helvetica";
                        var pres = EnsureFontResource(page, pf);
                        double pw;
                        try
                        {
                            pw = FontRepository.FindFont(pf)?.MeasureString(pl.Text, pbFs)
                                 ?? EstimateWidth(pl.Text, pbFs);
                        }
                        catch { pw = EstimateWidth(pl.Text, pbFs); }
                        pbB.BeginText().SetFont(pres, pbFs)
                            .MoveTextPosition(pbRight - pw, pbBaseline)
                            .ShowText(pl.Text).EndText();
                        var pbStep = pl.HeightPt > 0 ? pl.HeightPt : pbFs * 1.12;
                        pbBaseline -= pbStep;
                        pbAdv += pbStep;
                    }
                    pbB.RestoreState();
                    page.AddContentStream(pbB.Build());
                    if (isHeader) y -= pbAdv;
                    continue;
                }
                // ── report-band header ─────────────────────────────────────────
                // An <h5> of inline-block percentage spans over centred <h3>
                // headings: the band's LEFT is the band default (90) plus the
                // sheet's own @page margin, its WIDTH the header body's declared
                // physical width — the anchor holds
                // constant across every page and body width, moves 1:1 with the
                // @page margin, and each span aligns inside its percentage box.
                // The rest of the fragment keeps the ordinary block band, shifted
                // below the headings.
                var hfBandOffset = 0.0;
                var hfBandShift = 0.0;
                var hfBandSmall = false;
                var hfBandW = 0.0;
                if (isHeader && embedFont is null)
                {
                    var h5M = System.Text.RegularExpressions.Regex.Match(hc,
                        @"(?s)<h5[^>]*>(.*?)</h5>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    static double PhysPt(string v)
                    {
                        var m2 = System.Text.RegularExpressions.Regex.Match(v,
                            @"([\d.]+)\s*(cm|mm|in|pt)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (!m2.Success) return 0;
                        var n = double.Parse(m2.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                        return m2.Groups[2].Value.ToLowerInvariant() switch
                        {
                            "cm" => n * 28.346457, "mm" => n * 2.8346457, "in" => n * 72.0, _ => n,
                        };
                    }
                    var bodyWM = System.Text.RegularExpressions.Regex.Match(hc,
                        @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))[^'""]*\1",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var spans = h5M.Success
                        ? System.Text.RegularExpressions.Regex.Matches(h5M.Groups[1].Value,
                            @"(?s)<span\b[^>]*style\s*=\s*(['""])(?<st>[^'""]*width\s*:\s*[\d.]+%[^'""]*)\1[^>]*>(?<t>.*?)</span>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        : null;
                    if (spans is { Count: >= 2 } && bodyWM.Success
                        && PhysPt(bodyWM.Groups[2].Value) is > 0 and var bandW)
                    {
                        var pageMarginCss = 0.0;
                        var atPage = System.Text.RegularExpressions.Regex.Match(hc,
                            @"(?s)@page\s*\{(?<b>[^}]*)\}",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (atPage.Success)
                        {
                            var mDecl = System.Text.RegularExpressions.Regex.Match(atPage.Groups["b"].Value,
                                @"(?<![-\w])margin(-left)?\s*:\s*([^;}]+)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (mDecl.Success) pageMarginCss = PhysPt(mDecl.Groups[2].Value);
                        }
                        var bandL = RptBandLeftPt + pageMarginCss;
                        var h5Fs = RptH5FontPt;
                        var spaceW = RptSpaceEm * h5Fs;        // inter-inline-block whitespace
                        var boldRes = EnsureFontResource(page, "Helvetica-Bold");
                        var bandB = new ContentStreamBuilder();
                        bandB.SaveState();
                        double MeasureBold(string t, double fs2) => MeasureReportText(t, fs2, bold: true);
                        var h5Base = mTop + RptH5BasePt;
                        var bx = bandL;
                        foreach (System.Text.RegularExpressions.Match sp in spans)
                        {
                            var st = sp.Groups["st"].Value;
                            var pw = System.Text.RegularExpressions.Regex.Match(st, @"width\s*:\s*([\d.]+)%");
                            var boxW = pw.Success ? double.Parse(pw.Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture) / 100.0 * bandW : 0;
                            var txt = HtmlFragment.StripHtmlTags(sp.Groups["t"].Value).Trim();
                            if (txt.Length > 0)
                            {
                                var tw = MeasureBold(txt, h5Fs);
                                var tx = System.Text.RegularExpressions.Regex.IsMatch(st, @"text-align\s*:\s*center",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                    ? bx + (boxW - tw) / 2
                                    : System.Text.RegularExpressions.Regex.IsMatch(st, @"text-align\s*:\s*right",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                        ? bx + boxW - tw
                                        : bx;
                                bandB.BeginText().SetFont(boldRes, h5Fs)
                                    .MoveTextPosition(tx, page.Height - h5Base)
                                    .ShowText(txt).EndText();
                            }
                            bx += boxW + spaceW;
                        }
                        // centred <h3> headings on the band's own ladder
                        var h3Fs = RptH3FontPt;
                        var lastBase = h5Base;
                        var firstH3 = true;
                        foreach (System.Text.RegularExpressions.Match h3 in
                            System.Text.RegularExpressions.Regex.Matches(hc, @"(?s)<h3\b[^>]*>(.*?)</h3>",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            var txt = HtmlFragment.StripHtmlTags(h3.Groups[1].Value).Trim();
                            if (txt.Length == 0) continue;
                            lastBase += firstH3 ? RptH5ToH3Pt : RptH3PitchPt;
                            firstH3 = false;
                            var tw = MeasureBold(txt, h3Fs);
                            bandB.BeginText().SetFont(boldRes, h3Fs)
                                .MoveTextPosition(bandL + (bandW - tw) / 2, page.Height - lastBase)
                                .ShowText(txt).EndText();
                        }
                        bandB.RestoreState();
                        page.AddContentStream(bandB.Build());
                        // the DATA REGION below the headings: nested percentage columns
                        // of bold right-aligned labels, bands, checkboxes and framed
                        // fieldsets, all at the band's left in the sheet's small size.
                        // The first row's baseline sits 18.26 under the last heading's.
                        var regionHtml = System.Text.RegularExpressions.Regex.Replace(
                            System.Text.RegularExpressions.Regex.Match(hc,
                                @"(?s)<body\b[^>]*>(.*?)</body>",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                is { Success: true } rgb ? rgb.Groups[1].Value : hc,
                            @"(?s)<h5[^>]*>.*?</h5>|<h3\b[^>]*>.*?</h3>|<script\b.*?</script>|<!--.*?-->", "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        var regB = new ContentStreamBuilder();
                        regB.SaveState();
                        RenderReportRegion(page, regB, regionHtml, bandL, bandW,
                            lastBase + RptH3ToRegionPt, false,
                            boldRes, EnsureFontResource(page, "Helvetica"));
                        regB.RestoreState();
                        page.AddContentStream(regB.Build());
                        continue;
                    }
                }
                cssLeftIndent = Converters.HtmlToPdfConverter.CssBlockLeftIndentPt(
                    hc, htmlFrag.HtmlLoadOptions) + hfBandShift;
                // A block-STRUCTURED fragment (several <p>/<div> paragraphs) renders one
                // line per block — the flat tag-stripped join would run all paragraphs
                // together on a single overflowing line. With IsClipExtraContent the
                // band clips: header lines stop where the body's content begins, and a
                // footer sits just below the body's bottom content margin.
                // Inline text-transform resolves before block parsing so the banded
                // lines keep the authored casing. An IsEmbedFonts fragment stays on
                // the legacy path — its declared face must embed as a Type0 subset,
                // which the Standard-14 band writer cannot do.
                var hfBlocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
                    ApplyTextTransforms(hc),
                    hfBandSmall ? 9.75 : fontSize > 10 ? fontSize : 12.0);
                var hfTextBlocks = hfBlocks.FindAll(b => !string.IsNullOrWhiteSpace(b.Text));
                if (embedFont is null
                    && (hfTextBlocks.Count > 1 || (IsClipExtraContent && hfTextBlocks.Count == 1)))
                {
                    // Inline emphasis carried by every line of the fragment (the
                    // <p><u><strong>… header idiom) — resolved at fragment level.
                    var hfBold = System.Text.RegularExpressions.Regex.IsMatch(hc, @"<(b|strong)[\s>]",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var hfUnderline = System.Text.RegularExpressions.Regex.IsMatch(hc, @"<u[\s>]",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var hfFont = hfBold ? "Helvetica-Bold" : "Helvetica";
                    var hfRes = EnsureFontResource(page, hfFont);
                    // Body content edges this band must respect (the page's content
                    // margins through the same fall-through as mLeft).
                    var bodyTopMargin = Margin.TopTouched && Margin.Top > 0 ? Margin.Top
                        : page.PageInfo?.Margin is { TopTouched: true } ptm ? ptm.Top
                        : document?.PageInfo?.Margin is { TopTouched: true } dtm ? dtm.Top
                        : 90;
                    var bodyBottomMargin = page.PageInfo?.Margin is { BottomTouched: true } pbm ? pbm.Bottom
                        : document?.PageInfo?.Margin is { BottomTouched: true } dbm ? dbm.Bottom
                        : 72;
                    var hfB = new ContentStreamBuilder();
                    hfB.SaveState();
                    var lineIdx = 0;
                    foreach (var blk in hfTextBlocks)
                    {
                        var bfs = blk.FontSize > 0 ? blk.FontSize : fontSize;
                        // The HTML paragraph band steps on a 1.12 em pitch.
                        var pitch = bfs * 1.12;
                        double baseline;
                        if (isHeader)
                        {
                            baseline = page.Height - mTop - bfs - lineIdx * pitch - hfBandOffset;
                            // Clip: a header line whose descender/underline would touch
                            // the body's first line (cap top = top margin − cap ascent)
                            // is extra content.
                            if (IsClipExtraContent
                                && (page.Height - baseline) + bfs * 0.2 > bodyTopMargin - bfs * 0.72)
                                break;
                        }
                        else
                        {
                            // Footer band: with clipping the band tucks under the body's
                            // bottom content margin; otherwise keep the legacy bottom-up
                            // stack from the footer margin.
                            baseline = IsClipExtraContent
                                ? bodyBottomMargin - bfs - lineIdx * pitch
                                : mBottom + fontSize + lineIdx * pitch;
                            if (baseline < 0) break;
                        }
                        // a row that declares its own background paints the band behind
                        // its line, spanning its enclosing column's share of the band
                        if (blk.BackgroundColor is { } hbg && hfBandW > 0)
                            hfB.SetFillColor(hbg.R / 255.0, hbg.G / 255.0, hbg.B / 255.0)
                               .Rectangle(x + cssLeftIndent, baseline - RptBandDescentPt,
                                   hfBandW * (blk.WidthFrac > 0 ? blk.WidthFrac : 1.0), RptRowPitchPt)
                               .Fill()
                               .SetFillColor(0, 0, 0);
                        hfB.BeginText()
                            .SetFont(hfRes, bfs)
                            .MoveTextPosition(x + cssLeftIndent + blk.LeftIndent, baseline)
                            .ShowText(blk.Text)
                            .EndText();
                        if (hfUnderline)
                        {
                            double ulW;
                            try
                            {
                                ulW = FontRepository.FindFont(hfFont)?.MeasureString(blk.Text, bfs)
                                      ?? EstimateWidth(blk.Text, bfs);
                            }
                            catch { ulW = EstimateWidth(blk.Text, bfs); }
                            hfB.SetLineWidth(bfs * 0.07)
                                .MoveTo(x + cssLeftIndent + blk.LeftIndent, baseline - bfs * 0.12)
                                .LineTo(x + cssLeftIndent + blk.LeftIndent + ulW, baseline - bfs * 0.12)
                                .Stroke();
                        }
                        lineIdx++;
                    }
                    hfB.RestoreState();
                    page.AddContentStream(hfB.Build());
                    if (isHeader) y -= lineIdx * fontSize * 1.12;
                    continue;
                }
                text = HtmlFragment.StripHtmlTags(ApplyTextTransforms(hc));
                var blkStyle = System.Text.RegularExpressions.Regex.Match(hc,
                    @"<(?:div|p)\b[^>]*style\s*=\s*(['""])(?<s>[^'""]*)\1",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (blkStyle.Success)
                {
                    var st = blkStyle.Groups["s"].Value;
                    var fsm = System.Text.RegularExpressions.Regex.Match(st,
                        @"font-size\s*:\s*([\d.]+)\s*px",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (fsm.Success && double.TryParse(fsm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var fpx) && fpx > 0)
                        fs = (float)(fpx * 0.75);
                    var alm = System.Text.RegularExpressions.Regex.Match(st,
                        @"text-align\s*:\s*(center|right)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (alm.Success)
                        cssAlign = alm.Groups[1].Value.Equals("center", StringComparison.OrdinalIgnoreCase)
                            ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                }
            }

            if (string.IsNullOrWhiteSpace(text)) continue;

            // Inline paragraph: continue on the previous text line right after its
            // last glyph instead of starting a new line.
            bool inline = para is TextFragment inlineTf && inlineTf.IsInLineParagraph
                && !double.IsNaN(lastTextY);
            var drawX = inline ? lastTextEndX : x + cssLeftIndent;
            var drawY = inline ? lastTextY : y;
            // A centred/right-aligned fragment positions within the header band
            // (such lines centre on the page's content width).
            if (!inline)
            {
                var alignment = para is TextFragment alignTf
                    ? alignTf.HorizontalAlignment != HorizontalAlignment.None
                            && alignTf.HorizontalAlignment != HorizontalAlignment.Left
                        ? alignTf.HorizontalAlignment
                        : alignTf.TextState.HorizontalAlignment
                    : cssAlign;
                if (alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
                {
                    double alignW;
                    try
                    {
                        alignW = FontRepository.FindFont(fn)?.MeasureString(text!, fs)
                                 ?? EstimateWidth(text, fs);
                    }
                    catch { alignW = EstimateWidth(text, fs); }
                    var mRight = Margin.RightTouched ? Margin.Right : mLeft;
                    var bandRight = page.Width - mRight;
                    drawX = alignment == HorizontalAlignment.Center
                        ? mLeft + (bandRight - mLeft - alignW) / 2
                        : bandRight - alignW;
                }
            }

            var builder = new ContentStreamBuilder();
            builder.SaveState();
            if (embedFont?.SourceFontData?.TtfData is { Length: > 0 } embedTtf)
            {
                // Embedded face: register (or reuse) the Type0/CID font on the page and
                // show the run as hex glyph ids. The Type0 embedder presents the program
                // under a subset-tagged BaseFont, so the saved document's fonts read
                // back as embedded subsets (FontUtilities.SubsetFonts semantics).
                var pageFontDict = ResolvePageFontDict(page);
                var (embedRes, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    pageFontDict, embedTtf, embedFont.FontName ?? "Embedded", text!,
                    stripSpacesInBaseFont: true);
                builder.BeginText()
                    .SetFont(embedRes, fs)
                    .MoveTextPosition(drawX, drawY)
                    .ShowTextHex(hex)
                    .EndText()
                    .RestoreState();
            }
            else
            {
                var fontRes = EnsureFontResource(page, fn);
                builder.BeginText()
                    .SetFont(fontRes, fs)
                    .MoveTextPosition(drawX, drawY)
                    .ShowText(text!)
                    .EndText()
                    .RestoreState();
            }
            page.AddContentStream(builder.Build());

            double textW;
            try
            {
                var mw = FontRepository.FindFont(fn)?.MeasureString(text!, fs) ?? 0;
                textW = mw > 0 ? mw : EstimateWidth(text, fs);
            }
            catch { textW = EstimateWidth(text, fs); }
            lastTextY = drawY;
            lastTextEndX = drawX + textW;
            if (!inline) y -= fs * 1.2;
        }
    }

    /// <summary>Render the report header's DATA REGION: nested inline-block percentage
    /// columns of label/value rows (labels bold, right-aligned in the box their css
    /// gives them), background bands, checkbox rows, and grey-framed fieldsets whose
    /// legend rides the frame. Returns the height
    /// consumed; draw:false only measures (columns bottom-align on the tallest sibling).</summary>
    // ── report-dialect geometry ─────────────────────────────────────────────
    // Every value is an empirical constant of this band layout, holding
    // wherever the document repeats the shape.
    // Units: Pt = points, Em = fraction of the run's font size,
    // Frac = fraction of the enclosing box's width.
    private const double RptFontPt = 9.75;            // div { font-size: small } = 13 css px
    private const double RptRowPitchPt = 12.75;       // the small face's 17 css px normal line
    private const double RptSpaceEm = 0.278;          // one collapsed space between inline blocks
    private const double RptLabelMarginEm = 0.5;      // label { margin-right: .5em }
    private const double RptLabelBoxFrac = 0.40;      // label { width: 40% }
    private const double RptFieldsetLabelFrac = 0.30; // fieldset label { width: 30% }
    private const double RptBandDescentPt = 2.34;     // a row's background rect reaches this far under the baseline
    private const double RptCheckboxIndentPt = 4.1;   // column left → checkbox square
    private const double RptCheckboxSizePt = 7.75;    // the square's white fill side
    private const double RptCheckboxRisePt = 0.55;    // square bottom above the row baseline
    private const double RptCheckboxLabelGapPt = 5.6;  // square → its label: the source's
                                                       // own space plus the inline gap —
                                                       // the same gap for every label
    private const double RptCheckboxGray = 0.4;       // the square's stroke shade
    private const double RptFrameInsetPt = 1.5;       // region left → fieldset frame
    private const double RptFieldsetPadPt = 8.35;     // region left → fieldset content
    private const double RptLegendInsetPt = 9.85;     // region left → legend text
    private const double RptFrameOverhangPt = 13.7;   // frame width beyond the fieldset's percentage box
    private const double RptLegendGapPt = 2.0;        // the frame's top edge breaks this far around the legend
    private const double RptFrameGray = 0.502;        // #808080, the fieldset frame shade
    private const double RptStrokePt = 0.75;          // 1 css px
    private const double RptH5FontPt = 9.96;          // UA h5: 0.83 em of the 12 pt base
    private const double RptH3FontPt = 14.04;         // UA h3: 1.17 em of the 12 pt base
    private const double RptH5BasePt = 10.87;         // band top → the h5 line's baseline
    private const double RptH5ToH3Pt = 17.82;         // h5 baseline → first h3 baseline
    private const double RptH3PitchPt = 18.75;        // h3 baseline → next h3 baseline
    private const double RptH3ToRegionPt = 28.01;     // last h3 baseline → first data-row baseline
    private const double RptBandLeftPt = 90.0;        // the band's own default left — the header
                                                      // sits at this plus the sheet's @page margin
                                                      // (constant across page/body widths)

    // Segoe UI ASCII advances (fraction of em, glyphs 32..126), read once from the
    // face's own tables — the report dialect measures with real Segoe UI
    // metrics, so right-aligned and centred runs anchor at their true metric
    // positions ("Source:" measures 33.80 at 9.75). The
    // Standard-14 twin only supplies the drawn glyphs.
    private static readonly double[] SegoeAdvances =
    {
        0.2739, 0.2842, 0.3921, 0.5908, 0.5391, 0.8184, 0.8003, 0.2300, 0.3018, 0.3018,
        0.4170, 0.6841, 0.2168, 0.3999, 0.2168, 0.3896, 0.5391, 0.5391, 0.5391, 0.5391,
        0.5391, 0.5391, 0.5391, 0.5391, 0.5391, 0.5391, 0.2168, 0.2168, 0.6841, 0.6841,
        0.6841, 0.4482, 0.9551, 0.6450, 0.5732, 0.6191, 0.7012, 0.5059, 0.4883, 0.6860,
        0.7100, 0.2661, 0.3569, 0.5801, 0.4707, 0.8979, 0.7480, 0.7539, 0.5601, 0.7539,
        0.5981, 0.5312, 0.5239, 0.6870, 0.6211, 0.9341, 0.5898, 0.5527, 0.5703, 0.3018,
        0.3789, 0.3018, 0.6841, 0.4150, 0.2681, 0.5088, 0.5879, 0.4619, 0.5889, 0.5229,
        0.3130, 0.5889, 0.5659, 0.2422, 0.2422, 0.4971, 0.2422, 0.8613, 0.5659, 0.5859,
        0.5879, 0.5889, 0.3477, 0.4243, 0.3389, 0.5659, 0.4790, 0.7227, 0.4590, 0.4839,
        0.4521, 0.3018, 0.2393, 0.3018, 0.6841,
    };

    private static readonly double[] SegoeBoldAdvances =
    {
        0.2759, 0.3271, 0.4932, 0.5923, 0.5752, 0.8672, 0.8496, 0.2930, 0.3691, 0.3691,
        0.4551, 0.7070, 0.2710, 0.4043, 0.2710, 0.4434, 0.5752, 0.5752, 0.5752, 0.5752,
        0.5752, 0.5752, 0.5752, 0.5752, 0.5752, 0.5752, 0.2710, 0.2710, 0.7070, 0.7070,
        0.7070, 0.4380, 0.9541, 0.7031, 0.6411, 0.6240, 0.7373, 0.5322, 0.5200, 0.7109,
        0.7661, 0.3169, 0.4453, 0.6489, 0.5112, 0.9570, 0.7900, 0.7583, 0.6143, 0.7583,
        0.6528, 0.5605, 0.5859, 0.7231, 0.6670, 1.0049, 0.6553, 0.6069, 0.6069, 0.3691,
        0.4360, 0.3691, 0.7070, 0.4150, 0.3140, 0.5381, 0.6201, 0.4800, 0.6191, 0.5410,
        0.3833, 0.6191, 0.6021, 0.2842, 0.2842, 0.5591, 0.2842, 0.9160, 0.6050, 0.6113,
        0.6201, 0.6191, 0.3979, 0.4399, 0.3892, 0.6050, 0.5420, 0.7974, 0.5522, 0.5381,
        0.4790, 0.3691, 0.3262, 0.3691, 0.7070,
    };

    // The REAL Segoe UI faces, read once from the system font files: the report
    // dialect draws with the real faces' glyph shapes wherever they are
    // available (FindFont system faces carry no width tables, so the
    // files are read directly). Null on a machine without them — the dialect then
    // falls back to metric-anchored Standard-14 glyphs.
    private static byte[]? _segoeTtf, _segoeBoldTtf;
    private static bool _segoeProbed;

    internal static byte[]? SegoeReportTtf(bool bold)
    {
        if (!_segoeProbed)
        {
            _segoeProbed = true;
            try { _segoeTtf = System.IO.File.ReadAllBytes(@"C:\Windows\Fonts\segoeui.ttf"); } catch { }
            try { _segoeBoldTtf = System.IO.File.ReadAllBytes(@"C:\Windows\Fonts\segoeuib.ttf"); } catch { }
        }
        return bold ? _segoeBoldTtf : _segoeTtf;
    }

    /// <summary>Draw report text in the report's own face: the real Segoe UI
    /// embedded as a Type0 subset when the system provides it — exact shapes AND
    /// advances. Without it, Standard-14 glyphs anchor per character on the baked
    /// Segoe metrics, so the drawn shapes never drift more than one glyph's width
    /// from the true Segoe ink.</summary>
    private static void DrawReportWords(ContentStreamBuilder b, string res, string text,
        double x, double yPdf, double fs, bool bold, Page? page = null)
    {
        if (page is not null && SegoeReportTtf(bold) is { } segTtf)
        {
            // per WORD: Segoe kerns inside words, the Type0 embed does not —
            // anchoring each word at its metric position keeps the drift inside one
            // word's kerning, visually negligible
            var fd = ResolvePageFontDict(page);
            var wx2 = x;
            foreach (var word in text.Split(' '))
            {
                if (word.Length > 0)
                {
                    var (rn, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                        fd, segTtf, bold ? "SegoeUIBold" : "SegoeUI", word,
                        stripSpacesInBaseFont: true);
                    b.BeginText().SetFont(rn, fs)
                     .SetTextMatrix(1, 0, 0, 1, wx2, yPdf)
                     .ShowTextHexKerned(hex, KernAdjustments(word, bold)).EndText();
                }
                wx2 += MeasureReportText(word + " ", fs, bold);
            }
            return;
        }
        var cx = x;
        b.BeginText().SetFont(res, fs);
        foreach (var ch in text)
        {
            if (ch != ' ')
                b.SetTextMatrix(1, 0, 0, 1, cx, yPdf).ShowText(ch.ToString());
            cx += MeasureReportText(ch.ToString(), fs, bold);
        }
        b.EndText();
    }


    // Segoe UI kern pairs (em fractions, from the faces' own kern tables): Segoe
    // text kerns inside words, so both the measure and the drawn TJ runs
    // apply these. Format: two characters then the signed em value.
    private static readonly string SegoeKernData =
        "'r-0.0249;'s-0.0322;(j+0.1138;*A-0.0811;*J-0.0752;*c-0.0498;*d-0.0498;*e-0.0498;*g-0.0498;"
        + "*o-0.0498;*q-0.0498;A*-0.0630;A,+0.0332;AC-0.0132;AG-0.0132;AJ+0.0459;AO-0.0132;AT-0.0718;"
        + "AU-0.0132;AV-0.0571;AW-0.0361;AY-0.0762;AZ+0.0288;At-0.0132;Av-0.0210;Aw-0.0132;Ay-0.0181;"
        + "BT-0.0449;BY-0.0322;C?+0.0010;CC-0.0269;CG-0.0269;CO-0.0132;CQ-0.0269;D,-0.0630;D.-0.0630;"
        + "DA-0.0161;DT-0.0449;DX-0.0259;DZ-0.0239;EA+0.0049;EJ+0.0332;ET+0.0020;EW+0.0142;EX+0.0039;"
        + "F,-0.0752;F.-0.0752;FA-0.0649;FJ-0.0322;FS-0.0132;FT+0.0068;Fa-0.0371;Ff+0.0049;GT-0.0239;"
        + "GV-0.0132;Gy-0.0132;J,-0.0498;J.-0.0498;JA-0.0181;JJ-0.0322;Ja-0.0132;K,+0.0190;KC-0.0439;"
        + "KG-0.0439;KJ+0.0439;KO-0.0439;KQ-0.0439;KX+0.0181;KZ+0.0190;Kc-0.0132;Kd-0.0132;Ke-0.0132;"
        + "Kg-0.0132;Ko-0.0132;Kq-0.0132;Kt-0.0229;Kv-0.0361;Kw-0.0259;Ky-0.0449;L*-0.1011;L?-0.0498;"
        + "LA+0.0288;LC-0.0322;LG-0.0322;LJ+0.0488;LO-0.0342;LQ-0.0342;LT-0.0552;LU-0.0142;LV-0.0571;"
        + "LW-0.0239;LY-0.0630;LZ+0.0288;Lt-0.0132;Lv-0.0498;Lw-0.0322;Ly-0.0371;O,-0.0449;O.-0.0449;"
        + "OA-0.0132;OJ-0.0049;OT-0.0449;OX-0.0181;OY-0.0122;OZ-0.0239;P,-0.1592;P.-0.1592;PA-0.0771;"
        + "PG-0.0049;PJ-0.0630;PW+0.0190;PX-0.0298;Pa-0.0322;Pc-0.0371;Pd-0.0371;Pe-0.0371;Pg-0.0371;"
        + "Po-0.0371;Pq-0.0361;Q,-0.0449;Q.-0.0630;QA-0.0132;QT-0.0449;QX-0.0181;QY-0.0049;QZ-0.0239;"
        + "RC-0.0142;RG-0.0142;RJ+0.0278;RO-0.0098;RQ-0.0098;RT-0.0259;RY-0.0190;Rc-0.0259;Rd-0.0259;"
        + "Re-0.0278;Rg-0.0278;Ro-0.0288;Rq-0.0259;St-0.0322;Sv-0.0239;Sw-0.0132;Sy-0.0229;T,-0.0630;"
        + "T.-0.0879;T:-0.0112;TA-0.0752;TC-0.0449;TG-0.0449;TJ-0.0552;TO-0.0449;TQ-0.0449;TT+0.0190;"
        + "TV+0.0210;TW+0.0190;TX-0.0029;TY+0.0142;Ta-0.1060;Tc-0.1030;Td-0.1030;Te-0.1030;Tf-0.0469;"
        + "Tg-0.1030;Tm-0.0869;Tn-0.0869;To-0.1030;Tp-0.0869;Tq-0.1030;Tr-0.0869;Ts-0.0752;Tu-0.0869;"
        + "Tv-0.0498;Tw-0.0552;Tx-0.0879;Ty-0.0552;Tz-0.0630;UA-0.0200;V,-0.1001;V.-0.1118;VA-0.0571;"
        + "VC-0.0210;VG-0.0210;VJ-0.0342;VO-0.0059;VQ-0.0210;VS-0.0132;VT+0.0190;Va-0.0718;Vc-0.0630;"
        + "Vd-0.0630;Ve-0.0630;Vg-0.0630;Vm-0.0371;Vn-0.0371;Vo-0.0630;Vp-0.0371;Vq-0.0630;Vr-0.0371;"
        + "Vs-0.0322;Vu-0.0371;W,-0.0571;W.-0.0630;WA-0.0361;WT+0.0190;Wa-0.0371;Wc-0.0239;Wd-0.0239;"
        + "We-0.0239;Wg-0.0239;Wo-0.0239;Wq-0.0239;X,+0.0332;X.+0.0278;XC-0.0112;XG-0.0112;XJ+0.0469;"
        + "XO-0.0112;XQ-0.0112;XT+0.0161;Y,-0.0859;Y.-0.0952;YA-0.0771;YC-0.0220;YG-0.0220;YJ-0.0322;"
        + "YO-0.0220;YQ-0.0220;YS-0.0132;YT+0.0190;Ya-0.0972;Yc-0.0879;Yd-0.0879;Ye-0.0879;Yf-0.0132;"
        + "Yg-0.0879;Ym-0.0688;Yn-0.0688;Yo-0.0879;Yp-0.0688;Yq-0.0879;Yr-0.0688;Ys-0.0649;Yu-0.0688;"
        + "ZJ+0.0400;ZT+0.0190;Zy-0.0259;[j+0.1138;ba-0.0132;bf-0.0049;bx-0.0122;cJ+0.0342;cT-0.0498;"
        + "cY-0.0371;e'-0.0508;f)+0.0688;f,-0.0630;f--0.0498;f.-0.0630;f:+0.0400;f?+0.0322;f]+0.0688;"
        + "fb+0.0088;fh+0.0088;ft+0.0181;fv+0.0190;fw+0.0190;fx+0.0088;fy+0.0161;f}+0.0400;gj+0.0229;"
        + "jj+0.0171;k,+0.0400;k--0.0679;k.+0.0400;k:+0.0400;kc-0.0200;kd-0.0132;ke-0.0200;kg-0.0200;"
        + "ko-0.0200;kq-0.0132;kt-0.0078;n'-0.0508;o'-0.0708;oa-0.0132;of-0.0181;ox-0.0122;pa-0.0132;"
        + "pf-0.0181;px-0.0122;qj+0.0498;r,-0.0771;r--0.0630;r.-0.0830;r:+0.0400;rc-0.0132;rd-0.0132;"
        + "re-0.0132;rf+0.0190;rg-0.0132;rm-0.0020;rn-0.0020;ro-0.0132;rq-0.0132;rs+0.0068;rt+0.0288;"
        + "rv+0.0400;rw+0.0400;rx+0.0288;ry+0.0400;rz+0.0190;t--0.0552;t?-0.0259;tc-0.0132;td-0.0132;"
        + "te-0.0078;tg-0.0078;to-0.0078;tq-0.0078;tx+0.0142;u'-0.0322;v,-0.0571;v.-0.0630;va-0.0181;"
        + "vc-0.0059;vd-0.0078;ve-0.0059;vg-0.0059;vo-0.0059;vq-0.0078;w,-0.0439;w.-0.0498;wc-0.0029;"
        + "wd-0.0049;we-0.0049;wg-0.0029;wo-0.0029;wq-0.0049;xc-0.0078;xd-0.0078;xe-0.0078;xg-0.0078;"
        + "xo-0.0078;xq-0.0078;y'+0.0142;y,-0.0498;y.-0.0620;y?-0.0371;yc-0.0049;yd-0.0049;ye-0.0049;"
        + "yf+0.0020;yg-0.0049;yo-0.0049;yq-0.0049;yt+0.0029;{j+0.0991;";

    private static readonly string SegoeBoldKernData =
        "'r-0.0298;'s-0.0498;(j+0.0928;*A-0.0649;*J-0.0601;*c-0.0400;*d-0.0400;*e-0.0400;*g-0.0400;"
        + "*o-0.0400;*q-0.0400;A*-0.0601;A,+0.0288;AC-0.0151;AG-0.0098;AJ+0.0381;AO-0.0151;AT-0.0698;"
        + "AU-0.0171;AV-0.0552;AW-0.0322;AY-0.0752;AZ+0.0112;At-0.0200;Av-0.0220;Aw-0.0151;Ay-0.0200;"
        + "BT-0.0239;BY-0.0249;C?+0.0112;CC-0.0298;CG-0.0298;CO-0.0122;CQ-0.0220;D,-0.0498;D.-0.0498;"
        + "DA-0.0151;DT-0.0352;DX-0.0298;DZ-0.0200;EA+0.0142;EJ+0.0239;ET+0.0088;EV+0.0049;EW+0.0200;"
        + "EX+0.0200;F,-0.0698;F.-0.0698;FA-0.0542;FJ-0.0249;FS-0.0098;FT+0.0122;Fa-0.0298;Ff+0.0088;"
        + "GT-0.0200;GV-0.0098;Gy-0.0098;J,-0.0400;J.-0.0400;JA-0.0298;JJ-0.0249;Ja-0.0098;K,+0.0298;"
        + "K?+0.0112;KC-0.0288;KG-0.0288;KJ+0.0288;KO-0.0288;KQ-0.0288;KT+0.0049;KX+0.0200;KZ+0.0200;"
        + "Kc-0.0098;Kd-0.0098;Ke-0.0098;Kg-0.0098;Ko-0.0098;Kq-0.0098;Kt-0.0249;Kv-0.0352;Kw-0.0249;"
        + "Ky-0.0420;L*-0.1001;L?-0.0400;LA+0.0220;LC-0.0249;LG-0.0249;LJ+0.0278;LO-0.0249;LQ-0.0249;"
        + "LT-0.0659;LU-0.0200;LV-0.0571;LW-0.0352;LY-0.0708;LZ+0.0288;Lt-0.0098;Lv-0.0449;Lw-0.0298;"
        + "Ly-0.0352;O,-0.0498;O.-0.0391;OA-0.0151;OJ-0.0098;OT-0.0400;OX-0.0249;OY-0.0200;OZ-0.0200;"
        + "P,-0.1709;P.-0.1499;PA-0.0591;PG+0.0049;PJ-0.0659;PW+0.0171;PX-0.0220;Pa-0.0249;Pc-0.0298;"
        + "Pd-0.0298;Pe-0.0298;Pg-0.0298;Po-0.0298;Pq-0.0298;Q,-0.0391;Q.-0.0391;QA-0.0098;QT-0.0400;"
        + "QX-0.0200;QY-0.0151;QZ-0.0200;RC-0.0098;RG-0.0098;RJ+0.0239;RO-0.0098;RQ-0.0098;RT-0.0200;"
        + "RY-0.0098;Rc-0.0249;Rd-0.0249;Re-0.0249;Rg-0.0249;Ro-0.0249;Rq-0.0249;St-0.0249;Sv-0.0200;"
        + "Sw-0.0098;Sy-0.0249;T,-0.0708;T.-0.0908;T:-0.0088;TA-0.0698;TC-0.0352;TG-0.0352;TJ-0.0659;"
        + "TO-0.0371;TQ-0.0371;TT+0.0200;TV+0.0288;TW+0.0200;TX-0.0020;TY+0.0200;Ta-0.0850;Tc-0.0898;"
        + "Td-0.0898;Te-0.0898;Tf-0.0400;Tg-0.0898;Tm-0.0688;Tn-0.0688;To-0.0898;Tp-0.0640;Tq-0.0898;"
        + "Tr-0.0752;Ts-0.0752;Tu-0.0688;Tv-0.0400;Tw-0.0449;Tx-0.0698;Ty-0.0449;Tz-0.0391;UA-0.0220;"
        + "UJ-0.0181;V,-0.1001;V.-0.1001;V:-0.0200;V?+0.0078;VA-0.0518;VC-0.0200;VG-0.0200;VJ-0.0562;"
        + "VO-0.0020;VQ-0.0122;VS-0.0098;VT+0.0200;Va-0.0752;Vc-0.0649;Vd-0.0649;Ve-0.0649;Vg-0.0601;"
        + "Vm-0.0352;Vn-0.0298;Vo-0.0649;Vp-0.0352;Vq-0.0649;Vr-0.0352;Vs-0.0381;Vu-0.0269;W,-0.0601;"
        + "W.-0.0601;W:-0.0098;WA-0.0352;WT+0.0142;Wa-0.0400;Wc-0.0269;Wd-0.0269;We-0.0269;Wg-0.0269;"
        + "Wo-0.0269;Wq-0.0200;X,+0.0288;X.+0.0288;XC-0.0151;XG-0.0151;XJ+0.0332;XO-0.0151;XQ-0.0151;"
        + "XT+0.0200;Y,-0.1108;Y.-0.1108;YA-0.0752;YC-0.0249;YG-0.0249;YJ-0.0562;YO-0.0249;YQ-0.0249;"
        + "YS-0.0098;YT+0.0200;Ya-0.0898;Yc-0.0898;Yd-0.0898;Ye-0.0898;Yf-0.0151;Yg-0.0898;Ym-0.0649;"
        + "Yn-0.0649;Yo-0.0898;Yp-0.0669;Yq-0.0898;Yr-0.0649;Ys-0.0552;Yu-0.0649;ZJ+0.0239;ZT+0.0200;"
        + "Zy-0.0249;[j+0.0830;ba-0.0098;bf-0.0049;bx-0.0200;cJ+0.0342;cT-0.0400;cY-0.0298;e'-0.0698;"
        + "f)+0.0381;f*+0.0210;f,-0.0498;f--0.0400;f.-0.0498;f:+0.0400;f?+0.0298;f]+0.0381;fb+0.0151;"
        + "fh+0.0088;fk+0.0049;fl+0.0049;ft+0.0190;fv+0.0200;fw+0.0200;fx+0.0088;fy+0.0200;f}+0.0288;"
        + "gj+0.0088;jj+0.0142;k,+0.0400;k--0.0552;k.+0.0400;k:+0.0400;kc-0.0142;kd-0.0098;ke-0.0142;"
        + "kg-0.0142;ko-0.0142;kq-0.0098;kt-0.0059;kz+0.0078;n'-0.0601;o'-0.0801;oa-0.0098;of-0.0151;"
        + "oj-0.0020;ox-0.0200;pa-0.0098;pf-0.0151;px-0.0200;qj+0.0439;r,-0.0801;r--0.0498;r.-0.0801;"
        + "r:+0.0400;rc-0.0039;rd-0.0039;re-0.0039;rf+0.0249;rg-0.0039;rh+0.0029;ri+0.0039;rm+0.0029;"
        + "rn+0.0029;ro-0.0039;rq-0.0098;rs+0.0059;rt+0.0288;ru+0.0029;rv+0.0400;rw+0.0342;rx+0.0269;"
        + "ry+0.0400;rz+0.0200;t--0.0449;t?-0.0400;tc-0.0039;td-0.0039;te-0.0039;tg-0.0039;to-0.0039;"
        + "tq-0.0039;tx+0.0142;u'-0.0400;v,-0.0601;v.-0.0601;va-0.0151;vc-0.0068;vd-0.0068;ve-0.0098;"
        + "vg-0.0098;vo-0.0098;vq-0.0098;w,-0.0400;w.-0.0400;wc-0.0049;wd-0.0049;we-0.0049;wg-0.0049;"
        + "wo-0.0049;wq-0.0049;xc-0.0171;xd-0.0171;xe-0.0171;xg-0.0171;xo-0.0171;xq-0.0171;y'+0.0200;"
        + "y,-0.0552;y.-0.0552;y?+0.0010;yc-0.0098;yd-0.0098;ye-0.0098;yf+0.0078;yg-0.0098;yo-0.0098;"
        + "yq-0.0098;yt+0.0020;{j+0.0781;";

    private static Dictionary<int, double>? _segoeKern, _segoeBoldKern;

    private static Dictionary<int, double> SegoeKern(bool bold)
    {
        var cache = bold ? _segoeBoldKern : _segoeKern;
        if (cache is null)
        {
            cache = new Dictionary<int, double>();
            foreach (var e in (bold ? SegoeBoldKernData : SegoeKernData)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
                cache[(e[0] << 8) | e[1]] = double.Parse(e[2..],
                    System.Globalization.CultureInfo.InvariantCulture);
            if (bold) _segoeBoldKern = cache; else _segoeKern = cache;
        }
        return cache;
    }

    /// <summary>Measure report-band text in the real Segoe UI face metrics.</summary>
    internal static double MeasureReportText(string t, double fs, bool bold)
    {
        if (t.Length == 0) return 0;
        var table = bold ? SegoeBoldAdvances : SegoeAdvances;
        var kern = SegoeKern(bold);
        var w = 0.0;
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            w += c is >= (char)32 and < (char)127 ? table[c - 32]
                : c == ' ' ? table[0]
                : bold ? 0.6 : 0.55;
            if (i + 1 < t.Length && kern.TryGetValue((c << 8) | t[i + 1], out var kv))
                w += kv;
        }
        return w * fs;
    }

    /// <summary>TJ adjustments (thousandths of text space; positive moves the
    /// following glyphs left) for a word's internal kern pairs.</summary>
    private static double[] KernAdjustments(string word, bool bold)
    {
        var k = SegoeKern(bold);
        var adj = new double[Math.Max(0, word.Length - 1)];
        for (var i = 0; i + 1 < word.Length; i++)
            if (k.TryGetValue((word[i] << 8) | word[i + 1], out var v))
                adj[i] = -v * 1000.0;
        return adj;
    }

    /// <summary>Append kerned, word-anchored Segoe-embedded text ops for one line —
    /// the converter's report body draws through this. False when the face is
    /// unavailable (caller keeps its metric-anchored Standard-14 path).</summary>
    internal static bool TryAppendReportLineOps(System.Text.StringBuilder sb,
        Core.PdfDictionary fontDict, string line, double x, string yStr, double fs, bool bold)
    {
        if (SegoeReportTtf(bold) is not { } ttf) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var wx = x;
        foreach (var word in line.Split(' '))
        {
            if (word.Length > 0)
            {
                var (rn, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, ttf, bold ? "SegoeUIBold" : "SegoeUI", word,
                    stripSpacesInBaseFont: true);
                sb.Append('/').Append(rn).Append(' ')
                  .Append(fs.ToString("F1", inv))
                  .Append(" Tf 1 0 0 1 ").Append(wx.ToString("F2", inv))
                  .Append(' ').Append(yStr).Append(" Tm [");
                var adj = KernAdjustments(word, bold);
                var seg = 0;
                void Flush(int endExcl)
                {
                    sb.Append('<');
                    for (var g = seg * 2; g < endExcl * 2 && g < hex.Length; g++)
                        sb.Append(hex[g].ToString("X2"));
                    sb.Append('>');
                    seg = endExcl;
                }
                var glyphs = hex.Length / 2;
                for (var i = 0; i + 1 < glyphs && i < adj.Length; i++)
                {
                    if (adj[i] == 0) continue;
                    Flush(i + 1);
                    sb.Append(adj[i].ToString("0.####", inv));
                }
                Flush(glyphs);
                sb.Append("] TJ ");
            }
            wx += MeasureReportText(word + " ", fs, bold);
        }
        return true;
    }

    private static double RenderReportRegion(Page? page, ContentStreamBuilder? b, string html,
        double x, double w, double yTopBase, bool inFieldset, string? boldRes, string? plainRes)
    {
        // The dialect's rhythm: `div { font-size: small }` = 13 css px = 9.75 pt on the
        // face's normal 17 px line = 12.75 pt. Every distance below is an empirical
        // constant of the dialect, holding on both fieldsets of the region:
        //   FsLegendDrop  — frame top → legend baseline: the browser opens the frame at
        //                   the legend's mid-cap, so the border crosses its letters;
        //   FsLegendToRow — legend baseline → first row baseline (the frame's padding
        //                   plus one row seat);
        //   FsPadBottom   — last row baseline → frame bottom (padding + descent);
        //   FsGap         — frame bottom → the next frame's top (8 css px);
        //   CheckRowPitch — a row holding an <input> takes the input's taller line box.
        const double fs = RptFontPt, pitch = RptRowPitchPt;
        const double FsLegendDrop = 4.41, FsLegendToRow = 16.16;
        const double FsPadBottom = 10.4, FsGap = 6.0, CheckRowPitch = 14.34;
        var draw = page is not null && b is not null;
        double Measure(string t, bool bold) => MeasureReportText(t, fs, bold);
        var rx = new System.Text.RegularExpressions.Regex(
            @"(?s)<(?<tag>div|fieldset)\b(?<attrs>[^>]*)>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        double yBase = yTopBase;
        var pos = 0;
        var pendingCols = new List<(string inner, double frac)>();

        // a fragment with no block children IS one leaf row (a column whose
        // content is a bare input+label pair, or bare text)
        if (!rx.IsMatch(html))
        {
            if (HtmlFragment.StripHtmlTags(html).Trim().Length == 0
                && !html.Contains("<input", StringComparison.OrdinalIgnoreCase))
                return 0;
            html = "<div>" + html + "</div>";
        }

        void FlushCols()
        {
            if (pendingCols.Count == 0) return;
            // sibling columns bottom-align: the tallest sets the group's rows
            var heights = new List<double>();
            foreach (var (inner, frac) in pendingCols)
                heights.Add(RenderReportRegion(null, null, inner, 0, frac * w, 0,
                    inFieldset, null, null));
            var groupH = 0.0;
            foreach (var h in heights) if (h > groupH) groupH = h;
            var cx = x;
            for (var ci = 0; ci < pendingCols.Count; ci++)
            {
                var (inner, frac) = pendingCols[ci];
                RenderReportRegion(page, b, inner, cx, frac * w,
                    yBase + (groupH - heights[ci]), inFieldset, boldRes, plainRes);
                cx += frac * w + RptSpaceEm * fs;
            }
            yBase += groupH;
            pendingCols.Clear();
        }

        while (pos < html.Length)
        {
            var m = rx.Match(html, pos);
            if (!m.Success) break;
            // balanced end of this element
            var depth = 1;
            var scan = m.Index + m.Length;
            var tagRx = new System.Text.RegularExpressions.Regex("<(/?)" + m.Groups["tag"].Value + @"\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var end = html.Length;
            for (var tm = tagRx.Match(html, scan); tm.Success; tm = tagRx.Match(html, tm.Index + 1))
            {
                depth += tm.Groups[1].Value.Length > 0 ? -1 : 1;
                if (depth == 0) { end = tm.Index; break; }
            }
            var inner = html[(m.Index + m.Length)..end];
            pos = Math.Min(html.Length, end + m.Groups["tag"].Value.Length + 3);

            var attrs = m.Groups["attrs"].Value;
            var styleM = System.Text.RegularExpressions.Regex.Match(attrs,
                @"style\s*=\s*(['""])(?<s>[^'""]*)\1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var style = styleM.Success ? styleM.Groups["s"].Value : "";
            var fracM = System.Text.RegularExpressions.Regex.Match(style, @"width\s*:\s*([\d.]+)%");
            var isInline = System.Text.RegularExpressions.Regex.IsMatch(style,
                @"display\s*:\s*inline-block", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var isFieldset = m.Groups["tag"].Value.Equals("fieldset", StringComparison.OrdinalIgnoreCase);

            if (isFieldset)
            {
                FlushCols();
                var frac = fracM.Success ? double.Parse(fracM.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) / 100.0 : 1.0;
                // the legend TAKES the arriving row baseline and the frame opens
                // FsLegendDrop above it, crossing the legend's caps
                var legend = System.Text.RegularExpressions.Regex.Match(inner,
                    @"(?s)<legend[^>]*>(.*?)</legend>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var legendBase = yBase;
                var boxTop = legendBase - FsLegendDrop;
                var innerRows = System.Text.RegularExpressions.Regex.Replace(inner,
                    @"(?s)<legend[^>]*>.*?</legend>", "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var rowsTop = legendBase + FsLegendToRow;
                var rowsH = RenderReportRegion(null, null, innerRows, 0, frac * w, 0, true, null, null);
                var boxBottom = rowsTop + rowsH - pitch + FsPadBottom;
                if (draw)
                {
                    var legendText = legend.Success
                        ? HtmlFragment.StripHtmlTags(legend.Groups[1].Value).Trim() : "";
                    if (legendText.Length > 0)
                        DrawReportWords(b!, boldRes!, legendText,
                            x + RptLegendInsetPt, page!.Height - legendBase, fs, bold: true, page);
                    RenderReportRegion(page, b, innerRows, x + RptFieldsetPadPt, frac * w, rowsTop,
                        true, boldRes, plainRes);
                    // the frame: sides and bottom whole; the TOP edge breaks around
                    // the legend, which rides it
                    var bx0 = x + RptFrameInsetPt;
                    var bx1 = bx0 + frac * w + RptFrameOverhangPt;
                    var byTop = page!.Height - boxTop;
                    var byBot = page.Height - boxBottom;
                    b!.SetStrokeGray(RptFrameGray).SetLineWidth(RptStrokePt)
                      .MoveTo(bx0, byTop).LineTo(bx0, byBot).Stroke()
                      .MoveTo(bx1, byTop).LineTo(bx1, byBot).Stroke()
                      .MoveTo(bx0, byBot).LineTo(bx1, byBot).Stroke();
                    if (legendText.Length > 0)
                    {
                        var lw = MeasureReportText(legendText, fs, bold: true);
                        b.MoveTo(bx0, byTop).LineTo(x + RptLegendInsetPt - RptLegendGapPt, byTop).Stroke()
                         .MoveTo(x + RptLegendInsetPt + lw + RptLegendGapPt, byTop).LineTo(bx1, byTop).Stroke();
                    }
                    else
                        b.MoveTo(bx0, byTop).LineTo(bx1, byTop).Stroke();
                    b.SetStrokeGray(0);
                }
                yBase = boxBottom + FsGap + FsLegendDrop;
                continue;
            }
            if (isInline && fracM.Success)
            {
                pendingCols.Add((inner, double.Parse(fracM.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) / 100.0));
                continue;
            }
            FlushCols();
            // white-space:pre content (class="pre") keeps its own line breaks:
            // one row per source line
            if (System.Text.RegularExpressions.Regex.IsMatch(attrs,
                    @"class\s*=\s*(['""])[^'""]*\bpre\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                foreach (var preLine in HtmlFragment.StripHtmlTags(inner)
                             .Replace("\r", "").Split('\n'))
                {
                    var pl = preLine.Trim();
                    if (pl.Length == 0) continue;
                    if (draw)
                        DrawReportWords(b!, plainRes!, pl, x, page!.Height - yBase, fs, bold: false, page);
                    yBase += pitch;
                }
                continue;
            }
            // a leaf row: label + value, a checkbox + label, a background band,
            // plain text, or a blank spacer; a div of nested divs recurses
            if (System.Text.RegularExpressions.Regex.IsMatch(inner, @"(?s)^\s*<(div|fieldset)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && !System.Text.RegularExpressions.Regex.IsMatch(inner,
                    @"(?s)^\s*<div\b[^>]*background-color",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                yBase += RenderReportRegion(page, b, inner, x, w, yBase, inFieldset, boldRes, plainRes);
                continue;
            }
            var bgM = System.Text.RegularExpressions.Regex.Match(inner + " " + style,
                @"background-color\s*:\s*([-\w#]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var lbl = System.Text.RegularExpressions.Regex.Match(inner,
                @"(?s)<label[^>]*>(.*?)</label>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var val = System.Text.RegularExpressions.Regex.Match(inner,
                @"(?s)<span[^>]*>(.*?)</span>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var hasCheckbox = System.Text.RegularExpressions.Regex.IsMatch(inner,
                @"<input\b[^>]*type\s*=\s*(['""])?checkbox",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var text = HtmlFragment.StripHtmlTags(System.Text.RegularExpressions.Regex.Replace(
                inner, @"(?s)<(label|span|input)\b[^>]*>|</(label|span)>", "")).Trim();

            if (draw && bgM.Success
                && Converters.HtmlToPdfConverter.ParseCssColor(bgM.Groups[1].Value.Trim()) is { } bandBg)
                b!.SetFillColor(bandBg.R / 255.0, bandBg.G / 255.0, bandBg.B / 255.0)
                  .Rectangle(x, page!.Height - (yBase + RptBandDescentPt), w, pitch).Fill()
                  .SetFillColor(0, 0, 0);

            if (lbl.Success || hasCheckbox)
            {
                var labelText = lbl.Success
                    ? HtmlFragment.StripHtmlTags(lbl.Groups[1].Value).Trim() : "";
                // a checkbox row is TALLER — the input's own line box — and its
                // content seats on the taller row's OWN baseline
                var rowPitch = hasCheckbox ? CheckRowPitch : pitch;
                var rowBase = yBase + (rowPitch - pitch);
                if (draw)
                {
                    var cx2 = x;
                    if (hasCheckbox)
                    {
                        // the checkbox square, then its label INLINE (input+label css)
                        b!.SetStrokeGray(RptCheckboxGray).SetLineWidth(RptStrokePt)
                          .Rectangle(x + RptCheckboxIndentPt,
                              page!.Height - (rowBase + RptCheckboxRisePt),
                              RptCheckboxSizePt, RptCheckboxSizePt)
                          .Stroke().SetStrokeGray(0);
                        if (labelText.Length > 0)
                            DrawReportWords(b, boldRes!, labelText,
                                x + RptCheckboxIndentPt + RptCheckboxSizePt + RptCheckboxLabelGapPt,
                                page.Height - rowBase, fs, bold: true, page);
                    }
                    else
                    {
                        var labelRight = x + (inFieldset ? RptFieldsetLabelFrac : RptLabelBoxFrac) * w;
                        if (labelText.Length > 0)
                            DrawReportWords(b!, boldRes!, labelText,
                                labelRight - Measure(labelText, true), page!.Height - yBase, fs, bold: true, page);
                        var valText = val.Success
                            ? HtmlFragment.StripHtmlTags(val.Groups[1].Value).Trim() : "";
                        // a fieldset row's value follows after ONE space; outside,
                        // the label's .5-em margin comes first — both measured
                        if (valText.Length > 0)
                            DrawReportWords(b!, plainRes!, valText,
                                labelRight + (inFieldset ? RptSpaceEm
                                    : RptLabelMarginEm + RptSpaceEm) * fs,
                                page!.Height - yBase, fs, bold: false, page);
                        _ = cx2;
                    }
                }
                yBase += rowPitch;
                continue;
            }
            // plain text, an &nbsp; spacer line, or a genuinely EMPTY div — the
            // empty one gets NO line box at all
            var plainTxt = System.Text.RegularExpressions.Regex.Replace(
                text.Replace("&nbsp;", " ").Replace(' ', ' '), @"\s+", " ").Trim();
            if (draw && plainTxt.Length > 0)
                DrawReportWords(b!, plainRes!, plainTxt, x, page!.Height - yBase, fs, bold: false, page);
            if (plainTxt.Length > 0
                || inner.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase)
                || inner.Contains(' '))
                yBase += pitch;
        }
        FlushCols();
        return yBase - yTopBase;
    }

    /// <summary>A `cm` operator mapping the page's VISUAL (rotation-adjusted) space — where
    /// header/footer content is laid out using <c>page.Width</c>/<c>page.Height</c> — into raw
    /// page-content space, so content drawn for a <c>/Rotate 90/180/270</c> page appears upright
    /// at its laid-out position. Returns null for an unrotated page (Wm/Hm = raw MediaBox dims;
    /// same mapping the watermark-on-rotated-page path uses).</summary>
    private static string? VisualToRawRotationCm(Page page)
    {
        var mb = page.MediaBox;
        var wm = mb.Width.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var hm = mb.Height.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return page.Rotate switch
        {
            Rotation.on90 => $"0 1 -1 0 {wm} 0 cm",
            Rotation.on180 => $"-1 0 0 -1 {wm} {hm} cm",
            Rotation.on270 => $"0 -1 1 0 0 {hm} cm",
            _ => null,
        };
    }

    /// <summary>Walk a HeaderFooter's Paragraphs tree (descending into Tables/
    /// Rows/Cells and into FloatingBox/HeaderFooter containers) and emit one
    /// LinkAnnotation per TextFragment carrying a Hyperlink. Rects are
    /// approximate -- only the destination is asserted,
    /// not the rect -- but staying near the header/footer band
    /// keeps the click target plausible.</summary>
    private static void EmitNestedHyperlinks(Page page, Paragraphs paragraphs,
        double x, double y, double fontSize)
    {
        foreach (var para in paragraphs)
            EmitFromParagraph(page, para, x, y, fontSize);
    }

    private static void EmitFromParagraph(Page page, BaseParagraph para,
        double x, double y, double fontSize)
    {
        if (para is TextFragment tf)
        {
            if (tf.HyperlinkValue is { } h)
                EmitLinkAt(page, x, y, fontSize, EstimateWidth(tf.Text, fontSize), h);
            foreach (var seg in tf.Segments)
                if (seg.Hyperlink is { } sh)
                    EmitLinkAt(page, x, y, fontSize, EstimateWidth(seg.Text, fontSize), sh);
            return;
        }
        if (para is Table table)
        {
            foreach (var row in table.Rows)
                foreach (var cell in row.Cells)
                    foreach (var inner in cell.Paragraphs)
                        EmitFromParagraph(page, inner, x, y, fontSize);
        }
    }

    private static double EstimateWidth(string? text, double fontSize) =>
        (text?.Length ?? 0) * fontSize * 0.5;

    private static void EmitLinkAt(Page page, double x, double y, double fontSize,
        double width, Hyperlink hyperlink)
    {
        var rect = new Rectangle(x, y - fontSize, x + width, y);
        if (hyperlink is LocalHyperlink lh && lh.TargetPageNumber > 0)
        {
            page.Annotations.AddLinkAnnotation(rect,
                new Annotations.GoToAction(
                    new Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
        }
        else if (hyperlink is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
        {
            page.Annotations.AddLinkAnnotation(rect, wh.Url);
        }
    }

    /// <summary>Find the first CSS <c>font-family</c> declared in the fragment's HTML
    /// that resolves (via FontRepository, including FolderFontSource registrations) to a
    /// face with a real font program. Returns null when nothing resolves — the caller
    /// then keeps the Standard-14 path.</summary>
    private static Aspose.Pdf.Text.Font? ResolveDeclaredFont(string html)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
            html, @"font-family\s*:\s*[""']{0,2}([^;""'}]+)"))
        {
            var family = m.Groups[1].Value.Trim();
            if (family.Length == 0) continue;
            try
            {
                var f = Aspose.Pdf.Text.FontRepository.FindFont(family);
                if (f?.SourceFontData?.TtfData is { Length: > 0 }) return f;
            }
            catch { /* unknown family: try the next declaration */ }
        }
        return null;
    }

    /// <summary>The page's /Resources /Font dictionary, created on demand.</summary>
    private static PdfDictionary ResolvePageFontDict(Page page)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) { resources = new PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) { fontDict = new PdfDictionary(); resources.Set("Font", fontDict); }
        return fontDict;
    }

    /// <summary>
    /// Ensure the Standard 14 font is registered in the page's /Resources /Font dictionary.
    /// Returns the resource name (e.g. "F1").
    /// </summary>
    private static string EnsureFontResource(Page page, string baseFontName)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary;
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }

        var fontDict = resources.Get("Font") as PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        // Check if this base font is already registered
        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary;
            if (entry is null)
            {
                var raw = fontDict.Get(key);
                entry = page.Reader.ResolveDict(raw);
            }

            if (entry is not null)
            {
                var existing = entry.GetName("BaseFont");
                if (string.Equals(existing, baseFontName, StringComparison.Ordinal))
                    return key;
            }
        }

        // Find a unique resource name in the header/footer's own "HF" namespace so it
        // never collides with the body's F1..Fn fonts on a shared (overflow) page —
        // otherwise the footer could claim e.g. /F2 and the body text drawn with /F2
        // would render in the footer's face (wrong glyph metrics).
        var name = "HF1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"HF{++counter}";

        // Create the font dictionary
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(name, font);

        return name;
    }
}
