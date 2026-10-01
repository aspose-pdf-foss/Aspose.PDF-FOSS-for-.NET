using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Rewrite a matched TJ array: the empty, anchored, font-switched and in-place replacements.</summary>
    private void RewriteShownArray(ReflowReplaceState rr, PdfArray arr, string tjNormalizedOrig, string tjNormalizedSearch, int endPos, byte[] streamBytes, string search, string replacement, PdfDictionary pageDict, PdfReader reader)
    {
        var tjReplacedText = MatchAnyOperator
            ? replacement
            : tjNormalizedOrig.Replace(tjNormalizedSearch, replacement, StringComparison.Ordinal);
        rr.result.Write(streamBytes, rr.lastWritePos, rr.operands[0].startPos - rr.lastWritePos);

        if (tjReplacedText.Length == 0)
        {
            // Full deletion: drop the entire TJ operator so no
            // empty text-showing operator remains (which would
            // still be re-extracted as a zero-length fragment). When
            // KeepEmptyShowOperator is set (form-XObject deletion),
            // retain an empty `() Tj` so the emptied fragment stays
            // re-extractable as "" (see the Tj branch above).
            if (KeepEmptyShowOperator)
                rr.result.Write("() Tj"u8);
            _replacementCount++;
        }
        else if (AnchorTrailingOnReplace && replacement.Length == 0
            // Pure deletion of a proper substring: hoisted above the
            // font-switch branch, which a deletion never needs (the
            // split re-emits the surviving run from its original
            // bytes) but whose empty-replacement embed fails and
            // flattens the survivor at the op start.
            && tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal)
            && WriteAnchoredTJSplit(rr.result, arr, search, replacement,
                rr.currentToUnicode, rr.currentFontDict, rr.currentFontSize,
                rr.tmA, rr.tmB, rr.tmC, rr.tmD, rr.tmTx, rr.tmTy, rr.tcSpacing, rr.twSpacing, reader,
                NeedsTlmRestore(streamBytes, endPos)))
        {
            _replacementCount++;
        }
        else if (ForcedCidFallbackFamily is not null || NeedsFontSwitch(tjReplacedText, rr.currentToUnicode, rr.currentFontDict, reader, AllowSubsetGlyphFallback))
        {
            // Preserve any trailing text's position: split the TJ, font-switch
            // only the matched run, re-anchor the rest at its original absolute
            // Tm. Only under an anchored mode (None / AdjustSpaceWidth) and a
            // single occurrence — a reflowing replacement re-encodes the whole
            // run instead, closing the gap when the replacement is narrower.
            // Falls back to flattening the whole TJ when the split doesn't apply.
            var tjFsSingleOccurrence =
                tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                    == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal);
            if (!(tjFsSingleOccurrence
                  && WriteFontSwitchedTJSplit(rr.result, arr, search, replacement,
                    rr.currentToUnicode, rr.currentFontDict, rr.currentFontName, rr.currentFontSize,
                    rr.tmA, rr.tmB, rr.tmC, rr.tmD, rr.tmTx, rr.tmTy, rr.tcSpacing, rr.twSpacing, reader, pageDict,
                    NeedsTlmRestore(streamBytes, endPos),
                    anchored: AnchorTrailingOnReplace)))
                WriteFontSwitchedReplacement(rr.result, tjReplacedText, rr.currentFontDict,
                    rr.currentFontName, rr.currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
            _replacementCount++;
        }
        else if (AnchorTrailingOnReplace
            // The anchored split rewrites ONE occurrence and re-anchors
            // everything after it verbatim, so it must not run when the
            // array holds the search more than once (the later matches
            // would survive un-replaced inside the re-anchored tail).
            // A pure deletion (empty replacement) takes the split too:
            // the kern-compensated array rewrite keeps the RENDERED pen
            // in place, but consumers that walk glyph widths only would
            // read the trailing run shifted into the deleted span.
            && tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal)
            && WriteAnchoredTJSplit(rr.result, arr, search, replacement,
                rr.currentToUnicode, rr.currentFontDict, rr.currentFontSize,
                rr.tmA, rr.tmB, rr.tmC, rr.tmD, rr.tmTx, rr.tmTy, rr.tcSpacing, rr.twSpacing, reader,
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
            rr.currentToUnicode, rr.currentFontDict, reader, rr.currentFontSize) is { } newArr)
        {
            WriteTJArray(rr.result, newArr);
            rr.result.Write(" TJ"u8);
            _replacementCount++;
        }

        rr.lastWritePos = endPos;
    }
}
