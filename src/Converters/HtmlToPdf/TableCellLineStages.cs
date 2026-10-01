using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A line carrying several run marks records where each run starts, so the draw can dress them separately.</summary>
    private static void ApplyLineRunMarks(TableParseState ps, string text)
    {
        if (ps.lineRunMarks is { Count: > 1 } && text.Length > 0)
        {
            var raw = ps.line.ToString();
            var runSegs = new List<(string Text, bool Bold)>();
            for (var mi = 0; mi < ps.lineRunMarks.Count; mi++)
            {
                var segEnd = mi + 1 < ps.lineRunMarks.Count ? ps.lineRunMarks[mi + 1].Pos : raw.Length;
                var rawSeg = raw[ps.lineRunMarks[mi].Pos..segEnd];
                var segText = CollapseWs(rawSeg);
                if (segText.Length == 0) continue;
                // Word mail: the space between a bold label and its value sits at the START of the
                // plain run (`<b>Tel:</b> 27 31…`) — keep it, or the runs never reconcile with the line.
                if (ps.wordMailCells && runSegs.Count > 0 && char.IsWhiteSpace(rawSeg[0])
                    && !runSegs[^1].Text.EndsWith(' ')) segText = " " + segText;
                if (segEnd < raw.Length && char.IsWhiteSpace(raw[segEnd - 1])) segText += " ";
                if (runSegs.Count > 0 && runSegs[^1].Bold == ps.lineRunMarks[mi].Bold)
                    runSegs[^1] = (runSegs[^1].Text + segText, runSegs[^1].Bold);
                else
                    runSegs.Add((segText, ps.lineRunMarks[mi].Bold));
            }
            var joined = string.Concat(runSegs.ConvertAll(r => r.Text));
            if (runSegs.Count > 1 && joined == text)
                (ps.lineRunsByIdx ??= new())[ps.lines.Count] = runSegs;
        }
    }
}
