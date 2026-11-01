using System.IO;
using System.Linq;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>An XFA template's colours are read as the numbers they are whatever the culture of the machine: a
/// component written with a '+' is that component, in a text's font colour and in a line's edge colour.</summary>
public class XfaColourCultureTests
{
    /// <summary>A dynamic XFA form of a text drawn in <paramref name="colour"/> above a rule drawn in it.</summary>
    private static byte[] BuildForm(string colour)
    {
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"36pt\" w=\"540pt\" h=\"720pt\"/></pageArea></pageSet>"
            + "<draw name=\"label\" w=\"200pt\" h=\"20pt\"><font typeface=\"Helvetica\" size=\"10pt\"><fill><color value=\""
            + colour + "\"/></fill></font><value><text>Tinted</text></value></draw>"
            + "<draw name=\"rule\" w=\"200pt\" h=\"2pt\"><value><line><edge thickness=\"1pt\"><color value=\""
            + colour + "\"/></edge></line></value></draw></subform></template>";
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

    /// <summary>The text's colour and the stroke colours the converted page sets.</summary>
    private static string ConvertedColours(string colour, string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildForm(colour))))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        using var reopened = new Document(saved);
        var absorber = new TextFragmentAbsorber("Tinted");
        reopened.Pages[1].Accept(absorber);
        var text = absorber.TextFragments.Select(f => f.TextState.ForegroundColor?.ToString());
        var strokes = reopened.Pages[1].Contents.OfType<SetRGBColorStroke>().Select(op => op.ToString());
        return string.Join(" | ", text.Concat(strokes));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AColourComponentWithAPlusSignIsReadAsUnderTheInvariantCulture(string culture)
    {
        var expected = ConvertedColours("200,0,0", string.Empty);
        Assert.NotEqual(ConvertedColours("0,0,0", string.Empty), expected); // the colour reaches the page
        Assert.Contains(" RG", expected, StringComparison.Ordinal); // and the rule's stroke
        Assert.Equal(expected, ConvertedColours("+200,+0,+0", string.Empty)); // a component's '+' is no part of its value

        Assert.Equal(expected, ConvertedColours("+200,+0,+0", culture));
    }
}
