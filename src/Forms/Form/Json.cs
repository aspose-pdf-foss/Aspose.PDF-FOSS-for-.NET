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
    private PdfDictionary? ImportFieldEntry(System.Text.Json.JsonElement entry, PdfReader reader, PdfDictionary? parent, List<FieldSerializationResult> results)
    {
        var fe = new FieldEntryImportState();
        fe.entry = entry;
        fe.reader = reader;
        fe.parent = parent;
        fe.results = results;
        if (fe.entry.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var hasFieldType = fe.entry.TryGetProperty("FieldType", out var ftEl)
            && ftEl.ValueKind == System.Text.Json.JsonValueKind.String;
        var hasAcroForm = fe.entry.TryGetProperty("AcroFormData", out var acroEl)
            && acroEl.ValueKind != System.Text.Json.JsonValueKind.Null;
        // The single form-level AcroForm entry carries no field — apply its
        // dictionary data (/DA, /NeedAppearances, /DR) to the target form instead.
        if (fe.parent is null && !hasFieldType && hasAcroForm)
        {
            ImportAcroFormData(acroEl, fe.reader);
            return null;
        }

        var name = fe.entry.TryGetProperty("Name", out var n)
            && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
        if (string.IsNullOrEmpty(name)) return null;

        fe.dict = new PdfDictionary();
        fe.partial = fe.parent is not null && name!.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name!;
        fe.dict.Set("T", new PdfString(Compat.Latin1.GetBytes(fe.partial)));
        if (hasFieldType)
        {
            ImportFieldType(fe, ftEl);
        }
        if (fe.entry.TryGetProperty("Value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            fe.dict.Set("V", new PdfString(Compat.Latin1.GetBytes(v.GetString()!)));
        if (fe.entry.TryGetProperty("Flags", out var fl) && fl.ValueKind == System.Text.Json.JsonValueKind.Number
            && fl.TryGetInt32(out var flv) && flv != 0)
            fe.dict.Set("F", new PdfInteger(flv));
        if (fe.parent is not null) fe.dict.Set("Parent", fe.parent);

        // Carry the field's /DA so the appearance generator picks up the original
        // font + size (without it the default 12pt /Helv clips the value text).
        if (fe.entry.TryGetProperty("DefaultAppearance", out var daEl)
            && daEl.ValueKind == System.Text.Json.JsonValueKind.String)
            fe.dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(daEl.GetString()!)));

        // Choice-field options (/Opt) — required for a listbox to show all items,
        // and for a combo-box appearance that needs the display value list.
        ImportChoiceEntries(fe);

        // Restore each radio/check widget's identity within its group: /AS picks
        // the visible variant; the appearance generator uses /AS (when not "Off")
        // as the on-name for the widget's /AP/N, so the field's /V selects the
        // right widget visually after round-trip.
        if (fe.entry.TryGetProperty("AppearanceState", out var asEl)
            && asEl.ValueKind == System.Text.Json.JsonValueKind.String)
            fe.dict.Set("AS", new PdfName(asEl.GetString()!));

        // A widget carries a /Rect; reconstruct it so the field renders in place.
        if (fe.entry.TryGetProperty("Rect", out var rc)
            && rc.ValueKind == System.Text.Json.JsonValueKind.Array && rc.GetArrayLength() >= 4)
        {
            ImportWidgetRect(fe, rc);
            fe.dict.Set("Subtype", new PdfName("Widget"));
        }

        fe.pageIndex = 1;
        var hasOwnPage = fe.entry.TryGetProperty("Page", out var pg)
            && pg.ValueKind == System.Text.Json.JsonValueKind.Number
            && pg.TryGetInt32(out var pv) && pv > 0;
        if (hasOwnPage) fe.pageIndex = pg.GetInt32();

        // A child widget carries its OWN page in the export (a group root often has
        // none — its Page is null). Route the kid to that page through the same
        // _PlacePage hint CheckboxField.AddOption uses; PlaceFieldWidgets consumes
        // and removes the key when the root is placed.
        if (fe.parent is not null && hasOwnPage)
            fe.dict.Set("_PlacePage", new PdfInteger(fe.pageIndex));

        // PageRef records whether the SOURCE field carried a /P entry; the import
        // reproduces that — a field exported with PageRef=false lands in the page's
        // /Annots but gets NO /P (both shapes hold: a group kid
        // keeps /P, flat fields must not gain one).
        if (fe.entry.TryGetProperty("PageRef", out var prEl)
            && prEl.ValueKind == System.Text.Json.JsonValueKind.False)
            fe.dict.Set("_NoPageRef", PdfBoolean.True);

        fe.childApRoundTripped = false;
        if (fe.entry.TryGetProperty("ChildFields", out var kids) && kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            ImportChildFields(fe, kids);
        }

        fe.apRoundTripped = ImportAppearances(fe.entry, fe.dict);

        if (fe.parent is null)
        {
            AttachImportedRootField(fe, name);
        }
        return fe.dict;
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
        var ja = new JsonAppearanceState();
        ja.stateEl = stateEl;
        if (!ja.stateEl.TryGetProperty("Content", out var cEl)
            || cEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        try { ja.bytes = System.Convert.FromBase64String(cEl.GetString()!); }
        catch { return null; }

        ja.sd = new PdfDictionary();
        ja.sd.Set("Type", new PdfName("XObject"));
        ja.sd.Set("Subtype", new PdfName("Form"));

        ReadAppearanceBBox(ja);
        ReadAppearanceMatrix(ja);

        ja.fontDict = new PdfDictionary();
        ReadAppearanceFonts(ja);
        ja.resources = new PdfDictionary();
        if (ja.fontDict.Count > 0) ja.resources.Set("Font", ja.fontDict);

        // Rebuild /Resources/XObject image entries (e.g. a barcode field's
        // pre-rendered bars) from the captured decoded samples, so the
        // round-tripped content's Do operators draw again.
        ReadAppearanceImages(ja);
        ja.sd.Set("Resources", ja.resources);

        return new PdfStream(ja.sd, ja.bytes);
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
            acroForm.Set("DA", new PdfString(Compat.Latin1.GetBytes(da.GetString()!)));
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
