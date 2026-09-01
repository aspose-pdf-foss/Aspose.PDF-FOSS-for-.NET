using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Stamps;

public partial class TextStamp
{
    // Greedy space-only word wrap: pack whole words onto a line until the next word would
    // exceed maxW; a single word wider than maxW gets its own (overflowing) line. Explicit
    // '\n' forces a break. Words are measured with the real face metrics (MeasureRow).
    // Largest font size (bisected to AutoFitPrecision) at which the word-wrapped
    // text still fits Width×Height. The block is N wrapped lines tall, each line
    // one em of leading with a 1.1-em box on the single line, so the block height
    // is (N + 0.1)·fontSize. Width binds only when a single word is itself wider
    // than the box (an unbreakable word ⇒ the search collapses to 0).
    private double ComputeAutoFitFontSize(string baseFont, byte[] encoded)
    {
        var prec = AutoFitPrecision > 0 ? AutoFitPrecision : 0.1;
        double lo = 0, hi = 2000;
        if (!AutoFitFits(hi, baseFont, encoded))
        {
            while (hi - lo > prec)
            {
                var mid = (lo + hi) / 2;
                if (AutoFitFits(mid, baseFont, encoded)) lo = mid; else hi = mid;
            }
            return lo;
        }
        return hi;
    }

    private bool AutoFitFits(double f, string baseFont, byte[] encoded)
    {
        if (f <= 0) return true;
        var rows = AutoFitWrap(encoded, baseFont, f, Width);
        double blockWidth = 0;
        foreach (var r in rows)
        {
            var w = MeasureRow(r, baseFont, f);
            if (w > blockWidth) blockWidth = w;
        }
        var blockHeight = (rows.Count + 0.1) * f;
        return blockWidth <= Width + 1e-6 && blockHeight <= Height + 1e-6;
    }

    // Greedy word wrap for the auto-fit measurement. Unlike the render-side
    // WrapAtSpaces, the fit test ignores each line's TRAILING space: a word joins
    // the current line when (line + inter-word space + word), with no trailing
    // space, still fits — the box-fit rule.
    // Rows are returned already trailing-trimmed so their measured width is exact.
    private static List<byte[]> AutoFitWrap(byte[] enc, string baseFont, double fontSize, double maxW)
    {
        var rows = new List<byte[]>();
        foreach (var lineSeg in SplitRows(enc))
        {
            var words = new List<byte[]>();
            var w = new List<byte>();
            foreach (var b in lineSeg)
            {
                w.Add(b);
                if (b == (byte)' ') { words.Add(w.ToArray()); w = new List<byte>(); }
            }
            if (w.Count > 0) words.Add(w.ToArray());

            var cur = new List<byte>();
            foreach (var word in words)
            {
                if (cur.Count == 0) { cur.AddRange(word); continue; }
                var trial = new List<byte>(cur); trial.AddRange(word);
                if (MeasureRow(TrimTrailingSpace(trial), baseFont, fontSize) > maxW)
                {
                    rows.Add(TrimTrailingSpace(cur));
                    cur = new List<byte>(word);
                }
                else cur = trial;
            }
            rows.Add(TrimTrailingSpace(cur));
        }
        return rows.Count == 0 ? new List<byte[]> { enc } : rows;
    }

    private static List<byte[]> WrapAtSpaces(byte[] enc, string baseFont, double fontSize, double maxW)
    {
        var rows = new List<byte[]>();
        foreach (var lineSeg in SplitRows(enc))
        {
            var words = new List<byte[]>();
            var w = new List<byte>();
            foreach (var b in lineSeg)
            {
                w.Add(b);
                if (b == (byte)' ') { words.Add(w.ToArray()); w = new List<byte>(); }
            }
            if (w.Count > 0) words.Add(w.ToArray());

            var cur = new List<byte>();
            foreach (var word in words)
            {
                if (cur.Count == 0) { cur.AddRange(word); continue; }
                var trial = new List<byte>(cur); trial.AddRange(word);
                if (MeasureRow(trial.ToArray(), baseFont, fontSize) > maxW)
                {
                    rows.Add(TrimTrailingSpace(cur));
                    cur = new List<byte>(word);
                }
                else cur = trial;
            }
            rows.Add(TrimTrailingSpace(cur));
        }
        return rows.Count == 0 ? new List<byte[]> { enc } : rows;
    }

    private static byte[] TrimTrailingSpace(List<byte> row)
    {
        var n = row.Count;
        while (n > 0 && row[n - 1] == (byte)' ') n--;
        return row.GetRange(0, n).ToArray();
    }

    // Flatten an encoded buffer to a single line: newlines become spaces.
    private static byte[] JoinToOneLine(byte[] enc)
    {
        var outp = new byte[enc.Length];
        for (var i = 0; i < enc.Length; i++)
            outp[i] = enc[i] == (byte)'\n' ? (byte)' ' : enc[i];
        return outp;
    }

    // Split an encoded (1-byte-per-char) buffer on '\n' into rows, dropping the
    // newline bytes. Always yields at least one row.
    private static List<byte[]> SplitRows(byte[] enc)
    {
        var rows = new List<byte[]>();
        var cur = new List<byte>();
        foreach (var b in enc)
        {
            if (b == (byte)'\n') { rows.Add(cur.ToArray()); cur.Clear(); }
            else cur.Add(b);
        }
        rows.Add(cur.ToArray());
        return rows;
    }

    private static Aspose.Pdf.Text.TrueTypeParser? ResolveMetricParser(string family)
    {
        lock (_metricParsersLock)
        {
            if (_metricParsers.TryGetValue(family, out var cached)) return cached;
            Aspose.Pdf.Text.TrueTypeParser? parser = null;
            try
            {
                var ttf = Aspose.Pdf.Text.SystemFontResolver.Resolve(family);
                if (ttf is { Length: > 0 })
                {
                    var p = new Aspose.Pdf.Text.TrueTypeParser(ttf);
                    p.Parse();
                    if (p.UnitsPerEm > 0 && p.GlyphWidths.Length > 0)
                        parser = p;
                }
            }
            catch { parser = null; }
            _metricParsers[family] = parser;
            return parser;
        }
    }

    private static double MeasureRow(byte[] row, string baseFont, double fontSize)
    {
        // Real font families (e.g. Arial — even though it aliases onto Helvetica's
        // AFM) are measured from the resolved face's unrounded hmtx advances when a
        // system face is available: the integer-1/1000 path rounds e.g. Arial 'T'
        // (610.84) to 611, losing the precision an exact text-width assertion needs.
        // Genuine Core-14 names (Helvetica/Times/Courier) keep their AFM table.
        var std14 = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(baseFont);
        if (!Aspose.Pdf.Text.Standard14Fonts.IsCoreName(baseFont))
        {
            var parser = ResolveMetricParser(baseFont);
            if (parser is not null)
            {
                var text = Aspose.Pdf.Text.Cp1252.GetString(row);
                double units = 0;
                foreach (var ch in text)
                {
                    if (parser.CMap.TryGetValue(ch, out var gid) && gid >= 0 && gid < parser.GlyphWidths.Length)
                        units += parser.GlyphWidths[gid];
                    else
                        units += parser.UnitsPerEm * 0.5;
                }
                return units * fontSize / parser.UnitsPerEm;
            }
        }
        double w = 0;
        foreach (var b in row)
            w += (std14 ? Aspose.Pdf.Text.Standard14Fonts.GetWidth(baseFont, b) : 500) / 1000.0 * fontSize;
        return w;
    }

    // Break the (1-byte-per-char) encoded stamp text into rows no wider than
    // <paramref name="maxW"/> points. Prefer breaking at spaces; a word longer
    // than the row is split with a trailing hyphen (discretionary hyphenation).
    // Explicit '\n' always starts a new row.
    private static List<byte[]> WrapEncoded(byte[] enc, string baseFont, double fontSize, double maxW)
    {
        var std14 = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(baseFont);
        double W(int b) => (std14 ? Aspose.Pdf.Text.Standard14Fonts.GetWidth(baseFont, b) : 500) / 1000.0 * fontSize;

        var rows = new List<byte[]>();
        var cur = new List<byte>();
        double curW = 0;
        var lastSpace = -1; // index in cur of the most recent space

        foreach (var b in enc)
        {
            if (b == (byte)'\n')
            {
                rows.Add(cur.ToArray());
                cur.Clear(); curW = 0; lastSpace = -1;
                continue;
            }
            var bw = W(b);
            if (cur.Count > 0 && curW + bw > maxW)
            {
                if (lastSpace >= 0)
                {
                    rows.Add(cur.GetRange(0, lastSpace).ToArray());
                    cur = cur.GetRange(lastSpace + 1, cur.Count - lastSpace - 1);
                    curW = 0; foreach (var rb in cur) curW += W(rb);
                    lastSpace = -1;
                }
                else
                {
                    // Long unbreakable word: end the row with a hyphen and carry the
                    // current char to a fresh row. curW is already <= maxW, so the
                    // hyphen adds at most one glyph's width.
                    var hy = new List<byte>(cur) { (byte)'-' };
                    rows.Add(hy.ToArray());
                    cur.Clear(); curW = 0; lastSpace = -1;
                }
            }
            cur.Add(b); curW += bw;
            if (b == (byte)' ') lastSpace = cur.Count - 1;
        }
        if (cur.Count > 0) rows.Add(cur.ToArray());
        return rows.Count == 0 ? new List<byte[]> { enc } : rows;
    }
}
