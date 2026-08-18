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
        PruneOrphans();
        _document.Save(destFile);
    }

    // Stamping replaces a page's /Contents with a combined stream (existing + stamp),
    // leaving the original content stream orphaned in the xref. The default save serialises
    // every in-use entry, so the output keeps growing by the old content size on each stamp.
    // This pure reachability prune (RemoveUnusedObjects only) drops the orphans without
    // rewriting or recompressing live content; the shared stamp form stays reachable.
    private void PruneOrphans()
    {
        _document?.OptimizeResources(new Aspose.Pdf.Optimization.OptimizationOptions
        {
            RemoveUnusedObjects = true,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
    }

    /// <summary>
    /// Save the modified document to a stream.
    /// </summary>
    public void Save(Stream destStream)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        PruneOrphans();
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
        PruneOrphans();
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

        // A single PDF-page stamp instance is reused across every target page so its
        // imported source-page Form XObject is shared (imported once) rather than re-cloned
        // per page — see PdfPageStamp's per-document form cache.
        PdfPageStamp? pdfPageStamp = null;
        if (!stamp.IsTextStamp && stamp.LogoImage is null && stamp.PdfBytes is not null)
        {
            pdfPageStamp = new PdfPageStamp(new MemoryStream(stamp.PdfBytes), stamp.PdfPageNumber)
            {
                IsBackground = stamp.IsBackground,
                StampId = stamp.StampId,
                XIndent = stamp.XOrigin,
                YIndent = stamp.YOrigin,
                // Facade layout: the stamp form's fonts surface at page level
                // (Resources.Fonts["F1"]…) and the form's own /Font is emptied.
                PromoteFontsToPage = true,
            };
            if (stamp.ImageWidth > 0) pdfPageStamp.Width = stamp.ImageWidth;
            if (stamp.ImageHeight > 0) pdfPageStamp.Height = stamp.ImageHeight;
        }

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
            else if (pdfPageStamp is not null)
            {
                // PDF-page stamp (Stamp.BindPdf): draw the source page as a Form
                // XObject onto the target page, importing its resource graph.
                pdfPageStamp.ApplyTo(page);
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
        PruneOrphans();
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
        var fontName = string.IsNullOrEmpty(text.FontName) ? "Helvetica" : text.FontName;

        // Stamp bounds in page space. The logo box is TextWidth wide and ~1.1·FontSize tall
        // (a single line at default leading); a 90°/270° rotation swaps those dimensions. The
        // box is anchored at the stamp origin and grows +x/+y, per the GetStamps contract.
        double fontSize = text.FontSize;
        double boxW = text.TextWidth;
        double boxH = fontSize * 1.1;
        double rot = ((stamp.Rotation % 360f) + 360f) % 360f;
        bool quarterTurn = rot is 90f or 270f;
        double rectW = quarterTurn ? boxH : boxW;
        double rectH = quarterTurn ? boxW : boxH;
        double ox = stamp.XOrigin, oy = stamp.YOrigin;

        // Every line of the (possibly multi-line) FormattedText; .Text is only the first.
        var lines = new System.Collections.Generic.List<string>();
        foreach (var line in text.Lines) lines.Add(line.Text);
        if (lines.Count == 0) lines.Add(text.Text);

        // The stamp's drawing operators in local coordinates, first baseline at the
        // origin: an optional background box, the fill/stroke colour + render mode,
        // then one Tj per line. Shared by both the Form-XObject and inline paths.
        string DrawOps(string fontRes)
        {
            var b = new StringBuilder();
            if (!text.BackgroundColor.IsEmpty)
            {
                double descent = (Aspose.Pdf.Text.Standard14Fonts.IsStandard14(fontName)
                    ? Aspose.Pdf.Text.Standard14Fonts.GetDescent(fontName) : -207) * text.FontSize / 1000.0;
                b.Append($"{NormColor(text.BackgroundColor.R)} {NormColor(text.BackgroundColor.G)} {NormColor(text.BackgroundColor.B)} rg\n");
                b.Append($"0 {Format(descent)} {Format(text.TextWidth)} {Format(text.FontSize - descent)} re f\n");
            }
            if (ts?.ForegroundColor is { } fg)
                b.Append($"{NormColor(fg.R)} {NormColor(fg.G)} {NormColor(fg.B)} rg\n");
            else
                b.Append($"{NormColor(text.ForegroundColor.R)} {NormColor(text.ForegroundColor.G)} {NormColor(text.ForegroundColor.B)} rg\n");
            if (ts?.StrokingColor is { } sc)
                b.Append($"{NormColor(sc.R)} {NormColor(sc.G)} {NormColor(sc.B)} RG\n");
            if (ts is not null && (int)ts.RenderingMode != 0)
                b.Append($"{(int)ts.RenderingMode} Tr\n");
            b.Append($"BT /{fontRes} {Format(text.FontSize)} Tf 0 0 Td ");
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) b.Append($"0 {Format(-fontSize)} Td ");
                b.Append($"({EscapePdfString(lines[i])}) Tj ");
            }
            b.Append("ET\n");
            return b.ToString();
        }

        var sb = new StringBuilder();
        sb.Append($"%StampId={stamp.StampId}\n");
        sb.Append($"%StampRect={Format(ox)} {Format(oy)} {Format(ox + rectW)} {Format(oy + rectH)}\n");
        sb.Append("q\n");
        if (rot == 0f)
        {
            // Upright stamp: draw into a Form XObject so the text lands in
            // Resources.Forms; a Do at 1 0 0 1 ox oy is
            // pixel-identical to drawing there directly.
            var fmName = AddTextStampForm(page, DrawOps("F0"), fontName, boxW, fontSize, lines.Count);
            sb.Append($"1 0 0 1 {Format(ox)} {Format(oy)} cm\n");
            sb.Append($"/{fmName} Do\n");
        }
        else if (!stamp.IsBackground)
        {
            // Rotated FOREGROUND stamp: the exact operator sequence of the historical
            // inline placement — the rotation cm followed by the text ops — wrapped in
            // a Form XObject invoked at IDENTITY, so the stamp lands in Resources.Forms
            // (the public-API shape) while the renderer walks an identical operator
            // stream and the era-calibrated placement stays pixel-exact. The BBox spans
            // the page so nothing the inline form drew is clipped away.
            double radInline = rot * Math.PI / 180.0;
            double cosInline = Math.Cos(radInline), sinInline = Math.Sin(radInline);
            var inner = new StringBuilder();
            inner.Append($"{Format(cosInline)} {Format(sinInline)} {Format(-sinInline)} {Format(cosInline)} {Format(ox)} {Format(oy)} cm\n");
            inner.Append(DrawOps("F0"));
            var mboxFg = page.MediaBox;
            var fmNameFg = AddTextStampFormCore(page, inner.ToString(), fontName,
                mboxFg.LLX, mboxFg.LLY, mboxFg.URX, mboxFg.URY);
            sb.Append($"/{fmNameFg} Do\n");
        }
        else
        {
            // Rotated BACKGROUND stamp: the reference draws it through an UNROTATED
            // Form XObject whose BBox spans [0 0 max(TextWidth, pageWidth) pageHeight],
            // TextWidth being the real system-face advance sum (unrounded hmtx units,
            // e.g. Windows Arial for "Arial" — not the rounded Standard-14 AFM). The
            // rotation lives in the page-level cm, translated so the rotated block
            // rect [0,W]×[0,(N+0.1)·S] stays in the first quadrant, and the text
            // baseline inside the form is lifted by the font descent.
            double realW = 0;
            foreach (var line in lines)
                realW = Math.Max(realW, MeasureSystemFaceWidth(line, text, fontSize));
            var mbox = page.MediaBox;
            double bboxW = Math.Max(realW, mbox.Width);
            double bboxH = mbox.Height;

            var descent = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(fontName)
                ? Aspose.Pdf.Text.Standard14Fonts.GetDescent(fontName)
                : Aspose.Pdf.Text.Standard14Fonts.GetDescent("Helvetica");
            var lift = (descent < 0 ? -descent : 207) * fontSize / 1000.0;

            var fg = ts?.ForegroundColor ?? text.ForegroundColor;
            var fb = new StringBuilder();
            fb.Append("q\n0 0 0 0 re\n0 0 0 rg\n0 0 0 RG\nf*\nq\n");
            fb.Append($"BT\n/F0 {Format(fontSize)} Tf\n");
            fb.Append($"{NormColor(fg.R)} {NormColor(fg.G)} {NormColor(fg.B)} rg\n");
            if (ts?.StrokingColor is { } strokeCol)
                fb.Append($"{NormColor(strokeCol.R)} {NormColor(strokeCol.G)} {NormColor(strokeCol.B)} RG\n");
            if (ts is not null && (int)ts.RenderingMode != 0)
                fb.Append($"{(int)ts.RenderingMode} Tr\n");
            for (int i = 0; i < lines.Count; i++)
            {
                var lineY = lift + (lines.Count - 1 - i) * fontSize;
                fb.Append($"1 0 0 1 0 {Format(lineY)} Tm\n({EscapePdfString(lines[i])}) Tj\n");
            }
            fb.Append("0 g\n1 0 0 1 0 0 Tm\nET\nQ\nQ\n");

            var fmName = AddTextStampFormWithBBox(page, fb.ToString(), fontName, bboxW, bboxH);

            double rad = rot * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            // Shift the rotated block rect into the first quadrant: e/f undo the
            // most-negative rotated corner of [0,realW]×[0,blockH].
            double blockH = (lines.Count + 0.1) * fontSize;
            double minX = Math.Min(Math.Min(0, realW * cos),
                          Math.Min(-blockH * sin, realW * cos - blockH * sin));
            double minY = Math.Min(Math.Min(0, realW * sin),
                          Math.Min(blockH * cos, realW * sin + blockH * cos));
            if (stamp.Opacity < 1f)
            {
                var gsName = page.AddExtGState(new Content.ExtGState
                {
                    FillAlpha = stamp.Opacity,
                });
                sb.Append($"/{gsName} gs\n");
            }
            sb.Append($"{Format(cos)} {Format(sin)} {Format(-sin)} {Format(cos)} {Format(ox - minX)} {Format(oy - minY)} cm\n");
            sb.Append($"/{fmName} Do\n");
        }
        sb.Append("Q\n");
        AppendContent(page, Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>Advance width of <paramref name="line"/> at <paramref name="fontSize"/>
    /// measured with the REAL system face the caller asked for (unrounded hmtx units,
    /// e.g. Windows Arial for "Arial"), matching the public facade
    /// TextWidth model. Falls back to the FormattedText's AFM-based TextWidth when no
    /// TrueType face resolves.</summary>
    private static double MeasureSystemFaceWidth(string line, FormattedText text, double fontSize)
    {
        var faceName = text.RequestedFontName ?? text.FontName;
        if (!string.IsNullOrEmpty(faceName))
        {
            var ttf = Aspose.Pdf.Text.FontRepository.GetTtfData(faceName);
            if (ttf is not null)
            {
                var (widths, upm) = Aspose.Pdf.Text.FontRepository.ReadTtfRawMetrics(ttf);
                if (upm > 0 && widths is { Length: >= 256 })
                {
                    double total = 0;
                    foreach (var ch in line)
                        total += widths[ch < 256 ? ch : '?'];
                    return total * fontSize / upm;
                }
            }
        }
        return text.TextWidth;
    }

    /// <summary>Variant of <see cref="AddTextStampForm"/> with an explicit
    /// [0 0 <paramref name="bboxW"/> <paramref name="bboxH"/>] BBox (the rotated facade
    /// stamp's page-sized form).</summary>
    private static string AddTextStampFormWithBBox(Page page, string content, string fontName,
        double bboxW, double bboxH)
    {
        return AddTextStampFormCore(page, content, fontName, 0, 0, bboxW, bboxH);
    }

    /// <summary>Create a Form XObject holding a text stamp's <paramref name="content"/>
    /// (with its own /Resources/Font/F0), register it on the document, add it to the
    /// page's /Resources/XObject under a fresh Fm{n} name and return that name.</summary>
    private static string AddTextStampForm(Page page, string content, string fontName,
        double boxW, double fontSize, int lineCount)
    {
        return AddTextStampFormCore(page, content, fontName,
            -fontSize, -lineCount * fontSize - fontSize, boxW + fontSize, fontSize * 1.5);
    }

    private static string AddTextStampFormCore(Page page, string content, string fontName,
        double llx, double lly, double urx, double ury)
    {
        var reader = page.Reader;
        var doc = reader.OwnerDocument!;

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(fontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        var fontDict = new PdfDictionary();
        fontDict.Set("F0", font);
        var res = new PdfDictionary();
        res.Set("Font", fontDict);

        var xdict = new PdfDictionary();
        xdict.Set("Type", new PdfName("XObject"));
        xdict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(llx));
        bbox.Add(new PdfReal(lly));
        bbox.Add(new PdfReal(urx));
        bbox.Add(new PdfReal(ury));
        xdict.Set("BBox", bbox);
        xdict.Set("Resources", res);
        var bytes = Encoding.ASCII.GetBytes(content);
        xdict.Set("Length", new PdfInteger(bytes.Length));
        var stream = new PdfStream(xdict, bytes);

        var objNum = doc.AllocateObjectNumber();
        doc.AddNewObject(objNum, stream);

        // A page frequently inherits /Resources from the /Pages tree. Setting a fresh
        // empty dict on such a page would SHADOW the inherited fonts/shadings out of
        // rendering — seed the page-local copy with the inherited entries instead
        // (same contract as EnsureFont below).
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
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
            page.Dict.Set("Resources", resources);
            resourcesWereInherited = true;
        }
        var xobjs = reader.ResolveDict(resources.Get("XObject")) ?? resources.Get("XObject") as PdfDictionary;
        if (xobjs is null) { xobjs = new PdfDictionary(); resources.Set("XObject", xobjs); }
        else if (resourcesWereInherited)
        {
            // The /XObject dict came from the inherited resources and is shared with
            // sibling pages — copy it page-local so the stamp form does not leak into
            // (or collide on) their inherited resources.
            var localXobjs = new PdfDictionary();
            foreach (var key in xobjs.Keys)
            {
                var v = xobjs.Get(key);
                if (v is not null) localXobjs.Set(key, v);
            }
            xobjs = localXobjs;
            resources.Set("XObject", xobjs);
        }
        int n = 0;
        while (xobjs.ContainsKey($"Fm{n}")) n++;
        var name = $"Fm{n}";
        xobjs.Set(name, new PdfIndirectRef(objNum, 0));
        return name;
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
        else if (existing is PdfArray)
        {
            // /Contents is already an array — typically because an earlier stamp
            // (e.g. an image stamp) added its own content stream. Append this stamp
            // as a new array entry so the earlier stamps and the page's original
            // content are preserved; replacing the array would drop them.
            page.AddContentStream(contentBytes);
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
        // FormattedText.Text is only the first line; a header/footer built from a
        // multi-line FormattedText (AddNewLineText) must carry every line so the
        // stamp renders each as its own row.
        var bandText = string.Join("\n", formattedText.Lines.Select(l => l.Text));
        foreach (var page in _document.Pages)
        {
            var stamp = BuildTextStamp(bandText, formattedText);
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
        // Auto-detect JPEG vs PNG vs (Windows) GDI-decodable formats by header bytes,
        // falling back to raw RGB only for genuinely raw pixel payloads. A header/footer
        // image is normally an encoded PNG/JPEG file, never a raw width×height×3 buffer.
        var isJpeg = imageBytes.Length >= 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8;
        var isPng = imageBytes.Length >= 8 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50
                    && imageBytes[2] == 0x4E && imageBytes[3] == 0x47;
        foreach (var page in _document.Pages)
        {
            ImageStamp stamp;
            if (isJpeg) stamp = ImageStamp.FromJpeg(imageBytes);
            else if (isPng) stamp = ImageStamp.FromPngData(imageBytes);
            else if (OperatingSystem.IsWindows()
                     && ImageStamp.TryFromGdiPlusDecoder(imageBytes) is { } gdiStamp)
                stamp = gdiStamp;
            else stamp = ImageStamp.FromRgb(imageBytes, 100, 100);
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
            // TextState is the effective source of font/size at render time (its
            // defaults win over the bare stamp properties), so the FormattedText's
            // size and font must land there too, not only on FontSize/FontName.
            stamp.FontSize = (float)source.FontSize;
            stamp.TextState.FontSize = (float)source.FontSize;
            if (!string.IsNullOrEmpty(source.FontName))
            {
                stamp.FontName = source.FontName;
                stamp.TextState.FontName = source.FontName;
            }
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
