using System;
using System.Collections.Generic;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Describes one sampled image, however the operator that asked for it spelled its
/// parameters. The level-1 operators take their parameters on the stack and the
/// level-2 form takes a dictionary, but both describe the same thing.
/// </summary>
internal sealed class PsImageRequest
{
    /// <summary>Samples across.</summary>
    public int Width { get; set; }

    /// <summary>Samples down.</summary>
    public int Height { get; set; }

    /// <summary>Bits per component.</summary>
    public int BitsPerComponent { get; set; } = 8;

    /// <summary>Components per sample: 1 grey, 3 RGB, 4 CMYK.</summary>
    public int Components { get; set; } = 1;

    /// <summary>The matrix mapping user space onto the image's sample grid.</summary>
    public PsMatrix Matrix { get; set; } = PsMatrix.Identity;

    /// <summary>Whether this is a stencil painted in the current colour.</summary>
    public bool IsMask { get; set; }

    /// <summary>For a stencil, whether a set bit paints or leaves the page alone.</summary>
    public bool MaskInvert { get; set; }

    /// <summary>The sources the samples are read from, one per component when the
    /// program supplies them separately.</summary>
    public List<PsValue> Sources { get; } = new();

    /// <summary>The decode array, when the program gave one.</summary>
    public double[]? Decode { get; set; }
}

/// <summary>
/// The sampled-image operators. Samples are pulled by running the program's own data
/// source, converted to the eight-bit device RGB the reference converter writes, and
/// drawn as an image XObject under the placement the image matrix implies.
/// </summary>
internal static class PsImages
{
    /// <summary>The one sample value a bit depth of n can hold, less one.</summary>
    private static int MaxValue(int bits) => (1 << bits) - 1;

    /// <summary>Components a device RGB sample carries.</summary>
    private const int RgbComponents = 3;

    /// <summary>The bit depth every emitted image uses.</summary>
    private const int OutputBits = 8;

    /// <summary>A sample count beyond which an image is refused rather than allowed to
    /// exhaust memory on a malformed source.</summary>
    private const long MaxSamples = 64L * 1024 * 1024;

    /// <summary>Read the operands of <c>image</c>, which is either the level-1 form
    /// with its parameters on the stack or the level-2 form with a dictionary.</summary>
    public static void ImageOperator(PsInterpreter i)
    {
        var request = i.Peek().AsDictionary != null
            ? FromDictionary(i, i.PopDict())
            : FromStack(i, components: 1);
        Draw(i, request);
    }

    /// <summary><c>imagemask</c>: a stencil painted in the current colour.</summary>
    public static void ImageMaskOperator(PsInterpreter i)
    {
        PsImageRequest request;
        if (i.Peek().AsDictionary != null)
        {
            request = FromDictionary(i, i.PopDict());
        }
        else
        {
            var source = i.Pop();
            var matrix = MatrixOperand(i);
            var invert = i.PopBool();
            var height = i.PopCount();
            var width = i.PopCount();
            request = new PsImageRequest
            {
                Width = width, Height = height, BitsPerComponent = 1,
                Components = 1, Matrix = matrix, MaskInvert = invert,
            };
            request.Sources.Add(source);
        }

        request.IsMask = true;
        request.BitsPerComponent = 1;
        Draw(i, request);
    }

    /// <summary><c>colorimage</c>: like <c>image</c> but with a stated component
    /// count, and optionally one data source per component.</summary>
    public static void ColorImageOperator(PsInterpreter i)
    {
        var components = i.PopCount();
        var multiple = i.PopBool();
        var sources = new List<PsValue>();
        var count = multiple ? Math.Max(1, components) : 1;
        for (var k = 0; k < count; k++) sources.Insert(0, i.Pop());
        var matrix = MatrixOperand(i);
        var bits = i.PopCount();
        var height = i.PopCount();
        var width = i.PopCount();
        var request = new PsImageRequest
        {
            Width = width, Height = height, BitsPerComponent = bits,
            Components = Math.Max(1, components), Matrix = matrix,
        };
        request.Sources.AddRange(sources);
        Draw(i, request);
    }

    /// <summary>The level-1 operand order: width, height, bits, matrix, source.</summary>
    private static PsImageRequest FromStack(PsInterpreter i, int components)
    {
        var source = i.Pop();
        var matrix = MatrixOperand(i);
        var bits = i.PopCount();
        var height = i.PopCount();
        var width = i.PopCount();
        var request = new PsImageRequest
        {
            Width = width, Height = height, BitsPerComponent = bits,
            Components = components, Matrix = matrix,
        };
        request.Sources.Add(source);
        return request;
    }

    private static PsMatrix MatrixOperand(PsInterpreter i)
    {
        var value = i.Pop();
        return value.AsArray != null
            ? PsInterpreter.MatrixFromArray(value.AsArray)
            : PsMatrix.Identity;
    }

    /// <summary>The level-2 image dictionary.</summary>
    private static PsImageRequest FromDictionary(PsInterpreter i, PsDictionary dict)
    {
        var request = new PsImageRequest
        {
            Width = IntEntry(dict, "Width"),
            Height = IntEntry(dict, "Height"),
            BitsPerComponent = Math.Max(1, IntEntry(dict, "BitsPerComponent")),
            Components = ComponentsOf(i, dict),
            IsMask = dict.TryGet("ImageMask", out var mask) && mask.ToBool(),
        };
        if (dict.TryGet("ImageMatrix", out var m) && m.AsArray != null)
            request.Matrix = PsInterpreter.MatrixFromArray(m.AsArray);
        if (dict.TryGet("Decode", out var d) && d.AsArray != null)
            request.Decode = Numbers(d.AsArray);
        if (dict.TryGet("DataSource", out var source))
        {
            if (source.AsArray != null && !source.IsExecutable)
                for (var k = 0; k < source.AsArray.Length; k++)
                    request.Sources.Add(source.AsArray[k]);
            else request.Sources.Add(source);
        }

        if (request.IsMask)
        {
            request.BitsPerComponent = 1;
            request.MaskInvert = request.Decode != null && request.Decode.Length > 0 &&
                                 request.Decode[0] == 1;
        }

        return request;
    }

    /// <summary>How many components a level-2 image's samples carry: from the stated
    /// space when there is one, and one for a stencil.</summary>
    private static int ComponentsOf(PsInterpreter i, PsDictionary dict)
    {
        if (dict.TryGet("ImageMask", out var mask) && mask.ToBool()) return 1;
        if (!dict.TryGet("Decode", out var d) || d.AsArray is null)
            return ComponentsFor(i.Graphics.State.Space);
        return Math.Max(1, d.AsArray.Length / 2);
    }

    private static int ComponentsFor(PsColorSpace space) => space switch
    {
        PsColorSpace.Rgb => 3,
        PsColorSpace.Cmyk => 4,
        _ => 1,
    };

    private static int IntEntry(PsDictionary dict, string key) =>
        dict.TryGet(key, out var v) && v.IsNumber ? v.ToInt() : 0;

    private static double[] Numbers(PsArray array)
    {
        var values = new double[array.Length];
        for (var k = 0; k < array.Length; k++)
            values[k] = array[k].IsNumber ? array[k].Number : 0;
        return values;
    }

    /// <summary>Pull the samples, convert them and draw the image.</summary>
    private static void Draw(PsInterpreter i, PsImageRequest request)
    {
        if (request.Width <= 0 || request.Height <= 0) return;
        var perRow = (long)request.Width * request.Components * request.BitsPerComponent;
        var needed = (perRow + 7) / 8 * request.Height;
        if (needed <= 0 || needed > MaxSamples) return;
        var raw = PsImageData.Read(i, request, (int)needed);
        if (raw is null) return;
        // The samples are read either way — the program's own source is positioned past
        // them — but under the null device nothing is kept.
        if (!i.Graphics.Marking) return;
        var xobject = request.IsMask
            ? BuildStencil(request, raw)
            : BuildImage(request, raw);
        if (xobject is null) return;
        var name = i.Graphics.Writer.NextImageName();
        i.Graphics.Writer.AddResource("XObject", name, xobject);
        i.Graphics.Writer.DrawXObject(Placement(i, request), name, i.Graphics.State);
    }

    /// <summary>Where the image lands: the image matrix maps user space onto the
    /// sample grid, so its inverse maps the grid back, and the unit square a PDF image
    /// occupies is the grid scaled down by the sample counts.</summary>
    private static PsMatrix Placement(PsInterpreter i, PsImageRequest request)
    {
        var toUnit = PsMatrix.Scaling(request.Width, -request.Height)
            .Concat(PsMatrix.Translation(0, request.Height));
        return toUnit.Concat(request.Matrix.Invert()).Concat(i.Graphics.State.Ctm);
    }

    /// <summary>An ordinary image, written as eight-bit device RGB.</summary>
    private static PdfStream? BuildImage(PsImageRequest request, byte[] raw)
    {
        var rgb = PsImageData.ToRgb(request, raw);
        if (rgb is null) return null;
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("Width", new PdfInteger(request.Width));
        dict.Set("Height", new PdfInteger(request.Height));
        dict.Set("ColorSpace", new PdfName("DeviceRGB"));
        dict.Set("BitsPerComponent", new PdfInteger(OutputBits));
        return Compressed(dict, rgb);
    }

    /// <summary>A stencil: one bit per sample, painted in the current fill colour.
    /// The decode array carries the sense the program asked for.</summary>
    private static PdfStream BuildStencil(PsImageRequest request, byte[] raw)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("Width", new PdfInteger(request.Width));
        dict.Set("Height", new PdfInteger(request.Height));
        dict.Set("ImageMask", PdfBoolean.True);
        dict.Set("BitsPerComponent", new PdfInteger(1));
        dict.Set("Decode", new PdfArray(new List<PdfObject>
        {
            new PdfInteger(request.MaskInvert ? 1 : 0),
            new PdfInteger(request.MaskInvert ? 0 : 1),
        }));
        return Compressed(dict, raw);
    }

    private static PdfStream Compressed(PdfDictionary dict, byte[] data)
    {
        var packed = IO.Filters.ManagedDeflater.DeflateZlib(data);
        dict.Set("Filter", new PdfName("FlateDecode"));
        dict.Set("Length", new PdfInteger(packed.Length));
        return new PdfStream(dict, packed);
    }

    /// <summary>The largest value a component of the given depth holds.</summary>
    public static int Ceiling(int bits) => MaxValue(bits);

    /// <summary>Components a device RGB sample carries.</summary>
    public static int RgbWidth => RgbComponents;
}
