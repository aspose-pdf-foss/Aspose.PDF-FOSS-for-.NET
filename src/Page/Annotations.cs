using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>
    /// Register a text fragment whose <c>Hyperlink</c> was set after absorption, so that a
    /// link annotation is emitted for it on save (mirrors the generator hyperlink path, which
    /// only runs for newly-laid-out paragraphs — not absorber-edited fragments).
    /// </summary>
    internal void RegisterHyperlinkFragment(Text.TextFragment fragment)
    {
        _hyperlinkFragments ??= new();
        _hyperlinkFragments.Add(fragment);
    }

    /// <summary>Register a PageInformationAnnotation so its file-name+date appearance is
    /// generated on save. Enumerating /Annots re-resolves the dict to a generic /PrinterMark
    /// annotation (the C# subtype is lost), so the original typed instance is tracked here.</summary>
    internal void RegisterPageInfoAnnotation(PageInformationAnnotation annot)
    {
        _pageInfoAnnotations ??= new();
        _pageInfoAnnotations.Add(annot);
    }

    /// <summary>Generate the appearance of every registered PageInformationAnnotation with the
    /// supplied output file name. Called during save once the file name is known.</summary>
    internal void FlushPageInfoAnnotations(string fileName, DateTime date)
    {
        if (_pageInfoAnnotations is null) return;
        foreach (var pia in _pageInfoAnnotations)
            pia.GenerateInfoAppearance(fileName, date);
    }

    /// <summary>
    /// Emit a Link annotation for every fragment whose hyperlink was set via the absorber/edit
    /// path. The fragment rectangle is in the page's displayed (rotation-applied) coordinate
    /// frame, so it is mapped back to unrotated page space for the annotation /Rect. Called
    /// during save before the content stream is flushed.
    /// </summary>
    internal void FlushHyperlinkAnnotations()
    {
        if (_hyperlinkFragments is null || _hyperlinkFragments.Count == 0) return;
        foreach (var frag in _hyperlinkFragments)
        {
            var hyperlink = frag.HyperlinkValue;
            if (hyperlink is null || frag.Rectangle is null) continue;
            var rect = MapDisplayedRectToUnrotated(frag.Rectangle, RotateDegrees, MediaBox);
            EmitHyperlinkAnnotation(rect, hyperlink);
        }
        _hyperlinkFragments.Clear();
    }

    internal void EmitHyperlinkAnnotation(Rectangle rect, Hyperlink hyperlink)
    {
        if (hyperlink is LocalHyperlink lh && lh.TargetPageNumber > 0)
            Annotations.AddLinkAnnotation(rect,
                new Aspose.Pdf.Annotations.GoToAction(
                    new Aspose.Pdf.Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
        else if (hyperlink is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
            Annotations.AddLinkAnnotation(rect, wh.Url);
        else if (hyperlink is FileHyperlink fh && !string.IsNullOrEmpty(fh.FileName))
            Annotations.AddLinkAnnotation(rect,
                new Aspose.Pdf.Annotations.LaunchAction(fh.FileName) { NewWindow = fh.NewWindow });
    }

    private void TransformAnnotationRects(double sx, double sy, double tx, double ty)
    {
        var annotsObj = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        foreach (var annotRef in annotsObj)
        {
            var annotDict = _reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            // Sticky-note (/Text) annotations render as a fixed-size icon anchored at
            // their rectangle, so a content resize leaves their rect in place rather
            // than scaling it with the content.
            if (annotDict.GetName("Subtype") == "Text") continue;

            // Transform /Rect
            var rectArr = _reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is { Count: >= 4 })
                TransformCoordArray(rectArr, sx, sy, tx, ty);

            // Transform /QuadPoints (flat array of x,y pairs)
            var qpArr = _reader.Resolve(annotDict.Get("QuadPoints")) as PdfArray;
            if (qpArr is not null)
                TransformCoordArray(qpArr, sx, sy, tx, ty);
        }
    }

    /// <summary>Transform every annotation's /Rect (renormalised), /QuadPoints and appearance
    /// /Matrix by the page-rotation affine so the annotations move and orient with the content.</summary>
    private void TransformAnnotationGeometry(double a, double b, double c, double d, double e, double f)
    {
        var annots = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annots is null) return;

        foreach (var annotRef in annots)
        {
            var annotDict = _reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            var rectArr = _reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is { Count: >= 4 })
            {
                var nr = TransformRect(
                    new Rectangle(GetNum(rectArr[0]), GetNum(rectArr[1]), GetNum(rectArr[2]), GetNum(rectArr[3])),
                    a, b, c, d, e, f);
                rectArr.ReplaceAt(0, new PdfReal(nr.LLX));
                rectArr.ReplaceAt(1, new PdfReal(nr.LLY));
                rectArr.ReplaceAt(2, new PdfReal(nr.URX));
                rectArr.ReplaceAt(3, new PdfReal(nr.URY));
            }

            var qpArr = _reader.Resolve(annotDict.Get("QuadPoints")) as PdfArray;
            if (qpArr is not null)
            {
                for (int i = 0; i + 1 < qpArr.Count; i += 2)
                {
                    double xv = GetNum(qpArr[i]), yv = GetNum(qpArr[i + 1]);
                    qpArr.ReplaceAt(i,     new PdfReal(a * xv + c * yv + e));
                    qpArr.ReplaceAt(i + 1, new PdfReal(b * xv + d * yv + f));
                }
            }

            // Pre-rotate the normal appearance stream(s) by the linear part of the affine so the
            // annotation's drawn content turns with the page (the viewer fits the appearance BBox
            // into the new /Rect, so only the rotation — not the translation — belongs here).
            RotateAppearanceMatrices(annotDict, a, b, c, d);
        }
    }
}
