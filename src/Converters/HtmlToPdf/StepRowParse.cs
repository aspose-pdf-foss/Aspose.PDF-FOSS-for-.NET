using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Parse one step-row table into a StepRow: its number cell, content cell and acknowledge column, with the row's declared geometry.</summary>
    private static bool ParseStepRow(Match rm, string s, List<StepRow> rows)
    {
        var sp = new StepRowParseState();
        sp.rowHtml = ExtractBalancedInnerAt(s, rm.Index);
        if (sp.rowHtml is null) return true;
        sp.row = new StepRow
        {
            Clog = rm.Value.Contains("sr-step-clog", StringComparison.OrdinalIgnoreCase),
            Landscape = Regex.IsMatch(rm.Value, @"[\s'""]landscape[\s'""]", RegexOptions.IgnoreCase),
        };
        sp.bm = Regex.Match(sp.rowHtml,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*sr-bullet[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
            RegexOptions.IgnoreCase);
        if (sp.bm.Success && sp.bm.Groups[2].Value.Length > 0) sp.row.Bullet = DecodeEntities(sp.bm.Groups[2].Value);
        // a struck-through step wraps its number: <div class='slashed-dv'>35.</div> —
        // the number sits on a grey fill with a slash through it
        if (sp.row.Bullet is null)
        {
            ParseStepBullet(sp);
        }
        sp.im = Regex.Match(rm.Value, @"indent-(\d)");
        if (!sp.im.Success)
        {
            var bo = Regex.Match(sp.rowHtml,
                @"<div\b[^>]*class\s*=\s*(['""])[^'""]*sr-bullet[^'""]*\1[^>]*>", RegexOptions.IgnoreCase);
            sp.im = Regex.Match(bo.Success ? bo.Value : "", @"indent-(\d)");
        }
        if (sp.im.Success)
            sp.row.IndentPt = sp.im.Groups[1].Value switch
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
            ParseStepNumberColumn(sp, rm);
        }

        sp.content = ExtractBalancedDivInner(sp.rowHtml, "sr-content");
        if (sp.content is null) return true;
        // a form that frames its own note boxes places them through that path
        // instead of the block-per-sheet pagination the other dialect needs
        sp.row.Warn = _stepParaMarginPt is null && (Regex.IsMatch(sp.content,
            @"class\s*=\s*(['""])[^'""]*step-warning[\s'""]", RegexOptions.IgnoreCase)
            || Regex.IsMatch(sp.content, @"class\s*=\s*(['""])[^'""]*step-warning\1", RegexOptions.IgnoreCase));
        // the full-width generation's plain paragraphs take the linked sheet's
        // p line box (see _stepLinkedParaLinePt) — narrow step-col rows do not
        _stepRowColFull = rm.Value.Contains("step-col-full", StringComparison.OrdinalIgnoreCase);
        sp.row.ColFull = _stepRowColFull;
        sp.row.Items = WalkStepContent(sp.content);

        sp.ackBox = ExtractBalancedDivInner(sp.rowHtml, "sr-ack");
        if (sp.ackBox is not null)
            foreach (Match wm3 in Regex.Matches(sp.ackBox,
                @"<div\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>",
                RegexOptions.IgnoreCase))
            {
                var wIn = ExtractBalancedInnerAt(sp.ackBox, wm3.Index);
                if (wIn is null) continue;
                var labels = 0;
                foreach (Match lm3 in Regex.Matches(wIn,
                    @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                    RegexOptions.IgnoreCase))
                    if (DecodeEntities(lm3.Groups[2].Value).Trim().Length > 0) labels++;
                sp.row.AckHeightPt += wm3.Groups[2].Value.ToLowerInvariant() switch
                {
                    "signature" => 21.75 + 5.25 * labels,
                    "boolean" => 16.50 + 13.50 * labels,
                    _ => 18.75 + 5.25 * labels,
                };
            }

        sp.ack = ExtractBalancedDivInner(sp.rowHtml, "justify-content-end");
        if (sp.ack is not null && Regex.IsMatch(sp.ack,
            @"<td\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1",
            RegexOptions.IgnoreCase))
        {
            ParseStepAckControls(sp);
        }
        else if (sp.ack is not null)
        {
            ParseStepAckText(sp);
        }
        // a numbered step stands on the sheet even when it carries no content: the
        // form still gives it its number and the height its acknowledge column needs
        if (sp.row.Items.Count > 0 || sp.row.Bullet is not null) rows.Add(sp.row);
        return true;
    }

    /// <summary>A plain acknowledge cell: its text becomes the row's acknowledge label with its measured width.</summary>
    private static void ParseStepAckText(StepRowParseState sp)
    {
        foreach (Match wm2 in Regex.Matches(sp.ack!,
            @"<div\b[^>]*class\s*=\s*(['""])[^'""]*ack-(checkbox|signature|boolean)-widget[^'""]*\1[^>]*>",
            RegexOptions.IgnoreCase))
        {
            var wInner = ExtractBalancedInnerAt(sp.ack!, wm2.Index);
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
            if (w.Blanks.Count > 0) sp.row.Acks.Add(w);
        }
        sp.row.HasAck = sp.row.Acks.Count > 0;
    }

    /// <summary>The number column: the step's label and its declared width and indent.</summary>
    private static void ParseStepNumberColumn(StepRowParseState sp, Match rm)
    {
        var wide = Regex.IsMatch(rm.Value, @"[\s'""]landscape[\s'""]", RegexOptions.IgnoreCase);
        var wm = Regex.Match(rm.Value, @"indent-(\d)");
        sp.row.ContentWidthPt = 0.75 * (wm.Success
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

    /// <summary>An acknowledge cell carrying form controls: each checkbox, radio or input becomes an acknowledge item with its label.</summary>
    private static void ParseStepAckControls(StepRowParseState sp)
    {
        // The col-full form generation banks its acknowledge widgets in a
        // two-row TABLE under the content: the first row holds each widget's
        // blanks (and the labels its own cell carries), the second stacks the
        // remaining labels on a baseline all widgets share.
        sp.row.AckTable = true;
        sp.row.AckHair = sp.ack!.Contains('\u200a') || sp.ack.Contains("&#8202", StringComparison.Ordinal);
        var uiM = Regex.Match(sp.ack,
            @"<td\b[^>]*class\s*=\s*(['""])[^'""]*userinitials-wrap[^'""]*\1[^>]*>\s*([^<]+?)\s*</td>",
            RegexOptions.IgnoreCase);
        if (uiM.Success && uiM.Groups[2].Value.Trim().Length > 0)
            sp.row.AckInitials = DecodeEntities(uiM.Groups[2].Value).Trim();
        foreach (Match trm in Regex.Matches(sp.ack, @"<tr\b[^>]*>(.*?)</tr>",
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
                    if (w.Blanks.Count > 0) sp.row.Acks.Add(w);
                }
                else if (ti < sp.row.Acks.Count)
                {
                    foreach (Match lm in Regex.Matches(tdInner,
                        @"<div\b[^>]*class\s*=\s*(['""])[^'""]*a[csb]w-label[^'""]*\1[^>]*>\s*([^<]*?)\s*</div>",
                        RegexOptions.IgnoreCase))
                    {
                        var lt = DecodeEntities(lm.Groups[2].Value).Trim();
                        if (lt.Length > 0) sp.row.Acks[ti].Labels.Add(lt);
                    }
                }
                ti++;
            }
        }
        sp.row.HasAck = sp.row.Acks.Count > 0;
    }

    /// <summary>A row without a bullet cell: its number comes from the step-number span or stays empty for a continuation row.</summary>
    private static void ParseStepBullet(StepRowParseState sp)
    {
        var sm2 = Regex.Match(sp.rowHtml!,
            @"<div\b[^>]*class\s*=\s*(['""])(?<cls>[^'""]*slashed-dv[^'""]*)\1[^>]*>\s*(?<num>[^<]*?)\s*</div>",
            RegexOptions.IgnoreCase);
        if (sm2.Success && sm2.Groups["num"].Value.Length > 0)
        {
            sp.row.Bullet = DecodeEntities(sm2.Groups["num"].Value);
            sp.row.BulletSlashed = true;
            sp.row.BulletSlashWidthPt = sm2.Groups["cls"].Value
                .Contains("width-25", StringComparison.OrdinalIgnoreCase) ? 18.75 : 20.25;
        }
    }
}
