using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    /// <summary>Flatten honouring <paramref name="settings"/>. <paramref name="frmStartIndex"/>
    /// bases the flattened-field FRM{n} numbering (0 for document/form flatten, 1 for the facade
    /// FlattenAllFields). <paramref name="flattenNonWidgets"/> also folds non-widget annotations
    /// (e.g. FreeText) into the page content so their FRM index lines up with /Annots — the facade
    /// FlattenAllFields does this; document/form flatten leaves them for the annotation path.</summary>
    internal void Flatten(Document document, FlattenSettings? settings, int frmStartIndex,
        bool flattenNonWidgets, bool skipInvisible = false, bool keepAcroFormDict = false)
    {
        // 0. A DYNAMIC XFA form (no AcroForm widgets — its fields live only in the
        //    XFA template) must first be folded to a standard AcroForm: paint the
        //    form onto real pages (replacing the viewer placeholder page) and
        //    materialise flat fields. A static XFA form keeps its widget path.
        if (IsXfa)
        {
            var xfaAcro = document.Reader.ResolveDict(document.Catalog.Get("AcroForm"));
            var xfaFields = xfaAcro is null ? null : document.Reader.Resolve(xfaAcro.Get("Fields")) as PdfArray;
            if (xfaFields is null || xfaFields.Count == 0)
                FlattenXfa();
        }

        // 1. Force each field's appearance to reflect the current value.
        //    The per-type GenerateAppearance short-circuits when /AP is present,
        //    so for text/choice/check/button/radio fields we delete the existing
        //    /AP and re-emit — that's the only way to capture an updated value.
        var refresh = settings is null || settings.UpdateAppearances;
        if (refresh)
        {
            // Multiple Field wrappers can share the same underlying PdfDictionary
            // when _fields collects a field both directly from /AcroForm/Fields
            // AND through a kid-widget walk that points back at the same dict.
            // Regenerating twice on the same dict can clobber a successful
            // first regen if the second regen (with a stale /V or zero-size
            // /Rect on a duplicate wrapper) early-exits after the
            // Dict.Remove("AP") step. Dedupe by dict-identity so each
            // dictionary's /AP is generated exactly once.
            var seenDicts = new System.Collections.Generic.HashSet<PdfDictionary>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var f in _fields)
            {
                if (!seenDicts.Add(f.Dict)) continue;
                RegenerateAppearanceForFlatten(f);
            }
        }

        // 2. Hoist /AcroForm/DR fonts into each page's /Resources so the
        //    appearance Form-XObject's content stream (which references fonts by
        //    AcroForm-DR alias like /Helv) still resolves after step 4 strips
        //    the /AcroForm dict. /AP streams without their own /Resources
        //    fall back to the page's resources per PDF 32000-2 § 8.10.
        var acroForm = document.Reader.ResolveDict(document.Catalog.Get("AcroForm"));
        var drFonts = document.Reader.ResolveDict(document.Reader.ResolveDict(acroForm?.Get("DR"))?.Get("Font"));
        if (drFonts is not null)
        {
            foreach (var page in document.Pages)
                HoistDrFontsIntoPageResources(page, drFonts, document.Reader);
        }

        var hideButtons = settings is { HideButtons: true };
        // The PDF/A flatten stamps only widgets REACHABLE from the AcroForm /Fields
        // tree (field dicts + their /Kids). An orphan widget annotation (left behind
        // by a merge that deduplicated its field entry) gets no page-content fragment
        // — PDF/A output does not stamp orphan widgets. The same set also
        // dedupes a widget dict shared by several pages' /Annots.
        System.Collections.Generic.HashSet<PdfDictionary>? fieldWidgets = null;
        if (skipInvisible)
        {
            fieldWidgets = new System.Collections.Generic.HashSet<PdfDictionary>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
            void CollectFieldDicts(PdfArray arr, int depth)
            {
                if (depth > 16) return;
                foreach (var o in arr)
                {
                    if (document.Reader.ResolveDict(o) is not { } fd) continue;
                    fieldWidgets.Add(fd);
                    if (document.Reader.Resolve(fd.Get("Kids")) is PdfArray ka)
                        CollectFieldDicts(ka, depth + 1);
                }
            }
            if (document.Reader.Resolve(acroForm?.Get("Fields")) is PdfArray topFields)
                CollectFieldDicts(topFields, 0);
            // A document whose form lives only in page widgets (no /Fields entries)
            // has nothing to anchor the orphan filter — leave it inactive.
            if (fieldWidgets.Count == 0) fieldWidgets = null;
        }
        foreach (var page in document.Pages)
        {
            // The facade flatten (flattenNonWidgets) consumes the page's WHOLE
            // /Annots — probed: a sticky note and a highlight leave with the
            // fields — so annotations carrying no /AP first get the appearance a
            // viewer would synthesise (the same materialisation the save pass
            // runs), and whatever still has none is dropped rather than kept.
            if (flattenNonWidgets)
                foreach (var ann in page.Annotations)
                {
                    if (document.Reader.ResolveDict(ann.Dict.Get("AP")) is not null) continue;
                    var st = ann.Dict.GetName("Subtype");
                    if (st is "Widget" or "Popup" or "Link" or null) continue;
                    // A hidden annotation (/F bit 2) leaves without ink.
                    if (((int)ann.Dict.GetInt("F") & 2) != 0) continue;
                    try
                    {
                        // The synthesised set, op-measured: FreeText
                        // writes its /DA text; a highlight fills its quad boxes under
                        // its /CA; a strikeout is one rect-mid line; notes, shapes,
                        // ink and stamps draw themselves. An UNDERLINE or SQUIGGLY
                        // with no /AP draws NOTHING (both are dropped, with
                        // or without quads), and so do carets and file attachments.
                        if (ann is Aspose.Pdf.Annotations.FreeTextAnnotation freeText)
                            freeText.GenerateAppearance();
                        else if (ann is Aspose.Pdf.Annotations.LineAnnotation
                                     or Aspose.Pdf.Annotations.PolygonAnnotation
                                     or Aspose.Pdf.Annotations.PolylineAnnotation
                                     or Aspose.Pdf.Annotations.SquareAnnotation
                                     or Aspose.Pdf.Annotations.CircleAnnotation
                                     or Aspose.Pdf.Annotations.TextAnnotation
                                     or Aspose.Pdf.Annotations.InkAnnotation
                                     or Aspose.Pdf.Annotations.HighlightAnnotation
                                     or Aspose.Pdf.Annotations.StrikeOutAnnotation
                                     or Aspose.Pdf.Annotations.StampAnnotation)
                            ann.UpdateAppearances();
                    }
                    catch { /* an unsynthesisable appearance leaves the annotation to drop below */ }
                }
            FlattenFieldsOnPage(page, hideButtons, frmStartIndex, flattenNonWidgets, skipInvisible,
                fieldWidgets, dropUnstamped: flattenNonWidgets);
        }

        // Remove AcroForm from catalog (the PDF/A flatten keeps the dict — emptied
        // below — so its /DR fonts survive for DefaultResources readers).
        if (!keepAcroFormDict)
            document.Catalog.Remove("AcroForm");

        // Empty the /Fields array on the (now-detached) AcroForm dict too: Count reads the
        // AcroForm's /Fields (this Form's cached _acroForm, or the catalog's) before falling
        // back to _fields, so without this a flattened form still reports its old field count.
        // The PDF/A flatten keeps VISIBLE signature fields (their widgets stayed in
        // /Annots above); everything else is dropped.
        foreach (var af in new[] { acroForm, _acroForm })
        {
            if (af is null || !af.ContainsKey("Fields")) continue;
            var keptFields = new PdfArray();
            if (keepAcroFormDict && document.Reader.Resolve(af.Get("Fields")) is PdfArray oldFields)
            {
                foreach (var fRef in oldFields)
                {
                    var fd = document.Reader.ResolveDict(fRef);
                    if (fd?.GetName("FT") == "Sig" && HasVisibleWidget(fd, document.Reader))
                        keptFields.Add(fRef);
                }
            }
            af.Set("Fields", keptFields);
        }

        // Clear cached field list so Count reflects the flattened state
        _fields.Clear();
    }

    /// <summary>Flatten ONE page: fold every annotation on it that has (or can be given) an
    /// appearance into the page content as an FRM XObject, drop those annotations, and retire
    /// the form fields the page carried. A merged field+widget dict leaves the AcroForm /Fields
    /// array with it; a parent field whose widgets live on several pages survives, minus the
    /// kids that were on this page. The AcroForm itself stays — other pages may still have
    /// fields on it.</summary>
    internal void FlattenSinglePage(Document document, Page page)
    {
        var reader = document.Reader;
        var acroForm = reader.ResolveDict(document.Catalog.Get("AcroForm"));

        // Which field dicts sit on THIS page — captured before the stamping pass empties
        // /Annots, so the /Fields cleanup below knows what left with the page.
        var onThisPage = new System.Collections.Generic.HashSet<PdfDictionary>(
            System.Collections.Generic.ReferenceEqualityComparer.Instance);
        if (reader.Resolve(page.Dict.Get("Annots")) is PdfArray pageAnnots)
            foreach (var a in pageAnnots)
                if (reader.ResolveDict(a) is { } ad) onThisPage.Add(ad);

        // Give the annotations that carry NO appearance the one a viewer would synthesise —
        // shapes and notes draw themselves, and a field whose dict IS the widget (no
        // /Parent above it) emits its value. A widget reached through a parent's /Kids is
        // deliberately left alone: it has no generation path and simply draws nothing.
        // An annotation that already has an /AP keeps it verbatim.
        foreach (var annot in page.Annotations)
        {
            if (reader.ResolveDict(annot.Dict.Get("AP")) is not null) continue;
            var isWidget = annot.Dict.GetName("Subtype") == "Widget"
                || annot.Dict.ContainsKey("FT") || annot.Dict.ContainsKey("T");
            if (isWidget)
            {
                var widgetField = Field.Create(annot.Dict, reader);
                // A widget reached through a parent's /Kids only draws when its field
                // has a value; a valueless one paints nothing and leaves with the page.
                if (annot.Dict.ContainsKey("Parent") && string.IsNullOrEmpty(widgetField.Value))
                    continue;
                RegenerateAppearanceForFlatten(widgetField);
            }
            else if (annot is Aspose.Pdf.Annotations.SquareAnnotation
                            or Aspose.Pdf.Annotations.CircleAnnotation
                            or Aspose.Pdf.Annotations.TextAnnotation
                            or Aspose.Pdf.Annotations.InkAnnotation)
            {
                annot.UpdateAppearances();
            }
        }
        FlattenFieldsOnPage(page, flattenNonWidgets: true, dropUnstamped: true);

        // Retire the fields that lived on this page.
        foreach (var af in new[] { acroForm, _acroForm })
        {
            if (af is null || reader.Resolve(af.Get("Fields")) is not PdfArray fields) continue;
            var kept = new PdfArray();
            foreach (var fRef in fields)
            {
                var fd = reader.ResolveDict(fRef);
                if (fd is null) continue;
                // A merged field+widget that was on this page goes with it.
                if (onThisPage.Contains(fd)) continue;
                // A parent field keeps only the kids that were NOT on this page; it
                // survives as long as at least one remains.
                if (reader.Resolve(fd.Get("Kids")) is PdfArray kids)
                {
                    var keptKids = new PdfArray();
                    foreach (var k in kids)
                        if (reader.ResolveDict(k) is { } kd && !onThisPage.Contains(kd))
                            keptKids.Add(k);
                    if (keptKids.Count == 0) continue;
                    fd.Set("Kids", keptKids);
                }
                kept.Add(fRef);
            }
            af.Set("Fields", kept);
        }
        _fields.Clear();
    }

    /// <summary>True when the field's own dict or any of its widget kids is a
    /// non-hidden annotation (F bit 2 clear).</summary>
    private static bool HasVisibleWidget(PdfDictionary fieldDict, Aspose.Pdf.IO.PdfReader reader)
    {
        static bool Visible(PdfDictionary d)
        {
            var f = d.GetInt("F");
            return (f & 2) == 0;
        }
        if (fieldDict.ContainsKey("Rect") && Visible(fieldDict)) return true;
        if (reader.Resolve(fieldDict.Get("Kids")) is PdfArray kids)
            foreach (var k in kids)
                if (reader.ResolveDict(k) is { } kd && kd.ContainsKey("Rect") && Visible(kd))
                    return true;
        return false;
    }

    /// <summary>Re-emit the field's /AP/N based on its current /V by dropping
    /// the existing /AP and re-invoking the per-type generator. Each generator
    /// short-circuits when /AP is present so the value-driven appearance was
    /// only ever written on initial Form.Add (opening a PDF whose
    /// fields already have stale /AP from a prior save and flattening shipped
    /// the stale visuals).
    ///
    /// Caller is expected to dedupe by dict identity since one Field wrapper
    /// commonly shares its underlying PdfDictionary with another wrapper
    /// reached through a Kids walk. Removing /AP unconditionally on every
    /// _fields entry without dedupe would clobber a successfully-regenerated
    /// dict when its second wrapper is processed.</summary>
    private static void RegenerateAppearanceForFlatten(Field field)
    {
        // A parent field carrying only {V, Kids, T, FT} has no /Rect of its own — its
        // visuals live on the widget KIDS. An appearance is synthesised for those kids
        // only when the field actually HAS something to paint: a field with a value
        // gets it drawn at each kid's rect, while a VALUELESS field's AP-less kids draw
        // nothing at all and the flatten drops them (that is what keeps an empty
        // multi-widget field from stamping blank boxes). Kids that already carry an /AP
        // keep it verbatim, so a checkbox group's Off-state kids never gain checked
        // visuals.
        if (!field.Dict.ContainsKey("Rect")
            && field.Reader.Resolve(field.Dict.Get("Kids")) is PdfArray fieldKids)
        {
            if (string.IsNullOrEmpty(field.Value)) return;
            foreach (var kidObj in fieldKids)
            {
                var kidDict = field.Reader.ResolveDict(kidObj);
                if (kidDict is null || !kidDict.ContainsKey("Rect")) continue;
                if (kidDict.ContainsKey("T")) continue;   // nested field, not a pure widget
                if (kidDict.ContainsKey("AP")) continue;  // authored appearance wins
                RegenerateAppearanceForFlatten(Field.Create(kidDict, field.Reader));
            }
            return;
        }
        var oldAp = field.Dict.Get("AP");
        // A STATE-BASED appearance (/AP /N is a dictionary of named states, as a checkbox
        // or radio carries) is never rebuilt at flatten time: the authored states are the
        // truth, and the widget draws whichever one /AS selects — or nothing at all when
        // /AS names a state the dictionary does not define. Re-emitting would invent an
        // Off state the author never provided and stamp a box that should stay blank.
        if (field.Reader.Resolve(field.Reader.ResolveDict(oldAp)?.Get("N")) is PdfDictionary)
            return;
        // Preserve an existing appearance that carries a non-identity /Matrix (e.g. a field on a
        // /Rotate page): the per-type generator re-emits appearances axis-aligned, which would drop
        // the /Matrix and mis-place the flattened form (and can split the value's words). The
        // existing /AP already reflects the value, so keep it verbatim.
        if (HasNonIdentityAppearanceMatrix(field, oldAp)) return;
        // Drop and re-emit so the appearance reflects the current value. If the per-type
        // generator can't produce a new /AP (e.g. an Off checkbox / button it doesn't
        // synthesise), keep the original so flatten can still render it.
        field.Dict.Remove("AP");
        field.GenerateAppearance();
        if (field.Dict.Get("AP") is null && oldAp is not null)
            field.Dict.Set("AP", oldAp);
    }

    /// <summary>True when the field's /AP /N appearance carries a /Matrix that isn't the identity
    /// (a rotated/skewed/scaled appearance, e.g. a widget on a /Rotate page).</summary>
    private static bool HasNonIdentityAppearanceMatrix(Field field, PdfObject? apObj)
    {
        var reader = field.Reader;
        var ap = reader.ResolveDict(apObj);
        if (ap is null) return false;
        var n = reader.Resolve(ap.Get("N"));
        var ns = n as PdfStream;
        if (ns is null && n is PdfDictionary states)
            foreach (var k in states.Keys) { ns = reader.ResolveStream(states.Get(k)); if (ns is not null) break; }
        if (ns is null || reader.Resolve(ns.Dict.Get("Matrix")) is not PdfArray)
            return false;
        var m = ReadAppearanceMatrix(ns.Dict, reader);
        return System.Math.Abs(m[0] - 1) > 1e-6 || System.Math.Abs(m[3] - 1) > 1e-6
            || System.Math.Abs(m[1]) > 1e-6 || System.Math.Abs(m[2]) > 1e-6;
    }

    /// <summary>Copy every font entry from the AcroForm /DR /Font dict into the
    /// page's /Resources/Font (without overwriting an existing entry of the
    /// same alias). Lets the appearance Form-XObject /Do reference resolve
    /// against page resources after AcroForm is stripped at flatten end.</summary>
    private static void HoistDrFontsIntoPageResources(Page page, PdfDictionary drFonts, PdfReader reader)
    {
        var pageResources = EnsureOwnPageResources(page.Dict, reader);
        var pageFonts = reader.ResolveDict(pageResources.Get("Font"));
        if (pageFonts is null)
        {
            pageFonts = new PdfDictionary();
            pageResources.Set("Font", pageFonts);
        }
        foreach (var alias in drFonts.Keys)
        {
            if (pageFonts.ContainsKey(alias)) continue;
            var entry = drFonts.Get(alias);
            if (entry is not null) pageFonts.Set(alias, entry);
        }
    }

    /// <summary>Return the page's OWN /Resources, creating it seeded from the inherited
    /// resources when the page carries none (PDF 32000-1 §7.7.3.4 — /Resources is an
    /// inheritable page attribute). A page that inherits its resources has no
    /// /Resources key of its own; giving it a fresh EMPTY dict here would SHADOW the
    /// inherited fonts and XObjects, so its existing text renders as fallback-font
    /// garbage and its images vanish. The seed shallow-clones each dictionary category
    /// (Font, XObject, ExtGState, …) so a later page-local edit (adding a flattened
    /// FRM XObject, a DR font, a merged appearance resource) does not mutate the
    /// dictionary shared with the /Pages node and its sibling pages.</summary>
    internal static PdfDictionary EnsureOwnPageResources(PdfDictionary pageDict, PdfReader reader)
    {
        var res = reader.ResolveDict(pageDict.Get("Resources"));
        if (res is not null) return res;

        res = new PdfDictionary();
        var inherited = InheritedResources(pageDict, reader);
        if (inherited is not null)
        {
            foreach (var k in inherited.Keys)
            {
                var v = inherited.Get(k);
                if (v is null) continue;
                if (reader.ResolveDict(v) is { } sub)
                {
                    var clone = new PdfDictionary();
                    foreach (var sk in sub.Keys)
                    {
                        var sv = sub.Get(sk);
                        if (sv is not null) clone.Set(sk, sv);
                    }
                    res.Set(k, clone);
                }
                else
                {
                    res.Set(k, v);
                }
            }
        }
        pageDict.Set("Resources", res);
        return res;
    }

    /// <summary>Resolve the /Resources a page inherits from an ancestor /Pages node when
    /// it carries none of its own. Walks the /Parent chain; null if none is found.</summary>
    private static PdfDictionary? InheritedResources(PdfDictionary pageDict, PdfReader reader)
    {
        var parentObj = pageDict.Get("Parent");
        var visited = new System.Collections.Generic.HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            if (reader.ResolveDict(parent.Get("Resources")) is { } res)
                return res;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    /// <summary>
    /// Export form field data in FDF (Forms Data Format) per PDF spec §12.7.7.
    /// </summary>
    public byte[] ExportFdf()
    {
        var sb = new StringBuilder();
        sb.Append("%FDF-1.2\n");
        sb.Append("1 0 obj\n");
        sb.Append("<< /FDF << /Fields [\n");

        foreach (var field in _fields)
        {
            var name = field.FullName ?? field.PartialName;
            if (name is null) continue;
            var value = field.Value ?? "";
            sb.Append("  << /T (");
            sb.Append(EscapeFdfString(name));
            sb.Append(") /V (");
            sb.Append(EscapeFdfString(value));
            sb.Append(") >>\n");
        }

        sb.Append("] >> >>\n");
        sb.Append("endobj\n");
        sb.Append("trailer\n");
        sb.Append("<< /Root 1 0 R >>\n");
        sb.Append("%%EOF\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Import form field data from FDF bytes.
    /// </summary>
    public void ImportFdf(byte[] fdfData)
    {
        var text = Encoding.Latin1.GetString(fdfData);
        var pairs = ParseFdfFields(text);
        foreach (var (name, value) in pairs)
        {
            var field = FindFieldOrNull(name);
            if (field is null) continue;
            if (field is CheckboxField cb)
            {
                // FDF carries a checkbox's export value verbatim: check the box only
                // when the value names a declared on-state. "Off", empty, and any
                // non-matching value (e.g. "No" on a Yes/Off box) leave it unchecked.
                // Routing through the generic Value setter would instead coerce every
                // non-"Off" value to the on-state and wrongly tick the box.
                if (cb.IsDeclaredOnState(value)) cb.Value = value;
                else cb.Checked = false;
            }
            else
                field.Value = value;
        }
    }

    /// <summary>
    /// Export form field data in XFDF (XML Forms Data Format).
    /// </summary>
    public string ExportXfdf()
    {
        var ns = XNamespace.Get("http://ns.adobe.com/xfdf/");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "xfdf",
                new XElement(ns + "fields",
                    _fields
                        .Where(f => f.FullName is not null || f.PartialName is not null)
                        .Select(f => new XElement(ns + "field",
                            new XAttribute("name", f.FullName ?? f.PartialName!),
                            new XElement(ns + "value", f.Value ?? ""))))));

        return doc.Declaration + "\n" + doc;
    }

    /// <summary>
    /// Import form field data from XFDF XML string.
    /// </summary>
    public void ImportXfdf(string xfdfXml)
    {
        var doc = XDocument.Parse(xfdfXml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var fields = doc.Root?.Element(ns + "fields");
        if (fields is null) return;
        ImportXfdfFields(fields, ns, parentPath: null);
    }

    /// <summary>
    /// Recursively import the <c>&lt;field&gt;</c> children of <paramref name="container"/>.
    /// Flat exports name fields fully (<c>&lt;field name="SA Datum.0"&gt;</c>), while
    /// Acrobat nests partial names (<c>&lt;field name="SA Datum"&gt;&lt;field name="0"&gt;</c>),
    /// so the full field name is the dotted join of the ancestor <c>name</c> attributes.
    /// A field element carries its value in a direct <c>&lt;value&gt;</c> child; nested
    /// <c>&lt;field&gt;</c> elements (or a defensive <c>&lt;fields&gt;</c> wrapper) describe
    /// child fields.
    /// </summary>
    private void ImportXfdfFields(XElement container, XNamespace ns, string? parentPath)
    {
        foreach (var fieldEl in container.Elements(ns + "field"))
        {
            var name = fieldEl.Attribute("name")?.Value;
            if (name is null) continue;
            var path = parentPath is null ? name : parentPath + "." + name;

            var value = fieldEl.Element(ns + "value")?.Value;
            if (value is not null)
            {
                var field = FindFieldOrNull(path);
                if (field is not null)
                    field.Value = value;
            }

            // Recurse: Acrobat nests <field> directly; some writers wrap them in <fields>.
            ImportXfdfFields(fieldEl, ns, path);
            var nestedWrap = fieldEl.Element(ns + "fields");
            if (nestedWrap is not null)
                ImportXfdfFields(nestedWrap, ns, path);
        }
    }

    private static string EscapeFdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static List<(string name, string value)> ParseFdfFields(string fdf)
    {
        // FDF /Fields is a tree: each entry has an optional /T (partial name),
        // an optional /V (value), and an optional /Kids (child entries). The full
        // field name of a leaf is the dotted join of /T values from the root.
        // Acrobat exports hierarchical fields this way — e.g.
        //   <</Kids[<</T(0)/V(01-09-2010)>> ...]/T(SA Datum Sollicitatie)>>
        // resolves to "SA Datum Sollicitatie.0". A flat <</T(.)/V(.)>> scan would
        // mistake the kids' partial names ("0".."6") for full names.
        var result = new List<(string, string)>();
        var fieldsIdx = fdf.IndexOf("/Fields", StringComparison.Ordinal);
        if (fieldsIdx < 0) return result;
        var pos = fdf.IndexOf('[', fieldsIdx);
        if (pos < 0) return result;
        pos++; // step past '['
        ParseFdfFieldsArray(fdf, ref pos, parentPath: null, result);
        return result;
    }

    private static void ParseFdfFieldsArray(string t, ref int pos, string? parentPath,
        List<(string, string)> result)
    {
        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (t[pos] == ']') { pos++; return; }
            if (pos + 1 < t.Length && t[pos] == '<' && t[pos + 1] == '<')
            {
                pos += 2;
                ParseFdfFieldDict(t, ref pos, parentPath, result);
            }
            else
            {
                pos++; // tolerate stray bytes
            }
        }
    }

    private static void ParseFdfFieldDict(string t, ref int pos, string? parentPath,
        List<(string, string)> result)
    {
        string? partialName = null;
        string? value = null;
        int kidsStart = -1;

        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (pos + 1 < t.Length && t[pos] == '>' && t[pos + 1] == '>') { pos += 2; break; }
            if (t[pos] != '/') { pos++; continue; }
            pos++; // step past '/'
            int kStart = pos;
            while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++;
            var key = t.Substring(kStart, pos - kStart);
            FdfSkipWS(t, ref pos);
            if (key == "T" && pos < t.Length && t[pos] == '(')
                partialName = FdfReadStringLiteral(t, ref pos);
            else if (key == "V")
                value = FdfReadValue(t, ref pos);
            else if (key == "Kids" && pos < t.Length && t[pos] == '[')
            {
                kidsStart = pos + 1; // remember; consume below
                FdfSkipValue(t, ref pos);
            }
            else
                FdfSkipValue(t, ref pos);
        }

        var fullPath = (parentPath, partialName) switch
        {
            (null, null) => null,
            (null, _) => partialName,
            (_, null) => parentPath,
            _ => $"{parentPath}.{partialName}",
        };

        if (kidsStart >= 0)
        {
            int kp = kidsStart;
            ParseFdfFieldsArray(t, ref kp, fullPath, result);
        }
        else if (fullPath is not null)
        {
            result.Add((fullPath, value ?? ""));
        }
    }

    /// <summary>Read a /V value: either a string literal <c>(...)</c> or a name
    /// object <c>/Off</c> (checkbox states). Returns the decoded text; the name
    /// object's value is returned without the leading slash.</summary>
    private static string? FdfReadValue(string t, ref int pos)
    {
        FdfSkipWS(t, ref pos);
        if (pos >= t.Length) return null;
        if (t[pos] == '(') return FdfReadStringLiteral(t, ref pos);
        if (t[pos] == '/')
        {
            pos++; // step past '/'
            int s = pos;
            while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++;
            return t.Substring(s, pos - s);
        }
        FdfSkipValue(t, ref pos);
        return null;
    }

    private static void FdfSkipWS(string t, ref int pos)
    {
        while (pos < t.Length)
        {
            char c = t[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0') pos++;
            else if (c == '%') { while (pos < t.Length && t[pos] != '\n') pos++; }
            else break;
        }
    }

    private static bool IsFdfDelimOrWS(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0'
        || c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']'
        || c == '/' || c == '%';

    private static string FdfReadStringLiteral(string t, ref int pos)
    {
        pos++; // step past '('
        var sb = new StringBuilder();
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            char c = t[pos++];
            if (c == '\\')
            {
                if (pos >= t.Length) break;
                char esc = t[pos++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    case '\n': break;
                    case '\r': if (pos < t.Length && t[pos] == '\n') pos++; break;
                    case >= '0' and <= '7':
                    {
                        // Octal byte escape \d, \dd or \ddd.
                        int v = esc - '0';
                        for (var k = 0; k < 2 && pos < t.Length && t[pos] is >= '0' and <= '7'; k++)
                            v = v * 8 + (t[pos++] - '0');
                        sb.Append((char)(v & 0xFF));
                        break;
                    }
                    default: sb.Append(esc); break;
                }
            }
            else if (c == '(') { depth++; sb.Append(c); }
            else if (c == ')') { depth--; if (depth > 0) sb.Append(c); }
            else sb.Append(c);
        }
        // A UTF-16BE BOM marks a Unicode text string (PDF 32000 §7.9.2.2): the
        // chars gathered above are raw BYTES (the FDF was Latin1-decoded), so
        // fold byte pairs back into characters — Hebrew/CJK FDF values arrive
        // this way and would otherwise import as mojibake.
        if (sb.Length >= 2 && sb[0] == 'þ' && sb[1] == 'ÿ')
        {
            var chars = new StringBuilder((sb.Length - 2) / 2);
            for (var i = 2; i + 1 < sb.Length; i += 2)
                chars.Append((char)((sb[i] << 8) | sb[i + 1]));
            return chars.ToString();
        }
        return sb.ToString();
    }

    private static void FdfSkipValue(string t, ref int pos)
    {
        FdfSkipWS(t, ref pos);
        if (pos >= t.Length) return;
        char c = t[pos];
        if (c == '(') { FdfReadStringLiteral(t, ref pos); }
        else if (c == '[') { FdfSkipArray(t, ref pos); }
        else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') { FdfSkipDict(t, ref pos); }
        else if (c == '<') { pos++; while (pos < t.Length && t[pos] != '>') pos++; if (pos < t.Length) pos++; }
        else if (c == '/') { pos++; while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++; }
        else { while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++; }
    }

    private static void FdfSkipArray(string t, ref int pos)
    {
        pos++; // step past '['
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            char c = t[pos];
            if (c == '[') { depth++; pos++; }
            else if (c == ']') { depth--; pos++; }
            else if (c == '(') { FdfReadStringLiteral(t, ref pos); }
            else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') { FdfSkipDict(t, ref pos); }
            else pos++;
        }
    }

    private static void FdfSkipDict(string t, ref int pos)
    {
        pos += 2; // step past '<<'
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (pos + 1 < t.Length && t[pos] == '<' && t[pos + 1] == '<') { depth++; pos += 2; }
            else if (pos + 1 < t.Length && t[pos] == '>' && t[pos + 1] == '>') { depth--; pos += 2; }
            else if (t[pos] == '(') { FdfReadStringLiteral(t, ref pos); }
            else if (t[pos] == '[') { FdfSkipArray(t, ref pos); }
            else pos++;
        }
    }

    /// <summary>True when the widget belongs to a push-button field — field type
    /// /Btn with the Pushbutton flag (Ff bit 17, value 1&lt;&lt;16) set. /FT and /Ff
    /// are inherited, so walk up the /Parent chain when the widget itself omits them.</summary>
    private static bool IsPushButtonWidget(PdfDictionary widget, Aspose.Pdf.IO.PdfReader reader)
    {
        string? ft = null;
        long ff = 0;
        var dict = widget;
        var guard = 0;
        while (dict is not null && guard++ < 32)
        {
            ft ??= dict.GetName("FT");
            if (dict.Get("Ff") is { } ffObj && reader.Resolve(ffObj) is PdfInteger ffi && ff == 0)
                ff = ffi.Value;
            if (ft is not null && ff != 0) break;
            dict = reader.ResolveDict(dict.Get("Parent"));
        }
        return ft == "Btn" && (ff & (1L << 16)) != 0;
    }

    private void FlattenFieldsOnPage(Page page, bool hideButtons = false, int frmStartIndex = 0,
        bool flattenNonWidgets = false, bool skipInvisible = false,
        System.Collections.Generic.HashSet<PdfDictionary>? fieldWidgets = null,
        bool dropUnstamped = false)
    {
        var reader = page.Reader;
        var annotsObj = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        var remaining = new PdfArray();
        var appendContent = new System.IO.MemoryStream();
        // Flattened field appearances are registered as FRM{n} in /Annots order so a caller can
        // look each one up by position. The base index is path-dependent:
        // the document/form flatten numbers from FRM0, the facade FlattenAllFields from FRM1.
        int frmCounter = frmStartIndex;
        foreach (var annotRef in annotsObj)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null)
            {
                remaining.Add(annotRef);
                continue;
            }

            var subtype = annotDict.GetName("Subtype");
            // Merged field-widget dicts may omit /Subtype but still have field
            // properties (/FT, /T, or /Parent pointing to a field hierarchy).
            bool isWidget = subtype == "Widget"
                || (subtype is null && (annotDict.ContainsKey("FT") || annotDict.ContainsKey("T")
                    || annotDict.ContainsKey("Parent")));
            // Form-field flatten (document/form) folds only widgets into the page content and
            // leaves other annotation types (markup, line, link, …) for their own annotation
            // path. FlattenAllFields (flattenNonWidgets) instead flattens every annotation with
            // an appearance so the FRM{n} index lines up with the page /Annots order.
            if (!isWidget && !flattenNonWidgets)
            {
                remaining.Add(annotRef);
                continue;
            }

            // HideButtons: drop push-button widgets entirely (neither rendered
            // into page content nor kept as an annotation) so the flattened
            // output shows no buttons.
            if (isWidget && hideButtons && IsPushButtonWidget(annotDict, reader))
                continue;

            // PDF/A flatten: hidden widgets (F bit 2) and push buttons are dropped
            // without a page-content fragment; other widgets stamp regardless of
            // the Print bit (a filled text field with F=0 still shows its value
            // in the flattened output).
            if (isWidget && skipInvisible)
            {
                var fFlags = annotDict.GetInt("F");
                if ((fFlags & 2) != 0) continue;
                if (IsPushButtonWidget(annotDict, reader)) continue;
                // A visible signature widget is never folded into content: the
                // signature field survives PDF/A conversion (hidden ones were
                // dropped above).
                var widgetFt = annotDict.GetName("FT")
                    ?? reader.ResolveDict(annotDict.Get("Parent"))?.GetName("FT");
                if (widgetFt == "Sig") { remaining.Add(annotRef); continue; }
                if (fieldWidgets is not null && !fieldWidgets.Remove(annotDict)) continue;
            }

            // Build the content fragment that folds this widget's appearance into the page
            // (registering it as FRM{n}). Null when the widget has no usable appearance — a
            // widget is then dropped (orphan field), a non-widget kept in /Annots.
            var fragment = BuildWidgetFlattenFragment(page.Dict, annotDict, reader, $"FRM{frmCounter}");
            if (fragment is null)
            {
                // Flattening a PAGE consumes its whole /Annots array: an annotation that
                // draws nothing still LEAVES, it is not left behind as live markup. The
                // document/form flatten instead keeps non-widgets for the annotation path
                // that runs after it.
                if (!isWidget && !dropUnstamped) remaining.Add(annotRef);
                continue;
            }
            frmCounter++;
            var writer = new System.IO.StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write(fragment);
            writer.Flush();
        }

        // Update page annotations (remove flattened widgets)
        if (remaining.Count > 0)
            page.Dict.Set("Annots", remaining);
        else
            page.Dict.Remove("Annots");

        // Append the flattened content to the page. /Contents may be a single stream
        // OR an array of streams (PDF 32000-2 § 7.7.3.3) — Page.GetContentStreamBytes
        // concatenates the array case correctly; doing it inline only handled the
        // single-stream case, silently dropping the original page content for any
        // array-of-streams page.
        if (appendContent.Length > 0)
        {
            var existingData = page.GetContentStreamBytes() ?? [];

            // Bracket the original page content in a balanced q … Q before appending the
            // flattened field fragments. A page content stream may leave the CTM in a
            // non-default state — e.g. a leading global "0.12 0 0 0.12 0 0 cm" scale that
            // is never wrapped in q/Q (some form authoring tools draw the whole page in an
            // 8.33× coordinate space) — so appending fragments raw would draw every widget
            // appearance through that leftover transform (scaled + shoved into a page
            // corner). The outer q/Q restores the base CTM so each "q … cm /FRMn Do Q"
            // fragment is placed at its true /Rect. Harmless when the page content is
            // already clean (a no-op save/restore around it).
            byte[] pre = existingData.Length > 0 ? Encoding.ASCII.GetBytes("q\n") : [];
            byte[] mid = existingData.Length > 0 ? Encoding.ASCII.GetBytes("\nQ\n") : [];
            var frag = appendContent.ToArray();

            var combined = new byte[pre.Length + existingData.Length + mid.Length + frag.Length];
            int off = 0;
            pre.CopyTo(combined, off); off += pre.Length;
            existingData.CopyTo(combined, off); off += existingData.Length;
            mid.CopyTo(combined, off); off += mid.Length;
            frag.CopyTo(combined, off);

            page.SetContentStream(combined);
        }
    }
}
