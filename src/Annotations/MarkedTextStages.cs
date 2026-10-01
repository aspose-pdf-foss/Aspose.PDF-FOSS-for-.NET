using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class TextMarkupAnnotation : MarkupAnnotation
{
    /// <summary>The stages of the marked-text collection: one fragment against the quads, one line key, one output line.</summary>
    private void EmitMarkedLine(MarkedTextState mt, int li)
    {
        var line = mt.lines[li];
        line.Sort((a, b) => a.minX.CompareTo(b.minX));
        double lineRight = double.MinValue;
        foreach (var p in line) if (p.maxX > lineRight) lineRight = p.maxX;

        // A line ending well short of the block's right edge is a hard break (a label
        // or paragraph end), not a soft wrap: emit a trailing space on its last quad
        // to separate it from the next line. Wrapped lines that reach the margin join
        // with no gap. Each quad remains its own fragment (the fragment count matters).
        bool spaceAfterLine = li < mt.lines.Count - 1 && lineRight < mt.rightMargin - 20.0;
        for (var pi = 0; pi < line.Count; pi++)
        {
            var text = line[pi].text;
            if (spaceAfterLine && pi == line.Count - 1) text += " ";
            mt.result.Add(new Aspose.Pdf.Text.TextFragment(text));
        }
    }

    /// <summary>The stages of the marked-text collection: one fragment against the quads, one line key, one output line.</summary>
    private void CollectMarkedLineKey(MarkedTextState mt, (int q, int fi) key)
    {
        var list = mt.groups[key];
        if (list.Count == 0) return;
        list.Sort((a, b) => a.cx.CompareTo(b.cx));
        var sb = new System.Text.StringBuilder();
        var pieceMinX = double.MaxValue;
        foreach (var (ch, cx) in list) { sb.Append(ch); if (cx < pieceMinX) pieceMinX = cx; }
        var (_, minY, maxX, maxY) = mt.boxes[key.q];
        mt.pieces.Add(((minY + maxY) / 2.0, pieceMinX, maxX, sb.ToString()));
        if (maxX > mt.rightMargin) mt.rightMargin = maxX;
    }

    /// <summary>The stages of the marked-text collection: one fragment against the quads, one line key, one output line.</summary>
    private void MatchFragmentToQuads(MarkedTextState mt, Aspose.Pdf.Text.TextFragment f)
    {
        var runIndex = mt.fi++;
        foreach (Aspose.Pdf.Text.TextSegment seg in f.Segments)
        {
            var chars = seg.Characters;
            var text = seg.Text ?? string.Empty;
            for (var c = 1; c <= chars.Count && c <= text.Length; c++)
            {
                var r = chars[c].Rectangle;
                var cy = (r.LLY + r.URY) / 2.0;
                var bestQ = -1; var bestOv = mt.grazeTolerance;
                for (var q = 0; q < mt.boxes.Count; q++)
                {
                    var (minX, minY, maxX, maxY) = mt.boxes[q];
                    if (cy < minY - 2 || cy > maxY + 2) continue;
                    var overlapX = System.Math.Min(r.URX, maxX) - System.Math.Max(r.LLX, minX);
                    if (overlapX > bestOv) { bestOv = overlapX; bestQ = q; }
                }
                if (bestQ < 0) continue;
                var key = (bestQ, runIndex);
                if (!mt.groups.TryGetValue(key, out var list))
                {
                    list = new System.Collections.Generic.List<(char, double)>();
                    mt.groups[key] = list;
                    mt.order.Add(key);
                }
                list.Add((text[c - 1], (r.LLX + r.URX) / 2.0));
            }
        }
    }
}
