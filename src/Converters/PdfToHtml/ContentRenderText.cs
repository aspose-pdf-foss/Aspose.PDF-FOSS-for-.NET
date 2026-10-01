using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
// The show-text run of the content render, lifted out of RenderContentToHtml; it takes the render state and the inputs it reads.
    private static void ShowRun(ContentRenderState ct, Dictionary<string, HtmlFontRecord> fonts, StringBuilder sb, double pageHeight, double pageWidth, bool saveTransparentTexts, bool emCompensation, bool textOnly, StyleRegistry? styleReg, ClassNamer classNamer, List<LinkTarget>? linkTargets, RotationRegistry? rotReg, double pageLLX, double yTopRef, ZCounter? zCounter, bool pageTurnedOver, string text, double advTextSpace = double.NaN, double extTextSpace = double.NaN,
        List<(double pen, double glyph)>? perChar = null, List<int>? perCode = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        // An invisible run is dropped whole when the save does not ask for it. The
        // caller advances the text matrix after this returns, so a visible run
        // later on the same line still seats where its own pen puts it.
        if (Invisible(ct) && !saveTransparentTexts)
        {
            ct.pendingTjNum = 0;
            return;
        }

        var sr = new ShowRunState();
        sr.dev = ct.tm.Times(ct.ctm);
        sr.scale = Math.Sqrt(sr.dev.C * sr.dev.C + sr.dev.D * sr.dev.D);
        if (sr.scale <= 0) sr.scale = 1;
        sr.effSize = ct.fontSize * sr.scale;
        sr.effRise = ct.rise * sr.scale;
        sr.posX = sr.dev.E;
        sr.posY = sr.dev.F;

        sr.cssAngle = -Math.Atan2(sr.dev.B, sr.dev.A) * (180.0 / Math.PI);
        if (Math.Abs(sr.cssAngle) < 0.05) sr.cssAngle = 0;

        sr.divGapPt = styleReg is not null ? 0.0875 * sr.effSize * sr.effSize : 1.0 * sr.effSize;
        sr.lineYTol = styleReg is not null && ct.mcSeq != ct.groupMcSeq
            ? 0.2
            : Math.Max(0.5, Math.Max(sr.effSize, ct.groupFontSize) * 0.3);
        if (!TryJoinRunToGroup(sr, ct, sb, pageHeight, pageWidth, emCompensation, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, text, advTextSpace)) return;

        sr.wsOnlyShow = styleReg is not null && string.IsNullOrWhiteSpace(text);
        sr.sameLine = ct.groupActive &&
            Math.Abs(sr.effRise - ct.groupRise) <= 0.01 &&
            Math.Abs(sr.cssAngle - ct.groupAngle) <= 0.1 &&
            Math.Abs(sr.posY - ct.groupY) <= sr.lineYTol &&
            (sr.wsOnlyShow || (styleReg is not null && ct.lineOk) ||
             (ct.fontFamily == ct.groupFamily && ct.fontCssFamily == ct.groupCssFamily &&
              ct.fontWeight == ct.groupWeight && ct.fontStyle == ct.groupStyle &&
              ct.r == ct.groupR && ct.g == ct.groupG && ct.b == ct.groupB &&
              Invisible(ct) == ct.groupTransparent));
        sr.backTolPt = emCompensation ? 1.5 : 0.5;
        if (textOnly && sr.sameLine && ct.groupSegs.Count > 0 && sr.posX < ct.groupPenX - sr.backTolPt)
            sr.sameLine = false;
        if (!sr.sameLine)
        {
            OpenNewLineGroup(sr, ct, sb, pageHeight, pageWidth, emCompensation, textOnly, styleReg, classNamer, linkTargets, rotReg, pageLLX, yTopRef, zCounter, pageTurnedOver, advTextSpace);
        }
        ct.groupMcSeq = ct.mcSeq;
        ct.groupPenX = Math.Max(ct.groupPenX, sr.posX);
        if (!sr.wsOnlyShow) ct.groupLastShowText = text;

        sr.aligned = perChar is not null && perChar.Count == text.Length;

        // Record the show's glyphs for the stl_ line solver. Any show the
        // solver cannot model (no aligned advances, rotation) drops the whole
        // line back to the legacy emission.
        if (ct.lineGlyphs is not null && ct.lineOk)
        {
            EmitLineGlyphs(sr, ct, fonts, emCompensation, text, perChar, perCode);
        }

        AppendRunToGroupSegment(sr, ct, textOnly, zCounter, text, advTextSpace, extTextSpace, perChar);
    }
}
