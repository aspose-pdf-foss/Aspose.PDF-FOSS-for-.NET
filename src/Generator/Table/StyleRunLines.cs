using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The grid's mixed-weight lines (a bold label and its plain value) wrap across their runs in their
    /// own programs, as the built-in path wraps its segment runs, instead of standing on one line past the cell.</summary>
    internal bool HtmlWrapStyleRuns { get; set; }

    /// <summary>One line of style runs: each run's text, program and face name, the line's text, and its kerned
    /// width with the spaces it ends on left out (the width that aligns it).</summary>
    private readonly record struct StyleRunLine(List<(string Text, byte[] Ttf, string Name)> Runs, string Text, double Width);

    /// <summary>The runs on lines of the width by the built-in path's run wrap (spaces break across the run
    /// boundaries, a word wider than a line breaks between its characters), each piece measured in its own
    /// run's program at its kerned advance.</summary>
    private static List<StyleRunLine> WrapStyleRuns(List<(string Text, byte[] Ttf, string Name)> runs, double size, double width)
    {
        double Measure(RunPiece piece) => MeasureWidthKerned(piece.Text, size, runs[piece.Run].Ttf);
        var rules = new RunWrapRules { BreakOverlongWords = true, DropLineStartSpaces = true };
        var words = RunWordWrap.SplitIntoWords(runs.ConvertAll(r => r.Text), Measure, rules);
        var lines = new List<StyleRunLine>();
        foreach (var pieces in RunWordWrap.Wrap(words, width, 0, null, rules, Measure))
        {
            var lineRuns = new List<(string Text, byte[] Ttf, string Name)>();
            var lastRun = -1;
            foreach (var piece in pieces)
            {
                if (piece.Run == lastRun) lineRuns[^1] = (lineRuns[^1].Text + piece.Text, lineRuns[^1].Ttf, lineRuns[^1].Name);
                else lineRuns.Add((piece.Text, runs[piece.Run].Ttf, runs[piece.Run].Name));
                lastRun = piece.Run;
            }
            // (the spaces a line ends on draw nothing and take no part in its width)
            while (lineRuns.Count > 0 && lineRuns[^1].Text.TrimEnd(' ') is var kept && kept.Length < lineRuns[^1].Text.Length)
            {
                if (kept.Length > 0) { lineRuns[^1] = (kept, lineRuns[^1].Ttf, lineRuns[^1].Name); break; }
                lineRuns.RemoveAt(lineRuns.Count - 1);
            }
            if (lineRuns.Count == 0) continue;
            var lineWidth = 0.0;
            foreach (var (text, ttf, _) in lineRuns) lineWidth += MeasureWidthKerned(text, size, ttf);
            lines.Add(new StyleRunLine(lineRuns, string.Concat(lineRuns.ConvertAll(r => r.Text)), lineWidth));
        }
        return lines;
    }

    /// <summary>The runs of a fragment's mixed-weight line in the cell face's variants; null when a variant has no
    /// program.</summary>
    private static List<(string Text, byte[] Ttf, string Name)>? CellStyleRuns(List<(string Text, bool Bold)> gridRuns,
        string face, bool bold, bool italic)
    {
        var runs = new List<(string Text, byte[] Ttf, string Name)>();
        foreach (var (runText, runBold) in gridRuns)
        {
            if (CellFaceTtf(face, runBold || bold, italic) is not { } runTtf) return null;
            runs.Add((runText, runTtf, CellFaceName(face, runBold || bold, italic)));
        }
        return runs;
    }

    /// <summary>The runs of a cell line whose face lacks some of its characters: each stretch of characters the
    /// face cannot show takes the installed face that covers it, the rest keeps the requested face - otherwise the
    /// stretch draws as glyph 0. Null when the face covers the line or no installed face covers what it lacks.</summary>
    private static List<(string Text, byte[] Ttf, string Name)>? CoveringStyleRuns(string line, byte[] ttf, string name,
        Aspose.Pdf.Text.Font? font)
    {
        if (FontRepository.CoversText(ttf, line) || GetInlineGlyphParser(ttf) is not { } parser) return null;
        var runs = new List<(string Text, byte[] Ttf, string Name)>();
        var substituted = false;
        var i = 0;
        while (i < line.Length)
        {
            // A stretch: characters the face shows (spaces and controls among them), or characters it lacks.
            var lacking = Lacks(parser, line, i);
            var start = i;
            while (i < line.Length && Lacks(parser, line, i) == lacking)
                i += char.IsSurrogatePair(line, i) ? 2 : 1;
            var stretch = line.Substring(start, i - start);
            if (lacking && FontRepository.SubstituteForMissingGlyphs(stretch, font) is { TtfData: { } cover } sub)
            {
                runs.Add((stretch, cover, sub.FontName));
                substituted = true;
            }
            else if (runs.Count > 0 && ReferenceEquals(runs[^1].Ttf, ttf)) runs[^1] = (runs[^1].Text + stretch, ttf, name);
            else runs.Add((stretch, ttf, name));
        }
        return substituted ? runs : null;
    }

    /// <summary>Whether the face has no glyph for the character at <paramref name="index"/> (white space and
    /// controls draw nothing and count as shown).</summary>
    private static bool Lacks(GlyphOutlineParser parser, string line, int index)
    {
        if (char.IsWhiteSpace(line[index]) || char.IsControl(line[index])) return false;
        var cp = char.IsSurrogatePair(line, index) ? char.ConvertToUtf32(line[index], line[index + 1]) : line[index];
        return !parser.CMap.TryGetValue(cp, out var gid) || gid <= 0;
    }

    /// <summary>A line of style runs drawn run by run, each in its own face, advancing by its kerned width.</summary>
    private static void DrawStyleRuns(ContentStreamBuilder builder, PdfDictionary fontDict, CellLine line, double x, double baseline)
    {
        foreach (var (runText, runTtf, runName) in line.StyleRuns!)
        {
            var (runRes, runHex) = Type0FontEmbedder.Embed(fontDict, runTtf, runName, runText, stripSpacesInBaseFont: true);
            builder.BeginText();
            builder.SetFont(runRes, line.FontSize);
            ApplyColor(builder, line.ForegroundColor);
            builder.MoveTextPosition(x, baseline);
            if (line.KernTj && KernAdjustments(runText, runTtf) is { } runKern) builder.ShowTextHexKerned(runHex, runKern);
            else builder.ShowTextHex(runHex);
            builder.EndText();
            x += MeasureWidthKerned(runText, line.FontSize, runTtf);
        }
    }
}
