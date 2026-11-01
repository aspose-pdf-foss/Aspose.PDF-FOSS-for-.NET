using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>An image's /Decode array applied to its 8-bit samples (ISO 32000-1 8.9.5.2): each sample s of
/// component c becomes Dmin + s * (Dmax - Dmin) / 255, back on the 0..255 scale, so an inverting, lightening
/// or narrowing remap reaches the colour the sample stands for.</summary>
internal static class SampleDecode
{
    private const int SampleLevels = 256;
    private const double MaxSample = 255;

    /// <summary>The samples remapped through <paramref name="decode"/>, interleaved <paramref name="components"/>
    /// per pixel; null when the array is absent, too short, or the identity (nothing to remap).</summary>
    internal static byte[]? Remap(byte[] samples, int sampleCount, int components, PdfObject? decode, PdfReader? reader)
    {
        var map = Maps(decode, components, reader);
        if (map is null) return null;
        var remapped = new byte[samples.Length];
        Array.Copy(samples, remapped, samples.Length);
        RemapInPlace(remapped, Math.Min(sampleCount, samples.Length), map);
        return remapped;
    }

    /// <summary>The samples remapped in place; false (the samples untouched) when there is nothing to remap.</summary>
    internal static bool RemapInPlace(byte[] samples, int sampleCount, int components, PdfObject? decode, PdfReader? reader)
    {
        var map = Maps(decode, components, reader);
        if (map is null) return false;
        RemapInPlace(samples, Math.Min(sampleCount, samples.Length), map);
        return true;
    }

    private static void RemapInPlace(byte[] samples, int sampleCount, byte[][] map)
    {
        var components = map.Length;
        for (var i = 0; i < sampleCount; i++)
            samples[i] = map[i % components][samples[i]];
    }

    /// <summary>One 256-entry table per component, or null for an absent, short or identity array.</summary>
    private static byte[][]? Maps(PdfObject? decode, int components, PdfReader? reader)
    {
        var array = (reader?.Resolve(decode) ?? decode) as PdfArray;
        if (array is null || array.Count < components * 2) return null;
        var map = new byte[components][];
        var identity = true;
        for (var c = 0; c < components; c++)
        {
            var low = Number(array[c * 2], reader);
            var high = Number(array[c * 2 + 1], reader);
#pragma warning disable S1244 // the identity range is written as the exact numbers 0 and 1
            identity &= low == 0 && high == 1;
#pragma warning restore S1244
            map[c] = new byte[SampleLevels];
            for (var v = 0; v < SampleLevels; v++)
                map[c][v] = (byte)Math.Round(Math.Max(0, Math.Min(1, low + v / MaxSample * (high - low))) * MaxSample);
        }
        return identity ? null : map;
    }

    private static double Number(PdfObject? value, PdfReader? reader) => (reader?.Resolve(value) ?? value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };
}
