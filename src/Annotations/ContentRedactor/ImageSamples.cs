using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    // A JPEG or JPEG 2000 image is written back as a JPEG of this quality: its size stays in the
    // class of the original, where lossless samples of a scanned page would weigh several times more.
    private const int JpegQuality = 85;
    // The weights of red, green and blue in the gray the fill colour takes in a gray image (Rec. 601).
    private const double LumaRed = 0.299;
    private const double LumaGreen = 0.587;
    private const double LumaBlue = 0.114;

    /// <summary>The colour families whose samples the fill colour can be written in.</summary>
    private enum Family { Other, Gray, Rgb, Cmyk }

    /// <summary>The image dictionary (with its new filter) and encoded samples of an image whose
    /// pixels meeting <paramref name="area"/> (unit-square space) are painted: in the fill colour
    /// for an image, opaque for a mask of one, unpainted for a stencil. Null when its samples
    /// cannot be read.</summary>
    private (PdfDictionary Dict, byte[] Data)? Painted(PdfDictionary source, byte[] decoded,
        List<(double X, double Y)> area, ResourceCopy resources, bool isMask, byte[]? encoded = null)
    {
        var dict = Shallow(source);
        foreach (var key in new[] { "Filter", "DecodeParms", "Length" }) dict.Remove(key);
        var filter = LastFilter(source);
        try
        {
            if (filter is "DCTDecode" or "JPXDecode")
                return PaintedPhoto(dict, decoded, filter, area, isMask);
        }
        catch (Exception) { return null; }

        var stencil = source.GetBool("ImageMask");
        var (components, family) = stencil ? (1, Family.Other) : SpaceOf(source.Get("ColorSpace"), resources);
        var depth = stencil ? 1 : Int(source, "BitsPerComponent");
        int width = Int(source, "Width"), height = Int(source, "Height");
        var stride = (width * components * depth + 7) / 8;
        if (width <= 0 || height <= 0 || components <= 0 || depth is not (1 or 2 or 4 or 8 or 16)
            || decoded.Length < (long)stride * height)
            return null;

        var samples = (byte[])decoded.Clone();
        var values = FillSamples(source, family, components, depth, stencil, isMask);
        (int Left, int Top, int Right, int Bottom) changed = (int.MaxValue, int.MaxValue, -1, -1);
        foreach (var (row, first, last) in CoveredPixels(width, height, area))
        {
            changed = (Math.Min(changed.Left, first), Math.Min(changed.Top, row),
                Math.Max(changed.Right, last), Math.Max(changed.Bottom, row));
            for (var column = first; column <= last; column++)
                for (var c = 0; c < components; c++)
                    WriteSample(samples, (long)row * stride * 8 + ((long)column * components + c) * depth, depth, values[c]);
        }

        if (components == 1 && depth == 1)
        {
            // Bilevel scans stay bilevel: JBIG2 when they were, else CCITT G4. Both encoders take 1
            // for black, where samples hold 0. A JBIG2 scan keeps what lies clear of the painted
            // pixels as it was coded (its symbol dictionaries - a scanned page is mostly those);
            // failing that, it is coded whole, a quarter smaller than G4.
            var packed = samples.Select(b => (byte)~b).ToArray();
            if (filter == "JBIG2Decode")
            {
                dict.Set("Filter", new PdfName("JBIG2Decode"));
                var jbig2Parms = Jbig2Parms(source);
                var globals = Jbig2Globals(jbig2Parms);
                if (encoded is not null && changed.Right >= 0
                    && Jbig2Decoder.Analyze(encoded, globals) is { } layout
                    && Jbig2Encoder.Rewrite(layout, globals, packed,
                        (changed.Left, changed.Top, changed.Right - changed.Left + 1, changed.Bottom - changed.Top + 1))
                        is { } rewritten)
                {
                    if (jbig2Parms is not null) dict.Set("DecodeParms", jbig2Parms);
                    return (dict, rewritten);
                }
                return (dict, Jbig2Encoder.Encode(packed, width, height, stride));
            }
            var parms = new PdfDictionary();
            parms.Set("K", new PdfInteger(-1));
            parms.Set("Columns", new PdfInteger(width));
            parms.Set("Rows", new PdfInteger(height));
            dict.Set("Filter", new PdfName("CCITTFaxDecode"));
            dict.Set("DecodeParms", parms);
            return (dict, CcittG4Encoder.Encode(packed, width, height, stride));
        }
        dict.Set("Filter", new PdfName("FlateDecode"));
        return (dict, ManagedDeflater.DeflateZlib(samples));
    }

    /// <summary>A JPEG or JPEG 2000 image decoded, painted and written back as a JPEG (a mask as
    /// 8-bit gray samples).</summary>
    private (PdfDictionary Dict, byte[] Data)? PaintedPhoto(PdfDictionary dict, byte[] decoded, string filter,
        List<(double X, double Y)> area, bool isMask)
    {
        (byte[], int, int, int)? found = filter == "DCTDecode" ? JpegDecoder.Decode(decoded) : JpxDecoder.TryDecode(decoded);
        if (found is not { } decodedPhoto) return null;
        var (pixels, width, height, components) = decodedPhoto;
        if (components is not (1 or 3 or 4)) return null;
        if (pixels.Length < (long)width * height * components) return null;

        var rgba = new byte[(long)width * height * 4];
        for (long i = 0, n = (long)width * height; i < n; i++)
        {
            var (r, g, b) = Rgb(pixels, i * components, components);
            rgba[i * 4] = r;
            rgba[i * 4 + 1] = g;
            rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = 255;
        }
        var (fr, fg, fb) = isMask ? ((byte)255, (byte)255, (byte)255) : (Byte(_fill.R), Byte(_fill.G), Byte(_fill.B));
        foreach (var (row, first, last) in CoveredPixels(width, height, area))
            for (var column = first; column <= last; column++)
            {
                var at = ((long)row * width + column) * 4;
                (rgba[at], rgba[at + 1], rgba[at + 2]) = (fr, fg, fb);
            }

        foreach (var key in new[] { "Decode", "SMaskInData" }) dict.Remove(key);
        dict.Set("Width", new PdfInteger(width));
        dict.Set("Height", new PdfInteger(height));
        dict.Set("BitsPerComponent", new PdfInteger(8));
        if (isMask)
        {
            var gray = new byte[(long)width * height];
            for (long i = 0; i < gray.Length; i++) gray[i] = rgba[i * 4];
            dict.Set("ColorSpace", new PdfName("DeviceGray"));
            dict.Set("Filter", new PdfName("FlateDecode"));
            return (dict, ManagedDeflater.DeflateZlib(gray));
        }
        dict.Set("ColorSpace", new PdfName("DeviceRGB"));
        dict.Set("Filter", new PdfName("DCTDecode"));
        return (dict, IO.JpegEncoderImpl.Encode(rgba, width, height, JpegQuality));
    }

    private static (byte R, byte G, byte B) Rgb(byte[] pixels, long at, int components) => components switch
    {
        1 => (pixels[at], pixels[at], pixels[at]),
        3 => (pixels[at], pixels[at + 1], pixels[at + 2]),
        _ => ((byte)((255 - pixels[at]) * (255 - pixels[at + 3]) / 255),
              (byte)((255 - pixels[at + 1]) * (255 - pixels[at + 3]) / 255),
              (byte)((255 - pixels[at + 2]) * (255 - pixels[at + 3]) / 255)),
    };

    private static byte Byte(double unit) => (byte)Math.Round(Math.Max(0, Math.Min(1, unit)) * 255);

    /// <summary>What each component of a painted pixel holds: the fill colour in a gray, RGB or
    /// CMYK image; full opacity in a soft mask; "paint" in a stencil used as a mask and "leave
    /// unpainted" in a stencil drawn on its own; zero where the colour space has no such colour.
    /// A Decode array running from high to low reads the values backwards.</summary>
    private int[] FillSamples(PdfDictionary source, Family family, int components, int depth, bool stencil, bool isMask)
    {
        var max = (1 << depth) - 1;
        double[] units = (stencil, isMask, family) switch
        {
            (true, true, _) => [0],
            (true, false, _) => [1],
            (false, true, _) => [1],
            (_, _, Family.Gray) => [LumaRed * _fill.R + LumaGreen * _fill.G + LumaBlue * _fill.B],
            (_, _, Family.Rgb) => [_fill.R, _fill.G, _fill.B],
            (_, _, Family.Cmyk) => Cmyk(_fill.R, _fill.G, _fill.B),
            _ => new double[components],
        };
        var values = new int[components];
        var decode = _reader.Resolve(source.Get("Decode")) as PdfArray;
        for (var c = 0; c < components && c < units.Length; c++)
        {
            var unit = units[c];
            if (decode is { } d && d.Count >= 2 * c + 2 && Real(d[2 * c]) > Real(d[2 * c + 1])) unit = 1 - unit;
            values[c] = (int)Math.Round(unit * max);
        }
        return values;
    }

    private static double[] Cmyk(double r, double g, double b)
    {
        var k = 1 - Math.Max(r, Math.Max(g, b));
        if (k >= 1) return [0, 0, 0, 1];
        return [(1 - r - k) / (1 - k), (1 - g - k) / (1 - k), (1 - b - k) / (1 - k), k];
    }

    private double Real(PdfObject value) => _reader.Resolve(value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static void WriteSample(byte[] samples, long bit, int depth, int value)
    {
        if (depth == 8)
        {
            samples[bit >> 3] = (byte)value;
            return;
        }
        if (depth == 16)
        {
            samples[bit >> 3] = (byte)(value >> 8);
            samples[(bit >> 3) + 1] = (byte)value;
            return;
        }
        for (var i = depth - 1; i >= 0; i--, bit++)
        {
            var mask = (byte)(0x80 >> (int)(bit & 7));
            if (((value >> i) & 1) != 0) samples[bit >> 3] |= mask;
            else samples[bit >> 3] &= (byte)~mask;
        }
    }

    /// <summary>The parameters of a JBIG2 image's filter (its globals), if it has any.</summary>
    private PdfDictionary? Jbig2Parms(PdfDictionary image) => _reader.Resolve(image.Get("DecodeParms")) switch
    {
        PdfDictionary parms => parms,
        PdfArray { Count: > 0 } array => _reader.ResolveDict(array[array.Count - 1]),
        _ => null,
    };

    private byte[]? Jbig2Globals(PdfDictionary? parms) =>
        _reader.Resolve(parms?.Get("JBIG2Globals")) is PdfStream globals ? _reader.DecodeStream(globals) : null;

    private string? LastFilter(PdfDictionary dict) => _reader.Resolve(dict.Get("Filter")) switch
    {
        PdfName name => name.Value,
        PdfArray { Count: > 0 } array => (_reader.Resolve(array[array.Count - 1]) as PdfName)?.Value,
        _ => null,
    };

    private int Int(PdfDictionary dict, string key) => (int)Real(dict.Get(key) ?? PdfNull.Instance);

    /// <summary>How many components a colour space's samples have (0 when unknown) and the family
    /// the fill colour is written in.</summary>
    private (int Components, Family Family) SpaceOf(PdfObject? space, ResourceCopy resources, bool named = false)
    {
        switch (_reader.Resolve(space))
        {
            case PdfName { Value: "DeviceGray" or "G" or "CalGray" }:
                return (1, Family.Gray);
            case PdfName { Value: "DeviceRGB" or "RGB" or "CalRGB" }:
                return (3, Family.Rgb);
            case PdfName { Value: "DeviceCMYK" or "CMYK" }:
                return (4, Family.Cmyk);
            case PdfName other when !named:
                return SpaceOf(resources.Dict("ColorSpace")?.Get(other.Value), resources, named: true);
            case PdfArray { Count: > 0 } array:
                return (_reader.Resolve(array[0]) as PdfName)?.Value switch
                {
                    "CalGray" => (1, Family.Gray),
                    "CalRGB" => (3, Family.Rgb),
                    "Lab" => (3, Family.Other),
                    "Indexed" or "I" or "Separation" => (1, Family.Other),
                    "ICCBased" when array.Count > 1 && _reader.Resolve(array[1]) is PdfStream icc => Int(icc.Dict, "N") switch
                    {
                        1 => (1, Family.Gray),
                        3 => (3, Family.Rgb),
                        4 => (4, Family.Cmyk),
                        var n => (n, Family.Other),
                    },
                    "DeviceN" when array.Count > 1 && _reader.Resolve(array[1]) is PdfArray names => (names.Count, Family.Other),
                    { } name => SpaceOf(new PdfName(name), resources, named: true),
                    _ => (0, Family.Other),
                };
            default:
                return (0, Family.Other);
        }
    }
}
