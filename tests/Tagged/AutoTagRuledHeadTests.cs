using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a statistical release's first page: a title set between two rules, a release note set flush
/// right, and a table of figures whose headings stand in a ruled box - a rule over and under it, the rules between its
/// cells drawn as short strokes as thick as the box is tall - over unruled rows whose labels step in a level at a time.</summary>
public class AutoTagRuledHeadTests
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

    private static string Text(double x, double y, string text, double size = 10)
        => $"BT /F1 {size} Tf {x} {y} Td ({text}) Tj ET\n";

    // The table's column edges: the labels' column, then four columns of figures.
    private static readonly double[] Edges = { 72, 200, 285, 370, 455, 540 };

    private static string Page()
    {
        var s = new System.Text.StringBuilder();
        // The title between two rules, the lower one standing in its descent, just over the bottom of its text.
        s.Append("1 w 72 730 m 540 730 l S\n");
        s.Append(Text(72, 700, "STATISTICAL RELEASE", 24));
        s.Append("1 w 72 694 m 540 694 l S\n");
        s.Append(Text(72, 680, "R.1 RATES OF CREDIT"));
        // The table's title at the left; the release note flush right, its second line starting further in.
        s.Append(Text(72, 650, "Table 1. Rates"));
        s.Append(Text(540 - 21 * 6, 650, "For release at 4 p.m."));
        s.Append(Text(540 - 11 * 6, 640, "May 5, 2026"));
        // The headings' box: a rule over and under it, the rules between its cells short strokes 30 pt thick.
        s.Append("1 w 72 620 m 540 620 l S\n1 w 72 590 m 540 590 l S\n");
        foreach (var x in Edges.Skip(1).Take(4)) s.Append($"30 w {x - 0.5} 605 m {x + 0.5} 605 l S\n");
        // The headings: the name of the labels' column, and a year over each quarter.
        s.Append(Text(136 - 7 * 3, 599, "Account"));
        for (var c = 1; c < 5; c++)
        {
            s.Append(Text((Edges[c] + Edges[c + 1]) / 2 - 12, 608, "2025"));
            s.Append(Text((Edges[c] + Edges[c + 1]) / 2 - 6, 596, $"Q{c}"));
        }
        // The rows: labels stepping in 12 pt a level, figures flush right in their columns.
        var rows = new[] { (72.0, "Total"), (84.0, "Loans"), (96.0, "Cards"), (96.0, "Autos"), (84.0, "Leases"), (72.0, "Other") };
        for (var r = 0; r < rows.Length; r++)
        {
            var y = 575 - 12 * r;
            s.Append(Text(rows[r].Item1, y, rows[r].Item2));
            for (var c = 1; c < 5; c++) s.Append(Text(Edges[c + 1] - 10 - 24, y, $"{r + 1}{c}.{r}"));
        }
        return s.ToString();
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

    private static StructureAttributes Layout(StructureElement el) => el.Attributes.GetAttributes(AttributeOwnerStandard.Layout);

    private static List<StructureElement> Elements(Document doc) =>
        doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();

    private static List<List<StructureElement>> Rows(Document doc)
    {
        var table = Assert.Single(Elements(doc), e => e.S.Name == "Table");
        return table.FindElements<StructureElement>(true).Where(e => e.S.Name == "TR")
            .Select(r => r.ChildElements.OfType<StructureElement>().Where(c => c.S.Name is "TD" or "TH").ToList()).ToList();
    }

    [Fact]
    public void TheRuleStandingInATitlesDescentRulesTheTitleBelow()
    {
        using var doc = Tag(Build(Page()));
        var title = Assert.Single(Elements(doc), e => e.S.Name.StartsWith("H") && Text(e) == "STATISTICAL RELEASE");
        var thickness = Layout(title).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        Assert.NotNull(thickness);
        Assert.True(thickness![0] > 0 && thickness[1] > 0, $"the title is ruled above and below: [{string.Join(",", thickness)}]");
        var next = Assert.Single(Elements(doc), e => Text(e) == "R.1 RATES OF CREDIT" && e.S.Name != "Sect");
        Assert.Null(Layout(next).GetAttribute(AttributeKey.BorderThickness));
    }

    [Fact]
    public void APageLabelAtTheRightOfARunningHeadIsReadOnItsLine()
    {
        // The head, its page label flush right on its line, then two lines at the left under it: an empty channel runs
        // between the label and the lines at the left over all three rows.
        var content = Text(72, 740, "R.1 RATES OF CREDIT") + Text(540 - 6 * 5.4, 740, "Page 2", 9)
                      + Text(72, 726, "Table 2. Rates of credit") + Text(72, 714, "Seasonally adjusted")
                      + Text(72, 690, "The rates below are those of the banks reporting each week of the year.");
        using var doc = Tag(Build(content));
        var texts = Elements(doc).Where(e => e.S.Name is "P" || e.S.Name.StartsWith("H")).Select(Text).ToList();
        var label = texts.FindIndex(t => t.Contains("Page 2"));
        Assert.True(label >= 0 && texts[label].StartsWith("R.1"), string.Join(" | ", texts));
    }

    [Fact]
    public void AGroupHeadingInARuledHeadingsBoxSpansTheColumnsItsRuleRunsOver()
    {
        // The labels' column, two months, then four weeks; "Week ending" stands over the middle two weeks' columns, its
        // rule under it running over all four.
        double[] edges = { 72, 200, 260, 320, 380, 440, 500, 560 };
        var s = new System.Text.StringBuilder();
        s.Append(Text(72, 660, "Table 3. Rates by week"));
        // The box: a rule over and under it, uprights between the columns - between the weeks only under the group's rule.
        s.Append("1 w 72 620 m 560 620 l S\n1 w 72 590 m 560 590 l S\n");
        foreach (var x in edges.Skip(1).Take(3)) s.Append($"30 w {x - 0.5} 605 m {x + 0.5} 605 l S\n");
        foreach (var x in edges.Skip(4).Take(3)) s.Append($"15 w {x - 0.5} 597.5 m {x + 0.5} 597.5 l S\n");
        s.Append($"1 w {edges[3]} 605 m {edges[7]} 605 l S\n");
        s.Append(Text(136 - 7 * 3, 599, "Account"));
        s.Append(Text((edges[1] + edges[2]) / 2 - 12, 608, "2025") + Text((edges[1] + edges[2]) / 2 - 9, 596, "Aug"));
        s.Append(Text((edges[2] + edges[3]) / 2 - 12, 608, "2026") + Text((edges[2] + edges[3]) / 2 - 9, 596, "Feb"));
        s.Append(Text((edges[3] + edges[7]) / 2 - 33, 610, "Week ending"));
        for (var c = 3; c < 7; c++) s.Append(Text((edges[c] + edges[c + 1]) / 2 - 18, 596, $"Aug {10 + c}"));
        var rows = new[] { "Total", "Loans", "Cards", "Autos" };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 575 - 12 * r, rows[r]));
            for (var c = 1; c < 7; c++) s.Append(Text(edges[c + 1] - 8 - 24, 575 - 12 * r, $"{r + 1}{c}.{r}"));
        }
        using var doc = Tag(Build(s.ToString()));
        var week = Assert.Single(Rows(doc).SelectMany(r => r), c => Text(c) == "Week ending");
        Assert.Equal(4, week.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.ColSpan)?.GetNumberValue() ?? 1);
    }

    /// <summary>A table under a ruled headings box: years over months, "Week ending" over four week columns with its
    /// rule, "Account" set midway between the years and the months, the week dates 4 pt under the months; a rule
    /// closing the table 6 pt under its last row's text, a note set flush right under it.</summary>
    private static string WeeksTable()
    {
        double[] edges = { 72, 200, 260, 320, 380, 440, 500, 560 };
        var s = new System.Text.StringBuilder();
        s.Append(Text(72, 660, "Table 3. Rates by week"));
        s.Append("1 w 72 624 m 560 624 l S\n1 w 72 588 m 560 588 l S\n");
        foreach (var x in edges.Skip(1).Take(3)) s.Append($"36 w {x - 0.5} 606 m {x + 0.5} 606 l S\n");
        foreach (var x in edges.Skip(4).Take(3)) s.Append($"19 w {x - 0.5} 597.5 m {x + 0.5} 597.5 l S\n");
        s.Append($"1 w {edges[3]} 607 m {edges[7]} 607 l S\n");
        // Baselines 4, 5, 5 and 4 pt apart, as the headings are set.
        s.Append(Text((edges[3] + edges[7]) / 2 - 33, 614, "Week ending"));
        s.Append(Text((edges[1] + edges[2]) / 2 - 12, 610, "2025") + Text((edges[2] + edges[3]) / 2 - 12, 610, "2026"));
        s.Append(Text(136 - 7 * 3, 605, "Account"));
        s.Append(Text((edges[1] + edges[2]) / 2 - 9, 600, "Aug") + Text((edges[2] + edges[3]) / 2 - 9, 600, "Feb"));
        for (var c = 3; c < 7; c++) s.Append(Text((edges[c] + edges[c + 1]) / 2 - 18, 596, $"Aug {10 + c}"));
        var rows = new[] { "Total", "Loans", "Cards", "Autos" };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 575 - 9 * r, rows[r], 8));
            for (var c = 1; c < 7; c++) s.Append(Text(edges[c + 1] - 8 - 19.2, 575 - 9 * r, $"{r + 1}{c}.{r}", 8));
        }
        // The rows are 8 pt text 9 pt apart, the last on 548, its row's lower edge a size under it (540); the rule
        // closing the table runs across it 6 pt under that edge - more than half a row, less than one.
        s.Append("1 w 72 534 m 560 534 l S\n");
        s.Append(Text(560 - 24 * 4.8, 525, "(continued on next page)", 8));
        return s.ToString();
    }

    [Fact]
    public void AHeadingSetMidwayBetweenTwoHeadingRowsSpansBoth()
    {
        using var doc = Tag(Build(WeeksTable()));
        var rows = Rows(doc);
        var account = Assert.Single(rows.SelectMany(r => r), c => Text(c) == "Account");
        Assert.Equal(2, account.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.RowSpan)?.GetNumberValue() ?? 1);
        // The months and the week dates 4 pt under them head one row.
        Assert.Contains(rows, r => r.Select(Text).Contains("Aug") && r.Select(Text).Contains("Aug 13"));
    }

    [Fact]
    public void ARuleUnderATablesLastRowAcrossTheTableClosesIt()
    {
        using var doc = Tag(Build(WeeksTable()));
        // The table reaches down to the rule (534): a reader sets its last row down to it.
        var table = Assert.Single(Elements(doc), e => e.S.Name == "Table");
        var box = Layout(table).GetAttribute(AttributeKey.BBox)!.GetArrayNumberValue()!;
        Assert.Equal(534, box[1]!.Value, 0.5);
        var last = Rows(doc)[^1];
        Assert.Equal("Autos", Text(last[0]));
        var edges = Layout(last[0]).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        Assert.True(edges is { Length: 4 } && edges[1] > 0, "the last row is ruled below");
        var note = Assert.Single(Elements(doc), e => e.S.Name is "P" or "Note" && Text(e).StartsWith("(continued"));
        Assert.Null(Layout(note).GetAttribute(AttributeKey.BorderThickness));
    }

    [Fact]
    public void TextStandingApartAtTheRightNoRowSharingItsSidesIsReadTopToBottom()
    {
        // A title at the left; a note of two lines at the right under it; a heading centred under both; a paragraph. No
        // row holds text on both sides of the channel between the left and the right: no columns.
        var content = Text(72, 740, "R.6 MONEY MEASURES") + Text(540 - 21 * 4.8, 722, "For release at 4 p.m.", 8) + Text(540 - 11 * 4.8, 713, "May 5, 2026", 8)
                      + Text(306 - 10 * 6, 680, "Final Weekly Release")
                      + Text(72, 656, "This is the final publication of the weekly release of these measures.")
                      + Text(72, 644, "The first monthly release follows within the month after this one.");
        using var doc = Tag(Build(content));
        var texts = Elements(doc).Where(e => e.S.Name is "P" || e.S.Name.StartsWith("H")).Select(Text).Where(t => t.Length > 0).ToList();
        var (note, heading) = (texts.FindIndex(t => t.Contains("For release")), texts.FindIndex(t => t.StartsWith("Final Weekly")));
        Assert.True(note >= 0 && note < heading, string.Join(" | ", texts));
        var centred = Assert.Single(Elements(doc), e => e.S.Name != "Sect" && Text(e) == "Final Weekly Release");
        Assert.Equal("Center", Layout(centred).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
    }

    /// <summary>A table ruled into a band of headings - each of two lines, upright rules between them - over a band of
    /// rows of figures no rule parts, and under them a box of another table's headings with upright rules of its own
    /// (at 262 and 433), which the figures stand across.</summary>
    private static string BandsTable()
    {
        var s = new System.Text.StringBuilder();
        s.Append(Text(72, 660, "Table 4. Measures"));
        s.Append("1 w 72 620 m 540 620 l S\n1 w 72 590 m 540 590 l S\n1 w 72 520 m 540 520 l S\n1 w 72 500 m 540 500 l S\n");
        foreach (var x in Edges.Skip(1).Take(4)) s.Append($"30 w {x - 0.5} 605 m {x + 0.5} 605 l S\n");
        foreach (var x in new[] { 262, 433 }) s.Append($"20 w {x - 0.5} 510 m {x + 0.5} 510 l S\n");
        s.Append(Text(136 - 4 * 3, 602, "Date"));
        var heads = new[] { ("Currency in", "circulation"), ("Reserve", "balances"), ("Monetary", "base"), ("Total", "reserves") };
        for (var c = 1; c < 5; c++)
        {
            var middle = (Edges[c] + Edges[c + 1]) / 2;
            s.Append(Text(middle - heads[c - 1].Item1.Length * 3, 608, heads[c - 1].Item1) + Text(middle - heads[c - 1].Item2.Length * 3, 596, heads[c - 1].Item2));
        }
        var rows = new[] { "Sept.", "Oct.", "Nov.", "Dec.", "Jan." };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 577 - 12 * r, rows[r]));
            for (var c = 1; c < 5; c++) s.Append(Text(Edges[c + 1] - 10 - 24, 577 - 12 * r, $"{r + 1}{c}.{r}"));
        }
        s.Append(Text(90, 506, "Change at annual rates") + Text(340, 506, "M1") + Text(480, 506, "M2"));
        s.Append(Text(72, 486, "3 Months from Oct.") + Text(340, 486, "86.6") + Text(480, 486, "13.7"));
        s.Append(Text(72, 474, "6 Months from July") + Text(340, 474, "53.3") + Text(480, 474, "11.6"));
        return s.ToString();
    }

    [Fact]
    public void TheLinesOfAHeadingBandAreItsHeadingsLinesNoRows()
    {
        using var doc = Tag(Build(BandsTable()));
        var rows = Elements(doc).Where(e => e.S.Name == "TR").Select(r => r.ChildElements.OfType<StructureElement>().Select(Text).ToList()).ToList();
        var head = Assert.Single(rows, r => r.Contains("Date"));
        Assert.Equal(new[] { "Date", "Currency in circulation", "Reserve balances", "Monetary base", "Total reserves" },
            head.Select(t => string.Join(" ", t.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))));
    }

    [Fact]
    public void ABoxRuledIntoColumnsOfItsOwnUnderATableIsNoBandOfIt()
    {
        using var doc = Tag(Build(BandsTable()));
        var rows = Elements(doc).Where(e => e.S.Name == "TR").Select(r => r.ChildElements.OfType<StructureElement>().ToList()).ToList();
        var first = Assert.Single(rows, r => Text(r[0]) == "Sept.");
        Assert.Equal(new[] { "Sept.", "11.0", "12.0", "13.0", "14.0" }, first.Select(Text));
        // The table is the ruled one: its heading cells are ruled between them, where the upright rules stand.
        var head = Assert.Single(rows, r => r.Any(c => Text(c) == "Date"));
        double[] Edge(StructureElement c) => Layout(c).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue()?.Select(v => v ?? 0).ToArray() ?? new double[4];
        Assert.All(head.Skip(1), c => Assert.True(Edge(c)[2] > 0 && Edge(c)[0] > 0 && Edge(c)[1] > 0, Text(c)));
        // The box under it heads the table of its own rows.
        Assert.Contains(rows, r => r.Select(Text).SequenceEqual(new[] { "3 Months from Oct.", "86.6", "13.7" }));
    }

    [Fact]
    public void ARuleRightOverATablesFirstRowAcrossTheTableOpensIt()
    {
        // A table of text, no rule between its rows; a rule across it 7 pt over its headings' tops.
        var s = new System.Text.StringBuilder(Text(72, 660, "Table 5. Rates") + "1 w 72 640 m 540 640 l S\n");
        s.Append(Text(136 - 4 * 3, 623, "Date"));
        for (var c = 1; c < 5; c++) s.Append(Text(Edges[c + 1] - 10 - 24, 623, "2025"));
        var rows = new[] { "Sept.", "Oct.", "Nov.", "Dec." };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 605 - 12 * r, rows[r]));
            for (var c = 1; c < 5; c++) s.Append(Text(Edges[c + 1] - 10 - 24, 605 - 12 * r, $"{r + 1}{c}.{r}"));
        }
        using var doc = Tag(Build(s.ToString()));
        var table = Assert.Single(Elements(doc), e => e.S.Name == "Table");
        Assert.Equal(640, Layout(table).GetAttribute(AttributeKey.BBox)!.GetArrayNumberValue()![3]!.Value, 0.5);
    }

    /// <summary>A table of components under a ruled box of two bands.</summary>
    private static string ComponentsTable(bool ragged = false)
    {
        // A box of two bands 18 pt tall: a group heading over three columns in the upper, their names in the lower; "Date",
        // "Currency" and "Demand" midway beside both, their line 9 pt under the group's and 9 pt over the names'.
        double[] edges = { 72, 150, 228, 306, 384, 462, 540 };
        var s = new System.Text.StringBuilder(Text(72, 660, "Table 3. Components"));
        s.Append("1 w 72 624 m 540 624 l S\n1 w 306 606 m 540 606 l S\n1 w 72 588 m 540 588 l S\n");
        foreach (var x in edges.Skip(1).Take(3)) s.Append($"36 w {x - 0.5} 606 m {x + 0.5} 606 l S\n");
        foreach (var x in edges.Skip(4).Take(2)) s.Append($"18 w {x - 0.5} 597 m {x + 0.5} 597 l S\n");
        s.Append(Text(423 - 14 * 2.4, 612, "Other deposits", 8));
        var midway = new[] { "Date", "Currency", "Demand" };
        for (var c = 0; c < 3; c++) s.Append(Text((edges[c] + edges[c + 1]) / 2 - midway[c].Length * 2.4, 603, midway[c], 8));
        var names = new[] { "At banks", "At thrifts", "Total" };
        for (var c = 3; c < 6; c++) s.Append(Text((edges[c] + edges[c + 1]) / 2 - names[c - 3].Length * 2.4, 594, names[c - 3], 8));
        var rows = new[] { "Sept.", "Oct.", "Nov.", "Dec.", "Jan." };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 575 - 9 * r, rows[r], 8));
            for (var c = 1; c < 6; c++)
            {
                // (ragged: the last rows' figures run to thousands, all ending at one edge)
                var figure = (ragged && r > 2 ? "1," : "") + $"{r + 1}{c}.{r}";
                s.Append(Text(edges[c + 1] - 20 - figure.Length * 4.8, 575 - 9 * r, figure, 8));
            }
        }
        return s.ToString();
    }

    [Fact]
    public void HeadingsMidwayBesideTwoHeadingRowsNineApartAreNoRowOfTheirOwn()
    {
        using var doc = Tag(Build(ComponentsTable()));
        var names = new[] { "At banks", "At thrifts", "Total" };
        var head = Rows(doc).Where(r => r.Any(c => c.S.Name == "TH")).ToList();
        Assert.Equal(2, head.Count);
        var date = Assert.Single(head.SelectMany(r => r), c => Text(c) == "Date");
        Assert.Equal(2, date.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.RowSpan)?.GetNumberValue() ?? 1);
        Assert.Equal(names, head[1].Select(Text).Where(t => t.Length > 0));
    }

    [Fact]
    public void HeadingsOfSeveralLinesInARuledBoxOfTwoBandsAreOneCellEach()
    {
        // A box of two bands, 18 and 28 pt tall: a group heading over three columns in the upper band, their names on
        // two lines each in the lower; "Date" and a heading of three lines beside both bands, no rule across them.
        double[] edges = { 72, 150, 228, 306, 384, 462, 540 };
        var s = new System.Text.StringBuilder(Text(72, 660, "Table 4. Components"));
        s.Append("1 w 72 624 m 540 624 l S\n1 w 150 606 m 384 606 l S\n1 w 72 578 m 540 578 l S\n");
        foreach (var x in new[] { edges[1], edges[4], edges[5] }) s.Append($"46 w {x - 0.5} 601 m {x + 0.5} 601 l S\n");
        foreach (var x in edges.Skip(2).Take(2)) s.Append($"28 w {x - 0.5} 592 m {x + 0.5} 592 l S\n");
        void Centred(int from, int to, double y, string text) => s.Append(Text((edges[from] + edges[to]) / 2 - text.Length * 2.4, y, text, 8));
        Centred(1, 4, 612, "Savings deposits");
        Centred(0, 1, 598, "Date");
        var names = new[] { ("At commercial", "banks"), ("At thrift", "institutions") };
        for (var c = 0; c < 2; c++)
        {
            Centred(c + 1, c + 2, 594, names[c].Item1);
            Centred(c + 1, c + 2, 585, names[c].Item2);
        }
        Centred(3, 4, 590, "Total");
        Centred(4, 5, 602, "Retail");
        Centred(4, 5, 593, "money funds");
        Centred(5, 6, 607, "Memorandum:");
        Centred(5, 6, 598, "Institutional");
        Centred(5, 6, 589, "money funds");
        var rows = new[] { "Sept.", "Oct.", "Nov.", "Dec.", "Jan." };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 560 - 9 * r, rows[r], 8));
            for (var c = 1; c < 6; c++) s.Append(Text(edges[c + 1] - 20 - 19.2, 560 - 9 * r, $"{r + 1}{c}.{r}", 8));
        }
        using var doc = Tag(Build(s.ToString()));
        var head = Rows(doc).Where(r => r.Any(c => c.S.Name == "TH")).ToList();
        Assert.Equal(2, head.Count);
        static string Words(StructureElement c) => string.Join(" ", Text(c).Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(new[] { "Date", "Savings deposits", "Retail money funds", "Memorandum: Institutional money funds" }, head[0].Select(Words).Where(t => t.Length > 0));
        Assert.Equal(new[] { "At commercial banks", "At thrift institutions", "Total" }, head[1].Select(Words).Where(t => t.Length > 0));
        foreach (var cell in head[0].Where(c => Words(c) is not "Savings deposits" and not ""))
            Assert.Equal(2, cell.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.RowSpan)?.GetNumberValue() ?? 1);
        var table = Assert.Single(Elements(doc), e => e.S.Name == "Table");
        Assert.Equal(624, Layout(table).GetAttribute(AttributeKey.BBox)!.GetArrayNumberValue()![3]!.Value, 0.5);
    }

    [Fact]
    public void AnUnderlineInALinesDescentUnderItsTextAloneRulesNothing()
    {
        // Two paragraphs; the first one's last line underlined under its first 30 characters, the stroke in its descent.
        var content = Text(72, 700, "The survey went on over the hills and along the old river bank,")
                      + Text(72, 688, "past the mill, see the full report at the county office now.")
                      + "0.5 w 72 687 m 252 687 l S\n"
                      + Text(72, 664, "The second part tells of the valley and the farms beyond it.");
        using var doc = Tag(Build(content));
        Assert.All(Elements(doc).Where(e => e.S.Name == "P"), p => Assert.Null(Layout(p).GetAttribute(AttributeKey.BorderThickness)));
    }

    [Fact]
    public void LinesEndingTogetherAtTheEdgeTheLastStartingFurtherInAreSetFlushRight()
    {
        using var doc = Tag(Build(Page()));
        var note = Assert.Single(Elements(doc), e => e.S.Name == "P" && Text(e).StartsWith("For release"));
        Assert.Equal("End", Layout(note).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
    }

    [Fact]
    public void TheRuledHeadingsBoxIsTheTablesHeaderRow()
    {
        using var doc = Tag(Build(Page()));
        var rows = Rows(doc);
        Assert.Equal(new[] { "2025", "2025", "2025", "2025" }, rows[0].Select(Text).Where(t => t.Length > 0));
        Assert.Equal(new[] { "Account", "Q1", "Q2", "Q3", "Q4" }, rows[1].Select(Text).Where(t => t.Length > 0));
        Assert.All(rows.Take(2).SelectMany(r => r), c => Assert.Equal("TH", c.S.Name));
        Assert.Contains(rows.Skip(1), r => r.Select(Text).FirstOrDefault() == "Total");
    }

    [Fact]
    public void AStrokeThickerThanItIsLongRulesTheCellsItStandsBetween()
    {
        using var doc = Tag(Build(Page()));
        var rows = Rows(doc);
        var (years, head) = (rows[0].Where(c => Text(c).Length > 0).ToList(), rows[1].Where(c => Text(c).Length > 0).ToList());
        double[] Edge(StructureElement c) => Layout(c).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue()?.Select(v => v ?? 0).ToArray()
                                             ?? new double[4];
        // Over the years, under the quarters; between the headings, not at the table's outer sides.
        Assert.All(years, c => Assert.True(Edge(c)[0] > 0));
        Assert.All(head, c => Assert.True(Edge(c)[1] > 0));
        Assert.True(Edge(head[0])[3] > 0 && Edge(head[1])[2] > 0 && Edge(head[3])[3] > 0 && Edge(years[1])[2] > 0);
        Assert.Equal(0, Edge(head[^1])[3]);
    }

    [Fact]
    public void AHeadingMidwayBetweenTheRulesClosingItInIsCentred()
    {
        using var doc = Tag(Build(Page()));
        Assert.All(Rows(doc).Take(2).SelectMany(r => r).Where(c => Text(c).Length > 0),
            c => Assert.Equal("Center", Layout(c).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString()));
    }

    [Fact]
    public void ALabelSetInALevelStatesHowFarItStartsInFromItsColumn()
    {
        using var doc = Tag(Build(Page()));
        var labels = Rows(doc).Skip(1).Select(r => r.FirstOrDefault(c => Text(c).Length > 0 && char.IsLetter(Text(c)[0]))).OfType<StructureElement>()
            .ToDictionary(Text, c => Layout(c).GetAttribute(AttributeKey.StartIndent)?.GetNumberValue() ?? 0);
        Assert.Equal(0, labels["Total"]);
        Assert.Equal(12, labels["Loans"], 1);
        Assert.Equal(24, labels["Cards"], 1);
        Assert.Equal(12, labels["Leases"], 1);
    }

    [Fact]
    public void ANumberedNoteDrawnInOneShowWithItsNumberHasTheNumberAsItsLabel()
    {
        // A line over the notes, then three notes, each drawn by one show: its number, a gap, its text.
        var s = new System.Text.StringBuilder(Text(72, 700, "Components may not add to totals due to rounding.", 8));
        var notes = new[] { "Savings deposits include money market deposit accounts.", "Small time deposits are those issued in small amounts.",
            "Retail funds are not part of the institutional funds." };
        for (var n = 0; n < notes.Length; n++)
            s.Append($"BT /F1 8 Tf 80 {684 - 9 * n} Td [({n + 1}.) -400 ({notes[n]})] TJ ET\n");
        using var doc = Tag(Build(s.ToString()));
        var items = Elements(doc).Where(e => e.S.Name == "LI").ToList();
        Assert.Equal(3, items.Count);
        for (var n = 0; n < items.Count; n++)
        {
            var parts = items[n].ChildElements.OfType<StructureElement>().ToList();
            Assert.Equal(new[] { "Lbl", "LBody" }, parts.Select(e => e.S.Name));
            Assert.Equal($"{n + 1}.", Text(parts[0]));
            Assert.Equal(notes[n], Text(parts[1]));
        }
    }

    [Fact]
    public void AColumnOfCellsSetRoundOneMiddleIsCentred()
    {
        // Three columns: names, a column of cells of different widths set round x 300, figures set flush right.
        var s = new System.Text.StringBuilder(Text(72, 700, "Table 5. Measures"));
        var rows = new[] { ("Currency", "M1", "2,071.6"), ("Demand deposits", "M1 and M2", "19,658.2"), ("Savings", "M2", "917.4"),
            ("Small time deposits", "Non-M1 M2", "1,055.0"), ("Retail funds", "M2", "96.3") };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 670 - 12 * r, rows[r].Item1));
            s.Append(Text(300 - rows[r].Item2.Length * 3, 670 - 12 * r, rows[r].Item2));
            s.Append(Text(480 - rows[r].Item3.Length * 6, 670 - 12 * r, rows[r].Item3));
        }
        using var doc = Tag(Build(s.ToString()));
        var cells = Rows(doc);
        Assert.Equal(5, cells.Count);
        Assert.All(cells, row => Assert.Equal("Center", Layout(row[1]).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString()));
        Assert.All(cells, row => Assert.Equal("End", Layout(row[2]).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString()));
    }

    [Fact]
    public void AColumnEndsAtTheUprightRuleBetweenItsTextAndTheNexts()
    {
        // The rules between the headings drawn as lines from the box's top to its foot.
        var page = Page();
        foreach (var x in Edges.Skip(1).Take(4)) page = page.Replace($"30 w {x - 0.5} 605 m {x + 0.5} 605 l S", $"1 w {x} 590 m {x} 620 l S");
        using var doc = Tag(Build(page));
        // The columns are as wide as the rules set them, not as the texts standing in them, flush right 10 pt short of
        // the rules, would.
        var head = Rows(doc)[0];
        Assert.InRange(Layout(head[0]).GetAttribute(AttributeKey.Width)!.GetNumberValue()!.Value, 128, 130);
        Assert.All(head.Skip(1), cell => Assert.InRange(Layout(cell).GetAttribute(AttributeKey.Width)!.GetNumberValue()!.Value, 84.5, 85.5));
    }

    private static double? Number(StructureElement cell, AttributeKey key) => Layout(cell).GetAttribute(key)?.GetNumberValue();

    private static string? Align(StructureElement cell) => Layout(cell).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString();

    [Fact]
    public void FiguresInARuledColumnStateHowFarShortOfItsEdgeTheyEnd()
    {
        // Columns 78 pt wide between the dividers of the headings' box, the figures ending 20 pt short of them.
        using var doc = Tag(Build(ComponentsTable(ragged: true)));
        var body = Rows(doc).Where(r => r.All(c => c.S.Name == "TD")).ToList();
        Assert.Equal(5, body.Count);
        foreach (var row in body)
        {
            // The columns end at the dividers, not where their figures do.
            Assert.All(row, cell => Assert.InRange(Number(cell, AttributeKey.Width)!.Value, 77, 79));
            Assert.All(row.Skip(1), cell => Assert.Equal("End", Align(cell)));
            Assert.All(row.Skip(1), cell => Assert.InRange(Number(cell, AttributeKey.EndIndent)!.Value, 19, 21));
        }
    }

    [Fact]
    public void AFigureWithTheLetterOfANoteAfterItIsAFigureEndingWhereItsDigitsDo()
    {
        // Three columns of figures set flush right; in the last two rows each figure has an "e" after it, past the
        // edge the figures end at.
        var s = new System.Text.StringBuilder(Text(72, 730, "Table 7. Other items"));
        string[] heads = { "Date", "Loans", "Leases", "Total" };
        for (var c = 0; c < heads.Length; c++) s.Append(Text(c == 0 ? 72 : 150 + 100 * c - heads[c].Length * 6, 676, heads[c]));
        var rows = new[] { "Sept.", "Oct.", "Nov.", "Dec.", "Jan.", "Feb." };
        for (var r = 0; r < rows.Length; r++)
        {
            s.Append(Text(72, 660 - 12 * r, rows[r]));
            for (var c = 1; c < 4; c++)
            {
                var figure = $"{(r % 2 == 0 ? "1," : "")}{r + 1}{c}{r}.{r}";
                s.Append(Text(150 + 100 * c - figure.Length * 6, 660 - 12 * r, figure + (r >= 4 ? " e" : "")));
            }
        }
        using var doc = Tag(Build(s.ToString()));
        var cells = Rows(doc);
        Assert.Equal(7, cells.Count);
        Assert.All(cells[0], cell => Assert.Equal("TH", cell.S.Name));
        Assert.All(cells.Skip(1), row => Assert.All(row.Skip(1), cell => Assert.Equal("End", Align(cell))));
    }

    [Fact]
    public void RowsLeavingTheLastColumnEmptyUnderATableAreRowsOfIt()
    {
        // Rows of four columns; under them, a line's pitch further down, a label and rows with no figure in the last.
        var s = new System.Text.StringBuilder(Text(72, 730, "Table 8. Deposits"));
        string[] heads = { "Date", "Demand", "Balance", "Savings" };
        for (var c = 0; c < heads.Length; c++) s.Append(Text(c == 0 ? 72 : 150 + 100 * c - heads[c].Length * 6, 676, heads[c]));
        void Row(double y, string label, int r, int columns)
        {
            s.Append(Text(72, y, label));
            for (var c = 1; c < columns; c++)
            {
                var figure = $"{(c == 2 ? "1," : "")}{r + 1}{c}{r}.{r}";
                s.Append(Text(150 + 100 * c - figure.Length * 6, y, figure));
            }
        }
        var months = new[] { "Sept.", "Oct.", "Nov.", "Dec.", "Jan." };
        for (var r = 0; r < months.Length; r++) Row(660 - 12 * r, months[r], r, 4);
        s.Append(Text(72, 590, "Week ending"));
        var weeks = new[] { "Dec. 7", "Dec. 14", "Dec. 21", "Dec. 28" };
        for (var r = 0; r < weeks.Length; r++) Row(578 - 12 * r, weeks[r], r, 3);
        using var doc = Tag(Build(s.ToString()));
        var rows = Rows(doc);
        Assert.Equal(11, rows.Count);
        Assert.All(rows, row => Assert.Equal(4, row.Sum(c => (int)(c.Attributes.GetAttributes(AttributeOwnerStandard.Table).GetAttribute(AttributeKey.ColSpan)?.GetNumberValue() ?? 1))));
        Assert.Equal("", Text(rows[^1][^1]));
    }
}
