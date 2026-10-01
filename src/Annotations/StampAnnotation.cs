using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>A rubber-stamp annotation: a stamp such as "Approved" or "Draft", or a custom image, shown on the page.</summary>
public partial class StampAnnotation : MarkupAnnotation
{
    internal StampAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Creates a rubber-stamp annotation for <c>document</c>; set its rectangle and add it to a page.</summary>
    public StampAnnotation(Document document) : base(document)
    {
        Dict.Set("Subtype", new PdfName("Stamp"));
    }

    /// <summary>Creates a rubber-stamp annotation on <c>page</c> at <c>rect</c>.</summary>
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

    /// <summary>The ExtGState name an image stamp's appearance selects for its opacity.</summary>
    private const string OpacityStateName = "TransGs";

    /// <summary>Annotation opacity; an image stamp redraws its appearance under the new
    /// value, so an opacity set after the image still reaches the saved appearance.</summary>
    public override double Opacity
    {
        get => base.Opacity;
        set
        {
            base.Opacity = value;
            if (_image is not null) BuildImageAppearance(_image);
        }
    }

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
        // Map the unit image space into the BBox: q w 0 0 h 0 0 cm /Im0 Do Q - under the
        // stamp's opacity, selected as the TransGs graphics state, when it is not opaque.
        var opacity = Opacity;
        var gsOp = opacity < 1.0 ? $"/{OpacityStateName} gs " : string.Empty;
        var content = System.Text.Encoding.ASCII.GetBytes($"q {gsOp}{F(w)} 0 0 {F(h)} 0 0 cm /Im0 Do Q");

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
        if (opacity < 1.0)
        {
            var gs = new Core.PdfDictionary();
            gs.Set("Type", new Core.PdfName("ExtGState"));
            gs.Set("CA", new Core.PdfReal(opacity));
            gs.Set("ca", new Core.PdfReal(opacity));
            var egs = new Core.PdfDictionary();
            egs.Set(OpacityStateName, gs);
            res.Set("ExtGState", egs);
        }
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

    // The standard stamp palette (measured on the reference icons): approvals and
    // releases are green, sales and departmental marks blue, everything else red.
    private static readonly (double r, double g, double b) StampRed = (0.88, 0.20, 0.14);
    private static readonly (double r, double g, double b) StampGreen = (0.24, 0.66, 0.29);
    private static readonly (double r, double g, double b) StampBlue = (0.05, 0.43, 0.77);

    private static (string label, double r, double g, double b) StampStyle(StampIcon icon)
    {
        var (label, colour) = icon switch
        {
            StampIcon.Approved => ("APPROVED", StampGreen),
            StampIcon.ForComment => ("FOR COMMENT", StampGreen),
            StampIcon.ForPublicRelease => ("FOR PUBLIC RELEASE", StampGreen),
            StampIcon.Sold => ("SOLD", StampBlue),
            StampIcon.Departmental => ("DEPARTMENTAL", StampBlue),
            StampIcon.Experimental => ("EXPERIMENTAL", StampBlue),
            StampIcon.Final => ("FINAL", StampRed),
            StampIcon.NotApproved => ("NOT APPROVED", StampRed),
            StampIcon.AsIs => ("AS IS", StampRed),
            StampIcon.Expired => ("EXPIRED", StampRed),
            StampIcon.NotForPublicRelease => ("NOT FOR PUBLIC RELEASE", StampRed),
            StampIcon.Confidential => ("CONFIDENTIAL", StampRed),
            StampIcon.TopSecret => ("TOP SECRET", StampRed),
            _ => ("DRAFT", StampRed),
        };
        return (label, colour.r, colour.g, colour.b);
    }

    // The rubber-stamp frame (measured on a 200 x 50 pt icon): a heavy rounded outer
    // frame set in from the box, a hairline inner frame inside it, and a pale drop
    // shadow under both; the label fills the inner frame in bold capitals, breaking
    // into two lines when one line would have to shrink too far.
    private const double OuterFrameInset = 5.5;
    private const double OuterFrameWidth = 2.4;
    private const double OuterFrameRadius = 4.0;
    private const double InnerFrameInset = 9.5;
    private const double InnerFrameWidth = 1.0;
    private const double InnerFrameRadius = 2.5;
    private const double LabelSideInset = 15.0;
    private const double LabelMaxSizeOfHeight = 0.55;
    private const double TwoLineMaxSizeOfHeight = 0.36;
    private const double TwoLineThreshold = 0.62;
    private const double TwoLinePitch = 1.15;
    private const double CapHeightEm = 0.72;
    private const double ShadowOffset = 1.5;
    private const double ShadowGray = 0.85;
    private const string BoldFace = "Helvetica-Bold";
    private const string BoldResource = "HeBo";

    /// <summary>Regenerate the normal appearance (/AP /N): the standard rubber-stamp
    /// frame carrying the icon's label in the stamp colour.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) return;
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) return;

        var (label, cr, cg, cb) = StampStyle(_icon);
        var lines = LayoutLabel(label, w - 2 * LabelSideInset, h);

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        DrawFrames(b, r, ShadowOffset, -ShadowOffset, (ShadowGray, ShadowGray, ShadowGray));
        DrawLabel(b, r, lines, ShadowOffset, -ShadowOffset, (ShadowGray, ShadowGray, ShadowGray));
        DrawFrames(b, r, 0, 0, (cr, cg, cb));
        DrawLabel(b, r, lines, 0, 0, (cr, cg, cb));
        b.RestoreState();
        SetNormalAppearanceWithHelvetica(b.Build(), r);
    }

    private static void DrawFrames(Content.ContentStreamBuilder b, Rectangle r, double dx, double dy,
        (double r, double g, double b) colour)
    {
        b.SetStrokeColor(colour.r, colour.g, colour.b);
        b.SetLineWidth(OuterFrameWidth);
        RoundedRectangle(b, r.LLX + OuterFrameInset + dx, r.LLY + OuterFrameInset + dy,
            r.Width - 2 * OuterFrameInset, r.Height - 2 * OuterFrameInset, OuterFrameRadius);
        b.Stroke();
        b.SetLineWidth(InnerFrameWidth);
        RoundedRectangle(b, r.LLX + InnerFrameInset + dx, r.LLY + InnerFrameInset + dy,
            r.Width - 2 * InnerFrameInset, r.Height - 2 * InnerFrameInset, InnerFrameRadius);
        b.Stroke();
    }

    private static void RoundedRectangle(Content.ContentStreamBuilder b, double x, double y, double w, double h, double radius)
    {
        var rad = System.Math.Min(radius, System.Math.Min(w, h) / 2);
        var k = 0.5523 * rad;
        b.MoveTo(x + rad, y);
        b.LineTo(x + w - rad, y);
        b.CurveTo(x + w - rad + k, y, x + w, y + rad - k, x + w, y + rad);
        b.LineTo(x + w, y + h - rad);
        b.CurveTo(x + w, y + h - rad + k, x + w - rad + k, y + h, x + w - rad, y + h);
        b.LineTo(x + rad, y + h);
        b.CurveTo(x + rad - k, y + h, x, y + h - rad + k, x, y + h - rad);
        b.LineTo(x, y + rad);
        b.CurveTo(x, y + rad - k, x + rad - k, y, x + rad, y);
        b.ClosePath();
    }

    /// <summary>The label's lines with the size they draw at: one line at up to
    /// <see cref="LabelMaxSizeOfHeight"/> of the box height, or two lines split at the
    /// space nearest the middle when the single line would fall below
    /// <see cref="TwoLineThreshold"/> of that size.</summary>
    private static (System.Collections.Generic.List<string> lines, double fontSize) LayoutLabel(string label, double innerW, double h)
    {
        var fsMax = h * LabelMaxSizeOfHeight;
        var oneLine = System.Math.Min(fsMax, innerW / System.Math.Max(0.001, LabelWidthEm(label)));
        var split = SplitNearMiddle(label);
        if (oneLine >= fsMax * TwoLineThreshold || split is null)
            return (new System.Collections.Generic.List<string> { label }, oneLine);
        var widest = System.Math.Max(LabelWidthEm(split.Value.first), LabelWidthEm(split.Value.second));
        var twoLines = System.Math.Min(h * TwoLineMaxSizeOfHeight, innerW / System.Math.Max(0.001, widest));
        return (new System.Collections.Generic.List<string> { split.Value.first, split.Value.second }, twoLines);
    }

    private static (string first, string second)? SplitNearMiddle(string label)
    {
        var best = -1;
        for (var i = 0; i < label.Length; i++)
            if (label[i] == ' ' && (best < 0 || System.Math.Abs(i - label.Length / 2.0) < System.Math.Abs(best - label.Length / 2.0)))
                best = i;
        return best < 0 ? null : (label[..best], label[(best + 1)..]);
    }

    /// <summary>The label's advance in em at the bold face.</summary>
    private static double LabelWidthEm(string text)
    {
        var units = 0.0;
        foreach (var c in text) units += Text.Standard14Fonts.GetWidth(BoldFace, c);
        return units / 1000.0;
    }

    private static void DrawLabel(Content.ContentStreamBuilder b, Rectangle r,
        (System.Collections.Generic.List<string> lines, double fontSize) layout, double dx, double dy,
        (double r, double g, double b) colour)
    {
        var (lines, fs) = layout;
        var cap = fs * CapHeightEm;
        var pitch = fs * TwoLinePitch;
        var block = cap + (lines.Count - 1) * pitch;
        var cx = (r.LLX + r.URX) / 2 + dx;
        var top = (r.LLY + r.URY) / 2 + block / 2 + dy;
        b.SetFillColor(colour.r, colour.g, colour.b);
        b.BeginText();
        b.SetFont(BoldResource, fs);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineW = LabelWidthEm(lines[i]) * fs;
            b.SetTextMatrix(1, 0, 0, 1, cx - lineW / 2, top - cap - i * pitch);
            b.ShowText(lines[i]);
        }
        b.EndText();
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
