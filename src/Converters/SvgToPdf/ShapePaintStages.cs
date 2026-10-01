using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
    /// <summary>The stages of the SVG shape render: the fill and the stroke.</summary>
    private static void StrokeShape(ShapeRenderState sr)
    {
        var url = ParseUrlRef(sr.strokeVal);
        if (url is not null && sr.ctx.Defs.TryGetValue(url, out var gradEl) && IsGradient(gradEl))
        {
            var (r, g, b) = AverageGradientColor(gradEl, sr.ctx);
            sr.sb.Append($"{F(r)} {F(g)} {F(b)} RG\n");
        }
        else if (url is null)
        {
            var (r, g, b) = ParseColor(sr.strokeVal);
            sr.sb.Append($"{F(r)} {F(g)} {F(b)} RG\n");
        }
        EmitStrokeState(sr.style, sr.sb, sr.ctx);
    }

    /// <summary>The stages of the SVG shape render: the fill and the stroke.</summary>
    private static void FillShape(ShapeRenderState sr, double[] bbox)
    {
        var url = ParseUrlRef(sr.fillVal);
        if (sr.tilingName is not null)
        {
            sr.sb.Append($"/Pattern cs /{sr.tilingName} scn\n");
            sr.fillIsPattern = true;
        }
        else if (url is not null && sr.ctx.Defs.TryGetValue(url, out var gradEl) && IsGradient(gradEl))
        {
            var patName = RegisterGradientPattern(gradEl, sr.ctx, sr.newCtm, bbox);
            if (patName is not null)
            {
                sr.sb.Append($"/Pattern cs /{patName} scn\n");
                sr.fillIsPattern = true;
            }
            else
            {
                var (r, g, b) = AverageGradientColor(gradEl, sr.ctx);
                sr.sb.Append($"{F(r)} {F(g)} {F(b)} rg\n");
            }
        }
        else if (url is not null)
        {
            // Unresolvable paint server: paint black (matches historic behaviour that
            // kept white artwork on url() backgrounds visible).
            sr.sb.Append("0 0 0 rg\n");
        }
        else
        {
            var (r, g, b) = ParseColor(sr.fillVal);
            sr.sb.Append($"{F(r)} {F(g)} {F(b)} rg\n");
        }
    }
}
