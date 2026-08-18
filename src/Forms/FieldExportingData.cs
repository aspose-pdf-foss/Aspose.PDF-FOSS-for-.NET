using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;

namespace Aspose.Pdf
{
    /// <summary>
    /// Data-transfer object describing a single form field (or the form-level
    /// AcroForm dictionary) as produced by the form JSON export. Each export
    /// entry is either a field (with <see cref="FieldType"/> set) or the form's
    /// AcroForm data (with <see cref="FieldType"/> <c>null</c> and
    /// <see cref="AcroFormData"/> populated).
    /// </summary>
    public sealed class FieldExportingData
    {
        /// <summary>Fully qualified field name.</summary>
        public string? Name { get; set; }

        /// <summary>The field's value, if any.</summary>
        public string? Value { get; set; }

        /// <summary>The field flags (/Ff entry).</summary>
        public int? Flags { get; set; }

        /// <summary>The field type name (e.g. "Text", "Button"). Null for the
        /// entry that carries the form-level <see cref="AcroFormData"/>.</summary>
        public string? FieldType { get; set; }

        /// <summary>Form-level AcroForm dictionary data. Only set on the single
        /// AcroForm entry; null on field entries.</summary>
        public AcroFormData? AcroFormData { get; set; }

        /// <summary>True when this entry references its owning page indirectly
        /// (a group/non-terminal field whose widgets carry the page reference).</summary>
        public bool PageRef { get; set; }

        /// <summary>True when the field's widget is placed on a page; null when
        /// the field has no direct page placement (e.g. a group field).</summary>
        public bool? OnPage { get; set; }

        /// <summary>The widget rectangle as <c>[llx, lly, urx, ury]</c>, when the
        /// field has a placed widget. Null for a group/non-terminal field.</summary>
        public double[]? Rect { get; set; }

        /// <summary>1-based index of the page the widget is placed on, when known.</summary>
        public int? Page { get; set; }

        /// <summary>The field's /DA default-appearance string (font + size + colour).</summary>
        public string? DefaultAppearance { get; set; }

        /// <summary>The display values for a choice field's /Opt entries.</summary>
        public List<string>? Options { get; set; }

        /// <summary>The widget's /MK/CA normal caption (push-button label).</summary>
        public string? NormalCaption { get; set; }

        /// <summary>For a button widget, the non-"Off" key in the widget's
        /// /AP/N dict — the "on" appearance state name. Lets a re-imported
        /// radio/checkbox widget restore its identity within the group so /V
        /// selects the right widget visually.</summary>
        public string? OnStateName { get; set; }

        /// <summary>The widget's /AS appearance state — which /AP/N variant is
        /// currently shown. For a radio group, the widget whose /AS equals the
        /// field's /V is drawn filled; the rest render "Off".</summary>
        public string? AppearanceState { get; set; }

        /// <summary>Child fields of a group/non-terminal field.</summary>
        public List<FieldExportingData>? ChildFields { get; set; }

        /// <summary>The widget's appearance streams, captured verbatim so a
        /// re-imported field renders pixel-identically to the source rather
        /// than via the per-type generator. Keyed by variant ("N" / "D" / "R");
        /// each variant carries one or more state entries (a text-field /AP/N
        /// is a single stream — one entry with <see cref="AppearanceEntry.State"/>
        /// null; a radio /AP/N is a state dict — one entry per state). Absent
        /// when the field has no /AP, which routes import through the per-type
        /// appearance generator.</summary>
        public Dictionary<string, List<AppearanceEntry>>? Appearances { get; set; }
    }

    /// <summary>One state of a widget appearance variant (the body of an /AP/N,
    /// /AP/D, or /AP/R entry). For a text-field-style single-stream variant the
    /// list under the variant key has one <see cref="AppearanceEntry"/> with
    /// <see cref="State"/> null; for a button-style state dict each state name
    /// gets its own entry.</summary>
    public sealed class AppearanceEntry
    {
        /// <summary>The /AP/&lt;variant&gt;/&lt;state&gt; key — e.g. "Off" / "Yes"
        /// for a checkbox; null when the variant is a single stream with no
        /// enclosing state dict.</summary>
        public string? State { get; set; }

        /// <summary>The form XObject /BBox as <c>[llx, lly, urx, ury]</c>.</summary>
        public double[]? BBox { get; set; }

        /// <summary>The form XObject /Matrix (six entries), when non-identity.</summary>
        public double[]? Matrix { get; set; }

        /// <summary>Font resource names referenced by the appearance content —
        /// rebuilt as Standard-14 entries in the imported stream's /Resources.</summary>
        public List<string>? Fonts { get; set; }

        /// <summary>The decoded appearance content stream bytes, base64-encoded.</summary>
        public string? Content { get; set; }
    }

    /// <summary>
    /// Form-level AcroForm dictionary data surfaced by the form JSON export.
    /// </summary>
    public sealed class AcroFormData
    {
        /// <summary>The AcroForm /NeedAppearances flag.</summary>
        public bool? NeedAppearances { get; set; }

        /// <summary>The AcroForm default appearance string (/DA).</summary>
        public string? DefaultAppearance { get; set; }

        /// <summary>The AcroForm default resources (/DR) — fonts shared by fields.</summary>
        public DefaultResourcesData? DefaultResources { get; set; }
    }

    /// <summary>Form-level default resources (/DR) surfaced by the form JSON export.</summary>
    public sealed class DefaultResourcesData
    {
        /// <summary>Resource names of the fonts in /DR/Font.</summary>
        public List<string>? Fonts { get; set; }
    }
}

namespace Aspose.Pdf.Forms
{
    /// <summary>
    /// Builds <see cref="FieldExportingData"/> entries from form fields and
    /// serialises them to JSON. Shared by the form-, field- and widget-level
    /// JSON export entry points.
    /// </summary>
    internal static class FieldJsonExporter
    {
        /// <summary>Build the export entry for a single field, recursing into
        /// any named child (sub-)fields.</summary>
        public static FieldExportingData BuildField(Field field)
        {
            var page = field.OwnPageIndex;
            var data = new FieldExportingData
            {
                Name = field.FullName ?? field.PartialName ?? string.Empty,
                Value = field.Value,
                Flags = (int)field.Flags,
                FieldType = field.Type.ToString(),
                PageRef = false,
                OnPage = page > 0 ? true : (bool?)null,
                Rect = ReadRect(field),
                Page = page > 0 ? page : (int?)null,
                DefaultAppearance = ReadString(field, "DA"),
                Options = ReadOptions(field),
                NormalCaption = ReadMkString(field, "CA"),
                OnStateName = ReadOnStateName(field),
                AppearanceState = ReadName(field, "AS"),
                Appearances = ReadAppearances(field),
            };

            var children = BuildChildFields(field);
            if (children.Count > 0)
                data.ChildFields = children;

            return data;
        }

        /// <summary>Capture the widget's /AP entries verbatim so the round-trip
        /// reproduces Acrobat's pre-baked appearance instead of regenerating one.
        /// Returns null when /AP is absent.</summary>
        private static Dictionary<string, List<AppearanceEntry>>? ReadAppearances(Field field)
        {
            var ap = field.Reader.ResolveDict(field.Dict.Get("AP"));
            if (ap is null) return null;
            var result = new Dictionary<string, List<AppearanceEntry>>();
            foreach (var variant in new[] { "N", "D", "R" })
            {
                var v = field.Reader.Resolve(ap.Get(variant));
                var entries = new List<AppearanceEntry>();
                if (v is PdfStream stream)
                {
                    entries.Add(BuildAppearanceEntry(field, stream, state: null));
                }
                else if (v is PdfDictionary stateDict)
                {
                    foreach (var key in stateDict.Keys)
                    {
                        if (field.Reader.ResolveStream(stateDict.Get(key)) is { } s)
                            entries.Add(BuildAppearanceEntry(field, s, state: key));
                    }
                }
                if (entries.Count > 0) result[variant] = entries;
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>Capture a single appearance stream — decoded content, /BBox,
        /// /Matrix (when non-identity), and the names of fonts its /Resources
        /// references (rebuilt as Standard-14 entries on import).</summary>
        private static AppearanceEntry BuildAppearanceEntry(Field field, PdfStream stream, string? state)
        {
            var entry = new AppearanceEntry { State = state };

            var decoded = field.Reader.DecodeStream(stream);
            entry.Content = System.Convert.ToBase64String(decoded);

            if (field.Reader.Resolve(stream.Dict.Get("BBox")) is PdfArray bb && bb.Count >= 4)
            {
                entry.BBox = new double[4];
                for (var i = 0; i < 4; i++)
                    entry.BBox[i] = field.Reader.Resolve(bb[i]) switch
                    {
                        PdfReal r => r.Value,
                        PdfInteger i2 => i2.Value,
                        _ => 0,
                    };
            }
            if (field.Reader.Resolve(stream.Dict.Get("Matrix")) is PdfArray mm && mm.Count >= 6)
            {
                entry.Matrix = new double[6];
                for (var i = 0; i < 6; i++)
                    entry.Matrix[i] = field.Reader.Resolve(mm[i]) switch
                    {
                        PdfReal r => r.Value,
                        PdfInteger i2 => i2.Value,
                        _ => 0,
                    };
            }

            var res = field.Reader.ResolveDict(stream.Dict.Get("Resources"));
            var fonts = field.Reader.ResolveDict(res?.Get("Font"));
            if (fonts is not null && fonts.Count > 0)
                entry.Fonts = new List<string>(fonts.Keys);

            return entry;
        }

        private static string? ReadString(Field field, string key) =>
            field.Reader.Resolve(field.Dict.Get(key)) is PdfString s ? s.ToText() : null;

        private static string? ReadName(Field field, string key) =>
            field.Reader.Resolve(field.Dict.Get(key)) is PdfName n ? n.Value : null;

        /// <summary>Discover the widget's on-state name — the non-"Off" key in
        /// /AP/N. Returns null when no /AP/N is present (e.g. group field).</summary>
        private static string? ReadOnStateName(Field field)
        {
            if (field.Reader.ResolveDict(field.Dict.Get("AP")) is not { } ap) return null;
            if (field.Reader.ResolveDict(ap.Get("N")) is not { } n) return null;
            foreach (var key in n.Keys)
                if (key != "Off") return key;
            return null;
        }

        private static string? ReadMkString(Field field, string key)
        {
            if (field.Reader.ResolveDict(field.Dict.Get("MK")) is not { } mk) return null;
            return field.Reader.Resolve(mk.Get(key)) is PdfString s ? s.ToText() : null;
        }

        private static List<string>? ReadOptions(Field field)
        {
            if (field.Reader.Resolve(field.Dict.Get("Opt")) is not PdfArray arr) return null;
            var list = new List<string>();
            foreach (var it in arr)
            {
                var resolved = field.Reader.Resolve(it);
                if (resolved is PdfString s) list.Add(s.ToText());
                else if (resolved is PdfArray pair && pair.Count >= 2 &&
                         field.Reader.Resolve(pair[1]) is PdfString display)
                    list.Add(display.ToText());
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>Read the field widget's /Rect as [llx, lly, urx, ury], or null.</summary>
        private static double[]? ReadRect(Field field)
        {
            if (field.Reader.Resolve(field.Dict.Get("Rect")) is not PdfArray arr || arr.Count < 4)
                return null;
            var r = new double[4];
            for (var i = 0; i < 4; i++)
                r[i] = field.Reader.Resolve(arr[i]) switch
                {
                    PdfReal real => real.Value,
                    PdfInteger integer => integer.Value,
                    _ => 0,
                };
            return r;
        }

        /// <summary>Build the single form-level AcroForm entry.</summary>
        public static FieldExportingData BuildAcroForm(PdfDictionary? acroForm, PdfReader? reader)
        {
            var afd = new AcroFormData();
            if (acroForm is not null && reader is not null)
            {
                if (reader.Resolve(acroForm.Get("NeedAppearances")) is PdfBoolean b)
                    afd.NeedAppearances = b.Value;
                if (reader.Resolve(acroForm.Get("DA")) is PdfString s)
                    afd.DefaultAppearance = s.ToText();
                var dr = reader.ResolveDict(acroForm.Get("DR"));
                if (dr is not null)
                {
                    var drData = new DefaultResourcesData();
                    if (reader.ResolveDict(dr.Get("Font")) is { } fontDict)
                        drData.Fonts = new List<string>(fontDict.Keys);
                    afd.DefaultResources = drData;
                }
            }
            return new FieldExportingData { FieldType = null, AcroFormData = afd };
        }

        /// <summary>Serialise an object (a single entry or a list) to the
        /// stream as UTF-8 JSON and rewind the stream for immediate reading.</summary>
        public static void Write(Stream stream, object data, bool indented)
        {
            var options = new JsonSerializerOptions { WriteIndented = indented };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, data.GetType(), options);
            stream.Write(bytes, 0, bytes.Length);
            if (stream.CanSeek)
                stream.Position = 0;
        }

        private static List<FieldExportingData> BuildChildFields(Field field)
        {
            var list = new List<FieldExportingData>();
            // A radio group's option widgets carry no /T of their own (they share the
            // group's name and differ only by /AP state), yet each holds a distinct
            // /Rect + /AP that must round-trip so the re-imported group renders every
            // option in place. Surface those placed widgets as child entries too;
            // for every other field a kid without /T is just a plain widget already
            // covered by the field's own /Rect + /AP.
            var includeWidgetKids = field is RadioButtonField;
            foreach (var kidDict in field.AllKids())
            {
                var named = kidDict.ContainsKey("T");
                if (!named && !(includeWidgetKids && kidDict.ContainsKey("Rect")))
                    continue;
                var child = BuildField(new Field(kidDict, field.Reader));
                // A directly placed widget child references the page.
                child.PageRef = kidDict.ContainsKey("P") || child.OnPage == true;
                list.Add(child);
            }
            return list;
        }
    }
}
