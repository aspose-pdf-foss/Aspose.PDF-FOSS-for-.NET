using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    /// <summary>The graphics-state operators: the q/Q stack, the CTM, ExtGState and the stroke's width, caps, joins and dash.</summary>
    private bool RenderSvgStateOperator(SvgRenderState sv, ISet<string> usedBlendModes, string op)
    {
        switch (op)
        {
            // --- Graphics state stack ---
            case "q":
                sv.gsStack.Push(sv.gs.Clone());
                break;
            case "Q":
                if (sv.gsStack.Count > 0)
                    sv.gs = sv.gsStack.Pop();
                break;

            // --- CTM ---
            case "cm":
                if (sv.operands.Count >= 6)
                {
                    var m = new[]
                    {
                        Num(sv.operands[0]), Num(sv.operands[1]), Num(sv.operands[2]),
                        Num(sv.operands[3]), Num(sv.operands[4]), Num(sv.operands[5]),
                    };
                    sv.gs.Ctm = MulAffine(m, sv.gs.Ctm);
                }
                break;

            // --- ExtGState ---
            case "gs":
                if (sv.operands.Count >= 1 && sv.operands[0] is PdfName gsName)
                {
                    if (sv.extGStates.TryGetValue(gsName.Value, out var gsDict))
                    {
                        var caObj = gsDict.Get("ca");
                        if (caObj is PdfReal caR) sv.gs.FillAlpha = caR.Value;
                        else if (caObj is PdfInteger caI) sv.gs.FillAlpha = caI.Value;

                        var scaObj = gsDict.Get("CA");
                        if (scaObj is PdfReal scaR) sv.gs.StrokeAlpha = scaR.Value;
                        else if (scaObj is PdfInteger scaI) sv.gs.StrokeAlpha = scaI.Value;

                        var bmObj = gsDict.GetName("BM");
                        if (bmObj is not null)
                        {
                            sv.gs.BlendMode = bmObj;
                            if (bmObj != "Normal") usedBlendModes.Add(bmObj);
                        }
                    }
                }
                break;

            // --- Line width ---
            case "w":
                if (sv.operands.Count >= 1)
                    sv.gs.LineWidth = Num(sv.operands[0]);
                break;

            // --- Line cap ---
            case "J":
                if (sv.operands.Count >= 1)
                    sv.gs.LineCap = (int)Num(sv.operands[0]);
                break;

            // --- Line join ---
            case "j":
                if (sv.operands.Count >= 1)
                    sv.gs.LineJoin = (int)Num(sv.operands[0]);
                break;

            // --- Dash pattern ---
            case "d":
                if (sv.operands.Count >= 2 && sv.operands[0] is PdfArray dashArr)
                {
                    sv.gs.DashArray = new double[dashArr.Count];
                    for (int i = 0; i < dashArr.Count; i++)
                        sv.gs.DashArray[i] = Num(dashArr[i]);
                    sv.gs.DashPhase = Num(sv.operands[1]);
                }
                break;

        }
        return false;
    }

    /// <summary>The text operators: the font and its spacing, the text object and its matrix, and the strings shown.</summary>
    private bool RenderSvgTextOperator(SvgRenderState sv, StringBuilder sb, PdfReader reader, List<LinkRect>? links, string op)
    {
        switch (op)
        {
            case "Tf": case "Tc": case "Tw": case "Tz": case "Ts": case "Tr": case "TL":
                if (RenderSvgTextStateOperator(sv, reader, op)) return true;
                break;
            case "BT": case "Td": case "TD": case "Tm": case "T*": case "'": case "Tj": case "TJ":
                if (RenderSvgTextShowOperator(sv, sb, reader, links, op)) return true;
                break;
        }
        return false;
    }

    /// <summary>The colour operators: fill and stroke colour in every colour space the content sets.</summary>
    private bool RenderSvgColorOperator(SvgRenderState sv, PdfDictionary? resources, PdfReader reader, string op)
    {
        switch (op)
        {
            // --- Fill color (RGB) ---
            case "rg":
                if (sv.operands.Count >= 3)
                { sv.gs.FillR = Num(sv.operands[0]); sv.gs.FillG = Num(sv.operands[1]); sv.gs.FillB = Num(sv.operands[2]); }
                break;

            // --- Stroke color (RGB) ---
            case "RG":
                if (sv.operands.Count >= 3)
                { sv.gs.StrokeR = Num(sv.operands[0]); sv.gs.StrokeG = Num(sv.operands[1]); sv.gs.StrokeB = Num(sv.operands[2]); }
                break;

            // --- Grayscale fill ---
            case "g":
                if (sv.operands.Count >= 1)
                {
                    var gray = Num(sv.operands[0]);
                    sv.gs.FillR = gray; sv.gs.FillG = gray; sv.gs.FillB = gray;
                }
                break;

            // --- Grayscale stroke ---
            case "G":
                if (sv.operands.Count >= 1)
                {
                    var gray = Num(sv.operands[0]);
                    sv.gs.StrokeR = gray; sv.gs.StrokeG = gray; sv.gs.StrokeB = gray;
                }
                break;

            // --- CMYK fill ---
            case "k":
                if (sv.operands.Count >= 4)
                {
                    (sv.gs.FillR, sv.gs.FillG, sv.gs.FillB) = CmykToRgb(Num(sv.operands[0]), Num(sv.operands[1]), Num(sv.operands[2]), Num(sv.operands[3]));
                }
                break;

            // --- CMYK stroke ---
            case "K":
                if (sv.operands.Count >= 4)
                {
                    (sv.gs.StrokeR, sv.gs.StrokeG, sv.gs.StrokeB) = CmykToRgb(Num(sv.operands[0]), Num(sv.operands[1]), Num(sv.operands[2]), Num(sv.operands[3]));
                }
                break;

            // --- Colorspace-relative fill/stroke colour (sc/scn, SC/SCN) ---
            // The colorspace is set via cs/CS to a named space; rather than
            // resolve it, infer from the numeric operand count (1=gray,
            // 3=rgb, 4=cmyk). Without this, sc-coloured content (e.g. white
            // 1 1 1 sc interiors) all defaulted to black.
            case "cs":
            case "CS":
                if (sv.operands.Count >= 1 && sv.operands[0] is PdfName csn)
                {
                    var resolved = ResolveNamedColorSpace(csn.Value, resources, reader);
                    if (op == "cs") sv.gs.FillCs = resolved; else sv.gs.StrokeCs = resolved;
                }
                break;
            case "sc":
            case "scn":
                (sv.gs.FillR, sv.gs.FillG, sv.gs.FillB) =
                    sv.gs.FillCs is { TintTransform: not null } fcs && TintToRgb(fcs, sv.operands) is { } fillTint
                        ? fillTint
                        : SetColorFromComponents(sv.operands, sv.gs.FillR, sv.gs.FillG, sv.gs.FillB);
                break;
            case "SC":
            case "SCN":
                (sv.gs.StrokeR, sv.gs.StrokeG, sv.gs.StrokeB) =
                    sv.gs.StrokeCs is { TintTransform: not null } scs && TintToRgb(scs, sv.operands) is { } strokeTint
                        ? strokeTint
                        : SetColorFromComponents(sv.operands, sv.gs.StrokeR, sv.gs.StrokeG, sv.gs.StrokeB);
                break;

        }
        return false;
    }

    /// <summary>The path, painting, clipping and XObject operators.</summary>
    private bool RenderSvgPathOperator(SvgRenderState sv, StringBuilder sb, PdfDictionary? resources, PdfReader reader, int depth, ISet<string> usedBlendModes, List<LinkRect>? links, string op)
    {
        switch (op)
        {
            case "m": case "l": case "c": case "v": case "y": case "h": case "re":
                if (RenderSvgPathBuildOperator(sv, op)) return true;
                break;
            case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n":
                if (RenderSvgPaintOperator(sv, sb, op)) return true;
                break;
            // --- XObject invocation (Form recursion) ---
            case "Do":
                if (sv.operands.Count >= 1 && sv.operands[0] is PdfName xn)
                    RenderXObject(xn.Value, resources, reader, sb, depth, sv.gs, usedBlendModes, links);
                break;

        }
        return false;
    }
}
