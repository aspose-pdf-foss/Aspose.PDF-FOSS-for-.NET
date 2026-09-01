using System.IO.Compression;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>A Windows metafile - EMF (an EMR_HEADER record carrying the " EMF"
    /// signature at offset 40) or WMF (the placeable 0x9AC6CDD7 header, or a standard
    /// header whose type is 1 or 2 with a 9-word header size).</summary>
    internal static bool IsWindowsMetafile(byte[] d)
    {
        if (d is null) return false;
        if (d.Length >= 44 && d[0] == 0x01 && d[1] == 0 && d[2] == 0 && d[3] == 0
            && d[40] == 0x20 && d[41] == 0x45 && d[42] == 0x4D && d[43] == 0x46) return true;
        if (d.Length >= 4 && d[0] == 0xD7 && d[1] == 0xCD && d[2] == 0xC6 && d[3] == 0x9A) return true;
        if (d.Length >= 6 && (d[0] == 0x01 || d[0] == 0x02) && d[1] == 0x00
            && d[2] == 0x09 && d[3] == 0x00) return true;
        return false;
    }

    /// <summary>EMF and WMF are Windows metafile formats: they are a recorded stream of
    /// GDI calls, so reading one means having GDI. That is out of scope for this library
    /// off Windows, and saying so is better than the alternative - the decode cascade used
    /// to fall through to the PNG reader and report "Invalid PNG data", or, worse, to be
    /// swallowed by a caller that then failed much later with an index error. On Windows
    /// the GDI+ codec set reads them and this is a no-op.</summary>
    internal static void ThrowIfWindowsOnlyMetafile(byte[] data)
    {
        if (OperatingSystem.IsWindows() || !IsWindowsMetafile(data)) return;
        throw new PlatformNotSupportedException(
            "EMF and WMF are recorded GDI call streams and are readable on Windows only.");
    }

    private static bool IsPng(byte[] data) =>
        data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    /// <summary>SVG source: XML text whose leading kilobyte mentions an &lt;svg&gt; root.</summary>
    private static bool IsSvg(byte[] data)
    {
        if (data.Length < 5) return false;
        var head = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 1024));
        var trimmed = head.TrimStart('﻿', ' ', '\t', '\r', '\n');
        return (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<!DOCTYPE svg", StringComparison.OrdinalIgnoreCase))
               && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Create an image stamp from raw RGB pixel data.
    /// Pixels are row-major, top-to-bottom, 3 bytes per pixel (R, G, B).
    /// </summary>
    public static ImageStamp FromRgb(byte[] pixelData, int width, int height)
    {
        if (pixelData.Length != width * height * 3)
            throw new ArgumentException("Pixel data length must equal width × height × 3");

        var compressed = CompressFlate(pixelData);
        var stamp = new ImageStamp(compressed, width, height, "DeviceRGB", "FlateDecode", 8)
        {
            DisplayWidth = width,
            DisplayHeight = height
        };
        return stamp;
    }

    /// <summary>Attach an 8-bit DeviceGray soft mask (the source's alpha channel)
    /// so transparent pixels composite against the page behind instead of painting
    /// an opaque box. <paramref name="alpha"/> is row-major, one byte per pixel,
    /// matching the image dimensions.</summary>
    internal void SetAlphaMask(byte[] alpha)
    {
        if (alpha.Length != _width * _height)
            throw new ArgumentException("Alpha length must equal width × height");
        _smaskData = CompressFlate(alpha);
    }

    /// <summary>
    /// Create an image stamp from raw grayscale pixel data.
    /// </summary>
    public static ImageStamp FromGrayscale(byte[] pixelData, int width, int height)
    {
        if (pixelData.Length != width * height)
            throw new ArgumentException("Pixel data length must equal width × height");

        var compressed = CompressFlate(pixelData);
        var stamp = new ImageStamp(compressed, width, height, "DeviceGray", "FlateDecode", 8)
        {
            DisplayWidth = width,
            DisplayHeight = height
        };
        return stamp;
    }

    /// <summary>
    /// Create a 1-bit black/white image stamp from arbitrary image bytes (the
    /// <see cref="Image.IsBlackWhite"/> path). The source is decoded, each pixel is
    /// thresholded to black or white by luminance, and the result is packed at one
    /// bit per pixel (DeviceGray, FlateDecode) — far smaller than an 8-bit embed for
    /// scanned/bilevel documents. Returns null when the platform decoder is
    /// unavailable so the caller can fall back to a normal embed.
    /// </summary>
    /// <summary>True when the source image is genuinely bilevel (1 bit per pixel),
    /// so it can be embedded losslessly as a compact 1-bit image rather than an
    /// 8-bit re-encode. Returns false when the platform decoder is unavailable.</summary>
    internal static bool IsBilevelSource(byte[] imageData)
    {
        if (imageData is null || imageData.Length < 4) return false;
        // Off Windows the depth is read straight out of the header: a PNG states its bit
        // depth and colour type in IHDR, a BMP its bit count in the DIB header. Reporting
        // false there embedded every bilevel scan as 8-bit colour.
        if (!OperatingSystem.IsWindows()) return IsBilevelHeader(imageData);
        try
        {
#pragma warning disable CA1416
            using var ms = new MemoryStream(imageData);
            using var src = System.Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return src.PixelFormat == System.Drawing.Imaging.PixelFormat.Format1bppIndexed;
#pragma warning restore CA1416
        }
        catch { return false; }
    }

    /// <summary>Bit depth straight from the container header, for the platforms with no
    /// System.Drawing decoder. PNG: IHDR bit depth 1 with greyscale or palette colour type.
    /// BMP: a 1-bit DIB. Anything else (JPEG cannot be bilevel at all) reads as false.</summary>
    private static bool IsBilevelHeader(byte[] d)
    {
        // A header that already SAYS one bit settles it; anything else still has to be
        // judged on content below, so these only ever answer yes here.
        if (Facades.PdfFileMend.IsPng(d) && d.Length >= 26
            && d[24] == 1 && (d[25] == 0 || d[25] == 3))                // IHDR: depth, colour type
            return true;
        if (IO.BmpDecoder.IsBmp(d) && d.Length >= 30)
        {
            var dibSize = d[14] | (d[15] << 8) | (d[16] << 16) | (d[17] << 24);
            var bitCountAt = dibSize == 12 ? 24 : 28;
            if (d.Length > bitCountAt + 1
                && (d[bitCountAt] | (d[bitCountAt + 1] << 8)) == 1)
                return true;
        }
        // A TIFF states its depth in the BitsPerSample tag of its first IFD. A fax or
        // scanned page arrives this way and is exactly what the 1-bit embed is for: read
        // as 8-bit colour instead, one page went into the document at twice the size.
        if (IO.TiffDecoder.IsTiff(d) && TiffBitsPerSample(d) == 1) return true;

        // Neither header says bilevel, but the CONTENT still can be: a scan stored at 8
        // bits carrying only pure black and pure white is a bilevel picture, and the
        // reference embeds it as a 1-bit image - a 145 KB page went in at 135 KB of
        // 8-bit RGB here instead of 61 KB of 1-bit grey.
        return IsBilevelByPixels(d);
    }

    /// <summary>The BitsPerSample of a TIFF's first frame, or 0 when it cannot be read.
    /// Header-only: the IFD is walked for the one tag, nothing is decoded.</summary>
    private static int TiffBitsPerSample(byte[] d)
    {
        try
        {
            var le = d[0] == 0x49;
            int U16(int o) => le ? d[o] | (d[o + 1] << 8) : (d[o] << 8) | d[o + 1];
            long U32(int o) => le
                ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24))
                : (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
            var ifd = (int)U32(4);
            if (ifd <= 0 || ifd + 2 > d.Length) return 0;
            var count = U16(ifd);
            if (ifd + 2 + count * 12 > d.Length) return 0;
            for (var e = 0; e < count; e++)
            {
                var at = ifd + 2 + e * 12;
                if (U16(at) != 258) continue;                 // BitsPerSample
                var type = U16(at + 2);
                var n = U32(at + 4);
                // One sample fits in the entry; several are stored out of line, and a
                // bilevel image only ever has the one.
                if (n != 1) return 0;
                return type == 3 ? U16(at + 8) : (int)U32(at + 8);
            }
            // Absent means the format's default, which is 1 - a bilevel image.
            return 1;
        }
        catch { return 0; }
    }

    /// <summary>True when every pixel of a LOSSLESS image is pure black or pure white.
    /// Bails on the first pixel that is neither, so an ordinary colour image costs almost
    /// nothing. Only PNG is examined: a JPEG's own quantisation means it is never exactly
    /// two-valued, so scanning one would be wasted work.</summary>
    private static bool IsBilevelByPixels(byte[] d)
    {
        try
        {
            if (!Facades.PdfFileMend.IsPng(d)) return false;
            var (px, w, h, hasAlpha) = Facades.PdfFileMend.DecodePng(d);
            var comps = hasAlpha ? 4 : 3;
            if (w <= 0 || h <= 0 || px.Length < (long)w * h * comps) return false;
            for (var i = 0; i < w * h; i++)
            {
                var o = i * comps;
                byte r = px[o], g = px[o + 1], b = px[o + 2];
                var black = r == 0 && g == 0 && b == 0;
                var white = r == 255 && g == 255 && b == 255;
                if (!black && !white) return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>The managed half of <see cref="FromBlackWhite"/>: decode with the built-in
    /// PNG / JPEG / BMP readers and pack the same BT.601 luma threshold into a 1-bit image.
    /// Null when nothing can read the bytes, so the caller falls back to the ordinary
    /// colour embed rather than to a broken picture.</summary>
    private static ImageStamp? FromBlackWhiteManaged(byte[] imageData)
    {
        try
        {
            byte[] px; int w, h, comps;
            if (Facades.PdfFileMend.IsPng(imageData))
            {
                var (pixels, pw, ph, hasAlpha) = Facades.PdfFileMend.DecodePng(imageData);
                px = pixels; w = pw; h = ph; comps = hasAlpha ? 4 : 3;
            }
            else if (imageData[0] == 0xFF && imageData[1] == 0xD8)
            {
                var (pixels, jw, jh, jc) = IO.Filters.JpegDecoder.Decode(imageData);
                px = pixels; w = jw; h = jh; comps = jc;
            }
            else if (IO.BmpDecoder.IsBmp(imageData)
                     && IO.BmpDecoder.DecodeAsPng(imageData) is { } bmpPng)
            {
                var (pixels, bw, bh, hasAlpha) = Facades.PdfFileMend.DecodePng(bmpPng);
                px = pixels; w = bw; h = bh; comps = hasAlpha ? 4 : 3;
            }
            else return null;
            if (w <= 0 || h <= 0 || comps <= 0 || px.Length < (long)w * h * comps) return null;

            var rowBytes = (w + 7) / 8;              // packed 1-bpp row, byte-aligned
            var packed = new byte[rowBytes * h];
            for (var y = 0; y < h; y++)
            {
                var dstRow = y * rowBytes;
                for (var x = 0; x < w; x++)
                {
                    var at = (y * w + x) * comps;
                    int r, g, b, a = 255;
                    if (comps >= 3)
                    {
                        r = px[at]; g = px[at + 1]; b = px[at + 2];
                        if (comps >= 4) a = px[at + 3];
                    }
                    else { r = g = b = px[at]; }
                    if (a != 255)
                    {
                        // Flatten transparency onto white, as the cleared bitmap did.
                        var inv = 255 - a;
                        r = (r * a + 255 * inv + 127) / 255;
                        g = (g * a + 255 * inv + 127) / 255;
                        b = (b * a + 255 * inv + 127) / 255;
                    }
                    // ITU-R BT.601 luma; >=128 is white (bit set), else black (bit clear).
                    if ((r * 299 + g * 587 + b * 114) / 1000 >= 128)
                        packed[dstRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            return new ImageStamp(CompressFlate(packed), w, h, "DeviceGray", "FlateDecode", 1)
            {
                DisplayWidth = w,
                DisplayHeight = h,
            };
        }
        catch { return null; }
    }

    internal static ImageStamp? FromBlackWhite(byte[] imageData)
    {
        if (imageData is null || imageData.Length < 4) return null;
        // Off Windows the same threshold runs over managed-decoded pixels. Returning null
        // there meant IsBlackWhite quietly did nothing: the picture embedded as 8-bit
        // colour instead of the compact 1-bit image the property promises, which for a
        // scanned page is a forty-fold larger document.
        if (!OperatingSystem.IsWindows()) return FromBlackWhiteManaged(imageData);
        try
        {
#pragma warning disable CA1416
            using var ms = new MemoryStream(imageData);
            using var src = System.Drawing.Image.FromStream(ms);
            var w = src.Width;
            var h = src.Height;
            if (w <= 0 || h <= 0) return null;
            using var rgb = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(rgb))
            {
                // Flatten any transparency onto white so transparent areas stay white.
                g.Clear(System.Drawing.Color.White);
                g.DrawImage(src, 0, 0, w, h);
            }
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var bits = rgb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var stride = bits.Stride;
                var rowBytes = (w + 7) / 8;          // packed 1-bpp row, byte-aligned
                var packed = new byte[rowBytes * h];
                var srcPtr = bits.Scan0;
                for (var y = 0; y < h; y++)
                {
                    var srcOffset = y * stride;
                    var dstRow = y * rowBytes;
                    for (var x = 0; x < w; x++)
                    {
                        var b = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 0);
                        var g2 = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 1);
                        var r = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 2);
                        // ITU-R BT.601 luma; >=128 is white (bit set), else black (bit clear).
                        var lum = (r * 299 + g2 * 587 + b * 114) / 1000;
                        if (lum >= 128)
                            packed[dstRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    }
                }
                var compressed = CompressFlate(packed);
                return new ImageStamp(compressed, w, h, "DeviceGray", "FlateDecode", 1)
                {
                    DisplayWidth = w,
                    DisplayHeight = h
                };
            }
            finally { rgb.UnlockBits(bits); }
#pragma warning restore CA1416
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a 1-bit image stamp that embeds pre-encoded CCITT Group 4 (T.6) data
    /// directly as a CCITTFaxDecode XObject — used to pass a bilevel TIFF's existing
    /// G4 strip through to the PDF without re-encoding (the <see cref="Image.IsBlackWhite"/>
    /// fast path for fax/scan documents).
    /// </summary>
    internal static ImageStamp FromCcittG4(byte[] g4Data, int width, int height, bool blackIs1)
    {
        var parms = new PdfDictionary();
        parms.Set("K", new PdfInteger(-1));          // pure two-dimensional (G4 / T.6)
        parms.Set("Columns", new PdfInteger(width));
        parms.Set("Rows", new PdfInteger(height));
        if (blackIs1) parms.Set("BlackIs1", PdfBoolean.True);
        var stamp = new ImageStamp(g4Data, width, height, "DeviceGray", "CCITTFaxDecode", 1)
        {
            DisplayWidth = width,
            DisplayHeight = height,
            _decodeParms = parms
        };
        return stamp;
    }

    /// <summary>
    /// Create an image stamp from JPEG bytes. The JPEG data is embedded directly
    /// without re-encoding.
    /// </summary>
    public static ImageStamp FromJpeg(byte[] jpegData, int width, int height)
    {
        // A 4-component (CMYK) JPEG must be declared DeviceCMYK; and when it carries
        // an Adobe APP14 marker the samples are stored INVERTED (255 = no ink, the
        // Photoshop/libjpeg convention for standalone .jpg files), which the PDF
        // image dictionary expresses as /Decode [1 0 1 0 1 0 1 0]. PDF-embedded CMYK
        // DCT images use direct values, so only the file-import path needs this.
        var (comps, hasAdobe) = ParseJpegColorInfo(jpegData);
        var stamp = new ImageStamp(jpegData, width, height,
            comps == 4 ? "DeviceCMYK" : "DeviceRGB", "DCTDecode", 8)
        {
            DisplayWidth = width,
            DisplayHeight = height
        };
        if (comps == 4 && hasAdobe)
            stamp._decodeArray = new double[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        return stamp;
    }

    /// <summary>
    /// Create an image stamp from JPEG bytes, auto-detecting dimensions from the JPEG header.
    /// </summary>
    public static ImageStamp FromJpeg(byte[] jpegData)
    {
        var (w, h) = ParseJpegDimensions(jpegData);
        if (w == 0 || h == 0)
            throw new ArgumentException("Could not determine JPEG dimensions from header.");
        return FromJpeg(jpegData, w, h);
    }

    /// <summary>
    /// Create an image stamp from a JPEG file stream. Reads JPEG header for dimensions.
    /// </summary>
    public static ImageStamp FromJpegStream(Stream jpegStream)
    {
        using var ms = new MemoryStream();
        jpegStream.CopyTo(ms);
        var data = ms.ToArray();

        var (w, h) = ParseJpegDimensions(data);
        return FromJpeg(data, w, h);
    }

    /// <summary>Create a stamp from an encoded image file's bytes, picking the
    /// decoder by header the same way Page.AddImage does: JPEG and PNG embed
    /// natively; anything else goes through the GDI+ codec set. Throws when no
    /// decoder recognises the bytes.</summary>
    internal static ImageStamp FromEncodedBytes(byte[] imageData)
    {
        if (imageData.Length >= 2 && imageData[0] == 0xFF && imageData[1] == 0xD8)
            return FromJpeg(imageData);
        if (imageData.Length >= 4 && imageData[0] == 0x89 && imageData[1] == 0x50
            && imageData[2] == 0x4E && imageData[3] == 0x47)
            return FromPngData(imageData);
        if (OperatingSystem.IsWindows() && TryFromGdiPlusDecoder(imageData) is { } gdi)
            return gdi;
        if (TryFromManagedDecoder(imageData) is { } managed)
            return managed;
        ThrowIfWindowsOnlyMetafile(imageData);
        return FromPngData(imageData);
    }

    /// <summary>Last-resort image decoder: delegate to System.Drawing so any
    /// format the GDI+ codec set understands (GIF, TIFF, EMF, WMF, ICO, …)
    /// is converted to a raw-RGB ImageStamp. The bitmap is upgraded to 24bpp
    /// RGB if necessary (RGBA inputs drop the alpha channel for now — the
    /// stamp emitter writes /ColorSpace DeviceRGB without SMask).
    ///
    /// Returns null when no codec recognises the bytes — callers fall back
    /// to their format-specific error path.</summary>
    /// <summary>
    /// Parse a BMP file and return an ImageStamp. Handles the common 24-bit BGR and
    /// 32-bit BGRA Windows BMP variants (BITMAPINFOHEADER, BI_RGB, top-down or bottom-up)
    /// plus 8-bit and 4-bit paletted variants. RLE-compressed and OS/2 v1 fall back to
    /// ArgumentException. Lives here rather than on Page because every decode
    /// cascade needs it - Page.AddImage, the ImageStamp ctor and the resources
    /// collection all reach a BMP by a different route.
    /// </summary>
    internal static ImageStamp FromBmp(byte[] bmp)
    {
        if (bmp.Length < 54) throw new ArgumentException("BMP too small.");
        // File header: 'BM' (2) + size (4) + reserved (4) + offBits (4) = 14 bytes
        int offBits = bmp[10] | (bmp[11] << 8) | (bmp[12] << 16) | (bmp[13] << 24);
        // DIB header starts at 14: size (4) + width (4) + height (4) + planes (2) + bpp (2) + comp (4) + …
        int dibSize = bmp[14] | (bmp[15] << 8) | (bmp[16] << 16) | (bmp[17] << 24);
        int width = bmp[18] | (bmp[19] << 8) | (bmp[20] << 16) | (bmp[21] << 24);
        int heightRaw = bmp[22] | (bmp[23] << 8) | (bmp[24] << 16) | (bmp[25] << 24);
        bool topDown = heightRaw < 0;
        int height = topDown ? -heightRaw : heightRaw;
        int bpp = bmp[28] | (bmp[29] << 8);
        int compression = bmp[30] | (bmp[31] << 8) | (bmp[32] << 16) | (bmp[33] << 24);
        int paletteSize = bmp[46] | (bmp[47] << 8) | (bmp[48] << 16) | (bmp[49] << 24);
        if (width <= 0 || height <= 0)
            throw new ArgumentException("BMP has zero or negative dimensions.");
        if (compression != 0)
            throw new ArgumentException("Compressed BMP variants (RLE / BI_BITFIELDS) not supported.");
        if (bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32)
            throw new ArgumentException($"BMP bit-depth {bpp} not supported (4 / 8 / 24 / 32 only).");

        // Paletted BMPs: read color table starting at 14 + dibSize. Each entry is BGRA, 4 bytes.
        byte[]? palette = null;
        if (bpp <= 8)
        {
            int paletteOff = 14 + dibSize;
            int entries = paletteSize > 0 ? paletteSize : (1 << bpp);
            if (paletteOff + entries * 4 > bmp.Length)
                throw new ArgumentException("BMP palette truncated.");
            palette = new byte[entries * 3];
            for (int i = 0; i < entries; i++)
            {
                int s = paletteOff + i * 4;
                // Palette stored as BGRA; we want RGB.
                palette[i * 3 + 0] = bmp[s + 2];
                palette[i * 3 + 1] = bmp[s + 1];
                palette[i * 3 + 2] = bmp[s + 0];
            }
        }

        // BMP rows are padded to a 4-byte boundary.
        int srcStride = ((width * bpp + 31) / 32) * 4;
        if (offBits + srcStride * height > bmp.Length)
            throw new ArgumentException("BMP pixel data truncated.");

        var rgb = new byte[width * height * 3];
        for (int row = 0; row < height; row++)
        {
            // Bottom-up: file row (height-1-row) is what we read for output row (row).
            int srcRow = topDown ? row : (height - 1 - row);
            int srcRowOff = offBits + srcRow * srcStride;
            int dstRowOff = row * width * 3;
            for (int col = 0; col < width; col++)
            {
                int idx;
                if (bpp == 24 || bpp == 32)
                {
                    int s = srcRowOff + col * (bpp / 8);
                    rgb[dstRowOff + col * 3 + 0] = bmp[s + 2];
                    rgb[dstRowOff + col * 3 + 1] = bmp[s + 1];
                    rgb[dstRowOff + col * 3 + 2] = bmp[s + 0];
                    continue;
                }
                else if (bpp == 8)
                {
                    idx = bmp[srcRowOff + col];
                }
                else // bpp == 4
                {
                    int packed = bmp[srcRowOff + col / 2];
                    idx = (col & 1) == 0 ? (packed >> 4) : (packed & 0x0F);
                }
                int p = idx * 3;
                rgb[dstRowOff + col * 3 + 0] = palette![p + 0];
                rgb[dstRowOff + col * 3 + 1] = palette[p + 1];
                rgb[dstRowOff + col * 3 + 2] = palette[p + 2];
            }
        }
        return FromRgb(rgb, width, height);
    }

    /// <summary>Decode a raster the JPEG/PNG branches did not claim using the library's own
    /// managed decoders - BMP, GIF and TIFF. It is what stands in for the GDI+ codec set off
    /// Windows, where <see cref="TryFromGdiPlusDecoder"/> cannot run at all: without it a
    /// <c>new ImageStamp("logo.gif")</c> reported "Invalid PNG data" on Linux and macOS.
    /// Windows still goes through GDI+ first, so no already-calibrated output moves.
    /// A multi-frame TIFF contributes its first frame, as the GDI+ path does.
    /// Returns null when no managed decoder recognises the bytes.</summary>
    internal static ImageStamp? TryFromManagedDecoder(byte[] imageData)
    {
        if (imageData is null || imageData.Length < 4) return null;
        try
        {
            if (IO.GifDecoder.TryDecode(imageData, out var gifRgb, out var gifAlpha, out var gw, out var gh))
            {
                var stamp = FromRgb(gifRgb, gw, gh);
                foreach (var a in gifAlpha)
                    if (a != 255) { stamp._smaskData = CompressFlate(gifAlpha); break; }
                return stamp;
            }
            if (IO.TiffDecoder.IsTiff(imageData)
                && IO.TiffDecoder.DecodeFramesAsPng(imageData) is { Count: > 0 } frames)
                return FromPngData(frames[0]);
            if (imageData.Length > 2 && imageData[0] == (byte)'B' && imageData[1] == (byte)'M')
                return FromBmp(imageData);
        }
        catch { /* an unreadable raster falls to the caller's own error path */ }
        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static ImageStamp? TryFromGdiPlusDecoder(byte[] imageData)
    {
        if (imageData is null || imageData.Length < 4) return null;
        try
        {
            using var ms = new MemoryStream(imageData);
            using var src = System.Drawing.Image.FromStream(ms);
            var w = src.Width;
            var h = src.Height;
            // Decode into 32bpp ARGB so a transparent GIF/PNG keeps its alpha.
            using var argb = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(argb))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.DrawImage(src, 0, 0, w, h);
            }
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var bits = argb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var stride = bits.Stride;
                var rgbBytes = new byte[w * h * 3];
                var alpha = new byte[w * h];
                var hasAlpha = false;
                var srcPtr = bits.Scan0;
                for (var y = 0; y < h; y++)
                {
                    var srcOffset = y * stride;
                    // GDI+ Format32bppArgb is BGRA in memory; swap to RGB + alpha.
                    for (var x = 0; x < w; x++)
                    {
                        var b = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 0);
                        var g2 = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 1);
                        var r = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 2);
                        var a = System.Runtime.InteropServices.Marshal.ReadByte(srcPtr, srcOffset + x * 4 + 3);
                        var pi = y * w + x;
                        rgbBytes[pi * 3 + 0] = r;
                        rgbBytes[pi * 3 + 1] = g2;
                        rgbBytes[pi * 3 + 2] = b;
                        alpha[pi] = a;
                        if (a != 255) hasAlpha = true;
                    }
                }
                var stamp = FromRgb(rgbBytes, w, h);
                if (hasAlpha) stamp._smaskData = CompressFlate(alpha);
                return stamp;
            }
            finally { argb.UnlockBits(bits); }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create an image stamp from PNG file data. Decodes PNG to raw RGB pixels.
    /// </summary>
    public static ImageStamp FromPngData(byte[] pngData)
    {
        // Parse PNG IHDR for dimensions and color type
        if (pngData.Length < 24 || pngData[0] != 0x89 || pngData[1] != 0x50)
            throw new ArgumentException("Invalid PNG data");

        int ReadInt32BE(byte[] d, int offset) =>
            (d[offset] << 24) | (d[offset + 1] << 16) | (d[offset + 2] << 8) | d[offset + 3];

        var width = ReadInt32BE(pngData, 16);
        var height = ReadInt32BE(pngData, 20);
        var bitDepth = pngData[24];
        var colorType = pngData[25];

        // Collect all IDAT chunks, plus the PLTE palette and the tRNS per-index
        // alpha table for indexed-colour PNGs.
        var idatData = new MemoryStream();
        byte[]? palette = null;
        byte[]? trns = null;
        var pos = 8; // skip signature
        while (pos + 8 < pngData.Length)
        {
            var chunkLen = ReadInt32BE(pngData, pos);
            var chunkType = System.Text.Encoding.ASCII.GetString(pngData, pos + 4, 4);
            if (chunkType == "IDAT")
                idatData.Write(pngData, pos + 8, chunkLen);
            else if (chunkType == "PLTE" && pos + 8 + chunkLen <= pngData.Length)
            {
                palette = new byte[chunkLen];
                Array.Copy(pngData, pos + 8, palette, 0, chunkLen);
            }
            else if (chunkType == "tRNS" && pos + 8 + chunkLen <= pngData.Length)
            {
                trns = new byte[chunkLen];
                Array.Copy(pngData, pos + 8, trns, 0, chunkLen);
            }
            else if (chunkType == "IEND")
                break;
            pos += 12 + chunkLen; // length + type + data + CRC
        }

        // Decompress (skip 2-byte zlib header)
        var compressed = idatData.ToArray();
        if (compressed.Length < 2)
            throw new ArgumentException("No IDAT data in PNG");

        byte[] rawScanlines;
        using (var input = new MemoryStream(compressed, 2, compressed.Length - 2))
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            deflate.CopyTo(output);
            rawScanlines = output.ToArray();
        }

        // Determine bytes per pixel and extract RGB data
        var channels = colorType switch
        {
            0 => 1,  // Grayscale
            2 => 3,  // RGB
            3 => 1,  // Indexed (palette) — one index sample per pixel; PNG filter bpp is 1
            4 => 2,  // Grayscale + Alpha
            6 => 4,  // RGBA
            _ => 3
        };

        // Indexed PNGs pack the sample at the image bit depth (1/2/4/8); every other
        // supported colour type here is byte-per-channel.
        var stride = colorType == 3
            ? (width * bitDepth + 7) / 8
            : width * channels;
        var rgb = new byte[width * height * 3];

        // Alpha channel for the truecolour/grayscale+alpha types is split out into a
        // DeviceGray soft mask so transparent pixels show the page behind instead of
        // rendering as black. Built only when the source actually carries alpha:
        // an alpha channel (4/6), or an indexed image's tRNS per-index table —
        // without which a transparent palette entry would paint its PLTE colour
        // (typically black) over the page.
        var hasAlphaChannel = colorType == 4 || colorType == 6;
        var indexedAlpha = colorType == 3 && trns is not null;
        var alpha = hasAlphaChannel || indexedAlpha ? new byte[width * height] : null;
        var anyTransparent = false;

        // Reverse PNG filtering and extract RGB
        var prevRow = new byte[stride];
        var curRow = new byte[stride];
        var scanPos = 0;

        for (var y = 0; y < height; y++)
        {
            if (scanPos >= rawScanlines.Length) break;
            var filterByte = rawScanlines[scanPos++];

            // Read filtered row
            var bytesToRead = Math.Min(stride, rawScanlines.Length - scanPos);
            Array.Copy(rawScanlines, scanPos, curRow, 0, bytesToRead);
            scanPos += stride;

            // Apply PNG filter
            for (var x = 0; x < stride; x++)
            {
                byte a = x >= channels ? curRow[x - channels] : (byte)0;
                byte b = prevRow[x];
                byte c = x >= channels ? prevRow[x - channels] : (byte)0;

                curRow[x] = filterByte switch
                {
                    1 => (byte)(curRow[x] + a),             // Sub
                    2 => (byte)(curRow[x] + b),             // Up
                    3 => (byte)(curRow[x] + (a + b) / 2),   // Average
                    4 => (byte)(curRow[x] + PaethPredictor(a, b, c)), // Paeth
                    _ => curRow[x]                           // None
                };
            }

            // Convert to RGB
            for (var x = 0; x < width; x++)
            {
                var rgbIdx = (y * width + x) * 3;
                switch (colorType)
                {
                    case 0: // Grayscale
                        rgb[rgbIdx] = rgb[rgbIdx + 1] = rgb[rgbIdx + 2] = curRow[x];
                        break;
                    case 2: // RGB
                        rgb[rgbIdx] = curRow[x * 3];
                        rgb[rgbIdx + 1] = curRow[x * 3 + 1];
                        rgb[rgbIdx + 2] = curRow[x * 3 + 2];
                        break;
                    case 4: // Grayscale + Alpha
                        rgb[rgbIdx] = rgb[rgbIdx + 1] = rgb[rgbIdx + 2] = curRow[x * 2];
                        var ga = curRow[x * 2 + 1];
                        alpha![y * width + x] = ga;
                        if (ga != 255) anyTransparent = true;
                        break;
                    case 6: // RGBA
                        rgb[rgbIdx] = curRow[x * 4];
                        rgb[rgbIdx + 1] = curRow[x * 4 + 1];
                        rgb[rgbIdx + 2] = curRow[x * 4 + 2];
                        var ra = curRow[x * 4 + 3];
                        alpha![y * width + x] = ra;
                        if (ra != 255) anyTransparent = true;
                        break;
                    case 3: // Indexed: unpack the index at the image bit depth, look up /PLTE
                        int idx;
                        if (bitDepth == 8)
                            idx = curRow[x];
                        else
                        {
                            var bitPos = x * bitDepth;
                            var shift = 8 - bitDepth - (bitPos % 8);
                            idx = (curRow[bitPos / 8] >> shift) & ((1 << bitDepth) - 1);
                        }
                        var pi = idx * 3;
                        if (palette is not null && pi + 2 < palette.Length)
                        {
                            rgb[rgbIdx] = palette[pi];
                            rgb[rgbIdx + 1] = palette[pi + 1];
                            rgb[rgbIdx + 2] = palette[pi + 2];
                        }
                        if (indexedAlpha)
                        {
                            // tRNS lists alpha per palette index; indices past its
                            // end are fully opaque.
                            var ia = idx < trns!.Length ? trns[idx] : (byte)255;
                            alpha![y * width + x] = ia;
                            if (ia != 255) anyTransparent = true;
                        }
                        break;
                }
            }

            // Swap prev/cur
            (prevRow, curRow) = (curRow, prevRow);
        }

        var stamp = FromRgb(rgb, width, height);
        if (alpha is not null && anyTransparent)
            stamp.SetAlphaMask(alpha);
        return stamp;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
}
