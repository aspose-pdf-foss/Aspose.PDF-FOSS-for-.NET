
namespace Aspose.Pdf.Devices;

public sealed partial class TiffDevice : ImageDevice
{
    /// <summary>The stages of one TIFF page write: packing the rows for the colour depth, and writing the image file directory.</summary>
    private void PackTiffRows(TiffPageWriteState tp)
    {
        if (tp.isAlpha)
        {
            tp.stripInput = tp.rgba;
        }
        else if (tp.isBilevel)
        {
            if (UsesHalftone)
            {
                tp.stripInput = PackRgbaToHalftoneBilevel(tp.rgba, tp.w, tp.h);
            }
            else
            {
                var rgb = RgbaToRgb(tp.rgba, tp.w, tp.h);
                ThresholdToBlackAndWhite(rgb, BilevelCutoff(Settings.Brightness));
                tp.stripInput = PackRgbToBilevel(rgb, tp.w, tp.h);
            }
        }
        else if (tp.isPalette)
        {
            var rgb = RgbaToRgb(tp.rgba, tp.w, tp.h);
            var adaptive = TiffPaletteQuantizer.TryQuantizeAdaptive(rgb, tp.w, tp.h);
            if (adaptive is { } a)
            {
                tp.stripInput = a.indexed;
                tp.colorMap = a.colorMap;
            }
            else
            {
                tp.stripInput = TiffPaletteQuantizer.QuantizeRgbTo8bpp(rgb, tp.w, tp.h);
                tp.colorMap = TiffPaletteQuantizer.BuildColorMap332();
            }
        }
        else if (tp.is4bpp)
        {
            var rgb = RgbaToRgb(tp.rgba, tp.w, tp.h);
            var (indices, map) = TiffPaletteQuantizer.QuantizeTo4bpp(rgb, tp.w, tp.h);
            tp.stripInput = Pack4bpp(indices, tp.w, tp.h);
            tp.colorMap = map;
        }
        else
        {
            tp.stripInput = RgbaToRgb(tp.rgba, tp.w, tp.h);
        }
    }

    /// <summary></summary>
    private void WriteTiffStripAndDirectory(TiffPageWriteState tp)
    {
        var (strip, compressionTag) = EncodeStrip(tp.stripInput, tp.compression);
        tp.stripSize = strip.Length;

        tp.stripOffset = (uint)tp.output.Position;
        tp.bw.Write(strip);

        // Align to word boundary
        if (tp.output.Position % 2 != 0) tp.bw.Write((byte)0);

        // Auxiliary payloads referenced from IFD tags by offset:
        // BitsPerSample array (RGB/RGBA only), ColorMap (palette-only),
        // ExtraSamples (RGBA-only).
        uint bpsOffset = 0;
        if (!tp.isPalette && !tp.is4bpp && !tp.isBilevel)
        {
            bpsOffset = (uint)tp.output.Position;
            tp.bw.Write((ushort)8);
            tp.bw.Write((ushort)8);
            tp.bw.Write((ushort)8);
            if (tp.isAlpha) tp.bw.Write((ushort)8);
        }

        uint colorMapOffset = 0;
        if (tp.isPalette || tp.is4bpp)
        {
            colorMapOffset = (uint)tp.output.Position;
            foreach (var s in tp.colorMap!) tp.bw.Write(s);
        }

        // RATIONAL payloads for X/YResolution. TIFF tag 282/283 stores the resolution
        // as a fraction (numerator/denominator, both u32) at an out-of-line offset.
        // ResolutionUnit (296) = 2 means inches, so DPI/1 expresses N dots per inch
        // exactly. Without these tags, readers (System.Drawing, ImageSharp) fall back
        // to a hard-coded 96 DPI, which broke any test that asserted on the saved DPI.
        uint xResOffset = (uint)tp.output.Position;
        tp.bw.Write((uint)Resolution.X);
        tp.bw.Write((uint)1);
        uint yResOffset = (uint)tp.output.Position;
        tp.bw.Write((uint)Resolution.Y);
        tp.bw.Write((uint)1);

        tp.ifdOffset = (uint)tp.output.Position;
        tp.currentPos = tp.output.Position;
        tp.output.Position = tp.ifdOffsetPos;
        tp.bw.Write(tp.ifdOffset);
        tp.output.Position = tp.currentPos;

        // IFD tag count: base 13 (RGB/bilevel/RGBA without extras), +1 for palette
        // ColorMap, +1 for RGBA ExtraSamples.
        ushort tagCount = (ushort)(13 + (tp.isPalette || tp.is4bpp ? 1 : 0) + (tp.isAlpha ? 1 : 0));
        tp.bw.Write(tagCount);

        // Tags MUST be written in ascending tag-number order per the TIFF 6.0 spec.
        WriteTag(tp.bw, 256, 3, 1, (uint)tp.w);                               // ImageWidth
        WriteTag(tp.bw, 257, 3, 1, (uint)tp.h);                               // ImageLength
        if (tp.isPalette)
            WriteTag(tp.bw, 258, 3, 1, 8);                                 // BitsPerSample = 8 (inline)
        else if (tp.is4bpp)
            WriteTag(tp.bw, 258, 3, 1, 4);                                 // BitsPerSample = 4 (inline)
        else if (tp.isBilevel)
            WriteTag(tp.bw, 258, 3, 1, 1);                                 // BitsPerSample = 1 (inline)
        else
            WriteTag(tp.bw, 258, 3, tp.isAlpha ? 4u : 3u, bpsOffset);         // BitsPerSample offset → [8,8,8(,8)]
        WriteTag(tp.bw, 259, 3, 1, compressionTag);                        // Compression
        tp.photometric = (tp.isPalette || tp.is4bpp) ? 3u : tp.isBilevel ? 0u : 2u;
        WriteTag(tp.bw, 262, 3, 1, tp.photometric);                            // PhotometricInterpretation
        WriteTag(tp.bw, 273, 4, 1, tp.stripOffset);                           // StripOffsets
        tp.samplesPerPixel = (tp.isPalette || tp.is4bpp || tp.isBilevel) ? 1u : tp.isAlpha ? 4u : 3u;
        WriteTag(tp.bw, 277, 3, 1, tp.samplesPerPixel);                        // SamplesPerPixel
        WriteTag(tp.bw, 278, 3, 1, (uint)tp.h);                               // RowsPerStrip
        WriteTag(tp.bw, 279, 4, 1, (uint)tp.stripSize);                       // StripByteCounts
        WriteTag(tp.bw, 282, 5, 1, xResOffset);                            // XResolution (RATIONAL)
        WriteTag(tp.bw, 283, 5, 1, yResOffset);                            // YResolution (RATIONAL)
        WriteTag(tp.bw, 284, 3, 1, 1);                                     // PlanarConfiguration = Chunky
        WriteTag(tp.bw, 296, 3, 1, 2);                                     // ResolutionUnit = 2 (inches)
        if (tp.isPalette)
            WriteTag(tp.bw, 320, 3, 3 * 256, colorMapOffset);              // ColorMap (768 shorts)
        else if (tp.is4bpp)
            WriteTag(tp.bw, 320, 3, 3 * 16, colorMapOffset);               // ColorMap (48 shorts)
        if (tp.isAlpha)
            WriteTag(tp.bw, 338, 3, 1, 2);                                 // ExtraSamples = 2 (unassociated alpha, inline)

        tp.nextIfdPos = tp.output.Position;
        tp.bw.Write((uint)0);
    }
}
