using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf;

/// <summary>The grey conversion of what carries colour outside the content operators: image samples,
/// indexed palettes, and the functions of shadings and shading patterns. Only what is converted is
/// relabelled: a grey colour space over RGB samples or palette indices is a broken image.</summary>
public sealed partial class RgbToDeviceGrayConversionStrategy
{
    // The quality an RGB JPEG image is encoded at again once its samples are grey.
    private const int GrayJpegQuality = 90;
    private const int RgbComponents = 3;
    private const int SampledBitsEight = 8;
    private const int SampledBitsSixteen = 16;

    // Appended to a PostScript calculator function that leaves r g b on the stack: their luminance.
    private const string PostScriptLuminance = " 0.114 mul exch 0.587 mul add exch 0.299 mul add ";

    /// <summary>An RGB image gets grey samples (a JPEG one is encoded again as a grey JPEG); an indexed
    /// image over RGB keeps its indices and gets a grey palette. An image whose samples cannot be read
    /// here - JPEG 2000, a depth other than 8 - keeps its colours.</summary>
    private static void ConvertImageToGray(PdfStream image, PdfReader reader)
    {
        var colorSpace = reader.Resolve(image.Dict.Get("ColorSpace"));
        if (IsRgbSpace(colorSpace, reader))
            ConvertRgbImage(image, reader);
        else if (GrayIndexedSpace(colorSpace, reader) is { } indexed)
            image.Dict.Set("ColorSpace", indexed);
    }

    private static bool IsRgbSpace(PdfObject? colorSpace, PdfReader reader) => colorSpace switch
    {
        PdfName name => name.Value is "DeviceRGB" or "CalRGB" or "RGB",
        PdfArray { Count: > 0 } array when array[0] is PdfName { Value: "CalRGB" } => true,
        PdfArray { Count: >= 2 } array when array[0] is PdfName { Value: "ICCBased" } =>
            reader.ResolveStream(array[1])?.Dict.GetInt("N", RgbComponents) == RgbComponents,
        _ => false,
    };

    private static void ConvertRgbImage(PdfStream image, PdfReader reader)
    {
        var dict = image.Dict;
        var width = (int)dict.GetInt("Width");
        var height = (int)dict.GetInt("Height");
        var pixelCount = width * height;
        if (pixelCount <= 0 || dict.GetInt("BitsPerComponent", 8) != 8)
            return;
        var decode = reader.ResolveArray(dict.Get("Decode"));
        if (decode is not null && decode.Count != RgbComponents * 2)
            return;
        var lastFilter = LastFilter(dict.Get("Filter"));
        if (lastFilter is "JPXDecode" or "JPX")
            return;
        var jpeg = lastFilter is "DCTDecode" or "DCT";
        byte[] samples;
        try
        {
            samples = reader.DecodeStream(image);
            if (jpeg)
            {
                var (pixels, jpegWidth, jpegHeight, components) = JpegDecoder.Decode(samples);
                if (components != RgbComponents || jpegWidth != width || jpegHeight != height)
                    return;
                samples = pixels;
            }
        }
        catch (Exception)
        {
            return;
        }
        if (samples.Length < pixelCount * RgbComponents)
            return;

        SampleDecode.RemapInPlace(samples, pixelCount * RgbComponents, RgbComponents, decode, reader);
        var gray = GraySamples(samples, pixelCount);
        var data = jpeg ? JpegEncoderImpl.EncodeGray(gray, width, height, GrayJpegQuality) : gray;
        image.ReplaceData(data);
        dict.Remove("DecodeParms");
        // The grey samples carry the remap already; an RGB /Decode has the wrong arity for them.
        dict.Remove("Decode");
        if (jpeg)
            dict.Set("Filter", new PdfName("DCTDecode"));
        else
            dict.Remove("Filter");
        dict.Set("Length", new PdfInteger(data.Length));
        dict.Set("ColorSpace", new PdfName("DeviceGray"));
    }

    private static string? LastFilter(PdfObject? filter) => filter switch
    {
        PdfName name => name.Value,
        PdfArray { Count: > 0 } chain => (chain[chain.Count - 1] as PdfName)?.Value,
        _ => null,
    };

    /// <summary>[/Indexed /DeviceGray hival lookup] for an indexed space over RGB: the same indices,
    /// each palette entry turned grey. Null for any other space or an unreadable palette.</summary>
    private static PdfArray? GrayIndexedSpace(PdfObject? colorSpace, PdfReader reader)
    {
        if (colorSpace is not PdfArray { Count: >= 4 } indexed || indexed[0] is not PdfName { Value: "Indexed" or "I" })
            return null;
        if (!IsRgbSpace(reader.Resolve(indexed[1]), reader) || reader.Resolve(indexed[2]) is not PdfInteger hival)
            return null;
        var lookup = reader.Resolve(indexed[3]) switch
        {
            PdfString text => text.Value,
            PdfStream stream => reader.DecodeStream(stream),
            _ => null,
        };
        if (lookup is null)
            return null;
        var entries = (int)hival.Value + 1;
        var gray = new byte[entries];
        var readable = Math.Min(entries, lookup.Length / RgbComponents);
        Array.Copy(GraySamples(lookup, readable), gray, readable);
        var space = new PdfArray();
        space.Add(new PdfName("Indexed"));
        space.Add(new PdfName("DeviceGray"));
        space.Add(new PdfInteger(hival.Value));
        space.Add(new PdfString(gray, isHex: true));
        return space;
    }

    /// <summary>The shadings and patterns of a resource dictionary: an axial, radial or function-based
    /// shading over RGB gets a grey function; a tiling pattern's content is converted as a form's is.
    /// Mesh shadings, whose colours sit in their vertex data, keep their colours.</summary>
    private static void ConvertShadingsAndPatterns(PdfDictionary resources, PdfReader reader)
    {
        if (reader.ResolveDict(resources.Get("Shading")) is { } shadings)
        {
            foreach (var key in shadings.Keys)
                ConvertShading(reader.Resolve(shadings.Get(key)), reader);
        }
        if (reader.ResolveDict(resources.Get("Pattern")) is not { } patterns)
            return;
        foreach (var key in patterns.Keys)
        {
            switch (reader.Resolve(patterns.Get(key)))
            {
                case PdfStream tiling:
                    ConvertFormXObject(tiling, reader);
                    break;
                case PdfDictionary shadingPattern:
                    ConvertShading(reader.Resolve(shadingPattern.Get("Shading")), reader);
                    break;
            }
        }
    }

    private static void ConvertShading(PdfObject? shading, PdfReader reader)
    {
        var dict = shading switch { PdfStream stream => stream.Dict, PdfDictionary d => d, _ => null };
        if (dict is null || !IsRgbSpace(reader.Resolve(dict.Get("ColorSpace")), reader))
            return;
        if (dict.GetInt("ShadingType") is < 1 or > 3)
            return;
        if (GrayFunction(reader.Resolve(dict.Get("Function")), reader) is not { } function)
            return;
        dict.Set("Function", function);
        dict.Set("ColorSpace", new PdfName("DeviceGray"));
        if (reader.ResolveArray(dict.Get("Background")) is { Count: RgbComponents } background)
            dict.Set("Background", GrayArray(background, reader));
    }

    /// <summary>A function giving the luminance of what an RGB function gives, or null when it cannot be
    /// built (an array of one function per component, an unreadable sampled function).</summary>
    private static PdfObject? GrayFunction(PdfObject? function, PdfReader reader)
    {
        var dict = function switch { PdfStream stream => stream.Dict, PdfDictionary d => d, _ => null };
        if (dict is null)
            return null;
        return dict.GetInt("FunctionType", -1) switch
        {
            2 => GrayExponential(dict, reader),
            3 => GrayStitching(dict, reader),
            0 when function is PdfStream sampled => GraySampled(sampled, reader),
            4 when function is PdfStream calculator => GrayCalculator(calculator, reader),
            _ => null,
        };
    }

    private static PdfDictionary? GrayExponential(PdfDictionary dict, PdfReader reader)
    {
        if (reader.ResolveArray(dict.Get("C0")) is not { Count: RgbComponents } c0
            || reader.ResolveArray(dict.Get("C1")) is not { Count: RgbComponents } c1)
            return null;
        var gray = CopyWithGrayRange(dict);
        gray.Set("C0", GrayArray(c0, reader));
        gray.Set("C1", GrayArray(c1, reader));
        return gray;
    }

    private static PdfDictionary? GrayStitching(PdfDictionary dict, PdfReader reader)
    {
        if (reader.ResolveArray(dict.Get("Functions")) is not { } functions)
            return null;
        var grayFunctions = new PdfArray();
        foreach (var item in functions)
        {
            if (GrayFunction(reader.Resolve(item), reader) is not { } gray)
                return null;
            grayFunctions.Add(gray);
        }
        var stitched = CopyWithGrayRange(dict);
        stitched.Set("Functions", grayFunctions);
        return stitched;
    }

    /// <summary>A sampled function's table with one grey sample in place of each r g b triple, over the
    /// range [0 1]; 8 and 16 bits per sample are read.</summary>
    private static PdfStream? GraySampled(PdfStream function, PdfReader reader)
    {
        var dict = function.Dict;
        var bits = (int)dict.GetInt("BitsPerSample");
        var range = reader.ResolveArray(dict.Get("Range"));
        var decode = reader.ResolveArray(dict.Get("Decode")) ?? range;
        if (bits is not (SampledBitsEight or SampledBitsSixteen) || decode is not { Count: RgbComponents * 2 })
            return null;
        byte[] data;
        try { data = reader.DecodeStream(function); }
        catch (Exception) { return null; }
        var bytesPerSample = bits / 8;
        var max = (1 << bits) - 1;
        var count = data.Length / (bytesPerSample * RgbComponents);
        var gray = new byte[count * bytesPerSample];
        var rgb = new double[RgbComponents];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < RgbComponents; c++)
            {
                var at = (i * RgbComponents + c) * bytesPerSample;
                var raw = bytesPerSample == 1 ? data[at] : data[at] << 8 | data[at + 1];
                var low = Num(reader.Resolve(decode[c * 2]) ?? PdfNull.Instance);
                var high = Num(reader.Resolve(decode[c * 2 + 1]) ?? PdfNull.Instance);
                rgb[c] = low + raw * (high - low) / max;
            }
            var value = (int)Math.Round(Math.Max(0, Math.Min(1, RgbToGray(rgb[0], rgb[1], rgb[2]))) * max);
            if (bytesPerSample == 1)
            {
                gray[i] = (byte)value;
            }
            else
            {
                gray[i * 2] = (byte)(value >> 8);
                gray[i * 2 + 1] = (byte)value;
            }
        }
        var grayDict = CopyWithGrayRange(dict);
        grayDict.Remove("Decode");
        grayDict.Remove("Filter");
        grayDict.Remove("DecodeParms");
        grayDict.Set("Length", new PdfInteger(gray.Length));
        return new PdfStream(grayDict, gray);
    }

    /// <summary>A PostScript calculator function whose program ends by turning r g b into luminance.</summary>
    private static PdfStream? GrayCalculator(PdfStream function, PdfReader reader)
    {
        string program;
        try { program = Compat.Latin1.GetString(reader.DecodeStream(function)); }
        catch (Exception) { return null; }
        var end = program.LastIndexOf('}');
        if (end < 0)
            return null;
        var gray = Compat.Latin1.GetBytes(program.Insert(end, PostScriptLuminance));
        var grayDict = CopyWithGrayRange(function.Dict);
        grayDict.Remove("Filter");
        grayDict.Remove("DecodeParms");
        grayDict.Set("Length", new PdfInteger(gray.Length));
        return new PdfStream(grayDict, gray);
    }

    // A copy of a function dictionary with its output range one grey component wide.
    private static PdfDictionary CopyWithGrayRange(PdfDictionary dict)
    {
        var copy = new PdfDictionary();
        foreach (var key in dict.Keys)
            copy.Set(key, dict.Get(key)!);
        if (copy.Get("Range") is not null)
        {
            var range = new PdfArray();
            range.Add(new PdfInteger(0));
            range.Add(new PdfInteger(1));
            copy.Set("Range", range);
        }
        return copy;
    }

    private static PdfArray GrayArray(PdfArray rgb, PdfReader reader)
    {
        var gray = new PdfArray();
        gray.Add(new PdfReal(RgbToGray(
            Num(reader.Resolve(rgb[0]) ?? PdfNull.Instance),
            Num(reader.Resolve(rgb[1]) ?? PdfNull.Instance),
            Num(reader.Resolve(rgb[2]) ?? PdfNull.Instance))));
        return gray;
    }
}
