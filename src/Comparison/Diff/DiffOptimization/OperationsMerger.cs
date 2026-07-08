using System.Collections.Generic;

namespace Aspose.Pdf.Comparison.Diff.DiffOptimization
{
    /// <summary>Merges a diff into its canonical minimal form: coalesces adjacent runs of the
    /// same operation, and for a mixed delete/insert run factors any common prefix into the
    /// preceding equality and any common suffix into the following equality. Deletes and inserts
    /// are emitted in the order given by <see cref="EditOperationsOrder"/>. This is the classic
    /// diff clean-up-and-merge pass.</summary>
    public sealed class OperationsMerger : IDiffOptimizationOperation
    {
        private readonly EditOperationsOrder _order;

        /// <summary>Create the merger with the given delete/insert emission order.</summary>
        public OperationsMerger(EditOperationsOrder order) => _order = order;

        /// <inheritdoc/>
        public void Execute(List<DiffOperation> diffs)
        {
            if (diffs is null || diffs.Count == 0) return;

            // Trailing sentinel so a pending delete/insert run is always flushed at an equality.
            diffs.Add(new DiffOperation(Operation.Equal, string.Empty));
            int pointer = 0, countDelete = 0, countInsert = 0;
            string textDelete = string.Empty, textInsert = string.Empty;

            while (pointer < diffs.Count)
            {
                switch (diffs[pointer].Operation)
                {
                    case Operation.Insert:
                        countInsert++;
                        textInsert += diffs[pointer].Text;
                        pointer++;
                        break;
                    case Operation.Delete:
                        countDelete++;
                        textDelete += diffs[pointer].Text;
                        pointer++;
                        break;
                    default: // Equal — flush the accumulated run
                        if (countDelete + countInsert > 1)
                        {
                            if (countDelete != 0 && countInsert != 0)
                            {
                                var prefix = DiffUtils.FindCommonStartParts(textInsert, textDelete);
                                if (prefix.Length != 0)
                                {
                                    var x = pointer - countDelete - countInsert - 1;
                                    if (x >= 0 && diffs[x].Operation == Operation.Equal)
                                        diffs[x].Text += prefix;
                                    else
                                    {
                                        diffs.Insert(0, new DiffOperation(Operation.Equal, prefix));
                                        pointer++;
                                    }
                                    textInsert = textInsert.Substring(prefix.Length);
                                    textDelete = textDelete.Substring(prefix.Length);
                                }

                                var suffix = DiffUtils.FindCommonEndParts(textInsert, textDelete, 0);
                                if (suffix.Length != 0)
                                {
                                    diffs[pointer].Text = suffix + diffs[pointer].Text;
                                    textInsert = textInsert.Substring(0, textInsert.Length - suffix.Length);
                                    textDelete = textDelete.Substring(0, textDelete.Length - suffix.Length);
                                }
                            }

                            var removeAt = pointer - countDelete - countInsert;
                            diffs.RemoveRange(removeAt, countDelete + countInsert);
                            pointer = removeAt;
                            foreach (var op in BuildRun(textDelete, textInsert))
                            {
                                diffs.Insert(pointer, op);
                                pointer++;
                            }
                        }
                        else if (pointer != 0 && diffs[pointer - 1].Operation == Operation.Equal)
                        {
                            // Merge two adjacent equalities.
                            diffs[pointer - 1].Text += diffs[pointer].Text;
                            diffs.RemoveAt(pointer);
                        }
                        else
                        {
                            pointer++;
                        }
                        countDelete = 0;
                        countInsert = 0;
                        textDelete = string.Empty;
                        textInsert = string.Empty;
                        break;
                }
            }

            if (diffs.Count != 0 && diffs[diffs.Count - 1].Text.Length == 0)
                diffs.RemoveAt(diffs.Count - 1);
        }

        private IEnumerable<DiffOperation> BuildRun(string textDelete, string textInsert)
        {
            var del = textDelete.Length != 0 ? new DiffOperation(Operation.Delete, textDelete) : null;
            var ins = textInsert.Length != 0 ? new DiffOperation(Operation.Insert, textInsert) : null;
            if (_order == EditOperationsOrder.DeleteFirst)
            {
                if (del is not null) yield return del;
                if (ins is not null) yield return ins;
            }
            else
            {
                if (ins is not null) yield return ins;
                if (del is not null) yield return del;
            }
        }
    }
}
