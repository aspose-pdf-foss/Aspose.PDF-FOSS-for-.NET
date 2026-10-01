namespace Aspose.Pdf.Comparison
{
    /// <summary>Options for a text comparison: which part of each page to read, what to leave
    /// out, and how to order the edits a difference produces.</summary>
    public class ComparisonOptions
    {
        /// <summary>The region of the page to take text from. Null (the default) reads the
        /// whole page.</summary>
        public Rectangle? ExtractionArea { get; set; }

        /// <summary>Leave table content out of the comparison.</summary>
        public bool ExcludeTables { get; set; }

        /// <summary>Regions of the FIRST document to leave out of the comparison.</summary>
        public Rectangle[]? ExcludeAreas1 { get; set; }

        /// <summary>Regions of the SECOND document to leave out of the comparison.</summary>
        public Rectangle[]? ExcludeAreas2 { get; set; }

        /// <summary>Which of a delete/insert pair is emitted first where the two texts differ.
        /// Defaults to <see cref="Comparison.EditOperationsOrder.DeleteFirst"/>, the order the
        /// diff itself produces.</summary>
        public EditOperationsOrder EditOperationsOrder { get; set; } = EditOperationsOrder.DeleteFirst;

        /// <summary>Create options that read the whole page and exclude nothing.</summary>
        public ComparisonOptions() { }
    }
}
