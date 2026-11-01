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
                // The space between a bold label and its value sits at the START of the plain
                // run (`<b>Tel:</b> 27 31…`) — keep it, or the runs never reconcile with the line
                // (word mail and the built-in-face cells; the form-grid and UA dialects keep their
                // calibrated reconciliation).
                if ((ps.wordMailCells || !(ps.uaCellBoxes || ps.formGridDialect))
                    && runSegs.Count > 0 && char.IsWhiteSpace(rawSeg[0])
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

    /// <summary>Lines k and k+1 were rejoined into line k: the runs either line recorded move onto the
    /// joined line when they still spell it (the whitespace line before a paragraph joining the
    /// paragraph's first line), and every later line's runs move up one, so no line takes the runs
    /// - and so the text - of the line before it.</summary>
    private static void RejoinLineRuns(TableParseState ps, int k, string joined)
    {
        if (ps.lineRunsByIdx is not { Count: > 0 } map) return;
        bool Spells(List<(string Text, bool Bold)> runs) => string.Concat(runs.ConvertAll(r => r.Text)) == joined;
        List<(string Text, bool Bold)>? kept = null;
        if (map.TryGetValue(k, out var first) && Spells(first)) kept = first;
        else if (map.TryGetValue(k + 1, out var second) && Spells(second)) kept = second;
        var moved = new Dictionary<int, List<(string Text, bool Bold)>>();
        foreach (var kv in map)
        {
            if (kv.Key < k) moved[kv.Key] = kv.Value;
            else if (kv.Key > k + 1) moved[kv.Key - 1] = kv.Value;
        }
        if (kept is not null) moved[k] = kept;
        ps.lineRunsByIdx = moved;
    }
}
