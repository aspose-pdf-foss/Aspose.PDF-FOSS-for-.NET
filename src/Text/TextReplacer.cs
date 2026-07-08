using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Finds and replaces text in PDF content streams.
/// Only handles simple WinAnsiEncoding / Latin1 text operators (Tj, TJ, ', ").
/// For CIDFont / Type0 fonts with identity encoding, replacement is limited to
/// characters already present in the font.
/// </summary>
public sealed class TextReplacer
{
    private int _replacementCount;
    private bool _isRegex;
    private Regex? _regexPattern;
    private bool _allowCrossOperator;

    /// <summary>When true, <see cref="Replace(Page,string,string,bool)"/> also matches search
    /// text that spans multiple text operators (a word drawn glyph-by-glyph). Off by default;
    /// the rectangle/fragment replace path turns it on and relies on <see cref="TargetY"/>/
    /// <see cref="TargetX"/> to keep the cross-operator replacement inside the target region.</summary>
    internal bool AllowCrossOperator { get => _allowCrossOperator; set => _allowCrossOperator = value; }

    /// <summary>Number of replacements made in the last Replace call.</summary>
    public int ReplacementCount => _replacementCount;

    /// <summary>When true, a cross-operator replacement re-flows the rest of the LINE:
    /// the kept tail after the match is re-anchored at match-start + replacement width,
    /// and following same-line absolute-Tm runs shift left/right by the width delta —
    /// giving a same-line reflow when replacing with shorter/longer
    /// text. Off by default: page/document-wide Replace keeps every surviving glyph at its
    /// original position. Set by the <see cref="TextFragment.Text"/> setter.</summary>
    internal bool ReflowLineOnReplace { get; set; }

    /// <summary>
    /// When true, stop after the first match. Set by callers to honour
    /// <c>ReplaceTextStrategy.Scope.ReplaceFirst</c>. Default is replace-all.
    /// </summary>
    public bool ReplaceFirstOnly { get; set; }

    /// <summary>
    /// Redaction mode: when a match is fully deleted (replacement is empty),
    /// emit a TJ advance equal to the removed text's width instead of dropping
    /// the show operator, so following text on the same line keeps its position
    /// (no reflow). The glyphs are still gone from the content — extraction can
    /// no longer find them — but the layout is preserved. Used by
    /// <c>RedactionAnnotation.Redact()</c>.
    /// </summary>
    internal bool PreserveAdvanceOnDelete { get; set; }

    /// <summary>
    /// When true, a full deletion (replacement is empty) keeps an empty
    /// <c>() Tj</c> show operator instead of dropping it, so the emptied
    /// fragment is still re-extractable as a zero-length fragment. Set by the
    /// <see cref="Replace(XForm, string, string)"/> path (clearing a Form
    /// XObject's text) so an emptied form field remains a zero-length fragment
    /// in place rather than disappearing. Off by default so page-level
    /// deletions keep dropping the operator (no spurious empty fragments).
    /// </summary>
    internal bool KeepEmptyShowOperator { get; set; }

    /// <summary>
    /// When set, a replacement whose glyphs are absent from the source embedded subset
    /// font is font-switched to a fallback (source family if installed, else Times) so the
    /// missing glyphs render via whole-run substitution. Enabled only
    /// on the facade <c>PdfContentEditor.ReplaceText</c> path (which owns the whole
    /// replacement); the <c>TextFragment.Text</c> setter leaves it off, since that caller
    /// manages the font itself (e.g. sets <c>TextState.Font</c> after the text) and an
    /// auto-switch would change the run width and shift following text.
    /// </summary>
    internal bool AllowSubsetGlyphFallback { get; set; }

    // When a subset-glyph fallback substitutes the whole run in a different face
    // (source family / Times), record that family so the caller can report it on
    // the fragment's TextState.Font (matching the default no-character behaviour
    // that surfaces the substituted font). Thread-static because the embed helpers
    // that resolve the family are static; reset per replacement via ResetSwitchedFont.
    [ThreadStatic] private static string? s_switchedFontFamily;
    internal string? SwitchedFontFamily => s_switchedFontFamily;
    internal static void ResetSwitchedFont() => s_switchedFontFamily = null;
    internal static void RecordSwitchedFont(string family) => s_switchedFontFamily = family;

    // Emit `[ kern ] TJ` — an advance with no glyphs — that moves the text
    // position right by the width of <paramref name="removedBytes"/>, so text
    // after a fully-deleted run stays put. No-op (writes nothing) when metrics
    // are unavailable or the width is negligible.
    private void WriteDeletionAdvance(MemoryStream result, byte[] removedBytes,
        PdfDictionary? fontDict, PdfReader reader, double fontSize)
    {
        if (!PreserveAdvanceOnDelete || fontDict is null || fontSize <= 0) return;
        double width;
        try
        {
            var metrics = FontMetrics.FromFontDict(fontDict, reader);
            if (metrics is null) return;
            width = metrics.MeasureString(removedBytes, fontSize);
        }
        catch { return; }
        WriteAdvance(result, width, fontSize);
    }

    // Width-preserving advance for a fully-deleted TJ array: total advance = sum of
    // the strings' widths minus the kerning numbers (scaled), so the whole operator
    // is replaced by a glyph-less advance of the same width.
    private void WriteDeletionAdvanceTJ(MemoryStream result, PdfArray arr,
        PdfDictionary? fontDict, PdfReader reader, double fontSize)
    {
        if (!PreserveAdvanceOnDelete || fontDict is null || fontSize <= 0) return;
        double width;
        try
        {
            var metrics = FontMetrics.FromFontDict(fontDict, reader);
            if (metrics is null) return;
            width = 0;
            foreach (var el in arr)
            {
                if (el is PdfString ps) width += metrics.MeasureString(ps.Value, fontSize);
                else if (el is PdfInteger pi) width += -pi.Value * fontSize / 1000.0;
                else if (el is PdfReal pr) width += -pr.Value * fontSize / 1000.0;
            }
        }
        catch { return; }
        WriteAdvance(result, width, fontSize);
    }

    private static void WriteAdvance(MemoryStream result, double width, double fontSize)
    {
        if (width <= 0.05) return;
        // PDF TJ: a number is subtracted from the advance (positive = shift left),
        // so a NEGATIVE number advances right by width.
        var kern = (int)Math.Round(-width * 1000.0 / fontSize);
        if (kern == 0) return;
        result.Write(Encoding.ASCII.GetBytes($"[{kern}] TJ"));
    }

    /// <summary>
    /// When set, only replace inside text-showing operators whose composed
    /// page-space Y (Tm.ty × CTM[3] + CTM[5]) is within
    /// <see cref="TargetYTolerance"/> of <see cref="TargetY"/>. Used by the
    /// per-fragment <c>TextFragment.Text</c> setter to scope the replacement
    /// to the operator that produced this fragment, instead of every matching
    /// occurrence on the page (otherwise iterating fragments[i].Text in a loop
    /// re-replaces the substring "X" inside "X-changed" each pass and the
    /// replacement string accumulates).
    /// </summary>
    internal double? TargetY { get; set; }

    /// <summary>
    /// Y-coordinate tolerance for <see cref="TargetY"/> matching, in PDF
    /// points. Default 6pt — wide enough to absorb the descent offset that
    /// <c>TextFragmentAbsorber.ComputeSegmentPosition</c> bakes into
    /// <c>Position.YIndent</c> (~2-3pt for 12pt body fonts), tight enough to
    /// distinguish text on adjacent lines (line height usually ≥ 12pt).
    /// </summary>
    internal double TargetYTolerance { get; set; } = 6.0;

    /// <summary>
    /// When set, only replace inside text-showing operators whose composed
    /// page-space X (Tm.tx × CTM[0] + Tm.ty × CTM[2] + CTM[4]) is within
    /// <see cref="TargetXTolerance"/> of <see cref="TargetX"/>. Used together
    /// with <see cref="TargetY"/> to scope a replacement to a single fragment's
    /// position — needed for region-scoped (rectangle) replacement where several
    /// matches share a baseline Y but only some fall inside the rectangle.
    /// </summary>
    internal double? TargetX { get; set; }

    /// <summary>X-coordinate tolerance for <see cref="TargetX"/>, in PDF points.
    /// Tight enough to distinguish neighbouring words on a line (typically spaced
    /// well beyond this), loose enough to absorb origin-vs-glyph-start rounding.</summary>
    internal double TargetXTolerance { get; set; } = 4.0;

    /// <summary>
    /// When true, a text-showing operator matches only if its entire shown text
    /// equals the search string (not merely contains it). Used by fragment
    /// deletion so removing a short fragment such as "$" does not strip the same
    /// substring out of a longer operator such as "$ 200.00" on the same row.
    /// </summary>
    internal bool MatchWholeOperator { get; set; }

    /// <summary>
    /// When true, EVERY text-showing operator whose start position passes the
    /// <see cref="TargetY"/>/<see cref="TargetX"/> scoping is treated as a whole-operator
    /// match and replaced with the replacement text (typically empty = the operator is
    /// deleted), regardless of what it shows. Used to clear a page region at operator
    /// granularity — producers that draw one word (or one space) per operator defeat
    /// text-keyed deletion because no single operator's decode equals the coalesced
    /// segment text. Only deletion (empty replacement) is supported.
    /// </summary>
    internal bool MatchAnyOperator { get; set; }

    /// <summary>
    /// When true (TextFragment.Text setter under ReplaceAdjustment.None), a TJ
    /// rewrite with trailing text re-anchors the tail at its ORIGINAL absolute Tm so
    /// surrounding text keeps its exact position regardless of the replacement's
    /// width. Default false = the tail flows with the width delta (the reference's
    /// default replace behaviour, also what redaction and the facades expect).
    /// </summary>
    internal bool AnchorTrailingOnReplace { get; set; }

    /// <summary>
    /// Replace all occurrences of <paramref name="search"/> with <paramref name="replacement"/>
    /// in the given page's content stream(s).
    /// </summary>
    public void Replace(Page page, string search, string replacement)
    {
        Replace(page, search, replacement, false);
    }

    /// <summary>
    /// Replace with cross-operator support enabled.
    /// Used when the caller (e.g., TextFragment.Text setter) knows the text exists
    /// as a cross-operator fragment.
    /// </summary>
    public void ReplaceWithCrossOperator(Page page, string search, string replacement)
    {
        _allowCrossOperator = true;
        Replace(page, search, replacement, false);
        _allowCrossOperator = false;
    }

    /// <summary>
    /// Replace occurrences of <paramref name="search"/> with <paramref name="replacement"/>
    /// in the given page's content stream(s). When <paramref name="isRegex"/> is <c>true</c>,
    /// <paramref name="search"/> is treated as a regular expression pattern.
    /// </summary>
    public void Replace(Page page, string search, string replacement, bool isRegex)
    {
        _replacementCount = 0;
        if (string.IsNullOrEmpty(search) && !MatchAnyOperator) return;
        _isRegex = isRegex;
        _regexPattern = isRegex ? new Regex(search) : null;
        var reader = page.Reader;
        var processedXObjects = new HashSet<int>();

        // Walk the page's content stream first — Form XObjects invoked via /Do
        // are processed recursively from inside the walk so the parent's CTM at
        // each Do site flows into the XObject's text-matrix math (TargetY
        // scoping needs that composition; otherwise positions computed by the
        // absorber after the parent's cm don't line up with the XObject's
        // local Tm.ty values).
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count > 0)
        {
            var combined = CombineStreams(contentStreams);
            var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(page);
            var replaced = ReplaceInContentStream(combined, search, replacement,
                page.Dict, reader, processedXObjects, rA, rB, rC, rD, rTx, rTy);
            if (_replacementCount > 0)
            {
                page.SetContentStream(WrapInGraphicsState(replaced));
            }
        }

        // Catch-all: any Form XObject in the page's resources that wasn't
        // reached via /Do (e.g. unreferenced legacy entries) still gets a pass
        // with identity CTM, mirroring the prior behaviour.
        ReplaceInFormXObjects(page.Dict, reader, search, replacement, processedXObjects);

        _isRegex = false;
        _regexPattern = null;
    }

    /// <summary>Bracket replace-rewritten page content in a single q…Q pair to
    /// isolate the rewritten stream. Content that already BEGINS with a q is returned
    /// unchanged (content opening with q keeps its operator indices after an edit,
    /// while content opening with a BDC gains exactly one wrapper), which keeps
    /// repeated absorber passes from nesting graphics-state operators.</summary>
    internal static byte[] WrapInGraphicsState(byte[] content)
    {
        var ops = ContentStreamOperatorParser.ParseOperators(content);
        if (ops.Count >= 1 && ops[0] == "q")
            return content;

        var prefix = System.Text.Encoding.ASCII.GetBytes("q\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\nQ\n");
        var wrapped = new byte[prefix.Length + content.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        content.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + content.Length);
        return wrapped;
    }

    /// <summary>
    /// Replace text across all pages of a document, including Form XObjects.
    /// </summary>
    public void Replace(Document document, string search, string replacement)
        => Replace(document, search, replacement, false);

    /// <summary>
    /// Replace text across all pages of a document, including Form XObjects.
    /// When <paramref name="isRegex"/> is true, <paramref name="search"/> is a .NET regex.
    /// </summary>
    public void Replace(Document document, string search, string replacement, bool isRegex)
    {
        _replacementCount = 0;
        if (string.IsNullOrEmpty(search)) return;
        _isRegex = isRegex;
        _regexPattern = isRegex ? new Regex(search) : null;
        // Enable cross-operator replacement so matches that span separate
        // Tj/TJ operators (decoded text crossing positioning operators) are
        // not silently missed by the per-op matcher.
        var prevAllowCross = _allowCrossOperator;
        _allowCrossOperator = true;
        var processedXObjects = new HashSet<int>(); // track by obj number to avoid double processing

        try
        {
            foreach (var page in document.Pages)
            {
                if (ReplaceFirstOnly && _replacementCount > 0) break;
                var reader = page.Reader;

                // Walk page content first; XObjects are recursed via /Do.
                var contentStreams = GetContentStreams(page, reader);
                if (contentStreams.Count > 0)
                {
                    var combined = CombineStreams(contentStreams);
                    var count = _replacementCount;
                    var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(page);
                    var replaced = ReplaceInContentStream(combined, search, replacement,
                        page.Dict, reader, processedXObjects, rA, rB, rC, rD, rTx, rTy);

                    if (_replacementCount > count)
                    {
                        page.SetContentStream(WrapInGraphicsState(replaced));
                    }
                }

                // Catch-all for XObjects not reached via /Do.
                ReplaceInFormXObjects(page.Dict, reader, search, replacement, processedXObjects);
            }
        }
        finally
        {
            _isRegex = false;
            _regexPattern = null;
            _allowCrossOperator = prevAllowCross;
        }
    }

    /// <summary>
    /// Replace text within a single Form XObject's own content stream (and any
    /// Form XObjects nested in its resources). Used by the TextFragment.Text setter
    /// for fragments extracted via TextFragmentAbsorber.Visit(XForm), which carry a
    /// null SourcePage — the producing operator lives in the form, not the page.
    /// </summary>
    public void Replace(XForm form, string search, string replacement)
    {
        _replacementCount = 0;
        if (form is null || string.IsNullOrEmpty(search)) return;
        var reader = form.Reader;
        var processed = new HashSet<int>();
        // Cross-operator on: form text frequently spans separate Tj/TJ operators.
        var prevAllowCross = _allowCrossOperator;
        _allowCrossOperator = true;
        // Clearing a form's text should leave an empty, re-extractable fragment
        // (an emptied Typewriter-form field stays in place), so retain the
        // empty show operator on full deletion only for this form path.
        var prevKeepEmpty = KeepEmptyShowOperator;
        KeepEmptyShowOperator = true;
        try
        {
            // Nested Form XObjects first (identity CTM catch-all).
            ReplaceInFormXObjects(form.StreamDict, reader, search, replacement, processed);
            var decoded = form.DecodedBytes;
            var replaced = ReplaceInContentStream(decoded, search, replacement,
                form.StreamDict, reader, processed);
            if (_replacementCount > 0)
                form.SetDecodedContent(replaced);
        }
        finally
        {
            _allowCrossOperator = prevAllowCross;
            KeepEmptyShowOperator = prevKeepEmpty;
        }
    }

    /// <summary>
    /// Process Form XObjects referenced from a page/XObject's Resources/XObject dict.
    /// Updates each XObject's content stream in-place (via the reader cache).
    /// </summary>
    private void ReplaceInFormXObjects(PdfDictionary dict, PdfReader reader,
        string search, string replacement, HashSet<int> processed)
    {
        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return;
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        foreach (var key in xobjects.Keys)
        {
            var xobjRef = xobjects.Get(key);

            // Only deduplicate indirect refs (we need object number)
            if (xobjRef is PdfIndirectRef indRef && !processed.Add(indRef.ObjectNumber))
                continue;

            var xobjStream = reader.ResolveStream(xobjRef);
            if (xobjStream is null || xobjStream.Dict.GetName("Subtype") != "Form") continue;

            // Recursively process nested XObjects within this Form XObject
            ReplaceInFormXObjects(xobjStream.Dict, reader, search, replacement, processed);

            // Process the Form XObject's own content
            var decoded = reader.DecodeStream(xobjStream);
            var countBefore = _replacementCount;
            var replaced = ReplaceInContentStream(decoded, search, replacement,
                xobjStream.Dict, reader, processed);

            if (_replacementCount > countBefore)
            {
                // Update the cached PdfStream with modified (uncompressed) content.
                // Remove the existing filter so PdfWriter will re-compress correctly.
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.ReplaceData(replaced);
            }
        }
    }

    /// <summary>Initial CTM for a page's operator walk: the page-rotation matrix
    /// (identity for Rotate 0). The absorber reports every fragment coordinate in
    /// VIEWER space (rotation applied), and <see cref="TargetY"/>/<see cref="TargetX"/>
    /// are set from those fragments — so the walk's position gating must live in the
    /// same space. Matrices mirror TextFragmentAbsorber.PageRotationCtm.</summary>
    private static (double a, double b, double c, double d, double tx, double ty) PageRotationSeed(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return (1, 0, 0, 1, 0, 0);
        var mb = page.MediaBox;
        var w = mb.URX - mb.LLX;
        var h = mb.URY - mb.LLY;
        return rotate switch
        {
            90 => (0, -1, 1, 0, 0, w),
            180 => (-1, 0, 0, -1, w, h),
            270 => (0, 1, -1, 0, h, 0),
            _ => (1, 0, 0, 1, 0, 0),
        };
    }

    /// <summary>Reference-parity WholeWordsHyphenation reflow: keeps every original
    /// text-showing run VERBATIM (same bytes, kerns, font, per-run Tc) and only
    /// repositions runs by inserting an absolute Tm before (and a restoring Tm after)
    /// each moved run. Only the matched run itself is rewritten, re-encoded in its
    /// ORIGINAL font. Every run after the match shifts by the replacement's width
    /// delta; runs that would cross <paramref name="rightMargin"/> split at a space
    /// glyph and wrap onto the next original baseline; later lines pull up greedily
    /// with their original inter-run gaps preserved. Returns false when this page's
    /// structure can't be handled (CID font, missing glyphs, match not at a run
    /// start) so the caller can fall back to coarser strategies.</summary>
    internal bool ReflowFromMatch(Page page, string search, string replacement,
        double matchX, IReadOnlyList<(double y, double lx, double rx)> lines,
        double leftX, double rightMargin, double pitch)
    {
        if (lines.Count == 0 || string.IsNullOrEmpty(search)) return false;
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return false;
        var streamBytes = CombineStreams(contentStreams);
        var fonts = TextAbsorber.ResolveFonts(page.Dict, reader);
        var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(page);
        var textOps = CollectTextOps(streamBytes, fonts, reader, rA, rB, rC, rD, rTx, rTy);
        if (textOps.Count == 0) return false;

        var metricsCache = new Dictionary<PdfDictionary, FontMetrics?>();
        FontMetrics? MetricsOf(CrossTextOp o)
        {
            if (o.FontDict is null) return null;
            if (metricsCache.TryGetValue(o.FontDict, out var m)) return m;
            FontMetrics? built = null;
            try { built = FontMetrics.FromFontDict(o.FontDict, reader); } catch { }
            metricsCache[o.FontDict] = built;
            return built;
        }
        double PageX(CrossTextOp o) => o.CtmA * o.TmTx + o.CtmC * o.TmTy + o.CtmTx;
        double PageY(CrossTextOp o) => o.CtmB * o.TmTx + o.CtmD * o.TmTy + o.CtmTy;
        double ScaleOf(CrossTextOp o)
        {
            var det = Math.Abs(o.CtmA * o.CtmD - o.CtmB * o.CtmC);
            return det > 1e-12 ? Math.Sqrt(det) : 1.0;
        }
        // The text-matrix scale multiplies every advance: producers often set
        // `/F 1 Tf` and carry the size in Tm (e.g. `33 0 0 33 … Tm`).
        double TmScaleOf(CrossTextOp o) => Math.Sqrt(o.TmA * o.TmA + o.TmB * o.TmB);
        // Page-space advance of a byte run under an op's font state (glyph widths +
        // per-glyph Tc; the op's own TJ kerns only when measuring its full bytes).
        double AdvPage(CrossTextOp o, byte[] bytes, bool own)
        {
            var m = MetricsOf(o);
            double w;
            if (m is not null)
            {
                try { w = m.MeasureString(bytes, o.FontSize); }
                catch { w = o.FontSize * 0.5 * bytes.Length; }
            }
            else
                w = o.FontSize * 0.5 * bytes.Length;
            w += o.Tc * bytes.Length;
            if (own) w -= o.KernSum / 1000.0 * o.FontSize;
            return w * TmScaleOf(o) * ScaleOf(o);
        }

        // Assign ops to the paragraph lines. The op baseline (Tm origin) sits a couple
        // of points ABOVE the absorber's fragment Y (descent offset), so match by
        // nearest line within half the pitch.
        double yTol = Math.Max(4.0, Math.Min(6.0, pitch * 0.45));
        int LineOf(CrossTextOp o)
        {
            double py = PageY(o); int best = -1; double bestD = yTol;
            for (int li = 0; li < lines.Count; li++)
            {
                double d = Math.Abs(py - lines[li].y);
                if (d < bestD) { bestD = d; best = li; }
            }
            return best;
        }

        var affected = new List<(CrossTextOp op, int li, double px)>();
        foreach (var o in textOps)
        {
            int li = LineOf(o);
            if (li < 0) continue;
            double px = PageX(o);
            if (px < lines[li].lx - 0.5 || px > lines[li].rx + 1.0) continue;
            if (li == 0 && px < matchX - 0.5) continue;
            if (string.IsNullOrEmpty(o.Text)) continue;
            if (MetricsOf(o)?.IsCid == true) return false; // 2-byte codes: split unsafe
            affected.Add((o, li, px));
        }
        if (affected.Count == 0) return false;
        affected.Sort((a, b) => a.li != b.li ? a.li.CompareTo(b.li) : a.px.CompareTo(b.px));

        // The first affected run must carry the match (prefix inside the run is kept).
        var head = affected[0].op;
        if (affected[0].li != 0 || !head.Text.Contains(search, StringComparison.Ordinal))
            return false;
        var newHeadText = head.Text.Replace(search, replacement, StringComparison.Ordinal);
                // Encode the rewritten run in its ORIGINAL font. A subset's ToUnicode often
        // omits the space glyph's code (only 'real' glyphs get mapped), so recover the
        // space code from the paragraph's own bytes: any byte in an affected run of the
        // same font that DECODES space-like is the font's space. Bail (so the caller
        // falls back) when any character has no code at all.
        byte[]? TryEncodeInFont(string text)
        {
            if (head.ToUnicode is null)
            {
                foreach (var ch in text) if (ch > 0xFF) return null;
                return EncodeString(text, null, head.FontDict);
            }
            var rev = BuildReverseMap(head.ToUnicode);
            int spaceCode = -1;
            foreach (var (o, _, _) in affected)
            {
                if (!ReferenceEquals(o.FontDict, head.FontDict)) continue;
                foreach (var b in o.Bytes)
                {
                    var ch = DecodeString(new[] { b }, o.ToUnicode, o.FontDict, reader);
                    if (ch is " " or "\u00A0") { spaceCode = b; break; }
                }
                if (spaceCode >= 0) break;
            }
            var outBytes = new List<byte>(text.Length);
            foreach (var ch in text)
            {
                if (rev.TryGetValue(ch.ToString(), out var code) && code >= 0 && code <= 0xFF)
                    outBytes.Add((byte)code);
                else if ((ch == ' ' || ch == '\u00A0') && spaceCode >= 0)
                    outBytes.Add((byte)spaceCode);
                else
                    return null;
            }
            return outBytes.ToArray();
        }
        var newHeadBytes = TryEncodeInFont(newHeadText);
        if (newHeadBytes is null) return false;

        // Baseline page-Y per line, from each line's first affected op; missing lines
        // (fully emptied by the shift) interpolate from the previous baseline.
        var lineBaseY = new double?[lines.Count];
        foreach (var (o, li, _) in affected) lineBaseY[li] ??= PageY(o);
        for (int li = 1; li < lines.Count; li++) lineBaseY[li] ??= lineBaseY[li - 1] - pitch;
        double BaseY(int li) => li < lines.Count
            ? lineBaseY[li]!.Value
            : lineBaseY[^1]!.Value - pitch * (li - lines.Count + 1);

        // Greedy repack with MULTI-SPLIT: any run that crosses the right margin \u2014
        // including the rewritten match run, whose replacement can be several lines
        // long \u2014 splits at the last fitting space glyph as many times as needed; each
        // remainder continues from the paragraph's left margin on the next baseline.
        // A split piece re-emits as a plain Tj (the original TJ kerns are dropped for
        // the pieces; sub-point intra-run shifts, invisible to the layout checks).
        int LastFittingSpace(CrossTextOp o, byte[] bytes, double budget)
        {
            var m = MetricsOf(o);
            if (m is null || bytes.Length < 2) return -1;
            double run = 0; int lastFit = -1;
            for (int k = 0; k < bytes.Length; k++)
            {
                double gw;
                try { gw = m.MeasureString(new[] { bytes[k] }, o.FontSize); }
                catch { gw = o.FontSize * 0.5; }
                run += (gw + o.Tc) * TmScaleOf(o) * ScaleOf(o);
                if (run > budget) break;
                var ch = DecodeString(new[] { bytes[k] }, o.ToUnicode, o.FontDict, reader);
                if (ch is " " or " ") lastFit = k;
            }
            return lastFit < 0 ? -1 : lastFit + 1; // piece keeps the space glyph
        }

        var pieces = new List<(CrossTextOp op, double x, int line, byte[] bytes)>();
        double cursor = 0; int curLi = 0;
        double prevOrigEnd = 0; int prevOrigLine = -1;
        for (int j = 0; j < affected.Count; j++)
        {
            var (o, li, px) = affected[j];
            bool isHead = j == 0;
            var rest = isHead ? newHeadBytes : o.Bytes;
            double wOrig = AdvPage(o, o.Bytes, own: true);
            double gap = !isHead && li == prevOrigLine ? px - prevOrigEnd : 0.0;
            if (gap < -1.0 || gap > 3.0 * o.FontSize * TmScaleOf(o) * ScaleOf(o)) gap = 0.0;
            double startX = isHead ? px : cursor + gap;
            bool wholeOriginal = !isHead; // still the op's full bytes (kerns apply)
            int guard = 0;
            while (true)
            {
                if (++guard > 64) return false; // runaway split: fall back
                double w = AdvPage(o, rest, own: wholeOriginal);
                if (startX + w <= rightMargin + 0.25 || startX <= leftX + 0.25)
                {
                    pieces.Add((o, startX, curLi, rest));
                    cursor = startX + w;
                    break;
                }
                int k = LastFittingSpace(o, rest, rightMargin + 0.25 - startX);
                if (k <= 0 || k >= rest.Length)
                {
                    // No split point on this line: wrap the whole remainder.
                    curLi++; startX = leftX;
                    continue;
                }
                pieces.Add((o, startX, curLi, rest[..k]));
                rest = rest[k..];
                wholeOriginal = false;
                curLi++; startX = leftX;
            }
            prevOrigEnd = px + wOrig; prevOrigLine = li;
        }

        // Rewrite the stream. Pieces group back to their source op; ops are edited in
        // byte order regardless of reading order.
        string N(double v) => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);
        (double tx, double ty) SolveTm(CrossTextOp o, double px, double py)
        {
            var det = o.CtmA * o.CtmD - o.CtmB * o.CtmC;
            if (Math.Abs(det) < 1e-12) return (o.TmTx, o.TmTy);
            var dx = px - o.CtmTx; var dy = py - o.CtmTy;
            return ((o.CtmD * dx - o.CtmC * dy) / det, (-o.CtmB * dx + o.CtmA * dy) / det);
        }
        string TmOf(CrossTextOp o, double tx, double ty) =>
            $" {N(o.TmA)} {N(o.TmB)} {N(o.TmC)} {N(o.TmD)} {N(tx)} {N(ty)} Tm ";

        var byOp = new Dictionary<CrossTextOp, List<(double x, int line, byte[] bytes)>>();
        var opOrder = new List<CrossTextOp>();
        foreach (var pc in pieces)
        {
            if (!byOp.TryGetValue(pc.op, out var l))
            {
                byOp[pc.op] = l = new List<(double, int, byte[])>();
                opOrder.Add(pc.op);
            }
            l.Add((pc.x, pc.line, pc.bytes));
        }
        opOrder.Sort((a, b) => a.OpStart.CompareTo(b.OpStart));

        var result = new MemoryStream();
        int lastWritePos = 0;
        foreach (var o in opOrder)
        {
            var pl = byOp[o];
            var (tx0, ty0) = SolveTm(o, pl[0].x, BaseY(pl[0].line));
            bool moved = Math.Abs(tx0 - o.TmTx) > 1e-4 || Math.Abs(ty0 - o.TmTy) > 1e-4;
            bool isHead = ReferenceEquals(o, head);
            bool split = pl.Count > 1;
            if (!moved && !isHead && !split)
                continue; // untouched: copied verbatim with the surrounding bytes

            result.Write(streamBytes, lastWritePos, o.OpStart - lastWritePos);
            bool wroteTm = false;
            for (int i = 0; i < pl.Count; i++)
            {
                var (px2, li2, bytes2) = pl[i];
                if (i > 0 || moved || split)
                {
                    var (tx, ty) = SolveTm(o, px2, BaseY(li2));
                    result.Write(Encoding.ASCII.GetBytes(TmOf(o, tx, ty)));
                    wroteTm = true;
                }
                if (isHead || split)
                {
                    WriteStringOperand(result, bytes2, o.IsHex);
                    result.Write(" Tj"u8);
                }
                else
                {
                    // Moved but intact: keep the original operator bytes (kerns and all).
                    result.Write(streamBytes, o.OpStart, o.OpEnd - o.OpStart);
                }
            }
            if (wroteTm)
                result.Write(Encoding.ASCII.GetBytes(TmOf(o, o.TmTx, o.TmTy)));
            lastWritePos = o.OpEnd;
        }
        if (lastWritePos < streamBytes.Length)
            result.Write(streamBytes, lastWritePos, streamBytes.Length - lastWritePos);

        _replacementCount = 1;
        page.SetContentStream(result.ToArray());
        return true;
    }

    private byte[] ReplaceInContentStream(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader,
        HashSet<int>? processedXObjects = null,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        processedXObjects ??= new HashSet<int>();
        var countBefore = _replacementCount;
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);
        var lexer = new PdfLexer(streamBytes);
        var result = new MemoryStream();
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        string? currentFontName = null;
        Dictionary<int, string>? currentToUnicode = null;
        PdfDictionary? currentFontDict = null;
        double currentFontSize = 12.0;
        var lastWritePos = 0;

        // CTM (current transformation matrix) and TM (text matrix) tracking. Both
        // are 6-element matrices [a b c d tx ty]. CTM accumulates from `cm`
        // operators, push/pop on `q`/`Q`. TM is only meaningful inside BT/ET;
        // reset on BT, mutated by Td/TD/T*/Tm. Td translates in TEXT SPACE so
        // the dy from Td maps to ty += dy * tm.d (for axis-aligned Tm; full
        // matrix math handles rotation/skew correctly via tm composition).
        // Together CTM and TM let TargetY scope a per-fragment replace to the
        // right text-showing operator (page-space Y ≈ ctm.d × tm.ty + ctm.ty).
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmTx = 0, tmTy = 0;
        double tlLeading = 0;

        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            var endPos = (int)lexer.Position;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                    break;
                case TokenKind.Real:
                    operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                    break;
                case TokenKind.LiteralString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                    break;
                case TokenKind.HexString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                    break;
                case TokenKind.Name:
                    operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                    break;
                case TokenKind.ArrayStart:
                {
                    var arr = ParseContentArrayWithPositions(lexer, out var arrEndPos);
                    operands.Add((TokenKind.ArrayStart, arr, startPos, arrEndPos));
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "Tf":
                            if (operands.Count >= 2 && operands[0].obj is PdfName fontName)
                            {
                                currentFontName = fontName.Value;
                                if (operands[1].obj is PdfInteger fi) currentFontSize = fi.Value;
                                else if (operands[1].obj is PdfReal fr) currentFontSize = fr.Value;
                                if (fonts.TryGetValue(currentFontName, out var fontDict))
                                {
                                    currentFontDict = fontDict;
                                    currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                }
                                else
                                {
                                    currentFontDict = null;
                                    currentToUnicode = null;
                                }
                            }
                            break;

                        case "Tj":
                            if (operands.Count >= 1 && operands[0].obj is PdfString str
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx))
                            {
                                var decoded = DecodeString(str.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded = NormalizeForSearch(decoded);
                                var effSearch = ResolveRtlSearch(normalizedDecoded, normalizedSearch);
                                if (MatchesSearch(normalizedDecoded, effSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded, effSearch, replacement);
                                    // Write everything before this operand
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (newText.Length == 0)
                                    {
                                        // Full deletion: normally drop the show operator entirely so no
                                        // empty text-showing operator remains (which would still
                                        // be re-extracted as a zero-length fragment). In redaction
                                        // mode, leave a glyph-less advance so following text on the
                                        // line does not reflow. When KeepEmptyShowOperator is set
                                        // (form-XObject deletion), retain an empty `() Tj` so the
                                        // emptied fragment is still re-extractable as "" — an
                                        // emptied form field stays a zero-length fragment in place.
                                        if (KeepEmptyShowOperator)
                                            result.Write("() Tj"u8);
                                        WriteDeletionAdvance(result, str.Value, currentFontDict, reader, currentFontSize);
                                    }
                                    else if (NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        WriteFontSwitchedReplacement(result, newText, currentFontDict,
                                            currentFontName, currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback);
                                    }
                                    else
                                    {
                                        var encoded = EncodeString(newText, currentToUnicode, currentFontDict);
                                        WriteStringOperand(result, encoded, str.IsHex);
                                        result.Write(" Tj"u8);
                                    }
                                    lastWritePos = endPos;
                                }
                            }
                            break;

                        case "TJ":
                            if (operands.Count >= 1 && operands[0].obj is PdfArray arr
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx))
                            {
                                // Pre-check: compute what the replaced text would be to decide
                                // font switch BEFORE encoding (avoids round-trip corruption).
                                var tjOrigText = ConcatenateTJText(arr, currentToUnicode, currentFontDict, reader);
                                var tjNormalizedOrig = NormalizeForSearch(tjOrigText);
                                var tjNormalizedSearch = ResolveRtlSearch(tjNormalizedOrig, NormalizeForSearch(search));
                                if (MatchesSearch(tjNormalizedOrig, tjNormalizedSearch))
                                {
                                    var tjReplacedText = MatchAnyOperator
                                        ? replacement
                                        : tjNormalizedOrig.Replace(tjNormalizedSearch, replacement, StringComparison.Ordinal);
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (tjReplacedText.Length == 0)
                                    {
                                        // Full deletion: drop the entire TJ operator so no
                                        // empty text-showing operator remains (which would
                                        // still be re-extracted as a zero-length fragment). In
                                        // redaction mode, leave a glyph-less advance so following
                                        // text on the line does not reflow. When
                                        // KeepEmptyShowOperator is set (form-XObject deletion),
                                        // retain an empty `() Tj` so the emptied fragment stays
                                        // re-extractable as "" (see the Tj branch above).
                                        if (KeepEmptyShowOperator)
                                            result.Write("() Tj"u8);
                                        WriteDeletionAdvanceTJ(result, arr, currentFontDict, reader, currentFontSize);
                                        _replacementCount++;
                                    }
                                    else if (NeedsFontSwitch(tjReplacedText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        // Preserve any trailing text's position: split the TJ, font-switch
                                        // only the matched run, re-anchor the rest at its original absolute
                                        // Tm. Falls back to flattening the whole TJ when there's no trailing
                                        // text or the match isn't at the array start.
                                        if (!WriteFontSwitchedTJSplit(result, arr, search, replacement,
                                                currentToUnicode, currentFontDict, currentFontName, currentFontSize,
                                                tmA, tmB, tmC, tmD, tmTx, tmTy, reader, pageDict,
                                                NeedsTlmRestore(streamBytes, endPos)))
                                            WriteFontSwitchedReplacement(result, tjReplacedText, currentFontDict,
                                                currentFontName, currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback);
                                        _replacementCount++;
                                    }
                                    else if (AnchorTrailingOnReplace
                                        && WriteAnchoredTJSplit(result, arr, search, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        // ReplaceAdjustment.None with trailing text: split the TJ and
                                        // re-anchor the tail at its ORIGINAL absolute Tm instead of a
                                        // compensating kern — kern-blind consumers (extraction's
                                        // rect clip, sub-run positions) would misplace every glyph
                                        // after a large kern.
                                        _replacementCount++;
                                    }
                                    else if (TryReplaceTJArray(arr, search, replacement,
                                        currentToUnicode, currentFontDict, reader, currentFontSize, out var newArr))
                                    {
                                        WriteTJArray(result, newArr);
                                        result.Write(" TJ"u8);
                                        _replacementCount++;
                                    }

                                    lastWritePos = endPos;
                                }
                            }
                            break;

                        case "'":
                            // ' implicitly does T* before showing — advance the
                            // text matrix in text space (dy = -leading) so
                            // IsAtTargetY sees the post-T* position.
                            tmTx = -tlLeading * tmC + tmTx;
                            tmTy = -tlLeading * tmD + tmTy;
                            if (operands.Count >= 1 && operands[0].obj is PdfString str2
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx))
                            {
                                var decoded = DecodeString(str2.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded2 = NormalizeForSearch(decoded);
                                if (MatchesSearch(normalizedDecoded2, normalizedSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded2, normalizedSearch, replacement);

                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);
                                    if (NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        WriteFontSwitchedReplacement(result, newText, currentFontDict,
                                            currentFontName, currentFontSize, pageDict, reader, "'", AllowSubsetGlyphFallback);
                                    }
                                    else
                                    {
                                        var encoded = EncodeString(newText, currentToUnicode, currentFontDict);
                                        WriteStringOperand(result, encoded, str2.IsHex);
                                        result.Write(" '"u8);
                                    }
                                    lastWritePos = endPos;
                                }
                            }
                            break;

                        case "BT":
                            tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmTx = 0; tmTy = 0;
                            tlLeading = 0;
                            break;

                        case "Td":
                        case "TD":
                            // Td translates in TEXT SPACE: new TM = [1 0 0 1 dx dy] × current TM.
                            // For ty: newTy = dx*tm.b + dy*tm.d + tm.ty.
                            if (operands.Count >= 2)
                            {
                                double dx = ToDouble(operands[0].obj);
                                double dy = ToDouble(operands[1].obj);
                                tmTx = dx * tmA + dy * tmC + tmTx;
                                tmTy = dx * tmB + dy * tmD + tmTy;
                                if (op == "TD") tlLeading = -dy;
                            }
                            break;

                        case "Tm":
                            // Tm sets the text matrix absolutely.
                            if (operands.Count >= 6)
                            {
                                tmA = ToDouble(operands[0].obj);
                                tmB = ToDouble(operands[1].obj);
                                tmC = ToDouble(operands[2].obj);
                                tmD = ToDouble(operands[3].obj);
                                tmTx = ToDouble(operands[4].obj);
                                tmTy = ToDouble(operands[5].obj);
                            }
                            break;

                        case "TL":
                            if (operands.Count >= 1)
                                tlLeading = ToDouble(operands[0].obj);
                            break;

                        case "T*":
                            // T* is equivalent to `0 -leading Td` — translate dy=-leading in text space.
                            tmTx = -tlLeading * tmC + tmTx;
                            tmTy = -tlLeading * tmD + tmTy;
                            break;

                        case "q":
                            ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                            break;

                        case "Q":
                            if (ctmStack.Count > 0)
                                (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
                            break;

                        case "cm":
                            if (operands.Count >= 6)
                            {
                                double a = ToDouble(operands[0].obj);
                                double b = ToDouble(operands[1].obj);
                                double c = ToDouble(operands[2].obj);
                                double d = ToDouble(operands[3].obj);
                                double tx = ToDouble(operands[4].obj);
                                double ty = ToDouble(operands[5].obj);
                                // Pre-multiply current CTM by operator matrix per PDF 32000 §8.3.2.
                                var newA = a * ctmA + b * ctmC;
                                var newB = a * ctmB + b * ctmD;
                                var newC = c * ctmA + d * ctmC;
                                var newD = c * ctmB + d * ctmD;
                                var newTx = tx * ctmA + ty * ctmC + ctmTx;
                                var newTy = tx * ctmB + ty * ctmD + ctmTy;
                                ctmA = newA; ctmB = newB; ctmC = newC; ctmD = newD;
                                ctmTx = newTx; ctmTy = newTy;
                            }
                            break;

                        case "Do":
                            // Recurse into the referenced Form XObject with the
                            // current CTM as initial state, so the parent's cm
                            // composition flows into the XObject's text-matrix
                            // math (TargetY scoping needs that for content
                            // authored as `parent: cm Do` + `xobj: Td Tj`).
                            if (operands.Count >= 1 && operands[0].obj is PdfName xobjName)
                            {
                                var pageRes = reader.ResolveDict(pageDict.Get("Resources"));
                                var xobjsDict = pageRes is null ? null
                                    : reader.ResolveDict(pageRes.Get("XObject"));
                                var xobjRef = xobjsDict?.Get(xobjName.Value);
                                int? objNum = (xobjRef as PdfIndirectRef)?.ObjectNumber;
                                bool firstVisit = objNum is null || processedXObjects.Add(objNum.Value);
                                if (firstVisit && xobjRef is not null)
                                {
                                    var xobjStream = reader.ResolveStream(xobjRef);
                                    if (xobjStream is not null
                                        && xobjStream.Dict.GetName("Subtype") == "Form")
                                    {
                                        var xobjBytes = reader.DecodeStream(xobjStream);
                                        var beforeXobj = _replacementCount;
                                        var xobjReplaced = ReplaceInContentStream(xobjBytes,
                                            search, replacement,
                                            xobjStream.Dict, reader, processedXObjects,
                                            ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy);
                                        if (_replacementCount > beforeXobj)
                                        {
                                            xobjStream.Dict.Remove("Filter");
                                            xobjStream.Dict.Remove("DecodeParms");
                                            xobjStream.ReplaceData(xobjReplaced);
                                        }
                                    }
                                }
                            }
                            break;

                        case "BI":
                            // Write bytes up to (but not including) BI operator
                            result.Write(streamBytes, lastWritePos, startPos - lastWritePos);
                            SkipInlineImage(lexer);
                            lastWritePos = (int)lexer.Position;
                            operands.Clear();
                            continue;
                    }

                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }

        // Write remaining bytes
        if (lastWritePos < streamBytes.Length)
            result.Write(streamBytes, lastWritePos, streamBytes.Length - lastWritePos);

        var output = result.ToArray();

        // Always run cross-operator pass when enabled, even after per-op
        // replacements: per-op handles within-Tj matches, cross-op picks up
        // matches whose decoded text spans separate Tj/TJ operators (e.g.
        // "Page " in one Tj followed by "5 of 10" in another after a Td/Tm).
        // The cross-op routine itself skips single-operator matches so we
        // don't double-process spans the per-op pass already replaced.
        if (_allowCrossOperator)
        {
            var crossResult = TryCrossOperatorReplace(output, search, replacement, pageDict, reader,
                initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
            if (crossResult is not null)
                output = crossResult;
        }

        return output;
    }

    /// <summary>
    /// Cross-operator text replacement: collects text across consecutive Tj/TJ operators,
    /// finds the search string (literal or regex per <see cref="_isRegex"/>) in the
    /// concatenated text, and rewrites the operators. Used to catch matches whose
    /// decoded text spans positioned glyphs across separate Tj/TJ operators —
    /// invisible to the per-operator matcher.
    /// </summary>
    private byte[]? TryCrossOperatorReplace(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);

        // Collect text operators with everything needed to (a) build a gap-aware
        // concatenation for matching, (b) split a partially-matched first/last operator,
        // and (c) re-anchor / shift following same-line runs: decoded text + raw string
        // bytes, byte span, text matrix + CTM, font state (dict/ToUnicode/size/Tc), TJ
        // kern total, and the byte span of the op's positioning Tm x-operand (when the
        // op is Tm-positioned) so a follower's Tm can be rewritten in place.
        var textOps = CollectTextOps(streamBytes, fonts, reader,
            initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
        return TryCrossOperatorReplaceCore(streamBytes, search, replacement, pageDict, reader,
            normalizedSearch, textOps);
    }

    /// <summary>Walk a content stream and collect every text-showing operator with the
    /// full state needed to match, measure, split, or re-anchor it: decoded text + raw
    /// bytes, byte span, text matrix + composed CTM, font state (dict/ToUnicode/size/Tc),
    /// TJ kern total, and the positioning-Tm operand span. Shared by the cross-operator
    /// replace and the run-move reflow.</summary>
    private List<CrossTextOp> CollectTextOps(byte[] streamBytes,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var textOps = new List<CrossTextOp>();
        var lexer2 = new PdfLexer(streamBytes);
        var ops2 = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        Dictionary<int, string>? curToUnicode = null;
        PdfDictionary? curFontDict = null;
        string? curFontName = null;
        double curFontSize = 12;
        double curTc = 0;
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmTx = 0, tmTy = 0, tlLeading = 0;
        // Seed the CTM with the caller's context (the Do-site CTM when this stream is a
        // recursed Form XObject) so TargetY/TargetX scoping sees page-space positions.
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        // Pending positioning-Tm record, consumed by the next text-showing op.
        var pendingTm = (has: false, xStart: 0, xEnd: 0, xVal: 0.0);

        while (true)
        {
            var sp = (int)lexer2.Position;
            var tok = lexer2.NextToken();
            if (tok.Kind == TokenKind.Eof) break;
            var ep = (int)lexer2.Position;

            switch (tok.Kind)
            {
                case TokenKind.Integer:
                case TokenKind.Real:
                case TokenKind.Name:
                    ops2.Add((tok.Kind, tok.Kind == TokenKind.Name ? new PdfName(tok.StringValue!) :
                        tok.Kind == TokenKind.Integer ? new PdfInteger(tok.IntValue) :
                        (PdfObject)new PdfReal(tok.RealValue), sp, ep));
                    break;
                case TokenKind.LiteralString:
                    ops2.Add((tok.Kind, new PdfString(tok.BytesValue!), sp, ep));
                    break;
                case TokenKind.HexString:
                    ops2.Add((tok.Kind, new PdfString(tok.BytesValue!, isHex: true), sp, ep));
                    break;
                case TokenKind.ArrayStart:
                    var arr = ParseContentArrayWithPositions(lexer2, out var aep);
                    ops2.Add((TokenKind.ArrayStart, arr, sp, aep));
                    break;
                case TokenKind.Keyword:
                    var op = tok.StringValue!;
                    if (op == "Tf" && ops2.Count >= 2 && ops2[0].obj is PdfName fn)
                    {
                        curFontName = fn.Value;
                        if (ops2[1].obj is PdfInteger fsi) curFontSize = fsi.Value;
                        else if (ops2[1].obj is PdfReal fsr) curFontSize = fsr.Value;
                        if (fonts.TryGetValue(curFontName, out var fd))
                        { curFontDict = fd; curToUnicode = TextAbsorber.ParseToUnicodeFromDict(fd, reader); }
                        else { curFontDict = null; curToUnicode = null; }
                    }
                    else if (op == "Tc" && ops2.Count >= 1)
                        curTc = ToDouble(ops2[0].obj);
                    else if (op is "Tj" or "'" && ops2.Count >= 1 && ops2[0].obj is PdfString s)
                    {
                        if (op == "'") { tmTx = -tlLeading * tmC + tmTx; tmTy = -tlLeading * tmD + tmTy; pendingTm.has = false; }
                        var decoded = DecodeString(s.Value, curToUnicode, curFontDict, reader);
                        textOps.Add(new CrossTextOp
                        {
                            Text = decoded, Bytes = s.Value, IsHex = s.IsHex,
                            OpStart = ops2[0].startPos, OpEnd = ep,
                            TmA = tmA, TmB = tmB, TmC = tmC, TmD = tmD, TmTx = tmTx, TmTy = tmTy,
                            CtmA = ctmA, CtmB = ctmB, CtmC = ctmC, CtmD = ctmD, CtmTx = ctmTx, CtmTy = ctmTy,
                            FontDict = curFontDict, FontName = curFontName, ToUnicode = curToUnicode,
                            FontSize = curFontSize, Tc = curTc,
                            TmPositioned = pendingTm.has, TmXTokStart = pendingTm.xStart,
                            TmXTokEnd = pendingTm.xEnd, TmXVal = pendingTm.xVal,
                        });
                        pendingTm.has = false;
                    }
                    else if (op == "TJ" && ops2.Count >= 1 && ops2[0].obj is PdfArray tjArr)
                    {
                        var sb = new StringBuilder();
                        var byteBuf = new MemoryStream();
                        double kernSum = 0;
                        bool isHex = false; bool firstStr = true;
                        foreach (var item in tjArr)
                        {
                            if (item is PdfString ps)
                            {
                                sb.Append(DecodeString(ps.Value, curToUnicode, curFontDict, reader));
                                byteBuf.Write(ps.Value, 0, ps.Value.Length);
                                if (firstStr) { isHex = ps.IsHex; firstStr = false; }
                            }
                            else if (item is PdfInteger ki) kernSum += ki.Value;
                            else if (item is PdfReal kr) kernSum += kr.Value;
                        }
                        textOps.Add(new CrossTextOp
                        {
                            Text = sb.ToString(), Bytes = byteBuf.ToArray(), IsHex = isHex,
                            OpStart = ops2[0].startPos, OpEnd = ep,
                            TmA = tmA, TmB = tmB, TmC = tmC, TmD = tmD, TmTx = tmTx, TmTy = tmTy,
                            CtmA = ctmA, CtmB = ctmB, CtmC = ctmC, CtmD = ctmD, CtmTx = ctmTx, CtmTy = ctmTy,
                            FontDict = curFontDict, FontName = curFontName, ToUnicode = curToUnicode,
                            FontSize = curFontSize, Tc = curTc, KernSum = kernSum,
                            TmPositioned = pendingTm.has, TmXTokStart = pendingTm.xStart,
                            TmXTokEnd = pendingTm.xEnd, TmXVal = pendingTm.xVal,
                        });
                        pendingTm.has = false;
                    }
                    else if (op == "BT") { tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmTx = 0; tmTy = 0; tlLeading = 0; pendingTm.has = false; }
                    else if ((op == "Td" || op == "TD") && ops2.Count >= 2)
                    {
                        double dx = ToDouble(ops2[0].obj), dy = ToDouble(ops2[1].obj);
                        tmTx = dx * tmA + dy * tmC + tmTx;
                        tmTy = dx * tmB + dy * tmD + tmTy;
                        if (op == "TD") tlLeading = -dy;
                        pendingTm.has = false; // Td-positioned: inherits the line chain, no Tm patch
                    }
                    else if (op == "Tm" && ops2.Count >= 6)
                    {
                        tmA = ToDouble(ops2[0].obj); tmB = ToDouble(ops2[1].obj);
                        tmC = ToDouble(ops2[2].obj); tmD = ToDouble(ops2[3].obj);
                        tmTx = ToDouble(ops2[4].obj); tmTy = ToDouble(ops2[5].obj);
                        pendingTm = (true, ops2[4].startPos, ops2[4].endPos, tmTx);
                    }
                    else if (op == "TL" && ops2.Count >= 1) tlLeading = ToDouble(ops2[0].obj);
                    else if (op == "T*") { tmTx = -tlLeading * tmC + tmTx; tmTy = -tlLeading * tmD + tmTy; pendingTm.has = false; }
                    else if (op == "q") ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                    else if (op == "Q") { if (ctmStack.Count > 0) (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop(); }
                    else if (op == "cm" && ops2.Count >= 6)
                    {
                        double a = ToDouble(ops2[0].obj), b = ToDouble(ops2[1].obj), c = ToDouble(ops2[2].obj);
                        double dd = ToDouble(ops2[3].obj), tx = ToDouble(ops2[4].obj), ty = ToDouble(ops2[5].obj);
                        double nA = a * ctmA + b * ctmC, nB = a * ctmB + b * ctmD;
                        double nC = c * ctmA + dd * ctmC, nD = c * ctmB + dd * ctmD;
                        double nTx = tx * ctmA + ty * ctmC + ctmTx, nTy = tx * ctmB + ty * ctmD + ctmTy;
                        ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmTx = nTx; ctmTy = nTy;
                    }
                    ops2.Clear();
                    break;
                default:
                    ops2.Clear();
                    break;
            }
        }
        return textOps;
    }

    private byte[]? TryCrossOperatorReplaceCore(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader, string normalizedSearch, List<CrossTextOp> textOps)
    {
        // Advance (in text-space units) a byte string renders with an op's font state:
        // glyph widths + per-glyph Tc − TJ kerns (kern applied only when measuring the
        // op's own full bytes).
        var metricsCache = new Dictionary<PdfDictionary, FontMetrics?>();
        FontMetrics? MetricsOf(CrossTextOp o)
        {
            if (o.FontDict is null) return null;
            if (metricsCache.TryGetValue(o.FontDict, out var m)) return m;
            FontMetrics? built = null;
            try { built = FontMetrics.FromFontDict(o.FontDict, reader); } catch { }
            metricsCache[o.FontDict] = built;
            return built;
        }
        double Adv(CrossTextOp o, byte[] bytes, bool own)
        {
            var m = MetricsOf(o);
            double w;
            if (m is not null)
            {
                try { w = m.MeasureString(bytes, o.FontSize); }
                catch { w = o.FontSize * 0.5 * bytes.Length; }
            }
            else
                w = o.FontSize * 0.5 * bytes.Length;
            var glyphs = m?.IsCid == true ? (bytes.Length + 1) / 2 : bytes.Length;
            w += o.Tc * glyphs;
            if (own) w -= o.KernSum / 1000.0 * o.FontSize;
            return w;
        }

        // Gap-aware concatenation: like the absorber, insert a synthetic space between
        // two same-line ops separated by a word-sized positioning gap (text drawn
        // word-per-Tm with no space glyphs), so a spaced phrase can match across ops.
        // Synthetic chars map to op −1 and are trimmed off the match edges.
        var allText = new StringBuilder();
        var charToOp = new List<int>();
        for (var i = 0; i < textOps.Count; i++)
        {
            var cur = textOps[i];
            if (i > 0 && allText.Length > 0)
            {
                var prev = textOps[i - 1];
                bool sameCtm = Math.Abs(cur.CtmA - prev.CtmA) < 1e-6 && Math.Abs(cur.CtmC - prev.CtmC) < 1e-6
                    && Math.Abs(cur.CtmD - prev.CtmD) < 1e-6 && Math.Abs(cur.CtmTx - prev.CtmTx) < 1e-6
                    && Math.Abs(cur.CtmTy - prev.CtmTy) < 1e-6;
                bool horizontal = Math.Abs(cur.TmB) <= Math.Abs(cur.TmA) && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA);
                if (sameCtm && horizontal && Math.Abs(cur.TmTy - prev.TmTy) < 2.0)
                {
                    var gap = cur.TmTx - (prev.TmTx + Adv(prev, prev.Bytes, own: true));
                    var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
                    var lastChar = allText[^1];
                    var nextChar = cur.Text.Length > 0 ? cur.Text[0] : '\0';
                    if (gap > 0.2 * fs && gap <= 3.0 * fs && lastChar != ' ' && nextChar != ' ')
                    {
                        charToOp.Add(-1);
                        allText.Append(' ');
                    }
                }
            }
            cur.CharStart = allText.Length;
            foreach (var _ in cur.Text) charToOp.Add(i);
            allText.Append(cur.Text);
        }

        var fullText = NormalizeForSearch(allText.ToString());

        // Enumerate match spans as (start, length) — regex match, or a literal scan that
        // is ELASTIC over the synthetic gap-spaces (charToOp < 0): a synthetic space
        // matches a needle space OR nothing, so both "05 DEC 2012" and the fragment's
        // segment-joined "05DEC2012" find the same span.
        (int idx, int len) NextMatch(int from)
        {
            if (_isRegex && _regexPattern is not null)
            {
                var m = _regexPattern.Match(fullText, from);
                return m.Success ? (m.Index, m.Length) : (-1, 0);
            }
            if (normalizedSearch.Length == 0) return (-1, 0);
            for (var st = Math.Max(0, from); st < fullText.Length; st++)
            {
                int h = st, n = 0;
                while (n < normalizedSearch.Length && h < fullText.Length)
                {
                    if (fullText[h] == normalizedSearch[n]) { h++; n++; continue; }
                    if (h < charToOp.Count && charToOp[h] < 0) { h++; continue; } // skip synthetic space
                    break;
                }
                if (n == normalizedSearch.Length) return (st, h - st);
            }
            return (-1, 0);
        }

        var (searchIdx, searchLen) = NextMatch(0);
        if (searchIdx < 0) return null;

        string FormatNum(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

        // Byte-level patches (follower Tm x rewrites), applied while copying.
        var patches = new SortedList<int, (int end, byte[] text)>();
        var result = new MemoryStream();
        var lastWrite = 0;
        void CopyRange(int to)
        {
            while (patches.Count > 0)
            {
                var start = patches.Keys[0];
                if (start >= to) break;
                var (pEnd, pText) = patches.Values[0];
                patches.RemoveAt(0);
                if (start < lastWrite) continue; // inside an already-replaced span
                result.Write(streamBytes, lastWrite, start - lastWrite);
                result.Write(pText, 0, pText.Length);
                lastWrite = pEnd;
            }
            if (to > lastWrite)
            {
                result.Write(streamBytes, lastWrite, to - lastWrite);
                lastWrite = to;
            }
        }

        // Can every char of the replacement be encoded in the op's own font? (Reverse
        // ToUnicode coverage; keeps the replacement in the source face — and measured
        // with the source metrics — instead of switching to a fallback font.)
        bool CanEncodeInFont(CrossTextOp o, string text)
        {
            if (o.ToUnicode is null)
                return text.All(c => c <= 0xFF); // simple Latin1 encoding
            var reverse = BuildReverseMap(o.ToUnicode);
            return text.All(c => reverse.ContainsKey(c.ToString()));
        }

        var replaced = false;
        while (searchIdx >= 0)
        {
            if (ReplaceFirstOnly && replaced) break;

            // Trim synthetic gap-space chars off the match edges and locate the ops.
            var msIdx = searchIdx;
            var meIdx = searchIdx + searchLen - 1;
            while (msIdx < meIdx && msIdx < charToOp.Count && charToOp[msIdx] < 0) msIdx++;
            while (meIdx > msIdx && meIdx < charToOp.Count && charToOp[meIdx] < 0) meIdx--;
            int firstOp = msIdx < charToOp.Count ? charToOp[msIdx] : -1;
            int lastOp = meIdx < charToOp.Count ? charToOp[meIdx] : -1;

            bool inTarget = firstOp < 0 ||
                (IsAtTargetY(textOps[firstOp].TmTx, textOps[firstOp].TmTy,
                             textOps[firstOp].CtmB, textOps[firstOp].CtmD, textOps[firstOp].CtmTy)
                 && IsAtTargetX(textOps[firstOp].TmTx, textOps[firstOp].TmTy,
                                textOps[firstOp].CtmA, textOps[firstOp].CtmC, textOps[firstOp].CtmTx));

            // Matches inside ONE operator belong to the per-op pass; cross-op only adds
            // value for spans covering multiple operators.
            if (inTarget && firstOp >= 0 && lastOp >= 0 && firstOp != lastOp)
            {
                var fo = textOps[firstOp];
                var lo = textOps[lastOp];
                var prefixText = fo.Text.Substring(0, Math.Clamp(msIdx - fo.CharStart, 0, fo.Text.Length));
                var matchedLastLen = Math.Clamp(meIdx - lo.CharStart + 1, 0, lo.Text.Length);
                var suffixText = lo.Text.Substring(matchedLastLen);

                var prefixBytes = prefixText.Length > 0 ? EncodeString(prefixText, fo.ToUnicode, fo.FontDict) : Array.Empty<byte>();
                var suffixBytes = suffixText.Length > 0 ? EncodeString(suffixText, lo.ToUnicode, lo.FontDict) : Array.Empty<byte>();
                var matchedLastBytes = matchedLastLen > 0 ? EncodeString(lo.Text.Substring(0, matchedLastLen), lo.ToUnicode, lo.FontDict) : Array.Empty<byte>();

                // Copy everything before the first matched operator.
                CopyRange(fo.OpStart);

                // Prefix (kept head of the first op) stays in the original font at the
                // original pen position.
                if (prefixBytes.Length > 0)
                {
                    WriteStringOperand(result, prefixBytes, fo.IsHex);
                    result.Write(Encoding.ASCII.GetBytes(" Tj "));
                }

                // Replacement: re-encoded into the source font when its glyphs map
                // (source-metric width, keeps the face); otherwise the font-switch path.
                double advRepl;
                if (replacement.Length > 0 && CanEncodeInFont(fo, replacement))
                {
                    var replBytes = EncodeString(replacement, fo.ToUnicode, fo.FontDict);
                    WriteStringOperand(result, replBytes, fo.IsHex);
                    result.Write(Encoding.ASCII.GetBytes(" Tj "));
                    advRepl = Adv(fo, replBytes, own: false);
                }
                else if (replacement.Length > 0)
                {
                    WriteFontSwitchedReplacement(result, replacement, fo.FontDict,
                        fo.FontName, fo.FontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback);
                    result.WriteByte((byte)' ');
                    double est = 0;
                    foreach (var ch in replacement)
                    {
                        var cw = ch <= 0xFF ? Standard14Fonts.GetWidth("Helvetica", ch) : 0;
                        est += cw > 0 ? cw : 500;
                    }
                    advRepl = est / 1000.0 * fo.FontSize;
                }
                else
                    advRepl = 0;

                // Middle operators: keep their positioning/state gaps, blank the text.
                for (var oi = firstOp + 1; oi < lastOp; oi++)
                {
                    lastWrite = textOps[oi - 1].OpEnd;
                    CopyRange(textOps[oi].OpStart);
                    result.Write(Encoding.ASCII.GetBytes("() Tj "));
                }

                // Last operator: keep the tail after the match, re-anchored with an
                // absolute Tm. Reflow mode puts it at match-start + replacement width
                // (the reference same-line reflow); otherwise it keeps its original X.
                lastWrite = textOps[lastOp - 1 >= firstOp ? lastOp - 1 : firstOp].OpEnd;
                CopyRange(lo.OpStart);
                var advPrefix = prefixBytes.Length > 0 ? Adv(fo, prefixBytes, own: false) : 0;
                var advMatchedLast = matchedLastBytes.Length > 0 ? Adv(lo, matchedLastBytes, own: false) : 0;
                var oldSuffixTmX = lo.TmTx + advMatchedLast;
                var newSuffixTmX = ReflowLineOnReplace ? fo.TmTx + advPrefix + advRepl : oldSuffixTmX;
                if (suffixBytes.Length > 0)
                {
                    // Leading space: the copied gap can end in a keyword ("… Tc") with no
                    // trailing delimiter, and "Tc1 0 0 …" would lex as an unknown keyword.
                    result.Write(Encoding.ASCII.GetBytes(
                        $" {FormatNum(lo.TmA)} {FormatNum(lo.TmB)} {FormatNum(lo.TmC)} {FormatNum(lo.TmD)} {FormatNum(newSuffixTmX)} {FormatNum(lo.TmTy)} Tm "));
                    WriteStringOperand(result, suffixBytes, lo.IsHex);
                    result.Write(Encoding.ASCII.GetBytes(" Tj"));
                }
                else
                    result.Write(Encoding.ASCII.GetBytes("() Tj"));

                lastWrite = lo.OpEnd;
                _replacementCount++;
                replaced = true;

                // Same-line reflow: shift following absolute-Tm runs on this line left by
                // the width delta so words split across runs stay joined. Td-positioned
                // followers inherit the shift through the re-anchored suffix Tm.
                var delta = oldSuffixTmX - newSuffixTmX;
                if (Math.Abs(delta) > 0.01)
                {
                    for (var j = lastOp + 1; j < textOps.Count; j++)
                    {
                        var fl = textOps[j];
                        if (!fl.TmPositioned) continue;
                        if (fl.TmXTokStart < lastWrite) continue;
                        bool sameCtm = Math.Abs(fl.CtmA - lo.CtmA) < 1e-6 && Math.Abs(fl.CtmC - lo.CtmC) < 1e-6
                            && Math.Abs(fl.CtmD - lo.CtmD) < 1e-6 && Math.Abs(fl.CtmTx - lo.CtmTx) < 1e-6
                            && Math.Abs(fl.CtmTy - lo.CtmTy) < 1e-6;
                        if (!sameCtm || Math.Abs(fl.TmTy - lo.TmTy) >= 2.0) continue;
                        if (fl.TmXVal <= fo.TmTx) continue;
                        patches[fl.TmXTokStart] = (fl.TmXTokEnd,
                            Encoding.ASCII.GetBytes(FormatNum(fl.TmXVal - delta)));
                    }
                }
            }

            // Advance past the matched span (skipped single-op matches still advance
            // to avoid an infinite loop on regex zero-width corner cases).
            (searchIdx, searchLen) = NextMatch(searchIdx + Math.Max(searchLen, 1));
        }

        if (!replaced) return null;

        CopyRange(streamBytes.Length);

        return result.ToArray();
    }

    /// <summary>Per-text-operator record for <see cref="TryCrossOperatorReplace"/>.</summary>
    private sealed class CrossTextOp
    {
        public string Text = "";
        public byte[] Bytes = Array.Empty<byte>();
        public bool IsHex;
        public int OpStart, OpEnd;
        public double TmA = 1, TmB, TmC, TmD = 1, TmTx, TmTy;
        public double CtmA = 1, CtmB, CtmC, CtmD = 1, CtmTx, CtmTy;
        public PdfDictionary? FontDict;
        public string? FontName;
        public Dictionary<int, string>? ToUnicode;
        public double FontSize = 12, Tc, KernSum;
        public int CharStart = -1;
        public bool TmPositioned;
        public int TmXTokStart, TmXTokEnd;
        public double TmXVal;
    }

    private bool TryReplaceTJArray(PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        double fontSize, out PdfArray newArr)
    {
        // First, concatenate all string parts to see if search text spans them.
        // Large negative kernings are treated as synthetic word-space, mirroring
        // the TextFragmentAbsorber reader — but only when the next PdfString
        // doesn't already begin with ' ', so we don't double-up the space.
        var fullText = new StringBuilder();
        var parts = new List<(int index, string text, bool isHex)>();

        bool NextStringStartsWithSpace(int from)
        {
            for (var j = from + 1; j < arr.Count; j++)
            {
                if (arr[j] is PdfString ps)
                {
                    // Decode (not raw byte compare) — CID/Type0 fonts map non-0x20
                    // bytes to the space glyph via ToUnicode/encoding tables.
                    var decodedPeek = DecodeString(ps.Value, toUnicode, fontDict, reader);
                    return decodedPeek.Length > 0 && decodedPeek[0] == ' ';
                }
            }
            return false;
        }

        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, toUnicode, fontDict, reader);
                parts.Add((i, decoded, s.IsHex));
                fullText.Append(decoded);
            }
            else if ((arr[i] is PdfInteger adj && adj.Value < -190)
                  || (arr[i] is PdfReal adjR && adjR.Value < -190))
            {
                if (!NextStringStartsWithSpace(i))
                    fullText.Append(' ');
            }
        }

        var combinedText = fullText.ToString();
        var normalizedCombined = NormalizeForSearch(combinedText);
        var normalizedSearch = NormalizeForSearch(search);
        if (!MatchesSearch(normalizedCombined, normalizedSearch))
        {
            newArr = arr;
            return false;
        }

        // Locate the match span so we can rewrite only the matched region and
        // keep everything after it intact. Preserving the suffix structure keeps
        // downstream glyph positions aligned with the original layout instead of
        // flattening the whole TJ (which shifts after-match glyphs when the
        // replacement width differs from the matched region width).
        int matchStart = _isRegex && _regexPattern is not null
            ? _regexPattern.Match(normalizedCombined).Index
            : normalizedCombined.IndexOf(normalizedSearch, StringComparison.Ordinal);
        int matchLen = _isRegex && _regexPattern is not null
            ? _regexPattern.Match(normalizedCombined).Length
            : normalizedSearch.Length;

        // Flat-string fallback (used when match position is unavailable or when
        // match covers the whole TJ — splitting adds no value). The TJ caller
        // owns the _replacementCount increment, so this path must NOT call
        // ApplyReplace (which would double-count).
        PdfArray FlatReplace()
        {
            var replacedText = _isRegex && _regexPattern is not null
                ? _regexPattern.Replace(normalizedCombined, replacement)
                : normalizedCombined.Replace(normalizedSearch, replacement, StringComparison.Ordinal);
            var replacedBytes = EncodeString(replacedText, toUnicode, fontDict);
            var useHex = parts.Count > 0 && parts[0].isHex;
            var flat = new PdfArray();
            flat.Add(new PdfString(replacedBytes, useHex));
            return flat;
        }

        if (matchStart < 0 || matchStart + matchLen > combinedText.Length)
        {
            newArr = FlatReplace();
            return true;
        }

        // Replace-all across multiple occurrences: the structured single-match
        // path below only rewrites the first match (keeping the suffix intact),
        // so when every match must be replaced and more than one is present,
        // fall back to a flat replacement that substitutes them all.
        bool multipleMatches = _isRegex && _regexPattern is not null
            ? _regexPattern.Matches(normalizedCombined).Count > 1
            : CountOccurrences(normalizedCombined, normalizedSearch) > 1;
        if (!ReplaceFirstOnly && multipleMatches)
        {
            newArr = FlatReplace();
            return true;
        }

        // Build a per-character map (combinedText char index → arr element index).
        // Must use the SAME rule as the concatenation loop above: a synthetic
        // space is only appended for a large negative kerning when the next
        // PdfString doesn't already start with a space.  Keep the two in sync.
        var charMap = new List<int>(combinedText.Length);
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString sm)
            {
                var n = DecodeString(sm.Value, toUnicode, fontDict, reader).Length;
                for (var k = 0; k < n; k++) charMap.Add(i);
            }
            else if ((arr[i] is PdfInteger ia && ia.Value < -190)
                  || (arr[i] is PdfReal ra && ra.Value < -190))
            {
                if (!NextStringStartsWithSpace(i))
                    charMap.Add(-1); // synthetic space
            }
        }

        // Prefix/suffix text (unchanged portions on either side of the match).
        var prefixText = combinedText.Substring(0, matchStart);
        var suffixStart = matchStart + matchLen;
        var suffixText = combinedText.Substring(suffixStart);

        // If suffix is empty, flat-replace is equivalent (nothing to push back).
        if (suffixText.Length == 0)
        {
            newArr = FlatReplace();
            return true;
        }

        // Map match boundaries back to the TJ-array coordinates (arrIdx + byte
        // offset inside that string) so the width-compensation helper can
        // identify the matched slice of each PdfString.
        int startArrIdx = charMap[matchStart];
        int endArrIdx = charMap[matchStart + matchLen - 1];
        // Offset-inside-string = count of prior chars mapped to the same arrIdx
        // before the match boundary.
        int CountCharsUpTo(int stop, int arrIdx)
        {
            var c = 0;
            for (var k = 0; k < stop; k++)
                if (charMap[k] == arrIdx) c++;
            return c;
        }
        int startOffset = CountCharsUpTo(matchStart, startArrIdx);
        int endOffset = CountCharsUpTo(matchStart + matchLen - 1, endArrIdx);

        // Emit:  [ (prefix + replacement)  <compensation-kerning>  (suffix) ]
        //
        // Two sub-strings for the unchanged + replaced portion and the tail, with
        // an optional integer kerning between them that compensates for the width
        // change caused by the replacement. This keeps the post-match glyph row
        // at its original X — the behaviour that tests using ReplaceAdjustment.None
        // depend on.  When the replacement width matches the original matched
        // region (including any within-match kerning) the compensation is zero
        // and the kerning element is omitted.
        var useHex2 = parts.Count > 0 && parts[0].isHex;

        // Compute the width change the replacement introduces, in PDF
        // text-space (1/1000 em) units, so we can emit it as a TJ kerning.
        int kernCompensation = ComputeTJReplaceKern(arr, startArrIdx, startOffset,
            endArrIdx, endOffset, replacement,
            toUnicode, fontDict, reader, fontSize);

        newArr = new PdfArray();

        // Emit the prefix by COPYING the original TJ-array elements before the
        // match — this preserves the original inter-element kerns (including
        // big-negative kerns that were synthesized into spaces in `combinedText`
        // for matching purposes). Only the matched region itself is replaced.
        // The string element containing the match start contributes its leading
        // bytes (chars before startOffset) followed by the replacement bytes.
        for (var i = 0; i < startArrIdx; i++)
            newArr.Add(arr[i]);

        // Build the prefix-and-replacement bytes from the matched string's
        // leading slice + the replacement text.
        byte[] preRepBytes;
        if (arr[startArrIdx] is PdfString startStr && startOffset > 0)
        {
            // Decode just the prefix bytes (chars before startOffset) and
            // re-encode together with the replacement.
            var preBytes = new byte[startOffset];
            Buffer.BlockCopy(startStr.Value, 0, preBytes, 0, startOffset);
            var preStr = DecodeString(preBytes, toUnicode, fontDict, reader);
            preRepBytes = EncodeString(preStr + replacement, toUnicode, fontDict);
        }
        else
        {
            preRepBytes = EncodeString(replacement, toUnicode, fontDict);
        }
        newArr.Add(new PdfString(preRepBytes, useHex2));

        if (kernCompensation != 0)
        {
            // Split a single large compensation into several smaller kernings
            // so none individually trips the reader's word-break heuristic
            // (adj < -190 becomes synthetic space). Using chunks of |adj| ≤ 180
            // keeps each step below the threshold while still summing to the
            // needed advance correction. Only negative (push-right) splitting
            // matters here — positive kernings never trigger the heuristic.
            const int SafeChunk = 180;
            int remaining = kernCompensation;
            if (remaining < 0)
            {
                while (remaining < -SafeChunk)
                {
                    newArr.Add(new PdfInteger(-SafeChunk));
                    remaining += SafeChunk;
                }
                if (remaining != 0) newArr.Add(new PdfInteger(remaining));
            }
            else
            {
                // Positive kernings are already safe (advance shrink).
                newArr.Add(new PdfInteger(remaining));
            }
        }

        // Emit the suffix by COPYING the original TJ-array elements after the
        // match end, rather than collapsing them into a single PdfString. This
        // preserves the original kerning values (including big-negative kerns
        // that were synthesized into spaces in `combinedText` for matching
        // purposes) so subsequent text stays at its original X position. The
        // first PdfString after the match needs its leading bytes trimmed
        // when the match ended partway through it.
        bool firstSuffixString = true;
        for (var i = endArrIdx; i < arr.Count; i++)
        {
            var el = arr[i];
            if (i == endArrIdx)
            {
                // For the string containing the match end, emit only the bytes
                // AFTER the match.
                if (el is not PdfString endStr) continue;
                int trimStart = endOffset + 1;
                if (trimStart >= endStr.Value.Length) continue;
                var tail = new byte[endStr.Value.Length - trimStart];
                Buffer.BlockCopy(endStr.Value, trimStart, tail, 0, tail.Length);
                newArr.Add(new PdfString(tail, endStr.IsHex));
                firstSuffixString = false;
            }
            else
            {
                newArr.Add(el);
                if (el is PdfString) firstSuffixString = false;
            }
        }

        // If no suffix elements were emitted (match ended exactly at the last
        // string with no tail bytes), append an empty PdfString so the array
        // structure remains valid. Otherwise, if we emitted only kerns and no
        // PdfString (rare — match consumed the final string and only kerns
        // followed), append an empty string.
        if (firstSuffixString)
            newArr.Add(new PdfString(System.Array.Empty<byte>(), useHex2));
        return true;
    }

    /// <summary>
    /// Compute a TJ kerning adjustment (in 1/1000 em units, PDF sign convention:
    /// positive = shift left, i.e. shrink advance) that compensates for the
    /// width change between the matched region in the original TJ and the
    /// replacement string.  Returns 0 when the widths match (or when metrics
    /// aren't available — caller then emits no kerning, preserving the current
    /// behaviour for the no-font-metrics fallback path).
    /// </summary>
    private int ComputeTJReplaceKern(PdfArray arr,
        int startArrIdx, int startOffset, int endArrIdx, int endOffset,
        string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        double fontSize)
    {
        if (fontDict is null || fontSize <= 0) return 0;
        FontMetrics? metrics;
        try { metrics = FontMetrics.FromFontDict(fontDict, reader); }
        catch { return 0; }
        if (metrics is null) return 0;

        // --- Original matched-region width ---
        // Walk [startArrIdx,endArrIdx] summing (a) per-string glyph widths of
        // chars inside the match span and (b) kerning items between strings in
        // the span.  Widths come from MeasureString on byte sub-slices so
        // Type1/TrueType width tables are honoured.
        double origAdvance = 0;
        for (var i = startArrIdx; i <= endArrIdx; i++)
        {
            var el = arr[i];
            if (el is PdfString ps)
            {
                var bytes = ps.Value;
                int byteStart = 0, byteEnd = bytes.Length;
                if (i == startArrIdx && startOffset > 0)
                    byteStart = Math.Min(startOffset, bytes.Length);
                if (i == endArrIdx && endOffset + 1 < bytes.Length)
                    byteEnd = endOffset + 1;
                if (byteEnd > byteStart)
                {
                    var slice = new byte[byteEnd - byteStart];
                    Buffer.BlockCopy(bytes, byteStart, slice, 0, slice.Length);
                    try { origAdvance += metrics.MeasureString(slice, fontSize); }
                    catch { return 0; }
                }
            }
            else if (i > startArrIdx && i < endArrIdx)
            {
                // Kerning inside the match span (both edges are strings).
                // Spec: TJ number operand is subtracted from current advance,
                // scaled by fontSize/1000.
                double adj = el switch
                {
                    PdfInteger pi => pi.Value,
                    PdfReal pr => pr.Value,
                    _ => 0
                };
                origAdvance += -adj * fontSize / 1000.0;
            }
        }

        // --- Replacement width ---
        double newAdvance;
        try
        {
            var repBytes = EncodeString(replacement, toUnicode, fontDict);
            newAdvance = metrics.MeasureString(repBytes, fontSize);
        }
        catch { return 0; }

        // Delta in PDF points → back to 1/1000 em.  Positive delta means the
        // replacement is narrower than the original; we need a NEGATIVE TJ
        // kerning so the following text is pushed forward to the original X.
        var deltaPt = origAdvance - newAdvance;
        if (Math.Abs(deltaPt) < 0.05) return 0; // below visible threshold
        var kern = (int)Math.Round(-deltaPt * 1000.0 / fontSize);
        // Clamp to the PDF spec's reasonable range to avoid pathological values
        // from bad metrics: ±10000 is already a massive advance delta (~10em).
        if (kern > 10000) kern = 10000;
        if (kern < -10000) kern = -10000;
        return kern;
    }

    private static string DecodeString(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader? reader = null)
    {
        // Delegate to TextAbsorber for consistent decoding (handles /Differences, named encodings, etc.)
        if (reader is not null)
            return TextAbsorber.DecodeStringPublic(bytes, toUnicode, fontDict, reader);

        if (toUnicode is not null)
        {
            var isCid = fontDict?.GetName("Subtype") == "Type0";
            var sb = new StringBuilder();
            if (isCid && bytes.Length >= 2)
            {
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    var code = (bytes[i] << 8) | bytes[i + 1];
                    sb.Append(toUnicode.TryGetValue(code, out var mapped) ? mapped : "\uFFFD");
                }
            }
            else
            {
                foreach (var b in bytes)
                    sb.Append(toUnicode.TryGetValue(b, out var mapped) ? mapped : ((char)b).ToString());
            }
            return sb.ToString();
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Normalize text for search comparison: apply NFKD decomposition to map
    /// Arabic presentation forms to base characters, matching TextFragmentAbsorber behavior.
    /// </summary>
    private static string NormalizeForSearch(string text)
    {
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
        return text.Normalize(System.Text.NormalizationForm.FormKD);
    }

    /// <summary>
    /// Check that the page-space Y of the current text matrix
    /// (Tm.ty × CTM[3] + CTM[5]) is within tolerance of <see cref="TargetY"/>.
    /// Returns true unconditionally when TargetY is unset (page-wide replace,
    /// the default behaviour). Only handles axis-aligned scale+translate CTMs;
    /// rotation/skew degrades to "no replacement" which is safer than
    /// page-wide for the per-fragment use case.
    /// </summary>
    private bool IsAtTargetY(double tmTx, double tmTy, double ctmB, double ctmD, double ctmTy)
    {
        if (TargetY is not double targetY) return true;
        // Full Y row of the CTM: the ctmB×tmTx cross-term matters on rotated pages
        // (page /Rotate seeds a 90°/270° CTM where Y comes from the text-space X).
        var pageY = ctmB * tmTx + ctmD * tmTy + ctmTy;
        return Math.Abs(pageY - targetY) <= TargetYTolerance;
    }

    /// <summary>
    /// Check that the page-space X of the current text-matrix origin
    /// (Tm.tx × CTM[0] + Tm.ty × CTM[2] + CTM[4]) is within tolerance of
    /// <see cref="TargetX"/>. Returns true unconditionally when TargetX is unset
    /// (the default — X is not scoped). Companion to <see cref="IsAtTargetY"/>.
    /// </summary>
    private bool IsAtTargetX(double tmTx, double tmTy, double ctmA, double ctmC, double ctmTx)
    {
        if (TargetX is not double targetX) return true;
        var pageX = ctmA * tmTx + ctmC * tmTy + ctmTx;
        return Math.Abs(pageX - targetX) <= TargetXTolerance;
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0
    };

    /// <summary>True when <paramref name="s"/> contains a right-to-left script
    /// character (Hebrew / Arabic + presentation forms). Such text is frequently
    /// stored in the content stream in VISUAL (reversed) order, so a logical-order
    /// search term won't match the decoded run directly.</summary>
    private static bool IsRtlSearch(string s)
    {
        foreach (var c in s)
            if ((c >= '֐' && c <= '׿')   // Hebrew
                || (c >= '؀' && c <= 'ۿ') // Arabic
                || (c >= 'יִ' && c <= '﻿')) // Hebrew/Arabic presentation forms
                return true;
        return false;
    }

    /// <summary>Resolve the search variant actually present in <paramref name="runText"/>.
    /// Returns the original <paramref name="search"/> when it matches directly; for an
    /// RTL term that doesn't (the run is stored visually reversed) returns the reversed
    /// term when THAT is present, so the visual slice can be matched and replaced.
    /// Regex searches are returned unchanged (RTL-regex is not modelled).</summary>
    private string ResolveRtlSearch(string runText, string search)
    {
        if (_isRegex || string.IsNullOrEmpty(search)) return search;
        if (runText.Contains(search, StringComparison.Ordinal)) return search;
        if (IsRtlSearch(search))
        {
            var rev = new string(search.Reverse().ToArray());
            if (runText.Contains(rev, StringComparison.Ordinal)) return rev;
        }
        return search;
    }

    /// <summary>Check if <paramref name="text"/> contains a match for the current search.</summary>
    private bool MatchesSearch(string text, string normalizedSearch)
    {
        if (ReplaceFirstOnly && _replacementCount > 0) return false;
        if (MatchAnyOperator) return true;
        if (MatchWholeOperator)
            return string.Equals(text, normalizedSearch, StringComparison.Ordinal);
        if (_isRegex && _regexPattern is not null)
            return _regexPattern.IsMatch(text);
        return text.Contains(normalizedSearch, StringComparison.Ordinal);
    }

    /// <summary>Replace matches in <paramref name="text"/> for the current search.
    /// Honours <see cref="ReplaceFirstOnly"/>.</summary>
    private string ApplyReplace(string text, string normalizedSearch, string replacement)
    {
        if (MatchAnyOperator)
        {
            _replacementCount++;
            return replacement;
        }
        if (_isRegex && _regexPattern is not null)
        {
            if (ReplaceFirstOnly)
            {
                var match = _regexPattern.Match(text);
                if (!match.Success) return text;
                _replacementCount++;
                return string.Concat(text.AsSpan(0, match.Index), replacement,
                    text.AsSpan(match.Index + match.Length));
            }
            _replacementCount += _regexPattern.Matches(text).Count;
            return _regexPattern.Replace(text, replacement);
        }
        if (ReplaceFirstOnly)
        {
            int idx = text.IndexOf(normalizedSearch, StringComparison.Ordinal);
            if (idx < 0) return text;
            _replacementCount++;
            return string.Concat(text.AsSpan(0, idx), replacement,
                text.AsSpan(idx + normalizedSearch.Length));
        }
        _replacementCount += CountOccurrences(text, normalizedSearch);
        return text.Replace(normalizedSearch, replacement, StringComparison.Ordinal);
    }

    private static string ConcatenateTJText(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
    {
        // Pre-scan: collect negative adjustments for dynamic word-break detection.
        // Same algorithm as TextFragmentAbsorber: if all adjustments are uniformly
        // large (character spacing), don't insert spaces.
        var sb = new StringBuilder();
        int lastLen = 0;
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, toUnicode, fontDict, reader);
                sb.Append(decoded);
                lastLen = decoded.Length;
            }
            else
            {
                double v = 0;
                if (arr[i] is PdfInteger ai) v = ai.Value;
                else if (arr[i] is PdfReal ar) v = ar.Value;
                if (v < -190 && lastLen != 1 && (sb.Length == 0 || sb[^1] != ' '))
                    sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    private static string EnsureStandardFont(PdfDictionary pageDict, PdfReader reader)
    {
        const string fallbackName = "_AsposePdfHlv";
        var resources = pageDict.Get("Resources") as PdfDictionary;
        resources ??= reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var fonts = resources.Get("Font") as PdfDictionary;
        fonts ??= reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        if (fonts.Get(fallbackName) is null)
        {
            var fontDict = new PdfDictionary();
            fontDict.Set("Type", new PdfName("Font"));
            fontDict.Set("Subtype", new PdfName("Type1"));
            fontDict.Set("BaseFont", new PdfName("Helvetica"));
            fontDict.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fonts.Set(fallbackName, fontDict);
        }
        return fallbackName;
    }

    private static double GetCurrentFontSize(List<(TokenKind kind, PdfObject obj, int startPos, int endPos)> operands)
    {
        // Font size is typically the second operand before a Tf operator,
        // but here we're in a Tj context. Default to 12 if unknown.
        return 12.0;
    }

    /// <summary>Resolve (creating if absent) the page/XObject's own /Resources /Font
    /// dictionary so a fallback font can be registered locally.</summary>
    private static PdfDictionary GetOrCreatePageFontDict(PdfDictionary pageDict, PdfReader reader)
    {
        var resources = pageDict.Get("Resources") as PdfDictionary;
        resources ??= reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var fonts = resources.Get("Font") as PdfDictionary;
        fonts ??= reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        return fonts;
    }

    /// <summary>Family name usable for a font lookup, derived from a /BaseFont by
    /// stripping a 6-char subset tag ("ABCDEF+Name").</summary>
    private static string? SourceFontFamily(PdfDictionary? fontDict)
    {
        var bf = fontDict?.GetName("BaseFont");
        if (string.IsNullOrEmpty(bf)) return null;
        var plus = bf!.IndexOf('+');
        if (plus == 6) bf = bf.Substring(plus + 1);
        return bf;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
            if (ch >= '　' && ch <= '鿿') return true;
        return false;
    }

    /// <summary>
    /// Embed a Type0/CID fallback font — from the source font's own family when
    /// installed, else a script-appropriate face — that contains the glyphs for
    /// <paramref name="text"/>, and return its resource name plus the 2-byte
    /// glyph-id string. Used when the source font can't encode a non-Latin1
    /// replacement (Cyrillic/CJK not in its subset) so the run renders AND stays
    /// searchable via the embedder's /ToUnicode CMap. Re-embeds the source font's
    /// own family when installed (e.g. TimesNewRoman / FangSong / SimHei), else a
    /// script-appropriate face. Returns null when no suitable TTF is available
    /// (caller keeps the Standard-14 Latin path).
    /// </summary>
    private static (string resName, byte[] hexIds)? TryEmbedCidFallback(
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict)
    {
        var candidates = new List<string?> { SourceFontFamily(sourceFontDict) };
        // SimSun is the reference's default Han substitute (its harness also has
        // FangSong available, yet CJK replacements read back as SimSun); a
        // FangSong result comes from the SOURCE font family candidate above.
        if (ContainsCjk(text))
        {
            candidates.Add("SimSun");
            candidates.Add("FangSong");
            candidates.Add("MS Gothic");
        }
        candidates.Add("Arial");
        candidates.Add("TimesNewRoman");

        // The non-ASCII characters that actually need a glyph in the fallback face.
        var need = text.Where(c => c > 0x7F).Distinct().ToArray();

        byte[]? ttf = null;
        var family = "Arial";
        byte[]? firstAvail = null;
        var firstFamily = "";
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            byte[]? t;
            try { t = FontRepository.GetTtfData(c!); } catch { t = null; }
            if (t is not { Length: > 12 }) continue;
            if (firstAvail is null) { firstAvail = t; firstFamily = c!; }
            // Prefer a face that actually covers every needed non-ASCII glyph —
            // the source family may be a Latin-only subset with no Hebrew/CJK.
            try
            {
                var gp = new GlyphOutlineParser(t);
                if (need.All(ch => gp.CMap.TryGetValue(ch, out var g) && g != 0))
                { ttf = t; family = c!; break; }
            }
            catch { /* unparseable — skip */ }
        }
        if (ttf is null) { ttf = firstAvail; family = firstFamily; } // best-effort
        if (ttf is null) return null;

        try
        {
            var fonts = GetOrCreatePageFontDict(pageDict, reader);
            var (resName, hexIds) = Type0FontEmbedder.Embed(fonts, ttf, family, text, stripSpacesInBaseFont: true);
            return (resName, hexIds);
        }
        catch { return null; }
    }

    /// <summary>
    /// Font-switch a TJ whose matched run needs a fallback font, PRESERVING the position
    /// of text that follows the match in the same TJ array. The matched run is re-emitted
    /// in the fallback (CID) font; the trailing run is re-anchored with an ABSOLUTE Tm at
    /// its ORIGINAL local X so a following fragment keeps its
    /// absolute position regardless of the replacement width. Handles the match-at-start
    /// case (no prefix text before the match in the same TJ); returns false otherwise so
    /// the caller flattens the whole TJ (unchanged behaviour).</summary>
    /// <summary>Same-font TJ split for ReplaceAdjustment.None: rewrite the matched span
    /// with the replacement re-encoded in the op's OWN font and re-anchor the trailing
    /// elements at their original absolute Tm X, so trailing text keeps its exact
    /// position regardless of the replacement's width. A compensating kern would keep
    /// the RENDERED position but mislead kern-blind consumers (the extraction rect clip
    /// and sub-run positions walk glyph widths only), so the split is preferred.
    /// Handles matches that start and end at string-element boundaries (the shape
    /// one-char-per-element producers emit); returns false otherwise so the caller
    /// falls back to the kern-compensated array rewrite.</summary>
    /// <summary>Whether a TJ split's re-anchored suffix must be followed by a
    /// line-matrix restore: look ahead for the next operator that consumes text
    /// position. Relative positioning (Td/TD/T*/'/") computes from the Tlm that was
    /// live at the rewritten op, so the restore is REQUIRED — without it the next
    /// Td-positioned line inherits the suffix X and shifts by the re-anchor delta.
    /// A bare show op (Tj/TJ) instead continues from the suffix's pen, so a restore
    /// would misplace it; an absolute Tm, BT/ET, or end-of-stream makes the
    /// clobbered Tlm irrelevant.</summary>
    private static bool NeedsTlmRestore(byte[] streamBytes, int fromPos)
    {
        var lexer = new PdfLexer(streamBytes) { Position = fromPos };
        try
        {
            while (true)
            {
                var token = lexer.NextToken();
                if (token.Kind == TokenKind.Eof) return false;
                if (token.Kind != TokenKind.Keyword) continue;
                switch (token.StringValue)
                {
                    case "Td": case "TD": case "T*": case "'": case "\"":
                        return true;
                    case "Tj": case "TJ": case "Tm": case "BT": case "ET":
                        return false;
                }
            }
        }
        catch { return false; }
    }

    private bool WriteAnchoredTJSplit(MemoryStream result, PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, double fontSize,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        PdfReader reader, bool restoreTlm)
    {
        if (fontDict is null || fontSize <= 0 || string.IsNullOrEmpty(search)) return false;
        FontMetrics? metrics;
        try { metrics = FontMetrics.FromFontDict(fontDict, reader); } catch { return false; }
        if (metrics is null) return false;

        // Per-element char-start in the concatenated text (mirroring ConcatenateTJText's
        // synthetic-space rule) and the local pen X before each element (kern-aware).
        var charStart = new int[arr.Count];
        var localXBefore = new double[arr.Count];
        var sb = new StringBuilder();
        double localX = 0; int lastLen = 0;
        for (int i = 0; i < arr.Count; i++)
        {
            charStart[i] = sb.Length; localXBefore[i] = localX;
            if (arr[i] is PdfString s)
            {
                var dec = DecodeString(s.Value, toUnicode, fontDict, reader);
                sb.Append(dec); lastLen = dec.Length;
                try { localX += metrics.MeasureString(s.Value, fontSize); } catch { return false; }
            }
            else
            {
                double v = arr[i] is PdfInteger ai ? ai.Value : arr[i] is PdfReal ar2 ? ar2.Value : 0;
                if (v < -190 && lastLen != 1 && (sb.Length == 0 || sb[^1] != ' ')) sb.Append(' ');
                localX += -v * fontSize / 1000.0;
            }
        }
        var concat = sb.ToString();
        int matchStart = concat.IndexOf(search, StringComparison.Ordinal);
        if (matchStart < 0) return false;
        int matchEnd = matchStart + search.Length;

        // The match must start AT a string element boundary and end right before one
        // (or at the concatenation's end) so whole elements are consumed.
        int headEnd = -1, firstSuffix = -1;
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not PdfString) continue;
            if (charStart[i] == matchStart) headEnd = i;
            if (firstSuffix < 0 && charStart[i] >= matchEnd)
            {
                if (charStart[i] != matchEnd) return false; // ends mid-element
                firstSuffix = i;
            }
        }
        if (headEnd < 0) return false;
        if (firstSuffix < 0 && matchEnd != concat.Length) return false;

        if (replacement.Length > 0
            && NeedsFontSwitch(replacement, toUnicode, fontDict, reader, allowGlyphFallback: false))
            return false;
        var replBytes = replacement.Length > 0
            ? EncodeString(replacement, toUnicode, fontDict)
            : Array.Empty<byte>();

        bool isHex = false;
        foreach (var el in arr)
            if (el is PdfString ps0) { isHex = ps0.IsHex; break; }

        // Head: untouched leading elements plus the re-encoded replacement, one TJ.
        var headArr = new PdfArray();
        for (int i = 0; i < headEnd; i++) headArr.Add(arr[i]);
        if (replBytes.Length > 0) headArr.Add(new PdfString(replBytes, isHex));
        if (headArr.Count > 0)
        {
            WriteTJArray(result, headArr);
            result.Write(" TJ "u8);
        }

        if (firstSuffix >= 0)
        {
            // The pen advances along the text matrix's X axis: origin' = Tm·(localX, 0).
            // Adding localX to tmTx alone breaks rotated matrices (0 b -c 0), where the
            // advance lands in the Y component through tmB.
            double advX = localXBefore[firstSuffix];
            string N(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
            // Leading space: the bytes copied before this op can end in a keyword
            // ("… Tm") with no trailing delimiter, and "Tm0 0.99 …" would lex as an
            // unknown operator.
            result.Write(Encoding.ASCII.GetBytes(
                $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx + tmA * advX)} {N(tmTy + tmB * advX)} Tm "));
            var suffix = new PdfArray();
            for (int i = firstSuffix; i < arr.Count; i++) suffix.Add(arr[i]);
            WriteTJArray(result, suffix);
            result.Write(" TJ"u8);
            // Restore the line matrix: the suffix's absolute Tm also moved Tlm, but any
            // following RELATIVE positioning (Td/TD/T*/'/") computes from the Tlm that
            // was live at this op. Without the restore, the next Td-positioned line
            // inherits the suffix X and every later line shifts by the re-anchor delta.
            if (restoreTlm)
                result.Write(Encoding.ASCII.GetBytes(
                    $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx)} {N(tmTy)} Tm"));
        }
        return true;
    }

    private bool WriteFontSwitchedTJSplit(MemoryStream result, PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, string? fontName, double fontSize,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        PdfReader reader, PdfDictionary pageDict, bool restoreTlm)
    {
        if (fontDict is null || fontSize <= 0 || string.IsNullOrEmpty(fontName)) return false;
        var metrics = FontMetrics.FromFontDict(fontDict, reader);
        if (metrics is null) return false;

        // Per-element char-start (in the concatenated text, mirroring ConcatenateTJText's
        // synthetic-space rule) and the local pen X before each element.
        var charStart = new int[arr.Count];
        var localXBefore = new double[arr.Count];
        var sb = new StringBuilder();
        double localX = 0; int lastLen = 0;
        for (int i = 0; i < arr.Count; i++)
        {
            charStart[i] = sb.Length; localXBefore[i] = localX;
            if (arr[i] is PdfString s)
            {
                var dec = DecodeString(s.Value, toUnicode, fontDict, reader);
                sb.Append(dec); lastLen = dec.Length;
                try { localX += metrics.MeasureString(s.Value, fontSize); } catch { return false; }
            }
            else
            {
                double v = arr[i] is PdfInteger ai ? ai.Value : arr[i] is PdfReal ar2 ? ar2.Value : 0;
                if (v < -190 && lastLen != 1 && (sb.Length == 0 || sb[^1] != ' ')) sb.Append(' ');
                localX += -v * fontSize / 1000.0;
            }
        }
        var concat = sb.ToString();
        int matchStart = concat.IndexOf(search, StringComparison.Ordinal);
        if (matchStart != 0)
        {
            // Only the match-AT-START case is handled here (else caller flattens).
            var nn = NormalizeForSearch(concat);
            if (nn.IndexOf(NormalizeForSearch(search), StringComparison.Ordinal) != 0) return false;
            matchStart = 0;
        }
        int matchEnd = matchStart + search.Length;

        // First STRING element beginning at/after the match end starts the trailing run to
        // preserve. No trailing text → nothing to re-anchor → let the caller flatten.
        int firstSuffix = -1;
        for (int i = 0; i < arr.Count; i++)
            if (arr[i] is PdfString && charStart[i] >= matchEnd) { firstSuffix = i; break; }
        if (firstSuffix < 0) return false;

        // Font-switched replacement for the matched run (drawn at the current Tm origin).
        var cid = EmbedTimesCidForRun(pageDict, reader, replacement, fontDict);
        if (cid is not { } c) return false;
        var fs = fontSize.ToString("0.####", CultureInfo.InvariantCulture);
        result.Write(Encoding.ASCII.GetBytes($"/{c.resName} {fs} Tf <"));
        result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(c.hexIds)));
        result.Write(Encoding.ASCII.GetBytes("> Tj "));

        // Re-anchor the trailing run at its original absolute Tm (original font), so it keeps
        // its pre-replacement position independent of the replacement width.
        double suffixLocalX = localXBefore[firstSuffix];
        string N(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
        result.Write(Encoding.ASCII.GetBytes(
            $"/{fontName} {fs} Tf {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx + suffixLocalX)} {N(tmTy)} Tm "));
        var suffix = new PdfArray();
        for (int i = firstSuffix; i < arr.Count; i++) suffix.Add(arr[i]);
        WriteTJArray(result, suffix);
        result.Write(" TJ"u8);
        // Restore the line matrix: the suffix's absolute Tm also moved Tlm, but any
        // following RELATIVE positioning (Td/TD/T*/'/") computes from the Tlm that
        // was live at this op. Without the restore, the next Td-positioned line
        // inherits the suffix X and every later line shifts by the re-anchor delta.
        if (restoreTlm)
            result.Write(Encoding.ASCII.GetBytes(
                $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx)} {N(tmTy)} Tm"));
        return true;
    }

    /// <summary>
    /// Write a font-switched replacement show operator. For non-Latin1 text
    /// (Cyrillic/CJK) embed a Type0 CID fallback so the run renders + is
    /// searchable; otherwise fall back to the Standard-14 Helvetica + Latin1 path
    /// (unchanged behaviour for Latin replacements). Restores the original font
    /// afterwards. <paramref name="showOp"/> is "Tj" or "'".
    /// </summary>
    private static void WriteFontSwitchedReplacement(MemoryStream result, string newText,
        PdfDictionary? currentFontDict, string? currentFontName, double currentFontSize,
        PdfDictionary pageDict, PdfReader reader, string showOp, bool allowGlyphFallback = false)
    {
        var fs = currentFontSize.ToString("F1", CultureInfo.InvariantCulture);
        if (newText.Any(c => c > 0xFF))
        {
            var cid = TryEmbedCidFallback(pageDict, reader, newText, currentFontDict);
            if (cid is { } c)
            {
                result.Write(Encoding.ASCII.GetBytes($"/{c.resName} {fs} Tf <"));
                result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(c.hexIds)));
                result.Write(Encoding.ASCII.GetBytes($"> {showOp} /{currentFontName} {fs} Tf"));
                return;
            }
        }
        // Latin replacement whose glyphs are absent from the source subset font: substitute
        // the whole run in a Times New Roman Type0/CID subset, so the
        // missing glyphs render AND the run stays searchable via the embedder's /ToUnicode.
        else if (allowGlyphFallback && SimpleFontMissingGlyphChars(currentFontDict, reader, newText).Length > 0)
        {
            var times = EmbedTimesCidForRun(pageDict, reader, newText, currentFontDict);
            if (times is { } t)
            {
                result.Write(Encoding.ASCII.GetBytes($"/{t.resName} {fs} Tf <"));
                result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(t.hexIds)));
                result.Write(Encoding.ASCII.GetBytes($"> {showOp} /{currentFontName} {fs} Tf"));
                return;
            }
        }
        // Standard-font substitution for a run the source subset can't faithfully show (its
        // glyph is present by width but absent from the font's ToUnicode, so the encoding
        // can't be confirmed). Record the family the fragment should REPORT for the default
        // no-character behaviour (source family if installed, else Times New Roman). This is
        // a REPORT ONLY — the glyphs stay on this cheap Standard-14 path (no font embedded,
        // file size unaffected), and only the TextFragment.Text setter reads the record; the
        // facade ReplaceText path never surfaces it, so its output is byte-for-byte unchanged.
        if (allowGlyphFallback && IsEmbeddedSimpleFont(currentFontDict, reader))
            RecordSwitchedFont(ResolveReportedFallbackFamily(currentFontDict));
        var fallbackFont = EnsureStandardFont(pageDict, reader);
        result.Write(Encoding.ASCII.GetBytes($"/{fallbackFont} {fs} Tf "));
        var latin = Encoding.Latin1.GetBytes(newText);
        WriteStringOperand(result, latin, false);
        result.Write(Encoding.ASCII.GetBytes($" {showOp} /{currentFontName} {fs} Tf"));
    }

    /// <summary>
    /// Build a reverse map from Unicode characters to CID codes, including NFKD-decomposed
    /// variants so base Arabic characters (e.g., U+0627 Alef) can map to presentation form
    /// codes (e.g., U+FE8E → code N) that exist in the font's ToUnicode CMap.
    /// </summary>
    /// <remarks>
    /// Two-pass approach: first adds single-character NFKD decompositions (e.g., U+FEF3 → U+064A),
    /// then multi-character ones (e.g., U+FE8B → U+064A + U+0654). This ensures that plain
    /// presentation forms (like Yeh U+FEF1-FEF4) are preferred over compound forms
    /// (like Yeh-with-Hamza U+FE89-FE8C) when both decompose to the same base character.
    /// </remarks>
    private static Dictionary<string, int> BuildReverseMap(Dictionary<int, string> toUnicode)
    {
        var reverseMap = new Dictionary<string, int>();

        // Pass 0: direct Unicode string → code (no decomposition)
        foreach (var (code, unicode) in toUnicode)
            reverseMap.TryAdd(unicode, code);

        // Pass 1: single-char NFKD decompositions (plain presentation forms, e.g. U+FEF3 → U+064A)
        foreach (var (code, unicode) in toUnicode)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch < '\uFB50' || ch > '\uFDFF') && (ch < '\uFE70' || ch > '\uFEFF')) continue;

            var decomposed = unicode.Normalize(System.Text.NormalizationForm.FormKD);
            if (decomposed.Length == 1)
                reverseMap.TryAdd(decomposed, code);
        }

        // Pass 2: multi-char NFKD decompositions (compound forms, e.g. U+FE8B → U+064A + U+0654)
        // Only adds base characters that weren't already mapped in pass 1.
        foreach (var (code, unicode) in toUnicode)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch < '\uFB50' || ch > '\uFDFF') && (ch < '\uFE70' || ch > '\uFEFF')) continue;

            var decomposed = unicode.Normalize(System.Text.NormalizationForm.FormKD);
            if (decomposed.Length > 1)
            {
                foreach (var dc in decomposed)
                    reverseMap.TryAdd(dc.ToString(), code);
            }
        }

        return reverseMap;
    }

    private static bool NeedsFontSwitch(string text, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader? reader = null, bool allowGlyphFallback = false)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";

        // CID/Type0 fonts use 2-byte character codes.  If there is no ToUnicode
        // map we cannot build a reverse map, so we must switch to a standard font
        // for any replacement text.
        if (isCid && toUnicode is null)
            return true;

        // A simple (non-CID) font with NO ToUnicode is single-byte WinAnsi/Latin1:
        // it physically cannot encode a character outside Latin-1 (> 0xFF), so a
        // Cyrillic/Hebrew/CJK replacement must switch fonts (→ CID fallback in
        // WriteFontSwitchedReplacement). Without this, the reverse-map check below
        // is skipped and EncodeString silently Latin1-encodes the char to '?'.
        if (!isCid && toUnicode is null && text.Any(ch => ch > 0xFF))
            return true;

        if (toUnicode is not null)
        {
            var reverseMap = BuildReverseMap(toUnicode);

            if (text.Any(ch => !reverseMap.ContainsKey(ch.ToString())))
                return true;
        }

        // Base-encoded simple subset that lacks an embedded glyph for a replacement char
        // (a Type1/TrueType subset embeds only the glyphs it draws; the /Widths entry is 0
        // for the rest). Without a switch the missing glyphs render blank. Fires only for a
        // plain base encoding, so /Differences fonts fall through to the remap check below.
        // Gated to the facade ReplaceText path (allowGlyphFallback) — the TextFragment.Text
        // setter manages the font itself, so an auto-switch there would shift following text.
        if (allowGlyphFallback && !isCid && SimpleFontMissingGlyphChars(fontDict, reader, text).Length > 0)
            return true;

        // Non-CID fonts with /Encoding containing /Differences: if any replacement
        // character's Latin1 byte value is remapped by the Differences array, the
        // round-trip will produce wrong glyphs — switch to a standard font.
        if (!isCid && fontDict is not null && reader is not null)
        {
            var encodingObj = fontDict.Get("Encoding");
            PdfDictionary? encodingDict = null;
            if (encodingObj is PdfDictionary ed) encodingDict = ed;
            else if (encodingObj is not null) encodingDict = reader.ResolveDict(encodingObj);

            if (encodingDict is not null)
            {
                var diffsArr = encodingDict.Get("Differences") as PdfArray;
                if (diffsArr is null)
                {
                    var resolved = reader.Resolve(encodingDict.Get("Differences"));
                    diffsArr = resolved as PdfArray;
                }
                if (diffsArr is not null)
                {
                    // Build set of byte codes that are remapped by Differences
                    var remappedCodes = new HashSet<int>();
                    var code = 0;
                    for (var i = 0; i < diffsArr.Count; i++)
                    {
                        if (diffsArr[i] is PdfInteger pi)
                            code = (int)pi.Value;
                        else if (diffsArr[i] is PdfName)
                            remappedCodes.Add(code++);
                    }

                    // Check if any replacement character's Latin1 byte is remapped
                    foreach (var ch in text)
                    {
                        var b = (int)(ch <= 0xFF ? ch : 0x3F);
                        if (remappedCodes.Contains(b))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Characters in <paramref name="text"/> for which a base-encoded simple (non-CID)
    /// subset font has NO embedded glyph. A subset embeds only the glyphs it draws and
    /// zeroes the /Widths entry (or omits the code from /FirstChar../LastChar) for the
    /// rest — so a width of 0 / an out-of-range code marks an absent glyph. Only applied
    /// to a plain base encoding (WinAnsi/Standard/MacRoman name, no /Differences); a
    /// /Differences font is left to the remap check in <see cref="NeedsFontSwitch"/> so
    /// this never over-fires. Returns empty when coverage can't be judged (no /Widths,
    /// Type0, unknown encoding) — never guess a switch. Space is ignored (word gap, not
    /// a drawn glyph).
    /// </summary>
    /// <summary>
    /// True when the font is an EMBEDDED (FontFile/2/3) simple (non-Type0) font — a subset
    /// that embeds only the glyphs it draws. When such a font can't faithfully show a
    /// replacement char, the default no-character behaviour substitutes and REPORTS a
    /// fallback face. Used only to gate that report (not the rendering), so it deliberately
    /// covers /Differences subsets too; a non-embedded system-font reference is excluded
    /// (its real installed face has the glyph, so no substitution is reported).
    /// </summary>
    private static bool IsEmbeddedSimpleFont(PdfDictionary? fontDict, PdfReader? reader)
    {
        if (fontDict is null || reader is null) return false;
        if (fontDict.GetName("Subtype") == "Type0") return false;
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        return descriptor is not null &&
            (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
             || descriptor.Get("FontFile3") is not null);
    }

    /// <summary>The family the fragment should REPORT after a default no-character
    /// substitution: the source font's own family when it's installed (kept, like an
    /// Arial subset → Arial), else Times New Roman (source not available to expand).</summary>
    private static string ResolveReportedFallbackFamily(PdfDictionary? fontDict)
    {
        var src = SourceFontFamily(fontDict);
        if (!string.IsNullOrEmpty(src))
        {
            try { var t = FontRepository.GetTtfData(src!); if (t is { Length: > 12 }) return src!; }
            catch { /* not installed → fall through to Times */ }
        }
        return "TimesNewRoman";
    }

    private static char[] SimpleFontMissingGlyphChars(PdfDictionary? fontDict, PdfReader? reader, string text)
    {
        if (fontDict is null || reader is null) return Array.Empty<char>();
        if (fontDict.GetName("Subtype") == "Type0") return Array.Empty<char>();

        // Only an EMBEDDED font's /Widths tell the truth about glyph presence: a subset
        // embeds only the glyphs it draws (0-width for the rest). A NON-embedded font
        // (a system-font reference like "Arial,Bold") often ships /Widths only for the
        // codes it happens to use, but the real installed face still has every glyph — a
        // 0 width there is missing metadata, not a missing glyph. So gate on an embedded
        // FontFile/FontFile2/FontFile3; otherwise never treat a 0 width as absent.
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        bool embedded = descriptor is not null &&
            (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
             || descriptor.Get("FontFile3") is not null);
        if (!embedded) return Array.Empty<char>();

        // Only a plain base-encoding name (no Differences) has a code==WinAnsi-byte
        // mapping we can trust here.
        var enc = fontDict.Get("Encoding");
        string? encName = enc as PdfName is { } pn ? pn.Value
            : (reader.ResolveDict(enc)?.Get("Differences") is null
                ? reader.ResolveDict(enc)?.GetName("BaseEncoding")
                : null);
        if (encName is not ("WinAnsiEncoding" or "StandardEncoding" or "MacRomanEncoding"))
            return Array.Empty<char>();

        if (reader.Resolve(fontDict.Get("Widths")) is not PdfArray widths) return Array.Empty<char>();
        if (reader.Resolve(fontDict.Get("FirstChar")) is not PdfInteger fc) return Array.Empty<char>();
        int firstChar = (int)fc.Value;
        int lastChar = firstChar + widths.Count - 1;

        var missing = new List<char>();
        foreach (var ch in text.Distinct())
        {
            if (ch == ' ') continue;
            // Map char → single-byte code. ASCII (0x20-0x7E) is identity under WinAnsi/
            // Standard/MacRoman; Latin-1 (0xA0-0xFF) ≈ WinAnsi. Anything else can't be a
            // single-byte code here, so treat as absent-from-this-font.
            if (ch >= 0x100) { missing.Add(ch); continue; }
            int code = ch;
            if (code < firstChar || code > lastChar) { missing.Add(ch); continue; }
            var w = reader.Resolve(widths[code - firstChar]);
            double width = w is PdfInteger wi ? wi.Value : w is PdfReal wr ? wr.Value : 0;
            if (width == 0) missing.Add(ch);
        }
        return missing.ToArray();
    }

    /// <summary>
    /// Embed Times New Roman as a Type0/Identity-H subset covering <paramref name="text"/>
    /// and return its resource name + 2-byte glyph-id string. Used to font-switch a run
    /// whose source subset lacks glyphs for (Latin) replacement chars by substituting
    /// the whole run in Times. Returns null when Times isn't resolvable.
    /// </summary>
    private static (string resName, byte[] hexIds)? EmbedTimesCidForRun(
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict)
    {
        // Prefer re-embedding the SOURCE font's own family when it's installed (keep the
        // family, e.g. an Arial subset → Arial), else fall back to Times New Roman
        // (the source family isn't available to expand, e.g. Bookman/Folio not installed).
        byte[]? ttf = null;
        string family = "TimesNewRoman";
        foreach (var fam in new[] { SourceFontFamily(sourceFontDict), "TimesNewRoman", "Times New Roman", "Times" })
        {
            if (string.IsNullOrEmpty(fam)) continue;
            byte[]? t;
            try { t = FontRepository.GetTtfData(fam!); } catch { t = null; }
            if (t is { Length: > 12 }) { ttf = t; family = fam!; break; }
        }
        if (ttf is null) return null;
        try
        {
            var fonts = GetOrCreatePageFontDict(pageDict, reader);
            var emb = Type0FontEmbedder.Embed(fonts, ttf, family, text, stripSpacesInBaseFont: true);
            RecordSwitchedFont(family);
            return emb;
        }
        catch { return null; }
    }

    private static byte[] EncodeString(string text, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict)
    {
        if (toUnicode is not null)
        {
            // Build reverse map with NFKD fallback for Arabic presentation forms
            var reverseMap = BuildReverseMap(toUnicode);

            // If the font stores Arabic presentation forms in its ToUnicode map,
            // the content stream uses visual (reversed) order for RTL text.
            // Reverse RTL replacement text to match this visual-order convention.
            if (HasArabicPresentationForms(toUnicode) && IsArabicText(text))
            {
                var chars = text.ToCharArray();
                Array.Reverse(chars);
                text = new string(chars);
            }

            var isCid = fontDict?.GetName("Subtype") == "Type0";
            var result = new List<byte>();

            foreach (var ch in text)
            {
                var s = ch.ToString();
                if (reverseMap.TryGetValue(s, out var code))
                {
                    if (isCid)
                    {
                        result.Add((byte)((code >> 8) & 0xFF));
                        result.Add((byte)(code & 0xFF));
                    }
                    else
                    {
                        result.Add((byte)(code & 0xFF));
                    }
                }
                else
                {
                    // Fallback: use character value directly
                    if (isCid)
                    {
                        result.Add((byte)((ch >> 8) & 0xFF));
                        result.Add((byte)(ch & 0xFF));
                    }
                    else
                    {
                        result.Add((byte)ch);
                    }
                }
            }

            return result.ToArray();
        }

        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>
    /// Check if a ToUnicode map contains Arabic presentation form characters,
    /// indicating the font stores RTL text in visual (reversed) order.
    /// </summary>
    private static bool HasArabicPresentationForms(Dictionary<int, string> toUnicode)
    {
        foreach (var unicode in toUnicode.Values)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if text contains Arabic/Hebrew characters that would be rendered RTL.
    /// </summary>
    private static bool IsArabicText(string text)
    {
        foreach (var ch in text)
        {
            // Arabic block (U+0600-U+06FF), Arabic Supplement (U+0750-U+077F),
            // Arabic Extended (U+08A0-U+08FF), Arabic Presentation Forms
            if ((ch >= '\u0600' && ch <= '\u06FF') || (ch >= '\u0750' && ch <= '\u077F') ||
                (ch >= '\u08A0' && ch <= '\u08FF') ||
                (ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                return true;
        }
        return false;
    }

    private static void WriteStringOperand(MemoryStream ms, byte[] data, bool isHex)
    {
        if (isHex)
        {
            ms.WriteByte((byte)'<');
            ms.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(data)));
            ms.WriteByte((byte)'>');
        }
        else
        {
            ms.WriteByte((byte)'(');
            foreach (var b in data)
            {
                if (b == '(' || b == ')' || b == '\\')
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte(b);
                }
                else if (b == 0x0D) // CR — escape to prevent PdfLexer CR→LF normalization
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte((byte)'r');
                }
                else if (b == 0x0A) // LF
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte((byte)'n');
                }
                else
                {
                    ms.WriteByte(b);
                }
            }
            ms.WriteByte((byte)')');
        }
    }

    private static void WriteTJArray(MemoryStream ms, PdfArray arr)
    {
        ms.WriteByte((byte)'[');
        for (var i = 0; i < arr.Count; i++)
        {
            if (i > 0) ms.WriteByte((byte)' ');
            switch (arr[i])
            {
                case PdfString s:
                    WriteStringOperand(ms, s.Value, s.IsHex);
                    break;
                case PdfInteger n:
                    ms.Write(Encoding.ASCII.GetBytes(n.Value.ToString()));
                    break;
                case PdfReal r:
                    ms.Write(Encoding.ASCII.GetBytes(r.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }
        ms.WriteByte((byte)']');
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    private static PdfArray ParseContentArrayWithPositions(PdfLexer lexer, out int endPos)
    {
        var array = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof)
            {
                endPos = (int)lexer.Position;
                return array;
            }
            switch (t.Kind)
            {
                case TokenKind.Integer: array.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: array.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: array.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: array.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: array.Add(new PdfName(t.StringValue!)); break;
            }
        }
    }

    private static byte[] CombineStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1; // +1 for separator newline
        var result = new byte[total];
        var offset = 0;
        foreach (var s in streams)
        {
            s.CopyTo(result, offset);
            offset += s.Length;
            result[offset++] = (byte)'\n';
        }
        return result;
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    /// <summary>
    /// Make ResolveFonts accessible for text replacement.
    /// </summary>
    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);
}
