namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into TIFF image format.
/// Supports single-page and multi-page TIFF output.
/// </summary>
public sealed partial class TiffDevice : ImageDevice
{
    /// <summary>TIFF encoding settings (color depth, compression).</summary>
    public TiffSettings Settings { get; }

    /// <summary>Creates a TiffDevice that renders pages with the given <c>renderer</c> at the default resolution of 150 DPI. Uses the default TIFF settings.</summary>
    public TiffDevice(IPageRenderer renderer) : base(renderer)
    {
        Settings = new TiffSettings();
    }

    /// <summary>Creates a TiffDevice that renders pages with the given <c>renderer</c> at the given resolution (150 DPI when <c>resolution</c> is null). Uses the default TIFF settings.</summary>
    public TiffDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution)
    {
        Settings = new TiffSettings();
    }

    /// <summary>Creates a TiffDevice that renders pages with the built-in renderer at the default resolution of 150 DPI. Uses the default TIFF settings.</summary>
    public TiffDevice() : base()
    {
        Settings = new TiffSettings();
    }

    /// <summary>Creates a TiffDevice that renders pages with the built-in renderer at the given resolution. Uses the default TIFF settings.</summary>
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

    // ── Public device surface (reflection-shape compatibility) ───────

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

    /// <summary>Redeclares the inherited Resolution property on TiffDevice so
    /// reflection lookups that use BindingFlags.DeclaredOnly
    /// find it on this type.</summary>
    public new Resolution Resolution => base.Resolution;

    /// <summary>Redeclares the inherited RenderingOptions property on TiffDevice
    /// (same reason as <see cref="Resolution"/> above). Forwards to the base
    /// property.</summary>
    public new Aspose.Pdf.RenderingOptions RenderingOptions
    {
        get => base.RenderingOptions;
        set => base.RenderingOptions = value;
    }

    /// <summary>Bradley's adaptive threshold: a pixel goes black when it is darker than the mean of the
    /// window around it (a square an eighth of the width across) by more than <paramref name="threshold"/>,
    /// a fraction of that mean - so a scan keeps its text under uneven lighting. The first page of the
    /// TIFF is read with the library's own decoder and written back as a bilevel, LZW-compressed TIFF.</summary>
    public static void BinarizeBradley(Stream inputImageStream, Stream outputImageStream, double threshold)
    {
        if (inputImageStream is null) throw new ArgumentNullException(nameof(inputImageStream));
        if (outputImageStream is null) throw new ArgumentNullException(nameof(outputImageStream));
        if (inputImageStream.CanSeek) inputImageStream.Position = 0;
        using var input = new MemoryStream();
        inputImageStream.CopyTo(input);
        var frames = IO.TiffDecoder.DecodeFramesAsPng(input.ToArray());
        if (frames is null || frames.Count == 0)
            throw new ArgumentException("The input is not a TIFF this library reads.", nameof(inputImageStream));
        var (pixels, w, h, hasAlpha) = Facades.PdfFileMend.DecodePng(frames[0]);
        var comps = hasAlpha ? 4 : 3;
        var gray = new int[w * h];
        for (var i = 0; i < gray.Length; i++)
            gray[i] = (pixels[i * comps] * 299 + pixels[i * comps + 1] * 587 + pixels[i * comps + 2] * 114) / 1000;
        var black = BradleyBlack(gray, w, h, threshold);
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < gray.Length; i++)
        {
            var level = black[i] ? (byte)0 : (byte)255;
            rgba[i * 4] = level; rgba[i * 4 + 1] = level; rgba[i * 4 + 2] = level; rgba[i * 4 + 3] = 255;
        }
        EncodeBilevelImage(rgba, w, h, outputImageStream, CompressionType.LZW);
    }

    /// <summary>Which pixels Bradley's rule paints black: those darker than the mean of their window by more
    /// than the given fraction of it. The window sums come from an integral image, so each pixel costs the same
    /// whatever the window's size.</summary>
    private static bool[] BradleyBlack(int[] gray, int w, int h, double threshold)
    {
        var integral = new long[(w + 1) * (h + 1)];
        for (var y = 1; y <= h; y++)
        {
            long row = 0;
            for (var x = 1; x <= w; x++)
            {
                row += gray[(y - 1) * w + x - 1];
                integral[y * (w + 1) + x] = integral[(y - 1) * (w + 1) + x] + row;
            }
        }
        var half = Math.Max(1, w / 8) / 2;
        var black = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            var top = Math.Max(0, y - half);
            var bottom = Math.Min(h - 1, y + half);
            for (var x = 0; x < w; x++)
            {
                var left = Math.Max(0, x - half);
                var right = Math.Min(w - 1, x + half);
                var count = (right - left + 1) * (bottom - top + 1);
                var sum = integral[(bottom + 1) * (w + 1) + right + 1] - integral[top * (w + 1) + right + 1]
                        - integral[(bottom + 1) * (w + 1) + left] + integral[top * (w + 1) + left];
                black[y * w + x] = gray[y * w + x] * count <= sum * (1.0 - threshold);
            }
        }
        return black;
    }

    /// <summary>A black-and-white picture (every pixel pure black or pure white) as a one-page bilevel TIFF.</summary>
    internal static void EncodeBilevelImage(byte[] rgba, int w, int h, Stream output, CompressionType compression)
    {
        var dev = new TiffDevice(new TiffSettings { Compression = compression, Depth = ColorDepth.Format1bpp });
        dev.EncodeTiff(new[] { (rgba, w, h) }, output, compression);
    }

    // The default-brightness bilevel output is a three-zone halftone of the plain render,
    // judged per pixel on the mean of its three channels:
    // under 85 goes solid black, from 85 up to 170 renders as a 50 % checkerboard
    // (black on odd x+y parity), and 170 or lighter goes paper white. Measured
    // 2026-09-06 against six reference fax outputs (the reference's own 300-dpi render
    // alongside each 1-bit TIFF): the fitted zone edges sit at 84..92 and 164..180
    // with the middle band 88 levels wide on every page, agreeing on 99.0-100 % of
    // pixels; the channel MEAN is the metric because an orange (243,147,9) halftones
    // while its luminance would read as paper, and a cyan (2,175,235) halftones while
    // its darkest channel would read as ink. The one thing the reference does that this
    // does not: it drops a 0.25 pt rule whose single pixel of ink straddles two rows,
    // which both of our sample grids keep as a dotted rule.
    private const int BilevelBlackBelow = 85;
    private const int BilevelHalftoneBelow = 170;

    // Brightness at or under this keeps the halftone; above it the output is a plain
    // luminance threshold at BilevelCutoff.
    private const float HalftoneBrightnessMax = 0.5f;

    private bool UsesHalftone => Settings.Brightness <= HalftoneBrightnessMax;

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderForOutput(page);
        if (output.CanSeek)
        {
            EncodeTiff(new[] { (rgba.Data, rgba.Width, rgba.Height) }, output, Settings.Compression);
        }
        else
        {
            using var ms = new MemoryStream();
            EncodeTiff(new[] { (rgba.Data, rgba.Width, rgba.Height) }, ms, Settings.Compression);
            ms.Position = 0;
            ms.CopyTo(output);
        }
    }

    // The ordinary render, turned to the requested frame shape. A bilevel depth packs
    // it per pixel later (see BilevelBlackBelow and BilevelCutoff).
    private RgbaBuffer RenderForOutput(Page page)
    {
        var buf = RenderPage(page);
        // TiffSettings.Shape forces the frame orientation: Landscape rotates a
        // portrait render 90° (and Portrait the reverse) so every frame comes
        // out in the requested aspect.
        if (Settings.Shape == ShapeType.Landscape && buf.Height > buf.Width)
            buf = Rotate90(buf);
        else if (Settings.Shape == ShapeType.Portrait && buf.Width > buf.Height)
            buf = Rotate90(buf);
        return buf;
    }


    /// <summary>Rotate 90° clockwise (top of the source becomes the right edge).</summary>
    private static RgbaBuffer Rotate90(RgbaBuffer src)
    {
        int w = src.Width, h = src.Height;
        var dst = new byte[src.Data.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var s = (y * w + x) * 4;
                var d = (x * h + (h - 1 - y)) * 4;
                dst[d] = src.Data[s];
                dst[d + 1] = src.Data[s + 1];
                dst[d + 2] = src.Data[s + 2];
                dst[d + 3] = src.Data[s + 3];
            }
        }
        return new RgbaBuffer(dst, h, w);
    }

    // CCITT3/CCITT4 are bilevel-only fax encodings, so they imply 1-bit depth even
    // when the caller leaves Settings.Depth at the (24bpp) default:
    // requesting CCITT compression produces a 1bpp TIFF.
    private static bool IsBilevelRequested(TiffSettings s)
        => s.Compression is CompressionType.CCITT3 or CompressionType.CCITT4
        || s.Depth == ColorDepth.Format1bpp;

    // The strip-layout decision in WriteTiffPage keys off ColorDepth alone; CCITT
    // compressions need the same 1bpp packing path, so coerce the effective depth
    // up front rather than threading the compression flag through every helper.
    private static ColorDepth EffectiveDepth(TiffSettings s)
        => IsBilevelRequested(s) ? ColorDepth.Format1bpp : s.Depth;

    // Maps a raised TiffSettings.Brightness (above HalftoneBrightnessMax) to the 0..255
    // luminance cutoff of the plain threshold. Measured on the expected CCITT output of
    // a 300-dpi scanned page across six Brightness points: the cutoff is LINEAR at
    // 235×Brightness + 20 (0.5 → 138, 0.6 → 161, 0.8 → 208, 0.85 → 220, 0.95 → 243,
    // each within one grey level of the empirical best-fit threshold). At and below
    // 0.5 the expected output is no longer a pure threshold (a plain-threshold fit
    // leaves a 1.4% residual — it dithers the mid band): that is the halftone path.
    // The previous max(128, 255×B) mapping sat 4 levels low at 0.8, which on a scan
    // whose paper tone straddles the cutoff flipped whole regions (the bank-logo band
    // of the regression fixture binarised black where it is expected white).
    private static int BilevelCutoff(float brightness)
    {
        var t = (int)System.Math.Round(brightness * 235f + 20f);
        return t > 255 ? 255 : t;
    }

    private static void ThresholdToBlackAndWhite(byte[] rgb, int cutoff)
    {
        // Rec.601 LUMINANCE threshold — the expected raised-Brightness
        // bilevel is a luminance cutoff, measured to 0.05% on a 300-dpi scan (a
        // min-channel "ink coverage" test reads a JPEG's chroma noise as ink and
        // behaves like a cutoff ~18 levels higher, flipping paper-tone regions).
        // This path only runs for Brightness > 0.5, where any coloured ink's
        // luminance sits below the raised cutoff and still binarises to black; the
        // default-brightness path keeps the supersampled ink-coverage packing.
        for (var i = 0; i < rgb.Length; i += 3)
        {
            var lum = (299 * rgb[i] + 587 * rgb[i + 1] + 114 * rgb[i + 2] + 500) / 1000;
            var v = lum >= cutoff ? (byte)255 : (byte)0;
            rgb[i] = v; rgb[i + 1] = v; rgb[i + 2] = v;
        }
    }

    // Pack a rendered RGBA buffer to 1bpp bilevel bits (MSB-first, WhiteIsZero: bit set
    // ⇒ black, matching the IFD's Photometric=0 below) through the three-zone luminance
    // halftone described at BilevelBlackBelow.
    private static byte[] PackRgbaToHalftoneBilevel(byte[] rgba, int width, int height)
    {
        var bytesPerRow = (width + 7) / 8;
        var output = new byte[bytesPerRow * height];
        for (var y = 0; y < height; y++)
        {
            var rowDst = y * bytesPerRow;
            var rowSrc = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var si = rowSrc + x * 4;
                var grey = (rgba[si] + rgba[si + 1] + rgba[si + 2]) / 3;
                var black = grey < BilevelBlackBelow
                            || (grey < BilevelHalftoneBelow && ((x + y) & 1) == 1);
                if (black)
                    output[rowDst + (x >> 3)] |= (byte)(0x80 >> (x & 7));
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

        for (var i = startPage; i <= endPage; i++)
        {
            var page = document.Pages.At(i);
            var rgba = RenderForOutput(page);
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
                var rgba = RenderForOutput(page);
                pages.Add((rgba.Data, rgba.Width, rgba.Height));
            }
        }

        using var ms = new MemoryStream();
        EncodeTiff(pages.ToArray(), ms, Settings.Compression);
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
            var rgba = RenderForOutput(page);
            if (Settings.SkipBlankPages && IsBlankRaster(rgba.Data))
                continue;
            ifdOffsetPos = WriteTiffPage(bw, output, rgba.Data, rgba.Width, rgba.Height, ifdOffsetPos, Settings.Compression, EffectiveDepth(Settings));
            written++;
        }

        // Every page in the range was blank: emit the first page so the output
        // is a valid single-page TIFF rather than an empty (page-less) file.
        if (written == 0)
        {
            var page = document.Pages.At(startPage);
            var rgba = RenderForOutput(page);
            WriteTiffPage(bw, output, rgba.Data, rgba.Width, rgba.Height, ifdOffsetPos, Settings.Compression, EffectiveDepth(Settings));
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
        => EncodeRgbaImages(new[] { (rgba, w, h) }, output, compression);

    /// <summary>RGBA pictures as the pages of one TIFF, in order (PdfConverter.MergeImagesAsTiff without GDI+).</summary>
    internal static void EncodeRgbaImages((byte[] rgba, int w, int h)[] pages, Stream output, CompressionType compression)
    {
        var dev = new TiffDevice(new TiffSettings { Compression = compression, Depth = ColorDepth.Default });
        dev.EncodeTiff(pages, output, compression);
    }

    private void EncodeTiff((byte[] rgba, int w, int h)[] pages, Stream output, CompressionType compression)
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
            ifdOffsetPos = WriteTiffPage(bw, output, rgba, w, h, ifdOffsetPos, compression, EffectiveDepth(Settings));
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
    private long WriteTiffPage(BinaryWriter bw, Stream output, byte[] rgba, int w, int h, long ifdOffsetPos, CompressionType compression, ColorDepth depth)
    {
        var tp = new TiffPageWriteState();
        tp.bw = bw;
        tp.output = output;
        tp.rgba = rgba;
        tp.w = w;
        tp.h = h;
        tp.ifdOffsetPos = ifdOffsetPos;
        tp.compression = compression;
        tp.depth = depth;
        tp.isPalette = tp.depth == ColorDepth.Format8bpp;
        tp.is4bpp = tp.depth == ColorDepth.Format4bpp;
        tp.isBilevel = tp.depth == ColorDepth.Format1bpp;
        tp.isAlpha = tp.depth == ColorDepth.Default;

        tp.colorMap = null;
        PackTiffRows(tp);

        WriteTiffStripAndDirectory(tp);
        return tp.nextIfdPos;
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
