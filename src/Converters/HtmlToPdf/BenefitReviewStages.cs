using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Benefit review: the wrapped lines seated and emitted, breaking pages at the content bottom.</summary>
    private static void EmitBenefitReviewLines(BenefitReviewState bv)
    {
        foreach (var line in bv.lines)
        {
            var subs = WrapLine(bv, line);
            for (var si = 0; si < subs.Count; si++)
            {
                var sub = subs[si];
                bv.yBase = bv.prev is null
                    ? TopSeat(bv, sub.Kind)
                    : bv.yBase + (si == 0 ? Pitch(bv, bv.prev.Value, sub.Kind) : BrLinePt);
                if (bv.yBase + FsOf(bv, sub.Kind) * BrDescFrac > BrBottomPt && bv.prev is not null)
                {
                    OpenPage(bv);
                    bv.yBase = TopSeat(bv, sub.Kind);
                }
                if (sub.Kind == BrKind.Li && si == 0)
                    bv.sb.AppendLine($"BT 0 0 0 rg /F5 {BrBodyFs.ToString("F2", bv.inv)} Tf "
                        + $"1 0 0 1 {N(bv, BrBulletX)} {N(bv, Y(bv, bv.yBase))} Tm ({EscapePdfString("•")}) Tj ET");
                EmitLineAt(bv, sub, bv.yBase, sub.Kind == BrKind.Li ? BrLiTextX : BrTextX);
                bv.prev = sub.Kind;
            }
        }
    }

    /// <summary>Benefit review: the document and its first page opened.</summary>
    private static void OpenBenefitReviewDocument(BenefitReviewState bv)
    {
        bv.inv = System.Globalization.CultureInfo.InvariantCulture;
        bv.doc = new Document();
        bv.page = null!;
        bv.sb = null!;
        bv.pageBufs = new List<(Page Page, StringBuilder Buf)>();
        OpenPage(bv);
        bv.prev = null;
    }

    /// <summary>Benefit review: one tag of the body absorbed, with the text before it.</summary>
    private static bool AbsorbBenefitReviewTag(BenefitReviewState bv, Match m)
    {
        if (m.Index > bv.pos) AddText(bv, bv.body[bv.pos..m.Index]);
        bv.pos = m.Index + m.Length;
        var closing = m.Groups[1].Value.Length > 0;
        var tag = m.Groups[2].Value;
        var attrs = m.Groups[3].Value;
        if (bv.voidTags.Contains(tag))
        {
            if (!closing && bv.hiddenDepth == 0
                && tag.Equals("br", StringComparison.OrdinalIgnoreCase)) Flush(bv);
            return true;
        }
        if (!closing)
        {
            var classM = Regex.Match(attrs, "class[ ]*=[ ]*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            var styleM = Regex.Match(attrs, "style[ ]*=[ ]*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            var hidden = attrs.Contains("aria-hidden=\"true\"", StringComparison.OrdinalIgnoreCase)
                || (classM.Success && Regex.IsMatch(classM.Groups[1].Value, "(^| )ng-hide( |$)"))
                || (styleM.Success && Regex.IsMatch(styleM.Groups[1].Value,
                    "display[ ]*:[ ]*none", RegexOptions.IgnoreCase));
            var isBold = tag.Equals("strong", StringComparison.OrdinalIgnoreCase)
                || tag.Equals("b", StringComparison.OrdinalIgnoreCase);
            // an anchor is a link when it CARRIES an href — even an empty
            // one (the ng-click actions keep href=""); no attribute at all
            // (the nav tabs) draws as plain text
            var isLink = tag.Equals("a", StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(attrs, "(^|[ ])href[ ]*=", RegexOptions.IgnoreCase);
            var isLi = tag.Equals("li", StringComparison.OrdinalIgnoreCase);
            var isH1 = tag.Equals("h1", StringComparison.OrdinalIgnoreCase);
            var isH3 = tag.Equals("h3", StringComparison.OrdinalIgnoreCase)
                || tag.Equals("h2", StringComparison.OrdinalIgnoreCase);
            // a hidden block leaves no box, so it does not split the line
            if (bv.blockTags.Contains(tag) && bv.hiddenDepth == 0 && !hidden) Flush(bv);
            bv.stack.Add((tag, hidden, isBold, isLink, isLi, isH1, isH3));
            if (hidden) bv.hiddenDepth++;
            if (isBold) bv.boldDepth++;
            if (isLink) bv.linkDepth++;
            if (isLi) bv.liDepth++;
            if (isH1) bv.h1Depth++;
            if (isH3) bv.h3Depth++;
        }
        else
        {
            var s = bv.stack.FindLastIndex(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (s < 0) return true; // unmatched close: ignore
            // flush BEFORE popping, while the li/h1/h3 context still names
            // the line's kind
            if (bv.blockTags.Contains(tag) && bv.hiddenDepth == 0 && !bv.stack[s].Hidden) Flush(bv);
            // pop through any unclosed inner tags up to the match
            for (var k = bv.stack.Count - 1; k >= s; k--)
            {
                var p = bv.stack[k];
                if (p.Hidden) bv.hiddenDepth--;
                if (p.Bold) bv.boldDepth--;
                if (p.Link) bv.linkDepth--;
                if (p.Li) bv.liDepth--;
                if (p.H1) bv.h1Depth--;
                if (p.H3) bv.h3Depth--;
                bv.stack.RemoveAt(k);
            }
        }
        return true;
    }

    /// <summary>Benefit review: the block and void tag sets built.</summary>
    private static void BuildBenefitReviewTagSets(BenefitReviewState bv)
    {
        bv.blockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "div", "p", "h1", "h2", "h3", "h4", "ul", "ol", "li", "table", "tbody",
            "thead", "tr", "td", "th", "section", "header", "footer", "nav", "form",
            "fieldset", "blockquote", "pre", "article", "aside",
        };
        bv.voidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "br", "img", "input", "hr", "meta", "link", "col", "wbr", "source" };
    }
}
