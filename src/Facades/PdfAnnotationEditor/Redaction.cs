using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>Flattening a redaction annotation APPLIES it: the mark says the
    /// content beneath it must go, so the text under the rect is physically removed
    /// before the annotation is stamped and dropped. Painting the box alone would
    /// leave every redacted word still extractable (the expected result:
    /// an imported XFDF redact, flattened, no longer yields its text).</summary>
    private static void ApplyPendingRedactions(Document doc)
    {
        foreach (var page in doc.Pages)
        {
            List<Aspose.Pdf.Annotations.RedactionAnnotation>? redactions = null;
            foreach (var ann in page.Annotations)
                if (ann is Aspose.Pdf.Annotations.RedactionAnnotation ra)
                    (redactions ??= []).Add(ra);
            if (redactions is null) continue;
            foreach (var ra in redactions) ra.Redact();
        }
    }

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
        // White unless a colour is given.
        var fill = color is { Length: >= 3 }
            ? Color.FromRgb(Compat.Clamp(color[0], 0, 1), Compat.Clamp(color[1], 0, 1), Compat.Clamp(color[2], 0, 1))
            : Color.White;

        // Remove for good what the page paints in the area - glyphs, the parts of paths there,
        // and image pixels there, which take the fill colour - before the cover is drawn over it.
        Aspose.Pdf.Annotations.ContentRedactor.Apply(page, rect, fill);
        Aspose.Pdf.Annotations.RedactionAnnotation.MustRewriteWhole(page);

        var sb = new StringBuilder();
        sb.Append("q ");
        sb.Append($"{F(fill.R / 255.0)} {F(fill.G / 255.0)} {F(fill.B / 255.0)} rg ");
        sb.Append($"{F(rect.LLX)} {F(rect.LLY)} {F(rect.Width)} {F(rect.Height)} re f Q");
        AppendContentStream(page, Compat.Latin1.GetBytes(sb.ToString()));

        // Redaction removes form-field widgets covered by the area (they are no longer
        // visible/usable), pruning them from the page /Annots and the AcroForm /Fields.
        RemoveWidgetsInArea(doc, page, rect);
        Aspose.Pdf.Annotations.RedactionAnnotation.RemoveAnnotationsUnder(page, rect);
    }

    /// <summary>Remove every Widget annotation whose /Rect intersects <paramref name="area"/>
    /// from the page and, for those that are (or become) empty form fields, from the
    /// AcroForm /Fields tree — a redaction drops the fields under it.</summary>
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
}
