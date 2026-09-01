using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
    /// <summary>
    /// Groups fragments into horizontal lines based on Y-coordinate proximity.
    /// </summary>
    private static List<TextLine> GroupIntoLines(List<TextFragment> fragments)
    {
        var lines = new List<TextLine>();
        var sorted = fragments.OrderByDescending(f => GetY(f)).ThenBy(f => GetX(f)).ToList();

        foreach (var frag in sorted)
        {
            if (frag.Rectangle is null) continue;
            var fragMidY = (frag.Rectangle.LLY + frag.Rectangle.URY) / 2;
            var fragHeight = frag.Rectangle.Height;
            var tolerance = Math.Max(fragHeight * 0.5, 1.0);

            var found = false;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                // The join window is the LINE's own half-height, not only the joining
                // fragment's: a superscript citation run (fs 5.9 riding high on a
                // 10.3 pt CJK line) must land on the line whose band covers it — the
                // reference keys each line by (median, half-height) of the line.
                // The fragment-based tolerance stays as the floor so nothing that
                // joined before stops joining.
                var lineTol = Math.Max((lines[i].MaxY - lines[i].MinY) * 0.5, tolerance);
                if (Math.Abs(lines[i].MidY - fragMidY) <= lineTol)
                {
                    lines[i].Fragments.Add(frag);
                    lines[i].Recalc();
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var line = new TextLine();
                line.Fragments.Add(frag);
                line.Recalc();
                lines.Add(line);
            }
        }

        // Sort fragments within each line left-to-right
        foreach (var line in lines)
            line.Fragments.Sort((a, b) => GetX(a).CompareTo(GetX(b)));

        // Split lines with large horizontal gaps into separate lines.
        // This handles multi-column layouts where fragments at the same Y are in different columns.
        var splitLines = new List<TextLine>();
        foreach (var line in lines)
        {
            if (line.Fragments.Count <= 1)
            {
                splitLines.Add(line);
                continue;
            }

            // Detect column gaps adaptively:
            // Collect all horizontal gaps within this line, then look for a natural break point.
            // If there's a clear gap between word-level spacing and column-level spacing, use it.
            var gaps = new List<double>();
            for (var i = 1; i < line.Fragments.Count; i++)
            {
                var prev = line.Fragments[i - 1];
                var curr = line.Fragments[i];
                var prevRight = prev.Rectangle?.URX ?? GetX(prev);
                var currLeft = curr.Rectangle?.LLX ?? GetX(curr);
                var gap = currLeft - prevRight;
                if (gap > 0) gaps.Add(gap);
            }

            double gapThreshold;
            if (gaps.Count > 2)
            {
                gaps.Sort();
                var medianGap = gaps[gaps.Count / 2];
                // Column gaps should be significantly larger than word gaps
                gapThreshold = Math.Max(medianGap * 3, line.AvgFontSize * 2);
            }
            else
            {
                gapThreshold = line.AvgFontSize * 3;
            }
            var currentSplit = new TextLine();
            currentSplit.Fragments.Add(line.Fragments[0]);

            for (var i = 1; i < line.Fragments.Count; i++)
            {
                var prev = line.Fragments[i - 1];
                var curr = line.Fragments[i];
                var prevRight = prev.Rectangle?.URX ?? GetX(prev);
                var currLeft = curr.Rectangle?.LLX ?? GetX(curr);
                var gap = currLeft - prevRight;

                if (gap > gapThreshold)
                {
                    currentSplit.Recalc();
                    splitLines.Add(currentSplit);
                    currentSplit = new TextLine();
                }
                currentSplit.Fragments.Add(curr);
            }

            currentSplit.Recalc();
            splitLines.Add(currentSplit);
        }

        return splitLines;
    }

    /// <summary>
    /// Groups lines into paragraphs within a section.
    /// Rules: vertical gaps below the section threshold never
    /// split a paragraph; a line starts a new one only on a content trigger, and
    /// every trigger requires the line to lead with a capital letter:
    ///  - T-indent : the line starts more than ~0.55·F right of the paragraph's
    ///               left edge;
    ///  - T-numeric: the first token is a pure number, the previous line ends with
    ///               a period, and a capital follows the number;
    ///  - T-space  : the text begins with literal whitespace before the capital and
    ///               the previous line stops ≥ ~1.5 em short of the block's right edge.
    /// </summary>
    /// <summary>A line whose ink stops more than this many em before the section's
    /// right edge is SHORT (probed: a 4 em gap is short, 3 em is not; 2.9 em kept a
    /// heading pair together, 4.6 em split one).</summary>
    private const double ShortLineGapEm = 3.5;

    /// <summary>The smallest left-edge shift that, after a short line, opens a
    /// paragraph (probed: 1 pt splits at 10 pt; 0.06 pt does not).</summary>
    private const double MinShiftPt = 1.0;

    /// <summary>The same threshold scaled for large faces, where a glyph's side
    /// bearing alone moves a left edge by a few tenths of a point.</summary>
    private const double MinShiftEm = 0.1;

    /// <summary>The shift a non-capital lead needs after a short line (a half-em
    /// page number stays, a 26 em table row leaves).</summary>
    private const double LargeShiftEm = 2.0;

    private static List<MarkupParagraph> GroupIntoParagraphs(List<TextLine> lines, double pageBodyRight = double.NaN)
    {
        if (lines.Count == 0) return [];

        static string LineText(TextLine l) => string.Concat(l.Fragments.Select(f => f.Text));

        static bool LeadsWithCapital(string s, out int capIdx)
        {
            // Skip whitespace AND non-letter marks (bullets, dashes) — a bullet
            // item "• ESRI shape…" leads with the capital E. Digits stop the scan
            // (T-numeric owns numbered lines). A Han ideograph lead counts as a
            // capital: a CJK paragraph opens on its 2-em first-line indent
            // (measured on a two-column paper — every split lands on
            // an ideograph-led indented line, and same-edge CJK lines never split).
            for (var k = 0; k < s.Length; k++)
            {
                var c = s[k];
                if (char.IsUpper(c) || c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') { capIdx = k; return true; }
                if (char.IsLetter(c) || char.IsDigit(c)) { capIdx = k; return false; }
            }
            capIdx = -1; return false;
        }

        static bool LeadsWithLowercase(string s)
        {
            foreach (var c in s)
            {
                if (char.IsLetter(c)) return char.IsLower(c);
                if (char.IsDigit(c)) return false;
            }
            return true; // no letter or digit at all: never a paragraph lead
        }

        // The lead before the capital is a MARK - an opening quotation mark,
        // bracket, guillemet or apostrophe - and nothing else: a dash ("-Asp.Net")
        // or a bullet glyph leads a list item, and consecutive items stay in one
        // paragraph.
        static bool LeadsWithMark(string s, int capIdx)
        {
            var any = false;
            for (var k = 0; k < capIdx; k++)
            {
                var c = s[k];
                if (char.IsWhiteSpace(c)) continue;
                var cat = char.GetUnicodeCategory(c);
                var isMark = cat is System.Globalization.UnicodeCategory.OpenPunctuation
                    or System.Globalization.UnicodeCategory.InitialQuotePunctuation
                    or System.Globalization.UnicodeCategory.FinalQuotePunctuation
                    || c is '"' or '\'';
                if (!isMark) return false;
                any = true;
            }
            return any;
        }

        // A bullet-led line opens a hanging list item: its continuation lines sit
        // at the text indent, so the usual first-line-indent trigger does not
        // apply inside it, and leaving the item is itself a paragraph boundary.
        static bool BulletLead(string s)
        {
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                return "■□▪▫●○•‣◦·★☆►▶".IndexOf(c) >= 0;
            }
            return false;
        }

        // First token is a pure number ("1", "12", "1.2", "1,2") and a capital
        // letter follows it on the line.
        static bool NumericLead(string s)
        {
            var t = s.TrimStart();
            var k = 0;
            while (k < t.Length && (char.IsDigit(t[k]) || ((t[k] == '.' || t[k] == ',')
                   && k + 1 < t.Length && char.IsDigit(t[k + 1])))) k++;
            if (k == 0 || (k < t.Length && !char.IsWhiteSpace(t[k]))) return false;
            while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
            return k < t.Length && char.IsUpper(t[k]);
        }

        if (GridDebug)
            foreach (var l in lines)
                Console.Error.WriteLine($"[line] mid={l.MidY:F3} y={l.MinY:F3}..{l.MaxY:F3} x={l.MinX:F1} n={l.Fragments.Count} '{LineText(l)[..Math.Min(20, LineText(l).Length)]}'");
        var paragraphs = new List<MarkupParagraph>();
        var currentLines = new List<TextLine> { lines[0] };
        var paraLeft = lines[0].MinX;
        // The section's own right edge: a line is judged "short" against the
        // column it sits in, not the page body (a centred heading in a middle
        // column is not short by two columns' worth).
        var sectionRight = lines.Max(l => l.MaxX);
        // The T-space right edge is the PAGE body's right margin (a section may be
        // narrower than the column it sits in).
        var bodyRight = double.IsNaN(pageBodyRight) ? lines.Max(l => l.MaxX) : pageBodyRight;

        for (var i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var curr = lines[i];
            var f = curr.AvgFontSize > 0 ? curr.AvgFontSize : 12;
            var prevF = prev.AvgFontSize > 0 ? prev.AvgFontSize : 12;
            var text = LineText(curr);
            var prevRaw = LineText(prev);
            var prevText = prevRaw.TrimEnd();
            var capital = LeadsWithCapital(text, out var capIdx);

            // T-indent presumes left-aligned flow. A block that is NOT left-aligned
            // but IS right- or centre-aligned (a right-ragged-left header)
            // scatters its left edges by design, so an "indent" there carries no
            // meaning. A single-line paragraph classifies as left-aligned.
            // The paragraph's left-edge SCATTER separates flow text from
            // right-/centre-placed header blocks: a first-line indent leaves a
            // small (≤ ~4 em) scatter, while a right-anchored header
            // block scatters most of its width.
            double leftScatter = 0;
            foreach (var pl in currentLines)
                leftScatter = Math.Max(leftScatter, pl.MinX - paraLeft);
            var suppressIndent = leftScatter > 4 * f;
            // Inside a bullet item the continuation edge IS an indent relative to
            // the bullet column, so T-indent stays quiet there ("■English…" item's
            // capital-led "United Nations…" continuation).
            var paraBullet = BulletLead(LineText(currentLines[0]));
            var currBullet = BulletLead(text);
            var tIndent = capital && !suppressIndent && !paraBullet && curr.MinX > paraLeft + 0.55 * f;
            // T-outdent: a hanging-indent paragraph (first line outdented, continuation
            // lines indented — bullet/definition lists) starts anew when a line begins
            // LEFT of the continuation edge (consecutive "■ …" list items).
            var tOutdent = false;
            if (capital && currentLines.Count >= 2)
            {
                var contLeft = double.MaxValue;
                for (var li = 1; li < currentLines.Count; li++)
                    contLeft = Math.Min(contLeft, currentLines[li].MinX);
                tOutdent = curr.MinX < contLeft - 0.55 * f;
            }
            var tNumeric = prevText.EndsWith(".") && NumericLead(text);
            // T-font: a pronounced font-size change is a boundary of its own — a
            // 13.3-pt heading line ("Officer") against the 8-pt list body below it.
            // The sizes compared are the CURRENT line's first fragment against the
            // PREVIOUS line's last fragment (the font-size rule) — a line
            // AVERAGE dragged down by superscript citation runs must not read as a
            // size change (the ［11-18］ citations).
            var currLeadF = curr.Fragments.Count > 0 && curr.Fragments[0].FontSize > 0
                ? (double)curr.Fragments[0].FontSize : f;
            var prevTailF = prev.Fragments.Count > 0 && prev.Fragments[^1].FontSize > 0
                ? (double)prev.Fragments[^1].FontSize : prevF;
            var tFont = capital && Math.Max(currLeadF, prevTailF) > 1.25 * Math.Min(currLeadF, prevTailF);
            // T-bullet: a bullet line always OPENS a list item, and a non-bullet
            // line returning to the bullet column CLOSES one ("■Place: …" followed
            // by "Closing Date: …" at the same left edge).
            var tBullet = currBullet && !paraBullet
                          || paraBullet && currBullet && curr.MinX < paraLeft + 0.55 * f
                          || paraBullet && !currBullet && capital && curr.MinX < paraLeft + 0.55 * f;
            // The literal whitespace that separates the paragraphs may sit at the
            // START of this line or (with our line assembly) as the TRAILING space
            // run of the previous one; the right-edge gap is measured to the
            // previous line's ink (trailing spaces excluded).
            var trailingSpaces = prevRaw.Length - prevText.Length;
            var prevInkRight = prev.MaxX - trailingSpaces * 0.25 * prevF;
            var tSpace = capital && capIdx > 0 && text.Length > 0 && char.IsWhiteSpace(text[0])
                         && (bodyRight - prevInkRight) > 1.5 * prevF;
            // T-shift: after a SHORT line (ink ending more than ShortLineGapEm of its
            // size before the section's right edge) a capital-led line that starts
            // anywhere but on the previous line's left edge opens a paragraph - the
            // reference splits "Senior" / "Macro-Economist" (outdent 35) and
            // "Finance" / "Officer" (indent 3.7) but keeps "Senior Specialist in" /
            // "International Labour Standards" (gap 2.9 em) together. Probed on
            // equal-pitch Courier lines: a 4 em gap splits at a 1 pt shift either
            // way, a 3 em gap does not, and a same-edge line never splits this way.
            var prevShort = (sectionRight - prevInkRight) > ShortLineGapEm * prevF;
            // A lead that is not a capital (a digit, a bracketed number) needs a
            // shift of whole ems: a page number "6" half an em under a short last
            // line joins the paragraph (it is kept), a "(100) HAMBURG"
            // row outdented by 290 pt under "DEPARTURE PAGE :" opens one.
            var shift = Math.Abs(curr.MinX - prev.MinX);
            var tShift = prevShort
                         && (capital ? shift >= Math.Max(MinShiftPt, MinShiftEm * f)
                                     : !LeadsWithLowercase(text) && shift >= LargeShiftEm * f);

            // T-mark: a line that OPENS with a mark - a quotation mark, bracket,
            // guillemet, apostrophe - and then a capital letter starts a paragraph
            // on its own, with no indent and no gap (a newspaper quote paragraph
            // '"As a general rule ...' right under 'can be used. '). Probed on the
            // reference with equal-width lines: “As / "Pi / «Gamma / (Kappa / 'Alpha
            // all split; “1984, ‘theta and a bare capital (Theta) do not.
            var tMark = capital && capIdx > 0 && !currBullet && LeadsWithMark(text, capIdx);

            if (GridDebug)
                Console.Error.WriteLine($"[para] minX={curr.MinX:F1} paraLeft={paraLeft:F1} f={f:F1} cap={capital} scat={leftScatter:F0} supp={suppressIndent} tI={tIndent} tN={tNumeric} tS={tSpace} tO={tOutdent} tF={tFont} tB={tBullet} tM={tMark} tSh={tShift} '{text[..Math.Min(24, text.Length)]}'");
            if (tIndent || tNumeric || tSpace || tOutdent || tFont || tBullet || tMark || tShift)
            {
                paragraphs.Add(BuildParagraph(currentLines));
                currentLines = [curr];
                paraLeft = curr.MinX;
            }
            else
            {
                currentLines.Add(curr);
                paraLeft = Math.Min(paraLeft, curr.MinX);
            }
        }

        if (currentLines.Count > 0)
            paragraphs.Add(BuildParagraph(currentLines));

        return paragraphs;
    }

    /// <summary>Re-join a paragraph's per-line fragments into text, consulting the page's
    /// standalone space glyphs: a space is inserted between two fragments when either the
    /// plain horizontal-gap heuristic fires or a space glyph sits between them (documents
    /// that draw every inter-word space as its own overlapping run leave a near-zero gap).</summary>
    private static string AssembleTextWithSpaces(
        List<List<TextFragment>> lines, List<TextFragment> spaces)
    {
        static bool SpaceGlyphAt(List<TextFragment> spaces, Rectangle anchor, double fromX, double toX)
        {
            foreach (var s in spaces)
            {
                var r = s.Rectangle!;
                var cx = (r.LLX + r.URX) / 2;
                if (cx < fromX - 1) continue;
                if (cx > toX + r.Width) break;              // spaces sorted by LLX
                if (r.LLY < anchor.URY && r.URY > anchor.LLY) return true;
            }
            return false;
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            for (var fi = 0; fi < line.Count; fi++)
            {
                var f = line[fi];
                if (fi > 0 && f.Rectangle is not null)
                {
                    var prevFrag = line[fi - 1];
                    if (prevFrag.Rectangle is not null)
                    {
                        var gap = f.Rectangle.LLX - prevFrag.Rectangle.URX;
                        var spaceGlyph = gap <= f.FontSize * 0.15
                            && SpaceGlyphAt(spaces, f.Rectangle, prevFrag.Rectangle.URX, f.Rectangle.LLX);
                        // A positive word-sized gap ALWAYS gets a boundary space —
                        // even when the left fragment already ends with one
                        // ("queries  or" is emitted there); the space-glyph
                        // re-insertion keeps the duplicate guard.
                        if (gap > f.FontSize * 0.15
                            || (spaceGlyph && !prevFrag.Text.EndsWith(" ") && !f.Text.StartsWith(" ")))
                            sb.Append(' ');
                    }
                }
                sb.Append(f.Text);
            }
            // Standalone space glyphs at the end of a line are dropped (a trailing
            // space is kept only when it is part of the last word's own run).
            sb.Append("\r\n");
        }
        var text = sb.ToString();
        if (text.EndsWith("\r\n"))
            text = text[..^2];
        return text;
    }

    /// <summary>Punctuation that closes what precedes it and never takes a space
    /// in front of it.</summary>
    private static bool IsClosingPunctuation(char c) =>
        c is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']' or '}'
          or '%' or '’' or '”' or '»';

    /// <summary>A one-space fragment covering the pen gap between two drawn runs,
    /// seated on the gap itself and carrying the following run's text state.</summary>
    private static TextFragment GapSpaceFragment(TextFragment left, TextFragment right)
    {
        var l = left.Rectangle!;
        var r = right.Rectangle!;
        var rect = new Rectangle(l.URX, System.Math.Min(l.LLY, r.LLY),
                                 r.LLX, System.Math.Max(l.URY, r.URY));
        return new TextFragment(" ", rect, right.TextState);
    }

    private static MarkupParagraph BuildParagraph(List<TextLine> lines)
    {
        var sb = new StringBuilder();
        var lineFragments = new List<List<TextFragment>>();

        foreach (var line in lines)
        {
            var frags = new List<TextFragment>(line.Fragments.Count);
            lineFragments.Add(frags);

            for (var fi = 0; fi < line.Fragments.Count; fi++)
            {
                var f = line.Fragments[fi];
                // Insert space between fragments when there's a horizontal gap
                if (fi > 0 && f.Rectangle is not null)
                {
                    var prevFrag = line.Fragments[fi - 1];
                    if (prevFrag.Rectangle is not null)
                    {
                        var gap = f.Rectangle.LLX - prevFrag.Rectangle.URX;
                        if (gap > f.FontSize * 0.15)
                        {
                            sb.Append(' ');
                            // The word gap becomes a real fragment spanning it, so a caller
                            // walking Lines and concatenating fragment text reads the same
                            // string as the paragraph's own Text. A source that draws each
                            // glyph as its own run (and leaves the inter-word spacing to the
                            // pen) otherwise reads back as one unbroken word.
                            //
                            // Two gaps are NOT word spaces and get no fragment: one whose
                            // boundary the source already spells with a space character of
                            // its own (adding another would double it), and one between runs
                            // set at DIFFERENT sizes — that is a tracked heading or a raised
                            // initial being positioned, not a space between words.
                            var alreadySpaced = prevFrag.Text.EndsWith(" ", StringComparison.Ordinal)
                                || f.Text.StartsWith(" ", StringComparison.Ordinal);
                            var sameSize = System.Math.Abs(prevFrag.FontSize - f.FontSize) < 0.01;
                            // Nor does a gap in front of closing punctuation: a slanted or
                            // swashed final glyph leaves room after its box that the comma
                            // sits in, and no space belongs there.
                            var leadsPunctuation = f.Text.Length > 0 && IsClosingPunctuation(f.Text[0]);
                            if (!alreadySpaced && sameSize && !leadsPunctuation)
                                frags.Add(GapSpaceFragment(prevFrag, f));
                        }
                    }
                }
                frags.Add(f);
                sb.Append(f.Text);
            }

            sb.Append("\r\n");
        }

        var points = OutlinePolygon(lines);

        var text = sb.ToString();
        if (text.EndsWith("\r\n"))
            text = text[..^2];

        return new MarkupParagraph(text, points, lineFragments);
    }

    /// <summary>Two consecutive line edges closer than this share one polygon
    /// edge (the lower line's value for the right side, the upper line's for the
    /// left). Edges 0.25 pt apart merge; edges 1.79 pt apart step.</summary>
    private const double OutlineEdgeTolerance = 1.0;

    /// <summary>
    /// The paragraph polygon reported: the rectilinear OUTLINE of the
    /// stacked line boxes, counter-clockwise from the bottom line's lower-left
    /// corner - along the bottom line, up the right side stepping in or out where
    /// a line's right edge moves, across the top line, and down the left side
    /// stepping at a first-line indent. A plain paragraph yields the four corners
    /// LL, LR, UR, UL; a short last line or an indented first line adds a step.
    /// </summary>
    private static Point[] OutlinePolygon(List<TextLine> lines)
    {
        // A line's vertical extent in the outline is its LEFTMOST fragment's box: a
        // bullet glyph seated 1.6 pt above its item text sets both the item's
        // lower-left corner and the step above it (a "Place: ..." item's
        // corner sits at the bullet's bottom, not the text's).
        static (double bottom, double top) Extent(TextLine l)
        {
            TextFragment? leftmost = null;
            foreach (var f in l.Fragments)
                if (leftmost is null || (f.Rectangle?.LLX ?? GetX(f)) < (leftmost.Rectangle?.LLX ?? GetX(leftmost)))
                    leftmost = f;
            return leftmost?.Rectangle is { } r ? (r.LLY, r.URY) : (l.MinY, l.MaxY);
        }

        // lines are top-to-bottom; walk the right side bottom-up, the left side top-down.
        var pts = new List<Point>();
        var bottom = lines[^1];
        var top = lines[0];

        var right = bottom.MaxX;
        pts.Add(new Point(bottom.MinX, Extent(bottom).bottom));
        pts.Add(new Point(right, Extent(bottom).bottom));
        for (var i = lines.Count - 1; i > 0; i--)
        {
            var next = lines[i - 1];
            if (Math.Abs(next.MaxX - right) < OutlineEdgeTolerance) continue;
            pts.Add(new Point(right, Extent(lines[i]).top));
            right = next.MaxX;
            pts.Add(new Point(right, Extent(next).bottom));
        }
        pts.Add(new Point(right, Extent(top).top));

        var left = top.MinX;
        pts.Add(new Point(left, Extent(top).top));
        for (var i = 0; i < lines.Count - 1; i++)
        {
            var next = lines[i + 1];
            if (Math.Abs(next.MinX - left) < OutlineEdgeTolerance) continue;
            pts.Add(new Point(left, Extent(lines[i]).bottom));
            left = next.MinX;
            pts.Add(new Point(left, Extent(next).top));
        }
        // The walk closes on the first point; a left step right above the bottom
        // line would duplicate it, so the start corner takes the bottom run's edge.
        if (Math.Abs(left - pts[0].X) >= OutlineEdgeTolerance)
            pts[0] = new Point(left, pts[0].Y);
        return pts.ToArray();
    }

    private static double GetX(TextFragment f) =>
        f.PositionOrNull?.XIndent ?? f.Rectangle?.LLX ?? 0;

    private static double GetY(TextFragment f) =>
        f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;

    private static double Median(List<double> list)
    {
        if (list.Count == 0) return 0;
        var sorted = list.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    /// <summary>
    /// Represents a horizontal line of text.
    /// </summary>
    internal sealed class TextLine
    {
        public List<TextFragment> Fragments { get; } = [];
        public double MidY { get; private set; }
        public double MidX { get; private set; }
        public double MinY { get; private set; }
        public double MaxY { get; private set; }
        public double MinX { get; private set; }
        public double MaxX { get; private set; }
        public double AvgFontSize { get; private set; }

        public void Recalc()
        {
            MinX = Fragments.Min(f => f.Rectangle?.LLX ?? GetX(f));
            MaxX = Fragments.Max(f => f.Rectangle?.URX ?? GetX(f));
            MinY = Fragments.Min(f => f.Rectangle?.LLY ?? GetY(f));
            MaxY = Fragments.Max(f => f.Rectangle?.URY ?? (GetY(f) + f.FontSize));
            MidY = (MinY + MaxY) / 2;
            MidX = (MinX + MaxX) / 2;
            // Use actual rendered height for effective font size (handles scaled fonts with FontSize=1)
            var avgHeight = Fragments
                .Where(f => f.Rectangle is not null && f.Rectangle.Height > 0)
                .Select(f => f.Rectangle!.Height)
                .DefaultIfEmpty(12)
                .Average();
            var avgFontSz = Fragments.Average(f => f.FontSize > 0 ? f.FontSize : 12);
            AvgFontSize = Math.Max(avgHeight, avgFontSz);
        }

        private static double GetX(TextFragment f) =>
            f.PositionOrNull?.XIndent ?? f.Rectangle?.LLX ?? 0;
        private static double GetY(TextFragment f) =>
            f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;
    }
}
