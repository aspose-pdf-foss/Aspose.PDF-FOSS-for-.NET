using System.Text;
using System.Text.RegularExpressions;
namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The form-control and block tags of a metric table: the inputs, the fieldsets and the elements that open a band of their own.</summary>
    private static void OpenMetricControlTag(MetricTableState mt, Token tok, string tag)
    {
        switch (tag)
        {
            case "input":
                OpenMetricInput(mt, tok);
                break;
            case "textarea":
                OpenMetricTextArea(mt, tok);
                break;
            case "select":
                OpenMetricSelect(mt, tok);
                break;
            case "legend":
                // A UA form cell's legend is the fieldset's label on the frame, not cell text.
                if (mt.mps.uaFormCells && mt.mps.cell is not null) mt.mps.legendCapture = new StringBuilder();
                break;
            case "center":
                // (a UA cell wrapped in <center> centres its lines)
                if (mt.stdSerif && mt.mps.cell is not null) mt.mps.cell.Align = HorizontalAlignment.Center;
                break;
            case "fieldset":
                // A <fieldset> inside a cell is CHROME, not content: the UA frames the cell
                // with a 0.75 pt #808080 box and the text inside is laid out as it would be
                // anyway. The frame is drawn at render, around the cell's own box.
                if (mt.mps.cell is not null) mt.mps.cell.HasFieldset = true;
                break;
            case "i":
            case "em":
                // …and <i>/<em> italicises it, the same whole-cell flag an
                // inline `font-style: italic` sets. Without this a cell's
                // emphasis is simply lost: the grid drew <b> and nothing else.
                if (mt.mps.cell is not null) { mt.mps.cell.Italic = true; mt.mps.italicDepth++; }
                break;
            case "font":
                OpenMetricFont(mt, tok, tag);
                break;
            case "a":
                OpenMetricAnchor(mt, tok);
                break;
            case "span":
                OpenMetricSpan(mt, tok);
                break;
            case "div":
                OpenMetricDiv(mt, tok);
                break;
            case "img":
                HandleMetricImgOpen(mt.mps, tok, mt.text, mt.css, mt.stdSerif, mt.wrapperStacks, mt.reportCells, mt.rows, mt.face, mt.boldFace, mt.fm, mt.indent, mt.loadOptions, mt.p, mt.rtl, mt.s, mt.tableHtml, mt.tablePct, mt.tableWpt, tag);
                break;
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                OpenMetricHeading(mt, tok, tag);
                break;
            case "p":
                HandleMetricParaOpen(mt.mps, tok, mt.text, mt.css, mt.stdSerif, mt.wrapperStacks, mt.reportCells, mt.rows, mt.face, mt.boldFace, mt.fm, mt.indent, mt.loadOptions, mt.p, mt.rtl, mt.s, mt.tableHtml, mt.tablePct, mt.tableWpt, tag);
                mt.mps.paraOpenTextLen = mt.text.Length;
                mt.mps.paraZeroMargin = tok.Attributes is { } pzA && pzA.TryGetValue("style", out var pzSt) && pzSt is not null
                    && Regex.IsMatch(pzSt, @"(?<![-\w])margin\s*:\s*0(?:pt|px|em|%)?(?:\s+0(?:pt|px|em|%)?){0,3}\s*(?:;|$)", RegexOptions.IgnoreCase);
                break;
            case "br":
                // a <br> inside a cell is a hard line break (the letter's
                // item list stacks its items with them)
                if (mt.mps.cell is not null)
                    (mt.mps.curSeg is not null ? mt.mps.divText : mt.text).Append('\u0001');
                // (the sized runs carry the break too: a UA cell's lines may each open in their own size)
                if (mt.mps.cell is not null && mt.mps.curSeg is null && mt.stdSerif)
                {
                    if (mt.mps.sizedSegs.Count == 0 || mt.mps.sizedSegs[^1].Fs != mt.mps.cell.FontSize)
                        mt.mps.sizedSegs.Add((new StringBuilder(), mt.mps.cell.FontSize));
                    mt.mps.sizedSegs[^1].Sb.Append('\u0001');
                }
                if (mt.mps.uaBlockCells && mt.mps.curSeg is not null) CloseUaBlockLine(mt.mps);
                break;
            case "caption":
                // (a UA grid's caption is the centred line it opens with, not cell text)
                if (mt.stdSerif && mt.mps.nestDepth == 0 && !tok.IsSelfClosing) mt.mps.captionCapture = new StringBuilder();
                break;
            case "thead":
            case "tbody":
            case "tfoot":
                if (mt.mps.nestDepth == 0)
                    mt.mps.curSection = tag == "thead" ? 0 : tag == "tfoot" ? 2 : 1;
                break;
        }
    }
}
