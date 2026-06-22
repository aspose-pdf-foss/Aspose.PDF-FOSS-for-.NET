using Aspose.Pdf.Content;

namespace Aspose.Pdf.Stamps;

/// <summary>
/// A watermark stamp that draws diagonal text across the page.
/// Always drawn behind page content with transparency.
/// </summary>
public sealed class WatermarkStamp : Stamp
{
    /// <summary>The watermark text.</summary>
    public string Text { get; set; }

    /// <summary>Font name.</summary>
    public string FontName { get; set; } = "Helvetica";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 48;

    /// <summary>Text color as RGB (0-1 range). Default: light gray.</summary>
    public (double R, double G, double B) Color { get; set; } = (0.8, 0.8, 0.8);

    /// <summary>Rotation angle in degrees. Default: 45 (diagonal).</summary>
    public new double Rotate { get; set; } = 45;

    public WatermarkStamp(string text)
    {
        Text = text;
        IsBackground = true;
        Opacity = 0.3;
    }

    internal override byte[] BuildContentStream(Page page) => BuildContentStream(page, "F1");

    internal override byte[] BuildContentStream(Page page, string fontResourceName)
    {
        var pageWidth = page.Width;
        var pageHeight = page.Height;

        // Center on page
        var textWidth = Text.Length * FontSize * 0.5;
        var cx = pageWidth / 2;
        var cy = pageHeight / 2;

        var radians = Rotate * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // Offset so text is centered
        var tx = cx - (textWidth / 2 * cos);
        var ty = cy - (textWidth / 2 * sin);

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        // Apply opacity via ExtGState if not fully opaque
        if (Opacity < 1.0)
        {
            var gs = new Content.ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = page.AddExtGState(gs);
            builder.SetExtGState(gsName);
        }

        builder.SetFillColor(Color.R, Color.G, Color.B)
            .BeginText()
            .SetFont(fontResourceName, FontSize)
            .SetTextRenderingMode(0) // fill
            .SetTextMatrix(cos, sin, -sin, cos, tx, ty)
            .ShowText(Text)
            .EndText()
            .RestoreState();

        return builder.Build();
    }
}
