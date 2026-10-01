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
    /// <summary>Lays a chain of inline paragraphs out as one line: the joinable members become the per-segment styled runs of a single composite fragment (HTML members in the serif body face, text members keeping their state), which flows as one paragraph; the index skips the consumed members. True when the chain was laid out here.</summary>
    private bool LayoutInlineJoinedRun(PageContentLayoutState lc, BaseParagraph para)
    {
        var members = new List<BaseParagraph> { para };
        var k = lc.paraIdx + 1;
        for (; k < lc.pl.paraList.Count && ParagraphInlineFlag(lc.pl.paraList[k])
               && InlineJoinable(lc.pl.paraList[k]) is (_, _); k++)
            members.Add(lc.pl.paraList[k]);
        if (members.Count > 1)
        {
            if (LayoutInlineJoinedMembers(lc, members, k)) return true;
        }
        return false;
    }

    /// <summary>Joins two or more inline members into one composite fragment of styled segments and flows it as a single paragraph, advancing the index past the members it consumed. True when the chain was laid out here.</summary>
    private bool LayoutInlineJoinedMembers(PageContentLayoutState lc, List<BaseParagraph> members, int k)
    {
        var ij = new InlineJoinedRunState();
        ij.members = members;
        ij.k = k;
        ij.flow = lc.pl.flow;
        ij.joined = new Text.TextFragment();
        ij.anyHtmlStyled = false;
        CollectInlineJoinedSegments(ij);
        if (ij.flow.TryWriteStyledSegmentsLine(ij.joined))
        {
            lc.paraIdx = ij.k - 1;
            return true;
        }
        // Too wide for one line: CSS-styled inline members flow as
        // ONE wrapped paragraph. The first line sets on the leading
        // the opening fragment declares, the rest on the HTML
        // 1.12-em rhythm.
        if (FlowInlineJoinedHtmlLine(ij))
        {
            lc.paraIdx = ij.k - 1;
            return true;
        }
        return false;
    }

    /// <summary>When the chain holds HTML-styled members, lays the composite line out through the HTML paragraph path at the serif body face; true when it was laid out there.</summary>
    private bool FlowInlineJoinedHtmlLine(InlineJoinedRunState ij)
    {
        if (ij.anyHtmlStyled)
        {
            var styRuns2 = new List<FlowLayout.StyledRun>();
            double maxFs2 = 0, introLs = 0;
            foreach (var member in ij.members)
                if (member is Text.TextFragment lsf)
                {
                    if (lsf.TextState.LineSpacing > introLs) introLs = lsf.TextState.LineSpacing;
                    foreach (Text.TextSegment lss in lsf.Segments)
                        if (lss.TextState.LineSpacing > introLs) introLs = lss.TextState.LineSpacing;
                }
            foreach (var seg in ij.joined.Segments)
            {
                if (string.IsNullOrEmpty(seg.Text)) continue;
                var sz = seg.TextState.FontSizeTouched ? (double)seg.TextState.FontSize : 12.0;
                if (sz > maxFs2) maxFs2 = sz;
                styRuns2.Add(new FlowLayout.StyledRun
                {
                    Text = seg.Text, Size = sz, State = seg.TextState,
                });
            }
            if (styRuns2.Count > 0 && maxFs2 > 0)
            {
                // members wrap ATOMICALLY: one joins the current line
                // only when it fits whole, else it opens the next —
                // and a member longer than a full line word-wraps
                // alone. Greedy grouping, then one write per line.
                double RunWidth(FlowLayout.StyledRun r)
                {
                    var f = r.State.IsItalic ? "Helvetica-Oblique" : "Helvetica";
                    try
                    {
                        return Text.FontRepository.TryFindFont(f)
                            ?.MeasureString(r.Text, r.Size) ?? r.Text.Length * r.Size * 0.5;
                    }
                    catch { return r.Text.Length * r.Size * 0.5; }
                }
                var lineGroups = new List<List<FlowLayout.StyledRun>> { new() };
                var lw = 0.0;
                foreach (var r in styRuns2)
                {
                    var w = RunWidth(r);
                    if (lineGroups[^1].Count > 0 && lw + w > ij.flow.CurWidth + 0.5)
                    { lineGroups.Add(new()); lw = 0; }
                    lineGroups[^1].Add(r);
                    lw += w;
                }
                // the first line sets on the leading the opening
                // fragment declares (its box closes at the baseline);
                // the rest keep the HTML 1.12-em rhythm
                var htmlLead = maxFs2 * 0.12;
                for (var lg = 0; lg < lineGroups.Count; lg++)
                    ij.flow.WriteStyledParagraph(lineGroups[lg],
                        lg == 0 && introLs > 0
                            ? introLs - 0.2075 * maxFs2 : htmlLead);
                return true;
            }
        }
        return false;
    }

    /// <summary>Turns each member into a styled segment of the composite fragment: HTML members in the serif body face and their inline style, text members with their own state.</summary>
    private void CollectInlineJoinedSegments(InlineJoinedRunState ij)
    {
        foreach (var member in ij.members)
        {
            var (mText, mSerif) = InlineJoinable(member) ?? (string.Empty, false);
            var seg = new Text.TextSegment(mText);
            if (member is Text.TextFragment mf)
                seg.TextState.ApplyChangesFrom(mf.TextState);
            else if (member is HtmlFragment mh
                && System.Text.RegularExpressions.Regex.Match(mh.HtmlContent ?? "",
                    @"<span\b[^>]*style\s*=\s*(['""])(?<s>[^'""]*)\1",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    is { Success: true } mSty)
            {
                // an inline HTML member styles its run from its
                // outermost span's own CSS
                ij.anyHtmlStyled = true;
                var css = mSty.Groups["s"].Value;
                var fsm2 = System.Text.RegularExpressions.Regex.Match(css,
                    @"font-size\s*:\s*([\d.]+)\s*(pt|px)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fsm2.Success)
                {
                    var v = double.Parse(fsm2.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    seg.TextState.FontSize = (float)(fsm2.Groups[2].Value
                        .Equals("px", StringComparison.OrdinalIgnoreCase) ? v * 0.75 : v);
                }
                var fam = System.Text.RegularExpressions.Regex.Match(css,
                    @"font-family\s*:\s*['""]?([^;'""]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fam.Success) seg.TextState.FontName = fam.Groups[1].Value.Trim();
                var col = System.Text.RegularExpressions.Regex.Match(css,
                    @"(?<![-\w])color\s*:\s*([^;]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (col.Success && Converters.HtmlToPdfConverter
                        .ParseCssColor(col.Groups[1].Value.Trim()) is { } cc)
                    seg.TextState.ForegroundColor = cc;
                if (System.Text.RegularExpressions.Regex.IsMatch(css,
                        @"font-style\s*:\s*italic",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    seg.TextState.IsItalic = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(css,
                        @"text-decoration\s*:\s*line-through",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    seg.TextState.IsStrikeOut = true;
            }
            else if (mSerif)
                seg.TextState.FontName = "TimesNewRoman";
            ij.joined.Segments.Add(seg);
        }
    }
}
