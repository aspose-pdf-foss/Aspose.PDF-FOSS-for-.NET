using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
// Quote schedule: 
    private static Text.Font Face(QuoteScheduleState qs, bool b, bool i) => b && i ? qs.boldItalic! : b ? qs.bold! : i ? qs.italic! : qs.regular!;

    private static double Measure(QuoteScheduleState qs, string t, double fontPx, bool b, bool i)
    {
        if (t.Length == 0) return 0;
        try { return Face(qs, b, i).MeasureString(t, (float)(fontPx * QsPxToPt)); }
        catch { return t.Length * fontPx * QsPxToPt * 0.5; }
    }

    private static List<string> Wrap(QuoteScheduleState qs, string text, double fontPx, double budget, bool b, bool i)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || Measure(qs, trial, fontPx, b, i) <= budget)
            { cur.Clear(); cur.Append(trial); }
            else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
    }

    private static void Rule(QuoteScheduleState qs, double x0, double x1, double y, double w)
        => qs.flow.AddContentToSlot(qs.flow.CurrentSlot, Encoding.ASCII.GetBytes(
            Compat.Format(qs.inv,
                $"q 0 0 0 RG {w:0.###} w {x0:F2} {y:F2} m {x1:F2} {y:F2} l S Q\n")));

    private static void Break(QuoteScheduleState qs)
    {
        qs.flow.ForceNewPage();
        // An overflow slot only becomes a Page when its CONTENT buffer holds
        // something, and this arm writes through the deferred text queue — so
        // seed the buffer or the slot (and everything queued on it) is dropped.
        qs.flow.InjectContentAtCursor(new byte[] { (byte)'\n' });
        qs.y = qs.contentTop;
    }
}
