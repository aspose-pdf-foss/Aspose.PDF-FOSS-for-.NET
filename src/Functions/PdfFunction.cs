// PDF function dictionaries — PDF32000_2008 §7.10
//
// Functions define mathematical mappings from inputs to outputs.
// Used in colour spaces (tint transforms), shading patterns, soft masks.
//
// Types: 0 (Sampled), 2 (Exponential), 3 (Stitching), 4 (PostScript)

using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Functions;

/// <summary>Abstract base for all PDF function types.</summary>
public abstract class PdfFunction
{
    /// <summary>Input domain — one [min, max] pair per input dimension.</summary>
    public double[][] Domain { get; }

    /// <summary>Output range — one [min, max] pair per output. Null when /Range is absent.</summary>
    public double[][]? Range { get; }

    protected PdfFunction(double[][] domain, double[][]? range)
    {
        Domain = domain;
        Range = range;
    }

    /// <summary>Evaluate the function. Inputs are clamped to domain; outputs to range.</summary>
    public double[] Evaluate(double[] inputs)
    {
        var clamped = new double[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            var (lo, hi) = i < Domain.Length ? (Domain[i][0], Domain[i][1]) : (double.NegativeInfinity, double.PositiveInfinity);
            clamped[i] = Math.Max(lo, Math.Min(hi, inputs[i]));
        }
        var raw = EvaluateCore(clamped);
        if (Range is null) return raw;
        for (int i = 0; i < raw.Length; i++)
        {
            if (i >= Range.Length) break;
            raw[i] = Math.Max(Range[i][0], Math.Min(Range[i][1], raw[i]));
        }
        return raw;
    }

    protected abstract double[] EvaluateCore(double[] inputs);

    // ── Factory ──────────────────────────────────────────────────────────────

    /// <summary>Parse a PDF function from a dictionary/stream reference.</summary>
    internal static PdfFunction? Parse(PdfObject? obj, PdfReader reader)
    {
        if (obj is null) return null;
        try
        {
            PdfDictionary dict;
            byte[]? streamData = null;
            var resolved = reader.Resolve(obj);
            if (resolved is PdfStream stream)
            {
                dict = stream.Dict;
                streamData = reader.DecodeStream(stream);
            }
            else if (resolved is PdfDictionary d)
                dict = d;
            else
                return null;

            var ft = (int)dict.GetInt("FunctionType");
            return ft switch
            {
                0 => SampledFunction.Create(dict, reader, streamData),
                2 => ExponentialFunction.Create(dict),
                3 => StitchingFunction.Create(dict, reader),
                4 => PostScriptFunction.Create(dict, streamData),
                _ => null,
            };
        }
        catch { return null; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        var pow = Math.Pow(x, N);
        var result = new double[C0.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = C0[i] + pow * (C1[i] - C0[i]);
        return result;
    }

    internal static ExponentialFunction? Create(PdfDictionary dict)
    {
        var domain = ParsePairs(dict.Get("Domain") as PdfArray);
        var range = dict.Get("Range") is PdfArray r ? ParsePairs(r) : null;
        var n = PdfArrayHelper.GetDoubleFromDict(dict, "N", 1);
        var c0Arr = dict.Get("C0") as PdfArray;
        var c1Arr = dict.Get("C1") as PdfArray;
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
        // Find which sub-function to use
        int k = 0;
        for (int i = 0; i < Bounds.Length; i++)
        {
            if (x < Bounds[i]) break;
            k = i + 1;
        }
        if (k >= Functions.Length) k = Functions.Length - 1;

        // Map x into the sub-function's encode range
        var dLo = k == 0 ? Domain[0][0] : Bounds[k - 1];
        var dHi = k < Bounds.Length ? Bounds[k] : Domain[0][1];
        var eLo = k < Encode.Length ? Encode[k][0] : 0;
        var eHi = k < Encode.Length ? Encode[k][1] : 1;
        var t = dHi - dLo != 0 ? eLo + (x - dLo) * (eHi - eLo) / (dHi - dLo) : eLo;
        return Functions[k].Evaluate([t]);
    }

    internal static StitchingFunction? Create(PdfDictionary dict, PdfReader reader)
    {
        var domain = ParsePairs(dict.Get("Domain") as PdfArray);
        var range = dict.Get("Range") is PdfArray r ? ParsePairs(r) : null;
        var fnArr = reader.Resolve(dict.Get("Functions")) as PdfArray;
        if (fnArr is null) return null;
        var fns = new List<PdfFunction>();
        foreach (var item in fnArr)
        {
            var f = Parse(item, reader);
            if (f is not null) fns.Add(f);
        }
        var boundsArr = dict.Get("Bounds") as PdfArray;
        var bounds = boundsArr is not null ? PdfArrayHelper.ToDoubleArray(boundsArr) : [];
        var encodeArr = dict.Get("Encode") as PdfArray;
        var encode = encodeArr is not null ? ParsePairs(encodeArr) : [];
        return new StitchingFunction(domain, range, fns.ToArray(), bounds, encode);
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
    private readonly double[] _samples;
    private readonly int _nOutputs;
    private readonly double[][]? _encode; // per input: sample-grid range (default [0, Size_i-1])
    private readonly double[][]? _decode; // per output: value range (default = Range)

    private SampledFunction(double[][] domain, double[][]? range,
        int[] size, int bitsPerSample, double[] samples, int nOutputs,
        double[][]? encode, double[][]? decode)
        : base(domain, range)
    {
        Size = size; BitsPerSample = bitsPerSample; _samples = samples; _nOutputs = nOutputs;
        _encode = encode; _decode = decode;
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

        // Decode: map raw samples [0, 2^bps-1] onto /Decode (default /Range,
        // default [0,1] when neither is present).
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

    internal static SampledFunction? Create(PdfDictionary dict, PdfReader reader, byte[]? streamData)
    {
        if (streamData is null) return null;
        var domain = ParsePairs(dict.Get("Domain") as PdfArray);
        var range = dict.Get("Range") is PdfArray r ? ParsePairs(r) : null;
        var sizeArr = dict.Get("Size") as PdfArray;
        if (sizeArr is null) return null;
        var size = new int[sizeArr.Count];
        for (int i = 0; i < sizeArr.Count; i++)
            size[i] = PdfArrayHelper.GetInt(sizeArr, i);
        var bps = (int)dict.GetInt("BitsPerSample");
        if (bps <= 0) bps = 8;
        var encode = dict.Get("Encode") is PdfArray enc ? ParsePairs(enc) : null;
        var decode = dict.Get("Decode") is PdfArray dec ? ParsePairs(dec) : null;
        var nOutputs = range?.Length ?? 1;
        var totalSamples = 1;
        foreach (var s in size) totalSamples *= s;
        totalSamples *= nOutputs;
        var samples = DecodeSamples(streamData, bps, totalSamples);
        return new SampledFunction(domain, range, size, bps, samples, nOutputs, encode, decode);
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
            for (int i = 0; i < count && i * 4 + 3 < data.Length; i++)
                result[i] = (data[i * 4] << 24) | (data[i * 4 + 1] << 16) | (data[i * 4 + 2] << 8) | data[i * 4 + 3];
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
