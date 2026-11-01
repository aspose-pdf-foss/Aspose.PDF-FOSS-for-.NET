using System.IO;
using System.Linq;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>An XFA form's scripts run the same whatever the culture of the machine: a regular expression with the
/// i flag matches as JavaScript's does, under a Turkish culture too, whose case rules pair 'I' with a dotless small
/// letter.</summary>
public class XfaScriptCultureTests
{
    /// <summary>A dynamic XFA form of one field whose initialize script sets its value by
    /// <paramref name="expression"/>.</summary>
    private static byte[] BuildForm(string expression)
    {
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"36pt\" w=\"540pt\" h=\"720pt\"/></pageArea></pageSet>"
            + "<field name=\"probe\" w=\"200pt\" h=\"20pt\"><ui><textEdit/></ui>"
            + "<event activity=\"initialize\"><script contentType=\"application/x-javascript\">this.rawValue = "
            + expression + ";</script></event></field></subform></template>";
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

    /// <summary>The value the probe field has once the form is converted to an AcroForm under <paramref name="culture"/>.</summary>
    private static string? ConvertedValue(string expression, string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildForm(expression))))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        using var reopened = new Document(saved);
        return reopened.Form.Fields.Single(f => f.FullName?.Contains("probe") == true).Value;
    }

    [Theory]
    [InlineData("tr-TR", "/ti/i.test(\"TITLE\") ? \"found\" : \"missed\"", "found")]
    [InlineData("az-Latn-AZ", "/ti/i.test(\"TITLE\") ? \"found\" : \"missed\"", "found")]
    [InlineData("tr-TR", "\"TITLE\".replace(/i/gi, \"-\")", "T-TLE")]
    public void ACaseInsensitiveRegularExpressionMatchesAsJavaScriptDoes(string culture, string expression, string expected)
    {
        Assert.Equal(expected, ConvertedValue(expression, string.Empty));

        Assert.Equal(expected, ConvertedValue(expression, culture));
    }
}
