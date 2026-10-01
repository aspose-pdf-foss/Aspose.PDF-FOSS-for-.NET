using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class WatermarkArtifact : Artifact
{
    /// <summary>The stages of the text watermark build: the run measure, the line placement and the form composition.</summary>
    private void ComposeTextWatermarkForm(TextWatermarkBuildState tw)
    {

        tw.sb = new System.Text.StringBuilder("q\n");
        // Compose the /Rotate compensation and the watermark rotation into ONE cm —
        // a single composed matrix is emitted ahead of the form.
        if (tw.rotationCm is not null && PageRotationMatrix(tw.page) is { } pm)
        {
            var rad2 = Rotation * Math.PI / 180;
            var rc = Math.Cos(rad2); var rs = Math.Sin(rad2);
            var cx2 = tw.pageWidth / 2; var cy2 = tw.pageHeight / 2;
            double re = tw.x * rc - tw.y * rs + cx2 * (1 - rc) + cy2 * rs;
            double rf = tw.x * rs + tw.y * rc + cy2 * (1 - rc) - cx2 * rs;
            // [rotation] × [pageRot] (row-vector composition).
            double na = rc * pm[0] + rs * pm[2];
            double nb = rc * pm[1] + rs * pm[3];
            double nc = -rs * pm[0] + rc * pm[2];
            double nd = -rs * pm[1] + rc * pm[3];
            double ne = re * pm[0] + rf * pm[2] + pm[4];
            double nf = re * pm[1] + rf * pm[3] + pm[5];
            tw.sb.Append($"{WatermarkNum(tw, na)} {WatermarkNum(tw, nb)} {WatermarkNum(tw, nc)} {WatermarkNum(tw, nd)} {WatermarkNum(tw, ne)} {WatermarkNum(tw, nf)} cm\n");
        }
        else
        {
            if (PageRotationCm(tw.page) is { } rot) tw.sb.Append(rot).Append('\n');
            if (tw.rotationCm is not null) tw.sb.Append(tw.rotationCm).Append('\n');
        }
        if (Opacity < 1.0)
        {
            var gs = new ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity };
            tw.sb.Append($"/{tw.page.AddExtGState(gs)} gs\n");
        }
    }

    /// <summary></summary>
    private void PlaceTextWatermarkLines(TextWatermarkBuildState tw)
    {
        if (Math.Abs(Rotation) > 0.1)
        {
            var rad = Rotation * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var cx = tw.pageWidth / 2;
            var cy = tw.pageHeight / 2;
            tw.rotationCm = $"{WatermarkNum(tw, cos)} {WatermarkNum(tw, sin)} {WatermarkNum(tw, -sin)} {WatermarkNum(tw, cos)} " +
                $"{WatermarkNum(tw, tw.x * cos - tw.y * sin + cx * (1 - cos) + cy * sin)} " +
                $"{WatermarkNum(tw, tw.x * sin + tw.y * cos + cy * (1 - cos) - cx * sin)} cm";
            tw.inner.Append($"0 {WatermarkNum(tw, (tw.lines.Length - 1) * tw.pitch)} Td\n");
        }
        else
        {
            tw.inner.Append($"{WatermarkNum(tw, tw.x)} {WatermarkNum(tw, tw.y + (tw.lines.Length - 1) * tw.pitch)} Td\n");
        }
        for (var i = 0; i < tw.lines.Length; i++)
        {
            if (i > 0) tw.inner.Append($"0 {WatermarkNum(tw, -tw.pitch)} Td\n");
            tw.inner.Append($"({EscapeTextLiteral(tw.lines[i])}) Tj\n");
        }
    }
    private static string WatermarkNum(TextWatermarkBuildState tw, double v) => v.ToString("0.####", tw.ci);
}
