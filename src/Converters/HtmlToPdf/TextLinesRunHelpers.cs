using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The emphasis writer's run helpers: whether a position lies in a face run, and the colour run covering it.
    // Mixed-emphasis line in a real face: consecutive
    // embedded-face segments (regular / Bold / Italic
    // variants of the block family), the text position
    // advancing naturally between them. The runs carry
    // the emphasis truth even when a leading <b>
    // promoted the whole block's FontRes.
    private static bool InFaceRuns(BlockTextState bt, System.Collections.Generic.List<(int Start, int Length)>? runs, int p)
    {
        var inside = false;
        if (runs is not null)
            foreach (var (rs, rl) in runs)
            {
                var re = rs + rl;
                if (p >= rs && p < re) { inside = true; bt.fSegEnd = Math.Min(bt.fSegEnd, re); }
                else if (rs > p) bt.fSegEnd = Math.Min(bt.fSegEnd, rs);
            }
        return inside;
    }

    // Span-scoped colour runs: the run's ink applies to
    // its own segments only (the saved email's red bold
    // phrases on a black line).
    private static Color? ColorInRuns(BlockTextState bt, int p2)
    {
        Color? found = null;
        if (bt.block.ColorRuns is not null)
            foreach (var (rs, rl, rc) in bt.block.ColorRuns)
            {
                var re = rs + rl;
                if (p2 >= rs && p2 < re) { found = rc; bt.fSegEnd = Math.Min(bt.fSegEnd, re); }
                else if (rs > p2) bt.fSegEnd = Math.Min(bt.fSegEnd, rs);
            }
        return found;
    }

    /// <summary>The size run covering the position (0 = the block's own size); a run edge caps the segment.</summary>
    private static double SizeInRuns(BlockTextState bt, int p)
    {
        double found = 0;
        if (bt.block.SizeRuns is not null)
            foreach (var (rs, rl, rp) in bt.block.SizeRuns)
            {
                var re = rs + rl;
                if (p >= rs && p < re) { found = rp; bt.fSegEnd = Math.Min(bt.fSegEnd, re); }
                else if (rs > p) bt.fSegEnd = Math.Min(bt.fSegEnd, rs);
            }
        return found;
    }

    /// <summary>The face run covering the position (null = the block's own face); a run edge caps the segment.</summary>
    private static string? FamilyInRuns(BlockTextState bt, int p)
    {
        string? found = null;
        if (bt.block.FamilyRuns is not null)
            foreach (var (rs, rl, rf) in bt.block.FamilyRuns)
            {
                var re = rs + rl;
                if (p >= rs && p < re) { found = rf; bt.fSegEnd = Math.Min(bt.fSegEnd, re); }
                else if (rs > p) bt.fSegEnd = Math.Min(bt.fSegEnd, rs);
            }
        return found;
    }

    /// <summary>Each character's size in points: the block's, or the size run covering it.</summary>
    private static double[] CharSizes(Block block, double basePt)
    {
        var sizes = new double[block.Text.Length];
        for (var i = 0; i < sizes.Length; i++) sizes[i] = basePt;
        if (block.SizeRuns is not null)
            foreach (var (rs, rl, rp) in block.SizeRuns)
                for (var i = Math.Max(0, rs); i < Math.Min(sizes.Length, rs + rl); i++) sizes[i] = rp;
        return sizes;
    }

    /// <summary>The CSS line box of one run size: the face's ascent and descent at that size with
    /// the line-height's half-leading on each side - the sheet's factor of the size, the block's
    /// absolute box, else the flow's pitch scaled to the size (probed: a 24 pt run under a 1.6
    /// line-height seats 36.1 below an 18 pt line and 21.1 above a 9.75 pt one).</summary>
    private static (double above, double below) RunLineExtent(Block block, string face, double pt, double blockPt, double blockLineHeight)
    {
        var lh = block.RunNormalLines ? NormalLineOf(face, pt)
            : block.LineBoxPt > 0 ? block.LineBoxPt
            : block.SheetLineFactor > 0 ? block.SheetLineFactor * pt
            : blockLineHeight * (blockPt > 0 ? pt / blockPt : 1);
        var above = LineBoxAbove(face, pt, lh);
        return (above, lh - above);
    }

    /// <summary>A line's box over its runs: the largest ascent-side and descent-side extents.</summary>
    private static (double above, double below) LineExtent(Block block, string face, double[] sizes, int from, int to, double blockPt, double blockLineHeight)
    {
        // (a quirks-mode line whose ink stands wholly inside sized inline runs has no strut: the
        // line is the runs' own box - measured on the enterprise summary: 15 px title spans in a
        // 12 pt body pitch 12.75, not the body's 13.5)
        var strutless = _quirksRowStrut && _uaStdSerifFlow && LineWhollyInSizeRuns(block, from, to);
        var (above, below) = strutless ? (0.0, 0.0) : RunLineExtent(block, face, blockPt, blockPt, blockLineHeight);
        var seen = new HashSet<(string, double)> { (face, blockPt) };
        if (strutless) seen.Clear();
        for (var i = Math.Max(0, from); i < Math.Min(to, sizes.Length); i++)
        {
            var runFace = FaceAt(block, face, i);
            if (!seen.Add((runFace, sizes[i]))) continue;
            var (a, b) = RunLineExtent(block, runFace, sizes[i], blockPt, blockLineHeight);
            if (a > above) above = a;
            if (b > below) below = b;
        }
        return (above, below);
    }

    /// <summary>Every non-blank character of text[from..to) lies inside one of the block's size runs.</summary>
    private static bool LineWhollyInSizeRuns(Block block, int from, int to)
    {
        if (block.SizeRuns is null || to <= from) return false;
        var any = false;
        for (var i = Math.Max(0, from); i < Math.Min(to, block.Text.Length); i++)
        {
            if (char.IsWhiteSpace(block.Text[i])) continue;
            var inRun = false;
            foreach (var (rs, rl, _) in block.SizeRuns) if (i >= rs && i < rs + rl) { inRun = true; break; }
            if (!inRun) return false;
            any = true;
        }
        return any;
    }

    /// <summary>The line boxes of a size-run block's wrapped lines (the lines partition the text on single spaces).</summary>
    private static (double above, double below)[] LineExtents(Block block, string face, string[] lines, double blockPt, double blockLineHeight)
    {
        var sizes = CharSizes(block, blockPt);
        var ext = new (double above, double below)[lines.Length];
        var pos = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            ext[i] = LineExtent(block, face, sizes, pos, pos + lines[i].Length, blockPt, blockLineHeight);
            pos += lines[i].Length + 1;
        }
        return ext;
    }

    /// <summary>The advance of text[from..to) measured piecewise at each character's size.</summary>
    private static double RunsWidth(string face, string text, double[] sizes, int from, int to)
    {
        double w = 0;
        var i = from;
        while (i < to)
        {
            var j = i + 1;
            while (j < to && sizes[j] == sizes[i]) j++;
            w += MeasureFaceText(face, text[i..j], sizes[i]);
            i = j;
        }
        return w;
    }

    /// <summary>Wraps a size-run block on its runs' real advances: a line's box is its largest
    /// run's, a line whose box is level with the left float wraps at the narrow width, and the
    /// lines whose box clears the float take the full width.</summary>
    private static string[] SizeRunWordWrapPastFloat(Block block, string face, double blockPt, double blockLineHeight,
        double narrowWidth, double fullWidth, double firstBaselineY, double floatBottomY, bool besideFloat)
    {
        var text = block.Text;
        if (string.IsNullOrEmpty(text)) return [""];
        var sizes = CharSizes(block, blockPt);
        var plainAbove = RunLineExtent(block, face, blockPt, blockPt, blockLineHeight).above;
        var result = new List<string>();
        var line = new StringBuilder();
        double lineW = 0, baseline = firstBaselineY, prevBelow = 0;
        var lineStart = 0;
        var pos = 0;
        while (pos < text.Length)
        {
            var ws = pos;
            while (ws < text.Length && text[ws] == ' ') ws++;
            if (ws >= text.Length) break;
            var we = ws;
            while (we < text.Length && text[we] != ' ') we++;
            var w = RunsWidth(face, text, sizes, ws, we);
            var spaceW = line.Length > 0 ? RunsWidth(face, text, sizes, ws - 1, ws) : 0;
            // The box this line would have with the word on it decides whether it is level with the float.
            var (above, _) = LineExtent(block, face, sizes, lineStart, we, blockPt, blockLineHeight);
            var lineBase = result.Count == 0 ? firstBaselineY - (above - plainAbove) : baseline - (prevBelow + above);
            var limit = besideFloat && lineBase + above > floatBottomY + 1e-9 ? narrowWidth : fullWidth;
            if (line.Length > 0 && lineW + spaceW + w > limit)
            {
                result.Add(line.ToString());
                var (closedAbove, closedBelow) = LineExtent(block, face, sizes, lineStart, ws - 1, blockPt, blockLineHeight);
                baseline = result.Count == 1 ? firstBaselineY - (closedAbove - plainAbove) : baseline - (prevBelow + closedAbove);
                prevBelow = closedBelow;
                line.Clear(); lineW = 0; lineStart = ws;
            }
            if (line.Length > 0) { line.Append(' '); lineW += spaceW; }
            line.Append(text, ws, we - ws); lineW += w;
            pos = we;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    /// <summary>The ascent side of a CSS line box: the face's ascent at the size plus the half-leading.</summary>
    private static double LineBoxAbove(string face, double pt, double lineHeightPt)
    {
        var (asc, sum) = WinMetricsFor(face) ?? (0.8, 1.0);
        return asc * pt + (lineHeightPt - sum * pt) / 2;
    }

    /// <summary>A run's glyph string as a `[...] TJ` array carrying the face's pair kerning
    /// between adjacent glyphs (probed: the TTF kern table's unrounded units scaled to 1/1000
    /// em, written as the negated value between the two glyph strings; unkerned pairs stay in
    /// one string). A face without kerning, or a run of one glyph, is the plain string.</summary>
    private static string KernedTj(Text.GlyphOutlineParser? parser, double upm, byte[] hexGlyphIds)
    {
        var n = hexGlyphIds.Length / 2;
        if (parser is null || n < 2 || upm <= 0)
            return "<" + Compat.ToHexString(hexGlyphIds) + "> Tj ";
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder("[<");
        var prev = (hexGlyphIds[0] << 8) | hexGlyphIds[1];
        sb.Append(hexGlyphIds[0].ToString("X2")).Append(hexGlyphIds[1].ToString("X2"));
        for (var i = 1; i < n; i++)
        {
            var gid = (hexGlyphIds[i * 2] << 8) | hexGlyphIds[i * 2 + 1];
            var kern = parser.GetKernAdjustment(prev, gid);
            if (kern != 0)
                sb.Append("> ").Append((-kern * 1000.0 / upm).ToString("0.####", ic)).Append(" <");
            sb.Append(hexGlyphIds[i * 2].ToString("X2")).Append(hexGlyphIds[i * 2 + 1].ToString("X2"));
            prev = gid;
        }
        sb.Append(">] TJ ");
        return sb.ToString();
    }

    // The glyph parser of a font program the run writers hold by its bytes (the repository
    // hands out one array per face, so the reference is the key).
    private static readonly Dictionary<byte[], (Text.GlyphOutlineParser? parser, double upm)> _kernParserByTtf = new();

    /// <summary>A run's glyph string as a kerned `TJ` array for the face whose program these bytes are.</summary>
    private static string KernedTj(byte[] ttf, byte[] hexGlyphIds)
    {
        if (!_kernParserByTtf.TryGetValue(ttf, out var e))
        {
            try
            {
                var parser = new Text.GlyphOutlineParser(ttf);
                e = (parser, parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000);
            }
            catch { e = (null, 1000); }
            lock (_kernParserByTtf) _kernParserByTtf[ttf] = e;
        }
        return KernedTj(e.parser, e.upm, hexGlyphIds);
    }
}
