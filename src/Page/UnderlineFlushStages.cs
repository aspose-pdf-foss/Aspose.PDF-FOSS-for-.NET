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
    /// <summary>Flushes one underline fragment: its baseline and thickness from the font, the run's extent clipped to the fragment, and the rule rectangle written to the page content.</summary>
    private bool FlushUnderlineFragment(UnderlineFlushState ul, Text.TextFragment frag)
    {
        var fragPos = frag.PositionOrNull;
        if (fragPos is null) return true;
        ul.fragPos = fragPos;
        ul.fs = frag.TextState.FontSize;
        if (ul.fs <= 0) ul.fs = 12;

        if (frag.Rectangle is not null)
        {
            ul.w = frag.Rectangle.Width;
        }
        else
        {
            var font = frag.TextState.Font;
            if (font is not null)
            {
                try { ul.w = font.MeasureString(frag.Text, ul.fs); }
                catch { ul.w = frag.Text.Length * ul.fs * 0.5; }
            }
            else
            {
                ul.w = frag.Text.Length * ul.fs * 0.5;
            }
        }

        ul.ulThick = ul.fs * 0.05;
        ul.ulDescent = 0;
        ul.ulMetrics = frag.TextState.Font?.GetMetrics();
        if (ul.ulMetrics is not null && ul.ulMetrics.Descent != 0)
            ul.ulDescent = Math.Abs(ul.ulMetrics.Descent) / 1000.0;
        ul.ulOffset = ul.ulDescent > 0 ? (0.05 + ul.ulDescent / 10) * ul.fs : ul.fs * 0.07691;

        // A fragment whose SOURCE underline was captured (ToAttemptGetUnderlineFromSource,
        // then text-replaced): the source rule was spliced out, and the run it covered
        // is redrawn here — the REPLACEMENT at its own advance, then the run's TAIL
        // (the source rule normally spans more than the matched phrase: "Test bold
        // text 26" under one line) re-seated where the shorter/longer replacement
        // leaves it. Both pieces sit in the measured underline band:
        // bottom = the fragment's Position.YIndent + a tenth of the font's descent,
        // thickness = 5% of the font size — the same 0.05·fs the plain path uses.
        if (FlushCapturedUnderlineSources(ul, frag)) return true;

        ul.ulDirX = frag.TextDirX;
        ul.ulDirY = frag.TextDirY;
        ul.ulDirLen = Math.Sqrt(ul.ulDirX * ul.ulDirX + ul.ulDirY * ul.ulDirY);
        if (ul.ulDirLen > 1e-6 && Math.Abs(ul.ulDirY / ul.ulDirLen) > 0.01)
        {
            double ux = ul.ulDirX / ul.ulDirLen, uy = ul.ulDirY / ul.ulDirLen;
            double rw;
            try { rw = frag.TextState.Font?.MeasureString(frag.Text, ul.fs) ?? frag.Text.Length * ul.fs * 0.5; }
            catch { rw = frag.Text.Length * ul.fs * 0.5; }
            var fgr = frag.TextState.ForegroundColor;
            ul.builder.SaveState();
            ul.builder.SetFillColor(fgr?.R / 255.0 ?? 0, fgr?.G / 255.0 ?? 0, fgr?.B / 255.0 ?? 0);
            ul.builder.SetMatrix(ux, uy, -uy, ux, ul.fragPos.XIndent, ul.fragPos.YIndent);
            ul.builder.Rectangle(0, -ul.ulOffset, rw, ul.ulThick);
            ul.builder.Fill();
            ul.builder.RestoreState();
            return true;
        }

        ul.ctm = frag.ExtractionCtm;
        ul.yFlipped = ul.ctm is not null && ul.ctm.D < 0;
        ul.underlineY = ul.yFlipped
            ? ul.fragPos.YIndent + ul.ulOffset
            : ul.fragPos.YIndent - ul.ulOffset;
        ul.underlineH = ul.ulThick;

        ul.rectX = ul.fragPos.XIndent;
        ul.rectY = ul.underlineY;
        if (ul.ctm is not null)
        {
            (ul.rectX, ul.rectY) = ul.ctm.InverseTransformPoint(ul.fragPos.XIndent, ul.underlineY);
            var (wx, wy) = ul.ctm.InverseTransformPoint(ul.fragPos.XIndent + ul.w, ul.underlineY + ul.underlineH);
            ul.w = Math.Abs(wx - ul.rectX);
            ul.underlineH = Math.Abs(wy - ul.rectY);
        }

        ul.fg = frag.TextState.ForegroundColor;
        ul.r = ul.fg?.R / 255.0 ?? 0;
        ul.g = ul.fg?.G / 255.0 ?? 0;
        ul.b = ul.fg?.B / 255.0 ?? 0;

        ul.builder.SaveState();
        ul.builder.SetFillColor(ul.r, ul.g, ul.b);
        ul.builder.Rectangle(ul.rectX, ul.rectY, ul.w, ul.underlineH);
        ul.builder.Fill();
        ul.builder.RestoreState();
        return true;
    }
}
