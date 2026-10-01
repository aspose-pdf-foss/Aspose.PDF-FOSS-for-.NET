

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// The stages of the form-fill wrap: the coverage test, the measure, and the line placement.
    // Pick the face the lines are WRITTEN in: the source font when it covers the
    // replacement, else the same family's system face, else Times New Roman.
    private static bool Covers(Aspose.Pdf.Text.Font f, string s)
    {
        foreach (var ch in s)
            if (!char.IsWhiteSpace(ch) && !f.CanRepresent(ch)) return false;
        return true;
    }

    // Greedy wrap at spaces against the fragment width, measuring each candidate
    // line WITH its trailing separator space (which non-final lines keep). A
    // FindFont-produced face has stub FontInfo metrics (no font dict); its REAL
    // advances live on the attached FontData's raw TTF widths.
    private static double Measure(FormFillWrapState fw, string s)
    {
        try
        {
            if (fw.writeFont.SourceFontData is { } fd) return fd.MeasureString(s, fw.fs);
            return fw.writeFont.MeasureString(s, fw.fs);
        }
        catch { return s.Length * fw.fs * 0.5; }
    }

    /// <summary></summary>
    private void PlaceFormFillLines(FormFillWrapState fw)
    {
        for (int i = 0; i < fw.wrapped.Count; i++)
        {
            double by = fw.y0 - i * fw.step;
            var frag = new TextFragment(fw.wrapped[i]);
            frag.TextState.Font = fw.writeFont;
            frag.TextState.FontSize = (float)fw.fs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(fw.x0, by);
            fw.tb.AppendText(frag);
            double lw = Measure(fw, fw.wrapped[i]);
            fw.laidOut.Add((fw.wrapped[i], by, lw));
            if (lw > fw.maxLineW) fw.maxLineW = lw;
        }
        fw.page.ResetContentsCache();

        // Re-point this fragment at the laid-out block (mirrors the whole-paragraph
        // reflow): box LLY = last baseline, URY = first baseline + 1.1·fs.
        _rectangle = new Rectangle(fw.x0, fw.laidOut[^1].by, fw.x0 + fw.maxLineW, fw.laidOut[0].by + 1.1 * fw.fs);
        _segments.Clear();
        foreach (var ln in fw.laidOut)
        {
            var seg = new TextSegment(ln.text);
            seg.TextState.FontSize = (float)fw.fs;
            seg.TextState.FontName = fw.writeFont.FontName;
            seg.TextState.Font = fw.writeFont;
            seg.Owner = this;
            seg.Position = new Position(fw.x0, ln.by);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
    }

    /// <summary></summary>
    private void WrapFormFillWords(FormFillWrapState fw)
    {
        foreach (var word in fw.newText.Split(' '))
        {
            if (word.Length == 0) continue;
            var trial = fw.cur.Length == 0 ? word : fw.cur + " " + word;
            if (fw.cur.Length == 0 || Measure(fw, trial + " ") <= fw.width)
            {
                if (fw.cur.Length > 0) fw.cur.Append(' ');
                fw.cur.Append(word);
                continue;
            }
            fw.wrapped.Add(fw.cur.ToString() + " ");
            fw.cur.Clear();
            fw.cur.Append(word);
        }
    }
}
