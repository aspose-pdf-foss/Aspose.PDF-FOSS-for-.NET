using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class Annotation
{
    /// <summary>The stages of the annotation flatten: stamping the appearance stream onto the page.</summary>
    private void FlattenAppearance(AnnotationFlattenState af)
    {
        var rectArr = _reader.Resolve(_dict.Get("Rect")) as PdfArray;
        if (rectArr is not null && rectArr.Count >= 4)
        {
            var rect = Rectangle.FromPdfArray(rectArr);

            // Per PDF 32000 §12.5.5 the appearance is placed by mapping the
            // *transformed* appearance box (BBox corners run through the form's
            // /Matrix, then their upright bounding box) onto the annotation /Rect —
            // NOT the raw BBox. Ignoring /Matrix mis-places any appearance whose
            // Matrix is not the identity (e.g. a Line/callout leader whose Matrix
            // translates its BBox to the origin), drawing it at the wrong spot.
            var bboxArr = _reader.Resolve(af.appearanceStream!.Dict!.Get("BBox")) as PdfArray;
            double bboxX = 0, bboxY = 0, bboxW = rect.Width, bboxH = rect.Height;
            if (bboxArr is { Count: >= 4 })
            {
                var bbox = Rectangle.FromPdfArray(bboxArr);
                var mtx = _reader.Resolve(af.appearanceStream.Dict.Get("Matrix")) as PdfArray;
                double ma = 1, mb = 0, mc = 0, md = 1, me = 0, mf = 0;
                if (mtx is { Count: >= 6 })
                {
                    ma = PdfArrayHelper.GetDouble(mtx, 0); mb = PdfArrayHelper.GetDouble(mtx, 1);
                    mc = PdfArrayHelper.GetDouble(mtx, 2); md = PdfArrayHelper.GetDouble(mtx, 3);
                    me = PdfArrayHelper.GetDouble(mtx, 4); mf = PdfArrayHelper.GetDouble(mtx, 5);
                }
                // Transform the four BBox corners and take their bounding box.
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var (cx, cy) in new[] { (bbox.LLX, bbox.LLY), (bbox.URX, bbox.LLY), (bbox.URX, bbox.URY), (bbox.LLX, bbox.URY) })
                {
                    double px = ma * cx + mc * cy + me;
                    double py = mb * cx + md * cy + mf;
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                }
                bboxX = minX; bboxY = minY;
                bboxW = maxX - minX; bboxH = maxY - minY;
            }

            var sx = bboxW > 0 ? rect.Width / bboxW : 1.0;
            var sy = bboxH > 0 ? rect.Height / bboxH : 1.0;
            var tx = rect.LLX - bboxX * sx;
            var ty = rect.LLY - bboxY * sy;

            // When the appearance is itself a Form XObject (the typical case for
            // Ink, Stamp, FreeText, ...), preserve it as a named XForm in the
            // page's /Resources/XObject and reference it via /FRMn Do. This
            // matches the widget-flatten path in Forms.Form and keeps the form's
            // own BBox/Matrix/Resources contracts intact — visual output is
            // unchanged but tests can still inspect the appearance stream's
            // operators via Resources.Forms[name].
            var isFormXobject = af.appearanceStream.Dict.GetName("Subtype") == "Form";
            // Flatten removes the annotation, and with it the /CA the renderer
            // would have applied — bake the opacity into the stamped content via
            // a page-level ExtGState instead.
            var gsOp = Opacity < 1.0
                ? $"/{RegisterPageOpacityGState(af.pageDict!, Opacity)} gs "
                : "";
            using var appendContent = new MemoryStream();
            var writer = Compat.LeaveOpenWriter(appendContent, System.Text.Encoding.ASCII);
            if (isFormXobject)
            {
                var xformName = Forms.Form.RegisterAppearanceAsXForm(af.pageDict!, af.appearanceStream, _reader);
                writer.Write($"q {gsOp}{Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm /{xformName} Do Q\n");
                writer.Flush();
            }
            else
            {
                // Fallback for non-Form appearance streams — inline the bytes and
                // merge the annotation's /Resources into the page's /Resources so
                // the operators have access to their fonts/xobjects.
                var streamData = _reader.DecodeStream(af.appearanceStream);
                writer.Write($"q {gsOp}{Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm\n");
                writer.Flush();
                appendContent.Write(streamData);
                writer.Write("\nQ\n");
                writer.Flush();
                Forms.Form.MergeAnnotResources(af.pageDict!, af.appearanceStream.Dict, _reader);
            }

            // /Contents may be a single stream or an array of streams — decode
            // both forms so the underlying page content survives the rewrite.
            byte[] existingData = Aspose.Pdf.PdfPageStamp.GetPageContent(af.pageDict!, _reader);

            var contentArr = appendContent.ToArray();
            var combined = new byte[existingData.Length + 1 + contentArr.Length];
            existingData.CopyTo(combined, 0);
            if (existingData.Length > 0)
                combined[existingData.Length] = (byte)'\n';
            contentArr.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));

            af.pageDict!.Set("Contents", new PdfStream(new PdfDictionary(), combined));
        }
    }
}
