using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class Form
{
    /// <summary>
    /// Get the maximum character count of a text field by name.
    /// Returns 0 when the field is not a TextBoxField or has no /MaxLen.
    /// </summary>
    public int GetFieldLimit(string fieldName)
    {
        if (_doc?.Form is null) return 0;
        var field = _doc.Form.FindFieldOrNull(fieldName);
        return field is TextBoxField tb ? tb.MaxLen : 0;
    }

    /// <summary>Get a field value by name.</summary>
    public string? GetField(string fieldName)
    {
        if (_doc is null) return null;
        // For XFA forms, read from XFA datasets. A datasets node that no template field
        // binds to (an imported record with a name the form does not define) is not a
        // field value - such a path reads back empty, as the widget side does.
        if (_doc.Form.IsXfa)
        {
            var val = _doc.Form.GetXfaFieldValue(fieldName);
            if (val is not null && (val.Length == 0 || _doc.Form.XfaTemplateFieldExists(fieldName)))
                return val;
        }
        var field = _doc.Form.FindFieldOrNull(fieldName);
        // A name that resolves to no field reads as an EMPTY string, the same as an
        // existing field with no value (e.g. an unsigned signature field): the facade
        // never reports null for a bound document - callers test the value, not presence.
        if (field is null) return string.Empty;
        return field.Value ?? string.Empty;
    }

    /// <summary>Get the type of a field by name.</summary>
    public FieldType GetFieldType(string fieldName)
    {
        if (_doc is null) return FieldType.InvalidNameOrType;
        var field = _doc.Form.FindFieldOrNull(fieldName);
        if (field is not null)
        {
            // An image field is a push button laid out for an icon (/MK /TP 1) or
            // one whose appearance draws an image XObject (e.g. after FillImageField).
            // Detect that before the generic FT mapping, which reports PushButton.
            if (field is ButtonField && IsImageButton(field)) return FieldType.Image;
            // A barcode field is a text field carrying a paper-metadata dictionary
            // (/PMD, PDF 32000 §12.7.4.3) — the FT mapping alone reports Text.
            if (field.Type == Forms.FieldType.Text && field.Dict.ContainsKey("PMD"))
                return FieldType.Barcode;
            return MapToFacadeFieldType(field.Type);
        }
        // Dynamic XFA forms carry no AcroForm twin for their fields, so resolve the
        // type from the XFA template's <ui> widget instead.
        if (_doc.Form.IsXfa)
            return MapXfaUiKindToFieldType(_doc.Form.GetXfaFieldUiKind(fieldName));
        return FieldType.InvalidNameOrType;
    }

    /// <summary>Returns the XFA template node for the named field (or null when
    /// the document has no XFA / the field is absent). Delegates to the
    /// document-level form's XFA template lookup.</summary>
    public System.Xml.XmlNode? GetFieldTemplate(string fieldName)
        => _doc?.Form?.XFA?.GetFieldTemplate(fieldName);

    /// <summary>True when a button field is an image (icon) button: it carries the
    /// icon-only layout (/MK /TP 1) or its normal appearance draws an image XObject.</summary>
    private bool IsImageButton(Forms.Field field)
    {
        var reader = _doc!.Reader;
        var widgets = field.AllKids().ToList();
        if (widgets.Count == 0) widgets.Add(field.Dict);
        foreach (var w in widgets)
        {
            var mk = reader.ResolveDict(w.Get("MK"));
            if (mk is not null && mk.ContainsKey("TP") && mk.GetInt("TP") == 1) return true;

            var ap = reader.ResolveDict(w.Get("AP"));
            var n = ap is null ? null : reader.ResolveStream(ap.Get("N"));
            var res = n is null ? null : reader.ResolveDict(n.Dict.Get("Resources"));
            var xobj = res is null ? null : reader.ResolveDict(res.Get("XObject"));
            if (xobj is not null)
                foreach (var k in xobj.Keys)
                    if (reader.ResolveStream(xobj.Get(k)) is { } x && x.Dict.GetName("Subtype") == "Image")
                        return true;
        }
        return false;
    }

    private static FieldType MapXfaUiKindToFieldType(string? uiKind) => uiKind switch
    {
        "textEdit" => FieldType.Text,
        "passwordEdit" => FieldType.Text,
        "numericEdit" => FieldType.Numeric,
        "dateTimeEdit" => FieldType.DateTime,
        "choiceList" => FieldType.ComboBox,
        "choiceListMulti" => FieldType.ListBox,
        "button" => FieldType.PushButton,
        "checkButton" => FieldType.CheckBox,
        "exclGroup" => FieldType.Radio,
        "signature" => FieldType.Signature,
        "imageEdit" => FieldType.Image,
        "barcode" => FieldType.Barcode,
        _ => FieldType.InvalidNameOrType,
    };

    private static FieldType MapToFacadeFieldType(Forms.FieldType t)
    {
        return t switch
        {
            Forms.FieldType.Text => FieldType.Text,
            Forms.FieldType.Button => FieldType.PushButton,
            Forms.FieldType.CheckBox => FieldType.CheckBox,
            Forms.FieldType.RadioButton => FieldType.Radio,
            Forms.FieldType.Choice => FieldType.ListBox,
            Forms.FieldType.ListBox => FieldType.ListBox,
            Forms.FieldType.ComboBox => FieldType.ComboBox,
            Forms.FieldType.Signature => FieldType.Signature,
            Forms.FieldType.Barcode => FieldType.Barcode,
            Forms.FieldType.Numeric => FieldType.Numeric,
            Forms.FieldType.DateTime => FieldType.DateTime,
            _ => FieldType.InvalidNameOrType,
        };
    }

    /// <summary>
    /// Get visual facade properties for a field.
    /// Returns null if the field is not found.
    /// </summary>
    public FormFieldFacade? GetFieldFacade(string fieldName)
    {
        if (_doc is null) return null;
        var field = _doc.Form.FindFieldOrNull(fieldName);

        // For XFA forms, field may not exist in AcroForm — build facade from XFA template
        if (field is null && _doc.Form.IsXfa)
        {
            var xfaCap = _doc.Form.GetXfaFieldCaption(fieldName);
            return new FormFieldFacade { Caption = xfaCap ?? "" };
        }

        if (field is null) return null;

        // An XFA form's caption is what the page SHOWS next to the widget: in a tagged
        // document the caption's marked content is the widget's sibling in the structure
        // tree and wins over the template's <caption> (they differ once the page content
        // has been edited after generation); an untagged form keeps the template caption.
        // /TU is the description, not the caption - it is the last resort.
        string? fieldCaption = null;
        if (_doc.Form.IsXfa)
            fieldCaption = _doc.Form.GetTaggedCaption(field) ?? _doc.Form.GetXfaFieldCaption(fieldName);
        fieldCaption ??= field.AlternateName;

        var fieldRect = field.Rect;
        var facade = new FormFieldFacade { Caption = fieldCaption };
        if (fieldRect is not null)
        {
            facade.Box = new System.Drawing.Rectangle(
                (int)fieldRect.LLX,
                (int)fieldRect.LLY,
                (int)(fieldRect.URX - fieldRect.LLX),
                (int)(fieldRect.URY - fieldRect.LLY));
            // Position is the 1-based [page, llx, lly, urx, ury] rectangle tuple the
            // facade exposes; element 0 carries the owning page number.
            facade.Position = new float[]
            {
                System.Math.Max(0, field.PageIndex),
                (float)fieldRect.LLX, (float)fieldRect.LLY,
                (float)fieldRect.URX, (float)fieldRect.URY,
            };
        }

        // Try to extract font info from DA (default appearance)
        var da = GetDaString(field.Dict) ?? GetInheritedDa(field.Dict);
        if (da is not null)
        {
            // DA format: "/FontName fontSize Tf" or "0 g /FontName fontSize Tf"
            var parts = da.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "Tf" && i >= 2)
                {
                    facade.CustomFont = parts[i - 2].TrimStart('/');
                    if (float.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var size))
                        facade.FontSize = size;
                }
            }
        }

        // Page number: prefer the field's own PageIndex — it resolves a parent field's
        // page via its widget kids / inherited /P, whereas the annotation scan below only
        // matches a field dict that is itself in a page's /Annots.
        if (field.PageIndex >= 1)
        {
            facade.PageNumber = field.PageIndex;
        }
        else
        {
            for (int p = 1; p <= _doc.PageCount; p++)
            {
                var page = _doc.Pages.At(p);
                foreach (var annot in page.Annotations)
                {
                    if (annot.Dict == field.Dict)
                    {
                        facade.PageNumber = p;
                        break;
                    }
                }
                if (facade.PageNumber > 0) break;
            }
        }

        // Position mirrors FormFieldFacade.Position: [page, llx, lly, urx, ury].
        if (fieldRect is not null)
            facade.Position = new[]
            {
                (float)facade.PageNumber,
                (float)fieldRect.LLX, (float)fieldRect.LLY,
                (float)fieldRect.URX, (float)fieldRect.URY,
            };

        return facade;
    }

    /// <summary>
    /// Get the current value of a button (radio button) field.
    /// </summary>
    public string? GetButtonOptionCurrentValue(string fieldName)
    {
        if (_doc is null) return null;
        var field = _doc.Form.FindFieldOrNull(fieldName);
        if (field is not null)
        {
            var v = field.Value;
            return v is not null ? "/" + v.TrimStart('/') : null;
        }
        // XFA fallback: read current value from XFA datasets
        if (_doc.Form.IsXfa)
        {
            var v = _doc.Form.GetXfaFieldValue(fieldName);
            if (v is not null) return "/" + v.TrimStart('/');
            // No value set → Off
            return "/Off";
        }
        return null;
    }

    /// <summary>
    /// Get the option values of a button (radio button) field.
    /// Returns a dictionary of option export values to display labels.
    /// </summary>
    public Dictionary<string, string>? GetButtonOptionValues(string fieldName)
    {
        if (_doc is null) return null;
        var field = _doc.Form.FindFieldOrNull(fieldName);

        var result = new Dictionary<string, string>();
        if (field is not null)
        {
            CollectButtonOptions(field.Dict, result);

            // A named radio GROUP resolves to the group dict itself, which carries no /AP —
            // the option appearances live on its kid widgets, so read them from the group's
            // own /Kids before falling back to the sibling walk below.
            if (result.Count == 0)
            {
                var ownKids = _doc.Reader.Resolve(field.Dict.Get("Kids")) as Core.PdfArray;
                if (ownKids is not null)
                    foreach (var kidRef in ownKids)
                    {
                        var kidDict = _doc.Reader.ResolveDict(kidRef);
                        if (kidDict is not null) CollectButtonOptions(kidDict, result);
                    }
            }

            // Also check parent's Kids for radio groups
            if (result.Count == 0)
            {
                var parent = _doc.Reader.ResolveDict(field.Dict.Get("Parent"));
                if (parent is not null)
                {
                    var kids = _doc.Reader.Resolve(parent.Get("Kids")) as Core.PdfArray;
                    if (kids is not null)
                    {
                        foreach (var kidRef in kids)
                        {
                            var kidDict = _doc.Reader.ResolveDict(kidRef);
                            if (kidDict is not null) CollectButtonOptions(kidDict, result);
                        }
                    }
                }
            }
        }

        // XFA forms: template items are authoritative — replace AcroForm results
        if (_doc.Form.IsXfa)
        {
            var items = _doc.Form.GetXfaRadioButtonItems(fieldName);
            if (items is not null)
            {
                result.Clear();
                foreach (var item in items)
                    result[item] = item;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private void CollectButtonOptions(Core.PdfDictionary dict, Dictionary<string, string> result)
    {
        var apDict = _doc!.Reader.ResolveDict(dict.Get("AP"));
        if (apDict is null) return;
        var nObj = _doc.Reader.Resolve(apDict.Get("N"));
        if (nObj is Core.PdfDictionary nDict)
        {
            foreach (var key in nDict.Keys)
            {
                if (key != "Off" && !result.ContainsKey(key))
                    result[key] = key;
            }
        }
    }

    private static string? GetDaString(Core.PdfDictionary dict)
    {
        var obj = dict.Get("DA");
        return obj is Core.PdfString s ? s.ToText() : null;
    }

    private string? GetInheritedDa(Core.PdfDictionary dict)
    {
        var parent = _doc!.Reader.ResolveDict(dict.Get("Parent"));
        while (parent is not null)
        {
            var val = GetDaString(parent);
            if (val is not null) return val;
            parent = _doc.Reader.ResolveDict(parent.Get("Parent"));
        }
        return null;
    }

    /// <summary>
    /// Get the full qualified field name. Returns the field's FullName (dotted path for nested fields),
    /// or the PartialName if FullName is not available, or the input name as fallback.
    /// </summary>
    public string GetFullFieldName(string fieldName)
    {
        if (_doc is null) return fieldName;
        var field = _doc.Form.FindFieldOrNull(fieldName);
        return field?.FullName ?? field?.PartialName ?? fieldName;
    }

    /// <summary>True if a field with the given full name exists in the bound form.</summary>
    public bool HasField(string fullName)
    {
        if (_doc is null) return false;
        return _doc.Form.FindFieldOrNull(fullName) is not null;
    }

    /// <summary>Get the PropertyFlag set on a field (ReadOnly / Required / NoExport).</summary>
    public PropertyFlag GetFieldFlag(string fieldName)
    {
        if (_doc?.Form is null) return PropertyFlag.InvalidFlag;
        var f = _doc.Form.FindFieldOrNull(fieldName);
        if (f is null) return PropertyFlag.InvalidFlag;
        int ff = (int)f.Dict.GetInt("Ff");
        if ((ff & 1) != 0) return PropertyFlag.ReadOnly;
        if ((ff & 2) != 0) return PropertyFlag.Required;
        if ((ff & 4) != 0) return PropertyFlag.NoExport;
        return PropertyFlag.InvalidFlag;
    }

    /// <summary>Return the field's rich-text contents (the /RV entry) or null when absent.</summary>
    public string? GetRichText(string fieldName)
    {
        if (_doc?.Form is null) return null;
        var f = _doc.Form.FindFieldOrNull(fieldName);
        if (f is null) return null;
        var rv = f.Dict.Get("RV");
        return rv is Core.PdfString s ? s.ToText() : null;
    }

    /// <summary>Return the SubmitFormFlag recorded on a submit-button field, read back
    /// from its /A submit-form action's /Flags bitmask.</summary>
    public SubmitFormFlag GetSubmitFlags(string fieldName)
    {
        if (_doc?.Form is null) return SubmitFormFlag.Fdf;
        var f = _doc.Form.FindFieldOrNull(fieldName);
        if (f is null) return SubmitFormFlag.Fdf;
        var action = _doc.Reader.ResolveDict(f.Dict.Get("A"));
        var flags = action?.Get("Flags") is Core.PdfInteger n ? (int)n.Value : 0;
        const int xfdf = Annotations.SubmitFormAction.Xfdf;
        const int comments = Annotations.SubmitFormAction.IncludeAnnotations;
        return flags switch
        {
            Annotations.SubmitFormAction.SubmitPdf => SubmitFormFlag.Pdf,
            xfdf | comments => SubmitFormFlag.XfdfWithComments,
            xfdf => SubmitFormFlag.Xfdf,
            Annotations.SubmitFormAction.ExportFormat => SubmitFormFlag.Html,
            comments => SubmitFormFlag.FdfWithComments,
            _ => SubmitFormFlag.Fdf,
        };
    }

    /// <summary>True when the field has the Required flag set.</summary>
    public bool IsRequiredField(string fieldName)
    {
        if (_doc?.Form is null) return false;
        var f = _doc.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        return ((int)f.Dict.GetInt("Ff") & 2) != 0;
    }
}
