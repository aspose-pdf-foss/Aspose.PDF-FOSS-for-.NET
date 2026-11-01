using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class XfaPrototypeTests
{
    /// <summary>A dynamic XFA form whose flowing body holds three texts, each naming the same
    /// prototype with <paramref name="reference"/>: a draw 200pt high kept in the root's proto
    /// element. The content area is 300pt high.</summary>
    private static byte[] BuildForm(string reference)
    {
        var rows = string.Concat(Enumerable.Range(1, 3).Select(i =>
            $"<draw {reference}><value><text>ROW {i}</text></value></draw>"));
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"72pt\" w=\"540pt\" h=\"300pt\"/></pageArea></pageSet>"
            + "<proto><subform name=\"styles\"><draw name=\"tall\" id=\"tallId\" w=\"400pt\" h=\"200pt\">"
            + "<font size=\"20pt\"/><value><text>PROTOTYPE TEXT</text></value></draw></subform></proto>"
            + "<subform name=\"body\" layout=\"tb\">" + rows + "</subform></subform></template>";
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

    private static (string[] texts, double firstSize) Converted(string reference)
    {
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildForm(reference))))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        using var reopened = new Document(saved);
        var texts = Enumerable.Range(1, reopened.Pages.Count).Select(p =>
        {
            var absorber = new TextAbsorber();
            reopened.Pages[p].Accept(absorber);
            return absorber.Text;
        }).ToArray();
        var fragments = new TextFragmentAbsorber("ROW 1");
        reopened.Pages[1].Accept(fragments);
        return (texts, fragments.TextFragments.Count > 0 ? fragments.TextFragments[1].TextState.FontSize : 0);
    }

    [Theory]
    [InlineData("use=\"#tallId\"")]
    [InlineData("usehref=\"#tallId\"")]
    [InlineData("usehref=\".#som($template.#subform.styles.tall)\"")]
    [InlineData("use=\"form1.styles.tall\"")]
    public void ABoxTakesTheSizeAndFontOfThePrototypeItNames_AndKeepsItsOwnText(string reference)
    {
        var (pages, size) = Converted(reference);

        // Three boxes of the prototype's 200pt in areas of 300pt: one to a page.
        Assert.Equal(3, pages.Length);
        Assert.Contains("ROW 1", pages[0]);
        Assert.Contains("ROW 3", pages[2]);
        Assert.All(pages, p => Assert.DoesNotContain("PROTOTYPE TEXT", p));
        Assert.Equal(20, size, 1);
    }

    [Theory]
    [InlineData("usehref=\"other.xdp#tallId\"")]
    [InlineData("use=\"#noSuchId\"")]
    public void AReferenceThatNamesNothingInTheTemplate_LeavesTheBoxAsItIs(string reference)
    {
        var (pages, _) = Converted(reference);

        Assert.Single(pages);
        Assert.Contains("ROW 3", pages[0]);
    }
}
