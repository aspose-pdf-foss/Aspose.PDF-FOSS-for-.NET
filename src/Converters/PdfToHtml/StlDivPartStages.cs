using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
// Closes the open span of the part emitter.
    private static void Close(StlDivState sd)
    {
        if (sd.cur is { Count: > 0 })
            sd.spans.Add((sd.cur, sd.curStyle, false, sd.pendingInheritWs));
        sd.cur = null;
        sd.foldedSlots.Clear();
        sd.pendingInheritWs = null;
    }

    /// <summary>One span of the solved part: its text, letter-spacing and word-spacing values, its face and weight classes, its link and its closing tag.</summary>
    private static void EmitStlSpan(StlDivState sd, int s)
    {
        var (its, styleIdx, isNbsp, inheritWs) = sd.spans[s];
        sd.st = sd.styles[styleIdx];
        sd.fs = Math.Max(0.01, sd.st.FontSize);
        sd.fontNum = sd.styleReg.Font(sd.st.CssFamily,
            sd.emGrid
                ? Math.Round(sd.st.FontSize / 12.0, 2, MidpointRounding.AwayFromZero)
                : sd.st.FontSize / 12.0,
            sd.st.CssColor, null,
            sd.st.UseFallbackMetrics ? "Times New Roman" : null);
        sd.lhNum = sd.styleReg.LineHeight(sd.st.LineHeightEm > 0 ? Math.Round(sd.st.LineHeightEm, 6) : 1.2);

        sd.lsMilli = 0;
        sd.wsEm = null;
        if (isNbsp)
        {
            sd.text = "&nbsp;";
            sd.wsEm = Math.Round(its[0].E / 1000.0, 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            ResolveSpanSpacing(sd, its, inheritWs, s);
        }

        sd.emVal = Math.Round(sd.lsMilli / 1000.0, 4, MidpointRounding.AwayFromZero);
        sd.pxVal = Math.Round(sd.lsMilli * sd.fs * 4.0 / 3.0 / 1000.0, 4, MidpointRounding.AwayFromZero);
        sd.lsNum = sd.styleReg.LetterSpacingExact(sd.emVal, sd.pxVal);
        sd.lastFontNum = sd.fontNum; sd.lastLhNum = sd.lhNum; sd.lastLsNum = sd.lsNum;

        sd.weightCss = StlWeightStyleCss(sd.st.FauxBold, sd.st.FontStyle);
        sd.wsCss = sd.wsEm is { } w
            ? $"word-spacing:{w.ToString("0.####", CultureInfo.InvariantCulture)}em;"
            : "";
        sd.wsAttr = sd.weightCss.Length + sd.wsCss.Length > 0
            ? $" style=\"{sd.weightCss}{sd.wsCss}\""
            : "";
        sd.spanLink = sd.popupItems is null && sd.linkFor is not null
            ? sd.linkFor(its[0].StartX, its[^1].StartX)
            : null;
        if (sd.spanLink is not null)
        {
            sd.sb.Append($"<a href=\"{EscapeHtml(sd.spanLink.Uri)}\"" +
                (sd.spanLink.Uri.StartsWith('#') ? ">" : " target=\"_blank\">"));
            sd.spanLink.Wrapped = true;
        }
        sd.sb.Append($"<span class=\"{sd.classNamer.Attr(sd.fontNum, sd.lhNum, sd.lsNum)}\"{sd.wsAttr}>");
        sd.sb.Append(sd.text);
        if (s == sd.spans.Count - 1 && sd.renderedChars > 1 && sd.popupItems is null)
            sd.sb.Append(" &nbsp;");
        sd.sb.Append("</span>");
        if (sd.spanLink is not null) sd.sb.Append("</a>");
    }

    /// <summary>One item joins the current span or opens a new one when its style, cut or slot state changes.</summary>
    private static void CollectStlItemRun(StlDivState sd, int k)
    {
        var it = sd.partItems[k];
        if (it.IsSlot)
        {
            if (sd.cur is null || sd.cur.Count == 0)
            {
                // Slot with no open span (should not happen mid-line): externalize.
                sd.spans.Add((new List<StlItem> { it }, it.Style, true, null));
                sd.pendingInheritWs = it.E / 1000.0;
                return;
            }
            var mean = 0.0;
            if (sd.foldedSlots.Count > 0)
            {
                foreach (var v in sd.foldedSlots) mean += v;
                mean /= sd.foldedSlots.Count;
            }
            var mMilli = sd.styles[it.Style].SpaceAdvMilli;
            // The em-compensation dialect folds every DRAWN-space slot (the
            // solved ws absorbs their spread); a SYNTHESIZED slot keeps the
            // outlier rule — a char-spaced line's one wide gap becomes its
            // own nbsp span, not a ws inflation. A slot at
            // a CROSS-FONT boundary (next char changes family or size) is
            // charged to the boundary, never to the preceding span's ws —
            // dense-CJK spans carry NO ws; the connector
            // span after them takes the gap. Same-font boundaries keep the
            // fold (the anchored title solve depends on its inter-span
            // slot staying in the title span).
            var crossFont = k + 1 < sd.partItems.Count && !sd.partItems[k + 1].IsSlot
                && (sd.styles[sd.partItems[k + 1].Style].CssFamily != sd.styles[it.Style].CssFamily
                    || Math.Abs(sd.styles[sd.partItems[k + 1].Style].FontSize
                                - sd.styles[it.Style].FontSize) > 0.01);
            if (sd.emGrid && crossFont)
            {
                Close(sd);
                sd.spans.Add((new List<StlItem> { it }, it.Style, true, null));
                sd.pendingInheritWs = it.E / 1000.0;
                return;
            }
            if ((sd.emGrid && !it.Synth && !crossFont)
                || sd.foldedSlots.Count == 0 || Math.Abs(it.E - mean) <= 0.6 * mMilli)
            {
                sd.foldedSlots.Add(it.E);
                sd.cur.Add(it);
            }
            else
            {
                Close(sd);
                sd.spans.Add((new List<StlItem> { it }, it.Style, true, null));
                sd.pendingInheritWs = it.E / 1000.0;
            }
            return;
        }
        if (sd.cur is not null && (sd.cut[k] || !sd.styles[it.Style].SameSpan(sd.styles[sd.curStyle])))
        {
            var inherit = sd.pendingInheritWs;
            Close(sd);
            // A style-matching span straight after an externalized slot inherits
            // its ws; a font-change span does not.
            sd.pendingInheritWs = sd.styles[it.Style].SameSpan(sd.styles[sd.curStyle]) ? inherit : null;
        }
        if (sd.cur is null) { sd.cur = new List<StlItem>(); sd.curStyle = it.Style; }
        sd.cur.Add(it);
    }

    /// <summary>The part's items are seated: the slot folds, the inherited word spacing and the em-grid compensation.</summary>
    private static void SeatStlPartItems(StlDivState sd)
    {
        for (var k = 0; k < sd.partItems.Count; k++)
        {
            if (sd.partItems[k].IsSlot) continue;
            if (sd.lastRendered >= 0
                && !sd.styles[sd.partItems[k].Style].SameSpan(sd.styles[sd.partItems[sd.lastRendered].Style]))
                sd.cut[k] = true;
            sd.lastRendered = k;
        }

        // Atomization inside runs bounded by slots/style cuts. The EXPLICIT
        // em-compensation mode never atomizes: it emits ONE span per
        // style run and absorbs per-char outliers into the quantized line spacing.
        // (The trigger is the OPTION being set - the enum's em member is its
        // first value, but the field's DEFAULT is the pixel mode; a save that
        // never touches it solves at four decimals.)
        const double TAtom = 1000.0 / 11.0;
        sd.runStart = 0;
        if (!sd.emGrid)
        for (var k = 1; k <= sd.partItems.Count; k++)
        {
            if (k < sd.partItems.Count && !sd.partItems[k].IsSlot && !sd.cut[k] && !sd.partItems[k - 1].IsSlot) continue;
            // run = items[runStart..k)
            var internals = new List<int>();
            for (var t = sd.runStart; t < k; t++)
                if (!sd.partItems[t].IsSlot && sd.partItems[t].LsEligible) internals.Add(t);
            if (internals.Count >= 2)
            {
                for (var t = 0; t < internals.Count; t++)
                {
                    double sum = 0;
                    foreach (var u in internals) if (u != internals[t]) sum += sd.partItems[u].E;
                    var meanOther = sum / (internals.Count - 1);
                    if (Math.Abs(sd.partItems[internals[t]].E - meanOther) > TAtom)
                    {
                        var carrier = internals[t];
                        if (carrier > sd.runStart) sd.cut[carrier] = true;                 // [prefix][carrier
                        if (carrier + 1 < sd.partItems.Count) sd.cut[carrier + 1] = true;      // carrier][first-after
                        if (carrier + 2 < sd.partItems.Count && !sd.partItems[carrier + 1].IsSlot
                            && !sd.partItems[carrier + 2].IsSlot) sd.cut[carrier + 2] = true;  // first-after][rest
                        break;   // one atomization per run
                    }
                }
            }
            sd.runStart = k;
        }
    }

    /// <summary>A text span's letter-spacing error and word spacing: from its slots, the em grid, or the inherited word spacing, with the text escaped for HTML.</summary>
    private static void ResolveSpanSpacing(StlDivState sd, List<StlItem> its, double? inheritWs, int s)
    {
        var eligible = its.Where(x => !x.IsSlot && x.LsEligible).ToList();
        // A span-final char whose word continues into the next span stays
        // ls-eligible; the builder marked word-finals ineligible already.
        // The mean reads the LIG-basis error (LsE): the components-vs-lig
        // face delta stays out of the ls classes.
        if (eligible.Count > 0)
            sd.lsMilli = eligible.Average(x => sd.emGrid ? x.LsE : x.E);
        // The em-compensation mode keeps its spacing on a 0.01 em grid: the
        // letter-spacing FLOORS to the grid first (a floor, NOT
        // round-half-away) and the word-spacing
        // then solves against the floored value, absorbing the residue.
        if (sd.emGrid)
            sd.lsMilli = Math.Floor(sd.lsMilli / 10.0) * 10.0;
        var slots = its.Count(x => x.IsSlot);
        if (slots > 0 && !sd.emGrid)
        {
            double sumE = 0;
            for (var t = 0; t < its.Count; t++)
            {
                // The four-decimal dialect excludes the line-final char's
                // own advance residue.
                var isLineFinal = s == sd.spans.Count - 1 && t == its.Count - 1;
                if (!isLineFinal) sumE += its[t].E;
            }
            // CSS letter-spacing lands after every character - the space
            // slots included - except a span-final one, whose advance the
            // next box absorbs; the solve counts terms the same way.
            var lsTerms = its.Count - (its[^1].IsSlot ? 0 : 1);
            sd.wsEm = Math.Round(
                (sumE - lsTerms * sd.lsMilli) / slots / 1000.0,
                4, MidpointRounding.AwayFromZero);
        }
        else if (slots > 0)
        {
            ResolveSlotSpacing(sd, its, inheritWs, s, slots);
        }
        else if (inheritWs is { } iw && !sd.emGrid)
        {
            // The em-compensation dialect never inherits a filler's ws:
            // a slotless span there carries NO word-spacing (the import
            // charges ws at every adjacent-ideograph boundary, so an
            // inherited filler rate would re-stretch the whole span).
            sd.wsEm = Math.Round(iw, 4, MidpointRounding.AwayFromZero);
        }
        var t2 = new StringBuilder();
        foreach (var x in its)
        {
            if (x.IsSlot) t2.Append(' ');
            else if (x.Text is not null) t2.Append(x.Text);
            else t2.Append(x.Ch);
        }
        sd.text = EscapeHtml(t2.ToString());
    }
}
