using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Collection of annotations on a page.
/// </summary>
public sealed class AnnotationCollection : IReadOnlyList<Annotation>
{
    private readonly List<Annotation> _annotations;
    private readonly PdfDictionary _pageDict;
    private readonly PdfReader _reader;
    private readonly int _originalAnnotCount;

    /// <summary>Check if the underlying Annots array has changed since this collection was created.</summary>
    internal bool IsDirty(PdfDictionary pageDict, PdfReader reader)
    {
        var annotsObj = reader.Resolve(pageDict.Get("Annots"));
        var currentCount = annotsObj is PdfArray arr ? arr.Count : 0;
        return currentCount != _originalAnnotCount;
    }

    internal AnnotationCollection(PdfDictionary pageDict, PdfReader reader, Page? page = null)
    {
        _pageDict = pageDict;
        _reader = reader;
        _annotations = [];

        var annotsObj = reader.Resolve(pageDict.Get("Annots"));
        if (annotsObj is PdfArray arr)
        {
            _originalAnnotCount = arr.Count;
            foreach (var item in arr)
            {
                var annotDict = reader.ResolveDict(item);
                if (annotDict is not null)
                {
                    int objNum = item is PdfIndirectRef iref ? iref.ObjectNumber : -1;
                    var annot = Annotation.Create(annotDict, reader, objNum);
                    annot.SetPageDict(pageDict);
                    if (page is not null) annot.SetOwnerPage(page);
                    _annotations.Add(annot);
                }
            }
        }
    }

    public int Count => _annotations.Count;
    /// <summary>Get annotation by 1-based index.</summary>
    public Annotation this[int index] => _annotations[index - 1];

    /// <summary>Add a text (sticky note) annotation.</summary>
    public Annotation AddTextAnnotation(Rectangle rect, string contents,
        string? title = null, bool open = false)
    {
        var dict = AnnotationFactory.CreateTextAnnotation(rect, contents, title, open);
        return AddDict(dict);
    }

    /// <summary>Add a free text annotation (text displayed directly on the page).</summary>
    public Annotation AddFreeTextAnnotation(Rectangle rect, string contents,
        string? fontName = null, double fontSize = 12, double[]? color = null)
    {
        var dict = AnnotationFactory.CreateFreeTextAnnotation(rect, contents, fontName, fontSize, color);
        return AddDict(dict);
    }

    /// <summary>Add a link annotation with a URI action.</summary>
    public Annotation AddLinkAnnotation(Rectangle rect, string uri)
    {
        var dict = AnnotationFactory.CreateLinkAnnotation(rect, uri);
        return AddDict(dict);
    }

    /// <summary>Add a link annotation with a page destination (for TOC entries).</summary>
    public Annotation AddLinkAnnotation(Rectangle rect, int destinationPage, Rectangle destRect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        var rectArr = new PdfArray();
        rectArr.Add(new PdfReal(rect.LLX)); rectArr.Add(new PdfReal(rect.LLY));
        rectArr.Add(new PdfReal(rect.URX)); rectArr.Add(new PdfReal(rect.URY));
        dict.Set("Rect", rectArr);
        dict.Set("F", new PdfInteger(4));
        // Border: none (invisible link)
        var border = new PdfArray();
        border.Add(new PdfInteger(0)); border.Add(new PdfInteger(0)); border.Add(new PdfInteger(0));
        dict.Set("Border", border);
        // Destination: [pageObj /Fit]
        var dest = new PdfArray();
        dest.Add(new PdfInteger(destinationPage - 1)); // 0-based page index as placeholder
        dest.Add(new PdfName("Fit"));
        dict.Set("Dest", dest);
        return AddDict(dict);
    }

    /// <summary>Add a link annotation with a custom action.</summary>
    public Annotation AddLinkAnnotation(Rectangle rect, PdfAction action)
    {
        var dict = AnnotationFactory.CreateLinkAnnotationWithAction(rect, action);
        return AddDict(dict);
    }

    /// <summary>Add a highlight annotation.</summary>
    public Annotation AddHighlightAnnotation(Rectangle rect,
        double[]? quadPoints = null, double[]? color = null)
    {
        var dict = AnnotationFactory.CreateHighlightAnnotation(rect, quadPoints, color);
        return AddDict(dict);
    }

    /// <summary>Add an underline annotation.</summary>
    public Annotation AddUnderlineAnnotation(Rectangle rect,
        double[]? quadPoints = null, double[]? color = null)
    {
        var dict = AnnotationFactory.CreateUnderlineAnnotation(rect, quadPoints, color);
        return AddDict(dict);
    }

    /// <summary>Add a strikeout annotation.</summary>
    public Annotation AddStrikeOutAnnotation(Rectangle rect,
        double[]? quadPoints = null, double[]? color = null)
    {
        var dict = AnnotationFactory.CreateStrikeOutAnnotation(rect, quadPoints, color);
        return AddDict(dict);
    }

    /// <summary>Add a square (rectangle) annotation.</summary>
    public Annotation AddSquareAnnotation(Rectangle rect,
        double[]? borderColor = null, double[]? fillColor = null, double lineWidth = 1)
    {
        var dict = AnnotationFactory.CreateSquareAnnotation(rect, borderColor, fillColor, lineWidth);
        return AddDict(dict);
    }

    /// <summary>Add a circle (ellipse) annotation.</summary>
    public Annotation AddCircleAnnotation(Rectangle rect,
        double[]? borderColor = null, double[]? fillColor = null, double lineWidth = 1)
    {
        var dict = AnnotationFactory.CreateCircleAnnotation(rect, borderColor, fillColor, lineWidth);
        return AddDict(dict);
    }

    /// <summary>Add a line annotation.</summary>
    public Annotation AddLineAnnotation(Rectangle rect,
        double x1, double y1, double x2, double y2,
        double[]? color = null, double lineWidth = 1)
    {
        var dict = AnnotationFactory.CreateLineAnnotation(rect, x1, y1, x2, y2, color, lineWidth);
        return AddDict(dict);
    }

    /// <summary>Add an ink (freehand drawing) annotation.</summary>
    public Annotation AddInkAnnotation(Rectangle rect,
        double[][] inkPaths, double[]? color = null, double lineWidth = 1)
    {
        var dict = AnnotationFactory.CreateInkAnnotation(rect, inkPaths, color, lineWidth);
        return AddDict(dict);
    }

    /// <summary>Add a rubber stamp annotation.</summary>
    public Annotation AddStampAnnotation(Rectangle rect,
        string contents, string stampName = "Draft")
    {
        var dict = AnnotationFactory.CreateStampAnnotation(rect, contents, stampName);
        return AddDict(dict);
    }

    /// <summary>Add a caret annotation (insertion point).</summary>
    public Annotation AddCaretAnnotation(Rectangle rect, string? contents = null)
    {
        var dict = AnnotationFactory.CreateCaretAnnotation(rect, contents);
        return AddDict(dict);
    }

    /// <summary>Add a redaction annotation.</summary>
    public Annotation AddRedactAnnotation(Rectangle rect,
        double[]? color = null, string? overlayText = null)
    {
        var dict = AnnotationFactory.CreateRedactAnnotation(rect, color, overlayText);
        return AddDict(dict);
    }

    /// <summary>Add a file attachment annotation.</summary>
    public Annotation AddFileAttachmentAnnotation(Rectangle rect,
        string contents, string fileName, byte[] fileData)
    {
        var dict = AnnotationFactory.CreateFileAttachmentAnnotation(rect, contents, fileName, fileData);
        return AddDict(dict);
    }

    /// <summary>Add a squiggly underline annotation.</summary>
    public Annotation AddSquigglyAnnotation(Rectangle rect,
        double[]? quadPoints = null, double[]? color = null)
    {
        var dict = AnnotationFactory.CreateSquigglyAnnotation(rect, quadPoints, color);
        return AddDict(dict);
    }

    /// <summary>Add a polygon annotation.</summary>
    public Annotation AddPolygonAnnotation(Rectangle rect,
        double[] vertices, double[]? borderColor = null, double[]? fillColor = null, double lineWidth = 1)
    {
        var dict = AnnotationFactory.CreatePolygonAnnotation(rect, vertices, borderColor, fillColor, lineWidth);
        return AddDict(dict);
    }

    /// <summary>Add a polyline annotation.</summary>
    public Annotation AddPolyLineAnnotation(Rectangle rect,
        double[] vertices, double[]? color = null, double lineWidth = 1)
    {
        var dict = AnnotationFactory.CreatePolyLineAnnotation(rect, vertices, color, lineWidth);
        return AddDict(dict);
    }

    /// <summary>Add a popup annotation.</summary>
    public Annotation AddPopupAnnotation(Rectangle rect, bool open = false)
    {
        var dict = AnnotationFactory.CreatePopupAnnotation(rect, open);
        return AddDict(dict);
    }

    /// <summary>Add a watermark annotation.</summary>
    public Annotation AddWatermarkAnnotation(Rectangle rect, string? contents = null)
    {
        var dict = AnnotationFactory.CreateWatermarkAnnotation(rect, contents);
        return AddDict(dict);
    }

    /// <summary>Add a WatermarkAnnotation object.</summary>
    public Annotation Add(WatermarkAnnotation wa)
    {
        var dict = wa.Build();
        return AddDict(dict);
    }

    /// <summary>Add any annotation that was created programmatically.</summary>
    public void Add(Annotation annotation)
    {
        // Persist a FreeText's text alignment to /Q so it survives the annotation
        // being re-wrapped from its dict when appearances are generated on save.
        if (annotation is FreeTextAnnotation ft && ft.TextStyle is { } tstyle
            && tstyle.HorizontalAlignment != Aspose.Pdf.HorizontalAlignment.Left)
        {
            ft.Justification = tstyle.HorizontalAlignment == Aspose.Pdf.HorizontalAlignment.Right
                ? Justification.Right
                : Justification.Center;
        }
        AddDict(annotation.Dict);
    }

    /// <summary>Add an annotation. When <paramref name="considerRotation"/> is true and the
    /// page is rotated, the supplied /Rect is interpreted in the page's displayed (rotated)
    /// space and mapped back to the page's default coordinate space before insertion — so a
    /// caller that takes a rect from a TextFragment.Rectangle on a rotated page (e.g. a link
    /// over found text) gets an annotation at the visually-correct spot.
    /// Equivalent to pre-applying <c>page.RotationMatrix.Reverse().Transform(rect)</c>.</summary>
    public void Add(Annotation annotation, bool considerRotation)
    {
        if (considerRotation && annotation?.Rect is { } rect)
        {
            var m = PageRotationMatrix();
            if (!m.Equals(Matrix.Identity))
                annotation.Rect = m.Reverse().Transform(rect);
        }
        AddDict(annotation!.Dict);
    }

    /// <summary>Rotation matrix for the owning page (mirrors Page.RotationMatrix),
    /// computed from the inherited /Rotate and /MediaBox on the page dict.</summary>
    private Matrix PageRotationMatrix()
    {
        int rotation = (int)ReadInheritedNumber("Rotate") % 360;
        if (rotation < 0) rotation += 360;
        var mb = ReadInheritedMediaBox();
        double w = mb?.Width ?? 0, h = mb?.Height ?? 0;
        return rotation switch
        {
            90 => new Matrix(0, 1, -1, 0, h, 0),
            180 => new Matrix(-1, 0, 0, -1, w, h),
            270 => new Matrix(0, -1, 1, 0, 0, w),
            _ => Matrix.Identity,
        };
    }

    private double ReadInheritedNumber(string key)
    {
        var d = _pageDict;
        for (int i = 0; i < 32 && d is not null; i++)
        {
            var v = _reader.Resolve(d.Get(key));
            if (v is PdfInteger pi) return pi.Value;
            if (v is PdfReal pr) return pr.Value;
            d = _reader.ResolveDict(d.Get("Parent"));
        }
        return 0;
    }

    private Rectangle? ReadInheritedMediaBox()
    {
        var d = _pageDict;
        for (int i = 0; i < 32 && d is not null; i++)
        {
            if (_reader.Resolve(d.Get("MediaBox")) is PdfArray a && a.Count >= 4)
                return Rectangle.FromPdfArray(a, _reader);
            d = _reader.ResolveDict(d.Get("Parent"));
        }
        return null;
    }

    /// <summary>Remove every annotation from the collection.</summary>
    public void Clear()
    {
        _annotations.Clear();
        _pageDict.Remove("Annots");
    }

    /// <summary>True when <paramref name="annotation"/> is currently in
    /// the collection (matched by reference, then by backing dict).</summary>
    public bool Contains(Annotation annotation)
    {
        if (annotation is null) return false;
        foreach (var a in _annotations)
            if (ReferenceEquals(a, annotation) || ReferenceEquals(a.Dict, annotation.Dict))
                return true;
        return false;
    }

    /// <summary>Copy annotations into <paramref name="array"/> starting at
    /// <paramref name="index"/>.</summary>
    public void CopyTo(Annotation[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        _annotations.CopyTo(array, index);
    }

    /// <summary>Remove every annotation from the collection (Aspose.Pdf
    /// alias for <see cref="Clear"/>; kept for API parity).</summary>
    public void Delete() => Clear();

    /// <summary>Remove the annotation at the given 1-based index.</summary>
    public void Delete(int index)
    {
        if (index < 1 || index > _annotations.Count) return;
        RemoveAt(index - 1);
    }

    /// <summary>Find an annotation whose /NM (annotation name) entry
    /// matches <paramref name="name"/>.</summary>
    public Annotation? FindByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var a in _annotations)
        {
            var nm = a.Dict.Get("NM");
            if (nm is Core.PdfString s && string.Equals(s.ToText(), name, StringComparison.Ordinal))
                return a;
        }
        return null;
    }

    /// <summary>Remove the first match of <paramref name="annotation"/>;
    /// returns true when an annotation was removed.</summary>
    public bool Remove(Annotation annotation)
    {
        if (annotation is null) return false;
        for (var i = 0; i < _annotations.Count; i++)
        {
            if (ReferenceEquals(_annotations[i], annotation)
                || ReferenceEquals(_annotations[i].Dict, annotation.Dict))
            {
                RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Always false: callers may add and remove annotations.</summary>
    public bool IsReadOnly => false;

    /// <summary>Always false: callers serialise their own access.</summary>
    public bool IsSynchronized => false;

    /// <summary>Sentinel object for ICollection.SyncRoot-style locking.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>Remove the given annotation from the collection. Matches by the
    /// underlying PDF dictionary so callers iterating an earlier snapshot can
    /// still target an annotation in a later, refreshed collection.</summary>
    public void Delete(Annotation annotation)
    {
        var targetDict = annotation.Dict;
        for (var i = 0; i < _annotations.Count; i++)
        {
            if (ReferenceEquals(_annotations[i].Dict, targetDict))
            {
                RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>Remove an annotation at the given index.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _annotations.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _annotations.RemoveAt(index);

        var annotsArr = new PdfArray();
        foreach (var a in _annotations)
            annotsArr.Add(a.Dict);

        if (annotsArr.Count > 0)
            _pageDict.Set("Annots", annotsArr);
        else
            _pageDict.Remove("Annots");
    }

    /// <summary>Append an already-imported raw annotation dictionary (internal:
    /// PdfPageStamp carries the stamped page's annotations onto the target).</summary>
    internal Annotation AddImportedDict(PdfDictionary dict) => AddDict(dict);

    private Annotation AddDict(PdfDictionary dict)
    {
        var annot = Annotation.Create(dict, _reader);
        _annotations.Add(annot);

        var annotsObj = _reader.Resolve(_pageDict.Get("Annots"));
        PdfArray annotsArr;
        if (annotsObj is PdfArray existing)
        {
            annotsArr = existing;
        }
        else
        {
            annotsArr = new PdfArray();
            _pageDict.Set("Annots", annotsArr);
        }
        annotsArr.Add(dict);

        return annot;
    }

    /// <summary>
    /// Iterate annotations on a snapshot of the underlying list so callers can
    /// remove annotations (via <see cref="Delete"/> / <see cref="RemoveAt"/>)
    /// from inside a foreach without tripping the live-collection guard.
    /// </summary>
    public IEnumerator<Annotation> GetEnumerator() => _annotations.ToList().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walk every annotation in the collection and dispatch it to
    /// <paramref name="visitor"/>. Each annotation's
    /// <see cref="Annotation.Accept(AnnotationSelector)"/> override lands on
    /// the right typed Visit overload, populating
    /// <see cref="AnnotationSelector.Selected"/>.</summary>
    public void Accept(AnnotationSelector visitor)
    {
        if (visitor is null) return;
        foreach (var annotation in _annotations)
            annotation.Accept(visitor);
    }
}
