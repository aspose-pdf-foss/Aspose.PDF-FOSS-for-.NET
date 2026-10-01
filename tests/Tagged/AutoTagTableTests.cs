using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of tables beyond a plain grid (merged cells, tables going on over a
/// page break) and the read of the running headers and footers it marks as artifacts.</summary>
public class AutoTagTableTests
{
    /// <summary>An untagged document of A4-wide pages drawing <paramref name="pages"/> (F1
    /// Helvetica, F2 Helvetica-Bold).</summary>
    private static byte[] Build(params string[] pages) => BuildPages(pages.Select(p => (p, "")).ToArray());

    /// <summary>As <see cref="Build"/>, each page's dictionary also holding its <c>Extra</c> entries
    /// (a /Rotate, /Annots).</summary>
    private static byte[] BuildPages(params (string Content, string Extra)[] pageSpecs)
    {
        var pages = pageSpecs.Select(p => p.Content).ToArray();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(" ", pages.Select((_, i) => $"{5 + 2 * i} 0 R")) + $"] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        for (var i = 0; i < pages.Length; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {6 + 2 * i} 0 R " +
                        "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> " + pageSpecs[i].Extra + " >>");
            objects.Add($"<< /Length {pages[i].Length} >>\nstream\n{pages[i]}\nendstream");
        }
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
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

    /// <summary>A framed cell from (<paramref name="x0"/>, <paramref name="top"/>) to
    /// (<paramref name="x1"/>, <paramref name="bottom"/>) showing <paramref name="text"/>.</summary>
    private static string Cell(double x0, double top, double x1, double bottom, string text, string font = "F1")
        => $"0.75 w {x0} {bottom} {x1 - x0} {top - bottom} re S\nBT /{font} 10 Tf {x0 + 4} {top - 14} Td ({text}) Tj ET\n";

    private static string Line(double x, double y, string text, string font = "F1", int size = 10)
        => $"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = AutoTaggingSettings.Default,
        });
        return doc;
    }

    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent().Select(i => i.Text)).Trim();

    /// <summary>A worksheet's entry from <paramref name="y"/> up, <paramref name="height"/> high: its bold number at 46 on
    /// its first line, its lines at 60 (the last running on in leader dots to near the boxes), and its boxes ruled round from
    /// <paramref name="boxes"/> to the frame's side - the first holding the entry's number on its last line;
    /// <paramref name="shaded"/> fills the last box grey.</summary>
    private static string Entry(double y, double height, string number, string[] lines, double[] boxes, bool shaded = false)
    {
        var sb = new StringBuilder(Line(46, y + height - 10, number + ".", "F2", 8));
        // The last line runs on in leader dots (2.224 pt each at 8 pt) to 4 pt short of the first box.
        var helvetica = Aspose.Pdf.Text.FontRepository.FindFont("Helvetica");
        for (var k = 0; k < lines.Length; k++)
        {
            var text = lines[k];
            if (k == lines.Length - 1)
                text += " " + new string('.', (int)((boxes[0] - 4 - 60 - helvetica.MeasureString(text + " ", 8)) / 2.224));
            sb.Append(Line(60, y + height - 10 - 10 * k, text, "F1", 8));
        }
        if (shaded) sb.Append($"0.9 g {boxes[^2]} {y} {boxes[^1] - boxes[^2]} {height} re f 0 g\n");
        for (var k = 0; k + 1 < boxes.Length; k++) sb.Append($"0.5 w {boxes[k]} {y} {boxes[k + 1] - boxes[k]} {height} re S\n");
        return sb.Append(Line(boxes[0] + 3, y + 4, number, "F2", 8)).ToString();
    }

    [Fact]
    public void AWorksheetsEntriesRuledOnlyInTheirBoxesAreRowsSetFromTheLeftWithTheirShadesStated()
    {
        // A framed worksheet: a heading band ruled under; three entries of two lines boxed at 501-513 and 513-570; a ruled
        // subheading with a grey box beside it (449-570, no rule round it); two entries boxed at 449-460, 460-513 and a grey
        // 513-570.
        var frame = "0.5 w 42 564 528 136 re S 42 684 m 570 684 l S 42 592 m 570 592 l S\n" + Line(46, 689, "Step 1. Enter the finds made during the survey.", "F2", 8);
        double[] wide = [501, 513, 570], narrow = [449, 460, 513, 570];
        var first = Entry(660, 24, "1", ["Enter the flints found along the valley floor, and the", "blades found beside them"], wide)
                    + Entry(636, 24, "2", ["Enter the sherds found by the ford, and the coarse", "pottery found at the camp"], wide)
                    + Entry(612, 24, "3", ["Enter the bones found under the cairn, and the", "hearth stones found near it"], wide);
        var sub = "0.9 g 449 592 121 20 re f 0 g\n" + Line(60, 598, "Finds counted twice", "F2", 8);
        var second = Entry(578, 14, "4", ["Multiply line 3 by 0.5"], narrow, shaded: true) + Entry(564, 14, "5", ["Subtract line 4 from line 3"], narrow, shaded: true);
        var doc = Tag(Build(Line(42, 760, "Worksheet 1. The finds of the survey", "F2", 12) + frame + first + sub + second));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var cells = table.FindElements<StructureElement>(true).Where(e => e.StructureType?.Tag is "TD" or "TH").ToList();
        // Each entry is a row of its own, its text set from the left.
        var one = Assert.Single(cells, c => TextOf(c).StartsWith("1."));
        Assert.DoesNotContain("2.", TextOf(one));
        Assert.Null(one.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign));
        // ... and an entry of one line too, the entry under it no part of its cell (no rule parts them in the text column).
        Assert.DoesNotContain("5.", TextOf(Assert.Single(cells, c => TextOf(c).StartsWith("4."))));
        // ... across the columns no rule parts in its row (those the boxes under the subheading rule).
        Assert.Equal(3, one.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.ColSpan)?.GetNumberValue());
        // The grey boxes are shaded: the entries' last boxes and the box beside the subheading.
        var shaded = cells.Where(c => c.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BackgroundColor) is not null).ToList();
        Assert.True(shaded.Count >= 3, $"shaded cells: {shaded.Count}");
        Assert.DoesNotContain(shaded, c => TextOf(c).Contains("Finds counted"));
    }

    [Fact]
    public void AGreyBoxBesideASubheadingUnderAWhiteGapIsShadedInItsRowTheGapARowOfItsOwn()
    {
        // A framed worksheet: two entries boxed at 449-460, 460-513, 513-570, a rule across under them at 592; then a white
        // gap of 14.6 pt and a subheading of two lines beside a grey box (449-570, 555.4-577.4, no rule round it) whose top
        // runs through the subheading's first line box; two more entries under it.
        double[] narrow = [449, 460, 513, 570];
        var frame = "0.5 w 42 527.4 528 172.6 re S 42 620 m 570 620 l S 42 592 m 570 592 l S\n" + Line(46, 689, "Step 1. Enter the finds made during the survey.", "F2", 8);
        var first = Entry(606, 14, "1", ["Multiply line 3 by 0.5"], narrow) + Entry(592, 14, "2", ["Subtract line 1 from line 3"], narrow);
        var sub = "0.9 g 449 555.4 121 22 re f 0 g\n" + Line(60, 570, "Finds counted twice or more", "F2", 8)
                  + Line(60, 560, "(If line 2 is zero, enter -0- on lines 3 and 4.)", "F1", 8);
        var second = Entry(541.4, 14, "3", ["Enter the amount from line 1"], narrow) + Entry(527.4, 14, "4", ["Add lines 2 and 3"], narrow);
        var doc = Tag(Build(Line(42, 760, "Worksheet 2. The finds counted twice", "F2", 12) + frame + first + sub + second));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var cells = table.FindElements<StructureElement>(true).Where(e => e.StructureType?.Tag is "TD" or "TH").ToList();
        // The grey box is a cell of its own, shaded, in the subheading's row; the white gap above them is a row with no text.
        var shaded = Assert.Single(cells, c => c.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BackgroundColor) is not null);
        Assert.Equal("", TextOf(shaded));
        var rows = Rows(table);
        var at = rows.FindIndex(r => r.Any(c => TextOf(c).StartsWith("Finds counted")));
        Assert.Contains(shaded, rows[at]);
        Assert.All(rows[at - 1], c => Assert.Equal("", TextOf(c)));
    }

    private static double? Layout(StructureElement el, AttributeKey key)
        => el.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(key)?.GetNumberValue();

    private static List<List<StructureElement>> Rows(StructureElement table)
        => table.FindElements<TableTRElement>(true).Cast<StructureElement>()
            .Select(tr => tr.ChildElements.OfType<StructureElement>().Where(c => c is TableTDElement or TableTHElement).ToList())
            .ToList();

    private static int Span(StructureElement cell, AttributeKey key)
        => (int)(cell.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(key)?.GetNumberValue() ?? 1);

    [Fact]
    public void CellsWithNoRuleBetweenThemAreOneCellThatStatesItsSpans()
    {
        // Region | Sales (over two columns)
        // North (over two rows) | 12 | 14
        //                       |  7 |  8
        var page = Cell(72, 700, 172, 680, "Region") + Cell(172, 700, 372, 680, "Sales")
                   + Cell(72, 680, 172, 640, "North") + Cell(172, 680, 272, 660, "12") + Cell(272, 680, 372, 660, "14")
                   + Cell(172, 660, 272, 640, "7") + Cell(272, 660, 372, 640, "8");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["Region", "Sales"], rows[0].Select(TextOf));
        Assert.Equal(2, Span(rows[0][1], AttributeKey.ColSpan));
        Assert.Equal(["North", "12", "14"], rows[1].Select(TextOf));
        Assert.Equal(2, Span(rows[1][0], AttributeKey.RowSpan));
        Assert.Equal(1, Span(rows[1][1], AttributeKey.RowSpan));
        Assert.Equal(["7", "8"], rows[2].Select(TextOf));
    }

    [Fact]
    public void AFramedTableWithARuleUnderItsHeadIsATableWhereHeadingsElsewhereOnThePageAreRuledToo()
    {
        // Headings with a rule under each, far above and below; between them a frame of three columns of text,
        // ruled only round it and under its head, whose last heading is of two lines; its rows are numbered.
        string Prose(double top, int lines) => string.Concat(Enumerable.Range(0, lines).Select(i =>
            Line(72, top - 12 * i, "The survey went on over the hills and along the river to the ford")));
        var frame = "0.5 w 72 560 m 372 560 l S\n72 539 m 372 539 l S\n72 485 m 372 485 l S\n72 485 m 72 560 l S\n372 485 m 372 560 l S\n";
        var head = Line(76, 543, "The quarter includes...", "F2", 7) + Line(212, 543, "Quarter ends", "F2", 7)
                   + Line(296, 552, "Return", "F2", 7) + Line(296, 543, "is due", "F2", 7);
        var rows = string.Concat(new[] { ("January, February, March", "March 31", "April 30"), ("April, May, June", "June 30", "July 31"),
            ("July, August, September", "September 30", "October 31"), ("October, November, December", "December 31", "January 31") }
            .Select((r, i) => Line(76, 528 - 12 * i, $"{i + 1}.", "F2", 7) + Line(84, 528 - 12 * i, r.Item1, size: 7) + Line(212, 528 - 12 * i, r.Item2, size: 7) + Line(296, 528 - 12 * i, r.Item3, size: 7)));
        var page = Line(72, 740, "When to file", "F2", 14) + "0.5 w 72 734 m 372 734 l S\n" + Prose(716, 10)
                   + frame + head + rows + Prose(466, 6)
                   + Line(72, 370, "How to file", "F2", 14) + "0.5 w 72 364 m 372 364 l S\n" + Prose(346, 12)
                   + Line(72, 180, "Where to file", "F2", 14) + "0.5 w 72 174 m 372 174 l S\n" + Prose(156, 6);
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var cells = Rows(table);
        Assert.Equal(5, cells.Count);
        Assert.Equal(["The quarter includes...", "Quarter ends", "Return is due"], cells[0].Select(c => TextOf(c).Replace("  ", " ")));
        Assert.Equal(["2. April, May, June", "June 30", "July 31"], cells[2].Select(c => TextOf(c).Replace("  ", " ")));
        // Only what is drawn is a border: the frame, and the rule under the head (before, after, start, end).
        double?[] Borders(StructureElement cell) => cell.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue() ?? [0, 0, 0, 0];
        Assert.Equal([0.5, 0.5, 0, 0], Borders(cells[0][1]));
        Assert.Equal([0, 0, 0, 0], Borders(cells[2][1]));
        Assert.Equal([0, 0.5, 0, 0.5], Borders(cells[4][2]));
        // A column starts as far before its text as the first does before its own: 4 pt.
        Assert.InRange(Layout(cells[1][0], AttributeKey.Width) ?? 0, 135, 137);
        Assert.InRange(Layout(cells[1][1], AttributeKey.Width) ?? 0, 83, 85);
    }

    [Fact]
    public void RulesBetweenTheRowsDrawnColumnByColumnAreATableItsHeadOverTheFirstAndItsLastRowUnderTheLast()
    {
        // No rule down the page: each rule between two rows is drawn in three pieces, one a column. The head stands
        // over the first rule, the last row under the last one; the cells hold several lines of small type.
        string Rule(double y) => $"0.5 w 72 {y} m 200 {y} l S\n200 {y} m 262 {y} l S\n262 {y} m 328 {y} l S\n";
        string Cell(double x, double top, params string[] lines) => string.Concat(lines.Select((l, i) => Line(x, top - 10 * i, l, size: 8)));
        var head = Line(76, 576, "If you are in...", "F2", 8) + Cell(204, 576, "Without a", "payment...") + Cell(266, 576, "With a", "payment...");
        var first = Cell(76, 547, "Connecticut, Delaware,", "Georgia, Illinois, Indiana,", "Kentucky, Maine, Maryland,", "Ohio, Vermont, Virginia")
                    + Cell(204, 547, "Department of", "the Treasury", "Kansas City") + Cell(266, 547, "Revenue", "Service", "Box 932100", "Louisville");
        var second = Cell(76, 487, "Alabama, Alaska, Arizona,", "Florida, Hawaii, Idaho,", "Utah, Washington")
                     + Cell(204, 487, "Department of", "the Treasury", "Ogden") + Cell(266, 487, "Revenue", "Service", "Box 932100", "Louisville");
        var last = Cell(76, 427, "No legal residence or", "place of business in", "any state") + Cell(204, 427, "Revenue", "Service", "Ogden")
                   + Cell(266, 427, "Revenue", "Service", "Louisville");
        string Prose(double top, int lines) => string.Concat(Enumerable.Range(0, lines).Select(i =>
            Line(72, top - 12 * i, "The survey went on over the hills and along the river")));
        var page = Prose(700, 8) + Line(72, 596, "Mailing addresses", "F2", 11) + head + Rule(560) + first + Rule(500) + second + Rule(440) + last
                   + Prose(370, 6);
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var cells = Rows(table);
        Assert.Equal(4, cells.Count);
        Assert.All(cells, row => Assert.Equal(3, row.Count));
        string Words(StructureElement cell) => string.Join(" ", TextOf(cell).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["If you are in...", "Without a payment...", "With a payment..."], cells[0].Select(Words));
        Assert.Equal("Department of the Treasury Kansas City", Words(cells[1][1]));
        Assert.Equal(["No legal residence or place of business in any state", "Revenue Service Ogden", "Revenue Service Louisville"], cells[3].Select(Words));
        // The rules drawn are the borders: under the head and between the rows, none down the table or round it.
        double?[] Borders(StructureElement cell) => cell.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue() ?? [0, 0, 0, 0];
        Assert.Equal([0, 0.5, 0, 0], Borders(cells[0][1]));
        Assert.Equal([0.5, 0.5, 0, 0], Borders(cells[1][1]));
        Assert.Equal([0.5, 0, 0, 0], Borders(cells[3][2]));
        // The cells of a row, of different heights, all start as far under its top edge: they are set from the top.
        Assert.All(cells.SelectMany(row => row), cell => Assert.Equal("Before", cell.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.BlockAlign)?.GetNameValue()?.ToString()));
    }

    [Fact]
    public void TwoRuledTablesOnAPageWithACaptionBetweenThemAreTwoTables()
    {
        var page = Cell(72, 700, 172, 680, "amber") + Cell(172, 700, 272, 680, "10")
                   + Cell(72, 680, 172, 660, "basalt") + Cell(172, 680, 272, 660, "17")
                   + Line(72, 644, "Table 2: Metals")
                   + Cell(72, 630, 222, 610, "copper") + Cell(222, 630, 372, 610, "29")
                   + Cell(72, 610, 222, 590, "iron") + Cell(222, 610, 372, 590, "26");
        using var doc = Tag(Build(page));

        var root = doc.TaggedContent.StructTreeRootElement;
        var tables = root.FindElements<TableElement>(true).Cast<StructureElement>().ToList();
        Assert.Equal(2, tables.Count);
        Assert.Equal(["amber", "basalt"], Rows(tables[0]).Select(r => TextOf(r[0])));
        Assert.Equal(["copper", "iron"], Rows(tables[1]).Select(r => TextOf(r[0])));
        Assert.Contains(root.FindElements<ParagraphElement>(true).Cast<StructureElement>(), p => TextOf(p) == "Table 2: Metals");
    }

    [Fact]
    public void ARuledGridOfSingleCellsStatesNoSpans()
    {
        var page = Cell(72, 700, 172, 680, "Name") + Cell(172, 700, 272, 680, "Kind")
                   + Cell(72, 680, 172, 660, "amber") + Cell(172, 680, 272, 660, "stone");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        Assert.All(Rows(table).SelectMany(r => r), c =>
        {
            Assert.Null(c.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.RowSpan));
            Assert.Null(c.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.ColSpan));
        });
    }

    [Fact]
    public void ACellOfARuledTableStatesHowItsTextIsSetAndTheRoomRoundIt()
    {
        // Rows 30 pt tall: the left cells hold two lines, the right ones one line level with the left's first -
        // set from the top, 6 pt under the rule. The right column's figures stand midway across it, its heading
        // at its left.
        static string Box(double x0, double top, double x1, double bottom) => $"0.75 w {x0} {bottom} {x1 - x0} {top - bottom} re S\n";
        var page = Box(72, 700, 272, 670) + Box(272, 700, 472, 670) + Line(76, 686, "Month of the year") + Line(76, 674, "it was first used")
                   + Line(276, 686, "Share")
                   + Box(72, 670, 272, 640) + Box(272, 670, 472, 640) + Line(76, 656, "January, after the") + Line(76, 644, "first day of it")
                   + Line(360, 656, "2.461")
                   + Box(72, 640, 272, 610) + Box(272, 640, 472, 610) + Line(76, 626, "February, after the") + Line(76, 614, "first day of it")
                   + Line(360, 626, "2.247");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var cells = Rows(table).SelectMany(r => r).ToList();
        Assert.All(cells, c => Assert.Equal("Before", c.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.BlockAlign)?.GetNameValue()?.ToString()));
        var figure = cells.Single(c => TextOf(c) == "2.461").Attributes.GetAttributes(AttributeOwnerStandard.Layout);
        Assert.Equal("Center", figure.GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
        var padding = figure.GetAttribute(AttributeKey.Padding)!.GetArrayNumberValue()!;
        Assert.InRange(padding[0]!.Value, 4, 8);
        Assert.InRange(padding[1]!.Value, 12, 18);
        var heading = cells.Single(c => TextOf(c) == "Share").Attributes.GetAttributes(AttributeOwnerStandard.Layout);
        Assert.Null(heading.GetAttribute(AttributeKey.TextAlign));
    }

    [Fact]
    public void TheWidestCellOfAColumnOfCentredCellsIsCentredAsTheOthersAre()
    {
        // The right column's heading and figures all stand midway across it, the heading the widest of them; the
        // left column's cells all start at its left.
        static string Box(double x0, double top, double x1, double bottom) => $"0.75 w {x0} {bottom} {x1 - x0} {top - bottom} re S\n";
        static string Midway(double y, string text)
            => Line(Math.Round(272 + (200 - Aspose.Pdf.Text.FontRepository.FindFont("Helvetica").MeasureString(text, 10)) / 2, 1), y, text);
        var page = Box(72, 700, 272, 680) + Box(272, 700, 472, 680) + Line(76, 686, "Kind of filer") + Midway(686, "Total time in hours")
                   + Box(72, 680, 272, 660) + Box(272, 680, 472, 660) + Line(76, 666, "Small") + Midway(666, "62")
                   + Box(72, 660, 272, 640) + Box(272, 660, 472, 640) + Line(76, 646, "Large") + Midway(646, "1,240");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var cells = Rows(table).SelectMany(r => r).ToList();
        string? Align(string text) => cells.Single(c => TextOf(c) == text).Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString();
        Assert.Equal("Center", Align("Total time in hours"));
        Assert.Equal("Center", Align("62"));
        Assert.Equal("Center", Align("1,240"));
        Assert.Null(Align("Kind of filer"));
        Assert.Null(Align("Small"));
    }

    private const string FullLine = "The survey went on over the hills and along the river, past the old mill and the ford, until";

    [Fact]
    public void AParagraphOpeningAPageItsFirstLineSetInGoesOnWithNoneOfThePageBefore()
    {
        // The page before ends at the foot of its right column, on a full line closing a sentence; the next opens
        // at the left with a line set in from its second.
        const string part = "and submit it with the form of the quarter when you";
        var before = Line(315, 100, part) + Line(315, 88, part) + Line(315, 76, "pay what the form of the quarter shows that you owe.");
        var after = Line(84, 760, "Do not change the figure you entered on the form") + Line(72, 748, "by what you report on any later form of the year, and keep")
                    + Line(72, 736, "the papers of the season with the rest.");
        using var doc = Tag(Build(before, after));

        var paragraphs = doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>().ToList();
        Assert.StartsWith("Do not change", TextOf(Assert.Single(paragraphs, p => TextOf(p).Contains("Do not change"))));
    }

    [Fact]
    public void ASumOpeningAPageGoesOnWithNoParagraphOfThePageBefore()
    {
        // The page before ends on a full line ending no sentence; the next opens with a sum, its first line in
        // lower case, and goes on with text.
        var before = Line(72, 100, FullLine) + Line(72, 88, FullLine) + Line(72, 76, FullLine);
        var sum = Line(264, 760, "line 5d  \\(column 1\\)", size: 9) + Line(244, 748, "x      0.009", size: 9) + "0.5 w 244 746 m 294 746 l S\n"
                  + Line(264, 736, "line 5d  \\(column 2\\)", size: 9);
        var after = sum + Line(72, 700, FullLine) + Line(72, 688, FullLine) + Line(72, 676, "the light went.");
        using var doc = Tag(Build(before, after));
        var root = doc.TaggedContent.StructTreeRootElement;

        var formula = Assert.Single(root.FindElements<FormulaElement>(true));
        Assert.Contains("0.009", string.Concat(formula.GetMarkedContent(true).Select(i => i.Text)));
        Assert.DoesNotContain(root.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => string.Concat(p.GetMarkedContent(true).Select(i => i.Text)).Contains("line 5d"));
    }

    [Fact]
    public void ATableStatesWhereItStands()
    {
        var page = Cell(72, 700, 172, 680, "Name") + Cell(172, 700, 272, 680, "Kind")
                   + Cell(72, 680, 172, 660, "amber") + Cell(172, 680, 272, 660, "stone");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var box = table.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BBox)?.GetArrayNumberValue();
        Assert.NotNull(box);
        Assert.Equal(new double?[] { 72, 660, 272, 700 }, box!.Select(v => v.HasValue ? Math.Round(v.Value) : (double?)null));
    }

    /// <summary>A ruled table: its header row in bold, then <paramref name="rows"/> from
    /// <paramref name="top"/> down.</summary>
    private static string RuledTable(double top, IEnumerable<(string Item, string Weight)> rows, bool header)
    {
        var sb = new StringBuilder();
        var y = top;
        if (header)
        {
            sb.Append(Cell(72, y, 272, y - 20, "Item", "F2")).Append(Cell(272, y, 372, y - 20, "Weight", "F2"));
            y -= 20;
        }
        foreach (var (item, weight) in rows)
        {
            sb.Append(Cell(72, y, 272, y - 20, item)).Append(Cell(272, y, 372, y - 20, weight));
            y -= 20;
        }
        return sb.ToString();
    }

    [Fact]
    public void ATableGoingOnOverAPageBreakIsOneTableAndItsRepeatedHeaderIsNoContent()
    {
        var first = Line(72, 760, "The stones we weighed are listed below.") +
                    RuledTable(180, [("amber", "10"), ("basalt", "17"), ("cedar", "24")], header: true);
        var second = RuledTable(780, [("delta", "31"), ("ember", "38")], header: true) +
                     Line(72, 600, "That is every stone of the survey.");
        using var doc = Tag(Build(first, second));

        var root = doc.TaggedContent.StructTreeRootElement;
        var table = Assert.Single(root.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(["Item", "Weight"], rows[0].Select(TextOf));
        Assert.All(rows[0], c => Assert.IsType<TableTHElement>(c));
        Assert.Equal(["amber", "basalt", "cedar", "delta", "ember"], rows.Skip(1).Select(r => TextOf(r[0])));
        Assert.All(rows.Skip(1).SelectMany(r => r), c => Assert.IsType<TableTDElement>(c));
    }

    [Fact]
    public void AnUnruledTablesLastRowAloneOnThePageAfterStaysInTheTable()
    {
        static string Row(double y, string item, string weight) => Line(72, y, item) + Line(272, y, weight);
        var first = Line(72, 760, "The stones we weighed are listed below.") +
                    Row(120, "amber", "10") + Row(106, "basalt", "17") + Row(92, "cedar", "24");
        var second = Row(780, "delta", "31") +
                     Line(72, 750, "That is every stone of the survey, weighed on the morning of the last day of the dig.");
        using var doc = Tag(Build(first, second));

        var root = doc.TaggedContent.StructTreeRootElement;
        var table = Assert.Single(root.FindElements<TableElement>(true));
        Assert.Equal(["amber", "basalt", "cedar", "delta"], Rows(table).Select(r => TextOf(r[0])));
        Assert.Contains(root.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => TextOf(p).StartsWith("That is every stone", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RunningHeadersAndFootersReadAsPaginationArtifacts()
    {
        string Page(int n) => Line(72, 810, "Field notes", size: 9) + Line(290, 30, $"Page {n}", size: 9) +
                              Line(72, 700, $"The survey went on over the hills on day {n} of the dig.");
        using var doc = Tag(Build(Page(1), Page(2)));

        var items = doc.Pages[2].GetArtifactContent();
        var header = Assert.Single(items, i => i.ArtifactSubtype == Artifact.ArtifactSubtype.Header);
        Assert.Equal("Field notes", header.Text.Trim());
        Assert.Equal(Artifact.ArtifactType.Pagination, header.ArtifactType);
        Assert.Null(header.Element);
        Assert.Equal(9f, header.FontSize, 3);
        var footer = Assert.Single(items, i => i.ArtifactSubtype == Artifact.ArtifactSubtype.Footer);
        Assert.Equal("Page 2", footer.Text.Trim());
        // The body text is structure content, not an artifact.
        Assert.DoesNotContain(items, i => i.Text.Contains("survey"));
    }

    [Fact]
    public void ArtifactContentStatesTheArtifactsTypeAndSplitsAtAWideGap()
    {
        var page = "/Artifact <</Type /Pagination /Subtype /Footer>> BDC\n" +
                   Line(72, 30, "Annual report") + Line(480, 30, "Page 3") + "EMC\n" +
                   "/Artifact BMC\n" + Line(72, 400, "Draft") + "EMC\n" +
                   Line(72, 700, "Body text.");
        using var doc = new Document(new MemoryStream(Build(page)));

        var items = doc.Pages[1].GetArtifactContent();
        Assert.Equal(["Annual report", "Page 3", "Draft"], items.Select(i => i.Text.Trim()));
        Assert.All(items.Take(2), i =>
        {
            Assert.Equal(Artifact.ArtifactType.Pagination, i.ArtifactType);
            Assert.Equal(Artifact.ArtifactSubtype.Footer, i.ArtifactSubtype);
        });
        Assert.Equal(Artifact.ArtifactType.Undefined, items[2].ArtifactType);
        Assert.Equal(Artifact.ArtifactSubtype.Undefined, items[2].ArtifactSubtype);
    }

    [Fact]
    public void ArtifactsDrawnWordByWordKeepTheirSpacesAndTurnedTextStatesItsRotation()
    {
        var page = "/Artifact <</Type /Pagination /Subtype /Header>> BDC\n" + Line(72, 800, "NIST") + "EMC\n" +
                   "/Artifact <</Type /Pagination /Subtype /Header>> BDC\n" + Line(100, 800, "SP") + "EMC\n" +
                   "/Artifact BMC\nBT /F1 9 Tf 0 1 -1 0 20 200 Tm (Available free of charge) Tj ET\nEMC\n";
        using var doc = new Document(new MemoryStream(Build(page)));

        var items = doc.Pages[1].GetArtifactContent();
        Assert.Equal("NIST SP", string.Concat(items.Where(i => i.Rotation == 0).Select(i => i.Text)));
        Assert.Equal(90, Assert.Single(items, i => i.Rotation != 0).Rotation);
    }
    private static string Rule(double x0, double y0, double x1, double y1) => $"0.75 w {x0} {y0} m {x1} {y1} l S\n";

    [Fact]
    public void DataRowsAndColumnsNoRuleSeparatesAreRowsAndColumnsOfTheirOwn()
    {
        // Wage | Amount (its values in two columns, no rule between them)
        // ruled only above and below the three data rows
        var page = Rule(72, 700, 372, 700) + Rule(72, 680, 372, 680) + Rule(72, 620, 372, 620)
                   + Rule(72, 620, 72, 700) + Rule(172, 620, 172, 700) + Rule(372, 620, 372, 700)
                   + Line(80, 686, "Wage", "F2") + Line(180, 686, "Amount", "F2");
        foreach (var (y, wage, a, b) in new[] { (664, "$10", "1", "2"), (648, "$20", "3", "4"), (632, "$30", "5", "6") })
            page += Line(80, y, wage) + Line(190, y, a) + Line(290, y, b);
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(4, rows.Count);
        Assert.Equal(["Wage", "Amount"], rows[0].Select(TextOf));
        Assert.Equal(2, Span(rows[0][1], AttributeKey.ColSpan));
        Assert.Equal(["$10", "1", "2"], rows[1].Select(TextOf));
        Assert.Equal(["$20", "3", "4"], rows[2].Select(TextOf));
        Assert.Equal(["$30", "5", "6"], rows[3].Select(TextOf));
    }

    [Fact]
    public void CellsOfWrappedProseSideBySideStayOneRow()
    {
        var page = Rule(72, 700, 372, 700) + Rule(72, 640, 372, 640)
                   + Rule(72, 640, 72, 700) + Rule(222, 640, 222, 700) + Rule(372, 640, 372, 700)
                   + Line(80, 686, "The first cell runs") + Line(80, 672, "on over three") + Line(80, 658, "lines of prose.")
                   + Line(230, 686, "Another cell also") + Line(230, 672, "wraps its words") + Line(230, 658, "over three lines.");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var row = Assert.Single(Rows(table));
        Assert.Equal(["The first cell runs on over three lines of prose.", "Another cell also wraps its words over three lines."],
            row.Select(TextOf));
    }

    [Fact]
    public void ATableWithNoFrameAtItsSidesHasItsOuterColumns()
    {
        var page = Rule(40, 700, 560, 700) + Rule(40, 680, 560, 680) + Rule(40, 660, 560, 660)
                   + Rule(200, 660, 200, 700) + Rule(380, 660, 380, 700)
                   + Line(50, 686, "Stone") + Line(210, 686, "Weight") + Line(390, 686, "Colour")
                   + Line(50, 666, "amber") + Line(210, 666, "10") + Line(390, 666, "yellow");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(["Stone", "Weight", "Colour"], rows[0].Select(TextOf));
        Assert.Equal(["amber", "10", "yellow"], rows[1].Select(TextOf));
    }

    [Fact]
    public void AColumnRuleDrawnInPiecesStillHoldsTheTableTogether()
    {
        // The middle row's column rules are drawn in two pieces each: the row is no caption band.
        var page = Rule(72, 700, 372, 700) + Rule(72, 680, 372, 680) + Rule(72, 640, 372, 640) + Rule(72, 620, 372, 620);
        foreach (var x in new[] { 72, 222, 372 })
            page += Rule(x, 680, x, 700) + Rule(x, 660, x, 680) + Rule(x, 640, x, 660) + Rule(x, 620, x, 640);
        page += Line(80, 686, "Tax", "F2") + Line(230, 686, "Form", "F2")
                + Line(80, 656, "Income tax") + Line(230, 656, "Schedule C")
                + Line(80, 626, "SE tax") + Line(230, 626, "Schedule SE");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        Assert.Equal(["Tax", "Income tax", "SE tax"], Rows(table).Select(r => TextOf(r[0])));
    }

    [Fact]
    public void ATurnedPageHasItsRunningHeaderAndItsContentTagged()
    {
        // /Rotate 90: the content is drawn turned so that it reads upright on the shown page.
        (string, string) Page(int n) => ("q 0 1 -1 0 595 0 cm\n" + Line(72, 570, "Money Report", size: 9)
            + Line(720, 570, $"Page {n}", size: 9) + Line(72, 400, $"The measures moved on day {n} of the month.") + "Q\n", "/Rotate 90");
        using var doc = Tag(BuildPages(Page(1), Page(2), Page(3)));

        var items = doc.Pages[2].GetArtifactContent();
        Assert.Contains(items, i => i.ArtifactSubtype == Artifact.ArtifactSubtype.Header && i.Text.Contains("Money Report"));
        Assert.DoesNotContain(items, i => i.Text.Contains("measures"));
        Assert.Contains(doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => TextOf(p).Contains("The measures moved on day 2"));
    }

    [Fact]
    public void ALinkWhoseBoxOverhangsTheTextAfterItKeepsItsPlaceInTheParagraph()
    {
        // Citation links whose boxes reach into the bracket after the number.
        static string Link(double x0, double x1) =>
            $"<< /Type /Annot /Subtype /Link /Rect [{x0} 696 {x1} 710] /Border [0 0 0] /A << /S /URI /URI (http://cite.example/{x0}) >> >>";
        var page = Line(72, 700, "Recurrent memory [") + Line(200, 700, "13") + Line(211, 700, "] and gated recurrent [")
                   + Line(330, 700, "7") + Line(336, 700, "] networks.");
        using var doc = Tag(BuildPages((page, $"/Annots [{Link(199, 214)} {Link(329, 340)}]")));

        var paragraph = doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>()
            .Single(p => TextOf(p).Contains("networks"));
        var text = TextOf(paragraph);
        var order = new[] { "memory", "13", "and gated", "7", "networks" }.Select(s => text.IndexOf(s, System.StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }
    [Fact]
    public void ATableSetInTwoHalvesSideBySideReadsAsOneTableLeftHalfFirst()
    {
        // Income | Credit || Income | Credit - the right half goes on from the left one.
        var page = "";
        foreach (var x0 in new[] { 72, 312 })
        {
            page += Rule(x0, 700, x0 + 200, 700) + Rule(x0, 680, x0 + 200, 680) + Rule(x0, 620, x0 + 200, 620)
                    + Rule(x0, 620, x0, 700) + Rule(x0 + 100, 620, x0 + 100, 700) + Rule(x0 + 200, 620, x0 + 200, 700)
                    + Line(x0 + 8, 686, "Income", "F2") + Line(x0 + 108, 686, "Credit", "F2");
        }
        page += Rule(272, 700, 312, 700) + Rule(272, 680, 312, 680) + Rule(272, 620, 312, 620);
        var values = new[] { ("100", "5"), ("200", "9"), ("300", "14"), ("400", "18"), ("500", "23"), ("600", "27") };
        for (var k = 0; k < 6; k++)
        {
            var x0 = k < 3 ? 72 : 312;
            var y = 664 - 16 * (k % 3);
            page += Line(x0 + 8, y, values[k].Item1) + Line(x0 + 108, y, values[k].Item2);
        }
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(["Income", "100", "200", "300", "400", "500", "600"], rows.Select(r => TextOf(r[0])));
        Assert.All(rows.Skip(1), r => Assert.Equal(2, r.Count));
    }

    [Fact]
    public void ALineRecurringUnderATitleThatChangesIsNoRunningHeader()
    {
        string Page(string title) => Line(72, 810, title, size: 9) + Line(72, 800, "Not seasonally adjusted", size: 9)
                                     + Line(72, 600, $"The {title} figures are shown in the table below.");
        using var doc = Tag(Build(Line(72, 700, "Consumer credit rose in the month."), Page("Levels"), Page("Flows")));

        Assert.DoesNotContain(doc.Pages[2].GetArtifactContent(), i => i.Text.Contains("seasonally"));
        Assert.Contains(doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => TextOf(p).Contains("Not seasonally adjusted"));
    }

    [Fact]
    public void AColumnOfTextBesideATableStaysOutOfItAndTheTableKeepsItsRows()
    {
        // Left: a paragraph running down the column; right: a ruled table whose rows stand between
        // the paragraph's lines.
        var page = Rule(320, 700, 520, 700) + Rule(320, 680, 520, 680) + Rule(320, 620, 520, 620)
                   + Rule(320, 620, 320, 700) + Rule(420, 620, 420, 700) + Rule(520, 620, 520, 700)
                   + Line(328, 686, "Month", "F2") + Line(428, 686, "Rate", "F2")
                   + Line(42, 707, "The carryover of prior year operating") + Line(42, 693, "expenses is the amount shown in the")
                   + Line(42, 679, "last form that you filed to claim a") + Line(42, 665, "deduction for the use of the home.");
        string[] months = ["jan.", "feb.", "mar.", "apr."];
        for (var k = 0; k < 4; k++)
            page += Line(328, 672 - 14 * k, months[k]) + Line(428, 672 - 14 * k, $"{k + 1}.5%");
        using var doc = Tag(Build(page));

        var root = doc.TaggedContent.StructTreeRootElement;
        var table = Assert.Single(root.FindElements<TableElement>(true));
        Assert.Equal(["Month", .. months], Rows(table).Select(r => TextOf(r[0])));
        Assert.Equal(["Rate", "1.5%", "2.5%", "3.5%", "4.5%"], Rows(table).Select(r => TextOf(r[1])));
        Assert.Contains(root.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => TextOf(p) == "The carryover of prior year operating expenses is the amount shown in the " +
                               "last form that you filed to claim a deduction for the use of the home.");
    }
    [Fact]
    public void TwoColumnsOfTextWhoseLinesLineUpAreNoTable()
    {
        // A page in two columns: most lines of one column stand between the other's, a few share
        // a baseline with them (a heading beside a paragraph).
        var page = Line(42, 760, "Line 19", "F2") + Line(320, 760, "Attach your own statement showing the")
                   + Line(42, 746, "If you rent rather than own your") + Line(320, 746, "cost or other basis of additions")
                   + Line(42, 732, "home, include the rent you paid") + Line(320, 732, "and improvements, used at least");
        for (var k = 0; k < 8; k++)
            page += Line(42, 711 - 14 * k, "Enter here the amounts that you paid for the") + Line(320, 704 - 14 * k, "partly for business, placed in service after");
        using var doc = Tag(Build(page));

        Assert.Empty(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
    }

    /// <summary>An unruled row of <paramref name="cells"/> at <paramref name="y"/>, its cells at
    /// <paramref name="xs"/>.</summary>
    private static string Row(double y, double[] xs, string[] cells, string font = "F1")
        => string.Concat(cells.Select((c, i) => Line(xs[i], y, c, font)));

    [Fact]
    public void RowsOfTextOverABodyOfFiguresAreHeaderRows()
    {
        // A wage bracket table: two rows of plain text name what the figures under them are.
        double[] xs = [72, 220, 380];
        var page = Line(72, 760, "The amounts withheld by wage bracket are these.")
                   + Row(720, xs, ["If the wage amount", "Married filing jointly", "Single"])
                   + Row(706, xs, ["at least", "withholding", "withholding"])
                   + Row(692, xs, ["$0", "$0", "$0"]) + Row(678, xs, ["$155", "$0", "$1"])
                   + Row(664, xs, ["$165", "$2", "$3"]) + Row(650, xs, ["$175", "$4", "$5"]);
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(6, rows.Count);
        Assert.All(rows.Take(2).SelectMany(r => r), c => Assert.IsType<TableTHElement>(c));
        Assert.All(rows.Skip(2).SelectMany(r => r), c => Assert.IsType<TableTDElement>(c));
    }

    [Fact]
    public void EveryLeadingRowInBoldOverAPlainBodyIsAHeaderRow()
    {
        double[] xs = [72, 200, 330];
        var page = Row(720, xs, ["Male", "Female", "Multiple"], "F2")
                   + Row(706, xs, ["age", "age", "years"], "F2")
                   + Row(692, xs, ["35", "40", "41.5"]) + Row(678, xs, ["36", "41", "40.7"]) + Row(664, xs, ["37", "42", "39.9"]);
        using var doc = Tag(Build(page));

        var rows = Rows(Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true)));
        Assert.All(rows.Take(2).SelectMany(r => r), c => Assert.IsType<TableTHElement>(c));
        Assert.All(rows.Skip(2).SelectMany(r => r), c => Assert.IsType<TableTDElement>(c));
    }

    [Fact]
    public void ATableOfTwoRowsKeepsItsBoldHeaderRow()
    {
        double[] xs = [72, 200];
        var page = Row(720, xs, ["Name", "Kind"], "F2") + Row(706, xs, ["amber", "stone"]);
        using var doc = Tag(Build(page));

        var rows = Rows(Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true)));
        Assert.All(rows[0], c => Assert.IsType<TableTHElement>(c));
        Assert.All(rows[1], c => Assert.IsType<TableTDElement>(c));
    }

    [Fact]
    public void ATableGoingOnOverAPageBreakSkipsOnlyTheHeaderRowsItsPartRepeats()
    {
        double[] xs = [72, 220];
        string Header(double y) => Row(y, xs, ["Item", "Weight"], "F2") + Row(y - 14, xs, ["name", "grams"], "F2");
        var first = Line(72, 780, "The stones we weighed are listed below.") + Header(120)
                    + Row(92, xs, ["amber", "10"]) + Row(78, xs, ["basalt", "17"]) + Row(64, xs, ["cedar", "24"]);
        var second = Header(780) + Row(752, xs, ["delta", "31"]) + Row(738, xs, ["ember", "38"])
                     + Line(72, 600, "That is every stone of the survey.");
        using var doc = Tag(Build(first, second));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(["Item", "name", "amber", "basalt", "cedar", "delta", "ember"], rows.Select(r => TextOf(r[0])));
        Assert.All(rows.Take(2).SelectMany(r => r), c => Assert.IsType<TableTHElement>(c));
        Assert.All(rows.Skip(2).SelectMany(r => r), c => Assert.IsType<TableTDElement>(c));
    }

    [Fact]
    public void ATableWiderThanAColumnKeepsItsGapOverThePagesGutter()
    {
        // A table across both columns of a two-column page, one of its gaps over the gutter
        // the columns of text under it show.
        double[] xs = [60, 218, 377];
        var page = Row(760, xs, ["Model", "Parameters", "Accuracy"], "F2")
                   + Row(746, xs, ["Alpha", "12 million", "91.2"]) + Row(732, xs, ["Beta", "48 million", "93.5"])
                   + Row(718, xs, ["Gamma", "110 million", "94.1"]);
        for (var k = 0; k < 12; k++)
            page += Line(60, 680 - 14 * k, "Amber basalt cedar delta ember fjord garnet harbor.")
                    + Line(320, 673 - 14 * k, "Indigo juniper kestrel lagoon meadow nectar orchid.");
        using var doc = Tag(Build(page));

        var rows = Rows(Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true)));
        Assert.Equal(["Model", "Alpha", "Beta", "Gamma"], rows.Select(r => TextOf(r[0])));
        Assert.All(rows[0], c => Assert.IsType<TableTHElement>(c));
    }

    [Fact]
    public void AHeaderRowWithACellOverSeveralColumnsJoinsTheTableUnderIt()
    {
        // Over four columns of figures, a bold row of three cells: its first over the two age
        // columns (the gap after it falls on a column edge), the others over one column each.
        double[] xs = [72, 130, 220, 300];
        var page = Line(72, 720, "Male", "F2") + Line(220, 720, "61", "F2") + Line(300, 720, "62", "F2")
                   + Row(706, xs, ["Male", "Female", "66", "67"], "F2")
                   + Row(692, xs, ["35", "40", "39.4", "39.3"]) + Row(678, xs, ["36", "41", "38.5", "38.4"])
                   + Row(664, xs, ["37", "42", "37.7", "37.5"]);
        using var doc = Tag(Build(page));

        var rows = Rows(Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true)));
        Assert.Equal(5, rows.Count);
        Assert.Equal(["Male", "61", "62"], rows[0].Select(TextOf));
        Assert.Equal(2, Span(rows[0][0], AttributeKey.ColSpan));
        Assert.All(rows.Take(2).SelectMany(r => r), c => Assert.IsType<TableTHElement>(c));
        Assert.Equal(["35", "40", "39.4", "39.3"], rows[2].Select(TextOf));
    }

    [Fact]
    public void ARuledTableWithASecondHeaderBandAmongItsRowsIsTwoTables()
    {
        // One frame around two stacked tables, each under its own two bold header rows.
        var sb = new StringBuilder();
        var y = 760.0;
        void RuledRow(string a, string b, string font = "F1") { sb.Append(Cell(72, y, 172, y - 20, a, font)).Append(Cell(172, y, 272, y - 20, b, font)); y -= 20; }
        RuledRow("Item", "Weight", "F2"); RuledRow("name", "grams", "F2");
        RuledRow("amber", "10"); RuledRow("basalt", "17");
        RuledRow("Stone", "Price", "F2"); RuledRow("name", "cents", "F2");
        RuledRow("cedar", "24"); RuledRow("delta", "31");
        using var doc = Tag(Build(sb.ToString()));

        var tables = doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true);
        Assert.Equal(2, tables.Count);
        Assert.Equal(["Item", "name", "amber", "basalt"], Rows(tables[0]).Select(r => TextOf(r[0])));
        Assert.Equal(["Stone", "name", "cedar", "delta"], Rows(tables[1]).Select(r => TextOf(r[0])));
        Assert.All(tables.SelectMany(t => Rows(t).Take(2).SelectMany(r => r)), c => Assert.IsType<TableTHElement>(c));
    }

    [Fact]
    public void AParagraphGoesOnOverAPageBreakAfterASentenceThatFilledItsLastLine()
    {
        // The line before the break ends a sentence and runs to the margin: the next page's first
        // word had no room on it, so the paragraph goes on (not so when that line stopped short).
        var full = Line(72, 100, "Garnet dune anchor tundra canyon fjord anchor juniper cedar.")
                   + Line(72, 86, "Zephyr canyon dune yarrow harbor ember birch canyon fjord.");
        var next = Line(72, 760, "Canyon ripple quartz harbor indigo dune garnet delta kestrel.")
                   + Line(72, 746, "Nectar indigo xenon garnet lagoon meadow delta quartz.");
        using (var doc = Tag(Build(full, next)))
        {
            var p = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true));
            Assert.Contains("canyon fjord. Canyon ripple", TextOf(p));
        }

        var stopped = Line(72, 100, "Garnet dune anchor tundra canyon fjord anchor juniper cedar.")
                      + Line(72, 86, "Zephyr canyon dune.");
        using (var doc = Tag(Build(stopped, next)))
            Assert.Equal(2, doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Count);
    }

    [Fact]
    public void AFramedCoverBlockAndABoxedColumnAreNoTables()
    {
        // A cover's boxed column: a title and a note in a frame with a rule across it (one
        // column); a framed title block with a rule each way and text in one of its four cells.
        var page = Rule(42, 400, 42, 800) + Rule(282, 400, 282, 800) + Rule(42, 800, 282, 800) + Rule(42, 400, 282, 400) + Rule(42, 600, 282, 600)
                   + Line(50, 760, "Publication 550", "F2", 14) + Line(50, 500, "Get forms and other information")
                   + Rule(320, 500, 560, 500) + Rule(320, 700, 560, 700) + Rule(320, 500, 320, 700) + Rule(560, 500, 560, 700)
                   + Rule(440, 500, 440, 700) + Rule(320, 600, 560, 600) + Line(330, 640, "INFORMATION SECURITY", "F2", 14);
        using var doc = Tag(Build(page));

        Assert.Empty(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var texts = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Select(TextOf).ToList();
        Assert.Contains("Get forms and other information", texts);
        Assert.Contains("INFORMATION SECURITY", texts);
    }

    [Fact]
    public void TheParagraphAColumnRuleRunsBesideIsNoRowOfTheTable()
    {
        // A column's rule runs from the page's top rule down past a small ruled table in the
        // column; the paragraph between them is framed with the table but is no row of it.
        var page = Rule(300, 760, 540, 760) + Rule(300, 500, 300, 760)
                   + Line(306, 740, "The column's paragraph runs on across the whole") + Line(306, 726, "width of the column, for a few lines above the")
                   + Line(306, 712, "small table below it.")
                   + Cell(300, 560, 420, 540, "Item") + Cell(420, 560, 540, 540, "Number")
                   + Cell(300, 540, 420, 520, "amber") + Cell(420, 540, 540, 520, "12")
                   + Cell(300, 520, 420, 500, "basalt") + Cell(420, 520, 540, 500, "7");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        var rows = Rows(table);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["Item", "Number"], rows[0].Select(TextOf));
        Assert.Contains(doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => TextOf(p).StartsWith("The column's paragraph") && TextOf(p).EndsWith("below it."));
    }

    [Fact]
    public void ATableStatesTheSpaceAboveItAndItsRowsTheirHeightAndTheParagraphAfterItItsSpace()
    {
        // Lines at a 12 pt pitch, a 30 pt gap, a table of 20 pt rows, a 24 pt gap, a line.
        var page = Line(72, 700, "The lines above the table run on") + Line(72, 688, "to the second of them.")
                   + Cell(72, 658, 272, 638, "Item") + Cell(272, 658, 472, 638, "Number")
                   + Cell(72, 638, 272, 618, "amber") + Cell(272, 638, 472, 618, "12")
                   + Line(72, 584, "The line after the table.");
        using var doc = Tag(Build(page));

        var table = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<TableElement>(true));
        Assert.InRange(Layout(table, AttributeKey.SpaceBefore) ?? 0, 24, 30);
        Assert.All(Rows(table).SelectMany(r => r), c => Assert.InRange(Layout(c, AttributeKey.Height) ?? 0, 19, 21));
        var after = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>(),
            p => TextOf(p) == "The line after the table.");
        Assert.InRange(Layout(after, AttributeKey.SpaceBefore) ?? 0, 18, 26);
    }
}
