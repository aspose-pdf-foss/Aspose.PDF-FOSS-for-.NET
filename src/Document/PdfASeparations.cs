using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The exponential interpolation function type (ISO 32000-1 7.10.3) and its linear exponent.</summary>
    private const int ExponentialFunctionType = 2;
    private const int LinearTintExponent = 1;

    /// <summary>Gives every separation colour space whose tint transform is not a function one: a file
    /// may carry a separation whose transform object is missing (a null or a bare number), which a
    /// viewer that paints the space - a flattened appearance, a form drawn on a page - rejects as a
    /// syntax error, and which no archival level allows. The replacement is a linear ramp in the
    /// alternate space from no ink (its white) to full ink (its black): the only reading of a tint
    /// the file no longer defines. Rides the page and appearance resources once per conversion.</summary>
    private void RepairSeparationTintTransforms()
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<PdfArray>(ReferenceEqualityComparer.Instance);
        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = _reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            if (resources is not null) RepairSeparationTintsIn(resources, visited, seen);
            foreach (var appearance in CollectAnnotationAppearanceResources(page))
                RepairSeparationTintsIn(appearance, visited, seen);
        }
    }

    private void RepairSeparationTintsIn(PdfDictionary resources, HashSet<PdfDictionary> visited, HashSet<PdfArray> seen)
    {
        if (!visited.Add(resources)) return;
        if (_reader.ResolveDict(resources.Get("ColorSpace")) is { } spaces)
            foreach (var key in new List<string>(spaces.Keys))
                if (_reader.Resolve(spaces.Get(key)) is PdfArray space && seen.Add(space)) RepairSeparationTint(space);
        if (_reader.ResolveDict(resources.Get("XObject")) is not { } xobjects) return;
        foreach (var key in new List<string>(xobjects.Keys))
        {
            var form = _reader.ResolveStream(xobjects.Get(key));
            if (form?.Dict.GetName("Subtype") == "Form" && _reader.ResolveDict(form.Dict.Get("Resources")) is { } inner)
                RepairSeparationTintsIn(inner, visited, seen);
        }
    }

    /// <summary>Replaces the tint transform of a separation space that has none that is a function.</summary>
    private void RepairSeparationTint(PdfArray space)
    {
        const int TintTransformIndex = 3;
        if (space.Count <= TintTransformIndex || _reader.Resolve(space[0]) is not PdfName { Value: "Separation" }) return;
        var transform = _reader.Resolve(space[TintTransformIndex]);
        if (transform is PdfDictionary function && function.ContainsKey("FunctionType")) return;
        if (transform is PdfStream sampled && sampled.Dict.ContainsKey("FunctionType")) return;
        var components = ColorSpaceComponents(space[2], 0);
        if (components == 0) return;
        space.RemoveAt(TintTransformIndex);
        space.Insert(TintTransformIndex, TintRamp(components));
    }

    /// <summary>A linear Type 2 function over the tint from the alternate space's white to its black.</summary>
    private static PdfDictionary TintRamp(int components)
    {
        var white = new PdfArray();
        var black = new PdfArray();
        for (var i = 0; i < components; i++)
        {
            // Subtractive inks start at zero and darken by their last component; additive values start at one.
            var subtractive = components == CmykComponents;
            white.Add(new PdfInteger(subtractive ? 0 : 1));
            black.Add(new PdfInteger(subtractive ? (i == components - 1 ? 1 : 0) : 0));
        }
        var domain = new PdfArray();
        domain.Add(new PdfInteger(0));
        domain.Add(new PdfInteger(1));
        var function = new PdfDictionary();
        function.Set("FunctionType", new PdfInteger(ExponentialFunctionType));
        function.Set("Domain", domain);
        function.Set("C0", white);
        function.Set("C1", black);
        function.Set("N", new PdfInteger(LinearTintExponent));
        return function;
    }
}
