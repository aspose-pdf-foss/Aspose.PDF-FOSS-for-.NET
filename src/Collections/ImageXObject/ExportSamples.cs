using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public partial class ImageXObject
{
    private const int EightBits = 8;
    private const int SixteenBits = 16;
    private const int MaxByte = 255;
    private const int LabComponents = 3;
    private const double LabLightnessMax = 100;
    private const double LabAxisDefault = 100;
    // Sample tuples up to this many bits key the colour cache.
    private const int MaxCacheKeyBits = 62;

    /// <summary>
    /// The samples of a non-indexed image as 8-bit grey (one channel) or RGB (three), for the export
    /// paths, when its layout is one the plain copy cannot take: 1/2/4 or 16 bits per component, a
    /// /Decode remap, an ICC CMYK space, a /Separation, /DeviceN or /Lab space. Each sample is read at
    /// its depth, mapped through /Decode, and turned into colour as the renderers turn it. Null for an
    /// image the plain 8-bit copy already exports right (8-bit DeviceGray, RGB or CMYK, ICC grey or RGB,
    /// identity /Decode), and for a layout read nowhere else either.
    /// </summary>
    private (byte[] samples, int channels)? ExportSamples(byte[] decoded, int w, int h, int bpc)
    {
        // A stencil mask has no colour space of its own (its samples are paint/no-paint, read by
        // the bilevel paths); an absent entry is not the resolver's RGB default.
        if (IsImageMask || _stream.Dict.Get("ColorSpace") is null)
            return null;
        var space = SoftwarePageRenderer.ResolveImageColorSpace(_stream.Dict.Get("ColorSpace"), _reader);
        if (space.Palette is not null || w <= 0 || h <= 0 || bpc is not (1 or 2 or 4 or EightBits or SixteenBits))
            return null;
        var kind = space.BaseName switch
        {
            "DeviceGray" or "G" or "CalGray" => "DeviceGray",
            "DeviceRGB" or "RGB" or "CalRGB" => "DeviceRGB",
            "DeviceCMYK" or "CMYK" => "DeviceCMYK",
            "Separation" when space.TintTransform is not null => "Separation",
            "Lab" => "Lab",
            _ => null,
        };
        if (kind is null)
            return null;
        var comps = kind switch { "DeviceGray" => 1, "DeviceCMYK" => 4, "Separation" => space.TintComponents, _ => LabComponents };
        var decode = DecodeRanges(comps, kind);
        var plain = bpc == EightBits && decode is null && kind is "DeviceGray" or "DeviceRGB"
            || bpc == EightBits && decode is null && kind == "DeviceCMYK" && ColorSpace == "DeviceCMYK";
        if (plain)
            return null;

        var channels = kind == "DeviceGray" ? 1 : 3;
        var output = new byte[(long)w * h * channels];
        var rowBytes = ((long)w * comps * bpc + 7) / 8;
        var max = (double)((1 << bpc) - 1);
        var raw = new int[comps];
        var values = new double[comps];
        // Colour of a sample tuple, cached by the tuple: a tint transform or a colour conversion
        // is far dearer than the lookup, and images repeat their colours.
        var cacheable = comps * bpc <= MaxCacheKeyBits;
        var cache = new Dictionary<long, (byte r, byte g, byte b)>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                long key = 0;
                for (var c = 0; c < comps; c++)
                {
                    raw[c] = ReadSample(decoded, y * rowBytes, x * comps + c, bpc);
                    key = (key << bpc) | (uint)raw[c];
                }
                var at = ((long)y * w + x) * channels;
                if (cacheable && cache.TryGetValue(key, out var known))
                {
                    if (channels == 1) output[at] = known.r;
                    else (output[at], output[at + 1], output[at + 2]) = known;
                    continue;
                }
                for (var c = 0; c < comps; c++)
                {
                    var v = raw[c] / max;
                    values[c] = decode is null ? v : decode[c * 2] + v * (decode[c * 2 + 1] - decode[c * 2]);
                }
                var rgb = channels == 1 ? (ToByte(values[0]), (byte)0, (byte)0) : SampleRgb(values, kind, space);
                if (cacheable)
                    cache[key] = rgb;
                if (channels == 1) output[at] = rgb.Item1;
                else (output[at], output[at + 1], output[at + 2]) = rgb;
            }
        }
        return (output, channels);
    }

    private static (byte r, byte g, byte b) SampleRgb(double[] values, string kind, SoftwarePageRenderer.ImageColorSpaceInfo space)
    {
        switch (kind)
        {
            case "DeviceRGB":
                return (ToByte(values[0]), ToByte(values[1]), ToByte(values[2]));
            case "DeviceCMYK":
                return CmykToRgbLut.Convert(values[0], values[1], values[2], values[3]);
            case "Lab":
                var (r, g, b) = LabColor.ToRgb(values[0], values[1], values[2]);
                return (ToByte(r), ToByte(g), ToByte(b));
            default:
                var alternate = space.TintTransform!.Evaluate(values);
                if (alternate is null)
                    return (ToByte(1 - values[0]), ToByte(1 - values[0]), ToByte(1 - values[0]));
                return SoftwarePageRenderer.ComponentsToRgb(alternate, space.AltSpaceName ?? "DeviceGray");
        }
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * MaxByte);

    // Sample `index` of a row of packed samples, most significant bits first.
    private static int ReadSample(byte[] data, long rowStart, int index, int bpc)
    {
        if (bpc == SixteenBits)
        {
            var at16 = rowStart + (long)index * 2;
            return at16 + 1 < data.Length ? data[at16] << EightBits | data[at16 + 1] : 0;
        }
        var bit = (long)index * bpc;
        var at = rowStart + (bit >> 3);
        if (at >= data.Length)
            return 0;
        return bpc == EightBits ? data[at] : (data[at] >> (EightBits - bpc - (int)(bit & 7))) & ((1 << bpc) - 1);
    }

    /// <summary>The /Decode ranges, one (min, max) pair per component; null when the entry is absent
    /// and the identity applies. A /Lab image's default is its colour space's /Range for a* and b*.</summary>
    private double[]? DecodeRanges(int comps, string kind)
    {
        double[]? ranges = null;
        if (_reader.Resolve(_stream.Dict.Get("Decode")) is PdfArray decode && decode.Count >= comps * 2)
        {
            ranges = new double[comps * 2];
            for (var i = 0; i < comps * 2; i++)
                ranges[i] = _reader.Resolve(decode[i]) switch { PdfInteger n => n.Value, PdfReal r => r.Value, _ => 0 };
            var identity = kind != "Lab";
            for (var c = 0; c < comps && identity; c++)
                identity = ranges[c * 2] == 0 && ranges[c * 2 + 1] == 1;
            return identity ? null : ranges;
        }
        if (kind != "Lab")
            return null;
        ranges = new[] { 0, LabLightnessMax, -LabAxisDefault, LabAxisDefault, -LabAxisDefault, LabAxisDefault };
        if (_reader.Resolve(_stream.Dict.Get("ColorSpace")) is PdfArray { Count: >= 2 } lab
            && _reader.ResolveDict(lab[1]) is { } labDict
            && _reader.ResolveArray(labDict.Get("Range")) is { Count: 4 } range)
        {
            for (var i = 0; i < 4; i++)
                ranges[2 + i] = _reader.Resolve(range[i]) switch { PdfInteger n => n.Value, PdfReal r => r.Value, _ => ranges[2 + i] };
        }
        return ranges;
    }

    /// <summary>The image's transparency for export: its /SMask, else an explicit stencil /Mask (a
    /// 1-bit image whose set samples mask the picture out), resampled to the image grid; null when it
    /// has neither.</summary>
    private byte[]? MaskAlpha(int w, int h)
    {
        if (HasSoftMask && TryBuildSoftMaskAlpha(w, h) is { } soft)
            return soft;
        var (stencil, mw, mh) = SoftwarePageRenderer.ResolveStencilMaskAlpha(_stream.Dict.Get("Mask"), _reader);
        if (stencil is null || mw <= 0 || mh <= 0)
            return null;
        if (mw == w && mh == h)
            return stencil;
        var alpha = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(mh - 1, (int)((long)y * mh / h));
            for (var x = 0; x < w; x++)
                alpha[y * w + x] = stencil[sy * mw + Math.Min(mw - 1, (int)((long)x * mw / w))];
        }
        return alpha;
    }
}
