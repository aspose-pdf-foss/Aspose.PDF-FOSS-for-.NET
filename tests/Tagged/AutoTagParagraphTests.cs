using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of paragraphs and columns: where a paragraph ends, what a line's
/// size is, how text beside a figure and lists of short lines read.</summary>
public class AutoTagParagraphTests
{
    /// <summary>An untagged Letter page drawing <paramref name="content"/> with F1 Helvetica,
    /// F2 Helvetica-Bold and F3 Courier (6 pt per character at 10 pt), plus a 240 x 200 pt
    /// image resource Im1, and a form XObject Fm1 drawing <paramref name="form"/> when one is given.</summary>
    private static byte[] Build(string content, string pageExtra = "", string? form = null, string formBox = "0 0 612 792")
    {
        const string image = "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\n\u0080\nendstream";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [7 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
            image,
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 8 0 R " +
                "/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> /XObject << /Im1 6 0 R" + (form is null ? "" : " /Fm1 9 0 R")
                + " >> >> " + pageExtra + " >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        if (form is not null)
        {
            // (F9: a pi font whose code 2 is named H11003 - a sign no character stands for)
            objects.Add($"<< /Type /XObject /Subtype /Form /BBox [{formBox}] /Resources << /Font << /F1 3 0 R /F9 10 0 R >> >> /Length {form.Length} >>\nstream\n{form}\nendstream");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Universal-GreekwithMathPi /FirstChar 2 /LastChar 2 /Widths [833] "
                        + "/Encoding << /Type /Encoding /Differences [2 /H11003] >> >>");
        }
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

    private static string Line(double x, double y, string text, string font = "F3", int size = 10)
        => $"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n";

    private const string Words = "amber meadow lies beyond the northern fjord where willow and juniper grow along the quiet valley floor ";

    /// <summary>A column of Courier prose lines <paramref name="chars"/> characters (6 pt each)
    /// wide from (<paramref name="x"/>, <paramref name="top"/>) down at a 12 pt pitch, each line
    /// ending in its number (<paramref name="tag"/> plus two digits).</summary>
    private static string Column(double x, double top, string tag, int lines, int chars = 40, double pitch = 12)
    {
        var sb = new StringBuilder();
        var at = 0;
        for (var i = 0; i < lines; i++)
        {
            var mark = $" {tag}{i:00}";
            var body = new StringBuilder();
            while (body.Length < chars - mark.Length) { body.Append(Words[at]); at = (at + 1) % Words.Length; }
            var text = body.ToString().TrimEnd() + mark;
            // The column's first line opens a sentence: the text before it does not go on into it.
            if (i == 0) text = char.ToUpperInvariant(text[0]) + text.Substring(1);
            sb.Append(Line(x, top - i * pitch, text));
        }
        return sb.ToString();
    }

    private static Document Tag(byte[] pdf, HeadingRecognitionStrategy headings = HeadingRecognitionStrategy.Default)
    {
        var doc = new Document(new MemoryStream(pdf));
        var options = new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true, HeadingRecognitionStrategy = headings },
        };
        doc.Convert(options);
        return doc;
    }

    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent().Select(i => i.Text)).Trim();

    private static List<StructureElement> Blocks(Document doc, params string[] tags)
        => doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true)
            .Where(e => tags.Contains(e.S.Name)).ToList();

    private static double? Layout(StructureElement el, AttributeKey key)
        => el.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(key)?.GetNumberValue();

    [Fact]
    public void ColumnsAGutterOfOneAndAHalfEmsApartReadColumnByColumn()
    {
        // Left column 42..282, right column from 296: a 14 pt gutter at 10 pt text. The left
        // column's paragraph ends before the column does, so the right column's does not go on with it.
        var doc = Tag(Build(Column(42, 700, "L", 3) + Line(42, 664, "and the left column ends here. L03") + Column(296, 700, "R", 4)));

        var paragraphs = Blocks(doc, "P").Select(TextOf).ToList();
        Assert.Equal(2, paragraphs.Count);
        Assert.EndsWith("L03", paragraphs[0]);
        Assert.DoesNotContain("R0", paragraphs[0]);
        Assert.EndsWith("R03", paragraphs[1]);
        Assert.DoesNotContain("L0", paragraphs[1]);
    }

    [Fact]
    public void ABulletDrawnLargerThanItsLineDoesNotMakeTheLineAHeading()
    {
        // Three items whose bullet is set at 10.8 pt beside 9 pt text, after a 9 pt paragraph.
        var content = Column(42, 700, "P", 3);
        for (var i = 0; i < 3; i++)
            content += Line(42, 640 - i * 12, "\\225", "F1", 11) + Line(52, 640 - i * 12, $"Item {i} of the list", "F1", 10);
        var doc = Tag(Build(content));

        Assert.Empty(Blocks(doc, "H1", "H2", "H3", "H4", "H5", "H6"));
        var items = Blocks(doc, "LI");
        Assert.Equal(3, items.Count);
        Assert.Equal("Item 0 of the list", TextOf(items[0]).TrimStart('•', ' '));
    }

    [Fact]
    public void AShortLineInAJustifiedColumnEndsItsParagraph()
    {
        // Every line runs to 282 but the fourth, which stops at 162; the fifth starts at the
        // left edge at the usual pitch with no indent.
        var content = Column(42, 700, "A", 3) + Line(42, 664, "the short line ends here A03") + Column(42, 652, "B", 3);
        var doc = Tag(Build(content));

        var paragraphs = Blocks(doc, "P").Select(TextOf).ToList();
        Assert.Equal(2, paragraphs.Count);
        Assert.EndsWith("short line ends here A03", paragraphs[0]);
        Assert.EndsWith("B02", paragraphs[1]);
    }

    [Fact]
    public void ShortLinesSetFlushRightAreParagraphsOfTheirOwnAlignedToTheEnd()
    {
        // A full-width paragraph sets the column; below it three names each ending at 540.
        var content = Column(42, 700, "A", 3, 83);
        var names = new[] { "Scott Rose", "Oliver Borchert", "Stu Mitchell" };
        for (var i = 0; i < names.Length; i++)
            content += Line(540 - names[i].Length * 6, 640 - i * 12, names[i]);
        var doc = Tag(Build(content));

        var paragraphs = Blocks(doc, "P");
        Assert.Equal(4, paragraphs.Count);
        for (var i = 0; i < names.Length; i++)
        {
            var p = paragraphs[i + 1];
            Assert.Equal(names[i], TextOf(p));
            Assert.Equal("End", p.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
                .GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
            Assert.Null(Layout(p, AttributeKey.StartIndent));
        }
    }

    [Fact]
    public void ASectionNumberAtTheLeftOfItsTitleUnderAListItemsIndentedLinesIsNoColumn()
    {
        // Two bulleted items, their text and its lines going on 18 pt right of the bullets; under them a section heading,
        // its number at the margin, its title 13.5 pt on - a channel running down past the item's last lines too.
        var content = Line(90, 700, "\\225", "F1") + Line(108, 700, "Identity management system: it holds the records of subjects")
                      + Line(108, 686, "and their roles, so a policy is checked against the records")
                      + Line(90, 666, "\\225", "F1") + Line(108, 666, "Event management system: this gathers the events of systems")
                      + Line(108, 652, "centric information for later analysis, used to refine policies")
                      + Line(108, 638, "and warn of attacks.")
                      + Line(72, 612, "3.1", "F2", 11) + Line(100.8, 612, "Variations of the Approaches", "F2", 11)
                      + Column(72, 588, "B", 4, 80);
        var doc = Tag(Build(content));

        // The item's last lines are the item's; the heading reads whole, after the list.
        var list = Assert.Single(Blocks(doc, "L"));
        Assert.EndsWith("and warn of attacks.", TextOf(list));
        var heading = Blocks(doc, "H1", "H2", "H3", "H4", "H5", "H6", "P").Single(e => TextOf(e).Contains("Variations", StringComparison.Ordinal));
        Assert.Equal("3.1 Variations of the Approaches", TextOf(heading));
    }

    [Fact]
    public void AListOfReferencesWithWideLabelsIsNoColumns()
    {
        // References as a word processor sets them (12 pt Courier, 7.2 pt a character): each label at 77.4 pt - most of them
        // filling most of the 90 pt before the text - its text at 167.4 pt going on under itself, each clipped to its cell.
        var content = Line(72, 740, "The works this survey draws on, by the label each is cited by:", "F3", 12);
        string[] labels = ["[M-19-17]", "[NIST 798]", "[NIST 806]", "[NISTPRIV]", "[SP800-37]"];
        for (var k = 0; k < labels.Length; k++)
        {
            var y = 710 - 56 * k;
            var bottom = System.FormattableString.Invariant($"{y - 33}");
            content += $"q 72 {bottom} 90 50 re W n\n" + Line(77.4, y, labels[k], "F3", 12) + "Q\n"
                       + $"q 162 {bottom} 369 50 re W n\n" + Line(167.4, y, "Office of the Survey (2019) Field Notes", "F3", 12)
                       + Line(167.4, y - 13.8, "of the Ridge. Available at the office", "F3", 12)
                       + Line(167.4, y - 27.6, $"https://example.org/notes-{k}", "F3", 12) + "Q\n";
        }
        var doc = Tag(Build(content));

        // Each label opens its entry's paragraph: no column of labels and another of texts.
        var paragraphs = Blocks(doc, "P").Select(TextOf).ToList();
        foreach (var label in labels)
            Assert.Contains(paragraphs, p => p.StartsWith(label + " Office of the Survey", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ANotesNumberLinkedAfterALinesLastWordStandsOnThatLineThoughItsBoxReachesTheLineOverIt()
    {
        // A paragraph of three lines (12 pt Helvetica, 13.8 pt apart), the last ending at 182 pt; after it a note's number
        // set raised in 8 pt, a link round it reaching up into the line over it - more of its height on its own line. As a
        // word processor exports it, the number is drawn in the middle of the line over it.
        var content = Line(72, 707.5, "Amber meadow lies beyond the northern fjord where", "F1", 12)
                      + Line(72, 693.7, "along the valley floor and the ", "F1", 12) + Line(182.6, 684.4, "3", "F1", 8)
                      + Line(228.76, 693.7, "above it, past the ford", "F1", 12) + Line(72, 679.9, "the quiet valley floor.", "F1", 12);
        var doc = Tag(Build(content, "/Annots [<< /Type /Annot /Subtype /Link /Rect [182.6 685.5 187.1 696.5] /Border [0 0 0] /Dest [7 0 R /XYZ 0 792 0] >>]"));

        var paragraph = Assert.Single(Blocks(doc, "P"));
        Assert.EndsWith("the quiet valley floor. 3", TextOf(paragraph));
    }

    [Fact]
    public void TextAfterANoteDrawnInAFontOfItsOwnInsideASavedStateReadsInTheFontItIsShownIn()
    {
        // The body's font set (Helvetica), then a note down the margin in a wider face (Courier) inside q/Q, then - in one
        // text object with a paragraph's line over it, so its pieces are marked one by one - the body's line shown with no
        // font set again: its comma, space and hyphen shown on their own, each piece placed after the one before as
        // Helvetica sets it.
        var content = Column(72, 720, "A", 3, 80)
                      + "BT /F1 10 Tf ET\nq BT /F3 9 Tf 0 -9 9 0 20 300 Tm (A note down the margin) Tj ET Q\n"
                      + "BT 72 640 Td (A short paragraph ends here.) Tj 0 -40 Td "
                      + "(variations of this model) Tj (,) Tj ( ) Tj 107.82 0 Td (as well for networks, non) Tj (-) Tj "
                      + "113.37 0 Td (IP based networks.) Tj ET\n";
        var doc = Tag(Build(content));

        var texts = Blocks(doc, "P", "H1", "H2", "H3", "H4", "H5", "H6").Select(TextOf).ToList();
        Assert.True(texts.Contains("variations of this model, as well for networks, non-IP based networks."), string.Join(" | ", texts));
    }

    [Fact]
    public void AShortNameOverALongTitleBothEndingAtTheEdgeIsFlushRight()
    {
        // A cover set flush right at 540 - a title, names, a date - and at its foot an institute's name (a blank after it)
        // and, under it, its director's long title: the page's widest line, its text column found from it. The name
        // starts far further in than a first line is set.
        var content = Line(540 - 23 * 6, 700, "Zero Trust Architecture") + Line(540 - 10 * 6, 660, "Scott Rose")
                      + Line(540 - 15 * 6, 648, "Oliver Borchert") + Line(540 - 11 * 6, 400, "August 2020")
                      + Line(540 - 47 * 6, 120, "National Institute of Standards and Technology ")
                      + Line(540 - 59 * 6, 108, "Walter Copan, NIST Director and Under Secretary of Commerce");
        var doc = Tag(Build(content));

        var p = Blocks(doc, "P").Single(e => TextOf(e).Contains("Walter Copan", StringComparison.Ordinal));
        Assert.StartsWith("National Institute", TextOf(p), StringComparison.Ordinal);
        Assert.Equal("End", p.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
    }

    [Fact]
    public void AHeadingLineStartingAtTheParagraphIndentAndJustifiedToTheEdgeIsNotFlushRight()
    {
        // A column 42..288 whose paragraphs start their first line 12 pt in; a bold heading line a size larger starts
        // there too and is justified to the column's edge (its spaces widened), 6 pt more than a line under the paragraph
        // above, the paragraph it heads going on at the left edge.
        var content = Line(54, 700, "Amber meadow lies beyond the northern A") + Column(42, 688, "A", 2)
                      + "BT /F2 11 Tf 54 658 Td [(Getting ) -431.6 (forms, ) -431.6 (booklets, ) -431.6 (and ) -431.6 (the ) -431.6 (guides.)] TJ ET\n"
                      + Column(42, 646, "B", 3);
        var doc = Tag(Build(content));

        var heading = Blocks(doc, "H1", "H2", "H3", "H4", "H5", "H6", "P").Single(e => TextOf(e).StartsWith("Getting forms", StringComparison.Ordinal));
        Assert.Equal("Getting forms, booklets, and the guides.", TextOf(heading));
        Assert.NotEqual("End", heading.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
        // It starts where the paragraphs' first lines do.
        Assert.Equal(12, Layout(heading, AttributeKey.StartIndent) ?? 0, 1);
    }

    /// <summary>A line of 10 pt Courier 40 characters wide (240 pt) cut from the prose at <paramref name="at"/>, ending in
    /// a hyphen or a comma (<paramref name="hung"/>) or in a letter; a line ending in a letter is set 2.8 pt narrower, its
    /// letters drawn closer: the punctuation ending the others hangs past the edge it ends at.</summary>
    private static string Full(double x, double y, int at, char? hung = null, bool opens = false)
    {
        var text = string.Concat(Enumerable.Range(at * 41, 39).Select(k => Words[k % Words.Length])).Replace(' ', 'e').ToCharArray();
        for (var k = 6; k < 36; k += 7) text[k] = ' ';
        if (opens) text[0] = char.ToUpperInvariant(text[0]);
        var spacing = hung is null ? "-0.07 Tc " : "0 Tc ";
        return $"BT /F3 10 Tf {spacing}{x} {y} Td ({new string(text)}{hung ?? 's'}) Tj ET\n";
    }

    private static string? AlignOf(StructureElement el)
        => el.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString();

    [Fact]
    public void AColumnSetJustifiedWithItsPunctuationHungPastItsEdgeStatesItsParagraphsJustified()
    {
        // A column 42..282: a paragraph of five lines, one of two, a list of three short items set in, a paragraph of four.
        // Most lines end in a letter, 2.8 pt short of the edge the hyphens and commas ending the others hang to; each
        // paragraph's last line is short, the next starting under it at the usual pitch.
        var content = Full(42, 700, 0, opens: true) + Full(42, 688, 1, '-') + Full(42, 676, 2) + Full(42, 664, 3)
                      + "0 Tc " + Line(42, 652, "and the first of them ends here.")
                      + Full(42, 640, 4, opens: true) + "0 Tc " + Line(42, 628, "The second is two lines long.");
        var items = new[] { "the notes of the day", "a sketch of the ground", "the finds" };
        for (var i = 0; i < items.Length; i++)
            content += Line(47, 610 - i * 12, "\\225", "F1") + Line(59, 610 - i * 12, items[i]);
        content += Full(42, 568, 5, opens: true) + Full(42, 556, 6) + Full(42, 544, 7, ',') + "0 Tc " + Line(42, 532, "and the third ends here.");
        var doc = Tag(Build(content));

        var paragraphs = Blocks(doc, "P");
        var first = Assert.Single(paragraphs, p => TextOf(p).EndsWith("first of them ends here.", StringComparison.Ordinal));
        var second = Assert.Single(paragraphs, p => TextOf(p).EndsWith("The second is two lines long.", StringComparison.Ordinal));
        var third = Assert.Single(paragraphs, p => TextOf(p).EndsWith("the third ends here.", StringComparison.Ordinal));
        // Each is a paragraph of its own, its short line ending it, and set justified - the one of two lines too.
        Assert.DoesNotContain("first of them", TextOf(second));
        Assert.Equal("Justify", AlignOf(first));
        Assert.Equal("Justify", AlignOf(second));
        Assert.Equal("Justify", AlignOf(third));
        // The list's items, set in and ragged, are not.
        Assert.Equal(3, Blocks(doc, "LI").Count);
        Assert.DoesNotContain(Blocks(doc, "LI", "LBody"), i => AlignOf(i) == "Justify");
    }

    [Fact]
    public void TheTaggerReportsEachPageItAnalysesAndItsShareDoneToTheProgressHandler()
    {
        // A one-page document: the handler hears the page analysed, one of one, and the share done rising to the whole.
        var events = new List<(ProgressEventType Type, int Value, int Max)>();
        var doc = new Document(new MemoryStream(Build(Column(42, 700, "A", 6))));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings
            {
                EnableAutoTagging = true,
                CustomProgressHandler = e => events.Add((e.EventType, e.Value, e.MaxValue)),
            },
        });

        Assert.True(doc.IsTagged);
        var analysed = Assert.Single(events, e => e.Type == ProgressEventType.SourcePageAnalysed);
        Assert.Equal((1, 1), (analysed.Value, analysed.Max));
        var totals = events.Where(e => e.Type == ProgressEventType.TotalProgress).Select(e => e.Value).ToList();
        Assert.Equal(totals.OrderBy(v => v), totals);
        Assert.Equal(100, totals[^1]);
    }

    [Fact]
    public void ALongUnderlineUnderALinesTextRulesNoBlock()
    {
        // A paragraph, then a list whose first item holds an address three quarters of the column wide, underlined as a
        // link is: a rule as long as the address, 2 pt under its baseline.
        var content = Column(42, 700, "A", 3) + Line(42, 664, "To find the number of the office:")
                      + Line(47, 646, "\\225", "F1") + Line(59, 646, "Go to") + Line(95, 646, "www.surveyoffices.example/find")
                      + "0.4 w 95 644 m 275 644 l S\n"
                      + Line(47, 634, "\\225", "F1") + Line(59, 634, "Look in the book of offices, or")
                      + Line(47, 622, "\\225", "F1") + Line(59, 622, "Ask at the desk.")
                      + Column(42, 598, "B", 4);
        var doc = Tag(Build(content));

        Assert.Contains(Blocks(doc, "P"), p => TextOf(p).EndsWith("number of the office:", StringComparison.Ordinal));
        Assert.Equal(3, Blocks(doc, "LI").Count);
        // The underline is the address's: no block is ruled by it, neither the paragraph over it nor the one after the list.
        Assert.DoesNotContain(Blocks(doc, "P", "L", "LI", "LBody", "H1", "H2", "H3"), e =>
            e.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue() is { } drawn
            && drawn.Any(t => t > 0));
    }

    [Fact]
    public void ARuleUnderAHeadingsDescentAsWideAsItsColumnRulesTheHeading()
    {
        // A 14 pt bold heading as wide as the column, a rule across it 5.4 pt under its baseline - below its descent, not
        // in it: the heading's closing rule, however much of the rule its text stands over.
        var content = Column(42, 700, "A", 3) + Line(42, 660, "Completing and filing the form early", "F2", 14)
                      + "0.5 w 42 654.6 m 284 654.6 l S\n" + Column(42, 638, "B", 4);
        var doc = Tag(Build(content));

        var heading = Assert.Single(Blocks(doc, "H1", "H2", "H3", "H4", "H5", "H6"));
        var drawn = heading.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        Assert.True(drawn is { Length: 4 } d && d[1] > 0);
    }

    [Fact]
    public void AFractionInAColumnSetJustifiedIsOneFormulaThoughItsShortLinesEndParagraphs()
    {
        // The column of the test above, a fraction set out in lines within it, its sign (a pi font's, no character) a line
        // of its own between the multiplier's two: its short lines each end a paragraph in a justified column, and are one
        // Formula all the same.
        var step = Full(315, 426, 3, opens: true) + "0 Tc " + Line(315, 414, "part. It equals:");
        var fraction = "BT /F1 9 Tf 318 376 Td (Adjusted basis of) Tj 0 -10.3 Td (entire property) Tj 94.5 22.6 Td (Fair market value) Tj "
                       + "0 -10.3 Td (of contributed part) Tj 0 -15.4 Td (Fair market value) Tj 0 -10.3 Td (of entire property) Tj ET\n"
                       + "BT /F9 9.77 Tf 395.4 369.4 Td (\\002) Tj ET\n0.5 w 412.5 373 m 488.7 373 l S\n";
        var above = Full(315, 700, 0, opens: true) + Full(315, 688, 1, '-') + Full(315, 676, 2) + Full(315, 664, 3) + Full(315, 652, 4)
                    + Full(315, 640, 5) + "0 Tc " + Line(315, 628, "and the first of them ends here.");
        var under = Full(315, 320, 5, opens: true) + Full(315, 308, 6) + Full(315, 296, 7, ',') + Full(315, 284, 0) + "0 Tc " + Line(315, 272, "and the last ends here.");
        // (the page's left column, of the same prose from its top to its foot)
        var beside = string.Concat(Enumerable.Range(0, 36).Select(i => Full(42, 700 - i * 12, i, i % 5 == 1 ? '-' : null, opens: i == 0)));
        var doc = Tag(Build(beside + above + step + "/Fm1 Do\n" + under, form: fraction));

        var formula = Assert.Single(Blocks(doc, "Formula"));
        var text = formula.GetMarkedContent().Where(i => i.Kind == MarkedContentKind.Text).Select(i => i.Text.Trim()).ToList();
        foreach (var line in new[] { "Adjusted basis of", "entire property", "Fair market value", "of contributed part", "of entire property" })
            Assert.Contains(line, text);
        Assert.DoesNotContain(Blocks(doc, "P"), p => TextOf(p).Contains("Fair market") || TextOf(p).Contains("entire property"));
    }

    [Fact]
    public void TextBesideAFigureStaysInItsColumnAndStatesNoIndent()
    {
        // Two columns for three rows, then the left column gives way to a picture while the
        // right column goes on: its lines start where the column does, not 254 pt in.
        var content = Column(42, 700, "L", 2) + Line(42, 676, "and the left column ends here. L02") + Column(296, 700, "R", 3)
                      + "q 240 0 0 100 42 560 cm /Im1 Do Q\n" + Column(296, 664, "S", 5);
        var doc = Tag(Build(content));

        var beside = Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("S04"));
        Assert.Null(Layout(beside, AttributeKey.StartIndent));
        Assert.Null(Layout(beside, AttributeKey.TextIndent));
        Assert.DoesNotContain("L0", TextOf(beside));
    }

    [Fact]
    public void TextBesideATableInTheOtherColumnIsAColumnOfItsOwn()
    {
        // Left: a ruled table (a cover's title box); right: a column of prose for 5 rows. The
        // prose is one paragraph starting where its column does.
        var content = "0.75 w 42 640 240 60 re S\n" + Line(46, 686, "Publication 463", "F2", 12) + Line(46, 668, "Travel, Gift, and Car", "F2", 12)
                      + Line(46, 650, "Expenses", "F2", 12) + Column(296, 700, "R", 5);
        var doc = Tag(Build(content));

        var prose = Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("R00"));
        Assert.EndsWith("R04", TextOf(prose));
        Assert.Null(Layout(prose, AttributeKey.StartIndent));
        Assert.Null(Layout(prose, AttributeKey.TextIndent));
    }

    [Fact]
    public void AJustifiedBlockNarrowerThanItsColumnIsOneParagraphWithAnEndIndent()
    {
        // A body paragraph 83 characters wide sets the column (42..540); an abstract of 60
        // characters set in at 90 (90..450) follows, its last line short.
        var content = Column(42, 700, "A", 4, 83) + Column(90, 640, "B", 3, 60) + Line(90, 604, "the abstract ends here B03");
        var doc = Tag(Build(content));

        var paragraphs = Blocks(doc, "P");
        Assert.Equal(2, paragraphs.Count);
        var abstractBlock = paragraphs[1];
        Assert.EndsWith("ends here B03", TextOf(abstractBlock));
        Assert.Equal("Justify", abstractBlock.Attributes.GetAttributes(AttributeOwnerStandard.Layout)
            .GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
        Assert.Equal(48, Layout(abstractBlock, AttributeKey.StartIndent)!.Value, 0);
        Assert.Equal(90, Layout(abstractBlock, AttributeKey.EndIndent)!.Value, 0);
    }

    [Fact]
    public void BibliographyEntriesWithAHangingIndentAreOneParagraphEach()
    {
        // Two entries: the first line of each starts at 42 and runs full, the lines after start
        // at 60; the first entry's first line breaks a word over the line.
        var content = Line(42, 700, "Alice Amber and Bob Basalt. 2019. Convolu-") + Line(60, 688, "tional sequence to sequence learning. In")
                      + Line(60, 676, "Proceedings of the meadow, pages 1-9.")
                      + Line(42, 664, "Carol Cedar. 2020. Delta harbor fjord lagoon") + Line(60, 652, "and juniper. Journal of Willow, 4(2).");
        var doc = Tag(Build(content));

        var paragraphs = Blocks(doc, "P").Select(TextOf).ToList();
        Assert.Equal(2, paragraphs.Count);
        Assert.StartsWith("Alice Amber", paragraphs[0]);
        Assert.Contains("Convolutional sequence", paragraphs[0]);
        Assert.EndsWith("pages 1-9.", paragraphs[0]);
        Assert.Equal("Carol Cedar. 2020. Delta harbor fjord lagoon and juniper. Journal of Willow, 4(2).", paragraphs[1]);
        var first = Blocks(doc, "P")[0];
        Assert.Equal(18, Layout(first, AttributeKey.StartIndent)!.Value, 0);
        Assert.Equal(-18, Layout(first, AttributeKey.TextIndent)!.Value, 0);
    }

    [Fact]
    public void ShortLinesAtHeadingSizeAreAHeadingEach()
    {
        // Body text at 10 pt; two names at 18 pt one under the other, both flush right.
        var content = Column(42, 600, "A", 3, 83)
                      + Line(540 - 10 * 10.8, 700, "Scott Rose", "F1", 18)
                      + Line(540 - 15 * 10.8, 678, "Oliver Borchert", "F1", 18);
        var doc = Tag(Build(content));

        var headings = Blocks(doc, "H1").Select(TextOf).ToList();
        Assert.Equal(new[] { "Scott Rose", "Oliver Borchert" }, headings);
    }

    [Fact]
    public void AGridOfCentredNamesAndAffiliationsIsNoTable()
    {
        // Three authors in three columns, each column's name, affiliation and address centred
        // on it (Courier: 6 pt per character).
        var content = "";
        foreach (var centre in new[] { 120.0, 306.0, 492.0 })
            content += Line(centre - 42, 700, "Ashish Vaswani") + Line(centre - 36, 688, "Google Brain") + Line(centre - 57, 676, "avaswani@google.com");
        content += Column(42, 640, "P", 3, 80);
        var doc = Tag(Build(content));

        Assert.Empty(Blocks(doc, "Table"));
        Assert.Contains(Blocks(doc, "P"), p => TextOf(p).Contains("Google Brain"));
    }

    [Fact]
    public void TwoColumnsOfProseUnderAOneColumnBlockAreNoTable()
    {
        // An abstract across the page, then two columns of prose for three lines only: too few
        // for the page to show its gutter, and no table either.
        var content = Column(42, 700, "A", 4, 80) + Column(42, 640, "L", 3, 38) + Column(290, 640, "R", 3, 38);
        var doc = Tag(Build(content));

        Assert.Empty(Blocks(doc, "Table"));
        var paragraphs = Blocks(doc, "P").Select(TextOf).ToList();
        Assert.Contains(paragraphs, p => p.Contains("L00") && p.EndsWith("L02"));
        Assert.Contains(paragraphs, p => p.Contains("R00") && p.EndsWith("R02"));
    }

    [Fact]
    public void TheGapAfterAListsLabelsIsNoGutterAndTheListStatesItsSpace()
    {
        // Bullets 10 pt before their text, three of them at a 23 pt pitch under a paragraph.
        var content = Column(42, 760, "P", 3);
        for (var i = 0; i < 3; i++)
            content += Line(64, 700 - i * 23, "\\225", "F1") + Line(78, 700 - i * 23, $"Item {i} of the list", "F1");
        var doc = Tag(Build(content));

        var items = Blocks(doc, "LI");
        Assert.Equal(3, items.Count);
        Assert.Equal("Item 1 of the list", TextOf(items[1]).TrimStart('\u2022', ' '));
        Assert.DoesNotContain(Blocks(doc, "P"), p => TextOf(p) == "\u2022");
        Assert.True((Layout(Assert.Single(Blocks(doc, "L")), AttributeKey.SpaceBefore) ?? 0) > 10);
    }

    [Fact]
    public void ANumberEndingASentenceAtTheStartOfALineIsNoListLabel()
    {
        var prose = Line(42, 700, "Where you file a paper return depends on") + Line(42, 688, "whether you include a payment with Form")
                    + Line(42, 676, "941. Mail your return to the address listed") + Line(42, 664, "for your place in the table that follows.");
        var doc = Tag(Build(prose + Column(42, 640, "B", 6)));

        Assert.Empty(Blocks(doc, "L", "LI"));
        Assert.Contains(Blocks(doc, "P"), p => TextOf(p).Replace("  ", " ").Contains("a payment with Form 941. Mail your return"));
    }

    [Fact]
    public void ANumberClosingABracketUnderAParagraphsFirstLineSetInIsNoListLabel()
    {
        // The paragraph's first line is set in and ends "(Form"; its second starts "941)" back at the column's edge.
        var prose = Line(54, 652, "Do not change the sums you gave on \\(Form") + Line(42, 640, "941\\) by what the later forms report now.");
        var doc = Tag(Build(Column(42, 700, "A", 3) + prose + Column(42, 616, "B", 4)));

        Assert.Empty(Blocks(doc, "L", "LI"));
        Assert.Contains(Blocks(doc, "P"), p => TextOf(p).Replace("  ", " ").Contains("you gave on (Form 941) by what the later"));
    }

    [Fact]
    public void NumberedParagraphsAfterAListOfBulletsAreParagraphsAndNoItemsOfIt()
    {
        // Two bullets, then "15d." and "15e.", each opening a paragraph whose later lines start where its number does.
        var bullets = Line(42, 700, "\\225", "F1", 11) + Line(52, 700, "The number on a slip is another one.")
                      + Line(42, 688, "\\225", "F1", 11) + Line(52, 688, "The number of a cheque is the right one.");
        var numbered = Line(42, 670, "15d. Kind of account. Tick the box for") + Line(42, 658, "the kind of account the sum is paid into.")
                       + Line(42, 640, "15e. Number of account. The number is") + Line(42, 628, "up to seventeen characters, left to right.");
        var doc = Tag(Build(bullets + numbered + Column(42, 604, "B", 5)));

        Assert.Equal(2, Blocks(doc, "LI").Count);
        Assert.DoesNotContain(Blocks(doc, "LI"), i => TextOf(i).Contains("15d.") || TextOf(i).Contains("15e."));
        Assert.EndsWith("the sum is paid into.", TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).StartsWith("15d. Kind", StringComparison.Ordinal))));
        Assert.EndsWith("left to right.", TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).StartsWith("15e. Number", StringComparison.Ordinal))));
    }

    [Fact]
    public void ANumberedParagraphOnItsOwnItsLinesFlushWithItsNumberIsAParagraph()
    {
        // "5a." opens a paragraph whose later lines start where the number does; text follows it, no other number.
        var numbered = Line(42, 700, "5a. Taxable wages of the survey. Enter") + Line(42, 688, "the total wages and the taxable fringe of")
                       + Line(42, 676, "the party for the whole of the season, and") + Line(42, 664, "Form 941 for the pay of the sick.");
        var doc = Tag(Build(numbered + Column(42, 640, "B", 6)));

        Assert.Empty(Blocks(doc, "L", "LI"));
        var paragraph = Assert.Single(Blocks(doc, "P"), p => TextOf(p).StartsWith("5a. Taxable", StringComparison.Ordinal));
        Assert.EndsWith("Form 941 for the pay of the sick.", TextOf(paragraph));
    }

    /// <summary>A form's numbered lines in small type (7 pt Courier: 4.2 pt a character), set narrower than the column.</summary>
    private static string FormLine(double y, string label, string text) => Line(46, y, label, "F3", 7) + Line(61, y, text, "F3", 7);

    [Fact]
    public void ARuleOnALineAfterItsTextIsTheLinesBlankAnUnderlineIsTheTextsItUnderlines()
    {
        // A form's line: its text, the entry's number, and a blank to write on (its producer marked it an artifact);
        // and a word of the prose underlined.
        var form = FormLine(700, "1.", "Enter your state and local income taxes") + Line(238, 700, "1.", "F3", 7)
                   + "/Artifact BMC 0.5 w 251 699 m 293 699 l S EMC\n";
        var underline = "0.5 w 42 648 m 120 648 l S\n";
        var doc = Tag(Build(form + Column(42, 650, "B", 6) + underline + Column(296, 700, "R", 10)));

        var item = Assert.Single(Blocks(doc, "LI", "P"), b => TextOf(b).Contains("income taxes"));
        var blank = Assert.Single(item.GetMarkedContent(true), i => i.Kind == MarkedContentKind.Drawing);
        Assert.InRange(blank.Rectangle.LLX, 250, 252);
        Assert.InRange(blank.Rectangle.URX, 292, 294);
        var rule = Assert.Single(Blocks(doc, "P").Where(b => TextOf(b).Contains("B00")).SelectMany(b => b.GetMarkedContent(true)),
            i => i.Kind == MarkedContentKind.Drawing);
        Assert.InRange(rule.Rectangle.LLX, 41, 43);
    }

    [Fact]
    public void ARepeatedSignWithItsWordOnItIsAFigureWhereverItIsDrawn()
    {
        // A "CAUTION" sign - a dark box, a white triangle, its word under the triangle - drawn twice (one form, 36 pt a side),
        // each beside a note of three lines.
        const string sign = "0 g 0 3 36 36 re f 1 g 4 11 m 18 35 l 32 11 l f 1 g BT /F1 6 Tf 3 5 Td (CAUTION) Tj ET\n";
        string Note(double top, string first) => Line(84, top, first, "F1") + Line(84, top - 12, "will reach the camp before the others do, so keep", "F1")
                                                 + Line(84, top - 24, "the boxes of the day apart from the rest of them.", "F1");
        var page = "q 1 0 0 1 42 690 cm /Fm1 Do Q\n" + Note(718, "The finds of the ridge can take up to a week and")
                   + Column(42, 640, "B", 10)
                   + "q 1 0 0 1 42 400 cm /Fm1 Do Q\n" + Note(428, "The finds of the ford can take up to a month and");
        var doc = Tag(Build(page, form: sign, formBox: "0 0 39 39"));

        var figures = Blocks(doc, "Figure").Where(f => f.GetMarkedContent(true).Any()).ToList();
        Assert.Equal(2, figures.Count);
        // The sign's word is its label, no line of the notes.
        Assert.DoesNotContain(Blocks(doc, "P"), p => TextOf(p).Contains("CAUTION"));
    }

    [Fact]
    public void ASignAtTheFootOfTheLeftColumnIsReadThereNotUnderTheRightColumnsHeading()
    {
        // Two columns: the left one prose, a note at its foot beside a "CAUTION" sign (a form, 36 pt a side, at 42-78); the
        // right one opening with a heading high on the page, prose under it.
        const string form = "0 g 0 3 36 36 re f 1 g 4 11 m 18 35 l 32 11 l f 1 g BT /F1 6 Tf 3 5 Td (CAUTION) Tj ET\n";
        var sign = "q 1 0 0 1 42 394 cm /Fm1 Do Q\n";
        var left = Column(42, 700, "L", 20, 40) + Line(84, 428, "The finds of the ford can take a month", "F1")
                   + Line(84, 416, "to reach the camp, so keep the boxes", "F1") + Line(84, 404, "of the day apart from the rest.", "F1");
        var right = "BT /F2 16 Tf 320 700 Td (Finds and their boxes) Tj ET\n" + Column(320, 676, "R", 20, 40);
        var doc = Tag(Build(left + sign + right, form: form, formBox: "0 0 39 39"), HeadingRecognitionStrategy.Auto);

        var order = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true)
            .Where(e => e.S.Name is "P" or "Figure" or "H1" or "H2" or "H3" or "H4" && e.GetMarkedContent(true).Any()).ToList();
        var figure = order.FindIndex(e => e.S.Name == "Figure" || (e.S.Name == "P" && e.GetMarkedContent(true).All(i => i.Kind == MarkedContentKind.Drawing)));
        var note = order.FindIndex(e => TextOf(e).Contains("The finds of the ford"));
        var heading = order.FindIndex(e => TextOf(e).Contains("Finds and their boxes"));
        Assert.True(figure >= 0 && note >= 0 && heading >= 0, string.Join(" | ", order.Select(e => e.S.Name + ":" + TextOf(e)[..Math.Min(12, TextOf(e).Length)])));
        // The sign is read with the note beside it, in the left column, before the right column's heading.
        Assert.Equal(1, Math.Abs(figure - note));
        Assert.True(figure < heading, $"sign {figure}, heading {heading}");
    }

    [Fact]
    public void AHeadingBesideANoteOverTheColumnsIsAHeadingOfItsOwnRuledAsThePageRulesIt()
    {
        // A band over two columns: a heading at the left under a short double rule as wide as it is (2 pt over 0.5 pt), a
        // sign (a form) 36 pt right of it and a note of two lines just past the sign, a rule across under the band; then
        // each column opening with a heading under a double rule across it.
        const string sign = "0 g 0 3 36 36 re f 1 g 4 11 m 18 35 l 32 11 l f 1 g BT /F1 6 Tf 3 5 Td (CAUTION) Tj ET\n";
        var band = "2 w 42 758 m 73.5 758 l S 0.5 w 42 755.2 m 73.5 755.2 l S\n" + "BT /F2 12 Tf 42 737.5 Td (Index) Tj ET\n"
                   + "q 0.35 0 0 0.47 110 740 cm /Fm1 Do Q\n"
                   + "BT /F1 10 Tf 131.5 749 Td (To help us keep the finds of the survey in order, tell us of any we left out.) Tj ET\n"
                   + "BT /F1 10 Tf 131.5 738 Td (See the notes of the camp for the ways you can reach us.) Tj ET\n"
                   + "0.5 w 42 731 m 570 731 l S\n";
        var columns = "2 w 42 717 m 297 717 l S 0.5 w 42 714.7 m 297 714.7 l S 2 w 315 717 m 570 717 l S 0.5 w 315 714.7 m 570 714.7 l S\n"
                      + "BT /F2 12 Tf 42 703 Td (A) Tj ET\n" + "BT /F2 12 Tf 315 703 Td (F) Tj ET\n";
        // (entries of their own lengths in each column, not level across: columns, no table)
        string[] left = ["Amber finds 12", "Boxes of the day 30", "Camp notes 4", "Dawn walks along the ridge 18", "Flints by the ford 7", "Hearth stones 22", "Hills and their paths 6", "Juniper by the river 19"];
        string[] right = ["Ford crossings 9", "Fords and their banks 14", "Fragments of pottery 3", "Frost on the hills 27", "Furrows 11"];
        for (var k = 0; k < left.Length; k++) columns += Line(42, 688 - 11.5 * k, left[k], "F1");
        for (var k = 0; k < right.Length; k++) columns += Line(315, 682 - 14 * k, right[k], "F1");
        var doc = Tag(Build(band + columns, form: sign, formBox: "0 0 39 39"), HeadingRecognitionStrategy.Auto);

        var order = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true)
            .Where(e => e.S.Name is "P" or "Figure" || e.S.Name.StartsWith("H", StringComparison.Ordinal) && e.S.Name.Length <= 2)
            .Where(e => e.GetMarkedContent(true).Any()).ToList();
        double?[]? Thickness(StructureElement e) => e.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        string? Style(StructureElement e) => e.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderStyle)?.GetNameValue()?.ToString();
        // The heading is a block of its own, the note another after it: the band parts at the gap between them.
        var heading = Assert.Single(order, e => TextOf(e) == "Index");
        Assert.StartsWith("H", heading.S.Name);
        var note = Assert.Single(order, e => TextOf(e).Contains("tell us of any"));
        Assert.True(order.IndexOf(note) > order.IndexOf(heading));
        // The heading is ruled above by its double rule; the rule across under the band closes the note, not the heading.
        Assert.Equal("Double", Style(heading));
        Assert.Equal(2, Thickness(heading)![0]);
        Assert.Equal(0, Thickness(heading)![1] ?? 0);
        Assert.Equal(0.5, Thickness(note)![1]);
        // Each column's heading is ruled above by the double rule over it.
        foreach (var letter in new[] { "A", "F" })
        {
            var head = Assert.Single(order, e => TextOf(e) == letter);
            Assert.Equal("Double", Style(head));
            Assert.Equal(2, Thickness(head)![0]);
        }
    }

    [Fact]
    public void LinkUnderlinesAndARuleUnderAColumnsEntriesAreNoDrawingTheEntriesStayItsText()
    {
        // A column of index entries 11.5 pt apart, each ending in a page number underlined as a link (a line under it), and
        // a double rule across the column just under the last (5.8 pt): straight lines all running across, no drawing. (The
        // entries are near one length: the lines under their numbers stand within a few points of each other.)
        string[] entries = ["Dawn walks by it 8", "Deduction limits 22", "Determining age 17", "Donor finds box 10", "Drawings of it 5"];
        var column = "";
        var helvetica = Aspose.Pdf.Text.FontRepository.FindFont("Helvetica");
        for (var k = 0; k < entries.Length; k++)
        {
            var y = 700 - 11.5 * k;
            var number = entries[k][(entries[k].LastIndexOf(' ') + 1)..];
            var start = 42 + helvetica.MeasureString(entries[k][..^number.Length], 10);
            column += Line(42, y, entries[k], "F1") + FormattableString.Invariant($"0.5 w {start:F1} {y - 1.2:F1} m {start + helvetica.MeasureString(number, 10):F1} {y - 1.2:F1} l S\n");
        }
        column += "2 w 42 647 m 297 647 l S 0.5 w 42 644.7 m 297 644.7 l S\n" + "BT /F2 12 Tf 42 630 Td (E) Tj ET\n" + Line(42, 616, "Easement of the ford 14", "F1");
        var doc = Tag(Build(column), HeadingRecognitionStrategy.Auto);

        Assert.Empty(Blocks(doc, "Figure"));
        var text = string.Join(" ", Blocks(doc, "P", "LI", "H1", "H2", "H3", "H4", "H5", "H6").Select(TextOf));
        foreach (var entry in entries) Assert.Contains(entry, text);
    }

    [Fact]
    public void ABlankDrawnUnderItsLinesBaselineIsTheLinesBlank()
    {
        // A worksheet's numbered lines at 8 pt, their leader dots set smaller, each with a blank to write on drawn 3.35 pt
        // under its baseline (under 0.4 of the line's size, well above the next line's baseline 12 pt lower).
        var lines = "";
        for (var k = 0; k < 3; k++)
        {
            var y = 700 - 12 * k;
            lines += Line(46, y, $"{k + 4}.", "F1", 8) + Line(61, y, $"Subtract line {k + 1} from line {k + 2}", "F1", 8)
                     + FormattableString.Invariant($"BT /F1 6.4 Tf 205 {y} Td (....................) Tj ET\n")
                     + FormattableString.Invariant($"0.5 w 253 {y - 3.35} m 293 {y - 3.35} l S\n");
        }
        var doc = Tag(Build(lines + Column(42, 640, "B", 6) + Column(296, 700, "R", 10)));

        foreach (var k in new[] { 1, 2, 3 })
        {
            var entry = Assert.Single(Blocks(doc, "LI", "P"), b => TextOf(b).Contains($"Subtract line {k} "));
            var blank = Assert.Single(entry.GetMarkedContent(true), i => i.Kind == MarkedContentKind.Drawing);
            Assert.InRange(blank.Rectangle.LLX, 252, 254);
        }
    }

    [Fact]
    public void ARuleUnderTheTextOfTheNextColumnIsNoBlankOfTheLineBesideIt()
    {
        // Two columns sharing their baselines, a gutter of 14 pt between them; the first words of the right
        // column's last line are underlined, the rule starting just after the left column's line ends.
        var underline = "0.5 w 296 590 m 380 590 l S\n";
        var doc = Tag(Build(Column(42, 700, "L", 10) + Column(296, 700, "R", 10) + underline));

        Assert.DoesNotContain(Blocks(doc, "P", "LI").Where(b => TextOf(b).Contains("L09")).SelectMany(b => b.GetMarkedContent(true)),
            i => i.Kind == MarkedContentKind.Drawing);
    }

    [Fact]
    public void ANumberCarryingALetterIsAListLabel()
    {
        var form = FormLine(700, "7a.", "Subtract line 6 from line 5") + FormLine(690, "7b.", "Is line 5 less than or equal to 10,000")
                   + FormLine(680, "7c.", "Is line 7a greater than or equal to 40,000");
        var doc = Tag(Build(form + Column(42, 650, "B", 6)));

        var labels = Blocks(doc, "Lbl").Select(TextOf).ToList();
        Assert.Equal(new[] { "7a.", "7b.", "7c." }, labels);
    }

    [Fact]
    public void AWordALaterLineOfAnItemStartsWithFlushWithItsLabelIsItsText()
    {
        // A bullet at 42, its text at 54; the item's second line - one word - starts under the bullet.
        var list = Line(42, 700, "\u0095", "F3") + Line(54, 700, "If this is the first time, go on to Step", "F3")
                   + Line(42, 688, "5.", "F3") + Line(42, 676, "\u0095", "F3") + Line(54, 676, "If you are repeating it, compare the two.", "F3");
        var doc = Tag(Build(Column(42, 740, "A", 3) + list + Column(42, 640, "B", 3)));

        Assert.All(Blocks(doc, "Lbl"), l => Assert.DoesNotContain("5.", TextOf(l)));
        Assert.Contains(Blocks(doc, "LBody"), b => TextOf(b).EndsWith("Step 5.", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoColumnsSharingTheirBaselinesAroundAFewShortLinesAreNoTable()
    {
        // The left column a worksheet's short lines in small type, 9 pt apart; the right one prose, 12 pt apart. Two of
        // the worksheet's lines, 12 pt apart, share their baselines with the prose beside them.
        var sb = new StringBuilder();
        for (var i = 0; i < 8; i++) sb.Append(FormLine(700 - 9 * i, $"{i + 1}.", "Enter the amount from the line"));
        sb.Append(Line(61, 628, "Yes. Enter the amount", "F3", 7)).Append(Line(61, 616, "on line 8. Then go on.", "F3", 7));
        for (var i = 0; i < 8; i++) sb.Append(FormLine(604 - 9 * i, $"{i + 9}.", "Enter the amount from the line"));
        var doc = Tag(Build(sb + Column(296, 700, "R", 14)));

        Assert.Empty(Blocks(doc, "Table"));
    }

    [Fact]
    public void ALinkToADestinationInTheDocumentIsALink()
    {
        // A link with no action, leading to a place on the page by its /Dest.
        var link = "<< /Type /Annot /Subtype /Link /Rect [179 456 223 470] /Border [0 0 0] /Dest [7 0 R /XYZ 42 700 0] >>";
        var doc = Tag(Build(Column(42, 700, "L", 3) + Line(42, 460, "and the column ends at ") + Line(180, 460, "Line 29") + Line(222, 460, "."),
            $"/Annots [{link}]"));

        var anchor = Assert.Single(Blocks(doc, "Link"));
        Assert.Equal("Line 29", TextOf(anchor));
    }

    [Fact]
    public void AParagraphsLinksAreReadInTheOrderTheyStandInWhateverOrderThePageListsThem()
    {
        // Two links in one paragraph; the page lists the later one first (Courier: 6 pt a character).
        var first = Line(42, 472, "as shown under ") + Line(132, 472, "Line 29") + Line(174, 472, ", later, and then on");
        var second = Line(42, 460, "available at ") + Line(120, 460, "Line 31") + Line(162, 460, ".");
        var later = "<< /Type /Annot /Subtype /Link /Rect [119 456 163 470] /Border [0 0 0] /A << /S /URI /URI (http://notes.example/31) >> >>";
        var earlier = "<< /Type /Annot /Subtype /Link /Rect [131 468 175 482] /Border [0 0 0] /A << /S /URI /URI (http://notes.example/29) >> >>";
        var doc = Tag(Build(Column(42, 700, "L", 3) + first + second, $"/Annots [{later} {earlier}]"));

        Assert.Equal(new[] { "Line 29", "Line 31" }, Blocks(doc, "Link").Select(TextOf));
        var text = TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("as shown under")));
        Assert.EndsWith("as shown under Line 29, later, and then on available at Line 31.", text.Replace("  ", " "));
    }

    [Fact]
    public void TwoLinksToOnePlaceWithTextBetweenThemAreTwoLinksReadWhereTheyStand()
    {
        // A partner's name linked on one line, its web address linked two lines on, both to the same address
        // (Courier: 6 pt a character).
        var text = Line(42, 472, "a partner with the ") + Line(156, 472, "Fjord Center") + Line(228, 472, ". Its notes")
                   + Line(42, 460, "are kept for the valley and may be read") + Line(42, 448, "at ") + Line(60, 448, "fjord.example") + Line(138, 448, " any day.");
        var name = "<< /Type /Annot /Subtype /Link /Rect [155 468 229 482] /Border [0 0 0] /A << /S /URI /URI (http://fjord.example/) >> >>";
        var address = "<< /Type /Annot /Subtype /Link /Rect [59 444 139 458] /Border [0 0 0] /A << /S /URI /URI (http://fjord.example/) >> >>";
        var doc = Tag(Build(Column(42, 700, "L", 3) + text, $"/Annots [{name} {address}]"));

        Assert.Equal(new[] { "Fjord Center", "fjord.example" }, Blocks(doc, "Link").Select(TextOf));
        var paragraph = TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("a partner with")));
        var order = new[] { "Fjord Center", ". Its notes", "may be read", "fjord.example", "any day" }
            .Select(s => paragraph.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void ALinkWrappedOverALineEndIsOneLink()
    {
        // A link ending one line and going on at the start of the next, its two annotations to one address.
        var text = Line(42, 472, "the notes are kept under ") + Line(192, 472, "Valley Survey")
                   + Line(42, 460, "Records") + Line(84, 460, " for the year.");
        var first = "<< /Type /Annot /Subtype /Link /Rect [191 468 271 482] /Border [0 0 0] /A << /S /URI /URI (http://fjord.example/records) >> >>";
        var second = "<< /Type /Annot /Subtype /Link /Rect [41 456 85 470] /Border [0 0 0] /A << /S /URI /URI (http://fjord.example/records) >> >>";
        var doc = Tag(Build(Column(42, 700, "L", 3) + text, $"/Annots [{first} {second}]"));

        Assert.Equal("Valley Survey Records", TextOf(Assert.Single(Blocks(doc, "Link"))));
    }

    [Fact]
    public void AParagraphGoingOnAtTheTopOfTheNextColumnReadsOnAfterItsLink()
    {
        // Two columns; the left one's paragraph has a link on its last line there and goes on at the top of the
        // right column, standing higher on the page than the link (Courier: 6 pt a character).
        var last = Line(42, 460, "and the column ends at ") + Line(180, 460, "Line 29") + Line(222, 460, ", then");
        var link = "<< /Type /Annot /Subtype /Link /Rect [179 456 223 470] /Border [0 0 0] /A << /S /URI /URI (http://notes.example/29) >> >>";
        var right = Line(296, 700, "of the finds counted on the ridge above") + Column(296, 688, "R", 19);
        var doc = Tag(Build(Column(42, 700, "L", 20) + last + right, $"/Annots [{link}]"));

        var paragraph = Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("column ends at"));
        var text = TextOf(paragraph);
        var order = new[] { "column ends at", "Line 29", ", then", "of the finds" }.Select(s => text.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLinksOfAParagraphGoingOnInTheNextColumnAreReadTheLeftColumnsFirst(bool laterListedFirst)
    {
        // The paragraph has a link on its last line in the left column and one on its first line in the right
        // column, which stands higher on the page.
        var last = Line(42, 460, "and the column ends at ") + Line(180, 460, "Line 29") + Line(222, 460, ", then");
        var left = "<< /Type /Annot /Subtype /Link /Rect [179 456 223 470] /Border [0 0 0] /A << /S /URI /URI (http://notes.example/29) >> >>";
        var first = Line(296, 700, "of the finds see ") + Line(398, 700, "Line 31") + Line(440, 700, " on the ridge");
        var right = "<< /Type /Annot /Subtype /Link /Rect [397 696 441 710] /Border [0 0 0] /A << /S /URI /URI (http://notes.example/31) >> >>";
        var annots = laterListedFirst ? $"{right} {left}" : $"{left} {right}";
        var doc = Tag(Build(Column(42, 700, "L", 20) + last + first + Column(296, 688, "R", 19), $"/Annots [{annots}]"));

        Assert.Equal(new[] { "Line 29", "Line 31" }, Blocks(doc, "Link").Select(TextOf));
        var text = TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("column ends at")));
        var order = new[] { "column ends at", "Line 29", ", then", "of the finds see", "Line 31", "on the ridge" }
            .Select(s => text.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void AHeadingUnderATitleAcrossTheColumnsStatesTheSpaceBetweenThem()
    {
        // A title across the page, a subtitle in the left column 16 pt under its foot, then two columns.
        var content = Line(42, 740, "Instructions for the Northern Valley Survey", "F2", 20)
                      + Line(42, 700, "Expenses of the Field Party", "F2", 12)
                      + Column(42, 670, "L", 20) + Column(296, 670, "R", 20);
        var doc = Tag(Build(content));

        var subtitle = Assert.Single(Blocks(doc, "P", "H1", "H2", "H3", "H4", "H5", "H6"), b => TextOf(b).StartsWith("Expenses"));
        Assert.InRange(Layout(subtitle, AttributeKey.SpaceBefore) ?? 0, 8, 30);
    }

    [Fact]
    public void AParagraphAfterAFigureStatesTheSpaceBelowTheFigureNotAcrossIt()
    {
        // A paragraph, a picture 40 pt under it, a paragraph 10 pt under the picture.
        var content = Column(42, 760, "P", 2) + "q 240 0 0 100 42 608 cm /Im1 Do Q\n" + Column(42, 588, "Q", 2);
        var doc = Tag(Build(content));

        var figure = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<FigureElement>(true));
        Assert.InRange(Layout(figure.ParentElement!, AttributeKey.SpaceBefore) ?? 0, 34, 42);
        var after = Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("Q00"));
        Assert.InRange(Layout(after, AttributeKey.SpaceBefore) ?? 0, 0, 12);
    }

    [Fact]
    public void AFaceNamingItsWeightInTwoLettersIsBold()
    {
        // F1 renamed in place (the same length keeps the cross-reference offsets right).
        const string helvetica = "/BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";
        var bd = "/BaseFont /HelveticaNeueLTPro-Bd".PadRight(helvetica.Length - 2) + ">>";
        var pdf = Compat.Latin1.GetString(Build(Line(42, 700, "FEDERAL RESERVE statistical release", "F1", 22)));
        var doc = Tag(Compat.Latin1.GetBytes(pdf.Replace(helvetica, bd)));

        var item = Assert.Single(Blocks(doc, "H1", "H2", "P").SelectMany(b => b.GetMarkedContent()));
        Assert.True(item.IsBold);
    }

    [Fact]
    public void ARuleOverAHeadingAtAPagesTopOpensItBesideAParagraphGoingOnInTheNextColumn()
    {
        // Two columns: a rule over the left column's first heading; the left column's last paragraph goes on at the top
        // of the right column, which starts higher than the heading - its lines stand over the whole page.
        var heading = "0.5 w 42 742 m 282 742 l S\n" + Line(42, 723, "Field Notes", "F2", 17);
        var right = Line(296, 760, "of the finds counted on the ridge above") + Column(296, 748, "R", 20);
        var doc = Tag(Build(heading + Column(42, 700, "L", 20) + right));

        var title = Assert.Single(Blocks(doc, "H1", "H2", "H3"), h => TextOf(h) == "Field Notes");
        var ruled = title.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        Assert.NotNull(ruled);
        Assert.True(ruled![0] > 0, $"ruled above: [{string.Join(",", ruled)}]");
    }

    [Fact]
    public void ALabelTheProducerMarkedAsAPageArtifactOnALineOfTextIsItsLinesTextOnce()
    {
        // An entry of a list: its label drawn as a pagination artifact (after the entry's text), a hidden copy of the
        // label set 0.01 pt tall where the text starts, then the entry's text.
        var entry = "BT /F1 0.01 Tf 96 460 Td (561 ) Tj ET\n" + "BT /F1 10 Tf 96 460 Td (Determining the value of donated property) Tj ET\n"
                    + "/Artifact << /Type /Pagination >> BDC BT /F2 10 Tf 72 460 Td (561 ) Tj ET EMC\n";
        var doc = Tag(Build(Column(42, 700, "L", 3) + entry));

        var text = TextOf(Assert.Single(Blocks(doc, "P", "LI", "LBody"), p => TextOf(p).Contains("Determining")));
        Assert.Equal("561 Determining the value of donated property", string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void ABoxDrawnBeforeAnEntryInTheRightColumnIsAFigureOfTheEntryNotOfTheHeadingAboveIt()
    {
        // Two columns; the right one ends with a heading and, 12 pt under it, an entry opened by a box to tick: a dark
        // square with a white one over it, both drawn with the entry's label as a pagination artifact.
        var box = "/Artifact << /Type /Pagination >> BDC 0.2 g 297 458.6 5.9 6 re f 1 g 0 G 0.09 w 296 457.8 6 6 re B\n"
                  + "0 g BT /F2 10 Tf 306 460 Td (561 ) Tj ET EMC\n";
        var entry = box + "BT /F1 10 Tf 330 460 Td (Determining the value of donated property) Tj ET\n";
        var doc = Tag(Build(Column(42, 700, "L", 22) + Column(296, 700, "R", 18) + Line(296, 472, "Publication", "F2", 10) + entry));

        var figure = Assert.Single(doc.TaggedContent.StructTreeRootElement.FindElements<FigureElement>(true));
        Assert.Contains("Determining", TextOf(figure.ParentElement!));
    }

    [Fact]
    public void ARuleBetweenAListAndAHeadingUnderItOpensTheHeading()
    {
        // A numbered list, 19 pt under its last item a rule across the column, a heading 1 pt under the rule, prose.
        var list = Line(47, 560, "1.") + Line(60, 560, "The value claimed is twice the correct amount.")
                   + Line(47, 546, "2.") + Line(60, 546, "You underpaid your tax because of it.");
        var heading = "0.5 w 42 525 m 297 525 l S\n" + Line(42, 508, "When To Deduct", "F2", 17);
        var doc = Tag(Build(Column(42, 700, "L", 10) + list + heading + Column(42, 480, "M", 8)));

        var title = Assert.Single(Blocks(doc, "H1", "H2", "H3"), h => TextOf(h) == "When To Deduct");
        var ruled = title.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        Assert.NotNull(ruled);
        Assert.True(ruled![0] > 0, $"ruled above: [{string.Join(",", ruled)}]");
    }

    [Fact]
    public void ARuleOverTheNextColumnsHeadingIsNoBlankOfTheLineLevelWithIt()
    {
        // Two columns: the left one's paragraph ends in a short line; level with it a rule opens the right column's heading,
        // starting 33 pt after the line's end - within a blank's reach, but in the next column.
        var left = Column(42, 700, "L", 12) + Line(42, 556, "you use the cash or an accrual method, as");
        var right = Column(315, 700, "R", 10) + "0.5 w 315 558 m 570 558 l S\n" + Line(315, 541, "Limits on Deductions", "F2", 17)
                    + Column(315, 515, "S", 8);
        var doc = Tag(Build(left + right));

        Assert.DoesNotContain(Blocks(doc, "P").Where(p => TextOf(p).Contains("accrual")).SelectMany(p => p.GetMarkedContent(true)),
            i => i.Kind == MarkedContentKind.Drawing);
        var title = Assert.Single(Blocks(doc, "H1", "H2", "H3"), h => TextOf(h) == "Limits on Deductions");
        var ruled = title.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
        Assert.True(ruled is { } r && r[0] > 0);
    }

    [Fact]
    public void AFractionSetOutInLinesIsOneFormulaWithItsMultiplierAndItsBar()
    {
        // A paragraph ending "It equals:", then a fraction set out in lines, drawn in a form in one text object term by term: the
        // multiplier's two lines at the left, the numerator's two lines over a rule, the denominator's two under it.
        var step = Line(327, 402, "Step 2. Find the adjusted basis of") + Line(315, 390.5, "part. It equals:");
        var fraction = "BT /F1 9 Tf 318 364 Td (Adjusted basis of) Tj 0 -10.3 Td (entire property) Tj 94.5 22.6 Td (Fair market value) Tj "
                       + "0 -10.3 Td (of contributed part) Tj 0 -15.4 Td (Fair market value) Tj 0 -10.3 Td (of entire property) Tj ET\n"
                       + "0.5 w 412.5 361 m 488.7 361 l S\n";
        var doc = Tag(Build(Column(315, 470, "A", 5) + step + "/Fm1 Do\n" + Column(315, 320, "B", 8), form: fraction));

        var formula = Assert.Single(Blocks(doc, "Formula"));
        var text = formula.GetMarkedContent().Where(i => i.Kind == MarkedContentKind.Text).Select(i => i.Text.Trim()).ToList();
        Assert.Contains("of entire property", text);
        Assert.Contains("Adjusted basis of", text);
        Assert.Contains(formula.GetMarkedContent(), i => i.Kind == MarkedContentKind.Drawing);
        Assert.DoesNotContain("Adjusted", TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("equals"))));
    }

    [Fact]
    public void ABoldLeadRunningOnIntoItsParagraphsNextLineIsNoHeadingEvenWhenBoldLinesAreHeadings()
    {
        // Prose, then a paragraph led by bold words over its first line and into its second, which turns to the body's
        // type; headings recognised by weight too.
        var lead = Line(54, 588, "Filing deadline approaching and no", "F2") + Line(42, 576, "form.", "F2")
                   + Line(78, 576, "If the deadline is near and you still", "F1") + Line(42, 564, "have no form, you have two choices.", "F1");
        var doc = Tag(Build(Column(42, 700, "L", 8) + lead + Column(42, 540, "M", 8)), HeadingRecognitionStrategy.Auto);

        Assert.DoesNotContain(Blocks(doc, "H1", "H2", "H3", "H4", "H5", "H6"), h => TextOf(h).Contains("Filing"));
        Assert.Contains(Blocks(doc, "P"), p => TextOf(p).Contains("Filing deadline") && TextOf(p).Contains("two choices"));
    }

    [Fact]
    public void AHalfBuiltOfGlyphsReadsInItsPlaceOnItsLine()
    {
        // A paragraph's second line holds "70 1/2" built of glyphs: the 1 at 7 pt set 2.3 pt over the line, the slash at the
        // text's size, the 2 at 7 pt on the line.
        var half = Line(42, 688, "been at least age 70", "F1") + Line(135.4, 690.3, "1", "F1", 7) + Line(139.3, 688, "/", "F1")
                   + Line(142.1, 688, "2", "F1", 7) + Line(146, 688, " when the distribution was made.", "F1");
        var doc = Tag(Build(Line(42, 700, "You must have", "F1") + half + Line(42, 676, "Your total for the year is capped.", "F1")
                            + Column(42, 640, "L", 10)));

        var text = TextOf(Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("been at least")));
        Assert.Contains("age701/2when", text.Replace(" ", ""));
    }

    [Fact]
    public void LinesOfBulletedEntriesSetAcrossInColumnsAreNoListItems()
    {
        // Under a column of prose, a box ruled round a line across it and two lines each holding three bulleted
        // entries across it.
        static string Row(double y, string a, string b, string c)
            => Line(77, y, "\\225", "F1") + Line(84, y, a) + Line(241, y, "\\225", "F1") + Line(248, y, b)
               + Line(392, y, "\\225", "F1") + Line(399, y, c);
        var doc = Tag(Build(Column(42, 700, "L", 12) + "0.5 w 70 515 430 57 re S\n"
                            + Line(77, 555, "Get the survey's notes and maps, in the language you read, at:")
                            + Row(540, "Notes (English)", "Notes (French)", "Notes (German)")
                            + Row(528, "Maps (English)", "Maps (French)", "Maps (German)")));

        Assert.Empty(Blocks(doc, "L", "LI"));
        Assert.Contains(Blocks(doc, "P"), p => TextOf(p).Contains("Notes (English)") && TextOf(p).Contains("Maps (German)"));
    }

    [Fact]
    public void AnEntryWhoseMiddleFallsOnTheColumnsByChanceStartsWhereTheEntryUnderItStarts()
    {
        // A column 42-282 pt; under it two entries set in to 64 pt, 20 pt apart: the first 198 pt long (its middle 1 pt off
        // the column's), the second shorter.
        var entries = Line(64, 560, "Schedule of itemized deductions A") + Line(64, 540, "Noncash contributions");
        var doc = Tag(Build(Column(42, 700, "L", 10) + entries));

        var first = Assert.Single(Blocks(doc, "P"), p => TextOf(p).StartsWith("Schedule"));
        Assert.Null(first.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign));
        Assert.InRange(Layout(first, AttributeKey.StartIndent) ?? 0, 21, 23);
    }

    [Fact]
    public void AHeadingOfAFewWordsBesideTheOtherColumnsProseIsAHeadingNotATable()
    {
        // Left column: prose, a heading of three short lines at 17 pt (16 pt apart), prose. Right column: prose set in to
        // 333 pt beside the heading (an item's lines going on; its last two level with the heading's), then prose from 315 pt - the column's own edge.
        var heading = Line(42, 612, "Places That Qualify To", "F2", 17) + Line(42, 596, "Receive Gifts", "F2", 17)
                      + Line(42, 580, "Contributions", "F2", 17);
        var left = Column(42, 700, "L", 7) + heading + Column(42, 552, "M", 8);
        var right = Column(333, 700, "R", 9) + Column(333, 596, "Q", 2, pitch: 16) + Column(315, 556, "S", 8);
        var doc = Tag(Build(left + right));

        Assert.Empty(Blocks(doc, "Table"));
        var title = Assert.Single(Blocks(doc, "H1", "H2", "H3"), h => TextOf(h).StartsWith("Places"));
        Assert.Contains("Contributions", TextOf(title));
    }

    /// <summary>One ruled box of two, 8 pt print (Courier, 4.8 pt a character) from <paramref name="left"/> (its text
    /// 6 pt in): a heading of two lines centred on the box, the second underlined, an entry line, bulleted entries
    /// ("|" parts an entry's lines) from <paramref name="top"/> down, 10 pt apart, and a paragraph under them.</summary>
    private static string Box(double left, string heading, double top, params string[] entries)
    {
        double Centred(string text) => left + 132 - text.Length * 2.4;
        const string second = "Charitable Contributions";
        var text = left + 6;
        var sb = new StringBuilder(Line(Centred(heading), 688, heading, "F3", 8) + Line(Centred(second), 678, second, "F3", 8)
                                   + $"0.5 w {Centred(second)} 676.5 m {Centred(second) + second.Length * 4.8} 676.5 l S\n"
                                   + Line(text, 662, "Money or property you give to:", "F3", 8));
        var y = top;
        foreach (var entry in entries)
        {
            sb.Append(Line(text + 6, y, "\\225", "F1", 8));
            foreach (var part in entry.Split('|'))
            {
                sb.Append(Line(text + 17, y, part, "F3", 8));
                y -= 10;
            }
        }
        return sb.Append(Line(text, y - 10, "Expenses paid for a student", "F3", 8)).ToString();
    }

    [Fact]
    public void TwoRuledBoxesSideBySideHoldingEntriesAreBoxesWithTheirOwnHeadingsListsAndParagraphsNotATable()
    {
        // Body text over and under the boxes; the boxes 42-306 and 306-570 pt, 580-700 pt high, one rule between them.
        // The boxes' entries stand at heights of their own.
        var boxes = "0.5 w 42 580 528 120 re S 306 580 m 306 700 l S\n"
                    + Box(42, "Deductible As", 650, "Churches, synagogues, temples,|mosques, and other houses", "Nonprofit schools;", "War veterans' groups;")
                    + Box(306, "Not Deductible As", 646, "Civic leagues, social|and sports clubs,|labor unions;", "Individuals;");
        var doc = Tag(Build(Column(42, 760, "A", 5) + boxes + Column(42, 560, "B", 20)));

        Assert.Empty(Blocks(doc, "Table", "Note"));
        var divs = Blocks(doc, "Div");
        Assert.Equal(2, divs.Count);
        foreach (var div in divs)
        {
            Assert.Single(div.ChildElements.OfType<StructureElement>(), e => e.StructureType?.Tag == "L");
            var heading = div.ChildElements.OfType<StructureElement>().First();
            Assert.Equal(AttributeName.TextAlign_Center, heading.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign)?.GetNameValue());
            Assert.DoesNotContain("Money", TextOf(heading));
        }
        // The rule under each box's heading is its underline: the heading's own content.
        var underlined = Blocks(doc, "P").Where(p => TextOf(p).Contains("Charitable Contributions")).ToList();
        Assert.Equal(2, underlined.Count);
        Assert.All(underlined, p => Assert.Contains(p.GetMarkedContent(), i => i.Kind == MarkedContentKind.Drawing));
        // The prose under the boxes stands under their foot as the PDF sets it (its first baseline 20 pt under it).
        var under = Assert.Single(Blocks(doc, "P"), p => TextOf(p).Contains("B00"));
        Assert.InRange(Layout(under, AttributeKey.SpaceBefore) ?? 0, 8, 14);
    }

    [Fact]
    public void ShortLinesEndingSentencesUnderWhichTheTextGoesOnAtItsPitchMakeNoJustifiedColumn()
    {
        // A markdown-rendered paragraph of eight lines at one pitch: every second line reaches the text's edge, the line
        // after it ends its sentence short of the edge, and the next line opens a sentence again under it, at the pitch,
        // set in nowhere. A ragged column: the short lines are evidence of it, not paragraphs' last lines, so the
        // paragraph stays one.
        const string wide = "Aaasdasdasd asd as dasd asd asd as das d. Aaasdasdasd asd as dasd asd asd as das d. Aaasdasdasd";
        const string rest = "asd as dasd asd asd as das d.";
        var content = "BT /F2 14 Tf 57 740 Td (Title 2) Tj ET\n" + string.Concat(Enumerable.Range(0, 4).Select(i =>
            Line(57, 703.4 - 27.6 * i, wide, "F1", 12) + Line(57, 689.6 - 27.6 * i, rest, "F1", 12)));
        var doc = Tag(Build(content));

        var body = Blocks(doc, "P").Where(p => TextOf(p).StartsWith("Aaasdasdasd", StringComparison.Ordinal)).ToList();
        Assert.Single(body);
        Assert.EndsWith(rest, TextOf(body[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void FiguresUnderACentredTitleAreReadUnderTheHeadingOfTheirColumnAboveThemInTheirOrder()
    {
        // A centred title over the page, two headings under it with prose, and under the second heading's prose two rows
        // of pictures: three in a row, then two in a row lower down. The pictures are read under the second heading - the
        // title's words stand over them, but the title heads the page, the heading its column - the row of three first.
        var content = "BT /F2 16 Tf 281 755 Td (Title) Tj ET\n" + "BT /F2 14 Tf 57 721 Td (Title 2) Tj ET\n" + Column(57, 700, "A", 4, 60)
                      + "BT /F2 14 Tf 57 621 Td (Title 2 ds) Tj ET\n" + Column(57, 600, "B", 4, 60)
                      + "q 98 0 0 39 90 457 cm /Im1 Do Q\nq 47 0 0 64 205 421 cm /Im1 Do Q\nq 26 0 0 15 277 466 cm /Im1 Do Q\n"
                      + "q 54 0 0 32 271 311 cm /Im1 Do Q\nq 53 0 0 32 353 310 cm /Im1 Do Q\n";
        var doc = Tag(Build(content));

        var sections = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => e.S.Name == "Sect").ToList();
        Assert.Equal(2, sections.Count);
        var second = sections[1].ChildElements.OfType<StructureElement>().ToList();
        Assert.Equal("Title 2 ds", TextOf(second[0]));
        var pictures = second.Where(e => e.S.Name == "P" && e.GetMarkedContent(true).All(i => i.Kind == MarkedContentKind.Image)).ToList();
        Assert.Equal(2, pictures.Count);
        Assert.Equal(3, pictures[0].GetMarkedContent(true).Count);
        Assert.Equal(2, pictures[1].GetMarkedContent(true).Count);
        // Nothing of the pictures stands at the part's level, under the title.
        var part = (StructureElement)sections[0].ParentElement!;
        Assert.DoesNotContain(part.ChildElements.OfType<StructureElement>(), e => e.S.Name == "P" && e.GetMarkedContent(true).Any(i => i.Kind == MarkedContentKind.Image));
    }
}
