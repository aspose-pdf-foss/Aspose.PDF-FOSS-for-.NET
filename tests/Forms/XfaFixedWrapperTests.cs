using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class XfaFixedWrapperTests
{
    /// <summary>A dynamic XFA form drawn page by page: two bodies, each starting a page, the first
    /// holding one wrapper around a table of six 75pt rows - 450pt of rows for a content area of
    /// 300pt. <paramref name="rowContent"/> is placed in every row (an occurrence rule, or nothing).</summary>
    private static byte[] BuildForm(string rowContent)
    {
        var rows = string.Concat(Enumerable.Range(1, 6).Select(i =>
            $"<subform layout=\"row\" h=\"75pt\">{rowContent}<draw w=\"400pt\" h=\"75pt\"><value><text>ROW {i}</text></value></draw></subform>"));
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"72pt\" w=\"540pt\" h=\"300pt\"/></pageArea></pageSet>"
            + "<subform name=\"first\" layout=\"tb\"><subform name=\"content\" layout=\"tb\">"
            + "<subform name=\"table\" layout=\"table\" columnWidths=\"400pt\">" + rows + "</subform></subform></subform>"
            + "<subform name=\"second\" layout=\"tb\"><breakBefore startNew=\"1\"/>"
            + "<draw w=\"400pt\" h=\"100pt\"><value><text>SECOND BODY</text></value></draw></subform>"
            + "</subform></template>";
        const string content = "BT /F1 12 Tf 72 700 Td (Please wait...) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /NeedsRendering true /AcroForm 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Fields [] /XFA [(template) 7 0 R] >>",
            $"<< /Length {template.Length} >>\nstream\n{template}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string[] ConvertedPageTexts(string rowContent)
    {
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildForm(rowContent))))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        using var reopened = new Document(saved);
        return Enumerable.Range(1, reopened.Pages.Count).Select(p =>
        {
            var absorber = new TextAbsorber();
            reopened.Pages[p].Accept(absorber);
            return absorber.Text;
        }).ToArray();
    }

    [Fact]
    public void AWrapperAroundOneTableOfFixedRows_StaysOnItsPage()
    {
        var pages = ConvertedPageTexts(string.Empty);

        Assert.Equal(2, pages.Length);
        Assert.Contains("ROW 1", pages[0]);
        Assert.Contains("ROW 6", pages[0]);
        Assert.Contains("SECOND BODY", pages[1]);
    }

    [Fact]
    public void AWrapperAroundOneTableOfRepeatableRows_ContinuesItsRowsOnTheNextPage()
    {
        var pages = ConvertedPageTexts("<occur max=\"-1\"/>");

        Assert.Equal(3, pages.Length);
        Assert.Contains("ROW 4", pages[0]);
        Assert.Contains("ROW 5", pages[1]);
        Assert.Contains("SECOND BODY", pages[2]);
    }
}
