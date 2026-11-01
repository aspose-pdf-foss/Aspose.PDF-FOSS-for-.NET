using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public partial class ImageXObject
{
    /// <summary>Save the image as <see cref="Save(Stream, System.Drawing.Imaging.ImageFormat)"/> does (or as
    /// <see cref="Save(Stream)"/> when no format is given), with <paramref name="resolution"/> written as the file's
    /// density: the pixels are the image's own, the resolution says how large they print.</summary>
    internal void SaveAtResolution(Stream output, System.Drawing.Imaging.ImageFormat? format, int resolution)
    {
        using var file = new MemoryStream();
        if (format is null) Save(file);
        else Save(file, format);
        output.Write(WithDensity(file.ToArray(), resolution));
    }

    /// <summary>Save to a file whose extension names the format, at the given resolution.</summary>
    internal void SaveAtResolution(string path, int resolution)
    {
        using var fs = File.Create(path);
        SaveAtResolution(fs, FormatOfPath(path), resolution);
    }

    private const double MetresPerInch = 0.0254;
    private const int JfifUnitsOffset = 11;     // from the APP0 marker: FF E0, length (2), "JFIF\0", version (2)
    private const int JfifSegmentLength = 16;
    private const int JfifDotsPerInch = 1;
    private const int PngChunkOverhead = 12;    // header + CRC (4)
    private const int PngSignatureLength = 8;
    private const int PhysLength = 9;

    /// <summary>The encoded image with its density set to <paramref name="dpi"/>: a JPEG's JFIF APP0 (added when
    /// the file has none), a PNG's pHYs chunk. Other files, a non-positive resolution, and a JPEG whose colours
    /// a JFIF marker would make a decoder read differently are returned as they are.</summary>
    internal static byte[] WithDensity(byte[] file, int dpi)
    {
        if (dpi <= 0) return file;
        if (file.Length > 3 && file[0] == 0xFF && file[1] == 0xD8) return JpegWithDensity(file, dpi);
        if (file.Length > PngSignatureLength && file[0] == 0x89 && file[1] == 0x50 && file[2] == 0x4E && file[3] == 0x47)
            return PngWithDensity(file, (int)Math.Round(dpi / MetresPerInch));
        return file;
    }

    private static byte[] JpegWithDensity(byte[] jpeg, int dpi)
    {
        var density = Math.Min(dpi, 0xFFFF);
        if (jpeg.Length > JfifUnitsOffset + 5 && jpeg[2] == 0xFF && jpeg[3] == 0xE0
            && jpeg[6] == (byte)'J' && jpeg[7] == (byte)'F' && jpeg[8] == (byte)'I' && jpeg[9] == (byte)'F')
        {
            var patched = (byte[])jpeg.Clone();
            WriteJfifDensity(patched, 2, density);
            return patched;
        }
        if (JfifWouldRecolour(jpeg)) return jpeg;
        var result = new byte[jpeg.Length + 2 + JfifSegmentLength];
        result[0] = 0xFF;
        result[1] = 0xD8;
        result[2] = 0xFF;
        result[3] = 0xE0;
        result[4] = 0;
        result[5] = JfifSegmentLength;
        "JFIF\0"u8.ToArray().CopyTo(result, 6);
        result[11] = 1;   // version 1.01
        result[12] = 1;
        WriteJfifDensity(result, 2, density);
        Buffer.BlockCopy(jpeg, 2, result, 4 + JfifSegmentLength, jpeg.Length - 2);
        return result;
    }

    private static void WriteJfifDensity(byte[] jpeg, int marker, int density)
    {
        var at = marker + JfifUnitsOffset;
        jpeg[at] = JfifDotsPerInch;
        jpeg[at + 1] = (byte)(density >> 8);
        jpeg[at + 2] = (byte)density;
        jpeg[at + 3] = (byte)(density >> 8);
        jpeg[at + 4] = (byte)density;
    }

    // Whether a JFIF marker added to this JPEG would change how a decoder reads its colours: an APPE "Adobe"
    // segment states the colour transform itself, and three components named R, G and B are read as RGB only
    // while no JFIF marker says YCbCr.
    private static bool JfifWouldRecolour(byte[] jpeg)
    {
        var i = 2;
        while (i + 4 < jpeg.Length && jpeg[i] == 0xFF)
        {
            var marker = jpeg[i + 1];
            var length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (marker is 0xDA or 0xD9) return false;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return jpeg.Length > i + 16 && jpeg[i + 9] == 3
                    && jpeg[i + 10] == (byte)'R' && jpeg[i + 13] == (byte)'G' && jpeg[i + 16] == (byte)'B';
            if (marker == 0xEE && length >= 7 && i + 9 < jpeg.Length && jpeg[i + 4] == (byte)'A' && jpeg[i + 5] == (byte)'d')
                return true;
            i += 2 + length;
        }
        return false;
    }

    private static byte[] PngWithDensity(byte[] png, int pixelsPerMetre)
    {
        using var output = new MemoryStream(png.Length + PngChunkOverhead + PhysLength);
        output.Write(png, 0, PngSignatureLength);
        var at = PngSignatureLength;
        var written = false;
        while (at + PngChunkOverhead <= png.Length)
        {
            var length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
            var type = Compat.Latin1.GetString(png, at + 4, 4);
            var size = PngChunkOverhead + length;
            if (length < 0 || at + size > png.Length) break;
            if (!written && (type == "pHYs" || type == "IDAT" || type == "PLTE"))
            {
                PngEncoder.WritePhysChunk(output, pixelsPerMetre, pixelsPerMetre);
                written = true;
            }
            if (type != "pHYs") output.Write(png, at, size);
            at += size;
        }
        output.Write(png, at, png.Length - at);
        return written ? output.ToArray() : png;
    }
}
