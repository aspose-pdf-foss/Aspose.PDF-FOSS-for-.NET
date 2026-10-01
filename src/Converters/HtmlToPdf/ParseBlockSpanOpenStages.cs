using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An opening span takes its own declared typography, background and line box over the block style it stands in.</summary>
    private static void OpenSpanTypography(ParseBlocksState pb, Token tok)
    {
        if (pb.tagCmp == "span" && !tok.IsSelfClosing)
        {
            pb.spanDepth++;
            // The field-list dialect: the FIRST span of a fields-box row is the label column.
            var spanHost = pb.styleStack.Peek();
            if (pb.cv?.profile.fieldListDoc == true && Environment.GetEnvironmentVariable("ASPOSE_TRACE_BLOCKS") == "1")
                Console.Error.WriteLine($"[fieldspan] host={spanHost.Tag} inFields={spanHost.InFieldsBox} childOpened={spanHost.ChildOpened} colOpen={pb.titleColSpanDepth} depth={pb.styleStack.Count} cur='{(pb.currentText.Length > 20 ? pb.currentText.ToString()[..20] : pb.currentText.ToString())}'");
            if (pb.cv?.profile.fieldListDoc == true && pb.titleColSpanDepth < 0
                && spanHost.Tag == "div" && spanHost.InFieldsBox && !spanHost.ChildOpened)
            {
                Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, spanHost);
                pb.openTitleColW = 0;
                pb.openTitleColFrac = pb.cv.profile.fieldLabelFrac;
                pb.titleColSpanDepth = pb.spanDepth;
                pb.fieldLabelOpen = true;
            }
            spanHost.ChildOpened = true;
            // UA flow: a right-floated inline span is a box of its own - the run before it
            // flushes, the span's text flushes at its close as a right-floated block (measured
            // on a wiki page: the edit links stand at the right edge beside the tagline).
            if (pb.browserUa && tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var flSt) && flSt is not null
                && Regex.IsMatch(flSt, @"(?<![-\w])float\s*:\s*right", RegexOptions.IgnoreCase))
            {
                Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                pb.blockSpanDepths.Push(pb.spanDepth);
                pb.floatRightSpanDepths.Push(pb.spanDepth);
            }
            // UA flow: a span whose class rule (or style) says `unicode-bidi: isolate` is one run
            // the line breaks around, never inside (measured on a wiki page: the h2's edit links
            // wrap before the unit). A `white-space: nowrap` span alone wraps like any other
            // (measured on the form page whose labels declare it).
            if (pb.browserUa && tok.Attributes is not null && SpanIsBidiIsolate(pb, tok))
                pb.isolateSpanOpen.Push((pb.spanDepth, pb.currentText.Length));
            if (pb.metricLayout && pb.css is not null && tok.Attributes is not null
                && tok.Attributes.TryGetValue("class", out var spCls) && spCls is not null)
                foreach (var sc in spCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (pb.css.TryGetValue("." + sc, out var spr)
                        && spr.TryGetValue("display", out var spd)
                        && spd.Trim().Equals("block", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                        pb.blockSpanDepths.Push(pb.spanDepth);
                        break;
                    }
            // Inline-block title column (quirks CSS-run docs): the span's
            // class rule declares display:inline-block with a width — its
            // text becomes its own run, closed off from what preceded it.
            if (pb.inlineBlockCols && pb.titleColSpanDepth < 0 && pb.css is not null
                && tok.Attributes is not null
                && tok.Attributes.TryGetValue("class", out var ibCls) && ibCls is not null)
                foreach (var sc in ibCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (pb.css.TryGetValue("." + sc, out var ibr)
                        && ibr.TryGetValue("display", out var ibd)
                        && ibd.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)
                        && ibr.TryGetValue("width", out var ibw)
                        && ((TryParseLength(ibw) is { } ibwPt && ibwPt > 0) || PercentFraction(ibw) > 0))
                    {
                        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                        // a percent column resolves against the content width at layout
                        pb.openTitleColW = TryParseLength(ibw) ?? 0;
                        pb.openTitleColFrac = pb.openTitleColW > 0 ? 0 : PercentFraction(ibw);
                        pb.titleColSpanDepth = pb.spanDepth;
                        // the column's own margin-bottom paces its row (the label/input
                        // form's 10px between rows)
                        if (ibr.TryGetValue("margin-bottom", out var ibMb)
                            && TryParseLength(ibMb) is { } ibMbPt && ibMbPt > 0)
                            pb.styleStack.Peek().MarginBottom = Math.Max(pb.styleStack.Peek().MarginBottom, ibMbPt);
                        break;
                    }
        }
    }
}
