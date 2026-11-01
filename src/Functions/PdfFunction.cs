// PDF function dictionaries — PDF32000_2008 §7.10
//
// Functions define mathematical mappings from inputs to outputs.
// Used in colour spaces (tint transforms), shading patterns, soft masks.
//
// Types: 0 (Sampled), 2 (Exponential), 3 (Stitching), 4 (PostScript)

using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Functions;

/// <summary>Abstract base for all PDF function types.</summary>
public abstract class PdfFunction
{
    /// <summary>Input domain — one [min, max] pair per input dimension.</summary>
    public double[][] Domain { get; }

    /// <summary>Output range — one [min, max] pair per output. Null when /Range is absent.</summary>
    public double[][]? Range { get; }

#pragma warning disable S2368 // public API: the published signature
    protected PdfFunction(double[][] domain, double[][]? range)
#pragma warning restore S2368
    {
        Domain = domain;
        Range = range;
    }

    /// <summary>Evaluate the function. Inputs are clamped to domain; outputs to range.</summary>
    public double[] Evaluate(double[] inputs)
    {
        var clamped = new double[inputs.Length];
        for (int i = 0; i < inputs.Length; i++) clamped[i] = ClampInput(i, inputs[i]);
        var raw = EvaluateCore(clamped);
        if (Range is null) return raw;
        for (int i = 0; i < raw.Length && i < Range.Length; i++) raw[i] = ClampOutput(i, raw[i]);
        return raw;
    }

    /// <summary>The function of one input, written into a buffer the caller keeps: a shading
    /// rasteriser evaluates once per pixel, and the arrays <see cref="Evaluate"/> returns would be
    /// as many. Returns the function's output count; the buffer holds the outputs, clamped as
    /// <see cref="Evaluate"/> clamps them, when it is at least that long. The base evaluates
    /// through <see cref="Evaluate"/>; the gradient function types evaluate in place.</summary>
    internal virtual int EvaluateInto(double input, double[] outputs)
    {
        var result = Evaluate([input]);
        if (result.Length <= outputs.Length) Array.Copy(result, outputs, result.Length);
        return result.Length;
    }

    /// <summary>The input clamped to its domain; an input beyond the declared domain is free.</summary>
    protected double ClampInput(int i, double x)
    {
        var (lo, hi) = i < Domain.Length ? (Domain[i][0], Domain[i][1]) : (double.NegativeInfinity, double.PositiveInfinity);
        return Math.Max(lo, Math.Min(hi, x));
    }

    /// <summary>The output clamped to its range, when a range is declared for it.</summary>
    protected double ClampOutput(int i, double y)
        => Range is not null && i < Range.Length ? Math.Max(Range[i][0], Math.Min(Range[i][1], y)) : y;

    protected abstract double[] EvaluateCore(double[] inputs);

    // ── Factory ──────────────────────────────────────────────────────────────

    /// <summary>Parse a PDF function from a dictionary/stream reference, or from an
    /// array of 1-output functions (a shading's /Function, §8.7.4.5), read as
    /// one function of their outputs. Without a reader the object must stand alone:
    /// its entries are direct and a stream's bytes are decoded by its own filters.</summary>
    internal static PdfFunction? Parse(PdfObject? obj, PdfReader? reader)
    {
        if (obj is null) return null;
        try
        {
            PdfDictionary dict;
            byte[]? streamData = null;
            var resolved = Resolve(obj, reader);
            if (resolved is PdfArray array)
                return ArrayFunction.Create(array, reader);
            if (resolved is PdfStream stream)
            {
                dict = stream.Dict;
                streamData = reader is null
                    ? StreamFilter.Decode(stream.RawData, stream.Dict)
                    : reader.DecodeStream(stream);
            }
            else if (resolved is PdfDictionary d)
                dict = d;
            else
                return null;

            var ft = (int)dict.GetInt("FunctionType");
            return ft switch
            {
                0 => SampledFunction.Create(dict, reader, streamData),
                2 => ExponentialFunction.Create(dict, reader),
                3 => StitchingFunction.Create(dict, reader),
                4 => PostScriptFunction.Create(dict, streamData),
                _ => null,
            };
        }
        catch { return null; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A function entry that is an array, followed through an indirect
    /// reference: a producer may store /Encode, /Bounds, /C0 or /Domain as their own
    /// objects, and an unresolved reference reads as an absent entry.</summary>
    internal static PdfArray? ArrayEntry(PdfDictionary dict, string key, PdfReader? reader)
        => Resolve(dict.Get(key), reader) as PdfArray;

    /// <summary>A numeric function entry, followed through an indirect reference.</summary>
    internal static double NumberEntry(PdfDictionary dict, string key, PdfReader? reader, double defaultValue)
        => Resolve(dict.Get(key), reader) switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => defaultValue,
        };

    /// <summary>An entry followed through an indirect reference where there is a
    /// reader to follow it with; a standalone object's entries are its own.</summary>
    private static PdfObject? Resolve(PdfObject? obj, PdfReader? reader)
    {
        if (reader is not null) return reader.Resolve(obj);
        if (obj is PdfNull) return null;
        return obj;
    }

    internal static double[][] ParsePairs(PdfArray? arr)
    {
        if (arr is null) return [];
        var pairs = new double[arr.Count / 2][];
        for (int i = 0; i + 1 < arr.Count; i += 2)
            pairs[i / 2] = [PdfArrayHelper.GetDouble(arr, i), PdfArrayHelper.GetDouble(arr, i + 1)];
        return pairs;
    }
}

// ── Type 2: Exponential interpolation ────────────────────────────────────────

/// <summary>Exponential interpolation function (§7.10.3).</summary>
public sealed class ExponentialFunction : PdfFunction
{
    public double[] C0 { get; }
    public double[] C1 { get; }
    public double N { get; }

    private ExponentialFunction(double[][] domain, double[][]? range, double[] c0, double[] c1, double n)
        : base(domain, range)
    {
        C0 = c0; C1 = c1; N = n;
    }

    protected override double[] EvaluateCore(double[] inputs)
    {
        var x = inputs.Length > 0 ? inputs[0] : 0;
        var result = new double[C0.Length];
        Interpolate(x, result);
        return result;
    }

    internal override int EvaluateInto(double input, double[] outputs)
    {
        if (outputs.Length < C0.Length) return C0.Length;
        Interpolate(ClampInput(0, input), outputs);
        for (int i = 0; i < C0.Length; i++) outputs[i] = ClampOutput(i, outputs[i]);
        return C0.Length;
    }

    private void Interpolate(double x, double[] result)
    {
        var pow = Math.Pow(x, N);
        for (int i = 0; i < C0.Length; i++)
            result[i] = C0[i] + pow * (C1[i] - C0[i]);
    }

    internal static ExponentialFunction? Create(PdfDictionary dict, PdfReader? reader)
    {
        var domain = ParsePairs(ArrayEntry(dict, "Domain", reader));
        var range = ArrayEntry(dict, "Range", reader) is { } r ? ParsePairs(r) : null;
        var n = NumberEntry(dict, "N", reader, 1);
        var c0Arr = ArrayEntry(dict, "C0", reader);
        var c1Arr = ArrayEntry(dict, "C1", reader);
        var c0 = c0Arr is not null ? PdfArrayHelper.ToDoubleArray(c0Arr) : [0.0];
        var c1 = c1Arr is not null ? PdfArrayHelper.ToDoubleArray(c1Arr) : [1.0];
        return new ExponentialFunction(domain, range, c0, c1, n);
    }
}

// ── Type 3: Stitching function ───────────────────────────────────────────────

/// <summary>Stitching function (§7.10.4): combines sub-functions over sub-domains.</summary>
public sealed class StitchingFunction : PdfFunction
{
    public PdfFunction[] Functions { get; }
    public double[] Bounds { get; }
    public double[][] Encode { get; }

    private StitchingFunction(double[][] domain, double[][]? range,
        PdfFunction[] functions, double[] bounds, double[][] encode)
        : base(domain, range)
    {
        Functions = functions; Bounds = bounds; Encode = encode;
    }

    protected override double[] EvaluateCore(double[] inputs)
    {
        var x = inputs.Length > 0 ? inputs[0] : 0;
        var k = SubFunctionAt(x, out var t);
        return Functions[k].Evaluate([t]);
    }

    internal override int EvaluateInto(double input, double[] outputs)
    {
        var k = SubFunctionAt(ClampInput(0, input), out var t);
        var count = Functions[k].EvaluateInto(t, outputs);
        if (count > outputs.Length) return count;
        for (int i = 0; i < count; i++) outputs[i] = ClampOutput(i, outputs[i]);
        return count;
    }

    /// <summary>The sub-function the input falls to, and the input mapped into its encode range.</summary>
    private int SubFunctionAt(double x, out double t)
    {
        int k = 0;
        for (int i = 0; i < Bounds.Length; i++)
        {
            if (x < Bounds[i]) break;
            k = i + 1;
        }
        if (k >= Functions.Length) k = Functions.Length - 1;

        var dLo = k == 0 ? Domain[0][0] : Bounds[k - 1];
        var dHi = k < Bounds.Length ? Bounds[k] : Domain[0][1];
        var eLo = k < Encode.Length ? Encode[k][0] : 0;
        var eHi = k < Encode.Length ? Encode[k][1] : 1;
        t = dHi - dLo != 0 ? eLo + (x - dLo) * (eHi - eLo) / (dHi - dLo) : eLo;
        return k;
    }

    internal static StitchingFunction? Create(PdfDictionary dict, PdfReader? reader)
    {
        var domain = ParsePairs(ArrayEntry(dict, "Domain", reader));
        var range = ArrayEntry(dict, "Range", reader) is { } r ? ParsePairs(r) : null;
        var fnArr = ArrayEntry(dict, "Functions", reader);
        if (fnArr is null) return null;
        var fns = new List<PdfFunction>();
        foreach (var item in fnArr)
        {
            var f = Parse(item, reader);
            if (f is not null) fns.Add(f);
        }
        var boundsArr = ArrayEntry(dict, "Bounds", reader);
        var bounds = boundsArr is not null ? PdfArrayHelper.ToDoubleArray(boundsArr) : [];
        var encodeArr = ArrayEntry(dict, "Encode", reader);
        var encode = encodeArr is not null ? ParsePairs(encodeArr) : [];
        return new StitchingFunction(domain, range, fns.ToArray(), bounds, encode);
    }
}

// ── Array of 1-output functions ──────────────────────────────────────────────

/// <summary>An array of n 1-output functions standing for one n-output function
/// (a shading's /Function, §8.7.4.5): every member takes the same inputs
/// and gives one output, in array order. A member giving more than one output
/// contributes its first only; one giving none contributes 0. The domain is the
/// first member's; the range is the members' first ranges when all declare one,
/// else none - each member still clamps its own output to its own range.</summary>
internal sealed class ArrayFunction : PdfFunction
{
    private readonly PdfFunction[] _members;

    /// <summary>A member's outputs, per thread. Taken while a call evaluates its
    /// members and put back after, so a member that holds an array function deeper
    /// down (a stitching function's sub-function written as an array) gets its own.</summary>
    [ThreadStatic] private static double[]? _scratch;

    private ArrayFunction(PdfFunction[] members, double[][]? range)
        : base(members[0].Domain, range)
    {
        _members = members;
    }

    protected override double[] EvaluateCore(double[] inputs)
    {
        var result = new double[_members.Length];
        for (int i = 0; i < _members.Length; i++)
            result[i] = _members[i].Evaluate(inputs) is { Length: > 0 } output ? output[0] : 0;
        return result;
    }

    internal override int EvaluateInto(double input, double[] outputs)
    {
        if (outputs.Length < _members.Length) return _members.Length;
        input = ClampInput(0, input);
        var scratch = _scratch ?? new double[1];
        _scratch = null;
        for (int i = 0; i < _members.Length; i++)
        {
            var count = _members[i].EvaluateInto(input, scratch);
            if (count > scratch.Length)
            {
                scratch = new double[count];
                count = _members[i].EvaluateInto(input, scratch);
            }
            outputs[i] = ClampOutput(i, count > 0 ? scratch[0] : 0);
        }
        _scratch = scratch;
        return _members.Length;
    }

    /// <summary>The array's members as one function; null when the array is empty or
    /// a member is not a function, or is itself an array.</summary>
    internal static ArrayFunction? Create(PdfArray array, PdfReader? reader)
    {
        if (array.Count == 0) return null;
        var members = new PdfFunction[array.Count];
        var ranges = new double[array.Count][];
        var ranged = true;
        for (int i = 0; i < array.Count; i++)
        {
            var member = Parse(array[i], reader);
            if (member is null or ArrayFunction) return null;
            members[i] = member;
            if (member.Range is { Length: > 0 } range) ranges[i] = range[0];
            else ranged = false;
        }
        return new ArrayFunction(members, ranged ? ranges : null);
    }
}

// ── Type 4: PostScript calculator ────────────────────────────────────────────

/// <summary>PostScript calculator function (§7.10.5).</summary>
public sealed class PostScriptFunction : PdfFunction
{
    private readonly string _program;

    private PostScriptFunction(double[][] domain, double[][]? range, string program)
        : base(domain, range)
    {
        _program = program;
    }

    protected override double[] EvaluateCore(double[] inputs)
    {
        return PostScriptEvaluator.Evaluate(_program, inputs);
    }

    internal static PostScriptFunction? Create(PdfDictionary dict, byte[]? streamData)
    {
        var domain = ParsePairs(dict.Get("Domain") as PdfArray);
        var range = dict.Get("Range") is PdfArray r ? ParsePairs(r) : null;
        var program = streamData is not null ? Encoding.ASCII.GetString(streamData).Trim() : "{}";
        return new PostScriptFunction(domain, range, program);
    }
}

// ── Type 0: Sampled function ─────────────────────────────────────────────────

/// <summary>Sampled function (§7.10.2): lookup table with linear interpolation.</summary>
public sealed class SampledFunction : PdfFunction
{
    public int[] Size { get; }
    public int BitsPerSample { get; }

    /// <summary>How samples are interpolated: 1 for multilinear, 3 for a cubic spline.
    /// ⚠ The spline is applied to a function of ONE input; a function of several
    /// stays multilinear whatever it declares.</summary>
    public int Order { get; }
    private readonly double[] _samples;
    private readonly int _nOutputs;
    private readonly double[][]? _encode; // per input: sample-grid range (default [0, Size_i-1])
    private readonly double[][]? _decode; // per output: value range (default = Range)

    private SampledFunction(double[][] domain, double[][]? range,
        int[] size, int bitsPerSample, double[] samples, int nOutputs,
        double[][]? encode, double[][]? decode, int order)
        : base(domain, range)
    {
        Size = size; BitsPerSample = bitsPerSample; _samples = samples; _nOutputs = nOutputs;
        _encode = encode; _decode = decode; Order = order;
    }

    protected override double[] EvaluateCore(double[] inputs)
    {
        // General m-input multilinear interpolation (§7.10.2). Sample order: the
        // FIRST input varies fastest. A DeviceN tint transform (e.g. Cyan+Yellow
        // spot mixes) is a 2-input sampled function — the old 1-D-only path
        // returned all-zero components for it, painting everything black.
        var m = Size.Length;
        var result = new double[_nOutputs];
        if (m == 0 || inputs.Length < m) return result;

        // Domain → Encode → clamp to the sample grid.
        var e = new double[m];
        for (int i = 0; i < m; i++)
        {
            double lo = i < Domain.Length ? Domain[i][0] : 0;
            double hi = i < Domain.Length ? Domain[i][1] : 1;
            var x = Math.Max(Math.Min(inputs[i], Math.Max(lo, hi)), Math.Min(lo, hi));
            double e0 = _encode is not null && i < _encode.Length ? _encode[i][0] : 0;
            double e1 = _encode is not null && i < _encode.Length ? _encode[i][1] : Size[i] - 1;
            var t = hi - lo != 0 ? (x - lo) / (hi - lo) : 0;
            e[i] = Math.Max(0, Math.Min(Size[i] - 1, e0 + t * (e1 - e0)));
        }

        if (Order == CubicOrder && m == 1 && Size[0] > 2)
        {
            for (int c = 0; c < _nOutputs; c++)
                result[c] = NaturalCubicSpline(c, Size[0], e[0]);
            return Decoded(result);
        }

        // Accumulate the 2^m interpolation corners.
        var corners = 1 << m;
        for (int corner = 0; corner < corners; corner++)
        {
            double w = 1;
            long index = 0, stride = 1;
            for (int i = 0; i < m; i++)
            {
                var i0 = (int)Math.Floor(e[i]);
                var frac = e[i] - i0;
                var i1 = Math.Min(i0 + 1, Size[i] - 1);
                var hiBit = ((corner >> i) & 1) == 1;
                w *= hiBit ? frac : 1 - frac;
                index += (hiBit ? i1 : i0) * stride;
                stride *= Size[i];
            }
            if (w == 0) continue;
            var baseIdx = index * _nOutputs;
            for (int c = 0; c < _nOutputs; c++)
            {
                var si = baseIdx + c;
                if (si < _samples.Length) result[c] += w * _samples[si];
            }
        }

        return Decoded(result);
    }

    /// <summary>The /Order that asks for a cubic spline rather than multilinear
    /// interpolation (§7.10.2).</summary>
    private const int CubicOrder = 3;

    /// <summary>
    /// One output of a one-input function, interpolated at the encoded position
    /// <paramref name="x"/> by the natural cubic spline through all its samples:
    /// knots at the sample positions 0 .. n-1, second derivative zero at both ends.
    /// </summary>
    private double NaturalCubicSpline(int output, int n, double x)
    {
        var y = new double[n];
        for (int i = 0; i < n; i++) y[i] = _samples[i * _nOutputs + output];

        // Second derivatives at the knots: the tridiagonal system of unit spacing,
        // solved forwards and back (Thomas algorithm), zero at both ends.
        var second = new double[n];
        var upper = new double[n];
        var rhs = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            var diagonal = 4 - upper[i - 1];
            upper[i] = 1 / diagonal;
            rhs[i] = (6 * (y[i + 1] - 2 * y[i] + y[i - 1]) - rhs[i - 1]) / diagonal;
        }
        for (int i = n - 2; i > 0; i--) second[i] = rhs[i] - upper[i] * second[i + 1];

        var k = Math.Min((int)Math.Floor(x), n - 2);
        var b = x - k;
        var a = 1 - b;
        return a * y[k] + b * y[k + 1] + ((a * a * a - a) * second[k] + (b * b * b - b) * second[k + 1]) / 6;
    }

    /// <summary>Raw samples [0, 2^bps-1] mapped onto /Decode (default /Range,
    /// default [0,1] when neither is present).</summary>
    private double[] Decoded(double[] result)
    {
        var maxSample = (double)((1L << BitsPerSample) - 1);
        for (int c = 0; c < _nOutputs; c++)
        {
            double d0, d1;
            if (_decode is not null && c < _decode.Length) { d0 = _decode[c][0]; d1 = _decode[c][1]; }
            else if (Range is not null && c < Range.Length) { d0 = Range[c][0]; d1 = Range[c][1]; }
            else { d0 = 0; d1 = 1; }
            result[c] = d0 + result[c] / maxSample * (d1 - d0);
        }
        return result;
    }

    internal static SampledFunction? Create(PdfDictionary dict, PdfReader? reader, byte[]? streamData)
    {
        if (streamData is null) return null;
        var domain = ParsePairs(ArrayEntry(dict, "Domain", reader));
        var range = ArrayEntry(dict, "Range", reader) is { } r ? ParsePairs(r) : null;
        var sizeArr = ArrayEntry(dict, "Size", reader);
        if (sizeArr is null) return null;
        var size = new int[sizeArr.Count];
        for (int i = 0; i < sizeArr.Count; i++)
            size[i] = PdfArrayHelper.GetInt(sizeArr, i);
        var bps = (int)NumberEntry(dict, "BitsPerSample", reader, 0);
        if (bps <= 0) bps = 8;
        var encode = ArrayEntry(dict, "Encode", reader) is { } enc ? ParsePairs(enc) : null;
        var decode = ArrayEntry(dict, "Decode", reader) is { } dec ? ParsePairs(dec) : null;
        var nOutputs = range?.Length ?? 1;
        var totalSamples = 1;
        foreach (var s in size) totalSamples *= s;
        totalSamples *= nOutputs;
        var samples = DecodeSamples(streamData, bps, totalSamples);
        var order = (int)NumberEntry(dict, "Order", reader, 1);
        return new SampledFunction(domain, range, size, bps, samples, nOutputs, encode, decode, order);
    }

    private static double[] DecodeSamples(byte[] data, int bps, int count)
    {
        var result = new double[count];
        if (bps == 8)
        {
            for (int i = 0; i < count && i < data.Length; i++)
                result[i] = data[i];
        }
        else if (bps == 16)
        {
            for (int i = 0; i < count && i * 2 + 1 < data.Length; i++)
                result[i] = (data[i * 2] << 8) | data[i * 2 + 1];
        }
        else if (bps == 32)
        {
            // Unsigned: a sample with its top bit set is large, not negative.
            for (int i = 0; i < count && i * 4 + 3 < data.Length; i++)
                result[i] = (uint)((data[i * 4] << 24) | (data[i * 4 + 1] << 16) | (data[i * 4 + 2] << 8) | data[i * 4 + 3]);
        }
        else if (bps is 12 or 24)
        {
            // Samples that do not fill whole bytes evenly: read bit by bit, most
            // significant first, straight across byte boundaries.
            for (int i = 0; i < count; i++)
            {
                long value = 0;
                var bit = (long)i * bps;
                if ((bit + bps + 7) / 8 > data.Length) break;
                for (int b = 0; b < bps; b++, bit++)
                    value = (value << 1) | (uint)((data[bit / 8] >> (7 - (int)(bit % 8))) & 1);
                result[i] = value;
            }
        }
        else if (bps is 1 or 2 or 4)
        {
            // Sub-byte samples, big-endian bit packing within each byte.
            var perByte = 8 / bps;
            var mask = (1 << bps) - 1;
            for (int i = 0; i < count; i++)
            {
                var byteIdx = i / perByte;
                if (byteIdx >= data.Length) break;
                var shift = 8 - bps * (i % perByte + 1);
                result[i] = (data[byteIdx] >> shift) & mask;
            }
        }
        return result;
    }
}

// ── Helpers ──────────────────────────────────────────────────────────────────

internal static class PdfArrayHelper
{
    internal static double GetDouble(PdfArray arr, int index)
    {
        var obj = arr[index];
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0,
        };
    }

    internal static int GetInt(PdfArray arr, int index)
    {
        var obj = arr[index];
        return obj switch
        {
            PdfInteger i => (int)i.Value,
            PdfReal r => (int)r.Value,
            _ => 0,
        };
    }

    internal static double[] ToDoubleArray(PdfArray arr)
    {
        var result = new double[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = GetDouble(arr, i);
        return result;
    }

    internal static double GetDoubleFromDict(PdfDictionary dict, string key, double defaultValue)
    {
        var obj = dict.Get(key);
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => defaultValue,
        };
    }
}
