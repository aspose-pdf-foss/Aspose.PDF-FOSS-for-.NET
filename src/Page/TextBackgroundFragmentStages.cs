using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Text background fragment stages: the form-background skip, the background band and the strike-out runs.</summary>
    private static void EmitStrikeOuts(BgFragmentState fb, Text.TextFragment frag)
    {
        while (fb.si < fb.segList.Count)
        {
            var seg = fb.segList[fb.si];
            var bg = seg.TextState.BackgroundColor!;
            var segPos = seg.Position!;
            var fs = seg.TextState.FontSize > 0 ? seg.TextState.FontSize : frag.TextState.FontSize;
            double startX = segPos.XIndent;
            double startY = segPos.YIndent;

            // Scan forward to merge consecutive segments from the same source run.
            // This groups segments by physical Tj/TJ operator, matching the the public API's
            // per-run background rectangles.
            int lastMerged = fb.si;
            while (lastMerged + 1 < fb.segList.Count)
            {
                var nextSeg = fb.segList[lastMerged + 1];
                if (nextSeg.SourceRunIndex == seg.SourceRunIndex)
                    lastMerged++;
                else
                    break;
            }

            // Width spans from first segment start to last merged segment end
            double w;
            if (lastMerged + 1 < fb.segList.Count && fb.segList[lastMerged + 1].Position is not null)
            {
                w = fb.segList[lastMerged + 1].Position!.XIndent - startX;
            }
            else
            {
                // Last merged group: check if it's also the last segment of the fragment.
                // If all segments have bg color, use frag.Rectangle.URX.
                // Otherwise, compute width from font metrics for the covered segments.
                bool isLastFragSeg = (fb.segList.Count == frag.Segments.Count);
                if (isLastFragSeg && frag.Rectangle is not null)
                {
                    // A segment moved after absorption carries its box with it:
                    // the fragment rectangle still describes the ORIGINAL span,
                    // so shift its right edge by the segment's own displacement.
                    var segDx = seg.Rectangle is { } segRect ? startX - segRect.LLX : 0;
                    w = frag.Rectangle.URX + segDx - startX;
                }
                else
                {
                    // Compute width from font metrics for segments si.lastMerged
                    w = 0;
                    for (int k = fb.si; k <= lastMerged; k++)
                    {
                        var s = fb.segList[k];
                        var font = s.TextState.Font ?? frag.TextState.Font;
                        if (font is not null)
                        {
                            try { w += font.MeasureString(s.Text, fs); }
                            catch { w += s.Text.Length * fs * 0.5; }
                        }
                        else
                            w += s.Text.Length * fs * 0.5;
                    }
                }
            }

            var rawFs = seg.TextState.RawFontSize > 0 ? (double)seg.TextState.RawFontSize : fs;
            var tmD = Math.Abs(seg.TextState.TmD) > 0.001 ? Math.Abs(seg.TextState.TmD) : 1.0;
            var fontName2 = seg.TextState.FontName ?? frag.TextState.FontName ?? "";
            var font2 = seg.TextState.Font ?? frag.TextState.Font;
            double h = ComputeBgRectHeight(fontName2, font2, rawFs, tmD);

            EmitBg(fb, bg, startX, startY, w, h, h);
            fb.si = lastMerged + 1;
        }
    }

    /// <summary></summary>
    private static bool TryEmitBackgroundBand(BgFragmentState fb, Text.TextFragment frag)
    {
        if (fb.fragBg is { IsEmpty: false } && frag.Segments.Count > 0)
        {
            // Rotation-aware path: when the text direction is not horizontal
            // (text drawn under a rotating CTM), emit the highlight as a
            // rectangle oriented along the baseline via a cm transform, so it
            // follows the rotated text instead of being an axis-aligned box.
            // Horizontal text (the default TextDirX=1, TextDirY=0) is unaffected.
            double dirX = frag.TextDirX, dirY = frag.TextDirY;
            double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (dirLen > 1e-6 && Math.Abs(dirY / dirLen) > 0.01)
            {
                double ux = dirX / dirLen, uy = dirY / dirLen;
                double ox = frag.PositionOrNull?.XIndent ?? frag.Rectangle?.LLX ?? 0;
                double oy = frag.PositionOrNull?.YIndent ?? frag.Rectangle?.LLY ?? 0;

                double rRawFs = 0, rTmD = 1.0;
                string rFontName = frag.TextState.FontName ?? "";
                Text.FontInfo? rFont = frag.TextState.Font;
                foreach (var seg in frag.Segments)
                {
                    var rfs = seg.TextState.RawFontSize > 0 ? (double)seg.TextState.RawFontSize : (double)seg.TextState.FontSize;
                    if (rfs > rRawFs)
                    {
                        rRawFs = rfs;
                        rTmD = Math.Abs(seg.TextState.TmD) > 0.001 ? Math.Abs(seg.TextState.TmD) : 1.0;
                        rFontName = seg.TextState.FontName ?? rFontName;
                        rFont = seg.TextState.Font ?? rFont;
                    }
                }
                if (rRawFs <= 0) rRawFs = frag.TextState.FontSize;
                double rFs = frag.TextState.FontSize > 0 ? frag.TextState.FontSize : rRawFs;
                double rotW = rFont?.MeasureString(frag.Text, rFs) ?? frag.Text.Length * rFs * 0.5;
                double rotH = ComputeBgRectHeight(rFontName, rFont, rRawFs, rTmD);
                double rotDescent = rotH * 0.21;

                fb.builder.SaveState();
                fb.builder.SetFillColor(fb.fragBg.R / 255.0, fb.fragBg.G / 255.0, fb.fragBg.B / 255.0);
                fb.builder.SetMatrix(ux, uy, -uy, ux, ox, oy);
                fb.builder.Rectangle(0, -rotDescent, rotW, rotH);
                fb.builder.Fill();
                fb.builder.RestoreState();
                return true;
            }

            double fragW, fragH, fragX, fragY;

            if (fb.hasCtm)
            {
                // CTM path: compute width/height from current FontSize in
                // Tm (content-stream) space, then inverse-CTM the position.
                double localFs = frag.TextState.FontSize / fb.ctmScaleX;
                Text.FontInfo? fragFont = frag.TextState.Font;
                foreach (var seg in frag.Segments)
                    if (seg.TextState.Font is not null) { fragFont = seg.TextState.Font; break; }
                fragW = fragFont?.MeasureString(frag.Text, localFs)
                    ?? (frag.Text.Length * localFs * 0.5);
                fragH = localFs * 1.1;
                fragX = frag.Rectangle?.LLX ?? frag.PositionOrNull?.XIndent ?? 0;
                fragY = frag.Rectangle?.LLY ?? frag.PositionOrNull?.YIndent ?? 0;
                (fragX, fragY) = fb.ctm!.InverseTransformPoint(fragX, fragY);
            }
            else
            {
                // Standard path: use the fragment rectangle for position/width,
                // compute height from rawFs/TmD metrics.
                fragW = (frag.Rectangle?.Width ?? 0) - frag.TrailingTcPageSpace;
                fragX = (frag.Rectangle?.LLX ?? frag.PositionOrNull?.XIndent ?? 0) + frag.PostAbsorbDx;
                fragY = (frag.Rectangle?.LLY ?? frag.PositionOrNull?.YIndent ?? 0) + frag.PostAbsorbDy;

                double maxRawFs = 0;
                double maxTmD = 1.0;
                string maxFontName = frag.TextState.FontName ?? "";
                Text.FontInfo? maxFont = frag.TextState.Font;
                foreach (var seg in frag.Segments)
                {
                    var rfs = seg.TextState.RawFontSize > 0 ? (double)seg.TextState.RawFontSize : (double)seg.TextState.FontSize;
                    if (rfs > maxRawFs)
                    {
                        maxRawFs = rfs;
                        maxTmD = Math.Abs(seg.TextState.TmD) > 0.001 ? Math.Abs(seg.TextState.TmD) : 1.0;
                        maxFontName = seg.TextState.FontName ?? maxFontName;
                        maxFont = seg.TextState.Font ?? maxFont;
                    }
                }
                if (maxRawFs <= 0) maxRawFs = frag.TextState.FontSize;

                fragH = ComputeBgRectHeight(maxFontName, maxFont, maxRawFs, maxTmD);
            }

            EmitBg(fb, fb.fragBg, fragX, fragY, fragW, fragH,
                frag.Rectangle?.Height ?? fragH);
            return true;
        }
        return false;
    }

    /// <summary></summary>
    private bool SkipsFormBackground(BgFragmentState fb, Text.TextFragment frag)
    {
        if (fb.fragBg is { IsEmpty: false } && frag.SourceXObjStream is null
            && (frag.CapturedUnderlineSources is { Count: > 0 }
                || frag.CapturedBackgroundSources is { Count: > 0 })
            && frag.PositionOrNull is { } inlinePos && frag.Rectangle is { } inlineRect)
        {
            var inlineFs = frag.TextState.RawFontSize > 0
                ? (double)frag.TextState.RawFontSize
                : (frag.TextState.FontSize > 0 ? frag.TextState.FontSize : 12);
            var inlineH = ComputeBgRectHeight(frag.TextState.FontName ?? "",
                frag.TextState.Font, inlineFs,
                Math.Abs(frag.TextState.TmD) > 0.001 ? Math.Abs(frag.TextState.TmD) : 1.0);
            if (InsertBeforeTextObjectAt(
                    DecorationBlock(fb.fragBg, inlinePos.XIndent, inlinePos.YIndent,
                        inlineRect.Width, inlineH),
                    inlinePos.XIndent, inlinePos.YIndent))
                return true;
        }
        return false;
    }
}
