using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>The Word-mail cell dialect's arms: a cell's border sides and padding, and a broken picture's alt text laid inside its box.</summary>
internal static partial class HtmlToPdfConverter
{
    /// <summary>Alt text drawn in grey, whatever the run's own colour (the reference's broken-picture text).</summary>
    private static readonly Color WordMailAltTextColor = Color.Gray;

    /// <summary>Slack when counting the lines a picture box holds.</summary>
    private const double AltBoxLineEpsilon = 0.01;

    /// <summary>Word mail: a broken picture's alt text is laid out INSIDE the picture's box - wrapped at the box
    /// width, clipped to the lines the box height holds - in the run's size and face, in grey. A picture on a
    /// text line keeps the line and shows only its first fitted line (a 12 pt icon shows two letters).</summary>
    private static void AppendWordMailAltText(CellImgState ci, string alt)
    {
        var fs = ci.ps.curFontPt > 0 ? ci.ps.curFontPt : ci.cellFontSize;
        var fam = ci.ps.curFamily;
        // The lines must fit the cell's TEXT area: the table's default cell pad comes off the box width.
        var boxInset = ci.table.DefaultCellPadding is { } dcp ? dcp.Left + dcp.Right : 0;
        var boxW = ci.ciw > 0 ? Math.Max(1, ci.ciw * PxToPt - boxInset) : double.MaxValue;
        // The box holds its lines on the face's Windows line (ascent + descent, no gap): nine Verdana-9
        // lines of 10.9 fill the 100.5 pt picture, as the reference shows.
        var lineH = fam is { } af && WinMetricsFor(af) is { } am ? fs * am.sum : NormalLineHeightPt(fs);
        var maxLines = ci.cih > 0 ? Math.Max(1, (int)Math.Floor(ci.cih * PxToPt / lineH + AltBoxLineEpsilon)) : int.MaxValue;
        // Measured on the face's own advances: the generator draws them, and a Verdana line fitted on
        // narrower estimates re-wraps in the cell.
        double Width(string t) => fam is { } mf && WinMetricsFor(mf) is not null
            ? MeasureFaceText(mf, t, fs)
            : MeasureLine(ci.ps, ci.options, ci.cellFontSize, ci.dwFormCells, ci.fullWidthCjkMin, ci.widenProbe, t, false, fs, fam);
        var lines = WrapAltTextInBox(alt, boxW, maxLines, Width);
        if (lines.Count == 0) return;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1") Console.Error.WriteLine($"[alt] boxW={boxW:0.##} pad={ci.pad} padSide={ci.padSide} cellCssPad={ci.ps.cellCssPadPt} cellW={ci.ps.cellWidthPt:0.##} lines={lines.Count} first='{lines[0]}' w0={Width(lines[0]):0.##} maxLines={maxLines} lineH={lineH:0.##}");
        void BindLine()
        {
            if (!ci.ps.lineStyleSet) { ci.ps.lineFontPt = ci.ps.curFontPt; ci.ps.lineFamily = fam; ci.ps.lineStyleSet = true; }
            if (ci.ps.boldDepth == 0) ci.ps.lineAllBold = false;
            ci.ps.lineHadText = true;
        }
        if (!IsAllWhitespace(ci.ps.line))
        {
            ci.ps.line.Append(lines[0]);
            BindLine();
            return;
        }
        var savedPct = ci.ps.curLineHeightPct;
        ci.ps.curLineHeightPct = lineH / fs * WholeWidthPercent;
        foreach (var l in lines)
        {
            ci.ps.line.Append(l);
            BindLine();
            ci.ps.lineColor = WordMailAltTextColor;
            PushLine(ci.ps, ci.redlineCells, ci.dwFormCells, ci.widenProbe);
        }
        ci.ps.curLineHeightPct = savedPct;
    }

    /// <summary>Greedy word wrap into a box: words fill each line up to the width; a word wider than the box
    /// splits before its path separators, then by characters; at most maxLines lines come back.</summary>
    private static List<string> WrapAltTextInBox(string text, double boxW, int maxLines, Func<string, double> width)
    {
        // Pieces: (text, glued to the previous piece - a split inside one word takes no space back).
        var pieces = new List<(string Text, bool Glued)>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (width(word) <= boxW) { pieces.Add((word, false)); continue; }
            var first = true;
            foreach (var part in Regex.Split(word, @"(?=\\)"))
            {
                if (part.Length == 0) continue;
                if (width(part) <= boxW) { pieces.Add((part, !first)); first = false; continue; }
                var run = new StringBuilder();
                foreach (var ch in part)
                {
                    if (run.Length > 0 && width(run.ToString() + ch) > boxW) { pieces.Add((run.ToString(), !first)); first = false; run.Clear(); }
                    run.Append(ch);
                }
                if (run.Length > 0) { pieces.Add((run.ToString(), !first)); first = false; }
            }
        }
        var lines = new List<string>();
        var cur = "";
        foreach (var (piece, glued) in pieces)
        {
            var joined = cur.Length == 0 ? piece : glued ? cur + piece : cur + " " + piece;
            if (cur.Length > 0 && width(joined) > boxW)
            {
                lines.Add(cur);
                if (lines.Count >= maxLines) return lines;
                cur = piece;
            }
            else cur = joined;
        }
        if (cur.Length > 0 && lines.Count < maxLines) lines.Add(cur);
        return lines;
    }

    /// <summary>An SVG of the given pixel box that paints nothing — a broken picture's reserved space.</summary>
    /// <summary>The browser's broken-image placeholder: a white square in a 1 pt inset frame.</summary>
    private static string BrokenImageSvg(double s)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var d = s.ToString(inv);
        return "<svg xmlns='http://www.w3.org/2000/svg' width='" + d + "' height='" + d + "'>"
            + "<rect x='0' y='0' width='" + d + "' height='" + d + "' fill='#FFFFFF'/>"
            + "<path d='M0.5 " + d + " L0.5 0.5 L" + d + " 0.5' fill='none' stroke='#555555' stroke-width='1'/>"
            + "<path d='M" + (s - 0.5).ToString(inv) + " 0 L" + (s - 0.5).ToString(inv) + " " + (s - 0.5).ToString(inv) + " L0 " + (s - 0.5).ToString(inv) + "' fill='none' stroke='#AAAAAA' stroke-width='1'/></svg>";
    }

    /// <summary>The placeholder's side, 32 pt (measured: the frame 96.5..129.5 around a 32 pt square).</summary>
    private const double UaBrokenImagePt = 32.0;

    private static string BlankBoxSvg(double w, double h)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "<svg xmlns='http://www.w3.org/2000/svg' width='" + w.ToString(inv) + "' height='" + h.ToString(inv)
            + "'><rect x='0' y='0' width='" + w.ToString(inv) + "' height='" + h.ToString(inv) + "' fill='#FFFFFF'/></svg>";
    }

    /// <summary>Word mail: the cell's inline `padding` shorthand is its box padding - Word's 5.4 pt side pads
    /// ride outside the declared (content-box) width, and the vertical pair bands the rows.</summary>
    private static void ReadWordMailCellPadding(CellOpenState co, string st)
    {
        if (co.ps.cellCssPadPt > 0
            || Regex.Match(st, @"(?<![-\w])padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is not { Success: true } pm) return;
        var (pT, pR, pB, pL) = ChainPadPt(pm.Groups[1].Value.Trim(), co.ps.cellClassPt > 0 ? co.ps.cellClassPt : co.cellFontSize);
        if (pL + pR > 0) { co.ps.cellCssPadPt = pL + pR; co.ps.cellPadLeftPt = pL; }
        co.ps.cellChainPadTopPt = pT; co.ps.cellChainPadBotPt = pB;
    }

    /// <summary>Word mail: a cell's `border` shorthand boxes it, then each `border-&lt;side>` longhand adds that side or (`none`) removes it — the voucher frame and its header rules.</summary>
    private static void ApplyWordMailCellBorders(CellOpenState co, string st)
    {
        BorderSide sides = 0; double w = 0; Color? col = null;
        if (TryParseBorderShorthand(st, "border") is (var bw, var bc)) { sides = BorderSide.Box; w = bw; col = bc; }
        foreach (var (prop, side) in new[]
        {
            ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
            ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
        })
        {
            if (Regex.IsMatch(st, @"(?<![-\w])" + prop + @"\s*:\s*none", RegexOptions.IgnoreCase)) { sides &= ~side; continue; }
            if (TryParseBorderShorthand(st, prop) is not (var sw, var sc)) continue;
            sides |= side;
            if (sw > w) w = sw;
            col ??= sc;
        }
        if (sides == 0) return;
        co.ps.cell!.Border = new BorderInfo(sides, w <= 0 ? PxToPt : w, col ?? Color.Black);
    }
}
