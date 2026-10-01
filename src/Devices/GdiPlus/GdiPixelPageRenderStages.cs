using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    /// <summary>The stages of the GDI+ pixel-size page render: the rotation fit and the content render.</summary>
    private void RenderGdiPageContent(GdiPixelPageRenderState gp, Bitmap bitmap, Graphics g)
    {
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PagePom;
            g.CompositingQuality = PrintedPageImage ? CompositingQuality.AssumeLinear : CompositingQuality.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            _g = g;
            _bitmap = bitmap;

            var resources = SoftwarePageRenderer.ResolveInheritedPageResources(gp.page.Dict, _reader);
            _scope = BuildScope(resources);

            var contentBytes = SoftwarePageRenderer.GetPageContent(gp.page.Dict, _reader);
            _patternBaseCtm = gp.initialPageCtm;
            RenderContentStream(contentBytes, gp.initialPageCtm, null);
            _patternBaseCtm = null;

            // Annotations paint on top of page content (PDF 32000 §12.5).
            _g.ResetClip();
            _annotBaseCtm = gp.initialPageCtm;
            SafeDraw(() => DrawAnnotations(gp.page.Dict));
            _annotBaseCtm = null;
        }
    }

    /// <summary></summary>
    private void FitGdiPageRotation(GdiPixelPageRenderState gp)
    {
        if (gp.rot is 90 or 180 or 270)
        {
            // The rotation swings the CROP rectangle (the visible region), not the
            // media box — its dimensions AND lower-left offset anchor the swing
            // (mirrors SoftwarePageRenderer; a media-box anchor shifted a
            // 270°-rotated cropped page by the media/crop height difference).
            var w = gp.crop.Width;
            var h = gp.crop.Height;
            gp.effectiveMb = gp.rot == 180
                ? new Rectangle(0, 0, w, h)
                : new Rectangle(0, 0, h, w);
            gp.initialPageCtm = gp.rot switch
            {
                90 => new[] { 0.0, -1.0, 1.0, 0.0, -gp.crop.LLY, w + gp.crop.LLX },
                180 => new[] { -1.0, 0.0, 0.0, -1.0, w + gp.crop.LLX, h + gp.crop.LLY },
                270 => new[] { 0.0, 1.0, -1.0, 0.0, h + gp.crop.LLY, -gp.crop.LLX },
                _ => null,
            };
        }
        else
        {
            // Unrotated: the device box is the crop rectangle, so its lower-left maps
            // to the bottom-left pixel and cropped content is positioned correctly.
            gp.effectiveMb = gp.crop;
        }
    }
}
