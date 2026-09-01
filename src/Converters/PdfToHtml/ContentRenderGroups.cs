using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
// The text-group and style helpers of the content render, lifted out of RenderContentToHtml; each takes the render state and the inputs it reads.
    private static bool FauxBold(ContentRenderState ct) => ct.textRenderMode is 2 or 6;

    private static bool Invisible(ContentRenderState ct) => ct.textRenderMode is 3 or 7;

    private static bool FauxItalic(ContentRenderState ct) => Math.Abs(ct.tm.B) < 1e-6 && Math.Abs(ct.tm.D) > 1e-9
        && Math.Abs(ct.tm.C / ct.tm.D) > 0.1;

    private static string DeclStyle(ContentRenderState ct) => ct.fontStyle != "normal" ? ct.fontStyle
        : FauxItalic(ct) ? "italic" : "normal";

    private static double DevScale(ContentRenderState ct) => Math.Sqrt(Math.Abs(ct.ctm.A * ct.ctm.D - ct.ctm.B * ct.ctm.C));

    private static void CollectRuleCandidates(ContentRenderState ct, bool stroked, bool filled)
    {
        if (ct.rules is null) return;
        if (stroked)
            foreach (var (x0, y0, x1, y1) in ct.pathSegs)
            {
                if (Math.Abs(y1 - y0) > 0.35 || Math.Abs(x1 - x0) < 0.5) continue;
                ct.rules.Add(((y0 + y1) / 2, Math.Min(x0, x1), Math.Max(x0, x1),
                    ct.pathState.LineWidth * DevScale(ct),
                    ct.pathState.StrokeR, ct.pathState.StrokeG, ct.pathState.StrokeB));
            }
        if (filled)
            foreach (var (x0, y0, x1, y1) in ct.pendingRects)
            {
                var h = Math.Abs(y1 - y0);
                var w = Math.Abs(x1 - x0);
                if (w < 0.5 || h >= w) continue;
                ct.rules.Add(((y0 + y1) / 2, Math.Min(x0, x1), Math.Max(x0, x1), h,
                    ct.pathState.FillR, ct.pathState.FillG, ct.pathState.FillB));
            }
        ct.pathSegs.Clear();
        ct.pendingRects.Clear();
    }

    private static StlLinePark? FindParkedLine(ContentRenderState ct, double y)
    {
        for (var i = ct.parkedLines.Count - 1; i >= 0; i--)
            if (!ct.parkedLines[i].Closed
                && Math.Abs(ct.parkedLines[i].Y - y) <= ParkBaselineTolPt) return ct.parkedLines[i];
        return null;
    }

    private static StlLinePark? ParkCurrentLine(ContentRenderState ct, StyleRegistry? styleReg)
    {
        if (!ct.groupActive) return null;
        var p = ct.activePark;
        if (p is null) { p = new StlLinePark(); ct.parkedLines.Add(p); }
        ct.activePark = null;
        p.Segs = ct.groupSegs; p.Glyphs = ct.lineGlyphs; p.Styles = ct.lineStyles;
        p.Ok = ct.lineOk; p.StyleIdx = ct.lineStyleIdx;
        p.Pinned = ct.groupPinned; p.EndX = ct.groupEndX; p.PenX = ct.groupPenX;
        p.TextPenX = ct.groupTextPenX;
        p.X = ct.groupX; p.Y = ct.groupY; p.FontSize = ct.groupFontSize; p.Rise = ct.groupRise;
        p.Angle = ct.groupAngle; p.RawRise = ct.groupRawRise; p.IsType3 = ct.groupIsType3;
        p.Family = ct.groupFamily; p.CssFamily = ct.groupCssFamily; p.Weight = ct.groupWeight;
        p.Style = ct.groupStyle; p.DeclStyle = ct.groupDeclStyle; p.FauxBold = ct.groupFauxBold;
        p.R = ct.groupR; p.G = ct.groupG; p.B = ct.groupB; p.Transparent = ct.groupTransparent;
        p.Ascent = ct.groupAscent; p.LineHeight = ct.groupLineHeight;
        p.Z = ct.groupZ; p.McSeq = ct.groupMcSeq; p.TjNum = ct.groupTjNum; p.Chars = ct.groupChars;
        p.LastShowText = ct.groupLastShowText;
        // The parked line owns its collections; the active slot takes fresh ones,
        // and EVERY piece of live line state resets exactly as FlushGroup's tail
        // resets it — a parked line's pen surviving into the next line suppressed
        // the column split page-wide (every fresh line compared its gaps against
        // the stale 469 pt pen of a line long left).
        ct.groupSegs = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>();
        ct.lineGlyphs = styleReg is not null ? new List<StlLineGlyph>() : null;
        ct.lineStyles = styleReg is not null ? new List<StlRunStyle>() : null;
        ct.lineOk = true;
        ct.lineStyleIdx = -1;
        ct.groupActive = false;
        ct.groupLastShowText = "";
        ct.groupTjNum = 0;
        ct.groupChars = 0;
        ct.groupPinned = true;
        ct.groupEndX = 0;
        ct.groupPenX = 0;
        ct.groupTextPenX = 0;
        return p;
    }

    private static void ResumeParkedLine(ContentRenderState ct, StlLinePark p)
    {
        ct.groupSegs = p.Segs; ct.lineGlyphs = p.Glyphs; ct.lineStyles = p.Styles;
        ct.lineOk = p.Ok; ct.lineStyleIdx = p.StyleIdx;
        ct.groupPinned = p.Pinned; ct.groupEndX = p.EndX; ct.groupPenX = p.PenX;
        ct.groupTextPenX = p.TextPenX;
        ct.groupX = p.X; ct.groupY = p.Y; ct.groupFontSize = p.FontSize; ct.groupRise = p.Rise;
        ct.groupAngle = p.Angle; ct.groupRawRise = p.RawRise; ct.groupIsType3 = p.IsType3;
        ct.groupFamily = p.Family; ct.groupCssFamily = p.CssFamily; ct.groupWeight = p.Weight;
        ct.groupStyle = p.Style; ct.groupDeclStyle = p.DeclStyle; ct.groupFauxBold = p.FauxBold;
        ct.groupR = p.R; ct.groupG = p.G; ct.groupB = p.B; ct.groupTransparent = p.Transparent;
        ct.groupAscent = p.Ascent; ct.groupLineHeight = p.LineHeight;
        ct.groupZ = p.Z; ct.groupMcSeq = p.McSeq; ct.groupTjNum = p.TjNum; ct.groupChars = p.Chars;
        ct.groupLastShowText = p.LastShowText;
        ct.groupActive = true;
        ct.activePark = p;
    }

    private static string JoinGroupSegments(ContentRenderState ct, bool textOnly)
    {
        var ordered = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>(ct.groupSegs);
        ordered.Sort((a, b) => a.X.CompareTo(b.X));
        var joined = new StringBuilder();
        foreach (var (_, seg, _, _) in ordered)
        {
            if (seg.Length == 0) continue;
            if (textOnly && joined.Length > 0
                && !char.IsWhiteSpace(joined[joined.Length - 1]) && !char.IsWhiteSpace(seg[0]))
                joined.Append(' ');
            joined.Append(seg);
        }
        if (ordered.Count > 0) ct.groupX = ordered[0].X;
        return joined.ToString();
    }

    private static bool TrySolveStlLine(ContentRenderState ct, StringBuilder sb, double pageHeight, double pageWidth, bool emCompensation, StyleRegistry? styleReg, ClassNamer classNamer, List<LinkTarget>? linkTargets, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver)
    {
        if (styleReg is null || !ct.lineOk || ct.lineGlyphs is not { Count: > 0 }) return false;
        if (ct.rules is not null) return false;   // css text-decoration path keeps legacy emission
        if (ct.groupAngle != 0) return false;
        if (Math.Abs(ct.groupRawRise) > RiseThreshold) return false;
        var yTop = double.IsNaN(yTopRef) ? pageHeight : yTopRef;
        var divCls = classNamer.Cls("01");
        var zStyle = zCounter is not null && ct.groupZ > 0 ? $"z-index:{ct.groupZ};" : "";
        // A Type3 face is a set of content-stream procedures, not a program a
        // browser can be handed, so text transcribed from it is a best-effort
        // fallback and must not claim the annotation's hyperlink — the link keeps
        // its own click surface instead.
        var link = ct.groupIsType3 ? null : FindLinkTarget(linkTargets, ct.groupX, ct.groupPenX, ct.groupY);
        var popup = link?.PopupItems;
        // Wrapped is what suppresses the overlay, so the solver sets it per SPAN,
        // as it opens each anchor — a line whose spans all fall outside a rect (or
        // that the solver emitted empty) leaves that link its click surface.
        var solved = EmitStlSolvedDiv(sb, ct.lineGlyphs, ct.lineStyles!, styleReg, classNamer,
            divCls, zStyle, pageLLX, yTop, ct.groupY,
            ct.groupIsType3 ? null : (x0, x1) => FindLinkTarget(linkTargets, x0, x1, ct.groupY),
            popup,
            pageTurnedOver ? pageWidth / 12.0 : 0, pageTurnedOver ? pageHeight / 12.0 : 0,
            emGrid: emCompensation);
        return solved;
    }

    private static void FlushGroup(ContentRenderState ct, StringBuilder sb, double pageHeight, double pageWidth, bool textOnly, StyleRegistry? styleReg, ClassNamer classNamer, List<LinkTarget>? linkTargets, RotationRegistry? rotReg, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver, bool emCompensation)
    {
        // A line closed for real gives up its park slot, so it cannot be
        // emitted a second time when the page's remaining lines are closed.
        if (ct.groupActive && ct.activePark is { } closing) ct.parkedLines.Remove(closing);
        ct.activePark = null;
        var groupText = new StringBuilder(JoinGroupSegments(ct, textOnly));
        if (ct.groupActive && groupText.Length > 0 && TrySolveStlLine(ct, sb, pageHeight, pageWidth, emCompensation, styleReg, classNamer, linkTargets, pageLLX, yTopRef, zCounter, pageTurnedOver))
        {
            // Solved and emitted by the stl_ line solver.
        }
        else if (ct.groupActive && groupText.Length > 0)
        {
            if (styleReg is not null && string.IsNullOrWhiteSpace(groupText.ToString()))
            {
                // Whitespace-only group (re-drawn word-gap space glyphs over an
                // already-shown line, a positioned space show between columns, or
                // a stray trailing space glyph past the text): dropped in BOTH
                // stl_ dialects — a lone space div
                // must not grow the re-imported page width, and its font class
                // must not burn a class number.
            }
            else if (styleReg is not null)
            {
                // stl_ shape: a positioned stl_01 div wrapping the group's text.
                // Both stl_ dialects emit one span per word-anchored SEGMENT,
                // each letter/word-spacing-pinned so the measured boxes reach
                // their device anchors (external SVG-text saves
                // pin exactly like the PNG-background overlay); a group whose
                // face cannot resolve keeps the single whole-line span with the
                // plain Tc/TJ letter-spacing and the face's natural metric flow.
                // Channel bytes TRUNCATE (0.994118 -> 253 #FD) when forming
                // the emitted class colors.
                var color = ct.groupTransparent
                    ? TransparentTextColor
                    : $"#{(int)(Math.Clamp(ct.groupR, 0, 1) * 255):X2}{(int)(Math.Clamp(ct.groupG, 0, 1) * 255):X2}{(int)(Math.Clamp(ct.groupB, 0, 1) * 255):X2}";
                var fontNum = styleReg.Font(ct.groupCssFamily, ct.groupFontSize / 12.0, color, null);
                // Line-height is the font's hhea (asc+|desc|)/upm when a program
                // is available (1.117188 for Arial), the
                // generic 1.2 fallback otherwise.
                var lhNum = styleReg.LineHeight(ct.groupLineHeight > 0 ? Math.Round(ct.groupLineHeight, 6) : 1.2);
                var fs = Math.Max(0.01, ct.groupFontSize);
                var textAll = groupText.ToString();
                var face = ct.groupPinned && ct.groupEndX > ct.groupX + 0.01
                    && !string.IsNullOrWhiteSpace(textAll)
                    ? HtmlToPdfConverter.ResolveStlFace(ct.groupFamily) : null;

                // Fixed-layout geometry: x is measured from the MediaBox left
                // edge, the page top reference is LLY + floor(height), and the
                // run's visual top sits ascent×size above the baseline (the
                // font's usWinAscent fraction, not a full em).
                var yTop = double.IsNaN(yTopRef) ? pageHeight : yTopRef;
                var left = (ct.groupX - pageLLX) / 12.0;
                var top = (yTop - ct.groupY - ct.groupAscent * ct.groupFontSize) / 12.0;
                if (pageTurnedOver) { left -= pageWidth / 12.0; top -= pageHeight / 12.0; }
                // A rotated run carries a document-wide rotation class next to
                // stl_01 (vendor-prefixed transform block in the stylesheet).
                var divCls = ct.groupAngle != 0
                    ? $"{classNamer.Cls("01")} {classNamer.Cls(styleReg.Rotation(Math.Round(ct.groupAngle, 2)))}"
                    : classNamer.Cls("01");
                var zStyle = zCounter is not null && ct.groupZ > 0 ? $"z-index:{ct.groupZ};" : "";
                sb.Append($"<div class=\"{divCls}\" style=\"left:{Em4T(left)}em;top:{Em4T(top)}em;{zStyle}\">");
                // A text run inside a link annotation's rect renders as an anchor
                // wrapping the span(s) (div > a > span), carrying the link with
                // the text itself rather than only as an invisible overlay.
                // Containment is judged by OVERLAP, not the group origin: a rect
                // is fitted to the link's visible text with a little padding, so
                // the NEXT run's leading space can start inside the rect's right
                // padding without being the link's text.
                var link = ct.groupIsType3
                    ? null : FindLinkTarget(linkTargets, ct.groupX, ct.groupPenX, ct.groupY);
                var popupItems = link?.PopupItems;
                var linkOpen = link is null || popupItems is not null
                    ? null
                    : $"<a href=\"{EscapeHtml(link.Uri)}\"" +
                        (link.Uri.StartsWith('#') ? ">" : " target=\"_blank\">");
                // The single-span path opens the anchor around its one span here;
                // the pinned multi-segment path wraps EACH span in its own anchor.
                // Wrapped is set only where an anchor is really written: it is what
                // suppresses the click-surface overlay.
                if (linkOpen is not null && face is null)
                {
                    sb.Append(linkOpen);
                    if (link is not null) link.Wrapped = true;
                }

                // A page-menu widget wraps the caption span in a relative
                // hover box; its drop-up list class allocates after the
                // caption's own classes.
                var popupBoxNum = 0;
                if (popupItems is not null)
                {
                    popupBoxNum = styleReg.PopupBox();
                    sb.Append($"<div class=\"{classNamer.Cls(popupBoxNum)}\">");
                }

                if (face is not null)
                {
                    // Overlay segments in visual order; re-drawn whitespace-only
                    // overlap segments are dropped.
                    var ordered = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>(ct.groupSegs);
                    ordered.Sort((a, b) => a.X.CompareTo(b.X));
                    var emit = new List<(string text, double startX, double glyphEnd)>();
                    double coveredTo = double.MinValue;
                    foreach (var seg in ordered)
                    {
                        var st = seg.Text.ToString();
                        if (st.Length == 0) continue;
                        if (string.IsNullOrWhiteSpace(st) && seg.X < coveredTo - 0.5) continue;
                        emit.Add((st, seg.X, seg.GlyphEnd));
                        coveredTo = Math.Max(coveredTo, seg.GlyphEnd);
                    }
                    // Re-cut segment boundaries at word boundaries: a span is
                    // never split inside a word, but a justified line's
                    // raw shows often break mid-word ("laborat" + "ory"). A
                    // segment's leading word fragment moves into the previous
                    // span, and the following span starts at the fragment's
                    // measured end past its old device anchor.
                    var spaceAdv = HtmlToPdfConverter.MeasureStlExactText(face, " ", fs);
                    for (var si = 0; si + 1 < emit.Count; si++)
                    {
                        var cur = emit[si];
                        var nxt = emit[si + 1];
                        if (cur.text.Length == 0 || nxt.text.Length == 0) continue;
                        if (char.IsWhiteSpace(cur.text[^1]) || char.IsWhiteSpace(nxt.text[0])) continue;
                        // Only ABUTTING segments are a split word: a device gap
                        // approaching a space width at the boundary is a word
                        // gap (separately positioned words carry no space
                        // glyph), and those segments stay separate so the
                        // emission below writes the gap as a real space.
                        if (nxt.startX - cur.glyphEnd > 0.5 * spaceAdv) continue;
                        var cut = 0;
                        while (cut < nxt.text.Length && !char.IsWhiteSpace(nxt.text[cut])) cut++;
                        var headText = nxt.text[..cut];
                        if (cut == nxt.text.Length)
                        {
                            // The whole next segment is the word's tail: absorb it
                            // and re-examine the merged span's new right boundary.
                            emit[si] = (cur.text + headText, cur.startX, nxt.glyphEnd);
                            emit.RemoveAt(si + 1);
                            si--;
                        }
                        else
                        {
                            emit[si] = (cur.text + headText, cur.startX, cur.glyphEnd);
                            emit[si + 1] = (nxt.text[cut..],
                                nxt.startX + HtmlToPdfConverter.MeasureStlExactText(face, headText, fs),
                                nxt.glyphEnd);
                        }
                    }
                    var lastLsNum = 0;
                    for (var si = 0; si < emit.Count; si++)
                    {
                        var (segText, segX, segGlyphEnd) = emit[si];
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
                        if (si + 1 < emit.Count && !char.IsWhiteSpace(segText[^1])
                            && !char.IsWhiteSpace(emit[si + 1].text[0]))
                            segText += " ";
                        var target = (si + 1 < emit.Count ? emit[si + 1].startX : segGlyphEnd) - segX;
                        var natural = HtmlToPdfConverter.MeasureStlExactText(face, segText, fs);
                        var lsEm = Math.Round((target - natural) / (segText.Length * fs), 4);
                        var resid = target - natural - lsEm * segText.Length * fs;
                        var spaces = 0;
                        foreach (var ch in segText) if (ch == ' ') spaces++;
                        var wsEm = spaces > 0 ? Math.Round(resid / (spaces * fs), 4) : 0;
                        var lsNum = styleReg.LetterSpacing(lsEm);
                        // A ten-thousandth-scale residue is letter-spacing
                        // rounding noise, not a word gap worth
                        // bridging — no inline style for it.
                        // A bold/italic face carries its weight inline: the emitted
                        // font class names the FAMILY only, so a viewer that falls
                        // back to a system face would otherwise render the run regular.
                        var weightCss = StlWeightStyleCss(ct.groupFauxBold, ct.groupDeclStyle);
                        var wsCss = Math.Abs(wsEm) >= 0.001
                            ? $"word-spacing:{wsEm.ToString("0.####", CultureInfo.InvariantCulture)}em;"
                            : "";
                        var wsAttr = weightCss.Length + wsCss.Length > 0
                            ? $" style=\"{weightCss}{wsCss}\""
                            : "";
                        lastLsNum = lsNum;
                        // Each segment resolves its own target from its own extent
                        // (see the solver): a row of per-word hotspots gives each
                        // word its own href.
                        var segLink = popupItems is null && !ct.groupIsType3
                            ? FindLinkTarget(linkTargets, segX, segGlyphEnd, ct.groupY)
                            : null;
                        var segOpen = segLink is null
                            ? null
                            : $"<a href=\"{EscapeHtml(segLink.Uri)}\"" +
                                (segLink.Uri.StartsWith('#') ? ">" : " target=\"_blank\">");
                        if (segOpen is not null)
                        {
                            sb.Append(segOpen);
                            segLink!.Wrapped = true;
                        }
                        sb.Append($"<span class=\"{classNamer.Attr(fontNum, lhNum, lsNum)}\"{wsAttr}>");
                        var stlSeg = EscapeHtml(segText);
                        if (ct.groupRawRise > RiseThreshold) stlSeg = $"<sup>{stlSeg}</sup>";
                        else if (ct.groupRawRise < -RiseThreshold) stlSeg = $"<sub>{stlSeg}</sub>";
                        sb.Append(stlSeg);
                        // The line-end sentinel is a SPACE then the nbsp, as in the
                        // solved line path: the space belongs to the line, the nbsp
                        // hangs past it and stays outside the width budget.
                        if (si == emit.Count - 1 && popupItems is null) sb.Append(" &nbsp;");
                        sb.Append("</span>");
                        if (segOpen is not null) sb.Append("</a>");
                    }
                    if (popupItems is not null)
                    {
                        var listNum = styleReg.PopupList(popupBoxNum);
                        sb.Append($"<div class=\"{classNamer.Cls(listNum)}\">");
                        foreach (var (label, href) in popupItems)
                            sb.Append($"<a href=\"{href}\" class=\"{classNamer.Cls(fontNum)} " +
                                $"{classNamer.Cls(lhNum)}  {classNamer.Cls(lastLsNum)}\">{EscapeHtml(label)}</a>");
                        sb.Append("</div></div>");
                    }
                    sb.Append("</div>\n");
                }
                else
                {
                    var lsEm = ct.charSpacing / Math.Max(0.01, ct.groupFontSize)
                        - ct.groupTjNum / (1000.0 * Math.Max(1, ct.groupChars));
                    var lsNum = styleReg.LetterSpacing(System.Math.Round(lsEm, 4));
                    // A same-colour hairline under (or through) the baseline that
                    // covers the run start becomes CSS text-decoration; a
                    // decorated span carries the inline style and drops the
                    // trailing &nbsp;.
                    var decoration = FindDecoration(ct.rules, ct.groupX, ct.groupY, ct.groupFontSize,
                        ct.groupR, ct.groupG, ct.groupB);
                    sb.Append($"<span class=\"{classNamer.Attr(fontNum, lhNum, lsNum)}\"");
                    var groupWeightCss = StlWeightStyleCss(ct.groupFauxBold, ct.groupDeclStyle);
                    if (decoration is not null)
                        sb.Append($" style=\"{groupWeightCss}text-decoration:{decoration};word-spacing:0em;\"");
                    else if (groupWeightCss.Length > 0)
                        sb.Append($" style=\"{groupWeightCss}\"");
                    sb.Append('>');
                    // A non-trivial text rise marks a superscript/subscript run:
                    // wrap it in <sup>/<sub> so the markup carries the semantics
                    // (the .stl_ sup/sub CSS rules already position them). The
                    // rise is baked into `top`, so the tags are purely semantic -
                    // see EmitSpan for the non-stl_ counterpart.
                    var stlInner = EscapeHtml(groupText.ToString());
                    if (ct.groupRawRise > RiseThreshold) stlInner = $"<sup>{stlInner}</sup>";
                    else if (ct.groupRawRise < -RiseThreshold) stlInner = $"<sub>{stlInner}</sub>";
                    sb.Append(stlInner)
                        .Append(decoration is not null || popupItems is not null ? "</span>" : " &nbsp;</span>");
                    if (popupItems is not null)
                    {
                        var listNum = styleReg.PopupList(popupBoxNum);
                        sb.Append($"<div class=\"{classNamer.Cls(listNum)}\">");
                        foreach (var (label, href) in popupItems)
                            sb.Append($"<a href=\"{href}\" class=\"{classNamer.Cls(fontNum)} " +
                                $"{classNamer.Cls(lhNum)}  {classNamer.Cls(lsNum)}\">{EscapeHtml(label)}</a>");
                        sb.Append("</div></div>");
                    }
                    sb.Append(linkOpen is not null ? "</a></div>\n" : "</div>\n");
                }
            }
            else
            {
                EmitSpan(sb, groupText.ToString(), ct.groupX, ct.groupY, ct.groupFontSize,
                    ct.groupFamily, ct.groupWeight, ct.groupStyle, ct.groupR, ct.groupG, ct.groupB, pageHeight,
                    ct.groupRise, transparentText: textOnly,
                    rotationClass: ct.groupAngle != 0 && rotReg is not null
                        ? rotReg.Class(ct.groupAngle) : null);
            }
        }
        ct.groupSegs.Clear();
        ct.groupActive = false;
        ct.groupTjNum = 0;
        ct.groupChars = 0;
        ct.groupPinned = true;
        ct.groupEndX = 0;
        ct.groupPenX = 0;
        ct.groupTextPenX = 0;
        ct.lineGlyphs?.Clear();
        ct.lineOk = true;
        ct.lineStyleIdx = -1;
        ct.groupLastShowText = "";
    }

    private static void FlushParkedLines(ContentRenderState ct, StyleRegistry? styleReg, StringBuilder sb, double pageHeight, double pageWidth, bool textOnly, ClassNamer classNamer, List<LinkTarget>? linkTargets, RotationRegistry? rotReg, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver, bool emCompensation)
    {
        if (ct.parkedLines.Count == 0) { FlushGroup(ct, sb, pageHeight, pageWidth, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, emCompensation); return; }
        ParkCurrentLine(ct, styleReg);
        var order = ct.parkedLines;
        ct.parkedLines = new List<StlLinePark>();
        foreach (var p in order) { ResumeParkedLine(ct, p); FlushGroup(ct, sb, pageHeight, pageWidth, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, emCompensation); }
    }
}
