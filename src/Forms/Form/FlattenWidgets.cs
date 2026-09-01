using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>Build the page-content fragment that folds a widget annotation's /AP /N appearance
    /// into the page, registering it as the XForm <paramref name="frmName"/>. Resolves the current
    /// state for a multi-state (/AS) appearance, places the appearance per PDF 32000-1 §12.5.5
    /// (transform /BBox by /Matrix → axis-aligned bounds → map onto /Rect, leaving the form's own
    /// /Matrix on the XObject), and returns the <c>q … cm /FRMn Do Q</c> string. Null when there
    /// is no usable appearance or rectangle.</summary>
    private static string? BuildWidgetFlattenFragment(
        PdfDictionary pageDict, PdfDictionary annotDict, PdfReader reader, string frmName)
    {
        var apDict = reader.ResolveDict(annotDict.Get("AP"));
        if (apDict is null) return null;
        // Appearance selection for a flatten is STRICTLY state-driven (probed against the
        // reference flatten): when the widget carries an /AS, only /N[/AS] counts — a state
        // the appearance dictionary does not define draws NOTHING and the widget is skipped
        // (an unselected checkbox whose /AP/N holds only its on-state is the common case).
        // A state dictionary with no /AS to select from likewise draws nothing; only a bare
        // /N stream is used unconditionally. There is no "first non-Off state" fallback —
        // that fallback rendered unselected boxes as selected.
        var nResolved = reader.Resolve(apDict.Get("N"));
        PdfStream? appearanceStream;
        var asName = annotDict.GetName("AS");
        if (asName is not null)
            appearanceStream = nResolved is PdfDictionary asStates
                ? reader.ResolveStream(asStates.Get(asName))
                : null;
        else
            appearanceStream = nResolved as PdfStream;
        if (appearanceStream is null) return null;

        if (reader.Resolve(annotDict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4)
            return null;
        var rect = Rectangle.FromPdfArray(rectArr);

        double tllx = 0, tlly = 0, tw = rect.Width, th = rect.Height;
        if (reader.Resolve(appearanceStream.Dict.Get("BBox")) is PdfArray bboxArr && bboxArr.Count >= 4)
        {
            var bbox = Rectangle.FromPdfArray(bboxArr);
            double[] m = ReadAppearanceMatrix(appearanceStream.Dict, reader);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (px, py) in new[] { (bbox.LLX, bbox.LLY), (bbox.URX, bbox.LLY), (bbox.URX, bbox.URY), (bbox.LLX, bbox.URY) })
            {
                double qx = m[0] * px + m[2] * py + m[4];
                double qy = m[1] * px + m[3] * py + m[5];
                if (qx < minX) minX = qx; if (qx > maxX) maxX = qx;
                if (qy < minY) minY = qy; if (qy > maxY) maxY = qy;
            }
            tllx = minX; tlly = minY; tw = maxX - minX; th = maxY - minY;
        }

        var sx = tw > 0 ? rect.Width / tw : 1.0;
        var sy = th > 0 ? rect.Height / th : 1.0;
        var tx = rect.LLX - tllx * sx;
        var ty = rect.LLY - tlly * sy;
        var xformName = RegisterAppearanceAsXForm(pageDict, appearanceStream, reader, frmName);
        return $"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{xformName} Do\nQ\n";
    }

    /// <summary>Flatten a single field: fold each of its widget annotations into the owning page's
    /// content (as an FRM XObject placed at the widget /Rect), remove those widgets from the page
    /// /Annots, and drop the field from the AcroForm. Used by <see cref="Field.Flatten()"/>.</summary>
    internal void FlattenField(Field field)
    {
        var reader = _reader ?? OwnerDocument?.Reader;
        var doc = OwnerDocument;
        if (reader is null || doc is null) return;

        // The widget dicts this field contributes: a merged single-widget field is its own dict;
        // otherwise each kid widget.
        var widgets = new List<PdfDictionary>();
        if (field.Dict.ContainsKey("Rect")) widgets.Add(field.Dict);
        foreach (var kid in field.AllKids())
            if (kid.ContainsKey("Rect") && !ReferenceEquals(kid, field.Dict)) widgets.Add(kid);
        if (widgets.Count == 0) return;

        // Refresh the appearance unless it carries a non-identity /Matrix (see
        // RegenerateAppearanceForFlatten).
        RegenerateAppearanceForFlatten(field);

        foreach (var page in doc.Pages)
        {
            if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            var append = new System.IO.MemoryStream();
            var remaining = new PdfArray();
            int frm = NextFreeFrmIndex(page.Dict, reader);
            var writer = new System.IO.StreamWriter(append, System.Text.Encoding.ASCII, leaveOpen: true);
            bool changed = false;
            foreach (var annotRef in annots)
            {
                var annotDict = reader.ResolveDict(annotRef);
                if (annotDict is not null && widgets.Exists(w => ReferenceEquals(w, annotDict)))
                {
                    var frag = BuildWidgetFlattenFragment(page.Dict, annotDict, reader, $"FRM{frm}");
                    if (frag is not null) { writer.Write(frag); frm++; changed = true; continue; }
                }
                remaining.Add(annotRef);
            }
            writer.Flush();
            if (!changed) continue;
            if (remaining.Count > 0) page.Dict.Set("Annots", remaining); else page.Dict.Remove("Annots");
            if (append.Length > 0)
            {
                var existing = page.GetContentStreamBytes() ?? [];
                var combined = new byte[existing.Length + (existing.Length > 0 ? 1 : 0) + append.Length];
                existing.CopyTo(combined, 0);
                if (existing.Length > 0) combined[existing.Length] = (byte)'\n';
                append.ToArray().CopyTo(combined, existing.Length + (existing.Length > 0 ? 1 : 0));
                page.SetContentStream(combined);
            }
        }

        // Drop the field from the AcroForm /Fields and the cached list.
        var acro = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (reader.Resolve(acro?.Get("Fields")) is PdfArray fields)
        {
            var kept = new PdfArray();
            foreach (var fr in fields)
                if (!ReferenceEquals(reader.ResolveDict(fr), field.Dict)) kept.Add(fr);
            acro!.Set("Fields", kept);
        }
        _fields.Remove(field);
    }

    /// <summary>The next unused FRM{n} index in the page's XObject resources (so a single-field
    /// flatten doesn't collide with already-registered forms).</summary>
    private static int NextFreeFrmIndex(PdfDictionary pageDict, PdfReader reader)
    {
        var res = reader.ResolveDict(pageDict.Get("Resources"));
        var xobj = res is null ? null : reader.ResolveDict(res.Get("XObject"));
        int n = 0;
        if (xobj is not null)
            while (xobj.ContainsKey("FRM" + n)) n++;
        return n;
    }

    /// <summary>
    /// Register an appearance stream as a named XForm in the page's XObject resources.
    /// Returns the assigned name (e.g., "FRM0", "FRM1"). Shared with annotation
    /// flatten (Annotation.Flatten) so both code paths use the same naming.
    /// </summary>
    /// <summary>Read an appearance stream's /Matrix entry as [a b c d e f], defaulting to the
    /// identity matrix when absent or malformed.</summary>
    private static double[] ReadAppearanceMatrix(PdfDictionary apDict, PdfReader reader)
    {
        var m = new double[] { 1, 0, 0, 1, 0, 0 };
        if (reader.Resolve(apDict.Get("Matrix")) is PdfArray arr && arr.Count >= 6)
            for (int i = 0; i < 6; i++)
                m[i] = arr[i] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => m[i] };
        return m;
    }

    internal static string RegisterAppearanceAsXForm(
        PdfDictionary pageDict, PdfStream appearanceStream, PdfReader reader, string? preferredName = null)
    {
        var resources = EnsureOwnPageResources(pageDict, reader);

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }

        // Use the caller's preferred name when free (field flatten assigns 1-based FRM{n}
        // in /Annots order); otherwise generate a unique FRM0, FRM1, … (annotation flatten).
        string name;
        if (!string.IsNullOrEmpty(preferredName) && !xobjects.ContainsKey(preferredName!))
        {
            name = preferredName!;
        }
        else
        {
            int idx = 0;
            do
            {
                name = $"FRM{idx}";
                idx++;
            } while (xobjects.ContainsKey(name));
        }

        // Ensure the appearance stream has /Type /XObject and /Subtype /Form
        appearanceStream.Dict.Set("Type", new PdfName("XObject"));
        appearanceStream.Dict.Set("Subtype", new PdfName("Form"));

        xobjects.Set(name, appearanceStream);
        return name;
    }

    internal static void MergeAnnotResources(PdfDictionary pageDict, PdfDictionary apDict, PdfReader reader)
    {
        var apResources = reader.ResolveDict(apDict.Get("Resources"));
        if (apResources is null) return;

        var pageResources = EnsureOwnPageResources(pageDict, reader);

        // Merge each resource category (Font, XObject, ExtGState, etc.)
        foreach (var category in apResources.Keys)
        {
            var apCatDict = reader.ResolveDict(apResources.Get(category));
            if (apCatDict is null) continue;

            var pageCatDict = reader.ResolveDict(pageResources.Get(category));
            if (pageCatDict is null)
            {
                pageCatDict = new PdfDictionary();
                pageResources.Set(category, pageCatDict);
            }

            foreach (var key in apCatDict.Keys)
            {
                if (!pageCatDict.ContainsKey(key))
                {
                    var val = apCatDict.Get(key);
                    if (val is not null) pageCatDict.Set(key, val);
                }
            }
        }
    }
}
