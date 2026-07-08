using System.Collections.Generic;

namespace Aspose.Pdf.Comparison.Diff.DiffOptimization
{
    /// <summary>A post-processing pass that normalises a diff — a mutable list of
    /// <see cref="DiffOperation"/>s — in place, preserving the source and destination texts
    /// while producing a cleaner or more canonical sequence of edits.</summary>
    public interface IDiffOptimizationOperation
    {
        /// <summary>Rewrite <paramref name="diffs"/> in place.</summary>
        void Execute(List<DiffOperation> diffs);
    }
}
