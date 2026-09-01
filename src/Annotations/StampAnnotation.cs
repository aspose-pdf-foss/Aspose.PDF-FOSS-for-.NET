using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class StampAnnotation : MarkupAnnotation
{
    internal StampAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public StampAnnotation(Document document) : base(document)
    {
        Dict.Set("Subtype", new PdfName("Stamp"));
    }

    public StampAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Stamp"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Stamp;

    private StampIcon _icon = StampIcon.Draft;

    /// <summary>Named stamp icon. Setting it records the standard /Name and
    /// regenerates the stamp's normal appearance (a bordered banner with the
    /// stamp's label).</summary>
    public StampIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            Dict.Set("Name", new PdfName(value.ToString()));
            UpdateAppearances();
        }
    }

    private System.IO.Stream? _image;

    /// <summary>The stamp's image. When set programmatically the stored stream is
    /// returned; otherwise, for a stamp loaded from a document, the image is extracted
    /// from the normal appearance (/AP /N) — the first image XObject in its resources —
    /// and returned as a PNG stream.</summary>
    public System.IO.Stream? Image
    {
        get => _image ?? ExtractAppearanceImage();
        set
        {
            _image = value;
            // Embed the image into the normal appearance at its native resolution so the
            // stamp renders and round-trips through save (a reopened stamp's Image then
            // extracts the full-size source rather than nothing).
            if (value is not null) BuildImageAppearance(value);
        }
    }

    /// <summary>Generate the normal appearance (/AP /N) as a Form XObject that draws
    /// <paramref name="image"/> at native resolution, scaled to fill the stamp rectangle.
    /// The image XObject keeps the source pixel dimensions (DCTDecode pass-through for JPEG),
    /// so the resolution survives the save/reload round-trip.</summary>
    private void BuildImageAppearance(System.IO.Stream image)
    {
        var r = Rect;
        if (r is null) return;
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) return;

        byte[] bytes;
        if (image.CanSeek) image.Seek(0, System.IO.SeekOrigin.Begin);
        using (var ms = new System.IO.MemoryStream()) { image.CopyTo(ms); bytes = ms.ToArray(); }
        if (image.CanSeek) image.Seek(0, System.IO.SeekOrigin.Begin);
        if (bytes.Length == 0) return;

        Core.PdfStream imgXObject;
        try { imgXObject = new Aspose.Pdf.ImageStamp(new System.IO.MemoryStream(bytes)).BuildImageXObject(); }
        catch { return; } // not a decodable image — leave the stored stream untouched

        static string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        // Map the unit image space into the BBox: q w 0 0 h 0 0 cm /Im0 Do Q.
        var content = System.Text.Encoding.ASCII.GetBytes($"q {F(w)} 0 0 {F(h)} 0 0 cm /Im0 Do Q");

        var form = new Core.PdfDictionary();
        form.Set("Type", new Core.PdfName("XObject"));
        form.Set("Subtype", new Core.PdfName("Form"));
        form.Set("FormType", new Core.PdfInteger(1));
        var bb = new Core.PdfArray();
        bb.Add(new Core.PdfReal(0)); bb.Add(new Core.PdfReal(0));
        bb.Add(new Core.PdfReal(w)); bb.Add(new Core.PdfReal(h));
        form.Set("BBox", bb);

        var xobjs = new Core.PdfDictionary();
        xobjs.Set("Im0", imgXObject);
        var res = new Core.PdfDictionary();
        res.Set("XObject", xobjs);
        form.Set("Resources", res);
        form.Set("Length", new Core.PdfInteger(content.Length));

        var ap = InternalReader.ResolveDict(Dict.Get("AP")) ?? new Core.PdfDictionary();
        ap.Set("N", new Core.PdfStream(form, content));
        Dict.Set("AP", ap);
    }

    private System.IO.Stream? ExtractAppearanceImage()
    {
        var form = NormalAppearance;
        if (form is null) return null;
        var imgStream = FindImageXObject(form.StreamDict, form.Reader, 0);
        if (imgStream is null) return null;
        try
        {
            var xi = new Aspose.Pdf.XImage("StampImage", imgStream, form.Reader);
            return new System.IO.MemoryStream(xi.ToPng());
        }
        catch { return null; }
    }

    private static Core.PdfStream? FindImageXObject(Core.PdfDictionary streamDict, IO.PdfReader reader, int depth)
    {
        if (depth > 8) return null;
        var res = reader.ResolveDict(streamDict.Get("Resources"));
        var xobjs = reader.ResolveDict(res?.Get("XObject"));
        if (xobjs is null) return null;
        foreach (var key in xobjs.Keys)
        {
            if (reader.ResolveStream(xobjs.Get(key)) is not { } s) continue;
            var sub = s.Dict.GetName("Subtype");
            if (sub == "Image") return s;
            if (sub == "Form" && FindImageXObject(s.Dict, reader, depth + 1) is { } nested) return nested;
        }
        return null;
    }

    private static (string label, double r, double g, double b) StampStyle(StampIcon icon) => icon switch
    {
        StampIcon.Approved => ("APPROVED", 0.08, 0.51, 0.16),
        StampIcon.Final => ("FINAL", 0.08, 0.51, 0.16),
        StampIcon.ForPublicRelease => ("FOR PUBLIC RELEASE", 0.08, 0.51, 0.16),
        StampIcon.Sold => ("SOLD", 0.12, 0.24, 0.67),
        StampIcon.Departmental => ("DEPARTMENTAL", 0.12, 0.24, 0.67),
        StampIcon.Experimental => ("EXPERIMENTAL", 0.12, 0.24, 0.67),
        StampIcon.NotApproved => ("NOT APPROVED", 0.78, 0.12, 0.12),
        StampIcon.AsIs => ("AS IS", 0.78, 0.12, 0.12),
        StampIcon.Expired => ("EXPIRED", 0.78, 0.12, 0.12),
        StampIcon.NotForPublicRelease => ("NOT FOR PUBLIC RELEASE", 0.78, 0.12, 0.12),
        StampIcon.Confidential => ("CONFIDENTIAL", 0.78, 0.12, 0.12),
        StampIcon.ForComment => ("FOR COMMENT", 0.78, 0.12, 0.12),
        StampIcon.TopSecret => ("TOP SECRET", 0.78, 0.12, 0.12),
        _ => ("DRAFT", 0.78, 0.12, 0.12),
    };

    /// <summary>Regenerate the normal appearance (/AP /N): a bordered banner
    /// carrying the stamp's label in the stamp colour.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) return;
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) return;

        var (label, cr, cg, cb) = StampStyle(_icon);
        var len = System.Math.Max(1, label.Length);
        // ~0.6em average glyph advance for Helvetica caps; size to fit the width.
        var fontSize = System.Math.Max(4.0, System.Math.Min(h * 0.45, 1.55 * w / len));
        var textW = len * fontSize * 0.6;
        var tx = r.LLX + (w - textW) / 2;
        var ty = r.LLY + (h - fontSize) / 2;

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(cr, cg, cb);
        b.SetLineWidth(System.Math.Max(1.0, h * 0.05));
        b.Rectangle(r.LLX + 1, r.LLY + 1, w - 2, h - 2);
        b.Stroke();
        b.SetFillColor(cr, cg, cb);
        b.BeginText();
        b.SetFont("Helv", fontSize);
        b.MoveTextPosition(tx, ty);
        b.ShowText(label);
        b.EndText();
        b.RestoreState();
        SetNormalAppearanceWithHelvetica(b.Build(), r);
    }

    public string? IconName => Dict.GetName("Name");

    /// <summary>The stamp's normal appearance (/AP /N stream) wrapped as an XForm.</summary>
    public override XForm? NormalAppearance
    {
        get
        {
            var ap = InternalReader.ResolveDict(Dict.Get("AP"));
            if (ap is null) return null;
            var nStream = InternalReader.ResolveStream(ap.Get("N"));
            return nStream is null ? null : new XForm(nStream, InternalReader);
        }
    }
}
