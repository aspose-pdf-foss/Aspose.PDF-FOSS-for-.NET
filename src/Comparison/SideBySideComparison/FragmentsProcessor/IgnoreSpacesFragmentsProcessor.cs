using System.Collections.Generic;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>IgnoreSpaces mode: absorbs whitespace-free tokens, so the compared texts
    /// carry no spaces at all and edits never hinge on spacing differences.</summary>
    internal class IgnoreSpacesFragmentsProcessor : NormalFragmentProcessor
    {
        private static readonly Regex NonSpaceRuns = new Regex(@"\S+", RegexOptions.Compiled);

        internal IgnoreSpacesFragmentsProcessor(TextFragmentRectanglesComparer fragmentsComparer)
            : base(fragmentsComparer)
        {
        }

        protected override List<TextFragment> GetTextFragments(Page page)
        {
            var absorber = new TextFragmentAbsorber(NonSpaceRuns);
            absorber.Visit(page);
            var fragments = new List<TextFragment>();
            foreach (var fragment in absorber.TextFragments)
                if (!string.IsNullOrEmpty(fragment.Text))
                    fragments.Add(fragment);
            return fragments;
        }
    }
}
