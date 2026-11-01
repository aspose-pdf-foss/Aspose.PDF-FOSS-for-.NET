using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tagged;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    // Form XObjects drawn inside forms deeper than this are not looked into (a cycle guard).
    private const int MaxDrawnFormDepth = 10;

    /// <summary>
    /// The largest size, in points, each of <paramref name="candidates"/> is drawn at on the
    /// document's pages: their content, the forms it draws, their tiling patterns and their
    /// annotation appearances. An image's resolution is its pixels over the size it is shown at,
    /// and its largest placement needs the most of them. Only what can reach a candidate is read:
    /// a page, form, pattern or appearance whose resources name none is not decoded. The walk
    /// follows q, Q, cm and Do over the content lexer alone - the content parser behind
    /// <see cref="ImagePlacementAbsorber"/> also carries the text, path and colour state, and costs
    /// more than this whole pass on a drawing of thousands of placements.
    /// </summary>
    private static Dictionary<PdfStream, (double Width, double Height)> DrawnSizes(PdfReader reader,
        HashSet<PdfStream> candidates)
    {
        var walk = new DrawnWalk(reader, candidates);
        if (candidates.Count == 0) return walk.Sizes;
        foreach (var page in PageDicts(reader))
        {
            try
            {
                var resources = PageContentScan.InheritedResources(reader, page);
                if (walk.Reaches(resources))
                {
                    foreach (var content in ContentStreams(reader, page))
                        walk.Walk(content, resources, Identity(), 0);
                    WalkTilingPatterns(walk, resources);
                }
                WalkAppearances(walk, page);
            }
            catch
            {
                // A page that does not read places no more images; theirs keep the sizes found so far.
            }
        }
        return walk.Sizes;
    }

    private static double[] Identity() => [1, 0, 0, 1, 0, 0];

    /// <summary>The page's content streams, decoded, in order (one transformation state runs through them).</summary>
    private static IEnumerable<byte[]> ContentStreams(PdfReader reader, PdfDictionary page)
    {
        var contents = reader.Resolve(page.Get("Contents"));
        var streams = contents switch
        {
            PdfStream single => [single],
            PdfArray parts => parts.Select(reader.ResolveStream).OfType<PdfStream>().ToList(),
            _ => new List<PdfStream>(),
        };
        foreach (var stream in streams)
        {
            if (SafeDecode(stream, reader) is { } bytes) yield return bytes;
        }
    }

    /// <summary>A page's tiling patterns paint their content under their own matrix.</summary>
    private static void WalkTilingPatterns(DrawnWalk walk, PdfDictionary? resources)
    {
        var reader = walk.Reader;
        if (resources is null || reader.ResolveDict(resources.Get("Pattern")) is not { } patterns) return;
        foreach (var key in patterns.Keys)
        {
            if (reader.ResolveStream(patterns.Get(key)) is not { } pattern || pattern.Dict.GetInt("PatternType", 1) != 1) continue;
            var patternResources = reader.ResolveDict(pattern.Dict.Get("Resources"));
            if (walk.Reaches(patternResources) && SafeDecode(pattern, reader) is { } bytes)
                walk.Walk(bytes, patternResources, PageContentScan.FormMatrix(reader, pattern), 1);
        }
    }

    /// <summary>An annotation's normal appearance paints on the page, mapped onto its /Rect.</summary>
    private static void WalkAppearances(DrawnWalk walk, PdfDictionary page)
    {
        var reader = walk.Reader;
        if (reader.Resolve(page.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            if (reader.ResolveDict(item) is not { } annot || reader.ResolveDict(annot.Get("AP")) is not { } ap) continue;
            var normal = reader.Resolve(ap.Get("N"));
            if (normal is PdfDictionary states)
                normal = annot.GetName("AS") is { } state ? reader.Resolve(states.Get(state)) : null;
            if (normal is not PdfStream appearance) continue;
            var appearanceResources = reader.ResolveDict(appearance.Dict.Get("Resources"));
            if (walk.Reaches(appearanceResources) && SafeDecode(appearance, reader) is { } bytes)
                walk.Walk(bytes, appearanceResources, ImagePlacementAbsorber.AppearanceCtm(appearance, annot, reader), 1);
        }
    }

    /// <summary>The walk's state across the document: what has been found, and which forms can
    /// lead to a candidate image.</summary>
    private sealed class DrawnWalk(PdfReader reader, HashSet<PdfStream> candidates)
    {
        public PdfReader Reader { get; } = reader;
        public Dictionary<PdfStream, (double Width, double Height)> Sizes { get; } = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PdfStream, bool> _formReaches = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether drawing with these resources can draw a candidate image, directly or through forms.</summary>
        public bool Reaches(PdfDictionary? resources)
        {
            if (resources is null || Reader.ResolveDict(resources.Get("XObject")) is not { } xobjects) return false;
            foreach (var key in xobjects.Keys)
            {
                if (Reader.ResolveStream(xobjects.Get(key)) is not { } xobject) continue;
                if (candidates.Contains(xobject) || (xobject.Dict.GetName("Subtype") == "Form" && FormReaches(xobject, resources)))
                    return true;
            }
            return false;
        }

        private bool FormReaches(PdfStream form, PdfDictionary parentResources)
        {
            // A form without resources of its own draws with its parent's, which reach a candidate
            // only through this same dictionary: nothing more to find there.
            if (Reader.ResolveDict(form.Dict.Get("Resources")) is not { } own || ReferenceEquals(own, parentResources)) return false;
            if (_formReaches.TryGetValue(form, out var known)) return known;
            _formReaches[form] = false; // a form drawing itself reaches nothing new
            return _formReaches[form] = Reaches(own);
        }

        /// <summary>Follow a content stream's q, Q, cm and Do from the given transformation, recording each
        /// candidate image's drawn sides and looking into the forms that can reach one.</summary>
        public void Walk(byte[] content, PdfDictionary? resources, double[] ctm, int depth)
        {
            var xobjects = resources is null ? null : Reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is null || depth > MaxDrawnFormDepth) return;
            var lexer = new PdfLexer(content);
            var saved = new Stack<double[]>();
            var operands = new double[6]; // the last six numbers, oldest first
            var numbers = 0;
            string? name = null;
            while (true)
            {
                var token = lexer.NextToken();
                switch (token.Kind)
                {
                    case TokenKind.Eof:
                        return;
                    case TokenKind.Integer or TokenKind.Real:
                        Array.Copy(operands, 1, operands, 0, operands.Length - 1);
                        operands[^1] = token.Kind == TokenKind.Integer ? token.IntValue : token.RealValue;
                        numbers++;
                        continue;
                    case TokenKind.Name:
                        name = token.StringValue;
                        continue;
                    case TokenKind.Keyword:
                        switch (token.StringValue)
                        {
                            case "q": saved.Push(ctm); break;
                            case "Q": if (saved.Count > 0) ctm = saved.Pop(); break;
                            case "cm" when numbers >= operands.Length:
                                ctm = GraphicsState.MultiplyMatrices((double[])operands.Clone(), ctm);
                                break;
                            case "Do" when name is not null:
                                Draw(xobjects, name, resources!, ctm, depth);
                                break;
                            case "BI":
                                SkipInlineImage(lexer);
                                break;
                        }
                        break;
                }
                numbers = 0;
                name = null;
            }
        }

        private void Draw(PdfDictionary xobjects, string name, PdfDictionary resources, double[] ctm, int depth)
        {
            if (Reader.ResolveStream(xobjects.Get(name)) is not { } xobject) return;
            if (candidates.Contains(xobject))
            {
                // The image's unit square spans the CTM's first two rows: their lengths are its drawn sides.
                var width = Math.Sqrt(ctm[0] * ctm[0] + ctm[1] * ctm[1]);
                var height = Math.Sqrt(ctm[2] * ctm[2] + ctm[3] * ctm[3]);
                Sizes[xobject] = Sizes.TryGetValue(xobject, out var seen)
                    ? (Math.Max(seen.Width, width), Math.Max(seen.Height, height))
                    : (width, height);
                return;
            }
            if (xobject.Dict.GetName("Subtype") != "Form") return;
            var own = Reader.ResolveDict(xobject.Dict.Get("Resources"));
            if (own is not null && !ReferenceEquals(own, resources) && !FormReaches(xobject, resources)) return;
            if (SafeDecode(xobject, Reader) is not { } bytes) return;
            Walk(bytes, own ?? resources, GraphicsState.MultiplyMatrices(PageContentScan.FormMatrix(Reader, xobject), ctm), depth + 1);
        }

        /// <summary>An inline image's parameters and data, skipped to the end of its EI.</summary>
        private static void SkipInlineImage(PdfLexer lexer)
        {
            while (true)
            {
                var token = lexer.NextToken();
                if (token.Kind == TokenKind.Eof) return;
                if (token.Kind == TokenKind.Keyword && token.StringValue == "ID") break;
            }
            lexer.ReadInlineImageData();
        }
    }
}
