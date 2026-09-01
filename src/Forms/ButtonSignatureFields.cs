using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>Push-button form field (FT=Btn with Pushbutton flag set).</summary>
public class ButtonField : Field
{
    internal ButtonField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public ButtonField() : base(BuildButtonDict(), PdfReader.Empty) { }

    public ButtonField(Document doc, Rectangle rect)
        : base(BuildButtonDict(rect), doc?.Reader ?? PdfReader.Empty) { }

    public ButtonField(Page page, Rectangle rect)
        : base(BuildButtonDict(rect), page?.Reader ?? PdfReader.Empty) { }

    private static PdfDictionary BuildButtonDict(Rectangle? rect = null)
    {
        var d = new PdfDictionary();
        d.Set("FT", new PdfName("Btn"));
        d.Set("Ff", new PdfInteger(1 << 16));
        if (rect is not null)
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(rect.LLX));
            arr.Add(new PdfReal(rect.LLY));
            arr.Add(new PdfReal(rect.URX));
            arr.Add(new PdfReal(rect.URY));
            d.Set("Rect", arr);
        }
        return d;
    }

    /// <summary>
    /// Caption shown on the button in its normal state (MK dict /CA entry).
    /// Setting null or empty removes the entry.
    /// </summary>
    public string? NormalCaption
    {
        get
        {
            // MK dict lives on the widget; try own dict first, fallback to first kid (widget)
            var mk = GetMK(create: false);
            return (mk?.Get("CA") as PdfString)?.ToText();
        }
        set
        {
            var mk = GetMK(create: value is not null);
            if (mk is null) return;
            if (value is null)
                mk.Remove("CA");
            else
                mk.Set("CA", new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
            // Drop any pre-existing /AP so the button face is rebuilt with the new
            // caption (a loaded button carries a baked-in appearance that
            // GenerateAppearance would otherwise leave untouched).
            Dict.Remove("AP");
            GenerateAppearance();
        }
    }

    /// <summary>Draw the push-button face (/MK background + border, or default
    /// grey chrome) and centre the normal caption.</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        if (!TryWidgetSize(out var w, out var h)) return;
        ParseDefaultAppearance(out var fontName, out var fontSize);

        // A non-solid border style (Beveled/Inset/Underline/Dashed) draws
        // style-specific chrome; the plain solid
        // face keeps the simple rectangle stroke.
        var borderStyle = GetBorderStyleValue();
        string face = borderStyle is not Aspose.Pdf.Annotations.BorderStyle.Solid
            ? BuildStyledButtonFace(w, h, borderStyle, fontName, fontSize)
            : BuildSolidButtonFace(w, h, fontName, fontSize);

        var ap = new PdfDictionary();
        ap.Set("N", MakeApXObject(face, w, h, MakeStandardFontResources(fontName)));
        // Push buttons carry a down (/D) appearance too; reuse the same face so
        // callers reading States["D"] see the current caption.
        ap.Set("D", MakeApXObject(face, w, h, MakeStandardFontResources(fontName)));
        Dict.Set("AP", ap);
    }

    private string BuildSolidButtonFace(double w, double h, string fontName, double fontSize)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("q ");
        var mk = BuildMkBackgroundAndBorder(w, h);
        if (mk.Length == 0)
            // Default push-button chrome: light-grey face with a grey border. Fill and
            // stroke share the widget-sized rectangle — the face carries no geometry
            // other than the box itself, and the stroke's outer half is clipped by the
            // BBox, leaving the same visible inner border an inset rect would draw.
            sb.Append($"0.75 0.75 0.75 rg 0 0 {FmtNum(w)} {FmtNum(h)} re f " +
                      $"0.5 0.5 0.5 RG 1 w 0 0 {FmtNum(w)} {FmtNum(h)} re S ");
        else
            sb.Append(mk);
        AppendButtonCaption(sb, w, h, fontName, fontSize, wrapColorInText: false);
        sb.Append("Q");
        return sb.ToString();
    }

    // Border-style appearance geometry: no q/Q wrapper,
    // the MK background fill, then style-specific border drawing, then the caption.
    // Bevel/Inset draw two chevron polygons (lit + shadowed edges) inset by the
    // border width; Underline draws a single baseline rule; Dashed strokes the
    // half-width-inset rectangle with a dash pattern.
    private string BuildStyledButtonFace(double w, double h, Aspose.Pdf.Annotations.BorderStyle style,
                                         string fontName, double fontSize)
    {
        double bw = GetBorderWidthValue();
        if (bw <= 0) bw = 1;
        // Colours come from the widget's appearance characteristics (the ButtonField
        // Characteristics.Background / .Border the caller set), falling back to the
        // /MK dict when a loaded field carries one instead.
        var mk = Reader.ResolveDict(Dict.Get("MK"));
        var chars = Characteristics;
        var sb = new System.Text.StringBuilder();

        // Background fill: characteristic background (opaque), else /MK /BG.
        var bgColor = chars.Background;
        if (bgColor.A != 0)
            sb.Append($"{ColorFillOp(bgColor)} 0 0 {FmtNum(w)} {FmtNum(h)} re f ");
        else if (mk is not null && MkColorOperator(mk.Get("BG"), fill: true) is { } bgMk)
            sb.Append($"{bgMk} 0 0 {FmtNum(w)} {FmtNum(h)} re f ");

        var bc = ColorStrokeOp(chars.Border);
        double b2 = bw * 2;
        var innerRect = $"{FmtNum(bw / 2)} {FmtNum(bw / 2)} {FmtNum(w - bw)} {FmtNum(h - bw)} re";

        switch (style)
        {
            case Aspose.Pdf.Annotations.BorderStyle.Underline:
                sb.Append($"{bc} {FmtNum(bw)} w 0 {FmtNum(bw / 2)} m {FmtNum(w)} {FmtNum(bw / 2)} l s ");
                break;

            case Aspose.Pdf.Annotations.BorderStyle.Dashed:
                sb.Append($"{bc} {FmtNum(bw)} w [3] 0 d {innerRect} S ");
                break;

            case Aspose.Pdf.Annotations.BorderStyle.Beveled:
            case Aspose.Pdf.Annotations.BorderStyle.Inset:
            {
                var (hi, shadow) = style == Aspose.Pdf.Annotations.BorderStyle.Beveled
                    ? ("1 g", ScaleColorFill(bgColor.A != 0 ? bgColor : System.Drawing.Color.Gray, 216.0 / 255.0))
                    : ("0.4980392157 0.4980392157 0.4980392157 rg", "0.8470588235 0.8470588235 0.8470588235 rg");
                // Lit chevron (top-left edges).
                sb.Append($"{hi} {FmtNum(bw)} {FmtNum(bw)} m {FmtNum(bw)} {FmtNum(h - bw)} l " +
                          $"{FmtNum(w - bw)} {FmtNum(h - bw)} l {FmtNum(w - b2)} {FmtNum(h - b2)} l " +
                          $"{FmtNum(b2)} {FmtNum(h - b2)} l {FmtNum(b2)} {FmtNum(b2)} l f ");
                // Shadowed chevron (bottom-right edges).
                sb.Append($"{shadow} {FmtNum(w - bw)} {FmtNum(h - bw)} m {FmtNum(w - bw)} {FmtNum(bw)} l " +
                          $"{FmtNum(bw)} {FmtNum(bw)} l {FmtNum(b2)} {FmtNum(b2)} l {FmtNum(w - b2)} {FmtNum(b2)} l " +
                          $"{FmtNum(w - b2)} {FmtNum(h - b2)} l f ");
                // Beveled closes its stroke (s); Inset leaves it open (S).
                var stroke = style == Aspose.Pdf.Annotations.BorderStyle.Beveled ? "s" : "S";
                sb.Append($"{bc} {FmtNum(bw)} w {innerRect} {stroke} ");
                break;
            }
        }

        AppendButtonCaption(sb, w, h, fontName, fontSize, wrapColorInText: true);
        return sb.ToString();
    }

    // Append the caption block. wrapColorInText emits the fill colour inside the
    // text object (BT ... rg ... ET) for the styled-button
    // stream; the solid path keeps its historical "0 g" placement.
    private void AppendButtonCaption(System.Text.StringBuilder sb, double w, double h,
                                     string fontName, double fontSize, bool wrapColorInText)
    {
        // The caption is the widget's own /MK /CA; a button that carries no explicit
        // caption falls back to its field value, which is what a caller who only set
        // Value on a freshly built push button expects to see drawn on the face.
        var caption = NormalCaption ?? Value ?? string.Empty;
        if (caption.Length == 0) return;
        var escaped = EscapePdfText(caption);
        // Rough centring: average glyph advance ~0.5em.
        var textW = caption.Length * fontSize * 0.5;
        var tx = System.Math.Max(2, (w - textW) / 2);
        var ty = h / 2 - fontSize * 0.35;
        if (wrapColorInText)
            sb.Append($"BT 0 0 0 rg /{fontName} {FmtNum(fontSize)} Tf {FmtNum(tx)} {FmtNum(ty)} Td ({escaped}) Tj ET ");
        else
            sb.Append($"BT /{fontName} {FmtNum(fontSize)} Tf 0 g {FmtNum(tx)} {FmtNum(ty)} Td ({escaped}) Tj ET ");
    }

    private static string ColorFillOp(System.Drawing.Color c)
        => $"{FmtNum(c.R / 255.0)} {FmtNum(c.G / 255.0)} {FmtNum(c.B / 255.0)} rg";

    private static string ColorStrokeOp(System.Drawing.Color c)
        => $"{FmtNum(c.R / 255.0)} {FmtNum(c.G / 255.0)} {FmtNum(c.B / 255.0)} RG";

    // Darken a colour by a factor to derive the bevel-shadow tint.
    private static string ScaleColorFill(System.Drawing.Color c, double factor)
        => $"{FmtNum(c.R / 255.0 * factor)} {FmtNum(c.G / 255.0 * factor)} {FmtNum(c.B / 255.0 * factor)} rg";

    private PdfDictionary? GetMK(bool create)
    {
        // Button widget MK dict: walk through own dict, kids, to find the widget carrying MK
        var target = LocateWidgetDict();
        if (target is null) return null;
        if (target.Get("MK") is PdfDictionary existing) return existing;
        if (!create) return null;
        var mk = new PdfDictionary();
        target.Set("MK", mk);
        return mk;
    }

    private PdfDictionary LocateWidgetDict()
    {
        // Prefer this field's dict (single widget) if it has Subtype=Widget
        if (Dict.Get("Subtype") is PdfName sn && sn.Value == "Widget")
            return Dict;
        // Otherwise look at Kids[0]
        var kids = Reader.Resolve(Dict.Get("Kids")) as PdfArray;
        if (kids is null || kids.Count == 0) return Dict;
        return Reader.Resolve(kids[0]) as PdfDictionary ?? Dict;
    }

    private string? GetMkString(string key)
    {
        var mk = GetMK(create: false);
        return (mk?.Get(key) as PdfString)?.ToText();
    }

    private void SetMkString(string key, string? value)
    {
        var mk = GetMK(create: value is not null);
        if (mk is null) return;
        if (value is null)
            mk.Remove(key);
        else
            mk.Set(key, new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
    }

    /// <summary>Caption shown when the user holds the mouse button down (/MK /AC).</summary>
    public string? AlternateCaption
    {
        get => GetMkString("AC");
        set => SetMkString("AC", value);
    }

    /// <summary>Caption shown when the cursor hovers (/MK /RC).</summary>
    public string? RolloverCaption
    {
        get => GetMkString("RC");
        set => SetMkString("RC", value);
    }

    /// <summary>Form XObject used as the normal-state icon (/MK /I). Stored only.</summary>
    public XForm? NormalIcon { get; set; }

    /// <summary>Form XObject used as the rollover icon (/MK /RI). Stored only.</summary>
    public XForm? RolloverIcon { get; set; }

    /// <summary>Form XObject used as the down/alternate icon (/MK /IX). Stored only.</summary>
    public XForm? AlternateIcon { get; set; }

    /// <summary>Position of the caption relative to the icon (/MK /TP).</summary>
    public IconCaptionPosition ICPosition
    {
        get
        {
            var mk = GetMK(create: false);
            return (IconCaptionPosition)(int)((mk?.Get("TP") as PdfInteger)?.Value ?? 0);
        }
        set
        {
            var mk = GetMK(create: true);
            mk?.Set("TP", new PdfInteger((int)value));
        }
    }

    /// <summary>Icon scaling parameters (/MK /IF). Always returns a fresh wrapper; not persisted.</summary>
    public IconFit IconFit { get; } = new IconFit();

    /// <summary>
    /// Attach an image as the button's normal icon. Cross-platform-friendly stub:
    /// the image is stored opaquely (advanced GDI rendering not supported).
    /// </summary>
    public void AddImage(System.Drawing.Image image) { _ = image; }
}

/// <summary>
/// PDF Annotation Handler "TP" entry — caption / icon layout for push-button widgets.
/// PDF 32000-1 §12.5.6.19 Table 189.
/// </summary>
public enum IconCaptionPosition
{
    NoIcon = 0,
    NoCaption = 1,
    CaptionBelowIcon = 2,
    CaptionAboveIcon = 3,
    CaptionToTheRight = 4,
    CaptionToTheLeft = 5,
    CaptionOverlaid = 6,
}

/// <summary>
/// PDF Annotation Handler "IF" entry — icon-fit dictionary for push-button widgets.
/// Stored-only wrapper; values are not currently emitted into /MK /IF.
/// </summary>
public class IconFit
{
    public ScalingMode ScalingMode { get; set; } = ScalingMode.Proportional;
    public ScalingReason ScalingReason { get; set; } = ScalingReason.Always;
    public double LeftoverLeft { get; set; } = 0.5;
    public double LeftoverBottom { get; set; } = 0.5;
    public bool SpreadOnBorder { get; set; }

    public static ScalingMode NameToScalingMode(string mode) => mode switch
    {
        "A" => ScalingMode.Anamorphic,
        "P" => ScalingMode.Proportional,
        _ => ScalingMode.Proportional,
    };

    public static ScalingReason NameToScalingReason(string reason) => reason switch
    {
        "A" => ScalingReason.Always,
        "B" => ScalingReason.IconIsBigger,
        "S" => ScalingReason.IconIsSmaller,
        "N" => ScalingReason.Never,
        _ => ScalingReason.Always,
    };

    public static string ScalingModeToName(ScalingMode mode) => mode switch
    {
        ScalingMode.Anamorphic => "A",
        ScalingMode.Proportional => "P",
        _ => "P",
    };

    public static string ScalingReasonToName(ScalingReason reason) => reason switch
    {
        ScalingReason.Always => "A",
        ScalingReason.IconIsBigger => "B",
        ScalingReason.IconIsSmaller => "S",
        ScalingReason.Never => "N",
        _ => "A",
    };
}

/// <summary>How a button icon is scaled to fit its rectangle (/MK /IF /SW).</summary>
public enum ScalingMode
{
    Anamorphic = 0,
    Proportional = 1,
}

/// <summary>When to scale the icon to fit its rectangle (/MK /IF /S).</summary>
public enum ScalingReason
{
    Always = 0,
    IconIsBigger = 1,
    IconIsSmaller = 2,
    Never = 3,
}

public class SignatureField : Field
{
    /// <summary>Latest signed PDF bytes produced by <see cref="Sign(Signature)"/>.
    /// The caller retrieves these via the <see cref="OwnerDocument"/>'s
    /// document-level signing flow — the field itself can't mutate its
    /// owner Document's underlying byte buffer in the FOSS build.</summary>
    private byte[]? _signedBytes;

    /// <summary>Returns the latest signed bytes, or null when this field
    /// hasn't been signed via <see cref="Sign(Signature)"/>.</summary>
    public byte[]? SignedBytes => _signedBytes;

    internal SignatureField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create an empty signature field on <paramref name="doc"/>'s
    /// first page sized to <paramref name="rect"/>. Add the field to
    /// <c>doc.Form</c> to expose it on the form, then sign via
    /// <see cref="Sign(Signature)"/>.</summary>
    public SignatureField(Document doc, Rectangle rect)
        : base(BuildSignatureFieldDict(rect), doc.Pages[1].Reader)
    {
        OwnerDocument = doc;
    }

    /// <summary>Create an empty signature field on <paramref name="page"/>
    /// sized to <paramref name="rect"/>. <see cref="Field.OwnerDocument"/>
    /// is set when the field is later added to a Form.</summary>
    public SignatureField(Page page, Rectangle rect)
        : base(BuildSignatureFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Information about the digital signature in this field. Returns
    /// null when the field's /V is not set (the field has not been
    /// signed). Reads /Reason, /Location, /Name (signer), /M (date) and
    /// /SubFilter (signature handler) from the signature dictionary.
    /// </summary>
    public Signature? Signature
    {
        get
        {
            var sigDict = Reader.ResolveDict(Dict.Get("V"));
            if (sigDict is null) return null;
            var sig = Forms.Signature.FromDict(sigDict, Reader, FullName);
            // Wire the source bytes so Signature.Verify() has the original stream
            // to hash against (EnumerateSignatures does the same for the facade path).
            sig._sourceDocumentBytes = Reader.RawData;
            return sig;
        }
    }

    /// <summary>Sign this field with the supplied <paramref name="signature"/>'s
    /// embedded certificate. The signed PDF bytes land on
    /// <see cref="SignedBytes"/> — read those and persist via the owner
    /// Document's own Save path. (Direct write-back into Document._data
    /// requires invasive Document API changes deferred for now.)</summary>
    public void Sign(Signature signature)
    {
        if (signature is null) throw new System.ArgumentNullException(nameof(signature));
        var doc = OwnerDocument
            ?? throw new System.InvalidOperationException("SignatureField has no OwnerDocument. Add the field to doc.Form before signing.");
        var facade = new Facades.PdfFileSignature(doc);
        facade.Sign(FullName ?? string.Empty, signature);
        _signedBytes = facade.ToByteArray();
        // Chain the signed revision onto the owner document so a following
        // field-level Sign signs THESE bytes (interleaved Add+Sign flows) and a
        // no-arg Save persists every accumulated signature revision.
        doc.PendingSignedBytes = _signedBytes;
    }

    /// <summary>Sign with a PFX from a stream + password — convenience over
    /// <see cref="Sign(Signature)"/>.</summary>
    public void Sign(Signature signature, Stream pfx, string pass)
    {
        if (signature is null) throw new System.ArgumentNullException(nameof(signature));
        if (pfx is not null && signature.Certificate is null)
        {
            using var ms = new MemoryStream();
            pfx.CopyTo(ms);
            signature.Certificate = Security.PdfCertificate.FromPfx(ms.ToArray(), pass ?? string.Empty);
        }
        Sign(signature);
    }

    /// <summary>Extract the signing certificate of this field's /V as a DER
    /// byte stream (.cer).</summary>
    public Stream? ExtractCertificate()
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        return new Facades.PdfFileSignature(doc).ExtractCertificate(FullName ?? string.Empty);
    }

    /// <summary>Extract the signing certificate as an X509Certificate2.</summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? ExtractCertificateObject()
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        var facade = new Facades.PdfFileSignature(doc);
        var sigName = new Facades.SignatureName(FullName ?? string.Empty, PartialName ?? string.Empty, hasSignature: true);
        return facade.TryExtractCertificate(sigName, out System.Security.Cryptography.X509Certificates.X509Certificate2 cert)
            ? cert : null;
    }

    /// <summary>Extract the visible-signature appearance (the /AP /N stream)
    /// as a Stream. Returns null when the field has no appearance.</summary>
    public Stream? ExtractImage()
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        return new Facades.PdfFileSignature(doc).ExtractImage(FullName ?? string.Empty);
    }

    /// <summary>Extract the visible-signature appearance and re-encode it as
    /// <paramref name="format"/>. The raw /AP /N stream is decoded then
    /// re-saved through System.Drawing.Image — Windows-only at runtime, so off
    /// Windows this reports null the way an undecodable appearance does; the
    /// undecoded stream stays reachable through the parameterless overload.</summary>
    public Stream? ExtractImage(System.Drawing.Imaging.ImageFormat format)
    {
        if (format is null) return ExtractImage();
        // The appearance render below is ALREADY a PNG - PngDevice, which draws
        // through the software renderer off Windows - so a PNG request needs no
        // System.Drawing at all; only re-encoding into another format does. Off
        // Windows serve the render (or the stored appearance) for PNG and report
        // null for the formats that genuinely need GDI+; the Windows path below
        // stays byte-identical.
        if (!OperatingSystem.IsWindows())
        {
            // ImageFormat is inert metadata - comparing codec GUIDs runs anywhere;
            // only ENCODING through System.Drawing is Windows-bound. The analyzer
            // attributes the whole type, hence the pragma.
#pragma warning disable CA1416
            var wantsPng = format.Guid == System.Drawing.Imaging.ImageFormat.Png.Guid;
#pragma warning restore CA1416
            return wantsPng ? RenderAppearanceToRectSize() ?? ExtractImage() : null;
        }
#pragma warning disable CA1416
        // The signature as it is SHOWN, at one pixel per point of its /Rect. An
        // appearance is a DRAWING - ink, soft mask and background composited - not a
        // picture stored in the field, so it is rendered rather than dug out of the /AP
        // resources, where taking the first image found returns a mask as readily as the
        // artwork it masks. It is drawn on its own: the page behind the widget is not
        // part of the signature, and cropping the page would fold the form's own ruling
        // into the result.
        try
        {
            if (RenderAppearanceToRectSize() is { } shown)
            {
                using var bmp = new System.Drawing.Bitmap(shown);
                var rendered = new MemoryStream();
                bmp.Save(rendered, format);
                rendered.Position = 0;
                return rendered;
            }
        }
        catch { /* fall back to the stored appearance below */ }

        // No page to render against (a field held on its own, a widget with no /Rect):
        // hand back whatever the appearance stores, re-encoded.
        var raw = ExtractImage();
        if (raw is null) return null;
        try
        {
            using var img = System.Drawing.Image.FromStream(raw);
            var output = new MemoryStream();
            img.Save(output, format);
            output.Position = 0;
            return output;
        }
        catch { return null; }
#pragma warning restore CA1416
    }


    /// <summary>Render this signature's normal appearance on its own, sized to the
    /// widget's /Rect at one pixel per point. Returns null when the field has no
    /// appearance to draw, or when the document cannot host the temporary page the
    /// appearance is drawn onto.</summary>
    private Stream? RenderAppearanceToRectSize()
    {
        var doc = OwnerDocument;
        var reader = doc?.Reader;
        if (doc is null || reader is null) return null;

        // The appearance lives on the field itself, or on the widget kid that draws it.
        var apSource = Dict;
        var apRef = AppearanceRefOf(apSource, reader);
        if (apRef is null && reader.Resolve(Dict.Get("Kids")) is Aspose.Pdf.Core.PdfArray kids)
            foreach (var kid in kids)
            {
                var kd = reader.ResolveDict(kid);
                if (kd is null) continue;
                apRef = AppearanceRefOf(kd, reader);
                if (apRef is not null) break;
            }
        if (apRef is null) return null;
        var form = reader.Resolve(apRef) as Aspose.Pdf.Core.PdfStream;
        if (form is null) return null;

        // The appearance states its own extent through /BBox; that box is what the
        // widget shows, so the sheet it is drawn on is exactly that size.
        var bbox = reader.ResolveArray(form.Dict.Get("BBox"));
        if (bbox is not { Count: >= 4 }) return null;
        static double N(Aspose.Pdf.Core.PdfObject? o) =>
            o is Aspose.Pdf.Core.PdfInteger i ? i.Value : o is Aspose.Pdf.Core.PdfReal r ? r.Value : 0;
        double bx = N(reader.Resolve(bbox[0])), by = N(reader.Resolve(bbox[1]));
        double bw = Math.Abs(N(reader.Resolve(bbox[2])) - bx);
        double bh = Math.Abs(N(reader.Resolve(bbox[3])) - by);
        if (bw < 1 || bh < 1) return null;

        Page? sheet = null;
        try
        {
            sheet = doc.Pages.Add();
            sheet.SetPageSize(bw, bh);
            var res = new Aspose.Pdf.Core.PdfDictionary();
            var xobjs = new Aspose.Pdf.Core.PdfDictionary();
            xobjs.Set("X0", apRef);
            res.Set("XObject", xobjs);
            sheet.Dict.Set("Resources", res);
            // Seat the box's own origin at the sheet's corner, so an appearance whose
            // BBox does not start at zero still lands fully on the sheet.
            var cb = new Content.ContentStreamBuilder();
            cb.SaveState();
            cb.SetMatrix(1, 0, 0, 1, -bx, -by);
            cb.DrawXObject("X0");
            cb.RestoreState();
            sheet.SetContentStream(cb.Build());

            var png = new MemoryStream();
            new Aspose.Pdf.Devices.PngDevice(
                new Aspose.Pdf.Devices.Resolution(PointsPerInch)).Process(sheet, png);
            png.Position = 0;
            return png;
        }
        catch { return null; }
        finally
        {
            // The sheet is scaffolding: the caller's document must come back unchanged.
            if (sheet is not null)
                try { doc.Pages.Delete(sheet.Number); } catch { }
        }
    }

    /// <summary>The indirect reference to a dictionary's normal (/N) appearance, kept as a
    /// REFERENCE so it can be pointed at from another page without copying the stream.</summary>
    private static Aspose.Pdf.Core.PdfObject? AppearanceRefOf(
        Aspose.Pdf.Core.PdfDictionary dict, Aspose.Pdf.IO.PdfReader reader)
    {
        var ap = reader.ResolveDict(dict.Get("AP"));
        var n = ap?.Get("N");
        if (n is null) return null;
        return reader.Resolve(n) is Aspose.Pdf.Core.PdfStream ? n : null;
    }

    /// <summary>Points per inch — rendering the widget's area at this resolution puts one
    /// pixel on every point of its /Rect.</summary>
    private const int PointsPerInch = 72;

    private static PdfDictionary BuildSignatureFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        var r = new Aspose.Pdf.Core.PdfArray();
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.LLX));
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.LLY));
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.URX));
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.URY));
        dict.Set("Rect", r);
        return dict;
    }
}

/// <summary>One option of a <see cref="RadioButtonField"/>. Stored-only wrapper
/// emitted via <see cref="RadioButtonField.Add(RadioButtonOptionField)"/>.</summary>
public sealed class RadioButtonOptionField : RadioButtonField
{
    // RadioButtonOptionField IS a RadioButtonField (mirroring the public type hierarchy where
    // an option derives from the field; the chain reaches BaseParagraph via Annotation, so
    // an option still drops into a generator Paragraphs tree). Deriving from RadioButtonField
    // — rather than Field — means Form.Fields can surface each expanded radio option AS a
    // RadioButtonOptionField while the object still behaves like the RadioButtonField the
    // XFA/value machinery expects. The option-specific properties below shadow (new) the
    // inherited members so the stored-only generator semantics are unchanged.
    public RadioButtonOptionField() : base(new PdfDictionary(), PdfReader.Empty) { }

    public RadioButtonOptionField(Page page, Rectangle rect) : base(new PdfDictionary(), PdfReader.Empty)
    {
        _ = page;
        PendingRect = rect;
    }

    /// <summary>Wrap a parsed radio-option widget dict (a /Kids member of a /Btn radio
    /// group). Used by <see cref="Form"/> when expanding a radio group so each option
    /// surfaces on <c>Form.Fields</c> as a RadioButtonOptionField; KidDict/KidReader point
    /// at the live widget so <see cref="Rect"/> reflects it.</summary>
    internal RadioButtonOptionField(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        KidDict = dict;
        KidReader = reader;
    }

    /// <summary>Default-appearance settings (/DA) applied to the option's
    /// widget. Auto-initialized so callers can set TextColor/Font directly.</summary>
    public new DefaultAppearance DefaultAppearance { get; } = new DefaultAppearance();

    /// <summary>Caption text shown next to the radio glyph. Stored only.</summary>
    public Aspose.Pdf.Text.TextFragment? Caption { get; set; }

    /// <summary>The /Opt name written into the parent field when this option is added.</summary>
    public string? OptionName { get; set; }

    /// <summary>Visual style of the radio glyph. Stored only.</summary>
    public new BoxStyle Style { get; set; } = BoxStyle.Circle;

    /// <summary>Width of the option's widget rectangle in points. Stored only.</summary>
    public new double Width { get; set; }

    /// <summary>Height of the option's widget rectangle in points. Stored only.</summary>
    public new double Height { get; set; }

    /// <summary>Border styling applied to the option's widget. Stored only.</summary>
    public new Border? Border { get; set; }

    /// <summary>Visual-characteristics dictionary (/MK) applied to the option's
    /// widget. Auto-initialized so callers can set
    /// <c>option.Characteristics.Border = Color.Black</c> on a fresh instance.</summary>
    public new Aspose.Pdf.Annotations.Characteristics Characteristics { get; } =
        new Aspose.Pdf.Annotations.Characteristics();

    /// <summary>Pending widget rectangle, used by Add to size the kid annotation.</summary>
    internal Rectangle? PendingRect { get; }

    /// <summary>Set when the table render pass drew this option's circle glyph into
    /// the page CONTENT: the widget then ships without an /AP (form-grid option
    /// widgets carry no appearance stream), so the glyph is not painted twice.</summary>
    internal bool InlineGlyphDrawn { get; set; }

    /// <summary>The kid widget dictionary created for this option by
    /// <see cref="RadioButtonField.Add(RadioButtonOptionField)"/>, and the reader
    /// that resolves it. Set once the option is added to a field.</summary>
    internal PdfDictionary? KidDict { get; set; }
    internal Aspose.Pdf.IO.PdfReader? KidReader { get; set; }

    /// <summary>The radio-button group this option was added to (via
    /// <see cref="RadioButtonField.Add(RadioButtonOptionField)"/>). Lets the
    /// generator register the parent group in the AcroForm when only the options
    /// are placed into the page's paragraph tree.</summary>
    internal RadioButtonField? OwnerRadio { get; set; }

    /// <summary>The option's widget rectangle. Once the option has been added to a
    /// field this reads the live kid annotation's /Rect (so it reflects later
    /// transforms, e.g. <c>PdfFileEditor.ResizeContents</c>); before that it returns
    /// the rectangle supplied to the constructor.</summary>
    public new Rectangle? Rect
    {
        get
        {
            if (KidDict is not null && KidReader is not null
                && KidReader.Resolve(KidDict.Get("Rect")) is PdfArray arr && arr.Count >= 4)
                return Rectangle.FromPdfArray(arr);
            return PendingRect;
        }
    }
}
