using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Pdf to html content: the collected svg paths closed and emitted inline or handed to the sidecar.</summary>
    private static void EmitContentSvg(ContentRenderState ct)
    {
        if (ct.maskGroupOpen) ct.svgPaths.Append("</g>");
        if (ct.opacityGroupOpen) ct.svgPaths.Append("</g>");
        // path elements are handed to the caller (which writes them to a sidecar
        // <base>_files/img_NN.svg file); otherwise they are emitted inline.
        if (!ct.textOnly && ct.svgPaths.Length > 0)
        {
            if (ct.externalSvgPaths is not null)
            {
                ct.externalSvgPaths.Append(ct.svgPaths);
            }
            else
            {
                ct.sb.AppendLine($"<svg class=\"pdf-svg\" style=\"position:absolute;top:0;left:0;\" width=\"{F(ct.pageWidth)}pt\" height=\"{F(ct.pageHeight)}pt\" " +
                    $"viewBox=\"0 0 {F(ct.pageWidth)} {F(ct.pageHeight)}\" xmlns=\"http://www.w3.org/2000/svg\">");
                // Flip Y: PDF origin is bottom-left, SVG is top-left
                ct.sb.AppendLine($"<g transform=\"translate(0,{F(ct.pageHeight)}) scale(1,-1)\">");
                ct.sb.Append(ct.svgPaths);
                ct.sb.AppendLine("</g>");
                ct.sb.AppendLine("</svg>");
            }
        }
    }

    /// <summary>Pdf to html content: the render state's fonts, matrices, colours and line trackers initialised.</summary>
    private static void InitContentRenderState(ContentRenderState ct)
    {
        ct.lexer = new PdfLexer(ct.streamBytes);
        ct.operands = new List<PdfObject>();

        ct.charSpacing = 0;
        ct.wordSpacing = 0;
        ct.pendingTjNum = 0;
        ct.groupTjNum = 0;
        ct.groupChars = 0;

        ct.tx = 0;
        ct.ty = 0;
        ct.tlm = new CtmState();
        ct.tm = new CtmState();
        ct.leading = 0;
        ct.hasLeading = false;
        ct.fontSize = 12;
        ct.rise = 0;
        ct.fontIsType3 = false;
        ct.fontFamily = "sans-serif";
        ct.fontCssFamily = "sans-serif";
        ct.fontWeight = "normal";
        ct.textRenderMode = 0;
        // Modes 3 (invisible) and 7 (clip-only) paint no glyphs: this is the OCR text
        // layer a scanner lays under the page raster. It is not part of the visible
        // page, so it reaches the markup only when the save asks for it — and then it
        // is stated as what it is, a run in a fully transparent colour.
        ct.fontStyle = "normal";
        // A sheared text matrix (b = 0, significant c/d) is the faux-ITALIC idiom:
        // the upright face is slanted by the matrix rather than by a designed italic
        // (the classic shear is tan ≈ 1/3). A rotation carries b ≠ 0, so requiring a
        // zero b keeps rotated runs out; the threshold keeps rounding noise out.
        // The slant a faux-bold run DECLARES inline: the face's own italic when it
        // has one, else the matrix shear — the painted appearance either way.
        ct.fontAscent = 1.0;
        ct.fontLineHeight = 0.0;
        ct.r = 0;
        ct.g = 0;
        ct.b = 0;
        ct.currentFontKey = null;

        ct.ctm = ct.initialCtm?.Clone() ?? new CtmState();
        ct.ctmStack = new Stack<CtmState>();
        ct.colorStack = new Stack<(double r, double g, double b,
            double fr, double fg, double fb, double sr, double sg, double sb)>();
        ct.textSpacingStack = new Stack<(double cs, double ws)>();

        ct.pathState = new PathState();
        ct.svgPaths = new StringBuilder();
        ct.opCounter = 0;
        ct.pathOpenIndex = -1;
        ct.fillTintMap = null;
        ct.strokeTintMap = null;
        ct.cpx = 0;
        ct.cpy = 0;
        ct.clipD = null;
        ct.pendingClipD = null;
        ct.clipStack = new Stack<string?>();
        ct.shadingSeq = 0;
        ct.maskGroupOpen = false;
        ct.maskOpenClipDepth = 0;
        ct.opacityGroupOpen = false;
        ct.opacityOpenClipDepth = 0;
        ct.opacityGroupValue = 1.0;
        InitContentLineTrackers(ct);
    }

    /// <summary>Pdf to html content: the line, rule, path and park trackers initialised.</summary>
    private static void InitContentLineTrackers(ContentRenderState ct)
    {
        ct.rules = 
            ct.cssTextDecorations ? new() : null;
        ct.pathSegs = new List<(double X0, double Y0, double X1, double Y1)>();
        ct.curDevX = 0;
        ct.curDevY = 0;
        ct.pendingRects = new List<(double X0, double Y0, double X1, double Y1)>();
        ct.groupSegs = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>();
        ct.lineGlyphs = ct.styleReg is not null ? new List<StlLineGlyph>() : null;
        ct.lineStyles = ct.styleReg is not null ? new List<StlRunStyle>() : null;
        ct.lineOk = true;
        ct.lineStyleIdx = -1;
        ct.groupActive = false;
        ct.groupPinned = true;
        ct.groupEndX = 0;
        ct.groupPenX = 0;
        ct.groupTextPenX = 0;
        ct.groupX = 0;
        ct.groupY = 0;
        ct.groupFontSize = 12;
        ct.groupRise = 0;
        ct.groupAngle = 0;
        ct.groupRawRise = 0;
        ct.groupIsType3 = false;
        ct.groupFamily = "sans-serif";
        ct.groupCssFamily = "sans-serif";
        ct.groupWeight = "normal";
        ct.groupStyle = "normal";
        ct.groupFauxBold = false;
        ct.groupDeclStyle = "normal";
        ct.groupR = 0;
        ct.groupG = 0;
        ct.groupB = 0;
        ct.groupTransparent = false;
        ct.groupAscent = 1.0;
        ct.groupLineHeight = 0.0;
        ct.groupZ = 0;
        ct.mcSeq = 0;
        ct.groupMcSeq = 0;
        ct.mcStack = new Stack<bool>();
        ct.ocDepth = new Stack<int>();

        ct.groupLastShowText = "";

        ct.parkedLines = new List<StlLinePark>();
        ct.activePark = null;
    }
}
