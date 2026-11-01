using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Writes the flowed lines into the column: each run at its pen, justified lines spreading the slack over their word gaps, the column advancing by the line pitch.</summary>
    private static void EmitInlineColumnFlow(InlineColumnsState ic)
    {
        foreach (var (runs, justify) in ic.flow)
        {
            if (ic.y + ic.lineH > ic.bottom + 0.01)
            {
                if (++ic.col >= ic.nCols)
                {
                    ic.page = ic.doc.Pages.Add(ic.pageWidth, ic.pageHeight);
                    EnsureFonts(ic.page);
                    ic.col = 0;
                }
                ic.y = ic.marginTop;
            }
            var colX = ic.marginX + ic.col * (ic.colW + ic.gap);
            foreach (var (dx, text, bold) in runs)
            {
                var res = bold ? "F6" : "F5";
                var mFace = bold ? ic.face + " Bold" : ic.face;
                // A justified line stretches its word gaps to the column edge;
                // the paragraph's last line and every table cell stay natural.
                var tw2 = 0.0;
                if (justify)
                {
                    var spaces = 0;
                    foreach (var ch in text.Where(ch => ch == ' ')) spaces++;
                    var natural = MeasureFaceText(mFace, text, ic.fs);
                    if (spaces > 0 && natural < ic.colW)
                        tw2 = (ic.colW - natural) / spaces;
                }
                if (tw2 > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("BT");
                    sb.Append(Compat.Format(ic.inv, $"/{res} {ic.fs:F1} Tf {tw2:F3} Tw "));
                    sb.Append(Compat.Format(ic.inv, $"1 0 0 1 {colX + dx:F2} {ic.pageHeight - ic.y - ic.drop:F2} Tm "));
                    sb.Append($"({EscapePdfString(text)}) Tj 0 Tw ");
                    sb.AppendLine("ET");
                    ic.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else EmitPositionedRun(ic.page, res, ic.fs, colX + dx, ic.pageHeight - ic.y - ic.drop, text);
            }
            ic.y += ic.lineH;
        }
    }

    /// <summary>Collects the flow from the inline markup: each block's runs wrapped to the column width, blank lines between blocks, justified where the style says so.</summary>
    private static void CollectInlineColumnRuns(InlineColumnsState ic)
    {
        foreach (var groups in SafeRegex.Matches(ic.body, @"<(p|table)\b([^>]*)>([\s\S]*?)</\1\s*>",
                     RegexOptions.IgnoreCase).Select(im => im.Groups))
        {
            if (groups[1].Value.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                var inner = groups[3].Value;
                var text = CollapseWs(DecodeEntities(SafeRegex.Replace(inner, @"<[^>]+>", ""))).Trim();
                if (text.Length == 0) { AddBlank(ic); continue; }
                var justify = SafeRegex.IsMatch(groups[2].Value,
                    @"text-align\s*:\s*justify", RegexOptions.IgnoreCase);
                // A paragraph wholly inside <b>/<i> keeps that weight; mixed
                // emphasis draws at the paragraph's own weight (this dialect's
                // headings are wholly bold, its body wholly plain).
                var bold = SafeRegex.IsMatch(inner, @"^\s*(?:<[bi]\b[^>]*>\s*)+")
                    && SafeRegex.IsMatch(inner, @"(?:</[bi]\s*>\s*)+$");
                var lines = MeasuredWordWrap(text, ic.colW, bold ? ic.face + " Bold" : ic.face, ic.fs);
                for (var li = 0; li < lines.Length; li++)
                    ic.flow.Add((new List<(double, string, bool)> { (0, lines[li], bold) },
                        justify && li < lines.Length - 1));
                continue;
            }
            // A percent-width table: its columns take their declared shares of
            // that box, each cell's lines pouring through the same grid. A row
            // with no ink is a blank line, exactly like an empty paragraph.
            var tw = ic.colW;
            if (SafeRegex.Match(groups[2].Value, @"width\s*:\s*([\d.]+)%",
                    RegexOptions.IgnoreCase) is { Success: true } twM
                && double.TryParse(twM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var twPct))
                tw = ic.colW * twPct / 100.0;
            foreach (Match rm in SafeRegex.Matches(groups[3].Value,
                         @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
            {
                var cells = new List<(double W, string Text, bool Bold)>();
                foreach (var cmGroups in SafeRegex.Matches(rm.Groups[1].Value,
                             @"<t[dh]\b([^>]*)>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase).Select(cm => cm.Groups))
                {
                    var cw = tw / Math.Max(1, cells.Count + 1);
                    if (SafeRegex.Match(cmGroups[1].Value, @"width\s*:\s*([\d.]+)%",
                            RegexOptions.IgnoreCase) is { Success: true } cwM
                        && double.TryParse(cwM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var cwPct))
                        cw = tw * cwPct / 100.0;
                    var ctext = CollapseWs(DecodeEntities(
                        SafeRegex.Replace(cmGroups[2].Value, @"<[^>]+>", ""))).Trim();
                    cells.Add((cw, ctext, SafeRegex.IsMatch(cmGroups[2].Value, @"<b\b", RegexOptions.IgnoreCase)));
                }
                if (cells.Count == 0) continue;
                var wrapped = new List<string[]>();
                var deep = 0;
                foreach (var (cw, ctext, cb) in cells)
                {
                    string[] ls;
                    if (ctext.Length == 0) ls = System.Array.Empty<string>();
                    else ls = MeasuredWordWrap(ctext, cw, cb ? ic.face + " Bold" : ic.face, ic.fs);
                    wrapped.Add(ls);
                    deep = Math.Max(deep, ls.Length);
                }
                if (deep == 0) { AddBlank(ic); continue; }
                for (var li = 0; li < deep; li++)
                {
                    var runs = new List<(double, string, bool)>();
                    var cx = 0.0;
                    for (var ci = 0; ci < cells.Count; ci++)
                    {
                        if (li < wrapped[ci].Length && wrapped[ci][li].Length > 0)
                            runs.Add((cx, wrapped[ci][li], cells[ci].Bold));
                        cx += cells[ci].W;
                    }
                    ic.flow.Add((runs, false));
                }
            }
        }
    }
}
