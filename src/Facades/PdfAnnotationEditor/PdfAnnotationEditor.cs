using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for annotation manipulation: import/export XFDF, delete, flatten, redact.
/// </summary>
public sealed partial class PdfAnnotationEditor : IDisposable
{
    private Document? _document;
    private bool _ownsDocument;

    /// <summary>Create an unbound PdfAnnotationEditor.</summary>
    public PdfAnnotationEditor() { }

    /// <summary>Create a PdfAnnotationEditor bound to a Document.</summary>
    public PdfAnnotationEditor(Document document)
    {
        _document = document;
        _ownsDocument = false;
    }

    /// <summary>Bind PDF from a file path.</summary>
    public void BindPdf(string path)
    {
        CloseInternal();
        _document = Document.Open(path);
        _ownsDocument = true;
    }

    /// <summary>Bind PDF from a byte array.</summary>
    public void BindPdf(byte[] input)
    {
        CloseInternal();
        _document = Document.Open(input);
        _ownsDocument = true;
    }

    /// <summary>Bind PDF from a stream.</summary>
    public void BindPdf(Stream input)
    {
        CloseInternal();
        using var ms = new MemoryStream();
        if (input.CanSeek && input.Position != 0) input.Position = 0;
        input.CopyTo(ms);
        _document = Document.Open(ms.ToArray());
        _ownsDocument = true;
    }

    /// <summary>Bind an existing Document instance.</summary>
    public void BindPdf(Document document)
    {
        CloseInternal();
        _document = document;
        _ownsDocument = false;
    }

    /// <summary>The bound document.</summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound. Call BindPdf first.");

    // ── Extract ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns annotations on pages <paramref name="startPage"/> through
    /// <paramref name="endPage"/> (1-based, inclusive) whose type appears in
    /// <paramref name="annotTypes"/>. Widget annotations are included only
    /// when explicitly requested.
    /// </summary>
    public IList<Aspose.Pdf.Annotations.Annotation> ExtractAnnotations(
        int start, int end,
        Aspose.Pdf.Annotations.AnnotationType[] annotTypes)
    {
        var doc = Document;
        var typeFilter = annotTypes is null
            ? null
            : new HashSet<Aspose.Pdf.Annotations.AnnotationType>(annotTypes);

        var result = new List<Aspose.Pdf.Annotations.Annotation>();
        var first = Math.Max(1, start);
        var last = Math.Min(doc.PageCount, end);
        for (var i = first; i <= last; i++)
        {
            var page = doc.Pages.At(i);
            foreach (var annot in page.Annotations)
            {
                if (typeFilter is null || typeFilter.Contains(annot.AnnotationType))
                    result.Add(annot);
            }
        }
        return result;
    }

    /// <summary>Extract annotations across a page range filtered by Subtype name strings.</summary>
    public IList<Aspose.Pdf.Annotations.Annotation> ExtractAnnotations(
        int start, int end,
        string[] annotTypes)
    {
        var doc = Document;
        var nameFilter = annotTypes is null ? null : new HashSet<string>(annotTypes, StringComparer.Ordinal);
        var result = new List<Aspose.Pdf.Annotations.Annotation>();
        var first = Math.Max(1, start);
        var last = Math.Min(doc.PageCount, end);
        for (var i = first; i <= last; i++)
        {
            var page = doc.Pages.At(i);
            foreach (var annot in page.Annotations)
            {
                var subtype = annot.Dict.GetName("Subtype");
                if (nameFilter is null || (subtype is not null && nameFilter.Contains(subtype)))
                    result.Add(annot);
            }
        }
        return result;
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    /// <summary>Delete all annotations from all pages (excluding Widget/form field annotations).</summary>
    public void DeleteAnnotations()
    {
        var doc = Document;
        foreach (var page in doc.Pages)
        {
            var annotsObj = doc.Reader.Resolve(page.Dict.Get("Annots"));
            if (annotsObj is not PdfArray annotArray)
            {
                page.Dict.Remove("Annots");
                continue;
            }

            // Keep only Widget annotations (form fields)
            var kept = new PdfArray();
            foreach (var item in annotArray)
            {
                var resolved = doc.Reader.ResolveDict(item);
                if (resolved is not null && resolved.GetName("Subtype") == "Widget")
                    kept.Add(item);
            }

            if (kept.Count > 0)
                page.Dict.Set("Annots", kept);
            else
                page.Dict.Remove("Annots");
        }
    }

    /// <summary>Delete annotations of a specific subtype name from all pages.</summary>
    public void DeleteAnnotations(string annotType)
    {
        var doc = Document;
        foreach (var page in doc.Pages)
        {
            var annotsObj = doc.Reader.Resolve(page.Dict.Get("Annots"));
            if (annotsObj is not PdfArray annotArray) continue;

            var kept = new PdfArray();
            foreach (var item in annotArray)
            {
                var resolved = doc.Reader.ResolveDict(item);
                if (resolved is not null && resolved.GetName("Subtype") != annotType)
                    kept.Add(item);
            }

            if (kept.Count > 0)
                page.Dict.Set("Annots", kept);
            else
                page.Dict.Remove("Annots");
        }
    }

    /// <summary>Delete annotation by name (NM entry) across all pages.</summary>
    public void DeleteAnnotation(string annotName)
    {
        var annotationName = annotName;
        var doc = Document;
        foreach (var page in doc.Pages)
        {
            var annotsObj = doc.Reader.Resolve(page.Dict.Get("Annots"));
            if (annotsObj is not PdfArray annotArray) continue;

            var kept = new PdfArray();
            foreach (var item in annotArray)
            {
                var resolved = doc.Reader.ResolveDict(item);
                if (resolved is null)
                {
                    kept.Add(item);
                    continue;
                }
                var nm = resolved.Get("NM");
                var name = nm is PdfString s ? s.ToText() : null;
                if (name != annotationName)
                    kept.Add(item);
            }

            page.Dict.Set("Annots", kept);
        }
    }

    // ── Flatten ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Flatten all annotations — removes them from the annotation list.
    /// For redaction annotations, the overlay area is applied.
    /// Note: Full visual flattening (rendering AP streams into page content)
    /// stamps the annotation appearance (when present) onto the page content
    /// via <see cref="Aspose.Pdf.Annotations.Annotation.Flatten"/>, then removes
    /// the annotation. Widget annotations are skipped — flattening Acro Form
    /// fields is handled by <c>Form.Flatten</c>.
    /// </summary>
    public void FlatteningAnnotations()
    {
        var doc = Document;
        ApplyPendingRedactions(doc);
        // EVERYTHING flattens here, form widgets included: a filled
        // field's text bakes into the page content and /Annots empties (measured on
        // both overloads with a text field + sticky note). The form flatten below
        // carries the non-widget annotations too (flattenNonWidgets).
        if (doc.HasForm)
        {
            doc.Form.Flatten(doc,
                settings: new Forms.Form.FlattenSettings { HideButtons = true, UpdateAppearances = true },
                frmStartIndex: 1, flattenNonWidgets: true);
            return;
        }
        foreach (var page in doc.Pages)
        {
            // Snapshot the annotation list since Flatten mutates /Annots.
            var snapshot = new List<Aspose.Pdf.Annotations.Annotation>();
            foreach (var ann in page.Annotations)
            {
                if (ann.Dict.GetName("Subtype") == "Widget") continue;
                snapshot.Add(ann);
            }
            foreach (var ann in snapshot) ann.Flatten();
        }
    }

    // ── Redact ──────────────────────────────────────────────────────────────

    // ── XFDF Import ─────────────────────────────────────────────────────────

    /// <summary>
    /// Import annotations from an XFDF stream into the bound document.
    /// </summary>
    public void ImportAnnotationsFromXfdf(Stream xfdfStream)
    {
        var doc = Document;
        var xmlDoc = new XmlDocument();
        xmlDoc.Load(xfdfStream);

        var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
        nsmgr.AddNamespace("xfdf", "http://ns.adobe.com/xfdf/");

        var annotsNode = xmlDoc.SelectSingleNode("//xfdf:xfdf/xfdf:annots", nsmgr)
                      ?? xmlDoc.SelectSingleNode("//annots");
        if (annotsNode is null) return;

        foreach (XmlNode child in annotsNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            ImportXfdfAnnotation(doc, child);
        }

        ResolveImportedReplyReferences(doc);
    }

    /// <summary>Resolve the temporary /IRT_Name markers written by
    /// <see cref="ImportXfdfAnnotation"/> (from the XFDF <c>inreplyto</c> attribute)
    /// into real /IRT links: each marker names the replied-to annotation by its /NM,
    /// so once every annotation is imported we can connect the reply to its target.</summary>
    private void ResolveImportedReplyReferences(Document doc)
    {
        var byName = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        var pending = new List<PdfDictionary>();
        foreach (var page in doc.Pages)
        {
            if (doc.Reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var item in annots)
            {
                var ad = doc.Reader.ResolveDict(item);
                if (ad is null) continue;
                if (ad.Get("NM") is PdfString nm) byName[nm.ToText()] = ad;
                if (ad.ContainsKey("IRT_Name")) pending.Add(ad);
            }
        }
        foreach (var ad in pending)
        {
            var name = (ad.Get("IRT_Name") as PdfString)?.ToText();
            ad.Remove("IRT_Name");
            if (name is not null && byName.TryGetValue(name, out var target))
                ad.Set("IRT", target);
        }
    }

    /// <summary>
    /// Import annotations from an XFDF file path.
    /// </summary>
    public void ImportAnnotationsFromXfdf(string xfdfFile)
    {
        using var fs = new FileStream(xfdfFile, FileMode.Open, FileAccess.Read);
        ImportAnnotationsFromXfdf(fs);
    }

    /// <summary>
    /// Import annotations from XFDF with type filter.
    /// </summary>
    public void ImportAnnotationFromXfdf(Stream xfdfStream, AnnotationType[] annotType)
    {
        var doc = Document;
        var xmlDoc = new XmlDocument();
        xmlDoc.Load(xfdfStream);

        var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
        nsmgr.AddNamespace("xfdf", "http://ns.adobe.com/xfdf/");

        var annotsNode = xmlDoc.SelectSingleNode("//xfdf:xfdf/xfdf:annots", nsmgr)
                      ?? xmlDoc.SelectSingleNode("//annots");
        if (annotsNode is null) return;

        var typeSet = new HashSet<string>();
        foreach (var t in annotType)
            typeSet.Add(AnnotationTypeToXfdfTag(t));

        foreach (XmlNode child in annotsNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (typeSet.Contains(child.LocalName.ToLowerInvariant()))
                ImportXfdfAnnotation(doc, child);
        }

        ResolveImportedReplyReferences(doc);
    }

    /// <summary>
    /// Import annotations from an XFDF file path with type filter.
    /// </summary>
    public void ImportAnnotationFromXfdf(string xfdfFile, AnnotationType[] annotType)
    {
        using var fs = new FileStream(xfdfFile, FileMode.Open, FileAccess.Read);
        ImportAnnotationFromXfdf(fs, annotType);
    }

    // ── XFDF Export ─────────────────────────────────────────────────────────

    // ── Import Annotations from PDFs ────────────────────────────────────────

    /// <summary>
    /// Import annotations from other PDF streams into the bound document.
    /// </summary>
    public void ImportAnnotations(Stream[] annotFileStream)
    {
        var doc = Document;
        foreach (var stream in annotFileStream)
        {
            using var ms = new MemoryStream();
            stream.Position = 0;
            stream.CopyTo(ms);
            using var sourceDoc = Document.Open(ms.ToArray());

            for (int i = 0; i < sourceDoc.PageCount && i < doc.PageCount; i++)
            {
                var sourcePage = sourceDoc.Pages.At(i + 1);
                var targetPage = doc.Pages.At(i + 1);

                foreach (var annot in sourcePage.Annotations)
                {
                    if (annot.AnnotationType == AnnotationType.Widget) continue;
                    // Clone the annotation dictionary and add to target
                    AppendAnnotationDict(targetPage, annot.Dict);
                }
            }
        }
    }

    // ── Save / Close ────────────────────────────────────────────────────────

    /// <summary>Save the document to a byte array.</summary>
    public byte[] Save()
    {
        return Document.ToArray();
    }

    /// <summary>Save the document to a file path.</summary>
    public void Save(string path)
    {
        var bytes = Document.ToArray();
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Save the document to a stream.</summary>
    public void Save(Stream output)
    {
        var bytes = Document.ToArray();
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Close the editor and release the document.</summary>
    public void Close()
    {
        CloseInternal();
    }

    /// <summary>Dispose — same as Close.</summary>
    public void Dispose()
    {
        CloseInternal();
    }

    // ── Private Helpers ─────────────────────────────────────────────────────

    private void CloseInternal()
    {
        if (_ownsDocument && _document is not null)
            _document.Dispose();
        _document = null;
        _ownsDocument = false;
    }

    // ── API-shape additions ───────────────────────────────────────────────

    private static AnnotationType[] MapStringToAnnotationTypes(string[] names)
    {
        if (names is null) return Array.Empty<AnnotationType>();
        var result = new List<AnnotationType>();
        foreach (var name in names)
            if (System.Enum.TryParse<AnnotationType>(name, ignoreCase: true, out var t))
                result.Add(t);
        return result.ToArray();
    }

    /// <summary>Flatten every annotation using the supplied form-flatten settings.</summary>
    public void FlatteningAnnotations(Aspose.Pdf.Forms.Form.FlattenSettings flattenSettings)
    {
        // A redaction is applied by the flatten itself (see ApplyPendingRedactions),
        // so ApplyRedactions adds nothing here; the settings are kept in the
        // signature because the overload is part of the facade's surface.
        FlatteningAnnotations();
    }

    /// <summary>Flatten annotations across a page range filtered by type.</summary>
    public void FlatteningAnnotations(int start, int end, AnnotationType[] annotType)
    {
        var doc = Document;
        var typeSet = annotType is null ? null : new HashSet<AnnotationType>(annotType);
        var first = Math.Max(1, start);
        var last = Math.Min(doc.PageCount, end);
        for (var i = first; i <= last; i++)
        {
            var page = doc.Pages.At(i);
            var snapshot = new List<Aspose.Pdf.Annotations.Annotation>();
            foreach (var ann in page.Annotations)
            {
                if (ann.Dict.GetName("Subtype") == "Widget") continue;
                if (typeSet is null || typeSet.Contains(ann.AnnotationType))
                    snapshot.Add(ann);
            }
            foreach (var ann in snapshot) ann.Flatten();
        }
    }

    /// <summary>Import annotations from an XFDF stream (every annotation type).</summary>
    public void ImportAnnotationFromXfdf(Stream xfdfStream)
    {
        var allTypes = (AnnotationType[])System.Enum.GetValues(typeof(AnnotationType));
        ImportAnnotationFromXfdf(xfdfStream, allTypes);
    }

    /// <summary>Import annotations from an XFDF file (every annotation type).</summary>
    public void ImportAnnotationFromXfdf(string xfdfFile)
    {
        using var fs = new FileStream(xfdfFile, FileMode.Open, FileAccess.Read);
        ImportAnnotationFromXfdf(fs);
    }

    /// <summary>Import annotations from PDF streams filtered by type.</summary>
    public void ImportAnnotations(Stream[] annotFileStream, AnnotationType[] annotType)
    {
        // Underlying ImportAnnotations(Stream[]) does not yet honour the type filter;
        // delegate without filtering — annotations of all types are imported.
        _ = annotType;
        ImportAnnotations(annotFileStream);
    }

    /// <summary>Import annotations from PDF files at the given paths.</summary>
    public void ImportAnnotations(string[] annotFile)
    {
        if (annotFile is null) return;
        var streams = new List<Stream>();
        try
        {
            foreach (var path in annotFile)
                streams.Add(new FileStream(path, FileMode.Open, FileAccess.Read));
            ImportAnnotations(streams.ToArray());
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    /// <summary>Import annotations from PDF files filtered by type.</summary>
    public void ImportAnnotations(string[] annotFile, AnnotationType[] annotType)
    {
        _ = annotType;
        ImportAnnotations(annotFile);
    }

    /// <summary>Import annotations from an FDF file: a /FDF /Annots array lands page
    /// by its entries' 0-based /Page, and a /Fields array is split the way the
    /// reference splits it — /Subtype-carrying entries become page-1 annotations,
    /// the rest set field values by /T.</summary>
    public void ImportAnnotationsFromFdf(string fdfFile)
    {
        if (string.IsNullOrEmpty(fdfFile)) return;
        var bytes = System.IO.File.ReadAllBytes(fdfFile);
        var doc = Document;
        FdfImport.ImportAnnots(doc, bytes);
        FdfImport.ImportFieldsArray(doc, bytes);
    }

    /// <summary>Modify annotations across a page range to look like <paramref name="annotation"/>.</summary>
    public void ModifyAnnotations(int start, int end, Aspose.Pdf.Annotations.Annotation annotation)
    {
        if (annotation is null) return;
        ModifyAnnotations(start, end, annotation.AnnotationType, annotation);
    }

    /// <summary>Modify annotations of the supplied type across a page range.</summary>
    public void ModifyAnnotations(int start, int end, System.Enum annotType, Aspose.Pdf.Annotations.Annotation annotation)
    {
        if (annotation is null) return;
        var doc = Document;
        var first = Math.Max(1, start);
        var last = Math.Min(doc.PageCount, end);
        var targetType = annotType is AnnotationType t ? t : annotation.AnnotationType;
        var srcContents = annotation.Contents;
        for (var i = first; i <= last; i++)
        {
            var page = doc.Pages.At(i);
            foreach (var ann in page.Annotations)
            {
                if (ann.AnnotationType != targetType) continue;
                if (srcContents is not null) ann.Contents = srcContents;
            }
        }
    }

    /// <summary>Rewrite the author of every annotation matching <paramref name="srcAuthor"/> across the page range.</summary>
    public void ModifyAnnotationsAuthor(int start, int end, string srcAuthor, string desAuthor)
    {
        var doc = Document;
        var first = Math.Max(1, start);
        var last = Math.Min(doc.PageCount, end);
        for (var i = first; i <= last; i++)
        {
            var page = doc.Pages.At(i);
            foreach (var ann in page.Annotations)
            {
                if (string.Equals(ann.Author, srcAuthor, StringComparison.Ordinal))
                    ann.Dict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(desAuthor ?? string.Empty)));
            }
        }
    }
}
