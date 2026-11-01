using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>Convert one unfiltered scanline into the RGB (and alpha) planes.</summary>
    private static void ConvertPngRowToRgb(PngDecodeState pn, int y)
    {
        // The colour type is fixed for the image: the plain layouts copy the row in one
        // loop each, the palette layout keeps its per-pixel unpacking below.
        var row = pn.curRow;
        var rgb = pn.rgb;
        var rowStart = y * pn.width;
        switch (pn.colorType)
        {
            case 0: // Grayscale
                for (var x = 0; x < pn.width; x++)
                    rgb[(rowStart + x) * 3] = rgb[(rowStart + x) * 3 + 1] = rgb[(rowStart + x) * 3 + 2] = row[x];
                return;
            case 2: // RGB
                Array.Copy(row, 0, rgb, rowStart * 3, pn.width * 3);
                return;
            case 4: // Grayscale + Alpha
                for (var x = 0; x < pn.width; x++)
                {
                    rgb[(rowStart + x) * 3] = rgb[(rowStart + x) * 3 + 1] = rgb[(rowStart + x) * 3 + 2] = row[x * 2];
                    var ga = row[x * 2 + 1];
                    pn.alpha![rowStart + x] = ga;
                    if (ga != 255) pn.anyTransparent = true;
                }
                return;
            case 6: // RGBA
                for (var x = 0; x < pn.width; x++)
                {
                    rgb[(rowStart + x) * 3] = row[x * 4];
                    rgb[(rowStart + x) * 3 + 1] = row[x * 4 + 1];
                    rgb[(rowStart + x) * 3 + 2] = row[x * 4 + 2];
                    var ra = row[x * 4 + 3];
                    pn.alpha![rowStart + x] = ra;
                    if (ra != 255) pn.anyTransparent = true;
                }
                return;
            case 3:
                break;
            default:
                return;
        }
        for (var x = 0; x < pn.width; x++)
        {
            var rgbIdx = (rowStart + x) * 3;
            switch (pn.colorType)
            {
                case 3: // Indexed: unpack the index at the image bit depth, look up /PLTE
                    int idx;
                    if (pn.bitDepth == 8)
                        idx = pn.curRow[x];
                    else
                    {
                        var bitPos = x * pn.bitDepth;
                        var shift = 8 - pn.bitDepth - (bitPos % 8);
                        idx = (pn.curRow[bitPos / 8] >> shift) & ((1 << pn.bitDepth) - 1);
                    }
                    var pi = idx * 3;
                    if (pn.palette is not null && pi + 2 < pn.palette.Length)
                    {
                        pn.rgb[rgbIdx] = pn.palette[pi];
                        pn.rgb[rgbIdx + 1] = pn.palette[pi + 1];
                        pn.rgb[rgbIdx + 2] = pn.palette[pi + 2];
                    }
                    if (pn.indexedAlpha)
                    {
                        // tRNS lists alpha per palette index; indices past its
                        // end are fully opaque.
                        var ia = idx < pn.trns!.Length ? pn.trns[idx] : (byte)255;
                        pn.alpha![y * pn.width + x] = ia;
                        if (ia != 255) pn.anyTransparent = true;
                    }
                    break;
            }
        }
    }

    /// <summary>Read the next filtered scanline and undo its PNG filter in place.</summary>
    private static void UnfilterPngRow(PngDecodeState pn)
    {
        var filterByte = pn.rawScanlines[pn.scanPos++];

        // Read filtered row
        var bytesToRead = Math.Min(pn.stride, pn.rawScanlines.Length - pn.scanPos);
        Array.Copy(pn.rawScanlines, pn.scanPos, pn.curRow, 0, bytesToRead);
        pn.scanPos += pn.stride;

        // Apply PNG filter: one loop per filter, since the filter is fixed for the row and
        // a per-byte choice cost more than the arithmetic. The left neighbour of the first
        // pixel's bytes is zero, so the first bpp bytes are a row of their own.
        var row = pn.curRow;
        var prev = pn.prevRow;
        var bpp = pn.channels;
        var n = pn.stride;
        switch (filterByte)
        {
            case 1: // Sub
                for (var x = bpp; x < n; x++) row[x] = (byte)(row[x] + row[x - bpp]);
                break;
            case 2: // Up
                for (var x = 0; x < n; x++) row[x] = (byte)(row[x] + prev[x]);
                break;
            case 3: // Average
                for (var x = 0; x < bpp && x < n; x++) row[x] = (byte)(row[x] + prev[x] / 2);
                for (var x = bpp; x < n; x++) row[x] = (byte)(row[x] + (row[x - bpp] + prev[x]) / 2);
                break;
            case 4: // Paeth: with a = c = 0 the predictor is b
                for (var x = 0; x < bpp && x < n; x++) row[x] = (byte)(row[x] + prev[x]);
                for (var x = bpp; x < n; x++) row[x] = (byte)(row[x] + PaethPredictor(row[x - bpp], prev[x], prev[x - bpp]));
                break;
            default: // None
                break;
        }
    }

    /// <summary>Walk the chunk stream, collecting IDAT data, the palette and the tRNS table.</summary>
    private static void ReadPngChunks(PngDecodeState pn)
    {
        while (pn.pos + 8 < pn.pngData.Length)
        {
            var chunkLen = ReadInt32BE(pn.pngData, pn.pos);
            var chunkType = System.Text.Encoding.ASCII.GetString(pn.pngData, pn.pos + 4, 4);
            if (chunkType == "IDAT")
                pn.idatData.Write(pn.pngData, pn.pos + 8, chunkLen);
            else if (chunkType == "PLTE" && pn.pos + 8 + chunkLen <= pn.pngData.Length)
            {
                pn.palette = new byte[chunkLen];
                Array.Copy(pn.pngData, pn.pos + 8, pn.palette, 0, chunkLen);
            }
            else if (chunkType == "tRNS" && pn.pos + 8 + chunkLen <= pn.pngData.Length)
            {
                pn.trns = new byte[chunkLen];
                Array.Copy(pn.pngData, pn.pos + 8, pn.trns, 0, chunkLen);
            }
            else if (chunkType == "IEND")
                break;
            pn.pos += 12 + chunkLen; // length + type + data + CRC
        }
    }
}
