using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a watermark artifact that can be added to a PDF page.
/// Artifacts are marked content sequences that allow PDF processors to
/// distinguish page content from non-content elements like watermarks.
/// </summary>
public class WatermarkArtifact : Artifact
{
    /// <summary>Creates an instance of a Watermark artifact.</summary>
    public WatermarkArtifact() : base(ArtifactType.Pagination, ArtifactSubtype.Watermark)
    {
        ArtifactHorizontalAlignment = HorizontalAlignment.Center;
        ArtifactVerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Source bitmap for an image watermark, used only when emitting the
    /// artifact. The round-tripped image is surfaced via the inherited
    /// <see cref="Artifact.Image"/> (an <see cref="XImage"/> over the embedded XObject).</summary>
    internal System.Drawing.Image? SourceImage { get; set; }

    /// <summary>
    /// Set the text and text state for the watermark.
    /// </summary>
    public new void SetTextAndState(string text, TextState state)
    {
        Text = text;
        TextState = state;
    }

    /// <summary>
    /// Set watermark text from a FormattedText object (Facades API-style helper).
    /// </summary>
    public new void SetText(Aspose.Pdf.Facades.FormattedText formattedText)
    {
        if (formattedText is null) return;
        Text = formattedText.Text;
        TextState = new TextState
        {
            FontName = formattedText.FontName,
            FontSize = (float)formattedText.FontSize,
            ForegroundColor = formattedText.ForegroundColor,
        };
    }

    /// <summary>
    /// Build the content stream for rendering this artifact on a page.
    /// </summary>
    internal byte[] BuildContentStream(Page page) => BuildContentStream(page, "F1");

    internal byte[] BuildContentStream(Page page, string fontResourceName)
    {
        // Apply page-number substitution: replace the configured token with the
        // 1-based page number. A null/empty token disables substitution.
        var renderText = Text;
        if (!string.IsNullOrEmpty(renderText) && !string.IsNullOrEmpty(PageNumberReplacementString))
            renderText = renderText.Replace(PageNumberReplacementString, page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (string.IsNullOrEmpty(renderText)) return [];

        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var fontSize = TextState?.FontSize ?? 12;

        // Estimate text dimensions
        var charWidth = fontSize * 0.5; // approximate
        var textWidth = renderText!.Length * charWidth;
        var textHeight = fontSize;

        // Calculate position based on alignment / explicit Position / margins
        double x, y;
        if (Position is { } pos)
        {
            x = pos.X;
            y = pos.Y;
        }
        else
        {
            switch (ArtifactHorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    x = LeftMargin > 0 ? LeftMargin : 36; break;
                case HorizontalAlignment.Right:
                    x = pageWidth - textWidth - (RightMargin > 0 ? RightMargin : 36); break;
                default: // Center / None
                    x = (pageWidth - textWidth) / 2; break;
            }
            switch (ArtifactVerticalAlignment)
            {
                case VerticalAlignment.Top:
                    y = pageHeight - fontSize - (TopMargin > 0 ? TopMargin : 36); break;
                case VerticalAlignment.Bottom:
                    y = BottomMargin > 0 ? BottomMargin : 36; break;
                default: // Center / None
                    y = (pageHeight - textHeight) / 2; break;
            }
        }

        // Compute bounding box for /BBox in the BDC properties dict.
        var bbox = ComputeBBox(x, y, textWidth, textHeight);
        Rectangle = bbox;

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        // Apply opacity
        if (Opacity < 1.0)
        {
            var gs = new ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = page.AddExtGState(gs);
            builder.SetExtGState(gsName);
        }

        // Set text color
        if (TextState?.ForegroundColor is { } fg)
            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);

        // Begin marked content for artifact — use BDC with properties so the
        // /Type, /Subtype, and /BBox round-trip through ArtifactCollection.
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var bboxStr = $"[{bbox.LLX.ToString("0.##", ci)} {bbox.LLY.ToString("0.##", ci)} {bbox.URX.ToString("0.##", ci)} {bbox.URY.ToString("0.##", ci)}]";
        var dict = $"<</Type /{Type} /Subtype /{Subtype} /BBox {bboxStr}>>";
        builder.BeginMarkedContentWithProps("Artifact", dict);

        if (Math.Abs(Rotation) > 0.1)
        {
            var rad = Rotation * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var cx = pageWidth / 2;
            var cy = pageHeight / 2;

            builder.BeginText();
            builder.SetFont(fontResourceName, fontSize);
            builder.SetTextMatrix(cos, sin, -sin, cos,
                x * cos - y * sin + cx * (1 - cos) + cy * sin,
                x * sin + y * cos + cy * (1 - cos) - cx * sin);
            builder.ShowText(renderText);
            builder.EndText();
        }
        else
        {
            builder.BeginText();
            builder.SetFont(fontResourceName, fontSize);
            builder.MoveTextPosition(x, y);
            builder.ShowText(renderText);
            builder.EndText();
        }

        builder.EndMarkedContent();
        builder.RestoreState();

        return builder.Build();
    }

    private static Rectangle ComputeBBox(double x, double y, double width, double height)
    {
        return new Rectangle(x, y, x + width, y + height);
    }

    /// <summary>Embed <see cref="Image"/> as an image XObject and place it inside an
    /// /Artifact marked-content block tagged /Subtype /Watermark, so it round-trips
    /// through <see cref="ArtifactCollection"/> as a watermark.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void AddImageWatermark(Page page)
    {
        byte[] png;
        using (var ms = new MemoryStream())
        {
            SourceImage!.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            png = ms.ToArray();
        }
        var stamp = ImageStamp.FromPngData(png);
        int iw = stamp.PixelWidth, ih = stamp.PixelHeight;
        var imgName = stamp.RegisterXObject(page);

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.##", ci);
        double x = (page.Width - iw) / 2, y = (page.Height - ih) / 2;
        var bbox = $"[{F(x)} {F(y)} {F(x + iw)} {F(y + ih)}]";
        Rectangle = new Rectangle(x, y, x + iw, y + ih);

        var sb = new System.Text.StringBuilder();
        sb.Append("q\n");
        if (Opacity < 1.0)
        {
            var gs = new ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity };
            sb.Append($"/{page.AddExtGState(gs)} gs\n");
        }
        sb.Append($"/Artifact <</Type /{Type} /Subtype /{Subtype} /BBox {bbox}>> BDC\n");
        sb.Append($"q {F(iw)} 0 0 {F(ih)} {F(x)} {F(y)} cm /{imgName} Do Q\n");
        sb.Append("EMC\nQ\n");
        var content = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        if (IsBackground) page.PrependContentStream(content);
        else page.AddContentStream(content);
    }

    /// <summary>
    /// Add this artifact to a page.
    /// </summary>
    public void AddToPage(Page page)
    {
        if (SourceImage is not null && OperatingSystem.IsWindows())
        {
            AddImageWatermark(page);
            return;
        }
        // Register Helvetica and use the returned resource name. RegisterFont may
        // return a name other than "F1" when the page's existing resources already
        // use that slot — in 41508.pdf the original page reserves /F1 for an
        // embedded subset that lacks our watermark glyphs (g/p/q/y), so emitting
        // SetFont("F1", ...) into our content stream renders the text invisibly.
        var fontName = Table.RegisterFont(page);

        var content = BuildContentStream(page, fontName);
        if (IsBackground)
            page.PrependContentStream(content);
        else
            page.AddContentStream(content);
    }
}
