using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    // Colour stops sampled over a shading's domain: enough that a stitched function's steps read as
    // the ramp the page draws.
    private const int GradientStops = 101;

    /// <summary>
    /// The sh operator: an axial shading painted over the band between its axis ends, a radial one between
    /// its circles, and either over its own bounding box when it declares one (PDF 32000-1 §8.7.4.2). A
    /// shading of another kind is not drawn.
    /// </summary>
    private void RenderShadingOperator(SvgRenderState sv, StringBuilder sb, PdfDictionary? resources, PdfReader reader)
    {
        if (sv.operands.Count < 1 || sv.operands[0] is not PdfName name) return;
        var shadings = reader.ResolveDict(resources?.Get("Shading"));
        if (shadings is null) return;
        var shading = Aspose.Pdf.Shading.ShadingBase.Parse(shadings.Get(name.Value), reader);
        var gs = sv.gs;
        var paint = DefineGradient(shading, gs.Ctm);
        if (paint is null) return;
        if (shading is Aspose.Pdf.Shading.AxialShading axial && axial.BBox is not { Length: >= 4 })
        {
            EmitAxialBand(sb, gs, axial, paint);
            return;
        }
        if (shading is Aspose.Pdf.Shading.RadialShading radial && radial.BBox is not { Length: >= 4 })
        {
            EmitRadialRegion(sb, gs, radial, paint);
            return;
        }

        string d;
        if (shading!.BBox is { Length: >= 4 } box)
        {
            var (x0, y0) = Apply(gs.Ctm, box[0], box[1]);
            var (x1, y1) = Apply(gs.Ctm, box[2], box[1]);
            var (x2, y2) = Apply(gs.Ctm, box[2], box[3]);
            var (x3, y3) = Apply(gs.Ctm, box[0], box[3]);
            d = $"M{F(x0)} {F(y0)} L{F(x1)} {F(y1)} L{F(x2)} {F(y2)} L{F(x3)} {F(y3)} Z";
        }
        else
        {
            d = $"M0 0 L{F(_pageWidth)} 0 L{F(_pageWidth)} {F(_pageHeight)} L0 {F(_pageHeight)} Z";
        }
        EmitShadingPath(sb, gs, d, paint);
    }

    /// <summary>
    /// An axial shading paints the band between its two axis ends, right across the page perpendicular
    /// to the axis, under the clip in force; an end the shading extends adds that end's solid colour from
    /// the band's edge to the page's (PDF 32000-1 §8.7.4.5.3), and an end it does not extend leaves the
    /// page beyond the band unpainted - the gradient's own padding never shows.
    /// </summary>
    private void EmitAxialBand(StringBuilder sb, GState gs, Aspose.Pdf.Shading.AxialShading axial, string paint)
    {
        var (x0, y0) = Apply(gs.Ctm, axial.X0, axial.Y0);
        var (x1, y1) = Apply(gs.Ctm, axial.X1, axial.Y1);
        var length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        if (length <= 0) return;
        // Unit vector along the axis, and a reach across and beyond it that clears the page from any
        // point of the axis; the clip in force or the viewport cuts the surplus.
        var (ax, ay) = ((x1 - x0) / length, (y1 - y0) / length);
        var reach = _pageWidth + _pageHeight;
        var (nx, ny) = (-ay * reach, ax * reach);
        string Quad(double px, double py, double qx, double qy) =>
            $"M{F(px + nx)} {F(py + ny)} L{F(px - nx)} {F(py - ny)} L{F(qx - nx)} {F(qy - ny)} L{F(qx + nx)} {F(qy + ny)} Z";
        EmitShadingPath(sb, gs, Quad(x0, y0, x1, y1), paint);
        var extendStart = axial.Extend is { Length: >= 2 } && axial.Extend[0];
        var extendEnd = axial.Extend is { Length: >= 2 } && axial.Extend[1];
        if (!extendStart && !extendEnd || axial.Function is null) return;
        var lo = axial.Domain is { Length: > 0 } ? axial.Domain[0] : 0;
        var hi = axial.Domain is { Length: > 1 } ? axial.Domain[1] : 1;
        if (extendStart && StopColour(axial, axial.Function, lo) is { } start)
            EmitShadingPath(sb, gs, Quad(x0, y0, x0 - ax * reach, y0 - ay * reach), start);
        if (extendEnd && StopColour(axial, axial.Function, hi) is { } end)
            EmitShadingPath(sb, gs, Quad(x1, y1, x1 + ax * reach, y1 + ay * reach), end);
    }

    /// <summary>
    /// A radial shading paints the ring between its circles (PDF 32000-1 §8.7.4.5.4): beyond the larger
    /// circle only when that end is extended - the gradient's padding then gives it that end's colour, to
    /// the page's edges - and inside the smaller one only when that end is extended, where the padding gives
    /// the other end's colour; an end not extended leaves its side unpainted.
    /// </summary>
    private void EmitRadialRegion(StringBuilder sb, GState gs, Aspose.Pdf.Shading.RadialShading radial, string paint)
    {
        var extend = radial.Extend is { Length: >= 2 } ? radial.Extend : new[] { false, false };
        var reversed = radial.R0 > radial.R1;
        var (outerX, outerY, outerR, outerExtended) = reversed
            ? (radial.X0, radial.Y0, radial.R0, extend[0]) : (radial.X1, radial.Y1, radial.R1, extend[1]);
        var (innerX, innerY, innerR, innerExtended) = reversed
            ? (radial.X1, radial.Y1, radial.R1, extend[1]) : (radial.X0, radial.Y0, radial.R0, extend[0]);
        if (outerR <= 0) return;
        var d = outerExtended
            ? $"M0 0 L{F(_pageWidth)} 0 L{F(_pageWidth)} {F(_pageHeight)} L0 {F(_pageHeight)} Z"
            : CirclePath(gs.Ctm, outerX, outerY, outerR);
        if (!innerExtended && innerR > 0) d += " " + CirclePath(gs.Ctm, innerX, innerY, innerR);
        EmitShadingPath(sb, gs, d, paint, evenOdd: true);
    }

    /// <summary>A circle of shading space as four cubic arcs carried into page space by <paramref name="ctm"/>
    /// - exact under any affine map, which takes the circle to an ellipse.</summary>
    private static string CirclePath(double[] ctm, double cx, double cy, double r)
    {
        // The control-point distance of a quarter circle drawn as one cubic arc.
        const double Kappa = 0.5522847498;
        var k = Kappa * r;
        string P(double x, double y) { var (px, py) = Apply(ctm, x, y); return $"{F(px)} {F(py)}"; }
        return $"M{P(cx + r, cy)} C{P(cx + r, cy + k)} {P(cx + k, cy + r)} {P(cx, cy + r)} " +
            $"C{P(cx - k, cy + r)} {P(cx - r, cy + k)} {P(cx - r, cy)} " +
            $"C{P(cx - r, cy - k)} {P(cx - k, cy - r)} {P(cx, cy - r)} " +
            $"C{P(cx + k, cy - r)} {P(cx + r, cy - k)} {P(cx + r, cy)} Z";
    }

    /// <summary>A shading's region filled with <paramref name="fill"/>, under the state's clip, alpha and blend;
    /// <paramref name="evenOdd"/> leaves a region's inner subpath as a hole.</summary>
    private void EmitShadingPath(StringBuilder sb, GState gs, string d, string fill, bool evenOdd = false)
    {
        var attrs = new StringBuilder($"d=\"{d}\" fill=\"{fill}\"");
        if (evenOdd) attrs.Append(" fill-rule=\"evenodd\"");
        if (gs.FillAlpha < 1.0) attrs.Append($" fill-opacity=\"{F(gs.FillAlpha)}\"");
        attrs.Append(" stroke=\"none\"");
        if (gs.BlendMode != "Normal") attrs.Append($" style=\"mix-blend-mode:{MapBlendMode(gs.BlendMode)}\"");
        sb.AppendLine($"<path{ClipAttribute(gs)}  clip-rule=\"evenodd\" transform=\"matrix(1 0 0 1 0 0)\" {attrs} />");
    }

    /// <summary>True when a cs operand names the Pattern space, directly or through a colour-space resource.</summary>
    private static bool IsPatternSpace(string name, PdfDictionary? resources, PdfReader reader)
    {
        if (name == "Pattern") return true;
        var entry = reader.Resolve(reader.ResolveDict(resources?.Get("ColorSpace"))?.Get(name));
        return entry switch
        {
            PdfName n => n.Value == "Pattern",
            PdfArray { Count: > 0 } arr => arr[0] is PdfName family && family.Value == "Pattern",
            _ => false,
        };
    }

    /// <summary>The fill paint a shading pattern sets: its gradient, placed by the pattern matrix in the
    /// space the pattern belongs to. Null for a tiling pattern or a shading kind that is not drawn.</summary>
    private string? ShadingPatternPaint(string name, PdfDictionary? resources, PdfReader reader, GState gs)
    {
        var patterns = reader.ResolveDict(resources?.Get("Pattern"));
        if (patterns is null) return null;
        if (Aspose.Pdf.Shading.Pattern.Parse(patterns.Get(name), reader) is not Aspose.Pdf.Shading.ShadingPattern pattern) return null;
        var toPage = pattern.Matrix is { Length: >= 6 } ? MulAffine(pattern.Matrix, gs.PatternBase) : gs.PatternBase;
        return DefineGradient(pattern.Shading, toPage);
    }

    /// <summary>
    /// Define an axial or radial shading as a gradient in its own coordinates, carried into page space by
    /// <paramref name="toPage"/> as its gradientTransform - exact under any affine map, where transformed
    /// endpoints alone would skew the ramp - and return the paint referring to it. Null when the shading is
    /// of another kind or its colours cannot be evaluated.
    /// </summary>
    private string? DefineGradient(Aspose.Pdf.Shading.ShadingBase? shading, double[] toPage)
    {
        var (fn, domain) = shading switch
        {
            Aspose.Pdf.Shading.AxialShading a => (a.Function, a.Domain),
            Aspose.Pdf.Shading.RadialShading r => (r.Function, r.Domain),
            _ => (null, null),
        };
        if (fn is null) return null;
        // SVG 1.1 has one radius, so a radial ramp is defined from the larger circle; when the circles
        // shrink along the domain the stops are laid out the other way round.
        var reversed = shading is Aspose.Pdf.Shading.RadialShading { } rs && rs.R0 > rs.R1;
        // The ramp starts on the smaller circle, not at the focal point: the stops are laid out from the
        // fraction of the larger radius the smaller one reaches.
        var start = shading is Aspose.Pdf.Shading.RadialShading { } rr && Math.Max(rr.R0, rr.R1) > 0
            ? Math.Min(rr.R0, rr.R1) / Math.Max(rr.R0, rr.R1) : 0;
        var stops = GradientStopsOf(shading!, fn, domain, reversed, start);
        if (stops is null) return null;

        var id = $"sh{++_gradientCounter}";
        var transform = $"gradientTransform=\"matrix({F(toPage[0])} {F(toPage[1])} {F(toPage[2])} {F(toPage[3])} {F(toPage[4])} {F(toPage[5])})\"";
        switch (shading)
        {
            case Aspose.Pdf.Shading.AxialShading ax:
                _gradientDefs.AppendLine($"<linearGradient id=\"{id}\" gradientUnits=\"userSpaceOnUse\" " +
                    $"x1=\"{F(ax.X0)}\" y1=\"{F(ax.Y0)}\" x2=\"{F(ax.X1)}\" y2=\"{F(ax.Y1)}\" {transform}>{stops}</linearGradient>");
                break;
            case Aspose.Pdf.Shading.RadialShading ra:
                var (cx, cy, r, fx, fy) = reversed ? (ra.X0, ra.Y0, ra.R0, ra.X1, ra.Y1) : (ra.X1, ra.Y1, ra.R1, ra.X0, ra.Y0);
                _gradientDefs.AppendLine($"<radialGradient id=\"{id}\" gradientUnits=\"userSpaceOnUse\" " +
                    $"cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" fx=\"{F(fx)}\" fy=\"{F(fy)}\" {transform}>{stops}</radialGradient>");
                break;
        }
        return $"url(#{id})";
    }

    /// <summary>The colour stops of a shading's function over its domain, each through the shading's
    /// colour space; <paramref name="reversed"/> lays the domain out from its end, and <paramref name="start"/>
    /// is the offset the domain's first stop sits at.</summary>
    private static string? GradientStopsOf(Aspose.Pdf.Shading.ShadingBase shading, Functions.PdfFunction fn,
        double[]? domain, bool reversed, double start)
    {
        var lo = domain is { Length: > 0 } ? domain[0] : 0;
        var hi = domain is { Length: > 1 } ? domain[1] : 1;
        var stops = new StringBuilder();
        for (var i = 0; i < GradientStops; i++)
        {
            var t = i / (double)(GradientStops - 1);
            if (StopColour(shading, fn, lo + (hi - lo) * (reversed ? 1 - t : t)) is not { } colour) return null;
            var offset = start > 0 ? F(100 * (start + t * (1 - start))) : (i * 100 / (GradientStops - 1)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            stops.Append($"<stop offset=\"{offset}%\" stop-color=\"{colour}\" />");
        }
        return stops.ToString();
    }

    /// <summary>The shading's colour at <paramref name="t"/> of its domain as an SVG colour, through its colour
    /// space; null when the function cannot be evaluated, which skips the shading rather than guessing at it.</summary>
    private static string? StopColour(Aspose.Pdf.Shading.ShadingBase shading, Functions.PdfFunction fn, double t)
    {
        try
        {
            var comps = fn.Evaluate(new[] { t });
            if (comps is null) return null;
            var (r, g, b) = SoftwarePageRenderer.ComponentsToRgb(comps, shading.ColorSpaceName, shading.TintTransform, shading.AltSpaceName);
            return $"#{r:x2}{g:x2}{b:x2}";
        }
        catch
        {
            return null;
        }
    }
}
