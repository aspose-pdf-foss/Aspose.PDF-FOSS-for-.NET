using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>Convert one unfiltered scanline into the RGB (and alpha) planes.</summary>
    private static void ConvertPngRowToRgb(PngDecodeState pn, int y)
    {
        // Convert to RGB
        for (var x = 0; x < pn.width; x++)
        {
            var rgbIdx = (y * pn.width + x) * 3;
            switch (pn.colorType)
            {
                case 0: // Grayscale
                    pn.rgb[rgbIdx] = pn.rgb[rgbIdx + 1] = pn.rgb[rgbIdx + 2] = pn.curRow[x];
                    break;
                case 2: // RGB
                    pn.rgb[rgbIdx] = pn.curRow[x * 3];
                    pn.rgb[rgbIdx + 1] = pn.curRow[x * 3 + 1];
                    pn.rgb[rgbIdx + 2] = pn.curRow[x * 3 + 2];
                    break;
                case 4: // Grayscale + Alpha
                    pn.rgb[rgbIdx] = pn.rgb[rgbIdx + 1] = pn.rgb[rgbIdx + 2] = pn.curRow[x * 2];
                    var ga = pn.curRow[x * 2 + 1];
                    pn.alpha![y * pn.width + x] = ga;
                    if (ga != 255) pn.anyTransparent = true;
                    break;
                case 6: // RGBA
                    pn.rgb[rgbIdx] = pn.curRow[x * 4];
                    pn.rgb[rgbIdx + 1] = pn.curRow[x * 4 + 1];
                    pn.rgb[rgbIdx + 2] = pn.curRow[x * 4 + 2];
                    var ra = pn.curRow[x * 4 + 3];
                    pn.alpha![y * pn.width + x] = ra;
                    if (ra != 255) pn.anyTransparent = true;
                    break;
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

        // Apply PNG filter
        for (var x = 0; x < pn.stride; x++)
        {
            byte a = x >= pn.channels ? pn.curRow[x - pn.channels] : (byte)0;
            byte b = pn.prevRow[x];
            byte c = x >= pn.channels ? pn.prevRow[x - pn.channels] : (byte)0;

            pn.curRow[x] = filterByte switch
            {
                1 => (byte)(pn.curRow[x] + a),             // Sub
                2 => (byte)(pn.curRow[x] + b),             // Up
                3 => (byte)(pn.curRow[x] + (a + b) / 2),   // Average
                4 => (byte)(pn.curRow[x] + PaethPredictor(a, b, c)), // Paeth
                _ => pn.curRow[x]                           // None
            };
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
