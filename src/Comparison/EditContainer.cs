using System.Collections.Generic;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>A single change found by the side-by-side comparison, bound to the page
    /// rectangles it covers. Paired delete/insert edits share the same <see cref="Id"/>.</summary>
    public class EditContainer
    {
        /// <summary>The change's number in the comparison, counting from one across both pages
        /// in diff order.</summary>
        public int Id { get; }

        /// <summary>True for a positional marker of the OTHER document's change (emitted when
        /// <see cref="SideBySideComparisonOptions.AdditionalChangeMarks"/> is on) rather than
        /// a highlight of this page's own edited text.</summary>
        internal bool IsAdditionalMark { get; }

        /// <summary>The underlying text edit.</summary>
        public DiffOperation Operation { get; }

        /// <summary>Page-space rectangles covering the edited characters (one per text line).</summary>
        public List<Rectangle> Rects { get; } = new List<Rectangle>();

        internal EditContainer(int id, DiffOperation operation)
            : this(id, operation, false)
        {
        }

        internal EditContainer(int id, DiffOperation operation, bool isAdditionalMark)
        {
            Id = id;
            Operation = operation;
            IsAdditionalMark = isAdditionalMark;
        }
    }
}
