using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Set when a text edit on this page requested
    /// <see cref="Text.TextEditOptions.FontReplace.RemoveUnusedFonts"/>; the save
    /// pipeline then prunes /Font resources no longer referenced by any content.</summary>
    internal bool PruneUnusedFontsOnSave { get; set; }

    /// <summary>Fonts referenced by this page.</summary>
    public FontCollection Fonts =>
        _fonts ??= new FontCollection(_dict, _reader);

    /// <summary>
    /// Add a table to this page. The table renders itself to a content stream
    /// and registers required font resources.
    /// </summary>
    public void AddTable(Table table)
    {
        var contentBytes = table.Build(this);
        AddContentStream(contentBytes);
    }

    /// <summary>
    /// Add a graph (collection of shapes) to this page.
    /// ExtGState resources for opacity/blend mode are registered automatically.
    /// </summary>
    public void AddGraph(Drawing.Graph graph)
    {
        var contentBytes = graph.Build(this);
        AddContentStream(contentBytes);
    }

    /// <summary>
    /// Add a floating box to this page.
    /// The box is rendered to a content stream and appended to the page content.
    /// </summary>
    public void AddFloatingBox(FloatingBox box)
    {
        var contentBytes = box.Build(this);
        AddContentStream(contentBytes);
    }

    /// <summary>
    /// Add an ExtGState dictionary to this page's resources and return the resource name.
    /// </summary>
    public string AddExtGState(Content.ExtGState extGState) =>
        AddExtGStateDict(extGState.ToPdfDictionary());

    /// <summary>Put a graphics state dictionary under a fresh <c>GS<i>n</i></c>
    /// name in this page's /Resources/ExtGState, for a state the
    /// <see cref="Content.ExtGState"/> model has no room for.</summary>
    internal string AddExtGStateDict(PdfDictionary state)
    {
        // Resolve indirect /Resources and /ExtGState references rather than a
        // bare `as PdfDictionary` cast (which yields null for an indirect ref
        // and would replace the real dictionary, dropping the page's fonts and
        // other resources, with a fresh empty one).
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        var gsDict = resources.Get("ExtGState") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null)
        {
            gsDict = new PdfDictionary();
            resources.Set("ExtGState", gsDict);
        }

        // Find a unique name
        var name = "GS0";
        var counter = 0;
        while (gsDict.ContainsKey(name))
            name = $"GS{++counter}";

        gsDict.Set(name, state);
        return name;
    }

    /// <summary>Register an ExtGState under a lowercase sequential resource name
    /// (<c>gs1, gs2, …</c>) — the naming used for per-paint transparency states
    /// on drawable shapes, distinct from the uppercase <c>GS<i>n</i></c> series.</summary>
    internal string AddExtGStateSequential(Content.ExtGState extGState)
    {
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }
        var gsDict = resources.Get("ExtGState") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null)
        {
            gsDict = new PdfDictionary();
            resources.Set("ExtGState", gsDict);
        }
        var counter = 1;
        var name = "gs1";
        while (gsDict.ContainsKey(name))
            name = $"gs{++counter}";
        gsDict.Set(name, extGState.ToPdfDictionary());
        return name;
    }

    /// <summary>
    /// Add a shading dictionary to this page's /Resources/Shading and return the
    /// resource name (usable with the <c>sh</c> operator).
    /// </summary>
    internal string AddShading(PdfDictionary shadingDict)
    {
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        var shDict = resources.Get("Shading") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("Shading"));
        if (shDict is null)
        {
            shDict = new PdfDictionary();
            resources.Set("Shading", shDict);
        }

        var name = "Sh0";
        var counter = 0;
        while (shDict.ContainsKey(name))
            name = $"Sh{++counter}";

        shDict.Set(name, shadingDict);
        return name;
    }

    /// <summary>
    /// Register an axial gradient in this page's /Resources/Shading and return the
    /// resource name, for a caller writing its own content stream: clip to the
    /// shape the gradient is to fill and paint it with <c>/Name sh</c>.
    ///
    /// The gradient's endpoints are in the coordinate space the <c>sh</c> is
    /// painted in, so a caller that concatenates a matrix first states them in
    /// that space.
    /// </summary>
    public string AddShading(Aspose.Pdf.Drawing.GradientAxialShading gradient)
    {
        if (gradient is null) throw new System.ArgumentNullException(nameof(gradient));
        return AddShading(Aspose.Pdf.Drawing.Shape.BuildAxialShadingDict(gradient));
    }

    /// <summary>
    /// Register an axial gradient as a shading PATTERN in this page's
    /// /Resources/Pattern and return the resource name, for a caller filling a
    /// shape with it: <c>/Pattern cs /Name scn</c>, then paint the shape.
    ///
    /// This is the alternative to <see cref="AddShading(Aspose.Pdf.Drawing.GradientAxialShading)"/>,
    /// and the difference matters at a shape's EDGE: a clip-and-paint covers
    /// whole pixels, while a fill is antialiased against what is behind it.
    ///
    /// ⚠ Pattern space is the page's DEFAULT space, so a pattern is not moved by
    /// a matrix already in force when the shape is painted. A caller drawing
    /// under its own <c>cm</c> passes that same matrix here.
    /// </summary>
    public string AddPattern(Aspose.Pdf.Drawing.GradientAxialShading gradient,
                             Aspose.Pdf.Matrix? matrix = null)
    {
        if (gradient is null) throw new System.ArgumentNullException(nameof(gradient));

        var pattern = new PdfDictionary();
        pattern.Set("Type", new PdfName("Pattern"));
        pattern.Set("PatternType", new PdfInteger(ShadingPatternType));
        pattern.Set("Shading", Aspose.Pdf.Drawing.Shape.BuildAxialShadingDict(gradient));

        if (matrix is not null)
        {
            var entries = new PdfArray();
            foreach (var value in matrix.Data) entries.Add(new PdfReal(value));
            pattern.Set("Matrix", entries);
        }

        return AddPattern(pattern);
    }

    /// <summary>
    /// Register a LUMINOSITY soft mask that ramps along an axis, and return the
    /// /ExtGState resource name that puts it in force: paint <c>/Name gs</c>,
    /// and every mark that follows is laid down at the opacity the ramp gives
    /// its place on the page.
    ///
    /// A PDF axial shading carries no alpha at all, so a gradient that FADES is
    /// painted through a mask instead: a transparency group holding the same
    /// ramp in grey, whose luminosity -- black none, white full -- is read as
    /// the opacity (PDF 32000 &#167;11.6.5.2). The ramp handed here is therefore
    /// a ramp of GREYS, one per stop of the colour ramp it masks, on the axis
    /// that colour ramp runs along.
    ///
    /// &#9888; The group is rendered under the matrix in force when the
    /// <c>gs</c> runs, not the one in force when the paint happens, so the axis
    /// and the box are stated in the space the CALLER is drawing in. This is
    /// the opposite of a PATTERN, which is placed in the page's default space
    /// and carries a matrix of its own.
    /// </summary>
    public string AddLuminositySoftMask(Aspose.Pdf.Drawing.GradientAxialShading ramp,
                                        Aspose.Pdf.Rectangle box)
    {
        if (ramp is null) throw new System.ArgumentNullException(nameof(ramp));
        if (box is null) throw new System.ArgumentNullException(nameof(box));

        var mask = new PdfDictionary();
        mask.Set("Type", new PdfName("Mask"));
        mask.Set("S", new PdfName("Luminosity"));
        mask.Set("G", MaskGroup(ramp, box));
        mask.Set("BC", Grey(MaskedOutLuminosity));

        var state = new PdfDictionary();
        state.Set("Type", new PdfName("ExtGState"));
        state.Set("SMask", mask);
        return AddExtGStateDict(state);
    }

    /// <summary>The group a luminosity mask reads: the grey ramp, painted over
    /// the box the mask covers and nowhere else.</summary>
    private static PdfStream MaskGroup(Aspose.Pdf.Drawing.GradientAxialShading ramp,
                                       Aspose.Pdf.Rectangle box)
    {
        var shadings = new PdfDictionary();
        shadings.Set(MaskShadingName, Aspose.Pdf.Drawing.Shape.BuildAxialShadingDict(ramp));
        var resources = new PdfDictionary();
        resources.Set("Shading", shadings);

        var group = new PdfDictionary();
        group.Set("Type", new PdfName("Group"));
        group.Set("S", new PdfName("Transparency"));
        group.Set("CS", new PdfName("DeviceRGB"));

        var bounds = new PdfArray();
        foreach (var edge in new[] { box.LLX, box.LLY, box.URX, box.URY })
            bounds.Add(new PdfReal(edge));

        var content = Encoding.ASCII.GetBytes($"/{MaskShadingName} sh\n");

        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(FormXObjectType));
        form.Set("BBox", bounds);
        form.Set("Group", group);
        form.Set("Resources", resources);
        form.Set("Length", new PdfInteger(content.Length));
        return new PdfStream(form, content);
    }

    /// <summary>A grey, written as the three equal components a DeviceRGB group
    /// asks for.</summary>
    private static PdfArray Grey(double level)
    {
        var components = new PdfArray();
        for (var i = 0; i < RgbComponents; i++) components.Add(new PdfReal(level));
        return components;
    }

    /// <summary>Black: what a luminosity mask reads outside its group's box,
    /// which is nothing painted at all.</summary>
    private const double MaskedOutLuminosity = 0;

    private const int RgbComponents = 3;

    /// <summary>The only form type there is.</summary>
    private const int FormXObjectType = 1;

    private const string MaskShadingName = "Sh0";

    /// <summary>A pattern that paints a shading, rather than tiling a cell.</summary>
    private const int ShadingPatternType = 2;

    /// <summary>
    /// Add a pattern dictionary to this page's /Resources/Pattern and return the
    /// resource name (usable with <c>/Pattern cs /Name scn</c>).
    /// </summary>
    internal string AddPattern(PdfDictionary patternDict)
    {
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        var patDict = resources.Get("Pattern") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("Pattern"));
        if (patDict is null)
        {
            patDict = new PdfDictionary();
            resources.Set("Pattern", patDict);
        }

        var name = "P0";
        var counter = 0;
        while (patDict.ContainsKey(name))
            name = $"P{++counter}";

        patDict.Set(name, patternDict);
        return name;
    }

    /// <summary>
    /// Resolves the normal appearance stream (AP → N) for an annotation.
    /// Handles both direct streams and state dictionaries (where the current state
    /// is selected by the /AS entry, falling back to the first non-Off state).
    /// </summary>
    private PdfStream? ResolveAppearanceStream(PdfDictionary annotDict)
    {
        var apDict = _reader.ResolveDict(annotDict.Get("AP"));
        if (apDict is null) return null;

        var nResolved = _reader.Resolve(apDict.Get("N"));

        // Direct appearance stream — most common case
        if (nResolved is PdfStream ns)
            return ns;

        // State dictionary — /N is a dict mapping state names (e.g. "Yes"/"Off") to streams.
        // Select the stream for the current state (/AS), or the first non-Off state.
        if (nResolved is PdfDictionary stateDict)
        {
            var asName = annotDict.GetName("AS");
            if (asName is not null)
            {
                var stream = _reader.ResolveStream(stateDict.Get(asName));
                if (stream is not null) return stream;
            }
            foreach (var key in stateDict.Keys)
            {
                if (key == "Off") continue;
                var stream = _reader.ResolveStream(stateDict.Get(key));
                if (stream is not null) return stream;
            }
        }

        return null;
    }
}
