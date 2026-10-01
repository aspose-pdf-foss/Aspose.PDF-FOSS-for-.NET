using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    private static void ExtractRuns(byte[] streamBytes, PdfDictionary resourceDict,
        PdfReader reader, List<RawTextRun> result, int depth = 0,
        Matrix? inheritedCtm = null, List<RawFillRect>? fillRects = null,
        bool useFontEngineEncoding = false, bool keepAllFillRects = false,
        List<RawCoverRect>? coverRects = null,
        (double Llx, double Lly, double Urx, double Ury)? inheritedClip = null,
        bool strictFonts = false,
        HashSet<object>? seenForms = null,
        List<string>? missingFontKeys = null)
    {
        if (depth > 10) return; // prevent infinite recursion
        var xr = new ExtractRunsState();
        xr.streamBytes = streamBytes;
        xr.resourceDict = resourceDict;
        xr.reader = reader;
        xr.result = result;
        xr.depth = depth;
        xr.inheritedCtm = inheritedCtm;
        xr.fillRects = fillRects;
        xr.useFontEngineEncoding = useFontEngineEncoding;
        xr.keepAllFillRects = keepAllFillRects;
        xr.coverRects = coverRects;
        xr.inheritedClip = inheritedClip;
        xr.strictFonts = strictFonts;
        xr.seenForms = seenForms;
        xr.missingFontKeys = missingFontKeys;
        xr.fonts = ResolveFonts(xr.resourceDict, xr.reader);
        xr.t3Names = BuildType3SynthesizedNames(xr.resourceDict, xr.reader);
        xr.lexer = new PdfLexer(xr.streamBytes);
        xr.operands = new List<PdfObject>();
        xr.currentFontName = null;
        xr.currentFontNameForGuard = null;
        OpenExtractRunsState(xr);
        while (true)
        {
            var token = xr.lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    xr.operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    xr.operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    xr.operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    xr.operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    xr.operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                {
                    var arr = ParseArray(xr.lexer);
                    xr.operands.Add(arr);
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "q": case "Q": case "cm": case "w": case "Do":
                            ExtractRunsStateOperator(xr, op);
                            break;
                        case "g": case "rg": case "k": case "sc": case "scn": case "G": case "RG": case "K": case "SC": case "SCN":
                            ExtractRunsColorOperator(xr, op);
                            break;
                        case "re": case "m": case "l": case "c": case "v": case "y": case "h": case "W": case "W*": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "S": case "s": case "n":
                            ExtractRunsPathOperator(xr, op);
                            break;
                        case "BT": case "Tf": case "TL": case "Tr": case "Tc": case "Tw": case "Tz": case "Ts": case "Td": case "TD": case "Tm": case "T*":
                            ExtractRunsTextStateOperator(xr, op);
                            break;
                        case "Tj": case "TJ": case "'": case "\"":
                            ExtractRunsTextShowOperator(xr, op);
                            break;
                        // ── Inline image — skip binary data ──
                        case "BI":
                            SkipInlineImage(xr.lexer);
                            xr.operands.Clear();
                            continue;

                    }
                    xr.operands.Clear();
                    break;
                }
                default:
                    xr.operands.Clear();
                    break;
            }
        }
    }

    /// <summary>Per-character cumulative advances for a show string (n+1 entries),
    /// from byte-level metrics — the same construction the Tj path uses inline; the
    /// ' and \" show operators share it so substring matches inside their runs
    /// measure with real glyph widths instead of a uniform split.</summary>
    private static double[]? BuildCumWidthsForString(byte[] bytes, string text,
        FontMetrics? metrics, double fontSize, double charSpacing, double wordSpacing,
        double unscaledWidth)
    {
        if (metrics is null || text.Length == 0) return null;
        double[]? cum = null;
        if (text.Length == bytes.Length)
        {
            cum = new double[text.Length + 1];
            double w = 0;
            for (var ci = 0; ci < bytes.Length; ci++)
            {
                cum[ci] = w;
                w += metrics.MeasureStringExact(bytes[ci..(ci + 1)], fontSize)
                    + charSpacing + (text[ci] == ' ' ? wordSpacing : 0);
            }
            cum[text.Length] = w;
        }
        else if (bytes.Length == text.Length * 2)
        {
            cum = new double[text.Length + 1];
            double w = 0;
            for (var ci = 0; ci < text.Length; ci++)
            {
                cum[ci] = w;
                w += metrics.MeasureStringExact(bytes[(ci * 2)..(ci * 2 + 2)], fontSize)
                    + charSpacing + (text[ci] == ' ' ? wordSpacing : 0);
            }
            cum[text.Length] = w;
        }
        else
        {
            cum = new double[text.Length + 1];
            for (var ci = 0; ci <= text.Length; ci++)
                cum[ci] = unscaledWidth * ci / text.Length;
        }
        NormalizeDegenerateCumWidths(cum);
        return cum;
    }

    private static string DecodeBytes(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
    {
        // Delegate to TextAbsorber for consistent encoding handling. Fragments keep
        // U+00A0 verbatim (it is folded only in plain-text extraction).
        return TextAbsorber.DecodeStringPublic(bytes, toUnicode, fontDict, reader, useFontEngineEncoding,
            foldNbsp: false);
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream)
            result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    /// <summary>Skip inline image data (BI . ID &lt;data&gt; EI) per PDF spec §8.9.7.</summary>
    private static void SkipInlineImage(PdfLexer lexer) => TextAbsorber.SkipInlineImage(lexer);

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double GetNum(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    // A gray operator's colour surfaces on TextState as an RGB-marked value —
    // extracted colours compare equal to Color.FromRgb regardless of whether the
    // stream painted them with g/G or rg/RG (Color.FromGray carries the DeviceGray
    // marker, which would break that equality).
    private static Color GrayAsRgb(double g) => Color.FromRgb(g, g, g);

    /// <summary>
    /// A filled rectangle painted by the content stream — collected when
    /// <see cref="TextSearchOptions.SearchForTextRelatedGraphics"/> is enabled
    /// so that a fragment whose origin falls inside one of these rects can be
    /// reported with the rect's fill color as <c>TextState.BackgroundColor</c>.
    /// Coordinates are in the same absorber-space as <see cref="RawTextRun"/>
    /// (post-CTM, post-page-rotation).
    /// </summary>
    // RawX/Y/W/H carry the untransformed `re` operands so a save-time removal pass can
    // match and splice the exact rectangle operator out of the page content stream.
    private readonly record struct RawFillRect(double Llx, double Lly, double Urx, double Ury, Color FillColor,
        double RawX = 0, double RawY = 0, double RawW = 0, double RawH = 0);

    /// <summary>Spatial index over a page's fill rects keyed on each rect's vertical
    /// midpoint. The per-run background/underline/strikeout probes only care about rects
    /// near the run's baseline, so this lets them test a small Y-band slice instead of
    /// scanning the whole list — a vector-art page can carry hundreds of thousands of thin
    /// strokes, turning the old linear scans into an O(runs × rects) blow-up.
    ///
    /// Each probe originally reverse-scanned the list and returned the first hit, i.e. the
    /// matching rect with the greatest original index (top of the paint order). <see
    /// cref="FindTopMatch"/> reproduces that exactly by returning the max-index match among
    /// the candidates, so the result is order-independent and identical to the full scan as
    /// long as the candidate set covers every possible match. Rects taller than
    /// <see cref="TallCut"/> (whose midpoint can sit far from a baseline they still straddle)
    /// are always tested, so they are never missed by the Y-band slice.</summary>
    private sealed class FillRectIndex
    {
        private const double TallCut = 6.0;
        private readonly List<RawFillRect> _rects;
        private readonly int[] _order;   // rect indices sorted by cy ascending
        private readonly double[] _cy;   // cy of _rects[_order[k]] — parallel to _order, sorted
        private readonly List<int> _tall;

        public FillRectIndex(List<RawFillRect> rects)
        {
            _rects = rects;
            var n = rects.Count;
            _order = new int[n];
            var cyAll = new double[n];
            _tall = new List<int>();
            for (var i = 0; i < n; i++)
            {
                _order[i] = i;
                cyAll[i] = (rects[i].Lly + rects[i].Ury) / 2;
                if (rects[i].Ury - rects[i].Lly > TallCut) _tall.Add(i);
            }
            Array.Sort(_order, (a, b) => cyAll[a].CompareTo(cyAll[b]));
            _cy = new double[n];
            for (var k = 0; k < n; k++) _cy[k] = cyAll[_order[k]];
        }

        /// <summary>The extra Y margin a caller should add on each side of the exact
        /// predicate band so that a thin rect (height ≤ <see cref="TallCut"/>) whose
        /// midpoint drifts from the band edge is still in the slice.</summary>
        public const double Margin = TallCut;

        /// <summary>Return the candidate rect with the greatest original index whose
        /// midpoint lies in [yLo, yHi] (or that is a tall rect) and that satisfies
        /// <paramref name="match"/> — the same rect the old reverse full-scan returned.</summary>
        public RawFillRect? FindTopMatch(double yLo, double yHi, Func<RawFillRect, bool> match)
        {
            var best = -1;
            for (var k = LowerBound(yLo); k < _cy.Length && _cy[k] <= yHi; k++)
            {
                var idx = _order[k];
                if (idx > best && match(_rects[idx])) best = idx;
            }
            foreach (var idx in _tall)
                if (idx > best && match(_rects[idx])) best = idx;
            return best >= 0 ? _rects[best] : null;
        }

        private int LowerBound(double y)
        {
            int lo = 0, hi = _cy.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (_cy[mid] < y) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }

    // A body-sized opaque fill rect (page space) that can OCCLUDE earlier text.
    // RunsBefore = result.Count when the rect painted: runs with a smaller index
    // were drawn before it and are covered if their box lies inside the rect.
    private readonly record struct RawCoverRect(double Llx, double Lly, double Urx, double Ury, int RunsBefore);

    // Record an occlusion cover, restricted to the active clip (paint can't cover
    // beyond the clip region). Skipped when the clipped remainder drops below
    // body size — a sliver can't hide a text line.
    private static void AddCoverRect(List<RawCoverRect> covers, double llx, double lly, double urx, double ury,
        (double Llx, double Lly, double Urx, double Ury)? clip, int runsBefore)
    {
        if (clip is { } c)
        {
            llx = Math.Max(llx, c.Llx); lly = Math.Max(lly, c.Lly);
            urx = Math.Min(urx, c.Urx); ury = Math.Min(ury, c.Ury);
            if ((ury - lly) < 6.0 || (urx - llx) < 6.0) return;
        }
        covers.Add(new RawCoverRect(llx, lly, urx, ury, runsBefore));
    }

    private readonly record struct RawTextRun(string Text, double X, double Y, double FontSize,
        string? FontName, double Width, Matrix Ctm, FontMetrics? Metrics = null,
        double TmA = 1.0, double TmB = 0.0, double TmC = 0.0, double TmD = 1.0,
        // Per-character cumulative advance widths (text space): CharCumWidths[i] is the
        // advance from the run start to the START of character i, including TJ kerning
        // adjustments; null when not tracked (proportional fallback).
        double[]? CharCumWidths = null, int RenderingMode = 0,
        // Stroke line width (w) in effect when the run was shown; 1.0 is the PDF default.
        double LineWidth = 1.0,
        bool IsBold = false, bool IsItalic = false,
        Font? FontInfoObj = null, double TextRise = 0.0,
        double HScaling = 1.0,
        // Parallel to CharCumWidths: the position just AFTER each character's
        // own glyph advance, BEFORE any subsequent TJ kerning. Used by the
        // fragment-width computation so compensation kernings emitted between
        // the match and post-match text don't inflate rectangle widths.
        double[]? CharEndPositions = null,
        // Fill colour in effect when this run was drawn (PDF default black).
        // Captured onto the fragment's TextState.ForegroundColor during assembly.
        Color? FillColor = null,
        // Stroking colour in effect when this run was drawn (null = default).
        // Captured onto the fragment's TextState.StrokingColor during assembly.
        Color? StrokingColor = null,
        // Active clip bbox (device space) when this run was shown; null = unclipped.
        // A run whose rect misses this box entirely was clipped away — hidden text.
        (double Llx, double Lly, double Urx, double Ury)? ClipRect = null,
        // True for the continuation pieces of a run the absorber split at a large
        // inter-glyph gap (Tc/kern column layout). The search text places exactly
        // one boundary space between split siblings, independent of the run-gap
        // heuristics.
        bool GapSplit = false,
        // Character/word spacing (Tc/Tw) in effect for this run — CharEndPositions
        // fold them into each char's advance, so the gap detector adds them back
        // to recover the INK gap between neighbouring glyphs.
        double CharSpacing = 0.0, double WordSpacing = 0.0,
        double TmBaseY = 0.0,
        // The Form XObject stream this run was extracted from (innermost, when the
        // page's content reached it through Do). Post-extraction edits that must
        // land in the producing stream — e.g. the BackgroundColor highlight —
        // target this stream instead of the page content.
        Core.PdfStream? SourceXObj = null,
        // The character range [InkCharStart, InkCharEnd) of the run that belongs to
        // its non-blank pieces: a TJ array's whitespace-only string elements at either
        // end fall outside it (the page content box stops at the last glyph-bearing
        // piece). -1 = the whole run.
        int InkCharStart = -1, int InkCharEnd = -1);

    /// <summary>
    /// Detect superscript/subscript fragments heuristically by comparing each fragment
    /// with its neighbors. A fragment is super/subscript if its font size is significantly
    /// smaller than neighbors on the same visual line and its Y position is shifted.
    /// </summary>
    private static void DetectSuperSubscript(TextFragmentCollection fragments)
    {
        if (fragments.Count < 2) return;

        for (int i = 1; i <= fragments.Count; i++)
        {
            var frag = fragments[i];
            // Skip if already detected via Ts operator
            if (frag.TextState.TextRise != 0) continue;

            if (!frag.HasExplicitPosition) continue;
            var fs = frag.TextState.FontSize;
            var y = frag.Position!.YIndent;

            // Find the dominant (normal-sized) font size and Y from neighbors
            // that are horizontally adjacent (within the same visual line).
            double neighborFs = 0;
            double neighborY = double.NaN;

            // Check previous neighbor
            if (i > 1)
            {
                var prev = fragments[i - 1];
                if (prev.HasExplicitPosition && IsHorizontalNeighbor(frag, prev))
                {
                    neighborFs = prev.TextState.FontSize;
                    neighborY = prev.Position!.YIndent;
                }
            }
            // Check next neighbor if no previous or previous was also small
            if (neighborFs <= fs && i < fragments.Count)
            {
                var next = fragments[i + 1];
                if (next.HasExplicitPosition && IsHorizontalNeighbor(frag, next))
                {
                    neighborFs = next.TextState.FontSize;
                    neighborY = next.Position!.YIndent;
                }
            }

            if (neighborFs <= 0 || double.IsNaN(neighborY)) continue;

            // Super/subscript heuristic constraints:
            // 1. Fragment text must be short (≤5 chars) — real super/sub are brief
            // 2. Font must be significantly smaller (at most ~70% of neighbor size)
            if (frag.Text.Length > 5) continue;
            if (fs >= neighborFs * 0.7) continue;

            var yDiff = y - neighborY;
            var absYDiff = Math.Abs(yDiff);
            // Superscript: smaller font + Y is significantly higher (≥30% of neighbor font).
            // Subscript: smaller font + Y is at/near the same baseline (within 5% of neighbor font)
            // or below. In-between shifts (5-30%) are ambiguous — not marked.
            if (yDiff > neighborFs * 0.3)
            {
                frag.TextState.IsSuperscript = true;
            }
            else if (absYDiff < neighborFs * 0.05 || yDiff < -neighborFs * 0.05)
            {
                // Same baseline or below — subscript
                frag.TextState.IsSubscript = true;
            }
        }
    }

    /// <summary>Check if two fragments are on the same visual line (close Y, close X).
    /// Callers must ensure both Position values are non-null.</summary>
    private static bool IsHorizontalNeighbor(TextFragment a, TextFragment b)
    {
        // Y positions must be close (within the larger font size)
        var maxFs = Math.Max(a.TextState.FontSize, b.TextState.FontSize);
        var yDiff = Math.Abs(a.Position!.YIndent - b.Position!.YIndent);
        if (yDiff > maxFs) return false;

        // X positions should be reasonably close (fragments on the same line)
        var xDist = Math.Abs(a.Position.XIndent - b.Position.XIndent);
        return xDist < 500; // generous threshold for same-line proximity
    }

    /// <summary>
    /// Normalize Arabic Presentation Forms (U+FB50–U+FDFF, U+FE70–U+FEFF) to their
    /// base Unicode Arabic characters using NFKD decomposition.
    /// This allows text search to match regardless of whether the PDF uses
    /// presentation forms or standard Arabic codepoints.
    /// </summary>
    private static string NormalizeArabicPresentationForms(string text)
    {
        // Fast path: check if text contains any Arabic presentation form characters
        bool hasPresentationForms = false;
        foreach (var ch in text)
        {
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
            {
                hasPresentationForms = true;
                break;
            }
        }
        if (!hasPresentationForms) return text;

        // NFKD decomposition maps presentation forms to base characters
        return text.Normalize(System.Text.NormalizationForm.FormKD);
    }

    /// <summary>
    /// Index-preserving variant of <see cref="NormalizeArabicPresentationForms"/>:
    /// decomposes ONLY presentation-form chars (per char, NFKD) and reports, for
    /// every output char, the input index it came from (<c>newToOld</c>,
    /// null when nothing changed). Decompositions change the string length, so any
    /// char-index map built on the input (charToRun / runStartChar) MUST be
    /// re-projected through this mapping before matching on the output.
    /// </summary>
    private static (string result, int[]? newToOld) NormalizeArabicPresentationFormsWithMap(string text)
    {
        int[]? newToOld = default;
        newToOld = null;
        bool has = false;
        foreach (var ch in text)
            if ((ch >= 'ﭐ' && ch <= '﷿') || (ch >= 'ﹰ' && ch <= '﻿')) { has = true; break; }
        if (!has) return (text, newToOld);

        var sb = new StringBuilder(text.Length + 16);
        var map = new List<int>(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿'))
            {
                var piece = c.ToString().Normalize(System.Text.NormalizationForm.FormKD);
                // The page text is in VISUAL (drawn) order; NFKD hands back a ligature's
                // parts in LOGICAL order (lam, alef, hamza for U+FEF7), so they go in
                // reversed - the bidi pass that turns the line around then reads them
                // back as lam-alef-hamza, the order the caller's pattern normalises to.
                if (piece.Length > 1)
                    for (var k = piece.Length - 1; k >= 0; k--) { sb.Append(piece[k]); map.Add(i); }
                else
                    foreach (var pc in piece) { sb.Append(pc); map.Add(i); }
            }
            else
            {
                sb.Append(c); map.Add(i);
            }
        }
        newToOld = map.ToArray();
        return (sb.ToString(), newToOld);
    }

    // ── API-surface additions ────────────────────────────────────

    /// <summary>Apply the supplied font to every absorbed fragment.</summary>
    public void ApplyForAllFragments(Font font)
    {
        if (font is null) return;
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null) frag.TextState.Font = font;
        }
    }

    /// <summary>Apply the supplied font + size to every absorbed fragment.</summary>
    public void ApplyForAllFragments(Font font, float fontSize)
    {
        if (font is null) return;
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null)
            {
                frag.TextState.Font = font;
                frag.TextState.FontSize = fontSize;
            }
        }
    }

    /// <summary>Apply the supplied font size to every absorbed fragment.</summary>
    public void ApplyForAllFragments(float fontSize)
    {
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null) frag.TextState.FontSize = fontSize;
        }
    }

    /// <summary>Replace every fragment's text with the empty string across every page in the document.</summary>
    public void RemoveAllText(Aspose.Pdf.Document document)
    {
        if (document is null) return;
        foreach (var page in document.Pages)
            RemoveAllText(page);
    }

    /// <summary>Replace every fragment's text with the empty string on the given page.</summary>
    public void RemoveAllText(Aspose.Pdf.Page page)
    {
        if (page is null) return;
        Visit(page);
        foreach (var frag in _fragments)
            frag.Text = string.Empty;
        _fragments.Clear();
    }

    /// <summary>Replace every fragment's text with the empty string on the given page, restricted to <paramref name="rect"/>.</summary>
    public void RemoveAllText(Aspose.Pdf.Page page, Aspose.Pdf.Rectangle rect)
    {
        if (page is null) return;
        var prevSearch = _textSearchOptions;
        try
        {
            _textSearchOptions = new TextSearchOptions(rect);
            RemoveAllText(page);
        }
        finally
        {
            _textSearchOptions = prevSearch;
        }
    }

    /// <summary>Clear absorbed fragments, errors, and per-regex results.</summary>
    public void Reset()
    {
        _fragments.Clear();
        Errors.Clear();
        RegexResults.Clear();
    }
}
