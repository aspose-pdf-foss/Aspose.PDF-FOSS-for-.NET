using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Comparison.Diff.DiffOptimization
{
    /// <summary>Reduces a diff to its canonical form: coalesce adjacent runs of the same
    /// operation and factor the common prefix and suffix out of a mixed delete/insert run
    /// (<see cref="OperationsMerger"/>), then slide any single edit that is fenced by two
    /// equalities sideways to dissolve one of them (<see cref="OperationsSlideMerger"/>).
    ///
    /// The two passes feed each other - a slide dissolves an equality and so butts two edits
    /// of the same kind together, which the merger then coalesces - so they run alternately
    /// until the diff stops moving. Each productive slide removes one equality, so the number
    /// of useful rounds cannot exceed the length of the diff.</summary>
    public sealed class MergingOptimizer : IDiffOptimizationOperation
    {
        private readonly EditOperationsOrder _order;

        /// <summary>Create the optimizer with the given delete/insert emission order.</summary>
        public MergingOptimizer(EditOperationsOrder order) => _order = order;

        /// <inheritdoc/>
        public void Execute(List<DiffOperation> diffs)
        {
            if (diffs is null || diffs.Count == 0) return;

            var merger = new OperationsMerger(_order);
            var slider = new OperationsSlideMerger();

            var rounds = diffs.Count + 1;
            var previous = Shape(diffs);
            for (var round = 0; round < rounds; round++)
            {
                merger.Execute(diffs);
                slider.Execute(diffs);

                var shape = Shape(diffs);
                if (shape == previous) break;
                previous = shape;
            }
        }

        /// <summary>The diff written out as one string, so a round that changed nothing can be
        /// told from one that did.</summary>
        private static string Shape(List<DiffOperation> diffs)
        {
            var sb = new StringBuilder();
            foreach (var op in diffs)
            {
                // Length-framed, so no separator can be confused with the edit text itself.
                sb.Append((int)op.Operation).Append(':').Append(op.Text.Length)
                  .Append(':').Append(op.Text);
            }
            return sb.ToString();
        }
    }
}
