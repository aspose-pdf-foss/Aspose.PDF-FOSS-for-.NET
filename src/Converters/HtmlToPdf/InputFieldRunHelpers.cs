using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The helpers of the inline input-field run: closing one run line, placing one item and emitting one line.
    private static void EndRunLine(InputFieldBlockState ib)
    {
        if (ib.curItems.Count == 0) return;
        ib.runLines.Add((ib.curItems, ib.curHasText, ib.curMaxAdv, ib.curMaxAbove));
        ib.curItems = new List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)>();
        ib.pen = ib.lineLeft; ib.curHasText = false; ib.curMaxAdv = 0; ib.curMaxAbove = 0;
    }

    /// <summary></summary>
    private static bool EmitInlineFieldLine(InputFieldBlockState ib, List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)> items, bool hasText, double maxAdv, double maxAbove)
    {
        // A line with a mid-line TALL control drops extra first so
        // the box top clears the content above it.
        if (maxAbove > InputBoxAboveBaselinePt)
            ib.flow.y -= maxAbove - InputBoxAboveBaselinePt;
        // A control line advances by the control's flow cost; carrying
        // body text beside it adds the descent clearance. Text-only
        // (wrap remainder) lines keep the normal line box.
        var adv = maxAdv > 0 ? maxAdv + (hasText ? InlineMixedExtraPt : 0)
            : NormalLineHeightPt(ib.blockFontSize > 0 ? ib.blockFontSize : EscapedBodyFontPt);
        if (ib.flow.y - (maxAbove > InputBoxAboveBaselinePt ? SerifDescentRoomPt : adv) < ib.marginBottom)
        {
            ib.flow.page = ib.doc.Pages.Add(ib.pageWidth, ib.pageHeight);
            EnsureFonts(ib.flow.page, ib.docFontDict);
            ib.flow.y = FreshPageTopY(ib.profile, ib.pageHeight, ib.marginTop); ib.flow.pendingTopDrop = ib.profile.hasZeroTopMargin;
        }
        foreach (var (ctl, txt, x, fpt, res) in items)
        {
            if (ctl is not null)
                EmitControlAt(ctl, x, ib.flow.y, ib.flow, ib.doc, ib.lineHeight,
                    aboveOverride: ctl.InputMultiline && x > ib.lineLeft + 1e-6
                        && ctl.InputHeight > 0
                        ? ctl.InputHeight - TextareaBottomHangPt : null);
            else if (!string.IsNullOrEmpty(txt))
                EmitSerifRun(txt, res, fpt, x, ib.flow.y, ib.flow);
        }
        ib.flow.contentPage = ib.flow.page;
        ib.flow.y -= adv;
        return true;
    }

    /// <summary></summary>
    private static bool PlaceInlineFieldItem(InputFieldBlockState ib, Block it)
    {
        if (it.IsInputField)
        {
            var cW = it.InputWidth > 0
                ? System.Math.Min(it.InputWidth, ib.flow.contentWidth) : ib.flow.contentWidth;
            var penW = cW + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0);
            if (ib.curItems.Count > 0 && ib.pen + penW > ib.lineRight + 1e-6) EndRunLine(ib);
            // A tall control MID-LINE anchors its box BOTTOM at the
            // baseline and grows UP: it advances the flow like a
            // one-row control, but its line drops extra so the box
            // top clears the content above.
            var midLineTall = ib.curItems.Count > 0 && it.InputMultiline;
            ib.curItems.Add((it, null, ib.pen, 0, ""));
            ib.curMaxAdv = System.Math.Max(ib.curMaxAdv, midLineTall
                ? ControlFirstRowAdvancePt
                : it.InputAdvance > 0 ? it.InputAdvance : ControlFirstRowAdvancePt);
            if (midLineTall && it.InputHeight > 0)
                ib.curMaxAbove = System.Math.Max(ib.curMaxAbove, it.InputHeight - TextareaBottomHangPt);
            ib.pen += penW;
        }
        else if (!string.IsNullOrEmpty(it.Text))
        {
            var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
            var res = it.FontRes == "F2" ? "F6" : it.FontRes == "F3" ? "F7" : "F5";
            var face = res == "F6" ? "Times-Bold"
                : res == "F7" ? "Times-Italic" : "Times-Roman";
            int p = 0;
            while (p < it.Text.Length)
            {
                var sp = it.Text.IndexOf(' ', p);
                var wordEnd = sp < 0 ? it.Text.Length : sp + 1;
                while (wordEnd < it.Text.Length && it.Text[wordEnd] == ' ') wordEnd++;
                var token = it.Text.Substring(p, wordEnd - p);
                p = wordEnd;
                var wTrim = MeasureStd14(face, token.TrimEnd(' '), fpt);
                if (ib.curItems.Count > 0 && ib.pen + wTrim > ib.lineRight + 1e-6) EndRunLine(ib);
                // The space a wrap breaks at vanishes at the fresh line's start.
                var draw = ib.curItems.Count == 0 ? token.TrimStart(' ') : token;
                if (draw.Length == 0) continue;
                ib.curItems.Add((null, draw, ib.pen, fpt, res));
                ib.curHasText = true;
                ib.pen += MeasureStd14(face, draw, fpt);
            }
        }
        return true;
    }
}
