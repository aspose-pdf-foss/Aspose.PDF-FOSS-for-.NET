using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    // PNG colour types (IHDR): gray, truecolour, palette.
    private const byte PngGray = 0;
    private const byte PngTruecolour = 2;
    private const byte PngPalette = 3;
    private const int IhdrBitDepthOffset = 24;
    private const int IhdrColourTypeOffset = 25;
    private const int MaxDirectBitDepth = 8;
    private const int BilevelDepth = 1;
    // FlateDecode predictor 15: every row carries its own PNG filter type byte, as a PNG's rows do.
    private const int PngRowPredictor = 15;
    private const int RgbComponents = 3;

    /// <summary>A PNG whose rows a PDF image reads as they are - bilevel or 8-bit gray, a palette, 8-bit RGB, not
    /// interlaced, without transparency - embedded with the PNG's own deflated, filtered rows under
    /// FlateDecode with the PNG predictor, in DeviceGray, /Indexed or DeviceRGB at the PNG's depth. Expanding
    /// such a PNG to 8-bit RGB samples deflated without a predictor made a bilevel scan 24 times its samples and
    /// a smooth photo several times its PNG. Null for any other PNG, which is decoded instead.</summary>
    private static ImageStamp? FromPngRows(byte[] pngData)
    {
        if (!IsPng(pngData) || pngData.Length <= IhdrInterlaceOffset || pngData[IhdrInterlaceOffset] != 0) return null;
        var bitDepth = pngData[IhdrBitDepthOffset];
        var colourType = pngData[IhdrColourTypeOffset];
        if (bitDepth > MaxDirectBitDepth || colourType is not (PngGray or PngTruecolour or PngPalette)) return null;
        if (colourType == PngTruecolour && bitDepth != MaxDirectBitDepth) return null;
        if (colourType == PngGray && bitDepth is not (BilevelDepth or MaxDirectBitDepth)) return null;

        var pn = new PngDecodeState { pngData = pngData, idatData = new MemoryStream(), pos = 8 };
        ReadPngChunks(pn);
        var rows = pn.idatData.ToArray();
        if (pn.trns is not null || rows.Length < 2) return null;
        if (colourType == PngPalette && (pn.palette is not { Length: >= RgbComponents } palette)) return null;

        var width = ReadInt32BE(pngData, 16);
        var height = ReadInt32BE(pngData, 20);
        var colours = colourType == PngTruecolour ? RgbComponents : 1;
        var parms = new PdfDictionary();
        parms.Set("Predictor", new PdfInteger(PngRowPredictor));
        parms.Set("Colors", new PdfInteger(colours));
        parms.Set("BitsPerComponent", new PdfInteger(bitDepth));
        parms.Set("Columns", new PdfInteger(width));
        var colourSpace = colourType switch { PngGray => "DeviceGray", PngTruecolour => "DeviceRGB", _ => "Indexed" };
        var stamp = new ImageStamp(rows, width, height, colourSpace, "FlateDecode", bitDepth)
        {
            DisplayWidth = width,
            DisplayHeight = height,
            _decodeParms = parms,
        };
        if (colourType == PngPalette)
        {
            var entries = pn.palette!.Length / RgbComponents;
            stamp._colorSpaceObject = new PdfArray
            {
                new PdfName("Indexed"), new PdfName("DeviceRGB"), new PdfInteger(entries - 1),
                new PdfString(pn.palette.Take(entries * RgbComponents).ToArray(), isHex: true),
            };
        }
        return stamp;
    }
}
