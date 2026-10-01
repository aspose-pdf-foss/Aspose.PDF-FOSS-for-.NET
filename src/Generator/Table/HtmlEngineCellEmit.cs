using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// Html engine cell: 
    private static void FlushLine(HtmlEngineCellState hc, bool force)
    {
        // Trim the trailing spaces of the last run (a wrapped line never ends
        // in a visible space; fragment widths exclude it).
        while (hc.curRuns.Count > 0)
        {
            var last = hc.curRuns[^1];
            var trimmed = last.Text.TrimEnd(' ');
            if (trimmed.Length == 0) { hc.curRuns.RemoveAt(hc.curRuns.Count - 1); continue; }
            last.Text = trimmed;
            break;
        }
        if (hc.curRuns.Count == 0 && !force) { hc.curX = 0; return; }
        double maxSize = 0;
        var sb = new System.Text.StringBuilder();
        foreach (var r in hc.curRuns)
        {
            if (r.Size > maxSize) maxSize = r.Size;
            sb.Append(r.Text);
        }
        // Consecutive runs of one anchor become a single Link rectangle over exactly
        // the anchor's characters, measured with the metrics that laid the line out.
        List<(double XOff, double W, Hyperlink Link)>? linkRuns = null;
        for (var ri = 0; ri < hc.curRuns.Count; ri++)
        {
            var url = hc.curRuns[ri].Url;
            if (string.IsNullOrEmpty(url)) continue;
            var x0 = hc.curRuns[ri].X;
            var end = hc.curRuns[ri].X + MeasureWidthKerned(hc.curRuns[ri].Text, hc.curRuns[ri].Size, EngineRunTtf(hc.curRuns[ri]));
            while (ri + 1 < hc.curRuns.Count && hc.curRuns[ri + 1].Url == url)
            {
                ri++;
                end = hc.curRuns[ri].X + MeasureWidthKerned(hc.curRuns[ri].Text, hc.curRuns[ri].Size, EngineRunTtf(hc.curRuns[ri]));
            }
            (linkRuns ??= new()).Add((x0, end - x0, new WebHyperlink(url)));
        }
        // The line's CSS box is the larger of the root strut and the box of its
        // own biggest run (a 33pt span opens a 33pt line box; a 10pt small still
        // sits in the root 12pt strut) - probed: a mixed 12/33 line drops its
        // baseline winAscent+halfLead of the 33px box, and wrapped 33 lines
        // pitch at the pixel-rounded 33 box.
        // The line's strut is its block's own box (a p sized 10 pt pitches at 11.25 inside a
        // 12 pt cell; probed) or the cell's root box; each run's face box then raises it
        // (a Georgia 18 span on a Times block pitches at Georgia's 27 px, not Times' 28).
        var (ownBox, ownDrop) = EngineBlockStrut(hc);
        foreach (var r in hc.curRuns)
        {
            var (rBox, rDrop) = EngineRunLineBox(r);
            ownBox = Math.Max(ownBox, rBox);
            ownDrop = Math.Max(ownDrop, rDrop);
        }
        // A block's first line opens under the block's UA margin (a heading's).
        var blockMargin = hc.pendingBlockMargin;
        hc.pendingBlockMargin = 0;
        hc.lines.Add(new CellLine
        {
            Text = sb.ToString(),
            FontSize = maxSize > 0 ? maxSize : hc.baseSize,
            Runs = hc.curRuns.Count > 0 ? new List<HtmlRun>(hc.curRuns) : null,
            LinkRuns = linkRuns,
            KernTj = true,
            HtmlEngine = true,
            BoxH = ownBox + blockMargin,
            BaseOff = ownDrop + blockMargin,
            InListItem = hc.lists.Count > 0 || hc.inListItem,
            InBlock = hc.blockDepth > 0,
        });
        hc.curRuns.Clear();
        hc.curX = hc.lineIndent;
    }

    // The marker of the item whose first line is about to take ink: laid RIGHT
    // against the marker gap, so every ordinal of a list ends on the same x.
    private static void EmitPendingMarker(HtmlEngineCellState hc)
    {
        if (hc.pendingMarker is null) return;
        var marker = hc.pendingMarker;
        hc.pendingMarker = null;
        var gap = UaListMarkerGapEm * hc.baseSize;
        var w = MeasureWidthKerned(marker, hc.baseSize, _serifTtf!);
        hc.curRuns.Add(new HtmlRun { Text = marker, X = hc.lineIndent - gap - w, Size = hc.baseSize });
        // The gap itself is written as a space run — it carries no
        // ink but keeps the extracted text readable.
        hc.curRuns.Add(new HtmlRun { Text = " ", X = hc.lineIndent - gap, Size = hc.baseSize });
        hc.anyText = true;
    }

    private static void EmitText(HtmlEngineCellState hc, string raw)
    {
        // HTML whitespace collapse: any run of whitespace is one space. The text
        // between tags carries no markup, so decode entities WITHOUT trimming — the
        // trailing space before an inline element (e.g. "Min. Fee " before <small>)
        // is a real inter-word space that must render; leading spaces at a line
        // start are dropped separately below.
        var text = DecodeHtmlEntities(Regex.Replace(raw, @"\s+", " "));
        if (text.Length == 0) return;
        if (text.Trim().Length > 0) CommitFloats(hc);
        if (text == " ")
        {
            // Inter-tag whitespace: a space only mid-line, never at a line start.
            if (hc.curRuns.Count == 0 && hc.curX <= hc.lineIndent) return;
        }
        var size = ResolveEngineSize(hc);
        Color? spanColor = null;
        var spanUnderline = false;
        foreach (var sp in hc.spans)
        {
            if (sp.Size > 0) size = sp.Size;
            if (sp.Color is not null) spanColor = sp.Color;
            if (sp.Underline) spanUnderline = true;
        }
        // A linked run draws blue and underlined (probed: the "here" of a cell's <a href>
        // sets in #0000ff with the UA 0.1 em rule under it).
        if (hc.anchors.Count > 0 && hc.anchors.Peek().Length > 0)
        {
            spanColor ??= EngineLinkColor;
            spanUnderline = true;
        }
        var face = ResolveEngineFace(hc, out var runBold, out _);
        var ttf = face?.Ttf ?? (runBold ? _serifBoldTtf! : _serifTtf!);
        HtmlRun? run = null;   // runs split at tag boundaries: one piece = one run chain
        foreach (var token in SplitEngineTokens(text))
        {
            if (hc.curRuns.Count == 0 && hc.curX <= hc.lineIndent && token.TrimStart(' ').Length == 0) continue;
            var tokenText = hc.curRuns.Count == 0 && hc.curX <= hc.lineIndent && run is null
                ? token.TrimStart(' ') : token;
            if (tokenText.Length == 0) continue;
            var w = MeasureWidthKerned(tokenText, size, ttf);
            var visible = tokenText.TrimEnd(' ');
            var visibleW = visible.Length == tokenText.Length ? w : MeasureWidthKerned(visible, size, ttf);
            if (hc.availWidth > 0 && hc.curX + visibleW > hc.availWidth + 1e-6
                && (hc.curRuns.Count > 0 || hc.curX > hc.lineIndent))
            {
                FlushLine(hc, force: false);
                run = null;
                tokenText = tokenText.TrimStart(' ');
                if (tokenText.Length == 0) continue;
                w = MeasureWidthKerned(tokenText, size, ttf);
            }
            // IsBreakWords: a word still too wide for an EMPTY line breaks inside
            // itself, as many characters as fit per line, instead of overflowing
            // the column (which is what the flag off does).
            while (hc.breakWords && hc.availWidth > 0 && hc.curX <= hc.lineIndent + 1e-6
                   && MeasureWidthKerned(tokenText, size, ttf) > hc.availWidth + 1e-6)
            {
                var fit = 0;
                while (fit + 1 < tokenText.Length
                       && MeasureWidthKerned(tokenText[..(fit + 1)], size, ttf) <= hc.availWidth + 1e-6)
                    fit++;
                if (fit <= 0) break;
                hc.curRuns.Add(new HtmlRun
                {
                    Text = tokenText[..fit], X = 0, Size = size, Bold = runBold, Face = face,
                    Url = hc.anchors.Count > 0 ? hc.anchors.Peek() : null,
                    Color = spanColor, Underline = spanUnderline,
                });
                hc.anyText = true;
                FlushLine(hc, force: false);
                run = null;
                tokenText = tokenText[fit..];
                if (tokenText.Length == 0) break;
                w = MeasureWidthKerned(tokenText, size, ttf);
            }
            if (tokenText.Length == 0) continue;
            EmitPendingMarker(hc);
            if (run is null)
            {
                run = new HtmlRun
                {
                    Text = tokenText, X = hc.curX, Size = size, Bold = runBold, Face = face,
                    Url = hc.anchors.Count > 0 ? hc.anchors.Peek() : null,
                    Color = spanColor, Underline = spanUnderline,
                };
                hc.curRuns.Add(run);
            }
            else run.Text += tokenText;
            hc.curX += w;
            hc.anyText = true;
        }
    }
}
