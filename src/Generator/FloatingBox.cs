using System.Globalization;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>How <see cref="FloatingBox.Left"/> / <see cref="FloatingBox.Top"/> are interpreted.</summary>
public enum ParagraphPositioningMode
{
    Default,
    Absolute,
}

/// <summary>
/// Represents a floating box that can be positioned absolutely or flowed on a page.
/// Supports background color, border, padding, margin, and paragraph content.
/// </summary>
public class FloatingBox : BaseParagraph
{
    /// <summary>Width of the box in points.</summary>
    public double Width { get; set; }

    /// <summary>Height of the box in points.</summary>
    public double Height { get; set; }

    /// <summary>Left position in points (absolute). 0 with PositioningMode==Default means flow position.</summary>
    public double Left { get; set; }

    /// <summary>Top position in points (absolute, measured from top of page). 0 with PositioningMode==Default means flow position.</summary>
    public double Top { get; set; }

    /// <summary>Border around the box.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Background fill color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Whether the box should repeat on every page.</summary>
    public bool IsNeedRepeating { get; set; }

    /// <summary>Inner padding of the box.</summary>
    public MarginInfo? Padding { get; set; }

    /// <summary>Outer margin of the box. Auto-initialized so callers can
    /// set <c>box.Margin.Top = 10</c> on a freshly-constructed FloatingBox.</summary>
    public new MarginInfo Margin { get; set; } = new MarginInfo();

    /// <summary>Vertical alignment of content inside the box.</summary>
    public new VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

    /// <summary>Horizontal alignment of the box within its parent. Stored only;
    /// when <see cref="Left"/> is set, that absolute position takes precedence.</summary>
    public new HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Z-index for layering; higher draws on top.</summary>
    public new int ZIndex { get; set; }

    public ColumnInfo ColumnInfo { get; set; } = new ColumnInfo();

    /// <summary>
    /// Content paragraphs (TextFragment, Table, or other content objects).
    /// </summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Optional background image rendered behind the box content.</summary>
    public Image? BackgroundImage { get; set; }

    /// <summary>How <see cref="Left"/> / <see cref="Top"/> are interpreted —
    /// Default participates in document flow, Absolute pins to page coordinates.</summary>
    public ParagraphPositioningMode PositioningMode { get; set; } = ParagraphPositioningMode.Default;

    /// <summary>
    /// Create a floating box with default (zero) dimensions.
    /// </summary>
    public FloatingBox()
    {
    }

    /// <summary>
    /// Create a floating box with specified dimensions.
    /// </summary>
    /// <param name="width">Width in points.</param>
    /// <param name="height">Height in points.</param>
    public FloatingBox(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Create a floating box with Single-typed dimensions.</summary>
    public FloatingBox(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public override object Clone() => MemberwiseClone();

    /// <summary>
    /// Build the content stream bytes for this floating box on the given page.
    /// </summary>
    /// <param name="page">The page context (used for coordinate conversion and resource registration).</param>
    /// <returns>PDF content stream bytes ready to be appended to the page.</returns>
    /// <summary>Bottom margin of the hosting page's content area, set by the
    /// layout dispatcher before <see cref="Build"/>. A columned box flows its
    /// text down to this line (its own Height does not clip the content).</summary>
    internal double PageBottomMargin { get; set; } = 72;

    /// <summary>Columned text content (ColumnInfo with explicit widths): each column
    /// is a y-flipped, clipped band at the box's top; text is the concatenated
    /// paragraph text in the HtmlFragment default face (Times New Roman 12, embedded
    /// CID), lines on an (ascent+descent+lineGap)/em pitch filling column after
    /// column; the border wraps the FIRST column only and grows to the content
    /// height when that exceeds the box Height.</summary>
    private byte[]? BuildColumnContent(Page page, double boxX, double yTop, out double contentHeight)
    {
        contentHeight = 0;
        var widthsStr = ColumnInfo?.ColumnWidths;
        if (ColumnInfo is null || ColumnInfo.ColumnCount < 2 || string.IsNullOrWhiteSpace(widthsStr))
            return null;
        var parts = widthsStr.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var colWidths = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out colWidths[i]))
                return null;
        if (colWidths.Length == 0) return null;
        double.TryParse(ColumnInfo.ColumnSpacing, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var colSpacing);

        // Concatenate the box's text content (HtmlFragments stripped to text).
        var sb = new System.Text.StringBuilder();
        foreach (var para in Paragraphs)
        {
            var t = para switch
            {
                HtmlFragment hf => HtmlFragment.StripHtmlTags(hf.HtmlContent ?? ""),
                TextFragment tf => tf.Text,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(t))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
            }
        }
        if (sb.Length == 0) return null;

        var ttf = Text.FontRepository.GetTtfData("Times New Roman");
        if (ttf is null) return null;
        const double size = 12;
        // Layout metrics for the Times New Roman face revision these rules were
        // tuned on (hhea 1843/-461 over 2048): pitch = (ascent+|descent|)/em =
        // 1.125 em, first baseline = ascent/em below the column top. Pinned
        // rather than parsed — the locally installed face revision carries
        // slightly different values (1825/-443) and would drift every line off
        // the expected pitch.
        var pitch = 1.125 * size;
        var firstBaseline = 0.899902 * size;

        var fontData = new Text.FontData("Times New Roman", Text.FontType.TrueType);
        fontData.SetTtfData(ttf);
        var lines = Text.TextPaginator.WrapToWidth(sb.ToString(), "Times New Roman", size,
            colWidths[0], fontData);
        if (lines.Count == 0) return null;

        var clipH = yTop - PageBottomMargin;
        if (clipH <= pitch) return null;
        var perColumn = Math.Max(1, (int)Math.Floor(clipH / pitch));

        var fontDict = Table.ResolvePageFontDict(page);
        var b = new ContentStreamBuilder();
        var lineIdx = 0;
        var colX = boxX;
        for (var col = 0; col < ColumnInfo.ColumnCount && lineIdx < lines.Count; col++)
        {
            var colW = colWidths[Math.Min(col, colWidths.Length - 1)];
            b.SaveState();
            // Top-down local frame at the column's top-left, clipped to the
            // column width and the band down to the page bottom margin.
            b.SetMatrix(1, 0, 0, -1, colX, yTop);
            b.Rectangle(0, 0, colW, clipH);
            b.ClipEvenOdd();
            for (var i = 0; i < perColumn && lineIdx < lines.Count; i++, lineIdx++)
            {
                var line = lines[lineIdx];
                if (line.Length == 0) continue;
                var y = firstBaseline + pitch * i;
                if (col == 0) contentHeight = y;
                var (resName, hex) = Text.Type0FontEmbedder.Embed(
                    fontDict, ttf, "Times New Roman", line, stripSpacesInBaseFont: true);
                b.BeginText();
                b.SetFont(resName, size);
                b.SetTextMatrix(1, 0, 0, -1, 0, y);
                b.ShowTextHex(hex);
                b.EndText();
            }
            b.RestoreState();
            colX += colW + colSpacing;
        }
        return b.Build();
    }

    private double? ParseFirstColumnWidth()
    {
        var parts = ColumnInfo?.ColumnWidths?.Split(new[] { ' ', '\t', ',' },
            StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: > 0 }
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            ? w : null;
    }

    public byte[] Build(Page page)
    {
        var pageHeight = page.Height;
        var builder = new ContentStreamBuilder();

        // Calculate the box position in PDF coordinates (origin at bottom-left).
        // Left/Top are in page coordinates where Top is measured from the top edge.
        var marginLeft = Margin?.Left ?? 0;
        var marginTop = Margin?.Top ?? 0;
        var marginRight = Margin?.Right ?? 0;
        var marginBottom = Margin?.Bottom ?? 0;

        // Box origin (bottom-left corner in PDF coordinates)
        double boxX = Left + marginLeft;
        double boxY;

        if (PositioningMode == ParagraphPositioningMode.Absolute)
        {
            // Top is distance from the top of the page
            boxY = pageHeight - Top - marginTop - Height;
        }
        else
        {
            // Default: place at top of page with margin
            boxY = pageHeight - marginTop - Height;
        }

        // Columned text content: the columns flow past the box Height down to the
        // page bottom margin; the border wraps the first column only and grows to
        // the content height. Emitted as its own op run (content, then border).
        if (ColumnInfo is { ColumnCount: > 1 }
            && BuildColumnContent(page, boxX, boxY + Height, out var colContentH) is { } colOps)
        {
            if (Border is not null && Border.Side != BorderSide.None)
            {
                var borderH = Math.Max(Height, colContentH);
                var borderW = ParseFirstColumnWidth() ?? Width;
                var bb = new ContentStreamBuilder();
                bb.SaveState();
                bb.SetLineWidth(Border.Width);
                bb.SetStrokeColor(Border.Color);
                // One rect outset by half the stroke width around the first
                // column band.
                var half = Border.Width / 2;
                bb.Rectangle(boxX - half, boxY + Height - borderH - half,
                    borderW + Border.Width, borderH + Border.Width);
                bb.Stroke();
                bb.RestoreState();
                var borderOps = bb.Build();
                var combined = new byte[colOps.Length + borderOps.Length];
                colOps.CopyTo(combined, 0);
                borderOps.CopyTo(combined, colOps.Length);
                return combined;
            }
            return colOps;
        }

        var padLeft = Padding?.Left ?? 0;
        var padTop = Padding?.Top ?? 0;
        var padRight = Padding?.Right ?? 0;
        var padBottom = Padding?.Bottom ?? 0;

        builder.SaveState();

        // Draw background
        if (BackgroundColor is not null)
        {
            builder.SetFillColor(BackgroundColor);
            builder.Rectangle(boxX, boxY, Width, Height);
            builder.Fill();
        }

        // Draw border
        if (Border is not null && Border.Side != BorderSide.None)
        {
            builder.SetLineWidth(Border.Width);
            builder.SetStrokeColor(Border.Color);

            var side = Border.Side;

            if (side == BorderSide.All)
            {
                // A full border is emitted as a single rectangle subpath so the box
                // outline is one `re` operator (matching the all-sides border shape)
                // rather than four disjoint line segments.
                builder.Rectangle(boxX, boxY, Width, Height);
                builder.Stroke();
            }
            else
            {
                if (side.HasFlag(BorderSide.Bottom))
                {
                    builder.MoveTo(boxX, boxY);
                    builder.LineTo(boxX + Width, boxY);
                    builder.Stroke();
                }

                if (side.HasFlag(BorderSide.Top))
                {
                    builder.MoveTo(boxX, boxY + Height);
                    builder.LineTo(boxX + Width, boxY + Height);
                    builder.Stroke();
                }

                if (side.HasFlag(BorderSide.Left))
                {
                    builder.MoveTo(boxX, boxY);
                    builder.LineTo(boxX, boxY + Height);
                    builder.Stroke();
                }

                if (side.HasFlag(BorderSide.Right))
                {
                    builder.MoveTo(boxX + Width, boxY);
                    builder.LineTo(boxX + Width, boxY + Height);
                    builder.Stroke();
                }
            }
        }

        // Render paragraph content (text fragments)
        var contentX = boxX + padLeft;
        var contentY = boxY + Height - padTop; // Start from top of content area

        foreach (var paragraph in Paragraphs)
        {
            if (paragraph is TextFragment textFragment)
            {
                var fontSize = textFragment.TextState.FontSize;
                var fontResName = EnsureFontResource(page, textFragment.TextState.FontName ?? "Helvetica");

                // Move down by font size for each line (PDF text is baseline-positioned)
                contentY -= fontSize;

                if (contentY < boxY + padBottom)
                    break; // No more room

                builder.BeginText();
                builder.SetFont(fontResName, fontSize);

                // Apply foreground color if set
                if (textFragment.TextState.ForegroundColor is { } fg)
                {
                    builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
                }

                builder.MoveTextPosition(contentX, contentY);
                builder.ShowText(textFragment.Text);
                builder.EndText();

                // Add line spacing after the text
                var lineSpacing = textFragment.TextState.LineSpacing > 0
                    ? textFragment.TextState.LineSpacing
                    : fontSize * 0.2; // Default 20% of font size as inter-paragraph spacing
                contentY -= lineSpacing;
            }
            else if (paragraph is HtmlFragment htmlChild)
            {
                // An HtmlFragment child sets in the HTML engine's own face and rhythm
                // (Times New Roman 12 in a 13.5 pt line box, bold runs inline, <br> a
                // forced break) — the box only supplies the content origin. Without this
                // the fragment was silently dropped: the loop knew TextFragment, Table
                // and Image only.
                var htmlW = Width - padLeft - padRight;
                var consumed = Table.DrawHtmlEngineFragment(builder, page, htmlChild.HtmlContent,
                    contentX, contentY, htmlW > 0 ? htmlW : 0);
                if (consumed is { } usedH) contentY -= usedH;
            }
            else if (paragraph is Table table)
            {
                // Position the nested table at the box's content origin. Table.Build
                // would otherwise place it at the page's top-left (its Left/Top default
                // to 0), leaving the table detached from the box background. FlowLeftOffset
                // sets the X; the BuildMultiPage startY argument sets the top edge.
                table.FlowLeftOffset = contentX;
                var tableContents = table.BuildMultiPage(page, contentY);
                builder.RestoreState();
                page.AddContentStream(builder.Build());
                if (tableContents.Count > 0) page.AddContentStream(tableContents[0]);
                if (table.LastGraphDraws.Count > 0)
                    foreach (var gc in table.LastGraphDraws[0])
                        page.AddContentStream(gc);
                if (table.LastImageDraws.Count > 0)
                    foreach (var (data, rect) in table.LastImageDraws[0])
                        page.AddImage(data, rect);
                contentY -= table.LastRenderedHeight;
                // Start a new builder for remaining paragraphs
                builder = new ContentStreamBuilder();
                builder.SaveState();
            }
            else if (paragraph is Image image)
            {
                // Read the image bytes (stream or file).
                byte[]? data = null;
                if (image.ImageStream is not null)
                {
                    var keepPos = image.ImageStream.CanSeek ? image.ImageStream.Position : -1L;
                    if (image.ImageStream.CanSeek) image.ImageStream.Position = 0;
                    using var mem = new System.IO.MemoryStream();
                    image.ImageStream.CopyTo(mem);
                    data = mem.ToArray();
                    if (keepPos >= 0) image.ImageStream.Position = keepPos;
                }
                else if (!string.IsNullOrEmpty(image.File) && System.IO.File.Exists(image.File))
                {
                    data = System.IO.File.ReadAllBytes(image.File);
                }
                if (data is null) continue;

                // Size: explicit Fix dimensions win; otherwise the image's natural
                // size scaled by ImageScale (the page-level flow path applies the
                // same factor to a box-contained image).
                double imgW = image.FixWidth, imgH = image.FixHeight;
                if (imgW <= 0 || imgH <= 0)
                {
                    if (Document.TryGetImageNaturalSizePt(data, out var natW, out var natH))
                    {
                        var imScale = image.ImageScale > 0 ? image.ImageScale : 1.0;
                        if (imgW <= 0) imgW = natW * imScale;
                        if (imgH <= 0) imgH = natH * imScale;
                    }
                }
                if (imgW <= 0 || imgH <= 0) continue;

                // Offset by the image's own margin within the box content area.
                var imLeft = image.Margin?.Left ?? 0;
                var imTop = image.Margin?.Top ?? 0;
                var imBottom = image.Margin?.Bottom ?? 0;
                contentY -= imTop;
                var ix = contentX + imLeft;
                var iy = contentY - imgH;
                // Flush the background/border so the image draws on top of them, mirroring
                // how the nested-table branch above orders its content.
                builder.RestoreState();
                page.AddContentStream(builder.Build());
                page.AddImage(data, new Rectangle(ix, iy, ix + imgW, iy + imgH));
                contentY -= imgH + imBottom;
                builder = new ContentStreamBuilder();
                builder.SaveState();
            }
        }

        builder.RestoreState();

        return builder.Build();
    }

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

        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary;
            if (entry is null)
                entry = page.Reader.ResolveDict(fontDict.Get(key));
            if (entry is not null && string.Equals(entry.GetName("BaseFont"), baseFontName, StringComparison.Ordinal))
                return key;
        }

        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(name, font);
        return name;
    }
}
