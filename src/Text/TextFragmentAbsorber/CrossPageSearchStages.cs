using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>The stages of the cross-page search: collecting one page's runs into the concatenated text, and emitting one match as a fragment.</summary>
    private bool EmitCrossPageMatch(CrossPageSearchState xp, Match match)
    {
        if (match.Length == 0) return true;

        var startIdx = match.Index;
        var endIdx = match.Index + match.Length - 1;
        if (startIdx >= xp.charMap.Count || endIdx >= xp.charMap.Count) return true;

        // Find the first valid page/run for the match start
        var (startPageIdx, startRunIdx) = xp.charMap[startIdx];
        // Skip separators
        while (startPageIdx < 0 && startIdx <= endIdx)
        {
            startIdx++;
            if (startIdx < xp.charMap.Count) (startPageIdx, startRunIdx) = xp.charMap[startIdx];
        }
        if (startPageIdx < 0) return true;

        var startPage = xp.allPageRuns[startPageIdx].page;
        var startRuns = xp.allPageRuns[startPageIdx].runs;
        if (startRunIdx < 0 || startRunIdx >= startRuns.Count) return true;
        var firstRun = startRuns[startRunIdx];

        // Position from first run
        var (posX, posY) = ApplyCtm(firstRun.X, firstRun.Y, firstRun.Ctm);

        // Effective font size
        var upX_ = firstRun.TmC * firstRun.Ctm.A + firstRun.TmD * firstRun.Ctm.C;
        var upY_ = firstRun.TmC * firstRun.Ctm.B + firstRun.TmD * firstRun.Ctm.D;
        var tmScale = Math.Sqrt(upX_ * upX_ + upY_ * upY_);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? firstRun.FontSize * tmScale : firstRun.FontSize;

        var textState = new TextState
        {
            FontSize = (float)effectiveFs,
            FontName = firstRun.FontName,
            RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)firstRun.RenderingMode,
            LineWidth = firstRun.LineWidth,
            IsBold = firstRun.IsBold,
            IsItalic = firstRun.IsItalic,
            Font = firstRun.FontInfoObj ?? FontInfo.DefaultHelvetica,
            TextRise = firstRun.TextRise,
            IsSuperscript = firstRun.TextRise > 0,
            IsSubscript = firstRun.TextRise < 0,
        };
        textState.SetCapturedForegroundColor(ForegroundColorOf(firstRun));
        textState.StrokingColor = firstRun.StrokingColor;

        // Simple bounding rect from first run
        var w = firstRun.Width > 0 ? firstRun.Width : EstimateWidth(firstRun.Text, firstRun.FontSize);
        var h = firstRun.FontSize;
        var (px2, py2) = ApplyCtm(firstRun.X + w, firstRun.Y + h, firstRun.Ctm);
        var rect = new Rectangle(
            Math.Min(posX, px2), Math.Min(posY, py2),
            Math.Max(posX, px2), Math.Max(posY, py2));

        // Only the ANISOTROPIC part of the matrix is a horizontal scale. A matrix
        // that scales both axes alike carries the font size (a "1 Tf" run sized
        // by "7 0 0 7 Tm"), and that size is already in TextState.FontSize.
        textState.SourceTmScale = Math.Abs(firstRun.TmD) > 1e-9
            ? firstRun.TmA / firstRun.TmD
            : 1.0;
        var fragment = new TextFragment(LogicalizeRtlPresentationForms(match.Value), rect, textState)
        {
            PageIndex = startPage.Index,
            Position = new Position(Q(posX), Q(posY)),
            SourcePage = startPage,
            SourceXObjStream = firstRun.SourceXObj,
            ExtractionCtm = new Aspose.Pdf.Matrix(firstRun.Ctm.A, firstRun.Ctm.B, firstRun.Ctm.C, firstRun.Ctm.D, firstRun.Ctm.E, firstRun.Ctm.F),
            ExtractionTmTy = firstRun.TmBaseY,
        };

        _fragments.Add(fragment);
        return true;
    }

    /// <summary></summary>
    private bool CollectCrossPageRuns(CrossPageSearchState xp, int pi)
    {
        var (page, runs) = xp.allPageRuns[pi];
        var runStarts = new List<int>();
        xp.pageRunStartChars.Add(runStarts);

        // Insert page separator (except before first page)
        if (pi > 0 && xp.fullText.Length > 0)
        {
            xp.fullText.Append("\r\n");
            xp.charMap.Add((-1, -1)); // \r
            xp.charMap.Add((-1, -1)); // \n
        }

        for (int ri = 0; ri < runs.Count; ri++)
        {
            // Space insertion between runs on the same line
            if (ri > 0 && runs[ri].Text != "\r\n" && runs[ri - 1].Text != "\r\n")
            {
                var prev = runs[ri - 1];
                var deltaY = Math.Abs(runs[ri].Y - prev.Y);
                if (deltaY < 2.0)
                {
                    var prevEndX = prev.X + (prev.Width > 0 ? prev.Width * prev.HScaling : EstimateWidth(prev.Text, prev.FontSize));
                    var gap = runs[ri].X - prevEndX;
                    var fontSize = runs[ri].FontSize > 0 ? runs[ri].FontSize : 12.0;
                    var spaceThreshold = fontSize * 0.2;
                    var maxGap = fontSize * 3.0;
                    var lastChar = xp.fullText.Length > 0 ? xp.fullText[^1] : '\0';
                    var nextChar = runs[ri].Text.Length > 0 ? runs[ri].Text[0] : '\0';
                    // Require a real gap and avoid spacing inside letter-spaced words,
                    // where EVERY run is a single character. The earlier `both runs >= 2
                    // chars` rule was too strict: it also dropped the space at a word↔
                    // single-char-token boundary (e.g. "level" -> "1"), so a phrase search
                    // for "Heading level 1" failed to match the extracted "Heading level1".
                    // Suppress the space only when BOTH sides are single
                    // characters (the genuine letter-spacing case).
                    if (gap > spaceThreshold && gap <= maxGap && xp.fullText.Length > 0
                        && lastChar != ' ' && lastChar != '\n' && nextChar != ' '
                        && (prev.Text.Length >= 2 || runs[ri].Text.Length >= 2))
                    {
                        xp.charMap.Add((pi, ri - 1));
                        xp.fullText.Append(' ');
                    }
                }
            }

            runStarts.Add(xp.charMap.Count);
            var text = runs[ri].Text;
            // Keep newlines for regex
            foreach (var _ in text)
                xp.charMap.Add((pi, ri));
            xp.fullText.Append(text);
        }
        return true;
    }
}
