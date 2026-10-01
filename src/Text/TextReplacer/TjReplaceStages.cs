using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Tj replace: the array elements after the match copied through, the end string keeping only its tail bytes.</summary>
    private void EmitTjSuffixElements(TjReplaceState tj)
    {
        tj.firstSuffixString = true;
        for (var i = tj.endArrIdx; i < tj.arr.Count; i++)
        {
            var el = tj.arr[i];
            if (i == tj.endArrIdx)
            {
                // For the string containing the match end, emit only the bytes
                // AFTER the match.
                if (el is not PdfString endStr) continue;
                int trimStart = tj.endOffset + 1;
                if (trimStart >= endStr.Value.Length) continue;
                var tail = new byte[endStr.Value.Length - trimStart];
                Buffer.BlockCopy(endStr.Value, trimStart, tail, 0, tail.Length);
                tj.newArr.Add(new PdfString(tail, endStr.IsHex));
                tj.firstSuffixString = false;
            }
            else
            {
                tj.newArr.Add(el);
                if (el is PdfString) tj.firstSuffixString = false;
            }
        }
    }

    /// <summary>Tj replace: the elements before the match copied and the replacement emitted with its kern compensation.</summary>
    private void EmitTjPrefixAndReplacement(TjReplaceState tj)
    {
        tj.newArr = new PdfArray();

        // Emit the prefix by COPYING the original TJ-array elements before the
        // match — this preserves the original inter-element kerns (including
        // big-negative kerns that were synthesized into spaces in `combinedText`
        // for matching purposes). Only the matched region itself is replaced.
        // The string element containing the match start contributes its leading
        // bytes (chars before startOffset) followed by the replacement bytes.
        for (var i = 0; i < tj.startArrIdx; i++)
            tj.newArr.Add(tj.arr[i]);

        if (tj.arr[tj.startArrIdx] is PdfString startStr && tj.startOffset > 0)
        {
            // Decode just the prefix bytes (chars before startOffset) and
            // re-encode together with the replacement.
            var preBytes = new byte[tj.startOffset];
            Buffer.BlockCopy(startStr.Value, 0, preBytes, 0, tj.startOffset);
            var preStr = DecodeString(preBytes, tj.toUnicode, tj.fontDict, tj.reader);
            tj.preRepBytes = EncodeString(preStr + tj.replacement, tj.toUnicode, tj.fontDict);
        }
        else
        {
            tj.preRepBytes = EncodeString(tj.replacement, tj.toUnicode, tj.fontDict);
        }
        tj.newArr.Add(new PdfString(tj.preRepBytes, tj.useHex2));

        if (tj.kernCompensation != 0)
        {
            // Split a single large compensation into several smaller kernings
            // so none individually trips the reader's word-break heuristic
            // (adj ≤ −130 becomes synthetic space). Using chunks of |adj| ≤ 120
            // keeps each step below the threshold while still summing to the
            // needed advance correction. Only negative (push-right) splitting
            // matters here — positive kernings never trigger the heuristic.
            const int SafeChunk = 120;
            int remaining = tj.kernCompensation;
            if (remaining < 0)
            {
                while (remaining < -SafeChunk)
                {
                    tj.newArr.Add(new PdfInteger(-SafeChunk));
                    remaining += SafeChunk;
                }
                if (remaining != 0) tj.newArr.Add(new PdfInteger(remaining));
            }
            else
            {
                // Positive kernings are already safe (advance shrink).
                tj.newArr.Add(new PdfInteger(remaining));
            }
        }
    }

    /// <summary>Tj replace: the char-to-element map built over the array's strings.</summary>
    private void BuildTjCharMap(TjReplaceState tj)
    {
        tj.lastMapCh = '\0';
        for (var i = 0; i < tj.arr.Count; i++)
        {
            if (tj.arr[i] is PdfString sm)
            {
                var decMap = DecodeString(sm.Value, tj.toUnicode, tj.fontDict, tj.reader);
                for (var k = 0; k < decMap.Length; k++) tj.charMap.Add(i);
                if (decMap.Length > 0) tj.lastMapCh = decMap[^1];
            }
            else if ((tj.arr[i] is PdfInteger ia && tj.tjRule.Breaks(ia.Value))
                  || (tj.arr[i] is PdfReal ra && tj.tjRule.Breaks(ra.Value)))
            {
                if (tj.lastMapCh != '\0' && tj.lastMapCh != ' ')
                {
                    tj.charMap.Add(-1); // synthetic space
                    tj.lastMapCh = ' ';
                }
            }
        }
    }

    /// <summary>Tj replace: the array's strings concatenated into one searchable text.</summary>
    private void ConcatTjArrayText(TjReplaceState tj)
    {
        tj.fullText = new StringBuilder();
        tj.parts = new List<(int index, string text, bool isHex)>();

        tj.tjRule = TjBreakRuleOf(tj.arr, tj.toUnicode, tj.fontDict, tj.reader);
        for (var i = 0; i < tj.arr.Count; i++)
        {
            if (tj.arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, tj.toUnicode, tj.fontDict, tj.reader);
                tj.parts.Add((i, decoded, s.IsHex));
                tj.fullText.Append(decoded);
            }
            else if ((tj.arr[i] is PdfInteger adj && tj.tjRule.Breaks(adj.Value))
                  || (tj.arr[i] is PdfReal adjR && tj.tjRule.Breaks(adjR.Value)))
            {
                if (tj.fullText.Length > 0 && tj.fullText[^1] != ' ')
                    tj.fullText.Append(' ');
            }
        }
    }
}
