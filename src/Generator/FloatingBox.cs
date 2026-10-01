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
public partial class FloatingBox : BaseParagraph
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

    /// <summary>Inner padding of the box. Auto-initialised so an object
    /// initialiser can write <c>Padding = { Top = 20 }</c> on a fresh box.</summary>
    public MarginInfo? Padding { get; set; } = new MarginInfo();

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

    /// <summary>Gets or sets the column layout. With two or more columns and explicit <c>ColumnWidths</c>, the box's text flows from column to column down to the page's bottom margin.</summary>
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

    /// <summary>Continuation pages produced by the last <see cref="Build"/>: a
    /// breakable table inside a fixed-height box spills its remaining rows here,
    /// one content stream per fresh page (the box re-seated at the content top).
    /// The flow layout appends them to the document's overflow pages.</summary>
    internal List<byte[]> LastOverflowPages { get; } = new();

    /// <summary>The box rectangle (page points) the last <see cref="Build"/> painted:
    /// the area a paragraph <see cref="BaseParagraph.Hyperlink"/> covers.</summary>
    internal Rectangle LastBoxRect { get; private set; } = new Rectangle(0, 0, 0, 0);

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
    private (byte[]? result, double contentHeight) BuildColumnContent(Page page, double boxX, double yTop)
    {
        double contentHeight = default;
        contentHeight = 0;
        var widthsStr = ColumnInfo?.ColumnWidths;
        if (ColumnInfo is null || ColumnInfo.ColumnCount < 2 || string.IsNullOrWhiteSpace(widthsStr))
            return (null, contentHeight);
        var parts = widthsStr.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var colWidths = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out colWidths[i]))
                return (null, contentHeight);
        if (colWidths.Length == 0) return (null, contentHeight);
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
        if (sb.Length == 0) return (null, contentHeight);

        var ttf = Text.FontRepository.GetTtfData("Times New Roman");
        if (ttf is null) return (null, contentHeight);
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
        if (lines.Count == 0) return (null, contentHeight);

        var clipH = yTop - PageBottomMargin;
        if (clipH <= pitch) return (null, contentHeight);
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
        return (b.Build(), contentHeight);
    }

    private double? ParseFirstColumnWidth()
    {
        var parts = ColumnInfo?.ColumnWidths?.Split(new[] { ' ', '\t', ',' },
            StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: > 0 }
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            ? w : null;
    }

    /// <summary>
    /// Build the content stream bytes for this floating box on the given page.
    /// </summary>
    /// <param name="page">The page context (used for coordinate conversion and resource registration).</param>
    /// <returns>PDF content stream bytes ready to be appended to the page.</returns>
    public byte[] Build(Page page)
    {
        var bx = new FloatingBoxBuildState();
        bx.page = page;
        LastOverflowPages.Clear();
        bx.pageHeight = bx.page.Height;
        bx.builder = new ContentStreamBuilder();

        bx.marginLeft = Margin?.Left ?? 0;
        bx.marginTop = Margin?.Top ?? 0;
        bx.marginRight = Margin?.Right ?? 0;
        bx.marginBottom = Margin?.Bottom ?? 0;

        bx.boxX = Left + bx.marginLeft;

        if (PositioningMode == ParagraphPositioningMode.Absolute)
        {
            // Top is distance from the top of the page
            bx.boxY = bx.pageHeight - Top - bx.marginTop - Height;
        }
        else
        {
            // Default: place at top of page with margin
            bx.boxY = bx.pageHeight - bx.marginTop - Height;
        }

        // Columned text content: the columns flow past the box Height down to the
        // page bottom margin; the border wraps the first column only and grows to
        // the content height. Emitted as its own op run (content, then border).
        LastBoxRect = new Rectangle(bx.boxX, bx.boxY, bx.boxX + Width, bx.boxY + Height);

        if (ColumnInfo is { ColumnCount: > 1 }
            && BuildColumnContent(bx.page, bx.boxX, bx.boxY + Height) is ({ } colOps, var colContentH))
        {
            if (Border is not null && Border.HasAnySide)
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
                bb.Rectangle(bx.boxX - half, bx.boxY + Height - borderH - half,
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

        bx.padLeft = Padding?.Left ?? 0;
        bx.padTop = Padding?.Top ?? 0;
        bx.padRight = Padding?.Right ?? 0;
        bx.padBottom = Padding?.Bottom ?? 0;

        bx.builder.SaveState();

        // Draw background
        if (BackgroundColor is not null)
        {
            bx.builder.SetFillColor(BackgroundColor);
            bx.builder.Rectangle(bx.boxX, bx.boxY, Width, Height);
            bx.builder.Fill();
        }

        // Draw border
        if (Border is not null && Border.HasAnySide)
            StrokeBorder(bx.builder, Border, bx.boxX, bx.boxY, Width, Height);

        bx.contentX = bx.boxX + bx.padLeft;
        bx.contentY = bx.boxY + Height - bx.padTop;
        bx.overflowLines = new List<(string Text, double FontSize, string FontRes, Color? Fill)>();

        bx.boxCentreCursor = bx.boxY + Height / 2;
        foreach (var paragraph in Paragraphs)
        {
            if (!BuildParagraph(bx, paragraph)) break;
        }

        bx.builder.RestoreState();

        // What the box could not show continues on a fresh page, where it re-seats at the
        // page's content top with the same width, height and chrome.
        if (bx.overflowLines.Count > 0 && Height > 0)
            EmitContinuationPages(bx.page, bx.overflowLines, bx.boxX, bx.padLeft, bx.padTop, bx.padBottom);

        return bx.builder.Build();
    }

    /// <summary>Draw the lines a fixed-height box overflowed onto as many continuation pages as
    /// they need, each recorded in <see cref="LastOverflowPages"/>. The box re-seats under the
    /// page's own top margin (the same continuation seat a breakable table inside a fixed-height
    /// box uses) and keeps its background and border, so the continuation reads as the same box.</summary>
    private void EmitContinuationPages(Page page,
        List<(string Text, double FontSize, string FontRes, Color? Fill)> lines,
        double boxX, double padLeft, double padTop, double padBottom)
    {
        var topMargin = page.PageInfo?.Margin?.Top ?? 0;
        if (topMargin <= 0) topMargin = Margin?.Top ?? 0;
        var contBoxY = page.Height - topMargin - Height;
        var idx = 0;
        var guard = 0;
        while (idx < lines.Count && guard++ < 512)
        {
            var b = new ContentStreamBuilder();
            b.SaveState();
            if (BackgroundColor is not null)
            {
                b.SetFillColor(BackgroundColor);
                b.Rectangle(boxX, contBoxY, Width, Height);
                b.Fill();
            }
            if (Border is not null && Border.HasAnySide)
                StrokeBorder(b, Border, boxX, contBoxY, Width, Height);
            var y = contBoxY + Height - padTop;
            var drewAny = false;
            while (idx < lines.Count)
            {
                var (text, fs, fontRes, fill) = lines[idx];
                y -= fs;
                if (y < contBoxY + padBottom && drewAny) break;
                if (y < contBoxY + padBottom && !drewAny) { idx++; continue; } // never loop on a line that cannot fit
                if (fill is { } c) b.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
                b.BeginText();
                b.SetFont(fontRes, fs);
                b.MoveTextPosition(boxX + padLeft, y);
                b.ShowText(text);
                b.EndText();
                drewAny = true;
                idx++;
            }
            b.RestoreState();
            LastOverflowPages.Add(b.Build());
            if (!drewAny) break;
        }
    }

    /// <summary>Positive per-em descent of a Standard-14 face (Helvetica: 0.207) —
    /// the lift from a line box's bottom to the baseline drawn in it.</summary>
    private static double DescentEm(string faceName)
    {
        var d = Aspose.Pdf.Text.Standard14Fonts.GetDescent(faceName);
        return d < 0 ? -d / 1000.0 : d / 1000.0;
    }

    /// <summary>Stroke a box border around (x, y, w, h). A full box (all four
    /// sides, whether named by Side or assigned one by one) is one rectangle
    /// outset by half the stroke width on every side, so the stroke sits just
    /// outside the filled area (measured 2026-08-23); partial
    /// sides are individual edge lines on the box edge.</summary>
    internal static void StrokeBorder(ContentStreamBuilder builder, BorderInfo border,
        double x, double y, double w, double h)
    {
        // A side assigned its own GraphInfo strokes in that GraphInfo's colour
        // and width; the BorderInfo's own colour/width cover the rest.
        var styled = border.RawTop ?? border.RawBottom ?? border.RawLeft ?? border.RawRight;
        var width = styled is { LineWidth: > 0 } ? styled.LineWidth : border.Width;
        builder.SetLineWidth(width);
        builder.SetStrokeColor(styled?.Color ?? border.Color);
        var side = border.EffectiveSides;
        if ((side & BorderSide.Box) == BorderSide.Box)
        {
            // The box border is emitted TOP-anchored with a negative
            // height (re x, yTop, w, -h), outset by half the stroke width —
            // same ink as a bottom-anchored rect, but operator-inspecting
            // consumers see the top edge in Re.Y (probed 2026-08-28: a
            // 100x100 absolute box strokes re 389.95 770.05 100.1 -100.1).
            var half = width / 2;
            builder.Rectangle(x - half, y + h + half, w + width, -(h + width));
            builder.Stroke();
            return;
        }
        if (side.HasFlag(BorderSide.Bottom))
            builder.MoveTo(x, y).LineTo(x + w, y).Stroke();
        if (side.HasFlag(BorderSide.Top))
            builder.MoveTo(x, y + h).LineTo(x + w, y + h).Stroke();
        if (side.HasFlag(BorderSide.Left))
            builder.MoveTo(x, y).LineTo(x, y + h).Stroke();
        if (side.HasFlag(BorderSide.Right))
            builder.MoveTo(x + w, y).LineTo(x + w, y + h).Stroke();
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
