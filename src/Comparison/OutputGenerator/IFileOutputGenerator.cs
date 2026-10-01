using System.Collections.Generic;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Writes the result of a text comparison to a file. A generator takes the edit
    /// list a <see cref="Diff.DiffSolver"/> produced and renders it in one concrete format -
    /// JSON, Markdown, HTML or PDF - so the same diff can be published several ways.</summary>
    public interface IFileOutputGenerator
    {
        /// <summary>Render one document's edits to <paramref name="targetFilePath"/>.</summary>
        /// <param name="differences">The edits, in document order.</param>
        /// <param name="targetFilePath">Path of the file to write.</param>
        void GenerateOutput(List<DiffOperation> differences, string targetFilePath);

        /// <summary>Render a per-page (or per-section) edit list, one inner list per page, to
        /// <paramref name="targetFilePath"/>.</summary>
        /// <param name="differences">The edits, grouped in document order.</param>
        /// <param name="targetFilePath">Path of the file to write.</param>
        void GenerateOutput(List<List<DiffOperation>> differences, string targetFilePath);
    }
}
