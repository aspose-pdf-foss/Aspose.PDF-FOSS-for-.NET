using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>
    /// Register a fragment whose TextState.Underline was set after extraction.
    /// Called from the Underline setter when the segment traces back to this page.
    /// </summary>
    internal void RegisterUnderlineFragment(Text.TextFragment fragment)
    {
        _underlineFragments ??= new();
        _underlineFragments.Add(fragment);
    }

    /// <summary>
    /// Register a fragment whose captured source underline should be removed from the
    /// content stream, because its TextState.Underline was toggled off after extraction
    /// (ToAttemptGetUnderlineFromSource). Called from the Underline setter.
    /// </summary>
    internal void RegisterUnderlineRemoval(Text.TextFragment fragment)
    {
        _underlineRemovalFragments ??= new();
        _underlineRemovalFragments.Add(fragment);
    }

    /// <summary>
    /// Splice out the source underline rectangles for every registered removal fragment by
    /// matching their captured raw <c>re</c> operands against the page content operators.
    /// Called during save, alongside <see cref="FlushUnderlineRectangles"/>.
    /// </summary>
    internal void FlushUnderlineRemovals()
    {
        if (_underlineRemovalFragments is null || _underlineRemovalFragments.Count == 0) return;
        // Start from what the page ACTUALLY holds. An earlier save-time pass may have
        // appended or prepended a stream, and the cached operator view is a snapshot from
        // before it existed - flushing that view back would restore the page as it was and
        // drop the append. Only these passes need it: resetting inside the append itself
        // pulls the view out from under a caller that is still building with it.
        ResetContentsCache();
        var targets = new List<(double X, double Y, double W, double H)>();
        // A source rule normally runs past the matched phrase — under "Test bold text 26"
        // the rule also covers the " 26". Switching THIS fragment's underline off must not
        // take the rest of the line's rule with it, so the part the fragment never covered
        // is re-emitted, keeping the source rule's own band (a stroked rule keeps its
        // stroke width). The REPLACEMENT case is different and is not handled here: the
        // fragment is registered for redraw too, and FlushUnderlineRectangles reseats both
        // the replacement's rule and the tail's in the library's own band.
        var survivors = new Content.ContentStreamBuilder();
        var anySurvivor = false;
        foreach (var frag in _underlineRemovalFragments)
        {
            if (frag.CapturedUnderlineSources is { } list) targets.AddRange(list);
            if (frag.CapturedBackgroundSources is { } bgList) targets.AddRange(bgList);
            if (frag.CompanionRuleSources is { } compList) targets.AddRange(compList);
            if (_underlineFragments?.Contains(frag) == true
                || _underlineRedrawn?.Contains(frag) == true) continue;
            if (frag.CapturedUnderlinePageRect is not { } pr) continue;
            var from = frag.SourceUnderlineRunEndX;
            if (from <= pr.Llx || pr.Urx - from <= 0.5) continue;
            var col = frag.TextState.ForegroundColor;
            survivors.SaveState();
            survivors.SetFillColor(col?.R / 255.0 ?? 0, col?.G / 255.0 ?? 0, col?.B / 255.0 ?? 0);
            survivors.Rectangle(from, pr.Lly, pr.Urx - from, pr.Ury - pr.Lly);
            survivors.Fill();
            survivors.RestoreState();
            anySurvivor = true;
        }
        _underlineRemovalFragments.Clear();
        _underlineRedrawn = null;
        if (targets.Count == 0) return;

        var ops = Contents;
        // A spliced `re` takes its PAINT operator with it. Removing the rectangle alone left
        // a bare `f*` behind, which fills whatever path happens to be current - an operator
        // list that reads as valid but is not what the page draws.
        var paintOfRe = new System.Collections.Generic.Dictionary<Operator, Operator>();
        {
            var scan = ops.ToList();
            for (var i = 0; i < scan.Count; i++)
            {
                if (scan[i] is not Aspose.Pdf.Operators.Re && scan[i].CommandName != "re") continue;
                for (var j = i + 1; j < scan.Count && j <= i + 2; j++)
                {
                    var cmd = scan[j].CommandName;
                    if (cmd is "f" or "F" or "f*" or "b" or "b*" or "B" or "B*" or "n" or "s" or "S")
                    { paintOfRe[scan[i]] = scan[j]; break; }
                    if (cmd is not ("W" or "W*")) break;
                }
            }
        }
        var doomedPaint = new System.Collections.Generic.HashSet<Operator>();
        var removed = ops.RemoveWhere(op =>
        {
            if (doomedPaint.Remove(op)) return true;
            // Content operators materialize lazily as generic operators, so match by
            // command name + operands rather than only the typed classes.
            double x, y, w, h;
            if (op is Aspose.Pdf.Operators.Re re)
            {
                (x, y, w, h) = (re.X, re.Y, re.Width, re.Height);
            }
            else if (op.CommandName == "re")
            {
                var nums = ParseLeadingNumbers(op.ToString());
                if (nums is not { Length: >= 4 }) return false;
                (x, y, w, h) = (nums[0], nums[1], nums[2], nums[3]);
            }
            else if (op is Aspose.Pdf.Operators.MoveTo || op is Aspose.Pdf.Operators.LineTo
                || op.CommandName is "m" or "l")
            {
                // A stroked-line underline ("x1 y m x2 y l S") was captured with
                // raw X = left end, W = span, Y = the stroke's y. Splice both path
                // points whose coordinates hit either end of a captured target;
                // the leftover S strokes an empty path and paints nothing.
                double px, py;
                if (op is Aspose.Pdf.Operators.MoveTo mv) { px = mv.X; py = mv.Y; }
                else if (op is Aspose.Pdf.Operators.LineTo lt) { px = lt.X; py = lt.Y; }
                else
                {
                    var nums = ParseLeadingNumbers(op.ToString());
                    if (nums is not { Length: >= 2 }) return false;
                    (px, py) = (nums[0], nums[1]);
                }
                foreach (var t in targets)
                {
                    if (Math.Abs(py - t.Y) < 0.75 &&
                        (Math.Abs(px - t.X) < 0.75 || Math.Abs(px - (t.X + t.W)) < 0.75))
                        return true;
                }
                return false;
            }
            else return false;

            foreach (var t in targets)
            {
                if (Math.Abs(x - t.X) < 0.5 && Math.Abs(y - t.Y) < 0.5 &&
                    Math.Abs(w - t.W) < 0.5 && Math.Abs(h - t.H) < 0.5)
                {
                    if (paintOfRe.TryGetValue(op, out var paint)) doomedPaint.Add(paint);
                    return true;
                }
            }
            return false;
        });
        // Persist the edited operator list back to the page content stream.
        if (removed > 0) ops.FlushToPage();
        if (anySurvivor)
        {
            var survivorBytes = survivors.Build();
            if (survivorBytes.Length > 0) AddContentStream(survivorBytes);
        }
    }

    /// <summary>
    /// Emit thin filled rectangles below text for every registered underline fragment.
    /// Called during save after content stream operators are written.
    /// </summary>
    internal void FlushUnderlineRectangles()
    {
        if (_underlineFragments is null || _underlineFragments.Count == 0) return;
        // Start from what the page ACTUALLY holds. An earlier save-time pass may have
        // appended or prepended a stream, and the cached operator view is a snapshot from
        // before it existed - flushing that view back would restore the page as it was and
        // drop the append. Only these passes need it: resetting inside the append itself
        // pulls the view out from under a caller that is still building with it.
        ResetContentsCache();
        _underlineRedrawn = new HashSet<Text.TextFragment>(_underlineFragments);
        var builder = new Content.ContentStreamBuilder();
        foreach (var frag in _underlineFragments)
        {
            var fragPos = frag.PositionOrNull;
            if (fragPos is null) continue;
            var fs = frag.TextState.FontSize;
            if (fs <= 0) fs = 12;

            // Width: prefer fragment's Rectangle width (computed during absorption),
            // fall back to MeasureString.
            double w;
            if (frag.Rectangle is not null)
            {
                w = frag.Rectangle.Width;
            }
            else
            {
                var font = frag.TextState.Font;
                if (font is not null)
                {
                    try { w = font.MeasureString(frag.Text, fs); }
                    catch { w = frag.Text.Length * fs * 0.5; }
                }
                else
                {
                    w = frag.Text.Length * fs * 0.5;
                }
            }

            // Underline geometry (verified on Arial and Calibri
            // sources): the line's top edge sits a tenth of the font's descent below
            // the fragment's rect bottom (Position.YIndent), and the thickness is 5%
            // of the font size — so the bottom offset is (0.05 + descent/10)·fs.
            // Fonts without a descent metric keep the historical constant, which is
            // this same formula evaluated for a typical 0.269 descent.
            double ulThick = fs * 0.05;
            double ulDescent = 0;
            var ulMetrics = frag.TextState.Font?.GetMetrics();
            if (ulMetrics is not null && ulMetrics.Descent != 0)
                ulDescent = Math.Abs(ulMetrics.Descent) / 1000.0;
            double ulOffset = ulDescent > 0 ? (0.05 + ulDescent / 10) * fs : fs * 0.07691;

            // A fragment whose SOURCE underline was captured (ToAttemptGetUnderlineFromSource,
            // then text-replaced): the source rule was spliced out, and the run it covered
            // is redrawn here — the REPLACEMENT at its own advance, then the run's TAIL
            // (the source rule normally spans more than the matched phrase: "Test bold
            // text 26" under one line) re-seated where the shorter/longer replacement
            // leaves it. Both pieces sit in the measured underline band:
            // bottom = the fragment's Position.YIndent + a tenth of the font's descent,
            // thickness = 5% of the font size — the same 0.05·fs the plain path uses.
            if (frag.CapturedUnderlineSources is { Count: > 0 })
            {
                var afg = frag.TextState.ForegroundColor;
                // A face with no descent metric keeps the typical 0.216 the formula was
                // probed against, so the band never collapses onto YIndent itself.
                var ulBottom = fragPos.YIndent + (ulDescent > 0 ? ulDescent : 0.216) / 10 * fs;
                // A source rule normally runs past the matched phrase in BOTH directions -
                // one rule under "www.oliver.com" covers three runs. Splicing it out and
                // redrawing only the match leaves the runs on either side bare, so the rule
                // is re-laid piece by piece: the HEAD (whatever the rule covered left of the
                // match), the match itself at its new advance, and the TAIL. Each piece is
                // written inline, before the run it dresses, in the library's own band -
                // the same treatment the whole line gets.
                double headW = 0, headX = fragPos.XIndent;
                if (frag.CapturedUnderlinePageRect is { } srcRule && srcRule.Llx < fragPos.XIndent - 0.5)
                {
                    headX = srcRule.Llx;
                    headW = fragPos.XIndent - srcRule.Llx;
                }
                double tailW = 0, tailX = fragPos.XIndent + w;
                double tailBottom = ulBottom, tailThick = ulThick;
                if (frag.SourceUnderlineTrailingText is { Length: > 0 } tail)
                {
                    // The tail is the rest of the match's OWN run, so it follows the
                    // replacement at whatever advance that came to, in the same band.
                    try { tailW = frag.TextState.Font?.MeasureString(tail, fs) ?? 0; }
                    catch { tailW = 0; }
                }
                else if (frag.CapturedUnderlinePageRect is { } tailRule
                    && frag.SourceUnderlineRunEndX > tailRule.Llx
                    && tailRule.Urx > frag.SourceUnderlineRunEndX + 0.5)
                {
                    // ...otherwise the rule runs on past the match's run and dresses a
                    // SEPARATE one the replacement never moved. That piece is not ours to
                    // re-lay: it keeps the span AND THE BAND the source gave it, because the
                    // run under it still sits where it always did. Redrawing it in the
                    // library's band moves a rule whose text did not move.
                    tailX = frag.SourceUnderlineRunEndX;
                    tailW = tailRule.Urx - frag.SourceUnderlineRunEndX;
                    tailBottom = tailRule.Lly;
                    tailThick = tailRule.Ury - tailRule.Lly;
                }
                var placedInline = InsertBeforeTextObjectAt(
                    DecorationBlock(afg, fragPos.XIndent, ulBottom, w, ulThick),
                    fragPos.XIndent, fragPos.YIndent);
                if (placedInline)
                {
                    if (headW > 0.5)
                        InsertBeforeTextObjectAt(
                            DecorationBlock(afg, headX, ulBottom, headW, ulThick),
                            headX, fragPos.YIndent);
                    if (tailW > 0)
                        InsertBeforeTextObjectAt(
                            DecorationBlock(afg, tailX, tailBottom, tailW, tailThick),
                            tailX, fragPos.YIndent);
                    foreach (var comp in frag.CompanionRules ?? Enumerable.Empty<(double X, double W, Aspose.Pdf.Color Colour)>())
                        InsertBeforeTextObjectAt(
                            DecorationBlock(comp.Colour, comp.X, ulBottom, comp.W, ulThick),
                            comp.X, fragPos.YIndent);
                    continue;
                }
                builder.SaveState();
                builder.SetFillColor(afg?.R / 255.0 ?? 0, afg?.G / 255.0 ?? 0, afg?.B / 255.0 ?? 0);
                if (headW > 0.5) { builder.Rectangle(headX, ulBottom, headW, ulThick); builder.Fill(); }
                builder.Rectangle(fragPos.XIndent, ulBottom, w, ulThick);
                builder.Fill();
                if (tailW > 0)
                {
                    builder.Rectangle(tailX, tailBottom, tailW, tailThick);
                    builder.Fill();
                }
                builder.RestoreState();
                continue;
            }

            // Rotation-aware path: for text drawn under a rotating CTM, emit the
            // underline along the baseline via a cm transform (a perpendicular page-Y
            // offset would leave the line floating off the rotated text). Horizontal
            // text (TextDirX=1, TextDirY=0) is unaffected and takes the path below.
            double ulDirX = frag.TextDirX, ulDirY = frag.TextDirY;
            double ulDirLen = Math.Sqrt(ulDirX * ulDirX + ulDirY * ulDirY);
            if (ulDirLen > 1e-6 && Math.Abs(ulDirY / ulDirLen) > 0.01)
            {
                double ux = ulDirX / ulDirLen, uy = ulDirY / ulDirLen;
                double rw;
                try { rw = frag.TextState.Font?.MeasureString(frag.Text, fs) ?? frag.Text.Length * fs * 0.5; }
                catch { rw = frag.Text.Length * fs * 0.5; }
                var fgr = frag.TextState.ForegroundColor;
                builder.SaveState();
                builder.SetFillColor(fgr?.R / 255.0 ?? 0, fgr?.G / 255.0 ?? 0, fgr?.B / 255.0 ?? 0);
                builder.SetMatrix(ux, uy, -uy, ux, fragPos.XIndent, fragPos.YIndent);
                builder.Rectangle(0, -ulOffset, rw, ulThick);
                builder.Fill();
                builder.RestoreState();
                continue;
            }

            // In page space (Y-up), underline is BELOW baseline = lower Y.
            // But with CTM Y-flip, the offset direction reverses.
            var ctm = frag.ExtractionCtm;
            bool yFlipped = ctm is not null && ctm.D < 0;
            var underlineY = yFlipped
                ? fragPos.YIndent + ulOffset
                : fragPos.YIndent - ulOffset;
            var underlineH = ulThick;

            // Transform from page space to content-stream space using the inverse CTM.
            // Emit raw 're' in content-stream coordinates (no cm prefix) so
            // IsRectanglePresent matches the bare coordinates.
            double rectX = fragPos.XIndent, rectY = underlineY;
            if (ctm is not null)
            {
                (rectX, rectY) = ctm.InverseTransformPoint(fragPos.XIndent, underlineY);
                var (wx, wy) = ctm.InverseTransformPoint(fragPos.XIndent + w, underlineY + underlineH);
                w = Math.Abs(wx - rectX);
                underlineH = Math.Abs(wy - rectY);
            }

            var fg = frag.TextState.ForegroundColor;
            double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b = fg?.B / 255.0 ?? 0;

            builder.SaveState();
            builder.SetFillColor(r, g, b);
            builder.Rectangle(rectX, rectY, w, underlineH);
            builder.Fill();
            builder.RestoreState();
        }
        var bytes = builder.Build();
        if (bytes.Length > 0)
            AddContentStream(bytes);
        _underlineFragments.Clear();
    }
}
