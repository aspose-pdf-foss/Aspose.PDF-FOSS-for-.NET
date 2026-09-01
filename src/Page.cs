using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a page in a PDF document.
/// </summary>
public sealed partial class Page : IDisposable
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private int _index;
    private List<Text.TextFragment>? _attachedFragments;
    private HashSet<Text.TextFragment>? _bgColorFragments;
    private HashSet<Text.TextFragment>? _underlineFragments;

    /// <summary>The fragments whose rules the redraw pass has already re-laid this save.
    /// The removal pass runs after it and needs the same answer its guard used to read off
    /// <see cref="_underlineFragments"/> - which by then the redraw pass has emptied, so the
    /// guard fell through and re-emitted a rule the redraw had already written.</summary>
    private HashSet<Text.TextFragment>? _underlineRedrawn;
    private HashSet<Text.TextFragment>? _strikeOutFragments;
    private HashSet<Text.TextFragment>? _hyperlinkFragments;

    private List<PageInformationAnnotation>? _pageInfoAnnotations;
    private AnnotationCollection? _annotations;
    private XImageCollection? _images;
    private FontCollection? _fonts;
    private Resources? _resources;

    /// <summary>A page of this page's size that belongs to no document: the
    /// layout's dry runs draw into it to measure where content lands without
    /// touching the real page.</summary>
    internal Page CreateDetachedSibling()
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Page"));
        var mediaBox = new PdfArray();
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfReal(Width));
        mediaBox.Add(new PdfReal(Height));
        dict.Set("MediaBox", mediaBox);
        return new Page(dict, _reader, 0);
    }

    internal Page(PdfDictionary dict, PdfReader reader, int index)
    {
        _dict = dict;
        _reader = reader;
        _index = index;
    }

    /// <summary>0-based page index.</summary>
    internal int Index => _index;

    /// <summary>The object number this page was parsed from in the source
    /// document, or -1 for pages created in memory. Lets the save path write
    /// this page's authoritative in-memory <see cref="Dict"/> back to its
    /// original object number even when the reader's object cache has been
    /// dropped (e.g. by the page renderer), which would otherwise re-parse a
    /// pristine page dict and lose in-memory edits made after rendering.</summary>
    internal int SourceObjectNumber { get; set; } = -1;

    /// <summary>True for a placeholder standing in for a page-tree kid whose object
    /// could not be resolved via the xref (corrupt offsets). Keeps the page count
    /// aligned with the declared tree; the cross-document merge path skips these
    /// and reports their slots as null afterwards (reference-probed semantics).</summary>
    internal bool IsUnresolvedStub { get; set; }

    /// <summary>For a page imported from another document: the object number this page's
    /// dictionary must be written at, reserved so GoTo/Link destinations on other imported
    /// pages that target it resolve to this copy instead of deep-importing the source page.
    /// 0 for non-imported pages, which get a writer-allocated number.</summary>
    internal int ImportSlotObjNum { get; set; }

    /// <summary>Update the index without creating a new Page object.</summary>
    internal void SetIndex(int index) => _index = index;

    /// <summary>Register a fragment added via TextBuilder for sync on save.</summary>
    internal void RegisterAttachedFragment(Text.TextFragment fragment)
    {
        _attachedFragments ??= new();
        fragment.SourcePage = this;
        _attachedFragments.Add(fragment);
    }

    private List<Text.TextParagraph>? _attachedParagraphs;

    /// <summary>Register a paragraph appended via <see cref="Text.TextBuilder.AppendParagraph"/>
    /// so edits made after the append are written at save time.</summary>
    internal void RegisterAttachedParagraph(Text.TextParagraph paragraph)
    {
        _attachedParagraphs ??= new();
        if (!_attachedParagraphs.Contains(paragraph)) _attachedParagraphs.Add(paragraph);
    }

    /// <summary>Re-lay out every attached paragraph that changed since it was written.</summary>
    internal void SyncAttachedParagraphs()
    {
        if (_attachedParagraphs is null) return;
        foreach (var p in _attachedParagraphs) Text.TextBuilder.SyncAttachedParagraph(p);
    }

    private HashSet<Text.TextFragment>? _underlineRemovalFragments;

    /// <summary>A `q / <colour> rg / x y w h re / f / Q` block - the shape every regenerated
    /// rule and highlight takes.</summary>
    internal static System.Collections.Generic.List<Operator> DecorationBlock(
        Aspose.Pdf.Color? colour, double x, double y, double w, double h) => new()
    {
        new Aspose.Pdf.Operators.GSave(),
        new Aspose.Pdf.Operators.SetRGBColor(colour?.R / 255.0 ?? 0, colour?.G / 255.0 ?? 0,
            colour?.B / 255.0 ?? 0),
        new Aspose.Pdf.Operators.Re(x, y, w, h),
        new Aspose.Pdf.Operators.Fill(),
        new Aspose.Pdf.Operators.GRestore(),
    };

    /// <summary>Parse the leading whitespace-separated numeric operands from an operator's
    /// serialized form (e.g. "72 693 84 0.6 re" → [72, 693, 84, 0.6]).</summary>
    private static double[]? ParseLeadingNumbers(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                nums.Add(v);
            else break;
        }
        return nums.ToArray();
    }

    /// <summary>1-based page number.</summary>
    public int Number => _index + 1;

    /// <summary>Form fields whose widgets appear on this page, ordered by their tab order.</summary>
    public IList<Forms.Field> FieldsInTabOrder
    {
        get
        {
            var pageNumber = Number;
            var fields = new List<Forms.Field>();
            var catalog = _reader.Catalog;
            var acroForm = _reader.ResolveDict(catalog.Get("AcroForm"));
            var formFields = acroForm is null
                ? Forms.Form.FromPageWidgets(catalog, _reader)
                : new Forms.Form(acroForm, _reader);
            foreach (var field in formFields.Fields)
            {
                if (field.PageIndex == pageNumber)
                    fields.Add(field);
            }
            fields.Sort((a, b) => a.TabOrder.CompareTo(b.TabOrder));
            return fields;
        }
    }

    /// <summary>
    /// Page rectangle (defaults to MediaBox). Forwarder for compatibility with the
    /// public API where Page exposes a top-level Rect property. Setting
    /// <c>Rect</c> updates MediaBox, CropBox, BleedBox, TrimBox and ArtBox in
    /// one shot — the property acts as the
    /// primary page-size accessor.
    /// </summary>
    public Rectangle Rect
    {
        get => MediaBox;
        set
        {
            MediaBox = value;
            CropBox = value;
            BleedBox = value;
            TrimBox = value;
            ArtBox = value;
        }
    }

    /// <summary>
    /// The page rectangle. Compatibility alias of <see cref="Rect"/> for the public API,
    /// which exposes the page size as both <c>Rect</c> and <c>Rectangle</c>.
    /// </summary>
    public Rectangle Rectangle
    {
        get => Rect;
        set => Rect = value;
    }

    private PageInfo? _pageInfoCache;

    /// <summary>
    /// Page info container with width/height/margin properties. Forwarder
    /// for compatibility with the public API.
    /// </summary>
    public PageInfo PageInfo
    {
        get => _pageInfoCache ??= new PageInfo(this);
        // Assigning a free-standing descriptor SIZES THE PAGE: the bound instance
        // stays bound and takes the assigned values, so `page.PageInfo = new PageInfo
        // { … }` and setting the same properties one by one end in the same place.
        // Swapping the cache for the detached object instead left the media box
        // untouched and every later `page.PageInfo.Width = …` talking to nothing.
        set
        {
            if (value is null) return;
            var bound = _pageInfoCache ??= new PageInfo(this);
            if (!ReferenceEquals(bound, value)) bound.CopyFrom(value);
        }
    }

    /// <summary>Page background colour. When not explicitly set, reading it DETECTS
    /// an existing background from the content stream — a full-page rectangle
    /// filled right at the start of the page paints the page background (so the
    /// getter reports Crimson etc. for such documents).</summary>
    public Color? Background
    {
        get
        {
            if (_background is not null) return _background;
            if (!_backgroundDetected)
            {
                _backgroundDetected = true;
                _detectedBackground = DetectBackgroundColor();
            }
            return _detectedBackground;
        }
        set => _background = value;
    }
    /// <summary>The background the caller explicitly assigned; detection never
    /// leaks into the generator's apply-background pass.</summary>
    internal Color? ExplicitBackground => _background;
    private Color? _background;
    private Color? _detectedBackground;
    private bool _backgroundDetected;

    /// <summary>The media box for this page (required per spec).</summary>
    public Rectangle MediaBox
    {
        get => GetBox("MediaBox") ?? new Rectangle(0, 0, 612, 792);
        set => SetBox("MediaBox", value);
    }

    /// <summary>The crop box (defaults to media box).</summary>
    public Rectangle CropBox
    {
        get => GetBox("CropBox") ?? MediaBox;
        set => SetBox("CropBox", value);
    }

    /// <summary>The bleed box (defaults to crop box).</summary>
    public Rectangle BleedBox
    {
        get => GetBox("BleedBox") ?? CropBox;
        set => SetBox("BleedBox", value);
    }

    /// <summary>The trim box (defaults to crop box).</summary>
    public Rectangle TrimBox
    {
        get => GetBox("TrimBox") ?? CropBox;
        set => SetBox("TrimBox", value);
    }

    /// <summary>The art box (defaults to crop box).</summary>
    public Rectangle ArtBox
    {
        get => GetBox("ArtBox") ?? CropBox;
        set => SetBox("ArtBox", value);
    }

    /// <summary>Page rotation as Rotation enum.</summary>
    public Rotation Rotate
    {
        get => (Rotation)(int)_dict.GetInt("Rotate");
        set => _dict.Set("Rotate", new PdfInteger((int)value % 360));
    }

    /// <summary>Page rotation in degrees as int (0, 90, 180, 270).</summary>
    public int RotateDegrees
    {
        get => (int)_dict.GetInt("Rotate");
        set => _dict.Set("Rotate", new PdfInteger(value % 360));
    }

    /// <summary>
    /// Set the page rotation in degrees (0, 90, 180, 270).
    /// </summary>
    public void SetRotation(int degrees) => RotateDegrees = degrees;

    /// <summary>
    /// Affine transform that maps the unrotated PDF coordinate system to
    /// the page's user-visible (rotated) one. Composes a rotation about the
    /// MediaBox centre with the translation needed to keep the bottom-left
    /// of the rotated bounds at (0, 0). Identity when rotation is 0.
    /// </summary>
    public Matrix RotationMatrix
    {
        get
        {
            var rotation = ((int)Rotate) % 360;
            if (rotation < 0) rotation += 360;
            var rect = MediaBox;
            var w = rect?.Width ?? 0;
            var h = rect?.Height ?? 0;
            return rotation switch
            {
                90 => new Matrix(0, -1, 1, 0, 0, w),
                180 => new Matrix(-1, 0, 0, -1, w, h),
                270 => new Matrix(0, 1, -1, 0, h, 0),
                _ => Matrix.Identity,
            };
        }
    }

    /// <summary>
    /// Page display duration in seconds for presentation mode.
    /// Returns -1 if no duration is set.
    /// </summary>
    public double Duration
    {
        get
        {
            var val = _reader.Resolve(_dict.Get("Dur"));
            if (val is PdfReal r) return r.Value;
            if (val is PdfInteger i) return i.Value;
            return -1;
        }
        set
        {
            _dict.Set("Dur", new PdfReal(value));
        }
    }

    private void SetBox(string name, Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        _dict.Set(name, arr);
    }

    /// <summary>Walk every annotation on this page through
    /// <paramref name="visitor"/>; matches accumulate in
    /// <see cref="Annotations.AnnotationSelector.Selected"/>.</summary>
    public void Accept(Annotations.AnnotationSelector visitor)
    {
        if (visitor is null) return;
        Annotations.Accept(visitor);
    }

    /// <summary>Annotations on this page.</summary>
    public AnnotationCollection Annotations
    {
        get
        {
            // Re-create if the Annots array was modified externally (e.g., by Annotation.Flatten)
            if (_annotations is not null && _annotations.IsDirty(_dict, _reader))
                _annotations = null;
            return _annotations ??= new AnnotationCollection(_dict, _reader, this);
        }
    }

    /// <summary>
    /// Page resources (fonts, images) — provides access via a unified Resources object.
    /// </summary>
    public Resources Resources => _resources ??= new Resources(this);

    /// <summary>Method-style accessor for <see cref="Resources"/> — public-API compatibility.</summary>
    public Resources GetResources() => Resources;

    /// <summary>
    /// Pattern resources on this page (keyed by pattern name).
    /// </summary>
    public IReadOnlyDictionary<string, Pattern> Patterns
        => _patterns ??= Pattern.ResolvePatterns(_dict, _reader);
    private Dictionary<string, Pattern>? _patterns;

    private OperatorCollection? _contents;

    private ContentsAppender? _contentsAppender;

    /// <summary>
    /// Visible page width accounting for rotation.
    /// </summary>
    public double Width
    {
        get
        {
            var mb = MediaBox;
            var rot = RotateDegrees % 360;
            return (rot == 90 || rot == 270) ? mb.Height : mb.Width;
        }
    }

    /// <summary>
    /// Visible page height accounting for rotation.
    /// </summary>
    public double Height
    {
        get
        {
            var mb = MediaBox;
            var rot = RotateDegrees % 360;
            return (rot == 90 || rot == 270) ? mb.Width : mb.Height;
        }
    }

    /// <summary>The generator layout frame's height: the MEDIA box height regardless
    /// of /Rotate. A rotated page's paragraphs seat against the media edges and paint
    /// upright in them — the rotation then turns them on screen (measured:
    /// a table Left/Top-anchored on a /Rotate 90 landscape scan seats at
    /// the MEDIA top and reads sideways in the displayed page), while
    /// <see cref="Height"/> answers the rotated DISPLAY frame.</summary>
    internal double LayoutFrameHeight => MediaBox.Height;

    /// <summary>
    /// Gets the color type of this page by analyzing its content stream operators
    /// and image color spaces.
    /// </summary>
    public ColorType ColorType => ColorDetectHelper.GetColorType(this);

    /// <summary>
    /// The page transition effect, or null if none is set.
    /// </summary>
    public PageTransition? Transition => PageTransition.FromPageDict(_dict, _reader);

    /// <summary>
    /// Accept a TextAbsorber visitor (matching the public API).
    /// </summary>
    public void Accept(Text.TextAbsorber visitor) => visitor.Visit(this);

    /// <summary>
    /// Extract all text from the page using a TextAbsorber.
    /// </summary>
    public string GetText()
    {
        var absorber = new Text.TextAbsorber();
        Accept(absorber);
        return absorber.Text;
    }

    /// <summary>
    /// Accept a TextFragmentAbsorber visitor (matching the public API).
    /// </summary>
    public void Accept(Text.TextFragmentAbsorber visitor) => visitor.Visit(this);

    /// <summary>
    /// Tally how many text fragments on this page are drawn at each rotation
    /// angle. The key is the fragment rotation in degrees (CCW from the page
    /// x-axis, 0/90/180/270 for axis-aligned text) and the value is the number
    /// of fragments at that angle.
    /// </summary>
    public System.Collections.Generic.Dictionary<double, int> GetTextRotationStatistic()
    {
        var absorber = new Text.TextFragmentAbsorber();
        absorber.Visit(this);
        var stats = new System.Collections.Generic.Dictionary<double, int>();
        foreach (Text.TextFragment fragment in absorber.TextFragments)
        {
            var rotation = fragment.TextState.Rotation;
            stats[rotation] = stats.TryGetValue(rotation, out var count) ? count + 1 : 1;
        }
        return stats;
    }

    /// <summary>
    /// Accept an ImagePlacementAbsorber visitor (matching the public API).
    /// </summary>
    public void Accept(ImagePlacementAbsorber visitor) => visitor.Visit(this);

    /// <summary>
    /// Add a stamp to this page. The stamp's content stream is appended to the page's content.
    /// </summary>
    /// <summary>
    /// Render this page using the specified device and save to a file.
    /// </summary>
    public void SendTo(Devices.ImageDevice device, string outputFileName)
    {
        device.Process(this, outputFileName);
    }

    /// <summary>
    /// Render this page using the specified device and write to a stream.
    /// </summary>
    public void SendTo(Devices.ImageDevice device, Stream output)
    {
        device.Process(this, output);
    }

    // Whether the page's ORIGINAL content has been sandwiched in q/Q so appended
    // overlays start from the identity CTM (done once per page instance).
    private bool _contentStateWrapped;

    /// <summary>The trailing <c>BDC … EMC</c> block left at the end of the page
    /// content by <see cref="AddMarkedContentStream"/>: its tag/MCID and the
    /// stream segment holding it. Any other append clears the note, so a merge
    /// only happens across DIRECTLY consecutive same-tag/-MCID runs.</summary>
    private (string Tag, int Mcid, Core.PdfStream Segment)? _trailingMarkedContent;

    /// <summary>Marked-content tag wrapping a page-background fill emitted by
    /// <see cref="Background"/>. Lets a re-applied background find and remove the
    /// previous one instead of stacking, and lets Color.White act as "remove".</summary>
    internal const string BackgroundMarkerTag = "Background";

    /// <summary>Number of streams currently making up /Contents (0, 1, or the
    /// array length) — the insertion cursor for <see cref="InsertContentStreamAt"/>.</summary>
    internal int ContentStreamCount
    {
        get
        {
            var resolved = _reader.Resolve(_dict.Get("Contents"));
            return resolved is PdfArray arr ? arr.Count : resolved is PdfStream ? 1 : 0;
        }
    }

    /// <summary>Register this page — and any indirect /Resources (and /Resources/XObject)
    /// it owns — as dirty so an incremental (append-only) save re-writes the in-memory
    /// edits. A foreground stamp adds a /Contents stream and an /XObject entry to an
    /// already-existing page; only NEW objects are appended automatically, so the modified
    /// existing objects must be marked explicitly. No-op for a page not loaded from a document.</summary>
    internal void MarkDirty()
    {
        var doc = _reader.OwnerDocument;
        if (doc is null) return;
        var pageNum = doc.FindObjectNumber(_dict);
        if (pageNum > 0) doc.MarkDirty(pageNum, _dict);
        if (_dict.Get("Resources") is PdfIndirectRef rr && _reader.ResolveDict(rr) is { } rdict)
        {
            doc.MarkDirty(rr.ObjectNumber, rdict);
            if (rdict.Get("XObject") is PdfIndirectRef xr && _reader.ResolveDict(xr) is { } xdict)
                doc.MarkDirty(xr.ObjectNumber, xdict);
        }
    }

    /// <summary>Render this page into a PNG byte array at the requested resolution.</summary>
    public byte[] AsByteArray(Aspose.Pdf.Devices.Resolution resolution)
    {
        using var ms = new MemoryStream();
        new Aspose.Pdf.Devices.PngDevice(resolution ?? new Aspose.Pdf.Devices.Resolution(150)).Process(this, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Computes the scale and translation needed to map an appearance stream's BBox to
    /// the annotation's Rect on the page (PDF 32000 §12.5.5, Table 168).
    /// </summary>
    private (double sx, double sy, double tx, double ty) ComputeAppearanceCtm(
        Rectangle rect, PdfStream appearanceStream)
    {
        var bboxArr = _reader.Resolve(appearanceStream.Dict.Get("BBox")) as PdfArray;
        double bboxW = rect.Width, bboxH = rect.Height;
        double bboxX = 0, bboxY = 0;
        if (bboxArr is { Count: >= 4 })
        {
            var bbox = Rectangle.FromPdfArray(bboxArr);
            bboxW = bbox.Width;
            bboxH = bbox.Height;
            bboxX = bbox.LLX;
            bboxY = bbox.LLY;
        }

        var sx = bboxW > 0 ? rect.Width / bboxW : 1.0;
        var sy = bboxH > 0 ? rect.Height / bboxH : 1.0;
        var tx = rect.LLX - bboxX * sx;
        var ty = rect.LLY - bboxY * sy;
        return (sx, sy, tx, ty);
    }

    private static string Format(double v) =>
        v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Move the page's content into a Form XObject scaled by (sx,sy) and offset
    /// by (tx,ty), keeping the MediaBox. The moved content is bracketed with q/Q INSIDE the
    /// form (so the form's graphics state is self-balanced) and the page invokes it with a
    /// single cm + Do. Used by PdfPageEditor.Zoom.</summary>
    internal void ApplyZoomAsForm(double sx, double sy, double tx, double ty)
    {
        var originalContent = CollectContentBytes();
        var q = System.Text.Encoding.ASCII.GetBytes("q\n");
        var endQ = System.Text.Encoding.ASCII.GetBytes("\nQ\n");
        var bracketed = new byte[q.Length + originalContent.Length + endQ.Length];
        q.CopyTo(bracketed, 0);
        originalContent.CopyTo(bracketed, q.Length);
        endQ.CopyTo(bracketed, q.Length + originalContent.Length);

        var formName = WrapContentInForm(bracketed);
        // q … Q around the invocation: the resulting page stream is
        // q / cm / Do / Q, so cm sits at Contents[2] and Do at Contents[3].
        var bytes = System.Text.Encoding.ASCII.GetBytes(
            $"q\n{Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{formName} Do\nQ\n");
        SetContentStream(bytes);

        TransformAnnotationRects(sx, sy, tx, ty);
    }

    private static double GetNum(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static void TransformCoordArray(PdfArray arr, double sx, double sy, double tx, double ty)
    {
        for (int i = 0; i + 1 < arr.Count; i += 2)
        {
            double xv = GetNum(arr[i]);
            double yv = GetNum(arr[i + 1]);
            arr.ReplaceAt(i,     new PdfReal(sx * xv + tx));
            arr.ReplaceAt(i + 1, new PdfReal(sy * yv + ty));
        }
    }

    private void ComposeStreamMatrix(PdfStream stream, double a, double b, double c, double d)
    {
        // Existing form matrix (default identity).
        double ma = 1, mb = 0, mc = 0, md = 1, me = 0, mf = 0;
        if (_reader.Resolve(stream.Dict.Get("Matrix")) is PdfArray m && m.Count >= 6)
        {
            ma = GetNum(m[0]); mb = GetNum(m[1]); mc = GetNum(m[2]);
            md = GetNum(m[3]); me = GetNum(m[4]); mf = GetNum(m[5]);
        }
        // R * M with R = [a b c d 0 0] (rotation only). Translation of R is intentionally
        // dropped: the viewer maps the transformed BBox onto /Rect, supplying the offset.
        double na = a * ma + c * mb,        nb = b * ma + d * mb;
        double nc = a * mc + c * md,        nd = b * mc + d * md;
        double ne = a * me + c * mf,        nf = b * me + d * mf;
        var arr = new PdfArray();
        arr.Add(new PdfReal(na)); arr.Add(new PdfReal(nb)); arr.Add(new PdfReal(nc));
        arr.Add(new PdfReal(nd)); arr.Add(new PdfReal(ne)); arr.Add(new PdfReal(nf));
        stream.Dict.Set("Matrix", arr);
    }

    /// <summary>
    /// Determines whether the page is blank (has no meaningful content).
    /// A page is considered blank if it has no content stream or an empty/whitespace-only content stream,
    /// and no annotations, images, or form XObjects.
    /// </summary>
    /// <param name="tolerance">Coverage threshold (0..1). Pages whose drawn area
    /// is smaller than <paramref name="tolerance"/> count as blank. The current
    /// implementation does not perform coverage analysis — it returns true only
    /// when the page has zero visible content, matching <c>tolerance == 0</c>.</param>
    /// <summary>
    /// Convenience helper: render this page to a PNG and return the bytes
    /// as a <see cref="MemoryStream"/>. Equivalent to wrapping a
    /// <see cref="Aspose.Pdf.Devices.PngDevice"/> + Process(page, stream).
    /// </summary>
    public MemoryStream ConvertToPNGMemoryStream()
    {
        var ms = new MemoryStream();
        var device = new Aspose.Pdf.Devices.PngDevice();
        device.Process(this, ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Count "ink" bits in a packed 1-bpp raster (rows padded to bytes).
    /// With <paramref name="invert"/> true a 0 bit is ink, otherwise a 1 bit.</summary>
    private static long CountBits(byte[] bits, int w, int h, bool invert)
    {
        var rowBytes = (w + 7) / 8;
        if ((long)rowBytes * h > bits.Length) return 0;
        long count = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var bit = (bits[y * rowBytes + x / 8] >> (7 - x % 8)) & 1;
                if (invert == (bit == 0)) count++;
            }
        }
        return count;
    }

    private static double CoverageNum(PdfObject o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Header to render at the top of this page.</summary>
    public HeaderFooter? Header { get; set; }

    /// <summary>Footer to render at the bottom of this page.</summary>
    public HeaderFooter? Footer { get; set; }

    /// <summary>
    /// TOC information for this page. When set, the page acts as a Table of Contents.
    /// </summary>
    public TocInfo? TocInfo { get; set; }

    /// <summary>
    /// Collection of paragraph objects to add to this page (TextFragment, HtmlFragment, Table, Heading, etc.).
    /// Paragraphs are rendered on save.
    /// </summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Default stroke/fill style used to draw footnote / endnote separator lines.</summary>
    public GraphInfo NoteLineStyle { get; set; } = new();

    /// <summary>Run the paragraph-layout pass so this page's generator paragraphs are
    /// rendered and the geometry they leave behind (Cell.Width, Cell.Rect, the
    /// grid-expanded Row.Cells) can be read back before the document is saved. The pass
    /// is idempotent: every page is gated by <see cref="LayoutApplied"/>, so a later
    /// Save neither re-renders this page nor skips the others.</summary>
    internal void ProcessParagraphs() => _reader?.OwnerDocument?.ProcessParagraphs();

    /// <summary>The document this page belongs to, or null for a page not bound to one.
    /// Layout needs it to answer document-wide questions about the page being drawn —
    /// which number it carries, for the <c>$p</c>/<c>$P</c> macros a table cell may
    /// hold.</summary>
    internal Document? OwnerDocument => _reader?.OwnerDocument;

    /// <summary>
    /// Tracks whether Document.ApplyPageContent has already laid out this page's
    /// paragraphs and TOC content. Prevents duplicate rendering when both
    /// ProcessParagraphs and Save run the layout pass.
    /// </summary>
    internal bool LayoutApplied { get; set; }

    /// <summary>Y cursor left behind by the last paragraph-layout pass. Paragraphs
    /// added AFTER a ProcessParagraphs/Save round continue below the earlier
    /// content instead of restarting at the top margin (which overprinted the
    /// first paragraph and read back as one merged line).</summary>
    internal double? LayoutCursorY { get; set; }

    /// <summary>Tracks whether the page's <see cref="Header"/> / <see cref="Footer"/>
    /// have already been rendered, so a second layout pass (ProcessParagraphs then
    /// Save) does not emit them twice.</summary>
    internal bool HeaderFooterApplied { get; set; }

    private bool _contentIsolated;

    /// <summary>Tracks whether the page's <see cref="Background"/> fill has already
    /// been prepended to the content, so a second layout pass (ProcessParagraphs
    /// then Save) does not paint it twice.</summary>
    internal bool BackgroundApplied { get; set; }

    /// <summary>
    /// Collection for adding artifacts (watermarks, etc.) to this page.
    /// </summary>
    public ArtifactCollection Artifacts => _artifacts ??= new ArtifactCollection(this);
    private ArtifactCollection? _artifacts;

    /// <summary>
    /// Gets the layers (Optional Content Groups) referenced by this page.
    /// FOSS-extra accessor — exposes the underlying OCG-backed collection
    /// for callers that need the typed OptionalContentGroup API. The public-API
    /// shape goes through <see cref="Layers"/> (List&lt;Layer&gt;) instead.
    /// </summary>
    public LayerCollection OcgLayers
    {
        get
        {
            if (_layers is null)
            {
                _layers = new LayerCollection(LayerHelper.GetPageLayers(this, _reader));
                _layers.SetPage(this);
            }
            return _layers;
        }
    }
    private LayerCollection? _layers;

    /// <summary>
    /// Merge all layers on this page into a single layer with the given name.
    /// </summary>
    public void MergeLayers(string newLayerName)
    {
        LayerHelper.MergeLayersOnPage(this, newLayerName, _reader);
    }

    /// <summary>Merge all layers on this page, assigning the new OCG the given id.</summary>
    public void MergeLayers(string newLayerName, string newOptionalContentGroupId)
    {
        _ = newOptionalContentGroupId;
        LayerHelper.MergeLayersOnPage(this, newLayerName, _reader);
    }

    /// <summary>Save vector graphics from this page to <paramref name="pathToSave"/>. Stored only.</summary>
    public bool TrySaveVectorGraphics(string pathToSave) { _ = pathToSave; return false; }

    /// <summary>Diagnostic XML representation of this page. Stored only.</summary>
    public string AsXml() => string.Empty;

    /// <summary>Line-break notifications emitted by the flow layout for this page
    /// when <see cref="Document.EnableNotificationLogging"/> was set before save.</summary>
    public string GetNotifications() => NotificationLog;

    /// <summary>Accumulated line-break notifications for this page.</summary>
    internal string NotificationLog { get; set; } = string.Empty;

    /// <summary>Convert this page's colours to grayscale: content-stream colour operators,
    /// image XObjects, named colour-space resources, and annotation appearances.</summary>
    public void MakeGrayscale() => GrayscaleConverter.ConvertPage(this);

    /// <summary>Convert a degrees-int rotation to <see cref="Rotation"/>.</summary>
    public static Rotation IntToRotation(int rotation) => (((rotation % 360) + 360) % 360) switch
    {
        90 => Rotation.on90,
        180 => Rotation.on180,
        270 => Rotation.on270,
        _ => Rotation.None,
    };

    /// <summary>Convert <see cref="Rotation"/> to degrees as an int.</summary>
    public static int RotationToInt(Rotation rotation) => (int)rotation;

    /// <summary>Per-page additional actions (open / close / etc.), backed by /AA.</summary>
    public PageActionCollection Actions => _actions ??= new PageActionCollection(this);
    private PageActionCollection? _actions;

    /// <summary>User-unit factor (PDF 32000 §14.8.4 /UserUnit entry). Reads the
    /// page dict's /UserUnit (1.0 when absent); a positive assignment writes the
    /// entry, while a non-positive one (the corpus assigns -1) REMOVES it, so the
    /// page reads back at the 1.0 default after save.</summary>
    public double UserUnit
    {
        get
        {
            var v = _reader.Resolve(_dict.Get("UserUnit"));
            return v switch
            {
                Core.PdfReal r => r.Value,
                Core.PdfInteger i => i.Value,
                _ => 1.0,
            };
        }
        set
        {
            if (value > 0) _dict.Set("UserUnit", new Core.PdfReal(value));
            else _dict.Remove("UserUnit");
        }
    }

    /// <summary>Whether <see cref="Paragraphs"/> additions append at the end (vs flow position). Stored only.</summary>
    public bool IsAddParagraphsAfterLast { get; set; }

    /// <summary>Group / blending colour space dictionary for this page. Stored only.</summary>
    public Group? Group { get; set; }

    /// <summary>Tab order (PDF 32000 §12.5 /Tabs entry). Stored only.</summary>
    public TabOrder TabOrder { get; set; } = TabOrder.None;

    private Watermark? _watermark;
    private bool _watermarkSet;

    /// <summary>The page watermark. The getter detects an existing watermark from
    /// the page content (a /Subtype /Watermark artifact and its image) and returns
    /// an unavailable <see cref="Watermark"/> when there is none, so callers can read
    /// <c>Watermark.Available</c> without a null check. The setter stores a watermark
    /// that is drawn into the page (as a watermark artifact) on save.</summary>
    public Watermark? Watermark
    {
        get => _watermarkSet ? _watermark : DetectWatermark();
        set { _watermark = value; _watermarkSet = true; }
    }

    /// <summary>Watermark stored via the setter and awaiting render-on-save; null
    /// when none was set.</summary>
    internal Watermark? PendingWatermark => _watermarkSet ? _watermark : null;

    /// <summary>
    /// The raw page dictionary for power-user access.
    /// </summary>
    internal PdfDictionary Dict => _dict;

    /// <summary>
    /// The internal reader for object resolution.
    /// </summary>
    internal PdfReader Reader => _reader;

    private Rectangle? GetBox(string name)
    {
        var obj = _reader.Resolve(_dict.Get(name));
        if (obj is PdfArray arr && arr.Count >= 4)
        {
            var rect = Rectangle.FromPdfArray(ResolveArrayElements(arr));
            // Normalize inverted coordinates (some XFA PDFs have [0,792,612,0])
            if (rect.Width < 0 || rect.Height < 0)
                return new Rectangle(
                    Math.Min(rect.LLX, rect.URX), Math.Min(rect.LLY, rect.URY),
                    Math.Max(rect.LLX, rect.URX), Math.Max(rect.LLY, rect.URY));
            return rect;
        }
        return InheritBox(name);
    }

    private PdfArray ResolveArrayElements(PdfArray arr)
    {
        var result = new PdfArray();
        foreach (var item in arr)
        {
            var resolved = _reader.Resolve(item);
            result.Add(resolved ?? PdfNull.Instance);
        }
        return result;
    }

    /// <summary>Clears cached data.</summary>
    public void FreeMemory()
    {
        // Drop heavyweight wrappers; leave _dict and _attachedFragments
        // intact so pending edits are still flushed on save and properties
        // re-materialize on next access.
        _bgColorFragments = null;
        _underlineFragments = null;
        _underlineRemovalFragments = null;
        _strikeOutFragments = null;
        _annotations = null;
        _images = null;
        _fonts = null;
        _resources = null;
    }

    /// <summary>Frees up memory.</summary>
    public void Dispose() => FreeMemory();
}
