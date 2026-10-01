using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of statistical tables written a row per string: the figures of a row
/// spaced out into their columns inside one shown string, column headings with revision marks
/// set in full size above them.</summary>
public class AutoTagDenseTableTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Courier (6 pt per character at 10 pt).</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
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

    private static readonly string[] Labels = { "Total", "Revolving", "Nonrevolving", "Other" };
    private static readonly string[][] Figures =
    {
        new[] { "15.7", "17.7", "12.7", "12.0" },
        new[] { "16.9", "15.4", "18.9", "14.1" },
        new[] { "15.4", "15.4", "10.6", "11.2" },
        new[] { "11.1", "12.2", "13.3", "14.4" },
    };

    /// <summary>One row as one TJ: the label from x 72, then the figures from x 200, 54 pt apart
    /// (24 pt of figures and a 30 pt kerning gap).</summary>
    private static string Row(double y, string label, string[] figures)
        => $"BT /F1 10 Tf 72 {y} Td [({label}) -{(200 - 72 - label.Length * 6) * 100} "
           + string.Join(" -3000 ", figures.Select(f => $"({f})")) + "] TJ ET\n";

    private const string Prose = "BT /F1 10 Tf 72 700 Td (The table below gives the rates for each class of credit over four years.) Tj ET\n";

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

    private static List<List<string>> Cells(Document doc)
    {
        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Table");
        return table.FindElements<StructureElement>(true).Where(e => e.S.Name == "TR")
            .Select(r => r.ChildElements.OfType<StructureElement>().Where(c => c.S.Name is "TD" or "TH").Select(Text).ToList())
            .ToList();
    }

    [Fact]
    public void ARowWrittenAsOneStringIsCutIntoItsCells()
    {
        var content = Prose + string.Concat(Enumerable.Range(0, 4).Select(r => Row(660 - 14 * r, Labels[r], Figures[r])));
        using var doc = Tag(Build(content));
        var rows = Cells(doc);
        Assert.Equal(4, rows.Count);
        for (var r = 0; r < 4; r++)
            Assert.Equal(new[] { Labels[r] }.Concat(Figures[r]), rows[r]);
    }

    [Fact]
    public void ARowLabelWrappedOverTwoLinesStaysInItsRow()
    {
        // Row 2's label wraps: "2 Construction and" over "land loans" and its figures, indented under the number.
        var wrapped = "BT /F1 10 Tf 72 646 Td (2 Construction and) Tj ET\n" + Row(636, "  land loans", Figures[1]);
        var content = Prose + Row(660, "1 Total", Figures[0]) + wrapped + Row(622, "3 Other", Figures[2]) + Row(608, "4 Rest", Figures[3]);
        using var doc = Tag(Build(content));
        var rows = Cells(doc);
        Assert.Equal(4, rows.Count);
        Assert.Equal("2 Construction and land loans", string.Join(" ", rows[1][0].Split(' ', System.StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(Figures[1], rows[1].Skip(1));
    }

    [Fact]
    public void ANumberedLabelWrappedOntoAWordInCapitalsStaysInItsRow()
    {
        // Row 2's label wraps: "2 Funds sold and" over "RPs" and its figures - the part going on starts in capitals.
        var wrapped = "BT /F1 10 Tf 72 646 Td (2 Funds sold and) Tj ET\n" + Row(636, "  RPs", Figures[1]);
        var content = Prose + Row(660, "1 Total", Figures[0]) + wrapped + Row(622, "3 Other", Figures[2]) + Row(608, "4 Rest", Figures[3]);
        using var doc = Tag(Build(content));
        var rows = Cells(doc);
        Assert.Equal(4, rows.Count);
        Assert.Equal("2 Funds sold and RPs", string.Join(" ", rows[1][0].Split(' ', System.StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(Figures[1], rows[1].Skip(1));
    }

    [Fact]
    public void TheRowOverAWrappedLabelIsAsTallAsTheRowsPitch()
    {
        // Rows 14 pt apart; row 2's label wraps over two lines 10 pt apart, its figures on the second.
        var wrapped = "BT /F1 10 Tf 72 646 Td (2 Construction and) Tj ET\n" + Row(636, "  land loans", Figures[1]);
        var content = Prose + Row(660, "1 Total", Figures[0]) + wrapped + Row(622, "3 Other", Figures[2]) + Row(608, "4 Rest", Figures[3]);
        using var doc = Tag(Build(content));
        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Table");
        var heights = table.FindElements<StructureElement>(true).Where(e => e.S.Name == "TR")
            .Select(r => r.ChildElements.OfType<StructureElement>().First(c => c.S.Name is "TD" or "TH")
                .Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.Height)?.GetNumberValue() ?? 0).ToList();
        // Row 1 runs from its line down to midway to the wrapped label's first line, not to the middle of row 2.
        Assert.Equal(14, heights[0], 0.5);
        Assert.Equal(24, heights[1], 0.5);
    }

    [Fact]
    public void TheSpaceUnderATableIsMeasuredFromItsLastRowsEdge()
    {
        // Rows 14 pt apart, the last at 608; a paragraph under the table, its line 28 pt under the last row's: its space
        // over it is what is left under the last row's lower edge (a reader sets that row down to it), not under its text.
        var content = Prose + string.Concat(Enumerable.Range(0, 4).Select(r => Row(650 - 14 * r, Labels[r], Figures[r])))
                      + "BT /F1 10 Tf 72 580 Td (Figures are at an annual rate.) Tj ET\n";
        using var doc = Tag(Build(content));
        var note = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true), p => Text(p).StartsWith("Figures are"));
        var space = note.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.SpaceBefore)?.GetNumberValue() ?? 0;
        // The row's edge stands a size under its baseline (598); the line's top is 10 pt under it, less the usual gap
        // between lines: 8 pt - measured from the row's text it would be 14.
        Assert.InRange(space, 7, 9);
    }

    [Fact]
    public void AStackedHeaderBlockBecomesSpanningHeaderRows()
    {
        // Over the figures (x 200, 254, 308, 362): a group heading centred over the last two
        // columns, then two lines of stacked column names, then a section label at the left
        // edge; a caption of several words stands above it all and stays out.
        var caption = "BT /F1 10 Tf 72 740 Td (Seasonally adjusted, billions of dollars) Tj ET\n";
        var group = "BT /F1 10 Tf 320 720 Td (Not adjusted) Tj ET\n";
        var names1 = "BT /F1 10 Tf 200 706 Td (Total) Tj 54 0 Td (Bank) Tj 54 0 Td (Other) Tj 54 0 Td (Rest) Tj ET\n";
        var names2 = "BT /F1 10 Tf 200 694 Td (loans) Tj 54 0 Td (credit) Tj 54 0 Td (loans) Tj 54 0 Td (loans) Tj ET\n";
        var section = "BT /F1 10 Tf 72 680 Td (Assets) Tj ET\n";
        var content = caption + group + names1 + names2 + section
                      + string.Concat(Enumerable.Range(0, 4).Select(r => Row(666 - 14 * r, Labels[r], Figures[r])));
        using var doc = Tag(Build(content));
        var rows = Cells(doc);
        Assert.Equal(8, rows.Count);
        Assert.Equal(new[] { "", "Not adjusted" }, rows[0].Where(c => c is "" or "Not adjusted").Distinct());
        Assert.Equal(new[] { "", "Total", "Bank", "Other", "Rest" }, rows[1]);
        Assert.Equal(new[] { "", "loans", "credit", "loans", "loans" }, rows[2]);
        Assert.Equal("Assets", rows[3][0]);
        Assert.Equal(new[] { "Total" }.Concat(Figures[0]), rows[4]);
        var texts = doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Select(Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("Seasonally adjusted"));
    }

    /// <summary>Each cell's /BorderStyle name and /BorderThickness per edge (top, bottom, left, right), row by row.</summary>
    private static List<List<(string Style, double[] Edges)>> Borders(Document doc)
    {
        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Table");
        return table.FindElements<StructureElement>(true).Where(e => e.S.Name == "TR")
            .Select(r => r.ChildElements.OfType<StructureElement>().Where(c => c.S.Name is "TD" or "TH").Select(c =>
            {
                var layout = c.Attributes.GetAttributes(AttributeOwnerStandard.Layout);
                var style = layout.GetAttribute(AttributeKey.BorderStyle)?.GetNameValue()?.ToString() ?? "";
                var edges = layout.GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue()?.Select(t => t ?? 0).ToArray() ?? new double[4];
                return (style, edges);
            }).ToList())
            .ToList();
    }

    [Fact]
    public void ATableReadFromItsTextIsRuledWhereItsRulesStand()
    {
        // Rules over the headings, under them and under the last row, as statistical releases draw
        // them, a few points off the edges between the text's rows.
        var rules = "0.5 w 72 694 m 400 694 l S 72 672 m 400 672 l S 72 614 m 400 614 l S\n";
        var headings = Row(680, "Year", new[] { "2021", "2022", "2023", "2024" });
        var content = Prose + headings + string.Concat(Enumerable.Range(0, 4).Select(r => Row(660 - 14 * r, Labels[r], Figures[r]))) + rules;
        using var doc = Tag(Build(content));
        var rows = Borders(doc);
        Assert.Equal(5, rows.Count);
        // The headings: ruled over and under, no edge between the cells.
        Assert.All(rows[0], c => Assert.Equal(("Solid", 0.5, 0.5, 0.0, 0.0), (c.Style, c.Edges[0], c.Edges[1], c.Edges[2], c.Edges[3])));
        // The rows between stand unruled; the last is ruled under.
        Assert.All(rows[2], c => Assert.Equal(("None", 0.0), (c.Style, c.Edges.Sum())));
        Assert.All(rows[4], c => Assert.Equal((0.0, 0.5), (c.Edges[0], c.Edges[1])));
    }

    [Fact]
    public void TheBottomRuleOfATableRightOverATableReadFromItsTextIsNotTheLowerTablesTopEdge()
    {
        // A table ruled all round, then under it - a blank line between their rows, and between the rows under it - terms
        // and their definitions read as a table from their text, nothing drawn round them.
        var grid = "0.5 w 72 680 m 400 680 l S 72 660 m 400 660 l S 72 640 m 400 640 l S "
                   + "72 640 m 72 680 l S 180 640 m 180 680 l S 400 640 m 400 680 l S\n"
                   + "BT /F1 10 Tf 76 666 Td (NIST) Tj ET BT /F1 10 Tf 184 666 Td (National Institute) Tj ET\n"
                   + "BT /F1 10 Tf 76 646 Td (XOR) Tj ET BT /F1 10 Tf 184 646 Td (Exclusive-OR.) Tj ET\n";
        var terms = string.Concat(new[] { ("A", "The additional data."), ("C", "The ciphertext."), ("H", "The hash subkey."), ("P", "The plaintext.") }
            .Select((t, k) => $"BT /F1 12 Tf 72 {620 - 27.6 * k} Td ({t.Item1}) Tj ET BT /F1 12 Tf 180 {620 - 27.6 * k} Td ({t.Item2}) Tj ET "
                              + $"BT /F1 12 Tf 72 {606.2 - 27.6 * k} Td ( ) Tj ET\n"));
        using var doc = Tag(Build(Prose + grid + terms));
        var tables = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => e.S.Name == "Table").ToList();
        Assert.Equal(2, tables.Count);
        var cells = tables[1].FindElements<StructureElement>(true).Where(c => c.S.Name is "TD" or "TH").ToList();
        Assert.StartsWith("A", Text(cells[0]));
        // The upper table's bottom rule is its own edge: no cell of the lower table is ruled.
        foreach (var cell in cells)
        {
            var layout = cell.Attributes.GetAttributes(AttributeOwnerStandard.Layout);
            Assert.Equal(0.0, layout.GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue()?.Sum(t => t ?? 0) ?? 0);
        }
    }

    /// <summary>A row as <see cref="Row"/> with its label from <paramref name="x"/>.</summary>
    private static string RowAt(double x, double y, string label, string[] figures)
        => $"BT /F1 10 Tf {x} {y} Td [({label}) -{(200 - x - label.Length * 6) * 100} "
           + string.Join(" -3000 ", figures.Select(f => $"({f})")) + "] TJ ET\n";

    [Fact]
    public void SectionLabelsBetweenGroupsOfRowsAreRowsOfTheTable()
    {
        // Two groups of rows, their labels set in under section labels that stand out at the left.
        var content = Prose
                      + "BT /F1 10 Tf 72 680 Td (Loans) Tj ET\n" + RowAt(84, 666, "Car", Figures[0]) + RowAt(84, 652, "Home", Figures[1])
                      + "BT /F1 10 Tf 72 638 Td (Cards) Tj ET\n" + RowAt(84, 624, "Gold", Figures[2]) + RowAt(84, 610, "Plain", Figures[3]);
        using var doc = Tag(Build(content));
        var rows = Cells(doc);
        Assert.Equal(6, rows.Count);
        Assert.Equal(new[] { "Loans", "Car", "Home", "Cards", "Gold", "Plain" }, rows.Select(r => r[0]));
        Assert.Equal(Figures[3], rows[5].Skip(1));
    }

    [Fact]
    public void ARuleStandingAloneUnderAParagraphIsItsBottomBorder()
    {
        // A note, a rule 6 pt under its baseline across the text, and a paragraph well below.
        var content = Prose + "BT /F1 10 Tf 72 660 Td (Not seasonally adjusted. Percent except as noted.) Tj ET\n"
                      + "0.5 w 72 654 m 500 654 l S\n"
                      + "BT /F1 10 Tf 72 600 Td (The next section begins here, after the rule, with its own words.) Tj ET\n";
        using var doc = Tag(Build(content));
        var note = doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Single(p => Text(p).StartsWith("Not seasonally"));
        var layout = note.Attributes.GetAttributes(AttributeOwnerStandard.Layout);
        Assert.Equal("Solid", layout.GetAttribute(AttributeKey.BorderStyle)?.GetNameValue()?.ToString());
        Assert.Equal(new double?[] { 0, 0.5, 0, 0 }, layout.GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue());
        Assert.InRange(layout.GetAttribute(AttributeKey.Padding)!.GetArrayNumberValue()![1]!.Value, 2, 6);
    }

    [Fact]
    public void ARuleStandingAloneOverAHeadingNothingCloseAboveItIsTheHeadingsTopBorderAsThickAsItIsDrawn()
    {
        // The text ends well over a rule 2 pt thick; a heading starts 4 pt under the rule, its text after it.
        var content = Prose + "2 w 72 640 m 500 640 l S\n"
                      + "BT /F1 17 Tf 72 620 Td (Specific instructions) Tj ET\n"
                      + "BT /F1 10 Tf 72 600 Td (The next section begins here, under its heading, with its own words) Tj 0 -12 Td (and goes on for a line more so that it is a paragraph.) Tj ET\n";
        using var doc = Tag(Build(content));
        var heading = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Single(e => Text(e).Trim() == "Specific instructions" && e is not SectElement && e is not PartElement);
        var layout = heading.Attributes.GetAttributes(AttributeOwnerStandard.Layout);
        Assert.Equal("Solid", layout.GetAttribute(AttributeKey.BorderStyle)?.GetNameValue()?.ToString());
        Assert.Equal(new double?[] { 2, 0, 0, 0 }, layout.GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue());
        Assert.InRange(layout.GetAttribute(AttributeKey.Padding)!.GetArrayNumberValue()![0]!.Value, 1, 10);
    }

    [Fact]
    public void AnUnderlineOfAParagraphsLastLineOpensNoParagraphUnderIt()
    {
        // A long underline under most of a paragraph's last line (a link's), and a paragraph after a gap.
        var content = Prose + "BT /F1 10 Tf 72 660 Td (For more, see the notes of the survey of the northern valley and its trails.) Tj ET\n"
                      + "0.5 w 130 658.5 m 400 658.5 l S\n"
                      + "BT /F1 10 Tf 72 640 Td (The next paragraph begins here, after the gap, with its own words.) Tj ET\n";
        using var doc = Tag(Build(content));
        var next = doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Single(p => Text(p).StartsWith("The next paragraph"));
        Assert.Null(next.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderStyle));
    }

    [Fact]
    public void ARuleInTheNextColumnOverClosesNoParagraphLevelWithIt()
    {
        // A note in the left column, and a rule level with its foot in the right one.
        var content = Prose + "BT /F1 10 Tf 72 660 Td (Not seasonally adjusted.) Tj ET\n"
                      + "0.5 w 320 654 m 540 654 l S\n"
                      + "BT /F1 10 Tf 72 600 Td (The next section begins here, after the rule, with its own words.) Tj ET\n";
        using var doc = Tag(Build(content));
        var paragraphs = doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true);
        string? Style(string start) => paragraphs.Single(p => Text(p).StartsWith(start)).Attributes
            .GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderStyle)?.GetNameValue()?.ToString();
        Assert.Null(Style("Not seasonally"));
    }

    [Fact]
    public void ARowOfFiguresWrittenAsOneStringOneSpaceApartIsCutIntoItsCells()
    {
        // Tight columns: each row's figures one string, a single space between them (Courier: 30 pt a figure).
        string Tight(double y, string label, string[] figures)
            => $"BT /F1 10 Tf 72 {y} Td ({label}) Tj ET\nBT /F1 10 Tf 200 {y} Td ({string.Join(" ", figures)}) Tj ET\n";
        var content = Prose + string.Concat(Enumerable.Range(0, 4).Select(r => Tight(660 - 14 * r, Labels[r], Figures[r])));
        using var doc = Tag(Build(content));
        var rows = Cells(doc);
        Assert.Equal(4, rows.Count);
        for (var r = 0; r < 4; r++)
            Assert.Equal(new[] { Labels[r] }.Concat(Figures[r]), rows[r]);
    }

    [Fact]
    public void TwoFiguresWrittenAsOneStringInARowOfSingleFiguresAreTwoCells()
    {
        // Each figure its own string in columns 30 pt apart, but for one row whose third and fourth figures
        // are one string, a space between them (Courier: 6 pt a character).
        string Figure(double x, double y, string text) => $"BT /F1 10 Tf {x} {y} Td ({text}) Tj ET\n";
        string Spaced(int r) => Figure(72, 660 - 14 * r, Labels[r])
            + (r == 2
                ? Figure(200, 660 - 14 * r, Figures[r][0]) + Figure(230, 660 - 14 * r, Figures[r][1])
                  + Figure(260, 660 - 14 * r, Figures[r][2] + " " + Figures[r][3])
                : string.Concat(Enumerable.Range(0, 4).Select(c => Figure(200 + 30 * c, 660 - 14 * r, Figures[r][c]))));
        using var doc = Tag(Build(Prose + string.Concat(Enumerable.Range(0, 4).Select(Spaced))));
        var rows = Cells(doc);
        Assert.Equal(4, rows.Count);
        for (var r = 0; r < 4; r++)
            Assert.Equal(new[] { Labels[r] }.Concat(Figures[r]), rows[r]);
    }

    // Six columns of figures set flush right, 50 pt apart, ending at x 250, 300, ... 500.
    private static string FlushRight(double y, string label, string[] cells)
        => $"BT /F1 10 Tf 72 {y} Td ({label}) Tj ET\n"
           + string.Concat(cells.Select((t, c) => $"BT /F1 10 Tf {250 + 50 * c - 6 * t.Length} {y} Td ({t}) Tj ET\n"));

    private static readonly string[][] Ragged =
    {
        new[] { "5.7", "15.4", "125.3", "-6.0", "7.7", "12.9" },
        new[] { "115.1", "-2.4", "3.8", "44.6", "-105.5", "0.9" },
        new[] { "6.2", "23.0", "-17.1", "8.8", "19.3", "140.2" },
        new[] { "70.8", "5.4", "9.9", "-12.6", "3.1", "66.4" },
    };

    // Two years over their quarters, each centred over its three columns with a straddle rule under it.
    private static string StraddledTable()
        => Prose
           + "BT /F1 10 Tf 270 666 Td (2025) Tj ET\nBT /F1 10 Tf 420 666 Td (2026) Tj ET\n"
           + "0.5 w 214 662 m 350 662 l S 364 662 m 500 662 l S\n"
           + FlushRight(650, "", new[] { "Q2", "Q3", "Q4", "Q1", "Q2", "Q3" })
           + string.Concat(Enumerable.Range(0, 4).Select(r => FlushRight(630 - 14 * r, Labels[r], Ragged[r])));

    private static string? Align(StructureElement cell) =>
        cell.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString();

    private static int Span(StructureElement cell) =>
        (int)(cell.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.ColSpan)?.GetNumberValue() ?? 1);

    [Fact]
    public void AHeadingOverAGroupOfColumnsSpansTheColumnsItsStraddleRuleRunsOver()
    {
        using var doc = Tag(Build(StraddledTable()));
        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Table");
        var top = table.FindElements<StructureElement>(true).First(e => e.S.Name == "TR")
            .ChildElements.OfType<StructureElement>().Where(c => c.S.Name is "TD" or "TH").ToList();
        var groups = top.Where(c => Text(c).Length > 0).Select(c => (Text(c), Span(c), Align(c))).ToList();
        Assert.Equal(new[] { ("2025", 3, (string?)"Center"), ("2026", 3, "Center") }, groups);
        // The label's column and the columns before the groups stay single cells.
        Assert.Equal(7, top.Sum(Span));
    }

    [Fact]
    public void FiguresSetFlushRightInTheirColumnsAreAlignedToTheEnd()
    {
        using var doc = Tag(Build(StraddledTable()));
        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Table");
        var rows = table.FindElements<StructureElement>(true).Where(e => e.S.Name == "TR")
            .Select(r => r.ChildElements.OfType<StructureElement>().Where(c => c.S.Name is "TD" or "TH").ToList()).ToList();
        var total = rows.First(r => Text(r[0]) == "Total");
        Assert.Null(Align(total[0]));
        Assert.All(total.Skip(1), c => Assert.Equal("End", Align(c)));
        // The quarters heading the figures' columns stand flush right over them too.
        var quarters = rows.First(r => r.Any(c => Text(c) == "Q4"));
        Assert.All(quarters.Where(c => Text(c).Length > 0), c => Assert.Equal("End", Align(c)));
    }

    [Fact]
    public void ColumnsOfFiguresSetFlushRightAreAsWideAsTheirFiguresStandApart()
    {
        // The last column "n.a." all down: as wide a figure in every row, set as the other columns are.
        var content = Prose + string.Concat(Enumerable.Range(0, 4).Select(r =>
            FlushRight(630 - 14 * r, Labels[r], Ragged[r].Take(5).Append("n.a.").ToArray())));
        using var doc = Tag(Build(content));
        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Table");
        var total = table.FindElements<StructureElement>(true).First(e => e.S.Name == "TR" && Text(e).StartsWith("Total"))
            .ChildElements.OfType<StructureElement>().Where(c => c.S.Name is "TD" or "TH").ToList();
        Assert.Equal("End", Align(total[^1]));
        // Each column after the first of figures ends where its figures do, 50 pt after the one before it.
        var widths = total.Skip(2).Select(c => c.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.Width)!.GetNumberValue()!.Value).ToList();
        Assert.All(widths.Take(widths.Count - 1), w => Assert.InRange(w, 49.5, 50.5));
    }

    [Fact]
    public void RevisionMarksInFullSizeStayWithTheirHeadings()
    {
        var headings = Row(680, "Year", new[] { "2021", "2022", "2023", "2024" });
        // An "r" touching the end of 2022 (x 278) and of 2023 (x 332), raised 6 pt.
        var marks = "BT /F1 10 Tf 278 686 Td (r) Tj ET\nBT /F1 10 Tf 332 686 Td (r) Tj ET\n";
        var content = Prose + headings + string.Concat(Enumerable.Range(0, 4).Select(r => Row(660 - 14 * r, Labels[r], Figures[r])));
        using var doc = Tag(Build(content + marks));
        var rows = Cells(doc);
        Assert.Equal(5, rows.Count);
        Assert.Equal(5, rows[0].Count);
        Assert.DoesNotContain(rows, r => r.All(c => c is "" or "r"));
    }
}
