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
    /// Returns <see cref="object"/> to match the Aspose.PDF for .NET reflection shape.
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
    internal void RenderToPage(Page page, bool isHeader, int pageNumber)
    {
        var text = Text.Replace("#", pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        StampText(page, text, isHeader);
    }

    private void StampText(Page page, string textContent, bool isHeader)
    {
        // If we have Paragraphs, render them instead of plain text
        if (Paragraphs.Count > 0)
        {
            StampParagraphs(page, isHeader);
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

    private void StampParagraphs(Page page, bool isHeader)
    {
        var pageHeight = page.Height;
        var fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        var y = isHeader
            ? pageHeight - Margin.Top - fontSize
            : Margin.Bottom + fontSize;
        var x = Margin.Left;

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
                var imgH = img.FixHeight > 0 ? img.FixHeight : imgW;
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
                text = tf.Text ?? "";
                if (tf.TextState.FontName is not null) fn = tf.TextState.FontName;
                if (tf.TextState.FontSize > 0) fs = tf.TextState.FontSize;
            }
            else if (para is HtmlFragment htmlFrag)
            {
                text = HtmlFragment.StripHtmlTags(htmlFrag.HtmlContent ?? "");
            }

            if (string.IsNullOrWhiteSpace(text)) continue;

            var fontRes = EnsureFontResource(page, fn);
            var builder = new ContentStreamBuilder();
            builder.SaveState();
            builder.BeginText()
                .SetFont(fontRes, fs)
                .MoveTextPosition(x, y)
                .ShowText(text!)
                .EndText()
                .RestoreState();
            page.AddContentStream(builder.Build());
            y -= fs * 1.2;
        }
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

        // Find a unique resource name
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

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
