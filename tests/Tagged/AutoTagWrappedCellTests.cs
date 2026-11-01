using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of an unruled table whose cells are set at the foot of their rows: a cell of text wrapped over
/// lines stands over the line holding the row's name and figures, and its lines are that row's, one row however many
/// lines its cells take.</summary>
public class AutoTagWrappedCellTests
{
    /// <summary>A landscape page drawing <paramref name="content"/> with F1 Helvetica and F2 Helvetica-Bold, each with its
    /// widths and a descriptor stating its descent, as a word processor writes a font (the text's descenders then reach
    /// under its baseline).</summary>
    private static byte[] Build(string content)
    {
        string Font(string name)
        {
            var font = Aspose.Pdf.Text.FontRepository.FindFont(name);
            var widths = string.Join(" ", Enumerable.Range(32, 95).Select(c => System.Math.Round(font.MeasureString(((char)c).ToString(), 1000))
                .ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return $"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 126 /Widths [{widths}]"
                   + $" /FontDescriptor << /Type /FontDescriptor /FontName /{name} /Flags 32 /FontBBox [-166 -225 1000 931] /ItalicAngle 0"
                   + " /Ascent 718 /Descent -207 /CapHeight 718 /StemV 88 >> >>";
        }
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            Font("Helvetica"),
            Font("Helvetica-Bold"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 841.89 595.28] /Contents 6 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
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

    private static readonly double[] FigureEnds = [465, 516, 564, 615, 663];

    /// <summary>One line of the table as a word processor's tabs set it: a text object placing each cell by a matrix -
    /// the name at 56.7, the franchise at 187.1, the indication at 300.5, the figures ending at their tab stops (a null
    /// figure: an empty cell).</summary>
    private static string Line(double y, string? name, string? franchise, string? indication, params string?[] figures)
    {
        var helvetica = Aspose.Pdf.Text.FontRepository.FindFont("Helvetica");
        var sb = new System.Text.StringBuilder("BT /F1 1 Tf 0 g\n");
        // (a string's parentheses escaped, as the PDF writes them)
        void Cell(double x, string text) => sb.Append(System.FormattableString.Invariant(
            $"10 0 0 10 {x} {y} Tm ({text.Replace("(", "\\(").Replace(")", "\\)")}) Tj\n"));
        if (name is not null)
        {
            sb.Append("/F2 1 Tf\n");
            Cell(56.6924, name);
            sb.Append("/F1 1 Tf\n");
        }
        if (franchise is not null) Cell(187.0823, franchise);
        if (indication is not null) Cell(300.4724, indication);
        for (var k = 0; k < figures.Length; k++)
            if (figures[k] is { } figure) Cell(FigureEnds[k] - helvetica.MeasureString(figure, 10), figure);
        return sb.Append("ET\n").ToString();
    }

    [Fact]
    public void ACellsLinesWrappedOverTheLineHoldingItsRowsFiguresAreThatRow()
    {
        var content = "BT /F2 11 Tf 56.69 530.6 Td (Net sales of the top products in the second quarter) Tj ET\n"
                      + Line(488.72, "Brands", "Business Franchise", "Indication", "USD m", "% change", "USD m", "% change", "USD m")
                      + "0.5 w 56.69 484.5 m 765.7 484.5 l S\n"
                      + Line(475.72, "Gilenya", "Neuroscience", "Relapsing multiple sclerosis", "468", "-2", "398", "6", "866")
                      + Line(462.72, null, null, "Psoriasis, ankylosing")
                      + Line(451.72, null, "Immunology, Hepatology", "spondylitis and")
                      + Line(440.72, "Cosentyx", "and Dermatology", "psoriatic arthritis", "409", "33", "292", "53", "701")
                      + Line(427.72, null, null, "Age-related")
                      + Line(416.72, "Lucentis", "Ophthalmology", "macular degeneration", "515", "4", "515", "8", "515")
                      + Line(403.72, "Tasigna", "Oncology", "Chronic myeloid leukemia", "219", "6", "269", "2", "488")
                      + Line(390.72, null, null, "Chronic myeloid")
                      + Line(379.72, "Gleevec/Glivec", "Oncology", "leukemia and GIST", "109", "-39", "307", "-10", "416")
                      + Line(366.72, null, null, "Carcinoid tumors")
                      + Line(355.72, "Sandostatin", "Oncology", "and Acromegaly", "208", "0", "191", "-4", "399")
                      + Line(342.72, "Exjade/Jadenu", "Oncology", "Chronic iron overload", "137", "5", "152", "8", "289");
        using var doc = Tag(Build(content));
        var rows = doc.TaggedContent.StructTreeRootElement.FindElements<TableTRElement>(true)
            .Select(tr => tr.ChildElements.OfType<StructureElement>().Where(c => c is TableTDElement or TableTHElement).Select(Text).ToList())
            .ToList();

        var cosentyx = Assert.Single(rows, r => r.Contains("Cosentyx"));
        Assert.Contains("Immunology, Hepatology and Dermatology", cosentyx);
        Assert.Contains("Psoriasis, ankylosing spondylitis and psoriatic arthritis", cosentyx);
        Assert.Contains("409", cosentyx);
        var lucentis = Assert.Single(rows, r => r.Contains("Lucentis"));
        Assert.Contains("Age-related macular degeneration", lucentis);
        Assert.Contains("Chronic myeloid leukemia and GIST", Assert.Single(rows, r => r.Contains("Gleevec/Glivec")));
        // No row holds a wrapped cell's lines alone.
        Assert.DoesNotContain(rows, r => r.Contains("Psoriasis, ankylosing") || r.Contains("Age-related"));
    }

    /// <summary>The rows of <paramref name="doc"/>'s tables, each the texts of its cells.</summary>
    private static List<List<string>> Rows(Document doc)
        => doc.TaggedContent.StructTreeRootElement.FindElements<TableTRElement>(true)
            .Select(tr => tr.ChildElements.OfType<StructureElement>().Where(c => c is TableTDElement or TableTHElement).Select(Text).ToList())
            .ToList();

    /// <summary>A rule across the table under a row, through its descenders.</summary>
    private static string RuleUnder(double baseline)
        => System.FormattableString.Invariant($"0.4 w 56.69 {baseline - 1.4} m 771.02 {baseline - 1.4} l S\n");

    private static string Heading()
        => Line(488.72, "Brands", "Business Franchise", "Indication", "USD m", "% change", "USD m", "% change", "USD m") + RuleUnder(488.72);

    [Fact]
    public void ARowLeavingItsFirstFiguresEmptyUnderARuleThroughTheDescendersOfTheRowAboveIsARowOfItsOwn()
    {
        // The rows over it wrap their indications over two lines; the rule under Galvus runs through its descenders.
        var content = Heading()
                      + Line(475.72, "Tasigna", "Oncology", "Chronic myeloid leukemia", "219", "6", "269", "2", "488") + RuleUnder(475.72)
                      + Line(462.72, null, null, "Chronic myeloid")
                      + Line(451.72, "Gleevec/Glivec", "Oncology", "leukemia and GIST", "109", "-39", "307", "-10", "416") + RuleUnder(451.72)
                      + Line(438.72, null, null, "Carcinoid tumors")
                      + Line(427.72, "Sandostatin", "Oncology", "and Acromegaly", "208", "0", "191", "-4", "399") + RuleUnder(427.72)
                      + Line(414.72, "Afinitor/Votubia", "Oncology", "Breast cancer / TSC", "242", "15", "166", "-7", "408") + RuleUnder(414.72)
                      + Line(401.72, "Galvus Group", "Established Medicines", "Diabetes", null, null, "332", "7", "332") + RuleUnder(401.72)
                      + Line(388.72, "Tafinlar + Mekinist", "Oncology", "Melanoma", "115", "34", "169", "25", "284") + RuleUnder(388.72)
                      + Line(375.72, "Exjade/Jadenu", "Oncology", "Chronic iron overload", "137", "5", "152", "8", "289") + RuleUnder(375.72);
        using var doc = Tag(Build(content));
        var rows = Rows(doc);

        var galvus = Assert.Single(rows, r => r.Any(c => c.Contains("Galvus Group")));
        Assert.DoesNotContain(galvus, c => c.Contains("Afinitor"));
        Assert.Contains("332", galvus);
        Assert.Contains("Breast cancer / TSC", Assert.Single(rows, r => r.Any(c => c.Contains("Afinitor/Votubia"))));
    }

    [Fact]
    public void ARowWhoseCellWrapsOverItEndingARunOfRowsKeepsTheWrappedLine()
    {
        // "Immune" stands over the row's line; the row under it leaves its first figures empty, so the rows set alike end
        // with the wrapped row.
        var content = Heading()
                      + Line(475.72, "Tafinlar + Mekinist", "Oncology", "Melanoma", "115", "34", "169", "25", "284") + RuleUnder(475.72)
                      + Line(462.72, "Exjade/Jadenu", "Oncology", "Chronic iron overload", "137", "5", "152", "8", "289") + RuleUnder(462.72)
                      + Line(449.72, null, null, "Immune")
                      + Line(438.72, "Promacta/Revolade", "Oncology", "thrombocytopenic purpura", "147", "35", "145", "42", "292") + RuleUnder(438.72)
                      + Line(425.72, "Xolair", "Respiratory", "Asthma", null, null, "261", "12", "261") + RuleUnder(425.72)
                      + Line(412.72, "Diovan Group", "Established Medicines", "Hypertension", "20", "-5", "224", "0", "244") + RuleUnder(412.72)
                      + Line(399.72, "Exforge Group", "Established Medicines", "Hypertension", "5", "0", "245", "2", "250") + RuleUnder(399.72);
        using var doc = Tag(Build(content));
        var rows = Rows(doc);

        var promacta = Assert.Single(rows, r => r.Any(c => c.Contains("Promacta/Revolade")));
        Assert.Contains("Immune thrombocytopenic purpura", promacta);
        Assert.DoesNotContain(rows, r => r.Any(c => c == "Immune"));
    }

    [Fact]
    public void RowsWhoseCellsWrapOverThemOneAfterAnotherAreRowsOfOneTable()
    {
        // Each row's cells wrap over lines above its own, a wrapped line of the franchise running on into the
        // indication's text ("Immunology, Hepatology " ends where "TRAPS" starts).
        var content = Heading()
                      + Line(475.72, "Entresto", "Cardio-Metabolic", "Chronic Heart Failure", "129", "95", "110", "145", "239") + RuleUnder(475.72)
                      + Line(462.72, "Votrient", "Oncology", "Renal cell carcinoma", "108", "1", "111", "11", "219") + RuleUnder(462.72)
                      + Line(449.72, null, null, "Reduction of elevated")
                      + Line(438.72, "Travoprost Group", "Ophthalmology", "intraocular pressure", "50", "2", "84", "-13", "134") + RuleUnder(438.72)
                      + Line(425.72, null, null, "Auto-inflammatory (CAPS,")
                      + Line(414.72, null, "Immunology, Hepatology ", "TRAPS, HIDS/MKD, FMF,")
                      + Line(403.72, "Ilaris", "and Dermatology", "SJIA, AOSD and Gout)", "61", "30", "71", "38", "132") + RuleUnder(403.72)
                      + Line(390.72, null, "Immunology, Hepatology ", null)
                      + Line(379.72, "Neoral/Sandimmun(e)", "and Dermatology", "Transplantation", "7", "-30", "113", "-5", "120") + RuleUnder(379.72);
        using var doc = Tag(Build(content));
        var rows = Rows(doc);

        var ilaris = Assert.Single(rows, r => r.Any(c => c.Contains("Ilaris")));
        Assert.Contains("Immunology, Hepatology and Dermatology", ilaris);
        Assert.Contains("Auto-inflammatory (CAPS, TRAPS, HIDS/MKD, FMF, SJIA, AOSD and Gout)", ilaris);
        Assert.Contains("Immunology, Hepatology and Dermatology", Assert.Single(rows, r => r.Any(c => c.Contains("Neoral"))));
    }

    [Fact]
    public void ANumberWhoseThousandsASpacePartsIsOneCell()
    {
        // The totals set their thousands apart by a space, each group a string of its own ("2" and "434", the second a
        // digit and a space on, at 10 pt).
        var content = Heading()
                      + Line(475.72, "Top 20 products total", null, null, "2 434", "10", "4 394", "9", "6 828") + RuleUnder(475.72)
                      + Line(462.72, "Rest of portfolio", null, null, "589", "4", "1 459", "2", "2 048") + RuleUnder(462.72)
                      + Line(449.72, "Total Division sales", null, null, "3 023", "9", "5 853", "7", "8 876") + RuleUnder(449.72);
        foreach (var number in new[] { "2 434", "4 394", "6 828", "1 459", "2 048", "3 023", "5 853", "8 876" })
            content = content.Replace($"({number})", $"({number[0]}) Tj 0.834 0 Td ({number[2..]})");
        using var doc = Tag(Build(content));
        var rows = Rows(doc);

        var top = Assert.Single(rows, r => r.Any(c => c.Contains("Top 20 products total")));
        Assert.Contains(top, c => c.Replace(" ", "") == "2434");
        Assert.Contains("10", top);
    }
}
