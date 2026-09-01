using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    private static void AddText(Ctx ctx, XmlElement e, double x, double ytop, double w, double h, string text, bool capOverride = false,
        double? fsOverride = null, bool? boldOverride = null, double[]? colorOverride = null, XmlElement? alignSource = null)
    {
        double fs = fsOverride ?? FontSize(e);
        var (mt, mb, ml, mr) = Margins(e);
        bool bold = boldOverride ?? FontBold(e);
        var color = colorOverride ?? FontColor(e);
        double avail = Math.Max(4, w - ml - mr);
        // Plain-text line pitch: exactly the font size
        // (single-spaced), or the element's explicit <para lineHeight> when declared.
        double lineH = FirstChild(e, "para") is { } pe && LenN(pe.GetAttribute("lineHeight")) is { } plh && plh > 0
            ? plh
            : fs;
        double topY = ytop + mt;
        int li = 0;
        // The element's <para hAlign> shifts each wrapped line within the available
        // width (Designer right-aligned labels sit flush against their input box).
        // A caption aligns by its OWN <para>, not the field's — the field para
        // describes the value region (a centred value must not centre its label).
        var hAlign = FirstChild(alignSource ?? e, "para")?.GetAttribute("hAlign") ?? "";
        // Captions measure against their reserve with a slack: the Helvetica width
        // model overestimates narrow Designer faces (Myriad, Arial Narrow), and a
        // caption that fits its reserve in a viewer must not wrap here.
        if (alignSource is not null) avail *= 1.10;
        // Unicode line/paragraph separators are hard line breaks (a double U+2029 = a blank line).
        text = text.Replace((char)0x2028, (char)0x0A).Replace((char)0x2029, (char)0x0A).Replace((char)0x0D, (char)0x0A);
        var wide = FontWideFactor(e);
        // A resolvable non-default face (Verdana, Tahoma …) measures and paints
        // with its real advances; the Tz wide-factor stays 1 on that path.
        var family = ResolvedFamily(e, bold);
        double LineWidth(string l) => family is null
            ? TextWidth(l, fs, bold) * wide
            : FamTextWidth(l, fs, family, bold);
        foreach (var para in text.Split('\n'))
            foreach (var line in family is null
                ? WrapLine(para, avail / wide, fs, bold)
                : WrapLineMeasured(para, avail, l => FamTextWidth(l, fs, family, bold)))
            {
                double baseline = ctx.PageH - (topY + fs + li * lineH);
                if (line.Length > 0)
                {
                    double lx = x + ml;
                    if (hAlign is "right" or "center")
                    {
                        double slack = avail - LineWidth(line);
                        if (slack > 0) lx += hAlign == "right" ? slack : slack / 2;
                    }
                    ctx.Items.Add(new Item { Kind = "text", X = lx, Y = baseline, W = w, Text = line, FontSize = fs, Bold = bold, Color = color, HScale = family is null ? wide : 1.0, Family = family });
                }
                li++;
            }
    }

    /// <summary>Greedy word-wrap with an arbitrary line-measure callback (real
    /// font advances); same break rule as <see cref="WrapLine"/>.</summary>
    private static IEnumerable<string> WrapLineMeasured(string para, double maxWidth, Func<string, double> measure)
    {
        para = para.Trim();
        if (para.Length == 0) { yield return ""; yield break; }
        if (measure(para) <= maxWidth) { yield return para; yield break; }
        var words = para.Split(' ');
        var cur = new StringBuilder();
        foreach (var wd in words)
        {
            var trial = cur.Length == 0 ? wd : cur.ToString() + " " + wd;
            if (measure(trial) > maxWidth && cur.Length > 0)
            {
                yield return cur.ToString();
                cur.Clear(); cur.Append(wd);
            }
            else { cur.Clear(); cur.Append(trial); }
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    private sealed class RtRun
    {
        public string Text = "";
        public bool Bold, Italic;
        public double Size = 10;
        public bool Serif;
        public double LetterSpacing;   // extra advance per glyph (pt)
        public string? Family;         // resolvable non-default template face
    }

    private sealed class RtPara
    {
        public List<RtRun> Runs = new();
        public string Align = "left";
        public double SpaceAfter;
    }

    /// <summary>Render an exData XHTML body: per-paragraph alignment and spacing, per-run
    /// font family / size / weight / style, greedy mixed-run word wrap, and justified
    /// line filling (all lines but a paragraph's last).</summary>
    private static void AddRichText(Ctx ctx, XmlElement e, double x, double ytop, double w, double h, XmlElement exData)
    {
        var (mt, _, ml, mr) = Margins(e);
        double avail = Math.Max(4, w - ml - mr);
        // Rich text without an explicit font size uses the XFA default of 10pt (the
        // renderer-wide 8pt default is calibrated for plain caption/value text).
        double baseFs = FontSizeN(e) ?? 10;
        bool baseBold = FontBold(e);
        var color = FontColor(e);
        double cy = ytop + mt;

        // The draw's <para hAlign> is the default paragraph alignment; its <font typeface>
        // picks the base family — a typeface outside the well-known sans set renders as
        // Times (the standard substitution for unavailable Designer families).
        var defaultAlign = FirstChild(e, "para")?.GetAttribute("hAlign") is { Length: > 0 } ha ? ha : "left";
        var typeface = FirstChild(e, "font")?.GetAttribute("typeface") ?? "";
        var baseSerif = typeface.Length > 0 && !IsSansTypeface(typeface);

        // Measure pass: wrap every paragraph so a vAlign=middle/bottom block can be
        // positioned before emission.
        var (measured, contentH) = MeasureRichParas(ctx, e, avail, exData, baseFs, baseBold, baseSerif, defaultAlign);

        var vAlign = FirstChild(e, "para")?.GetAttribute("vAlign") ?? "";
        var (mtIgn, mb, _, _) = Margins(e);
        double inner = h - mt - mb;
        if (vAlign == "middle" && contentH < inner) cy += (inner - contentH) / 2;
        else if (vAlign == "bottom" && contentH < inner) cy += inner - contentH;

        foreach (var (para, lines, lineH) in measured)
        {
            if (lines.Count == 0)
            {
                cy += lineH; // empty paragraph = a blank line
                continue;
            }
            for (int li = 0; li < lines.Count; li++)
            {
                EmitRunLine(ctx, lines[li], x + ml, cy, avail, para.Align, li == lines.Count - 1, color);
                cy += lineH;
            }
            cy += para.SpaceAfter;
        }
    }

    /// <summary>Content height of a draw's exData rich text at the given text width —
    /// the flow-height twin of <see cref="AddRichText"/>'s measure pass.</summary>
    private static double RichContentHeight(Ctx ctx, XmlElement e, double avail, XmlElement exData)
    {
        double baseFs = FontSizeN(e) ?? 10;
        var defaultAlign = FirstChild(e, "para")?.GetAttribute("hAlign") is { Length: > 0 } ha ? ha : "left";
        var typeface = FirstChild(e, "font")?.GetAttribute("typeface") ?? "";
        var (_, contentH) = MeasureRichParas(ctx, e, avail, exData, baseFs, FontBold(e),
            typeface.Length > 0 && !IsSansTypeface(typeface), defaultAlign);
        return contentH;
    }

    /// <summary>Flatten a paragraph's inline content into styled runs (spans override the
    /// paragraph style; xfa-spacerun spans are spaces; an xfa:embed span injects the
    /// referenced element's value as an inline run).</summary>
    private static void CollectRuns(Ctx ctx, XmlNode node, RtRun style, List<RtRun> into)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlText or XmlWhitespace or XmlSignificantWhitespace or XmlCDataSection)
            {
                var text = child.Value ?? "";
                if (text.Length > 0)
                    into.Add(new RtRun { Text = text, Bold = style.Bold, Italic = style.Italic, Size = style.Size, Serif = style.Serif, LetterSpacing = style.LetterSpacing, Family = style.Family });
            }
            else if (child is XmlElement el)
            {
                var sub = new RtRun { Bold = style.Bold, Italic = style.Italic, Size = style.Size, Serif = style.Serif, LetterSpacing = style.LetterSpacing, Family = style.Family };
                if (el.LocalName is "b" or "strong") sub.Bold = true;
                if (el.LocalName is "i" or "em") sub.Italic = true;
                var st = ParseStyle(el.GetAttribute("style"));
                if (st.bold is not null) sub.Bold = st.bold.Value;
                if (st.italic is not null) sub.Italic = st.italic.Value;
                if (st.size is not null) sub.Size = st.size.Value;
                if (st.serif is not null) sub.Serif = st.serif.Value;
                if (st.lsPt is not null) sub.LetterSpacing = st.lsPt.Value;
                else if (st.lsEm is not null) sub.LetterSpacing = st.lsEm.Value * sub.Size;
                var embed = el.Attributes.OfType<XmlAttribute>()
                    .FirstOrDefault(a => a.LocalName == "embed")?.Value ?? "";
                if (embed.StartsWith("#", StringComparison.Ordinal))
                {
                    into.Add(new RtRun { Text = ResolveEmbedText(ctx, embed.TrimStart('#')), Bold = sub.Bold, Italic = sub.Italic, Size = sub.Size, Serif = sub.Serif, LetterSpacing = sub.LetterSpacing, Family = sub.Family });
                    continue;
                }
                if (el.GetAttribute("style").Contains("xfa-spacerun", StringComparison.Ordinal))
                {
                    // A spacerun's WIDTH matters: leading runs indent TOC-style rows, so
                    // keep the actual character count (NBSPs render as ordinary spaces).
                    var spaces = el.InnerText.Replace('\u00A0', ' ');
                    if (spaces.Trim().Length > 0 || spaces.Length == 0) spaces = " ";
                    into.Add(new RtRun { Text = spaces, Bold = sub.Bold, Italic = sub.Italic, Size = sub.Size, Serif = sub.Serif, LetterSpacing = sub.LetterSpacing, Family = sub.Family });
                    continue;
                }
                if (el.LocalName == "br")
                {
                    into.Add(new RtRun { Text = "\n", Bold = sub.Bold, Italic = sub.Italic, Size = sub.Size, Serif = sub.Serif, Family = sub.Family });
                    continue;
                }
                CollectRuns(ctx, el, sub, into);
            }
        }
    }

    private sealed class RtWord
    {
        public string Text = "";
        public RtRun Style = null!;
        public double Width;
    }

    /// <summary>Greedy word wrap over style-mixed runs; returns lines of styled words.</summary>
    private static List<List<RtWord>> WrapRuns(List<RtRun> runs, double maxWidth)
    {
        var words = new List<RtWord>();
        bool atLineStart = true;
        double pendingIndent = 0;
        foreach (var run in runs)
        {
            var normalized = run.Text.Replace(' ', ' ');
            var pieces = normalized.Split('\n');
            for (int pi = 0; pi < pieces.Length; pi++)
            {
                var piece = pieces[pi];
                int pos = 0;
                while (pos < piece.Length)
                {
                    if (piece[pos] == ' ')
                    {
                        // Leading whitespace at a HARD line start is kept as an indent
                        // marker ("\t" word, width only) — Designer TOC rows indent
                        // sub-entries with leading space runs.
                        if (atLineStart) pendingIndent += SpaceWidth(run);
                        pos++;
                        continue;
                    }
                    int end = piece.IndexOf(' ', pos);
                    if (end < 0) end = piece.Length;
                    var word = piece[pos..end];
                    if (atLineStart && pendingIndent > 0)
                    {
                        words.Add(new RtWord { Text = "\t", Style = run, Width = pendingIndent });
                        pendingIndent = 0;
                    }
                    atLineStart = false;
                    words.Add(new RtWord { Text = word, Style = run, Width = TextWidthF(word, run) });
                    pos = end;
                }
                if (pi < pieces.Length - 1)
                {
                    words.Add(new RtWord { Text = "\n", Style = run });
                    atLineStart = true;
                    pendingIndent = 0;
                }
            }
        }
        var lines = new List<List<RtWord>>();
        var cur = new List<RtWord>();
        double curW = 0;
        foreach (var word in words)
        {
            if (word.Text == "\n")
            {
                lines.Add(cur); cur = new List<RtWord>(); curW = 0;
                continue;
            }
            double space = cur.Count == 0 ? 0 : SpaceWidth(word.Style);
            if (curW + space + word.Width > maxWidth && cur.Count > 0)
            {
                lines.Add(cur); cur = new List<RtWord>(); curW = 0;
                space = 0;
            }
            cur.Add(word);
            curW += space + word.Width;
        }
        if (cur.Count > 0) lines.Add(cur);
        return lines;
    }

    private static double SpaceWidth(RtRun r) => TextWidthF(" ", r);

    /// <summary>Emit one wrapped line: words merged into same-style segments; justified
    /// lines distribute the slack across word gaps (never the paragraph's last line).</summary>
    private static void EmitRunLine(Ctx ctx, List<RtWord> line, double x, double ytop, double avail,
        string align, bool lastLine, double[] color)
    {
        if (line.Count == 0) return;
        double indent = 0;
        if (line[0].Text == "\t")
        {
            indent = line[0].Width;
            line = line.Skip(1).ToList();
            if (line.Count == 0) return;
        }
        double natural = line.Sum(w => w.Width) + line.Skip(1).Sum(w => SpaceWidth(w.Style));
        double gap = 0, startX = x + indent;
        if (align == "justify" && !lastLine && line.Count > 1 && natural + indent < avail)
            gap = (avail - indent - natural) / (line.Count - 1);
        else if (align == "center") startX = x + Math.Max(0, (avail - natural) / 2);
        else if (align == "right") startX = x + Math.Max(0, avail - natural);

        double fs = line.Max(w => w.Style.Size);
        double baseline = ctx.PageH - (ytop + fs);
        double cx = startX;
        int i = 0;
        while (i < line.Count)
        {
            // Merge adjacent words with the identical style into one text item when no
            // justification slack is being distributed.
            var st = line[i].Style;
            if (gap == 0)
            {
                var seg = new StringBuilder(line[i].Text);
                double segW = line[i].Width;
                int j = i + 1;
                while (j < line.Count && SameStyle(line[j].Style, st))
                {
                    seg.Append(' ').Append(line[j].Text);
                    segW += SpaceWidth(st) + line[j].Width;
                    j++;
                }
                ctx.Items.Add(new Item { Kind = "text", X = cx, Y = baseline, W = segW, Text = seg.ToString(), FontSize = st.Size, Bold = st.Bold, Italic = st.Italic, Serif = st.Serif, CharSpacing = st.LetterSpacing, Rich = true, Family = st.Family, Color = color });
                cx += segW + (j < line.Count ? SpaceWidth(line[j].Style) : 0);
                i = j;
            }
            else
            {
                ctx.Items.Add(new Item { Kind = "text", X = cx, Y = baseline, W = line[i].Width, Text = line[i].Text, FontSize = st.Size, Bold = st.Bold, Italic = st.Italic, Serif = st.Serif, CharSpacing = st.LetterSpacing, Rich = true, Family = st.Family, Color = color });
                cx += line[i].Width + SpaceWidth(st) + gap;
                i++;
            }
        }
    }

    private static bool SameStyle(RtRun a, RtRun b)
        => a.Bold == b.Bold && a.Italic == b.Italic && a.Serif == b.Serif && a.Family == b.Family
           && Math.Abs(a.Size - b.Size) < 0.01 && Math.Abs(a.LetterSpacing - b.LetterSpacing) < 0.001;

    private static bool IsSansTypeface(string typeface)
        => typeface.Contains("Arial", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Helvetica", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Verdana", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Tahoma", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Segoe", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Calibri", StringComparison.OrdinalIgnoreCase);

    /// <summary>Greedy word-wrap of a line to <paramref name="maxWidth"/> pt using an approximate
    /// Helvetica advance-width metric.</summary>
    private static IEnumerable<string> WrapLine(string para, double maxWidth, double fs, bool bold)
    {
        para = para.Trim();
        if (para.Length == 0) { yield return ""; yield break; }
        if (TextWidth(para, fs, bold) <= maxWidth) { yield return para; yield break; }
        var words = para.Split(' ');
        var cur = new StringBuilder();
        foreach (var wd in words)
        {
            var trial = cur.Length == 0 ? wd : cur.ToString() + " " + wd;
            if (TextWidth(trial, fs, bold) > maxWidth && cur.Length > 0)
            {
                yield return cur.ToString();
                cur.Clear(); cur.Append(wd);
            }
            else { cur.Clear(); cur.Append(trial); }
        }
        if (cur.Length > 0) yield return cur.ToString();
    }
}
