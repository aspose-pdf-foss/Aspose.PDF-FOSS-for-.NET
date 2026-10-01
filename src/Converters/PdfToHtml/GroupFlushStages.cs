using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Group flush stages: the text group emitted as HTML.</summary>
    private static void FlushTextGroup(GroupFlushState fg)
    {
        if (fg.styleReg is not null && string.IsNullOrWhiteSpace(fg.groupText.ToString()))
        {
            // Whitespace-only group (re-drawn word-gap space glyphs over an
            // already-shown line, a positioned space show between columns, or
            // a stray trailing space glyph past the text): dropped in BOTH
            // stl_ dialects — a lone space div
            // must not grow the re-imported page width, and its font class
            // must not burn a class number.
        }
        else if (fg.styleReg is not null)
        {
            FlushStyledGroup(fg);
        }
        else
        {
            EmitSpan(fg.sb, fg.groupText.ToString(), fg.ct.groupX, fg.ct.groupY, fg.ct.groupFontSize,
                fg.ct.groupFamily, fg.ct.groupWeight, fg.ct.groupStyle, fg.ct.groupR, fg.ct.groupG, fg.ct.groupB, fg.pageHeight,
                fg.ct.groupRise, transparentText: fg.textOnly,
                rotationClass: fg.ct.groupAngle != 0 && fg.rotReg is not null
                    ? fg.rotReg.Class(fg.ct.groupAngle) : null);
        }
    }

    /// <summary></summary>
    private static void FlushStyledGroup(GroupFlushState fg)
    {
        var sg = new StyledGroupState();
        sg.color = fg.ct.groupTransparent
            ? TransparentTextColor
            : $"#{(int)(Compat.Clamp(fg.ct.groupR, 0, 1) * 255):X2}{(int)(Compat.Clamp(fg.ct.groupG, 0, 1) * 255):X2}{(int)(Compat.Clamp(fg.ct.groupB, 0, 1) * 255):X2}";
        sg.fontNum = fg.styleReg!.Font(fg.ct.groupCssFamily, fg.ct.groupFontSize / 12.0, sg.color, null);
        sg.lhNum = fg.styleReg.LineHeight(fg.ct.groupLineHeight > 0 ? Math.Round(fg.ct.groupLineHeight, 6) : 1.2);
        sg.fs = Math.Max(0.01, fg.ct.groupFontSize);
        sg.textAll = fg.groupText.ToString();
        sg.face = fg.ct.groupPinned && fg.ct.groupEndX > fg.ct.groupX + 0.01
            && !string.IsNullOrWhiteSpace(sg.textAll)
            ? HtmlToPdfConverter.ResolveStlFace(fg.ct.groupFamily) : null;

        sg.yTop = double.IsNaN(fg.yTopRef) ? fg.pageHeight : fg.yTopRef;
        sg.left = (fg.ct.groupX - fg.pageLLX) / 12.0;
        sg.top = (sg.yTop - fg.ct.groupY - fg.ct.groupAscent * fg.ct.groupFontSize) / 12.0;
        if (fg.pageTurnedOver) { sg.left -= fg.pageWidth / 12.0; sg.top -= fg.pageHeight / 12.0; }
        sg.divCls = fg.ct.groupAngle != 0
            ? $"{fg.classNamer.Cls("01")} {fg.classNamer.Cls(fg.styleReg.Rotation(Math.Round(fg.ct.groupAngle, 2)))}"
            : fg.classNamer.Cls("01");
        sg.zStyle = fg.zCounter is not null && fg.ct.groupZ > 0 ? $"z-index:{fg.ct.groupZ};" : "";
        fg.sb.Append($"<div class=\"{sg.divCls}\" style=\"left:{Em4T(sg.left)}em;top:{Em4T(sg.top)}em;{sg.zStyle}\">");
        sg.link = fg.ct.groupIsType3
            ? null : FindLinkTarget(fg.linkTargets, fg.ct.groupX, fg.ct.groupPenX, fg.ct.groupY);
        sg.popupItems = sg.link?.PopupItems;
        sg.linkOpen = sg.link is null || sg.popupItems is not null
            ? null
            : $"<a href=\"{EscapeHtml(sg.link.Uri)}\"" +
                (sg.link.Uri.StartsWith('#') ? ">" : " target=\"_blank\">");
        // The single-span path opens the anchor around its one span here;
        // the pinned multi-segment path wraps EACH span in its own anchor.
        // Wrapped is set only where an anchor is really written: it is what
        // suppresses the click-surface overlay.
        if (sg.linkOpen is not null && sg.face is null)
        {
            fg.sb.Append(sg.linkOpen);
            if (sg.link is not null) sg.link.Wrapped = true;
        }

        sg.popupBoxNum = 0;
        if (sg.popupItems is not null)
        {
            sg.popupBoxNum = fg.styleReg.PopupBox();
            fg.sb.Append($"<div class=\"{fg.classNamer.Cls(sg.popupBoxNum)}\">");
        }

        if (sg.face is not null)
        {
            EmitFaceGroup(fg, sg);
        }
        else
        {
            EmitPlainGroup(fg, sg);
        }
    }

    /// <summary></summary>
    private static void EmitPlainGroup(GroupFlushState fg, StyledGroupState sg)
    {
        var lsEm = fg.ct.charSpacing / Math.Max(0.01, fg.ct.groupFontSize)
            - fg.ct.groupTjNum / (1000.0 * Math.Max(1, fg.ct.groupChars));
        var lsNum = fg.styleReg!.LetterSpacing(System.Math.Round(lsEm, 4));
        // A same-colour hairline under (or through) the baseline that
        // covers the run start becomes CSS text-decoration; a
        // decorated span carries the inline style and drops the
        // trailing &nbsp;.
        var decoration = FindDecoration(fg.ct.rules, fg.ct.groupX, fg.ct.groupY, fg.ct.groupFontSize,
            fg.ct.groupR, fg.ct.groupG, fg.ct.groupB);
        fg.sb.Append($"<span class=\"{fg.classNamer.Attr(sg.fontNum, sg.lhNum, lsNum)}\"");
        var groupWeightCss = StlWeightStyleCss(fg.ct.groupFauxBold, fg.ct.groupDeclStyle);
        if (decoration is not null)
            fg.sb.Append($" style=\"{groupWeightCss}text-decoration:{decoration};word-spacing:0em;\"");
        else if (groupWeightCss.Length > 0)
            fg.sb.Append($" style=\"{groupWeightCss}\"");
        fg.sb.Append('>');
        // A non-trivial text rise marks a superscript/subscript run:
        // wrap it in <sup>/<sub> so the markup carries the semantics
        // (the .stl_ sup/sub CSS rules already position them). The
        // rise is baked into `top`, so the tags are purely semantic -
        // see EmitSpan for the non-stl_ counterpart.
        var stlInner = EscapeHtml(fg.groupText.ToString());
        if (fg.ct.groupRawRise > RiseThreshold) stlInner = $"<sup>{stlInner}</sup>";
        else if (fg.ct.groupRawRise < -RiseThreshold) stlInner = $"<sub>{stlInner}</sub>";
        fg.sb.Append(stlInner)
            .Append(decoration is not null || sg.popupItems is not null ? "</span>" : " &nbsp;</span>");
        if (sg.popupItems is not null)
        {
            var listNum = fg.styleReg.PopupList(sg.popupBoxNum);
            fg.sb.Append($"<div class=\"{fg.classNamer.Cls(listNum)}\">");
            foreach (var (label, href) in sg.popupItems)
                fg.sb.Append($"<a href=\"{href}\" class=\"{fg.classNamer.Cls(sg.fontNum)} " +
                    $"{fg.classNamer.Cls(sg.lhNum)}  {fg.classNamer.Cls(lsNum)}\">{EscapeHtml(label)}</a>");
            fg.sb.Append("</div></div>");
        }
        fg.sb.Append(sg.linkOpen is not null ? "</a></div>\n" : "</div>\n");
    }

    /// <summary></summary>
    private static void EmitFaceGroup(GroupFlushState fg, StyledGroupState sg)
    {
        var ef = new FaceGroupState();
        ef.ordered = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>(fg.ct.groupSegs);
        ef.ordered.Sort((a, b) => a.X.CompareTo(b.X));
        ef.emit = new List<(string text, double startX, double glyphEnd)>();
        ef.coveredTo = double.MinValue;
        foreach (var seg in ef.ordered)
        {
            var st = seg.Text.ToString();
            if (st.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(st) && seg.X < ef.coveredTo - 0.5) continue;
            ef.emit.Add((st, seg.X, seg.GlyphEnd));
            ef.coveredTo = Math.Max(ef.coveredTo, seg.GlyphEnd);
        }
        ef.spaceAdv = HtmlToPdfConverter.MeasureStlExactText(sg.face!, " ", sg.fs);
        MergeFaceGaps(fg, sg, ef);
        ef.lastLsNum = 0;
        EmitFaceSegments(fg, sg, ef);
        if (sg.popupItems is not null)
        {
            var listNum = fg.styleReg!.PopupList(sg.popupBoxNum);
            fg.sb.Append($"<div class=\"{fg.classNamer.Cls(listNum)}\">");
            foreach (var (label, href) in sg.popupItems)
                fg.sb.Append($"<a href=\"{href}\" class=\"{fg.classNamer.Cls(sg.fontNum)} " +
                    $"{fg.classNamer.Cls(sg.lhNum)}  {fg.classNamer.Cls(ef.lastLsNum)}\">{EscapeHtml(label)}</a>");
            fg.sb.Append("</div></div>");
        }
        fg.sb.Append("</div>\n");
    }

    /// <summary></summary>
    private static void EmitFaceSegments(GroupFlushState fg, StyledGroupState sg, FaceGroupState ef)
    {
        for (var si = 0; si < ef.emit.Count; si++)
        {
            var (segText, segX, segGlyphEnd) = ef.emit[si];
            // Interior segments pin the span box to the NEXT
            // segment's device anchor so every word lands at its PDF
            // position; the LAST segment pins to its width-only glyph
            // edge - the line-width budget - and the
            // sentinel &nbsp; dangles beyond it.
            // A segment born from REPOSITIONING (each word its own Tj)
            // carries no space glyph before the next word; the word
            // gap is still written as a real space
            // inside the span - its ws slot absorbs the pin residual -
            // so the extracted text keeps its word boundaries.
            if (si + 1 < ef.emit.Count && !char.IsWhiteSpace(segText[^1])
                && !char.IsWhiteSpace(ef.emit[si + 1].text[0]))
                segText += " ";
            var target = (si + 1 < ef.emit.Count ? ef.emit[si + 1].startX : segGlyphEnd) - segX;
            var natural = HtmlToPdfConverter.MeasureStlExactText(sg.face!, segText, sg.fs);
            var lsEm = Math.Round((target - natural) / (segText.Length * sg.fs), 4);
            var resid = target - natural - lsEm * segText.Length * sg.fs;
            var spaces = 0;
            foreach (var ch in segText) if (ch == ' ') spaces++;
            var wsEm = spaces > 0 ? Math.Round(resid / (spaces * sg.fs), 4) : 0;
            var lsNum = fg.styleReg!.LetterSpacing(lsEm);
            // A ten-thousandth-scale residue is letter-spacing
            // rounding noise, not a word gap worth
            // bridging — no inline style for it.
            // A bold/italic face carries its weight inline: the emitted
            // font class names the FAMILY only, so a viewer that falls
            // back to a system face would otherwise render the run regular.
            var weightCss = StlWeightStyleCss(fg.ct.groupFauxBold, fg.ct.groupDeclStyle);
            var wsCss = Math.Abs(wsEm) >= 0.001
                ? $"word-spacing:{wsEm.ToString("0.####", CultureInfo.InvariantCulture)}em;"
                : "";
            var wsAttr = weightCss.Length + wsCss.Length > 0
                ? $" style=\"{weightCss}{wsCss}\""
                : "";
            ef.lastLsNum = lsNum;
            // Each segment resolves its own target from its own extent
            // (see the solver): a row of per-word hotspots gives each
            // word its own href.
            var segLink = sg.popupItems is null && !fg.ct.groupIsType3
                ? FindLinkTarget(fg.linkTargets, segX, segGlyphEnd, fg.ct.groupY)
                : null;
            var segOpen = segLink is null
                ? null
                : $"<a href=\"{EscapeHtml(segLink.Uri)}\"" +
                    (segLink.Uri.StartsWith('#') ? ">" : " target=\"_blank\">");
            if (segOpen is not null)
            {
                fg.sb.Append(segOpen);
                segLink!.Wrapped = true;
            }
            fg.sb.Append($"<span class=\"{fg.classNamer.Attr(sg.fontNum, sg.lhNum, lsNum)}\"{wsAttr}>");
            var stlSeg = EscapeHtml(segText);
            if (fg.ct.groupRawRise > RiseThreshold) stlSeg = $"<sup>{stlSeg}</sup>";
            else if (fg.ct.groupRawRise < -RiseThreshold) stlSeg = $"<sub>{stlSeg}</sub>";
            fg.sb.Append(stlSeg);
            // The line-end sentinel is a SPACE then the nbsp, as in the
            // solved line path: the space belongs to the line, the nbsp
            // hangs past it and stays outside the width budget.
            if (si == ef.emit.Count - 1 && sg.popupItems is null) fg.sb.Append(" &nbsp;");
            fg.sb.Append("</span>");
            if (segOpen is not null) fg.sb.Append("</a>");
        }
    }

    /// <summary></summary>
    private static void MergeFaceGaps(GroupFlushState fg, StyledGroupState sg, FaceGroupState ef)
    {
        for (var si = 0; si + 1 < ef.emit.Count; si++)
        {
            var cur = ef.emit[si];
            var nxt = ef.emit[si + 1];
            if (cur.text.Length == 0 || nxt.text.Length == 0) continue;
            if (char.IsWhiteSpace(cur.text[^1]) || char.IsWhiteSpace(nxt.text[0])) continue;
            // Only ABUTTING segments are a split word: a device gap
            // approaching a space width at the boundary is a word
            // gap (separately positioned words carry no space
            // glyph), and those segments stay separate so the
            // emission below writes the gap as a real space.
            if (nxt.startX - cur.glyphEnd > 0.5 * ef.spaceAdv) continue;
            var cut = 0;
            while (cut < nxt.text.Length && !char.IsWhiteSpace(nxt.text[cut])) cut++;
            var headText = nxt.text[..cut];
            if (cut == nxt.text.Length)
            {
                // The whole next segment is the word's tail: absorb it
                // and re-examine the merged span's new right boundary.
                ef.emit[si] = (cur.text + headText, cur.startX, nxt.glyphEnd);
                ef.emit.RemoveAt(si + 1);
                si--;
            }
            else
            {
                ef.emit[si] = (cur.text + headText, cur.startX, cur.glyphEnd);
                ef.emit[si + 1] = (nxt.text[cut..],
                    nxt.startX + HtmlToPdfConverter.MeasureStlExactText(sg.face!, headText, sg.fs),
                    nxt.glyphEnd);
            }
        }
    }
}
