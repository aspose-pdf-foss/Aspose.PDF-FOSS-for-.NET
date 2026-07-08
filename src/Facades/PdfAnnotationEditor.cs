using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for annotation manipulation: import/export XFDF, delete, flatten, redact.
/// </summary>
public sealed class PdfAnnotationEditor : IDisposable
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

    /// <summary>
    /// Redact an area on a page: adds a redaction annotation with the given fill color,
    /// then flattens the redaction (removes the annotation).
    /// </summary>
    /// <param name="pageIndex">1-based page number.</param>
    /// <param name="rect">The area to redact.</param>
    /// <param name="color">Fill color as RGB doubles [r, g, b] in 0.1 range.</param>
    public void RedactArea(int pageIndex, Rectangle rect, double[] color)
    {
        var doc = Document;
        if (pageIndex < 1 || pageIndex > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var page = doc.Pages.At(pageIndex);

        // Add a white content stream rectangle to cover the area
        var sb = new StringBuilder();
        sb.Append("q ");
        if (color is { Length: >= 3 })
            sb.Append($"{F(color[0])} {F(color[1])} {F(color[2])} rg ");
        else
            sb.Append("1 1 1 rg "); // white by default

        sb.Append($"{F(rect.LLX)} {F(rect.LLY)} {F(rect.Width)} {F(rect.Height)} re f Q");
        AppendContentStream(page, Encoding.Latin1.GetBytes(sb.ToString()));

        // Redaction removes form-field widgets covered by the area (they are no longer
        // visible/usable), pruning them from the page /Annots and the AcroForm /Fields.
        RemoveWidgetsInArea(doc, page, rect);
    }

    /// <summary>Remove every Widget annotation whose /Rect intersects <paramref name="area"/>
    /// from the page and, for those that are (or become) empty form fields, from the
    /// AcroForm /Fields tree — mirroring how Aspose.Pdf drops fields under a redaction.</summary>
    private static void RemoveWidgetsInArea(Document doc, Page page, Rectangle area)
    {
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;

        static double Num(PdfObject? o) => o switch
        {
            PdfReal r => r.Value, PdfInteger i => i.Value, _ => 0.0,
        };

        var removeWidgets = new HashSet<PdfDictionary>();
        foreach (var item in annots)
        {
            var ad = reader.ResolveDict(item);
            if (ad is null || ad.GetName("Subtype") != "Widget") continue;
            if (reader.Resolve(ad.Get("Rect")) is not PdfArray r || r.Count < 4) continue;
            double llx = Num(r[0]), lly = Num(r[1]), urx = Num(r[2]), ury = Num(r[3]);
            if (System.Math.Min(urx, area.URX) > System.Math.Max(llx, area.LLX)
                && System.Math.Min(ury, area.URY) > System.Math.Max(lly, area.LLY))
                removeWidgets.Add(ad);
        }
        if (removeWidgets.Count == 0) return;

        RebuildArrayExcluding(page.Dict, "Annots", annots, removeWidgets, reader);
        var pageNum = doc.FindObjectNumber(page.Dict);
        if (pageNum > 0) doc.MarkDirty(pageNum, page.Dict);

        var acro = reader.ResolveDict(reader.Catalog?.Get("AcroForm"));
        var fields = acro is null ? null : reader.Resolve(acro.Get("Fields")) as PdfArray;
        if (fields is null || acro is null) return;

        var removeFields = new HashSet<PdfDictionary>();
        foreach (var w in removeWidgets)
        {
            var parent = reader.ResolveDict(w.Get("Parent"));
            if (parent is null)
            {
                // Merged-leaf field: the widget dict is itself the /Fields entry.
                removeFields.Add(w);
                continue;
            }
            // Detach the widget from its field's /Kids; drop the field if it is left empty.
            if (reader.Resolve(parent.Get("Kids")) is PdfArray pkids)
            {
                RebuildArrayExcluding(parent, "Kids", pkids, removeWidgets, reader);
                if ((reader.Resolve(parent.Get("Kids")) as PdfArray)?.Count == 0)
                    removeFields.Add(parent);
                var pn = doc.FindObjectNumber(parent);
                if (pn > 0) doc.MarkDirty(pn, parent);
            }
        }
        if (removeFields.Count > 0)
        {
            RebuildArrayExcluding(acro, "Fields", fields, removeFields, reader);
            var acroNum = doc.FindObjectNumber(acro);
            if (acroNum > 0) doc.MarkDirty(acroNum, acro);
        }
    }

    private static void RebuildArrayExcluding(PdfDictionary owner, string key,
        PdfArray arr, HashSet<PdfDictionary> exclude, IO.PdfReader reader)
    {
        var kept = new PdfArray();
        foreach (var item in arr)
        {
            var d = reader.ResolveDict(item);
            if (d is not null && exclude.Contains(d)) continue;
            kept.Add(item);
        }
        owner.Set(key, kept);
    }

    /// <summary>
    /// Redact an area on a page with a System.Drawing.Color-compatible (R, G, B) color.
    /// </summary>
    public void RedactArea(int pageIndex, Rectangle rect, int r, int g, int b)
    {
        RedactArea(pageIndex, rect, [r / 255.0, g / 255.0, b / 255.0]);
    }

    /// <summary>
    /// Redact an area on a page with the given fill color.
    /// </summary>
    public void RedactArea(int pageIndex, Rectangle rect, System.Drawing.Color color)
    {
        RedactArea(pageIndex, rect, color.R, color.G, color.B);
    }

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

    /// <summary>
    /// Export annotations to an XFDF stream.
    /// </summary>
    /// <param name="xfdfStream">Output stream.</param>
    /// <param name="startPage">Start page (1-based).</param>
    /// <param name="endPage">End page (1-based).</param>
    /// <param name="annotTypes">Annotation types to export.</param>
    public void ExportAnnotationsXfdf(Stream xmlOutputStream, int start, int end, AnnotationType[] annotTypes)
    {
        var doc = Document;
        var typeSet = new HashSet<AnnotationType>(annotTypes);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            // Entitize carriage returns (&#xD;) instead of the default Replace, which
            // rewrites a lone \r in text as \r\n. XML parsers leave character references
            // unchanged, so an annotation's /Contents survives the export/import round-trip
            // byte-for-byte (a bare \r stays a bare \r).
            NewLineHandling = NewLineHandling.Entitize,
        };

        using var writer = XmlWriter.Create(xmlOutputStream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("xfdf", "http://ns.adobe.com/xfdf/");
        writer.WriteAttributeString("xml", "space", null, "preserve");

        writer.WriteStartElement("fields");
        writer.WriteEndElement(); // fields

        writer.WriteStartElement("annots");

        for (int pageIdx = Math.Max(1, start); pageIdx <= Math.Min(end, doc.PageCount); pageIdx++)
        {
            var page = doc.Pages.At(pageIdx);
            foreach (var annot in page.Annotations)
            {
                if (!typeSet.Contains(annot.AnnotationType)) continue;
                // Skip Popup annotations — they are written as children of their parent annotations
                if (annot.AnnotationType == AnnotationType.Popup) continue;
                WriteXfdfAnnotation(writer, annot, pageIdx - 1, doc.Reader); // XFDF uses 0-based pages
            }
        }

        writer.WriteEndElement(); // annots
        writer.WriteEndElement(); // xfdf
        writer.WriteEndDocument();
        writer.Flush();

        // Truncate any stale data if the stream was previously longer (e.g. FileMode.OpenOrCreate)
        if (xmlOutputStream.CanSeek && xmlOutputStream.CanWrite)
            xmlOutputStream.SetLength(xmlOutputStream.Position);
    }

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

    /// <summary>
    /// Imports a single annotation from an XFDF XML node into the document.
    /// Maps XFDF element/attribute names to PDF annotation dictionary entries
    /// per the XFDF specification (PDF 32000 §12.7.8). Each section handles
    /// one XFDF construct: rect, flags, color, contents, ink gestures, popup, etc.
    /// </summary>
    private static void ImportXfdfAnnotation(Document doc, XmlNode node)
    {
        var pageAttr = node.Attributes?["page"];
        int pageIdx = 0;
        if (pageAttr is not null)
            int.TryParse(pageAttr.Value, out pageIdx);

        // XFDF page is 0-based
        int pageNum = pageIdx + 1;
        if (pageNum < 1 || pageNum > doc.PageCount) return;

        var page = doc.Pages.At(pageNum);
        var subtype = XfdfTagToSubtype(node.LocalName.ToLowerInvariant());
        if (subtype is null) return;

        // Parse rect
        var rectAttr = node.Attributes?["rect"];
        Rectangle rect;
        if (rectAttr is not null)
        {
            var parts = rectAttr.Value.Split(',');
            if (parts.Length >= 4)
            {
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double llx);
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lly);
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double urx);
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double ury);
                rect = new Rectangle(llx, lly, urx, ury);
            }
            else
            {
                rect = new Rectangle(0, 0, 0, 0);
            }
        }
        else
        {
            rect = new Rectangle(0, 0, 0, 0);
        }

        // Build annotation dictionary
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));

        var rectArr = new PdfArray();
        rectArr.Add(new PdfReal(rect.LLX));
        rectArr.Add(new PdfReal(rect.LLY));
        rectArr.Add(new PdfReal(rect.URX));
        rectArr.Add(new PdfReal(rect.URY));
        dict.Set("Rect", rectArr);

        // Standard attributes
        SetIfPresent(dict, node, "name", "NM");
        SetIfPresent(dict, node, "title", "T");
        SetIfPresent(dict, node, "subject", "Subj");
        SetIfPresent(dict, node, "date", "M");
        SetIfPresent(dict, node, "creationdate", "CreationDate");

        // Flags
        var flagsAttr = node.Attributes?["flags"];
        if (flagsAttr is not null)
        {
            int flags = ParseFlags(flagsAttr.Value);
            dict.Set("F", new PdfInteger(flags));
        }
        else
        {
            dict.Set("F", new PdfInteger(4)); // Print
        }

        // Color
        var colorAttr = node.Attributes?["color"];
        if (colorAttr is not null)
        {
            var rgb = ParseHexColor(colorAttr.Value);
            if (rgb is not null)
            {
                var c = new PdfArray();
                c.Add(new PdfReal(rgb[0]));
                c.Add(new PdfReal(rgb[1]));
                c.Add(new PdfReal(rgb[2]));
                dict.Set("C", c);
            }
        }

        // Arbitrary FreeText rotation angle (Adobe XFDF) → /Rotate in degrees.
        // GenerateAppearance bakes this into the appearance stream and expands /Rect
        // to the rotated bounding box.
        var rotationAttr = node.Attributes?["rotation"];
        if (rotationAttr is not null
            && double.TryParse(rotationAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rotDeg)
            && Math.Abs(rotDeg % 360.0) > 1e-6)
        {
            dict.Set("Rotate", new PdfReal(rotDeg));
        }

        // FreeText callout line (Adobe XFDF "callout" attribute → /CL array of
        // 4 or 6 numbers: [x1 y1 x2 y2] or [x1 y1 x2 y2 x3 y3]).
        var calloutAttr = node.Attributes?["callout"];
        if (calloutAttr is not null)
        {
            var cl = new PdfArray();
            foreach (var n in calloutAttr.Value.Split(','))
                if (double.TryParse(n.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cv))
                    cl.Add(new PdfReal(cv));
            if (cl.Count >= 4) dict.Set("CL", cl);
        }

        // Interior color (for redact)
        var icAttr = node.Attributes?["interior-color"];
        if (icAttr is not null)
        {
            var rgb = ParseHexColor(icAttr.Value);
            if (rgb is not null)
            {
                var ic = new PdfArray();
                ic.Add(new PdfReal(rgb[0]));
                ic.Add(new PdfReal(rgb[1]));
                ic.Add(new PdfReal(rgb[2]));
                dict.Set("IC", ic);
            }
        }

        // Redaction overlay text (/OverlayText, /Repeat)
        var overlayAttr = node.Attributes?["overlay-text"];
        if (overlayAttr is not null)
            dict.Set("OverlayText", MakePdfTextString(overlayAttr.Value));
        var repeatAttr = node.Attributes?["repeat"];
        if (repeatAttr is not null)
            dict.Set("Repeat", repeatAttr.Value is "yes" or "true" ? PdfBoolean.True : PdfBoolean.False);

        // Contents (child element or attribute)
        var contentsNode = node.SelectSingleNode("contents") ?? node.SelectSingleNode("*[local-name()='contents']");
        if (contentsNode is not null)
            dict.Set("Contents", MakePdfTextString(contentsNode.InnerText));

        // Rich text (contents-richtext → /RC entry). The /RC value carries an
        // XML declaration prefix that cannot live inside the XFDF element, so
        // it is reconstructed here to round-trip faithfully.
        var richTextNode = node.SelectSingleNode("contents-richtext") ?? node.SelectSingleNode("*[local-name()='contents-richtext']");
        if (richTextNode is not null)
            dict.Set("RC", MakePdfTextString("<?xml version=\"1.0\"?>" + richTextNode.InnerXml));

        // Default appearance
        var daNode = node.SelectSingleNode("defaultappearance") ?? node.SelectSingleNode("*[local-name()='defaultappearance']");
        var daText = daNode?.InnerText;
        // FreeText text colour is carried by the Adobe XFDF "TextColor" attribute,
        // which takes precedence over the colour in the default appearance string.
        // Fold it into the /DA fill colour so the generated appearance renders it.
        var textColorAttr = node.Attributes?["TextColor"] ?? node.Attributes?["textcolor"];
        if (textColorAttr is not null)
        {
            var rgb = ParseHexColor(textColorAttr.Value);
            if (rgb is not null)
            {
                var tf = System.Text.RegularExpressions.Regex.Match(daText ?? "", @"/\S+\s+[\d.]+\s+Tf");
                var tfPart = tf.Success ? tf.Value : "/Helvetica 12 Tf";
                daText = string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######} rg {3}",
                    rgb[0], rgb[1], rgb[2], tfPart);
            }
        }
        if (daText is not null)
            dict.Set("DA", new PdfString(Encoding.Latin1.GetBytes(daText)));

        // Default style (/DS — free text)
        var dsNode = node.SelectSingleNode("defaultstyle") ?? node.SelectSingleNode("*[local-name()='defaultstyle']");
        if (dsNode is not null)
            dict.Set("DS", MakePdfTextString(dsNode.InnerText));

        // Justification (/Q — free text)
        var justAttr = node.Attributes?["justification"];
        if (justAttr is not null)
        {
            int q = justAttr.Value.ToLowerInvariant() switch { "centered" => 1, "center" => 1, "right" => 2, _ => 0 };
            dict.Set("Q", new PdfInteger(q));
        }

        // Icon (for text annotations)
        var iconAttr = node.Attributes?["icon"];
        if (iconAttr is not null)
            dict.Set("Name", new PdfName(iconAttr.Value));

        // Open (for text annotations)
        var openAttr = node.Attributes?["open"];
        if (openAttr is not null)
            dict.Set("Open", openAttr.Value == "yes" ? PdfBoolean.True : PdfBoolean.False);

        // Width / border style (/BS)
        var widthAttr = node.Attributes?["width"];
        var styleAttr = node.Attributes?["style"];
        var dashesAttr = node.Attributes?["dashes"];
        if (widthAttr is not null || styleAttr is not null || dashesAttr is not null)
        {
            var bs = new PdfDictionary();
            if (widthAttr is not null && double.TryParse(widthAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bw))
                bs.Set("W", new PdfReal(bw));
            string styleName = styleAttr?.Value.ToLowerInvariant() switch
            {
                "dash" => "D",
                "bevel" => "B",
                "inset" => "I",
                "underline" => "U",
                _ => "S",
            };
            bs.Set("S", new PdfName(styleName));
            if (dashesAttr is not null)
            {
                var d = ParseDoubleList(dashesAttr.Value);
                if (d.Length > 0)
                {
                    var dArr = new PdfArray();
                    foreach (var v in d) dArr.Add(new PdfReal(v));
                    bs.Set("D", dArr);
                }
            }
            dict.Set("BS", bs);
        }

        // Fringe (/RD rectangle differences — square/circle/caret)
        var fringeAttr = node.Attributes?["fringe"];
        if (fringeAttr is not null)
        {
            var rdv = ParseDoubleList(fringeAttr.Value);
            if (rdv.Length >= 4)
            {
                var rdArr = new PdfArray();
                foreach (var v in rdv) rdArr.Add(new PdfReal(v));
                dict.Set("RD", rdArr);
            }
        }

        // Symbol (/Sy — caret)
        var symbolAttr = node.Attributes?["symbol"];
        if (symbolAttr is not null && symbolAttr.Value.ToLowerInvariant() == "paragraph")
            dict.Set("Sy", new PdfName("P"));

        // Line geometry (/L, /LE, leader lines, caption — line annotations)
        var startAttr = node.Attributes?["start"];
        var endAttr = node.Attributes?["end"];
        if (startAttr is not null || endAttr is not null)
        {
            var s = startAttr is not null ? ParseDoubleList(startAttr.Value) : Array.Empty<double>();
            var e = endAttr is not null ? ParseDoubleList(endAttr.Value) : Array.Empty<double>();
            var lArr = new PdfArray();
            lArr.Add(new PdfReal(s.Length > 0 ? s[0] : 0));
            lArr.Add(new PdfReal(s.Length > 1 ? s[1] : 0));
            lArr.Add(new PdfReal(e.Length > 0 ? e[0] : 0));
            lArr.Add(new PdfReal(e.Length > 1 ? e[1] : 0));
            dict.Set("L", lArr);
        }
        var headAttr = node.Attributes?["head"];
        var tailAttr = node.Attributes?["tail"];
        if (headAttr is not null || tailAttr is not null)
        {
            var leArr = new PdfArray();
            leArr.Add(new PdfName(headAttr?.Value ?? "None"));
            leArr.Add(new PdfName(tailAttr?.Value ?? "None"));
            dict.Set("LE", leArr);
        }
        SetRealAttr(dict, node, "leaderLength", "LL");
        SetRealAttr(dict, node, "leaderExtend", "LLE");
        SetRealAttr(dict, node, "leaderOffset", "LLO");
        var captionAttr = node.Attributes?["caption"];
        if (captionAttr is not null)
            dict.Set("Cap", captionAttr.Value == "yes" ? PdfBoolean.True : PdfBoolean.False);
        var captionStyleAttr = node.Attributes?["caption-style"];
        if (captionStyleAttr is not null)
            dict.Set("CP", new PdfName(captionStyleAttr.Value));
        var coh = node.Attributes?["caption-offset-h"];
        var cov = node.Attributes?["caption-offset-v"];
        if (coh is not null || cov is not null)
        {
            var coArr = new PdfArray();
            coArr.Add(new PdfReal(coh is not null && double.TryParse(coh.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hh) ? hh : 0));
            coArr.Add(new PdfReal(cov is not null && double.TryParse(cov.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var vv) ? vv : 0));
            dict.Set("CO", coArr);
        }

        // File-attachment embedded file (/FS): rebuild the file spec + embedded
        // stream from the file metadata attributes and the base64 <data> child.
        var dataNode = node.SelectSingleNode("data") ?? node.SelectSingleNode("*[local-name()='data']");
        var fileAttr = node.Attributes?["file"];
        if (subtype == "FileAttachment" && (fileAttr is not null || dataNode is not null))
        {
            byte[] fileBytes = Array.Empty<byte>();
            if (dataNode is not null)
            {
                var enc = dataNode.Attributes?["encoding"]?.Value;
                var text = dataNode.InnerText.Trim();
                try { fileBytes = enc == "base64" ? Convert.FromBase64String(text) : Encoding.UTF8.GetBytes(dataNode.InnerText); }
                catch (FormatException) { fileBytes = Encoding.UTF8.GetBytes(dataNode.InnerText); }
            }

            var name = fileAttr?.Value ?? string.Empty;
            var fsDict = new PdfDictionary();
            fsDict.Set("Type", new PdfName("Filespec"));
            fsDict.Set("F", MakePdfTextString(name));
            fsDict.Set("UF", MakePdfTextString(name));

            var efStreamDict = new PdfDictionary();
            efStreamDict.Set("Type", new PdfName("EmbeddedFile"));
            var mimeAttr = node.Attributes?["mimetype"];
            if (mimeAttr is not null) efStreamDict.Set("Subtype", new PdfName(mimeAttr.Value));

            var prmDict = new PdfDictionary();
            var sizeA = node.Attributes?["size"];
            if (sizeA is not null && int.TryParse(sizeA.Value, out var sz)) prmDict.Set("Size", new PdfInteger(sz));
            var csA = node.Attributes?["checksum"];
            if (csA is not null) prmDict.Set("CheckSum", new PdfString(FromHex(csA.Value)));
            var crA = node.Attributes?["creation"];
            if (crA is not null) prmDict.Set("CreationDate", new PdfString(Encoding.Latin1.GetBytes(crA.Value)));
            var mdA = node.Attributes?["modification"];
            if (mdA is not null) prmDict.Set("ModDate", new PdfString(Encoding.Latin1.GetBytes(mdA.Value)));
            efStreamDict.Set("Params", prmDict);

            efStreamDict.Set("Length", new PdfInteger(fileBytes.Length));
            var efStream = new PdfStream(efStreamDict, fileBytes);
            var efDict = new PdfDictionary();
            efDict.Set("F", efStream);
            fsDict.Set("EF", efDict);
            dict.Set("FS", fsDict);
        }

        // Sound annotation embedded audio (/Sound): rebuild the sound stream from
        // the sampling attributes and the base64 <data> child.
        if (subtype == "Sound" && (dataNode is not null || node.Attributes?["rate"] is not null))
        {
            byte[] soundBytes = Array.Empty<byte>();
            string? dataMode = null, dataFilter = null;
            if (dataNode is not null)
            {
                var enc = dataNode.Attributes?["encoding"]?.Value;
                dataMode = dataNode.Attributes?["mode"]?.Value;
                dataFilter = dataNode.Attributes?["filter"]?.Value;
                var text = dataNode.InnerText.Trim();
                try
                {
                    soundBytes = enc switch
                    {
                        "hex" => FromHex(text),
                        "base64" => Convert.FromBase64String(text),
                        _ => Encoding.ASCII.GetBytes(dataNode.InnerText),
                    };
                }
                catch (FormatException) { soundBytes = Encoding.UTF8.GetBytes(dataNode.InnerText); }
            }
            var soundDict = new PdfDictionary();
            soundDict.Set("Type", new PdfName("Sound"));
            var rateA = node.Attributes?["rate"];
            soundDict.Set("R", new PdfReal(rateA is not null && double.TryParse(rateA.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rr) ? rr : 11025));
            var bitsA = node.Attributes?["bits"];
            if (bitsA is not null && int.TryParse(bitsA.Value, out var bb)) soundDict.Set("B", new PdfInteger(bb));
            var chA = node.Attributes?["channels"];
            if (chA is not null && int.TryParse(chA.Value, out var ch)) soundDict.Set("C", new PdfInteger(ch));
            // /E (sound encoding) — element "encoding" attr, normalised to the PDF name.
            var encA = node.Attributes?["encoding"];
            if (encA is not null)
                soundDict.Set("E", new PdfName(encA.Value.ToLowerInvariant() switch
                {
                    "signed" => "Signed",
                    "mulaw" => "muLaw",
                    "alaw" => "ALaw",
                    _ => "Raw",
                }));
            // "raw" data mode keeps the bytes filter-encoded, so preserve the filter
            // and let reads decode them; "filtered" data is already decoded.
            if (dataMode == "raw" && dataFilter is not null)
                soundDict.Set("Filter", new PdfName(dataFilter));
            soundDict.Set("Length", new PdfInteger(soundBytes.Length));
            var soundStream = new PdfStream(soundDict, soundBytes);
            dict.Set("Sound", soundStream);
        }

        // QuadPoints / coords
        var coordsAttr = node.Attributes?["coords"];
        if (coordsAttr is not null)
        {
            var qp = ParseDoubleList(coordsAttr.Value);
            if (qp.Length > 0)
            {
                var qpArr = new PdfArray();
                foreach (var v in qp) qpArr.Add(new PdfReal(v));
                dict.Set("QuadPoints", qpArr);
            }
        }

        // Vertices (polygon / polyline) — child element "x,y;x,y;..."
        var verticesNode = node.SelectSingleNode("vertices") ?? node.SelectSingleNode("*[local-name()='vertices']");
        if (verticesNode is not null)
        {
            var vArr = new PdfArray();
            foreach (var pair in verticesNode.InnerText.Split(';'))
            {
                var xy = pair.Split(',');
                if (xy.Length >= 2
                    && double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vx)
                    && double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vy))
                {
                    vArr.Add(new PdfReal(vx));
                    vArr.Add(new PdfReal(vy));
                }
            }
            if (vArr.Count > 0) dict.Set("Vertices", vArr);
        }

        // Intent (/IT). Adobe XFDF names the FreeText intent attribute "IT"
        // (uppercase); accept that spelling alongside "intent"/"it".
        var intentAttr = node.Attributes?["intent"] ?? node.Attributes?["it"] ?? node.Attributes?["IT"];
        if (intentAttr is not null)
        {
            // Map the lowercase-hyphenated polygon/polyline XFDF intent back to the
            // PascalCase /IT name; other intents (line) pass through unchanged.
            var itName = intentAttr.Value switch
            {
                "polygon-cloud" => "PolygonCloud",
                "polygon-dimension" => "PolygonDimension",
                "polyline-dimension" => "PolyLineDimension",
                _ => intentAttr.Value,
            };
            dict.Set("IT", new PdfName(itName));
        }

        // InReplyTo
        var irtAttr = node.Attributes?["inreplyto"];
        if (irtAttr is not null)
            dict.Set("IRT_Name", new PdfString(Encoding.Latin1.GetBytes(irtAttr.Value)));

        // State / StateModel — stored as PDF text strings (/State, /StateModel)
        var stateAttr = node.Attributes?["state"];
        if (stateAttr is not null)
            dict.Set("State", MakePdfTextString(stateAttr.Value));

        var stateModelAttr = node.Attributes?["statemodel"];
        if (stateModelAttr is not null)
            dict.Set("StateModel", MakePdfTextString(stateModelAttr.Value));

        // ReplyType (reply | group → /RT)
        var replyTypeAttr = node.Attributes?["replyType"];
        if (replyTypeAttr is not null)
        {
            string? rt = replyTypeAttr.Value.ToLowerInvariant() switch
            {
                "reply" => "R",
                "group" => "Group",
                _ => null
            };
            if (rt is not null) dict.Set("RT", new PdfName(rt));
        }

        // Opacity (/CA)
        var opacityAttr = node.Attributes?["opacity"];
        if (opacityAttr is not null && double.TryParse(opacityAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double op))
            dict.Set("CA", new PdfReal(op));

        // Ink gesture data
        var inkListNode = node.SelectSingleNode("inklist") ?? node.SelectSingleNode("*[local-name()='inklist']");
        if (inkListNode is not null)
        {
            var inkList = new PdfArray();
            foreach (XmlNode gesture in inkListNode.ChildNodes)
            {
                if (gesture.NodeType != XmlNodeType.Element) continue;
                var points = gesture.InnerText.Split(';');
                var pathArr = new PdfArray();
                foreach (var pt in points)
                {
                    var coords = pt.Split(',');
                    if (coords.Length >= 2)
                    {
                        if (double.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                            pathArr.Add(new PdfReal(x));
                        if (double.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                            pathArr.Add(new PdfReal(y));
                    }
                }
                inkList.Add(pathArr);
            }
            dict.Set("InkList", inkList);
        }

        // Stamp image appearance: an XFDF <imagedata> child carries the rubber
        // stamp's actual picture as a base64 data: URI (e.g. a scanned/scripted
        // "Guest" signature stamp). Decode it and build the /AP /N image
        // appearance so the stamp renders its real image instead of the fallback
        // icon banner (e.g. the "Draft" box synthesised from /Name).
        if (subtype == "Stamp")
        {
            var imgNode = node.SelectSingleNode("imagedata")
                ?? node.SelectSingleNode("*[local-name()='imagedata']");
            var imgBytes = DecodeDataUriBase64(imgNode?.InnerText);
            if (imgBytes is { Length: > 0 })
            {
                var stamp = new Aspose.Pdf.Annotations.StampAnnotation(dict, doc.Reader);
                stamp.Image = new MemoryStream(imgBytes);
            }
        }

        // Append the main annotation first so it precedes its popup in /Annots
        // (round-trip consumers index the markup annotation at position 1).
        AppendAnnotationDict(page, dict);

        // Popup child
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (childNode.NodeType == XmlNodeType.Element && childNode.LocalName.ToLowerInvariant() == "popup")
            {
                var popupDict = new PdfDictionary();
                popupDict.Set("Type", new PdfName("Annot"));
                popupDict.Set("Subtype", new PdfName("Popup"));

                var popupRectAttr = childNode.Attributes?["rect"];
                if (popupRectAttr is not null)
                {
                    var parts = popupRectAttr.Value.Split(',');
                    if (parts.Length >= 4)
                    {
                        var pr = new PdfArray();
                        foreach (var p in parts)
                        {
                            if (double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                                pr.Add(new PdfReal(v));
                        }
                        popupDict.Set("Rect", pr);
                    }
                }

                var popupFlagsAttr = childNode.Attributes?["flags"];
                if (popupFlagsAttr is not null)
                    popupDict.Set("F", new PdfInteger(ParseFlags(popupFlagsAttr.Value)));

                // Open state: check attribute first, then child element
                var popupOpenAttr = childNode.Attributes?["open"];
                string? openVal = popupOpenAttr?.Value;
                if (openVal is null)
                {
                    foreach (XmlNode pc in childNode.ChildNodes)
                        if (pc.NodeType == XmlNodeType.Element && pc.LocalName == "open")
                            { openVal = pc.InnerText; break; }
                }
                if (openVal is not null)
                    popupDict.Set("Open", openVal == "yes" ? PdfBoolean.True : PdfBoolean.False);

                // Link popup to parent
                popupDict.Set("Parent", dict);
                dict.Set("Popup", popupDict);

                // Add popup to page annotations array too
                AppendAnnotationDict(page, popupDict);
            }
        }
    }

    /// <summary>
    /// Writes a single annotation as an XFDF XML element.
    /// Maps PDF annotation dictionary entries back to XFDF attributes/elements
    /// per the XFDF specification (PDF 32000 §12.7.8). Inverse of ImportXfdfAnnotation.
    /// </summary>
    internal static void WriteXfdfAnnotation(XmlWriter writer, Annotation annot, int zeroBasedPage,
        IO.PdfReader? reader = null, bool writeContents = true, bool normalizeRichText = false)
    {
        var tag = AnnotationTypeToXfdfTag(annot.AnnotationType);
        if (tag == "unknown") return;

        writer.WriteStartElement(tag);

        // Page
        writer.WriteAttributeString("page", zeroBasedPage.ToString(CultureInfo.InvariantCulture));

        // Rect
        var r = annot.Rect;
        if (r is not null)
            writer.WriteAttributeString("rect",
                $"{F(r.LLX)},{F(r.LLY)},{F(r.URX)},{F(r.URY)}");

        // Color
        var colorArr = annot.Dict.Get("C");
        if (colorArr is PdfArray ca && ca.Count >= 3)
        {
            double cr = GetDouble(ca[0]), cg = GetDouble(ca[1]), cb = GetDouble(ca[2]);
            writer.WriteAttributeString("color",
                $"#{(int)Math.Round(cr * 255):X2}{(int)Math.Round(cg * 255):X2}{(int)Math.Round(cb * 255):X2}");
        }

        // Flags
        int flags = (int)annot.Flags;
        if (flags != 0)
            writer.WriteAttributeString("flags", FormatFlags(flags));

        // Title
        if (annot.Title is not null)
            writer.WriteAttributeString("title", annot.Title);

        // Subject
        var subj = annot.Dict.Get("Subj");
        string? subject = subj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null
        };
        if (subject is not null)
            writer.WriteAttributeString("subject", subject);

        // Date
        if (annot.ModifiedDate is not null)
            writer.WriteAttributeString("date", annot.ModifiedDate);

        // CreationDate — written as child element below (XFDF convention)

        // Icon (/Name — text, stamp, file-attachment and sound annotations)
        if (annot.AnnotationType is AnnotationType.Text or AnnotationType.Stamp
            or AnnotationType.FileAttachment or AnnotationType.Sound)
        {
            var iconName = annot.Dict.GetName("Name");
            if (iconName is not null)
                writer.WriteAttributeString("icon", iconName);
        }

        // Interior color (for redact)
        var icArr = annot.Dict.Get("IC");
        if (icArr is PdfArray ica && ica.Count >= 3)
        {
            double ir = GetDouble(ica[0]), ig = GetDouble(ica[1]), ib = GetDouble(ica[2]);
            writer.WriteAttributeString("interior-color",
                $"#{(int)Math.Round(ir * 255):X2}{(int)Math.Round(ig * 255):X2}{(int)Math.Round(ib * 255):X2}");
        }

        // Redaction overlay text (/OverlayText, /Repeat — redact annotations)
        if (annot.AnnotationType == AnnotationType.Redact)
        {
            var otObj = reader is not null ? reader.Resolve(annot.Dict.Get("OverlayText")) : annot.Dict.Get("OverlayText");
            if (otObj is PdfString otStr)
                writer.WriteAttributeString("overlay-text", otStr.ToText());
            if (annot.Dict.Get("Repeat") is PdfBoolean repB && repB.Value)
                writer.WriteAttributeString("repeat", "yes");
        }

        // Width / style / dashes (/BS — may be an indirect reference)
        bool styleWritten = false;
        var bsObj = annot.Dict.Get("BS");
        var bsd = bsObj as PdfDictionary ?? (reader is not null ? reader.ResolveDict(bsObj) : null);
        if (bsd is not null)
        {
            var wObj = bsd.Get("W");
            if (wObj is not null)
                writer.WriteAttributeString("width", F(GetDouble(wObj)));
            var styleXfdf = bsd.GetName("S") switch
            {
                "D" => "dash",
                "B" => "bevel",
                "I" => "inset",
                "U" => "underline",
                _ => null,
            };
            if (styleXfdf is not null)
            {
                writer.WriteAttributeString("style", styleXfdf);
                styleWritten = true;
            }
            var dObj = reader is not null ? reader.Resolve(bsd.Get("D")) : bsd.Get("D");
            if (dObj is PdfArray dArr && dArr.Count > 0)
            {
                var ds = new StringBuilder();
                for (int i = 0; i < dArr.Count; i++) { if (i > 0) ds.Append(','); ds.Append(F(GetDouble(dArr[i]))); }
                writer.WriteAttributeString("dashes", ds.ToString());
            }
        }

        // Border effect (/BE — cloudy borders on square/circle/freetext): style="cloudy" + intensity
        var beDict = annot.Dict.Get("BE") as PdfDictionary ?? (reader is not null ? reader.ResolveDict(annot.Dict.Get("BE")) : null);
        if (beDict is not null && beDict.GetName("S") == "C")
        {
            if (!styleWritten) writer.WriteAttributeString("style", "cloudy");
            var beI = beDict.Get("I");
            if (beI is PdfInteger || beI is PdfReal)
                writer.WriteAttributeString("intensity", F(GetDouble(beI!)));
        }

        // Fringe (/RD rectangle differences — square/circle/caret)
        var rdObj = annot.Dict.Get("RD");
        if (rdObj is PdfArray rdArr && rdArr.Count >= 4)
        {
            var fr = new StringBuilder();
            for (int i = 0; i < rdArr.Count; i++) { if (i > 0) fr.Append(','); fr.Append(F(GetDouble(rdArr[i]))); }
            writer.WriteAttributeString("fringe", fr.ToString());
        }

        // Symbol (/Sy — caret)
        if (annot.Dict.GetName("Sy") == "P")
            writer.WriteAttributeString("symbol", "paragraph");

        // FreeText callout line (/CL → "callout" attribute: comma-separated coords).
        var clObj = reader is not null ? reader.Resolve(annot.Dict.Get("CL")) : annot.Dict.Get("CL");
        if (clObj is PdfArray clArr && clArr.Count >= 4)
        {
            var co = new StringBuilder();
            for (int i = 0; i < clArr.Count; i++) { if (i > 0) co.Append(','); co.Append(F(GetDouble(clArr[i]))); }
            writer.WriteAttributeString("callout", co.ToString());
        }

        // Coords (QuadPoints)
        var qpObj = annot.Dict.Get("QuadPoints");
        if (qpObj is PdfArray qpa && qpa.Count > 0)
        {
            var coords = new StringBuilder();
            for (int i = 0; i < qpa.Count; i++)
            {
                if (i > 0) coords.Append(',');
                coords.Append(F(GetDouble(qpa[i])));
            }
            writer.WriteAttributeString("coords", coords.ToString());
        }

        // Intent (/IT). The polyline dimension intent uses the lowercase-hyphenated
        // XFDF form; all other intents (PolygonCloud, LineArrow, …) keep the raw name.
        var intentName = annot.Dict.GetName("IT");
        if (intentName is not null)
        {
            string intentXfdf = intentName == "PolyLineDimension" ? "polyline-dimension" : intentName;
            // Free-text annotations name the intent attribute "IT" (Adobe XFDF); others use "intent".
            writer.WriteAttributeString(annot.AnnotationType == AnnotationType.FreeText ? "IT" : "intent", intentXfdf);
        }

        // Justification (/Q — free text)
        if (annot.Dict.Get("Q") is PdfInteger qi)
        {
            string? just = qi.Value switch { 1 => "centered", 2 => "right", 0 => "left", _ => null };
            if (just is not null) writer.WriteAttributeString("justification", just);
        }

        // Line geometry (/L, /LE, leader lines, caption — line annotations)
        var lObj = reader is not null ? reader.Resolve(annot.Dict.Get("L")) : annot.Dict.Get("L");
        if (lObj is PdfArray lArr && lArr.Count >= 4)
        {
            writer.WriteAttributeString("start", $"{F(GetDouble(lArr[0]))},{F(GetDouble(lArr[1]))}");
            writer.WriteAttributeString("end", $"{F(GetDouble(lArr[2]))},{F(GetDouble(lArr[3]))}");
        }
        var leObj = reader is not null ? reader.Resolve(annot.Dict.Get("LE")) : annot.Dict.Get("LE");
        if (leObj is PdfArray leArr && leArr.Count >= 2)
        {
            if ((reader is not null ? reader.Resolve(leArr[0]) : leArr[0]) is PdfName headName)
                writer.WriteAttributeString("head", headName.Value);
            if ((reader is not null ? reader.Resolve(leArr[1]) : leArr[1]) is PdfName tailName)
                writer.WriteAttributeString("tail", tailName.Value);
        }
        else if (leObj is PdfName leName)
        {
            // Callout annotations (free text) carry a single /LE line-ending name.
            writer.WriteAttributeString("head", leName.Value);
            writer.WriteAttributeString("tail", "None");
        }
        var llObj = annot.Dict.Get("LL");
        if (llObj is PdfReal || llObj is PdfInteger)
            writer.WriteAttributeString("leaderLength", F(GetDouble(llObj!)));
        var lleObj = annot.Dict.Get("LLE");
        if (lleObj is PdfReal || lleObj is PdfInteger)
            writer.WriteAttributeString("leaderExtend", F(GetDouble(lleObj!)));
        var lloObj = annot.Dict.Get("LLO");
        if (lloObj is PdfReal || lloObj is PdfInteger)
            writer.WriteAttributeString("leaderOffset", F(GetDouble(lloObj!)));
        if (annot.Dict.Get("Cap") is PdfBoolean cap)
            writer.WriteAttributeString("caption", cap.Value ? "yes" : "no");
        var cpName = annot.Dict.GetName("CP");
        if (cpName is not null)
            writer.WriteAttributeString("caption-style", cpName);
        var coObj = reader is not null ? reader.Resolve(annot.Dict.Get("CO")) : annot.Dict.Get("CO");
        if (coObj is PdfArray coArr && coArr.Count >= 2)
        {
            writer.WriteAttributeString("caption-offset-h", F(GetDouble(coArr[0])));
            writer.WriteAttributeString("caption-offset-v", F(GetDouble(coArr[1])));
        }

        // File-attachment embedded-file metadata (/FS) — read straight from the
        // /FS/EF/F stream so the raw /Params strings round-trip verbatim. The file
        // bytes follow as a <data> child element below.
        if (annot.AnnotationType == AnnotationType.FileAttachment && reader is not null)
        {
            var fsd = reader.ResolveDict(annot.Dict.Get("FS"));
            var efd0 = reader.ResolveDict(fsd?.Get("EF"));
            var efStream0 = efd0 is null ? null : reader.ResolveStream(efd0.Get("F"));
            var nameObj = reader.Resolve(fsd?.Get("UF")) ?? reader.Resolve(fsd?.Get("F"));
            if (nameObj is PdfString nameStr) writer.WriteAttributeString("file", nameStr.ToText());
            var mimeName = efStream0?.Dict.GetName("Subtype");
            if (mimeName is not null) writer.WriteAttributeString("mimetype", mimeName);
            var prm = reader.ResolveDict(efStream0?.Dict.Get("Params"));
            long sz = prm?.Get("Size") is PdfInteger szi ? szi.Value : 0;
            writer.WriteAttributeString("size", sz.ToString(CultureInfo.InvariantCulture));
            if (prm?.Get("ModDate") is PdfString md) writer.WriteAttributeString("modification", md.ToText());
            if (prm?.Get("CreationDate") is PdfString cr) writer.WriteAttributeString("creation", cr.ToText());
            // /Params/CheckSum holds the raw 16-byte MD5; XFDF carries it hex-encoded.
            if (prm?.Get("CheckSum") is PdfString cs) writer.WriteAttributeString("checksum", ToHex(cs.Value));
        }

        // Sound annotation sampling parameters (/Sound R/B/C/E) — the audio bytes
        // follow as a <data> child element below.
        if (annot is Aspose.Pdf.Annotations.SoundAnnotation saMeta && saMeta.SoundData is { } sndMeta)
        {
            writer.WriteAttributeString("rate", sndMeta.Rate.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("bits", sndMeta.Bits.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("channels", sndMeta.Channels.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("encoding", sndMeta.Encoding switch
            {
                Aspose.Pdf.Annotations.SoundEncoding.Signed => "signed",
                Aspose.Pdf.Annotations.SoundEncoding.MuLaw => "muLaw",
                Aspose.Pdf.Annotations.SoundEncoding.ALaw => "aLaw",
                _ => "raw",
            });
        }

        // InReplyTo (/IRT — may be an indirect reference to the replied-to annotation)
        var irtD = annot.Dict.Get("IRT") as PdfDictionary
            ?? (reader is not null ? reader.ResolveDict(annot.Dict.Get("IRT")) : null);
        if (irtD is not null)
        {
            var irtNm = reader is not null ? reader.Resolve(irtD.Get("NM")) : irtD.Get("NM");
            if (irtNm is PdfString irtNmStr)
                writer.WriteAttributeString("inreplyto", irtNmStr.ToText());
        }

        // State / StateModel
        if (annot.AnnotationState is not null)
            writer.WriteAttributeString("state", annot.AnnotationState);
        if (annot.AnnotationStateModel is not null)
            writer.WriteAttributeString("statemodel", annot.AnnotationStateModel);

        // Open
        var openObj = annot.Dict.Get("Open");
        if (openObj is PdfBoolean ob)
            writer.WriteAttributeString("open", ob.Value ? "yes" : "no");

        // Opacity (/CA)
        var caObj = annot.Dict.Get("CA");
        if (caObj is PdfReal || caObj is PdfInteger)
            writer.WriteAttributeString("opacity", F(GetDouble(caObj)));

        // ReplyType (/RT → reply | group)
        var rtName = annot.Dict.GetName("RT");
        if (rtName is not null)
        {
            string? rt = rtName switch { "R" => "reply", "Group" => "group", _ => null };
            if (rt is not null) writer.WriteAttributeString("replyType", rt);
        }

        // Name (/NM) — XFDF attribute
        if (annot.Name is not null)
            writer.WriteAttributeString("name", annot.Name);

        // CreationDate (/CreationDate) — XFDF attribute
        var creationObj = annot.Dict.Get("CreationDate");
        if (creationObj is PdfString creationStr)
            writer.WriteAttributeString("creationdate", creationStr.ToText());

        // ── Child elements, ordered to match the XFDF output: geometry/data
        //    children, then contents-richtext, then popup, then default-appearance.

        // InkList (ink annotations)
        var inkListObj = annot.Dict.Get("InkList");
        if (inkListObj is PdfArray inkList && inkList.Count > 0)
        {
            writer.WriteStartElement("inklist");
            foreach (var gestureObj in inkList)
            {
                if (gestureObj is PdfArray gesture)
                {
                    writer.WriteStartElement("gesture");
                    var sb = new StringBuilder();
                    for (int i = 0; i < gesture.Count; i += 2)
                    {
                        if (i > 0) sb.Append(';');
                        if (i + 1 < gesture.Count)
                            sb.Append($"{F(GetDouble(gesture[i]))},{F(GetDouble(gesture[i + 1]))}");
                    }
                    writer.WriteString(sb.ToString());
                    writer.WriteEndElement();
                }
            }
            writer.WriteEndElement();
        }

        // Vertices (polygon / polyline) — child element, points separated by ';'
        var verticesChild = reader is not null ? reader.Resolve(annot.Dict.Get("Vertices")) : annot.Dict.Get("Vertices");
        if (verticesChild is PdfArray vca && vca.Count >= 2)
        {
            var vsb = new StringBuilder();
            for (int i = 0; i + 1 < vca.Count; i += 2)
            {
                if (i > 0) vsb.Append(';');
                vsb.Append($"{F(GetDouble(vca[i]))},{F(GetDouble(vca[i + 1]))}");
            }
            writer.WriteStartElement("vertices");
            writer.WriteString(vsb.ToString());
            writer.WriteEndElement();
        }

        // Default style (/DS — free text), before contents-richtext per XFDF order
        var dsObj = annot.Dict.Get("DS");
        if (dsObj is PdfString dsStr)
        {
            writer.WriteStartElement("defaultstyle");
            writer.WriteString(dsStr.ToText());
            writer.WriteEndElement();
        }

        // File-attachment embedded file (/FS/EF/F) as a <data> child.
        if (annot.AnnotationType == AnnotationType.FileAttachment && reader is not null)
        {
            var efd = reader.ResolveDict(reader.ResolveDict(annot.Dict.Get("FS"))?.Get("EF"));
            var efStream = efd is null ? null : reader.ResolveStream(efd.Get("F"));
            if (efStream is not null) WriteDataElement(writer, reader, efStream);
        }

        // Sound (/Sound stream) as a <data> child.
        if (annot.AnnotationType == AnnotationType.Sound && reader is not null)
        {
            var soundStream = reader.ResolveStream(annot.Dict.Get("Sound"));
            if (soundStream is not null) WriteDataElement(writer, reader, soundStream);
        }

        // Contents — emitted for the round-trip export (so /Contents survives),
        // omitted by WriteXfdf which carries the text only via contents-richtext.
        if (writeContents && annot.Contents is not null)
        {
            writer.WriteStartElement("contents");
            writer.WriteString(annot.Contents);
            writer.WriteEndElement();
        }

        // Rich text content (RC → contents-richtext)
        var rcObj = annot.Dict.Get("RC");
        if (rcObj is PdfString rcStr)
        {
            var rcText = rcStr.ToText();
            // Strip XML declaration if present — it can't appear inside another XML document
            if (rcText.StartsWith("<?xml", StringComparison.Ordinal))
            {
                int end = rcText.IndexOf("?>", StringComparison.Ordinal);
                if (end >= 0)
                    rcText = rcText.Substring(end + 2).TrimStart();
            }
            // Some XFDF producers normalise whitespace before a tag close
            // (" >") and after the xfa:spec attribute; the round-trip export
            // preserves the source verbatim.
            if (normalizeRichText)
                rcText = rcText.Replace(" >", ">").Replace("\"2.0.2\"  ", "\"2.0.2\" ");
            writer.WriteStartElement("contents-richtext");
            writer.WriteRaw(rcText);
            writer.WriteEndElement();
        }

        // Popup child (may be an indirect reference)
        var popupObj = annot.Dict.Get("Popup");
        var popup = popupObj as PdfDictionary
            ?? (reader is not null ? reader.ResolveDict(popupObj) : null);
        if (popup is not null)
        {
            writer.WriteStartElement("popup");
            var prObj = popup.Get("Rect");
            if (prObj is PdfArray pr && pr.Count >= 4)
                writer.WriteAttributeString("rect",
                    $"{F(GetDouble(pr[0]))},{F(GetDouble(pr[1]))},{F(GetDouble(pr[2]))},{F(GetDouble(pr[3]))}");

            var pf = popup.Get("F");
            if (pf is PdfInteger pfi && pfi.Value != 0)
                writer.WriteAttributeString("flags", FormatFlags((int)pfi.Value));

            writer.WriteAttributeString("page", zeroBasedPage.ToString(CultureInfo.InvariantCulture));

            // Open state as attribute (per XFDF spec for popup elements)
            var po = popup.Get("Open");
            if (po is PdfBoolean pob)
                writer.WriteAttributeString("open", pob.Value ? "yes" : "no");

            writer.WriteEndElement();
        }

        // Default appearance (/DA — free text), after contents-richtext per XFDF order
        var daObj = annot.Dict.Get("DA");
        if (daObj is PdfString daStr)
        {
            writer.WriteStartElement("defaultappearance");
            writer.WriteString(daStr.ToText());
            writer.WriteEndElement();
        }

        // Appearance stream (/AP /N) as a base64 <appearance> child (stamp annotations).
        if (annot.AnnotationType == AnnotationType.Stamp && reader is not null)
        {
            var apDict = reader.ResolveDict(annot.Dict.Get("AP"));
            var nStream = apDict is null ? null : reader.ResolveStream(apDict.Get("N"));
            if (nStream is not null)
            {
                var apBytes = reader.DecodeStream(nStream);
                writer.WriteStartElement("appearance");
                writer.WriteString(System.Convert.ToBase64String(apBytes));
                writer.WriteEndElement();
            }
        }

        writer.WriteEndElement(); // tag
    }

    private static void AppendAnnotationDict(Page page, PdfDictionary annotDict)
    {
        // Resolve /Annots — on many documents it is an INDIRECT reference to the
        // array, not an inline array. Without resolving, the existing annotations
        // (e.g. a page's own markup) would be mistaken for "none" and overwritten.
        // Rebuild as a direct array (existing items + the new one) so the page dict
        // is marked dirty and the full list — originals included — is written on save.
        var existing = page.Reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        var annotArray = new PdfArray();
        if (existing is not null)
            foreach (var item in existing) annotArray.Add(item);
        annotArray.Add(annotDict);
        page.Dict.Set("Annots", annotArray);
    }

    private static void AppendContentStream(Page page, byte[] streamData)
    {
        // Append a new content stream to the page
        var newStream = new PdfStream(new PdfDictionary(), streamData);
        newStream.Dict.Set("Length", new PdfInteger(streamData.Length));

        var contentsObj = page.Dict.Get("Contents");
        if (contentsObj is PdfArray contentsArr)
        {
            contentsArr.Add(newStream);
        }
        else if (contentsObj is PdfStream || contentsObj is PdfIndirectRef)
        {
            var arr = new PdfArray();
            arr.Add(contentsObj);
            arr.Add(newStream);
            page.Dict.Set("Contents", arr);
        }
        else
        {
            var arr = new PdfArray();
            arr.Add(newStream);
            page.Dict.Set("Contents", arr);
        }
    }

    /// <summary>
    /// Creates a PdfString with proper encoding: Latin1 for ASCII-only text,
    /// UTF-16BE with BOM for text containing non-Latin1 characters.
    /// </summary>
    private static PdfString MakePdfTextString(string text)
    {
        // Check if all characters fit in Latin1 (0-255)
        bool needsUnicode = false;
        foreach (char c in text)
        {
            if (c > 255) { needsUnicode = true; break; }
        }

        if (needsUnicode)
        {
            // PDF spec: UTF-16BE with BOM \xFE\xFF
            var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
            var withBom = new byte[utf16.Length + 2];
            withBom[0] = 0xFE;
            withBom[1] = 0xFF;
            Array.Copy(utf16, 0, withBom, 2, utf16.Length);
            return new PdfString(withBom);
        }

        return new PdfString(Encoding.Latin1.GetBytes(text));
    }

    private static void SetIfPresent(PdfDictionary dict, XmlNode node, string xmlAttr, string pdfKey)
    {
        // Check attribute first
        var attr = node.Attributes?[xmlAttr];
        if (attr is not null)
        {
            dict.Set(pdfKey, MakePdfTextString(attr.Value));
            return;
        }
        // Fallback: check child element (XFDF allows both forms)
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element &&
                child.LocalName.Equals(xmlAttr, StringComparison.OrdinalIgnoreCase))
            {
                dict.Set(pdfKey, MakePdfTextString(child.InnerText));
                return;
            }
        }
    }

    private static int ParseFlags(string flagsStr)
    {
        int flags = 0;
        foreach (var f in flagsStr.Split(','))
        {
            switch (f.Trim().ToLowerInvariant())
            {
                case "invisible": flags |= 1; break;
                case "hidden": flags |= 2; break;
                case "print": flags |= 4; break;
                case "nozoom": flags |= 8; break;
                case "norotate": flags |= 16; break;
                case "noview": flags |= 32; break;
                case "readonly": flags |= 64; break;
                case "locked": flags |= 128; break;
                case "togglenoview": flags |= 256; break;
                case "lockedcontents": flags |= 512; break;
            }
        }
        return flags;
    }

    private static string FormatFlags(int flags)
    {
        var parts = new List<string>();
        if ((flags & 1) != 0) parts.Add("invisible");
        if ((flags & 2) != 0) parts.Add("hidden");
        if ((flags & 4) != 0) parts.Add("print");
        if ((flags & 8) != 0) parts.Add("nozoom");
        if ((flags & 16) != 0) parts.Add("norotate");
        if ((flags & 32) != 0) parts.Add("noview");
        if ((flags & 64) != 0) parts.Add("readonly");
        if ((flags & 128) != 0) parts.Add("locked");
        if ((flags & 256) != 0) parts.Add("togglenoview");
        if ((flags & 512) != 0) parts.Add("lockedcontents");
        return string.Join(",", parts);
    }

    private static double[]? ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return null;
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return [r / 255.0, g / 255.0, b / 255.0];
    }

    /// <summary>Decode an XFDF <c>&lt;imagedata&gt;</c> payload — a base64 string
    /// optionally prefixed by a <c>data:image/...;base64,</c> URI header (and possibly
    /// wrapped in whitespace) — into raw image bytes. Returns null on empty/invalid input.</summary>
    private static byte[]? DecodeDataUriBase64(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        var comma = s.IndexOf(',');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            s = s[(comma + 1)..];
        // Strip any interior whitespace the XML pretty-printer may have inserted.
        s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        try { return Convert.FromBase64String(s); }
        catch { return null; }
    }

    private static void SetRealAttr(PdfDictionary dict, XmlNode node, string attr, string key)
    {
        var a = node.Attributes?[attr];
        if (a is not null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            dict.Set(key, new PdfReal(v));
    }

    private static double[] ParseDoubleList(string csv)
    {
        var parts = csv.Split(',');
        var result = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                result.Add(v);
        }
        return result.ToArray();
    }

    private static string? XfdfTagToSubtype(string tag) => tag switch
    {
        "text" => "Text",
        "link" => "Link",
        "freetext" => "FreeText",
        "line" => "Line",
        "square" => "Square",
        "circle" => "Circle",
        "polygon" => "Polygon",
        "polyline" => "PolyLine",
        "highlight" => "Highlight",
        "underline" => "Underline",
        "squiggly" => "Squiggly",
        "strikeout" => "StrikeOut",
        "stamp" => "Stamp",
        "caret" => "Caret",
        "ink" => "Ink",
        "popup" => "Popup",
        "fileattachment" => "FileAttachment",
        "sound" => "Sound",
        "movie" => "Movie",
        "widget" => "Widget",
        "screen" => "Screen",
        "printermark" => "PrinterMark",
        "trapnet" => "TrapNet",
        "watermark" => "Watermark",
        "3d" => "3D",
        "redact" => "Redact",
        "richmedia" => "RichMedia",
        _ => null,
    };

    private static string AnnotationTypeToXfdfTag(AnnotationType type) => type switch
    {
        AnnotationType.Text => "text",
        AnnotationType.Link => "link",
        AnnotationType.FreeText => "freetext",
        AnnotationType.Line => "line",
        AnnotationType.Square => "square",
        AnnotationType.Circle => "circle",
        AnnotationType.Polygon => "polygon",
        AnnotationType.PolyLine => "polyline",
        AnnotationType.Highlight => "highlight",
        AnnotationType.Underline => "underline",
        AnnotationType.Squiggly => "squiggly",
        AnnotationType.StrikeOut => "strikeout",
        AnnotationType.Stamp => "stamp",
        AnnotationType.Caret => "caret",
        AnnotationType.Ink => "ink",
        AnnotationType.Popup => "popup",
        AnnotationType.FileAttachment => "fileattachment",
        AnnotationType.Sound => "sound",
        AnnotationType.Movie => "movie",
        AnnotationType.Widget => "widget",
        AnnotationType.Screen => "screen",
        AnnotationType.PrinterMark => "printermark",
        AnnotationType.TrapNet => "trapnet",
        AnnotationType.Watermark => "watermark",
        AnnotationType.ThreeD => "3d",
        AnnotationType.Redact => "redact",
        AnnotationType.RichMedia => "richmedia",
        _ => "unknown",
    };

    private static double GetDouble(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    private static string FormatPdfDate(System.DateTime dt)
        => "D:" + dt.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    /// <summary>Write an embedded-stream payload as an XFDF &lt;data&gt; child:
    /// printable content is emitted filtered/ascii (decoded text); binary content
    /// is emitted raw/hex (the original encoded bytes). The original /Length and
    /// /Filter are recorded as attributes.</summary>
    private static void WriteDataElement(XmlWriter writer, IO.PdfReader reader, PdfStream stream)
    {
        var raw = stream.RawData ?? Array.Empty<byte>();
        var decoded = reader.DecodeStream(stream) ?? raw;
        long length = stream.Dict.Get("Length") is PdfInteger li ? li.Value : raw.Length;
        var filterName = stream.Dict.GetName("Filter");
        bool isAscii = decoded.Length > 0
            && Array.TrueForAll(decoded, b => b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126));
        writer.WriteStartElement("data");
        writer.WriteAttributeString("mode", isAscii ? "filtered" : "raw");
        writer.WriteAttributeString("encoding", isAscii ? "ascii" : "hex");
        writer.WriteAttributeString("length", length.ToString(CultureInfo.InvariantCulture));
        if (filterName is not null) writer.WriteAttributeString("filter", filterName);
        // Raw/hex payloads carry a single leading space (XFDF convention);
        // filtered/ascii payloads are written verbatim.
        writer.WriteString(isAscii ? Encoding.ASCII.GetString(decoded) : " " + ToHex(raw));
        writer.WriteEndElement();
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    private static byte[] FromHex(string hex)
    {
        hex = hex.Trim();
        if (hex.Length < 2) return Array.Empty<byte>();
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    // ── API-shape additions ───────────────────────────────────────────────

    /// <summary>Export every annotation in the bound document to an XFDF stream.</summary>
    public void ExportAnnotationsToXfdf(Stream xmlOutputStream)
    {
        var doc = Document;
        var allTypes = (AnnotationType[])System.Enum.GetValues(typeof(AnnotationType));
        ExportAnnotationsXfdf(xmlOutputStream, start: 1, end: doc.PageCount, allTypes);
    }

    /// <summary>Export annotations filtered by Subtype name strings.</summary>
    public void ExportAnnotationsXfdf(Stream xmlOutputStream, int start, int end, string[] annotTypes)
    {
        var enumTypes = MapStringToAnnotationTypes(annotTypes);
        ExportAnnotationsXfdf(xmlOutputStream, start, end, enumTypes);
    }

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
        // When ApplyRedactions is requested, redaction annotations must not merely be
        // painted into the page (an opaque overlay still leaves the text extractable):
        // each one applies its redaction first — physically removing the underlying
        // text and any form fields under the rect — before the regular
        // flatten drops the now-empty annotation from /Annots.
        if (flattenSettings is { ApplyRedactions: true })
        {
            var doc = Document;
            foreach (var page in doc.Pages)
            {
                var redactions = new List<Aspose.Pdf.Annotations.RedactionAnnotation>();
                foreach (var ann in page.Annotations)
                    if (ann is Aspose.Pdf.Annotations.RedactionAnnotation ra)
                        redactions.Add(ra);
                foreach (var ra in redactions) ra.Redact();
            }
        }
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

    /// <summary>Import annotations from an FDF file. FDF annotation import is not yet implemented.</summary>
    public void ImportAnnotationsFromFdf(string fdfFile)
    {
        _ = fdfFile;
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
