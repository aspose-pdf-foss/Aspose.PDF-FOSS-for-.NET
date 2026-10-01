using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>One run of a cell paragraph whose segments flow as runs: the
    /// segment's text in its own Standard-14 face, size and colour, and the leading
    /// and line box it brings to any line it stands on.</summary>
    private sealed class CellRun
    {
        public string Text = "";
        public string BaseFont = "Helvetica";
        public double Size;
        public double Leading;
        public double AscentEm;
        public double DescentEm;
        public Color? Color;
        public double CharSpacing;
        public double WordSpacing;

        /// <summary>The picture this run is, when it is one: a word of its own, as
        /// wide as its box, standing on the baseline.</summary>
        public byte[]? Picture;
        public double PictureWidth;
        public double PictureHeight;

        /// <summary>A tab run (<see cref="TextSegment.IsTab"/>): it draws nothing and takes the
        /// room its line gives it.</summary>
        public bool Tab;
    }

    /// <summary>The text a picture run stands in for: one non-space character, so
    /// the wrapper treats the picture as a word of its own.</summary>
    private const string PictureMark = "\uFFFC";

    /// <summary>True when the paragraph's segments were asked to flow as runs
    /// (<see cref="TextFormattingOptions.SegmentsFlowAsRuns"/>) and every inked one
    /// is a shape the cell draws through its Standard-14 faces: no embedded program,
    /// no newline, no placement of its own, no script the faces lack.</summary>
    private static bool SegmentsFlowAsRuns(BaseParagraph paragraph)
    {
        if (paragraph is not TextFragment { Segments: { Count: > 0 } } tf
            || tf.TextState.FormattingOptions is not { SegmentsFlowAsRuns: true }
            || tf.TextState.Font?.SourceFontData is not null) return false;
        var inked = 0;
        var pictures = 0;
        foreach (var seg in tf.Segments)
        {
            var text = seg.Text ?? string.Empty;
            // A picture among the runs is a word of its own when it has a box and bytes.
            if (seg.InlineImage is { } picture)
            {
                if (picture.FixWidth <= 0 || picture.FixHeight <= 0 || ReadRawImageBytes(picture) is null) return false;
                pictures++;
                continue;
            }
            if (text.Length == 0) continue;
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0 || seg.Position is not null
                || seg.TextState.FontData is not null || seg.TextState.Font?.SourceFontData is not null
                || ContainsCjk(text) || ArabicTextShaper.ContainsArabic(text)) return false;
            inked++;
        }
        // A fragment of ONE segment is the ordinary one-style paragraph -- its own empty
        // segment rides along, so the COUNT is what tells the two apart -- and a picture
        // makes a line of runs whatever else the fragment holds.
        return (tf.Segments.Count > 1 && inked > 0) || pictures > 0;
    }

    /// <summary>The runs a fragment's segments make in a cell: a segment's own size,
    /// face, colour, leading and line box, else the paragraph's -- what the page
    /// flow reads of the same fragment (see <c>FlowSegmentedRuns</c>), with the
    /// cell's resolved size and colour standing in for the paragraph's.</summary>
    private static List<CellRun> CollectCellRuns(TextFragment tf, double fragSize, double fragLeading,
        (double AscentEm, double DescentEm) fragBox, Color? fragColor)
    {
        var runs = new List<CellRun>();
        var fragState = tf.TextState;
        foreach (var seg in tf.Segments)
        {
            var text = seg.Text ?? string.Empty;
            if (seg.IsTab)
            {
                runs.Add(new CellRun
                {
                    Text = "\t", Size = fragSize, Leading = fragLeading,
                    AscentEm = fragBox.AscentEm, DescentEm = fragBox.DescentEm, Tab = true,
                });
                continue;
            }
            if (seg.InlineImage is { } picture)
            {
                // A picture run: the paragraph's size and box, so the line it stands
                // on keeps the text's leading and descent, and its own bytes and box.
                runs.Add(new CellRun
                {
                    Text = PictureMark,
                    Size = fragSize,
                    Leading = fragLeading,
                    AscentEm = fragBox.AscentEm,
                    DescentEm = fragBox.DescentEm,
                    Picture = ReadRawImageBytes(picture),
                    PictureWidth = picture.FixWidth,
                    PictureHeight = picture.FixHeight,
                });
                continue;
            }
            if (text.Length == 0) continue;
            var st = seg.TextState;
            var (ascentEm, descentEm) = st is { LineBoxAscentEm: { } a, LineBoxDescentEm: { } d } ? (a, d) : fragBox;
            runs.Add(new CellRun
            {
                Text = text,
                BaseFont = TextBuilder.MapToStandard14Public(st),
                Size = st.FontSizeTouched && st.FontSize > 0 ? st.FontSize : fragSize,
                Leading = st.LineSpacing > 0 ? st.LineSpacing : fragLeading,
                AscentEm = ascentEm,
                DescentEm = descentEm,
                Color = st.ForegroundColor ?? fragColor,
                CharSpacing = st.CharacterSpacing != 0 ? st.CharacterSpacing : fragState.CharacterSpacing,
                WordSpacing = st.WordSpacing != 0 ? st.WordSpacing : fragState.WordSpacing,
            });
        }
        return runs;
    }

    /// <summary>A run's advance for <paramref name="text"/>: its face's Standard-14
    /// widths at its size, plus its character and word spacing -- the flow's own
    /// measure of a run, so a paragraph wraps alike on the page and in a cell.</summary>
    private static double CellRunWidth(CellRun run, string text)
    {
        if (run.Picture is not null) return text.Length == 0 ? 0 : run.PictureWidth;
        double w = 0;
        foreach (var ch in text)
        {
            var cw = Standard14Fonts.GetWidth(run.BaseFont, ch < 256 ? ch : '?');
            w += (cw >= 0 ? cw : 500) * run.Size / 1000.0;
        }
        w += text.Length * run.CharSpacing;
        if (run.WordSpacing != 0)
            foreach (var c in text) if (c == ' ') w += run.WordSpacing;
        return w;
    }

    /// <summary>The runs as words, by the wrapper the page flow uses.</summary>
    private static List<RunWord> CellRunWords(List<CellRun> runs, RunWrapRules? rules = null)
    {
        var texts = new string[runs.Count];
        for (var i = 0; i < runs.Count; i++) texts[i] = runs[i].Text;
        return RunWordWrap.SplitIntoWords(texts, (ri, chunk) => CellRunWidth(runs[ri], chunk), rules);
    }

    /// <summary>The wrap's rules for runs holding a tab: the fragment's stops and interval;
    /// null for runs holding none.</summary>
    private static RunWrapRules? CellTabRules(List<CellRun> runs, TextFragment tf) =>
        runs.Exists(r => r.Tab)
            ? new RunWrapRules
            {
                TabRuns = ri => runs[ri].Tab,
                TabStops = tf.TextState.FormattingOptions?.RunTabStops,
                TabInterval = tf.TextState.FormattingOptions?.RunTabInterval ?? RunWrapRules.DefaultTabInterval,
            }
            : null;

    /// <summary>The runs on lines of the width, tabs placed by their stops.</summary>
    private static List<List<RunPiece>> WrapCellRuns(List<CellRun> runs, TextFragment tf, double width)
    {
        var rules = CellTabRules(runs, tf);
        return RunWordWrap.Wrap(CellRunWords(runs, rules), width, 0, null, rules,
            (Func<RunPiece, double>)(piece => CellRunWidth(runs[piece.Run], piece.Text)));
    }

    /// <summary>A cell paragraph whose segments flow as runs is laid the way the page
    /// flow lays it: the text wraps at spaces across the runs, and every line is
    /// boxed, pitched and seated by its LARGEST run -- the smaller runs share that
    /// baseline -- and drawn run by run in each run's own face, size and colour.
    /// True when the paragraph was laid here.</summary>
    private bool PlanSegmentRunsText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, RowPlanState rp)
    {
        if (pp.fragEmbeddedTtf is not null || !SegmentsFlowAsRuns(paragraph)) return false;
        var tf = (TextFragment)paragraph;
        // A run without a colour of its own draws in the PARAGRAPH's (else the
        // cell's), never in the colour of the first run that declared one -- that
        // reading is for a fragment styled through one segment, not one of runs.
        var paragraphColour = tf.TextState.ForegroundColor ?? pc.textState?.ForegroundColor;
        pp.color = paragraphColour;
        var runs = CollectCellRuns(tf, pp.fragFontSize, pp.fragLeading, pc.paraLineBox, paragraphColour);
        var width = pc.cell.IsWordWrapped && pc.availWidth > 0 ? pc.availWidth : double.MaxValue;
        var tallestBox = 0.0;
        var first = true;
        foreach (var line in WrapCellRuns(runs, tf, width))
        {
            var cellLine = BuildRunsLine(runs, line, pp);
            SeatPicturedLine(runs, line, cellLine, first);
            first = false;
            pc.lines.Add(cellLine);
            tallestBox = Math.Max(tallestBox, cellLine.OwnPitch > 0 ? cellLine.OwnPitch : cellLine.FontSize + cellLine.Leading);
        }
        // The lines carry their own leading and box -- each its largest run's --
        // so the paragraph's are not stamped over them.
        pc.paraLeading = 0;
        pc.paraLineBox = (0, 0);
        Consider(rp, tallestBox, tallestBox);
        return true;
    }

    /// <summary>One line of runs as a cell line: consecutive pieces of one run as one
    /// show at its offset, the largest run's size, leading and box as the line's
    /// own, and the width that aligns it (its trailing spaces excluded, as the
    /// flow excludes them).</summary>
    private static CellLine BuildRunsLine(List<CellRun> runs, List<RunPiece> line, RowPlanParagraphState pp)
    {
        line = TrimLineStart(line);
        var shows = new List<SpanRun>();
        var text = new System.Text.StringBuilder();
        var x = 0.0;
        var alignWidth = 0.0;
        var tallest = runs[line.Find(p => !runs[p.Run].Tab) is { Text: not null } first ? first.Run : line[0].Run];
        var i = 0;
        while (i < line.Count)
        {
            var runIndex = line[i].Run;
            if (runs[runIndex].Tab)
            {
                // A tab shows nothing: it moves what follows on, its leader drawn across its room.
                var advance = line[i].TabAdvance ?? 0;
                if (line[i].Stop?.LeaderRule is { } leader && advance > 0)
                    shows.Add(new SpanRun { X = x, Width = advance, Size = tallest.Size, Leader = leader });
                x += advance;
                alignWidth = x;
                i++;
                continue;
            }
            var show = new System.Text.StringBuilder(line[i].Text);
            var j = i + 1;
            while (j < line.Count && line[j].Run == runIndex) show.Append(line[j++].Text);
            var run = runs[runIndex];
            if (run.Size > tallest.Size) tallest = run;
            var showText = show.ToString();
            var w = CellRunWidth(run, showText);
            shows.Add(run.Picture is { } picture
                // (a picture run shows no text; it is blitted at its offset on the baseline)
                ? new SpanRun { X = x, Width = w, Size = run.Size, Picture = picture, PictureHeight = run.PictureHeight }
                : new SpanRun
                {
                    Text = showText, X = x, Width = w, Size = run.Size, Color = run.Color,
                    BaseFont = run.BaseFont, CharSpacing = run.CharSpacing, WordSpacing = run.WordSpacing,
                });
            text.Append(showText);
            alignWidth = x + CellRunWidth(run, j == line.Count ? showText.TrimEnd(' ') : showText);
            x += w;
            i = j;
        }
        return new CellLine
        {
            Text = text.ToString(),
            FontSize = tallest.Size,
            Leading = tallest.Leading,
            LineBoxAscentEm = tallest.AscentEm,
            LineBoxDescentEm = tallest.DescentEm,
            ForegroundColor = pp.color,
            Align = pp.lineAlign,
            SegRuns = shows,
            KernedWidth = alignWidth,
        };
    }

    /// <summary>The line without the spaces it opens on: a run's leading space
    /// rides its word through the wrap, but a line starts at its first glyph
    /// (probed: a cell paragraph of " and" then a picture sets "and" at the
    /// padding edge and the picture right after it).</summary>
    private static List<RunPiece> TrimLineStart(List<RunPiece> line)
    {
        var trimmed = new List<RunPiece>(line.Count);
        var opening = true;
        foreach (var piece in line)
        {
            if (opening)
            {
                var text = piece.Text.TrimStart(' ');
                if (text.Length == 0) continue;
                opening = false;
                trimmed.Add(text.Length == piece.Text.Length ? piece : new RunPiece(piece.Run, text));
                continue;
            }
            trimmed.Add(piece);
        }
        return trimmed.Count > 0 ? trimmed : line;
    }

    /// <summary>A line with a picture among its runs seats itself: the picture
    /// stands on the baseline and raises the line's ascent to its height when it
    /// is taller than the text's, the descent staying the text's; a line of
    /// pictures alone is exactly the tallest of them, with no descent under it.
    /// On every line after the paragraph's first a line holding a picture drops
    /// its whole box by the text's em descent, whatever the picture's height
    /// (probed at 8, 12, 18 and 24 pt: a 22.5 pt picture on a wrapped 12 pt line
    /// seats 22.5 + 2.98 under the line above, on the first line 22.5; a 6 pt
    /// picture beside 12 pt text adds the same 2.98; a picture alone on a later
    /// line is its height + 2.98 and nothing below).</summary>
    private static void SeatPicturedLine(List<CellRun> runs, List<RunPiece> line, CellLine cellLine, bool firstLine)
    {
        var pictures = 0.0;
        var text = false;
        foreach (var piece in line)
        {
            var run = runs[piece.Run];
            if (run.Picture is not null) pictures = Math.Max(pictures, run.PictureHeight);
            else if (piece.Text.Trim().Length > 0) text = true;
        }
        if (pictures <= 0) return;
        var drop = firstLine ? 0 : cellLine.LineBoxDescentEm * cellLine.FontSize;
        if (!text)
        {
            cellLine.OwnPitch = pictures + drop;
            cellLine.OwnBaseline = pictures + drop;
            return;
        }
        var pitch = cellLine.FontSize + cellLine.Leading;
        var ascent = DeclaredLineBoxBaseOff(cellLine);
        var descent = pitch - ascent;
        var raised = Math.Max(ascent, pictures) + drop;
        cellLine.OwnBaseline = raised;
        cellLine.OwnPitch = raised + descent;
    }

    /// <summary>What a cell paragraph of runs measures for the column: its widest
    /// word (a word straddling runs summed at each run's own size) and its whole
    /// width on one line.</summary>
    private static (double WidestWord, double LineWidth) MeasureCellRuns(TextFragment tf, double fragSize)
    {
        double widest = 0, line = 0;
        var runs = CollectCellRuns(tf, fragSize, 0, (0, 0), null);
        foreach (var word in CellRunWords(runs, CellTabRules(runs, tf)))
        {
            widest = Math.Max(widest, word.WordWidth);
            line += word.WordWidth + word.SpaceWidth;
        }
        if (runs.Exists(r => r.Tab)) line = TabbedLineWidth(runs, tf);
        return (widest, line);
    }

    /// <summary>The width a paragraph holding tabs asks of its column: its text on one line and
    /// the room its tabs to right, centre and anchor stops take there - a tab to a left stop or a
    /// default position asks for none (probed: 'Other', a default tab and 'x' ask 36.01, the text
    /// alone; 'Cell', a tab to a right stop at 150 and '9.99' ask 150).</summary>
    private static double TabbedLineWidth(List<CellRun> runs, TextFragment tf)
    {
        var width = 0.0;
        foreach (var line in WrapCellRuns(runs, tf, double.MaxValue))
            foreach (var piece in line)
                width += piece.TabAdvance is { } advance
                    ? piece.Stop is { AlignmentType: not TabAlignmentType.Left } ? advance : 0
                    : CellRunWidth(runs[piece.Run], piece.Text);
        return width;
    }

    /// <summary>Styled runs on one baseline, each in its own face, size, colour and
    /// spacing: a run naming a Standard-14 face draws in it, a bold or slanted one
    /// in the Helvetica variant, an embedded one through its Type0 program; an
    /// underlined run strokes its rule under its own ink. A spanning cell's lines
    /// and a cell paragraph of runs draw through the same emitter.</summary>
    private static void EmitStyledRuns(ContentStreamBuilder builder, string fontName, Page? page,
        List<SpanRun> runs, double lineX, double lineBase, Page? picturePage = null)
    {
        foreach (var run in runs)
        {
            // A picture run is drawn where it stands, between the shows before and
            // after it -- so the text layer keeps them apart as the reference's does.
            // With no page to register it on yet (a spill page) the caller blits it.
            if (run.Picture is { } picture && picturePage is not null)
            {
                var name = ImageStamp.FromEncodedBytes(picture).RegisterXObject(picturePage);
                builder.SaveState();
                builder.SetMatrix(run.Width, 0, 0, run.PictureHeight, lineX + run.X, lineBase);
                builder.DrawXObject(name);
                builder.RestoreState();
                builder.RecordInlineBox(lineBase, lineBase + run.PictureHeight);
                continue;
            }
            if (run.Leader is { } leader)
            {
                builder.SaveState();
                RulePainter.StrokeRule(builder, leader, lineX + run.X, lineBase + leader.LineWidth / 2, lineX + run.X + run.Width);
                builder.RestoreState();
                continue;
            }
            if (run.Text.Length == 0) continue;
            var rx = lineX + run.X;
            var spaced = run.CharSpacing != 0 || run.WordSpacing != 0;
            if (spaced) builder.SaveState();
            if (run.Ttf is not null && page is not null)
            {
                var srDict = ResolvePageFontDict(page);
                var (srRes, srHex) = Type0FontEmbedder.Embed(
                    srDict, run.Ttf, run.FontName ?? "Font", run.Text,
                    stripSpacesInBaseFont: true);
                builder.BeginText();
                builder.SetFont(srRes, run.Size);
                ApplyColor(builder, run.Color);
                builder.MoveTextPosition(rx, lineBase);
                builder.ShowTextHex(srHex);
                builder.EndText();
            }
            else
            {
                var srFont = fontName;
                if (run.BaseFont is { } baseFont && page is not null)
                    srFont = RegisterFont(page, baseFont);
                else if ((run.Bold || run.Italic) && page is not null)
                    srFont = RegisterFont(page, run.Bold && run.Italic
                        ? "Helvetica-BoldOblique"
                        : run.Bold ? "Helvetica-Bold" : "Helvetica-Oblique");
                builder.BeginText();
                builder.SetFont(srFont, run.Size);
                ApplyColor(builder, run.Color);
                if (run.CharSpacing != 0) builder.SetCharSpacing(run.CharSpacing);
                if (run.WordSpacing != 0) builder.SetWordSpacing(run.WordSpacing);
                builder.MoveTextPosition(rx, lineBase);
                builder.ShowText(run.Text);
                builder.EndText();
            }
            if (spaced) builder.RestoreState();
            if (run.Underline && run.Width > 0)
            {
                builder.SaveState();
                if (run.Color is { } ruc)
                    builder.SetStrokeColor(ruc.R / 255.0, ruc.G / 255.0, ruc.B / 255.0);
                builder.SetLineWidth(LinkUnderlineWPt * run.Size / LinkProbeBasePt);
                var ruy = lineBase - LinkUnderlineDropPt * run.Size / LinkProbeBasePt;
                builder.MoveTo(rx, ruy).LineTo(rx + run.Width, ruy).Stroke();
                builder.RestoreState();
            }
        }
    }
}
