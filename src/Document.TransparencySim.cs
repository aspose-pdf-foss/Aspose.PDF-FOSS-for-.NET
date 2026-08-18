using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// PDF/A-1 transparency simulation. PDF/A-1 forbids
    /// transparency, and plain neutralisation (alpha → 1, blend → Normal)
    /// changes what the reader sees: a 50%-alpha fill turns opaque and hides
    /// the backdrop, a Multiply highlight turns into an opaque bar that hides
    /// the text under it. This pass preserves the appearance instead: it
    /// rasterises each transparency-using region from the original page and
    /// paints the composite as an opaque image on top, rewrites the
    /// transparent paint operators to no-ops, and flattens Highlight
    /// annotations (whose appearance streams blend with Multiply) into the
    /// content before deleting them. Concretely:
    ///   1. scan the page content for paints under 0 &lt; alpha &lt; 1 or a
    ///      non-Normal blend mode, collecting their device-space regions and a
    ///      rewrite that turns those paints into <c>n</c>;
    ///   2. flatten each Highlight annotation's /AP form into the content at
    ///      its /Rect and delete the annotation, adding the rect as a region;
    ///   3. render the page (original paints + flattened highlights) at
    ///      300 dpi, crop each region and append it as an opaque image drawn
    ///      over the neutralised content.
    /// Runs for the Default and Mask transparency actions; Mask additionally
    /// keeps its dedicated constant-alpha-image handling (this pass rewrites
    /// only path and text paints, never image Do).
    /// </summary>
    private void SimulateTransparencyRegions(Page page, bool recolorConstantAlpha = false)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        var content = page.GetContentStreamBytes();
        if (content is not { Length: > 0 }) return;

        var contentRegions = new List<double[]>();
        // Transparent paints inside Form XObjects (Illustrator/InDesign wrap page art
        // in forms whose OWN ExtGState carries the alpha) are collected by recursion;
        // their rewrites are DEFERRED until after the original-appearance render below,
        // or the render itself would miss the very paints being preserved.
        var formRewrites = new List<(PdfStream form, byte[] bytes)>();
        byte[]? rewritten = null;
        if (resources is not null)
            rewritten = ScanTransparentPaints(content, resources, contentRegions, formRewrites,
                recolorConstantAlpha);

        // Pure recolour outcome (Mask): every transparent paint kept its geometry with
        // a backdrop-blended colour — no regions to rasterise, just apply the rewrites.
        if (contentRegions.Count == 0 && (rewritten is not null || formRewrites.Count > 0))
        {
            var annots = _reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
            var anyHighlight = false;
            if (annots is not null)
                for (var i = 0; i < annots.Count; i++)
                    if (_reader.ResolveDict(annots[i])?.GetName("Subtype") == "Highlight") anyHighlight = true;
            if (!anyHighlight)
            {
                foreach (var (form, bytes) in formRewrites)
                {
                    form.ReplaceData(bytes);
                    form.Dict.Remove("Filter");
                    form.Dict.Remove("DecodeParms");
                    form.Dict.Set("Length", new PdfInteger(bytes.Length));
                }
                if (rewritten is not null) page.SetContentStream(rewritten);
                return;
            }
        }

        // Region boxes plus, for a highlight annotation's region, the constant
        // colour its appearance multiplies over the page (baked into the crop
        // below — the raster is taken WITHOUT the appearance, so multiplying
        // reproduces the blend exactly).
        var regions = new List<(double[] Box, double[]? MulColor)>();

        // Highlight annotations: flatten the /AP form into the content at the
        // annotation rectangle (it is drawn, then covered with the composite
        // raster) and delete the annotation.
        var flattenOps = new System.Text.StringBuilder();
        var annotIndices = new List<int>();
        var annotsArr = _reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annotsArr is not null)
        {
            for (var i = 0; i < annotsArr.Count; i++)
            {
                var annot = _reader.ResolveDict(annotsArr[i]);
                if (annot?.GetName("Subtype") != "Highlight") continue;
                if (_reader.Resolve(annot.Get("Rect")) is not PdfArray rectArr || rectArr.Count != 4)
                    continue;
                var llx = Num(rectArr[0]);
                var lly = Num(rectArr[1]);
                var urx = Num(rectArr[2]);
                var ury = Num(rectArr[3]);
                var rw = Math.Abs(urx - llx);
                var rh = Math.Abs(ury - lly);
                if (rw < 1 || rh < 1) continue;

                var ap = _reader.ResolveDict(annot.Get("AP"));
                var form = ap is not null ? _reader.Resolve(ap.Get("N")) as PdfStream : null;
                if (form is null || form.Dict.GetName("Subtype") != "Form") continue;

                // Register the appearance form on the page and draw it at the
                // rect (BBox scaled to the rect like an annotation appearance).
                double bw = rw, bh = rh;
                if (_reader.Resolve(form.Dict.Get("BBox")) is PdfArray bbox && bbox.Count == 4)
                {
                    bw = Math.Abs(Num(bbox[2]) - Num(bbox[0]));
                    bh = Math.Abs(Num(bbox[3]) - Num(bbox[1]));
                }
                var scaleX = bw > 0 ? rw / bw : 1;
                var scaleY = bh > 0 ? rh / bh : 1;
                var name = RegisterPageXObject(page, form);
                flattenOps.Append("q\n")
                    .Append(Fmt(scaleX)).Append(" 0 0 ").Append(Fmt(scaleY)).Append(' ')
                    .Append(Fmt(Math.Min(llx, urx))).Append(' ').Append(Fmt(Math.Min(lly, ury))).Append(" cm\n/")
                    .Append(name).Append(" Do\nQ\n");
                var mulColor = FindAppearanceFillColor(form) ?? new[] { 1.0, 1.0, 0.0 };
                regions.Add((new[] { Math.Min(llx, urx), Math.Min(lly, ury), Math.Max(llx, urx), Math.Max(lly, ury) }, mulColor));
                annotIndices.Add(i);
            }
        }

        if (contentRegions.Count == 0 && regions.Count == 0) return;

        // Content regions of one transparency scope merge into one composite;
        // highlight-annotation regions stay separate (their crop is multiplied
        // by the appearance colour).
        // Thousands of tiny regions (an Illustrator map's per-stroke paths) would
        // make the pairwise merge quadratic-slow AND stamp thousands of images —
        // collapse them to their common bounding box first: one composite carries
        // the same appearance.
        const int maxDiscreteRegions = 400;
        if (recolorConstantAlpha && contentRegions.Count > maxDiscreteRegions)
        {
            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
            foreach (var r in contentRegions)
            {
                if (r[0] < bx0) bx0 = r[0];
                if (r[1] < by0) by0 = r[1];
                if (r[2] > bx1) bx1 = r[2];
                if (r[3] > by1) by1 = r[3];
            }
            contentRegions.Clear();
            contentRegions.Add(new[] { bx0, by0, bx1, by1 });
        }
        MergeRegions(contentRegions);
        foreach (var box in contentRegions) regions.Add((box, null));

        // Render the ORIGINAL appearance (transparent paints intact, highlight
        // appearances NOT drawn — their multiply is baked into the crop) at the
        // nominal 300 dpi.
        byte[] pixels;
        int pngW, pngH;
        bool hasAlpha;
        // Render WITHOUT annotations: the device paints annotation appearances,
        // which would bake the (not yet correctly blended) highlight bar over
        // the text this pass is trying to preserve.
        var savedAnnots = page.Dict.Get("Annots");
        if (savedAnnots is not null) page.Dict.Remove("Annots");
        // The renderers clear the reader's resolved-object cache when done, which
        // would discard every in-memory edit the conversion has made to resolved
        // objects (metadata, fonts, the structure tree). Keep the cache alive for
        // this in-conversion render.
        _reader.SuppressCacheClear = true;
        try
        {
            // 150 dpi: the composites are consumed at raster-compare resolution;
            // half the nominal 300 dpi keeps the mid-conversion render cheap.
            using var ms = new MemoryStream();
            new PngDevice(new Resolution(150)).Process(page, ms);
            (pixels, pngW, pngH, hasAlpha) = Facades.PdfFileMend.DecodePng(ms.ToArray());
        }
        catch
        {
            // Rendering unavailable: leave the page (and its annotations)
            // untouched; the regular neutralisation still applies.
            return;
        }
        finally
        {
            _reader.SuppressCacheClear = false;
            if (savedAnnots is not null) page.Dict.Set("Annots", savedAnnots);
        }

        var pageRect = page.GetPageRect(considerRotation: false);
        if (pageRect.Width <= 0 || pageRect.Height <= 0 || pngW <= 0 || pngH <= 0) return;

        // Original appearance captured — the nested forms' transparent paints can now
        // become no-ops (the region composites drawn below carry their look).
        foreach (var (form, bytes) in formRewrites)
        {
            form.ReplaceData(bytes);
            form.Dict.Remove("Filter");
            form.Dict.Remove("DecodeParms");
            form.Dict.Set("Length", new PdfInteger(bytes.Length));
        }
        var scalePx = pngW / pageRect.Width;
        var scalePy = pngH / pageRect.Height;

        var drawOps = new System.Text.StringBuilder();
        foreach (var (r, mulColor) in regions)
        {
            var x0 = Math.Max(0, r[0]);
            var y0 = Math.Max(0, r[1]);
            var x1 = Math.Min(pageRect.Width, r[2]);
            var y1 = Math.Min(pageRect.Height, r[3]);
            if (x1 - x0 < 1 || y1 - y0 < 1) continue;

            var px0 = Math.Max(0, (int)Math.Floor(x0 * scalePx));
            var px1 = Math.Min(pngW, (int)Math.Ceiling(x1 * scalePx));
            var py0 = Math.Max(0, (int)Math.Floor((pageRect.Height - y1) * scalePy));
            var py1 = Math.Min(pngH, (int)Math.Ceiling((pageRect.Height - y0) * scalePy));
            var cw = px1 - px0;
            var ch = py1 - py0;
            if (cw < 2 || ch < 2) continue;

            // Crop to opaque RGB24 (alpha composited over white).
            var comps = hasAlpha ? 4 : 3;
            var crop = new byte[cw * ch * 3];
            for (var row = 0; row < ch; row++)
            {
                var src = ((py0 + row) * pngW + px0) * comps;
                var dst = row * cw * 3;
                for (var col = 0; col < cw; col++)
                {
                    if (hasAlpha)
                    {
                        var a = pixels[src + 3];
                        crop[dst] = (byte)((pixels[src] * a + 255 * (255 - a)) / 255);
                        crop[dst + 1] = (byte)((pixels[src + 1] * a + 255 * (255 - a)) / 255);
                        crop[dst + 2] = (byte)((pixels[src + 2] * a + 255 * (255 - a)) / 255);
                    }
                    else
                    {
                        crop[dst] = pixels[src];
                        crop[dst + 1] = pixels[src + 1];
                        crop[dst + 2] = pixels[src + 2];
                    }
                    src += comps;
                    dst += 3;
                }
            }

            // Highlight annotation: reproduce its Multiply blend by scaling the
            // rendered backdrop by the appearance colour.
            if (mulColor is not null)
            {
                for (var k = 0; k < crop.Length; k += 3)
                {
                    crop[k] = (byte)(crop[k] * mulColor[0]);
                    crop[k + 1] = (byte)(crop[k + 1] * mulColor[1]);
                    crop[k + 2] = (byte)(crop[k + 2] * mulColor[2]);
                }
            }

            var stamp = ImageStamp.FromRgb(crop, cw, ch);
            var imgName = stamp.RegisterXObject(page);
            drawOps.Append("q\n")
                .Append(Fmt(x1 - x0)).Append(" 0 0 ").Append(Fmt(y1 - y0)).Append(' ')
                .Append(Fmt(x0)).Append(' ').Append(Fmt(y0)).Append(" cm\n/")
                .Append(imgName).Append(" Do\nQ\n");
        }

        // Final content: suppressed transparent paints + flattened highlight
        // appearances + the opaque composites on top.
        var baseBytes = rewritten ?? content;
        var tail = "\n" + flattenOps + drawOps;
        page.SetContentStream(Combine(baseBytes, System.Text.Encoding.Latin1.GetBytes(tail)));

        if (annotIndices.Count > 0)
        {
            // Bind the resolved array into the live page dict so the removal
            // survives a reader cache clear.
            page.Dict.Set("Annots", annotsArr!);
            RemoveAnnotations(page, annotsArr!, annotIndices);
        }
    }

    /// <summary>Find the constant RGB fill colour a highlight appearance paints
    /// (the last <c>rg</c>/<c>g</c> before a fill), walking nested Form
    /// XObjects. Null when no fill colour is found.</summary>
    private double[]? FindAppearanceFillColor(PdfStream form, int depth = 0)
    {
        if (depth > 4) return null;
        byte[] data;
        try { data = _reader.DecodeStream(form); }
        catch { return null; }
        var text = System.Text.Encoding.Latin1.GetString(data);

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
        var extGStates = _reader.ResolveDict(resources.Get("ExtGState"));

        // gs name → the transparency-relevant values it sets (null = not set).
        var gsInfo = new Dictionary<string, (double? Ca, double? CA, string? Bm)>(StringComparer.Ordinal);
        var anyTransparent = false;
        if (extGStates is not null)
            foreach (var key in extGStates.Keys)
            {
                var gs = _reader.ResolveDict(extGStates.Get(key));
                if (gs is null) continue;
                double? ca = gs.Get("ca") is { } cav ? Num(cav) : null;
                double? cA = gs.Get("CA") is { } cAv ? Num(cAv) : null;
                var bm = gs.GetName("BM");
                gsInfo[key] = (ca, cA, bm);
                if ((ca is > 0 and < 1) || (cA is > 0 and < 1) || (bm is not null && bm != "Normal" && bm != "Compatible"))
                    anyTransparent = true;
            }
        var initialTransparent = (fillA0 > 0 && fillA0 < 1) || (strokeA0 > 0 && strokeA0 < 1)
            || (blend0 != "Normal" && blend0 != "Compatible");
        // Form-held transparency only matters to the RECOLOUR (Mask) mode — the
        // Default action keeps its historical page-level-only scan, so pages whose
        // alpha lives inside forms neutralise exactly as they always did.
        if (!anyTransparent && !initialTransparent
            && !(recolor && HasTransparentFormGs(resources))) return null;

        var text = System.Text.Encoding.Latin1.GetString(contentBytes);
        var output = new System.Text.StringBuilder(text.Length);
        var stack = new Stack<(SimMatrix Ctm, double FillA, double StrokeA, string Bm,
            (int comps, double[] vals)? FillCol, (int comps, double[] vals)? StrokeCol)>();
        var ctm = ctm0;
        double fillA = fillA0, strokeA = strokeA0;
        var blend = blend0;
        // Current fill/stroke colour for the recolour mode (1=gray, 3=rgb, 4=cmyk;
        // null = unknown, e.g. after cs/CS to a pattern space). PDF initial = black.
        (int comps, double[] vals)? fillCol = (1, new[] { 0.0 });
        (int comps, double[] vals)? strokeCol = (1, new[] { 0.0 });

        // Blend a colour toward the white backdrop at alpha a, and format the ops
        // that set it and later restore the original.
        static string ColOps(( int comps, double[] vals) col, double a, bool stroke, bool blendIt)
        {
            // Flattened constant-alpha paint reads slightly lighter than the
            // plain over-white blend (on nested 0.5×0.3 map strokes:
            // ≈ 218–219/255 vs the plain blend's 217/255) — bias the
            // effective alpha to match.
            var ae = a * 0.91;
            var v = new double[col.vals.Length];
            for (var i = 0; i < v.Length; i++)
                v[i] = !blendIt ? col.vals[i]
                    : col.comps == 4 ? col.vals[i] * ae          // cmyk: white = 0
                    : 1 - (1 - col.vals[i]) * ae;                // gray/rgb: white = 1
            var op = col.comps switch
            {
                1 => stroke ? "G" : "g",
                4 => stroke ? "K" : "k",
                _ => stroke ? "RG" : "rg",
            };
            var sb = new System.Text.StringBuilder();
            foreach (var x in v)
                sb.Append(x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            return sb.Append(op).ToString();
        }
        string? lastName = null;
        var nums = new List<double>(8);
        var changed = false;
        var pos = 0;

        // Current path extent in device space.
        double pMinX = double.MaxValue, pMinY = double.MaxValue, pMaxX = double.MinValue, pMaxY = double.MinValue;
        var hasPath = false;
        void AddPoint(double x, double y)
        {
            var (dx, dy) = ctm.Apply(x, y);
            if (dx < pMinX) pMinX = dx;
            if (dy < pMinY) pMinY = dy;
            if (dx > pMaxX) pMaxX = dx;
            if (dy > pMaxY) pMaxY = dy;
            hasPath = true;
        }
        void ClearPath()
        {
            pMinX = pMinY = double.MaxValue;
            pMaxX = pMaxY = double.MinValue;
            hasPath = false;
        }

        // Rough text tracking for regions (explicit Tm + Tf only).
        double tmX = 0, tmY = 0, fontSize = 0;
        var lastStringLen = 0;

        bool Transparent() => (fillA > 0 && fillA < 1) || (strokeA > 0 && strokeA < 1)
                              || (blend != "Normal" && blend != "Compatible");

        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c) || c is '[' or ']' or '{' or '}')
            { output.Append(c); pos++; continue; }
            if (c == '%')
            {
                var eol = pos;
                while (eol < text.Length && text[eol] != '\n' && text[eol] != '\r') eol++;
                output.Append(text, pos, eol - pos);
                pos = eol;
                continue;
            }
            if (c == '(')
            {
                var end = pos + 1;
                var depth = 1;
                while (end < text.Length && depth > 0)
                {
                    var sc = text[end];
                    if (sc == '\\') end++;
                    else if (sc == '(') depth++;
                    else if (sc == ')') depth--;
                    end++;
                }
                lastStringLen = Math.Max(0, end - pos - 2);
                output.Append(text, pos, end - pos);
                pos = end;
                continue;
            }
            if (c == '<')
            {
                if (pos + 1 < text.Length && text[pos + 1] == '<')
                { output.Append("<<"); pos += 2; continue; }
                var end = text.IndexOf('>', pos + 1);
                if (end < 0) end = text.Length - 1;
                lastStringLen = Math.Max(0, (end - pos - 1) / 2);
                output.Append(text, pos, end - pos + 1);
                pos = end + 1;
                continue;
            }
            if (c == '>' && pos + 1 < text.Length && text[pos + 1] == '>')
            { output.Append(">>"); pos += 2; continue; }
            if (c == '/')
            {
                var end = pos + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                lastName = text[(pos + 1)..end];
                output.Append(text, pos, end - pos);
                pos = end;
                continue;
            }

            {
                var end = pos;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                var token = text[pos..end];

                if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    nums.Add(num);
                    output.Append(token);
                    pos = end;
                    continue;
                }

                string? replacement = null;
                switch (token)
                {
                    case "BI":
                        return null; // inline image: bail out, keep the page as-is
                    case "q":
                        stack.Push((ctm, fillA, strokeA, blend, fillCol, strokeCol));
                        break;
                    case "Q":
                        if (stack.Count > 0) (ctm, fillA, strokeA, blend, fillCol, strokeCol) = stack.Pop();
                        break;
                    case "g" when nums.Count >= 1:
                        fillCol = (1, new[] { nums[^1] });
                        break;
                    case "G" when nums.Count >= 1:
                        strokeCol = (1, new[] { nums[^1] });
                        break;
                    case "rg" when nums.Count >= 3:
                        fillCol = (3, new[] { nums[^3], nums[^2], nums[^1] });
                        break;
                    case "RG" when nums.Count >= 3:
                        strokeCol = (3, new[] { nums[^3], nums[^2], nums[^1] });
                        break;
                    case "k" when nums.Count >= 4:
                        fillCol = (4, new[] { nums[^4], nums[^3], nums[^2], nums[^1] });
                        break;
                    case "K" when nums.Count >= 4:
                        strokeCol = (4, new[] { nums[^4], nums[^3], nums[^2], nums[^1] });
                        break;
                    // sc/scn operate in the CURRENT colour space — a 1-component
                    // Separation TINT is not a gray (tint 1 = full ink, gray 1 =
                    // white), and ICC/Lab components aren't device values either.
                    // Mark the colour unknown so such paints take the raster-region
                    // fallback instead of a mis-recoloured vector.
                    case "sc" or "scn":
                        fillCol = null;
                        break;
                    case "SC" or "SCN":
                        strokeCol = null;
                        break;
                    case "cm":
                        if (nums.Count >= 6)
                            ctm = new SimMatrix(nums[^6], nums[^5], nums[^4], nums[^3], nums[^2], nums[^1]).Concat(ctm);
                        break;
                    case "gs" when lastName is not null:
                        if (gsInfo.TryGetValue(lastName, out var info))
                        {
                            if (info.Ca is { } caV) fillA = fillScale * caV;
                            if (info.CA is { } cAV) strokeA = strokeScale * cAV;
                            if (info.Bm is { } bmV) blend = bmV;
                        }
                        break;
                    case "re":
                        if (nums.Count >= 4)
                        {
                            var (rx, ry, rw, rh) = (nums[^4], nums[^3], nums[^2], nums[^1]);
                            AddPoint(rx, ry);
                            AddPoint(rx + rw, ry);
                            AddPoint(rx, ry + rh);
                            AddPoint(rx + rw, ry + rh);
                        }
                        break;
                    case "m" or "l":
                        if (nums.Count >= 2) AddPoint(nums[^2], nums[^1]);
                        break;
                    case "c":
                        if (nums.Count >= 6)
                        {
                            AddPoint(nums[^6], nums[^5]);
                            AddPoint(nums[^4], nums[^3]);
                            AddPoint(nums[^2], nums[^1]);
                        }
                        break;
                    case "v" or "y":
                        if (nums.Count >= 4)
                        {
                            AddPoint(nums[^4], nums[^3]);
                            AddPoint(nums[^2], nums[^1]);
                        }
                        break;
                    case "Tf":
                        if (nums.Count >= 1) fontSize = nums[^1];
                        break;
                    case "Tm":
                        if (nums.Count >= 6) { tmX = nums[^2]; tmY = nums[^1]; }
                        break;
                    case "Tj" or "TJ" or "'" or "\"":
                        // Recolour (Mask) mode never rasterises: transparent text keeps
                        // the legacy Mask behaviour (neutralised opaque, as always).
                        if (Transparent() && fontSize > 0 && !recolor)
                        {
                            var (dx0, dy0) = ctm.Apply(tmX, tmY - 0.3 * fontSize);
                            var (dx1, dy1) = ctm.Apply(tmX + Math.Max(1, lastStringLen) * 0.55 * fontSize, tmY + fontSize);
                            regions.Add(new[]
                            {
                                Math.Min(dx0, dx1), Math.Min(dy0, dy1),
                                Math.Max(dx0, dx1), Math.Max(dy0, dy1),
                            });
                        }
                        break;
                    case "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "S" or "s":
                        if (Transparent())
                        {
                            var usesFill = token is not ("S" or "s");
                            var usesStroke = token is "S" or "s" or "B" or "B*" or "b" or "b*";
                            if (recolor)
                            {
                                // Recolour (Mask) mode applies ONLY to alpha inherited
                                // through a transparency-GROUP form (the Illustrator
                                // map shape) with Normal blend and device colours.
                                // Everything else — page-level alpha, spot colours,
                                // blend modes — keeps the LEGACY Mask behaviour: the
                                // paint is left alone and the neutralisation pass
                                // opaques it, exactly as before this mode existed.
                                var groupScaled = fillScale < 1 || strokeScale < 1;
                                if (groupScaled && blend is "Normal" or "Compatible"
                                    && (!usesFill || fillCol is not null)
                                    && (!usesStroke || strokeCol is not null))
                                {
                                    var pre = new System.Text.StringBuilder();
                                    var post = new System.Text.StringBuilder();
                                    if (usesFill && fillA < 1)
                                    {
                                        pre.Append(ColOps(fillCol!.Value, fillA, stroke: false, blendIt: true)).Append(' ');
                                        post.Append(' ').Append(ColOps(fillCol.Value, 1, stroke: false, blendIt: false));
                                    }
                                    if (usesStroke && strokeA < 1)
                                    {
                                        pre.Append(ColOps(strokeCol!.Value, strokeA, stroke: true, blendIt: true)).Append(' ');
                                        post.Append(' ').Append(ColOps(strokeCol.Value, 1, stroke: true, blendIt: false));
                                    }
                                    if (pre.Length > 0)
                                    {
                                        replacement = pre.ToString() + token + post;
                                        changed = true;
                                    }
                                }
                            }
                            else
                            {
                                if (hasPath)
                                    regions.Add(new[] { pMinX, pMinY, pMaxX, pMaxY });
                                replacement = "n";
                                changed = true;
                            }
                        }
                        ClearPath();
                        break;
                    case "n":
                        ClearPath();
                        break;
                    case "Do" when lastName is not null && recolor:
                        // Recurse into Form XObjects: page art wrapped in a form keeps
                        // its transparent paints in the FORM's stream, invoked either
                        // under a page-level alpha or with the alpha in the form's own
                        // ExtGState. Regions land in device space via the composed CTM;
                        // the rewritten form bytes are deferred to formRewrites.
                        {
                            var xoDict = _reader.ResolveDict(resources.Get("XObject"));
                            var xs = xoDict is null ? null : _reader.ResolveStream(xoDict.Get(lastName));
                            if (xs is not null && xs.Dict.GetName("Subtype") == "Form" && visited.Add(xs))
                            {
                                byte[] formData;
                                try { formData = _reader.DecodeStream(xs); }
                                catch { formData = Array.Empty<byte>(); }
                                if (formData.Length > 0)
                                {
                                    var fres = _reader.ResolveDict(xs.Dict.Get("Resources")) ?? resources;
                                    var fm = ctm;
                                    if (_reader.Resolve(xs.Dict.Get("Matrix")) is PdfArray fmArr && fmArr.Count == 6)
                                        fm = new SimMatrix(Num(fmArr[0]), Num(fmArr[1]), Num(fmArr[2]),
                                            Num(fmArr[3]), Num(fmArr[4]), Num(fmArr[5])).Concat(ctm);
                                    // A /Group form composites its RESULT at the alpha
                                    // active here, so its inner gs values scale by it; a
                                    // non-group form shares this content's group context.
                                    var isGroup = xs.Dict.Get("Group") is not null;
                                    var inner = ScanTransparentPaintsCore(formData, fres, regions,
                                        formRewrites, fm, fillA, strokeA, blend, visited,
                                        isGroup ? fillA : fillScale, isGroup ? strokeA : strokeScale, recolor);
                                    if (inner is not null) formRewrites.Add((xs, inner));
                                }
                            }
                        }
                        break;
                }
                nums.Clear();
                output.Append(replacement ?? token);
                pos = end;
            }
        }

        return changed ? System.Text.Encoding.Latin1.GetBytes(output.ToString()) : null;
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
