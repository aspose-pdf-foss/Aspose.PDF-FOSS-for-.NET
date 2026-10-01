using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aspose.Pdf.Vector;

/// <summary>Writes the vector graphics of a page, or a chosen part of them, as SVG documents.</summary>
public class SvgExtractor
{
    private readonly SvgExtractionOptions _options;

    /// <summary>An extractor with the default options.</summary>
    public SvgExtractor() : this(new SvgExtractionOptions())
    {
    }

    /// <summary>An extractor with the given options.</summary>
    public SvgExtractor(SvgExtractionOptions options)
    {
        _options = options ?? new SvgExtractionOptions();
    }

    /// <summary>One SVG of the absorbed elements of <paramref name="page"/> that pass <paramref name="filter"/>.</summary>
    public string Extract(GraphicsAbsorber absorber, Predicate<GraphicElement> filter, Page page)
    {
        if (absorber is null) throw new ArgumentNullException(nameof(absorber));
        var chosen = absorber.Elements.Where(e => filter is null || filter(e)).ToList();
        return Extract(chosen, page);
    }

    /// <summary>Writes <see cref="Extract(GraphicsAbsorber, Predicate{GraphicElement}, Page)"/> to a file.</summary>
    public void Extract(GraphicsAbsorber absorber, Predicate<GraphicElement> filter, Page page, string svgFilePath)
    {
        SvgWriter.Save(svgFilePath, Extract(absorber, filter, page));
    }

    /// <summary>One SVG holding <paramref name="elements"/>, placed as they lie on <paramref name="page"/>.</summary>
    public string Extract(IEnumerable<GraphicElement> elements, Page page)
    {
        if (elements is null) throw new ArgumentNullException(nameof(elements));
        var chosen = Chosen(elements.ToList());
        return SvgWriter.Document(chosen, chosen.Count > 0 ? SvgWriter.Union(chosen) : PageBox(page));
    }

    /// <summary>Writes <see cref="Extract(IEnumerable{GraphicElement}, Page)"/> to a file.</summary>
    public void Extract(IEnumerable<GraphicElement> elements, Page page, string svgFilePath)
    {
        SvgWriter.Save(svgFilePath, Extract(elements, page));
    }

    /// <summary>One SVG per element (or per group, or per sub-path - see <see cref="SvgExtractionOptions"/>)
    /// of <paramref name="page"/>.</summary>
    public List<string> Extract(Page page)
    {
        if (page is null) throw new ArgumentNullException(nameof(page));
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);
        // The list is built from the collection, not asked of it: a ToList member on the collection
        // (the public API's own class has one) would take the call from the LINQ one.
        return Groups(Chosen(new List<GraphicElement>(absorber.Elements)))
            .Select(group => SvgWriter.Document(group, SvgWriter.Union(group)))
            .ToList();
    }

    /// <summary>Writes <see cref="Extract(Page)"/> into <paramref name="directory"/>, one numbered file each.</summary>
    public void Extract(Page page, string directory)
    {
        if (directory is null) throw new ArgumentNullException(nameof(directory));
        Directory.CreateDirectory(directory);
        var written = Extract(page);
        for (var index = 0; index < written.Count; index++)
            File.WriteAllText(Path.Combine(directory, $"{index + 1}.svg"), written[index]);
    }

    /// <summary>The elements the options keep, unpacked and filtered as they ask.</summary>
    private List<GraphicElement> Chosen(List<GraphicElement> elements)
    {
        var unpacked = new List<GraphicElement>();
        foreach (var element in elements)
            Unpack(element, unpacked);
        return unpacked.Where(Kept).ToList();
    }

    private void Unpack(GraphicElement element, List<GraphicElement> into)
    {
        if (element is XFormPlacement placement && OpensForm(placement))
        {
            foreach (var child in placement.Elements)
                Unpack(child, into);
            return;
        }
        into.Add(element);
    }

    // The predicate, when there is one, decides form by form; otherwise UnpackPageContentXForm decides for all.
    private bool OpensForm(XFormPlacement placement) =>
        _options.UnpackXFormPredicate is { } predicate ? predicate(placement) : _options.UnpackPageContentXForm;

    private bool Kept(GraphicElement element)
    {
        if (element is SubPath path && _options.MinStrokeWidth > 0 && path.IsStrokedThinnerThan(_options.MinStrokeWidth))
            return false;
        var bound = _options.ExtractionAreaBound;
        if (bound is null) return true;
        var r = element.Rectangle;
        return _options.StrictExtractionAreaBoundCheck
            ? bound.LLX <= r.LLX && bound.LLY <= r.LLY && r.URX <= bound.URX && r.URY <= bound.URY
            : r.LLX <= bound.URX && bound.LLX <= r.URX && r.LLY <= bound.URY && bound.LLY <= r.URY;
    }

    /// <summary>The elements as the SVGs they go into. With <see cref="SvgExtractionOptions.ExtractEverySubPathToSvg"/>
    /// each on its own; otherwise the elements whose painted areas touch or overlap - or, with
    /// <see cref="SvgExtractionOptions.AutoGrouping"/>, lie near each other - share one SVG. The SVGs run
    /// from the top of the page down, each holding its elements in painting order.</summary>
    private List<List<GraphicElement>> Groups(List<GraphicElement> elements)
    {
        if (_options.ExtractEverySubPathToSvg)
            return elements.Select(e => new List<GraphicElement> { e }).ToList();
        var parent = Enumerable.Range(0, elements.Count).ToArray();
        int Root(int k) { while (parent[k] != k) k = parent[k] = parent[parent[k]]; return k; }
        for (var a = 0; a < elements.Count; a++)
            for (var b = a + 1; b < elements.Count; b++)
                if (Near(elements[a], elements[b])) parent[Root(a)] = Root(b);
        return Enumerable.Range(0, elements.Count).GroupBy(Root)
            .Select(group => group.OrderBy(k => k).Select(k => elements[k]).ToList())
            .Select(group => (Group: group, Box: SvgWriter.Union(group)))
            .OrderByDescending(g => g.Box.URY).ThenBy(g => g.Box.LLX)
            .Select(g => g.Group)
            .ToList();
    }

    // Touching counts as near; AutoGrouping reaches GroupStrength of the smaller painted box further.
    private bool Near(GraphicElement a, GraphicElement b)
    {
        Rectangle ra = a.PaintBounds, rb = b.PaintBounds;
        var reach = _options.AutoGrouping
            ? _options.GroupStrength * Math.Min(Math.Min(ra.Width, ra.Height), Math.Min(rb.Width, rb.Height))
            : 0;
        var gapX = Math.Max(0, Math.Max(ra.LLX, rb.LLX) - Math.Min(ra.URX, rb.URX));
        var gapY = Math.Max(0, Math.Max(ra.LLY, rb.LLY) - Math.Min(ra.URY, rb.URY));
        return gapX <= reach && gapY <= reach;
    }

    private static Rectangle PageBox(Page page) => page?.Rect ?? Rectangle.Empty;
}
