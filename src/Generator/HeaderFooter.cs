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

    /// <summary>Margins controlling the position of the header/footer text.</summary>
    public MarginInfo Margin { get; set; } = new(20, 20, 20, 20);

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
    /// Returns <see cref="object"/> to match the Aspose.Pdf reflection shape.
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
    /// paragraph content at the top (header) or bottom (footer) margin.</summary>
    internal void RenderToPage(Page page, bool isHeader, int pageNumber, Document? document = null)
    {
        var text = Text.Replace("#", pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        text = ApplyLabelMacros(text, document, pageNumber);
        StampText(page, text, isHeader, document, pageNumber);
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
        Document? document = null, int pageNumber = 0)
    {
        // If we have Paragraphs, render them instead of plain text
        if (Paragraphs.Count > 0)
        {
            StampParagraphs(page, isHeader, document, pageNumber);
            return;
        }

        var fontName = TextState.FontName ?? "Helvetica";
        var fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        var fontResName = EnsureFontResource(page, fontName);

        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var textWidth = textContent.Length * fontSize * 0.5;

        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Left => Margin.Left,
            HorizontalAlignment.Right => pageWidth - Margin.Right - textWidth,
            _ => (pageWidth - textWidth) / 2,
        };

        var y = isHeader
            ? pageHeight - Margin.Top - fontSize
            : Margin.Bottom;

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

    private void StampParagraphs(Page page, bool isHeader,
        Document? document = null, int pageNumber = 0)
    {
        var pageHeight = page.Height;
        var fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        var y = isHeader
            ? pageHeight - Margin.Top - fontSize
            : Margin.Bottom + fontSize;
        var x = Margin.Left;
        // Baseline and end-X of the last rendered text paragraph, so a following
        // TextFragment with IsInLineParagraph continues on the SAME line directly
        // after it (the reference renders such fragments inline, with no gap).
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

                var imgW = img.FixWidth > 0 ? img.FixWidth : page.Width - Margin.Left - Margin.Right;
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
                Rectangle rect;
                if (isHeader)
                {
                    // Header: image top edge at current y, growing downward.
                    rect = new Rectangle(x, y - imgH, x + imgW, y);
                    y -= imgH + 4;
                }
                else
                {
                    // Footer: image bottom edge at Margin.Bottom, growing upward.
                    var bottom = Margin.Bottom;
                    rect = new Rectangle(x, bottom, x + imgW, bottom + imgH);
                    y = bottom + imgH + 4;
                }
                try { page.AddImage(imgData, rect); }
                catch (ArgumentException) { continue; }
                continue;
            }

            // ── FloatingBox ────────────────────────────────────────────
            // A header/footer box honours its Top/Left (positioned at page
            // coordinates) and renders its background plus nested paragraphs
            // (e.g. a Table). Its Left is relative to the page's left content
            // margin — matching the reference layout — so offset by that
            // margin (the 90 pt Generator default when untouched) while
            // rendering, then restore the caller's values.
            if (para is FloatingBox fb)
            {
                var hm = page.PageInfo?.Margin;
                var leftMargin = hm?.LeftTouched == true ? hm.Left : 90;
                var savedLeft = fb.Left;
                var savedMode = fb.PositioningMode;
                // A fixed-height header box crams its content: the reference
                // layout fits a nested table's rows inside the box height. FOSS
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
                var contents = tbl.BuildMultiPage(page, y);
                if (contents.Count > 0) page.AddContentStream(contents[0]);
                y -= tbl.LastRenderedHeight;
                continue;
            }

            string? text = null;
            var fn = TextState.FontName ?? "Helvetica";
            var fs = fontSize;

            if (para is TextFragment tf)
            {
                text = ApplyLabelMacros(tf.Text ?? "", document, pageNumber);
                if (tf.TextState.FontName is not null) fn = tf.TextState.FontName;
                if (tf.TextState.FontSize > 0) fs = tf.TextState.FontSize;
            }
            else if (para is HtmlFragment htmlFrag)
            {
                var hc = htmlFrag.HtmlContent ?? "";
                // An HTML <table> in a header/footer fragment renders as real columns
                // (rows × cells) rather than the flat tag-stripped text stack: build a
                // generator Table and lay it out bottom-aligned to the footer band.
                if (Converters.HtmlToPdfConverter.ContainsTable(hc))
                {
                    var htmlTbl = Converters.HtmlToPdfConverter.BuildTableFromHtml(hc);
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
                        htmlTbl.BuildMultiPage(page, page.Height, 0);
                        var startY = isHeader ? y : Margin.Bottom + htmlTbl.LastRenderedHeight;
                        var contents = htmlTbl.BuildMultiPage(page, startY, -page.Height);

                        if (rotCm is null)
                        {
                            if (contents.Count > 0) page.AddContentStream(contents[0]);
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
                text = HtmlFragment.StripHtmlTags(hc);
            }

            if (string.IsNullOrWhiteSpace(text)) continue;

            // Inline paragraph: continue on the previous text line right after its
            // last glyph instead of starting a new line.
            bool inline = para is TextFragment inlineTf && inlineTf.IsInLineParagraph
                && !double.IsNaN(lastTextY);
            var drawX = inline ? lastTextEndX : x;
            var drawY = inline ? lastTextY : y;

            var fontRes = EnsureFontResource(page, fn);
            var builder = new ContentStreamBuilder();
            builder.SaveState();
            builder.BeginText()
                .SetFont(fontRes, fs)
                .MoveTextPosition(drawX, drawY)
                .ShowText(text!)
                .EndText()
                .RestoreState();
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
