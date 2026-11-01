using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public partial class ButtonField
{
    // The icon of the state whose face is being built, and how its caption stands to it.
    private PdfStream? _faceIcon;
    private int _iconPosition;

    /// <summary>The resource name an appearance draws its icon under.</summary>
    private const string IconResourceName = "FRM";

    /// <summary>Space kept between the face's edge and an icon fitted into it.</summary>
    private const double IconInset = 2;

    /// <summary>Height of a caption's line beside an icon, in font sizes.</summary>
    private const double CaptionLineShare = 1.2;

    /// <summary>The icons of the button's states and how its caption stands to them (/MK /TP): the normal
    /// appearance draws /MK /I, the down appearance /MK /IX (else /I). A state without an icon, or a button that
    /// shows its caption only, draws none.</summary>
    private (PdfStream? normal, PdfStream? down, int position) StateIcons()
    {
        var mk = GetMK(create: false);
        if (mk is null) return (null, null, 0);
        var position = (int)((Reader.Resolve(mk.Get("TP")) as PdfInteger)?.Value ?? 0);
        if (position == (int)IconCaptionPosition.NoIcon) return (null, null, position);
        var normal = Reader.ResolveStream(mk.Get("I"));
        return (normal, Reader.ResolveStream(mk.Get("IX")) ?? normal, position);
    }

    /// <summary>The regions of an icon and its caption in a w x h face, as the caption position (/MK /TP)
    /// places them: the caption below, above, right or left of the icon takes a line of its height or its
    /// width; overlaid, both take the whole face; a button that shows its icon only draws no caption.</summary>
    private static ((double x, double y, double w, double h) icon, (double x, double y, double w, double h)? caption)
        IconLayout(double w, double h, int position, double captionWidth, double fontSize)
    {
        double line = System.Math.Min(h, fontSize * CaptionLineShare);
        double side = System.Math.Min(w, captionWidth + 2 * IconInset);
        return (IconCaptionPosition)position switch
        {
            IconCaptionPosition.NoCaption => ((0, 0, w, h), null),
            IconCaptionPosition.CaptionBelowIcon => ((0, line, w, h - line), (0, 0, w, line)),
            IconCaptionPosition.CaptionAboveIcon => ((0, 0, w, h - line), (0, h - line, w, line)),
            IconCaptionPosition.CaptionToTheRight => ((0, 0, w - side, h), (w - side, 0, side, h)),
            IconCaptionPosition.CaptionToTheLeft => ((side, 0, w - side, h), (0, 0, side, h)),
            _ => ((0, 0, w, h), (0, 0, w, h)),
        };
    }

    /// <summary>Draws an icon form fitted into a region: its box scaled to the region less an inset, keeping its
    /// aspect, and centred in it (the /MK /IF defaults).</summary>
    private string DrawIcon(PdfStream icon, (double x, double y, double w, double h) region)
    {
        var box = Reader.ResolveArray(icon.Dict.Get("BBox"));
        if (box is not { Count: >= 4 }) return string.Empty;
        var r = IconBounds(Rectangle.FromPdfArray(box, Reader), Reader.ResolveArray(icon.Dict.Get("Matrix")));
        double availW = region.w - 2 * IconInset, availH = region.h - 2 * IconInset;
        if (r.Width <= 0 || r.Height <= 0 || availW <= 0 || availH <= 0) return string.Empty;
        double scale = System.Math.Min(availW / r.Width, availH / r.Height);
        double tx = region.x + IconInset + (availW - r.Width * scale) / 2 - r.LLX * scale;
        double ty = region.y + IconInset + (availH - r.Height * scale) / 2 - r.LLY * scale;
        return $"q {FmtNum(scale)} 0 0 {FmtNum(scale)} {FmtNum(tx)} {FmtNum(ty)} cm /{IconResourceName} Do Q ";
    }

    /// <summary>Where an icon form draws: its box under its own /Matrix.</summary>
    private static Rectangle IconBounds(Rectangle box, PdfArray? matrix)
    {
        const int matrixLength = 6;
        if (matrix is not { Count: matrixLength }) return box;
        var m = new double[matrixLength];
        for (int i = 0; i < matrixLength; i++) m[i] = Functions.PdfArrayHelper.GetDouble(matrix, i);
        double[] xs = { box.LLX, box.URX, box.LLX, box.URX }, ys = { box.LLY, box.LLY, box.URY, box.URY };
        var tx = new double[xs.Length];
        var ty = new double[ys.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            tx[i] = m[0] * xs[i] + m[2] * ys[i] + m[4];
            ty[i] = m[1] * xs[i] + m[3] * ys[i] + m[5];
        }
        return new Rectangle(tx.Min(), ty.Min(), tx.Max(), ty.Max());
    }

    /// <summary>The appearance resources with the state's icon among its XObjects.</summary>
    private PdfDictionary WithIcon(PdfDictionary resources, PdfStream? icon)
    {
        if (icon is null) return resources;
        var xobjects = new PdfDictionary();
        xobjects.Set(IconResourceName, icon);
        resources.Set("XObject", xobjects);
        return resources;
    }

    /// <summary>Makes the picture the button's normal icon (/MK /I): a form that draws it at its pixel size,
    /// shown by the button's appearance, which is drawn again - icon only when the button showed no icon
    /// before. A picture that cannot be read is ignored.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AddImage(System.Drawing.Image image)
    {
        if (image is null) throw new System.ArgumentNullException(nameof(image));
        using var ms = new System.IO.MemoryStream();
        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        PdfStream picture;
        try { picture = new ImageStamp(ms).BuildImageXObject(); }
        catch (System.Exception e) when (e is System.ArgumentException or System.NotSupportedException) { return; }
        double w = image.Width, h = image.Height;
        if (w <= 0 || h <= 0) return;
        var images = new PdfDictionary();
        images.Set("Im0", picture);
        var resources = new PdfDictionary();
        resources.Set("XObject", images);
        var icon = MakeApXObject($"q {FmtNum(w)} 0 0 {FmtNum(h)} 0 0 cm /Im0 Do Q", w, h, resources);
        NormalIcon = new XForm(icon, Reader);
        // A button that showed its caption only now shows the picture.
        if (ICPosition == IconCaptionPosition.NoIcon) ICPosition = IconCaptionPosition.NoCaption;
        Dict.Remove("AP");
        GenerateAppearance();
    }
}
