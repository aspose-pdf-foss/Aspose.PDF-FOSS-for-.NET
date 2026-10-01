using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Task report stages: the token parse and the stage cards.</summary>
    private static void DrawTaskStages(TaskReportState tq)
    {
        foreach (var st in tq.stages)
        {
            OpenCard(tq, isTask: false, st.Title);
            foreach (var row in st.Rows)
            {
                foreach (var col in row)
                    if (col.PillText.Length == 0 && col.Lines.Count == 1
                        && MeasureFaceText("Helvetica", col.Lines[0], TrValueFs) > TrGuidanceWrapPt)
                        col.Lines = Wrap(tq, col.Lines[0], TrGuidanceWrapPt, TrValueFs);
                EmitRow(tq, row, TrStageColX);
            }
            var lastBotRule = tq.lastValueBase + TrDetailRulePt;   // the details rule
            foreach (var (heading, tasks) in st.Sections)
            {
                if (heading.Length == 0 && tasks.Count == 0) continue;
                if (heading.Length > 0)
                {
                    HRule(tq, tq.taskL - TrHalfRulePt, tq.taskR + TrHalfRulePt, lastBotRule);
                    var hb = lastBotRule + TrRuleToH2Pt;
                    if (hb + TrH2ToTaskPt + TrBandHPt > tq.bottom) { PageBreak(tq, 0); hb = tq.top + TrWfH2Drop; }
                    TrText(tq, TrH2Fs, tq.taskL - TrHalfRulePt, hb, heading);
                    tq.y = hb + TrH2ToTaskPt;
                }
                foreach (var tk in tasks)
                {
                    OpenCard(tq, isTask: true, tk.Title);
                    foreach (var row in tk.Rows) EmitRow(tq, row, TrTaskColX);
                    var botTd = tq.lastValueBase + TrCardBotPadPt;
                    HRule(tq, tq.taskL - TrHalfRulePt, tq.taskR + TrHalfRulePt, botTd);
                    CloseCardSides(tq, isTask: true, botTd);
                    tq.taskOpen = false;
                    lastBotRule = botTd;
                    tq.y = botTd + TrCardGapPt;
                }
            }
            // the stage closes one gap under its last task
            var stageBotTd = lastBotRule + TrCardGapPt;
            HRule(tq, tq.stageL - TrHalfRulePt, tq.stageR + TrHalfRulePt, stageBotTd);
            CloseCardSides(tq, isTask: false, stageBotTd);
            tq.stageOpen = false;
            tq.y = stageBotTd + TrCardGapPt;
        }
    }

    /// <summary></summary>
    private static bool ParseTaskTokens(TaskReportState tq)
    {
        for (var i = 0; i < tq.tokens.Count; i++)
        {
            var tk = tq.tokens[i];
            var text = tk.Value;
            var end = i + 1 < tq.tokens.Count ? tq.tokens[i + 1].Index : tq.body.Length;
            if (text.StartsWith("<div class=\"s-workflow__stage\"", StringComparison.OrdinalIgnoreCase))
            {
                tq.stage = new TrCard();
                tq.stages.Add(tq.stage);
                tq.task = null;
                tq.pendingCard = tq.stage;
            }
            else if (text.StartsWith("<div class=\"s-workflow__task\"", StringComparison.OrdinalIgnoreCase))
            {
                if (tq.stage is null) return false;
                tq.task = new TrCard();
                if (tq.stage.Sections.Count == 0) tq.stage.Sections.Add(("", new List<TrCard>()));
                tq.stage.Sections[^1].Tasks.Add(tq.task);
                tq.pendingCard = tq.task;
            }
            else if (text.StartsWith("<h2", StringComparison.OrdinalIgnoreCase))
            {
                var h = Flat(tq, tk.Groups[1].Value);
                if (tq.stage is null) tq.reportHeading = h;
                else { tq.stage.Sections.Add((h, new List<TrCard>())); tq.task = null; }
            }
            else if (text.StartsWith("<h3", StringComparison.OrdinalIgnoreCase))
            {
                if (tq.pendingCard is not null) tq.pendingCard.Title = Flat(tq, tk.Groups[2].Value);
                tq.pendingCard = null;
            }
            else // a label/value row, owned by the innermost open card
            {
                var owner = tq.task ?? tq.stage;
                if (owner is null) continue;
                var seg = tq.body[tk.Index..end];
                var cols = new List<TrCol>();
                foreach (Match cm in Regex.Matches(seg,
                    "<div class=\"col-sm-(?:3|12)\">((?:(?!<div class=\"col-sm)[\\s\\S])*)",
                    RegexOptions.IgnoreCase))
                {
                    var c = cm.Groups[1].Value;
                    var col = new TrCol();
                    var lm = Regex.Match(c, "<label[^>]*>((?:(?!</label>)[\\s\\S])*)</label>",
                        RegexOptions.IgnoreCase);
                    if (!lm.Success) continue;
                    col.Label = Flat(tq, lm.Groups[1].Value).ToUpperInvariant();
                    var rest = c[(lm.Index + lm.Length)..];
                    var pm = Regex.Match(rest, "<span class=\"g-status\">((?:(?!</span>)[\\s\\S])*)</span>",
                        RegexOptions.IgnoreCase);
                    if (pm.Success)
                        col.PillText = Flat(tq, pm.Groups[1].Value).ToUpperInvariant();
                    else
                    {
                        // attachment file names arrive one <div> per line
                        var divLines = Regex.Matches(rest, "<div>((?:(?!</div>)[\\s\\S])*)</div>",
                            RegexOptions.IgnoreCase);
                        if (divLines.Count > 0)
                            foreach (Match dm in divLines)
                            {
                                var t = Flat(tq, dm.Groups[1].Value);
                                if (t.Length > 0) col.Lines.Add(t);
                            }
                        else
                        {
                            var t = Flat(tq, rest);
                            if (t.Length > 0) col.Lines.Add(t);
                        }
                    }
                    cols.Add(col);
                }
                if (cols.Count > 0) owner.Rows.Add(cols);
            }
        }
        return true;
    }
}
