using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Heading, paragraph and smart-widget element tags: each opens or closes the step line it styles.</summary>
    private static bool WalkStepWidgetTags(StepWalkState ws, string html)
    {
        if (ws.tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            FlushStepLine(ws);
            var hm = HeadingMetrics(ws.tag);
            ws.gapNext += hm.Margin;
            ws.headingPt = hm.Size;
            ws.headingLinePt = hm.Line;
            ws.pSawText = ws.pHadContent = false;
            ws.i = ws.end + 1;
            return true;
        }
        if (ws.tag == "p")
        {
            FlushStepLine(ws);
            // A paragraph is a block of its own: it carries a margin above and below
            // that collapses with its neighbour's rather than adding to it, and none
            // at all against the ends of the content column.
            if (ws.items.Count > 0 && _stepParaMarginPt is { } pMar)
                ws.gapNext = Math.Max(ws.gapNext, pMar);
            ws.inPara = true;
            ws.pSawText = ws.pHadContent = false;
            ws.i = ws.end + 1;
            return true;
        }
        if (ws.cls.Contains("sws-element", StringComparison.OrdinalIgnoreCase))
        {
            // display:block blank: the underline takes its own line
            FlushStepLine(ws);
            ws.line.Segs.Add(new StepSeg { BlankPt = WidthPt(ws) });
            FlushStepLine(ws);
            ws.i = ws.selfClosed ? ws.end + 1 : SkipElement(html, ws.i, ws.tag);
            return true;
        }
        if (ws.cls.Contains("swe-element", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swt-element", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swn-element", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swd-element", StringComparison.OrdinalIgnoreCase))
        {
            ws.line.Segs.Add(new StepSeg
            {
                BlankPt = WidthPt(ws),
                PadLeftPt = ws.cls.Contains("swe-element", StringComparison.OrdinalIgnoreCase) ? 6 : 3,
            });
            ws.pendingPad = 0;
            ws.i = ws.selfClosed ? ws.end + 1 : SkipElement(html, ws.i, ws.tag);
            return true;
        }
        if (ws.cls.Contains("swb-symbol", StringComparison.OrdinalIgnoreCase))
        {
            ws.line.Segs.Add(new StepSeg { Radio = true });
            ws.pendingPad = 0;
            ws.i = ws.selfClosed ? ws.end + 1 : SkipElement(html, ws.i, ws.tag);
            return true;
        }
        if (Regex.IsMatch(ws.cls, @"sw[a-z]+-label", RegexOptions.IgnoreCase))
        {
            ws.pendingPad = Regex.IsMatch(ws.cls, @"(^|\s)ml-0(\s|$)") ? 0
                : Regex.IsMatch(ws.cls, @"sw[bs]-label", RegexOptions.IgnoreCase) ? 6 : 3;
            ws.i = ws.end + 1;
            return true;
        }
        // A widget that places a grid labels it first, and the label travels with the
        // grid across a sheet - the data-entry and the M&TE widget both do this.
        if (Regex.IsMatch(ws.cls, @"smart-widget-(de|mte-)table", RegexOptions.IgnoreCase))
        {
            FlushStepLine(ws);
            ws.inDetable = true;
            ws.i = ws.end + 1;
            return true;
        }
        if (ws.cls.Contains("smart-widget-signature", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swes-block", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swe-block", StringComparison.OrdinalIgnoreCase))
            FlushStepLine(ws);
        return false;
    }

    /// <summary>Class-driven arms: note bands, table wraps, multiple-choice options and captions.</summary>
    private static bool WalkStepClassArms(StepWalkState ws, string html)
    {
        ws.nbm = Regex.Match(ws.cls, @"step-(note|caution|alara|warning)(?![-\w])",
            RegexOptions.IgnoreCase);
        if (!ws.isClose && ws.nbm.Success && _stepParaMarginPt is not null)
        {
            FlushStepLine(ws);
            var (boxInner, boxPast) = ExtractBalancedInnerSpanAt(html, ws.i);
            if (boxInner is not null)
            {
                var kind = ws.nbm.Groups[1].Value.ToLowerInvariant();
                ws.items.Add(new StepItem
                {
                    BoxBorderPt = kind is "caution" or "warning" ? 3.75 : 0.75,
                    BoxDouble = kind == "caution",
                    BoxPadTopPt = _stepBoxPadPt is not null
                        && _stepBoxPadPt.TryGetValue(kind, out var bp) ? bp : 0.0,
                    GapBefore = ws.gapNext,
                });
                ws.gapNext = 0;
                var boxItems = WalkStepContent(boxInner);
                if (boxItems.Count > 0 && boxItems[0].Line is { } cap)
                {
                    cap.CenterBoxPt = 0.75 * kind switch
                    {
                        "caution" => 83.0, "warning" => 88.0, "alara" => 66.0, _ => 55.0,
                    };
                    foreach (var cs in cap.Segs)
                        if (cs.Text is not null)
                        { cs.Bold = true; cs.Text = cs.Text.ToUpperInvariant(); }
                    boxItems[0].GapBefore = _stepParaMarginPt.Value;
                }
                ws.items.AddRange(boxItems);
                ws.items.Add(new StepItem { BoxEnd = true });
                ws.i = boxPast;
                return true;
            }
            ws.i = ws.end + 1;
            return true;
        }

        // every smart-widget table kind wraps its grid the same way (data entry,
        // M&TE matrix, list), so they all place through the one table path
        if (ws.cls.Contains("swdt-tablewrap", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swmt-tablewrap", StringComparison.OrdinalIgnoreCase)
            || ws.cls.Contains("swl-tablewrap", StringComparison.OrdinalIgnoreCase))
        {
            FlushStepLine(ws);
            ws.inDetable = false;
            var (inner, past) = ExtractBalancedInnerSpanAt(html, ws.i);
            if (inner is not null)
            {
                var tbl = ParseStepTable(inner, StepWrapAlign(ws.cls, ws.style));
                if (tbl is not null)
                {
                    ws.items.Add(new StepItem { Table = tbl, GapBefore = ws.gapNext + 6 });
                    ws.gapNext = 0;
                }
                ws.i = past;
                return true;
            }
            ws.i = ws.end + 1;
            return true;
        }
        // Each option of a multiple-choice widget is a block: it takes its own line
        // under the widget's label rather than running on beside it, and the widget
        // paces its lines wider than the form's own pitch (15.85 across a
        // label->option->option->option run).
        if (!ws.isClose && ws.cls.Contains("swm-option", StringComparison.OrdinalIgnoreCase))
        {
            ws.line.LinePt = SwmOptionPitch;
            FlushStepLine(ws);
            ws.line.MarginTopPt = 2.25;      // .swm-option { margin-top: 3px }
            ws.inChoice = true;
            ws.i = ws.end + 1;
            return true;
        }
        if (!ws.isClose && ws.cls.Contains("smart-widget-multiplechoice", StringComparison.OrdinalIgnoreCase))
            ws.inChoice = true;
        if (ws.cls.Contains("swdt-caption", StringComparison.OrdinalIgnoreCase))
        {
            FlushStepLine(ws);
            var (inner, past) = ExtractBalancedInnerSpanAt(html, ws.i);
            if (inner is not null)
            {
                EmitStepText(ws, inner);
                FlushStepLine(ws);
                ws.gapNext += 3.75;    // caption margin-bottom, 5 css px
                ws.i = past;
                return true;
            }
            ws.i = ws.end + 1;
            return true;
        }
        return false;
    }

    /// <summary>A closing tag settles the line it closes; a bare div or p opens a paragraph.</summary>
    private static bool WalkStepStructureTags(StepWalkState ws, string html)
    {
        if (ws.isClose)
        {
            // a block that closes ends the line it was on, so a break that follows
            // it starts - and closes - an empty one
            if (ws.tag == "div") FlushStepLine(ws);
            if (ws.tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                FlushStepLine(ws);
                ws.gapNext += HeadingMetrics(ws.tag).Margin;
                ws.headingPt = 0;
                ws.headingLinePt = 0;
            }
            if (ws.tag == "p")
            {
                FlushStepLine(ws);
                if (ws.pSawText && !ws.pHadContent)
                    ws.items.Add(new StepItem
                    {
                        Line = new StepLine
                        {
                            Segs = { new StepSeg { Text = " " } },
                            EmptyPara = true,
                            // On the linked col-full rhythm an empty paragraph is
                            // ONE line box, flush; the legacy
                            // line-box-plus-margins pricing is the narrow
                            // family's calibration.
                            LinePt = _stepParaLinePt > 0 ? _stepParaLinePt
                                : _stepRowColFull && _stepLinkedParaLinePt > 0
                                    ? _stepLinkedParaLinePt : 0,
                            ParaMarginPt = _stepParaMarginPt ?? 0,
                            BlockMargined = _stepParaMarginPt is not null
                                || _stepParaLinePt <= 0
                                    && _stepRowColFull && _stepLinkedParaLinePt > 0,
                        },
                        GapBefore = ws.gapNext,
                        KeepWithNext = ws.inDetable,
                    });
                if (ws.pSawText && !ws.pHadContent) ws.gapNext = 0;
                ws.inPara = false;
                ws.pSawText = ws.pHadContent = false;
            }
            ws.i = ws.end + 1;
            return true;
        }

        // A block that declares its own text alignment sets every line it holds that
        // way across the content column.
        // ⚠ an author's OWN block only - the form's table wraps carry text-align too and
        // must keep going through the table path
        if (!ws.isClose && ws.tag is "div" or "p" && !ws.selfClosed && ws.cls.Length == 0
            && Regex.IsMatch(ws.style, @"text-align\s*:\s*(center|right)", RegexOptions.IgnoreCase))
        {
            FlushStepLine(ws);
            var (aInner, aPast) = ExtractBalancedInnerSpanAt(html, ws.i);
            if (aInner is not null)
            {
                var al = Regex.IsMatch(ws.style, @"text-align\s*:\s*center", RegexOptions.IgnoreCase)
                    ? 1 : 2;
                var aItems = WalkStepContent(aInner);
                if (aItems.Count > 0)
                {
                    aItems[0].GapBefore = ws.gapNext;
                    ws.gapNext = 0;
                    foreach (var ai in aItems) if (ai.Line is { } al2) al2.Align = al;
                    ws.items.AddRange(aItems);
                }
                ws.i = aPast;
                return true;
            }
            ws.i = ws.end + 1;
            return true;
        }
        return false;
    }

    /// <summary>Table, line-break, image and bold tags at the cursor.</summary>
    private static bool WalkStepInlineTags(StepWalkState ws, string html)
    {
        if (ws.tag == "table" && !ws.isClose)
        {
            FlushStepLine(ws);
            ws.inDetable = false;
            var tblEnd = SkipElement(html, ws.i, "table");
            var tbl = ParseStepTable(html[ws.i..tblEnd], StepWrapAlign(ws.cls, ws.style));
            if (tbl is not null)
            {
                // the grid's own box opens where the line above it closes: the
                // spacing it carries down its own edge is all that stands between
                ws.items.Add(new StepItem { Table = tbl, GapBefore = ws.gapNext });
                ws.gapNext = 0;
            }
            ws.i = tblEnd;
            return true;
        }

        if (ws.tag == "br")
        {
            // A break always closes a line box; with nothing on the line - after a
            // block has just closed, say - it closes an empty one.
            var before = ws.items.Count;
            FlushStepLine(ws);
            if (ws.items.Count == before)
            {
                ws.items.Add(new StepItem { Line = new StepLine(), GapBefore = ws.gapNext, KeepWithNext = ws.inDetable });
                ws.gapNext = 0;
            }
            ws.i = ws.end + 1;
            return true;
        }
        if (ws.tag == "img") { ws.i = ws.end + 1; return true; }
        if (ws.tag is "b" or "strong") { ws.boldDepth += ws.isClose ? -1 : 1; ws.i = ws.end + 1; return true; }
        return false;
    }

    /// <summary>The declared CSS width of the tag at the cursor in points, 0 when it has none.</summary>
    private static double WidthPt(StepWalkState ws)
    {
        var wm = Regex.Match(ws.style, @"width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        return wm.Success
            ? double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : 0;
    }
}
