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
    /// <summary>Serialize the form's fields to JSON via the supplied stream.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(Stream stream)
        => ExportToJson(stream, null);

    /// <summary>Serialize the form's fields to JSON in a file.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(string fileName)
        => ExportToJson(fileName, null);

    /// <summary>Serialize the form's fields to JSON via the supplied stream.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(Stream stream, ExportFieldsToJsonOptions? options)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var indent = options?.WriteIndented ?? false;
        var reader = _reader ?? OwnerDocument?.Reader;
        // Serialize ROOT fields only (one entry per AcroForm /Fields entry, matching
        // Count): a group/subform or radio group is a single entry whose descendants
        // the exporter nests under /ChildFields — not one flat entry per terminal.
        var roots = RootFields(reader);
        var entries = new List<FieldExportingData>(roots.Count + 1);
        var results = new List<FieldSerializationResult>(roots.Count);
        foreach (var f in roots)
        {
            entries.Add(FieldJsonExporter.BuildField(f));
            results.Add(new FieldSerializationResult
            {
                FieldFullName = f.FullName ?? f.PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            });
        }
        // Append a single entry carrying the form-level AcroForm dictionary data.
        entries.Add(FieldJsonExporter.BuildAcroForm(ResolveAcroForm(), reader));
        FieldJsonExporter.Write(stream, entries, indent);
        return results;
    }

    /// <summary>Serialize the form's fields to JSON in a file.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(string fileName, ExportFieldsToJsonOptions? options)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        return ExportToJson(fs, options);
    }

    /// <summary>Read form-field values from a JSON stream and apply them.</summary>
    public IEnumerable<FieldSerializationResult> ImportFromJson(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var results = new List<FieldSerializationResult>();
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return results;
        try
        {
            using var jdoc = System.Text.Json.JsonDocument.Parse(stream);
            var root = jdoc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in root.EnumerateArray())
                    ImportFieldEntry(entry, reader, null, results);
            }
            else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                ImportFieldEntry(root, reader, null, results);
            }
        }
        catch
        {
            // parse failure → whatever was reconstructed so far
        }
        return results;
    }

    /// <summary>Reconstruct a field (and its child fields) from a FieldExportingData
    /// JSON entry. Top-level fields are added to the form; child fields are wired
    /// as /Kids of their parent (so a group field contributes a single Form entry).
    /// Returns the built dictionary for use as a parent's kid.</summary>
    private PdfDictionary? ImportFieldEntry(
        System.Text.Json.JsonElement entry, PdfReader reader,
        PdfDictionary? parent, List<FieldSerializationResult> results)
    {
        if (entry.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var hasFieldType = entry.TryGetProperty("FieldType", out var ftEl)
            && ftEl.ValueKind == System.Text.Json.JsonValueKind.String;
        var hasAcroForm = entry.TryGetProperty("AcroFormData", out var acroEl)
            && acroEl.ValueKind != System.Text.Json.JsonValueKind.Null;
        // The single form-level AcroForm entry carries no field — apply its
        // dictionary data (/DA, /NeedAppearances, /DR) to the target form instead.
        if (parent is null && !hasFieldType && hasAcroForm)
        {
            ImportAcroFormData(acroEl, reader);
            return null;
        }

        var name = entry.TryGetProperty("Name", out var n)
            && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
        if (string.IsNullOrEmpty(name)) return null;

        var dict = new PdfDictionary();
        // A child's partial name is the last dotted segment; a top-level field uses its full name.
        var partial = parent is not null && name!.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name!;
        dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(partial)));
        if (hasFieldType)
        {
            var ftName = ftEl.GetString();
            if (MapFieldTypeToFt(ftName) is { } ft)
                dict.Set("FT", new PdfName(ft));
            // Restore the field-flag bits that distinguish the concrete /Btn and
            // /Ch subtypes (radio / push-button / combo) so the field rebuilds as
            // the right type — the flat export carries only the FieldType name.
            var ff = FieldTypeFlags(ftName);
            if (ff != 0) dict.Set("Ff", new PdfInteger(ff));
        }
        if (entry.TryGetProperty("Value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            dict.Set("V", new PdfString(Encoding.Latin1.GetBytes(v.GetString()!)));
        if (entry.TryGetProperty("Flags", out var fl) && fl.ValueKind == System.Text.Json.JsonValueKind.Number
            && fl.TryGetInt32(out var flv) && flv != 0)
            dict.Set("F", new PdfInteger(flv));
        if (parent is not null) dict.Set("Parent", parent);

        // Carry the field's /DA so the appearance generator picks up the original
        // font + size (without it the default 12pt /Helv clips the value text).
        if (entry.TryGetProperty("DefaultAppearance", out var daEl)
            && daEl.ValueKind == System.Text.Json.JsonValueKind.String)
            dict.Set("DA", new PdfString(Encoding.Latin1.GetBytes(daEl.GetString()!)));

        // Choice-field options (/Opt) — required for a listbox to show all items,
        // and for a combo-box appearance that needs the display value list.
        if (entry.TryGetProperty("Options", out var optEl)
            && optEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var oa = new PdfArray();
            foreach (var o in optEl.EnumerateArray())
                if (o.ValueKind == System.Text.Json.JsonValueKind.String)
                    oa.Add(new PdfString(Encoding.Latin1.GetBytes(o.GetString()!)));
            if (oa.Count > 0) dict.Set("Opt", oa);
        }

        // Push-button normal caption (/MK /CA) — ButtonField.GenerateAppearance
        // centres this string on the button face.
        if (entry.TryGetProperty("NormalCaption", out var ncEl)
            && ncEl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var mk = new PdfDictionary();
            mk.Set("CA", new PdfString(Encoding.Latin1.GetBytes(ncEl.GetString()!)));
            dict.Set("MK", mk);
        }

        // Restore each radio/check widget's identity within its group: /AS picks
        // the visible variant; the appearance generator uses /AS (when not "Off")
        // as the on-name for the widget's /AP/N, so the field's /V selects the
        // right widget visually after round-trip.
        if (entry.TryGetProperty("AppearanceState", out var asEl)
            && asEl.ValueKind == System.Text.Json.JsonValueKind.String)
            dict.Set("AS", new PdfName(asEl.GetString()!));

        // A widget carries a /Rect; reconstruct it so the field renders in place.
        if (entry.TryGetProperty("Rect", out var rc)
            && rc.ValueKind == System.Text.Json.JsonValueKind.Array && rc.GetArrayLength() >= 4)
        {
            var ra = new PdfArray();
            var idx = 0;
            foreach (var num in rc.EnumerateArray())
            {
                if (idx++ >= 4) break;
                ra.Add(new PdfReal(num.GetDouble()));
            }
            dict.Set("Rect", ra);
            dict.Set("Type", new PdfName("Annot"));
            dict.Set("Subtype", new PdfName("Widget"));
        }

        var pageIndex = 1;
        var hasOwnPage = entry.TryGetProperty("Page", out var pg)
            && pg.ValueKind == System.Text.Json.JsonValueKind.Number
            && pg.TryGetInt32(out var pv) && pv > 0;
        if (hasOwnPage) pageIndex = pg.GetInt32();

        // A child widget carries its OWN page in the export (a group root often has
        // none — its Page is null). Route the kid to that page through the same
        // _PlacePage hint CheckboxField.AddOption uses; PlaceFieldWidgets consumes
        // and removes the key when the root is placed.
        if (parent is not null && hasOwnPage)
            dict.Set("_PlacePage", new PdfInteger(pageIndex));

        // PageRef records whether the SOURCE field carried a /P entry; the import
        // reproduces that — a field exported with PageRef=false lands in the page's
        // /Annots but gets NO /P (both shapes hold: a group kid
        // keeps /P, flat fields must not gain one).
        if (entry.TryGetProperty("PageRef", out var prEl)
            && prEl.ValueKind == System.Text.Json.JsonValueKind.False)
            dict.Set("_NoPageRef", PdfBoolean.True);

        var childApRoundTripped = false;
        if (entry.TryGetProperty("ChildFields", out var kids) && kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var kidsArr = new PdfArray();
            foreach (var kid in kids.EnumerateArray())
            {
                var kidDict = ImportFieldEntry(kid, reader, dict, results);
                if (kidDict is not null)
                {
                    kidsArr.Add(kidDict);
                    // A radio option widget carries its own round-tripped /AP; its
                    // presence means the group must NOT be regenerated below (the
                    // radio generator overwrites kid appearances unconditionally).
                    if (kidDict.ContainsKey("AP")) childApRoundTripped = true;
                }
            }
            if (kidsArr.Count > 0) dict.Set("Kids", kidsArr);
        }

        // Restore the widget's /AP from captured stream bytes, when present.
        // /AP precedence over GenerateAppearance keeps Acrobat's pre-baked
        // appearance pixel-identical through the round-trip (the per-type
        // generator's first line short-circuits when /AP is populated).
        var apRoundTripped = ImportAppearances(entry, dict);

        if (parent is null)
        {
            var field = Field.Create(dict, reader);
            field.OwnerDocument = OwnerDocument;
            _fields.Add(field);
            AddToAcroFormFields(reader, dict);
            // Draw the field's appearance and place its widget(s) on the page so
            // the imported form renders, mirroring Form.Add for a created field.
            // Skip the generator pass when /AP was round-tripped from JSON --
            // the per-type generators check for /AP themselves but radio's path
            // overwrites kids unconditionally; skipping at this level is safer.
            if (!apRoundTripped && !childApRoundTripped) field.GenerateAppearance();
            PlaceFieldWidgets(dict, reader, pageIndex);
            results.Add(new FieldSerializationResult
            {
                FieldFullName = name,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            });
        }
        return dict;
    }

    /// <summary>Rebuild the widget's /AP dict from the JSON Appearances block,
    /// reconstructing each variant's Form-XObject stream verbatim. Returns true
    /// when at least one stream was attached -- the caller skips
    /// GenerateAppearance so the per-type generator can't overwrite the
    /// round-tripped content.</summary>
    private static bool ImportAppearances(System.Text.Json.JsonElement entry, PdfDictionary widgetDict)
    {
        if (!entry.TryGetProperty("Appearances", out var apsEl)
            || apsEl.ValueKind != System.Text.Json.JsonValueKind.Object)
            return false;

        var apDict = new PdfDictionary();
        var any = false;
        foreach (var variant in apsEl.EnumerateObject())
        {
            if (variant.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            // Single-stream variant: one entry with null state -> /AP/<v> = stream.
            // State-dict variant: many entries -> /AP/<v> = { state: stream, ... }.
            PdfDictionary? states = null;
            PdfStream? singleStream = null;
            var count = variant.Value.GetArrayLength();
            foreach (var stateEl in variant.Value.EnumerateArray())
            {
                if (stateEl.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var hasState = stateEl.TryGetProperty("State", out var sEl)
                    && sEl.ValueKind == System.Text.Json.JsonValueKind.String;
                var stateName = hasState ? sEl.GetString() : null;
                var s = BuildAppearanceStream(stateEl);
                if (s is null) continue;
                if (count == 1 && !hasState)
                {
                    singleStream = s;
                }
                else
                {
                    states ??= new PdfDictionary();
                    states.Set(stateName ?? "On", s);
                }
            }
            if (singleStream is not null)
            {
                apDict.Set(variant.Name, singleStream);
                any = true;
            }
            else if (states is not null)
            {
                apDict.Set(variant.Name, states);
                any = true;
            }
        }

        if (!any) return false;
        widgetDict.Set("AP", apDict);
        return true;
    }

    /// <summary>Build a Form XObject /AP stream from one Appearances entry --
    /// Base64-decode the content bytes, restore /BBox + /Matrix, and rebuild
    /// /Resources with Standard-14 fonts under the captured aliases.</summary>
    private static PdfStream? BuildAppearanceStream(System.Text.Json.JsonElement stateEl)
    {
        if (!stateEl.TryGetProperty("Content", out var cEl)
            || cEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        byte[] bytes;
        try { bytes = System.Convert.FromBase64String(cEl.GetString()!); }
        catch { return null; }

        var sd = new PdfDictionary();
        sd.Set("Type", new PdfName("XObject"));
        sd.Set("Subtype", new PdfName("Form"));

        if (stateEl.TryGetProperty("BBox", out var bbEl)
            && bbEl.ValueKind == System.Text.Json.JsonValueKind.Array && bbEl.GetArrayLength() >= 4)
        {
            var bb = new PdfArray();
            var i = 0;
            foreach (var n in bbEl.EnumerateArray())
            {
                if (i++ >= 4) break;
                bb.Add(new PdfReal(n.GetDouble()));
            }
            sd.Set("BBox", bb);
        }
        if (stateEl.TryGetProperty("Matrix", out var mxEl)
            && mxEl.ValueKind == System.Text.Json.JsonValueKind.Array && mxEl.GetArrayLength() >= 6)
        {
            var mx = new PdfArray();
            var i = 0;
            foreach (var n in mxEl.EnumerateArray())
            {
                if (i++ >= 6) break;
                mx.Add(new PdfReal(n.GetDouble()));
            }
            sd.Set("Matrix", mx);
        }

        // Rebuild /Resources/Font as Standard-14 entries keyed by the original
        // aliases (Helv, HeBo, ZaDb, ...) so the content's Tf operator resolves.
        var fontDict = new PdfDictionary();
        if (stateEl.TryGetProperty("Fonts", out var fontsEl)
            && fontsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var fEl in fontsEl.EnumerateArray())
            {
                if (fEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var alias = fEl.GetString();
                if (string.IsNullOrEmpty(alias)) continue;
                var fontEntry = new PdfDictionary();
                fontEntry.Set("Type", new PdfName("Font"));
                fontEntry.Set("Subtype", new PdfName("Type1"));
                fontEntry.Set("BaseFont", new PdfName(MapStandardAlias(alias!)));
                fontDict.Set(alias!, fontEntry);
            }
        }
        var resources = new PdfDictionary();
        if (fontDict.Count > 0) resources.Set("Font", fontDict);

        // Rebuild /Resources/XObject image entries (e.g. a barcode field's
        // pre-rendered bars) from the captured decoded samples, so the
        // round-tripped content's Do operators draw again.
        if (stateEl.TryGetProperty("Images", out var imgsEl)
            && imgsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var xobjDict = new PdfDictionary();
            foreach (var imgEl in imgsEl.EnumerateArray())
            {
                if (imgEl.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (!imgEl.TryGetProperty("Name", out var nmEl)
                    || nmEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                if (!imgEl.TryGetProperty("Data", out var dEl)
                    || dEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                byte[] samples;
                try { samples = System.Convert.FromBase64String(dEl.GetString()!); }
                catch { continue; }

                var idict = new PdfDictionary();
                idict.Set("Type", new PdfName("XObject"));
                idict.Set("Subtype", new PdfName("Image"));
                if (imgEl.TryGetProperty("Width", out var wEl) && wEl.TryGetInt32(out var w))
                    idict.Set("Width", new PdfInteger(w));
                if (imgEl.TryGetProperty("Height", out var hEl) && hEl.TryGetInt32(out var h))
                    idict.Set("Height", new PdfInteger(h));
                if (imgEl.TryGetProperty("BitsPerComponent", out var bEl) && bEl.TryGetInt32(out var bpc))
                    idict.Set("BitsPerComponent", new PdfInteger(bpc));
                if (imgEl.TryGetProperty("ColorSpace", out var csEl)
                    && csEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    idict.Set("ColorSpace", new PdfName(csEl.GetString()!));
                if (imgEl.TryGetProperty("ImageMask", out var imEl)
                    && imEl.ValueKind == System.Text.Json.JsonValueKind.True)
                    idict.Set("ImageMask", PdfBoolean.True);
                if (imgEl.TryGetProperty("Decode", out var decEl)
                    && decEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var da = new PdfArray();
                    foreach (var n in decEl.EnumerateArray()) da.Add(new PdfReal(n.GetDouble()));
                    if (da.Count > 0) idict.Set("Decode", da);
                }
                xobjDict.Set(nmEl.GetString()!, new PdfStream(idict, samples));
            }
            if (xobjDict.Count > 0) resources.Set("XObject", xobjDict);
        }
        sd.Set("Resources", resources);

        return new PdfStream(sd, bytes);
    }

    private static string MapStandardAlias(string alias) => alias switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "HeBO" => "Helvetica-BoldOblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "CoOb" => "Courier-Oblique",
        "CoBO" => "Courier-BoldOblique",
        "ZaDb" => "ZapfDingbats",
        "Symb" => "Symbol",
        _ => "Helvetica",
    };

    private static string? MapFieldTypeToFt(string? fieldType) => fieldType switch
    {
        "Text" or "Barcode" => "Tx", // a barcode field is a Tx field with /PMD data
        "Button" or "CheckBox" or "RadioButton" or "Radio" => "Btn",
        "Choice" or "ListBox" or "ComboBox" => "Ch",
        "Signature" => "Sig",
        _ => null,
    };

    /// <summary>The /Ff bits that mark a concrete button/choice subtype, so an
    /// imported field rebuilds as the right type (radio / push-button / combo).</summary>
    private static int FieldTypeFlags(string? fieldType) => fieldType switch
    {
        "RadioButton" or "Radio" => 1 << 15, // Radio
        "Button" => 1 << 16,                 // Pushbutton
        "ComboBox" => 1 << 17,               // Combo
        _ => 0,
    };

    private static PdfDictionary EnsureAcroForm(PdfReader reader)
    {
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            reader.Catalog.Set("AcroForm", acroForm);
        }
        return acroForm;
    }

    private static void AddToAcroFormFields(PdfReader reader, PdfDictionary fieldDict)
    {
        var acroForm = EnsureAcroForm(reader);
        var fields = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fields is null)
        {
            fields = new PdfArray();
            acroForm.Set("Fields", fields);
        }
        fields.Add(fieldDict);
    }

    /// <summary>Apply the form-level AcroForm data (/DA, /NeedAppearances, /DR) from
    /// a FieldExportingData AcroForm entry to the target document's AcroForm.</summary>
    private void ImportAcroFormData(System.Text.Json.JsonElement acro, PdfReader reader)
    {
        var acroForm = EnsureAcroForm(reader);
        if (acro.TryGetProperty("NeedAppearances", out var na)
            && (na.ValueKind == System.Text.Json.JsonValueKind.True || na.ValueKind == System.Text.Json.JsonValueKind.False))
            acroForm.Set("NeedAppearances", na.GetBoolean() ? PdfBoolean.True : PdfBoolean.False);
        if (acro.TryGetProperty("DefaultAppearance", out var da) && da.ValueKind == System.Text.Json.JsonValueKind.String)
            acroForm.Set("DA", new PdfString(Encoding.Latin1.GetBytes(da.GetString()!)));
        if (acro.TryGetProperty("DefaultResources", out var dr) && dr.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var drDict = new PdfDictionary();
            if (dr.TryGetProperty("Fonts", out var fonts) && fonts.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var fontDict = new PdfDictionary();
                foreach (var fn in fonts.EnumerateArray())
                    if (fn.ValueKind == System.Text.Json.JsonValueKind.String)
                        fontDict.Set(fn.GetString()!, new PdfDictionary());
                drDict.Set("Font", fontDict);
            }
            acroForm.Set("DR", drDict);
            DefaultResources = new Aspose.Pdf.Resources(drDict, reader);
        }
    }

    /// <summary>Read form-field values from a JSON file and apply them.</summary>
    public IEnumerable<FieldSerializationResult> ImportFromJson(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        return ImportFromJson(fs);
    }
}
