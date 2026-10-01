using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    /// <summary>The stages of the image downsample: expanding a packed 2/4-bit gray image before the resample.</summary>
    private static bool ExpandPackedGrayImage(ImageDownsampleState dn)
    {
        byte[] packed;
        try { packed = dn.reader.DecodeStream(dn.stream); }
        catch { return false; }

        var rowBytes = (dn.width * dn.bpc + 7) / 8;
        if (packed.Length < rowBytes * dn.height) return false;
        var maxVal = (1 << dn.bpc) - 1;
        var gray = new byte[dn.width * dn.height];
        for (var y = 0; y < dn.height; y++)
        {
            var rowOff = y * rowBytes;
            for (var x = 0; x < dn.width; x++)
            {
                var bitPos = x * dn.bpc;
                var b = packed[rowOff + (bitPos >> 3)];
                var shift = 8 - dn.bpc - (bitPos & 7);
                var sample = (b >> shift) & maxVal;
                gray[y * dn.width + x] = (byte)(sample * 255 / maxVal);
            }
        }

        var down = BoxFilterDownsample(gray, dn.width, dn.height, 1, dn.newWidth, dn.newHeight);
        var comp = Compress(down);
        dn.stream.ReplaceData(comp);
        dn.stream.Dict.Set("Filter", new PdfName("FlateDecode"));
        dn.stream.Dict.Set("Length", new PdfInteger(comp.Length));
        dn.stream.Dict.Set("Width", new PdfInteger(dn.newWidth));
        dn.stream.Dict.Set("Height", new PdfInteger(dn.newHeight));
        dn.stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
        dn.stream.Dict.Remove("DecodeParms");
        return false;
    }
}
