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
            // and Image only. A box that sized itself to its content has the lines
            // already wrapped; they are drawn, not parsed again.
            var consumed = _measuredHtml is not null && _measuredHtml.TryGetValue(htmlChild, out var measured)
                ? measured.Draw(bx.builder, bx.page, bx.contentX, bx.contentY)
                : Table.DrawHtmlEngineFragment(bx.builder, bx.page, htmlChild.HtmlContent,
                    bx.contentX, bx.contentY, ContentWidth());
            if (consumed is { } usedH) bx.contentY -= usedH;
        }
        else if (paragraph is Table table)
        {
            BuildTableParagraph(bx, table);
        }
        else if (paragraph is Graph graph)
        {
            BuildGraphParagraph(bx, graph);
        }
        else if (paragraph is Image image && TryBuildImageParagraph(bx, image)) return true;
        return true;
    }

    /// <summary>A graph child draws with its top-left corner at the content cursor, over the box's
    /// background and border, and the cursor moves below it.</summary>
    private static void BuildGraphParagraph(FloatingBoxBuildState bx, Graph graph)
    {
        bx.contentY -= graph.Height;
        bx.builder.RestoreState();
        bx.page.AddContentStream(bx.builder.Build());
        bx.page.AddContentStream(graph.Build(bx.page, bx.contentX, bx.contentY));
        bx.builder = new ContentStreamBuilder();
        bx.builder.SaveState();
    }
}
