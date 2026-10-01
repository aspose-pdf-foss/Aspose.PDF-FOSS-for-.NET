using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend : ISaveableFacade
{
    /// <summary>The stages of the mend PNG decoder: the chunk walk, the scanline unfilter and the palette expansion.</summary>
    private static (byte[] pixels, int width, int height, bool hasAlpha) DecodePalettePng(MendPngDecodeState pd)
    {
        var pixelCount = pd.width * pd.height;
        var palette = pd.palette!;   // the caller enters only with a PLTE chunk
        var indices = UnpackIndices(pd.raw, pd.width, pd.height, pd.stride, pd.bitDepth);
        byte R(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p] : (byte)0; }
        byte G(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p + 1] : (byte)0; }
        byte B(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p + 2] : (byte)0; }
        if (pd.trns is not null)
        {
            var rgba = new byte[pixelCount * 4];
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = indices[i];
                rgba[i * 4] = R(idx); rgba[i * 4 + 1] = G(idx); rgba[i * 4 + 2] = B(idx);
                rgba[i * 4 + 3] = idx < pd.trns.Length ? pd.trns[idx] : (byte)255;
            }
            return (rgba, pd.width, pd.height, true);
        }
        var rgb3 = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            var idx = indices[i];
            rgb3[i * 3] = R(idx); rgb3[i * 3 + 1] = G(idx); rgb3[i * 3 + 2] = B(idx);
        }
        return (rgb3, pd.width, pd.height, false);
    }

    /// <summary></summary>
    private static void UnfilterPngRows(MendPngDecodeState pd)
    {
        for (var y = 0; y < pd.height; y++)
        {
            var filterByte = pd.decompressed[pd.srcPos++];
            var rowStart = y * pd.stride;

            Array.Copy(pd.decompressed, pd.srcPos, pd.raw, rowStart, pd.stride);
            pd.srcPos += pd.stride;

            switch (filterByte)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (var x = pd.bpp; x < pd.stride; x++)
                        pd.raw[rowStart + x] = (byte)(pd.raw[rowStart + x] + pd.raw[rowStart + x - pd.bpp]);
                    break;
                case 2: // Up
                    for (var x = 0; x < pd.stride; x++)
                        pd.raw[rowStart + x] = (byte)(pd.raw[rowStart + x] + pd.prevRow[x]);
                    break;
                case 3: // Average
                    for (var x = 0; x < pd.stride; x++)
                    {
                        var a = x >= pd.bpp ? pd.raw[rowStart + x - pd.bpp] : 0;
                        pd.raw[rowStart + x] = (byte)(pd.raw[rowStart + x] + (a + pd.prevRow[x]) / 2);
                    }
                    break;
                case 4: // Paeth
                    for (var x = 0; x < pd.stride; x++)
                    {
                        var a = x >= pd.bpp ? pd.raw[rowStart + x - pd.bpp] : 0;
                        var b = pd.prevRow[x];
                        var c = x >= pd.bpp ? pd.prevRow[x - pd.bpp] : 0;
                        pd.raw[rowStart + x] = (byte)(pd.raw[rowStart + x] + PaethPredictor(a, b, c));
                    }
                    break;
            }

            Array.Copy(pd.raw, rowStart, pd.prevRow, 0, pd.stride);
        }
    }

    /// <summary></summary>
    private static void ReadPngChunks(MendPngDecodeState pd)
    {
        while (pd.pos < pd.png.Length - 4)
        {
            var chunkLen = ReadInt32BE(pd.png, pd.pos);
            var chunkType = Encoding.ASCII.GetString(pd.png, pd.pos + 4, 4);
            var dataStart = pd.pos + 8;

            if (chunkType == "IHDR")
            {
                pd.width = ReadInt32BE(pd.png, dataStart);
                pd.height = ReadInt32BE(pd.png, dataStart + 4);
                pd.bitDepth = pd.png[dataStart + 8];
                pd.colorType = pd.png[dataStart + 9];
            }
            else if (chunkType == "PLTE")
            {
                pd.palette = new byte[chunkLen];
                Array.Copy(pd.png, dataStart, pd.palette, 0, chunkLen);
            }
            else if (chunkType == "tRNS")
            {
                pd.trns = new byte[chunkLen];
                Array.Copy(pd.png, dataStart, pd.trns, 0, chunkLen);
            }
            else if (chunkType == "IDAT")
            {
                pd.idatData.Write(pd.png, dataStart, chunkLen);
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            pd.pos = dataStart + chunkLen + 4; // +4 for CRC
        }
    }
}
