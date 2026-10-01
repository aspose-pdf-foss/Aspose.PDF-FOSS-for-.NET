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
    internal void Flatten(Document document, FlattenSettings? settings, int frmStartIndex, bool flattenNonWidgets, bool skipInvisible = false, bool keepAcroFormDict = false)
    {
        var fl = new FormFlattenState();
        fl.document = document;
        fl.settings = settings;
        fl.frmStartIndex = frmStartIndex;
        fl.flattenNonWidgets = flattenNonWidgets;
        fl.skipInvisible = skipInvisible;
        fl.keepAcroFormDict = keepAcroFormDict;
        // 0. A DYNAMIC XFA form (no AcroForm widgets — its fields live only in the
        //    XFA template) must first be folded to a standard AcroForm: paint the
        //    form onto real pages (replacing the viewer placeholder page) and
        //    materialise flat fields. A static XFA form keeps its widget path.
        if (IsXfa)
        {
            var xfaAcro = fl.document.Reader.ResolveDict(fl.document.Catalog.Get("AcroForm"));
            var xfaFields = xfaAcro is null ? null : fl.document.Reader.Resolve(xfaAcro.Get("Fields")) as PdfArray;
            if (xfaFields is null || xfaFields.Count == 0)
                FlattenXfa();
        }

        fl.refresh = fl.settings is null || fl.settings.UpdateAppearances;
        if (fl.refresh)
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

        fl.acroForm = fl.document.Reader.ResolveDict(fl.document.Catalog.Get("AcroForm"));
        fl.drFonts = fl.document.Reader.ResolveDict(fl.document.Reader.ResolveDict(fl.acroForm?.Get("DR"))?.Get("Font"));
        if (fl.drFonts is not null)
        {
            foreach (var page in fl.document.Pages)
                HoistDrFontsIntoPageResources(page, fl.drFonts, fl.document.Reader);
        }

        fl.hideButtons = fl.settings is { HideButtons: true };
        fl.fieldWidgets = null;
        if (fl.skipInvisible)
        {
            fl.fieldWidgets = new System.Collections.Generic.HashSet<PdfDictionary>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
            void CollectFieldDicts(PdfArray arr, int depth)
            {
                if (depth > 16) return;
                foreach (var o in arr)
                {
                    if (fl.document.Reader.ResolveDict(o) is not { } fd) continue;
                    fl.fieldWidgets.Add(fd);
                    if (fl.document.Reader.Resolve(fd.Get("Kids")) is PdfArray ka)
                        CollectFieldDicts(ka, depth + 1);
                }
            }
            if (fl.document.Reader.Resolve(fl.acroForm?.Get("Fields")) is PdfArray topFields)
                CollectFieldDicts(topFields, 0);
            // A document whose form lives only in page widgets (no /Fields entries)
            // has nothing to anchor the orphan filter — leave it inactive.
            if (fl.fieldWidgets.Count == 0) fl.fieldWidgets = null;
        }
        foreach (var page in fl.document.Pages)
        {
            if (!FlattenPageAnnotations(fl, page)) break;
        }

        // Remove AcroForm from catalog (the PDF/A flatten keeps the dict — emptied
        // below — so its /DR fonts survive for DefaultResources readers).
        if (!fl.keepAcroFormDict)
            fl.document.Catalog.Remove("AcroForm");

        // Empty the /Fields array on the (now-detached) AcroForm dict too: Count reads the
        // AcroForm's /Fields (this Form's cached _acroForm, or the catalog's) before falling
        // back to _fields, so without this a flattened form still reports its old field count.
        // The PDF/A flatten keeps VISIBLE signature fields (their widgets stayed in
        // /Annots above); everything else is dropped.
        PruneFlattenedAcroFormFields(fl);

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

        return Compat.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Import form field data from FDF bytes.
    /// </summary>
    public void ImportFdf(byte[] fdfData)
    {
        var text = Compat.Latin1.GetString(fdfData);
        var pairs = FdfFieldScanner.ReadFields(text);
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

    private void FlattenFieldsOnPage(Page page, bool hideButtons = false, int frmStartIndex = 0, bool flattenNonWidgets = false, bool skipInvisible = false, System.Collections.Generic.HashSet<PdfDictionary>? fieldWidgets = null, bool dropUnstamped = false)
    {
        var fp = new FlattenPageState();
        fp.page = page;
        fp.hideButtons = hideButtons;
        fp.frmStartIndex = frmStartIndex;
        fp.flattenNonWidgets = flattenNonWidgets;
        fp.skipInvisible = skipInvisible;
        fp.fieldWidgets = fieldWidgets;
        fp.dropUnstamped = dropUnstamped;
        fp.reader = fp.page.Reader;
        fp.annotsObj = fp.reader.Resolve(fp.page.Dict.Get("Annots")) as PdfArray;
        if (fp.annotsObj is null) return;

        fp.remaining = new PdfArray();
        fp.appendContent = new System.IO.MemoryStream();
        fp.frmCounter = fp.frmStartIndex;
        foreach (var annotRef in fp.annotsObj)
        {
            FlattenAnnotation(fp, annotRef);
        }

        // Update page annotations (remove flattened widgets)
        if (fp.remaining.Count > 0)
            fp.page.Dict.Set("Annots", fp.remaining);
        else
            fp.page.Dict.Remove("Annots");

        // Append the flattened content to the page. /Contents may be a single stream
        // OR an array of streams (PDF 32000-2 § 7.7.3.3) — Page.GetContentStreamBytes
        // concatenates the array case correctly; doing it inline only handled the
        // single-stream case, silently dropping the original page content for any
        // array-of-streams page.
        if (fp.appendContent.Length > 0)
        {
            AppendFlattenedContent(fp);
        }
    }
}
