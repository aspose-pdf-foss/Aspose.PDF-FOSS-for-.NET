using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>The lightest level, on every channel, a pixel still counts as ink at: a pixel at or
    /// above it on all three is paper. A scan's paper is rarely pure white - off-white stock and
    /// JPEG noise put nearly every pixel a few levels below 255 - and counting each of those as ink
    /// made a blank scanned page score more coverage than a page of text.</summary>
    private const int NearWhiteLevel = 240;

    private const double Opaque = 1.0;
    private const int BitsPerByte = 8;
    private const int LastBitOfByte = 7;

    /// <summary>
    /// The share of an image XObject's pixels that put ink on the page, on the image's own pixel
    /// grid: each pixel read as the colour its colour space, bit depth and /Decode give it (a
    /// palette through its palette, an ICC or calibrated space by its components), counted when
    /// it is darker than near-white, and weighted by how much of it its /SMask or stencil /Mask
    /// lets through. A stencil image, or one the pixel reader cannot read, is measured from its
    /// raw samples as before. The count stops at the end of the first row where the ink passes
    /// <paramref name="budget"/> - the share the caller's verdict still allows - with the share
    /// counted so far: the rest cannot change the verdict, and a page of text is not read to its foot.
    /// </summary>
    private double ImageInkFraction(PdfStream image, double budget)
    {
        var dict = image.Dict;
        var width = (int)dict.GetInt("Width");
        var height = (int)dict.GetInt("Height");
        var stencil = dict.Get("ImageMask") is PdfBoolean { Value: true };
        var source = stencil || width <= 0 || height <= 0 ? null : new ImageXObject(string.Empty, image, _reader);
        var masked = _reader.ResolveStream(dict.Get("SMask")) is not null || _reader.ResolveStream(dict.Get("Mask")) is not null;
        if (!masked && source?.OneBitGrayRows() is { } oneBit)
            return OneBitInkFraction(oneBit.Rows, width, height, oneBit.BlackIs1, budget);
        var pixels = source?.GetPixelSource();
        if (pixels is null) return NonWhiteImageFraction(dict, _reader.DecodeStream(image));

        var shown = MaskShare(dict, width, height);
        var total = (long)width * height;
        var limit = budget * total;
        double ink = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixels(x, y);
                if (r >= NearWhiteLevel && g >= NearWhiteLevel && b >= NearWhiteLevel) continue;
                ink += shown?.Invoke(x, y) ?? Opaque;
            }
            if (ink > limit) break;
        }
        return ink / total;
    }

    /// <summary>The ink share of an unmasked 1-bit gray image counted from its packed rows, eight
    /// pixels a byte, exactly as <see cref="ImageInkFraction"/> counts the pixels it reads - a
    /// pixel is ink where its bit is the black one, a byte past the data reads as 0 bits - and
    /// stopping where that count stops.</summary>
    private static double OneBitInkFraction(byte[] rows, int width, int height, bool blackIs1, double budget)
    {
        var rowBytes = (width + LastBitOfByte) / BitsPerByte;
        // The bits of a row's last byte that lie within the width.
        var lastByteBits = (byte)(byte.MaxValue << (rowBytes * BitsPerByte - width));
        var total = (long)width * height;
        var limit = budget * total;
        long ink = 0;
        for (var y = 0; y < height; y++)
        {
            var at = (long)y * rowBytes;
            var ones = 0;
            for (var i = 0; i < rowBytes; i++, at++)
            {
                var bits = at < rows.Length ? rows[at] : (byte)0;
                ones += Compat.PopCount(i == rowBytes - 1 ? (byte)(bits & lastByteBits) : bits);
            }
            ink += blackIs1 ? ones : width - ones;
            if (ink > limit) break;
        }
        return (double)ink / total;
    }

    /// <summary>How much of the image pixel (x, y) its mask lets onto the page, 0..1: the soft
    /// mask's level, or whether the stencil /Mask paints it. Null when the image has neither,
    /// or a mask that cannot be read.</summary>
    private Func<int, int, double>? MaskShare(PdfDictionary dict, int width, int height)
    {
        if (_reader.ResolveStream(dict.Get("SMask")) is { } soft)
        {
            var mw = (int)soft.Dict.GetInt("Width");
            var mh = (int)soft.Dict.GetInt("Height");
            var alpha = mw > 0 && mh > 0 ? new ImageXObject(string.Empty, soft, _reader).GetPixelSource() : null;
            if (alpha is null) return null;
            return (x, y) => alpha((int)((long)x * mw / width), (int)((long)y * mh / height)).r / (double)byte.MaxValue;
        }

        if (_reader.ResolveStream(dict.Get("Mask")) is not { } mask) return null;
        var sw = (int)mask.Dict.GetInt("Width");
        var sh = (int)mask.Dict.GetInt("Height");
        if (sw <= 0 || sh <= 0) return null;
        byte[] bits;
        try { bits = _reader.DecodeStream(mask); }
        catch { return null; }
        // A stencil paints where its sample is 0, or 1 under an inverting /Decode (PDF 32000 §8.9.6.2).
        var paintBit = _reader.Resolve(mask.Dict.Get("Decode")) is PdfArray d && d.Count >= 2
            && CoverageNum(d[0]) > CoverageNum(d[1]) ? 1 : 0;
        long rowBytes = (sw + LastBitOfByte) / BitsPerByte;
        return (x, y) =>
        {
            var mx = (long)x * sw / width;
            var index = (long)y * sh / height * rowBytes + mx / BitsPerByte;
            var bit = index < bits.Length ? (bits[index] >> (LastBitOfByte - (int)(mx % BitsPerByte))) & 1 : 1;
            return bit == paintBit ? Opaque : 0;
        };
    }
}
