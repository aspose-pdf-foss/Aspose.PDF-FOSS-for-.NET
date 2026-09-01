using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// Base class for drawable shapes.
/// </summary>
public abstract class Shape
{
    public Aspose.Pdf.GraphInfo GraphInfo { get; set; } = new();

    /// <summary>Optional text label rendered with the shape. Stored only —
    /// concrete shapes don't currently emit the label.</summary>
    public Aspose.Pdf.Text.TextFragment? Text { get; set; }

    /// <summary>Whether this shape lies within a <paramref name="containerWidth"/>×<paramref name="containerHeight"/>
    /// box anchored at the origin. Override on concrete subclasses; the base returns true.</summary>
    public virtual bool CheckBounds(double containerWidth, double containerHeight) => true;

    internal abstract void Render(ContentStreamBuilder builder, Page? page = null);

    /// <summary>Emit this shape's outline into the CURRENT path without styling or
    /// painting it, for a container that paints its children as one region.</summary>
    /// <remarks>Returns false when the shape has no separable outline; the container
    /// then renders it on its own so it is never silently dropped. Kept distinct from
    /// <see cref="Render"/> because a composite fill has to reach the paint operator
    /// once, with every child's geometry already in the path — painting each child in
    /// turn fills them individually and never fills the region they enclose together.
    /// </remarks>
    internal virtual bool TryAppendGeometry(ContentStreamBuilder builder) => false;

    /// <summary>
    /// If opacity is non-default and a page context is available, register an ExtGState
    /// and emit the gs operator.
    /// </summary>
    protected void ApplyOpacity(ContentStreamBuilder builder, Page? page)
    {
        if (page is null) return;
        var needsGs = GraphInfo.FillOpacity < 1.0 || GraphInfo.StrokeOpacity < 1.0;
        if (!needsGs) return;

        var gs = new Content.ExtGState
        {
            FillAlpha = GraphInfo.FillOpacity,
            StrokeAlpha = GraphInfo.StrokeOpacity,
        };
        var name = page.AddExtGState(gs);
        builder.SetExtGState(name);
    }

    /// <summary>
    /// Apply common style: line width, colors, dash pattern, opacity.
    /// </summary>
    protected void ApplyStyle(ContentStreamBuilder builder, Page? page)
    {
        ApplyOpacity(builder, page);
        builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.DashPattern is { Length: > 0 })
            builder.SetDashPattern(GraphInfo.DashPattern, GraphInfo.DashPhase);
        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);
    }

    /// <summary>Two-pass paint for a shape whose fill and stroke carry their own
    /// transparency (colours built with an alpha channel): the stroke alpha and the
    /// fill alpha each get a DEDICATED ExtGState — registered on the page under the
    /// sequential lowercase names <c>gs1, gs2, …</c> with disjoint /CA and /ca keys,
    /// so the pair composes — then the region is filled (<c>f</c>) and the outline
    /// stroked (<c>S</c>) as separate paints. Returns false (leaving the caller on
    /// the combined single-gs path) when the shape lacks either colour, has no
    /// transparency, or fills with a gradient pattern.</summary>
    protected bool TryPaintTransparentFillStroke(ContentStreamBuilder builder, Page? page,
        Action<ContentStreamBuilder> addPath)
    {
        if (page is null) return false;
        if (GraphInfo.FillColor is null || GraphInfo.Color is null) return false;
        if (GraphInfo.FillColor.PatternColorSpace is not null) return false;
        if (GraphInfo.FillOpacity >= 1.0 && GraphInfo.StrokeOpacity >= 1.0) return false;

        if (GraphInfo.LineWidth != 1f) builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.DashPattern is { Length: > 0 })
            builder.SetDashPattern(GraphInfo.DashPattern, GraphInfo.DashPhase);
        builder.SetExtGState(page.AddExtGStateSequential(
            new Content.ExtGState { StrokeAlpha = GraphInfo.StrokeOpacity }));
        builder.SetExtGState(page.AddExtGStateSequential(
            new Content.ExtGState { FillAlpha = GraphInfo.FillOpacity }));

        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);
        addPath(builder);
        builder.Fill();

        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        addPath(builder);
        builder.Stroke();
        return true;
    }

    /// <summary>Apply appropriate paint operator based on fill/stroke settings.</summary>
    protected void Paint(ContentStreamBuilder builder)
    {
        // A shape is always stroked (the outline falls back to the default black
        // when no explicit Color is set); a FillColor additionally fills the region.
        if (GraphInfo.FillColor is not null)
            builder.FillAndStroke();
        else
            builder.Stroke();
    }

    /// <summary>Paint the shape with an axial-gradient fill when the FillColor
    /// carries a <see cref="GradientAxialShading"/>: the path is used as a clip
    /// for an <c>sh</c> paint (registered under the page's /Resources/Shading),
    /// then re-emitted and stroked for the outline. Returns false — leaving the
    /// caller on the solid-fill path — when there is no gradient or no page to
    /// host the shading resource.</summary>
    protected bool TryPaintGradient(ContentStreamBuilder builder, Page? page, Action<ContentStreamBuilder> addPath)
    {
        if (page is null || GraphInfo.FillColor?.PatternColorSpace is not GradientAxialShading grad)
            return false;

        var name = page.AddShading(BuildAxialShadingDict(grad));
        builder.SaveState();
        addPath(builder);
        builder.Clip();
        builder.Raw($"/{name} sh\n");
        builder.RestoreState();
        addPath(builder);
        builder.Stroke();
        return true;
    }

    /// <summary>Axial (type 2) shading dictionary for the gradient: DeviceRGB, an
    /// exponential (type 2, N=1) colour ramp between the start/end colours over the
    /// gradient axis, extended past both ends. Coordinates stay in graph-local
    /// space — the graph's content stream translates them into page position.</summary>
    internal static Aspose.Pdf.Core.PdfDictionary BuildAxialShadingDict(GradientAxialShading grad)
    {
        static Aspose.Pdf.Core.PdfArray Rgb(Aspose.Pdf.Color? c)
        {
            var arr = new Aspose.Pdf.Core.PdfArray();
            arr.Add(new Aspose.Pdf.Core.PdfReal((c?.R ?? 0) / 255.0));
            arr.Add(new Aspose.Pdf.Core.PdfReal((c?.G ?? 0) / 255.0));
            arr.Add(new Aspose.Pdf.Core.PdfReal((c?.B ?? 0) / 255.0));
            return arr;
        }

        var fn = new Aspose.Pdf.Core.PdfDictionary();
        fn.Set("FunctionType", new Aspose.Pdf.Core.PdfInteger(2));
        var domain = new Aspose.Pdf.Core.PdfArray();
        domain.Add(new Aspose.Pdf.Core.PdfInteger(0));
        domain.Add(new Aspose.Pdf.Core.PdfInteger(1));
        fn.Set("Domain", domain);
        fn.Set("C0", Rgb(grad.StartColor));
        fn.Set("C1", Rgb(grad.EndColor));
        fn.Set("N", new Aspose.Pdf.Core.PdfInteger(1));

        var sh = new Aspose.Pdf.Core.PdfDictionary();
        sh.Set("ShadingType", new Aspose.Pdf.Core.PdfInteger(2));
        sh.Set("ColorSpace", new Aspose.Pdf.Core.PdfName("DeviceRGB"));
        var coords = new Aspose.Pdf.Core.PdfArray();
        coords.Add(new Aspose.Pdf.Core.PdfReal(grad.Start.X));
        coords.Add(new Aspose.Pdf.Core.PdfReal(grad.Start.Y));
        coords.Add(new Aspose.Pdf.Core.PdfReal(grad.End.X));
        coords.Add(new Aspose.Pdf.Core.PdfReal(grad.End.Y));
        sh.Set("Coords", coords);
        sh.Set("Function", fn);
        var extend = new Aspose.Pdf.Core.PdfArray();
        extend.Add(Aspose.Pdf.Core.PdfBoolean.True);
        extend.Add(Aspose.Pdf.Core.PdfBoolean.True);
        sh.Set("Extend", extend);
        return sh;
    }
}
