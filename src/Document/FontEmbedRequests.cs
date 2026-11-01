using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>What a font read from the document was asked for through IsEmbedded / IsSubset that only the
    /// save can carry out: a composite font, a face no installed family resolves (substituted at save), a
    /// subset asked to become whole. <paramref name="Subset"/>: the program may be cut to the glyphs shown.
    /// <paramref name="TaggedName"/>: the subset-tagged /BaseFont the request was made under, put back when
    /// a whole program cannot replace the subset, or taken off when nothing could be embedded.
    /// <paramref name="ReplaceProgram"/>: the embedded subset program gives way to the whole face.</summary>
    private sealed record FontEmbedRequest(bool Subset, string? TaggedName, bool ReplaceProgram);

    private Dictionary<PdfDictionary, FontEmbedRequest>? _fontEmbedRequests;

    /// <summary>Ask the next save to embed a program into <paramref name="font"/>.</summary>
    internal void RequestFontEmbed(PdfDictionary font, bool subset, string? taggedName = null, bool replaceProgram = false)
    {
        _fontEmbedRequests ??= new Dictionary<PdfDictionary, FontEmbedRequest>(ReferenceEqualityComparer.Instance);
        _fontEmbedRequests[font] = new FontEmbedRequest(subset, taggedName, replaceProgram);
    }

    /// <summary>A pending request for <paramref name="font"/> now asks for the whole program.</summary>
    internal void CancelSubsetRequest(PdfDictionary font)
    {
        if (_fontEmbedRequests is not null && _fontEmbedRequests.TryGetValue(font, out var request))
            _fontEmbedRequests[font] = request with { Subset = false, TaggedName = null };
    }

    /// <summary>Withdraw a request: the font was told not to be embedded after all.</summary>
    internal void CancelFontEmbed(PdfDictionary font) => _fontEmbedRequests?.Remove(font);

    /// <summary>The save-time half of IsEmbedded / IsSubset on fonts read from the document: each requested
    /// font gets the program of its face (a composite one through the CID path, a simple one substituted
    /// like the embedding pass does), and a name never claims a subset program the font does not carry.</summary>
    private void EmbedRequestedFonts()
    {
        if (_fontEmbedRequests is not { Count: > 0 } requests) return;
        var ef = new FontEmbedState
        {
            includeStandard14 = true,
            reported = new HashSet<string>(StringComparer.Ordinal),
            done = new HashSet<PdfDictionary>(),
            fontFileCache = new Dictionary<string, (int objNum, string embedName)>(),
            visitedRes = new HashSet<PdfDictionary>(),
            companions = new Dictionary<PdfDictionary, PdfDictionary>(ReferenceEqualityComparer.Instance),
        };
        var compositeEmbedded = false;
        var wholeComposites = new List<(PdfDictionary font, string? taggedName)>();
        foreach (var (font, request) in requests)
        {
            if (request.ReplaceProgram && font.GetName("Subtype") == "Type0")
            {
                wholeComposites.Add((font, request.TaggedName));
                continue;
            }
            if (request.ReplaceProgram && !DetachSubsetForWholeFace(font, request.TaggedName)) continue;
            // The embedding pass reads this transient marker as "embed the whole program".
            if (!request.Subset) font.Set("AsposeEmbedFull", PdfBoolean.True);
            EmbedOneFont(ef, font);
            font.Remove("AsposeEmbedFull");
            if (!CarriesProgram(font))
            {
                if (request.TaggedName is not null) SetBaseFontName(font, StripSubsetPrefix(request.TaggedName));
            }
            else if (font.GetName("Subtype") == "Type0")
            {
                compositeEmbedded = true;
            }
        }
        requests.Clear();
        // A composite face embedded whole runs to many megabytes (a CJK face above all): it is cut to the
        // glyphs the pages show, as the standard-font embedding cuts it.
        if (compositeEmbedded)
            Optimization.FontSubsetter.SubsetNewlyEmbeddedCidFonts(_reader, ResolvePendingStream);
        // After the cut above: a subset asked to be whole stays whole.
        foreach (var (font, taggedName) in wholeComposites)
            ReplaceCompositeSubset(font, taggedName);
    }

    /// <summary>Put the whole face in place of a composite font's subset program. Its codes are glyph ids
    /// (Identity encoding and CID-to-GID map), so only a face with the subset's glyph numbering can take
    /// its place: the program the caller opened under that name, else the installed face, when either has
    /// the subset's glyph count. Without one the font keeps its subset program and tagged name.</summary>
    private void ReplaceCompositeSubset(PdfDictionary font, string? taggedName)
    {
        var bare = StripSubsetPrefix(taggedName ?? font.GetName("BaseFont") ?? "");
        var cid = _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants
            ? _reader.ResolveDict(descendants[0]) : null;
        var descriptor = cid is null ? null : _reader.ResolveDict(cid.Get("FontDescriptor"));
        var program = descriptor is null ? null : _reader.ResolveStream(descriptor.Get("FontFile2"));
        var identity = font.GetName("Encoding") is "Identity-H" or "Identity-V"
                       && cid!.Get("CIDToGIDMap") is null or PdfName { Value: "Identity" };
        var whole = program is null || !identity ? null : SameNumberingFace(_reader.DecodeStream(program), bare);
        if (whole is null)
        {
            if (taggedName is not null) SetBaseFontName(font, taggedName);
            return;
        }
        descriptor!.Remove("FontFile2");
        Text.FontEmbedder.EmbedIntoCidFontDict(this, whole, font, cid!);
        SetBaseFontName(font, bare);
    }

    /// <summary>The program the caller opened as <paramref name="name"/>, else the installed face, when it
    /// numbers its glyphs as <paramref name="subset"/> does: a glyph-numbering-preserving subset keeps every
    /// glyph id (trailing unused ones may be cut), so each id it holds has the face's own advance. Null when
    /// neither face agrees.</summary>
    private static byte[]? SameNumberingFace(byte[] subset, string name)
    {
        var advances = Advances(subset);
        if (advances is not { Length: > 0 }) return null;
        foreach (var candidate in new[] { Text.FontRepository.OpenedProgram(name), Text.SystemFontResolver.Resolve(name) })
        {
            if (candidate is not { Length: > 0 } || Advances(candidate) is not { } whole || whole.Length < advances.Length) continue;
            var same = true;
            for (var gid = 0; gid < advances.Length && same; gid++) same = advances[gid] == whole[gid];
            if (same) return candidate;
        }
        return null;
    }

    /// <summary>The advance of every glyph id of a TrueType program, or null when it does not parse.</summary>
    private static int[]? Advances(byte[] program)
    {
        try
        {
            var parser = new Text.TrueTypeParser(program);
            parser.Parse();
            return parser.GlyphWidths;
        }
        catch { return null; }
    }

    /// <summary>Drop the subset program of a simple font so the whole face can be embedded in its place;
    /// false, with the subset name put back, when no installed face could replace it.</summary>
    private bool DetachSubsetForWholeFace(PdfDictionary font, string? taggedName)
    {
        var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
        var bare = StripSubsetPrefix(taggedName ?? font.GetName("BaseFont") ?? "");
        if (descriptor is null || Text.SystemFontResolver.Resolve(bare) is not { Length: > 0 })
        {
            if (taggedName is not null) SetBaseFontName(font, taggedName);
            return false;
        }
        foreach (var key in new[] { "FontFile", "FontFile2", "FontFile3" })
            descriptor.Remove(key);
        return true;
    }

    /// <summary>True when the font, or its descendant for a composite font, carries a program.</summary>
    private bool CarriesProgram(PdfDictionary font)
    {
        var host = font;
        if (font.GetName("Subtype") == "Type0"
            && _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants)
            host = _reader.ResolveDict(descendants[0]) ?? font;
        var descriptor = _reader.ResolveDict(host.Get("FontDescriptor"));
        return descriptor is not null
               && (descriptor.Get("FontFile") ?? descriptor.Get("FontFile2") ?? descriptor.Get("FontFile3")) is not null;
    }

    /// <summary>Set /BaseFont on the font, its descendant and their descriptors.</summary>
    private void SetBaseFontName(PdfDictionary font, string name)
    {
        var dicts = new List<PdfDictionary> { font };
        if (_reader.Resolve(font.Get("DescendantFonts")) is PdfArray descendants)
            foreach (var entry in descendants)
                if (_reader.ResolveDict(entry) is { } descendant) dicts.Add(descendant);
        foreach (var dict in dicts)
        {
            dict.Set("BaseFont", new PdfName(name));
            if (_reader.ResolveDict(dict.Get("FontDescriptor")) is { } descriptor && descriptor.ContainsKey("FontName"))
                descriptor.Set("FontName", new PdfName(name));
        }
    }
}
