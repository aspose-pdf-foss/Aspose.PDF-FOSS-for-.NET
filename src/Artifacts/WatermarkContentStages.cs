using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class WatermarkArtifact : Artifact
{
    /// <summary>The stages of the watermark content stream: placement, paint, and the text show.</summary>
    private void ShowWatermarkText(WatermarkContentState wc)
    {
        if (Math.Abs(Rotation) > 0.1)
        {
            var rad = Rotation * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var cx = wc.pageWidth / 2;
            var cy = wc.pageHeight / 2;

            wc.builder.BeginText();
            wc.builder.SetFont(wc.fontResourceName, wc.fontSize);
            wc.builder.SetTextMatrix(cos, sin, -sin, cos,
                wc.x * cos - wc.y * sin + cx * (1 - cos) + cy * sin,
                wc.x * sin + wc.y * cos + cy * (1 - cos) - cx * sin);
            wc.builder.ShowText(wc.renderText);
            wc.builder.EndText();
        }
        else
        {
            wc.builder.BeginText();
            wc.builder.SetFont(wc.fontResourceName, wc.fontSize);
            wc.builder.MoveTextPosition(wc.x, wc.y);
            wc.builder.ShowText(wc.renderText);
            wc.builder.EndText();
        }
    }

    /// <summary>The stages of the watermark content stream: placement, paint, and the text show.</summary>
    private void ApplyWatermarkPaint(WatermarkContentState wc)
    {
        // Apply opacity
        if (Opacity < 1.0)
        {
            var gs = new ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = wc.page.AddExtGState(gs);
            wc.builder.SetExtGState(gsName);
        }

        // Set text color
        if (TextState?.ForegroundColor is { } fg)
            wc.builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
        else
            wc.builder.SetFillColor(0, 0, 0);
    }

    /// <summary>The stages of the watermark content stream: placement, paint, and the text show.</summary>
    private void PlaceWatermark(WatermarkContentState wc)
    {
        if (Position is { } pos)
        {
            wc.x = pos.X;
            wc.y = pos.Y;
        }
        else
        {
            switch (ArtifactHorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    wc.x = LeftMargin > 0 ? LeftMargin : 36; break;
                case HorizontalAlignment.Right:
                    wc.x = wc.pageWidth - wc.textWidth - (RightMargin > 0 ? RightMargin : 36); break;
                default: // Center / None
                    wc.x = (wc.pageWidth - wc.textWidth) / 2; break;
            }
            switch (ArtifactVerticalAlignment)
            {
                case VerticalAlignment.Top:
                    wc.y = wc.pageHeight - wc.fontSize - (TopMargin > 0 ? TopMargin : 36); break;
                case VerticalAlignment.Bottom:
                    wc.y = BottomMargin > 0 ? BottomMargin : 36; break;
                default: // Center / None
                    wc.y = (wc.pageHeight - wc.textHeight) / 2; break;
            }
        }
    }
}
