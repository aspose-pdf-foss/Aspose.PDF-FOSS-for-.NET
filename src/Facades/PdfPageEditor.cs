using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for page-level editing: rotation, resizing, margin adjustment,
/// and page box manipulation (CropBox, TrimBox, BleedBox, ArtBox).
/// Supports both stateless (byte[]-in / byte[]-out) and stateful (BindPdf / Save) modes.
/// </summary>
public sealed class PdfPageEditor : System.IDisposable
{
    // ── Page-transition style constants (T.J. Aspose legacy IDs; persisted via TransitionType) ──
    public const int SPLITVOUT = 1;
    public const int SPLITHOUT = 2;
    public const int SPLITVIN  = 3;
    public const int SPLITHIN  = 4;
    public const int BLINDV    = 5;
    public const int BLINDH    = 6;
    public const int INBOX     = 7;
    public const int OUTBOX    = 8;
    public const int LRWIPE    = 9;
    public const int RLWIPE    = 10;
    public const int BTWIPE    = 11;
    public const int TBWIPE    = 12;
    public const int DISSOLVE  = 13;
    public const int LRGLITTER = 14;
    public const int TBGLITTER = 15;
    public const int DGLITTER  = 16;

    private Document? _document;
    private int[]? _processPages;
    private float _moveX;
    private float _moveY;

    public PdfPageEditor() { }

    /// <summary>Bind directly at construction time. Caller owns the <paramref name="document"/>'s lifetime.</summary>
    public PdfPageEditor(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    // ── Stateful (BindPdf / Save) API ─────────────────────────────────────────

    /// <summary>
    /// Bind a PDF file by path for editing.
    /// </summary>
    public void BindPdf(string path)
    {
        _document = Document.Open(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Bind PDF data for editing.
    /// </summary>
    public void BindPdf(byte[] pdfData)
    {
        _document = Document.Open(pdfData);
    }

    /// <summary>
    /// Bind a PDF stream for editing.
    /// </summary>
    /// <summary>Bind to an existing <see cref="Document"/>; caller owns lifetime.</summary>
    public void BindPdf(Document srcDoc)
    {
        _document = srcDoc ?? throw new ArgumentNullException(nameof(srcDoc));
    }

    public void BindPdf(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _document = Document.Open(ms.ToArray());
    }

    /// <summary>
    /// Save the bound document to a file.
    /// </summary>
    public void Save(string outputFile)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        ApplyPendingPageResize();
        ApplyPendingContentTransform();
        var bytes = _document.ToArray();
        File.WriteAllBytes(outputFile, bytes);
    }

    /// <summary>Save the bound document to a stream.</summary>
    public void Save(Stream outputStream)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        ApplyPendingPageResize();
        ApplyPendingContentTransform();
        var bytes = _document.ToArray();
        outputStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Save the bound document and return PDF bytes.
    /// </summary>
    /// <summary>The currently-bound document, or throws if none.</summary>
    public Document Document => _document
        ?? throw new InvalidOperationException("No document bound. Call BindPdf first.");

    /// <summary>Page size to apply to subsequent operations (no-op stored).</summary>
    public PageSize? PageSize { get; set; }

    /// <summary>Horizontal alignment for subsequent operations (no-op stored).</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.None;

    /// <summary>Vertical alignment for subsequent operations (no-op stored).
    /// Canonical naming: property name is VerticalAlignmentType but type is
    /// VerticalAlignment.</summary>
    public VerticalAlignment VerticalAlignmentType { get; set; } = Aspose.Pdf.VerticalAlignment.None;

    /// <summary>Zoom factor for subsequent operations (no-op stored).</summary>
    public float Zoom { get; set; } = 1.0f;

    /// <summary>Apply queued changes to the bound document — resizes the target
    /// pages when <see cref="PageSize"/> is set.</summary>
    public void ApplyChanges() { ApplyPendingPageResize(); ApplyPendingContentTransform(); }

    private readonly HashSet<Page> _resizedPages = new();

    /// <summary>When <see cref="PageSize"/> is set, scale each target page's content
    /// by <see cref="Zoom"/> (positioned per the alignment settings) and resize its
    /// MediaBox to the requested size. Applied to the current <see cref="ProcessPages"/>
    /// targets that have not already been resized, so a per-page edit loop
    /// (set PageSize/Zoom/ProcessPages then ApplyChanges, repeated) resizes each page
    /// with its own settings and a trailing Save does not resize a page twice.</summary>
    private void ApplyPendingPageResize()
    {
        if (_document is null || PageSize is null) return;
        double newW = PageSize.Width, newH = PageSize.Height;
        if (newW <= 0 || newH <= 0) return;
        var sx = Zoom > 0 ? Zoom : 1.0;
        foreach (var page in TargetPagesForStateful())
        {
            if (!_resizedPages.Add(page)) continue;
            // The requested PageSize is the desired *visible* (rotation-aware) size. For a
            // page rotated 90/270 the MediaBox axes are swapped relative to the view, so the
            // stored MediaBox must use the swapped dimensions — otherwise the resized page
            // ends up portrait where it should be landscape.
            double pw = newW, ph = newH;
            var rot = ((page.RotateDegrees % 360) + 360) % 360;
            if (rot == 90 || rot == 270) (pw, ph) = (ph, pw);
            var box = page.MediaBox;
            double scaledW = box.Width * sx, scaledH = box.Height * sx;
            double tx = HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (pw - scaledW) / 2,
                HorizontalAlignment.Right => pw - scaledW,
                _ => 0,
            };
            double ty = VerticalAlignmentType switch
            {
                Aspose.Pdf.VerticalAlignment.Center => (ph - scaledH) / 2,
                Aspose.Pdf.VerticalAlignment.Top => ph - scaledH,
                _ => 0,
            };
            page.ApplyContentResize(sx, sx, tx, ty);
            page.SetMediaBox(new Rectangle(0, 0, pw, ph));
        }
    }

    private readonly HashSet<Page> _transformedPages = new();

    /// <summary>Apply a <see cref="Zoom"/> scale and <see cref="MovePosition"/> translation
    /// to the target pages' content without changing the page size. Wraps the original
    /// content as <c>q {Zoom} 0 0 {Zoom} {moveX} {moveY} cm /Fm Do Q</c>. Skipped when
    /// <see cref="PageSize"/> is set — that resize path already folds the zoom into the
    /// page-fit scale.</summary>
    private void ApplyPendingContentTransform()
    {
        if (_document is null || PageSize is not null) return;
        double sx = Zoom > 0 ? Zoom : 1.0;
        if (sx == 1.0 && _moveX == 0 && _moveY == 0) return;
        foreach (var page in TargetPagesForStateful())
        {
            if (!_transformedPages.Add(page)) continue;
            // ApplyZoomAsForm brackets the moved content with q/Q INSIDE the form
            // (form contents = q … original … Q).
            page.ApplyZoomAsForm(sx, sx, _moveX, _moveY);
        }
    }

    public byte[] Save()
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        ApplyPendingPageResize();
        ApplyPendingContentTransform();
        return _document.ToArray();
    }

    /// <summary>Release the bound document. Matches the
    /// BindPdf / Save / Close lifecycle.</summary>
    public void Close()
    {
        _document?.Dispose();
        _document = null;
    }

    /// <summary>IDisposable implementation; delegates to <see cref="Close"/>.</summary>
    public void Dispose() => Close();

    /// <summary>
    /// Get or set page rotation in degrees (0, 90, 180, 270). When set, applies to the pages
    /// listed in <see cref="ProcessPages"/> — or to every page when ProcessPages is null/empty.
    /// </summary>
    public int Rotation
    {
        get
        {
            if (_document is null) return 0;
            if (_document.PageCount == 0) return 0;
            return (int)_document.Pages.At(1).Dict.GetInt("Rotate");
        }
        set
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            foreach (var page in TargetPagesForStateful())
                page.Dict.Set("Rotate", new PdfInteger(((value % 360) + 360) % 360));
        }
    }

    /// <summary>
    /// Get or set the display duration (in seconds) for the pages in the bound document.
    /// The duration specifies how long the page is displayed during a presentation.
    /// A value of -1 (or any negative) removes the duration entry. When
    /// <see cref="ProcessPages"/> is non-null, only those pages are updated.
    /// </summary>
    public int DisplayDuration
    {
        get
        {
            if (_document is null) return -1;
            if (_document.PageCount == 0) return -1;
            var val = _document.Pages.At(1).Dict.Get("Dur");
            if (val is PdfInteger i) return (int)i.Value;
            if (val is PdfReal r) return (int)r.Value;
            return -1;
        }
        set
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            foreach (var page in TargetPagesForStateful())
            {
                if (value < 0)
                    page.Dict.Remove("Dur");
                else
                    page.Dict.Set("Dur", new PdfInteger(value));
            }
        }
    }

    private AlignmentType _alignment = Aspose.Pdf.Facades.AlignmentType.Left;

    /// <summary>Horizontal alignment used to position page content within the
    /// resized media box when <see cref="PageSize"/> is set. Like
    /// <see cref="VerticalAlignment"/>, setting it drives the content-positioning
    /// transform applied during <see cref="ApplyChanges"/>/<see cref="Save()"/>.</summary>
    public AlignmentType Alignment
    {
        get => _alignment;
        set
        {
            _alignment = value;
            HorizontalAlignment = value?.Name switch
            {
                "Left" => Aspose.Pdf.HorizontalAlignment.Left,
                "Center" => Aspose.Pdf.HorizontalAlignment.Center,
                "Right" => Aspose.Pdf.HorizontalAlignment.Right,
                _ => Aspose.Pdf.HorizontalAlignment.None,
            };
        }
    }

    private VerticalAlignmentType _verticalAlignment = Aspose.Pdf.Facades.VerticalAlignmentType.Top;

    /// <summary>Vertical alignment used to position page content within the
    /// resized media box when <see cref="PageSize"/> is set. This is the
    /// canonical property; setting it drives the content-centering
    /// transform applied during <see cref="ApplyChanges"/>/<see cref="Save()"/>.</summary>
    public VerticalAlignmentType VerticalAlignment
    {
        get => _verticalAlignment;
        set
        {
            _verticalAlignment = value;
            VerticalAlignmentType = value?.Name switch
            {
                "Top" => Aspose.Pdf.VerticalAlignment.Top,
                "Center" => Aspose.Pdf.VerticalAlignment.Center,
                "Bottom" => Aspose.Pdf.VerticalAlignment.Bottom,
                _ => Aspose.Pdf.VerticalAlignment.None,
            };
        }
    }

    /// <summary>
    /// Per-page rotation map, keyed by 1-based page number with rotation in degrees
    /// (0/90/180/270). Get builds the dictionary from the current document; set writes
    /// the listed rotations and leaves unlisted pages untouched.
    /// </summary>
    public Dictionary<int, int> PageRotations
    {
        get
        {
            var result = new Dictionary<int, int>();
            if (_document is null) return result;
            for (var i = 1; i <= _document.PageCount; i++)
                result[i] = (int)_document.Pages.At(i).Dict.GetInt("Rotate");
            return result;
        }
        set
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            if (value is null) return;
            foreach (var (pageNo, rotation) in value)
            {
                if (pageNo < 1 || pageNo > _document.PageCount) continue;
                _document.Pages.At(pageNo).Dict.Set("Rotate", new PdfInteger(((rotation % 360) + 360) % 360));
            }
        }
    }

    /// <summary>
    /// 1-based page numbers that subsequent stateful operations apply to. Null/empty =
    /// all pages. Currently honoured by <see cref="DisplayDuration"/>, <see cref="Rotation"/>,
    /// <see cref="TransitionType"/>, and <see cref="TransitionDuration"/>.
    /// </summary>
    public int[]? ProcessPages
    {
        get => _processPages;
        set => _processPages = value;
    }

    /// <summary>Transition style code (use the static constants on this class). 0 = no transition.</summary>
    public int TransitionType
    {
        get
        {
            if (_document is null || _document.PageCount == 0) return 0;
            return TransitionDictToCode(_document.Pages.At(1).Dict);
        }
        set
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            foreach (var page in TargetPagesForStateful())
                ApplyTransitionStyle(page, value);
        }
    }

    /// <summary>Transition effect duration in seconds (PDF /Trans /D). Default 1.</summary>
    public int TransitionDuration
    {
        get
        {
            if (_document is null || _document.PageCount == 0) return 1;
            if (_document.Pages.At(1).Reader.Resolve(_document.Pages.At(1).Dict.Get("Trans"))
                is PdfDictionary t)
            {
                if (t.Get("D") is PdfInteger i) return (int)i.Value;
                if (t.Get("D") is PdfReal r) return (int)r.Value;
            }
            return 1;
        }
        set
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            foreach (var page in TargetPagesForStateful())
            {
                if (page.Reader.Resolve(page.Dict.Get("Trans")) is PdfDictionary t)
                    t.Set("D", new PdfInteger(value));
            }
        }
    }

    /// <summary>Number of pages in the bound document.</summary>
    public int GetPages()
    {
        if (_document is null) return 0;
        return _document.PageCount;
    }

    /// <summary>1-based page rotation in degrees (0/90/180/270).</summary>
    public int GetPageRotation(int page)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        return (int)GetPage(_document, page).Dict.GetInt("Rotate");
    }

    /// <summary>1-based page size, derived from the page MediaBox. The dimensions are
    /// rotation-adjusted: a page with /Rotate 90 or 270 reports its displayed (swapped)
    /// width and height.</summary>
    public PageSize GetPageSize(int page)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        var p = GetPage(_document, page);
        var mb = p.MediaBox;
        int rot = (((int)p.Dict.GetInt("Rotate") % 360) + 360) % 360;
        return rot is 90 or 270
            ? new PageSize(mb.Height, mb.Width)
            : new PageSize(mb.Width, mb.Height);
    }

    /// <summary>
    /// Return the named page-box rectangle as <see cref="System.Drawing.Rectangle"/>.
    /// <paramref name="pageBoxName"/> matches the Aspose convention: "Media", "Crop",
    /// "Trim", "Bleed", or "Art" (case-insensitive). Falls back to MediaBox for unknown
    /// or unset boxes.
    /// </summary>
    public System.Drawing.Rectangle GetPageBoxSize(int page, string pageBoxName)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        var p = GetPage(_document, page);
        var key = (pageBoxName ?? "Media").ToLowerInvariant() switch
        {
            "crop" => "CropBox",
            "trim" => "TrimBox",
            "bleed" => "BleedBox",
            "art" => "ArtBox",
            _ => "MediaBox",
        };
        var rect = ResolveRect(p, key) ?? p.MediaBox;
        return new System.Drawing.Rectangle(
            (int)rect.LLX, (int)rect.LLY, (int)rect.Width, (int)rect.Height);
    }

    /// <summary>Record a translation offset (in points) for subsequent operations. Stored only.</summary>
    public void MovePosition(float moveX, float moveY)
    {
        _moveX = moveX;
        _moveY = moveY;
    }

    private IEnumerable<Page> TargetPagesForStateful()
    {
        if (_document is null) yield break;
        if (_processPages is null || _processPages.Length == 0)
        {
            foreach (var p in _document.Pages) yield return p;
            yield break;
        }
        foreach (var n in _processPages)
            if (n >= 1 && n <= _document.PageCount)
                yield return _document.Pages.At(n);
    }

    private static int TransitionDictToCode(PdfDictionary pageDict)
    {
        if (pageDict.Get("Trans") is not PdfDictionary t) return 0;
        var style = t.GetName("S") ?? "";
        var dim = t.GetName("Dm") ?? "";
        var motion = t.GetName("M") ?? "";
        var di = t.Get("Di") is PdfInteger d ? (int)d.Value : -1;
        return (style, dim, motion, di) switch
        {
            ("Split",    "V", "O", _)  => SPLITVOUT,
            ("Split",    "H", "O", _)  => SPLITHOUT,
            ("Split",    "V", "I", _)  => SPLITVIN,
            ("Split",    "H", "I", _)  => SPLITHIN,
            ("Blinds",   "V", _,   _)  => BLINDV,
            ("Blinds",   "H", _,   _)  => BLINDH,
            ("Box",      _,   "I", _)  => INBOX,
            ("Box",      _,   "O", _)  => OUTBOX,
            ("Wipe",     _,   _,   0)  => LRWIPE,
            ("Wipe",     _,   _,   180) => RLWIPE,
            ("Wipe",     _,   _,   90)  => BTWIPE,
            ("Wipe",     _,   _,   270) => TBWIPE,
            ("Dissolve", _,   _,   _)  => DISSOLVE,
            ("Glitter",  _,   _,   0)   => LRGLITTER,
            ("Glitter",  _,   _,   270) => TBGLITTER,
            ("Glitter",  _,   _,   315) => DGLITTER,
            _ => 0,
        };
    }

    private static void ApplyTransitionStyle(Page page, int code)
    {
        if (code <= 0)
        {
            page.Dict.Remove("Trans");
            return;
        }
        var t = new PdfDictionary();
        t.Set("Type", new PdfName("Trans"));
        switch (code)
        {
            case SPLITVOUT: t.Set("S", new PdfName("Split"));   t.Set("Dm", new PdfName("V")); t.Set("M", new PdfName("O")); break;
            case SPLITHOUT: t.Set("S", new PdfName("Split"));   t.Set("Dm", new PdfName("H")); t.Set("M", new PdfName("O")); break;
            case SPLITVIN:  t.Set("S", new PdfName("Split"));   t.Set("Dm", new PdfName("V")); t.Set("M", new PdfName("I")); break;
            case SPLITHIN:  t.Set("S", new PdfName("Split"));   t.Set("Dm", new PdfName("H")); t.Set("M", new PdfName("I")); break;
            case BLINDV:    t.Set("S", new PdfName("Blinds"));  t.Set("Dm", new PdfName("V")); break;
            case BLINDH:    t.Set("S", new PdfName("Blinds"));  t.Set("Dm", new PdfName("H")); break;
            case INBOX:     t.Set("S", new PdfName("Box"));     t.Set("M",  new PdfName("I")); break;
            case OUTBOX:    t.Set("S", new PdfName("Box"));     t.Set("M",  new PdfName("O")); break;
            case LRWIPE:    t.Set("S", new PdfName("Wipe"));    t.Set("Di", new PdfInteger(0));   break;
            case RLWIPE:    t.Set("S", new PdfName("Wipe"));    t.Set("Di", new PdfInteger(180)); break;
            case BTWIPE:    t.Set("S", new PdfName("Wipe"));    t.Set("Di", new PdfInteger(90));  break;
            case TBWIPE:    t.Set("S", new PdfName("Wipe"));    t.Set("Di", new PdfInteger(270)); break;
            case DISSOLVE:  t.Set("S", new PdfName("Dissolve")); break;
            case LRGLITTER: t.Set("S", new PdfName("Glitter")); t.Set("Di", new PdfInteger(0));   break;
            case TBGLITTER: t.Set("S", new PdfName("Glitter")); t.Set("Di", new PdfInteger(270)); break;
            case DGLITTER:  t.Set("S", new PdfName("Glitter")); t.Set("Di", new PdfInteger(315)); break;
        }
        page.Dict.Set("Trans", t);
    }

    private static Rectangle? ResolveRect(Page page, string key)
    {
        if (page.Reader.Resolve(page.Dict.Get(key)) is PdfArray arr && arr.Count >= 4)
            return Rectangle.FromPdfArray(arr);
        return null;
    }

    // ── Stateless API ─────────────────────────────────────────────────────────
    /// <summary>
    /// Rotate specified pages. If no page numbers given, rotates all pages.
    /// </summary>
    /// <param name="input">Source PDF bytes.</param>
    /// <param name="rotation">Rotation in degrees (0, 90, 180, 270).</param>
    /// <param name="pageNumbers">1-based page numbers to rotate. Empty = all pages.</param>
    public byte[] RotatePages(byte[] input, int rotation, params int[] pageNumbers)
    {
        using var doc = Document.Open(input);
        var pages = GetTargetPages(doc, pageNumbers);
        foreach (var page in pages)
        {
            var current = (int)page.Dict.GetInt("Rotate");
            page.Dict.Set("Rotate", new PdfInteger((current + rotation) % 360));
        }
        return doc.ToArray();
    }

    /// <summary>
    /// Set the rotation of specified pages to an absolute value.
    /// </summary>
    public byte[] SetRotation(byte[] input, int rotation, params int[] pageNumbers)
    {
        using var doc = Document.Open(input);
        var pages = GetTargetPages(doc, pageNumbers);
        foreach (var page in pages)
        {
            page.Dict.Set("Rotate", new PdfInteger(rotation % 360));
        }
        return doc.ToArray();
    }

    /// <summary>
    /// Resize specified pages to a new media box.
    /// </summary>
    public byte[] ResizePages(byte[] input, Rectangle newMediaBox, params int[] pageNumbers)
    {
        using var doc = Document.Open(input);
        var pages = GetTargetPages(doc, pageNumbers);
        foreach (var page in pages)
        {
            page.Dict.Set("MediaBox", MakeRectArray(newMediaBox));
        }
        return doc.ToArray();
    }

    /// <summary>
    /// Set the CropBox (visible area) on a specific page.
    /// Per PDF spec section 14.11.2, CropBox defaults to MediaBox if not specified.
    /// </summary>
    /// <param name="input">Source PDF bytes.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="rect">The crop box rectangle.</param>
    public byte[] SetCropBox(byte[] input, int pageNumber, Rectangle rect)
    {
        return SetBox(input, pageNumber, "CropBox", rect);
    }

    /// <summary>
    /// Set the TrimBox (intended finished page size after trimming) on a specific page.
    /// </summary>
    public byte[] SetTrimBox(byte[] input, int pageNumber, Rectangle rect)
    {
        return SetBox(input, pageNumber, "TrimBox", rect);
    }

    /// <summary>
    /// Set the BleedBox (region to which content should be clipped when printed) on a specific page.
    /// </summary>
    public byte[] SetBleedBox(byte[] input, int pageNumber, Rectangle rect)
    {
        return SetBox(input, pageNumber, "BleedBox", rect);
    }

    /// <summary>
    /// Set the ArtBox (meaningful content area) on a specific page.
    /// </summary>
    public byte[] SetArtBox(byte[] input, int pageNumber, Rectangle rect)
    {
        return SetBox(input, pageNumber, "ArtBox", rect);
    }

    /// <summary>
    /// Adjust page margins by modifying the CropBox relative to the MediaBox.
    /// The CropBox is set to MediaBox inset by the specified margins.
    /// </summary>
    /// <param name="input">Source PDF bytes.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="left">Left margin inset.</param>
    /// <param name="bottom">Bottom margin inset.</param>
    /// <param name="right">Right margin inset.</param>
    /// <param name="top">Top margin inset.</param>
    public byte[] SetMargins(byte[] input, int pageNumber, double left, double bottom, double right, double top)
    {
        using var doc = Document.Open(input);
        var page = GetPage(doc, pageNumber);
        var mb = page.MediaBox;
        var cropRect = new Rectangle(mb.LLX + left, mb.LLY + bottom, mb.URX - right, mb.URY - top);
        page.SetCropBox(cropRect);
        return doc.ToArray();
    }

    /// <summary>
    /// Get the CropBox for a specific page. Returns null if not explicitly set.
    /// </summary>
    public Rectangle? GetCropBox(byte[] input, int pageNumber)
    {
        return GetBox(input, pageNumber, "CropBox");
    }

    /// <summary>
    /// Get the TrimBox for a specific page. Returns null if not explicitly set.
    /// </summary>
    public Rectangle? GetTrimBox(byte[] input, int pageNumber)
    {
        return GetBox(input, pageNumber, "TrimBox");
    }

    /// <summary>
    /// Get the BleedBox for a specific page. Returns null if not explicitly set.
    /// </summary>
    public Rectangle? GetBleedBox(byte[] input, int pageNumber)
    {
        return GetBox(input, pageNumber, "BleedBox");
    }

    /// <summary>
    /// Get the ArtBox for a specific page. Returns null if not explicitly set.
    /// </summary>
    public Rectangle? GetArtBox(byte[] input, int pageNumber)
    {
        return GetBox(input, pageNumber, "ArtBox");
    }

    /// <summary>
    /// Scale page content by adjusting the MediaBox dimensions.
    /// The MediaBox origin stays the same; width and height are multiplied by scale factors.
    /// </summary>
    /// <param name="input">Source PDF bytes.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="scaleX">Horizontal scale factor (e.g. 2.0 = double width).</param>
    /// <param name="scaleY">Vertical scale factor (e.g. 0.5 = half height).</param>
    public byte[] ScalePage(byte[] input, int pageNumber, double scaleX, double scaleY)
    {
        using var doc = Document.Open(input);
        var page = GetPage(doc, pageNumber);
        var mb = page.MediaBox;
        var newWidth = mb.Width * scaleX;
        var newHeight = mb.Height * scaleY;
        var newMediaBox = new Rectangle(mb.LLX, mb.LLY, mb.LLX + newWidth, mb.LLY + newHeight);
        page.SetMediaBox(newMediaBox);
        return doc.ToArray();
    }

    /// <summary>
    /// Convert a Rectangle to a PdfArray with four real number elements [llx, lly, urx, ury].
    /// </summary>
    internal static PdfArray MakeRectArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    private byte[] SetBox(byte[] input, int pageNumber, string boxName, Rectangle rect)
    {
        using var doc = Document.Open(input);
        var page = GetPage(doc, pageNumber);
        page.Dict.Set(boxName, MakeRectArray(rect));
        return doc.ToArray();
    }

    private Rectangle? GetBox(byte[] input, int pageNumber, string boxName)
    {
        using var doc = Document.Open(input);
        var page = GetPage(doc, pageNumber);
        var obj = page.Reader.Resolve(page.Dict.Get(boxName));
        if (obj is PdfArray arr && arr.Count >= 4)
            return Rectangle.FromPdfArray(arr);
        return null;
    }

    private static Page GetPage(Document doc, int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page number {pageNumber} is out of range. Document has {doc.PageCount} page(s).");
        return doc.Pages.At(pageNumber);
    }

    private static List<Page> GetTargetPages(Document doc, int[] pageNumbers)
    {
        if (pageNumbers.Length == 0)
            return doc.Pages.ToList();

        var pages = new List<Page>();
        foreach (var num in pageNumbers)
        {
            if (num >= 1 && num <= doc.PageCount)
                pages.Add(doc.Pages.At(num));
        }
        return pages;
    }
}
