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
        var af = new AnnotationFlattenState();
        af.pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (af.pageDict is null) return;

        af.subtype = _dict.GetName("Subtype");

        // Stamp the annotation appearance onto the page content (if it has one).
        // Shape/markup annotations are often stored without an /AP; synthesise one
        // from the geometry so the figure is baked in instead of vanishing.
        if (ResolveAppearanceStream() is null && CanSynthesiseAppearance(this))
            UpdateAppearances();
        af.appearanceStream = ResolveAppearanceStream();
        if (af.appearanceStream is not null)
        {
            FlattenAppearance(af);
        }

        // Always remove the annotation from the page's /Annots array
        RemoveFromAnnotsArray(af.pageDict);
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
