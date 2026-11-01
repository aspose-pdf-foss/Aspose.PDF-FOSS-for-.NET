using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class XfaOverflowTargetTests
{
    /// <summary>A dynamic XFA form of two page areas, each drawing its own label, whose one flowing
    /// body holds six rows of 100pt for content areas of 300pt and names where its overflow goes
    /// with <paramref name="overflow"/> (an element placed first in the body).</summary>
    private static byte[] BuildForm(string overflow)
    {
        static string Area(string name, string label) =>
            $"<pageArea name=\"{name}\" id=\"{name}\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"72pt\" w=\"540pt\" h=\"300pt\"/>"
            + $"<draw x=\"36pt\" y=\"20pt\" w=\"300pt\" h=\"20pt\"><value><text>{label}</text></value></draw></pageArea>";
        var rows = string.Concat(Enumerable.Range(1, 6).Select(i =>
            $"<draw w=\"400pt\" h=\"100pt\"><value><text>ROW {i}</text></value></draw>"));
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet>" + Area("First", "MASTER ONE") + Area("Rest", "MASTER TWO") + "</pageSet>"
            + "<subform name=\"body\" layout=\"tb\">" + overflow + rows + "</subform></subform></template>";
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

    private static string[] ConvertedPageTexts(string overflow)
    {
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildForm(overflow))))
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
    public void WithoutAnOverflowTarget_EveryPageUsesTheFirstPageArea()
    {
        var pages = ConvertedPageTexts(string.Empty);

        Assert.Equal(2, pages.Length);
        Assert.All(pages, p => Assert.Contains("MASTER ONE", p));
        Assert.Contains("ROW 4", pages[1]);
    }

    [Theory]
    [InlineData("<overflow target=\"Rest\"/>")]
    [InlineData("<break overflowTarget=\"#Rest\"/>")]
    [InlineData("<break overflowTarget=\"Rest\"/>")]
    public void TheOverflowTargetNamesThePageAreaOfTheContinuationPages(string overflow)
    {
        var pages = ConvertedPageTexts(overflow);

        Assert.Equal(2, pages.Length);
        Assert.Contains("MASTER ONE", pages[0]);
        Assert.Contains("ROW 3", pages[0]);
        Assert.Contains("MASTER TWO", pages[1]);
        Assert.DoesNotContain("MASTER ONE", pages[1]);
        Assert.Contains("ROW 4", pages[1]);
    }
}
