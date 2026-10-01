using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Positioned DTP stages: style parsing, the tag walk and the page write.</summary>
    private static void WriteDtpPages(DtpPositionedState dp)
    {
        for (var p = 0; p < dp.pageOps.Count; p++)
        {
            var page = dp.doc.Pages.Add(dp.pageW, dp.pageH);
            EnsureFonts(page, dp.fontDict);
            foreach (var op in dp.pageOps[p])
            {
                if (op is string s)
                    page.AddContentStream(Encoding.ASCII.GetBytes(s));
                else if (op is ValueTuple<byte[], double, double, double, double> im)
                {
                    try
                    {
                        var stamp = ImageStamp.FromEncodedBytes(im.Item1);
                        stamp.XIndent = im.Item2;
                        stamp.YIndent = dp.pageH - im.Item3 - im.Item5;
                        stamp.DisplayWidth = im.Item4;
                        stamp.DisplayHeight = im.Item5;
                        stamp.ApplyTo(page);
                    }
                    catch { /* undecodable image: skip */ }
                }
            }
        }
    }

    /// <summary></summary>
    private static bool WalkDtpTag(DtpPositionedState dp)
    {
        var wt = new DtpTagState();
        wt.tm = dp.tagRx.Match(dp.bodyHtml, dp.pos);
        if (!wt.tm.Success) return false;
        wt.tagEnd = dp.bodyHtml.IndexOf('>', wt.tm.Index);
        if (wt.tagEnd < 0) return false;
        wt.openTag = dp.bodyHtml[wt.tm.Index..(wt.tagEnd + 1)];
        wt.tag = wt.tm.Groups[1].Value.ToLowerInvariant();
        wt.id = DtpAttr(wt.openTag, "id");
        if (wt.id is null || !dp.idRules.TryGetValue(wt.id, out var rule))
        {
            dp.pos = wt.tagEnd + 1;
            return true;
        }
        wt.cls = DtpAttr(wt.openTag, "class");
        wt.cr = wt.cls is not null && dp.classRules.TryGetValue(wt.cls, out var c0) ? c0 : new DtpClassRule();

        if (wt.tag == "img")
        {
            dp.pos = wt.tagEnd + 1;
            DrawDtpImage(dp, wt.openTag, rule, 0, 0);
            return true;
        }

        if (wt.tag == "input")
        {
            dp.pos = wt.tagEnd + 1;
            var type = DtpAttr(wt.openTag, "type");
            if (type is not null && !type.Equals("text", StringComparison.OrdinalIgnoreCase))
                return true;
            var page = (int)Math.Floor(rule.T / dp.band);
            var yTop = rule.T - page * dp.band + DtpVertMarginPt;
            var h = rule.HasH ? rule.H : DtpInputDefaultHPt;
            var bx = DtpSideMarginPt + rule.L;
            // Chrome: 1 pt black stroke regardless of the authored border.
            OpsFor(dp, page).Add(Compat.Format(dp.inv,
                $"q 0 0 0 RG 1 w {bx + DtpInputChromeInsetPt:F2} {dp.pageH - (yTop - DtpInputChromeOutsetPt + h + 2 * DtpInputChromeOutsetPt):F2} {rule.W - 2 * DtpInputChromeInsetPt:F2} {h + 2 * DtpInputChromeOutsetPt:F2} re S Q\n"));
            var value = DtpAttr(wt.openTag, "value");
            if (string.IsNullOrEmpty(value)) return true;
            value = EdgarHtmlRenderer.DecodeEntities(value);
            var run = new DtpRun { Bold = wt.cr.Bold, SizePt = wt.cr.SizePt, Color = wt.cr.Color };
            var faceName = DtpFaceName(run);
            var w = MeasureFaceText(faceName, value, run.SizePt);
            // center is honoured; the authored right-align is NOT (the
            // itemization values render at the left inset).
            var align = rule.Align ?? wt.cr.Align;
            var x = align == "center" ? bx + (rule.W - w) / 2 : bx + DtpInputPadPt;
            var baseline = yTop + h / 2 + (run.Bold ? DtpInputSeatBoldPt : DtpInputSeatRegPt);
            DrawText(dp, page, x, baseline, run, value);
            return true;
        }

        wt.depth = 1;
        wt.scan = wt.tagEnd + 1;
        wt.innerEnd = -1;
        wt.divRx = new Regex(@"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase);
        while (wt.depth > 0)
        {
            var dm = wt.divRx.Match(dp.bodyHtml, wt.scan);
            if (!dm.Success) break;
            wt.depth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
            if (wt.depth == 0) wt.innerEnd = dm.Index;
            wt.scan = dm.Index + dm.Length;
        }
        if (wt.innerEnd < 0) { dp.pos = wt.tagEnd + 1; return false; }
        wt.inner = dp.bodyHtml[(wt.tagEnd + 1)..wt.innerEnd];
        dp.pos = wt.scan;

        // Positioned images nested inside this container carry their own
        // id rules with coordinates RELATIVE to the container's box (the
        // contract's notice GIFs live 2900+ pt down a 792 pt-high parent).
        foreach (Match nm in Regex.Matches(wt.inner, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var nid = DtpAttr(nm.Value, "id");
            if (nid is not null && dp.idRules.TryGetValue(nid, out var nRule))
                DrawDtpImage(dp, nm.Value, nRule, rule.L, rule.T);
        }

        wt.defaultAlign = rule.Align ?? wt.cr.Align ?? "left";
        wt.lines = DtpParseRichText(wt.inner, wt.cr, dp.classRules, wt.defaultAlign);
        if (wt.lines.Count == 0) return true;

        wt.pageIdx = (int)Math.Floor(rule.T / dp.band);
        wt.yIn = rule.T - wt.pageIdx * dp.band + DtpVertMarginPt;
        wt.bottom = dp.pageH - DtpVertMarginPt;
        wt.first = true;
        wt.prevMso = false;
        EmitDtpLines(dp, wt, rule);
        return true;
    }

    /// <summary></summary>
    private static bool ParseDtpStyles(DtpPositionedState dp)
    {

        dp.styleText = new StringBuilder();
        foreach (Match sm in Regex.Matches(dp.html,
                     @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
            dp.styleText.Append(sm.Groups[1].Value).Append('\n');
        dp.styles = Regex.Replace(dp.styleText.ToString(), @"/\*[\s\S]*?\*/", " ");

        dp.idRules = new Dictionary<string, DtpIdRule>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(dp.styles,
                     @"(?:div|input|img)#([\w-]+)\s*\{([^}]*)\}", RegexOptions.IgnoreCase))
        {
            var body = m.Groups[2].Value;
            if (!Regex.IsMatch(body, @"position\s*:\s*absolute", RegexOptions.IgnoreCase)) continue;
            var rule = new DtpIdRule();
            if (TryDtpPt(body, "left") is not { } left || TryDtpPt(body, "top") is not { } top
                || TryDtpPt(body, "width") is not { } width) continue;
            rule.L = left; rule.T = top; rule.W = width;
            var height = TryDtpPt(body, "height");
            rule.HasH = height is not null;
            rule.H = height ?? 0;
            var am = Regex.Match(body, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (am.Success) rule.Align = am.Groups[1].Value.ToLowerInvariant();
            dp.idRules[m.Groups[1].Value] = rule;
        }
        dp.inputRuleCount = Regex.Matches(dp.styles,
            @"input#[\w-]+\s*\{[^}]*position\s*:\s*absolute", RegexOptions.IgnoreCase).Count;
        if (dp.idRules.Count < 10 || dp.inputRuleCount == 0) return false;

        dp.classRules = new Dictionary<string, DtpClassRule>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(dp.styles, @"\.([\w-]+)\s*\{([^}]*)\}"))
        {
            var body = m.Groups[2].Value;
            var cr = new DtpClassRule();
            var fm = Regex.Match(body, @"font\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
            if (fm.Success)
            {
                var shorthand = fm.Groups[1].Value;
                cr.Bold = Regex.IsMatch(shorthand, @"\bbold\b", RegexOptions.IgnoreCase);
                cr.Italic = Regex.IsMatch(shorthand, @"\bitalic\b", RegexOptions.IgnoreCase);
                var szm = Regex.Match(shorthand, @"([\d.]+)\s*pt");
                if (szm.Success) cr.SizePt = DtpNum(szm.Groups[1].Value);
                var famM = Regex.Match(shorthand, @"pt\s+'([^']+)'|pt\s+""([^""]+)""|pt\s+([A-Za-z][\w -]*)");
                if (famM.Success)
                    cr.Family = (famM.Groups[1].Success ? famM.Groups[1].Value
                        : famM.Groups[2].Success ? famM.Groups[2].Value : famM.Groups[3].Value).Trim();
            }
            var cm2 = Regex.Match(body, @"color\s*:\s*#([0-9a-fA-F]{6})");
            if (cm2.Success) cr.Color = DtpHexColor(cm2.Groups[1].Value);
            var am = Regex.Match(body, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (am.Success) cr.Align = am.Groups[1].Value.ToLowerInvariant();
            dp.classRules[m.Groups[1].Value] = cr;
        }
        return true;
    }

    /// <summary></summary>
    private static void EmitDtpLines(DtpPositionedState dp, DtpTagState wt, DtpIdRule rule)
    {
        foreach (var ll in wt.lines)
        {
            var maxSz = 10.0;
            foreach (var r in ll.Runs) if (r.SizePt > maxSz && r.Text.Trim().Length > 0) maxSz = r.SizePt;
            var wrapped = DtpWrap(ll, rule.W);
            for (var wi = 0; wi < wrapped.Count; wi++)
            {
                double pitch;
                if (wt.first) pitch = 0;
                else if (ll.Mso && wi == 0) pitch = DtpMsoParaPitchPt;
                else if (ll.Mso) pitch = DtpMsoWrapPitchPt;
                else if (wt.prevMso && wi == 0) pitch = DtpMsoParaPitchPt;
                else pitch = DtpLineFactor * maxSz;
                var seat = DtpSeat(maxSz);
                double baseline;
                if (wt.first)
                {
                    baseline = wt.yIn + seat;
                    wt.first = false;
                }
                else baseline = wt.yIn + pitch;
                if (baseline > wt.bottom)
                {
                    wt.pageIdx++;
                    baseline = DtpVertMarginPt + seat;
                }
                wt.yIn = baseline;

                var lineRuns = wrapped[wi];
                double lineW = 0;
                foreach (var r in lineRuns) lineW += DtpMeasureRun(r);
                var indent = wi == 0 ? ll.FirstIndent : ll.HangIndent;
                var x = DtpSideMarginPt + rule.L + indent;
                if (ll.Align == "center") x = DtpSideMarginPt + rule.L + (rule.W - lineW) / 2;
                else if (ll.Align == "right") x = DtpSideMarginPt + rule.L + rule.W - lineW;
                foreach (var r in lineRuns)
                {
                    DrawText(dp, wt.pageIdx, x, wt.yIn, r, r.Text);
                    // Underline per non-space segment (link underlines
                    // gap at the spaces).
                    if (r.Under) UnderlineRunSegments(dp, wt, r, x);
                    x += DtpMeasureRun(r);
                }
            }
            wt.prevMso = ll.Mso;
        }
    }

    /// <summary>Underline one drawn run per non-space segment - a link underline gaps at the
    /// spaces - starting at the run's pen position <paramref name="x"/>.</summary>
    private static void UnderlineRunSegments(DtpPositionedState dp, DtpTagState wt, DtpRun r, double x)
    {
        var sx = x;
        var i2 = 0;
        while (i2 < r.Text.Length)
        {
            if (r.Text[i2] == ' ')
            {
                sx += MeasureFaceText(DtpFaceName(r), " ", r.SizePt);
                i2++;
                continue;
            }
            var j = i2;
            while (j < r.Text.Length && r.Text[j] != ' ') j++;
            var segW = MeasureFaceText(DtpFaceName(r), r.Text[i2..j], r.SizePt);
            StrokeLine(dp, wt.pageIdx, sx, sx + segW, wt.yIn + DtpUnderlineDropPt, r.Color);
            sx += segW;
            i2 = j;
        }
    }
}
