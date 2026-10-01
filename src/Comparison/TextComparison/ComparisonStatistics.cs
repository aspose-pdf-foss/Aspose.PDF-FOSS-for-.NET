using System.Collections.Generic;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Counts over one compared text: how many characters it held in all, and how
    /// many characters and edit runs were deleted from it or inserted into it.</summary>
    public class TextItemComparisonStatistics
    {
        /// <summary>Creates an empty set of counts, all zero.</summary>
        public TextItemComparisonStatistics() { }

        /// <summary>Every character across every edit - unchanged, deleted and inserted.</summary>
        public int TotalCharacters { get; internal set; }

        /// <summary>Characters present only in the first text.</summary>
        public int DeletedCharactersCount { get; internal set; }

        /// <summary>Characters present only in the second text.</summary>
        public int InsertedCharactersCount { get; internal set; }

        /// <summary>Runs of deleted text.</summary>
        public int DeleteOperationsCount { get; internal set; }

        /// <summary>Runs of inserted text.</summary>
        public int InsertOperationsCount { get; internal set; }

        /// <summary>Add one edit to the counts.</summary>
        internal void Count(Diff.DiffOperation edit)
        {
            var n = edit.Text?.Length ?? 0;
            TotalCharacters += n;
            switch (edit.Operation)
            {
                case Operation.Delete:
                    DeletedCharactersCount += n;
                    DeleteOperationsCount++;
                    break;
                case Operation.Insert:
                    InsertedCharactersCount += n;
                    InsertOperationsCount++;
                    break;
            }
        }

        /// <summary>Fold another item's counts into this one.</summary>
        internal void Add(TextItemComparisonStatistics other)
        {
            TotalCharacters += other.TotalCharacters;
            DeletedCharactersCount += other.DeletedCharactersCount;
            InsertedCharactersCount += other.InsertedCharactersCount;
            DeleteOperationsCount += other.DeleteOperationsCount;
            InsertOperationsCount += other.InsertOperationsCount;
        }
    }

    /// <summary>Counts over a whole document comparison: the document totals, plus the
    /// counts of each page in order.</summary>
    public class DocumentComparisonStatistics : TextItemComparisonStatistics
    {
        /// <summary>Creates empty document counts, all zero and with no page entries.</summary>
        public DocumentComparisonStatistics() { }

        /// <summary>One entry per page, in page order.</summary>
        public List<TextItemComparisonStatistics> PagesStatistics { get; internal set; } = new();
    }
}
