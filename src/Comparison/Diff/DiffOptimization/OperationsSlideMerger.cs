using System.Collections.Generic;

namespace Aspose.Pdf.Comparison.Diff.DiffOptimization
{
    /// <summary>Shifts a single edit that is surrounded on both sides by equalities sideways to
    /// eliminate one of those equalities, canonicalising the diff. For example
    /// <c>Eq"aa" Ins"xaa" Eq"c"</c> becomes <c>Ins"aax" Eq"aac"</c> (the insert slides left over the
    /// preceding equality), and <c>Eq"1" Ins"23" Eq"2"</c> becomes <c>Eq"12" Ins"32"</c> (it slides
    /// right over the following one).</summary>
    public sealed class OperationsSlideMerger : IDiffOptimizationOperation
    {
        /// <inheritdoc/>
        public void Execute(List<DiffOperation> diffs)
        {
            if (diffs is null || diffs.Count < 3) return;

            var pointer = 1;
            while (pointer < diffs.Count - 1)
            {
                if (diffs[pointer - 1].Operation == Operation.Equal
                    && diffs[pointer + 1].Operation == Operation.Equal)
                {
                    var edit = diffs[pointer].Text;
                    var prev = diffs[pointer - 1].Text;
                    var next = diffs[pointer + 1].Text;

                    if (EndsWith(edit, prev))
                    {
                        // Slide the edit left over the preceding equality.
                        diffs[pointer].Text = prev + edit.Substring(0, edit.Length - prev.Length);
                        diffs[pointer + 1].Text = prev + next;
                        diffs.RemoveAt(pointer - 1);
                        pointer--;
                    }
                    else if (StartsWith(edit, next))
                    {
                        // Slide the edit right over the following equality.
                        diffs[pointer - 1].Text = prev + next;
                        diffs[pointer].Text = edit.Substring(next.Length) + next;
                        diffs.RemoveAt(pointer + 1);
                    }
                }
                pointer++;
            }
        }

        private static bool EndsWith(string text, string suffix)
            => suffix.Length <= text.Length
               && text.Substring(text.Length - suffix.Length) == suffix;

        private static bool StartsWith(string text, string prefix)
            => prefix.Length <= text.Length && text.Substring(0, prefix.Length) == prefix;
    }
}
