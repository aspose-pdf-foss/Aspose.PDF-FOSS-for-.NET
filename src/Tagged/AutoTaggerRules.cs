using System;
using System.Collections.Generic;
using System.Linq;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A rule standing alone runs at least this share of the page's text width (a short stroke is an
    // underline or a mark), and stands at most this many body sizes under the bottom of the block it
    // closes (its last line's text); it is drawn this thick.
    private const double LoneRuleShare = 0.3;
    private const double LoneRuleReachEms = 2.0;
    private const double LoneRuleThickness = 0.5;
    // The block a rule closes stands over it: this share of the narrower of the two overlaps the other
    // across (a block in the next column over, level with the rule, closes nothing).
    private const double RuleUnderShare = 0.5;
    // Two rules over the same span at most this far apart (points) are one rule drawn twice (a thick line and a thin one);
    // a short rule's ends lie within this many points of the heading's text it rules.
    private const double DoubleRuleGap = 4.0;
    private const double RuleSpanTolerance = 2.0;
    // Blocks whose feet stand this near (points) are as near a rule under them.
    private const double RuleTieTolerance = 2.0;

    /// <summary>A rule no table draws an edge of, and no drawing holds, closes the paragraph or
    /// heading standing just above it that it runs under most (a heading beside a note, the rule under both, closes the
    /// note): that block is ruled below, the rule the gap under its text away. With nothing standing that close above
    /// it, or the block there ruled by one nearer it, it opens the paragraph or heading starting just under it (a rule
    /// over a part's title): that block is ruled above. A rule as wide as a heading's text just over it opens that
    /// heading, however short. A second rule over the same span just past the first makes it a double rule.</summary>
    private static void MarkRuledBlocks(PageWork pw, List<Block> result, List<List<Line>?> blockLines, double bodySize)
    {
        var text = pw.Lines.Where(l => l.Running == 0 && l.Frags.Count > 0).ToList();
        if (text.Count == 0 || result.Count == 0) return;
        // The rules are placed on the blocks as they stand, a heading with its lines (kept apart from it); what they rule is
        // written back.
        var blocks = result.Select((b, k) => b.Lines is null && k < blockLines.Count && blockLines[k] is { Count: > 0 } kept ? b with { Lines = kept } : b).ToList();
        PlaceRules(pw, blocks, text, bodySize);
        for (var k = 0; k < result.Count; k++)
        {
            var b = result[k];
            (b.RuleGapBelow, b.RuleBox, b.RuleThickness, b.RuleGapAbove, b.RuleAbove, b.RuleDouble)
                = (blocks[k].RuleGapBelow, blocks[k].RuleBox, blocks[k].RuleThickness, blocks[k].RuleGapAbove, blocks[k].RuleAbove, blocks[k].RuleDouble);
            result[k] = b;
        }
    }

    private static void PlaceRules(PageWork pw, List<Block> blocks, List<Line> text, double bodySize)
    {
        var width = text.Max(l => l.Frags[^1].R) - text.Min(l => l.MinX);
        foreach (var rule in pw.Rules.Where(r => r.Horizontal))
        {
            if (pw.Tables.Any(t => SnapRules([rule], t.colX, t.rowY).Count > 0) || OnFrame(pw, rule)) continue;
            if (pw.Drawings.Any(d => rule.At >= d.Box.LLY && rule.At <= d.Box.URY && rule.From < d.Box.URX && rule.To > d.Box.LLX)) continue;
            // A rule the text of a line stands on, running no further than that text, is its underline (a link's, however
            // long the link): it rules no block.
            if (Underlines(text, rule)) continue;
            if (rule.To - rule.From < LoneRuleShare * width)
            {
                RuleHeading(blocks, rule, bodySize);
                continue;
            }
            // The blocks just above the rule, whatever their kind: a table or figure between closes nothing. A rule running
            // far past the text of a block's last line may stand a little over the bottom it states (a large title's
            // descent reaches past its glyphs); one running under that text alone is its underline. The nearest - of blocks
            // as near as it (a heading beside a note, both over the rule), the one the rule runs under most.
            var over = Enumerable.Range(0, blocks.Count).Where(i => (blocks[i].Y > rule.At
                    || (blocks[i].Y > rule.At - RuleOverlapEms * bodySize && RunsPastText(text, blocks[i], rule, bodySize)))
                    && StandsOver(blocks[i], rule) && !RuledBetween(pw, blocks[i], rule))
                .OrderBy(i => blocks[i].Y - rule.At).ToList();
            int? above = over.Count == 0 ? null
                : over.Where(i => blocks[i].Y - blocks[over[0]].Y <= RuleTieTolerance).OrderByDescending(i => Math.Round(Under(blocks[i], rule))).First();
            if (above is { } near && blocks[near].Y - rule.At > LoneRuleReachEms * Math.Max(blocks[near].Lines is { Count: > 0 } last ? last[^1].Size : bodySize, bodySize))
                above = null;
            if (above is null)
            {
                OpenBlock(blocks, rule, bodySize);
                continue;
            }
            // A list, table or figure nearest over the rule holds no rule under it: the rule opens the block under it, if any.
            if (blocks[above.Value].Kind is not (BlockKind.Paragraph or BlockKind.Heading))
            {
                OpenBlock(blocks, rule, bodySize);
                continue;
            }
            var i = above.Value;
            var size = blocks[i].Lines is { Count: > 0 } lines ? lines[^1].Size : bodySize;
            var b = blocks[i];
            if (b.RuleGapBelow is not null)
            {
                // A second rule over the same span just under the first draws it twice; one further off than the block's own
                // opens what stands under it.
                if (Stacked(b.RuleBox.At, b.RuleBox.From, b.RuleBox.To, rule))
                {
                    b.RuleDouble = true;
                    b.RuleThickness = Math.Max(b.RuleThickness, rule.Width);
                    b.RuleBox = b.RuleBox with { At = Math.Max(b.RuleBox.At, rule.At) };
                    b.RuleGapBelow = Math.Max(0, b.Y - b.RuleBox.At);
                    blocks[i] = b;
                    continue;
                }
                if (b.Y - b.RuleBox.At <= b.Y - rule.At)
                {
                    OpenBlock(blocks, rule, bodySize);
                    continue;
                }
            }
            b.RuleThickness = rule.Width;
            // The block's Y is the bottom of its last line's text: the rule stands the gap under it.
            b.RuleGapBelow = Math.Max(0, b.Y - rule.At);
            b.RuleBox = (rule.From, rule.To, rule.At, Math.Max(b.Y + size, b.SortTop));
            blocks[i] = b;
        }
    }

    // An underline stands within this share of its text's size under the baseline (in the descent; a heading's closing rule
    // stands further down, under it).
    private const double UnderlineDepthEms = 0.25;

    /// <summary>Whether a rule underlines a line: text of the line stands over most of the rule's length, the rule in
    /// that text's descent, and the rule runs no further than that text, give or take a size.</summary>
    private static bool Underlines(List<Line> text, PageContentScan.Rule rule)
    {
        foreach (var l in text)
        {
            var over = l.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text) && f.Base - rule.At >= 0 && f.Base - rule.At <= UnderlineDepthEms * f.Size
                                          && f.R > rule.From && f.X < rule.To).ToList();
            if (over.Count == 0) continue;
            var covered = over.Sum(f => Math.Max(0, Math.Min(f.R, rule.To) - Math.Max(f.X, rule.From)));
            if (covered > UnderlineCoverShare * (rule.To - rule.From)
                && rule.From >= over.Min(f => f.X) - l.Size && rule.To <= over.Max(f => f.R) + l.Size)
                return true;
        }
        return false;
    }

    /// <summary>How far a block's lines run over a rule across; nothing for a block without lines (a figure, a table).</summary>
    private static double Under(Block block, PageContentScan.Rule rule)
    {
        if (block.Lines is not { Count: > 0 } lines) return 0;
        var right = lines.Max(l => l.Frags.Count > 0 ? l.Frags[^1].R : l.MinX);
        return Math.Max(0, Math.Min(right, rule.To) - Math.Max(lines.Min(l => l.MinX), rule.From));
    }

    /// <summary>Whether another rule stands between a block and a rule under it, across the block's lines: the block is
    /// that one's to close.</summary>
    private static bool RuledBetween(PageWork pw, Block block, PageContentScan.Rule rule)
    {
        if (block.Lines is not { Count: > 0 } lines) return false;
        var (left, right) = (lines.Min(l => l.MinX), lines.Max(l => l.Frags.Count > 0 ? l.Frags[^1].R : l.MinX));
        return pw.Rules.Any(o => o.Horizontal && o.At > rule.At + RuleSpanTolerance && o.At < block.Y && !Stacked(rule.At, rule.From, rule.To, o)
                                 && o.From <= left + RuleSpanTolerance && o.To >= right - RuleSpanTolerance);
    }

    /// <summary>Whether a rule stands over the same span as one at <paramref name="at"/>, just past it: the two draw one
    /// rule twice.</summary>
    private static bool Stacked(double at, double from, double to, PageContentScan.Rule rule)
        => Math.Abs(rule.At - at) <= DoubleRuleGap && Math.Abs(rule.From - from) <= RuleSpanTolerance && Math.Abs(rule.To - to) <= RuleSpanTolerance;

    /// <summary>A rule too short to stand alone opens the heading under it whose text it spans, end to end (a heading's own
    /// rule over it, drawn as wide as its text).</summary>
    private static void RuleHeading(List<Block> blocks, PageContentScan.Rule rule, double bodySize)
    {
        var at = Enumerable.Range(0, blocks.Count).Where(i => blocks[i].Kind == BlockKind.Heading && blocks[i].Lines is { Count: > 0 } lines
                && Math.Abs(lines.Min(l => l.MinX) - rule.From) <= RuleSpanTolerance
                && Math.Abs(lines.Max(l => l.Frags.Count > 0 ? l.Frags[^1].R : l.MinX) - rule.To) <= RuleSpanTolerance
                && rule.At >= blocks[i].SortTop && rule.At - blocks[i].SortTop <= LoneRuleReachEms * Math.Max(lines[0].Size, bodySize))
            .Cast<int?>().FirstOrDefault();
        if (at is { } i) RuleAboveOf(blocks, i, rule);
    }

    // A block's first line may reach this share of its size over the rule that opens it.
    private const double RuleOverlapEms = 0.3;

    /// <summary>The paragraph or heading starting just under a rule nothing stands close above is ruled above. A rule
    /// standing among a block's lines - under its last line's text, an underline - opens nothing.</summary>
    private static void OpenBlock(List<Block> blocks, PageContentScan.Rule rule, double bodySize)
    {
        // Among the lines of the block across the rule's span: a paragraph going on from one column's foot at the next one's
        // top stands over the whole page, not beside a rule in the other column.
        if (blocks.Any(b => b.Lines?.Where(l => l.Frags.Count > 0 && l.MinX < rule.To && l.Frags[^1].R > rule.From).ToList() is { Count: > 0 } lines
                            && rule.At >= lines.Min(l => l.Y) - RuleOverlapEms * lines[^1].Size
                            && rule.At < lines.Max(l => l.Y + l.Size) - RuleOverlapEms * lines[0].Size && StandsOver(b, rule))) return;
        var under = Enumerable.Range(0, blocks.Count)
            .Where(i => blocks[i].SortTop <= rule.At + RuleOverlapEms * bodySize && StandsOver(blocks[i], rule))
            .OrderBy(i => rule.At - blocks[i].SortTop).Cast<int?>().FirstOrDefault();
        if (under is not { } at || blocks[at].Kind is not (BlockKind.Paragraph or BlockKind.Heading)) return;
        var b = blocks[at];
        if (rule.At - b.SortTop > LoneRuleReachEms * Math.Max(b.Lines is { Count: > 0 } lines ? lines[0].Size : bodySize, bodySize)) return;
        RuleAboveOf(blocks, at, rule);
    }

    /// <summary>Rules a block above: a second rule over the same span just over the first draws it twice; one further off
    /// than the block's own opens nothing.</summary>
    private static void RuleAboveOf(List<Block> blocks, int at, PageContentScan.Rule rule)
    {
        var b = blocks[at];
        if (b.RuleGapAbove is { } gap)
        {
            var had = b.SortTop + gap;
            if (!Stacked(had, b.RuleAbove.From, b.RuleAbove.To, rule))
            {
                if (had <= rule.At) return;
            }
            else
            {
                b.RuleDouble = true;
                b.RuleAbove = b.RuleAbove with { Thickness = Math.Max(b.RuleAbove.Thickness, rule.Width) };
                b.RuleGapAbove = Math.Max(0, Math.Min(had, rule.At) - b.SortTop);
                blocks[at] = b;
                return;
            }
        }
        b.RuleGapAbove = Math.Max(0, rule.At - b.SortTop);
        b.RuleAbove = (rule.From, rule.To, rule.Width);
        blocks[at] = b;
    }

    // A rule runs past a line's text when it reaches this many body sizes beyond it.
    private const double RulePastTextEms = 2.0;

    /// <summary>Whether a rule reaches well past the text of the line a block ends with, at either end.</summary>
    private static bool RunsPastText(List<Line> text, Block block, PageContentScan.Rule rule, double bodySize)
    {
        var last = text.Where(l => Math.Abs(l.Y - block.Y) <= LineTolerance && l.Frags[^1].R > rule.From && l.MinX < rule.To).ToList();
        if (last.Count == 0) return false;
        var past = RulePastTextEms * bodySize;
        return rule.To - last.Max(l => l.Frags[^1].R) > past || last.Min(l => l.MinX) - rule.From > past;
    }

    /// <summary>Whether a block's lines stand over a rule across: a block without lines (a figure, a table)
    /// is taken to.</summary>
    private static bool StandsOver(Block block, PageContentScan.Rule rule)
    {
        if (block.Lines is not { Count: > 0 } lines) return true;
        var left = lines.Min(l => l.MinX);
        var right = lines.Max(l => l.Frags.Count > 0 ? l.Frags[^1].R : l.MinX);
        var overlap = Math.Min(right, rule.To) - Math.Max(left, rule.From);
        return overlap >= RuleUnderShare * Math.Min(right - left, rule.To - rule.From);
    }

    /// <summary>State that a block is ruled below: /BorderStyle Solid, a /BorderThickness on the after
    /// edge alone, the gap between its text and the rule as its after /Padding, and its /BBox as wide as the
    /// rule runs (a rule under a short line spans the column).</summary>
    private static void StateRuleBelow(LS.StructureElement element, double gap, (double From, double To, double At, double Top) box, double drawn = 0)
    {
        var layout = element.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout);
        var style = new LS.StructureAttribute(LS.AttributeKey.BorderStyle);
        style.SetNameValue(LS.AttributeName.BorderStyle_Solid);
        layout.SetAttribute(style);
        var thickness = new LS.StructureAttribute(LS.AttributeKey.BorderThickness);
        thickness.SetArrayNumberValue([0, drawn > 0 ? Math.Round(drawn, 2) : LoneRuleThickness, 0, 0]);
        layout.SetAttribute(thickness);
        var padding = new LS.StructureAttribute(LS.AttributeKey.Padding);
        padding.SetArrayNumberValue([0, Math.Round(gap, 1), 0, 0]);
        layout.SetAttribute(padding);
        var bbox = new LS.StructureAttribute(LS.AttributeKey.BBox);
        bbox.SetArrayNumberValue([Math.Round(box.From, 1), Math.Round(box.At, 1), Math.Round(box.To, 1), Math.Round(box.Top, 1)]);
        layout.SetAttribute(bbox);
    }

    /// <summary>State that a block is ruled above: /BorderStyle Solid, a /BorderThickness on the before edge (beside
    /// the after edge's, when it is ruled below too), the gap between the rule and its text as its before /Padding,
    /// and its /BBox as wide as the rule runs.</summary>
    private static void StateRuleAbove(LS.StructureElement element, Block b)
    {
        // A rule drawn twice: /BorderStyle Double, the thicker line's thickness stated.
        if (b.RuleDouble && (b.RuleGapAbove is not null || b.RuleGapBelow is not null))
        {
            var doubled = new LS.StructureAttribute(LS.AttributeKey.BorderStyle);
            doubled.SetNameValue(LS.AttributeName.BorderStyle_Double);
            element.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(doubled);
        }
        if (b.RuleGapAbove is not { } gap) return;
        var layout = element.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout);
        var style = new LS.StructureAttribute(LS.AttributeKey.BorderStyle);
        style.SetNameValue(b.RuleDouble ? LS.AttributeName.BorderStyle_Double : LS.AttributeName.BorderStyle_Solid);
        layout.SetAttribute(style);
        var before = b.RuleAbove.Thickness > 0 ? Math.Round(b.RuleAbove.Thickness, 2) : LoneRuleThickness;
        var stated = layout.GetAttribute(LS.AttributeKey.BorderThickness)?.GetArrayNumberValue();
        var thickness = new LS.StructureAttribute(LS.AttributeKey.BorderThickness);
        thickness.SetArrayNumberValue([before, stated is { Length: 4 } ? stated[1] ?? 0 : 0, 0, 0]);
        layout.SetAttribute(thickness);
        var room = layout.GetAttribute(LS.AttributeKey.Padding)?.GetArrayNumberValue();
        var padding = new LS.StructureAttribute(LS.AttributeKey.Padding);
        padding.SetArrayNumberValue([Math.Round(gap, 1), room is { Length: 4 } ? room[1] ?? 0 : 0, 0, 0]);
        layout.SetAttribute(padding);
        if (layout.GetAttribute(LS.AttributeKey.BBox) is not null) return;
        var bbox = new LS.StructureAttribute(LS.AttributeKey.BBox);
        bbox.SetArrayNumberValue([Math.Round(b.RuleAbove.From, 1), Math.Round(b.Y, 1), Math.Round(b.RuleAbove.To, 1), Math.Round(b.SortTop + gap, 1)]);
        layout.SetAttribute(bbox);
    }
}
