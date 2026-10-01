using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Text fragment build: one raw run turned into a fragment.</summary>
    private bool BuildFragmentFromRun(BuildFragmentsState bf, RawTextRun run, bool[] laterInk)
    {
        bf.runIndex++;
        if (run.Text == "\r\n") return true;
        bf.occludedByLaterText = laterInk[bf.runIndex];
        bf.upX_ = run.TmC * run.Ctm.A + run.TmD * run.Ctm.C;
        bf.upY_ = run.TmC * run.Ctm.B + run.TmD * run.Ctm.D;
        bf.tmScale = Math.Sqrt(bf.upX_ * bf.upX_ + bf.upY_ * bf.upY_);
        bf.effectiveFontSize = bf.tmScale > 0.001 && Math.Abs(bf.tmScale - 1.0) > 0.001
            ? run.FontSize * bf.tmScale
            : run.FontSize;
        bf.textState = new TextState
        {
            FontSize = (float)bf.effectiveFontSize,
            FontName = run.FontName,
            RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode,
            LineWidth = run.LineWidth,
            IsBold = run.IsBold,
            IsItalic = run.IsItalic,
            Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica,
            TextRise = run.TextRise,
            IsSuperscript = run.TextRise > 0,
            IsSubscript = run.TextRise < 0,
        };
        bf.textState.SetCapturedForegroundColor(ForegroundColorOf(run));
        bf.textState.StrokingColor = run.StrokingColor;
        bf.width = (run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize)) * run.HScaling;
        // The box ends at the last glyph's advance: the character spacing that
        // follows it - and the word spacing too when that glyph is a space - is
        // pen movement, not text. The expected box for "CHANCEN ERGREIFEN!"
        // at Tc -0.02 is 0.32 pt WIDER than the pen advance, and a justified
        // line's trailing space contributes no Tw to its box.
        if (run.Width > 0 && run.Text.Length > 0)
            bf.width -= (run.CharSpacing + (run.Text[^1] == ' ' ? run.WordSpacing : 0)) * run.HScaling;

        // The fragment box is the same canonical line box the phrase search
        // reports: bottom at baseline + descent, 1.1 x FontSize tall (the
        // reference returns that box for every font on every page probed -
        // embedded CFF, non-embedded TrueType, CJK, rotated text alike); the
        // font's own ascent only wins for an EXTREME metric box.
        (bf.descentOffset, bf.ascentHeight) = ComputeDescentAscent(run, coreFaceDescent: false);

        bf.rectStartX = run.X + run.TmC * bf.descentOffset;
        bf.rectStartY = run.Y + run.TmD * bf.descentOffset;
        var (rx1, ry1) = ApplyCtm(bf.rectStartX, bf.rectStartY, run.Ctm);
        bf.endX = run.X + run.TmA * bf.width + run.TmC * bf.ascentHeight;
        bf.endY = run.Y + run.TmB * bf.width + run.TmD * bf.ascentHeight;
        var (rx2, ry2) = ApplyCtm(bf.endX, bf.endY, run.Ctm);

        bf.llx = Math.Min(rx1, rx2);
        bf.lly = Math.Min(ry1, ry2);
        bf.urx = Math.Max(rx1, rx2);
        bf.ury = Math.Max(ry1, ry2);

        bf.rect = new Rectangle(bf.llx, bf.lly, bf.urx, bf.ury);
        bf.px = rx1;
        bf.py = ry1;

        if (RunOutsideSearchRect(bf, run)) return true;

        if (ClipRunToSearchRect(bf, run)) return true;

        CaptureRunDecorations(bf, run);

        ApplyRunClipVisibility(bf, run);
        ApplyRunCoverOcclusion(bf);

        EmitRunFragment(bf, run);
        return true;
    }
}
