using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    /// <summary>The text-state operators: the font, spacing, scale, rise, render mode and leading.</summary>
    private bool RenderSvgTextStateOperator(SvgRenderState sv, PdfReader reader, string op)
    {
        switch (op)
        {
            // --- Font ---
            case "Tf":
                if (sv.operands.Count >= 2)
                {
                    if (sv.operands[0] is PdfName fn)
                    {
                        if (sv.fonts.TryGetValue(fn.Value, out var fd))
                        {
                            sv.gs.FontDict = fd;
                            sv.gs.ToUnicode = Text.TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                            var baseFont = fd.GetName("BaseFont") ?? "sans-serif";
                            sv.gs.FontName = MapFontName(baseFont);
                            try { sv.gs.Metrics = Text.FontMetrics.FromFontDict(fd, reader); }
                            catch { sv.gs.Metrics = null; }
                        }
                        else
                        {
                            sv.gs.FontDict = null;
                            sv.gs.ToUnicode = null;
                            sv.gs.Metrics = null;
                            sv.gs.FontName = fn.Value;
                        }
                    }
                    sv.gs.FontSize = Num(sv.operands[1]);
                }
                break;

            // --- Text state ---
            case "Tc":
                if (sv.operands.Count >= 1) sv.gs.CharSpacing = Num(sv.operands[0]);
                break;
            case "Tw":
                if (sv.operands.Count >= 1) sv.gs.WordSpacing = Num(sv.operands[0]);
                break;
            case "Tz":
                if (sv.operands.Count >= 1) sv.gs.HorizScale = Num(sv.operands[0]) / 100.0;
                break;
            case "Ts":
                if (sv.operands.Count >= 1) sv.gs.TextRise = Num(sv.operands[0]);
                break;
            case "Tr":
                if (sv.operands.Count >= 1) sv.gs.RenderMode = (int)Num(sv.operands[0]);
                break;

            case "TL":
                if (sv.operands.Count >= 1)
                    sv.gs.TextLeading = Num(sv.operands[0]);
                break;
        }
        return false;
    }

    /// <summary>The text object, its positioning and the strings it shows.</summary>
    private bool RenderSvgTextShowOperator(SvgRenderState sv, StringBuilder sb, PdfReader reader, List<LinkRect>? links, string op)
    {
        switch (op)
        {
            // --- Text object begin: reset text + line matrices ---
            case "BT":
                Array.Copy(Identity, sv.tm, 6);
                Array.Copy(Identity, sv.tlm, 6);
                break;

            // --- Text positioning (operate on the text line matrix) ---
            case "Td":
                if (sv.operands.Count >= 2)
                {
                    sv.tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(sv.operands[0]), Num(sv.operands[1]) }, sv.tlm);
                    Array.Copy(sv.tlm, sv.tm, 6);
                }
                break;
            case "TD":
                if (sv.operands.Count >= 2)
                {
                    sv.gs.TextLeading = -Num(sv.operands[1]);
                    sv.tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(sv.operands[0]), Num(sv.operands[1]) }, sv.tlm);
                    Array.Copy(sv.tlm, sv.tm, 6);
                }
                break;
            case "Tm":
                if (sv.operands.Count >= 6)
                {
                    var m = new[] { Num(sv.operands[0]), Num(sv.operands[1]), Num(sv.operands[2]),
                        Num(sv.operands[3]), Num(sv.operands[4]), Num(sv.operands[5]) };
                    Array.Copy(m, sv.tm, 6); Array.Copy(m, sv.tlm, 6);
                }
                break;
            case "T*":
                sv.tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -sv.gs.TextLeading }, sv.tlm);
                Array.Copy(sv.tlm, sv.tm, 6);
                break;

            // --- Text show: ' (move to next line then show) ---
            case "'":
                sv.tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -sv.gs.TextLeading }, sv.tlm);
                Array.Copy(sv.tlm, sv.tm, 6);
                if (sv.operands.Count >= 1 && sv.operands[0] is PdfString qs)
                    ShowText(sb, sv.gs, sv.tm, new PdfObject[] { qs }, reader, links);
                break;

            // --- Text show ---
            case "Tj":
                if (sv.operands.Count >= 1 && sv.operands[0] is PdfString s)
                    ShowText(sb, sv.gs, sv.tm, new PdfObject[] { s }, reader, links);
                break;
            case "TJ":
                if (sv.operands.Count >= 1 && sv.operands[0] is PdfArray tja)
                    ShowText(sb, sv.gs, sv.tm, tja.ToArray(), reader, links);
                break;

        }
        return false;
    }
}
