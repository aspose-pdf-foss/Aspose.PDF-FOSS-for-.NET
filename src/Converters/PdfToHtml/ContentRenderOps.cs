using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One content-stream token of the render, verbatim: the body of RenderContentToHtml's
    /// operator loop. Returns false where the loop ended; a continue became return true.</summary>
    private static bool RenderContentOp(ContentRenderState ct)
    {
        var token = ct.lexer.NextToken();
        if (token.Kind == TokenKind.Eof) return false;

        switch (token.Kind)
        {
            case TokenKind.Integer: ct.operands.Add(new PdfInteger(token.IntValue)); break;
            case TokenKind.Real: ct.operands.Add(new PdfReal(token.RealValue)); break;
            case TokenKind.LiteralString: ct.operands.Add(new PdfString(token.BytesValue!)); break;
            case TokenKind.HexString: ct.operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
            case TokenKind.Name: ct.operands.Add(new PdfName(token.StringValue!)); break;
            case TokenKind.ArrayStart:
                ct.operands.Add(ParseArray(ct.lexer));
                break;
            case TokenKind.Keyword:
            {
                var op = token.StringValue!;
                // Operator ordinal within this content stream. A path element's
                // id is the 0-based index of the operator that opened its
                // construction, so the emitted SVG identifies each path by
                // where it is authored rather than by a dense running count.
                var opIndex0 = ct.opCounter++;
                if (ct.pathState.Data.Length == 0 && ct.pathOpenIndex < 0
                    && op is "m" or "re" or "l" or "c" or "v" or "y")
                    ct.pathOpenIndex = opIndex0;
                // UseZOrder paint counter: each path paint op and image Do is
                // one atomic object (whatever the subpath count or clip
                // outcome); an ExtGState carrying a soft mask adds the mask
                // form's own object count at EVERY gs that loads it. Glyphs
                // count in ShowRun; forms count through their contents.
                if (ct.zCounter is not null)
                {
                    switch (op)
                    {
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            ct.zCounter.V++;
                            break;
                        case "BI":
                            ct.zCounter.V++;
                            break;
                        case "Do":
                            if (ct.operands.Count >= 1 && ct.operands[0] is PdfName zxn
                                && IsImageXObject(zxn.Value, ct.imageXObjects, ct.resources, ct.reader))
                                ct.zCounter.V++;
                            break;
                        case "gs":
                            if (ct.resources is not null && ct.operands.Count >= 1 && ct.operands[0] is PdfName zgs)
                            {
                                var egs = ct.reader.ResolveDict(
                                    ct.reader.ResolveDict(ct.resources.Get("ExtGState"))?.Get(zgs.Value));
                                var smDict = egs is not null ? ct.reader.ResolveDict(egs.Get("SMask")) : null;
                                var maskForm = smDict is not null ? ct.reader.ResolveStream(smDict.Get("G")) : null;
                                if (maskForm is not null)
                                    ct.zCounter.V += CountMaskPaintOps(maskForm, ct.reader, ct.zCounter.MaskMemo, depth: 0);
                            }
                            break;
                    }
                }
                if (RenderOperator(ct, op)) return true;
                ct.operands.Clear();
                break;
            }
            default:
                ct.operands.Clear();
                break;
        }
        return true;
    }

    /// <summary>Dispatches one content operator to its arm; true when the arm finished the token itself and the caller must return at once.</summary>
    private static bool RenderOperator(ContentRenderState ct, string op)
    {
        switch (op)
        {
            // ── Graphics state stack ──
            case "q":
                SaveStateOp(ct, op);
                break;
            case "Q":
                RestoreStateOp(ct, op);
                break;

            // ── ExtGState: a luminosity /SMask masks what follows ──
            // The mask group is rasterised to a grayscale sidecar
            // (shaped as the group's BBox at 200 dpi) and
            // applied as an SVG mask around the painted content until a
            // gs clears the soft mask or the q-scope pops.
            case "gs":
                ApplyGraphicsStateOp(ct, op);
                break;

            // ── CTM ──
            case "cm":
                ConcatMatrixOp(ct, op);
                break;

            // ── Marked content ──
            // Line grouping distinguishes shows WITHIN one structure
            // content item from shows across items (see sameLine), so
            // only /MCID-carrying BDC marks advance the sequence —
            // ActualText spans and artifacts are not line boundaries.
            case "BDC":
                BeginMarkedContentOp(ct, op);
                break;
            case "BMC":
                ct.mcStack.Push(false);
                ct.ocDepth.Push(0);
                break;
            case "EMC":
                EndMarkedContentOp(ct, op);
                break;

            // ── Text state ──
            case "BT":
                ct.tlm.Set(1, 0, 0, 1, 0, 0);
                ct.tm.Set(1, 0, 0, 1, 0, 0);
                ct.tx = 0; ct.ty = 0;
                break;
            case "ET":
                break;
            case "Tf":
                SetFontOp(ct, op);
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
                SetTextMatrixOp(ct, op);
                break;
            case "T*":
                ct.tlm.Concat(1, 0, 0, 1, 0, -(ct.hasLeading ? ct.leading : ct.fontSize * 1.2));
                ct.tm.CopyFrom(ct.tlm);
                ct.tx = ct.tm.E; ct.ty = ct.tm.F;
                break;
            case "Ts":
                if (ct.operands.Count >= 1)
                    ct.rise = Num(ct.operands[0]);
                break;
            case "Tc": // character spacing
                if (ct.operands.Count >= 1)
                    ct.charSpacing = Num(ct.operands[0]);
                break;
            case "Tw": // word spacing (single-byte code 32)
                SetWordSpacingOp(ct, op);
                break;

            // ── Color ──
            case "rg":
                SetFillRgbOp(ct, op);
                break;
            case "RG":
                SetStrokeRgbOp(ct, op);
                break;
            case "g":
                SetFillGrayOp(ct, op);
                break;
            case "G":
                SetStrokeGrayOp(ct, op);
                break;
            case "k":
                SetFillCmykOp(ct, op);
                break;
            case "K":
                SetStrokeCmykOp(ct, op);
                break;
            // Colour space selection: a Separation/DeviceN space carries a
            // tint transform for its scn operand; other spaces keep the
            // component-count mapping.
            case "cs":
                ct.fillTintMap = ct.operands.Count >= 1 && ct.operands[0] is PdfName fcs
                    ? TryBuildTintMap(ct.resources, fcs.Value, ct.reader) : null;
                break;
            case "CS":
                SetStrokeColorSpaceOp(ct, op);
                break;

            // Colour in the current colour space (sc/scn): numeric
            // components map like gray/rgb/cmyk by count; a trailing
            // pattern NAME operand leaves the colour untouched.
            case "sc" or "scn":
                SetFillColorOp(ct, op);
                break;
            case "SC" or "SCN":
                SetStrokeColorOp(ct, op);
                break;

            // ── Line width ──
            case "w":
                SetLineWidthOp(ct, op);
                break;

            // ── Text showing ──
            case "Tj":
                ShowTextOp(ct, op);
                break;
            case "TJ":
                ShowTextArrayOp(ct, op);
                break;
            case "'":
                NextLineShowTextOp(ct, op);
                break;

            // ── XObject (Do operator): images drawn, forms recursed ──
            case "Do":
                DrawXObjectOp(ct, op);
                break;

            // ── Path construction ──
            // Coordinates are user-space: the CTM maps them to the page
            // space the SVG's outer matrix expects (content drawn under a
            // scaling cm — e.g. q 0.12 0 0 0.12 0 0 cm — landed kilopoints
            // off-canvas when emitted raw).
            case "m": // moveto
                MoveToOp(ct, op);
                break;
            case "l": // lineto
                LineToOp(ct, op);
                break;
            case "c": // curveto
                CurveToOp(ct, op);
                break;
            case "v": // curveto (initial point replicated)
                CurveToInitialOp(ct, op);
                break;
            case "y": // curveto (final point replicated)
                CurveToFinalOp(ct, op);
                break;
            case "h": // closepath
                ct.pathState.Data.Append("Z");
                break;
            case "re": // rectangle
                RectangleOp(ct, op);
                break;

            // ── Path painting ──
            case "S": // stroke
                StrokeOp(ct, op);
                break;
            case "s": // close and stroke
                CloseStrokeOp(ct, op);
                break;
            case "f" or "F": // fill (nonzero)
                FillOp(ct, op);
                break;
            case "f*": // fill (even-odd)
                FillEvenOddOp(ct, op);
                break;
            case "B": // fill and stroke (nonzero)
                FillStrokeOp(ct, op);
                break;
            case "B*": // fill and stroke (even-odd)
                FillStrokeEvenOddOp(ct, op);
                break;
            case "b": // close, fill and stroke (nonzero)
                CloseFillStrokeOp(ct, op);
                break;
            case "b*": // close, fill and stroke (even-odd)
                CloseFillStrokeEvenOddOp(ct, op);
                break;
            case "W":
            case "W*":
                ClipOp(ct, op);
                break;
            case "n": // end path (no paint)
                EndPathOp(ct, op);
                break;
            case "sh":
                ShadingOp(ct, op);
                break;
            case "BI":
                SkipInlineImage(ct.lexer);
                ct.operands.Clear();
                return true;
        }
        return false;
    }
}
