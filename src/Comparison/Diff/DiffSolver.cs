using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Comparison.Diff
{
    /// <summary>Character-level text diff (Myers O(ND) bisect algorithm). Produces the raw
    /// <see cref="DiffOperation"/> sequence; callers normalise it with the passes in
    /// <see cref="DiffOptimization"/> (<see cref="DiffOptimization.OperationsMerger"/> et al.).</summary>
    internal sealed class DiffSolver
    {
        /// <summary>Diff <paramref name="oldText"/> against <paramref name="newText"/>.
        /// The returned edits rebuild the source via <see cref="DiffUtils.AssemblySourceText"/>
        /// and the destination via <see cref="DiffUtils.AssemblyDestinationText"/>.</summary>
        public List<DiffOperation> FindDiff(string? oldText, string? newText)
            => Main(oldText ?? string.Empty, newText ?? string.Empty);

        private static List<DiffOperation> Main(string text1, string text2)
        {
            var diffs = new List<DiffOperation>();
            if (text1 == text2)
            {
                if (text1.Length != 0) diffs.Add(new DiffOperation(Operation.Equal, text1));
                return diffs;
            }

            var prefix = DiffUtils.FindCommonStartParts(text1, text2);
            var mid1 = text1.Substring(prefix.Length);
            var mid2 = text2.Substring(prefix.Length);
            var suffix = DiffUtils.FindCommonEndParts(mid1, mid2, 0);
            if (suffix.Length != 0)
            {
                mid1 = mid1.Substring(0, mid1.Length - suffix.Length);
                mid2 = mid2.Substring(0, mid2.Length - suffix.Length);
            }

            if (prefix.Length != 0) diffs.Add(new DiffOperation(Operation.Equal, prefix));
            Compute(mid1, mid2, diffs);
            if (suffix.Length != 0) diffs.Add(new DiffOperation(Operation.Equal, suffix));
            return diffs;
        }

        private static void Compute(string text1, string text2, List<DiffOperation> diffs)
        {
            if (text1.Length == 0)
            {
                if (text2.Length != 0) diffs.Add(new DiffOperation(Operation.Insert, text2));
                return;
            }
            if (text2.Length == 0)
            {
                diffs.Add(new DiffOperation(Operation.Delete, text1));
                return;
            }

            var longText = text1.Length > text2.Length ? text1 : text2;
            var shortText = text1.Length > text2.Length ? text2 : text1;
            var at = longText.IndexOf(shortText, StringComparison.Ordinal);
            if (at != -1)
            {
                // The shorter text sits whole inside the longer one: two pure edits around it.
                var op = text1.Length > text2.Length ? Operation.Delete : Operation.Insert;
                if (at != 0) diffs.Add(new DiffOperation(op, longText.Substring(0, at)));
                diffs.Add(new DiffOperation(Operation.Equal, shortText));
                if (at + shortText.Length != longText.Length)
                    diffs.Add(new DiffOperation(op, longText.Substring(at + shortText.Length)));
                return;
            }
            if (shortText.Length == 1)
            {
                // After the containment check a single char shares nothing with the other text.
                diffs.Add(new DiffOperation(Operation.Delete, text1));
                diffs.Add(new DiffOperation(Operation.Insert, text2));
                return;
            }

            Bisect(text1, text2, diffs);
        }

        /// <summary>Walk the forward and reverse edit paths until they overlap, then split at
        /// the middle snake and solve each half recursively (Myers' linear-space refinement).</summary>
        private static void Bisect(string text1, string text2, List<DiffOperation> diffs)
        {
            int len1 = text1.Length, len2 = text2.Length;
            var maxD = (len1 + len2 + 1) / 2;
            var vOffset = maxD;
            var vLength = 2 * maxD + 2;
            var v1 = new int[vLength];
            var v2 = new int[vLength];
            for (var i = 0; i < vLength; i++) { v1[i] = -1; v2[i] = -1; }
            v1[vOffset + 1] = 0;
            v2[vOffset + 1] = 0;
            var delta = len1 - len2;
            // With an odd delta the paths can only overlap on the forward walk;
            // with an even delta only on the reverse walk.
            var front = delta % 2 != 0;
            int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;

            for (var d = 0; d < maxD; d++)
            {
                for (var k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
                {
                    var k1Offset = vOffset + k1;
                    var x1 = k1 == -d || (k1 != d && v1[k1Offset - 1] < v1[k1Offset + 1])
                        ? v1[k1Offset + 1]
                        : v1[k1Offset - 1] + 1;
                    var y1 = x1 - k1;
                    while (x1 < len1 && y1 < len2 && text1[x1] == text2[y1]) { x1++; y1++; }
                    v1[k1Offset] = x1;
                    if (x1 > len1) k1End += 2;          // ran off the right edge
                    else if (y1 > len2) k1Start += 2;   // ran off the bottom edge
                    else if (front)
                    {
                        var k2Offset = vOffset + delta - k1;
                        if (k2Offset >= 0 && k2Offset < vLength && v2[k2Offset] != -1
                            && x1 >= len1 - v2[k2Offset])
                        {
                            BisectSplit(text1, text2, x1, y1, diffs);
                            return;
                        }
                    }
                }

                for (var k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
                {
                    var k2Offset = vOffset + k2;
                    var x2 = k2 == -d || (k2 != d && v2[k2Offset - 1] < v2[k2Offset + 1])
                        ? v2[k2Offset + 1]
                        : v2[k2Offset - 1] + 1;
                    var y2 = x2 - k2;
                    while (x2 < len1 && y2 < len2
                           && text1[len1 - x2 - 1] == text2[len2 - y2 - 1]) { x2++; y2++; }
                    v2[k2Offset] = x2;
                    if (x2 > len1) k2End += 2;
                    else if (y2 > len2) k2Start += 2;
                    else if (!front)
                    {
                        var k1Offset = vOffset + delta - k2;
                        if (k1Offset >= 0 && k1Offset < vLength && v1[k1Offset] != -1)
                        {
                            var x1 = v1[k1Offset];
                            var y1 = vOffset + x1 - k1Offset;
                            if (x1 >= len1 - x2)
                            {
                                BisectSplit(text1, text2, x1, y1, diffs);
                                return;
                            }
                        }
                    }
                }
            }

            // The number of edits equals the number of characters: no commonality at all.
            diffs.Add(new DiffOperation(Operation.Delete, text1));
            diffs.Add(new DiffOperation(Operation.Insert, text2));
        }

        private static void BisectSplit(string text1, string text2, int x, int y,
            List<DiffOperation> diffs)
        {
            diffs.AddRange(Main(text1.Substring(0, x), text2.Substring(0, y)));
            diffs.AddRange(Main(text1.Substring(x), text2.Substring(y)));
        }
    }
}
