using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a publication's cover: a display title beside a block of smaller lines on baselines of their
/// own, and under them a picture in a ruled box with a second ruled box under it, sharing its edge, holding a line and
/// two rows of three parts the page draws column by column.</summary>
public class AutoTagCoverTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Courier (6 pt per character at 10 pt) and Im1, a
    /// picture of 2 x 2 grey pixels.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> /XObject << /Im1 6 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length 9 >>\nstream\n20E0E020>\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string Text(double x, double y, string text, double size = 10)
        => $"BT /F1 {size} Tf {x} {y} Td ({text}) Tj ET\n";

    // The title at the left, two lines of 27 pt; at the right three smaller lines, each on a baseline of its own.
    private static readonly string Band = Text(170, 735, "Field Notes", 27) + Text(170, 707, "Yearbook", 27)
        + Text(460, 750, "Report 526", 12) + Text(460, 725, "For planning", 14) + Text(460, 700, "2025 Season", 16);

    // Two boxes ruled 5 pt thick, the lower sharing the upper's bottom edge; the rules down each side drawn per box.
    private const string Boxes = "5 w 42 598 m 570.7 598 l S 568.2 109.9 m 568.2 600.5 l S 42 109.9 m 570.7 109.9 l S "
                                 + "44.5 109.9 m 44.5 600.5 l S 568.2 52.4 m 568.2 109.9 l S 42 54.9 m 570.7 54.9 l S 44.5 52.4 m 44.5 109.9 l S\n";

    private const string Picture = "q 518 0 0 478 47 116 cm /Im1 Do Q\n";

    // The lower box's line, then its two rows of three parts drawn a column at a time.
    private static readonly string Parts = Text(77, 90, "Get the reports faster at:", 9)
        + Text(77, 78, "* Field (One)", 9) + Text(77, 67, "* Earth (Four)", 9)
        + Text(241, 78, "* Water (Two)", 9) + Text(241, 67, "* Cloud (Five)", 9)
        + Text(392, 78, "* Stone (Three)", 9) + Text(392, 67, "* River (Six)", 9);

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text)).Trim();

    private static StructureAttributes Layout(StructureElement el) => el.Attributes.GetAttributes(AttributeOwnerStandard.Layout);

    private static List<StructureElement> Elements(Document doc) =>
        doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();

    [Fact]
    public void ATitleBesideABlockOfSmallerLinesOnBaselinesOfTheirOwnIsReadBeforeIt()
    {
        using var doc = Tag(Build(Band + Boxes + Picture + Parts));
        var texts = Elements(doc).Where(e => e.S.Name is "P" || e.S.Name.StartsWith("H")).Select(Text).Where(t => t.Length > 0).ToList();
        var (title, report) = (texts.FindIndex(t => t.StartsWith("Field Notes")), texts.FindIndex(t => t.StartsWith("Report 526")));
        Assert.True(title >= 0 && report > title, string.Join(" | ", texts));
        // The title's two lines read on together, the block's lines after them.
        Assert.Contains("Yearbook", texts[title]);
    }

    [Fact]
    public void BoxesRuledRoundAPictureAndRoundLinesAreDivsStatingTheirRules()
    {
        using var doc = Tag(Build(Band + Boxes + Picture + Parts));
        var divs = Elements(doc).Where(e => e.S.Name == "Div" && Layout(e).GetAttribute(AttributeKey.BorderThickness) is not null).ToList();
        Assert.Equal(2, divs.Count);
        double?[] Rules(StructureElement div) => Layout(div).GetAttribute(AttributeKey.BorderThickness)!.GetArrayNumberValue()!;
        // The picture's box: its top and sides; the edge it shares with the box under it is that box's top.
        Assert.Single(divs[0].FindElements<StructureElement>(true), e => e.S.Name == "Figure");
        Assert.Equal(new double?[] { 5, 0, 5, 5 }, Rules(divs[0]));
        Assert.Equal(new double?[] { 5, 5, 5, 5 }, Rules(divs[1]));
        // The lines in the lower box are its paragraph, not the labels of a drawing.
        var lines = Assert.Single(divs[1].FindElements<StructureElement>(true), e => e.S.Name == "P");
        Assert.StartsWith("Get the reports", Text(lines));
        // The box's rules are the box's: none of them rules its paragraph.
        Assert.Null(Layout(lines).GetAttribute(AttributeKey.BorderThickness));
    }

    /// <summary>A notice a word processor sets on a grey shade inside a box, drawn line by line as it draws a shaded,
    /// bordered paragraph: under each line's band a grey fill across the box and a short black fill down each side (0.96 pt
    /// wide), a rule across the top and one across the foot.</summary>
    private static string ShadedNotice()
    {
        var sb = new System.Text.StringBuilder();
        const double top = 429.4, band = 12.5;
        for (var k = 0; k < 14; k++)
        {
            var y = top - (k + 1) * band;
            sb.Append(System.FormattableString.Invariant($"0.855 g 62.5 {y} 487 {band} re f 0 g 61.6 {y} 0.96 {band} re f 549.5 {y} 0.96 {band} re f\n"));
        }
        sb.Append(System.FormattableString.Invariant($"0 g 61.6 {top} 488.8 0.96 re f 61.6 {top - 14 * band - 0.96} 488.8 0.96 re f\n"));
        for (var k = 0; k < 13; k++)
            sb.Append(Text(72, top - 10 - k * band, $"Certain entities may be named in this notice to describe a method N{k:D2}", 9));
        return sb.ToString();
    }

    [Fact]
    public void ANoticeSetOnAShadeInsideABoxIsADivStatingItsRulesAndShade()
    {
        using var doc = Tag(Build(Band + ShadedNotice()));
        var div = Assert.Single(Elements(doc), e => e.S.Name == "Div" && Layout(e).GetAttribute(AttributeKey.BorderThickness) is not null);
        // Ruled on all four sides, its sides drawn a line at a time.
        Assert.All(Layout(div).GetAttribute(AttributeKey.BorderThickness)!.GetArrayNumberValue()!, t => Assert.True(t > 0));
        Assert.Equal(new double?[] { 0.855, 0.855, 0.855 }, Layout(div).GetAttribute(AttributeKey.BackgroundColor)?.GetArrayNumberValue());
        Assert.Contains(div.FindElements<StructureElement>(true), e => e.S.Name == "P" && Text(e).Contains("N12"));
    }

    /// <summary>A chapter heading set in white on a black band ruled round with hairlines, the band as tight as its line:
    /// the heading's descent reaches under the band's bottom rule. Prose under it.</summary>
    private static string Chapter(double bottom, string heading, string tag)
    {
        var top = bottom + 16.2;
        var sb = new System.Text.StringBuilder(System.FormattableString.Invariant(
            $"0 g 66.5 {bottom} 479 {top - bottom} re f 0.48 w 66.3 {bottom} m 545.7 {bottom} l S 66.3 {top} m 545.7 {top} l S 66.3 {bottom} m 66.3 {top} l S 545.7 {bottom} m 545.7 {top} l S\n"));
        sb.Append("1 g ").Append(Text(72, bottom + 2.2, heading, 14)).Append("0 g\n");
        for (var k = 0; k < 4; k++)
            sb.Append(Text(72, bottom - 24 - 12 * k, $"The survey of the ridge went on through the summer months {tag}{k}"));
        return sb.ToString();
    }

    [Fact]
    public void AHeadingOnABandRuledRoundItIsTheBoxsHeadingThoughItsDescentReachesUnderTheBand()
    {
        using var doc = Tag(Build(Chapter(700, "1 Introduction", "A") + Chapter(560, "2 Methods", "B")));
        var divs = Elements(doc).Where(e => e.S.Name == "Div" && Layout(e).GetAttribute(AttributeKey.BorderThickness) is not null).ToList();
        foreach (var heading in new[] { "1 Introduction", "2 Methods" })
            Assert.Contains(divs, d => Text(d) == heading);
        Assert.All(divs, d => Assert.Equal(new double?[] { 0, 0, 0 }, Layout(d).GetAttribute(AttributeKey.BackgroundColor)?.GetArrayNumberValue()));
    }

    [Fact]
    public void AStringTurnedToRunDownTheMarginReadsAsItsWordsInTheBoxItCovers()
    {
        // A running header down the left margin: a string turned a quarter clockwise (Courier 9 pt, 5.4 pt a character),
        // shown in kerned pieces, from (19.68, 559.26) down the page.
        var side = "/Artifact <</Type /Pagination /Subtype /Header >>BDC BT /F1 1 Tf 0 -9 9 0 19.68 559.26 Tm "
                   + "[(T)-5.3 (hi)-1 (s)-3 ( pub)-7 (l)-1 (i)-1 (c)-3 (at)-5 (i)-7.7 (o)-7 (n)] TJ ET EMC\n";
        using var doc = Tag(Build(side + Chapter(600, "1 Introduction", "A")));
        var turned = doc.Pages[1].GetArtifactContent().Where(i => i.Kind == MarkedContentKind.Text && i.Rotation == 270).ToList();
        var run = Assert.Single(turned);
        Assert.Equal("This publication", run.Text);
        // Its box: across the margin a size wide, from where it starts down to where it ends (16 characters, less the kerns).
        Assert.InRange(run.Rectangle.Width, 8.9, 9.1);
        Assert.Equal(559.26, run.Rectangle.URY, 1);
        Assert.InRange(run.Rectangle.Height, 85, 87);
    }

    [Fact]
    public void TextDrawnAColumnAtATimeIsReadARowAtATime()
    {
        using var doc = Tag(Build(Band + Boxes + Picture + Parts));
        var lines = Assert.Single(Elements(doc), e => e.S.Name == "P" && Text(e).StartsWith("Get the reports"));
        var words = Text(lines).Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Where(w => w.StartsWith("(")).ToList();
        Assert.Equal(new[] { "(One)", "(Two)", "(Three)", "(Four)", "(Five)", "(Six)" }, words);
    }
}
