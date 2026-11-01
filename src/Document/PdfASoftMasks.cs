using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private const int EightBitSamples = 8;
    private const int OneBitSamples = 1;
    private const int FullAlpha = 255;

    /// <summary>
    /// ISO 19005-1 clause 6.4: an image carries no /SMask. A soft mask that is all or nothing
    /// says the same as a hard one, so it is restated exactly: one fully opaque throughout
    /// goes, and one of only opaque and clear samples becomes a one-bit /Mask (a set bit masks
    /// the sample out), which part 1 admits. A mask with partial alpha, a matte or a decode of
    /// its own is left as it is: no restatement would draw it the same.
    /// </summary>
    private void ReplacePartOneSoftMask(PdfDictionary image)
    {
        if (_reader.ResolveStream(image.Get("SMask")) is not { } mask) return;
        if (mask.Dict.Get("Matte") is not null || mask.Dict.Get("Decode") is not null) return;
        int width = (int)mask.Dict.GetInt("Width"), height = (int)mask.Dict.GetInt("Height");
        var bpc = (int)mask.Dict.GetInt("BitsPerComponent");
        if (width <= 0 || height <= 0 || bpc is not (EightBitSamples or OneBitSamples)) return;
        byte[] samples;
        try { samples = _reader.DecodeStream(mask); }
        catch { return; }

        var rowBytes = (width + 7) / 8;
        var bits = new byte[rowBytes * height];
        var opaque = true;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                int alpha;
                if (bpc == EightBitSamples)
                {
                    var at = y * width + x;
                    if (at >= samples.Length) return;
                    alpha = samples[at];
                    if (alpha is not (0 or FullAlpha)) return;
                }
                else
                {
                    var at = y * rowBytes + x / 8;
                    if (at >= samples.Length) return;
                    alpha = (samples[at] >> (7 - x % 8) & 1) == 1 ? FullAlpha : 0;
                }
                if (alpha != 0) continue;
                opaque = false;
                bits[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            }

        image.Remove("SMask");
        if (opaque) return;
        var stencil = new PdfDictionary();
        stencil.Set("Type", new PdfName("XObject"));
        stencil.Set("Subtype", new PdfName("Image"));
        stencil.Set("Width", new PdfInteger(width));
        stencil.Set("Height", new PdfInteger(height));
        stencil.Set("ImageMask", PdfBoolean.True);
        stencil.Set("BitsPerComponent", new PdfInteger(OneBitSamples));
        image.Set("Mask", new PdfStream(stencil, bits));
    }
}
