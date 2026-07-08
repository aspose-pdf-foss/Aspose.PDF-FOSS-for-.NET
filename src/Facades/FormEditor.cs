using System.IO;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for form operations: fill fields, flatten forms, import/export data.
/// </summary>
public sealed class FormEditor : IDisposable
{
    private Document? _document;
    private string? _srcFileName;
    private Stream? _srcStream;
    private string? _destFileName;
    private Stream? _destStream;
    private PdfFormat? _convertTo;

    /// <summary>The document bound to this editor, exposed so it can be
    /// chained into another facade.</summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>Default constructor for stateless byte[]-based API.</summary>
    public FormEditor() { }

    /// <summary>Bind the editor to a document for in-place operations.</summary>
    public FormEditor(Document document)
    {
        _document = document;
    }

    /// <summary>Bind a document and pre-configure an output stream for the parameterless Save.</summary>
    public FormEditor(Document document, Stream destStream)
    {
        _document = document;
        _destStream = destStream;
    }

    /// <summary>Bind a document and pre-configure an output file path for the parameterless Save.</summary>
    public FormEditor(Document document, string destFileName)
    {
        _document = document;
        _destFileName = destFileName;
    }

    /// <summary>Open from an input stream, writing to an output stream on Save.</summary>
    public FormEditor(Stream srcStream, Stream destStream)
    {
        _srcStream = srcStream;
        _destStream = destStream;
        using var ms = new MemoryStream();
        if (srcStream.CanSeek) srcStream.Position = 0;
        srcStream.CopyTo(ms);
        _document = Document.Open(ms.ToArray());
    }

    /// <summary>Open from an input file, writing to an output file on Save.</summary>
    public FormEditor(string srcFileName, string destFileName)
    {
        _srcFileName = srcFileName;
        _destFileName = destFileName;
        _document = Document.Open(File.ReadAllBytes(srcFileName));
    }

    // ── Stateful destination/source properties ────────────────────────────

    /// <summary>Source file path used by the parameterless ctor + Save flow.</summary>
    public string? SrcFileName
    {
        get => _srcFileName;
        set
        {
            _srcFileName = value;
            if (value is not null) _document = Document.Open(File.ReadAllBytes(value));
        }
    }

    /// <summary>Source stream used by the parameterless ctor + Save flow.</summary>
    public Stream? SrcStream
    {
        get => _srcStream;
        set
        {
            _srcStream = value;
            if (value is not null)
            {
                using var ms = new MemoryStream();
                if (value.CanSeek) value.Position = 0;
                value.CopyTo(ms);
                _document = Document.Open(ms.ToArray());
            }
        }
    }

    /// <summary>Destination file path written by the parameterless <see cref="Save()"/>.</summary>
    public string? DestFileName
    {
        get => _destFileName;
        set => _destFileName = value;
    }

    /// <summary>Destination stream written by the parameterless <see cref="Save()"/>.</summary>
    public Stream? DestStream
    {
        get => _destStream;
        set => _destStream = value;
    }

    /// <summary>
    /// Target PDF/A or PDF version for the saved output. Stored only; the save path emits plain PDF.
    /// </summary>
    public PdfFormat ConvertTo
    {
        set => _convertTo = value;
    }

    /// <summary>Visual appearance applied by <see cref="AddField"/> and <see cref="DecorateField()"/>.</summary>
    public FormFieldFacade Facade { get; set; } = new FormFieldFacade();

    /// <summary>Default list of items used by <see cref="AddField"/> when creating ListBox / ComboBox fields.</summary>
    public string[]? Items { get; set; }

    /// <summary>Export-value pairs paired with <see cref="Items"/>.</summary>
    public string[][]? ExportItems { get; set; }

    /// <summary>Radio-button item edge length (in points) used when laying out a radio group.</summary>
    public double RadioButtonItemSize { get; set; } = 12.0;

    /// <summary>Gap (in points) between radio-button items in a group.</summary>
    public float RadioGap { get; set; } = 4f;

    /// <summary>When true, radio-button items are laid out horizontally; otherwise vertically.</summary>
    public bool RadioHoriz { get; set; }

    /// <summary>Submit format used by <see cref="AddSubmitBtn"/> and <see cref="SetSubmitFlag"/>.</summary>
    public SubmitFormFlag SubmitFlag { get; set; } = SubmitFormFlag.Fdf;

    // ── BindPdf overloads ─────────────────────────────────────────────────

    /// <summary>Bind a PDF file by path for editing.</summary>
    public void BindPdf(string path)
    {
        _document = Document.Open(File.ReadAllBytes(path));
    }

    /// <summary>Bind a PDF from a byte buffer.</summary>
    public void BindPdf(byte[] pdfData)
    {
        _document = Document.Open(pdfData);
    }

    /// <summary>Bind a PDF from a stream.</summary>
    public void BindPdf(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _document = Document.Open(ms.ToArray());
    }

    /// <summary>Bind a PDF from an already-open <see cref="Document"/>.</summary>
    public void BindPdf(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    // ── Save overloads ────────────────────────────────────────────────────

    /// <summary>Save the bound document to a file path.</summary>
    public void Save(string path)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        _document.Save(path);
    }

    /// <summary>Save the bound document to a stream.</summary>
    public void Save(Stream destStream)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        _document.Save(destStream);
    }

    /// <summary>
    /// Save the bound document to the configured destination (<see cref="DestFileName"/>
    /// or <see cref="DestStream"/>). Throws when neither is configured.
    /// </summary>
    public void Save()
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        if (_destFileName is null && _destStream is null)
            throw new InvalidOperationException("No destination configured. Set DestFileName or DestStream, or call Save(string)/Save(Stream).");
        ApplyConvertTo();
        var bytes = _document.ToArray();
        if (_destFileName is not null) File.WriteAllBytes(_destFileName, bytes);
        if (_destStream is not null) _destStream.Write(bytes);
    }

    /// <summary>Apply a <see cref="ConvertTo"/> target PDF version to the bound document
    /// so the saved file header carries it. Only the plain <c>v_X_Y</c> versions are
    /// honoured here; PDF/A and other conformance formats need a full conversion pipeline
    /// that this facade does not implement.</summary>
    private void ApplyConvertTo()
    {
        if (_document is null || _convertTo is null) return;
        var version = _convertTo switch
        {
            PdfFormat.v_1_0 => "1.0",
            PdfFormat.v_1_1 => "1.1",
            PdfFormat.v_1_2 => "1.2",
            PdfFormat.v_1_3 => "1.3",
            PdfFormat.v_1_4 => "1.4",
            PdfFormat.v_1_5 => "1.5",
            PdfFormat.v_1_6 => "1.6",
            PdfFormat.v_1_7 => "1.7",
            PdfFormat.v_2_0 => "2.0",
            _ => null,
        };
        if (version is not null) _document.SetVersion(version);
    }

    /// <summary>Close and release the bound document.</summary>
    public void Close()
    {
        _document?.Dispose();
        _document = null;
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    // ── Field-set helpers ─────────────────────────────────────────────────

    /// <summary>Set a field value by name. Returns false if the field was not found.</summary>
    public bool SetField(string fieldName, string value)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindByName(fieldName);
        if (field is null) return false;
        field.Value = value;
        return true;
    }

    /// <summary>Check a checkbox field by name. Returns false if the field was not found or is not a checkbox.</summary>
    public bool CheckField(string fieldName)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindByName(fieldName);
        if (field is not CheckboxField cb) return false;
        cb.IsChecked = true;
        return true;
    }

    /// <summary>Uncheck a checkbox field by name. Returns false if the field was not found or is not a checkbox.</summary>
    public bool UncheckField(string fieldName)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindByName(fieldName);
        if (field is not CheckboxField cb) return false;
        cb.IsChecked = false;
        return true;
    }

    // ── Field-add convenience helpers ────────────────────────────────────

    /// <summary>Add a text field to the bound document.</summary>
    public void AddTextField(string fieldName, int pageNumber, double llx, double lly, double urx, double ury)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var page = _document.Pages[pageNumber];
        var builder = new FormFieldBuilder(_document);
        builder.AddTextField(page, fieldName, new Rectangle(llx, lly, urx, ury));
    }

    /// <summary>Add a checkbox field to the bound document.</summary>
    public void AddCheckBox(string fieldName, int pageNumber, double llx, double lly, double urx, double ury, bool isChecked = false)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var page = _document.Pages[pageNumber];
        var builder = new FormFieldBuilder(_document);
        builder.AddCheckBox(page, fieldName, new Rectangle(llx, lly, urx, ury), isChecked);
    }

    // ── AddField ─────────────────────────────────────────────────────────

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
        if (_document.Form.FindByName(fieldName) is not ChoiceField cf) return;
        cf.Options.Clear();
        foreach (var e in ExportItems)
        {
            if (e is { Length: >= 2 }) cf.AddOption(e[0], e[1]);
            else if (e is { Length: 1 }) cf.AddOption(e[0]);
        }
    }

    // ── List-item helpers ────────────────────────────────────────────────

    /// <summary>Add a single item to a ListBox / ComboBox field.</summary>
    public void AddListItem(string fieldName, string itemName)
    {
        if (_document?.Form is null) return;
        if (_document.Form.FindByName(fieldName) is ChoiceField cf)
            cf.AddOption(itemName);
    }

    /// <summary>Add a single item with an export-value pair (display, export).</summary>
    public void AddListItem(string fieldName, string[] exportName)
    {
        if (_document?.Form is null || exportName is null || exportName.Length == 0) return;
        if (_document.Form.FindByName(fieldName) is ChoiceField cf)
            cf.AddOption(exportName.Length > 1 ? exportName[1] : exportName[0], exportName[0]);
    }

    /// <summary>Remove a single item from a ListBox / ComboBox field. Item removal is not currently implemented; the call is a no-op kept for API parity.</summary>
    public void DelListItem(string fieldName, string itemName)
    {
        _ = fieldName;
        _ = itemName;
    }

    // ── Submit-button + scripts ──────────────────────────────────────────

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
        btn.NormalCaption = label;
        btn.Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));

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
        var a = _document.Reader.ResolveDict(field.Dict.Get("A"));
        if (a is null)
        {
            a = new PdfDictionary();
            a.Set("S", new PdfName("SubmitForm"));
            field.Dict.Set("A", a);
        }
        return a;
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
        var field = _document.Form.FindByName(fieldName);
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

    // ── Copy / move / rename ─────────────────────────────────────────────

    /// <summary>Copy a field within the bound document to another page.</summary>
    public void CopyInnerField(string fieldName, string newFieldName, int pageNum)
        => CopyInnerField(fieldName, newFieldName, pageNum, abscissa: float.NaN, ordinate: float.NaN);

    /// <summary>Copy a field within the bound document to another page at the given coordinates.</summary>
    public void CopyInnerField(string fieldName, string newFieldName, int pageNum, float abscissa, float ordinate)
    {
        if (_document?.Form is null) return;
        var src = _document.Form.FindByName(fieldName);
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
        var srcField = src.Form.FindByName(fieldName);
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

    /// <summary>Apply <see cref="Facade"/> to every field in the document.</summary>
    public void DecorateField()
    {
        if (_document?.Form is null) return;
        foreach (var f in _document.Form.Fields)
            ApplyFacade(f);
    }

    /// <summary>Apply <see cref="Facade"/> to every field of the given type.</summary>
    public void DecorateField(FieldType fieldType)
    {
        if (_document?.Form is null) return;
        foreach (var f in _document.Form.Fields)
        {
            if (MapToFacadeType(f.Type) == fieldType)
                ApplyFacade(f);
        }
    }

    /// <summary>Apply <see cref="Facade"/> to a single named field.</summary>
    public void DecorateField(string fieldName)
    {
        if (_document?.Form is null) return;
        var f = _document.Form.FindByName(fieldName);
        if (f is not null) ApplyFacade(f);
    }

    // Standard AcroForm /DA font abbreviations (PDF spec, the base-14 set) that
    // are always valid even though FontRepository keys on full family names.
    private static readonly System.Collections.Generic.HashSet<string> StandardDaFontAbbreviations = new(StringComparer.Ordinal)
    {
        "Helv", "HeBo", "HeOb", "HeBO", "Cour", "CoBo", "CoOb", "CoBO",
        "TiRo", "TiBo", "TiIt", "TiBI", "Symb", "ZaDb",
    };

    private static bool IsLoadableFormFont(string fontName)
        => StandardDaFontAbbreviations.Contains(fontName)
           || Aspose.Pdf.Text.FontRepository.FindFont(fontName) is not null;

    private void ApplyFacade(Field field)
    {
        // A custom font must be resolvable: a standard PDF /DA font abbreviation (Helv,
        // ZaDb, Symb, …), a Standard-14 face, or a system font found via FontRepository.
        // Otherwise fail loudly — DecorateField throws rather than writing a /DA referencing
        // a missing font. (A field whose existing /DA names ZapfDingbats — the checkbox
        // glyph font — must not trip this guard.)
        if (!string.IsNullOrEmpty(Facade.CustomFont) && !IsLoadableFormFont(Facade.CustomFont!))
        {
            throw new ArgumentException("Could not load specified font : " + Facade.CustomFont);
        }

        // Active implementation: rewrite the field's /DA when the facade sets a font or size.
        // Color/alignment changes are recorded on the field's /MK dict but not currently
        // re-emitted into the appearance stream.
        if (Facade.FontSize > 0 || !string.IsNullOrEmpty(Facade.CustomFont))
        {
            // A caller-specified CustomFont must be loadable: a standard PDF font
            // abbreviation, or a font name FontRepository can resolve (Standard-14,
            // a registered source, or a host system font). An unknown family is an
            // error rather than a silent fall-through to the default font.
            if (!string.IsNullOrEmpty(Facade.CustomFont) && !IsLoadableFormFont(Facade.CustomFont!))
                throw new ArgumentException($"Could not load specified font : {Facade.CustomFont}");
            var fontName = Facade.CustomFont ?? "Helv";
            var fontSize = Facade.FontSize > 0 ? Facade.FontSize : 0f;
            var da = $"/{fontName} {fontSize.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} Tf 0 g";
            field.Dict.Set("DA", new PdfString(System.Text.Encoding.UTF8.GetBytes(da)));
        }

        // Border style/width → /BS on each widget (WidgetAnnotation.Border resolves
        // Style from /BS /S). Written on the kid widgets so per-widget Border surfaces it.
        if (Facade.BorderStyle != FormFieldFacade.BorderStyleUndefined || Facade.BorderWidth > 0)
            ApplyBorderStyle(field);

        // Re-emit a checkbox's on/off appearance from the facade (background, border,
        // glyph colour and style) when the facade carries any visible decoration —
        // a background/border/text colour, a border width, or an explicit button style.
        if (field.Type == Forms.FieldType.CheckBox &&
            (Facade.BackgroundColor.A != 0 || Facade.BorderColor.A != 0 || Facade.TextColor.A != 0
             || Facade.BorderWidth > 0 || Facade.ButtonStyle != FormFieldFacade.CheckBoxStyleUndefined))
            DecorateCheckBox(field);

        // A text / choice field's facade border colour is recorded on /MK /BC and drawn
        // into the widget appearance — the value text the appearance already carries is
        // kept; a stroked rectangle at the facade width is appended so the decorated
        // border renders (checkboxes regenerate their whole face above instead).
        else if (field.Type is Forms.FieldType.Text or Forms.FieldType.Choice
                 or Forms.FieldType.ComboBox or Forms.FieldType.ListBox
                 && (Facade.BorderColor.A != 0 || Facade.BorderWidth > 0))
            DecorateTextBorder(field);
    }

    /// <summary>Record the facade border colour on each of a non-button field's widgets
    /// (/MK /BC) and append a stroked border rectangle to the widget's existing /AP /N so
    /// the decorated border renders without disturbing the value text already drawn there.</summary>
    private void DecorateTextBorder(Field field)
    {
        if (_document is null) return;
        var color = Facade.BorderColor.A != 0 ? Facade.BorderColor : System.Drawing.Color.Black;
        int bw = Facade.BorderWidth > 0 ? (int)Facade.BorderWidth : 1;
        var widgets = new System.Collections.Generic.List<PdfDictionary>(field.AllKids());
        if (widgets.Count == 0) widgets.Add(field.Dict);

        foreach (var widget in widgets)
        {
            if (_document.Reader.Resolve(widget.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var rect = Rectangle.FromPdfArray(ra);
            double w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0) continue;
            WriteMk(widget, "BC", color);

            double hbw = bw / 2.0;
            var sb = new System.Text.StringBuilder();
            sb.Append("\nq\n");
            sb.Append($"{Col(color.R)} {Col(color.G)} {Col(color.B)} RG\n");
            sb.Append($"{Num(bw)} w\n");
            sb.Append($"{Num(hbw)} {Num(hbw)} {Num(w - bw)} {Num(h - bw)} re\n");
            sb.Append("S\n");
            sb.Append("Q\n");
            var borderBytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

            var ap = _document.Reader.ResolveDict(widget.Get("AP"));
            var n = ap is null ? null : _document.Reader.ResolveStream(ap.Get("N"));
            if (n is not null)
            {
                var existing = _document.Reader.DecodeStream(n);
                var combined = new byte[existing.Length + borderBytes.Length];
                System.Array.Copy(existing, combined, existing.Length);
                System.Array.Copy(borderBytes, 0, combined, existing.Length, borderBytes.Length);
                n.ReplaceData(combined);
                n.Dict.Remove("Filter");
                n.Dict.Set("Length", new PdfInteger(combined.Length));
            }
            else
            {
                var apDict = new PdfDictionary();
                apDict.Set("Type", new PdfName("XObject"));
                apDict.Set("Subtype", new PdfName("Form"));
                var bbox = new PdfArray();
                bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
                bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
                apDict.Set("BBox", bbox);
                apDict.Set("Length", new PdfInteger(borderBytes.Length));
                var newAp = new PdfDictionary();
                newAp.Set("N", new PdfStream(apDict, borderBytes));
                widget.Set("AP", newAp);
            }
        }
    }

    /// <summary>Regenerate a checkbox's /AP /N and /AP /D appearances from the facade:
    /// a filled background, a stroked border at the facade width, and the on-state glyph
    /// (ZapfDingbats) in the facade text colour. The operator order matches the standard
    /// decorated-checkbox face.</summary>
    private void DecorateCheckBox(Field field)
    {
        if (_document is null) return;
        var widgets = new System.Collections.Generic.List<PdfDictionary>(field.AllKids());
        if (widgets.Count == 0) widgets.Add(field.Dict);

        // The "Cross" style and the undefined default both draw the stroked-X mark
        // (matching Aspose.Pdf); the other styles draw a ZapfDingbats glyph.
        int style = Facade.ButtonStyle == FormFieldFacade.CheckBoxStyleUndefined
            ? FormFieldFacade.CheckBoxStyleCross : Facade.ButtonStyle;
        int bw = Facade.BorderWidth > 0 ? (int)Facade.BorderWidth : 1;

        foreach (var widget in widgets)
        {
            if (_document.Reader.Resolve(widget.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var rect = Rectangle.FromPdfArray(ra);
            double w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0) continue;

            // Preserve the existing on-state name (the non-Off key in /AP /N), falling
            // back to /AS, then "On".
            var onName = "On";
            if (_document.Reader.ResolveDict(widget.Get("AP")) is { } apOld &&
                _document.Reader.ResolveDict(apOld.Get("N")) is { } nOld)
            {
                foreach (var k in nOld.Keys) if (k != "Off") { onName = k; break; }
            }
            else if (widget.GetName("AS") is { } asn && asn != "Off") onName = asn;

            // Resolve colours: an explicitly-set facade colour wins, otherwise an unset
            // colour preserves the widget's existing /MK value (so e.g. setting only the
            // border colour keeps the original background), otherwise a sensible default.
            var bg = Facade.BackgroundColor.A != 0 ? Facade.BackgroundColor
                : (ReadMkColor(widget, "BG") ?? System.Drawing.Color.White);
            var border = Facade.BorderColor.A != 0 ? Facade.BorderColor
                : (ReadMkColor(widget, "BC") ?? System.Drawing.Color.Black);
            var text = Facade.TextColor.A != 0 ? Facade.TextColor : System.Drawing.Color.Black;

            var n = new PdfDictionary();
            n.Set(onName, BuildCheckBoxFace(w, h, bw, style, bg, border, text, withMark: true));
            n.Set("Off", BuildCheckBoxFace(w, h, bw, style, bg, border, text, withMark: false));
            var d = new PdfDictionary();
            d.Set(onName, BuildCheckBoxFace(w, h, bw, style, bg, border, text, withMark: true));
            d.Set("Off", BuildCheckBoxFace(w, h, bw, style, bg, border, text, withMark: false));
            var ap = new PdfDictionary();
            ap.Set("N", n);
            ap.Set("D", d);
            widget.Set("AP", ap);

            // Record the decoration on /MK (/BG background, /BC border) and the widget
            // /C (the glyph/text colour) so the loaded field surfaces them via
            // Characteristics.Background/Border and Field.Color.
            WriteMk(widget, "BG", bg);
            WriteMk(widget, "BC", border);
            WriteWidgetColor(widget, text);
        }
    }

    /// <summary>Build a decorated-checkbox appearance face: a filled background, a
    /// stroked border at the facade width, and (for the on state) the mark — a stroked
    /// diagonal "X" for the Cross style, otherwise a ZapfDingbats glyph. The operator
    /// order matches Aspose.Pdf's decorated-checkbox face.</summary>
    private PdfStream BuildCheckBoxFace(double w, double h, int bw, int style,
        System.Drawing.Color bg, System.Drawing.Color border, System.Drawing.Color text, bool withMark)
    {
        double hbw = bw / 2.0;
        bool isCross = style == FormFieldFacade.CheckBoxStyleCross;
        var sb = new System.Text.StringBuilder();
        sb.Append("q\n");
        sb.Append($"{Col(bg.R)} {Col(bg.G)} {Col(bg.B)} rg\n");
        sb.Append($"0 0 {Num(w)} {Num(h)} re\n");
        sb.Append("f\n");
        sb.Append("q\n");
        sb.Append($"{Col(border.R)} {Col(border.G)} {Col(border.B)} RG\n");
        sb.Append($"{Num(hbw)} {Num(hbw)} {Num(w - bw)} {Num(h - bw)} re\n");
        sb.Append($"{bw} w\n");
        sb.Append("s\n");
        sb.Append("Q\n");
        sb.Append("Q\n");
        if (withMark)
        {
            sb.Append($"{Num(bw)} {Num(bw)} {Num(w - 2 * bw)} {Num(h - 2 * bw)} re\n");
            sb.Append("W\n");
            sb.Append("n\n");
            sb.Append("q\n");
            if (isCross)
            {
                // Two stroked diagonals from (2bw,2bw)→(w-2bw,h-2bw) and (w-2bw,2bw)→(2bw,h-2bw).
                double lo = 2 * bw, hix = w - 2 * bw, hiy = h - 2 * bw;
                sb.Append($"{Col(text.R)} {Col(text.G)} {Col(text.B)} RG\n");
                sb.Append($"{Num(lo)} {Num(lo)} m\n");
                sb.Append($"{Num(hix)} {Num(hiy)} l\n");
                sb.Append("S\n");
                sb.Append($"{Num(hix)} {Num(lo)} m\n");
                sb.Append($"{Num(lo)} {Num(hiy)} l\n");
                sb.Append("S\n");
            }
            else
            {
                // ZapfDingbats glyph centred in the inner box. The glyph is written with
                // the style value as its character code (the ZaDb /Encoding /Differences
                // maps it to the proper dingbat); font size fills the inner box and the
                // baseline offset centres a ~2/3-em glyph: Td = (w + 4·bw)/6.
                double fontSize = w - 2 * bw;
                double td = (w + 4 * bw) / 6.0;
                sb.Append($"{Col(text.R)} {Col(text.G)} {Col(text.B)} rg\n");
                sb.Append("BT\n");
                sb.Append($"{Num(td)} {Num(td)} Td\n");
                sb.Append($"/ZaDb {Num(fontSize)} Tf\n");
                // Glyph keyed by the style value (the /Encoding /Differences maps it to the
                // dingbat). Emit the raw byte so the round-tripped text length is 1.
                sb.Append($"({(char)style}) Tj\n");
                sb.Append("ET\n");
            }
            sb.Append("Q\n");
        }
        var bytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

        var apDict = new PdfDictionary();
        apDict.Set("Type", new PdfName("XObject"));
        apDict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apDict.Set("BBox", bbox);
        apDict.Set("Length", new PdfInteger(bytes.Length));
        var zadb = new PdfDictionary();
        zadb.Set("Type", new PdfName("Font"));
        zadb.Set("Subtype", new PdfName("Type1"));
        zadb.Set("BaseFont", new PdfName("ZapfDingbats"));
        // Map the style char codes (1..6) to the corresponding ZapfDingbats glyph names
        // so the mark renders even though it is keyed by the style value.
        var enc = new PdfDictionary();
        enc.Set("Type", new PdfName("Encoding"));
        var diff = new PdfArray();
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleCheck)); diff.Add(new PdfName("a20"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleCircle)); diff.Add(new PdfName("a71"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleDiamond)); diff.Add(new PdfName("a73"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleSquare)); diff.Add(new PdfName("a72"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleStar)); diff.Add(new PdfName("a35"));
        enc.Set("Differences", diff);
        zadb.Set("Encoding", enc);
        var fonts = new PdfDictionary();
        fonts.Set("ZaDb", zadb);
        var resources = new PdfDictionary();
        resources.Set("Font", fonts);
        apDict.Set("Resources", resources);
        return new PdfStream(apDict, bytes);
    }

    /// <summary>Read a /MK colour entry (/BG or /BC) as a System.Drawing.Color, or null
    /// when absent/empty (a 0-length array = transparent/no colour).</summary>
    private System.Drawing.Color? ReadMkColor(PdfDictionary widget, string key)
    {
        if (_document is null) return null;
        var mk = _document.Reader.ResolveDict(widget.Get("MK"));
        if (mk is null) return null;
        if (_document.Reader.Resolve(mk.Get(key)) is not PdfArray arr || arr.Count == 0) return null;
        double[] c = new double[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            c[i] = arr[i] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => 0.0 };
        return c.Length switch
        {
            1 => GrayToColor(c[0]),
            3 => System.Drawing.Color.FromArgb(To255(c[0]), To255(c[1]), To255(c[2])),
            4 => CmykToColor(c[0], c[1], c[2], c[3]),
            _ => (System.Drawing.Color?)null,
        };
    }

    /// <summary>Write a /MK colour entry (/BG or /BC) as an RGB array.</summary>
    private void WriteMk(PdfDictionary widget, string key, System.Drawing.Color color)
    {
        if (_document is null) return;
        var mk = _document.Reader.ResolveDict(widget.Get("MK"));
        if (mk is null) { mk = new PdfDictionary(); widget.Set("MK", mk); }
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        mk.Set(key, arr);
    }

    /// <summary>Write the widget's /C (annotation colour) as an RGB array — DecorateField
    /// records the glyph/text colour here so Field.Color surfaces it after a reload.</summary>
    private static void WriteWidgetColor(PdfDictionary widget, System.Drawing.Color color)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        widget.Set("C", arr);
    }

    private static int To255(double v) => (int)System.Math.Round(System.Math.Clamp(v, 0, 1) * 255);
    private static System.Drawing.Color GrayToColor(double g) { int v = To255(g); return System.Drawing.Color.FromArgb(v, v, v); }
    private static System.Drawing.Color CmykToColor(double c, double m, double y, double k)
        => System.Drawing.Color.FromArgb(To255((1 - c) * (1 - k)), To255((1 - m) * (1 - k)), To255((1 - y) * (1 - k)));

    private static string Col(byte b) => Num(b / 255.0);
    private static string Num(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Write the facade's border style/width as a /BS dictionary on the
    /// field's widget annotations (kids, or the field dict itself when it is a
    /// single-widget leaf).</summary>
    private void ApplyBorderStyle(Field field)
    {
        var style = Facade.BorderStyle switch
        {
            FormFieldFacade.BorderStyleDashed => "D",
            FormFieldFacade.BorderStyleBeveled => "B",
            FormFieldFacade.BorderStyleInset => "I",
            FormFieldFacade.BorderStyleUnderline => "U",
            _ => "S",
        };
        var width = Facade.BorderWidth > 0 ? (int)Facade.BorderWidth : 1;

        var targets = new System.Collections.Generic.List<PdfDictionary>();
        foreach (var kid in field.AllKids()) targets.Add(kid);
        if (targets.Count == 0) targets.Add(field.Dict);

        foreach (var t in targets)
        {
            var bs = new PdfDictionary();
            bs.Set("Type", new PdfName("Border"));
            bs.Set("W", new PdfInteger(width));
            bs.Set("S", new PdfName(style));
            t.Set("BS", bs);
        }
    }

    private static FieldType MapToFacadeType(Forms.FieldType t)
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

    /// <summary>Reset every property on <see cref="Facade"/>.</summary>
    public void ResetFacade() => Facade.Reset();

    /// <summary>Alias for <see cref="ResetFacade"/> covering nested-facade callers.</summary>
    public void ResetInnerFacade() => Facade.Reset();

    /// <summary>Move a field's bounding box.</summary>
    public bool MoveField(string fieldName, float llx, float lly, float urx, float ury)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindByName(fieldName);
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
        _document.Form.FindByName(fieldName)?.SetPartialName(newFieldName);
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
        _document.Form.FindByName(fieldName)?.Dict.Remove("A");
    }

    // ── Setters that return success ──────────────────────────────────────

    /// <summary>Set the horizontal alignment of a field's value. Alignment uses <see cref="FormFieldFacade.AlignLeft"/> etc.</summary>
    public bool SetFieldAlignment(string fieldName, int alignment)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindByName(fieldName);
        if (f is null) return false;
        f.Dict.Set("Q", new PdfInteger(alignment));
        return true;
    }

    /// <summary>Set the vertical alignment of a field's value.</summary>
    public bool SetFieldAlignmentV(string fieldName, int alignment)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindByName(fieldName);
        if (f is null) return false;
        // Vertical alignment is recorded on /MK; not honoured by the appearance writer here.
        f.Dict.Set("MK_TV", new PdfInteger(alignment));
        return true;
    }

    /// <summary>Set the visibility / read-only / required flags on a field.</summary>
    public bool SetFieldAppearance(string fieldName, AnnotationFlags flags)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindByName(fieldName);
        if (f is null) return false;
        f.Dict.Set("F", new PdfInteger((int)flags));
        return true;
    }

    /// <summary>Return the visibility / read-only / required flags on a field.</summary>
    public AnnotationFlags GetFieldAppearance(string fieldName)
    {
        if (_document?.Form is null) return AnnotationFlags.None;
        var f = _document.Form.FindByName(fieldName);
        if (f is null) return AnnotationFlags.None;
        return (AnnotationFlags)f.Dict.GetInt("F");
    }

    /// <summary>Set ReadOnly / Required / NoExport.</summary>
    public bool SetFieldAttribute(string fieldName, PropertyFlag flag)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindByName(fieldName);
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
        var f = _document.Form.FindByName(fieldName);
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
        var f = _document.Form.FindByName(fieldName);
        if (f is TextBoxField tb) { tb.MaxLen = fieldLimit; return true; }
        return false;
    }

    /// <summary>Set the SubmitForm action flag on a button field.</summary>
    public bool SetSubmitFlag(string fieldName, SubmitFormFlag submitFormFlag)
    {
        if (_document?.Form is null) return false;
        var f = _document.Form.FindByName(fieldName);
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
        var f = _document.Form.FindByName(fieldName);
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
        var f = _document.Form.FindByName(fieldName);
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

    // ── Stateless (byte[]-in / byte[]-out) helpers ───────────────────────

    /// <summary>Fill form fields with the given values (field name → value).</summary>
    public byte[] FillFields(byte[] input, Dictionary<string, string> fieldValues)
    {
        using var doc = Document.Open(input);
        if (doc.Form is null)
            throw new InvalidOperationException("Document has no form.");

        foreach (var (name, value) in fieldValues)
        {
            var field = doc.Form.FindByName(name);
            if (field is not null)
                field.Value = value;
        }

        return doc.ToArray();
    }

    /// <summary>Fill a single form field by name.</summary>
    public byte[] FillField(byte[] input, string fieldName, string value)
        => FillFields(input, new Dictionary<string, string> { [fieldName] = value });

    /// <summary>Flatten the form — render fields as static content and remove the AcroForm.</summary>
    public byte[] FlattenForm(byte[] input)
    {
        using var doc = Document.Open(input);
        if (!doc.HasForm)
            return input;
        doc.Form.Flatten(doc);
        return doc.ToArray();
    }

    /// <summary>Get all field names in the document's form.</summary>
    public string[] GetFieldNames(byte[] input)
    {
        using var doc = Document.Open(input);
        if (!doc.HasForm) return [];

        var names = new List<string>();
        foreach (var field in doc.Form.Fields)
        {
            if (field.FullName is not null)
                names.Add(field.FullName);
        }
        return names.ToArray();
    }

    /// <summary>Get the value of a specific field.</summary>
    public string? GetFieldValue(byte[] input, string fieldName)
    {
        using var doc = Document.Open(input);
        return doc.Form?.FindByName(fieldName)?.Value;
    }

    /// <summary>Get the type of a specific field.</summary>
    public Forms.FieldType? GetFieldType(byte[] input, string fieldName)
    {
        using var doc = Document.Open(input);
        return doc.Form?.FindByName(fieldName)?.Type;
    }

    /// <summary>Check if the document has a form.</summary>
    public bool HasForm(byte[] input)
    {
        using var doc = Document.Open(input);
        return doc.HasForm;
    }

    /// <summary>Get the total number of form fields.</summary>
    public int GetFieldCount(byte[] input)
    {
        using var doc = Document.Open(input);
        return doc.Form?.Count ?? 0;
    }

    /// <summary>Export form data as a dictionary (field name → value).</summary>
    public Dictionary<string, string> ExportFormData(byte[] input)
    {
        using var doc = Document.Open(input);
        var result = new Dictionary<string, string>();
        if (doc.Form is null) return result;

        foreach (var field in doc.Form.Fields)
        {
            if (field.Value is not null && field.FullName is not null)
                result[field.FullName] = field.Value;
        }
        return result;
    }

    /// <summary>Import form data from a dictionary.</summary>
    public byte[] ImportFormData(byte[] input, Dictionary<string, string> data)
        => FillFields(input, data);

    /// <summary>
    /// Rename a form field in the supplied bytes. Returns the updated PDF bytes and
    /// whether the field was found. Stateless variant of <see cref="RenameField(string,string)"/>.
    /// </summary>
    public (byte[] Pdf, bool Found) RenameField(byte[] input, string oldName, string newPartialName)
    {
        using var doc = Document.Open(input);
        if (doc.Form is null) return (input, false);

        var field = doc.Form.FindByName(oldName);
        if (field is null) return (input, false);

        field.SetPartialName(newPartialName);
        return (doc.ToArray(), true);
    }
}
