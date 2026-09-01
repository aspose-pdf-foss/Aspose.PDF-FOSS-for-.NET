using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
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
    public string AddExtGState(Content.ExtGState extGState)
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

        gsDict.Set(name, extGState.ToPdfDictionary());
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
