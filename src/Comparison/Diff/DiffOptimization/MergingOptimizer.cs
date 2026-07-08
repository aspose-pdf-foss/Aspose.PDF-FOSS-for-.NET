using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Comparison.Diff.DiffOptimization
{
    /// <summary>Semantic clean-up pass: eliminates equalities that are no larger than the edits
    /// surrounding them (folding those short common runs back into the adjacent delete/insert),
    /// then re-merges the result into canonical form. Produces a diff with fewer, more meaningful
    /// edits at the cost of a slightly larger edit distance.</summary>
    public sealed class MergingOptimizer : IDiffOptimizationOperation
    {
        private readonly EditOperationsOrder _order;

        /// <summary>Create the optimizer with the given delete/insert emission order.</summary>
        public MergingOptimizer(EditOperationsOrder order) => _order = order;

        /// <inheritdoc/>
        public void Execute(List<DiffOperation> diffs)
        {
            if (diffs is null || diffs.Count == 0) return;

            var changes = false;
            var equalities = new Stack<int>();   // indices of equalities pending review
            string? lastEquality = null;
            var pointer = 0;
            // Running edit lengths before (…1) and after (…2) the last equality.
            int insertions1 = 0, deletions1 = 0, insertions2 = 0, deletions2 = 0;

            while (pointer < diffs.Count)
            {
                if (diffs[pointer].Operation == Operation.Equal)
                {
                    equalities.Push(pointer);
                    insertions1 = insertions2;
                    deletions1 = deletions2;
                    insertions2 = 0;
                    deletions2 = 0;
                    lastEquality = diffs[pointer].Text;
                }
                else
                {
                    if (diffs[pointer].Operation == Operation.Insert)
                        insertions2 += diffs[pointer].Text.Length;
                    else
                        deletions2 += diffs[pointer].Text.Length;

                    if (lastEquality is not null
                        && lastEquality.Length <= Math.Max(insertions1, deletions1)
                        && lastEquality.Length <= Math.Max(insertions2, deletions2))
                    {
                        var eqIndex = equalities.Pop();
                        // Split the equality into a delete + insert of the same text.
                        diffs.Insert(eqIndex, new DiffOperation(Operation.Delete, lastEquality));
                        diffs[eqIndex + 1].Operation = Operation.Insert;
                        if (equalities.Count != 0) equalities.Pop();
                        pointer = equalities.Count != 0 ? equalities.Peek() : -1;
                        insertions1 = deletions1 = insertions2 = deletions2 = 0;
                        lastEquality = null;
                        changes = true;
                    }
                }
                pointer++;
            }

            if (changes)
                new OperationsMerger(_order).Execute(diffs);
        }
    }
}
