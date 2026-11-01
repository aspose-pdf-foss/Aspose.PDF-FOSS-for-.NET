using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class PendingLayoutPathsTests
{
    private static Document Saved(Document doc)
    {
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    private static string PageText(Document doc, int page = 1)
    {
        var absorber = new TextAbsorber();
        doc.Pages[page].Accept(absorber);
        return absorber.Text;
    }

    /// <summary>A dynamic XFA form - its one page a notice, NeedsRendering set - whose template draws one line of text.</summary>
    private static byte[] DynamicForm()
    {
        const string template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"72pt\" w=\"540pt\" h=\"600pt\"/></pageArea></pageSet>"
            + "<subform name=\"body\" layout=\"tb\"><draw w=\"300pt\" h=\"20pt\"><value><text>FORM CONTENT</text></value></draw></subform>"
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

    [Fact]
    public void APageOfADynamicXfaFormAddedToAnotherDocumentIsTheLaidOutForm_NotItsNotice()
    {
        using var form = new Document(new MemoryStream(DynamicForm()));
        using var copy = new Document();
        foreach (var page in form.Pages) copy.Pages.Add(page);
        using var back = Saved(copy);

        var text = PageText(back);
        Assert.Contains("FORM CONTENT", text);
        Assert.DoesNotContain("Please wait", text);
    }

    [Fact]
    public void APageStampOfAPageNotYetLaidOutCarriesItsParagraphsAndBackground()
    {
        using var source = new Document();
        var stampPage = source.Pages.Add();
        stampPage.SetPageSize(300, 100);
        stampPage.PageInfo.Margin = new MarginInfo(5, 5, 5, 5);
        stampPage.Background = Color.FromRgb(1, 0.5, 0);
        stampPage.Paragraphs.Add(new TextFragment("STAMPED PARAGRAPH"));
        using var target = new Document();
        target.Pages.Add();
        target.Pages[1].AddStamp(new PdfPageStamp(stampPage));
        using var back = Saved(target);

        Assert.Contains("STAMPED PARAGRAPH", PageText(back));
        var page = back.Pages[1];
        var forms = page.Reader.ResolveDict(page.Reader.ResolveDict(page.Dict.Get("Resources"))?.Get("XObject"))!;
        var stamped = string.Concat(forms.Keys.Select(k => Encoding.ASCII.GetString(page.Reader.DecodeStream(page.Reader.ResolveStream(forms.Get(k))!))));
        Assert.Matches(@"1 0\.5\d* 0 rg", stamped);
    }

    [Fact]
    public void TableCellsTakeTheDocumentsDefaultText_UnlessTheTableSetsItsOwn()
    {
        using var doc = new Document();
        doc.PageInfo.DefaultTextState = new TextState { FontSize = 17 };
        var page = doc.Pages.Add();
        var plain = new Table { ColumnWidths = "200" };
        plain.Rows.Add().Cells.Add("DEFAULTED");
        var own = new Table { ColumnWidths = "200", DefaultCellTextState = new TextState { FontSize = 9 } };
        own.Rows.Add().Cells.Add("TABLE OWN");
        page.Paragraphs.Add(plain);
        page.Paragraphs.Add(own);
        using var back = Saved(doc);

        var absorber = new TextFragmentAbsorber();
        back.Pages[1].Accept(absorber);
        var sizes = absorber.TextFragments.ToDictionary(f => f.Text, f => f.TextState.FontSize);
        Assert.Equal(17, sizes["DEFAULTED"], 1);
        Assert.Equal(9, sizes["TABLE OWN"], 1);
    }
}
