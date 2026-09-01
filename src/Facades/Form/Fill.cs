using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class Form
{
    /// <summary>Fill a field with a value.</summary>
    public bool FillField(string fieldName, string fieldValue)
    {
        if (_doc is null) return false;
        // For XFA forms, update the XFA datasets XML directly
        if (_doc.Form.IsXfa)
        {
            // Only fill a path that resolves to a genuine XFA template field — a non-matching
            // (e.g. wrong-container) or partial (leaf-only) path returns false
            // rather than silently creating a stray datasets node.
            if (!_doc.Form.XfaTemplateFieldExists(fieldName)) return false;
            _doc.Form.SetXfaFieldValue(fieldName, fieldValue);
            // Also try AcroForm field if it exists
            var field = _doc.Form.FindFieldOrNull(fieldName);
            if (field is not null) field.Value = fieldValue;
            return true;
        }
        else
        {
            var field = _doc.Form.FindFieldOrNull(fieldName);
            if (field is null) return false;
            // The facade fills raw: a value the field's /AA/F formatter cannot
            // parse is still stored and rendered verbatim (the DOM Value setter
            // keeps reject-invalid semantics).
            if (field is TextBoxField tb) tb.SetValue(fieldValue, validateFormat: false);
            else field.Value = fieldValue;
            return true;
        }
    }

    /// <summary>Fill a field with a boolean value (for checkboxes/radio buttons).</summary>
    public bool FillField(string fieldName, bool beChecked)
    {
        // "Check it" means the field's OWN on-state, not the literal "Yes": a checkbox
        // declares whatever name it likes ("1MM-4.9MM"), and a value assignment is stored
        // verbatim, so passing a generic token here would write a state the field does not
        // have. Resolve the declared state and fill with that.
        if (beChecked && _doc?.Form.FindFieldOrNull(fieldName) is CheckboxField box)
            return FillField(fieldName, box.OnValue);
        return FillField(fieldName, beChecked ? "Yes" : "Off");
    }

    /// <summary>Fill a barcode field by name with the given data.</summary>
    public bool FillBarcodeField(string fieldName, string data)
        => FillField(fieldName, data);

    /// <summary>Fill a text field with the supplied value, optionally fitting font size to the box.</summary>
    public bool FillField(string fieldName, string value, bool fitFontSize)
    {
        _ = fitFontSize; // fit-to-box not currently honoured; value still gets set.
        return FillField(fieldName, value);
    }

    /// <summary>Fill a choice or radio field by selected-option index (0-based).</summary>
    public bool FillField(string fieldName, int index)
    {
        if (_doc?.Form is null) return false;
        var field = _doc.Form.FindFieldOrNull(fieldName);
        // A radio button is selected by widget index. Its appearance on-state can
        // differ from its export value (the /Opt entry) — "index and value do not
        // match" — so drive the selection by index (RadioButtonField.Selected is
        // 1-based) and, for an XFA-backed form, write the option's export value into
        // the XFA datasets directly. We deliberately bypass the reverse
        // XFA->acro-field sync here: re-applying the export value would re-match it
        // against an unrelated widget on-state and move the selection.
        if (field is RadioButtonField rb)
        {
            if (index < 0 || index >= rb.Options.Count) return false;
            rb.Selected = index + 1; // RadioButtonField.Selected is 1-based
            _doc.Form.SetXfaFieldValue(fieldName, rb.Options[index + 1].Value);
            return true;
        }
        if (field is ChoiceField cf)
        {
            if (index >= 0 && index < cf.Options.Count)
            {
                cf.Selected = index;
                return true;
            }
        }
        return false;
    }

    /// <summary>Fill a list-box field with the supplied multi-select values.</summary>
    public void FillField(string fieldName, string[] fieldValues)
    {
        if (_doc?.Form is null || fieldValues is null) return;
        var field = _doc.Form.FindFieldOrNull(fieldName);
        if (field is null) return;
        field.Value = string.Join(",", fieldValues);
    }

    /// <summary>
    /// Apply multiple field values and return the resulting PDF as a stream.
    /// Returns true when every name was found.
    /// </summary>
    public bool FillFields(string[] fieldNames, string[] fieldValues, out Stream output)
    {
        output = new MemoryStream();
        if (_doc is null || fieldNames is null || fieldValues is null) return false;
        bool allOk = true;
        for (var i = 0; i < fieldNames.Length && i < fieldValues.Length; i++)
        {
            if (!FillField(fieldNames[i], fieldValues[i])) allOk = false;
        }
        // A signed document must be updated incrementally so the original bytes
        // (and thus the existing signature's /ByteRange) survive; a full rewrite
        // would invalidate the signature.
        var bytes = _doc.Form is { SignaturesExist: true }
            ? _doc.ToArrayIncremental()
            : _doc.ToArray();
        output.Write(bytes, 0, bytes.Length);
        output.Position = 0;
        return allOk;
    }

    /// <summary>Fill an image field by embedding the image as the field widget's
    /// normal appearance (/AP/N), scaled to fill each widget's rectangle. When the
    /// field name is shared by several widgets (e.g. repeated on multiple pages)
    /// every widget receives the image.</summary>
    public void FillImageField(string fieldName, Stream imageStream)
    {
        if (_doc?.Form is null || imageStream is null) return;

        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        var imageBytes = ms.ToArray();
        if (imageBytes.Length == 0) return;

        // XFA image fields carry their picture as a base64 datasets value tagged with a
        // contentType; record it so it round-trips through XFA.GetFieldNode. A dynamic
        // XFA field has no AcroForm widget (FindByName is null), so this is the only sink.
        if (_doc.Form.IsXfa)
            _doc.Form.SetXfaFieldImage(fieldName, Convert.ToBase64String(imageBytes),
                DetectImageContentType(imageBytes));

        var field = _doc.Form.FindFieldOrNull(fieldName);
        if (field is null) return;

        // A field is either a terminal widget (its own dict carries /Rect + /AP) or
        // a parent with one /Kids widget per placement. Target every widget.
        var widgets = field.AllKids().ToList();
        if (widgets.Count == 0) widgets.Add(field.Dict);

        foreach (var widget in widgets)
            SetImageAppearance(widget, imageBytes);

        // Surface the field as an image push-button: filling a (text or button) field
        // with an image converts it to a push button whose icon is the image, so a
        // reloaded document reports FieldType.Image and the field is
        // a ButtonField carrying the image appearance.
        field.Dict.Set("FT", new Core.PdfName("Btn"));
        field.Dict.Set("Ff", new Core.PdfInteger(65536)); // push button
        field.Dict.Remove("V");
        field.Dict.Remove("DV");
    }

    /// <summary>Fill an image field from a file path.</summary>
    public void FillImageField(string fieldName, string imageFileName)
    {
        if (string.IsNullOrEmpty(imageFileName) || !File.Exists(imageFileName)) return;
        using var fs = File.OpenRead(imageFileName);
        FillImageField(fieldName, fs);
    }

    /// <summary>MIME type for an XFA image field's datasets <c>contentType</c>, from the
    /// image's magic bytes. Uses <c>image/jpg</c> (XFA's spelling, not image/jpeg).</summary>
    private static string DetectImageContentType(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 3 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D) return "image/bmp";
        return "image/jpg";
    }

    /// <summary>Build an image XObject from <paramref name="imageBytes"/> and write
    /// it as the widget's normal appearance, scaled to fill the widget rectangle.</summary>
    private void SetImageAppearance(Core.PdfDictionary widget, byte[] imageBytes)
    {
        var reader = _doc!.Reader;
        if (reader.Resolve(widget.Get("Rect")) is not Core.PdfArray rectArr || rectArr.Count < 4)
            return;
        var rect = Rectangle.FromPdfArray(rectArr, reader);
        double w = rect.Width, h = rect.Height;
        if (w <= 0 || h <= 0) return;

        // Decode (PNG/JPEG/GDI+) and build a standalone image XObject (with /SMask
        // for transparency), reusing the page-image pipeline.
        using var imgSrc = new MemoryStream(imageBytes, writable: false);
        var stamp = new ImageStamp(imgSrc);
        var imgStream = stamp.BuildImageXObject();

        var xobjects = new Core.PdfDictionary();
        xobjects.Set("Im0", imgStream);
        var resources = new Core.PdfDictionary();
        resources.Set("XObject", xobjects);

        // Honour the widget's icon rotation (/MK /R, in degrees). For 90°/270° the
        // appearance is drawn in a coordinate space with width/height swapped and
        // mapped back into the widget rect by the form /Matrix.
        int rot = 0;
        var mk = reader.ResolveDict(widget.Get("MK"));
        if (mk is not null && mk.ContainsKey("R"))
            rot = (int)(((mk.GetInt("R") % 360) + 360) % 360);
        bool swap = rot == 90 || rot == 270;
        double boxW = swap ? h : w;   // appearance-space box dimensions
        double boxH = swap ? w : h;

        // Proportional fit, centered, inset by a 2-unit icon margin on each side —
        // the standard Acrobat push-button icon fit (/MK /IF default). The image is
        // scaled to fit inside the (inset) box preserving its aspect ratio, then
        // centered; stretch-to-fill would distort non-matching aspect ratios.
        const double inset = 2.0;
        double availW = Math.Max(0, boxW - 2 * inset);
        double availH = Math.Max(0, boxH - 2 * inset);
        double dw = availW, dh = availH, tx = inset, ty = inset;
        if (stamp.PixelWidth > 0 && stamp.PixelHeight > 0 && availW > 0 && availH > 0)
        {
            double scale = Math.Min(availW / stamp.PixelWidth, availH / stamp.PixelHeight);
            dw = stamp.PixelWidth * scale;
            dh = stamp.PixelHeight * scale;
            tx = inset + (availW - dw) / 2.0;
            ty = inset + (availH - dh) / 2.0;
        }
        var content = Encoding.ASCII.GetBytes(
            $"q {Fmt(dw)} 0 0 {Fmt(dh)} {Fmt(tx)} {Fmt(ty)} cm /Im0 Do Q");

        var apN = new Core.PdfDictionary();
        apN.Set("Type", new Core.PdfName("XObject"));
        apN.Set("Subtype", new Core.PdfName("Form"));
        apN.Set("FormType", new Core.PdfInteger(1));
        apN.Set("BBox", MakeRectArray(0, 0, boxW, boxH));
        if (rot != 0)
            apN.Set("Matrix", IconRotationMatrix(rot, w, h));
        apN.Set("Resources", resources);
        apN.Set("Length", new Core.PdfInteger(content.Length));
        var apStream = new Core.PdfStream(apN, content);

        var ap = reader.ResolveDict(widget.Get("AP")) ?? new Core.PdfDictionary();
        ap.Set("N", apStream);
        widget.Set("AP", ap);

        // Mark the widget as an icon (image) button so GetFieldType reports Image after
        // a reload, and expose the icon via /MK /I.
        if (mk is null) { mk = new Core.PdfDictionary(); widget.Set("MK", mk); }
        mk.Set("TP", new Core.PdfInteger(1));
        mk.Set("I", apStream);
    }

    /// <summary>The /Matrix that maps an icon appearance drawn in the (rotated)
    /// appearance space back into the widget rectangle for an /MK /R rotation.</summary>
    private static Core.PdfArray IconRotationMatrix(int rot, double w, double h)
    {
        var (a, b, c, d, e, f) = rot switch
        {
            90  => (0.0, 1.0, -1.0, 0.0, h, 0.0),
            180 => (-1.0, 0.0, 0.0, -1.0, w, h),
            270 => (0.0, -1.0, 1.0, 0.0, 0.0, w),
            _   => (1.0, 0.0, 0.0, 1.0, 0.0, 0.0),
        };
        var arr = new Core.PdfArray();
        foreach (var v in new[] { a, b, c, d, e, f }) arr.Add(new Core.PdfReal(v));
        return arr;
    }

    private static Core.PdfArray MakeRectArray(double llx, double lly, double urx, double ury)
    {
        var arr = new Core.PdfArray();
        arr.Add(new Core.PdfReal(llx));
        arr.Add(new Core.PdfReal(lly));
        arr.Add(new Core.PdfReal(urx));
        arr.Add(new Core.PdfReal(ury));
        return arr;
    }

    private static string Fmt(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
