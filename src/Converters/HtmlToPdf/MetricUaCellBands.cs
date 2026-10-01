using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // A UA cell's content model: bands in source order, each with the typography and block style
    // its own markup states, inherited through the blocks it stands in; and inside a band or a
    // plain cell, RUNS - every stretch of ink in the size, face, weight, style and inset the
    // inline markup around it states.

    /// <summary>The typography one run of a UA cell's ink draws in.</summary>
    private sealed class MetricRunStyle
    {
        public double? Fs;
        public string? Face;
        public bool Bold;
        public bool Italic;
        /// <summary>An inline box's own left padding: the pen advances by it before the run.</summary>
        public double PadLeft;
        public bool SameAs(MetricRunStyle o)
            => Fs == o.Fs && Face == o.Face && Bold == o.Bold && Italic == o.Italic && PadLeft == o.PadLeft;
    }

    /// <summary>The marks a UA cell's text carries while it is parsed: one private-use character per
    /// run style change, indexing the cell's style list. They leave the text at the cell (or band)
    /// close, as run positions.</summary>
    private const int MetricRunMarkBase = 0xE000;
    private const int MetricRunMarkLimit = 0xE800;
    private static bool IsRunMark(char ch) => ch >= (char)MetricRunMarkBase && ch < (char)MetricRunMarkLimit;
    private static char NbspChar => (char)0xA0;

    /// <summary>The slant a face takes when its italic is asked for but no italic file exists: the
    /// engine shears the upright glyphs by 0.3 (probed on the safety data sheet: `&lt;i>` Arial Black
    /// draws `1 0 0.3 -1 x y Tm`); a face with a real italic, or the Standard-14 serif, draws its
    /// own italic unsheared.</summary>
    private const double SyntheticItalicShear = 0.3;


    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _italicFaceExists =
        new(StringComparer.OrdinalIgnoreCase);

    private static double SyntheticItalicShearFor(string? face, bool bold, bool italic)
    {
        if (!italic || face is null) return 0;
        var upright = face + (bold ? " Bold" : "");
        var name = upright + " Italic";
        var exists = _italicFaceExists.GetOrAdd(name, n =>
        {
            // (the repository resolves a missing style to the family's nearest file: an italic
            // that comes back as the upright's own bytes is no italic)
            try
            {
                var it = Text.FontRepository.GetTtfData(n) ?? Text.FontRepository.GetTtfData(n.Replace(" ", ""));
                if (it is null) return false;
                var up = Text.FontRepository.GetTtfData(upright) ?? Text.FontRepository.GetTtfData(upright.Replace(" ", ""));
                return up is null || !SameFontBytes(it, up);
            }
            catch { return false; }
        });
        return exists ? 0 : SyntheticItalicShear;
    }

    /// <summary>Two font programs are the same file when their lengths agree and every sampled
    /// byte agrees (a stride keeps the check cheap on a multi-megabyte face).</summary>
    private const int FontBytesSampleStride = 997;

    private static bool SameFontBytes(byte[] a, byte[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i += FontBytesSampleStride) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>The extent a sheared run takes past its advance: the slant carries its tallest ink
    /// right by the shear of the ascent (probed on the safety data sheet: the 36 pt title's page ends
    /// 10.67 past its advance, and the title centres on that extent).</summary>
    private static double ShearOverhangPt(double shear, (double asc, double sum) fm, double fs)
        => shear > 0 ? shear * fm.asc * fs : 0;

    /// <summary>The style the next ink of a UA cell draws in, and a mark for it in the text when it
    /// differs from the run before (the FIRST run is marked too, so a cell whose ink is wholly in one
    /// style still knows it).</summary>
    private static void MarkMetricRun(MetricTableState mt)
    {
        var mps = mt.mps;
        if (mps.cell is null) return;
        var cur = new MetricRunStyle
        {
            Fs = mps.cell.FontSize, Face = mps.cell.Face, Bold = mps.cell.Bold || mps.boldDepth > 0,
            Italic = mps.cell.Italic, PadLeft = mps.pendingRunPadLeft,
        };
        mps.pendingRunPadLeft = 0;
        if (mps.runStyles.Count > 0 && mps.runStyles[^1].SameAs(cur) && cur.PadLeft == 0) return;
        if (mps.runStyles.Count >= MetricRunMarkLimit - MetricRunMarkBase) return;
        mps.runStyles.Add(cur);
        if (mps.curSeg is null) mps.textMarkCount++;
        (mps.curSeg is not null ? mps.divText : mt.text).Append((char)(MetricRunMarkBase + mps.runStyles.Count - 1));
    }

    /// <summary>The marks leave a text: the text without them, and the runs they marked (position in
    /// the cleaned text, style) - null when the text marks no run.</summary>
    private static (string text, List<(int pos, MetricRunStyle st)>? runs) SplitOutRunMarks(string marked, List<MetricRunStyle> styles)
    {
        var any = false;
        foreach (var ch in marked) if (IsRunMark(ch)) { any = true; break; }
        if (!any) return (marked, null);
        var sb = new StringBuilder(marked.Length);
        var runs = new List<(int pos, MetricRunStyle st)>();
        foreach (var ch in marked)
        {
            if (IsRunMark(ch))
            {
                var idx = ch - MetricRunMarkBase;
                if (idx < styles.Count) runs.Add((sb.Length, styles[idx]));
                continue;
            }
            sb.Append(ch);
        }
        var text = sb.ToString();
        // (marks left a leading space behind: the trim shifts every run with it)
        var lead = 0;
        while (lead < text.Length && text[lead] == ' ') lead++;
        if (lead > 0)
        {
            text = text[lead..];
            for (var i = 0; i < runs.Count; i++) runs[i] = (Math.Max(0, runs[i].pos - lead), runs[i].st);
        }
        text = text.TrimEnd(' ');
        return (text, runs.Count == 0 ? null : runs);
    }

    /// <summary>Removes every run mark from a raw text buffer.</summary>
    private static void StripRunMarks(StringBuilder sb)
    {
        var any = false;
        for (var i = 0; i < sb.Length; i++) if (IsRunMark(sb[i])) { any = true; break; }
        if (!any) return;
        var clean = new StringBuilder(sb.Length);
        for (var i = 0; i < sb.Length; i++) if (!IsRunMark(sb[i])) clean.Append(sb[i]);
        sb.Clear();
        sb.Append(clean);
    }

    /// <summary>The style of the ink at a position (the last run starting at or before it).</summary>
    private static MetricRunStyle? RunStyleAt(List<(int pos, MetricRunStyle st)> runs, int at)
    {
        MetricRunStyle? s = null;
        foreach (var (pos, st) in runs) { if (pos > at) break; s = st; }
        return s;
    }

    /// <summary>Whether the runs draw in more than one style over the text's ink.</summary>
    private static bool RunsAreMixed(string text, List<(int pos, MetricRunStyle st)> runs)
    {
        MetricRunStyle? first = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch) || ch == NbspChar || ch == MetricHardBreakChar) continue;
            var s = RunStyleAt(runs, i);
            if (s is null) continue;
            if (first is null) first = s;
            else if (!first.SameAs(s) || s.PadLeft > 0) return true;
        }
        return false;
    }

    /// <summary>The marks leave a cell's text: ink wholly in one style keeps the cell's flags (its
    /// weight and style), ink in several keeps the runs.</summary>
    private static void TakeRuns(MetricParseState mps, MetricCell cell)
    {
        var (t, runs) = SplitOutRunMarks(cell.Text, mps.runStyles);
        cell.Text = t;
        if (runs is null) return;
        if (RunsAreMixed(t, runs)) { cell.Runs = runs; return; }
        var s = FirstInkRunStyle(t, runs);
        if (s is not null) { cell.Bold |= s.Bold; cell.Italic |= s.Italic; }
    }

    private static void TakeRuns(MetricParseState mps, MetricDivSeg seg)
    {
        var (t, runs) = SplitOutRunMarks(seg.Text, mps.runStyles);
        seg.Text = t;
        if (runs is null) return;
        if (RunsAreMixed(t, runs)) { seg.Runs = runs; return; }
        var s = FirstInkRunStyle(t, runs);
        if (s is not null)
        {
            seg.Bold |= s.Bold; seg.Italic |= s.Italic; seg.Face ??= s.Face; seg.PadLeft += s.PadLeft;
            // (a heading's ink wholly inside a sized inline draws at that size, not the UA heading
            // size - measured on the financial statement: `<h2><span 11pt>` heads draw 11)
            if (seg.IsHeading && s.Fs is not null) seg.FontSize = s.Fs; else seg.FontSize ??= s.Fs;
        }
    }

    private static MetricRunStyle? FirstInkRunStyle(string text, List<(int pos, MetricRunStyle st)> runs)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch) || ch == NbspChar || ch == MetricHardBreakChar) continue;
            return RunStyleAt(runs, i);
        }
        return null;
    }

    /// <summary>A cell whose hard lines open in different styles draws each line as a band of its
    /// own - in the size, face, weight and style its first ink saw, the runs inside it kept - so a
    /// title line over a subtitle line each take their own face and box (the safety data sheet's
    /// Arial Black 36 over Arial Narrow italic 18). False when the lines share one style.</summary>
    private static bool BuildRunLineSegments(MetricParseState mps)
    {
        if (mps.cell is not { Runs: { } runs } cell || cell.Text.IndexOf(MetricHardBreakChar) < 0) return false;
        var lines = cell.Text.Split(MetricHardBreakChar);
        var styles = new List<MetricRunStyle?>();
        var pos = 0;
        foreach (var ln in lines)
        {
            MetricRunStyle? s = null;
            for (var i = 0; i < ln.Length; i++)
            {
                var ch = ln[i];
                if (char.IsWhiteSpace(ch) || ch == NbspChar) continue;
                s = RunStyleAt(runs, pos + i);
                break;
            }
            styles.Add(s);
            pos += ln.Length + 1;
        }
        MetricRunStyle? seen = null;
        var differ = false;
        foreach (var s in styles)
        {
            if (s is null) continue;
            if (seen is null) seen = s;
            else if (s.Fs != seen.Fs || s.Face != seen.Face || s.Italic != seen.Italic) { differ = true; break; }
        }
        if (!differ) return false;
        var segs = new List<MetricDivSeg>();
        pos = 0;
        for (var li = 0; li < lines.Length; li++)
        {
            var ln = lines[li].Trim(' ');
            var s = styles[li];
            var seg = new MetricDivSeg
            {
                Text = s is null ? NbspChar.ToString() : ln,
                FontSize = s?.Fs ?? cell.FontSize, Face = s?.Face ?? cell.Face, Bold = s?.Bold ?? cell.Bold,
                Italic = s?.Italic ?? cell.Italic, Fore = cell.Fore, PadLeft = s?.PadLeft ?? 0,
            };
            if (s is not null)
            {
                var start = lines[li].Length - lines[li].TrimStart(' ').Length;
                var lineRuns = new List<(int pos, MetricRunStyle st)>();
                foreach (var (rp, st) in runs)
                    if (rp >= pos && rp < pos + lines[li].Length) lineRuns.Add((Math.Max(0, rp - pos - start), st));
                if (lineRuns.Count > 0 && RunsAreMixed(ln, lineRuns)) seg.Runs = lineRuns;
            }
            segs.Add(seg);
            pos += lines[li].Length + 1;
        }
        while (segs.Count > 0 && segs[^1].Text == NbspChar.ToString()) segs.RemoveAt(segs.Count - 1);
        cell.DivSegs = segs;
        return true;
    }

    /// <summary>A nested block's starting style: what it inherits from the block it stands in -
    /// typography, colour, alignment and the accumulated side insets - never its margins or box.</summary>
    private static MetricDivSeg InheritedUaBlockSeg(MetricDivSeg parent) => new()
    {
        FontSize = parent.FontSize, Face = parent.Face, Bold = parent.Bold, Italic = parent.Italic, Fore = parent.Fore,
        AlignRight = parent.AlignRight, AlignCenter = parent.AlignCenter,
        PadLeft = parent.PadLeft, PadRight = parent.PadRight,
    };

    /// <summary>A UA cell block's own inline style and align attribute: size, weight, style, colour,
    /// alignment, its left inset (margin-left and padding-left, added to what it inherits), its right
    /// inset (margin-right and padding-right), and its stated vertical margins (negative allowed).</summary>
    private static void ReadUaCellBlockStyle(MetricDivSeg seg, Token tok, double cellEmPt)
    {
        if (tok.Attributes is not { } a) return;
        if (a.TryGetValue("align", out var al) && al is not null)
        {
            var alv = al.Trim().ToLowerInvariant();
            if (alv is "center" or "right" or "left") { seg.AlignCenter = alv == "center"; seg.AlignRight = alv == "right"; }
        }
        if (!a.TryGetValue("style", out var st) || st is null) return;
        ReadUaCellBlockStyleText(seg, st, cellEmPt);
    }

    /// <summary>The block's declarations as a style string - inline, or a sheet rule's spelled out
    /// the same way - read into the band: size, weight, style, face, colour, alignment, side insets
    /// and margins.</summary>
    private static void ReadUaCellBlockStyleText(MetricDivSeg seg, string st, double cellEmPt)
    {
        var fsM = Regex.Match(st, @"(?<![-\w])font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (fsM.Success && TryParseCssFontSize(fsM.Groups[1].Value.Trim()) is { } fs && fs > 0) seg.FontSize = fs;
        var emPt = seg.FontSize ?? (cellEmPt > 0 ? cellEmPt : UaDefaultFontPt);
        if (Regex.IsMatch(st, @"(?<![-\w])font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)) seg.Bold = true;
        if (Regex.IsMatch(st, @"(?<![-\w])font-style\s*:\s*italic", RegexOptions.IgnoreCase)) seg.Italic = true;
        if (Regex.Match(st, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } ffM
            && FirstFontFamily(ffM.Groups[1].Value) is { Length: > 0 } fam && SourceEngineFaces.Contains(fam))
            seg.Face = fam;
        if (Regex.Match(st, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } cM
            && ParseCssColor(cM.Groups[1].Value.Trim()) is { } col)
            seg.Fore = col;
        if (Regex.Match(st, @"(?<![-\w])text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase) is { Success: true } taM)
        {
            var ta = taM.Groups[1].Value.ToLowerInvariant();
            seg.AlignCenter = ta == "center"; seg.AlignRight = ta == "right";
        }
        if (Regex.IsMatch(st, @"(?<![-\w])float\s*:\s*right", RegexOptions.IgnoreCase)) { seg.FloatRight = true; seg.AlignRight = true; }
        double Len(string v) => StatedMarginPt(v, emPt) ?? 0;
        var (pt, pr, pb, pl) = CssPaddingSidesPt(st, emPt);
        seg.PadLeft += pl;
        seg.PadRight += pr;
        if (Regex.Match(st, @"(?<![-\w])margin-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mlM) seg.PadLeft += Len(mlM.Groups[1].Value);
        if (Regex.Match(st, @"(?<![-\w])margin-right\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mrM) seg.PadRight += Len(mrM.Groups[1].Value);
        if (Regex.Match(st, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mM)
        {
            var parts = mM.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                seg.MarginTopStatedPt = StatedMarginPt(parts[0], emPt);
                seg.MarginBottomStatedPt = StatedMarginPt(parts[parts.Length >= 3 ? 2 : 0], emPt);
                var right = parts.Length >= 2 ? parts[1] : parts[0];
                var left = parts.Length == 4 ? parts[3] : right;
                seg.PadRight += Len(right); seg.PadLeft += Len(left);
            }
        }
        if (Regex.Match(st, @"(?<![-\w])margin-top\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mtM)
            seg.MarginTopStatedPt = StatedMarginPt(mtM.Groups[1].Value, emPt);
        if (Regex.Match(st, @"(?<![-\w])margin-bottom\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mbM)
            seg.MarginBottomStatedPt = StatedMarginPt(mbM.Groups[1].Value, emPt);
        // (a block's vertical padding is band space above and below its lines)
        seg.MarginTopPt = (seg.MarginTopStatedPt ?? 0) + pt;
        seg.MarginBottomPt = (seg.MarginBottomStatedPt ?? 0) + pb;
        seg.MarginsExplicit = true;
    }

    /// <summary>Opens a heading's band in a plain UA cell: the text before it flushed as a band of its
    /// own, the heading at the UA size and weight of its level (the sheet's element rule and its
    /// inline style over that), its UA margins explicit, closed like a div at the heading's end.</summary>
    private static void OpenUaCellHeadingBand(MetricTableState mt, Token tok, string tag)
    {
        var mps = mt.mps;
        if (!mps.uaBlockCells && mps.curSeg is null) FlushUaCellText(mps, mt.text, mt.reportCells);
        CloseSeg(mps, mt.text, mt.reportCells, mt.stdSerif);
        var em = mps.cell!.FontSize ?? mps.fontSize;
        var (sizeEm, marginEm) = UaHeadingEm(tag);
        var seg = mps.divStyleStack.Count > 0 ? InheritedUaBlockSeg(mps.divStyleStack[^1]) : new MetricDivSeg { Fore = mps.cell.Fore, Face = mps.cell.Face };
        seg.Bold = true;
        seg.FontSize = sizeEm * em;
        seg.IsHeading = true;
        seg.DirectChild = mps.inlineWrapDepth == 0;
        // (the sheet's element rule dresses the band - face and colour; a rule that SIZES the level
        // keeps the heading on the calibrated cell-line model, see OpenMetricHeading)
        if (mt.css.TryGetValue(tag, out var headRule))
        {
            var probe = new MetricCell { FontSize = seg.FontSize, Face = seg.Face, Bold = true };
            ApplyCellClassBag(mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, probe, headRule);
            seg.Face = probe.Face ?? seg.Face;
            seg.Fore = probe.Fore ?? seg.Fore;
        }
        // ...and the sheet's DESCENDANT rules reaching the heading through the open blocks' classes
        // (`.header_sub h2 { padding: 20px; font-size: 18px; color; margin: -15px 3px 0 0; height:
        // 10px }`): the heading's size, colour, paddings and margins, and an EXACT box height its
        // line overflows (probed on the e-mail cards: the 18 px heads stand 20 px in from their
        // card, pulled 15 px up, in a 10 px box).
        // (the vertical PADDINGS the rules and the style state are band space of their own,
        //  kept on top of whichever margin - stated or UA - the heading ends up with)
        double headPadT = 0, headPadB = 0;
        if (AncestorClassRuleDecls(mt.css, mps.divStyleStack, tag, mps.hostBlockClasses) is { } ancDecls)
        {
            var ancProbe = new MetricCell { FontSize = seg.FontSize, Face = seg.Face, Bold = true };
            ApplyCellClassBag(mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, ancProbe, ancDecls);
            if (ancProbe.FontFromClass) seg.FontSize = ancProbe.FontSize;
            seg.Face = ancProbe.Face ?? seg.Face;
            seg.Fore = ancProbe.Fore ?? seg.Fore;
            ReadUaCellBlockStyleText(seg, DeclsAsStyle(ancDecls), seg.FontSize ?? em);
            headPadT += seg.MarginTopPt - (seg.MarginTopStatedPt ?? 0);
            headPadB += seg.MarginBottomPt - (seg.MarginBottomStatedPt ?? 0);
            if (ancProbe.HeightPt > 0) { seg.LineBoxPt = ancProbe.HeightPt; seg.LineBoxExact = true; }
        }
        if (tok.Attributes is { } headAttrs && headAttrs.TryGetValue("style", out var headSt) && headSt is not null)
        {
            ReadUaCellBlockStyle(seg, tok, seg.FontSize ?? em);
            headPadT += seg.MarginTopPt - (seg.MarginTopStatedPt ?? 0);
            headPadB += seg.MarginBottomPt - (seg.MarginBottomStatedPt ?? 0);
        }
        else ReadUaCellBlockStyle(seg, tok, seg.FontSize ?? em);
        var margin = marginEm * (seg.FontSize ?? em);
        seg.MarginTopPt = (seg.MarginTopStatedPt ?? margin) + headPadT;
        seg.MarginBottomPt = (seg.MarginBottomStatedPt ?? margin) + headPadB;
        seg.PadTopPt = headPadT;
        seg.MarginsExplicit = true;
        mps.divStyleStack.Add(seg);
        mps.divBoxStack.Add(false);
        mps.curSeg = seg;
        mps.uaHeadingBands++;
    }

    /// <summary>The sheet's descendant rules on <paramref name="tag"/> through the classes of the open
    /// blocks (`.cls tag`, `div.cls tag`), outer block first so an inner block's rule wins; null when
    /// none applies.</summary>
    private static Dictionary<string, string>? AncestorClassRuleDecls(IReadOnlyDictionary<string, Dictionary<string, string>> css, List<MetricDivSeg> openBlocks, string tag, string[]? hostClasses = null)
    {
        Dictionary<string, string>? merged = null;
        var chains = new List<string[]>();
        if (hostClasses is { Length: > 0 }) chains.Add(hostClasses);
        foreach (var block in openBlocks) if (block.Classes is { Length: > 0 } bc) chains.Add(bc);
        foreach (var classes in chains)
        {
            foreach (var cls in classes)
                foreach (var key in new[] { "." + cls + " " + tag, "div." + cls + " " + tag })
                    if (css.TryGetValue(key, out var rule))
                    {
                        merged ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var d in rule) merged[d.Key] = d.Value;
                    }
        }
        return merged;
    }

    /// <summary>A rule's declarations spelled as one inline-style string.</summary>
    private static string DeclsAsStyle(Dictionary<string, string> decls)
    {
        var sb = new StringBuilder();
        foreach (var d in decls) sb.Append(d.Key).Append(": ").Append(d.Value).Append("; ");
        return sb.ToString();
    }

    /// <summary>A band that draws nothing and takes no space: no text, margins, box, rule, fill or line box.</summary>
    private static bool UaBandIsInert(MetricDivSeg sg)
        => sg.MarginTopPt == 0 && sg.MarginBottomPt == 0 && !sg.BoxOpen && !sg.BoxClose && !sg.BorderBottom
            && sg.LineBoxPt == 0 && sg.Bg is null && sg.NestedTable < 0 && !sg.EmptyBlock && !sg.FloatRight;

    /// <summary>A UA cell's text that stands outside any block - before its first block, or after the
    /// last - is a band of its own in source order, in the typography the cell carries there; the
    /// nested-grid markers stay in the cell text for CloseCell's lift.</summary>
    private static void FlushUaCellText(MetricParseState mps, StringBuilder text, bool reportCells)
    {
        if (mps.cell is null || text.Length == 0) return;
        var raw = text.ToString();
        var markerRx = (char)2 + "\\d+" + (char)3;
        var markers = string.Concat(from Match m in Regex.Matches(raw, markerRx) select m.Value);
        var plain = CollapseWs(Regex.Replace(raw, markerRx, " ")).Trim(' ').TrimEnd(MetricHardBreakChar).Trim(' ');
        var ink = false;
        foreach (var ch in plain) if (!(ch == ' ' || ch == NbspChar || ch == MetricHardBreakChar || IsRunMark(ch))) { ink = true; break; }
        // (a `<br>` after a block, with no ink after it, is an empty line of its own - measured on
        // the lab report: `</h1><br/><table>` seats the grid one line under the heading's margin)
        if (!ink && mps.cell.DivSegs is { Count: > 0 } && !reportCells)
        {
            // (the breaks before the first grid marker stand between the band and the grid; the
            // breaks after the last marker are lines under the grid)
            var firstMarker = raw.IndexOf('\u0002');
            var lastMarker = raw.LastIndexOf('\u0003');
            var breaks = 0;
            for (var i = 0; i < (firstMarker < 0 ? raw.Length : firstMarker); i++) if (raw[i] == MetricHardBreakChar) breaks++;
            for (var i = 0; i < breaks; i++)
                mps.cell.DivSegs.Add(new MetricDivSeg
                {
                    Text = NbspChar.ToString(), FontSize = mps.cell.FontSize, Face = mps.cell.Face, Fore = mps.cell.Fore,
                });
            if (lastMarker >= 0)
                for (var i = lastMarker + 1; i < raw.Length; i++) if (raw[i] == MetricHardBreakChar) mps.cell.TrailingBreakLines++;
        }
        if (ink)
        {
            var leadTypo = mps.cell.DivSegs is null && mps.firstInkSeen;
            var band = new MetricDivSeg
            {
                Text = plain,
                FontSize = leadTypo ? mps.firstInkFs : mps.cell.FontSize,
                Face = leadTypo ? mps.firstInkFace : mps.cell.Face,
                Fore = leadTypo ? mps.firstInkFore : mps.cell.Fore,
                Bold = mps.cell.Bold, Italic = mps.cell.Italic,
            };
            if (!reportCells) TakeRuns(mps, band);
            (mps.cell.DivSegs ??= new List<MetricDivSeg>()).Add(band);
        }
        text.Clear();
        text.Append(markers);
    }

    /// <summary>Where each wrapped line starts in the text it was wrapped from.</summary>
    private static int[] LineStartOffsets(string text, string[] lines)
    {
        var offs = new int[lines.Length];
        var pos = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].Length == 0 ? pos : text.IndexOf(lines[i], pos, StringComparison.Ordinal);
            if (idx < 0) idx = pos;
            offs[i] = idx;
            pos = idx + lines[i].Length;
        }
        return offs;
    }

    /// <summary>A line's pieces of one style: consecutive characters whose run style, read at their
    /// position in the source text, agrees; a piece starting a padded run carries the pad.</summary>
    private static List<(string text, MetricRunStyle st, double pad)> SplitStyledRuns(string line, int lineStart, List<(int pos, MetricRunStyle st)> runs, MetricRunStyle fallback)
    {
        var parts = new List<(string text, MetricRunStyle st, double pad)>();
        var sb = new StringBuilder();
        MetricRunStyle? cur = null;
        var pad = 0.0;
        for (var i = 0; i < line.Length; i++)
        {
            var abs = lineStart + i;
            var s = RunStyleAt(runs, abs) ?? fallback;
            var starts = false;
            foreach (var (pos, _) in runs) if (pos == abs) { starts = true; break; }
            if (cur is not null && (!ReferenceEquals(s, cur) || (starts && s.PadLeft > 0)))
            {
                parts.Add((sb.ToString(), cur, pad)); sb.Clear(); pad = 0;
            }
            if (cur is null || !ReferenceEquals(s, cur) || (starts && s.PadLeft > 0)) { cur = s; if (starts) pad = s.PadLeft; }
            sb.Append(line[i]);
        }
        if (cur is not null && sb.Length > 0) parts.Add((sb.ToString(), cur, pad));
        return parts;
    }

    private static MetricCell RunProbe(MetricCell probe, MetricRunStyle st)
        => new() { Face = st.Face ?? probe.Face, Bold = st.Bold || probe.Bold, FontSize = st.Fs ?? probe.FontSize, Italic = st.Italic || probe.Italic };

    private static MetricRunStyle FallbackRunStyle(MetricCell probe)
        => new() { Fs = probe.FontSize, Face = probe.Face, Bold = probe.Bold, Italic = probe.Italic };

    /// <summary>A styled line's advance: each piece in its own face and size, its pad before it.</summary>
    private static double MeasureStyledLine(string face, string boldFace, MetricCell probe, double fs, string line, int lineStart, List<(int pos, MetricRunStyle st)> runs)
    {
        var w = 0.0;
        foreach (var (text, st, pad) in SplitStyledRuns(line, lineStart, runs, FallbackRunStyle(probe)))
        {
            var rp = RunProbe(probe, st);
            w += pad + MeasureFaceText(CellFaceName(face, boldFace, rp), text, rp.FontSize ?? fs);
        }
        return w;
    }

    /// <summary>Draws a styled line: each piece in its own face, size, weight and style, seated after
    /// the advance of the pieces before it, on the line's one baseline.</summary>
    private static void EmitStyledLine(MetricRowsState mr, Page page, MetricCell probe, double fs, double x, double y,
        string line, int lineStart, List<(int pos, MetricRunStyle st)> runs, double shear)
    {
        var rx = x;
        foreach (var (text, st, pad) in SplitStyledRuns(line, lineStart, runs, FallbackRunStyle(probe)))
        {
            rx += pad;
            if (text.Length == 0) continue;
            var rp = RunProbe(probe, st);
            var rShear = SyntheticItalicShearFor(rp.Face, rp.Bold, rp.Italic);
            if (rShear > 0) rp.Italic = false;
            var face = CellFaceName(mr.face, mr.boldFace, rp);
            var rfs = rp.FontSize ?? fs;
            EmitCellLineRuns(page, MetricRunRes(mr, page, rp), rfs, rx, y, text, face, rShear > 0 ? rShear : shear);
            rx += MeasureFaceText(face, text, rfs);
        }
    }

    /// <summary>The wrap of a styled text: words measured in the face and size of their own runs
    /// (hard breaks split first; a word wider than the box char-packs on its own line).</summary>
    private static string[] MeasuredWordWrapStyled(string text, double maxWidth, string face, string boldFace, MetricCell probe, double fs,
        List<(int pos, MetricRunStyle st)> runs)
    {
        if (text.Contains(MetricHardBreakChar))
        {
            var all = new List<string>();
            var pos = 0;
            foreach (var seg in text.Split(MetricHardBreakChar))
            {
                var lead = seg.Length - seg.TrimStart(' ').Length;
                var segRuns = new List<(int pos, MetricRunStyle st)>();
                foreach (var (rp, st) in runs) segRuns.Add((rp - pos - lead, st));
                all.AddRange(MeasuredWordWrapStyled(seg.Trim(' '), maxWidth, face, boldFace, probe, fs, segRuns));
                pos += seg.Length + 1;
            }
            return all.Count == 0 ? [""] : all.ToArray();
        }
        if (string.IsNullOrEmpty(text)) return [""];
        if (MeasureStyledLine(face, boldFace, probe, fs, text, 0, runs) <= maxWidth) return [text];
        var result = new List<string>();
        var line = new StringBuilder();
        double lineW = 0;
        var at = 0;
        foreach (var word in text.Split(' '))
        {
            var w = MeasureStyledLine(face, boldFace, probe, fs, word, at, runs);
            var gap = line.Length > 0 ? MeasureStyledLine(face, boldFace, probe, fs, " ", at - 1, runs) : 0;
            if (line.Length > 0 && lineW + gap + w > maxWidth)
            {
                result.Add(line.ToString());
                line.Clear(); lineW = 0; gap = 0;
            }
            if (gap > 0) line.Append(' ');
            line.Append(word); lineW += gap + w;
            at += word.Length + 1;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    /// <summary>The widest word of a styled text, each in its own run's face and size.</summary>
    private static double StyledMinContentPt(string face, string boldFace, MetricCell probe, double fs, string text, List<(int pos, MetricRunStyle st)> runs)
    {
        var minW = 0.0;
        var at = 0;
        foreach (var word in text.Split(new[] { ' ', MetricHardBreakChar }))
        {
            if (word.Length > 0) minW = Math.Max(minW, MeasureStyledLine(face, boldFace, probe, fs, word, at, runs));
            at += word.Length + 1;
        }
        return minW;
    }
}
