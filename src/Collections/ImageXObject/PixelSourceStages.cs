using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public partial class ImageXObject
{
// The stages of the pixel source: the indexed, 1-bit and per-component getters.

    /// <summary>The indexed-colour source: palette lookups per packed sample, or null when the
    /// image carries no palette.</summary>
    private IO.PixelGetter? IndexedSource(PixelSourceState ps, int paletteSize)
    {
        if (ps.indexedPalette is null) return null;
        return (int x, int y) =>
        {
            var idx = ReadPackedIndex(ps.decoded, x, y, ps.w, ps.bpc);
            if (idx >= paletteSize) idx = paletteSize - 1;
            var src = idx * 3;
            return (ps.indexedPalette[src], ps.indexedPalette[src + 1], ps.indexedPalette[src + 2]);
        };
    }

    /// <summary>The 1-bit source, honouring /Decode and /BlackIs1; null unless the image is 1 bpc.</summary>
    private IO.PixelGetter? OneBitSource(PixelSourceState ps)
    {
        if (ps.bpc != 1) return null;
        var blackIs1 = false;
        var decodeArr = _reader.Resolve(_stream.Dict.Get("Decode"));
        if (decodeArr is PdfArray da && da.Count >= 2)
        {
            var first = da[0] is PdfInteger i ? i.Value : (da[0] is PdfReal r ? (long)r.Value : 0);
            blackIs1 = first == 1;
        }
        var parms = _reader.ResolveDict(_stream.Dict.Get("DecodeParms"));
        if (parms is not null)
        {
            var bi1 = parms.Get("BlackIs1");
            if (bi1 is PdfBoolean b) blackIs1 = b.Value;
        }
        var srcBytesPerRow = (ps.w + 7) / 8;
        return (int x, int y) =>
        {
            var byteIdx = (long)y * srcBytesPerRow + (x / 8);
            var bitIdx = 7 - (x % 8);
            var bit = (byteIdx < ps.decoded.Length) ? (ps.decoded[byteIdx] >> bitIdx) & 1 : 0;
            var v = blackIs1
                ? (bit == 1 ? (byte)0 : (byte)255)
                : (bit == 1 ? (byte)255 : (byte)0);
            return (v, v, v);
        };
    }

    /// <summary>The per-component sources: gray, DeviceCMYK and RGB samples; null for any other layout.</summary>
    private IO.PixelGetter? ComponentSource(PixelSourceState ps)
    {
        ps.components = ComponentCount;
        if (ps.components == 1)
        {
            return (int x, int y) =>
            {
                var idx = (long)y * ps.w + x;
                var v = idx < ps.decoded.Length ? ps.decoded[idx] : (byte)0;
                return (v, v, v);
            };
        }
        if (ps.components == 4 && ColorSpace is "DeviceCMYK")
        {
            return (int x, int y) =>
            {
                var idx = ((long)y * ps.w + x) * 4;
                if (idx + 3 >= ps.decoded.Length) return (0, 0, 0);
                var c = ps.decoded[idx] / 255.0;
                var m = ps.decoded[idx + 1] / 255.0;
                var yk = ps.decoded[idx + 2] / 255.0;
                var k = ps.decoded[idx + 3] / 255.0;
                return ((byte)(255 * (1 - c) * (1 - k)),
                    (byte)(255 * (1 - m) * (1 - k)),
                    (byte)(255 * (1 - yk) * (1 - k)));
            };
        }
        if (ps.components == 3)
        {
            return (int x, int y) =>
            {
                var idx = ((long)y * ps.w + x) * 3;
                if (idx + 2 >= ps.decoded.Length) return (0, 0, 0);
                return (ps.decoded[idx], ps.decoded[idx + 1], ps.decoded[idx + 2]);
            };
        }
        return null;
    }
}
