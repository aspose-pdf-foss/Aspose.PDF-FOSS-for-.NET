using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>A dynamic XFA form laid out to pages: scripts that test a member of an exclusive group, and a long word
/// in a rich text.</summary>
public class XfaRenderedLayoutTests
{
    private const string Page = "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
        + "<contentArea x=\"36pt\" y=\"36pt\" w=\"540pt\" h=\"700pt\"/></pageArea></pageSet>";

    /// <summary>A dynamic form (NeedsRendering, a notice page) of the template body and the data record given.</summary>
    private static byte[] BuildForm(string body, string record)
    {
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + Page + body + "</subform></template>";
        var datasets = "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\"><xfa:data><form1>" + record
            + "</form1></xfa:data></xfa:datasets>";
        const string content = "BT /F1 12 Tf 72 700 Td (Please wait...) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /NeedsRendering true /AcroForm 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Fields [] /XFA [(template) 7 0 R (datasets) 8 0 R] >>",
            $"<< /Length {Encoding.UTF8.GetByteCount(template)} >>\nstream\n{template}\nendstream",
            $"<< /Length {Encoding.UTF8.GetByteCount(datasets)} >>\nstream\n{datasets}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.UTF8.GetBytes(s));
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

    private static Document Standard(byte[] form)
    {
        var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(form)))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        return new Document(saved);
    }

    private static string Text(Document doc)
    {
        var absorber = new TextAbsorber();
        doc.Pages.Accept(absorber);
        return absorber.Text;
    }

    private static string Section(string name, string text, string member, string value) =>
        $"<subform name=\"{name}\" layout=\"tb\"><draw w=\"300pt\" h=\"20pt\"><value><text>{text}</text></value></draw>"
        + "<calculate><script contentType=\"application/x-javascript\">"
        + $"if (choices.pick.{member}.rawValue == {value}) {{ this.presence = \"visible\"; }} else {{ this.presence = \"hidden\"; }}"
        + "</script></calculate></subform>";

    [Fact]
    public void ASectionAScriptHidesByTheChosenMemberOfAGroupIsNotLaidOut()
    {
        const string group = "<subform name=\"choices\" layout=\"tb\"><exclGroup name=\"pick\" layout=\"tb\">"
            + "<field name=\"optA\" w=\"100pt\" h=\"12pt\"><ui><checkButton shape=\"round\"/></ui><items><text>1</text></items></field>"
            + "<field name=\"optB\" w=\"100pt\" h=\"12pt\"><ui><checkButton shape=\"round\"/></ui><items><text>2</text></items></field>"
            + "</exclGroup></subform>";
        using var doc = Standard(BuildForm(group + Section("sectionA", "SECTION ALPHA", "optA", "1") + Section("sectionB", "SECTION BETA", "optB", "2"),
            "<choices><pick>2</pick></choices>"));

        var text = Text(doc);
        Assert.Contains("SECTION BETA", text);
        Assert.DoesNotContain("SECTION ALPHA", text);
    }

    [Fact]
    public void ALongWordOfARichTextBreaksInsideItsBox()
    {
        const string draw = "<subform name=\"cells\" layout=\"tb\"><draw name=\"head\" w=\"50pt\" h=\"40pt\"><value><exData contentType=\"text/html\">"
            + "<body xmlns=\"http://www.w3.org/1999/xhtml\"><p>12. Auto Reimbursements</p></body></exData></value></draw></subform>";
        using var doc = Standard(BuildForm(draw, ""));

        var absorber = new TextFragmentAbsorber();
        doc.Pages[1].Accept(absorber);
        var fragments = absorber.TextFragments.Cast<TextFragment>().Where(f => f.Text.Trim().Length > 0).ToList();
        Assert.NotEmpty(fragments);
        foreach (var f in fragments)
            Assert.True(f.Rectangle!.URX <= 36 + 50 + 1, $"'{f.Text}' runs to {f.Rectangle.URX:0.#}, past the box");
    }
}
