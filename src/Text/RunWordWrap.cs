namespace Aspose.Pdf.Text;

/// <summary>A piece of one run on one line: the run it comes from, the text and
/// where that text starts in the run's own text.</summary>
internal readonly struct RunPiece
{
    public readonly int Run;
    public readonly string Text;
    public readonly int Start;
    /// <summary>For a tab: the room it takes on its line; null for text.</summary>
    public readonly double? TabAdvance;
    /// <summary>For a tab: the stop it went to, null for a default position.</summary>
    public readonly TabStop? Stop;
    public RunPiece(int run, string text) : this(run, text, -1) { }
    public RunPiece(int run, string text, int start) { Run = run; Text = text; Start = start; TabAdvance = null; Stop = null; }
    public RunPiece(int run, string text, int start, double tabAdvance, TabStop? stop)
    {
        Run = run;
        Text = text;
        Start = start;
        TabAdvance = tabAdvance;
        Stop = stop;
    }
}

/// <summary>One word of a paragraph of runs: its pieces (a word may straddle a
/// run boundary), the width of the word proper and of the spaces after it, and
/// whether a line break is forced after it.</summary>
internal readonly struct RunWord
{
    public readonly List<RunPiece> Pieces;
    public readonly double WordWidth;
    public readonly double SpaceWidth;
    public readonly bool ForcedBreak;
    /// <summary>The word is a tab run: its room is decided where it lands on its line.</summary>
    public readonly bool IsTab;
    public RunWord(List<RunPiece> pieces, double wordWidth, double spaceWidth) : this(pieces, wordWidth, spaceWidth, false) { }
    public RunWord(List<RunPiece> pieces, double wordWidth, double spaceWidth, bool forcedBreak, bool isTab = false)
    {
        Pieces = pieces;
        WordWidth = wordWidth;
        SpaceWidth = spaceWidth;
        ForcedBreak = forcedBreak;
        IsTab = isTab;
    }
}

/// <summary>What a wrap honours beyond breaking after spaces. The default rules
/// (every member unset) are the plain wrap the page flow and table cells use.</summary>
internal sealed class RunWrapRules
{
    /// <summary>True when a line may end after the character at the index of a
    /// run's text (the run, its text, the index); the character stays on the
    /// line. Spaces always may.</summary>
    public Func<int, string, int, bool>? BreaksAfter { get; init; }

    /// <summary>A line feed, a carriage return or the pair of them ends the line;
    /// the break stays on the line it ends and occupies nothing.</summary>
    public bool LineFeedsBreak { get; init; }

    /// <summary>A word wider than a whole line starts a line of its own and breaks
    /// between its characters, each line holding at least one.</summary>
    public bool BreakOverlongWords { get; init; }

    /// <summary>Spaces a line would start with (the paragraph's first line, or a
    /// line after a forced break) are dropped from it.</summary>
    public bool DropLineStartSpaces { get; init; }

    /// <summary>True for a run that is a tab (<see cref="TextSegment.IsTab"/>): a word of its
    /// own whose room is decided on its line (<see cref="TextFormattingOptions.RunTabStops"/>).</summary>
    public Func<int, bool>? TabRuns { get; init; }

    /// <summary>The stops the tabs go to, in any order; null has none.</summary>
    public TabStops? TabStops { get; init; }

    /// <summary>The distance between default tab positions.</summary>
    public double TabInterval { get; init; } = DefaultTabInterval;

    /// <summary>Half an inch, the distance default tab positions stand apart when none is set.</summary>
    public const double DefaultTabInterval = 36;
}

/// <summary>Greedy word wrap over a sequence of runs that each measure their own
/// text: the page flow's segmented paragraphs and a table cell's use the same one,
/// so a word wraps the same way wherever the runs stand.</summary>
internal static class RunWordWrap
{
    /// <summary>The runs as words. A word is a maximal stretch of non-space
    /// characters, across run boundaries, with the spaces after it; a word's
    /// leading spaces ride it, spaces after a word end it once the next word starts.</summary>
    public static List<RunWord> SplitIntoWords(IReadOnlyList<string> texts, Func<int, string, double> measure) =>
        SplitIntoWords(texts, measure, null);

    /// <summary>The runs as words under the rules: a word also ends after a
    /// character the rules break after, and at a forced line break.</summary>
    public static List<RunWord> SplitIntoWords(IReadOnlyList<string> texts, Func<int, string, double> measure, RunWrapRules? rules) =>
        SplitIntoWords(texts, piece => measure(piece.Run, piece.Text), rules);

    /// <summary>The runs as words under the rules, each piece measured where it
    /// stands in its run (its run, text and start).</summary>
    public static List<RunWord> SplitIntoWords(IReadOnlyList<string> texts, Func<RunPiece, double> measure, RunWrapRules? rules)
    {
        var words = new List<RunWord>();
        var pieces = new List<RunPiece>();
        double wordWidth = 0, spaceWidth = 0;
        var inSpaces = false;
        var breakPending = false;
        void Close(bool forced = false)
        {
            if (pieces.Count == 0) return;
            words.Add(new RunWord(pieces, wordWidth, spaceWidth, forced));
            pieces = new List<RunPiece>();
            wordWidth = 0;
            spaceWidth = 0;
            inSpaces = false;
            breakPending = false;
        }
        for (var ri = 0; ri < texts.Count; ri++)
        {
            var text = texts[ri];
            if (rules?.TabRuns is { } isTab && isTab(ri))
            {
                Close();
                words.Add(new RunWord([new RunPiece(ri, text, 0)], 0, 0, false, isTab: true));
                continue;
            }
            var start = 0;
            while (start < text.Length)
            {
                if (rules is { LineFeedsBreak: true } && IsLineFeed(text[start]))
                {
                    var feed = text[start] == '\r' && start + 1 < text.Length && text[start + 1] == '\n' ? 2 : 1;
                    pieces.Add(new RunPiece(ri, text.Substring(start, feed), start));
                    Close(forced: true);
                    start += feed;
                    continue;
                }
                var spaces = text[start] == ' ';
                var end = start;
                while (end < text.Length && (text[end] == ' ') == spaces && !(rules is { LineFeedsBreak: true } && IsLineFeed(text[end])))
                {
                    end++;
                    if (!spaces && rules?.BreaksAfter is { } breaks && breaks(ri, text, end - 1)) break;
                }
                var chunk = text.Substring(start, end - start);
                if (!spaces && (inSpaces || breakPending)) Close();
                var piece = new RunPiece(ri, chunk, start);
                pieces.Add(piece);
                var w = measure(piece);
                if (spaces && pieces.Count > 1) { spaceWidth += w; inSpaces = true; }
                else wordWidth += w;
                if (!spaces && rules?.BreaksAfter is { } after && after(ri, text, end - 1)) breakPending = true;
                start = end;
            }
        }
        Close();
        return words;
    }

    private static bool IsLineFeed(char c) => c is '\n' or '\r';

    /// <summary>The words on lines of <paramref name="width"/>: a word goes on the
    /// line when the word itself fits, its spaces hanging past the edge if they
    /// must; a word too long for an empty line takes the line anyway. A run's
    /// <paramref name="extraOf"/> is what it occupies past its glyphs, charged once
    /// per line when the run first appears on it.</summary>
    public static List<List<RunPiece>> Wrap(List<RunWord> words, double width, double firstLineIndent,
        Func<int, double>? extraOf = null) =>
        Wrap(words, width, firstLineIndent, extraOf, null, (Func<RunPiece, double>?)null);

    /// <summary>The words on lines under the rules; <paramref name="measure"/>
    /// measures a piece of a run's text, and is needed when the rules drop line
    /// start spaces or break overlong words.</summary>
    public static List<List<RunPiece>> Wrap(List<RunWord> words, double width, double firstLineIndent,
        Func<int, double>? extraOf, RunWrapRules? rules, Func<int, string, double>? measure) =>
        Wrap(words, width, firstLineIndent, extraOf, rules, measure is null ? (Func<RunPiece, double>?)null : piece => measure(piece.Run, piece.Text));

    /// <summary>The words on lines under the rules, each piece measured where it
    /// stands in its run.</summary>
    public static List<List<RunPiece>> Wrap(List<RunWord> words, double width, double firstLineIndent,
        Func<int, double>? extraOf, RunWrapRules? rules, Func<RunPiece, double>? measure)
    {
        if (rules is not null && (rules.DropLineStartSpaces || rules.BreakOverlongWords) && measure is null)
            throw new ArgumentNullException(nameof(measure), "these rules measure pieces of words");
        var lines = new List<List<RunPiece>>();
        var line = new List<RunPiece>();
        var available = width - firstLineIndent;
        var used = 0.0;
        var runsOnLine = new HashSet<int>();
        var tabs = rules?.TabRuns is not null ? new TabPlacer(rules, measure) : null;
        double NewExtra(List<RunPiece> pieces)
        {
            if (extraOf is null) return 0;
            var extra = 0.0;
            foreach (var piece in pieces)
                if (!runsOnLine.Contains(piece.Run)) extra += extraOf(piece.Run);
            return extra;
        }
        void NewLine()
        {
            if (tabs is not null) used += tabs.Resolve(line, used, available);
            lines.Add(line);
            line = new List<RunPiece>();
            available = width;
            used = 0;
            runsOnLine.Clear();
        }
        void Place(RunWord word, double extra)
        {
            line.AddRange(word.Pieces);
            foreach (var piece in word.Pieces) runsOnLine.Add(piece.Run);
            used += word.WordWidth + word.SpaceWidth + extra;
        }
        foreach (var source in words)
        {
            if (source.IsTab && tabs is not null)
            {
                foreach (var piece in source.Pieces) used += tabs.Place(line, piece, used, available);
                continue;
            }
            var word = line.Count == 0 && rules is { DropLineStartSpaces: true } ? WithoutLeadingSpaces(source, measure!) : source;
            if (word.Pieces.Count == 0)
            {
                if (word.ForcedBreak) NewLine();
                continue;
            }
            var newExtra = NewExtra(word.Pieces);
            if (line.Count > 0 && used + word.WordWidth + newExtra > available + 1e-6)
            {
                NewLine();
                if (rules is { DropLineStartSpaces: true }) word = WithoutLeadingSpaces(word, measure!);
                newExtra = NewExtra(word.Pieces);
            }
            if (rules is { BreakOverlongWords: true } && line.Count == 0 && word.WordWidth + newExtra > available + 1e-6)
                word = PlaceByCharacters(word, measure!, extraOf, lines, ref line, ref available, width, runsOnLine, out newExtra);
            Place(word, newExtra);
            if (word.ForcedBreak) NewLine();
        }
        if (line.Count > 0)
        {
            tabs?.Resolve(line, used, available);
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Where the tabs of a line being filled land. A tab goes to the first stop past
    /// where the line has got to: to a left stop, or to the next default position (never past
    /// the line's end) when no stop is past it, it takes its room at once. To any other stop it
    /// HANGS: what follows is laid as though the tab took no room, and when the next tab comes or
    /// the line ends the tab takes the room that aligns it on the stop, pulled back as far as it
    /// must be for it to end within the line (see <see cref="TextFormattingOptions.RunTabStops"/>).</summary>
    private sealed class TabPlacer
    {
        private readonly RunWrapRules _rules;
        private readonly Func<RunPiece, double>? _measure;
        private int _hangingAt = -1;
        private TabStop? _hangingStop;
        private double _hangingFrom;

        public TabPlacer(RunWrapRules rules, Func<RunPiece, double>? measure)
        {
            _rules = rules;
            _measure = measure;
        }

        /// <summary>Puts the tab on the line; answers the room the line took, a hanging tab
        /// before it settling first (a tab hanging itself takes none yet).</summary>
        public double Place(List<RunPiece> line, RunPiece tab, double used, double available)
        {
            var settled = Resolve(line, used, available);
            used += settled;
            var stop = NextStop(used);
            if (stop is null)
            {
                var interval = _rules.TabInterval > 0 ? _rules.TabInterval : RunWrapRules.DefaultTabInterval;
                var room = interval - used % interval;
                if (used + room > available) room = Math.Max(0, available - used);
                line.Add(new RunPiece(tab.Run, tab.Text, tab.Start, room, null));
                return settled + room;
            }
            if (stop.AlignmentType == TabAlignmentType.Left)
            {
                line.Add(new RunPiece(tab.Run, tab.Text, tab.Start, stop.Position - used, stop));
                return settled + stop.Position - used;
            }
            line.Add(new RunPiece(tab.Run, tab.Text, tab.Start, 0, stop));
            _hangingAt = line.Count - 1;
            _hangingStop = stop;
            _hangingFrom = used;
            return settled;
        }

        /// <summary>Settles a hanging tab over what followed it on the line; answers its room.</summary>
        public double Resolve(List<RunPiece> line, double used, double available)
        {
            if (_hangingAt < 0) return 0;
            var stop = _hangingStop!;
            var after = used - _hangingFrom;
            var aligned = stop.AlignmentType switch
            {
                TabAlignmentType.Right => after,
                TabAlignmentType.Center => after / 2,
                _ => AnchorOffset(line, _hangingAt + 1, stop.AnchorCharacter) ?? after,
            };
            var room = Math.Max(0, stop.Position - _hangingFrom - aligned);
            if (_hangingFrom + room + after > available) room -= _hangingFrom + room + after - available;
            var tab = line[_hangingAt];
            line[_hangingAt] = new RunPiece(tab.Run, tab.Text, tab.Start, room, stop);
            _hangingAt = -1;
            return room;
        }

        /// <summary>The first stop past the position, null when none is.</summary>
        private TabStop? NextStop(double used)
        {
            TabStop? next = null;
            if (_rules.TabStops is { } stops)
                foreach (var stop in stops.Stops)
                    if (stop.Position > used && (next is null || stop.Position < next.Position)) next = stop;
            return next;
        }

        /// <summary>How far into the pieces from <paramref name="from"/> the first anchor character
        /// stands; null when none of them holds it.</summary>
        private double? AnchorOffset(List<RunPiece> line, int from, char anchor)
        {
            if (_measure is null) return null;
            var offset = 0.0;
            for (var i = from; i < line.Count; i++)
            {
                var piece = line[i];
                var at = piece.Text.IndexOf(anchor);
                if (at >= 0) return offset + _measure(new RunPiece(piece.Run, piece.Text.Substring(0, at), piece.Start));
                offset += _measure(piece);
            }
            return null;
        }
    }

    /// <summary>The word without the spaces it starts with.</summary>
    private static RunWord WithoutLeadingSpaces(RunWord word, Func<RunPiece, double> measure)
    {
        var pieces = new List<RunPiece>(word.Pieces);
        var wordWidth = word.WordWidth;
        var spaceWidth = word.SpaceWidth;
        while (pieces.Count > 0 && pieces[0].Text.Length > 0 && pieces[0].Text[0] == ' ')
        {
            // A word that is spaces only holds them as its word proper when they
            // are its one piece, otherwise as the spaces after its first piece.
            if (pieces.Count == 1 && spaceWidth == 0) wordWidth -= measure(pieces[0]);
            else if (pieces.Count == 1) spaceWidth -= measure(pieces[0]);
            else wordWidth -= measure(pieces[0]);
            pieces.RemoveAt(0);
        }
        if (pieces.Count == 0) return new RunWord(pieces, 0, 0, word.ForcedBreak);
        return new RunWord(pieces, Math.Max(0, wordWidth), spaceWidth, word.ForcedBreak);
    }

    /// <summary>Lays a word too wide for a whole line out a character at a time,
    /// ending a line before the character that would overflow it (a line always
    /// takes one); the characters left over, with the word's spaces, come back as
    /// the word the current line continues with.</summary>
    private static RunWord PlaceByCharacters(RunWord word, Func<RunPiece, double> measure, Func<int, double>? extraOf,
        List<List<RunPiece>> lines, ref List<RunPiece> line, ref double available, double width,
        HashSet<int> runsOnLine, out double tailExtra)
    {
        var (glyphs, spaces) = SplitTrailingSpaces(word.Pieces);
        var used = 0.0;
        var current = new List<(int Run, string Text, int Start)>();
        for (var i = 0; i < glyphs.Count; i++)
        {
            var (run, text, start) = glyphs[i];
            var w = measure(new RunPiece(run, text, start)) + (extraOf is not null && !runsOnLine.Contains(run) ? extraOf(run) : 0);
            if (current.Count > 0 && used + w > available + 1e-6)
            {
                line.AddRange(Merge(current));
                lines.Add(line);
                line = new List<RunPiece>();
                available = width;
                runsOnLine.Clear();
                current.Clear();
                used = 0;
                w = measure(new RunPiece(run, text, start)) + (extraOf is not null ? extraOf(run) : 0);
            }
            current.Add(glyphs[i]);
            runsOnLine.Add(run);
            used += w;
        }
        var tail = Merge(current);
        tail.AddRange(spaces);
        tailExtra = 0;
        return new RunWord(tail, used, word.SpaceWidth, word.ForcedBreak);
    }

    /// <summary>A word's characters one by one (a surrogate pair as one), and the
    /// pieces of spaces and forced breaks after them.</summary>
    private static (List<(int Run, string Text, int Start)> Glyphs, List<RunPiece> Spaces) SplitTrailingSpaces(List<RunPiece> pieces)
    {
        var last = pieces.Count;
        while (last > 0 && (pieces[last - 1].Text.Trim(' ').Length == 0 || IsLineFeed(pieces[last - 1].Text[0]))) last--;
        var glyphs = new List<(int, string, int)>();
        for (var p = 0; p < last; p++)
        {
            var piece = pieces[p];
            for (var i = 0; i < piece.Text.Length; i++)
            {
                var n = char.IsHighSurrogate(piece.Text[i]) && i + 1 < piece.Text.Length ? 2 : 1;
                glyphs.Add((piece.Run, piece.Text.Substring(i, n), piece.Start < 0 ? -1 : piece.Start + i));
                i += n - 1;
            }
        }
        return (glyphs, pieces.GetRange(last, pieces.Count - last));
    }

    /// <summary>Consecutive characters of one run joined back into one piece.</summary>
    private static List<RunPiece> Merge(List<(int Run, string Text, int Start)> glyphs)
    {
        var pieces = new List<RunPiece>();
        var i = 0;
        while (i < glyphs.Count)
        {
            var run = glyphs[i].Run;
            var start = glyphs[i].Start;
            var text = new System.Text.StringBuilder();
            while (i < glyphs.Count && glyphs[i].Run == run) text.Append(glyphs[i++].Text);
            pieces.Add(new RunPiece(run, text.ToString(), start));
        }
        return pieces;
    }
}
