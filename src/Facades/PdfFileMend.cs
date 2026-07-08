using System.Globalization;
using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Word wrap mode for PdfFileMend text operations.
/// </summary>
public enum WordWrapMode
{
    /// <summary>Default word wrapping.</summary>
    Default,
    /// <summary>Wrap by words (no mid-word breaks).</summary>
    ByWords,
}

/// <summary>
/// Text positioning mode for PdfFileMend.
/// </summary>
public enum PositioningMode
{
    /// <summary>Legacy line spacing mode.</summary>
    Legacy,
    /// <summary>Modern line spacing mode.</summary>
    ModernLineSpacing,
    /// <summary>Use the document's current positioning mode.</summary>
    Current,
}

/// <summary>
/// Facade for adding text and images to existing PDF documents.
/// </summary>
public sealed class PdfFileMend : ISaveableFacade
{
    void IDisposable.Dispose() => Close();

    private Document? _document;
    private string? _inputFile;
    private string? _outputFile;
    private Stream? _inputStream;
    private Stream? _outputStream;
    private bool _ownsDocument;

    private bool _isWordWrap;

    /// <summary>
    /// Whether to enable word wrapping for AddText operations. Set-only on
    /// the public surface to match Aspose.Pdf; internal code reads
    /// <see cref="WrapMode"/> for the resolved behaviour.
    /// </summary>
    public bool IsWordWrap { set => _isWordWrap = value; }

    /// <summary>
    /// Word wrap mode (Default or ByWords).
    /// </summary>
    public WordWrapMode WrapMode { get; set; }

    /// <summary>
    /// Text positioning mode.
    /// </summary>
    public PositioningMode TextPositioningMode { get; set; }

    /// <summary>
    /// Input file path.
    /// </summary>
    public string? InputFile
    {
        get => _inputFile;
        set
        {
            _inputFile = value;
            if (value is not null && _document is null)
                LoadFromFile(value);
        }
    }

    /// <summary>
    /// Output file path.
    /// </summary>
    public string? OutputFile
    {
        get => _outputFile;
        set => _outputFile = value;
    }

    /// <summary>
    /// Input stream.
    /// </summary>
    public Stream? InputStream
    {
        get => _inputStream;
        set
        {
            _inputStream = value;
            if (value is not null && _document is null)
                LoadFromStream(value);
        }
    }

    /// <summary>
    /// Output stream.
    /// </summary>
    public Stream? OutputStream
    {
        get => _outputStream;
        set => _outputStream = value;
    }

    /// <summary>
    /// The document bound to this PdfFileMend, exposing the in-progress
    /// result so it can be chained into another facade.
    /// </summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>
    /// Default constructor.
    /// </summary>
    public PdfFileMend()
    {
    }

    /// <summary>
    /// Create a PdfFileMend from input/output file paths.
    /// </summary>
    public PdfFileMend(string inputFileName, string outputFileName)
    {
        _inputFile = inputFileName;
        _outputFile = outputFileName;
        LoadFromFile(inputFileName);
    }

    /// <summary>
    /// Create a PdfFileMend from input/output streams.
    /// </summary>
    public PdfFileMend(Stream inputStream, Stream outputStream)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        LoadFromStream(inputStream);
    }

    /// <summary>Bind a pre-loaded <see cref="Document"/>.</summary>
    public PdfFileMend(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDocument = false;
    }

    /// <summary>Bind a document and pre-set the destination file path.</summary>
    public PdfFileMend(Document document, string outputFileName)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _outputFile = outputFileName;
        _ownsDocument = false;
    }

    /// <summary>Bind a document and pre-set the destination stream.</summary>
    public PdfFileMend(Document document, Stream destStream)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _outputStream = destStream;
        _ownsDocument = false;
    }

    /// <summary>
    /// Bind an existing Document to this PdfFileMend.
    /// </summary>
    public void BindPdf(Document document)
    {
        _document = document;
        _ownsDocument = false;
    }

    /// <summary>
    /// Bind a PDF document from a file path.
    /// </summary>
    public void BindPdf(string inputFile)
    {
        _inputFile = inputFile;
        LoadFromFile(inputFile);
    }

    /// <summary>
    /// Bind a PDF document from a stream.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        LoadFromStream(inputStream);
    }

    /// <summary>
    /// Add formatted text to a specific page at the given lower-left position.
    /// </summary>
    public bool AddText(FormattedText text, int pageNum, float lowerLeftX, float lowerLeftY)
    {
        return AddText(text, pageNum, lowerLeftX, lowerLeftY, 0, 0);
    }

    /// <summary>
    /// Add formatted text to a specific page within the given rectangle.
    /// </summary>
    public bool AddText(FormattedText text, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        if (pageNum < 1 || pageNum > _document.PageCount)
            return false;

        var page = _document.Pages.At(pageNum);
        AddTextToPage(page, text, lowerLeftX, lowerLeftY, upperRightX, upperRightY);
        return true;
    }

    /// <summary>
    /// Add formatted text to multiple pages within the given rectangle.
    /// </summary>
    public bool AddText(FormattedText text, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        foreach (var pn in pageNums)
        {
            if (pn < 1 || pn > _document.PageCount) continue;
            var page = _document.Pages.At(pn);
            AddTextToPage(page, text, lowerLeftX, lowerLeftY, upperRightX, upperRightY);
        }
        return true;
    }

    /// <summary>
    /// Add an image from a stream to a specific page.
    /// </summary>
    public bool AddImage(Stream imageStream, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageStream, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(Stream imageStream, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        if (pageNum < 1 || pageNum > _document.PageCount)
            return false;

        using var ms = new MemoryStream();
        imageStream.Position = 0;
        imageStream.CopyTo(ms);
        var imageData = ms.ToArray();

        var page = _document.Pages.At(pageNum);
        AddImageToPage(page, imageData, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
        return true;
    }

    /// <summary>
    /// Add an image from a file path to a specific page.
    /// </summary>
    public bool AddImage(string imageName, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageName, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(string imageName, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        var imageData = File.ReadAllBytes(imageName);
        return AddImage(new MemoryStream(imageData), pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
    }

    /// <summary>
    /// Add an image from a stream to multiple pages.
    /// </summary>
    public bool AddImage(Stream imageStream, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageStream, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(Stream imageStream, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        using var ms = new MemoryStream();
        imageStream.Position = 0;
        imageStream.CopyTo(ms);
        var imageData = ms.ToArray();

        foreach (var pn in pageNums)
        {
            if (pn < 1 || pn > _document.PageCount) continue;
            var page = _document.Pages.At(pn);
            AddImageToPage(page, imageData, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
        }
        return true;
    }

    /// <summary>
    /// Add an image from a file path to multiple pages.
    /// </summary>
    public bool AddImage(string imageName, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageName, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(string imageName, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        var imageData = File.ReadAllBytes(imageName);
        return AddImage(new MemoryStream(imageData), pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
    }

    /// <summary>
    /// Add an image with compositing parameters (blend mode). Stream variant.
    /// </summary>
    public bool AddImage(Stream imageStream, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageStream, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Add an image with compositing parameters (blend mode). File variant.
    /// </summary>
    public bool AddImage(string imageName, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageName, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Add an image with compositing parameters to multiple pages. Stream variant.
    /// </summary>
    public bool AddImage(Stream imageStream, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageStream, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Add an image with compositing parameters to multiple pages. File variant.
    /// </summary>
    public bool AddImage(string imageName, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageName, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Save the modified document to a specific file path.
    /// </summary>
    public void Save(string destFile)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");
        _document.Save(destFile);
    }

    /// <summary>
    /// Save the modified document to a stream.
    /// </summary>
    public void Save(Stream destStream)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");
        _document.Save(destStream);
    }

    /// <summary>
    /// Close the document and save to the output file/stream.
    /// </summary>
    public void Close()
    {
        if (_document is null) return;

        if (_outputFile is not null)
        {
            _document.Save(_outputFile);
        }
        else if (_outputStream is not null)
        {
            _document.Save(_outputStream);
        }

        if (_ownsDocument)
            _document.Dispose();
        _document = null;
    }

    // ── Private implementation ──────────────────────────────────────────────

    private void LoadFromFile(string path)
    {
        _document = Document.Open(path);
        _ownsDocument = true;
    }

    private void LoadFromStream(Stream stream)
    {
        _document = Document.Open(stream);
        _ownsDocument = true;
    }

    private void AddTextToPage(Page page, FormattedText ft, float llx, float lly, float urx, float ury)
    {
        var sb = new StringBuilder();
        sb.Append("q\n");

        // Register font in page resources. CustomFontFile takes precedence so a
        // .ttf path supplied via FormattedText is actually embedded; otherwise
        // EnsureFont falls back to a Type1 base font by name.
        string fontName;
        if (!string.IsNullOrEmpty(ft.CustomFontFile) && File.Exists(ft.CustomFontFile))
        {
            var resourceName = AllocateFontResourceName(page);
            var embedder = FontEmbedder.EmbedFromFile(_document!, ft.CustomFontFile, resourceName);
            embedder.AddToPage(page);
            fontName = resourceName;
        }
        else
        {
            fontName = EnsureFont(page, ft.FontName);
        }

        // Baseline position: the point overload places the first baseline at
        // lly + 0.3*fontSize + |descent|, the rectangle overload at ~lly + 0.5*fontSize.
        // descentPt is negative for typical fonts.
        var metricsFont = string.IsNullOrEmpty(ft.FontName) ? "Helvetica" : ft.FontName!;
        double descentPt = (Standard14Fonts.IsStandard14(metricsFont)
            ? Standard14Fonts.GetDescent(metricsFont) : -207) * ft.FontSize / 1000.0;
        double startY = (ury > 0 && ury > lly)
            ? lly + ft.FontSize * 0.5
            : lly + ft.FontSize * 0.3 - descentPt;

        // Clamp to page bounds so off-page coordinates don't silently render nothing.
        var pageMediaBox = page.MediaBox;
        var pageHeight = pageMediaBox.URY - pageMediaBox.LLY;
        if (startY > pageHeight - ft.FontSize)
            startY = pageHeight - ft.FontSize;

        // Emit the background-colour fill behind the text (before the glyphs so it renders
        // underneath). Fills a FULL-PAGE-WIDTH bar per line: bottom at
        // baseline + descent − 0.2*fontSize, height 1.325*fontSize (the glyph
        // box plus padding). This makes PdfFileMend honour FormattedText.BackgroundColor.
        if (!ft.BackgroundColor.IsEmpty)
        {
            var bg = ft.BackgroundColor;
            double pad = ft.FontSize * 0.2;
            double bgH = ft.FontSize * 1.325;
            double bgX = pageMediaBox.LLX;
            double bgW = pageMediaBox.URX - pageMediaBox.LLX;
            double baseline = startY;
            for (var i = 0; i < ft.Lines.Count; i++)
            {
                if (i > 0)
                {
                    var extra = ft.Lines[i - 1].LineSpacing;
                    baseline -= ft.DefaultLineSpacing + (extra > 0 ? extra : 0);
                }
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{0:F3} {1:F3} {2:F3} rg\n{3:F2} {4:F2} {5:F2} {6:F2} re\nf\n",
                    bg.R / 255.0, bg.G / 255.0, bg.B / 255.0,
                    bgX, baseline + descentPt - pad, bgW, bgH);
            }
        }

        // Set foreground color
        var fg = ft.ForegroundColor;
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{0:F3} {1:F3} {2:F3} rg\n",
            fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);

        sb.Append("BT\n");
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "/{0} {1:G} Tf\n", fontName, ft.FontSize);

        // Position first line. The text x is offset by 0.2*fontSize from llx.
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{0:F2} {1:F2} Td\n", (double)llx + ft.FontSize * 0.2, startY);

        // Emit first line
        sb.AppendFormat("({0}) Tj\n", EscapePdfString(ft.Lines[0].Text));

        // Emit subsequent lines with TL (text leading) and T* (next line). A line's
        // /lineSpacing is extra leading applied AFTER it (before the next line), so the
        // baseline-to-baseline pitch into line i is the default line height plus the
        // PREVIOUS line's spacing — matching FormattedText.AddNewLineText semantics.
        for (var i = 1; i < ft.Lines.Count; i++)
        {
            var line = ft.Lines[i];
            var extra = ft.Lines[i - 1].LineSpacing;
            var leading = ft.DefaultLineSpacing + (extra > 0 ? extra : 0);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{0:F2} TL\n", leading);
            sb.Append("T*\n");
            sb.AppendFormat("({0}) Tj\n", EscapePdfString(line.Text));
        }

        sb.Append("ET\n");
        sb.Append("Q\n");

        // The font is declared /WinAnsiEncoding (see AddOrGetFont), so the glyph
        // bytes must follow Windows-1252, not Latin-1. They differ in 0x80-0x9F,
        // where WinAnsi carries the "smart" punctuation/symbols (' " – — ™ …).
        // Encoding the stream with Latin-1 turned those into '?' and lost the text;
        // Cp1252 maps them to their real bytes so extraction round-trips. All other
        // code points ≤ 0xFF (and the ASCII operators) encode identically.
        AppendContent(page, Cp1252.GetBytes(sb.ToString()));
    }

    private void AddImageToPage(Page page, byte[] imageData, float llx, float lly, float urx, float ury,
        BlendMode blend = BlendMode.Normal)
    {
        var width = urx - llx;
        var height = ury - lly;
        var rect = new Rectangle(llx, lly, urx, ury);
        var blendName = blend == BlendMode.Normal ? null : blend.ToString();

        // Detect image format and create appropriate stamp
        if (IsJpeg(imageData))
        {
            var stamp = ImageStamp.FromJpeg(imageData);
            stamp.X = llx;
            stamp.Y = lly;
            stamp.DisplayWidth = width;
            stamp.DisplayHeight = height;
            stamp.BlendMode = blendName;
            stamp.ApplyTo(page);
        }
        else if (IsPng(imageData))
        {
            var (pixels, imgW, imgH, hasAlpha) = DecodePng(imageData);
            ImageStamp stamp;
            if (hasAlpha)
            {
                // Separate RGB and Alpha channels
                var rgb = new byte[imgW * imgH * 3];
                var alpha = new byte[imgW * imgH];
                for (var i = 0; i < imgW * imgH; i++)
                {
                    rgb[i * 3] = pixels[i * 4];
                    rgb[i * 3 + 1] = pixels[i * 4 + 1];
                    rgb[i * 3 + 2] = pixels[i * 4 + 2];
                    alpha[i] = pixels[i * 4 + 3];
                }
                // Embed the RGB and attach the alpha channel as a DeviceGray /SMask
                // so the source's transparency is honoured (a transparent PNG must
                // show the page behind it, not paint an opaque box over it).
                stamp = ImageStamp.FromRgb(rgb, imgW, imgH);
                stamp.SetAlphaMask(alpha);
            }
            else
            {
                stamp = ImageStamp.FromRgb(pixels, imgW, imgH);
            }
            stamp.X = llx;
            stamp.Y = lly;
            stamp.DisplayWidth = width;
            stamp.DisplayHeight = height;
            stamp.BlendMode = blendName;
            stamp.ApplyTo(page);
        }
        else
        {
            // Try to treat as raw image data — use AddImage on Page
            page.AddImage(imageData, rect);
        }
    }

    private static string AllocateFontResourceName(Page page)
    {
        var pageDict = page.Dict;
        var resources = pageDict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(pageDict.Get("Resources"));
        var fontDict = resources is null ? null
            : (resources.Get("Font") as PdfDictionary
                ?? page.Reader.ResolveDict(resources.Get("Font")));
        var existingCount = fontDict is null ? 0 : fontDict.Keys.Count();
        return $"F{existingCount}";
    }

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
            // sibling pages — copy it page-local so the new font does not leak into
            // (or collide on) their inherited resources.
            var localFonts = new PdfDictionary();
            foreach (var key in fontDict.Keys)
            {
                var v = fontDict.Get(key);
                if (v is not null) localFonts.Set(key, v);
            }
            fontDict = localFonts;
            resources.Set("Font", fontDict);
        }

        // Check if font already exists
        var count = 0;
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

        // Create new font entry (Type1 base font)
        var pdfFontName = $"F{count}";
        var newFont = new PdfDictionary();
        newFont.Set("Type", new PdfName("Font"));
        newFont.Set("Subtype", new PdfName("Type1"));
        newFont.Set("BaseFont", new PdfName(fontName));
        // For Latin text, use WinAnsiEncoding
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

    private static void AppendContent(Page page, byte[] contentBytes)
    {
        // Wrap the original page content in q...Q so any persistent CTM (a cm
        // operator at the top of the original stream that lacks a closing Q)
        // doesn't leak into the appended drawing. Otherwise the new text gets
        // drawn in the original stream's transformed user space.
        WrapExistingContentInSaveRestore(page);
        page.AddContentStream(contentBytes);
    }

    private static void WrapExistingContentInSaveRestore(Page page)
    {
        var existing = page.Reader.Resolve(page.Dict.Get("Contents"));
        if (existing is PdfStream stream)
        {
            var data = page.Reader.DecodeStream(stream);
            var wrapped = new byte[data.Length + 4];
            wrapped[0] = (byte)'q';
            wrapped[1] = (byte)'\n';
            data.CopyTo(wrapped, 2);
            wrapped[wrapped.Length - 2] = (byte)'\n';
            wrapped[wrapped.Length - 1] = (byte)'Q';
            stream.Dict.Remove("Filter");
            stream.Dict.Remove("DecodeParms");
            stream.ReplaceData(wrapped);
        }
        else if (existing is PdfArray arr && arr.Count > 0)
        {
            // Prepend q to first stream, append Q to last stream — preserves
            // intermediate boundaries and indirect refs.
            if (page.Reader.ResolveStream(arr[0]) is PdfStream first)
            {
                var data = page.Reader.DecodeStream(first);
                var wrapped = new byte[data.Length + 2];
                wrapped[0] = (byte)'q';
                wrapped[1] = (byte)'\n';
                data.CopyTo(wrapped, 2);
                first.Dict.Remove("Filter");
                first.Dict.Remove("DecodeParms");
                first.ReplaceData(wrapped);
            }
            if (page.Reader.ResolveStream(arr[arr.Count - 1]) is PdfStream last)
            {
                var data = page.Reader.DecodeStream(last);
                var wrapped = new byte[data.Length + 2];
                data.CopyTo(wrapped, 0);
                wrapped[wrapped.Length - 2] = (byte)'\n';
                wrapped[wrapped.Length - 1] = (byte)'Q';
                last.Dict.Remove("Filter");
                last.Dict.Remove("DecodeParms");
                last.ReplaceData(wrapped);
            }
        }
    }

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private static bool IsPng(byte[] data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    /// <summary>
    /// Minimal PNG decoder. Returns raw pixel data (RGB or RGBA), width, height, and hasAlpha flag.
    /// </summary>
    internal static (byte[] pixels, int width, int height, bool hasAlpha) DecodePng(byte[] png)
    {
        var pos = 8; // skip signature
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        byte[]? palette = null;   // PLTE: RGB triples, one per index (colorType 3)
        byte[]? trns = null;      // tRNS: alpha per palette index (optional)
        var idatData = new MemoryStream();

        while (pos < png.Length - 4)
        {
            var chunkLen = ReadInt32BE(png, pos);
            var chunkType = Encoding.ASCII.GetString(png, pos + 4, 4);
            var dataStart = pos + 8;

            if (chunkType == "IHDR")
            {
                width = ReadInt32BE(png, dataStart);
                height = ReadInt32BE(png, dataStart + 4);
                bitDepth = png[dataStart + 8];
                colorType = png[dataStart + 9];
            }
            else if (chunkType == "PLTE")
            {
                palette = new byte[chunkLen];
                Array.Copy(png, dataStart, palette, 0, chunkLen);
            }
            else if (chunkType == "tRNS")
            {
                trns = new byte[chunkLen];
                Array.Copy(png, dataStart, trns, 0, chunkLen);
            }
            else if (chunkType == "IDAT")
            {
                idatData.Write(png, dataStart, chunkLen);
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            pos = dataStart + chunkLen + 4; // +4 for CRC
        }

        if (width == 0 || height == 0)
            throw new ArgumentException("Invalid PNG: could not read IHDR");

        // Decompress IDAT data (deflate inside zlib wrapper)
        var compressedData = idatData.ToArray();
        byte[] decompressed;
        using (var compMs = new MemoryStream(compressedData))
        using (var zlib = new ZLibStream(compMs, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            zlib.CopyTo(outMs);
            decompressed = outMs.ToArray();
        }

        var hasAlpha = colorType == 4 || colorType == 6;
        var channels = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            3 => 1, // Palette index
            4 => 2, // Grayscale + Alpha
            6 => 4, // RGBA
            _ => 3,
        };
        // Bytes per pixel used by the row filters is ceil(bitsPerPixel/8), min 1
        // (PNG spec §9.2). Stride is the packed scanline length; for 8/16-bit this
        // equals width*channels*(bitDepth/8) as before, and it also handles the
        // sub-byte (1/2/4-bit) palette/grayscale case.
        var bpp = Math.Max(1, channels * bitDepth / 8);
        var stride = (width * channels * bitDepth + 7) / 8;

        // Unfilter scanlines
        var raw = new byte[height * stride];
        var prevRow = new byte[stride];
        var srcPos = 0;

        for (var y = 0; y < height; y++)
        {
            var filterByte = decompressed[srcPos++];
            var rowStart = y * stride;

            Array.Copy(decompressed, srcPos, raw, rowStart, stride);
            srcPos += stride;

            switch (filterByte)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (var x = bpp; x < stride; x++)
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + raw[rowStart + x - bpp]);
                    break;
                case 2: // Up
                    for (var x = 0; x < stride; x++)
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + prevRow[x]);
                    break;
                case 3: // Average
                    for (var x = 0; x < stride; x++)
                    {
                        var a = x >= bpp ? raw[rowStart + x - bpp] : 0;
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + (a + prevRow[x]) / 2);
                    }
                    break;
                case 4: // Paeth
                    for (var x = 0; x < stride; x++)
                    {
                        var a = x >= bpp ? raw[rowStart + x - bpp] : 0;
                        var b = prevRow[x];
                        var c = x >= bpp ? prevRow[x - bpp] : 0;
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + PaethPredictor(a, b, c));
                    }
                    break;
            }

            Array.Copy(raw, rowStart, prevRow, 0, stride);
        }

        // Convert to RGB or RGBA
        if (colorType == 3 && palette is not null) // Palette index -> RGB (RGBA when tRNS present)
        {
            var pixelCount = width * height;
            var indices = UnpackIndices(raw, width, height, stride, bitDepth);
            byte R(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p] : (byte)0; }
            byte G(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p + 1] : (byte)0; }
            byte B(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p + 2] : (byte)0; }
            if (trns is not null)
            {
                var rgba = new byte[pixelCount * 4];
                for (var i = 0; i < pixelCount; i++)
                {
                    var idx = indices[i];
                    rgba[i * 4] = R(idx); rgba[i * 4 + 1] = G(idx); rgba[i * 4 + 2] = B(idx);
                    rgba[i * 4 + 3] = idx < trns.Length ? trns[idx] : (byte)255;
                }
                return (rgba, width, height, true);
            }
            var rgb3 = new byte[pixelCount * 3];
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = indices[i];
                rgb3[i * 3] = R(idx); rgb3[i * 3 + 1] = G(idx); rgb3[i * 3 + 2] = B(idx);
            }
            return (rgb3, width, height, false);
        }
        if (colorType == 0) // Grayscale -> RGB
        {
            var rgb = new byte[width * height * 3];
            for (var i = 0; i < width * height; i++)
            {
                rgb[i * 3] = raw[i];
                rgb[i * 3 + 1] = raw[i];
                rgb[i * 3 + 2] = raw[i];
            }
            return (rgb, width, height, false);
        }
        else if (colorType == 4) // Grayscale+Alpha -> RGBA
        {
            var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                rgba[i * 4] = raw[i * 2];
                rgba[i * 4 + 1] = raw[i * 2];
                rgba[i * 4 + 2] = raw[i * 2];
                rgba[i * 4 + 3] = raw[i * 2 + 1];
            }
            return (rgba, width, height, true);
        }

        // colorType 2 (RGB) or 6 (RGBA) — already in correct format
        return (raw, width, height, hasAlpha);
    }

    /// <summary>Unpack one palette/grayscale index per pixel from filtered scanlines,
    /// handling packed sub-byte depths (1/2/4-bit, MSB-first) as well as 8-bit.</summary>
    private static byte[] UnpackIndices(byte[] raw, int width, int height, int stride, int bitDepth)
    {
        var outp = new byte[width * height];
        if (bitDepth == 8)
        {
            for (var y = 0; y < height; y++)
                Array.Copy(raw, y * stride, outp, y * width, width);
            return outp;
        }
        var mask = (1 << bitDepth) - 1;
        for (var y = 0; y < height; y++)
        {
            var rowBase = y * stride;
            for (var x = 0; x < width; x++)
            {
                var bit = x * bitDepth;
                var shift = 8 - bitDepth - (bit % 8);
                outp[y * width + x] = (byte)((raw[rowBase + bit / 8] >> shift) & mask);
            }
        }
        return outp;
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static int ReadInt32BE(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}

// CompositingParameters / BlendMode / ImageFilterType moved to top-level
// Aspose.Pdf namespace (src/CompositingParameters.cs) so they match the
// reflection signature used by PdfFileMend.AddImage(...).
