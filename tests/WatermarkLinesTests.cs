using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class WatermarkLinesTests
{
    [Fact]
    public void Watermark_Lines_StackUpwardFromThePosition_InTheStyledFace()
    {
        byte[] saved;
        using (var doc = Document.Open(PdfBuilder.BuildMinimal()))
        {
            using var artifact = new WatermarkArtifact();
            var state = new TextState { FontStyle = FontStyles.Bold, FontSize = 10f };
            state.Font = FontRepository.FindFont("Arial", true);
            artifact.SetLinesAndState(new[] { "The first line", "The second line", "The third line" }, state);
            artifact.Position = new Point(100, 400);
            doc.Pages[1].Artifacts.Add(artifact);
            using var ms = new MemoryStream();
            doc.Save(ms);
            saved = ms.ToArray();
        }

        using var reloaded = Document.Open(saved);
        var page = reloaded.Pages[1];
        var forms = page.Resources.Forms;
        Assert.True(forms.Count >= 1);
        var content = Encoding.ASCII.GetString(forms[1].DecodedBytes);
        var runs = content.Split(new[] { " Tj" }, StringSplitOptions.None).Length - 1;
        Assert.Equal(3, runs);
        // Every line after the first steps DOWN by one pitch: at least the face's
        // ascent + descent (1.117 em for Arial), at most a generous 1.3 em.
        var steps = System.Text.RegularExpressions.Regex.Matches(content, @"0 -(\d+(?:\.\d+)?) Td");
        Assert.Equal(2, steps.Count);
        foreach (System.Text.RegularExpressions.Match m in steps)
        {
            var pitch = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(pitch, 11.0, 13.0);
        }
        // The bold style reaches the written face.
        var fontNames = page.Resources.Fonts.Select(f => f.BaseFont).ToList();
        Assert.Contains(fontNames, n => n is not null && n.Contains("Bold"));
    }
}
