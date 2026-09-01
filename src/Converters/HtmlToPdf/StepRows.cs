using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    internal static bool TryParseProcedureStepRows(string? html, out List<StepRow> rows,
        bool paraHasMargin = false, HtmlLoadOptions? options = null)
    {
        rows = new List<StepRow>();
        var s = html ?? "";
        ReadStepHeadingCss(s);
        // A form that links its stylesheet next to itself declares its p line box there
        // (16px/16px on these sheets). ONLY that is taken, and only the full-width
        // step-col-full generation consumes it: the narrow step-col family keeps its
        // fragment rhythm — headings, margins, box pads — even with the same sheet
        // on disk beside it.
        _stepLinkedParaLinePt = 0;
        if (options is not null && _stepParaLinePt <= 0)
        {
            var inlined = InlineLinkedStylesheets(s, options);
            var lpm = Regex.Match(inlined,
                @"(?<![-\w.#])p\s*\{[^}]*line-height\s*:\s*([\d.]+)\s*px[^}]*\}",
                RegexOptions.IgnoreCase);
            if (lpm.Success && double.TryParse(lpm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lpx) && lpx > 0)
                _stepLinkedParaLinePt = lpx * 0.75;
        }
        // The fragment's IsParagraphHasMargin honours the paragraph margin even when the
        // document's own p rule is out of reach behind a linked stylesheet: one line box
        // of the body's em, with the 1.12-em margin the sheet family declares. A fragment
        // that leaves the flag off keeps the flush rhythm this family is calibrated to.
        if (paraHasMargin && _stepParaMarginPt is null)
        {
            _stepParaMarginPt = 12.0 * 1.12;
            _stepParaLinePt = 12.0;
        }
        if (s.IndexOf("step-row", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("sr-content", StringComparison.OrdinalIgnoreCase) < 0
            || s.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0) return false;
        // Every table in the document must be one this dialect knows how to place: a
        // smart-widget table of any of its kinds, or an ordinary author table the step
        // walker renders through the generic grid. A table of some other shape means the
        // document is not this form after all, and the generic flow should keep it.
        var tableTags = Regex.Matches(s, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var placeable = 0;
        foreach (Match tm in tableTags)
            if (Regex.IsMatch(tm.Value, @"class\s*=\s*['""]sw(dt|mt|l)-table[\s'""]", RegexOptions.IgnoreCase)
                || !Regex.IsMatch(tm.Value, @"class\s*=", RegexOptions.IgnoreCase))
                placeable++;
        if (tableTags.Count != placeable) return false;

        foreach (Match rm in Regex.Matches(s,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*step-row[^'""]*\1[^>]*>", RegexOptions.IgnoreCase))
        {
            var rowHtml = ExtractBalancedInnerAt(s, rm.Index);
            if (rowHtml is null) continue;
            var row = new StepRow
            {
                Clog = rm.Value.Contains("sr-step-clog", StringComparison.OrdinalIgnoreCase),
                Landscape = Regex.IsMatch(rm.Value, @"[\s'""]landscape[\s'""]", RegexOptions.IgnoreCase),
            };
            var bm = Regex.Match(rowHtml,
                @"<div\b[^>]*class\s*=\s*(['""])[^'""]*sr-bullet[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                RegexOptions.IgnoreCase);
            if (bm.Success && bm.Groups[2].Value.Length > 0) row.Bullet = DecodeEntities(bm.Groups[2].Value);
            // a struck-through step wraps its number: <div class='slashed-dv'>35.</div> —
            // the number sits on a grey fill with a slash through it
            if (row.Bullet is null)
            {
                var sm2 = Regex.Match(rowHtml,
                    @"<div\b[^>]*class\s*=\s*(['""])(?<cls>[^'""]*slashed-dv[^'""]*)\1[^>]*>\s*(?<num>[^<]*?)\s*</div>",
                    RegexOptions.IgnoreCase);
                if (sm2.Success && sm2.Groups["num"].Value.Length > 0)
                {
                    row.Bullet = DecodeEntities(sm2.Groups["num"].Value);
                    row.BulletSlashed = true;
                    row.BulletSlashWidthPt = sm2.Groups["cls"].Value
                        .Contains("width-25", StringComparison.OrdinalIgnoreCase) ? 18.75 : 20.25;
                }
            }
            // the indent is declared on the row, or on the bullet column it pads out
            // (read the bullet's OPEN TAG, not its captured body — a slashed bullet's
            // body is a nested div the body regex cannot take)
            var im = Regex.Match(rm.Value, @"indent-(\d)");
            if (!im.Success)
            {
                var bo = Regex.Match(rowHtml,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*sr-bullet[^'""]*\1[^>]*>", RegexOptions.IgnoreCase);
                im = Regex.Match(bo.Success ? bo.Value : "", @"indent-(\d)");
            }
            if (im.Success)
                row.IndentPt = im.Groups[1].Value switch
                {
                    "3" => 74 * 0.75, "4" => 137.84 * 0.75, "5" => 194.48 * 0.75,
                    "6" => 243.92 * 0.75, _ => 0,
                };

            // The content column is a fixed width the form declares per row: wide
            // for a landscape row, narrower otherwise, each less the indent the row
            // carries. It runs on past the sheet's right edge and is simply clipped
            // there. A row that stacks its acknowledge under the content instead of
            // beside it declares no width and takes what the sheet leaves.
            if (!Regex.IsMatch(rm.Value, @"step-col", RegexOptions.IgnoreCase))
            {
                var wide = Regex.IsMatch(rm.Value, @"[\s'""]landscape[\s'""]", RegexOptions.IgnoreCase);
                var wm = Regex.Match(rm.Value, @"indent-(\d)");
                row.ContentWidthPt = 0.75 * (wm.Success
                    ? wm.Groups[1].Value switch
                    {
                        "3" => wide ? 655.0 : 416.0,
                        "4" => wide ? 591.16 : 352.16,
                        "5" => wide ? 534.52 : 295.52,
                        "6" => wide ? 485.08 : 246.08,
                        _ => wide ? 729.0 : 490.0,
                    }
                    : wide ? 729.0 : 490.0);
            }

            var content = ExtractBalancedDivInner(rowHtml, "sr-content");
            if (content is null) continue;
            // a form that frames its own note boxes places them through that path
            // instead of the block-per-sheet pagination the other dialect needs
            row.Warn = _stepParaMarginPt is null && (Regex.IsMatch(content,
                @"class\s*=\s*(['""])[^'""]*step-warning[\s'""]", RegexOptions.IgnoreCase)
                || Regex.IsMatch(content, @"class\s*=\s*(['""])[^'""]*step-warning\1", RegexOptions.IgnoreCase));
            // the full-width generation's plain paragraphs take the linked sheet's
            // p line box (see _stepLinkedParaLinePt) — narrow step-col rows do not
            _stepRowColFull = rm.Value.Contains("step-col-full", StringComparison.OrdinalIgnoreCase);
            row.ColFull = _stepRowColFull;
            row.Items = WalkStepContent(content);

            // The row is a flex line, so it is as tall as its tallest column - and the
            // acknowledge column is a column like any other. A bare `sr-ack` holder is
            // banked so far right that nothing it carries falls on the sheet, but it
            // still sets a floor under the row. The measured anchors: the
            // checkbox blank sits 13.87 below the widget's top, the signature's a
            // little lower, the boolean's pair of option boxes 3.00 below it and 13.50
            // tall, and each widget then stacks its own labels underneath.
            var ackBox = ExtractBalancedDivInner(rowHtml, "sr-ack");
            if (ackBox is not null)
                foreach (Match wm3 in Regex.Matches(ackBox,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>",
                    RegexOptions.IgnoreCase))
                {
                    var wIn = ExtractBalancedInnerAt(ackBox, wm3.Index);
                    if (wIn is null) continue;
                    var labels = 0;
                    foreach (Match lm3 in Regex.Matches(wIn,
                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                        RegexOptions.IgnoreCase))
                        if (DecodeEntities(lm3.Groups[2].Value).Trim().Length > 0) labels++;
                    row.AckHeightPt += wm3.Groups[2].Value.ToLowerInvariant() switch
                    {
                        "signature" => 21.75 + 5.25 * labels,
                        "boolean" => 16.50 + 13.50 * labels,
                        _ => 18.75 + 5.25 * labels,
                    };
                }

            // The acknowledge column is drawn only when the row banks it to the sheet's
            // end: a bare `sr-ack` holder carries no widget the form puts on the page.
            var ack = ExtractBalancedDivInner(rowHtml, "justify-content-end");
            if (ack is not null && Regex.IsMatch(ack,
                @"<td\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1",
                RegexOptions.IgnoreCase))
            {
                // The col-full form generation banks its acknowledge widgets in a
                // two-row TABLE under the content: the first row holds each widget's
                // blanks (and the labels its own cell carries), the second stacks the
                // remaining labels on a baseline all widgets share.
                row.AckTable = true;
                row.AckHair = ack.Contains('\u200a') || ack.Contains("&#8202", StringComparison.Ordinal);
                var uiM = Regex.Match(ack,
                    @"<td\b[^>]*class\s*=\s*(['""])[^'""]*userinitials-wrap[^'""]*\1[^>]*>\s*([^<]+?)\s*</td>",
                    RegexOptions.IgnoreCase);
                if (uiM.Success && uiM.Groups[2].Value.Trim().Length > 0)
                    row.AckInitials = DecodeEntities(uiM.Groups[2].Value).Trim();
                foreach (Match trm in Regex.Matches(ack, @"<tr\b[^>]*>(.*?)</tr>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var isLabelRow = Regex.IsMatch(trm.Value,
                        @"^<tr\b[^>]*class\s*=\s*['""][^'""]*empty", RegexOptions.IgnoreCase);
                    var ti = 0;
                    foreach (Match tdm in Regex.Matches(trm.Groups[1].Value,
                        @"<td\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>(.*?)</td>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        var tdInner = tdm.Groups[3].Value;
                        if (!isLabelRow)
                        {
                            var w = new AckWidget { Kind = tdm.Groups[2].Value.ToLowerInvariant() };
                            if (w.Kind == "boolean")
                            {
                                foreach (Match om in Regex.Matches(tdInner,
                                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-opt[\s'""][^>]*>",
                                    RegexOptions.IgnoreCase))
                                {
                                    var oInner = ExtractBalancedInnerAt(tdInner, om.Index);
                                    if (oInner is null) continue;
                                    var isBox = Regex.IsMatch(oInner,
                                        @"class\s*=\s*(['""])[^'""]*abw-blank[^'""]*box[^'""]*\1",
                                        RegexOptions.IgnoreCase);
                                    // the blank's own body is the CHECK slot, the label its own div
                                    var blankBody = Regex.Match(oInner,
                                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-blank[^'""]*\1[^>]*>(?<b>[^<]*)</div>",
                                        RegexOptions.IgnoreCase);
                                    var isCheck = blankBody.Success && Regex.IsMatch(
                                        blankBody.Groups["b"].Value, @"&check;|✓");
                                    var lblM = Regex.Match(oInner,
                                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                                        RegexOptions.IgnoreCase);
                                    var optLabel = lblM.Success
                                        ? Regex.Replace(DecodeEntities(lblM.Groups[2].Value), @"\s+", " ").Trim()
                                        : null;
                                    w.Blanks.Add((49.95, isBox,
                                        optLabel is { Length: > 0 } ? optLabel : null, isCheck));
                                }
                            }
                            else
                            {
                                var cbBody = Regex.Match(tdInner,
                                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[cs]w-blank[^'""]*\1[^>]*>(?<b>[^<]*)</div>",
                                    RegexOptions.IgnoreCase);
                                w.Blanks.Add((104.25, false, null,
                                    cbBody.Success && Regex.IsMatch(cbBody.Groups["b"].Value, @"&check;|✓")));
                                foreach (Match lm in Regex.Matches(tdInner,
                                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[cs]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                                    RegexOptions.IgnoreCase))
                                {
                                    var lt = DecodeEntities(lm.Groups[2].Value).Trim();
                                    if (lt.Length > 0) w.TopLabels.Add(lt);
                                }
                            }
                            if (w.Blanks.Count > 0) row.Acks.Add(w);
                        }
                        else if (ti < row.Acks.Count)
                        {
                            foreach (Match lm in Regex.Matches(tdInner,
                                @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                                RegexOptions.IgnoreCase))
                            {
                                var lt = DecodeEntities(lm.Groups[2].Value).Trim();
                                if (lt.Length > 0) row.Acks[ti].Labels.Add(lt);
                            }
                        }
                        ti++;
                    }
                }
                row.HasAck = row.Acks.Count > 0;
            }
            else if (ack is not null)
            {
                foreach (Match wm2 in Regex.Matches(ack,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>",
                    RegexOptions.IgnoreCase))
                {
                    var wInner = ExtractBalancedInnerAt(ack, wm2.Index);
                    if (wInner is null) continue;
                    var w = new AckWidget();
                    var kind = wm2.Groups[2].Value.ToLowerInvariant();
                    w.Kind = kind;
                    // the generator writes a hair space before a checkbox blank; it is
                    // a real line box above the blank and deepens the widget's stack
                    w.Hair = Regex.IsMatch(wInner,
                        @"(?: |&#8202;?)\s*<div\b[^>]*acw-blank", RegexOptions.IgnoreCase);
                    if (kind == "boolean")
                    {
                        foreach (Match om in Regex.Matches(wInner,
                            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*abw-opt[\s'""][^>]*>",
                            RegexOptions.IgnoreCase))
                        {
                            var oInner = ExtractBalancedInnerAt(wInner, om.Index);
                            if (oInner is null) continue;
                            var isBox = Regex.IsMatch(oInner,
                                @"class\s*=\s*(['""])[^'""]*abw-blank[^'""]*box[^'""]*\1", RegexOptions.IgnoreCase);
                            var optLabel = Regex.Replace(
                                DecodeEntities(HtmlFragment.StripHtmlTags(oInner)), @"\s+", " ").Trim();
                            w.Blanks.Add((isBox ? 50.2 : 49.0, isBox, optLabel.Length > 0 ? optLabel : null, false));
                        }
                    }
                    else
                    {
                        w.Blanks.Add((kind == "checkbox" ? 104.64 : 104.0, false, null, false));
                    }
                    foreach (Match lm2 in Regex.Matches(wInner,
                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                        RegexOptions.IgnoreCase))
                        w.Labels.Add(DecodeEntities(lm2.Groups[2].Value).Trim());
                    if (w.Blanks.Count > 0) row.Acks.Add(w);
                }
                row.HasAck = row.Acks.Count > 0;
            }
            // a numbered step stands on the sheet even when it carries no content: the
            // form still gives it its number and the height its acknowledge column needs
            if (row.Items.Count > 0 || row.Bullet is not null) rows.Add(row);
        }
        return rows.Count > 0;
    }

    /// <summary>Linear walk of step-content HTML into flowed items. Data-entry table
    /// cells recurse through the same walk, keeping only their line items.</summary>
    private static List<StepItem> WalkStepContent(string html)
    {
        var items = new List<StepItem>();
        var line = new StepLine();
        var boldDepth = 0;
        var inDetable = false;
        var pSawText = false;
        var pHadContent = false;
        double pendingPad = 0, gapNext = 0, headingPt = 0, headingLinePt = 0;
        var inChoice = false;
        var inPara = false;

        void Flush()
        {
            while (line.Segs.Count > 0 && line.Segs[^1].Text is { } t && t.Trim().Length == 0)
                line.Segs.RemoveAt(line.Segs.Count - 1);
            line.FontPt = headingPt;
            if (headingLinePt > 0) line.LinePt = headingLinePt;
            else if (inChoice && line.LinePt <= 0) line.LinePt = SwmOptionPitch;
            // a paragraph sets on the line box the document declares for it, grown to the
            // blank's own box where the line carries one
            else if (inPara && line.LinePt <= 0 && _stepParaLinePt > 0)
            {
                line.LinePt = _stepParaLinePt;
                // a blank is seated ON the baseline, so the box it needs is added under
                // the line rather than shared around it
                if (line.Segs.Exists(sg => sg.BlankPt > 0) && SwElementLinePt > _stepParaLinePt)
                {
                    line.AscentLinePt = _stepParaLinePt;
                    line.LinePt = SwElementLinePt;
                }
            }
            // …and a full-width row's paragraph takes the LINKED sheet's p line box
            // when the document's own styles declare none (see _stepLinkedParaLinePt)
            else if (inPara && line.LinePt <= 0 && _stepRowColFull && _stepLinkedParaLinePt > 0)
            {
                line.LinePt = _stepLinkedParaLinePt;
                if (line.Segs.Exists(sg => sg.BlankPt > 0) && SwElementLinePt > _stepLinkedParaLinePt)
                {
                    line.AscentLinePt = _stepLinkedParaLinePt;
                    line.LinePt = SwElementLinePt;
                }
            }
            if (line.Segs.Count > 0)
            {
                // a label that ends the line with nothing in it draws nothing and still
                // asks for its margin
                line.TrailPadPt = pendingPad;
                items.Add(new StepItem { Line = line, GapBefore = gapNext, KeepWithNext = inDetable });
                gapNext = 0;
            }
            line = new StepLine();
        }

        void EmitText(string raw)
        {
            var text = Regex.Replace(DecodeEntities(raw).Replace(' ', ' '), @"\s+", " ");
            foreach (var piece in Regex.Split(text, "([⃝☐◯⬤])"))
            {
                if (piece.Length == 0) continue;
                if (piece is "⃝" or "◯")
                { line.Segs.Add(new StepSeg { Radio = true }); pendingPad = 0; continue; }
                // the form face has no BLACK LARGE CIRCLE: the selected option's
                // glyph renders as the missing-glyph box, same ink as the checkbox
                if (piece is "☐" or "⬤")
                { line.Segs.Add(new StepSeg { Checkbox = true }); pendingPad = 0; continue; }
                if (piece.Trim().Length == 0)
                {
                    pSawText = true;
                    if (line.Segs.Count > 0 && !(line.Segs[^1].Text is { } pt && pt.EndsWith(' ')))
                        line.Segs.Add(new StepSeg { Text = " " });
                    continue;
                }
                line.Segs.Add(new StepSeg { Text = piece, Bold = boldDepth > 0, PadLeftPt = pendingPad });
                pendingPad = 0;
                pHadContent = true;
            }
        }

        var i = 0;
        var n = html.Length;
        while (i < n)
        {
            if (html[i] != '<')
            {
                var j = html.IndexOf('<', i);
                if (j < 0) j = n;
                EmitText(html[i..j]);
                i = j;
                continue;
            }
            var end = html.IndexOf('>', i);
            if (end < 0) break;
            var tagStr = html[i..(end + 1)];
            var nm = Regex.Match(tagStr, @"^</?\s*([A-Za-z][A-Za-z0-9]*)");
            if (!nm.Success) { i = end + 1; continue; }
            var tag = nm.Groups[1].Value.ToLowerInvariant();
            var isClose = tagStr[1] == '/';
            var selfClosed = tagStr.EndsWith("/>", StringComparison.Ordinal);
            var cls = Regex.Match(tagStr, @"class\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;
            var style = Regex.Match(tagStr, @"style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;

            // An author's own table in the step content — no widget wrapper around it —
            // still lays out as a grid rather than dissolving into the line stream.
            if (tag == "table" && !isClose)
            {
                Flush();
                inDetable = false;
                var tblEnd = SkipElement(html, i, "table");
                var tbl = ParseStepTable(html[i..tblEnd], StepWrapAlign(cls, style));
                if (tbl is not null)
                {
                    // the grid's own box opens where the line above it closes: the
                    // spacing it carries down its own edge is all that stands between
                    items.Add(new StepItem { Table = tbl, GapBefore = gapNext });
                    gapNext = 0;
                }
                i = tblEnd;
                continue;
            }

            double WidthPt()
            {
                var wm = Regex.Match(style, @"width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                return wm.Success
                    ? double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                    : 0;
            }

            if (tag == "br")
            {
                // A break always closes a line box; with nothing on the line - after a
                // block has just closed, say - it closes an empty one.
                var before = items.Count;
                Flush();
                if (items.Count == before)
                {
                    items.Add(new StepItem { Line = new StepLine(), GapBefore = gapNext, KeepWithNext = inDetable });
                    gapNext = 0;
                }
                i = end + 1;
                continue;
            }
            if (tag == "img") { i = end + 1; continue; }
            if (tag is "b" or "strong") { boldDepth += isClose ? -1 : 1; i = end + 1; continue; }
            if (isClose)
            {
                // a block that closes ends the line it was on, so a break that follows
                // it starts - and closes - an empty one
                if (tag == "div") Flush();
                if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                {
                    Flush();
                    gapNext += HeadingMetrics(tag).Margin;
                    headingPt = 0;
                    headingLinePt = 0;
                }
                if (tag == "p")
                {
                    Flush();
                    if (pSawText && !pHadContent)
                        items.Add(new StepItem
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
                            GapBefore = gapNext,
                            KeepWithNext = inDetable,
                        });
                    if (pSawText && !pHadContent) gapNext = 0;
                    inPara = false;
                    pSawText = pHadContent = false;
                }
                i = end + 1;
                continue;
            }

            // A block that declares its own text alignment sets every line it holds that
            // way across the content column.
            // ⚠ an author's OWN block only - the form's table wraps carry text-align too and
            // must keep going through the table path
            if (!isClose && tag is "div" or "p" && !selfClosed && cls.Length == 0
                && Regex.IsMatch(style, @"text-align\s*:\s*(center|right)", RegexOptions.IgnoreCase))
            {
                Flush();
                var aInner = ExtractBalancedInnerAt(html, i, out var aPast);
                if (aInner is not null)
                {
                    var al = Regex.IsMatch(style, @"text-align\s*:\s*center", RegexOptions.IgnoreCase)
                        ? 1 : 2;
                    var aItems = WalkStepContent(aInner);
                    if (aItems.Count > 0)
                    {
                        aItems[0].GapBefore = gapNext;
                        gapNext = 0;
                        foreach (var ai in aItems) if (ai.Line is { } al2) al2.Align = al;
                        items.AddRange(aItems);
                    }
                    i = aPast;
                    continue;
                }
                i = end + 1;
                continue;
            }

            // A note, caution, ALARA or warning box: a framed block the form rules in its
            // own border width, holding a centred caption over its text. The caption sits
            // in a box of its own declared width, centred in the content column, and the
            // frame stands at least 80 css px tall.
            var nbm = Regex.Match(cls, @"step-(note|caution|alara|warning)(?![-\w])",
                RegexOptions.IgnoreCase);
            if (!isClose && nbm.Success && _stepParaMarginPt is not null)
            {
                Flush();
                var boxInner = ExtractBalancedInnerAt(html, i, out var boxPast);
                if (boxInner is not null)
                {
                    var kind = nbm.Groups[1].Value.ToLowerInvariant();
                    items.Add(new StepItem
                    {
                        BoxBorderPt = kind is "caution" or "warning" ? 3.75 : 0.75,
                        BoxDouble = kind == "caution",
                        BoxPadTopPt = _stepBoxPadPt is not null
                            && _stepBoxPadPt.TryGetValue(kind, out var bp) ? bp : 0.0,
                        GapBefore = gapNext,
                    });
                    gapNext = 0;
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
                    items.AddRange(boxItems);
                    items.Add(new StepItem { BoxEnd = true });
                    i = boxPast;
                    continue;
                }
                i = end + 1;
                continue;
            }

            // every smart-widget table kind wraps its grid the same way (data entry,
            // M&TE matrix, list), so they all place through the one table path
            if (cls.Contains("swdt-tablewrap", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swmt-tablewrap", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swl-tablewrap", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                inDetable = false;
                var inner = ExtractBalancedInnerAt(html, i, out var past);
                if (inner is not null)
                {
                    var tbl = ParseStepTable(inner, StepWrapAlign(cls, style));
                    if (tbl is not null)
                    {
                        items.Add(new StepItem { Table = tbl, GapBefore = gapNext + 6 });
                        gapNext = 0;
                    }
                    i = past;
                    continue;
                }
                i = end + 1;
                continue;
            }
            // Each option of a multiple-choice widget is a block: it takes its own line
            // under the widget's label rather than running on beside it, and the widget
            // paces its lines wider than the form's own pitch (15.85 across a
            // label->option->option->option run).
            if (!isClose && cls.Contains("swm-option", StringComparison.OrdinalIgnoreCase))
            {
                line.LinePt = SwmOptionPitch;
                Flush();
                line.MarginTopPt = 2.25;      // .swm-option { margin-top: 3px }
                inChoice = true;
                i = end + 1;
                continue;
            }
            if (!isClose && cls.Contains("smart-widget-multiplechoice", StringComparison.OrdinalIgnoreCase))
                inChoice = true;
            if (cls.Contains("swdt-caption", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var inner = ExtractBalancedInnerAt(html, i, out var past);
                if (inner is not null)
                {
                    EmitText(inner);
                    Flush();
                    gapNext += 3.75;    // caption margin-bottom, 5 css px
                    i = past;
                    continue;
                }
                i = end + 1;
                continue;
            }
            // a heading is a block of its own, like a paragraph: it ends the line the
            // content before it was on, then sets at its own size with its own margins
            if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                Flush();
                var hm = HeadingMetrics(tag);
                gapNext += hm.Margin;
                headingPt = hm.Size;
                headingLinePt = hm.Line;
                pSawText = pHadContent = false;
                i = end + 1;
                continue;
            }
            if (tag == "p")
            {
                Flush();
                // A paragraph is a block of its own: it carries a margin above and below
                // that collapses with its neighbour's rather than adding to it, and none
                // at all against the ends of the content column.
                if (items.Count > 0 && _stepParaMarginPt is { } pMar)
                    gapNext = Math.Max(gapNext, pMar);
                inPara = true;
                pSawText = pHadContent = false;
                i = end + 1;
                continue;
            }
            if (cls.Contains("sws-element", StringComparison.OrdinalIgnoreCase))
            {
                // display:block blank: the underline takes its own line
                Flush();
                line.Segs.Add(new StepSeg { BlankPt = WidthPt() });
                Flush();
                i = selfClosed ? end + 1 : SkipElement(html, i, tag);
                continue;
            }
            if (cls.Contains("swe-element", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swt-element", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swn-element", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swd-element", StringComparison.OrdinalIgnoreCase))
            {
                line.Segs.Add(new StepSeg
                {
                    BlankPt = WidthPt(),
                    PadLeftPt = cls.Contains("swe-element", StringComparison.OrdinalIgnoreCase) ? 6 : 3,
                });
                pendingPad = 0;
                i = selfClosed ? end + 1 : SkipElement(html, i, tag);
                continue;
            }
            if (cls.Contains("swb-symbol", StringComparison.OrdinalIgnoreCase))
            {
                line.Segs.Add(new StepSeg { Radio = true });
                pendingPad = 0;
                i = selfClosed ? end + 1 : SkipElement(html, i, tag);
                continue;
            }
            if (Regex.IsMatch(cls, @"sw[a-z]+-label", RegexOptions.IgnoreCase))
            {
                pendingPad = Regex.IsMatch(cls, @"(^|\s)ml-0(\s|$)") ? 0
                    : Regex.IsMatch(cls, @"sw[bs]-label", RegexOptions.IgnoreCase) ? 6 : 3;
                i = end + 1;
                continue;
            }
            // A widget that places a grid labels it first, and the label travels with the
            // grid across a sheet - the data-entry and the M&TE widget both do this.
            if (Regex.IsMatch(cls, @"smart-widget-(de|mte-)table", RegexOptions.IgnoreCase))
            {
                Flush();
                inDetable = true;
                i = end + 1;
                continue;
            }
            if (cls.Contains("smart-widget-signature", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swes-block", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("swe-block", StringComparison.OrdinalIgnoreCase))
                Flush();
            i = end + 1;
        }
        Flush();
        return items;
    }

    /// <summary>The line a multiple-choice widget paces its label and options on -
    /// wider than the form's own pitch. Measured across six consecutive gaps of a
    /// label->option->option->option run.</summary>
    internal const double SwmOptionPitch = 15.85;

    /// <summary>The layout side reads the parsed document's paragraph margin back when it
    /// seats the acknowledge table under the content.</summary>
    internal static double? StepParaMargin => _stepParaMarginPt;

    /// <summary>Every fill-in blank the form draws is an inline-block 15 css px tall with a
    /// 3 px margin under it, so a paragraph carrying one sets on an 18 px line box.</summary>
    private const double SwElementLinePt = 18 * 0.75;

    /// <summary>Read <c>hN { font-size: Npx; line-height: Npx }</c> out of the document's
    /// style blocks. A form that sizes its headings this way also resets their margin
    /// (<c>h1,…,h6 { margin: 0 }</c>) and their weight, so headings set at their declared
    /// size, on their declared line, with nothing above or below.</summary>
    private static void ReadStepHeadingCss(string html)
    {
        _stepHeadingCss = null;
        _stepBoxPadPt = null;
        foreach (Match bm in Regex.Matches(html,
            @"\.step-(note|caution|warning|alara)\b[^{]*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase))
        {
            var pt = Regex.Match(bm.Groups["body"].Value,
                @"padding-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (pt.Success)
                (_stepBoxPadPt ??= new())[bm.Groups[1].Value.ToLowerInvariant()] =
                    0.75 * double.Parse(pt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        Dictionary<string, (double, double, double)>? map = null;
        foreach (Match m in Regex.Matches(html,
            @"(?<tag>h[1-6])\s*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase))
        {
            var body = m.Groups["body"].Value;
            var fs = Regex.Match(body, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (!fs.Success) continue;
            var lh = Regex.Match(body, @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            var size = double.Parse(fs.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            var line = lh.Success
                ? double.Parse(lh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : size;
            (map ??= new())[m.Groups["tag"].Value.ToLowerInvariant()] = (size, line, 0.0);
        }
        if (map is not null) _stepHeadingCss = map;

        // A paragraph's margin is one em of the size the document gives it. A form that
        // declares nothing for `p` is left alone: those documents lay flush, and the
        // no-`p`-rule family is calibrated that way.
        _stepParaMarginPt = null;
        _stepParaLinePt = 0;
        var pm = Regex.Match(html,
            @"(?<![-\w])p\s*\{(?<body>[^}]*font-size\s*:\s*(?<px>[\d.]+)\s*px[^}]*)\}",
            RegexOptions.IgnoreCase);
        if (pm.Success && double.TryParse(pm.Groups["px"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pxv) && pxv > 0)
        {
            var paraPt = pxv * 0.75;
            _stepParaMarginPt = paraPt * 1.12;
            var plh = Regex.Match(pm.Groups["body"].Value,
                @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            _stepParaLinePt = plh.Success
                ? double.Parse(plh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : paraPt;
        }
    }

    /// <summary>Parse a data-entry <c>swdt-table</c>: fixed th widths, header texts,
    /// and td cells re-walked into line stacks.</summary>
    /// <summary>Where a table wrap seats its grid in the content column. The form says so
    /// either in the wrap's own class (<c>swmt-tablewrap right</c>) or inline
    /// (<c>style='text-align:right;'</c>); both spellings appear in one document.</summary>
    private static int StepWrapAlign(string cls, string style)
    {
        var m = Regex.Match(style, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(cls, @"(?:^|\s)(left|center|right)(?:\s|$)", RegexOptions.IgnoreCase);
        return m.Success
            ? m.Groups[1].Value.ToLowerInvariant() switch { "center" => 1, "right" => 2, _ => 0 }
            : 0;
    }

    private static StepTable? ParseStepTable(string wrapHtml, int align)
    {
        var tm = Regex.Match(wrapHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tm.Success) return null;
        var t = new StepTable { Align = align };
        // An author's own table sets at the size the form gives its tables — a step
        // below the body — but a table whose cells carry the form's own widgets keeps
        // the widgets' size, because it is their label runs that set the text.
        if (!Regex.IsMatch(tm.Value, @"class\s*=\s*['""]sw", RegexOptions.IgnoreCase))
        {
            t.FormRhythm = true;
            if (wrapHtml.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0)
                t.CellFontPt = 10.5;
        }
        var csm = Regex.Match(tm.Value, @"cellspacing\s*=\s*[""']?([\d.]+)",
            RegexOptions.IgnoreCase);
        if (csm.Success)
            t.CellSpacingPt = double.Parse(csm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        // ⚠ the declared width, NOT the max-width that may sit in front of it in the
        // same style - an unbounded `width` matches inside `max-width` and the grid then
        // fills the whole column
        var wm = Regex.Match(tm.Value, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        if (wm.Success)
        {
            t.WidthPt = double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75;
            t.WidthDeclared = true;
        }

        foreach (Match trm in Regex.Matches(wrapHtml, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var tr = trm.Groups[1].Value;
            var ths = Regex.Matches(tr, @"<th\b([^>]*)>([\s\S]*?)</th\s*>", RegexOptions.IgnoreCase);
            if (ths.Count > 0 && t.Header.Count == 0)
            {
                foreach (Match th in ths)
                {
                    var wa = Regex.Match(th.Groups[1].Value, @"width\s*=\s*['""]?([\d.]+)px", RegexOptions.IgnoreCase);
                    t.ColPts.Add(wa.Success
                        ? double.Parse(wa.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                        : 48.75);
                    t.Header.Add(Regex.Replace(
                        DecodeEntities(HtmlFragment.StripHtmlTags(th.Groups[2].Value)), @"\s+", " ").Trim());
                }
                continue;
            }
            var tds = Regex.Matches(tr, @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (tds.Count == 0) continue;
            // A table that heads its columns with plain cells rather than <th> declares
            // the grid on its first row: take the widths from there.
            if (t.ColPts.Count == 0 && t.Header.Count == 0)
                foreach (Match td in tds)
                {
                    var cw = Regex.Match(td.Groups[1].Value,
                        @"width\s*[:=]\s*['""]?\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                    t.ColPts.Add(cw.Success
                        ? double.Parse(cw.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                        : 48.75);
                }
            var rowCells = new List<List<StepLine>>();
            var rowBg = new List<Color?>();
            foreach (Match td in tds)
            {
                var cellLines = new List<StepLine>();
                foreach (var it in WalkStepContent(td.Groups[2].Value))
                    if (it.Line is not null) cellLines.Add(it.Line);
                rowCells.Add(cellLines);
                var bgm = Regex.Match(td.Groups[1].Value,
                    @"background(?:-color)?\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
                rowBg.Add(bgm.Success ? ParseCssColor(bgm.Groups[1].Value) : null);
            }
            t.Rows.Add(rowCells);
            t.RowBg.Add(rowBg);
            // the row is at least as tall as the tallest min-height its cells declare
            var minH = 0.0;
            foreach (Match mh in Regex.Matches(tr,
                @"min-height\s*:\s*([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase))
                if (double.TryParse(mh.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var mhv))
                    minH = Math.Max(minH, mh.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                        ? mhv * 0.75 : mhv);
            t.RowMinPt.Add(minH);
        }
        if (t.ColPts.Count == 0 || t.Rows.Count == 0) return null;
        if (t.WidthPt <= 0) foreach (var c in t.ColPts) t.WidthPt += c;
        return t;
    }

    /// <summary>Index just past the matching close of the element opening at
    /// <paramref name="openIdx"/> (same-tag nesting honored).</summary>
    private static int SkipElement(string html, int openIdx, string tag)
    {
        var d = 0;
        foreach (Match m in Regex.Matches(html[openIdx..], @"<(/?)" + tag + @"\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (m.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                if (d == 0) return openIdx + m.Index + m.Length;
                continue;
            }
            d += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (d == 0) return openIdx + m.Index + m.Length;
        }
        return html.Length;
    }

    /// <summary>The inner HTML of the first <c>&lt;div&gt;</c> whose class contains
    /// <paramref name="classToken"/>, honoring nested div nesting.</summary>
    private static string? ExtractBalancedDivInner(string html, string classToken)
    {
        var open = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*" + Regex.Escape(classToken) + @"[^'""]*\1[^>]*>",
            RegexOptions.IgnoreCase);
        return open.Success ? ExtractBalancedInnerAt(html, open.Index) : null;
    }

    /// <summary>Balanced inner HTML of the div opening at <paramref name="openIdx"/>;
    /// the overload with <paramref name="pastEnd"/> also reports the index just past
    /// the close tag.</summary>
    private static string? ExtractBalancedInnerAt(string html, int openIdx)
        => ExtractBalancedInnerAt(html, openIdx, out _);

    private static string? ExtractBalancedInnerAt(string html, int openIdx, out int pastEnd)
    {
        pastEnd = html.Length;
        var open = Regex.Match(html[openIdx..], @"^<div\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return null;
        var i = openIdx + open.Length;
        var d = 1;
        foreach (Match t in Regex.Matches(html[i..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            d += t.Groups[1].Value.Length > 0 ? -1 : 1;
            if (d == 0)
            {
                pastEnd = i + t.Index + t.Length;
                return html.Substring(i, t.Index);
            }
        }
        return null;
    }

    /// <summary>Resolve knockout <c>data-bind="text: name"</c> spans against observable
    /// literals declared in the document's own scripts (<c>name = ko.observable('…')</c>,
    /// applied via <c>ko.applyBindings</c>): the bound span renders its observable's text.
    /// The enclosing heading splits at the span so the bound text keeps its own DOM-node
    /// run (and takes the browser heading size), matching how a scripted engine draws
    /// it. HTML without both binding halves passes through untouched.</summary>
    internal static string ApplyKnockoutTextBindings(string html)
    {
        if (string.IsNullOrEmpty(html)
            || html.IndexOf("data-bind", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("ko.observable", StringComparison.Ordinal) < 0
            || !Regex.IsMatch(html, @"ko\.applyBindings\s*\(")) return html ?? "";

        var lits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match sm in Regex.Matches(html, @"<script\b[^>]*>([\s\S]*?)</script\s*>",
                     RegexOptions.IgnoreCase))
            foreach (Match om in Regex.Matches(sm.Groups[1].Value,
                         @"(?:this\s*\.\s*)?(\w+)\s*=\s*ko\.observable\(\s*(['""])(.*?)\2\s*\)"))
                lits[om.Groups[1].Value] = om.Groups[3].Value;
        if (lits.Count == 0) return html;

        return Regex.Replace(html,
            @"<(h[1-6])([^>]*)>((?:(?!</\1>|<span)[\s\S])*?)<span[^>]*data-bind\s*=\s*(['""])\s*text\s*:\s*(\w+)\s*\4[^>]*>[\s\S]*?</span>\s*</\1>",
            m =>
            {
                if (!lits.TryGetValue(m.Groups[5].Value, out var lit)) return m.Value;
                // The browser heading size (h1 = 2 em of the 12 pt UA base, h2 = 1.5 em, …)
                // applies to both halves, so the bound run wraps where the scripted
                // engine's does.
                var uaPt = m.Groups[1].Value.ToLowerInvariant() switch
                {
                    "h1" => 24, "h2" => 18, "h3" => 14, "h4" => 12, "h5" => 10, _ => 9,
                };
                var open = $"<{m.Groups[1].Value} style=\"font-size:{uaPt}pt\"{m.Groups[2].Value}>";
                return $"{open}{m.Groups[3].Value}</{m.Groups[1].Value}>{open}{lit}</{m.Groups[1].Value}>";
            }, RegexOptions.IgnoreCase);
    }
}
