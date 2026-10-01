using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Pulls an image's samples out of whatever the program offered as a data source, and
/// converts them to the eight-bit device RGB the page writer emits.
/// </summary>
internal static class PsImageData
{
    /// <summary>The bit depth of an emitted component.</summary>
    private const int OutputBits = 8;

    /// <summary>The largest value an emitted component holds.</summary>
    private const int OutputMax = 255;

    /// <summary>How many times a data procedure may be run without producing bytes
    /// before it is treated as exhausted.</summary>
    private const int EmptyRunLimit = 8;

    /// <summary>Read <paramref name="needed"/> bytes from the request's sources. When
    /// the program supplies one source per component the streams are interleaved into
    /// the sample order the rest of the pipeline expects.</summary>
    public static byte[]? Read(PsInterpreter interpreter, PsImageRequest request, int needed)
    {
        if (request.Sources.Count == 0) return null;
        if (request.Sources.Count == 1)
            return Fill(interpreter, request.Sources[0], needed);
        return Interleave(interpreter, request, needed);
    }

    /// <summary>One stream per component: read each to its own length, then weave the
    /// components back together sample by sample.</summary>
    private static byte[]? Interleave(PsInterpreter interpreter, PsImageRequest request, int needed)
    {
        var planes = new List<byte[]>(request.Sources.Count);
        var each = needed / Math.Max(1, request.Sources.Count);
        foreach (var source in request.Sources)
        {
            var plane = Fill(interpreter, source, each);
            if (plane is null) return null;
            planes.Add(plane);
        }

        var result = new byte[needed];
        var at = 0;
        for (var k = 0; k < each && at < needed; k++)
            foreach (var plane in planes)
            {
                if (at >= needed) break;
                result[at++] = k < plane.Length ? plane[k] : (byte)0;
            }

        return result;
    }

    /// <summary>Read from one source until enough bytes have arrived. A string is the
    /// whole supply; a file is read directly; a procedure is run again and again, each
    /// run expected to leave a string.</summary>
    private static byte[]? Fill(PsInterpreter interpreter, PsValue source, int needed)
    {
        var buffer = new byte[needed];
        var filled = 0;
        if (source.Type == PsType.String)
        {
            Copy(source.AsString!.ToArray(), buffer, ref filled);
            return buffer;
        }

        if (source.Type == PsType.File)
        {
            Copy(source.AsFile!.Read(needed), buffer, ref filled);
            return buffer;
        }

        var empty = 0;
        while (filled < needed && empty < EmptyRunLimit)
        {
            var chunk = RunDataProcedure(interpreter, source);
            if (chunk is null) return filled > 0 ? buffer : null;
            if (chunk.Length == 0)
            {
                empty++;
                continue;
            }

            empty = 0;
            Copy(chunk, buffer, ref filled);
        }

        return buffer;
    }

    /// <summary>Run a data procedure once and take the string it leaves. The idiom the
    /// corpus uses is a read into a scratch string, so the interpreter's own operators
    /// do the work and this only has to collect the result.</summary>
    private static byte[]? RunDataProcedure(PsInterpreter interpreter, PsValue source)
    {
        var before = interpreter.OperandCount;
        try
        {
            interpreter.Invoke(source);
        }
        catch (PsErrorSignal)
        {
            return null;
        }

        if (interpreter.OperandCount <= before) return null;
        var value = interpreter.Pop();
        while (interpreter.OperandCount > before) interpreter.Pop();
        return value.Type == PsType.String ? value.AsString!.ToArray() : null;
    }

    private static void Copy(byte[] chunk, byte[] buffer, ref int filled)
    {
        var n = Math.Min(chunk.Length, buffer.Length - filled);
        Array.Copy(chunk, 0, buffer, filled, n);
        filled += n;
    }

    /// <summary>Convert packed samples to eight-bit device RGB, row by row. Each row
    /// starts on a byte boundary, as PostScript specifies.</summary>
    public static byte[]? ToRgb(PsImageRequest request, byte[] raw)
    {
        var w = request.Width;
        var h = request.Height;
        var comps = Math.Max(1, request.Components);
        var bits = Math.Max(1, request.BitsPerComponent);
        var rowBytes = ((long)w * comps * bits + 7) / 8;
        var rgb = new byte[(long)w * h * 3 <= int.MaxValue ? w * h * 3 : 0];
        if (rgb.Length == 0) return null;
        var sample = new double[comps];
        for (var y = 0; y < h; y++)
        {
            var rowStart = y * rowBytes;
            for (var x = 0; x < w; x++)
            {
                ReadSample(raw, rowStart, x, comps, bits, request.Decode, sample);
                WriteRgb(rgb, (y * w + x) * 3, sample, comps);
            }
        }

        return rgb;
    }

    /// <summary>Read one sample's components, normalised to zero..one and mapped
    /// through the decode array when the program gave one.</summary>
    private static void ReadSample(byte[] raw, long rowStart, int x, int comps, int bits,
        double[]? decode, double[] sample)
    {
        var max = (1 << bits) - 1;
        for (var c = 0; c < comps; c++)
        {
            var index = (long)(x * comps + c) * bits;
            var value = ReadBits(raw, rowStart * 8 + index, bits) / (double)max;
            if (decode != null && decode.Length >= 2 * (c + 1))
            {
                var lo = decode[2 * c];
                var hi = decode[2 * c + 1];
                value = lo + value * (hi - lo);
            }

            sample[c] = value < 0 ? 0 : value > 1 ? 1 : value;
        }
    }

    /// <summary>Read a big-endian bit field, zero past the end of the buffer.</summary>
    private static int ReadBits(byte[] data, long bitOffset, int bits)
    {
        var value = 0;
        for (var k = 0; k < bits; k++)
        {
            var at = bitOffset + k;
            var index = at >> 3;
            var bit = index >= 0 && index < data.Length
                ? (data[index] >> (7 - (int)(at & 7))) & 1
                : 0;
            value = (value << 1) | bit;
        }

        return value;
    }

    /// <summary>Write one sample as RGB, converting from the space its component count
    /// implies. Anything unrecognised is read as grey from its first component.</summary>
    private static void WriteRgb(byte[] rgb, int at, double[] sample, int comps)
    {
        PsColor color;
        if (comps >= 4) color = PsColor.FromCmyk(sample[0], sample[1], sample[2], sample[3]);
        else if (comps == 3) color = PsColor.FromRgb(sample[0], sample[1], sample[2]);
        else color = PsColor.FromGray(sample[0]);
        rgb[at] = Byte(color.Red);
        rgb[at + 1] = Byte(color.Green);
        rgb[at + 2] = Byte(color.Blue);
    }

    private static byte Byte(double value) =>
        (byte)Math.Round(value * OutputMax, MidpointRounding.AwayFromZero);

    /// <summary>The bit depth every emitted image uses.</summary>
    public static int EmittedBits => OutputBits;
}
