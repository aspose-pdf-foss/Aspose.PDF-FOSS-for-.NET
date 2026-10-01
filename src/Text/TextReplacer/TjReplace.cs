using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    private PdfArray? TryReplaceTJArray(PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        double fontSize)
    {
        PdfArray? newArr = default;
        var tj = new TjReplaceState();
        tj.arr = arr;
        tj.search = search;
        tj.replacement = replacement;
        tj.toUnicode = toUnicode;
        tj.fontDict = fontDict;
        tj.reader = reader;
        tj.fontSize = fontSize;
        var ok = TryReplaceTJArrayCore(tj);
        newArr = tj.newArr;
        return (ok) ? newArr : null;
    }

    /// <summary>The body of the TJ-array replacement, on its own state.</summary>
    private bool TryReplaceTJArrayCore(TjReplaceState tj)
    {
        ConcatTjArrayText(tj);

        tj.combinedText = tj.fullText.ToString();
        tj.normalizedCombined = NormalizeForSearch(tj.combinedText);
        tj.normalizedSearch = NormalizeForSearch(tj.search);
        if (!MatchesSearch(tj.normalizedCombined, tj.normalizedSearch))
        {
            tj.newArr = tj.arr;
            return false;
        }

        tj.matchStart = _isRegex && _regexPattern is not null
            ? _regexPattern.Match(tj.normalizedCombined).Index
            : tj.normalizedCombined.IndexOf(tj.normalizedSearch, StringComparison.Ordinal);
        tj.matchLen = _isRegex && _regexPattern is not null
            ? _regexPattern.Match(tj.normalizedCombined).Length
            : tj.normalizedSearch.Length;

        if (tj.matchStart < 0 || tj.matchStart + tj.matchLen > tj.combinedText.Length)
        {
            tj.newArr = FlatReplace(tj);
            return true;
        }

        tj.multipleMatches = _isRegex && _regexPattern is not null
            ? _regexPattern.Matches(tj.normalizedCombined).Count > 1
            : CountOccurrences(tj.normalizedCombined, tj.normalizedSearch) > 1;
        if (!ReplaceFirstOnly && tj.multipleMatches)
        {
            tj.newArr = FlatReplace(tj);
            return true;
        }

        tj.charMap = new List<int>(tj.combinedText.Length);
        BuildTjCharMap(tj);

        tj.prefixText = tj.combinedText.Substring(0, tj.matchStart);
        tj.suffixStart = tj.matchStart + tj.matchLen;
        tj.suffixText = tj.combinedText.Substring(tj.suffixStart);

        // If suffix is empty, flat-replace is equivalent (nothing to push back).
        if (tj.suffixText.Length == 0)
        {
            tj.newArr = FlatReplace(tj);
            return true;
        }

        tj.startArrIdx = tj.charMap[tj.matchStart];
        tj.endArrIdx = tj.charMap[tj.matchStart + tj.matchLen - 1];
        tj.startOffset = CountCharsUpTo(tj, tj.matchStart, tj.startArrIdx);
        tj.endOffset = CountCharsUpTo(tj, tj.matchStart + tj.matchLen - 1, tj.endArrIdx);

        tj.useHex2 = tj.parts.Count > 0 && tj.parts[0].isHex;

        tj.kernCompensation = ComputeTJReplaceKern(tj.arr, tj.startArrIdx, tj.startOffset,
            tj.endArrIdx, tj.endOffset, tj.replacement,
            tj.toUnicode, tj.fontDict, tj.reader, tj.fontSize);

        EmitTjPrefixAndReplacement(tj);

        EmitTjSuffixElements(tj);

        // If no suffix elements were emitted (match ended exactly at the last
        // string with no tail bytes), append an empty PdfString so the array
        // structure remains valid. Otherwise, if we emitted only kerns and no
        // PdfString (rare — match consumed the final string and only kerns
        // followed), append an empty string.
        if (tj.firstSuffixString)
            tj.newArr.Add(new PdfString(System.Array.Empty<byte>(), tj.useHex2));
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

    /// <summary>
    /// Synthetic-space eligibility for a TJ array — MUST stay in sync with
    /// TextFragmentAbsorber/TextAbsorber: one space per
    /// adjustment ≤ −130/1000 em iff the array is "armed" — any ≥2-glyph piece,
    /// or any glyph that is NOT an uppercase letter or punctuation (font type is
    /// irrelevant) — and not the letter-tracking shape (>10 pieces all
    /// single-glyph). The only per-gap suppression is a space glyph immediately
    /// left of the gap.
    /// </summary>
    private static bool TjSynthEligible(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
    {
        var isType0 = fontDict?.GetName("Subtype") == "Type0";
        var pieces = 0;
        var multiGlyph = false;
        foreach (var el in arr)
            if (el is PdfString ps0)
            {
                pieces++;
                if (ps0.Value.Length >= (isType0 ? 4 : 2)) multiGlyph = true;
            }
        if (pieces < 2) return false;
        // Letter-tracking shape: >10 pieces, all single-glyph → collapse.
        if (pieces > 10 && !multiGlyph) return false;
        if (multiGlyph) return true;
        foreach (var el in arr)
        {
            if (el is not PdfString ps) continue;
            var dec = DecodeString(ps.Value, toUnicode, fontDict, reader);
            if (dec.Length >= 2) return true;
            foreach (var c in dec)
                if (!char.IsUpper(c) && !char.IsPunctuation(c))
                    return true;
        }
        return false;
    }

    /// <summary>Per-array TJ word-break rule for the REPLACE paths. DELIBERATELY
    /// NARROWER than the absorbers' (which add median-relative letter-tracking
    /// breaks and backward-jump breaks): only the corpus-validated armed −130
    /// rule. The absorbers' extra synthetic spaces sit at spliced element
    /// boundaries, and the replace/kern-compensation path re-anchors trailing
    /// text wrongly around them (deleting one bracketed token slid the next
    /// token onto the deleted token's X). A search string containing such a
    /// space simply no-ops here (not found) — safe; a wrong re-anchor moves
    /// text.</summary>
    internal readonly struct TjBreakRule
    {
        public readonly bool Eligible;
        public TjBreakRule(bool eligible) { Eligible = eligible; }
        public bool Breaks(double v) => Eligible && v <= -130;
    }

    private static TjBreakRule TjBreakRuleOf(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
        => new TjBreakRule(TjSynthEligible(arr, toUnicode, fontDict, reader));

    private static string ConcatenateTJText(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
    {
        // Armed-array synthetic-space rule (see TjBreakRule for why the
        // absorbers' wider rules are not mirrored here).
        var rule = TjBreakRuleOf(arr, toUnicode, fontDict, reader);
        var sb = new StringBuilder();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                sb.Append(DecodeString(s.Value, toUnicode, fontDict, reader));
            }
            else
            {
                double v = 0;
                if (arr[i] is PdfInteger ai) v = ai.Value;
                else if (arr[i] is PdfReal ar) v = ar.Value;
                if (rule.Breaks(v) && sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
            }
        }
        return sb.ToString();
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
}
