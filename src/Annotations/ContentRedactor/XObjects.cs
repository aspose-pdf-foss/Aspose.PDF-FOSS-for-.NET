using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    // A pixel edge this close to the area's edge (in pixels) counts as on it.
    private const double PixelSlack = 1e-6;

    private static readonly (double X, double Y)[] UnitSquare = [(0, 0), (1, 0), (1, 1), (0, 1)];

    /// <summary>Plan the Do of an image or a form: nothing when it is drawn clear of the area,
    /// else a redacted copy drawn in its place - an image with its pixels there painted, a form
    /// with its content there removed. A form drawn inside the area is removed whole, and so is
    /// what cannot be redacted in part (an image whose samples cannot be read, a form nested too
    /// deep).</summary>
    private void PlanXObject(Walk walk, string name, GraphicsState state)
    {
        if (walk.Resources.XObject(name) is not { } xobject) return;
        walk.DrawCounts[name] = walk.DrawCounts.TryGetValue(name, out var drawn) ? drawn + 1 : 1;
        var copy = xobject.Dict.GetName("Subtype") switch
        {
            "Image" => PlanImage(walk, xobject, state.Ctm),
            "Form" => PlanForm(walk, xobject, state.Ctm),
            _ => null,
        };
        if (xobject.Dict.GetName("Subtype") == "Form")
            walk.FormsInheritResources |= xobject.Dict.Get("Resources") is null;
        if (copy is not null)
        {
            var copyName = walk.Resources.AddXObject(copy);
            walk.DrawReplacement = $"/{copyName} Do";
            walk.Redrawn.Add((name, copyName, walk.Current!.Start));
        }
        (walk.DrawReplacement is null ? walk.KeptNames : walk.ReplacedNames).Add(name);
    }

    /// <summary>The painted copy of an image drawn partly or wholly in the area; null when it is
    /// drawn clear of it or cannot be painted (then its Do goes).</summary>
    private PdfStream? PlanImage(Walk walk, PdfStream image, double[] ctm)
    {
        var coverage = CoverageOf(UnitSquare.Select(p => Transform(ctm, p.X, p.Y)).ToList());
        if (coverage == Coverage.Outside) return null;
        var painted = BlankedImage(image, ctm, walk.Resources);
        if (painted is null) walk.DrawReplacement = string.Empty;
        return painted;
    }

    /// <summary>The redacted copy of a form drawn partly in the area; null when it is drawn clear
    /// of it, holds nothing there, or goes whole.</summary>
    private PdfStream? PlanForm(Walk walk, PdfStream form, double[] ctm)
    {
        var formCtm = GraphicsState.MultiplyMatrices(Tagged.PageContentScan.FormMatrix(_reader, form), ctm);
        var box = _reader.Resolve(form.Dict.Get("BBox")) is PdfArray { Count: 4 } bbox
            ? Rectangle.FromPdfArray(bbox, _reader) : null;
        if (box is not null)
        {
            (double X, double Y)[] corners = [(box.LLX, box.LLY), (box.URX, box.LLY), (box.URX, box.URY), (box.LLX, box.URY)];
            var coverage = CoverageOf(corners.Select(p => Transform(formCtm, p.X, p.Y)).ToList());
            if (coverage == Coverage.Outside) return null;
            if (coverage == Coverage.Inside)
            {
                walk.DrawReplacement = string.Empty;
                return null;
            }
        }
        if (walk.Depth >= MaxFormDepth)
        {
            walk.DrawReplacement = string.Empty;
            return null;
        }

        var own = _reader.ResolveDict(form.Dict.Get("Resources"));
        var resources = new ResourceCopy(_reader, own ?? walk.Resources.Current);
        var rewritten = Rewrite(_reader.DecodeStream(form), resources, formCtm, walk.Depth + 1);
        if (rewritten is null) return null;

        var dict = Shallow(form.Dict);
        dict.Remove("Filter");
        dict.Remove("DecodeParms");
        dict.Remove("Length");
        dict.Remove("PieceInfo");
        if (resources.Copy is { } copy) dict.Set("Resources", copy);
        return new PdfStream(dict, rewritten);
    }
}
