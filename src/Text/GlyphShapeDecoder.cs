using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Last-resort decoder for symbolic embedded TrueType fonts that carry NO
/// Unicode semantics at all — no /Encoding, no /ToUnicode, a post table
/// without glyph names and only a (1,0)/(3,0)-PUA cmap (Ghostscript-style
/// subsets whose codes are sequential-by-first-use bytes). This decoder
/// recognises each glyph by its OUTLINE SHAPE,
/// matching the embedded outline against reference
/// shapes rasterised from an installed sans-serif font.
///
/// Gate (3-state machine, locked path): a code below 0x20 proves the
/// font is not character-coded and locks shape recognition on for the font.
/// Fonts that only ever show letter-plausible codes are left to the normal
/// decode chain (identity-like WinAnsi fallback).
/// </summary>
internal static class GlyphShapeDecoder
{
    private sealed class FontEntry
    {
        public bool Locked;
        public GlyphOutlineParser? Outlines;
        public readonly Dictionary<byte, string> CodeToText = new();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, FontEntry> _cache = new();

    public static string? TryDecode(byte[] bytes, PdfDictionary fontDict, PdfReader reader)
    {
        if (bytes.Length == 0) return null;
        var entry = GetEntry(fontDict, reader);
        if (entry?.Outlines is null) return null;
        lock (entry)
        {
            if (!entry.Locked)
            {
                foreach (var b in bytes)
                    if (b < 0x20) { entry.Locked = true; break; }
                if (!entry.Locked) return null;
            }
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                if (!entry.CodeToText.TryGetValue(b, out var s))
                {
                    s = ClassifyCode(entry.Outlines, b);
                    entry.CodeToText[b] = s;
                }
                sb.Append(s);
            }
            return sb.ToString();
        }
    }

    private static FontEntry? GetEntry(PdfDictionary fontDict, PdfReader reader)
    {
        if (_cache.TryGetValue(fontDict, out var cached)) return cached;
        var entry = new FontEntry();
        if (fontDict.GetName("Subtype") == "TrueType"
            && reader.ResolveDict(fontDict.Get("FontDescriptor")) is { } fd
            && reader.ResolveStream(fd.Get("FontFile2")) is { } ff2)
        {
            try
            {
                var parser = new GlyphOutlineParser(reader.DecodeStream(ff2));
                parser.MirrorSymbolPuaEntries();
                if (parser.CMap.Count > 0) entry.Outlines = parser;
            }
            catch { /* malformed font program: stay ineligible */ }
        }
        _cache.AddOrUpdate(fontDict, entry);
        return entry;
    }

    private static string ClassifyCode(GlyphOutlineParser outlines, byte code)
    {
        if (!outlines.CMap.TryGetValue(code, out var gid)
            && !outlines.CMap.TryGetValue(0xF000 + code, out gid))
            return ((char)code).ToString();
        var outline = outlines.GetOutline(gid);
        // A mapped glyph with no ink is the font's space.
        if (outline is null || outline.Contours.Length == 0) return " ";
        var ch = GlyphShapeClassifier.Classify(outline, outlines.UnitsPerEm);
        return ch?.ToString() ?? ((char)code).ToString();
    }
}

/// <summary>
/// Matches a TrueType glyph outline against reference character shapes.
/// Both sides are rasterised into a bounding-box-normalised occupancy grid;
/// the best candidate maximises grid agreement (IoU) minus penalties on the
/// em-relative metrics (height, width, descender) that the normalisation
/// removes — those metrics are what separate 'C' from 'c' and 'O' from 'o'.
/// </summary>
internal static class GlyphShapeClassifier
{
    private const int N = 24;              // occupancy grid resolution
    private const double MinScore = 0.35;  // below this, no candidate is credible

    private sealed class RefShape
    {
        public char Ch;
        public bool[] Grid = System.Array.Empty<bool>();    // bbox-stretched
        public bool[] EmGrid = System.Array.Empty<bool>();  // em-anchored (size/position preserved)
        public double RelH, RelW, RelBot;  // bbox height/width per em; descent below baseline per em
    }

    // Latin letters, digits and the punctuation/symbol repertoire these
    // subsets are observed to use, plus German umlaut/eszett extras.
    private const string Candidates =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
        ".,:;/()+-_@®&%!?'\"*=üäöÜÄÖß";

    // Shapes whose OUTPUT differs from the character actually drawn:
    // small filled bullet separators (disc or
    // diamond dingbats) classify as '®' (bate_map 0x3d→®) — register the
    // bullet shapes under that output so they decode accordingly.
    private static readonly (char Shape, char Output)[] AliasCandidates =
        { ('•', '®'), ('◆', '®'), ('●', '®') };

    // Several reference faces blunt single-typeface idiosyncrasies (a Verdana
    // '2' catches grotesque '2's that Arial's misses); best score wins overall.
    private static readonly string[] ReferenceFonts = { "Arial", "Verdana", "Tahoma", "Calibri", "Segoe UI" };
    private const int M = 16;              // em-anchored grid resolution

    private static List<RefShape>? _refs;
    private static readonly object _initLock = new();

    public static char? Classify(GlyphOutline outline, int unitsPerEm)
    {
        var refs = _refs;
        if (refs is null)
        {
            lock (_initLock)
            {
                refs = _refs ??= OperatingSystem.IsWindows()
                    ? BuildReferenceShapes()
                    : BuildReferenceShapesManaged();
            }
        }
        if (refs.Count == 0) return null;

        var grid = RasterizeOutline(outline, out var emGrid, out var relH, out var relW, out var relBot, unitsPerEm);
        if (grid is null) return null;

        RefShape? best = null;
        double bestScore = double.MinValue;
        var dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_SHAPEDEBUG") == "1"
            ? new List<(char ch, double score)>() : null;
        foreach (var r in refs)
        {
            // Bbox-stretched grid: pure shape, size-independent.
            var inter = 0; var union = 0;
            for (var i = 0; i < grid.Length; i++)
            {
                if (grid[i] && r.Grid[i]) inter++;
                if (grid[i] || r.Grid[i]) union++;
            }
            var iou = union == 0 ? 0 : (double)inter / union;
            // Em-anchored grid: absolute size and baseline position — the
            // discriminator for case pairs (C/c) and the I/l/1/t family.
            var einter = 0; var eunion = 0;
            for (var i = 0; i < emGrid.Length; i++)
            {
                if (emGrid[i] && r.EmGrid[i]) einter++;
                if (emGrid[i] || r.EmGrid[i]) eunion++;
            }
            var eiou = eunion == 0 ? 0 : (double)einter / eunion;
            var score = 0.55 * iou + 0.45 * eiou
                        - 0.8 * System.Math.Abs(relH - r.RelH)
                        - 0.3 * System.Math.Abs(relW - r.RelW)
                        - 0.8 * System.Math.Abs(relBot - r.RelBot);
            if (score > bestScore) { bestScore = score; best = r; }
            dbg?.Add((r.Ch, score));
        }
        if (dbg is not null)
        {
            dbg.Sort((a, b) => b.score.CompareTo(a.score));
            Console.Error.WriteLine($"[shape] relH={relH:F3} relW={relW:F3} relBot={relBot:F3} top: "
                + string.Join(" ", dbg.GetRange(0, System.Math.Min(5, dbg.Count)).ConvertAll(t => $"'{t.ch}'={t.score:F3}")));
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_SHAPEDEBUG_GRID") == "1")
                for (var row = 0; row < N; row++)
                {
                    var sbg = new StringBuilder(N);
                    for (var c = 0; c < N; c++) sbg.Append(grid[row * N + c] ? '#' : '.');
                    Console.Error.WriteLine(sbg.ToString());
                }
        }
        return bestScore >= MinScore ? best!.Ch : null;
    }

    // Em-anchored window shared by both sides: x spans one em centred on the
    // glyph bbox centre; y spans baseline−0.30 em … baseline+0.80 em (row 0 = top).
    private const double EmYTop = 0.80, EmYBot = 0.30;

    // ── Embedded-glyph rasterisation ─────────────────────────────────────

    /// <summary>Flatten the glyph's quadratic contours and scanline-fill them
    /// (even-odd) into the two comparison grids: an N×N grid stretched over the
    /// glyph bbox and an M×M em-anchored grid; row 0 = top in both.</summary>
    private static bool[]? RasterizeOutline(GlyphOutline outline, out bool[] emGrid,
        out double relH, out double relW, out double relBot, int unitsPerEm)
    {
        emGrid = System.Array.Empty<bool>();
        relH = relW = relBot = 0;
        var w = outline.XMax - outline.XMin;
        var h = outline.YMax - outline.YMin;
        if (w <= 0 || h <= 0 || unitsPerEm <= 0) return null;
        relH = h / unitsPerEm;
        relW = w / unitsPerEm;
        relBot = -outline.YMin / unitsPerEm; // descent below the baseline (positive down)

        var polys = new List<List<(double X, double Y)>>();
        foreach (var contour in outline.Contours)
        {
            var pts = ExpandContour(contour);
            if (pts.Count >= 3) polys.Add(pts);
        }
        if (polys.Count == 0) return null;

        List<double> Crossings(double y)
        {
            var xs = new List<double>();
            foreach (var poly in polys)
            {
                for (var i = 0; i < poly.Count; i++)
                {
                    var (x1, y1) = poly[i];
                    var (x2, y2) = poly[(i + 1) % poly.Count];
                    if (y1 == y2) continue;
                    if ((y1 <= y && y2 > y) || (y2 <= y && y1 > y))
                        xs.Add(x1 + (y - y1) / (y2 - y1) * (x2 - x1));
                }
            }
            xs.Sort();
            return xs;
        }

        static void FillRow(bool[] g, int cells, int row, List<double> xs, double x0, double span)
        {
            for (var k = 0; k + 1 < xs.Count; k += 2)
            {
                var c0 = (int)System.Math.Ceiling((xs[k] - x0) / span * cells - 0.5);
                var c1 = (int)System.Math.Floor((xs[k + 1] - x0) / span * cells - 0.5);
                for (var c = System.Math.Max(0, c0); c <= System.Math.Min(cells - 1, c1); c++)
                    g[row * cells + c] = true;
            }
        }

        var grid = new bool[N * N];
        for (var row = 0; row < N; row++)
        {
            // Grid row 0 is the TOP of the bbox (matches the bitmap-side raster).
            var y = outline.YMax - (row + 0.5) * h / N;
            FillRow(grid, N, row, Crossings(y), outline.XMin, w);
        }

        emGrid = new bool[M * M];
        var em = (double)unitsPerEm;
        var exSpan = em;
        var ex0 = (outline.XMin + outline.XMax) / 2 - em / 2;
        for (var row = 0; row < M; row++)
        {
            var y = EmYTop * em - (row + 0.5) * (EmYTop + EmYBot) * em / M; // baseline-relative
            FillRow(emGrid, M, row, Crossings(y), ex0, exSpan);
        }
        return grid;
    }

    /// <summary>TrueType contour → polyline: consecutive off-curve points imply an
    /// on-curve midpoint; each quadratic segment is subdivided into 8 chords.</summary>
    private static List<(double X, double Y)> ExpandContour(ContourPoint[] contour)
    {
        var result = new List<(double, double)>();
        if (contour.Length == 0) return result;

        // Normalise so the sequence starts on-curve.
        var pts = new List<ContourPoint>(contour);
        if (!pts[0].OnCurve)
        {
            var last = pts[^1];
            if (last.OnCurve) { pts.Insert(0, last); pts.RemoveAt(pts.Count - 1); }
            else pts.Insert(0, new ContourPoint((pts[0].X + last.X) / 2, (pts[0].Y + last.Y) / 2, true));
        }

        var n = pts.Count;
        var i = 0;
        while (i < n)
        {
            var cur = pts[i];
            var next = pts[(i + 1) % n];
            if (next.OnCurve)
            {
                result.Add((cur.X, cur.Y));
                i++;
                continue;
            }
            // cur (on) → next (off) → after: quadratic; synthesise the implied
            // on-curve point when two off-curve points are adjacent.
            var after = pts[(i + 2) % n];
            var end = after.OnCurve
                ? after
                : new ContourPoint((next.X + after.X) / 2, (next.Y + after.Y) / 2, true);
            result.Add((cur.X, cur.Y));
            for (var t = 1; t < 8; t++)
            {
                var tt = t / 8.0;
                var mt = 1 - tt;
                result.Add((mt * mt * cur.X + 2 * mt * tt * next.X + tt * tt * end.X,
                            mt * mt * cur.Y + 2 * mt * tt * next.Y + tt * tt * end.Y));
            }
            if (after.OnCurve) i += 2;
            else { pts[(i + 1) % n] = end; i++; }
        }
        return result;
    }

    /// <summary>
    /// The template shapes built from the font FILES instead of through GDI+, so glyph
    /// recognition works away from Windows. Each candidate's outline goes through the very
    /// same <see cref="RasterizeOutline"/> the subject glyph uses, which is a better
    /// comparison than the GDI+ path gets: both sides are then filled by one rasteriser,
    /// with identical grids and identical relative measures.
    /// </summary>
    private static List<RefShape> BuildReferenceShapesManaged()
    {
        var refs = new List<RefShape>();
        foreach (var fontName in ReferenceFonts)
        {
            try
            {
                var bytes = SystemFontResolver.Resolve(fontName);
                if (bytes is null || bytes.Length == 0) continue;
                var parser = new GlyphOutlineParser(bytes);
                if (parser.CMap.Count == 0) continue;

                var pairs = new List<(char Shape, char Output)>();
                foreach (var c in Candidates) pairs.Add((c, c));
                pairs.AddRange(AliasCandidates);
                foreach (var (shapeCh, ch) in pairs)
                {
                    if (!parser.CMap.TryGetValue(shapeCh, out var gid)) continue;
                    if (parser.GetOutline(gid) is not { } outline) continue;
                    var grid = RasterizeOutline(outline, out var emGrid,
                        out var relH, out var relW, out var relBot, parser.UnitsPerEm);
                    if (grid is null) continue;
                    refs.Add(new RefShape
                    {
                        Ch = ch,
                        Grid = grid,
                        EmGrid = emGrid,
                        RelH = relH,
                        RelW = relW,
                        RelBot = relBot,
                    });
                }
            }
            catch
            {
                // Face not installed or unreadable: skip this family.
            }
        }
        return refs;
    }

    // ── Reference-shape construction (GDI+, Windows only) ────────────────

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<RefShape> BuildReferenceShapes()
    {
        var refs = new List<RefShape>();
        foreach (var fontName in ReferenceFonts)
        {
            try
            {
                using var family = new System.Drawing.FontFamily(fontName);
                const float em = 1000f;
                var ascentEm = em * family.GetCellAscent(System.Drawing.FontStyle.Regular)
                                  / family.GetEmHeight(System.Drawing.FontStyle.Regular);
                using var bmp = new System.Drawing.Bitmap(N, N);
                using var embmp = new System.Drawing.Bitmap(M, M);
                var pairs = new List<(char Shape, char Output)>();
                foreach (var c in Candidates) pairs.Add((c, c));
                pairs.AddRange(AliasCandidates);
                foreach (var (shapeCh, ch) in pairs)
                {
                    using var path = new System.Drawing.Drawing2D.GraphicsPath();
                    path.AddString(shapeCh.ToString(), family, (int)System.Drawing.FontStyle.Regular, em,
                        new System.Drawing.PointF(0, 0), System.Drawing.StringFormat.GenericTypographic);
                    if (path.PointCount == 0) continue;
                    var b = path.GetBounds();
                    if (b.Width <= 0 || b.Height <= 0) continue;

                    // Bbox-stretched grid (AddString y grows down, so bitmap
                    // row 0 is already the glyph top).
                    var grid = FillToGrid(bmp, path, N,
                        N / b.Width, N / b.Height, -b.X * N / b.Width, -b.Y * N / b.Height);

                    // Em-anchored grid: x = one em centred on the bbox centre,
                    // y = baseline−EmYBot…baseline+EmYTop (baseline sits
                    // ascentEm below the AddString origin, y grows down).
                    var ex0 = b.X + b.Width / 2 - em / 2;
                    var ey0 = ascentEm - EmYTop * em;                 // window top (device y)
                    var eySpan = (EmYTop + EmYBot) * em;
                    var emGrid = FillToGrid(embmp, path, M,
                        M / em, M / eySpan, -ex0 * M / em, -ey0 * M / eySpan);

                    var bottom = b.Y + b.Height;                      // lowest ink (device y)
                    refs.Add(new RefShape
                    {
                        Ch = ch,
                        Grid = grid,
                        EmGrid = emGrid,
                        RelH = b.Height / em,
                        RelW = b.Width / em,
                        RelBot = (bottom - ascentEm) / em,            // >0 = descends below baseline
                    });
                }
            }
            catch
            {
                // Face not installed / GDI+ unavailable: skip this family.
            }
        }
        return refs;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool[] FillToGrid(System.Drawing.Bitmap bmp, System.Drawing.Drawing2D.GraphicsPath path,
        int cells, double sx, double sy, double tx, double ty)
    {
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.Transform = new System.Drawing.Drawing2D.Matrix((float)sx, 0, 0, (float)sy, (float)tx, (float)ty);
        g.FillPath(System.Drawing.Brushes.Black, path);
        var grid = new bool[cells * cells];
        for (var y = 0; y < cells; y++)
            for (var x = 0; x < cells; x++)
                grid[y * cells + x] = bmp.GetPixel(x, y).R < 128;
        return grid;
    }
}
