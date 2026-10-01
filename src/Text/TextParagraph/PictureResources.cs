using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    /// <summary>The name <paramref name="xobject"/> goes by in the page's XObject
    /// resources: the entry already holding that very stream, else a new one.</summary>
    internal static string EnsureXObject(Page page, PdfStream xobject)
    {
        var xobjects = EnsureResourceDict(page, "XObject");
        foreach (var key in xobjects.Keys)
            if (ReferenceEquals(xobjects.Get(key), xobject)) return key;
        var prefix = xobject.Dict.Get("Subtype") is PdfName { Value: "Form" } ? "Fm" : "Im";
        var n = 1;
        while (xobjects.ContainsKey($"{prefix}{n}")) n++;
        var name = $"{prefix}{n}";
        xobjects.Set(name, xobject);
        return name;
    }

    /// <summary>The name of an ExtGState on the page painting with the blend mode
    /// <paramref name="mode"/> and nothing else: an existing entry, else a new one.</summary>
    internal static string EnsureBlendExtGState(Page page, string mode)
    {
        var states = EnsureExtGStateDict(page);
        foreach (var key in states.Keys)
            if (page.Reader.ResolveDict(states.Get(key)) is { Count: 1 } entry
                && entry.Get("BM") is PdfName { Value: var bm } && bm == mode)
                return key;
        var n = 1;
        while (states.ContainsKey($"GSb{n}")) n++;
        var name = $"GSb{n}";
        var state = new PdfDictionary();
        state.Set("BM", new PdfName(mode));
        states.Set(name, state);
        return name;
    }

    /// <summary>Gives <paramref name="to"/> each XObject <paramref name="from"/> holds that
    /// <paramref name="content"/> draws, under the same name, where it has none of that
    /// name: a page a flow spills onto draws the pictures the flow named on the page it
    /// began, and takes no others.</summary>
    internal static void CarryXObjects(Page from, Page to, byte[] content)
    {
        var resources = from.Reader.ResolveDict(from.Dict.Get("Resources"));
        if (resources is null || from.Reader.ResolveDict(resources.Get("XObject")) is not { } source) return;
        var text = Compat.Latin1.GetString(content);
        PdfDictionary? target = null;
        foreach (var key in source.Keys)
        {
            if (text.IndexOf("/" + key + " Do", StringComparison.Ordinal) < 0 || source.Get(key) is not { } xobject) continue;
            target ??= EnsureResourceDict(to, "XObject");
            if (!target.ContainsKey(key)) target.Set(key, xobject);
        }
    }

    /// <summary>The page's resource dictionary of that kind, made when missing.</summary>
    private static PdfDictionary EnsureResourceDict(Page page, string kind)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var dict = page.Reader.ResolveDict(resources.Get(kind));
        if (dict is null)
        {
            dict = new PdfDictionary();
            resources.Set(kind, dict);
        }
        return dict;
    }
}
