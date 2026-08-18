using System;
using System.Collections.Generic;
using System.Globalization;

namespace Aspose.Pdf.IO;

/// <summary>
/// Managed DICOM (.dcm) still-image decoder for the generator image pipeline.
/// Handles the native (uncompressed) transfer syntaxes — implicit and explicit
/// VR little-endian — in both the standard form (128-byte preamble + "DICM")
/// and the headerless raw data-set form some modalities export. Grayscale
/// (MONOCHROME1/2, 8- or 16-bit, signed or unsigned) is windowed to 8-bit via
/// the Modality LUT (RescaleSlope/Intercept) and the VOI window (WindowCenter/
/// Width, PS3.3 C.11.2.1.2 linear function); RGB (SamplesPerPixel 3) passes
/// through. Encapsulated (compressed) pixel data is declined — the caller
/// falls back to its other decoders.
/// </summary>
internal static class DicomDecoder
{
    /// <summary>True when the bytes look like a DICOM data set (with or without
    /// the "DICM" preamble). The headerless probe walks a few leading elements
    /// so arbitrary binaries that merely start with 0x0008 don't false-positive.</summary>
    public static bool IsDicom(byte[]? data)
    {
        if (data is null || data.Length < 16) return false;
        if (data.Length > 132 && data[128] == 'D' && data[129] == 'I' && data[130] == 'C' && data[131] == 'M')
            return true;
        // Headerless data set: must start at tag group 0x0002/0x0008 and the
        // first few elements must chain consistently in either VR form.
        var group = (ushort)(data[0] | (data[1] << 8));
        if (group != 0x0002 && group != 0x0008) return false;
        return ProbeElements(data, 0, explicitVr: true) || ProbeElements(data, 0, explicitVr: false);
    }

    private static bool ProbeElements(byte[] d, int pos, bool explicitVr)
    {
        ushort lastGroup = 0;
        for (var n = 0; n < 4; n++)
        {
            if (pos + 8 > d.Length) return n > 0;
            var group = (ushort)(d[pos] | (d[pos + 1] << 8));
            if (group < lastGroup || group > 0x7FE0) return false;
            lastGroup = group;
            if (!TryReadHeader(d, ref pos, explicitVr, out _, out _, out var len, out _))
                return false;
            if (len == 0xFFFFFFFF || (long)pos + len > d.Length) return false;
            pos += (int)len;
        }
        return true;
    }

    /// <summary>Decode every frame to an 8-bit PNG (grayscale or RGB).
    /// Returns null when the file isn't a decodable native-syntax DICOM.</summary>
    public static List<byte[]>? DecodeFramesAsPng(byte[]? data)
    {
        if (data is null) return null;
        try { return DecodeCore(data); }
        catch { return null; }
    }

    private static List<byte[]>? DecodeCore(byte[] data)
    {
        var pos = 0;
        var explicitVr = false;
        var haveVrMode = false;
        if (data.Length > 132 && data[128] == 'D' && data[131] == 'M')
        {
            pos = 132;
            // File meta group (0002,xxxx) is always explicit VR LE; it names the
            // transfer syntax of the body that follows.
            string? ts = null;
            while (pos + 8 <= data.Length)
            {
                var group = (ushort)(data[pos] | (data[pos + 1] << 8));
                if (group != 0x0002) break;
                var p = pos;
                if (!TryReadHeader(data, ref p, explicitVr: true, out _, out var elem, out var len, out _))
                    return null;
                if (len == 0xFFFFFFFF || (long)p + len > data.Length) return null;
                if (elem == 0x0010)
                    ts = System.Text.Encoding.ASCII.GetString(data, p, (int)len).Trim('\0', ' ');
                pos = p + (int)len;
            }
            if (ts is not null)
            {
                if (ts == "1.2.840.10008.1.2") { explicitVr = false; haveVrMode = true; }
                else if (ts == "1.2.840.10008.1.2.1") { explicitVr = true; haveVrMode = true; }
                else return null; // big-endian or encapsulated (compressed) syntax
            }
        }
        if (!haveVrMode)
        {
            // Sniff the body's VR form from its first element.
            var p = pos;
            explicitVr = TryReadHeader(data, ref p, explicitVr: true, out _, out _, out var lenE, out var vrE)
                         && vrE is not null && lenE != 0xFFFFFFFF && (long)p + lenE <= data.Length
                         && ProbeElements(data, pos, explicitVr: true);
        }

        int rows = 0, cols = 0, bitsAllocated = 8, samples = 1, planarConfig = 0;
        var signed = false;
        var frames = 1;
        var photometric = "MONOCHROME2";
        double slope = 1, intercept = 0;
        double? winCenter = null, winWidth = null;
        byte[]? pixelData = null;

        while (pos + 8 <= data.Length)
        {
            if (!TryReadHeader(data, ref pos, explicitVr, out var group, out var elem, out var len, out var vr))
                return null;
            if (len == 0xFFFFFFFF)
            {
                if (group == 0x7FE0 && elem == 0x0010)
                    return null; // encapsulated (compressed) pixel data
                SkipUndefinedLength(data, ref pos);
                continue;
            }
            if ((long)pos + len > data.Length)
            {
                // A truncated trailing element (some exports pad the last tag);
                // usable only if the pixel data was already seen.
                break;
            }
            var start = pos;
            pos += (int)len;
            if (group == 0x0028)
            {
                switch (elem)
                {
                    case 0x0002: samples = ReadUs(data, start, (int)len); break;
                    case 0x0004: photometric = ReadString(data, start, (int)len).Trim(); break;
                    case 0x0006: planarConfig = ReadUs(data, start, (int)len); break;
                    case 0x0008: frames = Math.Max(1, (int)ReadDs(data, start, (int)len, 1)); break;
                    case 0x0010: rows = ReadUs(data, start, (int)len); break;
                    case 0x0011: cols = ReadUs(data, start, (int)len); break;
                    case 0x0100: bitsAllocated = ReadUs(data, start, (int)len); break;
                    case 0x0103: signed = ReadUs(data, start, (int)len) == 1; break;
                    case 0x1050: winCenter = TryReadDs(data, start, (int)len); break;
                    case 0x1051: winWidth = TryReadDs(data, start, (int)len); break;
                    case 0x1052: intercept = ReadDs(data, start, (int)len, 0); break;
                    case 0x1053: slope = ReadDs(data, start, (int)len, 1); break;
                }
            }
            else if (group == 0x7FE0 && elem == 0x0010)
            {
                pixelData = new byte[len];
                Array.Copy(data, start, pixelData, 0, (int)len);
                break; // pixel data is the last interesting element
            }
        }

        if (pixelData is null || rows <= 0 || cols <= 0) return null;
        if (bitsAllocated != 8 && bitsAllocated != 16) return null;
        if (samples != 1 && samples != 3) return null;

        var bytesPerSample = bitsAllocated / 8;
        var frameSize = rows * cols * samples * bytesPerSample;
        if (frameSize <= 0 || pixelData.Length < frameSize) return null;
        var frameCount = Math.Max(1, Math.Min(frames, pixelData.Length / frameSize));

        var result = new List<byte[]>(frameCount);
        for (var f = 0; f < frameCount; f++)
        {
            var offset = f * frameSize;
            byte[] png;
            if (samples == 3)
            {
                // RGB pass-through (8-bit only; 16-bit RGB DICOM is vanishingly rare).
                if (bitsAllocated != 8) return null;
                var rgb = new byte[rows * cols * 3];
                if (planarConfig == 1)
                {
                    var plane = rows * cols;
                    for (var i = 0; i < plane; i++)
                    {
                        rgb[i * 3] = pixelData[offset + i];
                        rgb[i * 3 + 1] = pixelData[offset + plane + i];
                        rgb[i * 3 + 2] = pixelData[offset + 2 * plane + i];
                    }
                }
                else
                {
                    Array.Copy(pixelData, offset, rgb, 0, rgb.Length);
                }
                png = PngEncoder.Encode(rgb, cols, rows, colorType: 2);
            }
            else
            {
                png = PngEncoder.Encode(
                    WindowMonochrome(pixelData, offset, rows * cols, bitsAllocated, signed,
                                     slope, intercept, winCenter, winWidth,
                                     invert: photometric.StartsWith("MONOCHROME1", StringComparison.OrdinalIgnoreCase)),
                    cols, rows, colorType: 0);
            }
            result.Add(png);
        }
        return result;
    }

    /// <summary>Modality LUT + linear VOI window (PS3.3 C.11.2.1.2) to 8-bit gray.
    /// Without a stored window the frame is min/max auto-scaled.</summary>
    private static byte[] WindowMonochrome(byte[] src, int offset, int count, int bitsAllocated,
        bool signed, double slope, double intercept, double? center, double? width, bool invert)
    {
        var vals = new double[count];
        if (bitsAllocated == 8)
        {
            for (var i = 0; i < count; i++)
            {
                double raw = signed ? unchecked((sbyte)src[offset + i]) : src[offset + i];
                vals[i] = raw * slope + intercept;
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var u = (ushort)(src[offset + i * 2] | (src[offset + i * 2 + 1] << 8));
                double raw = signed ? unchecked((short)u) : u;
                vals[i] = raw * slope + intercept;
            }
        }

        double c, w;
        if (center.HasValue && width.HasValue && width.Value >= 1)
        {
            c = center.Value;
            w = width.Value;
        }
        else
        {
            double min = double.MaxValue, max = double.MinValue;
            for (var i = 0; i < count; i++)
            {
                if (vals[i] < min) min = vals[i];
                if (vals[i] > max) max = vals[i];
            }
            if (max <= min) { min = 0; max = Math.Max(1, max); }
            c = (min + max) / 2;
            w = max - min + 1;
        }

        var denom = Math.Max(1, w - 1);
        var gray = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var y = ((vals[i] - (c - 0.5)) / denom + 0.5) * 255.0;
            var b = y <= 0 ? (byte)0 : y >= 255 ? (byte)255 : (byte)Math.Round(y);
            gray[i] = invert ? (byte)(255 - b) : b;
        }
        return gray;
    }

    /// <summary>Read one element header, advancing <paramref name="pos"/> past it.
    /// Item/delimiter tags (group FFFE) always carry a plain 4-byte length.</summary>
    private static bool TryReadHeader(byte[] d, ref int pos, bool explicitVr,
        out ushort group, out ushort elem, out uint len, out string? vr)
    {
        group = 0; elem = 0; len = 0; vr = null;
        if (pos + 8 > d.Length) return false;
        group = (ushort)(d[pos] | (d[pos + 1] << 8));
        elem = (ushort)(d[pos + 2] | (d[pos + 3] << 8));
        if (!explicitVr || group == 0xFFFE)
        {
            len = (uint)(d[pos + 4] | (d[pos + 5] << 8) | (d[pos + 6] << 16) | (d[pos + 7] << 24));
            pos += 8;
            return true;
        }
        var v0 = d[pos + 4];
        var v1 = d[pos + 5];
        if (v0 < 'A' || v0 > 'Z' || v1 < 'A' || v1 > 'Z') return false;
        vr = ((char)v0).ToString() + (char)v1;
        if (vr is "OB" or "OW" or "OF" or "OL" or "OD" or "SQ" or "UC" or "UR" or "UT" or "UN")
        {
            if (pos + 12 > d.Length) return false;
            len = (uint)(d[pos + 8] | (d[pos + 9] << 8) | (d[pos + 10] << 16) | (d[pos + 11] << 24));
            pos += 12;
        }
        else
        {
            len = (ushort)(d[pos + 6] | (d[pos + 7] << 8));
            pos += 8;
        }
        return true;
    }

    /// <summary>Skip a sequence with undefined length: consume items (recursing
    /// into nested undefined lengths) until the sequence delimiter (FFFE,E0DD).</summary>
    private static void SkipUndefinedLength(byte[] d, ref int pos)
    {
        while (pos + 8 <= d.Length)
        {
            var group = (ushort)(d[pos] | (d[pos + 1] << 8));
            var elem = (ushort)(d[pos + 2] | (d[pos + 3] << 8));
            var len = (uint)(d[pos + 4] | (d[pos + 5] << 8) | (d[pos + 6] << 16) | (d[pos + 7] << 24));
            pos += 8;
            if (group == 0xFFFE && elem == 0xE0DD) return;
            if (len == 0xFFFFFFFF) SkipUndefinedLength(d, ref pos);
            else pos = (int)Math.Min((long)pos + len, d.Length);
        }
    }

    private static int ReadUs(byte[] d, int pos, int len)
        => len >= 2 ? d[pos] | (d[pos + 1] << 8) : 0;

    private static string ReadString(byte[] d, int pos, int len)
        => System.Text.Encoding.ASCII.GetString(d, pos, len).Trim('\0', ' ');

    /// <summary>Decimal String; multi-valued attributes ("c1\c2\...") take the first value.</summary>
    private static double ReadDs(byte[] d, int pos, int len, double fallback)
        => TryReadDs(d, pos, len) ?? fallback;

    private static double? TryReadDs(byte[] d, int pos, int len)
    {
        var s = ReadString(d, pos, len);
        var sep = s.IndexOf('\\');
        if (sep >= 0) s = s[..sep];
        return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }
}
