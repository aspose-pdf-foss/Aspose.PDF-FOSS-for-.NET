using Aspose.Pdf.Core;

namespace Aspose.Pdf;

/// <summary>An image file stored under the filter a caller asks for (<see cref="XImageCollection.Add(Stream, ImageFilterType)"/>):
/// JPEG samples, CCITT G4 bilevel samples, or deflated samples, each with the picture's transparency as a soft mask.</summary>
internal static class ImageFilterEncoder
{
    // The library's standard re-encode quality.
    private const int JpegQuality = 90;
    // A pixel darker than this luminance is black in a bilevel image.
    private const int BlackBelow = 128;
    private const int LumaRedPerMille = 299;
    private const int LumaGreenPerMille = 587;
    private const int LumaBluePerMille = 114;
    private const int PerMille = 1000;

    /// <summary>The image XObject of <paramref name="file"/> stored under <paramref name="filter"/>, or null when no
    /// decoder reads the file or the filter asks for nothing these encoders do (JPEG 2000).</summary>
    internal static PdfStream? Encode(byte[] file, ImageFilterType filter)
    {
        if (filter is not (ImageFilterType.Jpeg or ImageFilterType.CCITTFax or ImageFilterType.Flate)) return null;
        if (ImageStamp.DecodeRaster(file) is not var (rgb, alpha, width, height)) return null;
        byte[] data;
        PdfDictionary dict;
        switch (filter)
        {
            case ImageFilterType.Jpeg:
                data = IO.JpegEncoderImpl.Encode((int x, int y) =>
                {
                    var o = ((long)y * width + x) * 3;
                    return (rgb[o], rgb[o + 1], rgb[o + 2]);
                }, width, height, JpegQuality);
                dict = ImageDict(width, height, "DeviceRGB", 8, "DCTDecode");
                break;
            case ImageFilterType.CCITTFax:
                var stride = (width + 7) / 8;
                data = IO.Filters.CcittG4Encoder.Encode(PackBilevel(rgb, 3, width, height), width, height, stride);
                dict = ImageDict(width, height, "DeviceGray", 1, "CCITTFaxDecode");
                dict.Set("DecodeParms", G4Parms(width, height));
                alpha = null;   // a bilevel image keeps no partial transparency
                break;
            default:
                data = IO.Filters.ManagedDeflater.DeflateSamples(rgb);
                dict = ImageDict(width, height, "DeviceRGB", 8, "FlateDecode");
                break;
        }
        if (alpha is not null && Array.Exists(alpha, a => a != byte.MaxValue))
        {
            var mask = IO.Filters.ManagedDeflater.DeflateSamples(alpha);
            var maskDict = ImageDict(width, height, "DeviceGray", 8, "FlateDecode");
            maskDict.Set("Length", new PdfInteger(mask.Length));
            dict.Set("SMask", new PdfStream(maskDict, mask));
        }
        dict.Set("Length", new PdfInteger(data.Length));
        return new PdfStream(dict, data);
    }

    /// <summary>Gray or RGB samples (<paramref name="components"/> each) thresholded to one bit per pixel, rows
    /// padded to a byte, bit 1 = black - as the CCITT and JBIG2 encoders take them.</summary>
    internal static byte[] PackBilevel(byte[] pixels, int components, int width, int height)
    {
        var stride = (width + 7) / 8;
        var packed = new byte[(long)stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((long)y * width + x) * components;
                var luma = components < 3
                    ? pixels[at]
                    : (pixels[at] * LumaRedPerMille + pixels[at + 1] * LumaGreenPerMille + pixels[at + 2] * LumaBluePerMille) / PerMille;
                if (luma < BlackBelow) packed[(long)y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return packed;
    }

    /// <summary>The CCITT parameters of a pure two-dimensional (Group 4) image.</summary>
    internal static PdfDictionary G4Parms(int width, int height)
    {
        var parms = new PdfDictionary();
        parms.Set("K", new PdfInteger(-1));
        parms.Set("Columns", new PdfInteger(width));
        parms.Set("Rows", new PdfInteger(height));
        return parms;
    }

    private static PdfDictionary ImageDict(int width, int height, string colorSpace, int bitsPerComponent, string filter)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("Width", new PdfInteger(width));
        dict.Set("Height", new PdfInteger(height));
        dict.Set("BitsPerComponent", new PdfInteger(bitsPerComponent));
        dict.Set("ColorSpace", new PdfName(colorSpace));
        dict.Set("Filter", new PdfName(filter));
        return dict;
    }
}
