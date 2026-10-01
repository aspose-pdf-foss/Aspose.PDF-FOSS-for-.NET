
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>
    /// Reflow <paramref name="newText"/> into <paramref name="rect"/>: word-wrap
    /// to the rectangle width, optionally fit the font size
    /// (<see cref="TextReplaceOptions.FontSizeAdjustment"/>), delete the original
    /// paragraph operators, and write the wrapped lines top-anchored inside the
    /// rectangle. Updates <see cref="_text"/>, <see cref="_segments"/> and
    /// <see cref="_rectangle"/> to the laid-out block. Returns false (leaving the
    /// caller to fall back to the in-place swap) when the geometry is unusable.
    /// </summary>
    private bool TryReflowIntoRectangle(string oldText, string newText, Rectangle rect)
    {
        var rr = new ReflowRectangleState();
        rr.page = SourcePage!;
        rr.font = TextState.Font!;
        rr.baseFs = TextState.FontSize;
        if (rect.Width <= 1 || rect.Height <= 1 || string.IsNullOrEmpty(newText)) return false;

        rect = ReadReflowGeometry(rr, rect);

        rr.wrapWidth = rect.Width;
        rr.sx = TextState.SourceTmScale is > 0.01 and < 100 ? TextState.SourceTmScale : 1.0;
        rr.wrapWidthT = rr.wrapWidth / rr.sx;
        rr.fit = _replaceOptions?.FontSizeAdjustmentAction ?? TextReplaceOptions.FontSizeAdjustment.None;

        if (rr.fit is TextReplaceOptions.FontSizeAdjustment.None
            && string.Equals(FoldWs(oldText), FoldWs(newText), StringComparison.Ordinal)
            && _segments.Count > 0
            && _segments.All(s => s.Position is not null && s.Rectangle is not null
                && !string.IsNullOrEmpty(s.Text))
            && TryTranslateBlock(rr.page, oldText, newText, rect, rr.baseFs))
        {
            return true;
        }
        SolveReflowFontSize(rr, rect, newText);

        rr.lines = rr.hmtxMeasure is not null
            ? WrapToWidth(newText, rr.font, rr.fs, rr.wrapWidthT, trailingSpace: true, measure: s => rr.hmtxMeasure(s, rr.fs))
            : rr.srcMeasure is not null
            ? WrapToWidth(newText, rr.font, rr.fs, rr.wrapWidthT, trailingSpace: true, measure: s => rr.srcMeasure(s, rr.fs))
            : WrapToWidth(newText, rr.font, rr.fs, rr.wrapWidthM / rr.sx, trailingSpace: true);
        if (rr.lines.Count == 0) return false;
        SeatReflowedBaselines(rr, rect);
        RemoveReflowedSource(rr, rect, oldText);
        WriteReflowedLines(rr, rect);
        ReportReflowedRectangle(rr, rect, newText);
        return true;
    }

    /// <summary>Pure translation of an unchanged paragraph into a same-size
    /// rectangle: shift the block's own positioning operators (Tm, and a
    /// leading Td straight after BT) by the move delta, leaving the show
    /// strings, fonts, and kerning untouched — the re-absorbed block reproduces
    /// the original geometry exactly, moved by the shift.</summary>
    private bool TryTranslateBlock(Page page, string oldText, string newText, Rectangle rect, double baseFs)
    {
        var bt = new BlockTranslateState();
        bt.page = page;
        bt.oldText = oldText;
        bt.newText = newText;
        bt.rect = rect;
        bt.baseFs = baseFs;
        bt.obLLX = double.MaxValue;
        bt.obLLY = double.MaxValue;
        bt.obURX = double.MinValue;
        bt.obURY = double.MinValue;
        if (AbsorbedRectangle is { } ab)
        {
            bt.obLLX = ab.LLX; bt.obLLY = ab.LLY; bt.obURX = ab.URX; bt.obURY = ab.URY;
        }
        else
        {
            foreach (var s in _segments)
            {
                var sr = s.Rectangle!;
                if (sr.LLX < bt.obLLX) bt.obLLX = sr.LLX;
                if (sr.LLY < bt.obLLY) bt.obLLY = sr.LLY;
                if (sr.URX > bt.obURX) bt.obURX = sr.URX;
                if (sr.URY > bt.obURY) bt.obURY = sr.URY;
            }
        }
        bt.dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_GRIDDEBUG") == "1";
        if (Math.Abs(bt.rect.Width - (bt.obURX - bt.obLLX)) >= 0.5
            || Math.Abs(bt.rect.Height - (bt.obURY - bt.obLLY)) >= 0.5)
        {
            if (bt.dbg) Console.Error.WriteLine($"[xlate] size mismatch rect={bt.rect.Width:F2}x{bt.rect.Height:F2} ob={(bt.obURX - bt.obLLX):F2}x{(bt.obURY - bt.obLLY):F2}");
            return false;
        }

        bt.dx = bt.rect.LLX - bt.obLLX;
        bt.dy = bt.rect.URY - bt.obURY;

        bt.padX = 2.0;
        bt.padY = Math.Max(4.0, bt.baseFs * 0.5);
        bt.shifted = 0;
        bt.afterBt = false;
        bt.toReplace = new List<Aspose.Pdf.Operators.SetTextMatrix>();
        // Materialize so the enumerated instances are the collection's own
        // (mutations persist and Index is stamped for the delete/insert swap).
        bt.page.Contents.EnsureMaterialized();
        foreach (var op in bt.page.Contents)
        {
            TranslateBlockOperator(bt, op);
        }
        foreach (var tm in bt.toReplace)
        {
            var at = tm.Index;
            bt.page.Contents.Delete(new Aspose.Pdf.Operator[] { tm });
            bt.page.Contents.Insert(at, new Aspose.Pdf.Operators.SetTextMatrix(
                tm.A, tm.B, tm.C, tm.D, tm.E + bt.dx, tm.F + bt.dy));
            bt.shifted++;
        }
        if (bt.shifted == 0)
        {
            if (bt.dbg) Console.Error.WriteLine("[xlate] no positioning ops found in region");
            return false;
        }
        bt.page.Contents.FlushToPage();
        if (bt.dbg) Console.Error.WriteLine($"[xlate] shifted {bt.shifted} positioning ops by ({bt.dx:F2},{bt.dy:F2})");

        _rectangle = new Rectangle(bt.obLLX + bt.dx, bt.obLLY + bt.dy, bt.obURX + bt.dx, bt.obURY + bt.dy);
        _text = bt.newText;
        foreach (var s in _segments)
        {
            s.Position = new Position(s.Position!.XIndent + bt.dx, s.Position.YIndent + bt.dy);
            s.Rectangle = new Rectangle(s.Rectangle!.LLX + bt.dx, s.Rectangle.LLY + bt.dy,
                s.Rectangle.URX + bt.dx, s.Rectangle.URY + bt.dy);
        }
        return true;
    }

    /// <summary>
    /// IsFormFillingMode replace: word-wrap an over-wide replacement into the matched
    /// fragment's own rectangle. Lines break greedily at spaces
    /// against the ORIGINAL fragment's width, left-aligned at its LLX; the first line
    /// keeps the original baseline and each following line steps 1.2·fs down; the font
    /// size never changes; every non-final line keeps its trailing separator space (so
    /// re-extraction reassembles the exact replacement string). When the source
    /// (subset) font lacks replacement glyphs the lines are written in the system face
    /// of the same family, else Times New Roman. Returns false (leaving the ordinary
    /// in-place replace to run) when the replacement fits on one line or the page
    /// structure defeats it.
    /// </summary>
    private bool TryFormFillWrap(Page page, string oldText, string newText)
    {
        var fw = new FormFillWrapState();
        fw.page = page;
        fw.oldText = oldText;
        fw.newText = newText;
        if (_rectangle is null || _position is null || string.IsNullOrEmpty(fw.newText))
            return false;
        fw.font = TextState.Font;
        fw.fs = TextState.FontSize;
        if (fw.font is null || fw.fs <= 0) return false;
        fw.width = _rectangle.Width;
        if (fw.width < 2) return false;

        fw.writeFont = fw.font;
        if (!Covers(fw.writeFont, fw.newText))
        {
            var fam = fw.font.FontName ?? string.Empty;
            int plus = fam.IndexOf('+');
            if (plus >= 0 && plus + 1 < fam.Length) fam = fam[(plus + 1)..];
            int comma = fam.IndexOf(',');
            if (comma > 0) fam = fam[..comma];
            Aspose.Pdf.Text.Font? sys = null;
            if (fam.Length > 0)
                try { sys = FontRepository.TryFindFont(fam, ignoreCase: true); } catch { }
            if (sys is null || !Covers(sys, fw.newText))
                try { sys = FontRepository.TryFindFont("Times New Roman", ignoreCase: true); } catch { }
            if (sys is null || !Covers(sys, fw.newText)) return false;
            fw.writeFont = sys;
        }

        fw.wrapped = new System.Collections.Generic.List<string>();
        fw.cur = new System.Text.StringBuilder();
        WrapFormFillWords(fw);
        if (fw.cur.Length > 0) fw.wrapped.Add(fw.cur.ToString());
        if (fw.wrapped.Count <= 1) return false; // fits: ordinary in-place replace

        fw.del = new TextReplacer
        {
            MatchAnyOperator = true,
            TargetX = (_rectangle.LLX + _rectangle.URX) / 2,
            TargetXTolerance = fw.width / 2 + 1.0,
        };
        fw.targetYs = new System.Collections.Generic.List<double>();
        fw.baseY = (BaselinePosition ?? _position).YIndent;
        fw.targetYs.Add(fw.baseY);
        if (System.Math.Abs(_position.YIndent - fw.baseY) > 0.01) fw.targetYs.Add(_position.YIndent);
        foreach (var ty in fw.targetYs)
        {
            fw.del.TargetY = ty;
            fw.del.Replace(fw.page, string.Empty, string.Empty);
            if (fw.del.ReplacementCount > 0) break;
        }
        if (fw.del.ReplacementCount == 0) return false;

        fw.tb = new TextBuilder(fw.page);
        fw.x0 = _rectangle.LLX;
        fw.y0 = _position.YIndent;
        fw.step = 1.2 * fw.fs;
        fw.laidOut = new System.Collections.Generic.List<(string text, double by, double w)>();
        fw.maxLineW = 0;
        PlaceFormFillLines(fw);
        return true;
    }

    /// <summary>The wrapper above, with a PER-LINE width budget: line i wraps at
    /// <paramref name="budgetOf"/>(i). A paragraph re-flow refills the source line grid,
    /// where every line carries its own capacity (see the re-flow's line-budget law);
    /// a plain wrap passes the same width for every line.</summary>
    private static System.Collections.Generic.List<string> WrapToBudgets(string text, FontInfo font, double fs, Func<int, double> budgetOf, bool trailingSpace = false, bool allowCharBreak = false, Func<string, double>? measure = null)
    {
        double M(string s) => measure?.Invoke(s) ?? MeasureOrEstimate(font, s, fs, trailingSpace);
        var lines = new System.Collections.Generic.List<string>();
        var words = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Split(' ');
        var cur = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            double maxWidth = budgetOf(lines.Count);
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (M(trial) <= maxWidth)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
                continue;
            }
            // The word doesn't fit the current line. Flush the line, then place the word fresh.
            if (cur.Length > 0)
            {
                lines.Add(trailingSpace ? cur.ToString() + " " : cur.ToString());
                cur.Clear();
                maxWidth = budgetOf(lines.Count);   // the flushed line moved us onto the next budget
            }
            if (!allowCharBreak || M(word) <= maxWidth)
            {
                // Fits on its own line (or char-break disabled: keep the original behaviour of a
                // lone over-wide word occupying its own line).
                cur.Append(word);
            }
            else
            {
                // A single word wider than the line (an unbreakable long token, e.g. a no-space
                // replacement): character-break it so it stays within the page instead of running
                // off the right edge. Emit as many leading characters as fit per line.
                int start = 0;
                while (start < word.Length)
                {
                    double chunkMax = budgetOf(lines.Count);
                    int take = 1;
                    while (start + take < word.Length &&
                           M(word.Substring(start, take + 1)) <= chunkMax) take++;
                    string chunk = word.Substring(start, take);
                    start += take;
                    if (start < word.Length) lines.Add(trailingSpace ? chunk + " " : chunk);
                    else cur.Append(chunk); // last chunk continues the current line
                }
            }
        }
        // Splitting on spaces drops one the text itself ENDS with, and that space is
        // part of the replacement: the extent reported for the last line covers it, the
        // same way every earlier line covers the break's own trailing space.
        if (cur.Length > 0)
            lines.Add(trailingSpace && text.Length > 0 && text[^1] == ' '
                ? cur.ToString() + " "
                : cur.ToString());
        return lines;
    }

    /// <summary>Under <see cref="TextEditOptions.ClippingPathsProcessingMode.Expand"/>,
    /// after a paragraph reflow APPENDED overflow line(s) below the paragraph's last
    /// baseline: extend the bottom of every clip rectangle that contains the paragraph
    /// down to the appended line's baseline (the new clip is the
    /// union of the old clip and the reflowed extent — bottom lands exactly ON the
    /// appended baseline, top/left/right unchanged, and a clip never shrinks).</summary>
    private void ExpandClipsToReflowBottom(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        double newBottom)
    {
        if (double.IsNaN(newBottom) || paraLines.Count == 0) return;
        var expand = _textEditOptions?.ClippingPathsProcessing
            == TextEditOptions.ClippingPathsProcessingMode.Expand;
        if (!expand)
            foreach (var s in _segments)
                if (s.TextEditOptions.ClippingPathsProcessing
                    == TextEditOptions.ClippingPathsProcessingMode.Expand)
                {
                    expand = true;
                    break;
                }
        if (!expand) return;

        double paraLeft = double.MaxValue, paraRight = 0;
        foreach (var l in paraLines)
        {
            if (l.lx < paraLeft) paraLeft = l.lx;
            if (l.rx > paraRight) paraRight = l.rx;
        }
        double paraTop = paraLines[0].y, paraBottom = paraLines[^1].y;

        var ops = page.Contents;
        ops.EnsureMaterialized();
        Aspose.Pdf.Operator? prev = null;
        var changed = false;
        foreach (var op in ops)
        {
            if (op is Aspose.Pdf.Operators.Clip or Aspose.Pdf.Operators.EOClip
                && prev is Aspose.Pdf.Operators.Re re
                && re.X <= paraLeft + 5 && re.X + re.Width >= paraRight - 5
                && re.Y <= paraBottom + 2 && re.Y + re.Height >= paraTop - 2
                && re.Y > newBottom + 0.01)
            {
                re.Height += re.Y - newBottom;
                re.Y = newBottom;
                changed = true;
            }
            prev = op;
        }
        if (changed) ops.FlushToPage();
    }
}
