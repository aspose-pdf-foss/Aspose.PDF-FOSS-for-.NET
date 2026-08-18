using System;
using System.Collections.Generic;
using System.IO;
using Aspose.Pdf.Comparison.SideBySideComparison;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Result of a document-level side-by-side comparison: per-page change lists
    /// plus the full per-page edit sequences.</summary>
    public class SideBySideDocsComparisonResult
    {
        /// <summary>True when any page pair differs.</summary>
        public bool HasChanges { get; }

        /// <summary>Per page: the first document's change highlights.</summary>
        public List<List<EditContainer>> FirstDocChanges { get; }

        /// <summary>Per page: the second document's change highlights.</summary>
        public List<List<EditContainer>> SecondDocChanges { get; }

        /// <summary>Per page: the full normalized edit list.</summary>
        public List<List<Diff.DiffOperation>> FullChanges { get; }

        public SideBySideDocsComparisonResult(bool hasChanges,
            List<List<EditContainer>> firstDocChanges,
            List<List<EditContainer>> secondDocChanges,
            List<List<Diff.DiffOperation>> fullChanges)
        {
            HasChanges = hasChanges;
            FirstDocChanges = firstDocChanges;
            SecondDocChanges = secondDocChanges;
            FullChanges = fullChanges;
        }
    }

    /// <summary>Result of a page-level side-by-side comparison.</summary>
    public class SideBySidePagesComparisonResult
    {
        /// <summary>True when the pages differ.</summary>
        public bool HasChanges { get; }

        /// <summary>The first page's change highlights.</summary>
        public List<EditContainer> FirstPageChanges { get; }

        /// <summary>The second page's change highlights.</summary>
        public List<EditContainer> SecondPageChanges { get; }

        /// <summary>The full normalized edit list.</summary>
        public List<Diff.DiffOperation> FullChanges { get; }

        public SideBySidePagesComparisonResult(bool hasChanges,
            List<EditContainer> firstPageChanges, List<EditContainer> secondPageChanges,
            List<Diff.DiffOperation> fullChanges)
        {
            HasChanges = hasChanges;
            FirstPageChanges = firstPageChanges;
            SecondPageChanges = secondPageChanges;
            FullChanges = fullChanges;
        }
    }

    /// <summary>Compares the text of two pages or documents and produces a result PDF that
    /// shows both versions side by side with the changes highlighted (deletions on the left
    /// page, insertions on the right).</summary>
    public static class SideBySidePdfComparer
    {
        /// <summary>Compare two pages and write the side-by-side result to a file.</summary>
        public static SideBySidePagesComparisonResult Compare(Page page1, Page page2,
            string targetPdfPath, SideBySideComparisonOptions options)
        {
            using var target = new Document();
            var result = ComparePagesInto(target, page1, page2, options ?? new SideBySideComparisonOptions());
            target.Save(targetPdfPath);
            return result;
        }

        /// <summary>Compare two pages and write the side-by-side result to a stream.</summary>
        public static SideBySidePagesComparisonResult Compare(Page page1, Page page2,
            Stream targetStream, SideBySideComparisonOptions options)
        {
            using var target = new Document();
            var result = ComparePagesInto(target, page1, page2, options ?? new SideBySideComparisonOptions());
            target.Save(targetStream);
            return result;
        }

        /// <summary>Compare two documents page by page and write the side-by-side result
        /// to a file.</summary>
        public static SideBySideDocsComparisonResult Compare(Document document1, Document document2,
            string targetPdfPath, SideBySideComparisonOptions options)
        {
            using var target = new Document();
            var result = CompareDocsInto(target, document1, document2, options ?? new SideBySideComparisonOptions());
            target.Save(targetPdfPath);
            return result;
        }

        /// <summary>Compare two documents page by page and write the side-by-side result
        /// to a stream.</summary>
        public static SideBySideDocsComparisonResult Compare(Document document1, Document document2,
            Stream targetStream, SideBySideComparisonOptions options)
        {
            using var target = new Document();
            var result = CompareDocsInto(target, document1, document2, options ?? new SideBySideComparisonOptions());
            target.Save(targetStream);
            return result;
        }

        private static SideBySidePagesComparisonResult ComparePagesInto(Document target,
            Page? page1, Page? page2, SideBySideComparisonOptions options)
        {
            var comparer = new PagesTextFragmentsComparer(
                page1, page2, CreateProcessor(options), new TextChangeMapper(options), options);
            var diffs = comparer.Compare(out var firstEdits, out var secondEdits);
            SideBySideDocumentBuider.BuildResult(target, page1, page2, firstEdits, secondEdits, options);

            var hasChanges = diffs.Exists(d => d.Operation != Diff.Operation.Equal);
            return new SideBySidePagesComparisonResult(hasChanges, firstEdits, secondEdits, diffs);
        }

        private static SideBySideDocsComparisonResult CompareDocsInto(Document target,
            Document document1, Document document2, SideBySideComparisonOptions options)
        {
            var firstChanges = new List<List<EditContainer>>();
            var secondChanges = new List<List<EditContainer>>();
            var fullChanges = new List<List<Diff.DiffOperation>>();
            var hasChanges = false;

            var count1 = document1?.Pages.Count ?? 0;
            var count2 = document2?.Pages.Count ?? 0;
            for (var i = 1; i <= Math.Max(count1, count2); i++)
            {
                var page1 = i <= count1 ? document1!.Pages[i] : null;
                var page2 = i <= count2 ? document2!.Pages[i] : null;
                var pageResult = ComparePagesInto(target, page1, page2, options);
                firstChanges.Add(pageResult.FirstPageChanges);
                secondChanges.Add(pageResult.SecondPageChanges);
                fullChanges.Add(pageResult.FullChanges);
                hasChanges |= pageResult.HasChanges;
            }

            return new SideBySideDocsComparisonResult(hasChanges, firstChanges, secondChanges, fullChanges);
        }

        private static ExtractedFragmentsProcessorBase CreateProcessor(SideBySideComparisonOptions options)
        {
            var rectanglesComparer = new TextFragmentRectanglesComparer();
            return options.ComparisonMode switch
            {
                ComparisonMode.IgnoreSpaces => new IgnoreSpacesFragmentsProcessor(rectanglesComparer),
                ComparisonMode.ParseSpaces => new ParseSpacesFragmentsProcessor(rectanglesComparer),
                _ => new NormalFragmentProcessor(rectanglesComparer),
            };
        }
    }
}
