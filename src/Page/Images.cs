using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Image XObjects on this page.</summary>
    public XImageCollection Images =>
        _images ??= new XImageCollection(_dict, _reader) { OwnerPage = this };

    /// <summary>
    /// Add an image to this page at the specified position and size.
    /// Supports JPEG and raw RGB pixel data.
    /// </summary>
    /// <param name="imageData">Image bytes (JPEG format or raw RGB pixels).</param>
    /// <param name="rect">Position and size rectangle (LLX, LLY, URX, URY).</param>
    /// <param name="blackWhite">When true, embed the image as a 1-bit black/white
    /// XObject (<see cref="Image.IsBlackWhite"/>) for a much smaller stream; falls
    /// back to the normal colour embed when the source can't be decoded.</param>
    public void AddImage(byte[] imageData, Rectangle rect, bool blackWhite = false)
        => AddImage(imageData, rect, blackWhite, aspectFit: false);

    // aspectFit: the file-path overload treats the rectangle as a bounding box (the
    // image keeps its own aspect ratio, centred); every other caller — the generator
    // flow, the HTML converter, direct byte[]/stream users — fills the rectangle
    // exactly as given.
    private void AddImage(byte[] imageData, Rectangle rect, bool blackWhite, bool aspectFit, Rectangle? clip = null)
    {
        var ia = new ImageAddState();
        ia.imageData = imageData;
        ia.rect = rect;
        ia.blackWhite = blackWhite;
        if (ia.blackWhite && ImageStamp.FromBlackWhite(ia.imageData) is { } bwStamp)
        {
            bwStamp.X = ia.rect.LLX;
            bwStamp.Y = ia.rect.LLY;
            bwStamp.DisplayWidth = ia.rect.Width;
            bwStamp.DisplayHeight = ia.rect.Height;
            bwStamp.CompensatePageRotation = true;
            bwStamp.ClipBox = clip;
            bwStamp.ApplyTo(this);
            return;
        }

        ia.isJpeg = ia.imageData.Length >= 2 && ia.imageData[0] == 0xFF && ia.imageData[1] == 0xD8;
        ia.isPng = ia.imageData.Length >= 4 && ia.imageData[0] == 0x89 && ia.imageData[1] == 0x50
                    && ia.imageData[2] == 0x4E && ia.imageData[3] == 0x47;
        ia.isBmp = ia.imageData.Length >= 2 && ia.imageData[0] == 0x42 && ia.imageData[1] == 0x4D;
        ia.isJpx = (ia.imageData.Length >= 12 && ia.imageData[0] == 0x00 && ia.imageData[1] == 0x00
                     && ia.imageData[2] == 0x00 && ia.imageData[3] == 0x0C && ia.imageData[4] == 0x6A
                     && ia.imageData[5] == 0x50 && ia.imageData[6] == 0x20 && ia.imageData[7] == 0x20)
                    || (ia.imageData.Length >= 4 && ia.imageData[0] == 0xFF && ia.imageData[1] == 0x4F
                        && ia.imageData[2] == 0xFF && ia.imageData[3] == 0x51);

        if (ia.isJpeg)
        {
            ia.stamp = ImageStamp.FromJpegStream(new MemoryStream(ia.imageData));
        }
        else if (ia.isPng)
        {
            // Embed PNG as a FlateDecode image with SMask for alpha
            ia.stamp = ImageStamp.FromPngData(ia.imageData);
        }
        else if (ia.isBmp)
        {
            ia.stamp = ImageStamp.FromBmp(ia.imageData);
        }
        else if (ia.isJpx
                 && Aspose.Pdf.IO.Filters.JpxDecoder.TryDecode(ia.imageData) is (var jxPx, var jxW, var jxH, var jxC)
                 && (jxC == 1 || jxC == 3))
        {
            // JPEG 2000 (.jp2/.jpx): GDI+/System.Drawing can't decode it, so decode to raw
            // samples with the built-in JPXDecode decoder and embed as a Flate RGB/Gray image.
            ia.stamp = jxC == 3 ? ImageStamp.FromRgb(jxPx, jxW, jxH) : ImageStamp.FromGrayscale(jxPx, jxW, jxH);
        }
        else
        {
            DecodeImageStamp(ia);
        }

        ia.dx = ia.rect.LLX;
        ia.dy = ia.rect.LLY;
        ia.dw = ia.rect.Width;
        ia.dh = ia.rect.Height;
        if (aspectFit && ia.stamp.PixelWidth > 0 && ia.stamp.PixelHeight > 0 && ia.dw > 0 && ia.dh > 0)
        {
            var scale = System.Math.Min(ia.dw / ia.stamp.PixelWidth, ia.dh / ia.stamp.PixelHeight);
            var fitW = ia.stamp.PixelWidth * scale;
            var fitH = ia.stamp.PixelHeight * scale;
            ia.dx += (ia.dw - fitW) / 2;
            ia.dy += (ia.dh - fitH) / 2;
            ia.dw = fitW; ia.dh = fitH;
        }
        ia.stamp.X = ia.dx;
        ia.stamp.Y = ia.dy;
        ia.stamp.DisplayWidth = ia.dw;
        ia.stamp.DisplayHeight = ia.dh;
        ia.stamp.CompensatePageRotation = true;
        ia.stamp.ClipBox = clip;
        ia.stamp.ApplyTo(this);
    }

    /// <summary>Place a pre-encoded CCITT Group 4 (1-bit) image at the given rectangle —
    /// the <see cref="Image.IsBlackWhite"/> fast path that embeds a bilevel TIFF's G4
    /// strip without re-encoding.</summary>
    internal void AddCcittImage(byte[] g4Data, int pixelWidth, int pixelHeight, bool blackIs1, Rectangle rect)
    {
        var stamp = ImageStamp.FromCcittG4(g4Data, pixelWidth, pixelHeight, blackIs1);
        stamp.X = rect.LLX;
        stamp.Y = rect.LLY;
        stamp.DisplayWidth = rect.Width;
        stamp.DisplayHeight = rect.Height;
        stamp.ApplyTo(this);
    }

    /// <summary>
    /// Add an image from a stream to this page at the specified position and size.
    /// </summary>
    public void AddImage(Stream imageStream, Rectangle rect)
    {
        if (imageStream is null) throw new ArgumentNullException(nameof(imageStream));
        // Callers commonly pass a stream they just wrote to (e.g.
        // 'bitmap.Save(image, Bmp); page.AddImage(image, ...);') — the
        // position sits at end-of-stream after the write, so a naive CopyTo
        // copies zero bytes and the byte[] overload throws 'Unsupported
        // image format'. Rewind seekable streams first.
        if (imageStream.CanSeek) imageStream.Position = 0;
        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        AddImage(ms.ToArray(), rect);
    }

    /// <summary>Add an image from a file path; the rectangle bounds the image, which
    /// keeps its own aspect ratio centred inside it.</summary>
    public void AddImage(string imagePath, Rectangle rectangle)
    {
        if (imagePath is null) throw new ArgumentNullException(nameof(imagePath));
        AddImage(File.ReadAllBytes(imagePath), rectangle, blackWhite: false, aspectFit: true);
    }

    /// <summary>Add an image placed at <paramref name="imageRect"/> and cropped to <paramref name="bbox"/>: the
    /// image fills its rectangle as usual and only the part inside the bounding box shows.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, Rectangle bbox)
        => AddImage(imageStream, imageRect, bbox, autoAdjustRectangle: false);

    /// <summary>Add an image placed at <paramref name="imageRect"/> and cropped to <paramref name="bbox"/>;
    /// <paramref name="autoAdjustRectangle"/> is recorded only.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, Rectangle bbox, bool autoAdjustRectangle)
    {
        if (imageStream is null) throw new ArgumentNullException(nameof(imageStream));
        _ = autoAdjustRectangle;
        // No box, no crop: `AddImage(stream, rect, null, false)` places the image whole, as it always did.
        if (imageStream.CanSeek) imageStream.Position = 0;
        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        AddImage(ms.ToArray(), imageRect, blackWhite: false, aspectFit: false, clip: bbox);
    }

    /// <summary>Add an image with explicit pixel size + proportion flag (bbox defaults to
    /// the image rectangle). Mirrors the public 5-argument overload used to control
    /// image resolution.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, int imageWidth, int imageHeight, bool saveImageProportions)
    {
        AddImage(imageStream, imageRect, imageWidth, imageHeight, saveImageProportions, imageRect);
    }

    /// <summary>Add an image with explicit pixel size + bbox. Stored only.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, int imageWidth, int imageHeight, bool saveImageProportions, Rectangle bbox)
    {
        _ = imageWidth; _ = imageHeight; _ = saveImageProportions; _ = bbox;
        AddImage(imageStream, imageRect);
    }

    /// <summary>Insert an image and overlay an HOCR (OCR) string as an invisible text
    /// layer (text rendering mode 3), so the page shows the image but its recognised
    /// text is searchable / copy-pasteable. Used to build a searchable image PDF.</summary>
    public void AddImage(string hocr, Stream imageStream, Rectangle imageRect)
    {
        AddImage(imageStream, imageRect);
        if (!string.IsNullOrEmpty(hocr))
            Document.OverlayHocrAsInvisibleText(this, hocr, mendModel: true);
    }

    /// <summary>Insert an image and overlay an HOCR (OCR) string as an invisible text
    /// layer; <paramref name="bbox"/> is accepted for API compatibility.</summary>
    public void AddImage(string hocr, Stream imageStream, Rectangle imageRect, Rectangle bbox)
    {
        _ = bbox;
        AddImage(hocr, imageStream, imageRect);
    }

    /// <summary>Fraction of the image's pixels that are not pure white (any
    /// channel below max), on the image's own pixel grid — the metric
    /// <see cref="IsBlank"/> uses. An inverting /Decode flips the interpretation.</summary>
    private double NonWhiteImageFraction(PdfDictionary dict, byte[] data, bool inline)
    {
        int W(string a, string b) => (int)(dict.Get(a) is not null ? dict.GetInt(a) : dict.GetInt(b));
        var w = W("Width", "W");
        var h = W("Height", "H");
        if (w <= 0 || h <= 0) return 0;
        var bpc = W("BitsPerComponent", "BPC");
        if (bpc == 0) bpc = 8;
        var filter = dict.GetName("Filter") ?? dict.GetName("F");
        var decodeInverts = (_reader.Resolve(dict.Get("Decode") ?? dict.Get("D")) is PdfArray da)
                            && da.Count >= 2 && CoverageNum(da[0]) > CoverageNum(da[1]);
        long total = (long)w * h;
        if (total <= 0) return 0;

        // Image mask / bilevel: fraction of painting (1-after-Decode) bits.
        var isMask = (dict.Get("ImageMask") ?? dict.Get("IM")) is PdfBoolean im && im.Value;

        long nonWhite = 0;
        switch (filter)
        {
            case "DCTDecode":
            case "DCT":
            {
                var (px, jw, jh, comps) = IO.Filters.JpegDecoder.Decode(data,
                    Devices.SoftwarePageRenderer.CmykDecodeInverts(dict));
                total = (long)jw * jh;
                var n = comps == 1 ? 1 : 3;
                for (long i = 0; i < total; i++)
                {
                    var o = i * n;
                    for (var c = 0; c < n; c++)
                        if (px[o + c] < 255) { nonWhite++; break; }
                }
                break;
            }
            case "JPXDecode":
            {
                if (IO.Filters.JpxDecoder.TryDecode(data) is not (var px, var jw, var jh, var comps))
                    return 0;
                total = (long)jw * jh;
                var n = comps >= 3 ? 3 : 1;
                for (long i = 0; i < total; i++)
                {
                    var o = i * n;
                    for (var c = 0; c < n; c++)
                        if (px[o + c] < 255) { nonWhite++; break; }
                }
                break;
            }
            // CCITTFaxDecode and JBIG2Decode are applied by DecodeStream itself and
            // arrive here as 1-bpc DeviceGray rasters (0 = black) — the raw default
            // below counts their ink bits.
            default:
            {
                // Raw samples (any stream-level filters already undone).
                var csObj = _reader.Resolve(dict.Get("ColorSpace") ?? dict.Get("CS"));
                var comps = csObj switch
                {
                    PdfName n2 when n2.Value is "DeviceRGB" or "RGB" or "CalRGB" => 3,
                    PdfName n2 when n2.Value is "DeviceCMYK" or "CMYK" => 4,
                    PdfArray => 1, // Indexed/ICC etc. — treat one component per sample
                    _ => 1,
                };
                if (isMask) comps = 1;
                if (bpc == 8)
                {
                    var rowLen = w * comps;
                    if ((long)rowLen * h > data.Length) return 0;
                    var whiteVal = decodeInverts ? 0 : 255;
                    for (long i = 0; i < total; i++)
                    {
                        var o = i * comps;
                        for (var c = 0; c < comps; c++)
                            if (data[o + c] != whiteVal) { nonWhite++; break; }
                    }
                }
                else if (bpc == 1 && comps == 1)
                {
                    // 1 = white for DeviceGray; an ImageMask's 1 = painted (per
                    // Decode). Count the ink bits.
                    var invert = isMask ? !decodeInverts : decodeInverts;
                    nonWhite = CountBits(data, w, h, invert: !invert);
                }
                else
                {
                    return 0; // exotic depths: no contribution rather than a guess
                }
                break;
            }
        }
        return total > 0 ? (double)nonWhite / total : 0;
    }

    /// <summary>Page background image. Stored only.</summary>
    public Image? BackgroundImage { get; set; }

    /// <summary>Decode a watermark's image XObject into a <see cref="System.Drawing.Image"/>.
    /// The backing stream is kept open (Image.FromStream requires it for the image's
    /// lifetime).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Image LoadWatermarkImage(XImage xi)
    {
        using var ms = new MemoryStream();
        xi.Save(ms);
        return System.Drawing.Image.FromStream(new MemoryStream(ms.ToArray()));
    }
}
