using System.Globalization;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class FloatingBox : BaseParagraph
{
    /// <summary>Floating box paragraphs: one paragraph of the box laid out into the content stream.</summary>
    private bool BuildParagraph(FloatingBoxBuildState bx, BaseParagraph paragraph)
    {
        if (paragraph is TextFragment textFragment)
        {
            if (!BuildTextParagraph(bx, textFragment)) return false;
        }
        else if (paragraph is HtmlFragment htmlChild)
        {
            // An HtmlFragment child sets in the HTML engine's own face and rhythm
            // (Times New Roman 12 in a 13.5 pt line box, bold runs inline, <br> a
            // forced break) — the box only supplies the content origin. Without this
            // the fragment was silently dropped: the loop knew TextFragment, Table
            // and Image only.
            var htmlW = Width - bx.padLeft - bx.padRight;
            var consumed = Table.DrawHtmlEngineFragment(bx.builder, bx.page, htmlChild.HtmlContent,
                bx.contentX, bx.contentY, htmlW > 0 ? htmlW : 0);
            if (consumed is { } usedH) bx.contentY -= usedH;
        }
        else if (paragraph is Table table)
        {
            BuildTableParagraph(bx, table);
        }
        else if (paragraph is Image image)
        {
            if (TryBuildImageParagraph(bx, image)) return true;
        }
        return true;
    }
}
