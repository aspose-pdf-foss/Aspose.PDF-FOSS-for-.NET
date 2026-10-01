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
        var fg = new GroupFlushState();
        fg.ct = ct;
        fg.sb = sb;
        fg.pageHeight = pageHeight;
        fg.pageWidth = pageWidth;
        fg.textOnly = textOnly;
        fg.styleReg = styleReg;
        fg.classNamer = classNamer;
        fg.linkTargets = linkTargets;
        fg.rotReg = rotReg;
        fg.pageLLX = pageLLX;
        fg.yTopRef = yTopRef;
        fg.zCounter = zCounter;
        fg.pageTurnedOver = pageTurnedOver;
        fg.emCompensation = emCompensation;
        // A line closed for real gives up its park slot, so it cannot be
        // emitted a second time when the page's remaining lines are closed.
        if (fg.ct.groupActive && fg.ct.activePark is { } closing) fg.ct.parkedLines.Remove(closing);
        fg.ct.activePark = null;
        fg.groupText = new StringBuilder(JoinGroupSegments(fg.ct, fg.textOnly));
        if (fg.ct.groupActive && fg.groupText.Length > 0 && TrySolveStlLine(fg.ct, fg.sb, fg.pageHeight, fg.pageWidth, fg.emCompensation, fg.styleReg, fg.classNamer, fg.linkTargets, fg.pageLLX, fg.yTopRef, fg.zCounter, fg.pageTurnedOver))
        {
            // Solved and emitted by the stl_ line solver.
        }
        else if (fg.ct.groupActive && fg.groupText.Length > 0)
        {
            FlushTextGroup(fg);
        }
        fg.ct.groupSegs.Clear();
        fg.ct.groupActive = false;
        fg.ct.groupTjNum = 0;
        fg.ct.groupChars = 0;
        fg.ct.groupPinned = true;
        fg.ct.groupEndX = 0;
        fg.ct.groupPenX = 0;
        fg.ct.groupTextPenX = 0;
        fg.ct.lineGlyphs?.Clear();
        fg.ct.lineOk = true;
        fg.ct.lineStyleIdx = -1;
        fg.ct.groupLastShowText = "";
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
