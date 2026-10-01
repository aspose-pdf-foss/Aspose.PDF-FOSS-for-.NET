using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Renders one UA-serif element: a heading or paragraph's inline runs (bold spans, breaks) wrapped to the UA width at the element's size, seated at the UA margins with the block rhythm.</summary>
    private bool RenderUaSerifElement(UaSerifChunkState uc, System.Text.RegularExpressions.Match em)
    {
        uc.isHead = false;
        if (em.Groups["tag"].Success)
        {
            uc.isHead = em.Groups["tag"].Value.StartsWith("h",
                StringComparison.OrdinalIgnoreCase);
            uc.inner = em.Groups["in"].Value;
        }
        else
        {
            uc.inner = em.Groups["bare"].Value;
            if (uc.inner.Trim().Length == 0) return true;
        }
        uc.uaRuns = new List<(string T, Color? C, bool Bold, bool Styled, bool Lead, bool Trail)>();
        uc.uaStack = new Stack<(Color?, bool)>();
        uc.uaC = null;
        uc.uaStyled = false;
        uc.uaBold = uc.isHead ? 1 : 0;
        uc.rp = 0;
        uc.uaForceLead = false;
        TokenizeUaSerifRuns(uc);
        if (uc.uaRuns.Count == 0)
        {
            // a <p> holding only <br> keeps one blank line box;
            // a truly empty <p> takes nothing
            if (System.Text.RegularExpressions.Regex.IsMatch(uc.inner, @"<br\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                uc.flow.AdvanceY(UaSerifPitchPt);
                uc.uaAtBoxEdge = true;
                uc.uaAfterHead = false;
            }
            return true;
        }
        uc.uaFs = uc.isHead ? UaSerifH2Pt : UaSerifPt;
        uc.uaLines = new List<List<(double X, string T, Color? C, bool Bold)>>();
        uc.uaCur = new List<(double, string, Color?, bool)>();
        uc.uaLineStyled = false;
        uc.uaX = 0;
        uc.uaPrevOpen = false;   // previous run ended mid-word
        WrapUaSerifRuns(uc);
        if (uc.uaCur.Count > 0) uc.uaLines.Add(uc.uaCur);
        uc.firstOfElement = true;
        EmitUaSerifLines(uc);
        return true;
    }

    /// <summary>Writes the wrapped lines: each run at its pen in the serif face (bold where marked), the block advancing by the line pitch.</summary>
    private void EmitUaSerifLines(UaSerifChunkState uc)
    {
        foreach (var line2 in uc.uaLines)
        {
            var drop = uc.uaAtBoxEdge ? UaSerifSeatPt
                : uc.isHead && uc.firstOfElement ? UaSerifH2BeforePt
                : uc.uaAfterHead ? UaSerifH2AfterPt
                : uc.firstOfElement && uc.uaLineStyled ? UaSerifMixedPitchPt
                : UaSerifPitchPt;
            uc.flow.AdvanceY(drop);
            var baseY = uc.flow.CurrentY;
            foreach (var (lx, lt, lc, lb) in line2)
            {
                if (lc is { } lcc)
                    uc.uaB.SetFillColor(lcc.R / 255.0, lcc.G / 255.0, lcc.B / 255.0);
                uc.uaB.BeginText().SetFont(lb ? uc.uaTimesB : uc.uaTimes, uc.uaFs)
                   .MoveTextPosition(uc.marginLeft + lx, baseY)
                   .ShowText(lt).EndText();
                if (lc is not null) uc.uaB.SetFillColor(0, 0, 0);
            }
            uc.uaAfterHead = uc.isHead;
            uc.uaAtBoxEdge = false;
            uc.firstOfElement = false;
        }
    }

    /// <summary>Wraps the element's runs into lines at the UA width, measuring word by word, keeping a run's leading and trailing spaces as marked.</summary>
    private void WrapUaSerifRuns(UaSerifChunkState uc)
    {
        for (var ri = 0; ri < uc.uaRuns.Count; ri++)
        {
            var (rt, rc, rb, rstyled, rlead, rtrail) = uc.uaRuns[ri];
            var runWords = rt.Split(' ');
            for (var wi = 0; wi < runWords.Length; wi++)
            {
                var word = runWords[wi];
                if (word.Length == 0) continue;
                var w2 = UaMeasure(word, rb, uc.uaFs);
                // a run starting without whitespace continues the
                // previous run's word ("opmaak" + "." = "opmaak.")
                var glue = wi == 0 && uc.uaPrevOpen && !rlead;
                if (glue && uc.uaCur.Count > 0)
                    uc.uaX -= UaMeasure(" ", rb, uc.uaFs);
                if (!glue && uc.uaCur.Count > 0 && uc.uaX + w2 > uc.uaWrapPt)
                {
                    uc.uaLines.Add(uc.uaCur);
                    uc.uaCur = new List<(double, string, Color?, bool)>();
                    uc.uaX = 0;
                }
                uc.uaCur.Add((uc.uaX, word, rc, rb));
                uc.uaX += w2 + UaMeasure(" ", rb, uc.uaFs);
                if (rstyled) uc.uaLineStyled = true;
            }
            uc.uaPrevOpen = !rtrail;
        }
    }

    /// <summary>Splits the element's inline markup into runs: text between tags, a span or strong/b/em/i pushing and popping colour and weight, a break closing the run, the tail flushed.</summary>
    private void TokenizeUaSerifRuns(UaSerifChunkState uc)
    {
        void EmitRun(string raw)
        {
            if (raw.Length == 0) return;
            var lead = uc.uaForceLead || char.IsWhiteSpace(raw[0]);
            var t = System.Text.RegularExpressions.Regex.Replace(
                HtmlFragment.StripHtmlTags(raw), @"\s+", " ").Trim();
            if (t.Length == 0) { uc.uaForceLead = true; return; }
            uc.uaRuns.Add((t, uc.uaC, uc.uaBold > 0, uc.uaStyled, lead,
                char.IsWhiteSpace(raw[^1])));
            uc.uaForceLead = false;
        }
        foreach (System.Text.RegularExpressions.Match tg in
            System.Text.RegularExpressions.Regex.Matches(uc.inner, @"<[^>]*>"))
        {
            EmitRun(uc.inner[uc.rp..tg.Index]);
            uc.rp = tg.Index + tg.Length;
            var tag2 = tg.Value;
            if (System.Text.RegularExpressions.Regex.IsMatch(tag2, @"^<\s*/\s*span",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            { if (uc.uaStack.Count > 0) (uc.uaC, uc.uaStyled) = uc.uaStack.Pop(); }
            else if (System.Text.RegularExpressions.Regex.IsMatch(tag2, @"^<\s*span",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                uc.uaStack.Push((uc.uaC, uc.uaStyled));
                var st = System.Text.RegularExpressions.Regex.Match(tag2,
                    @"style\s*=\s*(['""])(?<s>[^'""]*)\1",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (st.Success)
                {
                    var cm2 = System.Text.RegularExpressions.Regex.Match(
                        st.Groups["s"].Value, @"(?<![-\w])color\s*:\s*([^;]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (cm2.Success && Converters.HtmlToPdfConverter
                            .ParseCssColor(cm2.Groups[1].Value.Trim()) is { } cc2)
                    { uc.uaC = cc2; uc.uaStyled = true; }
                }
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(tag2,
                @"^<\s*(strong|b)[\s>]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) uc.uaBold++;
            else if (System.Text.RegularExpressions.Regex.IsMatch(tag2,
                @"^<\s*/\s*(strong|b)[\s>]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) uc.uaBold--;
        }
        EmitRun(uc.inner[uc.rp..]);
    }
}
