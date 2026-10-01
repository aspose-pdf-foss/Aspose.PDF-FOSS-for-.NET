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
    /// <summary>Html engine cell: one tag of the markup absorbed, with the text before it emitted.</summary>
    private static void AbsorbHtmlEngineTag(HtmlEngineCellState hc, Match m)
    {
        // A tag inside a float column: the column's own parse consumed it.
        if (m.Index < hc.pos) return;
        if (m.Index > hc.pos) EmitText(hc, hc.html!.Substring(hc.pos, m.Index - hc.pos));
        hc.pos = m.Index + m.Length;
        var closing = m.Groups[1].Value.Length > 0;
        var tag = m.Groups[2].Value.ToLowerInvariant();
        switch (tag)
        {
            case "b" or "strong":
                hc.boldDepth += closing ? -1 : 1;
                if (hc.boldDepth < 0) hc.boldDepth = 0;
                break;
            case "small":
                // Inline: size drops to 10pt (no compounding when nested); the line
                // structure comes from div/br only.
                hc.smallDepth += closing ? -1 : 1;
                if (hc.smallDepth < 0) hc.smallDepth = 0;
                break;
            case "div" when !closing && EngineFloatWidth(hc, m.Value) is { } floatW:
                LayoutEngineFloat(hc, m, floatW);
                break;
            case "div" or "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (!closing) CommitFloats(hc);
                AbsorbEngineBlockTag(hc, m.Value, tag, closing);
                break;
            case "table":
                EngineTableTag(hc, closing);
                break;
            case "tbody" or "thead" or "tfoot" or "tr":
                FlushLine(hc, force: false);
                break;
            case "td" or "th":
                FlushLine(hc, force: false);
                if (tag == "th") { hc.boldDepth += closing ? -1 : 1; if (hc.boldDepth < 0) hc.boldDepth = 0; }
                break;
            case "img":
                if (!closing) EngineImageAlt(hc, m.Value);
                break;
            case "em" or "i":
                if (closing) PopEngineFace(hc); else hc.faceStack.Add(ParseEngineFaceStyle(m.Value, tag, hc.baseSize));
                break;
            case "u":
                if (closing) { if (hc.spans.Count > 0) hc.spans.RemoveAt(hc.spans.Count - 1); }
                else hc.spans.Add((null, 0, true));
                break;
            case "a":
                // Inline anchor: its runs draw like their neighbours and carry the
                // href so the line can annotate them.
                if (closing) { if (hc.anchors.Count > 0) hc.anchors.Pop(); }
                else
                {
                    var href = HrefRegex.Match(m.Value);
                    hc.anchors.Push(href.Success ? href.Groups["u"].Value.Trim() : "");
                }
                break;
            case "span":
                if (closing)
                {
                    if (hc.spans.Count > 0) hc.spans.RemoveAt(hc.spans.Count - 1);
                    PopEngineFace(hc);
                }
                else
                {
                    hc.spans.Add(ParseSpanStyle(m.Value));
                    hc.faceStack.Add(ParseEngineFaceStyle(m.Value, tag, hc.baseSize));
                }
                break;
            case "br":
                FlushLine(hc, force: true);   // forced line — empty box when nothing pending
                break;
            case "ul" or "ol" or "li":
                AbsorbEngineListTag(hc, tag, closing);
                break;
        }
    }

    /// <summary>A list opens its box margin, nests its indent and numbers or bullets its items; an item seats its marker.</summary>
    private static void AbsorbEngineListTag(HtmlEngineCellState hc, string tag, bool closing)
    {
        switch (tag)
        {
            case "ul" or "ol":
                FlushLine(hc, force: false);
                if (closing)
                {
                    if (hc.lists.Count > 0) hc.lists.RemoveAt(hc.lists.Count - 1);
                    // A top-level list closes on its block margin too: the line after it
                    // opens 1.12 em lower (probed: a heading paragraph after a list seats
                    // 26.94 under the last item, the item's box plus the list margin).
                    if (hc.lists.Count == 0) hc.pendingBlockMargin = Math.Max(hc.pendingBlockMargin, EngineListMarginEm * hc.baseSize);
                }
                else
                {
                    // A top-level list opens on its own block margin — one empty
                    // line box above its first item (a nested one takes none, the
                    // UA `ol ol { margin: 0 }` reset); a list closing just before
                    // collapses its margin into that box.
                    if (hc.lists.Count == 0) { hc.pendingBlockMargin = 0; FlushLine(hc, force: true); }
                    hc.lists.Add((tag == "ol", 0));
                }
                hc.pendingMarker = null;
                hc.lineIndent = hc.lists.Count * UaListIndentPt;
                hc.curX = hc.lineIndent;
                break;
            case "li":
                FlushLine(hc, force: false);
                // A bare <li> outside any list still lays out as a list ITEM (a block
                // whose last line keeps its full box), only without a marker or indent.
                hc.inListItem = !closing;
                if (!closing && hc.lists.Count > 0)
                {
                    var top = hc.lists[^1];
                    if (top.Ordered)
                    {
                        top.Counter++;
                        hc.lists[^1] = top;
                        hc.pendingMarker = top.Counter.ToString(CultureInfo.InvariantCulture) + ".";
                    }
                    // A list nested in another takes the UA's second-level circle.
                    else hc.pendingMarker = hc.lists.Count > 1 ? EngineNestedBullet : "\u2022";
                }
                else hc.pendingMarker = null;
                hc.curX = hc.lineIndent;
                break;
        }
    }
}
