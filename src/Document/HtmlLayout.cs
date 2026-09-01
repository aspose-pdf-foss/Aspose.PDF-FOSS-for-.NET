using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The CSS "normal" line height (pt) for an embedded face at a
    /// point size: the OS/2 win line-box height quantized to whole CSS pixels, recovering
    /// half the leading when the pixel round truncated down (exact across
    /// 6-22pt for Verdana/Calibri). Zero when the metrics are unreadable.</summary>
    private static double HtmlNormalLineHeightPt(byte[]? ttf, double sizePt)
    {
        if (ttf is null || ttf.Length < 12) return 0;
        try
        {
            var tp = new Text.TrueTypeParser(ttf);
            tp.Parse();
            if (tp.UsWinAscent <= 0 || tp.UnitsPerEm <= 0) return 0;
            double upm = tp.UnitsPerEm, winSum = tp.UsWinAscent + tp.UsWinDescent;
            var px = sizePt * 96.0 / 72.0;
            var rawPx = winSum * px / upm;
            var rpx = Math.Round(rawPx, MidpointRounding.AwayFromZero);
            var pitchPx = rpx + Math.Max(0, rawPx - rpx) / 2;
            return pitchPx * 0.75;
        }
        catch { return 0; }
    }

    /// <summary>Render a recognised step-list (a single <c>ul</c> whose items nest heading /
    /// paragraph blocks) with browser-style HTML layout: embedded serif faces,
    /// pixel-quantized CSS line boxes, UA block margins, pair-kerned runs split at inline-bold
    /// edges, and a real bullet marker. All metrics exact to 4 decimals.
    /// Returns false without emitting anything when the serif faces are unavailable, the flow
    /// has already page-broken, or the list does not fit the remaining page space — the caller
    /// then falls back to the legacy flat flow.</summary>
    private static bool RenderHtmlStepList(List<Converters.HtmlToPdfConverter.StepListItem> items,
        FlowLayout flow, double marginLeft, double marginRight, Color? htmlColor)
    {
        if (flow.HasOverflowed) return false;
        byte[]? regTtf, boldTtf;
        try
        {
            regTtf = Text.FontRepository.GetTtfData("Times New Roman");
            boldTtf = Text.FontRepository.GetTtfData("Times New Roman Bold");
        }
        catch { return false; }
        if (regTtf is null || boldTtf is null) return false;

        Text.TrueTypeParser tp;
        Text.GlyphOutlineParser gpReg, gpBold;
        try
        {
            tp = new Text.TrueTypeParser(regTtf);
            tp.Parse();
            gpReg = new Text.GlyphOutlineParser(regTtf);
            gpBold = new Text.GlyphOutlineParser(boldTtf);
        }
        catch { return false; }
        if (tp.UnitsPerEm <= 0 || tp.UsWinAscent <= 0) return false;

        double upm = tp.UnitsPerEm, winAsc = tp.UsWinAscent, winDesc = tp.UsWinDescent;
        var hheaSum = tp.Ascent + System.Math.Abs(tp.Descent) + tp.LineGap;
        const double em = 12.0;                    // HTML default body size (16px)

        // CSS "normal" line box: the hhea line height rounds to whole CSS pixels; the
        // baseline sits winAscent + half the surplus leading below the box top.
        double Pitch(double s) => 0.75 * System.Math.Floor(hheaSum * (s * 96.0 / 72.0) / upm + 0.5);
        double Asc(double s) => winAsc * s / upm + (Pitch(s) - (winAsc + winDesc) * s / upm) / 2;

        // UA defaults: heading sizes and margins in em of the base size. The margin
        // resolves from the exact CSS size while Tf/metrics carry the 3-decimal-truncated
        // value (a float-formatting quirk — visually inert, kept for fidelity).
        (double size, double margin, bool bold) TagStyle(string tag)
        {
            var (factor, marginEm) = tag switch
            {
                "h1" => (2.0, 0.67),
                "h2" => (1.5, 0.75),
                "h3" => (1.17, 0.83),
                _ => (1.0, 0.0),
            };
            var css = factor * em;
            return (System.Math.Floor(css * 1000.0) / 1000.0, marginEm * css, tag is "h1" or "h2" or "h3");
        }

        int Gid(char c, bool bold)
        {
            var gp = bold ? gpBold : gpReg;
            return gp.CMap.TryGetValue(c, out var g) ? g : 0;
        }
        double GlyphW(int gid, bool bold, double s)
        {
            var gp = bold ? gpBold : gpReg;
            var u = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
            return gp.GetAdvanceWidth(gid) * s / u;
        }
        double KernW(int prev, int cur, bool bold, double s)
        {
            var gp = bold ? gpBold : gpReg;
            var u = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
            return gp.GetKernAdjustment(prev, cur) * s / u;
        }

        // Greedy space-break wrap with pair kerning (IsBreakWords=false semantics: a word
        // never splits; an overlong word overflows). The space a line breaks at is dropped;
        // all other spaces stay in-string, so a run boundary keeps its inter-word space.
        List<List<(char c, bool bold)>> Wrap(List<(char c, bool bold)> stream, double size, double maxW)
        {
            var lines = new List<List<(char c, bool bold)>>();
            var line = new List<(char c, bool bold)>();
            var w = 0.0;
            var prevGid = -1;
            var prevBold = false;

            (double w, int endGid, bool endBold) Measure(int from, int to, int pg, bool pb)
            {
                var mw = 0.0;
                for (var k = from; k < to; k++)
                {
                    var (c, bl) = stream[k];
                    var gid = Gid(c, bl);
                    if (pg >= 0 && pb == bl) mw += KernW(pg, gid, bl, size);
                    mw += GlyphW(gid, bl, size);
                    pg = gid;
                    pb = bl;
                }
                return (mw, pg, pb);
            }

            var i = 0;
            while (i < stream.Count)
            {
                // One wrap token: the pending space (if any) plus the following word.
                var j = i + (stream[i].c == ' ' ? 1 : 0);
                while (j < stream.Count && stream[j].c != ' ') j++;
                var (extW, endGid, endBold) = Measure(i, j, prevGid, prevBold);
                if (line.Count > 0 && w + extW > maxW + 1e-9)
                {
                    lines.Add(line);
                    line = new List<(char c, bool bold)>();
                    (w, prevGid, prevBold) = (0, -1, false);
                    var from = stream[i].c == ' ' ? i + 1 : i;
                    if (from < j)
                    {
                        (w, prevGid, prevBold) = Measure(from, j, -1, false);
                        for (var k = from; k < j; k++) line.Add(stream[k]);
                    }
                }
                else
                {
                    for (var k = i; k < j; k++) line.Add(stream[k]);
                    w += extW;
                    prevGid = endGid;
                    prevBold = endBold;
                }
                i = j;
            }
            if (line.Count > 0) lines.Add(line);
            return lines;
        }

        // ---- Lay the whole list out (top-down distances from the flow cursor) ----
        var pageW = flow.CurrentPage.Width;
        var ulMargin = 1.12 * em;                 // ul top/bottom margin
        var liLeft = marginLeft + 30.0;           // ul default padding-left: 40px per level

        var runsOut = new List<(double yDown, double x, string text, bool bold, double size)>();
        var bulletsOut = new List<(double yDown, double x)>();
        var yDown = 0.0;
        var pendingMargin = ulMargin;             // collapses (max) with the next block's own

        foreach (var item in items)
        {
            var textLeft = liLeft + item.PadLeftPt;
            var maxW = pageW - marginRight - textLeft;
            var liFirstLine = true;
            foreach (var block in item.Blocks)
            {
                var (size, margin, boldTag) = TagStyle(block.Tag);
                var stream = new List<(char c, bool bold)>();
                foreach (var r in block.Runs)
                    foreach (var ch in r.Text)
                        stream.Add((ch, boldTag || r.Bold));
                if (stream.Count == 0) continue;

                var lines = Wrap(stream, size, maxW);
                var pitch = Pitch(size);
                yDown += System.Math.Max(pendingMargin, margin);
                var baseline = yDown + Asc(size);
                foreach (var ln in lines)
                {
                    if (liFirstLine)
                    {
                        var bAdv = GlyphW(Gid('•', false), false, em);
                        bulletsOut.Add((baseline, liLeft - 0.375 * em - bAdv));
                        liFirstLine = false;
                    }
                    var x = textLeft;
                    var gi = 0;
                    while (gi < ln.Count)
                    {
                        var runBold = ln[gi].bold;
                        var sb = new System.Text.StringBuilder();
                        var runW = 0.0;
                        var pg = -1;
                        while (gi < ln.Count && ln[gi].bold == runBold)
                        {
                            var gid = Gid(ln[gi].c, runBold);
                            if (pg >= 0) runW += KernW(pg, gid, runBold, size);
                            runW += GlyphW(gid, runBold, size);
                            sb.Append(ln[gi].c);
                            pg = gid;
                            gi++;
                        }
                        runsOut.Add((baseline, x, sb.ToString(), runBold, size));
                        x += runW;
                    }
                    baseline += pitch;
                }
                yDown += lines.Count * pitch;
                pendingMargin = margin;
            }
        }
        var totalH = yDown + ulMargin;
        if (runsOut.Count == 0 || flow.CurrentY - totalH < flow.BottomMargin) return false;

        // ---- Emit: bullet markers + text runs as embedded Type0 serif ----
        var csb = new Content.ContentStreamBuilder();
        if (htmlColor is not null) csb.SetFillColor(htmlColor);
        var fontDict = Table.ResolvePageFontDict(flow.CurrentPage);

        void EmitRun(string text, bool bold, double size, double x, double yAbs)
        {
            var (res, hex) = Text.Type0FontEmbedder.Embed(fontDict,
                bold ? boldTtf : regTtf,
                bold ? "Times New Roman Bold" : "Times New Roman",
                text, stripSpacesInBaseFont: true);
            csb.BeginText();
            csb.SetFont(res, size);
            csb.MoveTextPosition(x, yAbs);
            if (StepKernAdjustments(text, bold ? gpBold : gpReg) is { } adj)
                csb.ShowTextHexKerned(hex, adj);
            else
                csb.ShowTextHex(hex);
            csb.EndText();
        }

        foreach (var (by, bx) in bulletsOut)
            EmitRun("•", false, em, bx, flow.CurrentY - by);
        foreach (var (ry, rx, text, bold, size) in runsOut)
            EmitRun(text, bold, size, rx, flow.CurrentY - ry);

        flow.InjectContentAtCursor(csb.Build());
        flow.AdvanceY(totalH);
        return true;
    }

    private static void ShapeArabicForGenerator(Text.TextFragment tf)
    {
        if (!Text.ArabicTextShaper.ContainsArabic(tf.Text)) return;
        // The fragment keeps its own embedded face when that face carries the Arabic
        // glyphs (a caller's Arial Unicode MS paragraph must not be re-dressed in
        // Arial and lose its ideographs); only a face without them — the Standard-14
        // default above all — routes through the Arabic-capable host Arial.
        var own = tf.TextState.Font;
        var arabicChars = new System.Text.StringBuilder();
        foreach (var c in tf.Text)
            if (Text.ArabicTextShaper.ContainsArabic(c.ToString())) arabicChars.Append(c);
        // A face that cannot show the Arabic is traded for the one the
        // repository's substitution picks for it (a Standard-14 fragment moves
        // to the host serif; a face short of a few letters to the face that
        // covers them), Arial being the last resort.
        Text.Font? font;
        if (own?.SourceFontData?.TtfData is { Length: > 0 } ownTtf
            && Text.FontRepository.CoversText(ownTtf, arabicChars.ToString()))
            font = own;
        else
        {
            var sub = Text.FontRepository.SubstituteForMissingGlyphs(tf.Text, own);
            font = sub?.TtfData is { Length: > 0 }
                ? (Text.Font?)sub
                : Text.FontRepository.TryFindFont("Arial");
        }
        if (font?.SourceFontData?.TtfData is null) return;

        // Collect each segment's display text (shape Arabic segments to visual order; keep
        // non-Arabic segments as-is) and the effective size. Segment-level bidi: a fragment
        // whose first content segment is Arabic lays out right-to-left, so the segments are
        // emitted in reverse order (each segment is treated as a directional unit,
        // which keeps e.g. a leading "." attached to its Latin segment rather than migrating
        // to the adjacent Arabic run as a per-character bidi pass would).
        var size = tf.TextState.FontSize;
        var displays = new List<string>();
        var firstArabic = (bool?)null;
        if (tf.Segments is { Count: > 0 })
        {
            foreach (var s in tf.Segments)
            {
                if (string.IsNullOrEmpty(s.Text)) continue;
                var arabic = Text.ArabicTextShaper.ContainsArabic(s.Text);
                firstArabic ??= arabic;
                if (s.TextState.FontSize > 0 && size <= 0) size = s.TextState.FontSize;
                displays.Add(arabic ? Text.ArabicTextShaper.Shape(s.Text) : s.Text);
            }
        }
        if (displays.Count == 0) displays.Add(Text.ArabicTextShaper.Shape(tf.Text));
        if (firstArabic == true) displays.Reverse();

        tf.TextState.Font = font;
        if (size > 0) tf.TextState.FontSize = size;
        tf.Text = string.Concat(displays);
    }
}
