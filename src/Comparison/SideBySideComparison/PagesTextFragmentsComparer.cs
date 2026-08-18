using System.Collections.Generic;
using Aspose.Pdf.Comparison.Diff;
using Aspose.Pdf.Comparison.Diff.DiffOptimization;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>Compares the text of two pages: extracts each side's comparison text through
    /// the mode-specific fragments processor, diffs them, and maps the edits back onto both
    /// pages' geometry.</summary>
    internal class PagesTextFragmentsComparer
    {
        private readonly Page? _page1;
        private readonly Page? _page2;
        private readonly ExtractedFragmentsProcessorBase _fragmentsProcessor;
        private readonly TextChangeMapper _changesMapper;
        private readonly SideBySideComparisonOptions _options;

        /// <summary>The first page's comparison text (available after <see cref="Compare"/>).</summary>
        internal string Text1 { get; private set; } = string.Empty;

        /// <summary>The second page's comparison text (available after <see cref="Compare"/>).</summary>
        internal string Text2 { get; private set; } = string.Empty;

        internal PagesTextFragmentsComparer(Page? page1, Page? page2,
            ExtractedFragmentsProcessorBase fragmentsProcessor, TextChangeMapper changesMapper,
            SideBySideComparisonOptions options)
        {
            _page1 = page1;
            _page2 = page2;
            _fragmentsProcessor = fragmentsProcessor;
            _changesMapper = changesMapper;
            _options = options;
        }

        /// <summary>Diff the two pages' texts. <paramref name="firstEdits"/> receives the
        /// change highlights for the first page, <paramref name="secondEdits"/> for the
        /// second; the full normalized edit list is returned.</summary>
        internal List<DiffOperation> Compare(out List<EditContainer> firstEdits,
            out List<EditContainer> secondEdits)
        {
            _fragmentsProcessor.SearchArea = _options.ComparisonArea1;
            _fragmentsProcessor.ExcludeAreas = EffectiveExcludeAreas(_options.ExcludeAreas1, _page1);
            var fragments1 = _fragmentsProcessor.PrepareFragments(_page1, out var text1);
            Text1 = text1;

            _fragmentsProcessor.SearchArea = _options.ComparisonArea2;
            _fragmentsProcessor.ExcludeAreas = EffectiveExcludeAreas(_options.ExcludeAreas2, _page2);
            var fragments2 = _fragmentsProcessor.PrepareFragments(_page2, out var text2);
            Text2 = text2;

            var diffs = new DiffSolver().FindDiff(Text1, Text2);
            new OperationsMerger(Diff.DiffOptimization.EditOperationsOrder.DeleteFirst).Execute(diffs);
            new OperationsSlideMerger().Execute(diffs);
            // Canonicalising merges only — deliberately NOT the semantic pass
            // (MergingOptimizer), which folds a short equality back into the edits flanking it.
            // Side-by-side reports every change it finds: "mak" vs "creat" stays split on the
            // shared "a" (delete "m", insert "cre", equal "a", delete "k", insert "t") instead of
            // collapsing to one delete/insert pair, so both the highlights and the reported
            // change counts keep that granularity.

            var ids = AssignChangeIds(diffs);
            firstEdits = _changesMapper.FindEditedFragments(fragments1, diffs, Diff.Operation.Delete, ids);
            secondEdits = _changesMapper.FindEditedFragments(fragments2, diffs, Diff.Operation.Insert, ids);
            return diffs;
        }

        private Rectangle[]? EffectiveExcludeAreas(Rectangle[]? areas, Page? page)
        {
            if (!_options.ExcludeTables) return areas;
            return ComparisonUtils.AddTablesToExcludeAreas(areas, page);
        }

        /// <summary>Number every change: each non-Equal operation gets the next id, counting
        /// from one across the whole diff. The two halves of a replacement are consecutive
        /// rather than shared, so a reader can follow the changes of both pages in one
        /// sequence and see where each one falls relative to the other's.</summary>
        private static Dictionary<int, int> AssignChangeIds(List<DiffOperation> diffs)
        {
            var ids = new Dictionary<int, int>();
            var id = 0;
            for (var i = 0; i < diffs.Count; i++)
            {
                if (diffs[i].Operation == Diff.Operation.Equal) continue;
                ids[i] = ++id;
            }
            return ids;
        }
    }
}
