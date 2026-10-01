using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Find the constant RGB fill colour a highlight appearance paints
    /// (the last <c>rg</c>/<c>g</c> before a fill), walking nested Form
    /// XObjects. Null when no fill colour is found.</summary>
    private double[]? FindAppearanceFillColor(PdfStream form, int depth = 0)
    {
        if (depth > 4) return null;
        byte[] data;
        try { data = _reader.DecodeStream(form); }
        catch { return null; }
        var text = Compat.Latin1.GetString(data);

        // Last non-stroking colour before a fill in this stream.
        double[]? color = null;
        var m = System.Text.RegularExpressions.Regex.Match(text,
            @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+rg[\s\S]*?\bf\*?\b");
        if (m.Success)
        {
            color = new[]
            {
                double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        if (color is not null) return color;

        // Recurse into nested form XObjects.
        var res = _reader.ResolveDict(form.Dict.Get("Resources"));
        var xobjects = res is not null ? _reader.ResolveDict(res.Get("XObject")) : null;
        if (xobjects is not null)
        {
            foreach (var key in xobjects.Keys)
            {
                if (_reader.Resolve(xobjects.Get(key)) is PdfStream nested
                    && nested.Dict.GetName("Subtype") == "Form"
                    && FindAppearanceFillColor(nested, depth + 1) is { } found)
                    return found;
            }
        }
        return null;
    }

    private static void RemoveAnnotations(Page page, PdfArray annotsArr, List<int> indices)
    {
        for (var i = indices.Count - 1; i >= 0; i--)
            annotsArr.RemoveAt(indices[i]);
        if (annotsArr.Count == 0)
            page.Dict.Remove("Annots");
    }

    /// <summary>Register a shared XObject stream under a fresh name in the
    /// page's /Resources /XObject dictionary and return the name.</summary>
    private string RegisterPageXObject(Page page, PdfStream stream)
    {
        // Bind the resolved dictionaries into the live page dict so the
        // registration survives a reader cache clear (the save path prefers the
        // live Page.Dict; a resolved-but-indirect Resources would be re-parsed).
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) resources = new PdfDictionary();
        page.Dict.Set("Resources", resources);
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) xobjects = new PdfDictionary();
        resources.Set("XObject", xobjects);
        var name = "FRM0";
        var counter = 0;
        while (xobjects.ContainsKey(name)) name = $"FRM{++counter}";
        xobjects.Set(name, stream);
        return name;
    }

    private static byte[] Combine(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    private static string Fmt(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static double Num(object? obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private readonly record struct SimMatrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly SimMatrix Identity = new(1, 0, 0, 1, 0, 0);

        public SimMatrix Concat(SimMatrix m) => new(
            m.A * A + m.B * C, m.A * B + m.B * D,
            m.C * A + m.D * C, m.C * B + m.D * D,
            m.E * A + m.F * C + E, m.E * B + m.F * D + F);

        public (double X, double Y) Apply(double x, double y)
            => (A * x + C * y + E, B * x + D * y + F);
    }

    /// <summary>Token scan of the page content: collect the device-space
    /// regions of paint executed under partial alpha (0 &lt; a &lt; 1) or a
    /// non-Normal blend mode, and produce a rewrite where those paints become
    /// <c>n</c>. Text shown under transparency contributes an approximate box
    /// to the region but keeps painting (the composite raster covers it).
    /// Returns null when nothing was suppressed (regions may still be added
    /// for text-only transparency).</summary>
    private byte[]? ScanTransparentPaints(byte[] contentBytes, PdfDictionary resources,
        List<double[]> regions, List<(PdfStream form, byte[] bytes)> formRewrites,
        bool recolor = false)
        => ScanTransparentPaintsCore(contentBytes, resources, regions, formRewrites,
            SimMatrix.Identity, 1, 1, "Normal", new HashSet<PdfStream>(), 1, 1, recolor);

    /// <summary>True when a Form XObject reachable from <paramref name="resources"/>
    /// carries a transparent ExtGState of its own — the signal to scan a page whose
    /// top-level graphics states are all opaque.</summary>
    private bool HasTransparentFormGs(PdfDictionary resources, HashSet<PdfDictionary>? seen = null, int depth = 0)
    {
        if (depth > 4) return false;
        seen ??= new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (!seen.Add(resources)) return false;
        var xo = _reader.ResolveDict(resources.Get("XObject"));
        if (xo is null) return false;
        foreach (var k in xo.Keys)
        {
            if (_reader.ResolveStream(xo.Get(k)) is not { } xs) continue;
            if (xs.Dict.GetName("Subtype") != "Form") continue;
            var fres = _reader.ResolveDict(xs.Dict.Get("Resources"));
            if (fres is null) continue;
            var eg = _reader.ResolveDict(fres.Get("ExtGState"));
            if (eg is not null)
                foreach (var gk in eg.Keys)
                {
                    var gs = _reader.ResolveDict(eg.Get(gk));
                    if (gs is null) continue;
                    double? ca = gs.Get("ca") is { } cav ? Num(cav) : null;
                    double? cA = gs.Get("CA") is { } cAv ? Num(cAv) : null;
                    var bm = gs.GetName("BM");
                    if ((ca is > 0 and < 1) || (cA is > 0 and < 1)
                        || (bm is not null && bm != "Normal" && bm != "Compatible"))
                        return true;
                }
            if (HasTransparentFormGs(fres, seen, depth + 1)) return true;
        }
        return false;
    }

    // fillScale/strokeScale: the constant alpha a TRANSPARENCY GROUP was invoked
    // under. Inside such a group a `gs` sets alpha relative to the group's own
    // backdrop — the group's RESULT still composites at the outer alpha — so an
    // inner `/GS0 gs` with ca 1 must not cancel the outer 0.3 (Illustrator maps
    // wrap thousands of 30%-alpha strokes exactly this way).
    private byte[]? ScanTransparentPaintsCore(byte[] contentBytes, PdfDictionary resources,
        List<double[]> regions, List<(PdfStream form, byte[] bytes)> formRewrites,
        SimMatrix ctm0, double fillA0, double strokeA0, string blend0, HashSet<PdfStream> visited,
        double fillScale, double strokeScale, bool recolor = false)
    {
        var tp = new TransparentScanState();
        tp.extGStates = _reader.ResolveDict(resources.Get("ExtGState"));

        tp.gsInfo = new Dictionary<string, (double? Ca, double? CA, string? Bm, bool? Sm)>(StringComparer.Ordinal);
        tp.anyTransparent = false;
        if (tp.extGStates is not null)
            foreach (var key in tp.extGStates.Keys)
            {
                var gs = _reader.ResolveDict(tp.extGStates.Get(key));
                if (gs is null) continue;
                double? ca = gs.Get("ca") is { } cav ? Num(cav) : null;
                double? cA = gs.Get("CA") is { } cAv ? Num(cAv) : null;
                var bm = gs.GetName("BM");
                bool? sm = gs.Get("SMask") switch
                {
                    null => null,
                    PdfName n2 => n2.Value == "None" ? false : null,
                    _ => _reader.Resolve(gs.Get("SMask")) is PdfDictionary or PdfStream ? true : null,
                };
                tp.gsInfo[key] = (ca, cA, bm, sm);
                if ((ca is > 0 and < 1) || (cA is > 0 and < 1) || (bm is not null && bm != "Normal" && bm != "Compatible")
                    || sm == true)
                    tp.anyTransparent = true;
            }
        tp.initialTransparent = (fillA0 > 0 && fillA0 < 1) || (strokeA0 > 0 && strokeA0 < 1)
            || (blend0 != "Normal" && blend0 != "Compatible");
        // Form-held transparency only matters to the RECOLOUR (Mask) mode — the
        // Default action keeps its historical page-level-only scan, so pages whose
        // alpha lives inside forms neutralise exactly as they always did.
        if (!tp.anyTransparent && !tp.initialTransparent
            && !(recolor && HasTransparentFormGs(resources))) return null;

        OpenTransparentScan(tp, contentBytes, ctm0, fillA0, strokeA0, blend0);

        while (tp.pos < tp.text.Length)
        {
            if (!ScanTransparentToken(tp, resources, regions, formRewrites, visited, fillScale, strokeScale, recolor)) break;
        }

        if (tp.bailOut) return null;
        return tp.changed ? Compat.Latin1.GetBytes(tp.output.ToString()) : null;
    }

    /// <summary>Merge intersecting / near-touching (≤ 2 pt) region boxes until
    /// stable, so one transparency scope yields one composite image.</summary>
    private static void MergeRegions(List<double[]> regions)
    {
        const double slack = 2.0;
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < regions.Count && !merged; i++)
            {
                for (var j = i + 1; j < regions.Count; j++)
                {
                    var a = regions[i];
                    var b = regions[j];
                    if (a[0] - slack <= b[2] && b[0] - slack <= a[2]
                        && a[1] - slack <= b[3] && b[1] - slack <= a[3])
                    {
                        regions[i] = new[]
                        {
                            Math.Min(a[0], b[0]), Math.Min(a[1], b[1]),
                            Math.Max(a[2], b[2]), Math.Max(a[3], b[3]),
                        };
                        regions.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }
        }
    }
}
