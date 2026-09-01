using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class Field
{
    /// <summary>Resolve the 1-based owning page index for a widget/field dict by
    /// its /P entry (walking /Parent), then by scanning each page's /Annots.
    /// Returns -1 when no owning page is found.</summary>
    private int ResolvePageIndexFor(PdfDictionary dict)
    {
        // The widget annotation dicts this field is drawn by: the field dict
        // itself (merged field+widget) plus any pure-widget /Kids (a field whose
        // visual widget is a separate object). A /Kids entry that carries its own
        // /T is a child *field*, not a widget of this field, so it is excluded —
        // a non-terminal (group) field is not itself on any single page.
        var widgets = new List<PdfDictionary> { dict };
        if (_reader.Resolve(dict.Get("Kids")) is Core.PdfArray kids)
            foreach (var kid in kids)
                if (_reader.ResolveDict(kid) is { } kidDict && kidDict.Get("T") is null)
                    widgets.Add(kidDict);

        var pageDict = _reader.ResolveDict(dict.Get("P"));
        // A missing /P on the field: try each widget's /P, then walk /Parent up.
        foreach (var w in widgets)
        {
            if (pageDict is not null) break;
            pageDict = _reader.ResolveDict(w.Get("P"));
        }
        if (pageDict is null)
        {
            var parent = _reader.ResolveDict(dict.Get("Parent"));
            while (parent is not null && pageDict is null)
            {
                pageDict = _reader.ResolveDict(parent.Get("P"));
                parent = _reader.ResolveDict(parent.Get("Parent"));
            }
        }
        var pages = new PageCollection(_reader);
        if (pageDict is not null)
        {
            for (var i = 1; i <= pages.Count; i++)
                if (ReferenceEquals(pages[i].Dict, pageDict)) return i;
        }
        for (var i = 1; i <= pages.Count; i++)
        {
            var annots = _reader.Resolve(pages[i].Dict.Get("Annots")) as Core.PdfArray;
            if (annots is null) continue;
            foreach (var item in annots)
            {
                var annotDict = _reader.ResolveDict(item);
                if (widgets.Any(w => ReferenceEquals(annotDict, w)))
                    return i;
            }
        }
        return -1;
    }

    private PdfStream? ResolveNormalAppearanceStream(PdfDictionary dict)
    {
        var ap = _reader.ResolveDict(dict.Get("AP"));
        if (ap is null) return null;
        var nObj = _reader.Resolve(ap.Get("N"));
        if (nObj is PdfStream direct) return direct;
        if (nObj is PdfDictionary stateDict)
        {
            // /AP/N is a state-keyed dict (e.g. /Yes /Off for checkboxes).
            // Pick the on-state stream — anything that isn't /Off — falling
            // back to the first stream we find.
            PdfStream? firstAny = null;
            foreach (var key in stateDict.Keys)
            {
                var resolved = _reader.ResolveStream(stateDict.Get(key));
                if (resolved is null) continue;
                firstAny ??= resolved;
                if (key != "Off") return resolved;
            }
            return firstAny;
        }
        return null;
    }

    /// <summary>Recompute every field listed in the AcroForm /CO (calculation
    /// order) whose /AA/C calculate action is a recognised built-in, persisting the
    /// result into each field's /V and appearance. Mirrors Acrobat's "calculate"
    /// event that fires whenever any field value changes. Re-entrancy guarded.</summary>
    private protected void TriggerRecalculation()
    {
        if (_recalculating) return;
        PdfDictionary? acroForm;
        try { acroForm = _reader.ResolveDict(_reader.Catalog?.Get("AcroForm")); }
        catch (System.InvalidOperationException) { return; }
        if (acroForm is null) return;
        if (_reader.Resolve(acroForm.Get("CO")) is not PdfArray co || co.Count == 0) return;

        void MarkFieldTreeDirty(PdfDictionary fieldDict)
        {
            if (OwnerDocument is null) return;
            var fn = OwnerDocument.FindObjectNumber(fieldDict);
            if (fn >= 0) OwnerDocument.MarkDirty(fn, fieldDict);
            if (_reader.Resolve(fieldDict.Get("Kids")) is PdfArray kids)
                foreach (var k in kids)
                    if (_reader.ResolveDict(k) is PdfDictionary kd)
                    {
                        var kn = k is PdfIndirectRef kr ? kr.ObjectNumber : OwnerDocument.FindObjectNumber(kd);
                        if (kn >= 0) OwnerDocument.MarkDirty(kn, kd);
                    }
        }

        _recalculating = true;
        try
        {
            foreach (var entry in co)
            {
                if (_reader.ResolveDict(entry) is not PdfDictionary fieldDict) continue;
                // A field whose value was explicitly assigned keeps it — the
                // calculate event never overwrites a direct assignment.
                if (IsExplicitlyAssigned(fieldDict)) continue;
                var computed = FieldCalculateScript.ComputeValue(fieldDict, _reader);
                if (computed is null) continue;
                new TextBoxField(fieldDict, _reader).ApplyCalculatedValue(computed);
                MarkFieldTreeDirty(fieldDict);
            }
        }
        finally { _recalculating = false; }
    }

    /// <summary>
    /// Set the field value. For text fields, sets /V as a PdfString.
    /// For checkboxes, sets /V and /AS as a PdfName.
    /// </summary>
    protected virtual void SetValue(string? value)
    {
        MarkExplicitlyAssigned(_dict);
        if (value is null)
        {
            // Clearing the value drops the /V key entirely — a field with no value
            // must not carry a /V (a null-valued /V still reports HasKey("V") == true).
            _dict.Remove("V");
        }
        else
        {
            _dict.Set("V", EncodePdfTextString(value));
        }
        // Mark dirty for incremental save
        if (OwnerDocument is not null && ObjectNumber >= 0)
            OwnerDocument.MarkDirty(ObjectNumber, _dict);
    }

    /// <summary>
    /// Encode a string as a PDF text string: Latin1 for ASCII-safe text,
    /// UTF-16BE with BOM for text containing non-Latin1 characters.
    /// </summary>
    internal static PdfString EncodePdfTextString(string value)
    {
        // Check if all characters fit in Latin1 (0x00–0xFF)
        bool needsUnicode = false;
        foreach (char c in value)
        {
            if (c > 0xFF) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return new PdfString(System.Text.Encoding.Latin1.GetBytes(value));

        // UTF-16BE with BOM prefix (0xFE 0xFF)
        byte[] utf16 = System.Text.Encoding.BigEndianUnicode.GetBytes(value);
        byte[] withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        utf16.CopyTo(withBom, 2);
        return new PdfString(withBom);
    }

    /// <summary>Set the partial name (/T entry) of this field.</summary>
    internal void SetPartialName(string name)
    {
        _dict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
    }

    private FieldType DetermineType()
    {
        var ft = GetInheritedName("FT");
        var flags = (int)GetInheritedInt("Ff");

        // NB: Field now inherits Annotation.FieldType (a string /FT accessor), which
        // shadows the FieldType enum in expression position — so qualify the enum.
        return ft switch
        {
            "Tx" => Aspose.Pdf.Forms.FieldType.Text,
            "Btn" => (flags & (1 << 16)) != 0 ? Aspose.Pdf.Forms.FieldType.Button      // bit 17: Pushbutton
                   : (flags & (1 << 15)) != 0 ? Aspose.Pdf.Forms.FieldType.RadioButton // bit 16: Radio
                   : Aspose.Pdf.Forms.FieldType.CheckBox,
            // The /Ff combo bit (bit 18, 0-based 17) splits Ch into combo / list.
            // The form-JSON round-trip and facade callers rely on this so a re-imported
            // combo rebuilds as ComboBoxField, not ListBoxField.
            "Ch" => (flags & (1 << 17)) != 0 ? Aspose.Pdf.Forms.FieldType.ComboBox
                                              : Aspose.Pdf.Forms.FieldType.ListBox,
            "Sig" => Aspose.Pdf.Forms.FieldType.Signature,
            _ => Aspose.Pdf.Forms.FieldType.Unknown,
        };
    }

    private string? GetInheritedName(string key)
    {
        var name = _dict.GetName(key);
        if (name is not null) return name;

        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            name = parent.GetName(key);
            if (name is not null) return name;
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }
        return null;
    }

    private long GetInheritedInt(string key)
    {
        if (_dict.ContainsKey(key)) return _dict.GetInt(key);

        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            if (parent.ContainsKey(key)) return parent.GetInt(key);
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }
        return 0;
    }

    private string? BuildFullName()
    {
        var parts = new List<string>();
        var current = _dict;
        while (current is not null)
        {
            var t = current.Get("T");
            if (t is PdfString s)
                parts.Add(s.ToText());
            else if (t is PdfName n)
                parts.Add(n.Value);
            current = _reader.ResolveDict(current.Get("Parent"));
        }
        parts.Reverse();
        return parts.Count > 0 ? string.Join(".", parts) : null;
    }

    private new string? GetString(string key)
    {
        var obj = _reader.Resolve(_dict.Get(key));
        return obj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null,
        };
    }

    /// <summary>Build a PdfArray for a rectangle.</summary>
    internal static PdfArray MakeRectArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    internal static Field Create(PdfDictionary dict, PdfReader reader)
    {
        var ft = dict.GetName("FT");
        // Check parent for inherited FT
        if (ft is null)
        {
            var parent = reader.ResolveDict(dict.Get("Parent"));
            while (parent is not null && ft is null)
            {
                ft = parent.GetName("FT");
                parent = reader.ResolveDict(parent.Get("Parent"));
            }
        }

        return ft switch
        {
            "Tx" => CreateTextField(dict, reader),
            "Btn" => CreateButtonField(dict, reader),
            "Ch" => CreateChoiceField(dict, reader),
            "Sig" => new SignatureField(dict, reader),
            _ => new Field(dict, reader),
        };
    }

    private static TextBoxField CreateTextField(PdfDictionary dict, PdfReader reader)
    {
        if (BarcodeField.IsBarcode(dict, reader)) return new BarcodeField(dict, reader);
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        return (flags & (1 << 25)) != 0
            ? new RichTextBoxField(dict, reader)
            : new TextBoxField(dict, reader);
    }

    private static Field CreateButtonField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        if ((flags & (1 << 16)) != 0) return new ButtonField(dict, reader); // bit 17: Pushbutton
        if ((flags & (1 << 15)) != 0) return new RadioButtonField(dict, reader); // bit 16: Radio
        return new CheckboxField(dict, reader);
    }

    private static ChoiceField CreateChoiceField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        if ((flags & (1 << 17)) != 0) return new ComboBoxField(dict, reader); // bit 18: Combo
        return new ListBoxField(dict, reader);
    }

    private static long GetInheritedInt(PdfDictionary dict, PdfReader reader, string key)
    {
        if (dict.ContainsKey(key)) return dict.GetInt(key);
        var parent = reader.ResolveDict(dict.Get("Parent"));
        while (parent is not null)
        {
            if (parent.ContainsKey(key)) return parent.GetInt(key);
            parent = reader.ResolveDict(parent.Get("Parent"));
        }
        return 0;
    }
}
