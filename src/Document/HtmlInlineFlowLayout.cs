using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Where a block's lines seat and how the block's box closes.</summary>
    private sealed class HtmlLinePlacement
    {
        public double Left;
        public double Width;
        public HorizontalAlignment Align;
        public string? Marker;
        /// <summary>The block's box ends at its last baseline (plus <see cref="EndExtra"/>)
        /// rather than at the last line box's bottom.</summary>
        public bool EndAtBaseline;
        public double EndExtra;
    }

    /// <summary>Lay an in-page fragment out on the UA flow. LAW (probed on the reference
    /// engine, 2026-09-07): the fragment sets in its base face (Times New Roman 12 pt unless
    /// it, a body style rule or the page names one) on CSS line boxes — the face's hhea line
    /// height rounded to whole pixels, the baseline half the surplus leading plus the ascent
    /// under the box top; a line's box is the tallest of its runs' boxes (and the strut's)
    /// sharing one baseline; a picture stands on the baseline, or centres on the x-height
    /// when vertical-align says middle; a control stands on the baseline in its own box; a
    /// <c>br</c> closes the line; lines wrap greedily at spaces on the content width
    /// rounded up to whole pixels. Paragraphs and divisions carry no margin (1.12 em when
    /// the fragment asks for paragraph margins), headings their UA margins, a list its 1.12
    /// em box; margins collapse by the larger; a division at least fills its CSS height. An
    /// element block ends at its last line box, and so does an anonymous piece followed by
    /// anything; the fragment's LAST anonymous piece ends at its last baseline plus the
    /// descent of the fragment's first element's face when the piece holds an element (a
    /// control counts as none). The fragment's own margins are spent in full; its trailing
    /// block margin is not.</summary>
    private bool TryLayoutUaFlowFragment(HtmlFragmentLayoutState hl) => TryLayoutUaFlowFragment(hl, hl.htmlContent, lastPiece: true);

    /// <summary>Lay out one piece of a fragment on the UA flow: the whole fragment, or a text
    /// piece between the tables of a mixed fragment (the tables lay out as tables between
    /// the pieces; only the fragment's last piece ends at a baseline).</summary>
    private bool TryLayoutUaFlowFragment(HtmlFragmentLayoutState hl, string html, bool lastPiece)
    {
        if (!IsUaFlowHtml(html)) return false;
        var st = new HtmlInlineFlowState();
        st.hl = hl;
        st.flow = hl.flow;
        st.html = html;
        st.lastPiece = lastPiece;
        if (!ResolveInlineBase(st)) return false;
        st.items = new List<HtmlInlineItem>();
        var blocks = ParseHtmlFlowBlocks(html, hl, st.basePt, out var firstIsControl, out var firstStyle);
        if (blocks.Count == 0) return false;
        st.defaultColor = hl.htmlColor;
        st.fontDict = Table.ResolvePageFontDict(st.flow.CurrentPage);
        st.left = st.flow.CurrentLeft;
        st.width = st.flow.CurWidth;
        if (firstStyle is not null && !firstIsControl)
        {
            var f = InlineFaceFor(st, firstStyle.Family, firstStyle.Bold, firstStyle.Italic);
            st.endExtra = f.Descent(firstStyle.SizePt > 0 ? firstStyle.SizePt : st.basePt);
        }
        if (hl.html.Margin is { Top: > 0 } mt) st.flow.AdvanceY(mt.Top);
        st.csb = new Content.ContentStreamBuilder();
        var pending = 0.0;
        var startY = st.flow.CurrentY;
        foreach (var b in blocks) PlaceFlowBlock(st, b, ReferenceEquals(b, blocks[^1]), ref pending);
        st.flow.InjectContentAtCursor(st.csb.Build());
        FlushFragmentLink(st);
        // The fragment reports its widest line and the height its box spent (probed: a
        // bare one-line fragment reports its kerned width and the 10.7988 baseline drop).
        hl.html.Rectangle = new System.Drawing.RectangleF((float)st.left, (float)st.flow.CurrentY, (float)st.widestLine, (float)(startY - st.flow.CurrentY));
        if (hl.html.Margin is { Bottom: > 0 } mb) st.flow.AdvanceY(mb.Bottom);
        return true;
    }

    /// <summary>The fragment's base face and size: its own text state's font, else the
    /// page's assigned default text state's (either must carry measurable TrueType data,
    /// or the fragment keeps the calibrated engines), else a body style rule's family,
    /// else the UA body face.</summary>
    private bool ResolveInlineBase(HtmlInlineFlowState st)
    {
        var ts = st.hl.html.TextState;
        var (bodyFamily, bodyPt) = HtmlBodyStyleRule(st.html);
        UaFace? given = null;
        var givenFont = ts?.Font ?? DefaultTextStateFace(st.hl.page);
        if (givenFont is not null)
        {
            given = UaFace.FromFont(givenFont, "FUa" + (st.faceCount + 1));
            if (given is null) return false;
            st.faceCount++;
            st.baseFamily = givenFont.FontName;
        }
        else if (bodyFamily is { Length: > 0 })
            st.baseFamily = bodyFamily;
        if (ts is { FontSizeTouched: true } && ts.FontSize > 0) st.basePt = ts.FontSize;
        else if (bodyPt > 0) st.basePt = bodyPt;
        var strut = given
                    ?? TryLoadInlineFace(st, st.baseFamily, false, false)
                    ?? TryLoadInlineFace(st, UaInlineBaseFamily, false, false);
        if (strut is null) return false;
        st.strut = strut;
        st.faces[st.baseFamily.ToLowerInvariant()] = strut;
        return true;
    }

    /// <summary>Open a block under its collapsed margin, seat its lines and close its box (an
    /// anonymous piece ends at its baseline only as the fragment's last block; before a block,
    /// a table or a break it ends at its full line box - probed: Arial 12's 0.047 half-leading
    /// counts, Times 12's title piece seats the paragraph after it one full 13.5 box down).</summary>
    private void PlaceFlowBlock(HtmlInlineFlowState st, HtmlFlowBlock b, bool last, ref double pending)
    {
        var anonymous = b.Kind == HtmlFlowBlockKind.Anonymous;
        if (!anonymous && b.Items.Count == 0)
        {
            // An empty element block has no box; its margins collapse through it.
            pending = System.Math.Max(pending, System.Math.Max(b.MarginTop, b.MarginBottom));
            return;
        }
        var margin = System.Math.Max(pending, b.MarginTop);
        if (margin > 0) st.flow.AdvanceY(margin);
        var lines = BuildInlineLines(st, b.Items, st.width - b.IndentPt);
        var place = new HtmlLinePlacement
        {
            Left = st.left + b.IndentPt, Width = st.width - b.IndentPt,
            Align = b.Align ?? st.hl.html.HorizontalAlignment, Marker = b.Marker,
            EndAtBaseline = anonymous && last && st.lastPiece, EndExtra = anonymous && b.HasElement ? st.endExtra : 0,
        };
        var startY = st.flow.CurrentY;
        for (var li = 0; li < lines.Count; li++)
        {
            st.widestLine = System.Math.Max(st.widestLine, lines[li].Pen);
            PlaceInlineLine(st, lines[li], place, li == lines.Count - 1);
            place.Marker = null;
        }
        var used = startY - st.flow.CurrentY;
        if (b.HeightPt > used && used >= 0) st.flow.AdvanceY(b.HeightPt - used);
        pending = b.MarginBottom;
    }

    /// <summary>Greedy space-break wrap of the items onto lines of <paramref name="width"/>:
    /// a word never splits unless the fragment breaks words and the word alone overflows;
    /// the space a line breaks at is dropped; a break closes the line.</summary>
    private static List<HtmlInlineLine> BuildInlineLines(HtmlInlineFlowState st, List<HtmlInlineItem> items, double width)
    {
        var lines = new List<HtmlInlineLine>();
        var line = new HtmlInlineLine();
        var maxW = InlinePixelWrapWidth(width);
        void Close() { lines.Add(line); line = new HtmlInlineLine(); }
        foreach (var item in items)
        {
            if (item.Kind == HtmlInlineKind.Break) { Close(); continue; }
            if (item.Kind != HtmlInlineKind.Text)
            {
                var bw = InlineBoxWidth(item);
                if (line.HasContent && line.Pen + bw > maxW) Close();
                line.Cells.Add(new HtmlInlineCell { Item = item, X = line.Pen, W = bw });
                line.Pen += bw;
                continue;
            }
            foreach (Match tm in HtmlFlowTokenRegex.Matches(item.Text))
                PlaceInlineToken(st, lines, ref line, item, tm.Value, maxW);
        }
        if (line.HasContent || lines.Count == 0) lines.Add(line);
        return lines;
    }

    /// <summary>A wrap token: leading spaces plus a word (which may open with hyphens), where a
    /// word also ends after a hyphen followed by a letter or digit (the UA breaks "well-known"
    /// as "well-" / "known" and, probed, "0123456789-0123456789" after its hyphen when the
    /// word does not fit); a standalone hyphen run ("issues - opinion") and a run of spaces
    /// alone are tokens too.</summary>
    private static readonly Regex HtmlFlowTokenRegex = new(@" *(?:-+)?(?:[^ -]+(?:-(?![A-Za-z0-9]))?[^ -]*)+?(?:-(?=[A-Za-z0-9])|(?= )|$)| *-+(?= |$)| +", RegexOptions.Compiled);

    /// <summary>The width lines wrap on: the content width rounded UP to whole CSS pixels
    /// (probed: a 483 pt = 644 px width refuses a 0.12 pt overrun, a 523.75 pt = 698.33 px
    /// one takes a 0.16 pt overrun).</summary>
    private static double InlinePixelWrapWidth(double widthPt) =>
        CssPxToPt * System.Math.Ceiling(widthPt / CssPxToPt - WrapPixelEpsilon);

    /// <summary>Tolerance under which a width already sits on a whole pixel.</summary>
    private const double WrapPixelEpsilon = 1e-6;

    private static void PlaceInlineToken(HtmlInlineFlowState st, List<HtmlInlineLine> lines, ref HtmlInlineLine line,
        HtmlInlineItem item, string token, double maxW)
    {
        var draw = line.HasContent ? token : token.TrimStart(' ');
        if (draw.Length == 0) return;
        var w = InlineTextWidth(st, item, draw);
        if (line.HasContent && line.Pen + w > maxW && draw.Trim().Length > 0)
        {
            lines.Add(line);
            line = new HtmlInlineLine();
            draw = token.TrimStart(' ');
            if (draw.Length == 0) return;
            w = InlineTextWidth(st, item, draw);
        }
        if (!line.HasContent && w > maxW && st.hl.html.IsBreakWords && draw.Length > 1)
        {
            var fit = 1;
            while (fit < draw.Length && InlineTextWidth(st, item, draw[..(fit + 1)]) <= maxW) fit++;
            AppendInlineCell(st, line, item, draw[..fit]);
            lines.Add(line);
            line = new HtmlInlineLine();
            PlaceInlineToken(st, lines, ref line, item, draw[fit..], maxW);
            return;
        }
        AppendInlineCell(st, line, item, draw);
    }

    /// <summary>Add text to the line, merged into the previous cell when it belongs to the
    /// same run (one show per run per line).</summary>
    private static void AppendInlineCell(HtmlInlineFlowState st, HtmlInlineLine line, HtmlInlineItem item, string text)
    {
        var w = InlineTextWidth(st, item, text);
        if (line.Cells.Count > 0 && ReferenceEquals(line.Cells[^1].Item, item))
        {
            line.Cells[^1].Text += text;
            line.Cells[^1].W = InlineTextWidth(st, item, line.Cells[^1].Text);
            line.Pen = line.Cells[^1].X + line.Cells[^1].W;
            return;
        }
        line.Cells.Add(new HtmlInlineCell { Item = item, Text = text, X = line.Pen, W = w });
        line.Pen += w;
    }

    private static double InlineBoxWidth(HtmlInlineItem item) => item.Kind switch
    {
        HtmlInlineKind.Image => item.ImageW,
        HtmlInlineKind.Radio => RadioLeftMarginPt + RadioBoxPt + RadioRightMarginPt,
        HtmlInlineKind.Checkbox => CheckboxLeftMarginPt + CheckboxBoxPt + CheckboxRightMarginPt,
        _ => 0,
    };

    /// <summary>The line's box: the strut's and every run's above/below on the shared
    /// baseline, a baseline-standing picture or control raising the above, a middle-aligned
    /// picture centred on the strut's x-height.</summary>
    private static void MeasureInlineLine(HtmlInlineFlowState st, HtmlInlineLine line)
    {
        var above = st.strut.Above(st.basePt);
        var below = st.strut.Below(st.basePt);
        foreach (var c in line.Cells)
        {
            switch (c.Item.Kind)
            {
                case HtmlInlineKind.Text:
                    var (face, size) = InlineRunFace(st, c.Item);
                    above = System.Math.Max(above, face.Above(size));
                    below = System.Math.Max(below, face.Below(size));
                    break;
                case HtmlInlineKind.Image when c.Item.ImageMiddle:
                    var half = st.strut.XHeightPt(st.basePt) / 2;
                    above = System.Math.Max(above, c.Item.ImageH / 2 + half);
                    below = System.Math.Max(below, c.Item.ImageH / 2 - half);
                    break;
                case HtmlInlineKind.Image:
                    above = System.Math.Max(above, c.Item.ImageH);
                    break;
                case HtmlInlineKind.Radio:
                    above = System.Math.Max(above, RadioLineAbovePt);
                    break;
                case HtmlInlineKind.Checkbox:
                    above = System.Math.Max(above, CheckboxTopMarginPt + CheckboxBoxPt);
                    break;
            }
        }
        line.Above = above;
        line.Below = below;
    }

    /// <summary>Open the line's box at the cursor (on the next page when it does not fit),
    /// seat its cells (and the block's marker) on the baseline and advance: by the full box,
    /// or on a baseline-ended block's last line to the baseline plus its closing descent.</summary>
    private void PlaceInlineLine(HtmlInlineFlowState st, HtmlInlineLine line, HtmlLinePlacement place, bool last)
    {
        MeasureInlineLine(st, line);
        var lineH = line.Above + line.Below;
        var flow = st.flow;
        if (flow.CurrentY - lineH < flow.BottomMargin && flow.CurrentY < flow.ContentTop)
        {
            flow.InjectContentAtCursor(st.csb.Build());
            st.csb = new Content.ContentStreamBuilder();
            FlushFragmentLink(st);
            flow.ForceNewPage();
        }
        var top = flow.CurrentY;
        var baseline = top - line.Above;
        var slack = place.Align switch
        {
            HorizontalAlignment.Right => place.Width - line.Pen,
            HorizontalAlignment.Center => (place.Width - line.Pen) / 2,
            _ => 0,
        };
        if (slack < 0) slack = 0;
        if (place.Marker is { Length: > 0 } marker) EmitListMarker(st, marker, place.Left, baseline);
        var imageBelow = 0.0;
        foreach (var c in line.Cells)
        {
            var x0 = place.Left + slack + c.X;
            switch (c.Item.Kind)
            {
                case HtmlInlineKind.Text: EmitInlineText(st, c, x0, baseline); break;
                case HtmlInlineKind.Image: imageBelow = System.Math.Max(imageBelow, EmitInlineImage(st, c, x0, top, baseline)); break;
                case HtmlInlineKind.Radio: EmitInlineRadio(st, c, x0, baseline); break;
                case HtmlInlineKind.Checkbox: EmitInlineCheckbox(st, c, x0, top); break;
            }
        }
        if (st.hl.html.Hyperlink is not null && line.Pen > 0)
            st.linkBox = UnionBox(st.linkBox, new Rectangle(place.Left + slack, baseline - line.Below, place.Left + slack + line.Pen, top));
        var endAtBaseline = last && place.EndAtBaseline;
        flow.AdvanceY(endAtBaseline ? line.Above + System.Math.Max(place.EndExtra, imageBelow) : lineH);
    }

    /// <summary>A hyperlink set on the fragment itself covers every line it laid out on the
    /// page with ONE Link annotation (the rule the calibrated paths follow).</summary>
    private static void FlushFragmentLink(HtmlInlineFlowState st)
    {
        if (st.linkBox is { } box && st.hl.html.Hyperlink is { } link) st.flow.QueueLink(box, link);
        st.linkBox = null;
    }

    private static Rectangle UnionBox(Rectangle? a, Rectangle b) => a is null ? b
        : new Rectangle(System.Math.Min(a.LLX, b.LLX), System.Math.Min(a.LLY, b.LLY), System.Math.Max(a.URX, b.URX), System.Math.Max(a.URY, b.URY));

    /// <summary>A list marker in the base face with its right edge the marker gap before the item's text.</summary>
    private void EmitListMarker(HtmlInlineFlowState st, string marker, double textLeft, double baseline)
    {
        var item = new HtmlInlineItem { Kind = HtmlInlineKind.Text, Text = marker };
        var w = InlineTextWidth(st, item, marker);
        EmitInlineText(st, new HtmlInlineCell { Item = item, Text = marker, W = w }, textLeft - UaListMarkerGapPt - w, baseline);
    }
}
