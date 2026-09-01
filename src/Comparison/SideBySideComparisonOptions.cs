namespace Aspose.Pdf.Comparison
{
    /// <summary>How the side-by-side comparer treats whitespace when extracting the texts
    /// to diff.</summary>
    public enum ComparisonMode
    {
        /// <summary>Compare the extracted text runs as-is.</summary>
        Normal,

        /// <summary>Ignore all whitespace: only non-space characters are compared.</summary>
        IgnoreSpaces,

        /// <summary>Reconstruct inter-word spaces and line breaks from glyph geometry and
        /// include them in the comparison.</summary>
        ParseSpaces
    }

    /// <summary>Options for <see cref="SideBySidePdfComparer"/>: whitespace handling,
    /// comparison/exclusion areas and the marker colours used in the output document.</summary>
    public class SideBySideComparisonOptions
    {
        /// <summary>Whitespace handling for the text comparison.</summary>
        public ComparisonMode ComparisonMode { get; set; }

        /// <summary>When true, each page also marks the POSITION of the counterpart's change
        /// (a thin caret where the other document inserted or deleted text).</summary>
        public bool AdditionalChangeMarks { get; set; }

        /// <summary>When true, text inside detected tables is excluded from the comparison.</summary>
        public bool ExcludeTables { get; set; }

        /// <summary>Restrict the comparison on the first page/document to this area
        /// (null compares the whole page).</summary>
        public Rectangle? ComparisonArea1 { get; set; }

        /// <summary>Restrict the comparison on the second page/document to this area
        /// (null compares the whole page).</summary>
        public Rectangle? ComparisonArea2 { get; set; }

        /// <summary>Areas on the first page/document whose text is ignored.</summary>
        public Rectangle[]? ExcludeAreas1 { get; set; }

        /// <summary>Areas on the second page/document whose text is ignored.</summary>
        public Rectangle[]? ExcludeAreas2 { get; set; }

        /// <summary>Marker colour for deleted text (left side). Default red.</summary>
        public Color DeleteColor { get; set; } = Color.Red;

        /// <summary>Marker colour for inserted text (right side). Default green.</summary>
        public Color InsertColor { get; set; } = Color.Green;
    }
}
