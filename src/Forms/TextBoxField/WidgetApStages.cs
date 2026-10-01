using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class TextBoxField
{
    /// <summary>The stages of the widget appearance dictionary: the unicode font resolve and the plain (non-comb) content.</summary>
    private void BuildPlainWidgetContent(WidgetApState wa)
    {
        var shown = wa.text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        // Widget quadding from /Q (widget kid first, else the field).
        double tx = 2;
        int q = (int)((wa.widgetDict?.Get("Q") is PdfInteger wq ? wq.Value : Dict.GetInt("Q")));
        if (q is 1 or 2 && shown.Length > 0)
        {
            double em = 0;
            foreach (char c in shown) em += GetGlyphWidthEm(c, wa.fontName);
            var tw = em * wa.fontSize;
            tx = q == 2 ? System.Math.Max(2, wa.w - tw - 2) : System.Math.Max(2, (wa.w - tw) / 2);
        }
        string showOp;
        if (wa.wuFonts is not null)
        {
            var (_, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                wa.wuFonts, wa.wuTtf!, wa.wuFam, Aspose.Pdf.Text.BidiText.ToVisualOrder(shown));
            var sb = new System.Text.StringBuilder(hex.Length * 2 + 2);
            sb.Append('<');
            foreach (var b in hex) sb.Append(b.ToString("X2"));
            sb.Append('>');
            showOp = sb.ToString();
        }
        else
        {
            showOp = "(" + shown.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
        }
        var wY = (wa.h - ApLineHeight(wa.fontName, wa.fontSize)) / 2 + wa.fontSize * ApDescent(wa.fontName) / 1000.0;
        wa.content = $"/Tx BMC\nq\nBT\n/{(wa.wuFonts is not null ? wa.wuRes : wa.fontName)} {Format(wa.fontSize)} Tf\n{ExtractDaColor(wa.da)}\n{Format(tx)} {Format(wY)} Td\n{showOp} Tj\nET\nQ\nEMC\n";
    }

    /// <summary>The stages of the widget appearance dictionary: the unicode font resolve and the plain (non-comb) content.</summary>
    private void ResolveUnicodeWidgetFont(WidgetApState wa)
    {
        wa.wuFam = NormalizeStdFontName(wa.fontName);
        wa.wuTtf = Aspose.Pdf.Text.SystemFontResolver.Resolve(wa.wuFam)
                ?? Aspose.Pdf.Text.SystemFontResolver.Resolve("Arial");
        if ((wa.wuTtf is not { Length: > 12 } || !CoversBeyondAnsi(wa.wuTtf, wa.text))
            && TextStamp.TryResolveCjkTtf(wa.text) is { } cjk)
        {
            wa.wuTtf = cjk.ttf;
            wa.wuFam = cjk.name;
        }
        if (wa.wuTtf is { Length: > 12 })
        {
            wa.wuFonts = new PdfDictionary();
            (wa.wuRes, _) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(wa.wuFonts, wa.wuTtf, wa.wuFam, "");
            _uniAppearanceTtf = wa.wuTtf;
        }
        else
        {
            wa.wuTtf = null;
        }
    }
}
