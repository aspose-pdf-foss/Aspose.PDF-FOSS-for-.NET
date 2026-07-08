using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Visitor that filters annotations across one or more pages. Used by
/// <see cref="PageCollection.Accept(AnnotationSelector)"/> and
/// <see cref="AnnotationCollection.Accept(AnnotationSelector)"/> to populate
/// <see cref="Selected"/> with the annotations that match a typed Visit
/// overload. Subclass and override the relevant <c>Visit(SubType)</c>
/// methods to filter by annotation kind; the default behaviour is to add
/// every visited annotation to <see cref="Selected"/>.
/// </summary>
public class AnnotationSelector
{
    private readonly Annotation? _template;

    /// <summary>Annotations matched by the most recent Accept-walk.</summary>
    public IList<Annotation> Selected { get; } = new List<Annotation>();

    /// <summary>Create a selector that accepts every annotation.</summary>
    public AnnotationSelector() { }

    /// <summary>Create a selector that retains <paramref name="annotation"/>
    /// as a template (kept for API parity; not currently consulted by the
    /// default Visit implementations).</summary>
    public AnnotationSelector(Annotation annotation)
    {
        _template = annotation;
    }

    private void Match(Annotation annotation)
    {
        if (annotation is null) return;
        // When constructed with a template annotation, the selector acts as
        // a type filter: only annotations whose runtime class equals the
        // template's class are admitted. This matches the Aspose.Pdf
        // expectation that a caller passing 'new LinkAnnotation(page, rect)'
        // as the template gets back ONLY LinkAnnotation instances
        // (the cast '(LinkAnnotation)anno' would otherwise crash when a
        // StampAnnotation was admitted on a mixed-annotation page).
        if (_template is not null && annotation.GetType() != _template.GetType())
            return;
        Selected.Add(annotation);
    }

    public virtual void Visit(BleedMarkAnnotation bleedMark) => Match(bleedMark);
    public virtual void Visit(CaretAnnotation caret) => Match(caret);
    public virtual void Visit(CircleAnnotation circle) => Match(circle);
    public virtual void Visit(ColorBarAnnotation colorBar) => Match(colorBar);
    public virtual void Visit(FileAttachmentAnnotation attachment) => Match(attachment);
    public virtual void Visit(FreeTextAnnotation freetext) => Match(freetext);
    public virtual void Visit(HighlightAnnotation highlight) => Match(highlight);
    public virtual void Visit(InkAnnotation ink) => Match(ink);
    public virtual void Visit(LineAnnotation line) => Match(line);
    public virtual void Visit(LinkAnnotation link) => Match(link);
    public virtual void Visit(MovieAnnotation movie) => Match(movie);
    public virtual void Visit(PDF3DAnnotation pdf3D) => Match(pdf3D);
    public virtual void Visit(PageInformationAnnotation pageInformation) => Match(pageInformation);
    public virtual void Visit(PolygonAnnotation polygon) => Match(polygon);
    public virtual void Visit(PolylineAnnotation polyline) => Match(polyline);
    public virtual void Visit(PopupAnnotation popup) => Match(popup);
    public virtual void Visit(RedactionAnnotation redact) => Match(redact);
    public virtual void Visit(RegistrationMarkAnnotation registrationMark) => Match(registrationMark);
    public virtual void Visit(RichMediaAnnotation richMedia) => Match(richMedia);
    public virtual void Visit(ScreenAnnotation screen) => Match(screen);
    public virtual void Visit(SquareAnnotation square) => Match(square);
    public virtual void Visit(SquigglyAnnotation squiggly) => Match(squiggly);
    public virtual void Visit(StampAnnotation stamp) => Match(stamp);
    public virtual void Visit(StrikeOutAnnotation strikeOut) => Match(strikeOut);
    public virtual void Visit(TextAnnotation text) => Match(text);
    public virtual void Visit(TrimMarkAnnotation trimMark) => Match(trimMark);
    public virtual void Visit(UnderlineAnnotation underline) => Match(underline);

    /// <summary>Visit the WatermarkAnnotation builder type.</summary>
    public virtual void Visit(WatermarkAnnotation watermark) { _ = watermark; }

    public virtual void Visit(WidgetAnnotation widget) => Match(widget);
}

// ── Stub annotation types (pre-press marker annotations) ───────────────────
//
// These six types exist in the public Aspose.Pdf API surface but the
// underlying PDF semantics are pre-press / printer-mark only -- the FOSS
// HTML / image-output paths don't render them, so the stubs hold just the
// bare ctor + visitor-Accept hookup needed to compile reflection-equivalent
// callers.

public sealed partial class BleedMarkAnnotation : Annotation
{
    internal BleedMarkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);

    public BleedMarkAnnotation(Page page, PrinterMarkCornerPosition position) : base(page, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("PrinterMark"));
        Position = position;
    }

    public new AnnotationType AnnotationType => AnnotationType.BleedMark;

    /// <summary>Which corner of the page this mark sits in. Stored only.</summary>
    public PrinterMarkCornerPosition Position { get; set; } = PrinterMarkCornerPosition.TopLeft;
}

public sealed partial class ColorBarAnnotation : Annotation
{
    // Tint percentages (low-to-high) of the stepped colour scale.
    private static readonly double[] ColorBarTints = { 0, 5, 25, 50, 75, 95, 100 };

    private ColorsOfCMYK _colorOfCMYK = ColorsOfCMYK.Black;

    internal ColorBarAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);

    /// <summary>Create a colour-bar pre-press annotation for the selected CMYK channel.</summary>
    public ColorBarAnnotation(Page page, Rectangle rect, ColorsOfCMYK colorOfCMYK = ColorsOfCMYK.Black) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("PrinterMark"));
        // Print-only, non-interactive mark.
        Dict.Set("F", new PdfInteger(4));
        _colorOfCMYK = colorOfCMYK;
        UpdateAppearances();
    }

    /// <summary>Always <see cref="AnnotationType.ColorBar"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.ColorBar;

    /// <summary>CMYK channel rendered by this bar. Updating it regenerates the
    /// annotation appearance.</summary>
    public ColorsOfCMYK ColorOfCMYK
    {
        get => _colorOfCMYK;
        set { _colorOfCMYK = value; UpdateAppearances(); }
    }

    private Color ColorBarTintColor(double tintPercent)
    {
        var t = tintPercent / 100.0;
        return _colorOfCMYK switch
        {
            ColorsOfCMYK.Cyan => Color.FromCmyk(t, 0, 0, 0),
            ColorsOfCMYK.Magenta => Color.FromCmyk(0, t, 0, 0),
            ColorsOfCMYK.Yellow => Color.FromCmyk(0, 0, t, 0),
            _ => Color.FromCmyk(0, 0, 0, t),
        };
    }

    /// <summary>Regenerate the normal appearance (/AP /N): a strip of tint
    /// patches (0–100%) bordered in black with the tint percentage labelled in
    /// each patch, laid out along the bar's long axis.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) return;
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) return;
        var vertical = w < h;
        var n = ColorBarTints.Length;
        var fontSize = System.Math.Max(4.0, System.Math.Min(9.0, (vertical ? h / n : w / n) * 0.4));

        var b = new Aspose.Pdf.Content.ContentStreamBuilder();
        b.SaveState();
        b.SetLineWidth(0.5);
        b.SetStrokeGray(0.0);
        for (var i = 0; i < n; i++)
        {
            double px, py, pw, ph;
            if (vertical) { pw = w; ph = h / n; px = r.LLX; py = r.LLY + i * ph; }
            else { pw = w / n; ph = h; px = r.LLX + i * pw; py = r.LLY; }
            b.SetFillColor(ColorBarTintColor(ColorBarTints[i]));
            b.Rectangle(px, py, pw, ph);
            b.FillAndStroke();
        }
        // Tint percentage labels: white on dark patches, the full bar colour on
        // light ones, so they read against the patch.
        for (var i = 0; i < n; i++)
        {
            double px, py, pw, ph;
            if (vertical) { pw = w; ph = h / n; px = r.LLX; py = r.LLY + i * ph; }
            else { pw = w / n; ph = h; px = r.LLX + i * pw; py = r.LLY; }
            if (ColorBarTints[i] >= 50) b.SetFillColor(1, 1, 1);
            else b.SetFillColor(ColorBarTintColor(100));
            b.BeginText();
            b.SetFont("Helv", fontSize);
            b.MoveTextPosition(px + 2, py + ph - fontSize - 1);
            b.ShowText(((int)ColorBarTints[i]).ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.EndText();
        }
        b.RestoreState();
        SetColorBarAppearance(b.Build(), r);
    }

    // Build the /AP /N form XObject with a Helvetica resource so the tint labels render.
    private void SetColorBarAppearance(byte[] content, Rectangle r)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(r.LLX)); bb.Add(new PdfReal(r.LLY));
        bb.Add(new PdfReal(r.URX)); bb.Add(new PdfReal(r.URY));
        form.Set("BBox", bb);

        var helv = new PdfDictionary();
        helv.Set("Type", new PdfName("Font"));
        helv.Set("Subtype", new PdfName("Type1"));
        helv.Set("BaseFont", new PdfName("Helvetica"));
        var fonts = new PdfDictionary();
        fonts.Set("Helv", helv);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        form.Set("Resources", res);
        form.Set("Length", new PdfInteger(content.Length));

        var ap = InternalReader.ResolveDict(Dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", new PdfStream(form, content));
        Dict.Set("AP", ap);
    }

    /// <summary>Transform the annotation rect through <paramref name="transform"/>.</summary>
    public new void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        var r = Rect;
        if (r is null) return;
        transform.Transform(r.LLX, r.LLY, out var x1, out var y1);
        transform.Transform(r.URX, r.URY, out var x2, out var y2);
        Rect = new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        UpdateAppearances();
    }
}

public sealed partial class PDF3DAnnotation : Annotation
{
    private byte[] _imagePreview = System.Array.Empty<byte>();
    private int _defaultViewIndex;

    internal PDF3DAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public PDF3DAnnotation(Page page, Rectangle rect, PDF3DArtwork pdf3DArtwork) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("3D"));
        Pdf3DArtwork = pdf3DArtwork;
    }

    public PDF3DAnnotation(Page page, Rectangle rect, PDF3DArtwork pdf3DArtwork, PDF3DActivation activation)
        : this(page, rect, pdf3DArtwork)
    {
        _ = activation; // stored as part of the activation dict; FOSS keeps it nominal
    }

    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);

    public new AnnotationType AnnotationType => AnnotationType.PDF3D;

    private PDF3DArtwork? _artwork;
    private bool _artworkResolved;

    /// <summary>The 3D artwork behind this annotation. For an annotation read
    /// from an existing document it is lazily reconstructed from the /3DD 3D
    /// stream (content + views + per-view cross-sections) on first access.</summary>
    public PDF3DArtwork? Pdf3DArtwork
    {
        get
        {
            if (_artwork is null && !_artworkResolved)
            {
                _artworkResolved = true;
                _artwork = TryReadArtwork();
            }
            return _artwork;
        }
        private set { _artwork = value; _artworkResolved = true; }
    }

    /// <summary>Default (annotation-level) 3D view, taken from the artwork's
    /// first view when present.</summary>
    public PDF3DView? DefaultView
    {
        get
        {
            var va = Pdf3DArtwork?.ViewArray;
            return va is { Count: > 0 } ? va[_defaultViewIndex + 1] : null;
        }
    }

    public PDF3DContent? Content
    {
        get => Pdf3DArtwork?.Content;
        set { if (Pdf3DArtwork is not null) Pdf3DArtwork.Content = value; }
    }

    public PDF3DLightingScheme? LightingScheme => Pdf3DArtwork?.LightingScheme;
    public PDF3DRenderMode? RenderMode => Pdf3DArtwork?.RenderMode;
    public PDF3DViewArray? ViewArray => Pdf3DArtwork?.ViewArray;

    public void SetDefaultViewIndex(int index) => _defaultViewIndex = index;

    public Stream GetImagePreview() => new MemoryStream(_imagePreview, writable: false);

    public void SetImagePreview(Stream image)
    {
        if (image is null) { _imagePreview = System.Array.Empty<byte>(); return; }
        using var ms = new MemoryStream();
        image.CopyTo(ms);
        _imagePreview = ms.ToArray();
    }

    public void SetImagePreview(string filename)
    {
        _imagePreview = string.IsNullOrEmpty(filename) || !File.Exists(filename)
            ? System.Array.Empty<byte>()
            : File.ReadAllBytes(filename);
    }

    public void ClearImagePreview() => _imagePreview = System.Array.Empty<byte>();

    // ── reading path: reconstruct the artwork from the /3DD 3D stream ────────
    private PDF3DArtwork? TryReadArtwork()
    {
        var reader = InternalReader;
        if (reader is null) return null;
        if (reader.Resolve(Dict.Get("3DD")) is not PdfStream stream3d) return null;
        var sdict = stream3d.Dict;
        var doc = reader.OwnerDocument;

        var content = new PDF3DContent();
        string? ext = reader.Resolve(sdict.Get("Subtype")) is PdfName sub ? sub.Value?.ToUpperInvariant() : null;
        byte[] bytes;
        try { bytes = reader.DecodeStream(stream3d, stream3d.ObjectNumber, stream3d.Generation); }
        catch { bytes = System.Array.Empty<byte>(); }
        content.SetReadContent(bytes, ext);

        var artwork = new PDF3DArtwork(doc!, content);

        if (reader.Resolve(sdict.Get("VA")) is PdfArray va)
        {
            foreach (var vref in va)
            {
                if (reader.Resolve(vref) is not PdfDictionary vd) continue;
                var camera = ReadMatrix(reader, vd.Get("C2W"));
                double orbit = ReadNum(reader, vd.Get("CO"));
                string name = reader.Resolve(vd.Get("XN")) is PdfString xn ? xn.ToText() : string.Empty;
                var view = new PDF3DView(doc!, camera ?? new Matrix3D(), orbit, name);

                if (reader.Resolve(vd.Get("SA")) is PdfArray sa)
                {
                    foreach (var cref in sa)
                    {
                        if (reader.Resolve(cref) is not PdfDictionary cd) continue;
                        var cs = new PDF3DCrossSection(doc!);
                        if (reader.Resolve(cd.Get("C")) is PdfArray cc && cc.Count >= 3)
                            cs.Center = new Point3D(ReadNum(reader, cc[0]), ReadNum(reader, cc[1]), ReadNum(reader, cc[2]));
                        if (reader.Resolve(cd.Get("O")) is PdfArray oo && oo.Count >= 3)
                            cs.CuttingPlaneOrientation = new PDF3DCuttingPlaneOrientation(
                                ReadNullNum(reader, oo[0]), ReadNullNum(reader, oo[1]), ReadNullNum(reader, oo[2]));
                        cs.CuttingPlaneOpacity = ReadNum(reader, cd.Get("PO"));
                        if (ReadColor(reader, cd.Get("PC")) is Color pc) cs.CuttingPlaneColor = pc;
                        if (ReadColor(reader, cd.Get("IC")) is Color ic) cs.CuttingPlanesIntersectionColor = ic;
                        if (reader.Resolve(cd.Get("IV")) is PdfBoolean iv) cs.Visibility = iv.Value;
                        view.CrossSectionsArray.Add(cs);
                    }
                }
                artwork.ViewArray.Add(view);
            }
        }
        return artwork;
    }

    private static double ReadNum(PdfReader reader, PdfObject? obj) => reader.Resolve(obj) switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 0,
    };

    private static double? ReadNullNum(PdfReader reader, PdfObject? obj) => reader.Resolve(obj) switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => null,
    };

    private static Matrix3D? ReadMatrix(PdfReader reader, PdfObject? obj)
    {
        if (reader.Resolve(obj) is not PdfArray a || a.Count < 12) return null;
        var v = new double[12];
        for (int i = 0; i < 12; i++) v[i] = ReadNum(reader, a[i]);
        return new Matrix3D(v);
    }

    // A 3D colour is written as [/DeviceRGB r g b] (or a bare [r g b]); map to
    // the FOSS Color (0–1 components rounded to 8-bit, matching the reference).
    private static Color? ReadColor(PdfReader reader, PdfObject? obj)
    {
        if (reader.Resolve(obj) is not PdfArray a) return null;
        int start = a.Count >= 4 && reader.Resolve(a[0]) is PdfName ? 1 : 0;
        if (a.Count - start < 3) return null;
        return Color.FromRgb(ReadNum(reader, a[start]), ReadNum(reader, a[start + 1]), ReadNum(reader, a[start + 2]));
    }
}

public sealed partial class PageInformationAnnotation : Annotation
{
    internal PageInformationAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);

    public PageInformationAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("PrinterMark"));
        // Track this typed instance for save-time appearance generation: enumerating the
        // page /Annots later re-resolves the dict to a generic /PrinterMark annotation.
        page?.RegisterPageInfoAnnotation(this);
    }

    public new AnnotationType AnnotationType => AnnotationType.PageInformation;

    /// <summary>Generate the page-information /AP /N appearance — the source file name and the
    /// date printed along the bottom margin band when a stamp is flattened. Called at save
    /// time, when the output file name is known.</summary>
    internal void GenerateInfoAppearance(string fileName, DateTime date)
    {
        if (Rect is not { } r) return;
        string text = $"{fileName}   {date.ToShortDateString()}";
        string esc = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        double fontSize = 8;
        double height = Math.Abs(r.URY - r.LLY);
        double tx = Math.Min(r.LLX, r.URX) + 2;
        double ty = Math.Min(r.LLY, r.URY) + Math.Max(2.0, height / 2 - fontSize / 2);
        string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var content = System.Text.Encoding.ASCII.GetBytes(
            $"BT /Helv {F(fontSize)} Tf {F(tx)} {F(ty)} Td ({esc}) Tj ET\n");
        var bbox = new Rectangle(Math.Min(r.LLX, r.URX), Math.Min(r.LLY, r.URY),
            Math.Max(r.LLX, r.URX), Math.Max(r.LLY, r.URY));
        SetNormalAppearanceWithHelvetica(content, bbox);
    }
}

public sealed partial class RegistrationMarkAnnotation : Annotation
{
    internal RegistrationMarkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);

    /// <summary>Create a registration mark on <paramref name="page"/> at <paramref name="position"/>.</summary>
    public RegistrationMarkAnnotation(Page page, PrinterMarkSidePosition position) : base(page, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("PrinterMark"));
        Position = position;
    }

    /// <summary>Always <see cref="AnnotationType.RegistrationMark"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.RegistrationMark;

    /// <summary>Which side of the page this mark sits on. Stored only.</summary>
    public PrinterMarkSidePosition Position { get; set; } = PrinterMarkSidePosition.Top;
}

public sealed partial class TrimMarkAnnotation : Annotation
{
    internal TrimMarkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);

    public TrimMarkAnnotation(Page page, PrinterMarkCornerPosition position) : base(page, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("PrinterMark"));
        Position = position;
    }

    public new AnnotationType AnnotationType => AnnotationType.TrimMark;

    /// <summary>Which corner of the page this mark sits in. Stored only.</summary>
    public PrinterMarkCornerPosition Position { get; set; } = PrinterMarkCornerPosition.TopLeft;
}

/// <summary>The set of printer's-mark families <see cref="PrinterMarkAnnotation.AddPrinterMarks"/>
/// generates on a page (PDF 32000 pre-press marks).</summary>
[System.Flags]
public enum PrinterMarksKind
{
    /// <summary>No marks.</summary>
    None = 0,
    /// <summary>Trim marks at the four trim-box corners.</summary>
    TrimMarks = 1,
    /// <summary>Bleed marks at the four bleed-box corners.</summary>
    BleedMarks = 2,
    /// <summary>Registration marks centred on the four page sides.</summary>
    RegistrationMarks = 4,
    /// <summary>One CMYK colour bar per process channel.</summary>
    ColorBars = 8,
    /// <summary>A page-information mark (file name + date).</summary>
    PageInformation = 16,
    /// <summary>Every mark family.</summary>
    All = TrimMarks | BleedMarks | RegistrationMarks | ColorBars | PageInformation,
}

/// <summary>Aggregate printer's-mark generator. <see cref="AddPrinterMarks"/> adds the
/// requested standard pre-press marks (trim, bleed, registration, colour bars and
/// page information) to every page of a document.</summary>
public sealed partial class PrinterMarkAnnotation : Annotation
{
    internal PrinterMarkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Always <see cref="AnnotationType.PrinterMark"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.PrinterMark;

    /// <summary>Add the requested families of printer's marks to every page of
    /// <paramref name="document"/>. Marks are positioned relative to each page's
    /// trim box (falling back to the media box).</summary>
    public static void AddPrinterMarks(Document document, PrinterMarksKind printerMarksKind)
    {
        if (document is null || printerMarksKind == PrinterMarksKind.None) return;
        foreach (Page page in document.Pages)
            AddPrinterMarks(page, printerMarksKind);
    }

    /// <summary>Add the requested families of printer's marks to a single page.</summary>
    public static void AddPrinterMarks(Page page, PrinterMarksKind printerMarksKind)
    {
        if (page is null || printerMarksKind == PrinterMarksKind.None) return;

        if (printerMarksKind.HasFlag(PrinterMarksKind.TrimMarks))
        {
            page.Annotations.Add(new TrimMarkAnnotation(page, PrinterMarkCornerPosition.TopLeft));
            page.Annotations.Add(new TrimMarkAnnotation(page, PrinterMarkCornerPosition.TopRight));
            page.Annotations.Add(new TrimMarkAnnotation(page, PrinterMarkCornerPosition.BottomLeft));
            page.Annotations.Add(new TrimMarkAnnotation(page, PrinterMarkCornerPosition.BottomRight));
        }

        if (printerMarksKind.HasFlag(PrinterMarksKind.BleedMarks))
        {
            page.Annotations.Add(new BleedMarkAnnotation(page, PrinterMarkCornerPosition.TopLeft));
            page.Annotations.Add(new BleedMarkAnnotation(page, PrinterMarkCornerPosition.TopRight));
            page.Annotations.Add(new BleedMarkAnnotation(page, PrinterMarkCornerPosition.BottomLeft));
            page.Annotations.Add(new BleedMarkAnnotation(page, PrinterMarkCornerPosition.BottomRight));
        }

        if (printerMarksKind.HasFlag(PrinterMarksKind.RegistrationMarks))
        {
            page.Annotations.Add(new RegistrationMarkAnnotation(page, PrinterMarkSidePosition.Top));
            page.Annotations.Add(new RegistrationMarkAnnotation(page, PrinterMarkSidePosition.Bottom));
            page.Annotations.Add(new RegistrationMarkAnnotation(page, PrinterMarkSidePosition.Left));
            page.Annotations.Add(new RegistrationMarkAnnotation(page, PrinterMarkSidePosition.Right));
        }

        if (printerMarksKind.HasFlag(PrinterMarksKind.ColorBars))
        {
            var box = page.TrimBox ?? page.MediaBox;
            var barW = System.Math.Max(8.0, (box.URX - box.LLX) / 16.0);
            var barH = System.Math.Max(4.0, 8.0);
            var y = System.Math.Max(box.LLY - barH - 2.0, 0.0);
            var channels = new[] { ColorsOfCMYK.Cyan, ColorsOfCMYK.Magenta, ColorsOfCMYK.Yellow, ColorsOfCMYK.Black };
            for (int i = 0; i < channels.Length; i++)
            {
                var x = box.LLX + i * (barW + 2.0);
                page.Annotations.Add(new ColorBarAnnotation(page,
                    new Rectangle(x, y, x + barW, y + barH), channels[i]));
            }
        }

        if (printerMarksKind.HasFlag(PrinterMarksKind.PageInformation))
        {
            var box = page.TrimBox ?? page.MediaBox;
            var media = page.MediaBox;
            page.Annotations.Add(new PageInformationAnnotation(page,
                new Rectangle(box.LLX, media.LLY, box.URX, box.LLY)));
        }
    }
}

// ── Accept overrides on the existing 22 concrete annotation types ──────────
//
// The base virtual `Annotation.Accept` is a no-op so static-typed callers
// that hold an `Annotation` reference still get reflection-shape parity.
// Each concrete partial below declares its own `Accept` override so the
// double-dispatch lands on the typed Visit overload.

public partial class CaretAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class CircleAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class FileAttachmentAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class FreeTextAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class HighlightAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class InkAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class LineAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class LinkAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class MovieAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class PolygonAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class PolylineAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class PopupAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class RedactionAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class RichMediaAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class ScreenAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class SquareAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class SquigglyAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class StampAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class StrikeOutAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class TextAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class UnderlineAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public partial class WidgetAnnotation
{
    public override void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}

public sealed partial class WatermarkAnnotation
{
    public void Accept(AnnotationSelector visitor) => visitor.Visit(this);
}
