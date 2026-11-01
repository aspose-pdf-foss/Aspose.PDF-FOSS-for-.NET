using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Collection of fonts referenced by a page.
/// </summary>
public sealed class FontCollection : IEnumerable<Font>
{
    private readonly List<Font> _fonts = new();
    private int _nextResId = 1;

    // The page this collection belongs to. Kept so that adding a font can put it
    // into the page's own resources; a collection built without a page (the
    // absorber's view of accumulated fonts) has none and stays read-only.
    private readonly PdfDictionary? _pageDict;
    private readonly PdfReader? _pageReader;
    // A collection read from a resource dictionary that carries /Font itself (the
    // AcroForm /DR, a form XObject's resources) adds its fonts there.
    private readonly PdfDictionary? _resourceDict;

    /// <summary>Empty collection — used by <see cref="FontAbsorber"/> to expose accumulated fonts.</summary>
    internal FontCollection() { }

    internal FontCollection(PdfDictionary pageDict, PdfReader reader)
    {
        _pageDict = pageDict;
        _pageReader = reader;
        // /Resources is an inheritable page attribute: a page dict frequently carries no
        // /Resources of its own and inherits the nearest ancestor's in the /Pages tree.
        // Resolve the effective resources so fonts referenced by the page content (e.g.
        // /FAAAAI Tf) are discoverable rather than reporting an empty collection.
        var resources = ResolveEffectiveResources(pageDict, reader);
        if (resources is not null)
        {
            var fontDict = reader.ResolveDict(resources.Get("Font"));
            if (fontDict is not null)
            {
                var t3 = 0;
                foreach (var key in fontDict.Keys)
                {
                    var font = reader.ResolveDict(fontDict.Get(key));
                    if (font is not null)
                    {
                        var fi = new Font(key, font, reader);
                        if (fi.IsNamelessType3) fi.SynthesizedFontName = $"T3Font_{t3++}";
                        _fonts.Add(fi);
                    }
                }
            }
        }
    }

    /// <summary>Resolve a page's effective /Resources, walking the /Parent chain for the
    /// inherited dictionary when the page itself declares none. Returns null when no page
    /// or ancestor carries /Resources.</summary>
    private static PdfDictionary? ResolveEffectiveResources(PdfDictionary pageDict, PdfReader reader)
    {
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is not null) return resources;

        var parentObj = pageDict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            var res = reader.ResolveDict(parent.Get("Resources"));
            if (res is not null) return res;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    private FontCollection(PdfReader reader, PdfDictionary resourceDict)
    {
        _resourceDict = resourceDict;
        _pageReader = reader;
    }

    /// <summary>True when an added font can be declared: the collection belongs to a page or a resource dictionary.</summary>
    private bool CanDeclare => _pageReader is not null && (_pageDict is not null || _resourceDict is not null);

    /// <summary>The /Font dictionary an added font is declared in, created when absent.</summary>
    private PdfDictionary DeclaredFonts()
    {
        if (_pageDict is not null) return GetOrCreateFontResources(_pageDict, _pageReader!);
        var fonts = _pageReader!.ResolveDict(_resourceDict!.Get("Font"));
        if (fonts is null) _resourceDict.Set("Font", fonts = new PdfDictionary());
        return fonts;
    }

    /// <summary>Build a collection from a resource dictionary that carries /Font
    /// directly (e.g. the AcroForm /DR), as opposed to a page dict whose fonts
    /// live under /Resources/Font.</summary>
    internal static FontCollection ForResources(PdfDictionary resourceDict, PdfReader reader)
    {
        var fc = new FontCollection(reader, resourceDict);
        var fontDict = reader.ResolveDict(resourceDict.Get("Font"));
        if (fontDict is not null)
        {
            var t3 = 0;
            foreach (var key in fontDict.Keys)
            {
                var font = reader.ResolveDict(fontDict.Get(key));
                if (font is not null)
                {
                    var fi = new Font(key, font, reader);
                    if (fi.IsNamelessType3) fi.SynthesizedFontName = $"T3Font_{t3++}";
                    fc._fonts.Add(fi);
                }
            }
        }
        return fc;
    }

    /// <summary>Gets the number of fonts in the collection.</summary>
    public int Count => _fonts.Count;

    /// <summary>1-based indexer for API compatibility.</summary>
    public Font this[int index] => _fonts[index - 1];

    /// <summary>Look up a font by its PDF resource name (e.g. "F1") or BaseFont name.</summary>
    public Font this[string name]
    {
        get
        {
            foreach (var f in _fonts)
                if (f.ResourceName == name || f.BaseFont == name || f.FontName == name)
                    return f;
            throw new KeyNotFoundException($"Font '{name}' not found in collection.");
        }
    }

    public IEnumerator<Font> GetEnumerator() => _fonts.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

#pragma warning disable S2325 // public API: the published instance member
    public bool IsReadOnly => false;
#pragma warning restore S2325
#pragma warning disable S2325 // public API: the published instance member
    public bool IsSynchronized => false;
#pragma warning restore S2325
    public object SyncRoot { get; } = new();

    /// <summary>Returns <c>true</c> when the collection contains the given font object.</summary>
    public bool Contains(Font item) => _fonts.Contains(item);

    /// <summary>Returns <c>true</c> when a font in the collection has the given resource name (e.g. "F1"), BaseFont name or font name.</summary>
    public bool Contains(string name)
    {
        foreach (var f in _fonts)
            if (f.ResourceName == name || f.BaseFont == name || f.FontName == name)
                return true;
        return false;
    }

    /// <summary>Copies the fonts into <c>array</c>, starting at the zero-based <c>index</c>.</summary>
    public void CopyTo(Font[] array, int index) => _fonts.CopyTo(array, index);

    /// <summary>Removes the given font from this collection only (the page's resources are not changed); returns <c>true</c> when it was found.</summary>
    public bool Remove(Font item) => _fonts.Remove(item);

    /// <summary>
    /// Add a font and emit the PDF resource name assigned to it (e.g. "F1", "F2", ...).
    /// </summary>
    /// <summary>
    /// Add a font to the page's resources and report the name its content
    /// stream should select it by.
    ///
    /// The font is embedded into the page's /Resources /Font dictionary through
    /// the same writer the document model uses, so a page drawn with operators
    /// alone declares the face it references. Without this the returned name
    /// named nothing: the content stream selected a font the page never
    /// declared, and a reader silently substituted one.
    /// </summary>
    public void Add(Font newFont, out string resName)
    {
        if (newFont is null) throw new ArgumentNullException(nameof(newFont));

        // A Type 1 program is declared as the simple font it is, its program embedded.
        if (CanDeclare && newFont.SourceFontData?.Type1 is { } type1)
        {
            resName = DeclareType1(DeclaredFonts(), type1);
            newFont.PageResourceName = resName;
            _fonts.Add(newFont);
            return;
        }

        // A predefined CJK face is declared by name in its CMap, with no program.
        if (CanDeclare && newFont.SourceFontData?.PredefinedCid is { } cid)
        {
            resName = DeclarePredefinedCid(DeclaredFonts(), cid);
            newFont.PageResourceName = resName;
            _fonts.Add(newFont);
            return;
        }

        var program = newFont.ProgramBytes();
        if (CanDeclare && program is not null)
        {
            var fontDict = DeclaredFonts();
            // No text yet: this registers the face. Each run shown against it
            // adds its own glyphs, and the save-time subsetter keeps only those.
            // /BaseFont carries the face's PostScript name, which is not its full
            // name: "Lato-Regular" rather than "Lato Regular".
            var baseFont = FontRepository.ReadTtfPostScriptName(program)
                           ?? newFont.FontName ?? "Font";
            (resName, _) = Type0FontEmbedder.Embed(fontDict, program, baseFont, string.Empty);
            newFont.EmbeddedBaseFontName = baseFont;
            newFont.PageResourceName = resName;
            _fonts.Add(newFont);
            return;
        }

        // A Standard-14 face has no program to embed: it is declared by name, as a
        // plain Type1 font every reader carries, and its text is written in WinAnsi.
        var core = program is null ? Standard14Fonts.CanonicalName(newFont.FontName ?? string.Empty) : null;
        if (CanDeclare && core is not null)
        {
            resName = DeclareStandard14(DeclaredFonts(), core);
            newFont.PageResourceName = resName;
            _fonts.Add(newFont);
            return;
        }

        // No page to write to, or no font program to embed: keep the old
        // behaviour of naming the font without registering it.
        resName = $"F{_nextResId++}";
        _fonts.Add(newFont);
    }

    /// <summary>
    /// The resource name of a Type1 entry for <paramref name="baseFont"/> in the page's
    /// /Font dictionary, written when the page does not declare one already. Symbol and
    /// ZapfDingbats keep their built-in encodings; the text faces use WinAnsi.
    /// </summary>
    private static string DeclareStandard14(PdfDictionary fonts, string baseFont)
    {
        foreach (var key in fonts.Keys)
        {
            if (fonts.Get(key) is PdfDictionary entry
                && entry.GetName("Subtype") == "Type1"
                && entry.GetName("BaseFont") == baseFont
                && entry.Get("FontDescriptor") is null)
                return key;
        }

        var counter = 1;
        while (fonts.ContainsKey($"F{counter}")) counter++;
        var name = $"F{counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        if (!Standard14Fonts.IsSymbolic(baseFont))
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fonts.Set(name, font);
        return name;
    }

    /// <summary>The resource name of <paramref name="type1"/> in the page's /Font dictionary,
    /// declared when the page does not hold it already.</summary>
    private static string DeclareType1(PdfDictionary fonts, Type1FontFile type1)
    {
        foreach (var key in fonts.Keys)
        {
            if (fonts.Get(key) is PdfDictionary entry
                && entry.Get("FontDescriptor") is PdfDictionary descriptor
                && ReferenceEquals(descriptor.Get("FontFile"), type1.Program()))
                return key;
        }

        var counter = 1;
        while (fonts.ContainsKey($"F{counter}")) counter++;
        var name = $"F{counter}";
        fonts.Set(name, type1.FontDictionary());
        return name;
    }

    /// <summary>The resource name of <paramref name="cid"/> in the page's /Font dictionary,
    /// declared when the page does not hold the face in that CMap already.</summary>
    internal static string DeclarePredefinedCid(PdfDictionary fonts, PredefinedCidFont cid)
    {
        var declared = fonts.Keys.FirstOrDefault(key =>
            fonts.Get(key) is PdfDictionary entry
            && entry.GetName("Subtype") == "Type0"
            && entry.GetName("BaseFont") == cid.FaceName
            && entry.GetName("Encoding") == cid.CMap.Name);
        if (declared is not null) return declared;

        var counter = 1;
        while (fonts.ContainsKey($"F{counter}")) counter++;
        var name = $"F{counter}";
        fonts.Set(name, cid.FontDictionary());
        return name;
    }

    /// <summary>The page's /Resources /Font dictionary, created when absent.</summary>
    internal static PdfDictionary GetOrCreateFontResources(PdfDictionary pageDict, PdfReader reader)
    {
        // Resolve an indirect /Resources rather than casting it away: a bare cast
        // yields null for an indirect reference and would replace the real
        // dictionary with an empty one, dropping everything already on the page.
        var resources = pageDict.Get("Resources") as PdfDictionary
            ?? reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }

        var fonts = resources.Get("Font") as PdfDictionary
            ?? reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        return fonts;
    }
}
