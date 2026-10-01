using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Search fragments: one regex match becomes a text fragment.</summary>
    private bool EmitMatch(SearchFragmentState sf, Match match)
    {
        var em = new EmitMatchState();
        if (SkipsMatch(sf, em, match)) return true;
        ResolveMatchRuns(sf, em, match);
        em.fragment = new TextFragment(em.absorbedText, em.rect, em.textState)
        {
            PageIndex = sf.pageIndex,
            Position = new Position(Q(em.posX), Q(em.posY)),
            SourcePage = sf.sourcePage,
            Form = sf.sourceForm,
            SourceXObjStream = em.firstRun.SourceXObj,
            TextDirX = em.sTdx, TextDirY = em.sTdy,
            ExtractionCtm = new Aspose.Pdf.Matrix(em.firstRun.Ctm.A, em.firstRun.Ctm.B,
                em.firstRun.Ctm.C, em.firstRun.Ctm.D, em.firstRun.Ctm.E, em.firstRun.Ctm.F),
            ExtractionTmTy = em.firstRun.TmBaseY,
            TrailingTcPageSpace = em.trailingTc,
            ReplaceOptions = TextReplaceOptions,
        };
        DetectMatchUnderline(sf, em, match);
        ApplyCapturedMarks(sf, em);
        // Underline off must splice out all of them.
        TryUnderlineFromSource(sf, em);
        _fragments.Add(em.fragment);
        return true;
    }
}
