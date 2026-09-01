using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
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

        var tjRule = TjBreakRuleOf(arr, toUnicode, fontDict, reader);
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, toUnicode, fontDict, reader);
                parts.Add((i, decoded, s.IsHex));
                fullText.Append(decoded);
            }
            else if ((arr[i] is PdfInteger adj && tjRule.Breaks(adj.Value))
                  || (arr[i] is PdfReal adjR && tjRule.Breaks(adjR.Value)))
            {
                if (fullText.Length > 0 && fullText[^1] != ' ')
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
        // Must use the SAME rule as the concatenation loop above — keep in sync.
        var charMap = new List<int>(combinedText.Length);
        var lastMapCh = '\0';
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString sm)
            {
                var decMap = DecodeString(sm.Value, toUnicode, fontDict, reader);
                for (var k = 0; k < decMap.Length; k++) charMap.Add(i);
                if (decMap.Length > 0) lastMapCh = decMap[^1];
            }
            else if ((arr[i] is PdfInteger ia && tjRule.Breaks(ia.Value))
                  || (arr[i] is PdfReal ra && tjRule.Breaks(ra.Value)))
            {
                if (lastMapCh != '\0' && lastMapCh != ' ')
                {
                    charMap.Add(-1); // synthetic space
                    lastMapCh = ' ';
                }
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
            // (adj ≤ −130 becomes synthetic space). Using chunks of |adj| ≤ 120
            // keeps each step below the threshold while still summing to the
            // needed advance correction. Only negative (push-right) splitting
            // matters here — positive kernings never trigger the heuristic.
            const int SafeChunk = 120;
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
