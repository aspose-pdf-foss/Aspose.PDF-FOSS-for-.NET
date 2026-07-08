namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into TIFF image format.
/// Supports single-page and multi-page TIFF output.
/// </summary>
public sealed class TiffDevice : ImageDevice
{
    /// <summary>TIFF encoding settings (color depth, compression).</summary>
    public TiffSettings Settings { get; }

    public TiffDevice(IPageRenderer renderer) : base(renderer)
    {
        Settings = new TiffSettings();
    }

    public TiffDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution)
    {
        Settings = new TiffSettings();
    }

    public TiffDevice() : base()
    {
        Settings = new TiffSettings();
    }

    public TiffDevice(Resolution resolution) : base(resolution)
    {
        Settings = new TiffSettings();
    }

    // ── Constructors with TiffSettings ────────────────────────────────────────

    /// <summary>Creates a TiffDevice with the specified settings and default resolution.</summary>
    public TiffDevice(TiffSettings settings) : base(new SoftwarePageRenderer())
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with the specified settings and converter.</summary>
    public TiffDevice(TiffSettings settings, IIndexBitmapConverter converter) : base(new SoftwarePageRenderer())
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with the given target dimensions and default settings.</summary>
    public TiffDevice(int width, int height) : base(width, height)
    {
        Settings = new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with width, height, resolution, and default settings.</summary>
    public TiffDevice(int width, int height, Resolution resolution) : base(width, height, resolution)
    {
        Settings = new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with width, height, and settings.</summary>
    public TiffDevice(int width, int height, TiffSettings settings) : base(width, height)
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with width, height, settings, and converter.</summary>
    public TiffDevice(int width, int height, TiffSettings settings, IIndexBitmapConverter converter) : base(width, height)
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with width, height, resolution, and settings.</summary>
    public TiffDevice(int width, int height, Resolution resolution, TiffSettings settings) : base(width, height, resolution)
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with width, height, resolution, settings, and converter.</summary>
    public TiffDevice(int width, int height, Resolution resolution, TiffSettings settings, IIndexBitmapConverter converter) : base(width, height, resolution)
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with a page size and settings. The output is
    /// sized to the page size at the default 150 DPI (1 PDF point = 1/72 inch).</summary>
    public TiffDevice(Aspose.Pdf.PageSize pageSize, TiffSettings settings)
        : base((int)Math.Round(pageSize.Width * 150 / 72.0), (int)Math.Round(pageSize.Height * 150 / 72.0))
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with a page size, settings, and converter.</summary>
    public TiffDevice(Aspose.Pdf.PageSize pageSize, TiffSettings settings, IIndexBitmapConverter converter)
        : base((int)Math.Round(pageSize.Width * 150 / 72.0), (int)Math.Round(pageSize.Height * 150 / 72.0))
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with resolution and settings.</summary>
    public TiffDevice(Resolution resolution, TiffSettings settings) : base(resolution)
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>Creates a TiffDevice with resolution, settings, and converter.</summary>
    public TiffDevice(Resolution resolution, TiffSettings settings, IIndexBitmapConverter converter) : base(new SoftwarePageRenderer(), resolution)
    {
        Settings = settings ?? new TiffSettings();
    }

    // ── PageSize-based constructors ───────────────────────────────────────────

    /// <summary>Creates a TiffDevice sized to the given <see cref="PageSize"/>
    /// at the default 150 DPI. The pixel dimensions are derived from the page
    /// size and resolution (1 PDF point = 1/72 inch).</summary>
    public TiffDevice(Aspose.Pdf.PageSize pageSize)
        : base((int)(pageSize.Width * 150 / 72), (int)(pageSize.Height * 150 / 72))
    {
        Settings = new TiffSettings();
    }

    /// <summary>Creates a TiffDevice sized to the given <see cref="PageSize"/> at
    /// the given <see cref="Resolution"/>.</summary>
    public TiffDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution)
        : base((int)(pageSize.Width * resolution.X / 72), (int)(pageSize.Height * resolution.Y / 72), resolution)
    {
        Settings = new TiffSettings();
    }

    /// <summary>PageSize + Resolution + Settings.</summary>
    public TiffDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution, TiffSettings settings)
        : base((int)(pageSize.Width * resolution.X / 72), (int)(pageSize.Height * resolution.Y / 72), resolution)
    {
        Settings = settings ?? new TiffSettings();
    }

    /// <summary>PageSize + Resolution + Settings + Converter.</summary>
    public TiffDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution, TiffSettings settings, IIndexBitmapConverter converter)
        : base((int)(pageSize.Width * resolution.X / 72), (int)(pageSize.Height * resolution.Y / 72), resolution)
    {
        Settings = settings ?? new TiffSettings();
    }

    // ── Public surface that mirrors Aspose.Pdf ───────────────────────

    /// <summary>Pixel width of the output bitmap. 0 when the device follows the
    /// page's natural size at <see cref="Resolution"/>.</summary>
    public new int Width => TargetWidth;

    /// <summary>Pixel height of the output bitmap. 0 when the device follows the
    /// page's natural size at <see cref="Resolution"/>.</summary>
    public new int Height => TargetHeight;

    /// <summary>Form-presentation mode (Editor / Production) consulted when the
    /// page contains AcroForm widgets. Stored only — the renderer treats every
    /// run as Production.</summary>
    public new FormPresentationMode FormPresentationMode { get; set; } = FormPresentationMode.Production;

    /// <summary>Redeclares the inherited Resolution property on TiffDevice so the
    /// Aspose.Pdf reflection signature (which uses BindingFlags.DeclaredOnly)
    /// finds it on this type.</summary>
    public new Resolution Resolution => base.Resolution;

    /// <summary>Redeclares the inherited RenderingOptions property on TiffDevice
    /// (same reason as <see cref="Resolution"/> above). Forwards to the base
    /// property.</summary>
    public new Aspose.Pdf.RenderingOptions RenderingOptions
    {
        get => base.RenderingOptions;
        set => base.RenderingOptions = value;
    }

    /// <summary>Adaptive Bradley/Roth threshold binarization. The FOSS image
    /// pipeline doesn't expose a generic Bitmap I/O path; calling this throws
    /// rather than silently producing wrong bytes.</summary>
    public static void BinarizeBradley(Stream inputImageStream, Stream outputImageStream, double threshold)
    {
        _ = inputImageStream; _ = outputImageStream; _ = threshold;
        throw new NotImplementedException(
            "TiffDevice.BinarizeBradley requires a generic Bitmap I/O path that the FOSS image pipeline does not implement.");
    }

    // Supersample factor used for bilevel output: render at N× then area-average
    // the luminance of each N×N source block before thresholding. A hard threshold
    // on AA'd 1× rendering leaves ragged glyph edges and random "dust" pixels in
    // the 1bpp output — area-averaging the supersampled luminance produces the
    // clean glyph silhouettes GDI+ writes when downsampling to bilevel. 2× is the
    // sweet spot: 3×+ doesn't visibly improve and grows the render buffer to 9×.
    private const int BilevelSupersample = 2;

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderForOutput(page, out var super);
        if (output.CanSeek)
        {
            EncodeTiff(new[] { (rgba.Data, rgba.Width, rgba.Height) }, output, Settings.Compression, super);
        }
        else
        {
            using var ms = new MemoryStream();
            EncodeTiff(new[] { (rgba.Data, rgba.Width, rgba.Height) }, ms, Settings.Compression, super);
            ms.Position = 0;
            ms.CopyTo(output);
        }
    }

    // Picks the render path that matches the requested depth. Bilevel paths
    // get a 2×-supersampled buffer; the packer area-averages luminance before
    // thresholding. Other depths get the normal RenderPage output.
    private RgbaBuffer RenderForOutput(Page page, out int superFactor)
    {
        if (EffectiveDepth(Settings) == ColorDepth.Format1bpp)
        {
            superFactor = BilevelSupersample;
            return RenderSupersampled(page, superFactor);
        }
        superFactor = 1;
        return RenderPage(page);
    }

    // Render at superFactor × the natural output pixel size. When the caller
    // pinned a target size (e.g. SaveAsTIFF(file, 200, 250, …) routes through
    // the (int,int,…) TiffDevice constructor), the natural output is TargetWidth
    // × TargetHeight — render at super × that and downsample. Otherwise scale
    // the DPI by super and let the renderer derive the pixel grid from the page
    // box. Either way the resulting buffer is super × the final output bitmap.
    private RgbaBuffer RenderSupersampled(Page page, int superFactor)
    {
        if (TargetWidth > 0 && TargetHeight > 0)
        {
            if (Renderer is SoftwarePageRenderer swDirect)
                return swDirect.RenderPageAtPixelSize(page, TargetWidth * superFactor, TargetHeight * superFactor);
            if (OperatingSystem.IsWindows() && Renderer is GdiPlusPageRenderer gdiDirect)
                return gdiDirect.RenderPageAtPixelSize(page, TargetWidth * superFactor, TargetHeight * superFactor);
        }

        var xDpi = Resolution.X * superFactor;
        var yDpi = Resolution.Y * superFactor;
        if (Renderer is SoftwarePageRenderer sw)
            return sw.RenderPage(page, xDpi, yDpi);
        if (OperatingSystem.IsWindows() && Renderer is GdiPlusPageRenderer gdi)
            return gdi.RenderPage(page, xDpi, yDpi);
        return Renderer.RenderPage(page.Reader.RawData, page.Number, xDpi);
    }

    // CCITT3/CCITT4 are bilevel-only fax encodings, so they imply 1-bit depth even
    // when the caller leaves Settings.Depth at the (24bpp) default. Mirroring the
    // behaviour of Aspose.Pdf: requesting CCITT compression produces a 1bpp TIFF.
    private static bool IsBilevelRequested(TiffSettings s)
        => s.Compression is CompressionType.CCITT3 or CompressionType.CCITT4
        || s.Depth == ColorDepth.Format1bpp;

    // The strip-layout decision in WriteTiffPage keys off ColorDepth alone; CCITT
    // compressions need the same 1bpp packing path, so coerce the effective depth
    // up front rather than threading the compression flag through every helper.
    private static ColorDepth EffectiveDepth(TiffSettings s)
        => IsBilevelRequested(s) ? ColorDepth.Format1bpp : s.Depth;

    // Maps TiffSettings.Brightness (0..1) to the 0..255 bilevel cutoff used by both
    // 1bpp packers: a pixel is black when its lightness (min RGB channel) is below the
    // cutoff. Aspose.Pdf binarises with a floor at the mid-grey cutoff 128
    // (the value both packers hard-coded before) and only *raises* it as Brightness
    // climbs above 0.5, linearly to 255 at Brightness 1.0 — cutoff = max(128,
    // Brightness×255). Brightness ≤ 0.5 (the default) leaves output unchanged; higher
    // Brightness pushes the cutoff up so mid-grey ink — scanned form rules, faint
    // handwriting — also binarises to black instead of dropping out (Brightness 0.85 ⇒
    // 217). Calibrated against 1bpp/CCITT output at Brightness 0.85
    // (more black) and 0.1 (unchanged from the 128 floor, not the naïve 0.1×255=26).
    private static int BilevelCutoff(float brightness)
    {
        var t = (int)System.Math.Round(brightness * 255f);
        if (t < 128) t = 128;
        return t > 255 ? 255 : t;
    }

    private static void ThresholdToBlackAndWhite(byte[] rgb, int cutoff)
    {
        // Ink-coverage threshold: a pixel is white only when every channel is light
        // (near paper white), so coloured ink — e.g. a cyan/azure heading printed as
        // CMYK — binarises to black rather than dropping out, matching how the
        // Aspose.Pdf fax/bilevel converter preserves any non-white ink. For grey
        // content (R=G=B) this is identical to the previous Rec.601 luminance cutoff.
        for (var i = 0; i < rgb.Length; i += 3)
        {
            var ink = System.Math.Min(rgb[i], System.Math.Min(rgb[i + 1], rgb[i + 2]));
            var v = ink >= cutoff ? (byte)255 : (byte)0;
            rgb[i] = v; rgb[i + 1] = v; rgb[i + 2] = v;
        }
    }

    // Downsample a supersampled RGBA buffer to 1bpp bilevel bits packed MSB-first.
    // Each output bit is the area-averaged ink coverage (255 − min channel) of the
    // corresponding (super × super) source block, thresholded at 128 — identical to a
    // luminance cutoff for grey content but keeping coloured ink (e.g. a CMYK cyan
    // heading) black instead of dropping it. Output uses the WhiteIsZero convention
    // (bit set ⇒ black) to match the IFD's Photometric=0 below.
    private static byte[] PackSupersampledRgbaToBilevel(byte[] rgba, int srcW, int srcH, int super,
                                                       int cutoff, out int dstW, out int dstH)
    {
        dstW = srcW / super;
        dstH = srcH / super;
        var bytesPerRow = (dstW + 7) / 8;
        var output = new byte[bytesPerRow * dstH];
        var samplesPerBlock = super * super;
        var threshold = cutoff * samplesPerBlock;
        var stride = srcW * 4;
        for (var dy = 0; dy < dstH; dy++)
        {
            var rowDst = dy * bytesPerRow;
            var sy0 = dy * super;
            for (var dx = 0; dx < dstW; dx++)
            {
                var sx0 = dx * super;
                var sum = 0;
                for (var oy = 0; oy < super; oy++)
                {
                    var rowSrc = (sy0 + oy) * stride;
                    for (var ox = 0; ox < super; ox++)
                    {
                        var si = rowSrc + (sx0 + ox) * 4;
                        // Per-pixel lightness as the minimum channel (255 − ink coverage):
                        // white only when every channel is light, so coloured ink stays dark.
                        sum += System.Math.Min(rgba[si], System.Math.Min(rgba[si + 1], rgba[si + 2]));
                    }
                }
                if (sum < threshold)
                    output[rowDst + (dx >> 3)] |= (byte)(0x80 >> (dx & 7));
            }
        }
        return output;
    }

    /// <summary>
    /// Render a range of pages to a single multi-page TIFF.
    /// </summary>
    /// <param name="document">The PDF document.</param>
    /// <param name="startPage">1-based start page (inclusive).</param>
    /// <param name="endPage">1-based end page (inclusive). Defaults to last page.</param>
    public byte[] ProcessRange(Document document, int startPage, int endPage = 0)
    {
        if (endPage <= 0) endPage = document.PageCount;
        var pages = new List<(byte[] rgba, int w, int h)>();
        var super = 1;

        for (var i = startPage; i <= endPage; i++)
        {
            var page = document.Pages.At(i);
            var rgba = RenderForOutput(page, out super);
            if (Settings.SkipBlankPages && IsBlankRaster(rgba.Data))
                continue;
            pages.Add((rgba.Data, rgba.Width, rgba.Height));
        }

        // If every page in the range was blank, fall back to rendering the whole
        // range rather than emitting an empty (page-less) TIFF.
        if (pages.Count == 0)
        {
            for (var i = startPage; i <= endPage; i++)
            {
                var page = document.Pages.At(i);
                var rgba = RenderForOutput(page, out super);
                pages.Add((rgba.Data, rgba.Width, rgba.Height));
            }
        }

        using var ms = new MemoryStream();
        EncodeTiff(pages.ToArray(), ms, Settings.Compression, super);
        return ms.ToArray();
    }

    // A page is "blank" for SkipBlankPages purposes when its rendered raster
    // carries no visible ink — every pixel is at (or imperceptibly close to)
    // paper white. Uses the same min-channel ink metric as the bilevel packer
    // so a faint coloured mark still counts as content. Pure-white empty pages
    // (the common "blank page" case) render with zero ink pixels and are dropped.
    private static bool IsBlankRaster(byte[] rgba)
    {
        for (var i = 0; i + 2 < rgba.Length; i += 4)
        {
            var ink = System.Math.Min(rgba[i], System.Math.Min(rgba[i + 1], rgba[i + 2]));
            if (ink < 250)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Render a range of pages to a multi-page TIFF and write to stream.
    /// Writes directly to <paramref name="output"/> when it is seekable,
    /// avoiding the 2GB MemoryStream limit on large multi-page outputs.
    /// Renders and encodes one page at a time to keep memory bounded for
    /// arbitrarily large documents.
    /// </summary>
    public void ProcessRange(Document document, int startPage, int endPage, Stream output)
    {
        if (!output.CanSeek)
        {
            // Non-seekable output: fall back to the byte[] path.
            var tiff = ProcessRange(document, startPage, endPage);
            output.Write(tiff);
            return;
        }

        var bw = new BinaryWriter(output);

        // TIFF header: little-endian, magic 42
        bw.Write((byte)'I');
        bw.Write((byte)'I');
        bw.Write((ushort)42);

        // Placeholder for first IFD offset
        var ifdOffsetPos = output.Position;
        bw.Write((uint)0);

        var written = 0;
        for (var i = startPage; i <= endPage; i++)
        {
            var page = document.Pages.At(i);
            var rgba = RenderForOutput(page, out var super);
            if (Settings.SkipBlankPages && IsBlankRaster(rgba.Data))
                continue;
            ifdOffsetPos = WriteTiffPage(bw, output, rgba.Data, rgba.Width, rgba.Height, ifdOffsetPos, Settings.Compression, EffectiveDepth(Settings), super);
            written++;
        }

        // Every page in the range was blank: emit the first page so the output
        // is a valid single-page TIFF rather than an empty (page-less) file.
        if (written == 0)
        {
            var page = document.Pages.At(startPage);
            var rgba = RenderForOutput(page, out var super);
            WriteTiffPage(bw, output, rgba.Data, rgba.Width, rgba.Height, ifdOffsetPos, Settings.Compression, EffectiveDepth(Settings), super);
        }
        bw.Flush();
    }

    /// <summary>
    /// Render a range of pages to TIFF.    /// </summary>
    public void Process(Document document, int fromPage, int toPage, Stream output)
        => ProcessRange(document, fromPage, toPage, output);

    /// <summary>
    /// Render a range of pages to a TIFF file.    /// </summary>
    public void Process(Document document, int startPage, int endPage, string outputFileName)
    {
        using var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write);
        ProcessRange(document, startPage, endPage, fs);
    }

    /// <summary>
    /// Render all pages to a TIFF file.    /// </summary>
    public void Process(Document document, string outputFileName)
    {
        Process(document, 1, document.Pages.Count, outputFileName);
    }

    /// <summary>
    /// Render all pages to a TIFF stream.    /// </summary>
    public void Process(Document document, Stream output)
    {
        ProcessRange(document, 1, document.Pages.Count, output);
    }

    private static byte[] RgbaToRgb(byte[] rgba, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int s = 0, d = 0; s < rgba.Length; s += 4, d += 3)
        {
            rgb[d] = rgba[s];
            rgb[d + 1] = rgba[s + 1];
            rgb[d + 2] = rgba[s + 2];
        }
        return rgb;
    }

    /// <summary>Encode a single RGBA raster as a one-page TIFF (used by
    /// <see cref="Aspose.Pdf.XImage.Save(System.IO.Stream, Aspose.Pdf.Drawing.ImageFormat)"/>).
    /// Default colour depth keeps full 24-bit RGB unless a bilevel compression is requested.</summary>
    internal static void EncodeRgbaImage(byte[] rgba, int w, int h, Stream output, CompressionType compression)
    {
        var dev = new TiffDevice(new TiffSettings { Compression = compression, Depth = ColorDepth.Default });
        dev.EncodeTiff(new[] { (rgba, w, h) }, output, compression);
    }

    private void EncodeTiff((byte[] rgba, int w, int h)[] pages, Stream output, CompressionType compression, int superFactor = 1)
    {
        // Requires a seekable stream because IFD offsets are back-patched.
        var bw = new BinaryWriter(output);

        // TIFF header: little-endian, magic 42
        bw.Write((byte)'I');
        bw.Write((byte)'I');
        bw.Write((ushort)42);

        // Placeholder for first IFD offset
        var ifdOffsetPos = output.Position;
        bw.Write((uint)0);

        foreach (var (rgba, w, h) in pages)
        {
            ifdOffsetPos = WriteTiffPage(bw, output, rgba, w, h, ifdOffsetPos, compression, EffectiveDepth(Settings), superFactor);
        }

        bw.Flush();
    }

    // Write one TIFF page (strip + IFD), patching the previous IFD offset
    // placeholder to point at this page's IFD. Returns the file position of
    // the next-IFD placeholder for the caller to patch with the following
    // page's IFD offset (or 0 for the last page).
    //
    // Input is the rendered RGBA buffer (4 bytes/pixel). The strip layout is
    // selected per ColorDepth so the same upstream pixels can land as 1bpp
    // bilevel, 8bpp palette, 24bpp RGB, or 32bpp RGBA without re-rendering.
    private long WriteTiffPage(BinaryWriter bw, Stream output,
                               byte[] rgba, int w, int h, long ifdOffsetPos,
                               CompressionType compression, ColorDepth depth, int superFactor = 1)
    {
        var isPalette = depth == ColorDepth.Format8bpp;
        var is4bpp = depth == ColorDepth.Format4bpp;
        var isBilevel = depth == ColorDepth.Format1bpp;
        // Default depth keeps the source alpha channel — emits 32bpp RGBA. Explicit
        // Format24bpp drops alpha. The Aspose.Pdf default is 32bpp ARGB; this
        // matches PixelFormat.Format32bppArgb when the TIFF is read back.
        var isAlpha = depth == ColorDepth.Default;

        // Pick the strip layout per requested depth:
        //   1bpp: 1 packed bit per pixel, MSB-first, /Photometric=WhiteIsZero so
        //         CCITT-style templates and our output share the same convention.
        //         When superFactor > 1 the bilevel packer area-averages each
        //         super × super source block to grayscale, then thresholds — output
        //         dimensions become w/super × h/super.
        //   8bpp: indexed palette — adaptive (≤256 unique colours, lossless)
        //         falling back to 3-3-2 uniform.
        //   4bpp: indexed palette — adaptive ≤16 colours (lossless) else the
        //         16 most frequent, packed 2 indices/byte (high nibble first).
        //  32bpp: RGBA straight through with ExtraSamples=2 (unassociated alpha).
        //   else: 24-bit RGB (alpha stripped).
        byte[] stripInput;
        ushort[]? colorMap = null;
        if (isAlpha)
        {
            stripInput = rgba;
        }
        else if (isBilevel)
        {
            var cutoff = BilevelCutoff(Settings.Brightness);
            if (superFactor > 1)
            {
                stripInput = PackSupersampledRgbaToBilevel(rgba, w, h, superFactor, cutoff, out var bw1, out var bh1);
                w = bw1;
                h = bh1;
            }
            else
            {
                var rgb = RgbaToRgb(rgba, w, h);
                ThresholdToBlackAndWhite(rgb, cutoff);
                stripInput = PackRgbToBilevel(rgb, w, h);
            }
        }
        else if (isPalette)
        {
            var rgb = RgbaToRgb(rgba, w, h);
            var adaptive = TiffPaletteQuantizer.TryQuantizeAdaptive(rgb, w, h);
            if (adaptive is { } a)
            {
                stripInput = a.indexed;
                colorMap = a.colorMap;
            }
            else
            {
                stripInput = TiffPaletteQuantizer.QuantizeRgbTo8bpp(rgb, w, h);
                colorMap = TiffPaletteQuantizer.BuildColorMap332();
            }
        }
        else if (is4bpp)
        {
            var rgb = RgbaToRgb(rgba, w, h);
            var (indices, map) = TiffPaletteQuantizer.QuantizeTo4bpp(rgb, w, h);
            stripInput = Pack4bpp(indices, w, h);
            colorMap = map;
        }
        else
        {
            stripInput = RgbaToRgb(rgba, w, h);
        }

        var (strip, compressionTag) = EncodeStrip(stripInput, compression);
        var stripSize = strip.Length;

        // Write strip data first
        var stripOffset = (uint)output.Position;
        bw.Write(strip);

        // Align to word boundary
        if (output.Position % 2 != 0) bw.Write((byte)0);

        // Auxiliary payloads referenced from IFD tags by offset:
        // BitsPerSample array (RGB/RGBA only), ColorMap (palette-only),
        // ExtraSamples (RGBA-only).
        uint bpsOffset = 0;
        if (!isPalette && !is4bpp && !isBilevel)
        {
            bpsOffset = (uint)output.Position;
            bw.Write((ushort)8);
            bw.Write((ushort)8);
            bw.Write((ushort)8);
            if (isAlpha) bw.Write((ushort)8);
        }

        uint colorMapOffset = 0;
        if (isPalette || is4bpp)
        {
            colorMapOffset = (uint)output.Position;
            foreach (var s in colorMap!) bw.Write(s);
        }

        // RATIONAL payloads for X/YResolution. TIFF tag 282/283 stores the resolution
        // as a fraction (numerator/denominator, both u32) at an out-of-line offset.
        // ResolutionUnit (296) = 2 means inches, so DPI/1 expresses N dots per inch
        // exactly. Without these tags, readers (System.Drawing, ImageSharp) fall back
        // to a hard-coded 96 DPI, which broke any test that asserted on the saved DPI.
        uint xResOffset = (uint)output.Position;
        bw.Write((uint)Resolution.X);
        bw.Write((uint)1);
        uint yResOffset = (uint)output.Position;
        bw.Write((uint)Resolution.Y);
        bw.Write((uint)1);

        // Patch previous IFD offset to point here
        var ifdOffset = (uint)output.Position;
        var currentPos = output.Position;
        output.Position = ifdOffsetPos;
        bw.Write(ifdOffset);
        output.Position = currentPos;

        // IFD tag count: base 13 (RGB/bilevel/RGBA without extras), +1 for palette
        // ColorMap, +1 for RGBA ExtraSamples.
        ushort tagCount = (ushort)(13 + (isPalette || is4bpp ? 1 : 0) + (isAlpha ? 1 : 0));
        bw.Write(tagCount);

        // Tags MUST be written in ascending tag-number order per the TIFF 6.0 spec.
        WriteTag(bw, 256, 3, 1, (uint)w);                               // ImageWidth
        WriteTag(bw, 257, 3, 1, (uint)h);                               // ImageLength
        if (isPalette)
            WriteTag(bw, 258, 3, 1, 8);                                 // BitsPerSample = 8 (inline)
        else if (is4bpp)
            WriteTag(bw, 258, 3, 1, 4);                                 // BitsPerSample = 4 (inline)
        else if (isBilevel)
            WriteTag(bw, 258, 3, 1, 1);                                 // BitsPerSample = 1 (inline)
        else
            WriteTag(bw, 258, 3, isAlpha ? 4u : 3u, bpsOffset);         // BitsPerSample offset → [8,8,8(,8)]
        WriteTag(bw, 259, 3, 1, compressionTag);                        // Compression
        // Photometric: 0=WhiteIsZero (bilevel min-is-white), 2=RGB(/RGBA), 3=Palette.
        var photometric = (isPalette || is4bpp) ? 3u : isBilevel ? 0u : 2u;
        WriteTag(bw, 262, 3, 1, photometric);                            // PhotometricInterpretation
        WriteTag(bw, 273, 4, 1, stripOffset);                           // StripOffsets
        var samplesPerPixel = (isPalette || is4bpp || isBilevel) ? 1u : isAlpha ? 4u : 3u;
        WriteTag(bw, 277, 3, 1, samplesPerPixel);                        // SamplesPerPixel
        WriteTag(bw, 278, 3, 1, (uint)h);                               // RowsPerStrip
        WriteTag(bw, 279, 4, 1, (uint)stripSize);                       // StripByteCounts
        WriteTag(bw, 282, 5, 1, xResOffset);                            // XResolution (RATIONAL)
        WriteTag(bw, 283, 5, 1, yResOffset);                            // YResolution (RATIONAL)
        WriteTag(bw, 284, 3, 1, 1);                                     // PlanarConfiguration = Chunky
        WriteTag(bw, 296, 3, 1, 2);                                     // ResolutionUnit = 2 (inches)
        if (isPalette)
            WriteTag(bw, 320, 3, 3 * 256, colorMapOffset);              // ColorMap (768 shorts)
        else if (is4bpp)
            WriteTag(bw, 320, 3, 3 * 16, colorMapOffset);               // ColorMap (48 shorts)
        if (isAlpha)
            WriteTag(bw, 338, 3, 1, 2);                                 // ExtraSamples = 2 (unassociated alpha, inline)

        // Next IFD offset placeholder (0 for last page; caller patches when
        // writing the next page).
        var nextIfdPos = output.Position;
        bw.Write((uint)0);
        return nextIfdPos;
    }

    // Pack a thresholded RGB buffer into 1bpp MSB-first bytes with
    // /Photometric=WhiteIsZero (bit 0 = white, bit 1 = black). Caller has
    // already passed the buffer through ThresholdToBlackAndWhite, so each
    // pixel is either pure (255,255,255) or pure (0,0,0).
    private static byte[] PackRgbToBilevel(byte[] rgb, int width, int height)
    {
        var bytesPerRow = (width + 7) / 8;
        var output = new byte[bytesPerRow * height];
        for (var y = 0; y < height; y++)
        {
            var rowSrc = y * width * 3;
            var rowDst = y * bytesPerRow;
            for (var x = 0; x < width; x++)
            {
                // Black input pixel → bit set (per WhiteIsZero convention).
                if (rgb[rowSrc + x * 3] < 128)
                {
                    var byteIdx = rowDst + (x >> 3);
                    output[byteIdx] |= (byte)(0x80 >> (x & 7));
                }
            }
        }
        return output;
    }

    // Pack one-index-per-pixel (each 0..15) into 4bpp rows: the left pixel of
    // each pair goes in the high nibble, and every scanline is padded to a whole
    // byte because TIFF requires byte-aligned rows. Mirrors the 1bpp packer.
    private static byte[] Pack4bpp(byte[] indices, int width, int height)
    {
        var bytesPerRow = (width + 1) / 2;
        var output = new byte[bytesPerRow * height];
        for (var y = 0; y < height; y++)
        {
            var rowSrc = y * width;
            var rowDst = y * bytesPerRow;
            for (var x = 0; x < width; x++)
            {
                var nibble = (byte)(indices[rowSrc + x] & 0x0F);
                if ((x & 1) == 0)
                    output[rowDst + (x >> 1)] = (byte)(nibble << 4);
                else
                    output[rowDst + (x >> 1)] |= nibble;
            }
        }
        return output;
    }

    // Encode a raw strip per the requested compression, returning both the
    // bytes and the TIFF-tag Compression value to write in the IFD. Unsupported
    // compressions fall back to uncompressed bytes with tag=1 so the file stays
    // readable instead of advertising a compression we haven't actually applied.
    // The strip layout (RGB vs. palette-indexed) is decided by the caller.
    private static (byte[] data, uint tag) EncodeStrip(byte[] bytes, CompressionType compression)
    {
        return compression switch
        {
            CompressionType.LZW => (LzwTiffEncoder.Encode(bytes), 5u),
            _ => (bytes, 1u),
        };
    }

    private static void WriteTag(BinaryWriter bw, ushort tag, ushort type, uint count, uint value)
    {
        bw.Write(tag);
        bw.Write(type);
        bw.Write(count);
        bw.Write(value);
    }
}
