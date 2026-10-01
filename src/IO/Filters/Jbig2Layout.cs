namespace Aspose.Pdf.IO.Filters;

/// <summary>One symbol instance of a text region: the symbol's index among those the region can
/// use, its box on the page, and - for an instance refined in place - the bitmap it was drawn with
/// (packed rows, 1 = black).</summary>
internal readonly record struct Jbig2Placement(int SymbolId, int X, int Y, int Width, int Height, byte[]? Refined = null);

/// <summary>What a text region placed, and how - enough to write it again with some placements
/// left out. <see cref="Reencodable"/> is false for a region whose instances cannot be written back
/// as they were (Huffman-coded, or refined per instance).</summary>
internal sealed class Jbig2TextRegionPlacements(int symbolCount, int regionCombination, int symbolCombination,
    bool defaultPixel)
{
    public int SymbolCount { get; } = symbolCount;
    public int RegionCombination { get; } = regionCombination;
    public int SymbolCombination { get; } = symbolCombination;
    public bool DefaultPixel { get; } = defaultPixel;
    public bool Reencodable { get; init; }
    public List<Jbig2Placement> Instances { get; } = new();
}

/// <summary>A generic region as decoded: its box, its combination operator and its bitmap (packed
/// rows, 1 = black).</summary>
internal sealed record Jbig2RegionBitmap(int X, int Y, int Width, int Height, int Combination, byte[] Bitmap);

/// <summary>A segment of a JBIG2 stream: where its header and data lie in the stream, and - for a
/// region segment - the region's box on the page.</summary>
internal readonly record struct Jbig2Segment(int Number, int Type, int HeaderStart, int DataStart, int DataLength,
    (int X, int Y, int Width, int Height)? Region);

/// <summary>A JBIG2 page taken apart: its segments, the placements of its text regions, and the page
/// it decodes to (packed rows, 1 = black).</summary>
internal sealed class Jbig2Layout
{
    public required byte[] Data { get; init; }
    public required List<Jbig2Segment> Segments { get; init; }
    public required Dictionary<int, Jbig2TextRegionPlacements> TextRegions { get; init; }
    public required Dictionary<int, Jbig2RegionBitmap> GenericRegions { get; init; }
    public required int PageWidth { get; init; }
    public required int PageHeight { get; init; }
    public required byte[] Page { get; init; }
}

internal static partial class Jbig2Decoder
{
    // Segment types that open with a region segment information field (T.88 §7.4.1): text,
    // pattern-based halftone, generic and refinement regions, intermediate or immediate.
    private static readonly HashSet<int> RegionTypes = [4, 6, 7, 20, 22, 23, 36, 38, 39, 40, 42, 43];

    /// <summary>Take a PDF JBIG2 stream apart (its globals decoded with it but not listed). Null
    /// when it does not decode to one known-size page.</summary>
    internal static Jbig2Layout? Analyze(byte[] data, byte[]? globals)
    {
        var combined = CombineGlobalsAndPage(globals, data);
        var offset = combined.Length - data.Length;
        var ctx = new DecodeContext(combined)
        {
            Captures = new Dictionary<int, Jbig2TextRegionPlacements>(),
            GenericCaptures = new Dictionary<int, Jbig2RegionBitmap>(),
        };
        byte[] page;
        try { page = ctx.DecodeAll(); }
        catch (System.Exception) { return null; }
        if (page.Length == 0 || ctx.PageWidth <= 0 || ctx.PageHeight <= 0) return null;

        static int Int(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
        var segments = new List<Jbig2Segment>();
        foreach (var header in ctx.Headers)
        {
            if (header.HeaderStart < offset) continue;
            (int, int, int, int)? region = null;
            if (RegionTypes.Contains(header.Type) && header.DataLength >= 17)
                region = (Int(combined, header.DataStart + 8), Int(combined, header.DataStart + 12),
                    Int(combined, header.DataStart), Int(combined, header.DataStart + 4));
            segments.Add(new Jbig2Segment(header.Number, header.Type, header.HeaderStart - offset,
                header.DataStart - offset, header.DataLength, region));
        }

        return new Jbig2Layout
        {
            Data = data,
            Segments = segments,
            TextRegions = ctx.Captures!,
            GenericRegions = ctx.GenericCaptures!,
            PageWidth = ctx.PageWidth,
            PageHeight = ctx.PageHeight,
            Page = page,
        };
    }

    private sealed partial class DecodeContext
    {
        internal IReadOnlyList<SegmentHeader> Headers => _headers;
        internal int PageWidth => _pageWidth;
        internal int PageHeight => _pageHeight;
    }
}
