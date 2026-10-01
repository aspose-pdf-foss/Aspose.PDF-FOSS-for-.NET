using System.Collections.Generic;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Renders the result of a text comparison to a string, for a caller that wants
    /// the markup itself rather than a file - the same content an
    /// <see cref="IFileOutputGenerator"/> would write.</summary>
    public interface IStringOutputGenerator
    {
        /// <summary>Render one document's edits.</summary>
        string GenerateOutput(List<DiffOperation> differences);

        /// <summary>Render a per-page edit list, one inner list per page.</summary>
        string GenerateOutput(List<List<DiffOperation>> differences);
    }
}
