using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    // Annotation flag bits (PDF 32000-1 §12.5.3): Hidden, and NoView, which keeps an
    // annotation off the screen while it still prints.
    private const int AnnotHiddenFlag = 0x02;
    private const int AnnotNoViewFlag = 0x20;

    /// <summary>
    /// Draw the page's annotations after its content, each through its normal appearance
    /// stream placed into its rectangle (PDF 32000-1 §12.5.5), the way a viewer shows the
    /// page. An annotation without an appearance stream is not synthesised here.
    /// </summary>
    private void RenderAnnotations(Page page, PdfReader reader, StringBuilder sb, GState pageGs,
        ISet<string> usedBlendModes, List<LinkRect> links)
    {
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null) continue;
            var flags = (int)annot.GetInt("F");
            if ((flags & AnnotHiddenFlag) != 0 || (flags & AnnotNoViewFlag) != 0) continue;
            var form = AppearanceStreams.Normal(annot, reader);
            if (form is null) continue;
            var toRect = AppearanceStreams.FitToRect(annot, form, reader);
            if (toRect is null) continue;

            var gs = pageGs.Clone();
            gs.Ctm = MulAffine(toRect, pageGs.Ctm);
            // The annotation's constant opacity covers its whole appearance, so it is a group
            // opacity around the stream's output, not a fill alpha the stream's own ExtGState
            // would replace.
            var opacity = reader.Resolve(annot.Get("CA")) is PdfInteger or PdfReal ? Num(reader.Resolve(annot.Get("CA"))!) : 1.0;
            var grouped = opacity < 1.0;
            if (grouped) sb.AppendLine($"<g style=\"opacity:{F(opacity)}\">");
            RenderForm(form, null, reader, sb, 1, gs, usedBlendModes, links);
            if (grouped) sb.AppendLine("</g>");
        }
    }
}
