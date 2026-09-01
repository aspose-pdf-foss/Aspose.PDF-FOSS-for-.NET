using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>Normal mode: compares the absorbed text runs as-is, with no whitespace
    /// synthesis or filtering.</summary>
    internal class NormalFragmentProcessor : ExtractedFragmentsProcessorBase
    {
        internal NormalFragmentProcessor(TextFragmentRectanglesComparer fragmentsComparer)
            : base(fragmentsComparer)
        {
        }

        protected override List<TextFragment> GetTextFragments(Page page)
        {
            var absorber = new TextFragmentAbsorber();
            absorber.Visit(page);
            var fragments = new List<TextFragment>();
            foreach (var fragment in absorber.TextFragments)
                if (!string.IsNullOrEmpty(fragment.Text))
                    fragments.Add(fragment);
            return fragments;
        }

        protected override List<Fragment> ProcessSortedFragments(List<TextFragment> fragments)
        {
            var result = new List<Fragment>(fragments.Count);
            foreach (var fragment in fragments)
                result.Add(new Fragment(fragment));
            return result;
        }
    }
}
