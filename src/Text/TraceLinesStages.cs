using System.Text;

namespace Aspose.Pdf.Text;

internal static partial class TextPaginator
{
    /// <summary>The stages of the line trace: one paragraph at a time.</summary>
    private static void TraceParagraphLines(TraceLinesState tl, int pi, double firstLineIndent, FontData? fontData, double maxWidth)
    {
        var paragraph = tl.paragraphs[pi];
        if (paragraph.Length == 0)
        {
            tl.raw.Add((string.Empty, 0, true, pi, false));
            tl.globalLineCount++;
            return;
        }
        var words = paragraph.Split(' ');
        var current = new StringBuilder();
        double currentWidth = 0;
        var paraLines = new List<(string, double)>();
        var keptBreakSpace = new List<bool>();
        for (var wi = 0; wi < words.Length; wi++)
        {
            var word = words[wi];
            // Preserve delimiter/extra spaces (kept in lock-step with
            // WrapToWidth above).
            if (word.Length == 0)
            {
                if (wi > 0) { current.Append(' '); currentWidth += tl.spaceWidth; }
                continue;
            }
            var wordWidth = tl.measurer(word);
            var sep = wi > 0 && current.Length > 0;
            var needed = wordWidth + (sep ? tl.spaceWidth : 0);
            var effectiveMax = tl.globalLineCount == 0 && paraLines.Count == 0
                ? maxWidth - firstLineIndent : maxWidth;
            if (currentWidth + needed > effectiveMax && current.Length > 0)
            {
                // Lock-step with WrapToWidth: the break space is reported only
                // when it fit the line.
                keptBreakSpace.Add(sep && currentWidth + tl.spaceWidth <= effectiveMax);
                paraLines.Add((current.ToString(), currentWidth));
                current.Clear();
                currentWidth = 0;
            }
            else if (sep) { current.Append(' '); currentWidth += tl.spaceWidth; }
            if (current.Length == 0 && wordWidth > effectiveMax && CharacterFillApplies(fontData))
            {
                var rest = word;
                while (rest.Length > 0)
                {
                    effectiveMax = tl.globalLineCount == 0 && paraLines.Count == 0
                        ? maxWidth - firstLineIndent : maxWidth;
                    (var take, var pieceWidth) = LongestFittingPrefix(rest, tl.measurer, effectiveMax);
                    if (take >= rest.Length)
                    {
                        current.Append(rest);
                        currentWidth = pieceWidth;
                        break;
                    }
                    keptBreakSpace.Add(false);
                    paraLines.Add((rest.Substring(0, take), pieceWidth));
                    rest = rest.Substring(take);
                }
            }
            else
            {
                current.Append(word);
                currentWidth += wordWidth;
            }
        }
        if (current.Length > 0) { paraLines.Add((current.ToString(), currentWidth)); keptBreakSpace.Add(false); }
        for (var li = 0; li < paraLines.Count; li++)
        {
            tl.raw.Add((paraLines[li].Item1, paraLines[li].Item2, li == paraLines.Count - 1, pi, keptBreakSpace[li]));
            tl.globalLineCount++;
        }
    }
}
