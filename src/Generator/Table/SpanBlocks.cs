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
    /// <summary>True when the text contains a CJK / Han / Kana / Hangul character — the only
    /// case where a fragment's embedded font must override the table's Standard-14 render path
    /// (a plain embedded Latin font keeps the existing path to avoid changing green layouts).</summary>
    private static bool IsCjk(int o) =>
        (o >= 0x3400 && o <= 0x9FFF) || (o >= 0x3000 && o <= 0x30FF)
        || (o >= 0xF900 && o <= 0xFAFF) || (o >= 0xAC00 && o <= 0xD7AF);

    /// <summary>True when the text contains any CJK ideograph, kana, or Hangul character.</summary>
    private static bool ContainsCjk(string s)
    {
        foreach (var c in s) if (IsCjk(c)) return true;
        return false;
    }

    /// <summary>True when the text has CJK content AND the embedded font actually covers every
    /// CJK glyph. Only then does routing through Type0/CID help — a font without the glyphs
    /// (e.g. Arial on Japanese) must keep the existing path so its layout is unchanged.</summary>
    private static bool CjkCoveredBy(string s, byte[] ttf)
    {
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return false;
        var any = false;
        foreach (var ch in s)
        {
            if (!IsCjk(ch)) continue;
            any = true;
            if (!gp.CMap.TryGetValue(ch, out var g) || g == 0) return false;
        }
        return any;
    }

    /// <summary>True when the text holds codepoints OUTSIDE the single-face model —
    /// CJK radicals, plane-2 ideographs, technical symbols — that the primary face
    /// leaves as notdefs. These draw through the per-codepoint coverage chain
    /// (the chain substitutes SimSun / Segoe UI Symbol / SimSun-ExtB per glyph).</summary>
    private static bool NeedsCjkChain(string s, byte[]? primaryTtf)
    {
        var pp = primaryTtf is null ? null : GetInlineGlyphParser(primaryTtf);
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            var ext = (cp >= 0x2E80 && cp <= 0x2FDF) || cp >= 0x20000
                || (cp >= 0x2300 && cp <= 0x23FF);
            if (!ext) continue;
            if (pp is null || !pp.CMap.TryGetValue(cp, out var g) || g == 0) return true;
        }
        return false;
    }

    /// <summary>Greedy per-codepoint width wrap like WrapCjkToWidth, but each
    /// codepoint measures in the coverage-chain face that will draw it.</summary>
    private static List<string> WrapChainToWidth(string s, double fontSize, double availWidth,
        byte[]? primaryTtf, string primaryName)
    {
        var lines = new List<string>();
        if (availWidth <= 0) { lines.Add(s); return lines; }
        var chain0 = Aspose.Pdf.Text.CjkFallbackFont.ChainFaces();
        var effPrimary = primaryTtf ?? (chain0.Count > 0 ? chain0[0].Bytes : null);
        var cur = new System.Text.StringBuilder();
        double curW = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var frag = s[i].ToString();
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                frag = s.Substring(i, 2);
                i++;
            }
            var segs = SegmentByCoverageChain(frag, effPrimary, primaryName);
            var fragW = segs is { Count: > 0 }
                ? MeasureWidthWithFont(segs[0].Text, fontSize, segs[0].Ttf)
                : effPrimary is not null ? MeasureWidthWithFont(frag, fontSize, effPrimary)
                : fontSize;
            if (cur.Length > 0 && curW + fragW > availWidth)
            {
                lines.Add(cur.ToString());
                cur.Clear();
                curW = 0;
            }
            cur.Append(frag);
            curW += fragW;
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return lines;
    }

    /// <summary>A cell line: already-wrapped text with per-line font/color.</summary>
    private sealed class CellLine
    {
        public string Text = "";
        public double FontSize;

        /// <summary>Caller-declared leading (points) for this line: its pitch is
        /// <c>FontSize + Leading</c> and its baseline sits that much deeper, because
        /// the leading lies ABOVE the glyphs. Sourced from the paragraph's
        /// <c>TextState.LineSpacing</c> (or the document's under the XML dialect);
        /// zero when nothing declared one.</summary>
        public double Leading;
        /// <summary>The line came from the HTML engine's own parse, so a blank one is a
        /// real line box the markup asked for (a leading <c>&lt;br&gt;</c>) rather than a
        /// spacer the cell may collapse.</summary>
        public bool HtmlEngine;
        public Color? ForegroundColor;
        public bool Bold;                // draw with the bold face of the table font
        // The source fragment demands CSS line boxes even at a uniform size
        // (see TextFragment.CssLineBoxAlways).
        public bool CssForce;

        /// <summary>Extra left inset for this line within the cell content box —
        /// list items in an HTML cell indent by the list's margin-start, with
        /// the bullet hanging to the left of the indented text.</summary>
        public double LeftIndent;

        /// <summary>Extra right inset for a right-aligned line — the source
        /// paragraph's own margin-right seats the line this far inside the
        /// cell's padded right edge.</summary>
        public double RightInsetPt;

        /// <summary>Redline decorations for this line (strike/underline kinds
        /// as in the converter's DecorRuns; colour for marker borders).</summary>
        public List<(int Kind, Color? C)>? Decors;

        /// <summary>DataWorks form-grid input boxes for this line's
        /// InlineInputChar markers, in order.</summary>
        public List<(double W, double H, string Value, bool Mono, double Lift)>? InputBoxes;
        public List<(int S, int L, Color C)>? ColorRuns;
        /// <summary>The line's OWN css box (DataWorks control cells advance
        /// per-line instead of at the row's uniform pitch).</summary>
        public double OwnLinePt;

        /// <summary>Extra space above this line from the source fragment's
        /// Margin.Top (applied to the fragment's first wrapped line) — label
        /// groups inside a row-spanning sidebar cell are separated by each
        /// fragment's own top margin.</summary>
        public double TopGap;

        /// <summary>True for the blank lines that reserve a cell image's vertical
        /// footprint — they must survive the leading-blank-spacer drop, or the
        /// text after the image rides up underneath its blit.</summary>
        public bool ImgReserve;

        /// <summary>When set, this line renders a form-control glyph (e.g. a radio
        /// button circle) ahead of <see cref="Text"/>, which holds the option caption.</summary>
        public Aspose.Pdf.Forms.RadioButtonOptionField? Option;

        /// <summary>Radio options riding INLINE in <see cref="Text"/>: one entry per
        /// <see cref="InlineRadioChar"/>/<see cref="InlineRadioCheckedChar"/> in the
        /// text, in order. The render pass draws each as a circle glyph advancing the
        /// pen (captions are ordinary line text between the markers) and places the
        /// option's widget over its glyph.</summary>
        public List<Aspose.Pdf.Forms.RadioButtonOptionField>? InlineOptions;

        /// <summary>When set, this line carries a checkbox field whose widget is placed at
        /// the laid-out cell position (its /AP appearance supplies the box and check glyph).</summary>
        public Aspose.Pdf.Forms.CheckboxField? Checkbox;

        /// <summary>Hyperlink carried by the source fragment, if any. Rendered as a
        /// link annotation over the line's text rectangle.</summary>
        public Hyperlink? Hyperlink;

        /// <summary>FootNote carried by the source fragment (attached to the
        /// fragment's last laid-out line): its superscript marker renders right
        /// after this line's text and the note body joins the page-bottom band.</summary>
        public Note? FootNote;

        /// <summary>Inline hyperlinked runs (HTML <c>&lt;a&gt;</c> inside a table cell):
        /// pre-measured x-offset/width pairs (same metrics that laid the line out),
        /// each annotated over just its own glyph run rather than the whole line.</summary>
        public List<(double XOff, double W, Hyperlink Link)>? LinkRuns;

        /// <summary>Kerned embedded-face width of <see cref="Text"/> (set with <see cref="Runs"/>);
        /// used instead of the Standard-14 estimate when centring/right-aligning the line.</summary>
        public double KernedWidth;

        /// <summary>When set, this line holds already-shaped Arabic/Unicode text (visual order)
        /// that must be drawn with the embedded TrueType program here as a Type0/CID font (the
        /// table's default Standard-14 font can't display it). The render pass embeds the font
        /// and emits the line as hex glyph IDs.</summary>
        public byte[]? Type0Ttf;
        public string? Type0FontName;
        // When set, the Type0 render emits each space-separated token as its own positioned
        // run (space-separated CJK), so the absorber surfaces per-token fragments. Not set for
        // shaped Arabic (which is one visual-order run and must not be re-split).
        public bool Type0SplitTokens;
        // Form-grid mixed-style line: the render draws these segments in order, each
        // embedded in its own face variant, advancing by the segment's kerned width.
        public List<(string Text, byte[] Ttf, string Name)>? StyleRuns;
        // Stroke an underline beneath this line's drawn run (<u> in a cell of the
        // over-declared grid dialect).
        public bool Underline;

        /// <summary>Horizontal alignment for this individual line within the cell content box.
        /// Cell text and TextFragments inherit the cell's resolved alignment (cell → row →
        /// table DefaultCellTextState); an HtmlFragment keeps its own block alignment (left
        /// unless its style requests centre/right), so a single cell can mix alignments.</summary>
        public HorizontalAlignment Align = HorizontalAlignment.Left;

        // CSS line-box metrics carried from the source fragment (HTML styled-cell path):
        // ascent/descent as fractions of em. Zero = legacy layout.
        public double CssAsc;
        public double CssDesc;
        // Resolved CSS line-box geometry (set only when the OWNING CELL is in css-box
        // mode — its lines carry mixed font sizes): the box height this line occupies
        // and the baseline offset from the box top. Zero = legacy uniform stepping.
        public double BoxH;
        public double BaseOff;
        // A paragraph-margin spacer (generator dialect): an empty line whose BoxH is
        // the gap it reserves — never dropped as a "leading blank".
        public bool MarginSpacer;
        // An HTML-engine line born from a PLAIN-TEXT HtmlFragment in a generator
        // cell (the probed exact-stack dialect) — cells holding one price as
        // exact stacks; markup-family HTML cells keep the calibrated css boxes.
        public bool GenEngineExact;

        // Emit the Type0 run as a TJ array with the font's pair-kerning adjustments
        // (HTML-engine cell path only — those runs are kerned).
        public bool KernTj;

        // HTML-engine cell line: styled serif runs drawn at per-run x-offsets on the
        // common baseline (mixed bold/size within one line). Null = single-style line.
        public List<HtmlRun>? Runs;

        /// <summary>Styled pieces of a SPANNING cell's line, one per source segment run.
        /// A multi-segment fragment is how a caller mixes weight, slant, colour and
        /// underline within a line, and a spanning cell draws from these lines rather
        /// than from the inline layout. Null = the line is one uniform style.</summary>
        public List<SpanRun>? SegRuns;

        /// <summary>Inline boxes drawn behind this line's text (title plates, status
        /// pills) — see <see cref="InlineBoxDecoration"/>.</summary>
        public List<InlineBoxDecoration>? Boxes;
    }

    /// <summary>One slice of a row on one page. A row with content taller than the
    /// available page height produces multiple slices across consecutive pages.</summary>
    private sealed class RowSlice
    {
        public RowPlan Plan = null!;
        public int LineStart;
        public int LineCount;
        public double TopY;
        public double Height;
        public int RowIndex;
    }

    /// <summary>A cell spanning multiple rows (RowSpan &gt; 1). Its background, border and
    /// content are drawn once per page over the union of its rows' slices; the rows below
    /// the start row leave the spanned grid columns vacant.</summary>
    /// <summary>One styled piece of a spanning cell's line: the text a single source
    /// segment contributed, its x offset within the line, and the style it declared.</summary>
    private sealed class SpanRun
    {
        public string Text = "";
        public double X;
        public double Width;
        public double Size;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public Color? Color;
        public byte[]? Ttf;
        public string? FontName;
    }

    /// <summary>Segments of <paramref name="tf"/> that actually carry ink.</summary>
    private static int CountInkSegments(Aspose.Pdf.Text.TextFragment tf)
    {
        var n = 0;
        foreach (var seg in tf.Segments) if (!string.IsNullOrEmpty(seg.Text)) n++;
        return n;
    }

    /// <summary>Lay a multi-segment fragment out for a SPANNING cell: the segments run
    /// together on one line, each keeping its own size, weight, slant, colour, underline
    /// and face, and the line wraps at <paramref name="availWidth"/> like any other.
    /// Every line carries its runs (and their total width, which is what centres it).</summary>
    private List<CellLine> BuildSpanSegLines(Aspose.Pdf.Text.TextFragment tf, double availWidth,
        double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState,
        HorizontalAlignment align, double fragFontSize)
    {
        var lines = new List<CellLine>();
        var runs = new List<SpanRun>();
        var x = 0.0;
        var maxFs = 0.0;

        void FlushLine()
        {
            if (runs.Count == 0) return;
            lines.Add(new CellLine
            {
                Text = string.Concat(runs.ConvertAll(r => r.Text)),
                FontSize = maxFs > 0 ? maxFs : fragFontSize,
                Align = align,
                SegRuns = new List<SpanRun>(runs),
                KernedWidth = x,
            });
            runs.Clear();
            x = 0;
            maxFs = 0;
        }

        foreach (var seg in tf.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var ss = seg.TextState;
            var fs = ss.FontSizeTouched ? ss.FontSize
                : tf.TextState.FontSizeTouched ? tf.TextState.FontSize : defaultFontSize;
            var bold = ss.IsBold || tf.TextState.IsBold;
            var italic = ss.IsItalic || tf.TextState.IsItalic;
            var underline = ss.Underline || tf.TextState.Underline;
            var colour = ss.ForegroundColor ?? tf.TextState.ForegroundColor
                         ?? cellTextState?.ForegroundColor;
            var (ttf, faceName) = StyledSegmentFace(ss, bold, italic);

            double Measure(string t) => ttf is not null
                ? MeasureWidthWithFont(t, fs, ttf)
                : bold || italic ? MeasureFaceExact(t, fs, bold) : MeasureWidthExact(t, fs);

            foreach (var piece in seg.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (piece.Length == 0) { FlushLine(); continue; }
                foreach (var token in SplitKeepingSpaces(piece))
                {
                    var w = Measure(token);
                    // A leading space never opens a wrapped line.
                    if (runs.Count == 0 && x <= 0 && string.IsNullOrWhiteSpace(token)) continue;
                    if (x > 0 && x + w > availWidth + 1e-6 && !string.IsNullOrWhiteSpace(token))
                    {
                        FlushLine();
                        w = Measure(token);
                    }
                    runs.Add(new SpanRun
                    {
                        Text = token, X = x, Width = w, Size = fs,
                        Bold = bold, Italic = italic, Underline = underline,
                        Color = colour, Ttf = ttf, FontName = faceName,
                    });
                    x += w;
                    if (fs > maxFs) maxFs = fs;
                }
            }
        }
        FlushLine();
        return lines;
    }

    private sealed class SpanBlock
    {
        public int StartRow;             // first row index (inclusive)
        public int EndRow;               // last row index (exclusive), clamped to Rows.Count
        public int GridCol;              // starting grid column
        public int ColSpan;              // grid columns covered
        public Cell Cell = null!;
        public Row Row = null!;
        public List<CellLine> Lines = new();
        public double LineHeight;
        public double TightLine;
    }

    /// <summary>Wrapped content lines for a row-spanning cell (text-only: TextFragment /
    /// HtmlFragment). Mirrors the plain-text path of <see cref="BuildRowPlan"/>.</summary>
    /// <summary>True when a rotation turns the text onto the cell's other axis — a
    /// quarter turn either way. Half turns and the unrotated case still advance along the
    /// cell's width, so they keep the ordinary wrap.</summary>
    private static bool IsQuarterTurn(double rotation)
    {
        var deg = ((rotation % 360) + 360) % 360;
        return Math.Abs(deg - 90) < 0.01 || Math.Abs(deg - 270) < 0.01;
    }

    private void BuildSpanBlockLines(SpanBlock block, double[] colWidths)
    {
        var cell = block.Cell;
        var row = block.Row;
        var padding = EffectivePad(cell, row);
        var dp = DefaultPad(cell, row);
        // Same box the GRID cells wrap in: where the cell border joins the column pitch
        // the text starts at the border's inner edge and takes no implicit padding on
        // top of it, and the pitch itself is not text space. Measuring the span cell
        // with the border counted twice wrapped its heading a line early.
        var (spanPitchL, spanPitchR) = CellBorderPitch();
        var padLeft = padding?.Left ?? (spanPitchL > 0 ? 0 : dp);
        var padRight = padding?.Right ?? (spanPitchR > 0 ? 0 : dp);
        var width = GetCellWidth(colWidths, block.GridCol, block.ColSpan);
        var availWidth = width - padLeft - padRight - _columnPitch;

        var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
        var defaultFontSize = ResolveCellFontSize(cell, row);
        var cellAlign = ResolveCellAlignment(cell, row);
        double maxLine = 0, tight = 0;

        foreach (var paragraph in cell.Paragraphs)
        {
            string? text = null;
            double fragFontSize = defaultFontSize;
            Color? color = null;
            var fragBold = false;
            var fragAlign = cellAlign;
            // A fragment turned a quarter turn advances along the cell's HEIGHT, so the
            // width-derived extent above describes the wrong axis for it: a tall narrow
            // column would break such a run after every character and report each one as
            // its own fragment. The run stays whole; its own axis is what bounds it.
            var quarterTurned = paragraph is TextFragment qt && IsQuarterTurn(qt.TextState.Rotation);
            if (paragraph is TextFragment tf)
            {
                text = tf.Text;
                fragFontSize = ResolveCellParagraphFontSize(tf, defaultFontSize, cell, row);
                color = tf.TextState.ForegroundColor ?? textState?.ForegroundColor;
                fragBold = tf.TextState.IsBold || DeclaredCellBold(cell, row);
                if (!fragBold)
                    foreach (var fseg in tf.Segments)
                        if (fseg.TextState.IsBold && !string.IsNullOrEmpty(fseg.Text))
                        { fragBold = true; break; }
            }
            else if (paragraph is HtmlFragment html)
            {
                text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                color = textState?.ForegroundColor;
                // The fragment's own stylesheet text-align overrides the
                // spanning-cell default (a block declared left stays left in a
                // centred span cell). Its stylesheet font-size (px) sizes the
                // lines the same way it does in a grid cell.
                var hAligned = (html.HtmlContent ?? "").Replace(" ", string.Empty);
                if (hAligned.IndexOf("text-align:left", StringComparison.OrdinalIgnoreCase) >= 0)
                    fragAlign = HorizontalAlignment.Left;
                else if (hAligned.IndexOf("text-align:right", StringComparison.OrdinalIgnoreCase) >= 0)
                    fragAlign = HorizontalAlignment.Right;
                else if (hAligned.IndexOf("text-align:center", StringComparison.OrdinalIgnoreCase) >= 0)
                    fragAlign = HorizontalAlignment.Center;
                var hfs = Regex.Match(html.HtmlContent ?? "", @"font-size\s*:\s*([\d.]+)\s*px",
                    RegexOptions.IgnoreCase);
                if (hfs.Success && double.TryParse(hfs.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var hpx) && hpx > 0)
                    fragFontSize = hpx * 0.75;
            }
            // A deliberately-kept blank paragraph (a styled &nbsp; spacer <p>) reserves
            // its line box in the spanning cell as vertical space.
            if (string.IsNullOrEmpty(text) && paragraph is TextFragment kb && kb.CssKeepBlank)
            {
                var kfs = ResolveFragmentFontSize(kb, defaultFontSize);
                block.Lines.Add(new CellLine { Text = " ", FontSize = kfs, Align = fragAlign });
                if (kfs > maxLine) { maxLine = kfs; tight = kfs; }
                continue;
            }
            if (string.IsNullOrEmpty(text)) continue;
            if (fragFontSize > maxLine) { maxLine = fragFontSize; tight = fragFontSize; }
            var fragFirstLine = block.Lines.Count;
            // A MULTI-SEGMENT fragment is how a caller mixes weight, slant, colour and
            // underline inside one line. A spanning cell draws from these lines rather
            // than from the inline layout, so laying the segments out as styled runs
            // here is what keeps the emphasis a ColSpan cell already gets.
            if (paragraph is TextFragment segTf && CountInkSegments(segTf) > 1
                && BuildSpanSegLines(segTf, availWidth, defaultFontSize, textState,
                       fragAlign, fragFontSize) is { Count: > 0 } segLines)
            {
                foreach (var sl in segLines) block.Lines.Add(sl);
                if (ParagraphMargin(paragraph) is { Top: > 0 } segM && block.Lines.Count > fragFirstLine)
                    block.Lines[fragFirstLine].TopGap = segM.Top;
                continue;
            }
            // Opted-in band tables (HonorCellFontFaces): a spanning cell's serif
            // fragment draws in the embedded serif face with its real kerned
            // metrics, exactly like the grid-cell path — the Standard-14 Helvetica
            // fallback wraps wider than the serif-measured span width.
            if (HonorCellFontFaces && paragraph is TextFragment sbtf
                && IsSerifCssFamily(sbtf.TextState.Font?.FontName)
                && (fragBold ? BoldSerifTtf() : SerifTtf()) is { } spanSerifTtf)
            {
                if (text.IndexOf('☐') >= 0 || text.IndexOf('☒') >= 0)
                    text = text.Replace('☐', '□').Replace('☒', '□');
                foreach (var segment in text.Split('\n'))
                {
                    if (segment.Length == 0) continue;
                    // A whitespace-only paragraph (an &nbsp; spacer <p>) keeps its
                    // line box — the kerned wrap would swallow it entirely.
                    if (string.IsNullOrWhiteSpace(segment.Replace(' ', ' ')))
                    {
                        block.Lines.Add(new CellLine
                            { Text = segment, FontSize = fragFontSize, ForegroundColor = color, Align = fragAlign });
                        continue;
                    }
                    foreach (var l in WrapKernedLines(segment, fragFontSize, spanSerifTtf, availWidth))
                        block.Lines.Add(new CellLine
                        {
                            Text = l,
                            FontSize = fragFontSize,
                            ForegroundColor = color,
                            Align = fragAlign,
                            Type0Ttf = spanSerifTtf,
                            Type0FontName = fragBold ? "Times New Roman Bold" : "Times New Roman",
                            KernTj = true,
                            KernedWidth = MeasureWidthKerned(l, fragFontSize, spanSerifTtf),
                        });
                }
                if (ParagraphMargin(paragraph) is { Top: > 0 } sbm && block.Lines.Count > fragFirstLine)
                    block.Lines[fragFirstLine].TopGap = sbm.Top;
                continue;
            }
            foreach (var segment in text.Split('\n'))
            {
                if (segment.Length == 0) continue;
                // The generator measures on the bare AFM advance; the HTML dialects keep
                // the calibrated estimate (see the cell wrap in BuildRowPlan).
                var spanMeas = GeneratorDialect && !XmlGeneratorModel
                    ? new Func<string, double>(s => MeasureWidthExactAfm(s, fragFontSize))
                    : null;
                if (!quarterTurned
                    && cell.IsWordWrapped
                    && (spanMeas is null ? MeasureWidth(segment, fragFontSize) : spanMeas(segment)) > availWidth)
                {
                    foreach (var l in WrapText(segment, fragFontSize, availWidth, spanMeas))
                        block.Lines.Add(new CellLine { Text = l, FontSize = fragFontSize, ForegroundColor = color, Bold = fragBold, Align = fragAlign });
                }
                else
                    block.Lines.Add(new CellLine { Text = segment, FontSize = fragFontSize, ForegroundColor = color, Bold = fragBold, Align = fragAlign });
            }
            // The fragment's own top margin separates it from the previous
            // fragment in the spanning cell (applied above its first line).
            if (paragraph.Margin is { Top: > 0 } fm && block.Lines.Count > fragFirstLine)
                block.Lines[fragFirstLine].TopGap = fm.Top;
        }
        block.LineHeight = maxLine > 0 ? maxLine : defaultFontSize;
        block.TightLine = tight > 0 ? tight : block.LineHeight;
    }

    /// <summary>Resolve a cell's effective horizontal text alignment by walking
    /// cell → row → table. The default (<see cref="HorizontalAlignment.Left"/>/None) at a
    /// level means "inherit", so a table-wide <c>DefaultCellTextState.HorizontalAlignment =
    /// Center</c> reaches cells that don't override it — previously the cell's auto-created
    /// <c>DefaultCellTextState</c> (Left) shadowed the table default wholesale.</summary>
    private HorizontalAlignment ResolveCellAlignment(Cell cell, Row row)
    {
        static bool Set(HorizontalAlignment a) =>
            a != HorizontalAlignment.Left && a != HorizontalAlignment.None;

        if (Set(cell.Alignment)) return cell.Alignment;
        if (cell.DefaultCellTextState is { } cts && Set(cts.HorizontalAlignment)) return cts.HorizontalAlignment;
        if (row.DefaultCellTextState is { } rts && Set(rts.HorizontalAlignment)) return rts.HorizontalAlignment;
        if (DefaultCellTextState is { } tts && tts.HorizontalAlignment != HorizontalAlignment.None)
            return tts.HorizontalAlignment;
        return HorizontalAlignment.Left;
    }

    /// <summary>Resolve a cell's default text size by walking cell → row → table
    /// <see cref="DefaultCellTextState"/> for the first level whose FontSize was
    /// explicitly set (<see cref="Aspose.Pdf.Text.TextState.FontSizeTouched"/>).
    /// A TextState's FontSize defaults to 10 pt, so a plain <c>?? FontSize</c> chain
    /// stops at the cell's auto-created state and lets that 10 pt default shadow an
    /// explicit <c>row.DefaultCellTextState.FontSize = 8</c> — mirroring the
    /// alignment walk fixes that. Falls back to the effective default size when no
    /// level set one, preserving prior behaviour for untouched tables.</summary>
    private double ResolveCellFontSize(Cell cell, Row row)
    {
        if (cell.DefaultCellTextState is { FontSizeTouched: true } c) return c.FontSize;
        if (row.DefaultCellTextState is { FontSizeTouched: true } r) return r.FontSize;
        if (DefaultCellTextState is { FontSizeTouched: true } t) return t.FontSize;
        var ts = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
        return ts?.FontSize > 0 ? ts.FontSize : 12;
    }

    /// <summary>Best-effort horizontal alignment for an HtmlFragment from its inline CSS.
    /// A block-level HtmlFragment defaults to left and does NOT inherit the cell's text
    /// alignment (matching the generator, where a plain HtmlFragment stays left in a
    /// centred cell while a <c>justify-content:center</c>/<c>text-align:center</c> one centres).</summary>
    private static HorizontalAlignment ParseHtmlAlignment(string? html)
    {
        if (string.IsNullOrEmpty(html)) return HorizontalAlignment.Left;
        var h = html.Replace(" ", string.Empty);
        if (h.IndexOf("text-align:center", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("justify-content:center", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("align=\"center\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("align='center'", StringComparison.OrdinalIgnoreCase) >= 0)
            return HorizontalAlignment.Center;
        if (h.IndexOf("text-align:right", StringComparison.OrdinalIgnoreCase) >= 0 ||
            h.IndexOf("justify-content:flex-end", StringComparison.OrdinalIgnoreCase) >= 0)
            return HorizontalAlignment.Right;
        return HorizontalAlignment.Left;
    }

    /// <summary>Lines for an HTML cell whose body is block text followed by a
    /// <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c> list; null when the fragment carries no
    /// list items. The fragment's stylesheet font-size (px, browser 0.75 px→pt)
    /// sizes every line. Items indent by the list margin-start (CSS default
    /// 40px) and keep that indent on continuation lines, with the bullet
    /// hanging to the left of each item's first line.</summary>
    private List<CellLine>? BuildHtmlListCellLines(string? htmlContent, double availWidth,
        double defaultSize, Color? color, HorizontalAlignment blockAlign)
    {
        var h = htmlContent ?? "";
        if (h.IndexOf("<li", StringComparison.OrdinalIgnoreCase) < 0) return null;

        var size = defaultSize;
        var fsm = Regex.Match(h, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (fsm.Success && double.TryParse(fsm.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var px) && px > 0)
            size = px * 0.75;

        var result = new List<CellLine>();
        var listStart = Regex.Match(h, @"<[uo]l\b", RegexOptions.IgnoreCase);
        var head = HtmlFragment.StripHtmlTags(listStart.Success ? h[..listStart.Index] : h).Trim();
        // Justified block text in a narrow list cell seats each line at the
        // column's right edge (short lines keep their natural width).
        var headAlign = (h.Replace(" ", string.Empty))
            .IndexOf("text-align:justify", StringComparison.OrdinalIgnoreCase) >= 0
            ? HorizontalAlignment.Right : blockAlign;
        foreach (var seg in head.Split('\n'))
        {
            var s = seg.Trim();
            if (s.Length == 0) continue;
            foreach (var l in WrapText(s, size, availWidth))
                result.Add(new CellLine { Text = l, FontSize = size, ForegroundColor = color, Align = headAlign, BoxH = size, BaseOff = size });
        }

        const double listIndent = 40 * 0.75; // CSS default margin-start
        var bulletPrefix = (char)0x95 + " "; // WinAnsi bullet
        var bulletW = MeasureWidthExact(bulletPrefix, size);
        foreach (Match im in Regex.Matches(h, @"<li\b[^>]*>(.*?)</li>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var item = HtmlFragment.StripHtmlTags(im.Groups[1].Value).Trim();
            if (item.Length == 0) continue;
            var wrapped = WrapText(item, size, availWidth - listIndent + bulletW);
            for (var wi = 0; wi < wrapped.Count; wi++)
                result.Add(new CellLine
                {
                    Text = wi == 0 ? bulletPrefix + wrapped[wi] : wrapped[wi],
                    FontSize = size,
                    ForegroundColor = color,
                    Align = HorizontalAlignment.Left,
                    LeftIndent = wi == 0 ? listIndent - bulletW : listIndent,
                    BoxH = size,
                    BaseOff = size,
                });
        }
                // The list block reserves a strip below its last item (the list bottom
        // margin collapsed with the item's negative margin), keeping the row
        // height a list-bearing cell produces.
        result.Add(new CellLine { Text = "", FontSize = size, BoxH = size / 2, BaseOff = size / 2 });
        return result.Count > 0 ? result : null;
    }

    /// <summary>Default per-side cell padding when no explicit padding is set. A "bare" cell —
    /// no border and no explicit margin/cell-padding anywhere in the cell → row → table chain —
    /// gets 0, matching the generator (its borderless default cell seats text at the
    /// content edge and its row pitch is just the font size). Cells that carry a border or
    /// explicit padding keep the historical 2pt default (their templates depend on it).</summary>
    private double DefaultPad(Cell cell, Row row)
    {
        // A BorderInfo with Side=None is decorative-only — such cells behave
        // exactly like borderless ones (text at the content edge,
        // row pitch = font size), so only a border that actually DRAWS keeps
        // the historical 2 pt padding.
        static bool Draws(BorderInfo? b) => b is not null && b.Side != BorderSide.None;
        var hasBorder = Draws(cell.Border) || Draws(row.DefaultCellBorder) || Draws(row.Border)
            || Draws(DefaultCellBorder) || Draws(Border);
        var hasPad = cell.Margin is not null || row.DefaultCellPadding is not null || DefaultCellPadding is not null;
        return (!hasBorder && !hasPad) ? 0.0 : 2.0;
    }
}
