using System.IO;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class FormEditor
{
    /// <summary>
    /// Add a field of the given <paramref name="fieldType"/> at the requested rectangle on
    /// <paramref name="pageNum"/>. Returns true on success.
    /// </summary>
    public bool AddField(FieldType fieldType, string fieldName, int pageNum, float llx, float lly, float urx, float ury)
        => AddField(fieldType, fieldName, initValue: null, pageNum, llx, lly, urx, ury);

    /// <summary>
    /// Add a field with an initial value (text fields and combo boxes). Returns true on success.
    /// </summary>
    public bool AddField(FieldType fieldType, string fieldName, string? initValue, int pageNum, float llx, float lly, float urx, float ury)
    {
        if (_document is null) return false;
        var page = _document.Pages[pageNum];
        var rect = new Rectangle(llx, lly, urx, ury);
        var builder = new FormFieldBuilder(_document);
        switch (fieldType)
        {
            case FieldType.Text:
            case FieldType.MultiLineText:
            case FieldType.Numeric:
            case FieldType.DateTime:
                builder.AddTextField(page, fieldName, rect);
                _document.Form.SyncNewlyAddedFields();
                if (initValue is not null) SetField(fieldName, initValue);
                return true;
            case FieldType.CheckBox:
                builder.AddCheckBox(page, fieldName, rect, isChecked: false);
                _document.Form.SyncNewlyAddedFields();
                // A facade with visible decoration applies to the checkbox at add time.
                // Unlike DecorateField, the add-time face keys its glyph (and /MK /CA)
                // by the standard caption character for the style ("4" check, "8" cross…).
                if ((Facade.BackgroundColor.A != 0 || Facade.BorderColor.A != 0 || Facade.TextColor.A != 0
                     || Facade.BorderWidth > 0 || Facade.ButtonStyle != FormFieldFacade.CheckBoxStyleUndefined)
                    && _document.Form[fieldName] is Forms.Field newCb)
                {
                    if (Facade.BorderStyle != FormFieldFacade.BorderStyleUndefined || Facade.BorderWidth > 0)
                        ApplyBorderStyle(newCb);
                    DecorateCheckBox(newCb, useCaptionGlyph: true);
                }
                return true;
            case FieldType.ComboBox:
                builder.AddComboBox(page, fieldName, rect, BuildOptionDisplay(), initValue);
                _document.Form.SyncNewlyAddedFields();
                ApplyExportItems(fieldName);
                if (initValue is not null) SetField(fieldName, initValue);
                return true;
            case FieldType.ListBox:
                builder.AddListBox(page, fieldName, rect, BuildOptionDisplay(), initValue);
                _document.Form.SyncNewlyAddedFields();
                ApplyExportItems(fieldName);
                if (initValue is not null) SetField(fieldName, initValue);
                return true;
            case FieldType.Signature:
                builder.AddSignatureField(page, fieldName, rect);
                _document.Form.SyncNewlyAddedFields();
                return true;
            case FieldType.Image:
            {
                // An image field is a push button laid out for an icon. Mark it with
                // the icon-only layout (/MK /TP 1) so GetFieldType reports Image (vs a
                // captioned PushButton); FillImageField later writes the icon into /AP/N.
                var imgBtn = new ButtonField(page, rect);
                imgBtn.PartialName = fieldName;
                var mk = new PdfDictionary();
                mk.Set("TP", new PdfInteger(1));
                imgBtn.Dict.Set("MK", mk);
                _document.Form.Add(imgBtn, pageNum);
                return true;
            }
            case FieldType.Radio:
            case FieldType.PushButton:
            case FieldType.Barcode:
            case FieldType.InvalidNameOrType:
            default:
                // Not yet implemented for these field types.
                return false;
        }
    }

    /// <summary>The display strings for a ListBox / ComboBox built from
    /// <see cref="ExportItems"/> (each entry is [export, display]); empty when none set.</summary>
    private string[] BuildOptionDisplay()
    {
        if (ExportItems is null) return System.Array.Empty<string>();
        var list = new System.Collections.Generic.List<string>(ExportItems.Length);
        foreach (var e in ExportItems)
            if (e is { Length: > 0 }) list.Add(e.Length >= 2 ? e[1] : e[0]);
        return list.ToArray();
    }

    /// <summary>Rewrite the field's /Opt as export/display pairs from
    /// <see cref="ExportItems"/> so export values (not just display text) round-trip.</summary>
    private void ApplyExportItems(string fieldName)
    {
        if (ExportItems is null || _document?.Form is null) return;
        if (_document.Form.FindFieldOrNull(fieldName) is not ChoiceField cf) return;
        cf.Options.Clear();
        foreach (var e in ExportItems)
        {
            if (e is { Length: >= 2 }) cf.AddOption(e[0], e[1]);
            else if (e is { Length: 1 }) cf.AddOption(e[0]);
        }
    }

    /// <summary>Add a single item to a ListBox / ComboBox field. The item is its own
    /// export value. For an XFA field the template's items lists are updated too.</summary>
    public void AddListItem(string fieldName, string itemName)
    {
        if (_document?.Form is null) return;
        if (_document.Form.FindFieldOrNull(fieldName) is ChoiceField cf)
            cf.AddOption(itemName);
        if (_document.Form.IsXfa)
            _document.Form.AddXfaListItem(fieldName, itemName, itemName);
    }

    /// <summary>Add a single item with an export-value pair ([display, export]; a
    /// single-element array is both). For an XFA field the template's items lists
    /// are updated too.</summary>
    public void AddListItem(string fieldName, string[] exportName)
    {
        if (_document?.Form is null || exportName is null || exportName.Length == 0) return;
        var display = exportName[0];
        var export = exportName.Length > 1 ? exportName[1] : exportName[0];
        if (_document.Form.FindFieldOrNull(fieldName) is ChoiceField cf)
            cf.AddOption(export, display);
        if (_document.Form.IsXfa)
            _document.Form.AddXfaListItem(fieldName, display, export);
    }

    /// <summary>Remove a single item from a ListBox / ComboBox field. Item removal is not currently implemented; the call is a no-op kept for API compatibility.</summary>
    public void DelListItem(string fieldName, string itemName)
    {
        _ = fieldName;
        _ = itemName;
    }

    /// <summary>
    /// Add a Submit button. The implementation creates a push-button field with the requested
    /// label and target rectangle; the submit URL is stored on the field's action dictionary.
    /// </summary>
    public void AddSubmitBtn(string fieldName, int page, string label, string url, float llx, float lly, float urx, float ury)
    {
        if (_document is null) return;
        var pageObj = _document.Pages[page];
        var btn = new ButtonField(pageObj, new Rectangle(llx, lly, urx, ury));
        btn.PartialName = fieldName;
        // The caption draws at half the button height (a 25pt-high button captions
        // at 12.5pt) — the size the appearance generator reads back from /DA, so the
        // DA must be in place BEFORE the caption assignment regenerates the appearance.
        var captionSize = (ury - lly) / 2;
        btn.Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "/Helv {0:0.##} Tf 0 g", captionSize))));
        btn.NormalCaption = label;

        // Wire the button's activation action to a SubmitForm action targeting the URL.
        var action = new SubmitFormAction();
        action.Dict.Set("F", new PdfString(System.Text.Encoding.Latin1.GetBytes(url)));
        action.Flags = SubmitFormAction.Xfdf;
        btn.OnActivated = action;

        _document.Form.Add(btn, page);
    }

    /// <summary>Resolve the field's /A submit-form action, creating an empty one when absent.</summary>
    private PdfDictionary? EnsureSubmitAction(Field field)
    {
        if (_document is null) return null;
        var reader = _document.Reader;
        var a = reader.ResolveDict(field.Dict.Get("A"));
        if (a is null)
        {
            a = new PdfDictionary();
            a.Set("S", new PdfName("SubmitForm"));
            field.Dict.Set("A", a);
            return a;
        }
        if (a.GetName("S") == "SubmitForm") return a;

        // The field already has an activation action of another kind (a JavaScript
        // action, say). That action stays - the submit target chains after it in
        // /Next, which is where a reader fires it and where the public Next
        // collection surfaces it.
        var current = a;
        while (true)
        {
            var next = reader.Resolve(current.Get("Next"));
            if (next is PdfDictionary nd)
            {
                if (nd.GetName("S") == "SubmitForm") return nd;
                current = nd;
                continue;
            }
            if (next is PdfArray na)
            {
                foreach (var item in na)
                    if (reader.ResolveDict(item) is { } d && d.GetName("S") == "SubmitForm")
                        return d;
                var appended = new PdfDictionary();
                appended.Set("S", new PdfName("SubmitForm"));
                na.Add(appended);
                return appended;
            }
            var submit = new PdfDictionary();
            submit.Set("S", new PdfName("SubmitForm"));
            current.Set("Next", submit);
            return submit;
        }
    }

    /// <summary>Map a high-level <see cref="SubmitFormFlag"/> to the PDF /Flags bitmask.</summary>
    private static int MapSubmitFlag(SubmitFormFlag flag) => flag switch
    {
        SubmitFormFlag.Fdf => 0,
        SubmitFormFlag.FdfWithComments => SubmitFormAction.IncludeAnnotations,
        SubmitFormFlag.Html => SubmitFormAction.ExportFormat,
        SubmitFormFlag.Pdf => SubmitFormAction.SubmitPdf,
        SubmitFormFlag.Xfdf => SubmitFormAction.Xfdf,
        SubmitFormFlag.XfdfWithComments => SubmitFormAction.Xfdf | SubmitFormAction.IncludeAnnotations,
        _ => 0,
    };

    /// <summary>Attach a JavaScript action script to a field. Returns true on success.</summary>
    public bool AddFieldScript(string fieldName, string script)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindFieldOrNull(fieldName);
        if (field is null) return false;
        // Set the field's activation action (/A) to a JavaScript action so it round-trips
        // as OnActivated -> JavascriptAction. The JS
        // interpreter is not invoked at view time by this library.
        var action = new PdfDictionary();
        action.Set("S", new PdfName("JavaScript"));
        action.Set("JS", new PdfString(System.Text.Encoding.UTF8.GetBytes(script ?? string.Empty)));
        field.Dict.Set("A", action);
        return true;
    }

    /// <summary>Replace the field's JavaScript action script. Returns true on success.</summary>
    public bool SetFieldScript(string fieldName, string script) => AddFieldScript(fieldName, script);

    /// <summary>Copy a field within the bound document to another page.</summary>
    public void CopyInnerField(string fieldName, string newFieldName, int pageNum)
        => CopyInnerField(fieldName, newFieldName, pageNum, abscissa: float.NaN, ordinate: float.NaN);

    /// <summary>Copy a field within the bound document to another page at the given coordinates.</summary>
    public void CopyInnerField(string fieldName, string newFieldName, int pageNum, float abscissa, float ordinate)
    {
        if (_document?.Form is null) return;
        var src = _document.Form.FindFieldOrNull(fieldName);
        if (src is null) return;
        // Active implementation: a shallow copy that adds a new text field with the same
        // rectangle on the target page; richer per-field-type copying is future work.
        var srcRect = src.Rect ?? new Rectangle(0, 0, 100, 20);
        var rect = float.IsNaN(abscissa) || float.IsNaN(ordinate)
            ? srcRect
            : new Rectangle(abscissa, ordinate, abscissa + (srcRect.URX - srcRect.LLX), ordinate + (srcRect.URY - srcRect.LLY));
        var builder = new FormFieldBuilder(_document);
        builder.AddTextField(_document.Pages[pageNum], newFieldName, rect);
    }

    /// <summary>Copy a field from another PDF file into the bound document at the same position.</summary>
    public void CopyOuterField(string srcFileName, string fieldName)
        => CopyOuterField(srcFileName, fieldName, pageNum: 1);

    /// <summary>Copy a field from another PDF file into the bound document at a specified page.</summary>
    public void CopyOuterField(string srcFileName, string fieldName, int pageNum)
        => CopyOuterField(srcFileName, fieldName, pageNum, abscissa: float.NaN, ordinate: float.NaN);

    /// <summary>Copy a field from another PDF file at explicit coordinates.</summary>
    public void CopyOuterField(string srcFileName, string fieldName, int pageNum, float abscissa, float ordinate)
    {
        if (_document is null) return;
        using var src = Document.Open(File.ReadAllBytes(srcFileName));
        if (src.Form is null) return;
        var srcField = src.Form.FindFieldOrNull(fieldName);
        if (srcField is null) return;
        var srcRect = srcField.Rect ?? new Rectangle(0, 0, 100, 20);
        var rect = float.IsNaN(abscissa) || float.IsNaN(ordinate)
            ? srcRect
            : new Rectangle(abscissa, ordinate, abscissa + (srcRect.URX - srcRect.LLX), ordinate + (srcRect.URY - srcRect.LLY));
        var builder = new FormFieldBuilder(_document);
        var page = _document.Pages[pageNum];
        // Preserve the source field's type so a copied combo/list/checkbox
        // round-trips as the same field kind rather than collapsing to a text
        // field.
        switch (srcField)
        {
            case ComboBoxField combo:
                builder.AddComboBox(page, fieldName, rect,
                    ChoiceOptions(combo),
                    combo.SelectedValues.Count > 0 ? combo.SelectedValues[0] : null);
                break;
            case ListBoxField list:
                builder.AddListBox(page, fieldName, rect,
                    ChoiceOptions(list),
                    list.SelectedValues.Count > 0 ? list.SelectedValues[0] : null);
                break;
            case CheckboxField check:
                builder.AddCheckBox(page, fieldName, rect, check.Checked);
                break;
            default:
                builder.AddTextField(page, fieldName, rect);
                break;
        }
    }

    /// <summary>Read a choice field's option export values (1-based collection).</summary>
    private static string[] ChoiceOptions(ChoiceField field)
    {
        var opts = field.Options;
        var result = new string[opts.Count];
        for (var i = 0; i < opts.Count; i++)
            result[i] = opts[i + 1].Value;
        return result;
    }

    /// <summary>Move a field's bounding box.</summary>
    public bool MoveField(string fieldName, float llx, float lly, float urx, float ury)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindFieldOrNull(fieldName);
        if (field is null) return false;
        // Active: rewrite /Rect on the field dict.
        var rectArr = new PdfArray();
        rectArr.Add(new PdfInteger((long)llx));
        rectArr.Add(new PdfInteger((long)lly));
        rectArr.Add(new PdfInteger((long)urx));
        rectArr.Add(new PdfInteger((long)ury));
        field.Dict.Set("Rect", rectArr);
        return true;
    }

    /// <summary>Rename a form field. Silently no-ops if the field does not exist.</summary>
    public void RenameField(string fieldName, string newFieldName)
    {
        if (_document?.Form is null) return;
        _document.Form.FindFieldOrNull(fieldName)?.SetPartialName(newFieldName);
    }

    /// <summary>Remove a field by name.</summary>
    public void RemoveField(string fieldName)
    {
        if (_document is null) return;
        _document.RemoveFormField(fieldName);
    }

    /// <summary>Remove the action attached to a field.</summary>
    public void RemoveFieldAction(string fieldName)
    {
        if (_document?.Form is null) return;
        _document.Form.FindFieldOrNull(fieldName)?.Dict.Remove("A");
    }

    /// <summary>Set the horizontal alignment of a field's value. Alignment uses <see cref="FormFieldFacade.AlignLeft"/> etc.</summary>
    public bool SetFieldAlignment(string fieldName, int alignment)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        f.Dict.Set("Q", new PdfInteger(alignment));
        return true;
    }

    /// <summary>Set the vertical alignment of a field's value.</summary>
    public bool SetFieldAlignmentV(string fieldName, int alignment)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        // Vertical alignment is recorded on /MK; not honoured by the appearance writer here.
        f.Dict.Set("MK_TV", new PdfInteger(alignment));
        return true;
    }

    /// <summary>Set the visibility / read-only / required flags on a field.</summary>
    public bool SetFieldAppearance(string fieldName, AnnotationFlags flags)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        f.Dict.Set("F", new PdfInteger((int)flags));
        return true;
    }

    /// <summary>Return the visibility / read-only / required flags on a field.</summary>
    public AnnotationFlags GetFieldAppearance(string fieldName)
    {
        if (_document?.Form is null) return AnnotationFlags.None;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return AnnotationFlags.None;
        return (AnnotationFlags)f.Dict.GetInt("F");
    }

    /// <summary>Set ReadOnly / Required / NoExport.</summary>
    public bool SetFieldAttribute(string fieldName, PropertyFlag flag)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        int ff = (int)f.Dict.GetInt("Ff");
        switch (flag)
        {
            case PropertyFlag.ReadOnly: ff |= 1; break;
            case PropertyFlag.Required: ff |= 2; break;
            case PropertyFlag.NoExport: ff |= 4; break;
            default: return false;
        }
        f.Dict.Set("Ff", new PdfInteger(ff));
        return true;
    }

    /// <summary>Set the comb-cell count for a text field (PDF 1.5+).</summary>
    public bool SetFieldCombNumber(string fieldName, int combNumber)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        f.Dict.Set("MaxLen", new PdfInteger(combNumber));
        int ff = (int)f.Dict.GetInt("Ff");
        ff |= 1 << 24; // Comb flag (bit 25)
        f.Dict.Set("Ff", new PdfInteger(ff));
        return true;
    }

    /// <summary>Set the maximum character count of a text field by name.</summary>
    public bool SetFieldLimit(string fieldName, int fieldLimit)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is TextBoxField tb) { tb.MaxLen = fieldLimit; return true; }
        return false;
    }

    /// <summary>Set the SubmitForm action flag on a button field.</summary>
    public bool SetSubmitFlag(string fieldName, SubmitFormFlag submitFormFlag)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        var action = EnsureSubmitAction(f);
        if (action is null) return false;
        action.Set("Flags", new PdfInteger(MapSubmitFlag(submitFormFlag)));
        return true;
    }

    /// <summary>Set the SubmitForm action target URL on a button field.</summary>
    public bool SetSubmitUrl(string fieldName, string url)
    {
        if (_document?.Form is null) return false;

        // For an XFA form the reader-visible submit target lives in the XFA template
        // <submit target> (what GetFieldTemplate exposes), so update it too — the
        // AcroForm SubmitForm /F alone doesn't round-trip through the template.
        bool xfaOk = _document.Form.SetXfaSubmitUrl(fieldName, url);

        bool acroOk = false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is not null)
        {
            var action = EnsureSubmitAction(f);
            if (action is not null)
            {
                action.Set("F", new PdfString(System.Text.Encoding.UTF8.GetBytes(url)));
                acroOk = true;
            }
        }
        return xfaOk || acroOk;
    }

    /// <summary>Convert a single-line text field into a multi-line one.</summary>
    public bool Single2Multiple(string fieldName)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is null) return false;
        int ff = (int)f.Dict.GetInt("Ff");
        ff |= 1 << 12; // Multiline flag (bit 13)
        f.Dict.Set("Ff", new PdfInteger(ff));
        return true;
    }

    /// <summary>Flatten all form fields in the bound document.</summary>
    public void FlattenAllFields()
    {
        if (_document is null) return;
        if (!_document.HasForm) return;
        _document.Form.Flatten(_document);
    }
}
