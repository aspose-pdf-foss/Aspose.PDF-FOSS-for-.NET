using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for adding stamps, page numbers, headers, footers, and watermarks.
/// Supports both stateless (byte[]-in / byte[]-out) and stateful (constructor with paths / AddStamp / Close) modes.
/// </summary>
public sealed class PdfFileStamp : System.IDisposable
{
    // ── Page-position constants (1..8 layout) ─────────────────────────────────
    public const int PosUpperLeft = 1;
    public const int PosUpperMiddle = 2;
    public const int PosUpperRight = 3;
    public const int PosSidesLeft = 4;
    public const int PosSidesRight = 5;
    public const int PosBottomLeft = 6;
    public const int PosBottomMiddle = 7;
    public const int PosBottomRight = 8;

    private Document? _document;
    private string? _outputPath;
    private byte[]? _inputData;
    private Stream? _inputStream;
    private Stream? _outputStream;

    /// <summary>
    /// Default constructor for stateless mode.
    /// </summary>
    public PdfFileStamp()
    {
    }

    /// <summary>
    /// Bind an already-loaded Document. Save target stays unset until OutputFile/OutputStream is configured.
    /// </summary>
    public PdfFileStamp(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// Bind a Document and pre-configure an output stream for the parameterless Save.
    /// </summary>
    public PdfFileStamp(Document document, Stream outputStream)
    {
        _document = document;
        _outputStream = outputStream;
    }

    /// <summary>
    /// Bind a Document and pre-configure an output file path for the parameterless Save.
    /// </summary>
    public PdfFileStamp(Document document, string outputFile)
    {
        _document = document;
        _outputPath = outputFile;
    }

    /// <summary>
    /// Open from an input stream, writing to an output stream on Save.
    /// </summary>
    public PdfFileStamp(Stream inputStream, Stream outputStream)
        : this(inputStream, outputStream, keepSecurity: false)
    {
    }

    /// <summary>
    /// Open from an input stream, writing to an output stream on Save. The keepSecurity flag is recorded
    /// on <see cref="KeepSecurity"/> for callers to inspect.
    /// </summary>
    public PdfFileStamp(Stream inputStream, Stream outputStream, bool keepSecurity)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        KeepSecurity = keepSecurity;
        _inputData = ReadAll(inputStream);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Open from an input file, writing to an output file on Save.
    /// </summary>
    public PdfFileStamp(string inputFile, string outputFile)
        : this(inputFile, outputFile, keepSecurity: false)
    {
    }

    /// <summary>
    /// Open from an input file, writing to an output file on Save. The keepSecurity flag is recorded
    /// on <see cref="KeepSecurity"/> for callers to inspect.
    /// </summary>
    public PdfFileStamp(string inputFile, string outputFile, bool keepSecurity)
    {
        _inputFile = inputFile;
        _outputPath = outputFile;
        KeepSecurity = keepSecurity;
        _inputData = File.ReadAllBytes(inputFile);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Create a PdfFileStamp for given input bytes, saving to the given output file on Close().
    /// </summary>
    public PdfFileStamp(byte[] inputData, string outputPath)
    {
        _inputData = inputData;
        _document = Document.Open(inputData);
        _outputPath = outputPath;
    }

    /// <summary>
    /// Bind a PDF document from a file path for stateful processing.
    /// </summary>
    public void BindPdf(string path)
    {
        _inputData = File.ReadAllBytes(path);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Bind a PDF document from a stream for stateful processing.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        if (inputStream.CanSeek) inputStream.Seek(0, SeekOrigin.Begin);
        _inputData = ReadAll(inputStream);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Bind a PDF document for stateful processing.
    /// </summary>
    public void BindPdf(Document doc)
    {
        _document = doc;
    }

    private static byte[] ReadAll(Stream s)
    {
        if (s is MemoryStream ms) return ms.ToArray();
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Save the modified document to the specified path.
    /// </summary>
    public void Save(string destFile)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        _document.Save(destFile);
    }

    /// <summary>
    /// Save the modified document to a stream.
    /// </summary>
    public void Save(Stream destStream)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        var data = _document.ToArray();
        destStream.Write(data);
    }

    /// <summary>
    /// Save the modified document to a byte array.
    /// </summary>
    public byte[] Save()
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        return _document.ToArray();
    }

    /// <summary>Applies the requested <see cref="ConvertTo"/> target to the bound
    /// document. For the plain PDF version formats (v_1_x / v_2_0) this sets the
    /// document version so the saved file carries the requested header.</summary>
    private void ApplyConvertTo()
    {
        if (_document is null || _convertTo is null) return;
        var name = _convertTo.Value.ToString();
        if (name.StartsWith("v_", StringComparison.Ordinal))
            _document.SetVersion(name.Substring(2).Replace('_', '.'));
    }

    /// <summary>The bound document.</summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>
    /// Input file path. Setting binds the PDF lazily so that subsequent
    /// page-dimension queries reflect the input.
    /// </summary>
    public string? InputFile
    {
        get => _inputFile;
        set
        {
            _inputFile = value;
            if (value is not null) BindPdf(value);
        }
    }
    private string? _inputFile;

    /// <summary>
    /// Output file path. Save targets this when the parameterless Save is later wired up.
    /// </summary>
    public string? OutputFile
    {
        get => _outputPath;
        set => _outputPath = value;
    }

    /// <summary>
    /// Width of the first page in the bound document. Defaults to A4 (595)
    /// when no document is bound.
    /// </summary>
    public float PageWidth =>
        _document is null || _document.Pages.Count == 0
            ? 595f
            : (float)_document.Pages[1].MediaBox.Width;

    /// <summary>
    /// Height of the first page in the bound document. Defaults to A4 (842)
    /// when no document is bound.
    /// </summary>
    public float PageHeight =>
        _document is null || _document.Pages.Count == 0
            ? 842f
            : (float)_document.Pages[1].MediaBox.Height;

    /// <summary>
    /// Input stream — setting binds the PDF eagerly so page-dimension queries reflect the new input.
    /// </summary>
    public Stream? InputStream
    {
        get => _inputStream;
        set
        {
            _inputStream = value;
            if (value is not null)
            {
                _inputData = ReadAll(value);
                _document = Document.Open(_inputData);
            }
        }
    }

    /// <summary>
    /// Output stream — Save() writes here when no explicit destination is passed.
    /// </summary>
    public Stream? OutputStream
    {
        get => _outputStream;
        set => _outputStream = value;
    }

    /// <summary>
    /// If true, the source document's security (encryption, permissions) should be preserved on Save.
    /// Recorded for callers to inspect; the current save path does not re-apply source encryption.
    /// </summary>
    public bool KeepSecurity { get; set; }

    /// <summary>
    /// Numbering style for AddPageNumber. Defaults to <see cref="Aspose.Pdf.NumberingStyle.Decimal"/>.
    /// </summary>
    public NumberingStyle NumberingStyle { get; set; } = NumberingStyle.Decimal;

    /// <summary>
    /// Hint: optimize the saved output for size. Stored for callers to inspect; not currently honoured by Save.
    /// </summary>
    public bool OptimizeSize { get; set; }

    /// <summary>
    /// Rotation (degrees) applied to AddPageNumber stamps.
    /// </summary>
    public float PageNumberRotation { get; set; }

    /// <summary>
    /// Stamp identifier embedded as a content-stream comment by AddStamp.
    /// </summary>
    public int StampId { get; set; }

    /// <summary>
    /// Starting number used by AddPageNumber. Defaults to 1.
    /// </summary>
    public int StartingNumber { get; set; } = 1;

    /// <summary>
    /// PDF/A or PDF version target for the saved output. Stored for callers to inspect; the current
    /// Save path emits plain PDF regardless of this value.
    /// </summary>
    public PdfFormat ConvertTo { set => _convertTo = value; }
    private PdfFormat? _convertTo;

    /// <summary>
    /// Add a facade stamp to the bound document.
    /// The stamp is applied to the pages specified by stamp.Pages (or all pages if not set).
    /// A %StampId comment is embedded in the content stream for later retrieval.
    /// </summary>
    public void AddStamp(Stamp stamp)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        var pages = stamp.Pages;
        foreach (var page in _document.Pages)
        {
            if (pages is not null && pages.Length > 0)
            {
                bool shouldApply = false;
                foreach (var p in pages)
                {
                    if (p == page.Number) { shouldApply = true; break; }
                }
                if (!shouldApply) continue;
            }

            if (stamp.IsTextStamp && stamp.LogoText is not null)
            {
                ApplyTextStamp(page, stamp);
            }
            else if (stamp.LogoImage is not null)
            {
                ApplyImageStamp(page, stamp);
            }
            else if (stamp.PdfBytes is not null)
            {
                // PDF-page stamp (Stamp.BindPdf): draw the source page as a Form
                // XObject onto the target page, importing its resource graph.
                var pageStamp = new PdfPageStamp(new MemoryStream(stamp.PdfBytes), stamp.PdfPageNumber)
                {
                    IsBackground = stamp.IsBackground,
                    XIndent = stamp.XOrigin,
                    YIndent = stamp.YOrigin,
                };
                if (stamp.ImageWidth > 0) pageStamp.Width = stamp.ImageWidth;
                if (stamp.ImageHeight > 0) pageStamp.Height = stamp.ImageHeight;
                pageStamp.ApplyTo(page);
            }
        }
    }

    /// <summary>
    /// Close the document and save to the output path.
    /// </summary>
    public void Close()
    {
        if (_document is null) return;
        ApplyConvertTo();
        if (_outputPath is not null)
        {
            _document.Save(_outputPath);
        }
        else if (_outputStream is not null)
        {
            // Stream-bound stamp (e.g. PdfFileStamp(inputStream, outputStream)):
            // flush the stamped document to the configured output stream, mirroring
            // Save(Stream). Without this the output stream stays empty and any
            // subsequent reader rejects it as a headerless file.
            var data = _document.ToArray();
            _outputStream.Write(data, 0, data.Length);
        }
        _document.Dispose();
        _document = null;
    }

    /// <summary>IDisposable implementation; delegates to <see cref="Close"/>.</summary>
    public void Dispose() => Close();

    private static void ApplyTextStamp(Page page, Stamp stamp)
    {
        var text = stamp.LogoText!;
        var ts = stamp.TextState;
        // Register the stamp's font as a page resource and reference its actual
        // resource key. Emitting the raw base-font name (e.g. /Courier) referenced
        // a font that wasn't in the page's /Resources/Font, so the glyphs silently
        // failed to render.
        var fontName = string.IsNullOrEmpty(text.FontName) ? "Helvetica" : text.FontName;
        var fontRes = EnsureFont(page, fontName);
        var sb = new StringBuilder();
        // Always emit the %StampId comment (id 0 when the caller did not assign
        // one) so that even unnamed text stamps are discoverable through the
        // stamp facade — matching the image-stamp path.
        sb.Append($"%StampId={stamp.StampId}\n");
        sb.Append("q\n");
        // Background-colour fill behind the glyphs, honouring FormattedText.BackgroundColor.
        // Drawn first so it renders underneath the text (e.g. a highlighted text box).
        if (!text.BackgroundColor.IsEmpty)
        {
            double descent = (Aspose.Pdf.Text.Standard14Fonts.IsStandard14(fontName)
                ? Aspose.Pdf.Text.Standard14Fonts.GetDescent(fontName) : -207) * text.FontSize / 1000.0;
            sb.Append($"{NormColor(text.BackgroundColor.R)} {NormColor(text.BackgroundColor.G)} {NormColor(text.BackgroundColor.B)} rg\n");
            sb.Append($"{Format(stamp.XOrigin)} {Format(stamp.YOrigin + descent)} {Format(text.TextWidth)} {Format(text.FontSize - descent)} re f\n");
        }
        // A bound TextState overrides text-rendering defaults: fill colour (rg),
        // stroking colour (RG, used by the stroke/fill+stroke rendering modes) and
        // the rendering mode itself (Tr). These are graphics/text-state operators
        // and are valid before the BT...ET block; the text extractor reads them
        // back into TextFragment.TextState. When no TextState is
        // bound, fall back to the FormattedText's own foreground colour.
        if (ts?.ForegroundColor is { } fg)
            sb.Append($"{NormColor(fg.R)} {NormColor(fg.G)} {NormColor(fg.B)} rg\n");
        else
            sb.Append($"{NormColor(text.ForegroundColor.R)} {NormColor(text.ForegroundColor.G)} {NormColor(text.ForegroundColor.B)} rg\n");
        if (ts?.StrokingColor is { } sc)
            sb.Append($"{NormColor(sc.R)} {NormColor(sc.G)} {NormColor(sc.B)} RG\n");
        if (ts is not null && (int)ts.RenderingMode != 0)
            sb.Append($"{(int)ts.RenderingMode} Tr\n");
        sb.Append($"BT /{fontRes} {Format(text.FontSize)} Tf ");
        sb.Append($"{Format(stamp.XOrigin)} {Format(stamp.YOrigin)} Td ");
        sb.Append($"({EscapePdfString(text.Text)}) Tj ET\n");
        sb.Append("Q\n");
        AppendContent(page, Encoding.ASCII.GetBytes(sb.ToString()));
    }

    // Ensure a Type1 base font named <paramref name="fontName"/> is present in the
    // page's /Resources/Font dictionary, returning its resource key (e.g. "F1").
    // Mirrors PdfFileMend.EnsureFont — a text stamp must reference a font that
    // actually exists in the page resources or the glyphs won't render.
    private static string EnsureFont(Page page, string fontName)
    {
        var pageDict = page.Dict;

        // A page's /Resources is frequently inherited from the /Pages tree (the page
        // dict itself carries no /Resources). Resolve the effective resources, and
        // when they are inherited give the page a private copy seeded with the
        // inherited entries — otherwise setting a fresh /Resources here would shadow
        // (and thereby drop from rendering) the page's existing embedded fonts.
        var resources = page.Reader.ResolveDict(pageDict.Get("Resources"));
        var resourcesWereInherited = false;
        if (resources is null)
        {
            var inherited = ResolveInheritedResources(page);
            resources = new PdfDictionary();
            if (inherited is not null)
                foreach (var key in inherited.Keys)
                {
                    var v = inherited.Get(key);
                    if (v is not null) resources.Set(key, v);
                }
            pageDict.Set("Resources", resources);
            resourcesWereInherited = true;
        }

        var fontDict = page.Reader.ResolveDict(resources.Get("Font"))
            ?? resources.Get("Font") as PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        else if (resourcesWereInherited)
        {
            // The /Font dict came from the inherited resources and is shared with
            // sibling pages — copy it page-local so the new stamp font does not leak
            // into (or collide on) their inherited resources.
            var localFonts = new PdfDictionary();
            foreach (var key in fontDict.Keys)
            {
                var v = fontDict.Get(key);
                if (v is not null) localFonts.Set(key, v);
            }
            fontDict = localFonts;
            resources.Set("Font", fontDict);
        }

        // Reuse an existing entry for the same base font when present.
        int count = 0;
        foreach (var key in fontDict.Keys)
        {
            count++;
            var existing = page.Reader.ResolveDict(fontDict.Get(key));
            if (existing is not null)
            {
                var baseName = existing.GetName("BaseFont");
                if (baseName == fontName || baseName == "/" + fontName)
                    return key;
            }
        }

        var pdfFontName = $"F{count}";
        var newFont = new PdfDictionary();
        newFont.Set("Type", new PdfName("Font"));
        newFont.Set("Subtype", new PdfName("Type1"));
        newFont.Set("BaseFont", new PdfName(fontName));
        newFont.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(pdfFontName, newFont);
        return pdfFontName;
    }

    // Resolve a page's /Resources inherited from the /Pages tree, walking the
    // /Parent chain (mirrors Page's inherited-attribute lookup). Returns null when
    // no ancestor declares /Resources.
    private static PdfDictionary? ResolveInheritedResources(Page page)
    {
        var parentObj = page.Dict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;
            var parent = page.Reader.ResolveDict(parentObj);
            if (parent is null) break;
            var res = page.Reader.ResolveDict(parent.Get("Resources"));
            if (res is not null) return res;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    // Format an 8-bit colour component as a normalised PDF colour operand
    // (0..1, invariant culture, no exponent). 128/255 → "0.501961", which the
    // text extractor maps back to byte 128 (#808080).
    private static string NormColor(byte c) =>
        (c / 255.0).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string Format(double v) =>
        v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    private static void ApplyImageStamp(Page page, Stamp stamp)
    {
        var imageData = stamp.LogoImage!;
        // Auto-detect JPEG vs PNG vs raw RGB by header bytes; falls back to raw RGB
        // with the stamp's declared dimensions if the bytes match no known image format.
        var isJpeg = imageData.Length >= 2 && imageData[0] == 0xFF && imageData[1] == 0xD8;
        var isPng  = imageData.Length >= 8 && imageData[0] == 0x89 && imageData[1] == 0x50
                  && imageData[2] == 0x4E && imageData[3] == 0x47;
        ImageStamp imgStamp;
        if (isJpeg)
            imgStamp = ImageStamp.FromJpeg(imageData);
        else if (isPng)
            imgStamp = ImageStamp.FromPngData(imageData);
        else if (OperatingSystem.IsWindows()
                 && ImageStamp.TryFromGdiPlusDecoder(imageData) is { } gdiStamp)
        {
            // GIF / TIFF / EMF / WMF / ICO via System.Drawing. Without this
            // branch the legacy raw-RGB fallback below raises ArgumentException
            // ('Pixel data length must equal width × height × 3') for any
            // non-square / non-RGB input.
            imgStamp = gdiStamp;
        }
        else
        {
            // Last resort: treat as raw RGB. Use the caller's SetImageSize
            // dimensions if given; else infer a sqrt-based fallback from the
            // payload length under the assumption of 3 bytes/pixel (square).
            int w, h;
            if (stamp.ImageWidth > 0 && stamp.ImageHeight > 0)
            {
                w = (int)stamp.ImageWidth;
                h = (int)stamp.ImageHeight;
            }
            else
            {
                var pixelCount = imageData.Length / 3;
                var side = (int)Math.Sqrt(pixelCount);
                w = side > 0 ? side : 1;
                h = side > 0 ? side : 1;
            }
            imgStamp = ImageStamp.FromRgb(imageData, w, h);
        }
        imgStamp.X = stamp.XOrigin;
        imgStamp.Y = stamp.YOrigin;
        // Carry the facade stamp's JPEG quality through to the embedded image.
        imgStamp.Quality = stamp.Quality;
        // Honour the facade stamp's rotation/opacity/background so an image stamp
        // configured via Stamp.Rotation/Opacity/IsBackground renders as requested
        // (e.g. a 90-degree-rotated logo) rather than always upright/opaque/foreground.
        imgStamp.RotateAngle = stamp.Rotation;
        imgStamp.Opacity = stamp.Opacity;
        imgStamp.Background = stamp.IsBackground;
        // ImageStamp.ApplyTo emits the stamp as its own content stream and writes a
        // %StampId marker comment into it (always — id 0 when the caller did not
        // assign one) so PdfContentEditor.GetStamps can recover even unnamed stamps.
        imgStamp.StampId = stamp.StampId;
        imgStamp.ForceStampIdComment = true;
        imgStamp.ApplyTo(page);
    }

    private static void AppendContent(Page page, byte[] contentBytes)
    {
        var existing = page.Reader.Resolve(page.Dict.Get("Contents"));
        if (existing is PdfStream es)
        {
            var existingData = page.Reader.DecodeStream(es);
            var combined = new byte[existingData.Length + 1 + contentBytes.Length];
            existingData.CopyTo(combined, 0);
            combined[existingData.Length] = (byte)'\n';
            contentBytes.CopyTo(combined, existingData.Length + 1);
            page.SetContentStream(combined);
        }
        else
        {
            page.SetContentStream(contentBytes);
        }
    }

    private static byte[] GetPageContentBytes(Page page)
    {
        var contentsObj = page.Reader.Resolve(page.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
            return page.Reader.DecodeStream(stream);
        return [];
    }

    // ── Stateful header / footer / page-number API ──────────────────────────

    public void AddHeader(FormattedText formattedText, float topMargin) =>
        AddHeader(formattedText, topMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddHeader(FormattedText formattedText, float topMargin, float leftMargin, float rightMargin) =>
        ApplyTextBand(formattedText, top: true, primaryMargin: topMargin, leftMargin, rightMargin);

    public void AddHeader(Stream imageStream, float topMargin) =>
        AddHeader(imageStream, topMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddHeader(Stream inputStream, float topMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(ReadAll(inputStream), top: true, primaryMargin: topMargin, leftMargin, rightMargin);

    public void AddHeader(string imageFile, float topMargin) =>
        AddHeader(imageFile, topMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddHeader(string imageFile, float topMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(File.ReadAllBytes(imageFile), top: true, primaryMargin: topMargin, leftMargin, rightMargin);

    public void AddFooter(FormattedText formattedText, float bottomMargin) =>
        AddFooter(formattedText, bottomMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddFooter(FormattedText formattedText, float bottomMargin, float leftMargin, float rightMargin) =>
        ApplyTextBand(formattedText, top: false, primaryMargin: bottomMargin, leftMargin, rightMargin);

    public void AddFooter(Stream imageStream, float bottomMargin) =>
        AddFooter(imageStream, bottomMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddFooter(Stream imageStream, float bottomMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(ReadAll(imageStream), top: false, primaryMargin: bottomMargin, leftMargin, rightMargin);

    public void AddFooter(string imageFile, float bottomMargin) =>
        AddFooter(imageFile, bottomMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddFooter(string imageFile, float bottomMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(File.ReadAllBytes(imageFile), top: false, primaryMargin: bottomMargin, leftMargin, rightMargin);

    public void AddPageNumber(FormattedText formattedText) =>
        AddPageNumber(formattedText.Text, PosBottomMiddle, 36f, 36f, 36f, 36f, formattedText);

    public void AddPageNumber(FormattedText formattedText, int position) =>
        AddPageNumber(formattedText.Text, position, 36f, 36f, 36f, 36f, formattedText);

    public void AddPageNumber(FormattedText formattedText, int position,
        float leftMargin, float rightMargin, float topMargin, float bottomMargin) =>
        AddPageNumber(formattedText.Text, position, leftMargin, rightMargin, topMargin, bottomMargin, formattedText);

    public void AddPageNumber(FormattedText formattedText, float x, float y) =>
        ApplyPageNumberAtXY(formattedText.Text, x, y, formattedText);

    public void AddPageNumber(string formatString) =>
        AddPageNumber(formatString, PosBottomMiddle, 36f, 36f, 36f, 36f, sourceText: null);

    public void AddPageNumber(string formatString, int position) =>
        AddPageNumber(formatString, position, 36f, 36f, 36f, 36f, sourceText: null);

    public void AddPageNumber(string formatString, int position,
        float leftMargin, float rightMargin, float topMargin, float bottomMargin) =>
        AddPageNumber(formatString, position, leftMargin, rightMargin, topMargin, bottomMargin, sourceText: null);

    public void AddPageNumber(string formatString, float x, float y) =>
        ApplyPageNumberAtXY(formatString, x, y, sourceText: null);

    private void AddPageNumber(string formatString, int position,
        float leftMargin, float rightMargin, float topMargin, float bottomMargin,
        FormattedText? sourceText)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var (hAlign, vAlign, useTop) = ResolvePosition(position);
        var pageCount = _document.PageCount;
        for (var i = 1; i <= pageCount; i++)
        {
            var page = _document.Pages[i];
            var rendered = RenderPageNumberText(formatString, StartingNumber + i - 1, pageCount, NumberingStyle);
            var stamp = BuildTextStamp(rendered, sourceText);
            stamp.HorizontalAlignment = hAlign;
            stamp.VerticalAlignment = vAlign;
            stamp.XIndent = leftMargin > 0 ? leftMargin : (rightMargin > 0 ? -rightMargin : 0);
            stamp.YIndent = useTop ? topMargin : bottomMargin;
            stamp.RotateAngle = PageNumberRotation;
            page.AddStamp(stamp);
        }
    }

    private void ApplyPageNumberAtXY(string formatString, float x, float y, FormattedText? sourceText)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var pageCount = _document.PageCount;
        for (var i = 1; i <= pageCount; i++)
        {
            var page = _document.Pages[i];
            var rendered = RenderPageNumberText(formatString, StartingNumber + i - 1, pageCount, NumberingStyle);
            var stamp = BuildTextStamp(rendered, sourceText);
            stamp.HorizontalAlignment = HorizontalAlignment.None;
            stamp.VerticalAlignment = VerticalAlignment.None;
            stamp.XIndent = x;
            stamp.YIndent = y;
            stamp.RotateAngle = PageNumberRotation;
            page.AddStamp(stamp);
        }
    }

    private void ApplyTextBand(FormattedText formattedText, bool top, float primaryMargin, float leftMargin, float rightMargin)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var fontSize = formattedText.FontSize;
        foreach (var page in _document.Pages)
        {
            var stamp = BuildTextStamp(formattedText.Text, formattedText);
            stamp.HorizontalAlignment = HorizontalAlignment.Center;
            stamp.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            stamp.XIndent = leftMargin - rightMargin;
            stamp.YIndent = primaryMargin;
            stamp.MetaRect = TextBandRect(formattedText, top, primaryMargin, leftMargin, rightMargin,
                page.MediaBox.Width, page.MediaBox.Height);
            page.AddStamp(stamp);
        }
    }

    /// <summary>Bounding rectangle a header/footer text band occupies: the text line is
    /// centred within the [leftMargin, pageW-rightMargin] span; a footer sits with its box
    /// bottom at <paramref name="primaryMargin"/> and a header with its box top one tenth of
    /// the font size below <c>pageH - topMargin</c> (the half-leading), each box being exactly
    /// the font size tall.</summary>
    private static Rectangle TextBandRect(FormattedText ft, bool top, float primaryMargin,
        float leftMargin, float rightMargin, double pageW, double pageH)
    {
        double fontSize = ft.FontSize;
        double w;
        try
        {
            var font = Aspose.Pdf.Text.FontRepository.FindFont(ft.FontName ?? "Helvetica");
            w = font is not null ? font.MeasureString(ft.Text, fontSize) : ft.Text.Length * fontSize * 0.5;
        }
        catch { w = ft.Text.Length * fontSize * 0.5; }
        double llx = leftMargin + (pageW - leftMargin - rightMargin - w) / 2;
        double lly = top ? (pageH - primaryMargin - fontSize * 0.1 - fontSize) : primaryMargin;
        return new Rectangle(llx, lly, llx + w, lly + fontSize);
    }

    private void ApplyImageBand(byte[] imageBytes, bool top, float primaryMargin, float leftMargin, float rightMargin)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var isJpeg = imageBytes.Length >= 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8;
        foreach (var page in _document.Pages)
        {
            var stamp = isJpeg ? ImageStamp.FromJpeg(imageBytes) : ImageStamp.FromRgb(imageBytes, 100, 100);
            var w = stamp.DisplayWidth > 0 ? stamp.DisplayWidth : 100;
            var h = stamp.DisplayHeight > 0 ? stamp.DisplayHeight : 100;
            var pageW = page.MediaBox.Width;
            var pageH = page.MediaBox.Height;
            // Centre within the [leftMargin, pageW-rightMargin] span; footer box bottom at
            // the margin, header box top at pageH-margin.
            stamp.X = leftMargin + (pageW - leftMargin - rightMargin - w) / 2;
            stamp.Y = top ? (pageH - h - primaryMargin) : primaryMargin;
            stamp.MetaRect = new Rectangle(stamp.X, stamp.Y, stamp.X + w, stamp.Y + h);
            stamp.ApplyTo(page);
        }
    }

    private TextStamp BuildTextStamp(string text, FormattedText? source)
    {
        var stamp = new TextStamp(text) { StampId = StampId };
        if (source is not null)
        {
            stamp.FontSize = (float)source.FontSize;
            if (!string.IsNullOrEmpty(source.FontName))
                stamp.FontName = source.FontName;
            if (source.ForegroundColor is not null)
                stamp.Color = source.ForegroundColor;
        }
        return stamp;
    }

    private static (HorizontalAlignment h, VerticalAlignment v, bool top) ResolvePosition(int position) => position switch
    {
        PosUpperLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, true),
        PosUpperMiddle => (HorizontalAlignment.Center, VerticalAlignment.Top, true),
        PosUpperRight => (HorizontalAlignment.Right, VerticalAlignment.Top, true),
        PosSidesLeft => (HorizontalAlignment.Left, VerticalAlignment.Center, false),
        PosSidesRight => (HorizontalAlignment.Right, VerticalAlignment.Center, false),
        PosBottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom, false),
        PosBottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom, false),
        _ => (HorizontalAlignment.Center, VerticalAlignment.Bottom, false), // PosBottomMiddle (default)
    };

    private static string RenderPageNumberText(string formatString, int n, int total, NumberingStyle style)
    {
        var numStr = style switch
        {
            NumberingStyle.LowerAlpha => ToAlpha(n, false),
            NumberingStyle.UpperAlpha => ToAlpha(n, true),
            NumberingStyle.LowerRoman => ToRoman(n).ToLowerInvariant(),
            NumberingStyle.UpperRoman => ToRoman(n),
            NumberingStyle.None => "",
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, formatString, numStr, total);
        }
        catch (FormatException)
        {
            return numStr;
        }
    }

    private static string ToAlpha(int n, bool upper)
    {
        if (n <= 0) return "";
        var sb = new StringBuilder();
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)((upper ? 'A' : 'a') + (n % 26)));
            n /= 26;
        }
        return sb.ToString();
    }

    private static string ToRoman(int n)
    {
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var letters = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var sb = new StringBuilder();
        for (var i = 0; i < values.Length && n > 0; i++)
            while (n >= values[i]) { sb.Append(letters[i]); n -= values[i]; }
        return sb.ToString();
    }

    // ── Stateless API (existing) ──────────────────────────────────────────────

    /// <summary>
    /// Add a text stamp to all pages.
    /// </summary>
    public byte[] AddTextStamp(byte[] input, string text,
        HorizontalAlignment hAlign = HorizontalAlignment.Center,
        VerticalAlignment vAlign = VerticalAlignment.Bottom,
        double fontSize = 12, string fontName = "Helvetica")
    {
        using var doc = Document.Open(input);
        var stamp = new TextStamp(text)
        {
            FontSize = (float)fontSize,
            FontName = fontName,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add page numbers to all pages.
    /// </summary>
    public byte[] AddPageNumbers(byte[] input,
        string format = "Page {0} of {1}",
        HorizontalAlignment hAlign = HorizontalAlignment.Center,
        VerticalAlignment vAlign = VerticalAlignment.Bottom,
        double fontSize = 10)
    {
        using var doc = Document.Open(input);
        var stamp = new PageNumberStamp
        {
            Format = format,
            FontSize = fontSize,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
        };
        stamp.ApplyToAll(doc);
        return doc.ToArray();
    }

    /// <summary>
    /// Add a header text to all pages.
    /// </summary>
    public byte[] AddHeader(byte[] input, string text,
        double fontSize = 10, double margin = 36)
    {
        using var doc = Document.Open(input);
        var stamp = new TextStamp(text)
        {
            FontSize = (float)fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            YIndent = margin,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add a footer text to all pages.
    /// </summary>
    public byte[] AddFooter(byte[] input, string text,
        double fontSize = 10, double margin = 36)
    {
        using var doc = Document.Open(input);
        var stamp = new TextStamp(text)
        {
            FontSize = (float)fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            YIndent = margin,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add a watermark text to all pages.
    /// </summary>
    public byte[] AddWatermark(byte[] input, string text,
        double fontSize = 48, double rotation = 45, double opacity = 0.3)
    {
        using var doc = Document.Open(input);
        var stamp = new WatermarkStamp(text)
        {
            FontSize = fontSize,
            Rotate = rotation,
            Opacity = opacity,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add an RGB image stamp to all pages.
    /// </summary>
    public byte[] AddImageStamp(byte[] input, byte[] rgbPixels, int width, int height,
        double displayWidth = 0, double displayHeight = 0,
        double x = 100, double y = 100)
    {
        using var doc = Document.Open(input);
        var stamp = ImageStamp.FromRgb(rgbPixels, width, height);
        stamp.X = x;
        stamp.Y = y;
        stamp.DisplayWidth = displayWidth > 0 ? displayWidth : width;
        stamp.DisplayHeight = displayHeight > 0 ? displayHeight : height;

        foreach (var page in doc.Pages)
            stamp.ApplyTo(page);

        return doc.ToArray();
    }

    /// <summary>
    /// Add a grayscale image stamp to all pages.
    /// </summary>
    public byte[] AddGrayscaleImageStamp(byte[] input, byte[] grayPixels, int width, int height,
        double displayWidth = 0, double displayHeight = 0,
        double x = 100, double y = 100)
    {
        using var doc = Document.Open(input);
        var stamp = ImageStamp.FromGrayscale(grayPixels, width, height);
        stamp.X = x;
        stamp.Y = y;
        stamp.DisplayWidth = displayWidth > 0 ? displayWidth : width;
        stamp.DisplayHeight = displayHeight > 0 ? displayHeight : height;

        foreach (var page in doc.Pages)
            stamp.ApplyTo(page);

        return doc.ToArray();
    }
}
