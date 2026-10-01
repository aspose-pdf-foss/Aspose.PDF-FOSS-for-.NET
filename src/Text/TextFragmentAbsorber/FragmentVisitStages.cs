using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>The stages of the fragment visit: the single-stream walk and the annotation search.</summary>
    private void VisitAnnotationText(FragmentVisitState fv)
    {
        foreach (var annotation in fv.page.Annotations)
        {
            var appearance = annotation?.NormalAppearance;
            if (appearance is not null) Visit(appearance);
        }
    }

    /// <summary>x</summary>
    private void VisitSingleStream(FragmentVisitState fv)
    {
        // Search for the phrase in concatenated text, then map matches
        // back to source runs for bounding rectangles
        BuildSearchFragments(fv.rawFragments, fv.page.Index, fv.page, fillRects: fv.fillRects);

        // Apply rectangle filter if set — a fragment is kept when its bounding box overlaps the
        // search rect (a search box that clips only the ascender band of a
        // run still finds it), falling back to start-position containment when no bbox is known.
        if (fv.searchRect is not null && !fv.searchRect.IsEmpty)
        {
            for (var i = _fragments.Count - 1; i >= 0; i--)
            {
                if (!FragmentInSearchRect(fv.searchRect, _fragments.GetInternal(i)))
                {
                    // Only remove fragments added during this Visit call (they have matching pageIndex)
                    if (_fragments.GetInternal(i).PageIndex == fv.page.Index)
                        _fragments.RemoveAt(i);
                }
            }
        }
    }
}
