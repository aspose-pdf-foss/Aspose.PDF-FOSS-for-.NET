using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class Annotation
{
    /// <summary>
    /// Flatten this annotation — render its visual appearance into the page content
    /// and remove it from the page's annotations array.
    /// Requires the annotation's /P (page) entry to be set, which is standard for most PDFs.
    /// </summary>
    public void Flatten()
    {
        // Get the page this annotation belongs to
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (pageDict is null) return;

        var subtype = _dict.GetName("Subtype");

        // Stamp the annotation appearance onto the page content (if it has one).
        // Shape/markup annotations are often stored without an /AP; synthesise one
        // from the geometry so the figure is baked in instead of vanishing.
        if (ResolveAppearanceStream() is null && CanSynthesiseAppearance(this))
            UpdateAppearances();
        var appearanceStream = ResolveAppearanceStream();
        if (appearanceStream is not null)
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
                var bboxArr = _reader.Resolve(appearanceStream.Dict.Get("BBox")) as PdfArray;
                double bboxX = 0, bboxY = 0, bboxW = rect.Width, bboxH = rect.Height;
                if (bboxArr is { Count: >= 4 })
                {
                    var bbox = Rectangle.FromPdfArray(bboxArr);
                    var mtx = _reader.Resolve(appearanceStream.Dict.Get("Matrix")) as PdfArray;
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
                var isFormXobject = appearanceStream.Dict.GetName("Subtype") == "Form";
                // Flatten removes the annotation, and with it the /CA the renderer
                // would have applied — bake the opacity into the stamped content via
                // a page-level ExtGState instead.
                var gsOp = Opacity < 1.0
                    ? $"/{RegisterPageOpacityGState(pageDict, Opacity)} gs "
                    : "";
                using var appendContent = new MemoryStream();
                var writer = new StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
                if (isFormXobject)
                {
                    var xformName = Forms.Form.RegisterAppearanceAsXForm(pageDict, appearanceStream, _reader);
                    writer.Write($"q {gsOp}{Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm /{xformName} Do Q\n");
                    writer.Flush();
                }
                else
                {
                    // Fallback for non-Form appearance streams — inline the bytes and
                    // merge the annotation's /Resources into the page's /Resources so
                    // the operators have access to their fonts/xobjects.
                    var streamData = _reader.DecodeStream(appearanceStream);
                    writer.Write($"q {gsOp}{Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm\n");
                    writer.Flush();
                    appendContent.Write(streamData);
                    writer.Write("\nQ\n");
                    writer.Flush();
                    Forms.Form.MergeAnnotResources(pageDict, appearanceStream.Dict, _reader);
                }

                // /Contents may be a single stream or an array of streams — decode
                // both forms so the underlying page content survives the rewrite.
                byte[] existingData = Aspose.Pdf.PdfPageStamp.GetPageContent(pageDict, _reader);

                var contentArr = appendContent.ToArray();
                var combined = new byte[existingData.Length + 1 + contentArr.Length];
                existingData.CopyTo(combined, 0);
                if (existingData.Length > 0)
                    combined[existingData.Length] = (byte)'\n';
                contentArr.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));

                pageDict.Set("Contents", new PdfStream(new PdfDictionary(), combined));
            }
        }

        // Always remove the annotation from the page's /Annots array
        RemoveFromAnnotsArray(pageDict);
    }

    /// <summary>Register a fill+stroke alpha ExtGState on the page's resources and
    /// return its name. Used when flattening bakes an annotation's /CA into content.</summary>
    private string RegisterPageOpacityGState(PdfDictionary pageDict, double opacity)
    {
        var resources = _reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var egs = _reader.ResolveDict(resources.Get("ExtGState"));
        if (egs is null)
        {
            egs = new PdfDictionary();
            resources.Set("ExtGState", egs);
        }
        var name = "GSf0";
        var counter = 0;
        while (egs.ContainsKey(name)) name = $"GSf{++counter}";
        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        gs.Set("CA", new PdfReal(opacity));
        gs.Set("ca", new PdfReal(opacity));
        egs.Set(name, gs);
        return name;
    }

    private void RemoveFromAnnotsArray(PdfDictionary pageDict)
    {
        var annotsObj = _reader.Resolve(pageDict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        var remaining = new PdfArray();
        foreach (var annotRef in annotsObj)
        {
            bool isThis = false;
            if (annotRef is PdfIndirectRef iref && _dictObjNum >= 0)
                isThis = iref.ObjectNumber == _dictObjNum;
            else
            {
                var annotDict = _reader.ResolveDict(annotRef);
                isThis = annotDict is not null && ReferenceEquals(annotDict, _dict);
            }
            if (isThis) continue;
            remaining.Add(annotRef);
        }
        if (remaining.Count > 0)
            pageDict.Set("Annots", remaining);
        else
            pageDict.Remove("Annots");
    }
}
