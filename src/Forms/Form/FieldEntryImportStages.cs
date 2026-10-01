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
    /// <summary>The stages of one imported field entry: the field type, the choice entries, the widget rectangle, the child fields and the root-field attach.</summary>
    private void AttachImportedRootField(FieldEntryImportState fe, string? name)
    {
        var field = Field.Create(fe.dict, fe.reader);
        field.OwnerDocument = OwnerDocument;
        _fields.Add(field);
        AddToAcroFormFields(fe.reader, fe.dict);
        // Draw the field's appearance and place its widget(s) on the page so
        // the imported form renders, mirroring Form.Add for a created field.
        // Skip the generator pass when /AP was round-tripped from JSON --
        // the per-type generators check for /AP themselves but radio's path
        // overwrites kids unconditionally; skipping at this level is safer.
        if (!fe.apRoundTripped && !fe.childApRoundTripped) field.GenerateAppearance();
        PlaceFieldWidgets(fe.dict, fe.reader, fe.pageIndex);
        fe.results.Add(new FieldSerializationResult
        {
            FieldFullName = name!,
            FieldSerializationStatus = FieldSerializationStatus.Success,
        });
    }

    /// <summary></summary>
    private void ImportChildFields(FieldEntryImportState fe, System.Text.Json.JsonElement kids)
    {
        var kidsArr = new PdfArray();
        foreach (var kid in kids.EnumerateArray())
        {
            var kidDict = ImportFieldEntry(kid, fe.reader, fe.dict, fe.results);
            if (kidDict is not null)
            {
                kidsArr.Add(kidDict);
                // A radio option widget carries its own round-tripped /AP; its
                // presence means the group must NOT be regenerated below (the
                // radio generator overwrites kid appearances unconditionally).
                if (kidDict.ContainsKey("AP")) fe.childApRoundTripped = true;
            }
        }
        if (kidsArr.Count > 0) fe.dict.Set("Kids", kidsArr);
    }

    /// <summary></summary>
    private void ImportChoiceEntries(FieldEntryImportState fe)
    {
        if (fe.entry.TryGetProperty("Options", out var optEl)
            && optEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var oa = new PdfArray();
            foreach (var o in optEl.EnumerateArray())
                if (o.ValueKind == System.Text.Json.JsonValueKind.String)
                    oa.Add(new PdfString(Compat.Latin1.GetBytes(o.GetString()!)));
            if (oa.Count > 0) fe.dict.Set("Opt", oa);
        }

        // Push-button normal caption (/MK /CA) — ButtonField.GenerateAppearance
        // centres this string on the button face.
        if (fe.entry.TryGetProperty("NormalCaption", out var ncEl)
            && ncEl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var mk = new PdfDictionary();
            mk.Set("CA", new PdfString(Compat.Latin1.GetBytes(ncEl.GetString()!)));
            fe.dict.Set("MK", mk);
        }
    }

    /// <summary></summary>
    private void ImportFieldType(FieldEntryImportState fe, System.Text.Json.JsonElement ftEl)
    {
        var ftName = ftEl.GetString();
        if (MapFieldTypeToFt(ftName) is { } ft)
            fe.dict.Set("FT", new PdfName(ft));
        // Restore the field-flag bits that distinguish the concrete /Btn and
        // /Ch subtypes (radio / push-button / combo) so the field rebuilds as
        // the right type — the flat export carries only the FieldType name.
        var ff = FieldTypeFlags(ftName);
        if (ff != 0) fe.dict.Set("Ff", new PdfInteger(ff));
    }

    /// <summary></summary>
    private void ImportWidgetRect(FieldEntryImportState fe, System.Text.Json.JsonElement rc)
    {
        var ra = new PdfArray();
        var idx = 0;
        foreach (var num in rc.EnumerateArray())
        {
            if (idx++ >= 4) break;
            ra.Add(new PdfReal(num.GetDouble()));
        }
        fe.dict.Set("Rect", ra);
        fe.dict.Set("Type", new PdfName("Annot"));
    }
}
