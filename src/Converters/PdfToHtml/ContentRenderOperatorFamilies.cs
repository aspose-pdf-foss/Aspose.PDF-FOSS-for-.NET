using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The graphics-state operators: the q/Q stack, ExtGState, the CTM, marked content and the line width.</summary>
    private static bool? RenderStateOperator(ContentRenderState ct, string op)
    {
        switch (op)
        {
            // ── Graphics state stack ──
            case "q":
                SaveStateOp(ct);
                break;
            case "Q":
                RestoreStateOp(ct);
                break;

            // ── ExtGState: a luminosity /SMask masks what follows ──
            // The mask group is rasterised to a grayscale sidecar
            // (shaped as the group's BBox at 200 dpi) and
            // applied as an SVG mask around the painted content until a
            // gs clears the soft mask or the q-scope pops.
            case "gs":
                ApplyGraphicsStateOp(ct);
                break;

            // ── CTM ──
            case "cm":
                ConcatMatrixOp(ct);
                break;

            // ── Marked content ──
            // Line grouping distinguishes shows WITHIN one structure
            // content item from shows across items (see sameLine), so
            // only /MCID-carrying BDC marks advance the sequence —
            // ActualText spans and artifacts are not line boundaries.
            case "BDC":
                BeginMarkedContentOp(ct);
                break;
            case "BMC":
                ct.mcStack.Push(false);
                ct.ocDepth.Push(0);
                break;
            case "EMC":
                EndMarkedContentOp(ct);
                break;

            // ── Line width ──
            case "w":
                SetLineWidthOp(ct);
                break;

        }
        return null;
    }

    /// <summary>The scale a Tz operand is written in: percent.</summary>
    private const double PercentScale = 100.0;

    /// <summary>The text operators: the text object, its font, its positioning and the strings it shows.</summary>
    private static bool? RenderTextOperator(ContentRenderState ct, string op)
    {
        switch (op)
        {
            // ── Text state ──
            case "BT":
                ct.tlm.Set(1, 0, 0, 1, 0, 0);
                ct.tm.Set(1, 0, 0, 1, 0, 0);

                break;
            case "ET":
                break;
            case "Tf":
                SetFontOp(ct);
                break;
            case "Tr":
                if (ct.operands.Count >= 1) ct.textRenderMode = (int)Num(ct.operands[0]);
                break;
            case "TL":
                if (ct.operands.Count >= 1)
                { ct.leading = Num(ct.operands[0]); ct.hasLeading = true; }
                break;
            case "Td" or "TD":
                MoveTextLineOp(ct, op);
                break;
            case "Tm":
                SetTextMatrixOp(ct);
                break;
            case "T*":
                ct.tlm.Concat(1, 0, 0, 1, 0, -(ct.hasLeading ? ct.leading : ct.fontSize * 1.2));
                ct.tm.CopyFrom(ct.tlm);

                break;
            case "Ts":
                if (ct.operands.Count >= 1)
                    ct.rise = Num(ct.operands[0]);
                break;
            case "Tc": // character spacing
                if (ct.operands.Count >= 1)
                    ct.charSpacing = Num(ct.operands[0]);
                break;
            case "Tz": // horizontal scaling, in percent
                if (ct.operands.Count >= 1)
                    ct.horizScale = Num(ct.operands[0]) / PercentScale;
                break;
            case "Tw": // word spacing (single-byte code 32)
                SetWordSpacingOp(ct);
                break;

            // ── Text showing ──
            case "Tj":
                ShowTextOp(ct);
                break;
            case "TJ":
                ShowTextArrayOp(ct);
                break;
            case "'":
                NextLineShowTextOp(ct);
                break;

        }
        return null;
    }

    /// <summary>The colour operators: fill and stroke colour in every colour space the content sets.</summary>
    private static bool? RenderColorOperator(ContentRenderState ct, string op)
    {
        switch (op)
        {
            // ── Color ──
            case "rg":
                SetFillRgbOp(ct);
                break;
            case "RG":
                SetStrokeRgbOp(ct);
                break;
            case "g":
                SetFillGrayOp(ct);
                break;
            case "G":
                SetStrokeGrayOp(ct);
                break;
            case "k":
                SetFillCmykOp(ct);
                break;
            case "K":
                SetStrokeCmykOp(ct);
                break;
            // Colour space selection: a Separation/DeviceN space carries a
            // tint transform for its scn operand; other spaces keep the
            // component-count mapping.
            case "cs":
                ct.fillTintMap = ct.operands.Count >= 1 && ct.operands[0] is PdfName fcs
                    ? TryBuildTintMap(ct.resources, fcs.Value, ct.reader) : null;
                break;
            case "CS":
                SetStrokeColorSpaceOp(ct);
                break;

            // Colour in the current colour space (sc/scn): numeric
            // components map like gray/rgb/cmyk by count; a trailing
            // pattern NAME operand leaves the colour untouched.
            case "sc" or "scn":
                SetFillColorOp(ct);
                break;
            case "SC" or "SCN":
                SetStrokeColorOp(ct);
                break;

        }
        return null;
    }

    /// <summary>The path, painting, clipping, shading, XObject and inline-image operators.</summary>
    private static bool? RenderPathOperator(ContentRenderState ct, string op)
    {
        switch (op)
        {
            // ── XObject (Do operator): images drawn, forms recursed ──
            case "Do":
                DrawXObjectOp(ct);
                break;

            // ── Path construction ──
            // Coordinates are user-space: the CTM maps them to the page
            // space the SVG's outer matrix expects (content drawn under a
            // scaling cm — e.g. q 0.12 0 0 0.12 0 0 cm — landed kilopoints
            // off-canvas when emitted raw).
            case "m": // moveto
                MoveToOp(ct);
                break;
            case "l": // lineto
                LineToOp(ct);
                break;
            case "c": // curveto
                CurveToOp(ct);
                break;
            case "v": // curveto (initial point replicated)
                CurveToInitialOp(ct);
                break;
            case "y": // curveto (final point replicated)
                CurveToFinalOp(ct);
                break;
            case "h": // closepath
                ct.pathState.Data.Append("Z");
                break;
            case "re": // rectangle
                RectangleOp(ct);
                break;

            // ── Path painting ──
            case "S": // stroke
                StrokeOp(ct);
                break;
            case "s": // close and stroke
                CloseStrokeOp(ct);
                break;
            case "f" or "F": // fill (nonzero)
                FillOp(ct);
                break;
            case "f*": // fill (even-odd)
                FillEvenOddOp(ct);
                break;
            case "B": // fill and stroke (nonzero)
                FillStrokeOp(ct);
                break;
            case "B*": // fill and stroke (even-odd)
                FillStrokeEvenOddOp(ct);
                break;
            case "b": // close, fill and stroke (nonzero)
                CloseFillStrokeOp(ct);
                break;
            case "b*": // close, fill and stroke (even-odd)
                CloseFillStrokeEvenOddOp(ct);
                break;
            case "W":
            case "W*":
                ClipOp(ct, evenOdd: op == "W*");
                break;
            case "n": // end path (no paint)
                EndPathOp(ct);
                break;
            case "sh":
                ShadingOp(ct);
                break;
            case "BI":
                SkipInlineImage(ct.lexer);
                ct.operands.Clear();
                return true;
        }
        return null;
    }
}
