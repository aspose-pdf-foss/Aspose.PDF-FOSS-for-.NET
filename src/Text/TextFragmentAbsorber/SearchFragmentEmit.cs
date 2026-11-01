using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
// Search fragments: the match emitter.
    // Phase 3: for each match build a TextFragment with position, rect and
    // segments; ends with the same-phrase RTL reorder over [preCount..).
    private void EmitMatches(SearchFragmentState sf, MatchCollection matches, int preCount)
    {
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
    foreach (Match match in matches)
#pragma warning restore S3267
    {
        if (!EmitMatch(sf, match)) break;
    }

    sf.newMatches = _fragments.Count - preCount;
    sf.samePhrase = sf.newMatches > 1;
    sf.anyRtl = false;
    for (var i = preCount; sf.samePhrase && i < _fragments.Count; i++)
    {
        var t = _fragments.GetInternal(i).Text;
        if (i > preCount && !string.Equals(t, _fragments.GetInternal(preCount).Text, StringComparison.Ordinal))
            sf.samePhrase = false;
        if (t.Any(ch => BidiReorderer.IsRtlChar(ch))) { sf.anyRtl = true; }
    }
    if (sf.newMatches > 1 && sf.samePhrase && sf.anyRtl && HasMajorUpwardJump(sf.rawFragments))
    {
        var inner = _fragments.Inner;
        var slice = inner.GetRange(preCount, inner.Count - preCount);
        slice.Sort((a, b) =>
        {
            var ya = a.Position?.YIndent ?? 0;
            var yb = b.Position?.YIndent ?? 0;
            if (Math.Abs(ya - yb) > 0.5) return yb.CompareTo(ya); // top first
            return (a.Position?.XIndent ?? 0).CompareTo(b.Position?.XIndent ?? 0);
        });
        for (var i = 0; i < slice.Count; i++) inner[preCount + i] = slice[i];
    }
    }
}
