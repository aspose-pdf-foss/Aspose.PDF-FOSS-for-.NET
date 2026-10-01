

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// A stage of the rectangle reflow.
    // Same text into a same-size rectangle is a pure TRANSLATION: keep the
    // original line structure (strings, pitch, per-line widths) and move it.
    // Re-wrapping would put every measurement error of a metrics-less font
    // into the block's shape; translation cancels them all.
    // The matched text carries the source line breaks; the replacement has
    // plain spaces — compare whitespace-folded.
    private static string FoldWs(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    private TextFragment MakeLine(ReflowRectangleState rr, string text, double x, double y)
    {
        var lf = new TextFragment(text);
        lf.TextState.Font = rr.font;
        lf.TextState.FontName = TextState.FontName;
        lf.TextState.FontSize = (float)rr.fs;
        lf.TextState.IsBold = TextState.IsBold;
        lf.TextState.IsItalic = TextState.IsItalic;
        // Weight the source got by STROKING its own outline travels with the
        // replacement: the stand-in is the regular face either way, so dropping the
        // mode is what turns a bold heading into a light one.
        lf.TextState.RenderingMode = TextState.RenderingMode;
        if (TextState.StrokingColor is { } strokeCol) lf.TextState.StrokingColor = strokeCol;
        if (TextState.ForegroundColor is { } fg) lf.TextState.ForegroundColor = fg;
        if (rr.arialFace)
        {
            lf.TextState.Std14FaceOverride = rr.writtenFontName;
            lf.TextState.EmitStandard14Descriptor = true;
            // Written in the system face, the resource carries that face's own
            // advances — the same choice already made for its vertical metrics.
            // Arial is drawn on a 2048-unit em, so its advances are not whole
            // 1000ths, and the core table's rounded ones make the extent read
            // back a twentieth of a point short over a paragraph.
            lf.TextState.Std14Widths = SystemFaceWidths(rr.writtenFontName);
        }
        else if (rr.substFace is not null)
        {
            lf.TextState.Std14FaceOverride = rr.substFace;
            lf.TextState.Std14Widths = rr.substWidths;
        }
        lf.TextState.SourceTmScale = rr.sx;
        lf.Position = new Position(x, y);
        return lf;
    }

    private static double Measure(ReflowRectangleState rr, string s)
    {
        // The written face measures the result: a substituted run reports the
        // stand-in's advances, not the source font's.
        if (rr.hmtxMeasure is not null) return rr.hmtxMeasure(s, rr.fs);
        if (rr.srcMeasure is not null)
        {
            var v = rr.srcMeasure(s, rr.fs);
            if (v >= 0) return v;
        }
        try { return rr.font.MeasureString(s, rr.fs) * rr.measureScale; }
        catch { return s.Length * rr.fs * 0.5; }
    }

    /// <summary>The reflowed block reports its own box, text and segments back to the fragment.</summary>
    private void ReportReflowedRectangle(ReflowRectangleState rr, Rectangle rect, string newText)
    {

        // The delete (SetContentStream) + line writes (AddContentStream) edited the
        // raw /Contents; drop any materialised typed-operator view so a later
        // page.Contents use (and save) re-reads the reflowed content.
        rr.page.ResetContentsCache();

        rr.lastBaseline = rr.lines.Count == 1
            ? rr.firstBaseline
            : rr.gridFirst - (rr.lines.Count - 1) * rr.leading;
        rr.reportedURY = rr.arialFace ? rr.firstBaseline + rr.ascentH : rect.URY;
        rr.reportedLineHeightEm = 1.1;
        rr.singleLineLLY = rr.reportedURY - rr.reportedLineHeightEm * rr.fs;
        rr.reportedLLY = rr.lines.Count == 1
            ? Math.Max(rr.lastBaseline + rr.descentOff, rr.singleLineLLY)
            : rr.singleLineLLY;
        _rectangle = new Rectangle(rect.LLX, rr.reportedLLY, rect.LLX + rr.maxW, rr.reportedURY);
        _text = newText;
        _segments.Clear();
        for (var i = 0; i < rr.lines.Count; i++)
        {
            var seg = new TextSegment(rr.lines[i]);
            seg.TextState.FontSize = (float)rr.fs;
            seg.TextState.FontName = TextState.FontName;
            seg.TextState.Font = rr.font;
            seg.Owner = this;
            seg.Position = new Position(rect.LLX, i == 0 ? rr.firstBaseline : rr.gridFirst - i * rr.leading);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
    }

    /// <summary>Every wrapped line is written at its baseline, justified when the options ask for it.</summary>
    private void WriteReflowedLines(ReflowRectangleState rr, Rectangle rect)
    {
        for (var i = 0; i < rr.lines.Count; i++)
        {
            var lineY = i == 0 ? rr.firstBaseline : rr.gridFirst - i * rr.leading;
            var words = rr.justify
                ? rr.lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : null;
            if (words is { Length: > 1 })
            {
                // Justified line: spread the words so the line's ink spans the
                // wrap width exactly — the widened inter-word gaps read back as
                // the stretched spaces of the justification. The last line keeps
                // its natural spacing. Each line ends with its own trailing
                // space run positioned at the line's ink end (like the source
                // lines it replaces), so the line's reported extent follows the
                // SOURCE advances, not the written face's.
                var wordW = new double[words.Length];
                double inkW = 0;
                for (var wi = 0; wi < words.Length; wi++) { wordW[wi] = Measure(rr, words[wi]); inkW += wordW[wi]; }
                var lastLine = i == rr.lines.Count - 1;
                var gap = lastLine
                    ? Measure(rr, " ")
                    : (rr.wrapWidthT - inkW) / (words.Length - 1);
                if (gap > 0)
                {
                    // Justification seats the last NON-SPACE character on the wrap
                    // edge; the break's own space then hangs past it at its natural
                    // advance. The last line is not justified and ends at its final
                    // word. The final run's reported right edge is its start plus
                    // the WRITTEN face's advance, so anchor it by that.
                    var lineEnd = lastLine
                        ? rect.LLX + (inkW + gap * (words.Length - 1)) * rr.sx
                        : rect.LLX + rr.wrapWidth + Measure(rr, " ") * rr.sx;
                    var written = TextBuilder.MapToStandard14Public(TextState);
                    double WrittenWidth(string s)
                    {
                        // The written run's bytes are WinAnsi — measure the same
                        // codes the re-absorber will (en dash lives at 0x96, not
                        // U+2013).
                        double w0 = 0;
                        foreach (var c in s)
                        {
                            var gw = Standard14Fonts.GetWidth(written,
                                Aspose.Pdf.Content.ContentStreamBuilder.ToWinAnsi(c));
                            if (gw > 0) w0 += gw * rr.fs / 1000.0;
                        }
                        return w0;
                    }
                    // The final run must span at least an em: the absorber's
                    // line-break sentinel occupies a 1-em box at the last run's
                    // start, and a shorter final run would let it poke past the
                    // line's true end. Fold preceding words in until it doesn't.
                    var lastCount = 1;
                    string lastText;
                    double wWritten;
                    while (true)
                    {
                        var tail = string.Join(" ", words[(words.Length - lastCount)..]);
                        lastText = lastLine ? tail : tail + " ";
                        wWritten = WrittenWidth(lastText);
                        if (wWritten <= 0) { wWritten = wordW[^1] + Measure(rr, " "); break; }
                        if (wWritten >= rr.fs || lastCount >= words.Length) break;
                        lastCount++;
                    }

                    var x = 0.0;
                    for (var wi = 0; wi < words.Length - lastCount; wi++)
                    {
                        rr.tb.AppendText(MakeLine(rr, words[wi], rect.LLX + x * rr.sx, lineY));
                        x += wordW[wi] + gap;
                    }
                    rr.tb.AppendText(MakeLine(rr, lastText, lineEnd - wWritten * rr.sx, lineY));
                    if (lineEnd - rect.LLX > rr.maxW) rr.maxW = lineEnd - rect.LLX;
                    continue;
                }
            }
            rr.tb.AppendText(MakeLine(rr, rr.lines[i], rect.LLX, lineY));
            var w = Measure(rr, rr.lines[i]) * rr.sx;
            if (w > rr.maxW) rr.maxW = w;
        }
    }

    /// <summary>The original paragraph is removed line by line, then the absorbed region swept for what a fragmented line left behind.</summary>
    private void RemoveReflowedSource(ReflowRectangleState rr, Rectangle rect, string oldText)
    {

        // Remove the original paragraph: delete each source line operator at its
        // own baseline Y so a repeated substring elsewhere on the page is
        // untouched — then sweep the absorbed region for whatever a fragmented
        // multi-run line left behind (a paragraph drawn as dozens of kerned
        // sub-runs never deletes cleanly by text matching alone).
        DeleteReflowSource(rr.page, oldText);
        // The region sweep is a FALLBACK for a paragraph drawn as dozens of kerned
        // sub-runs, which text matching cannot delete cleanly. Run it only when the
        // delete actually left the source behind, and even then take whole text
        // blocks only, so a block that also carries neighbouring text survives.
        {
            double sLLX = double.MaxValue, sLLY = double.MaxValue;
            double sURX = double.MinValue, sURY = double.MinValue;
            if (AbsorbedRectangle is { } ab0)
            {
                sLLX = ab0.LLX; sLLY = ab0.LLY; sURX = ab0.URX; sURY = ab0.URY;
            }
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr) continue;
                if (sr.LLX < sLLX) sLLX = sr.LLX;
                if (sr.LLY < sLLY) sLLY = sr.LLY;
                if (sr.URX > sURX) sURX = sr.URX;
                if (sr.URY > sURY) sURY = sr.URY;
            }
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
                Console.Error.WriteLine($"[sweep] abs={(AbsorbedRectangle is null ? "null" : AbsorbedRectangle.ToString())} segs={_segments.Count} rect=({sLLX:F2},{sLLY:F2},{sURX:F2},{sURY:F2})");
            if (sURX > sLLX && sURY > sLLY)
            {
                var sweep = new Rectangle(sLLX - 1, sLLY - 1, sURX + 1, sURY + 1);
                // The sweep strips whole BT…ET blocks, so it can take neighbouring
                // text with it. It exists for the paragraph drawn as dozens of
                // kerned sub-runs that text matching cannot delete cleanly — so run
                // it only when ink actually SURVIVED inside the region, and never
                // when a neighbour would be caught in the same block.
                // A single-segment source was one plain run: the text-matched delete
                // above removes it whole, so sweeping the region only risks taking a
                // line-mate drawn in the same text block. Keep the sweep for the
                // many-sub-run paragraph it exists for.
                // ★ And only when that delete actually LEFT SOMETHING BEHIND. It reports
                // how many runs it removed, so a paragraph whose every segment was
                // matched and deleted has nothing left to sweep, and sweeping it anyway
                // destroys every line-mate sharing its text block — a six-glyph line
                // took the whole five-line box around it with it.
                if (_segments.Count > 1 && _unresolvedSegments > 0)
                    TableAbsorber.RemoveTableContent(rr.page, sweep, textOnly: true);

                // A rule the source drew UNDER this text belongs to it and goes with it —
                // left behind, it underscores whatever words now occupy that space, at the
                // old text's width. The band is the replaced text's OWN extent, and only a
                // path lying wholly inside it is claimed, so a page rule running beneath
                // the line (wider than the line) is not mistaken for its underline.
                var ulBand = new Rectangle(sLLX - 0.5, sLLY - 0.30 * rr.baseFs, sURX + 0.5, sLLY + 0.06 * rr.baseFs);
                TableAbsorber.RemoveTableContent(rr.page, ulBand, decorationOnly: true);
            }
        }

        rr.tb = new TextBuilder(rr.page);
        rr.maxW = 0;
    }

    /// <summary>The block's leading and first baseline: a resized block spreads over the whole rectangle, an unchanged one keeps the source seat.</summary>
    private void SeatReflowedBaselines(ReflowRectangleState rr, Rectangle rect)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[fit2] fs={rr.fs:F6} lines={rr.lines.Count} subst={rr.substFace ?? "none"} sysW={(rr.substWidths is not null)}");

        rr.leading = rr.leadingRatio * rr.fs;
        // A block whose size the fitter actually CHANGED distributes its lines over
        // the FULL rectangle — in both directions: the baseline pitch becomes the
        // rect height over the line count. A size the fitter left alone (the text
        // already fits, so nothing was shrunk) keeps the glyph size's own leading.
        // A FILL mode always paces its lines over the whole rectangle, and so does any
        // fit that actually changed the size. A shrink that found the text already
        // fitting leaves both the size and the block's own leading alone.
        if (rr.lines.Count > 1 && rect.Height > 0
            && (rr.fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                    or TextReplaceOptions.FontSizeAdjustment.Increase
                || (rr.fit is not TextReplaceOptions.FontSizeAdjustment.None
                    && Math.Abs(rr.fs - rr.baseFs) > 1e-6)))
            rr.leading = rect.Height / rr.lines.Count;
        rr.writtenFontName = rr.substFace ?? TextBuilder.MapToStandard14Public(TextState);
        rr.arialFace = rr.font.SourceFontData is null && rr.writtenFontName == "Helvetica"
            && IsArialFamily(TextState.FontName);
        if (rr.arialFace) rr.writtenFontName = "Arial";
        rr.descentOff = rr.font.SourceFontData is null
            ? Standard14Fonts.GetWrittenFaceDescent(rr.writtenFontName) * rr.fs / 1000.0
            : (rr.font.GetMetrics()?.Descent ?? -212) * rr.fs / 1000.0;
        rr.ascentH = rr.fs * 1.1 + rr.descentOff;
        rr.firstBaseline = rect.URY - rr.ascentH;
        rr.gridFirst = rr.firstBaseline;
        // When the ORIGINAL baseline grid is recoverable, continue from it (plus
        // whatever vertical shift the target rectangle carries relative to the
        // absorbed box) — the reflowed text keeps the source's exact first
        // baseline and pitch instead of re-derived approximations of them.
        // Read both straight from the block's own positioning operators; fall
        // back to the segment-position reconstruction.
        if (rr.fs == rr.baseFs && AbsorbedRectangle is { } abr)
        {
            var shift = rect.URY - abr.URY;
            var dSrc = SourceDescentUnits(rr.page) * rr.baseFs / 1000.0;
            if (dSrc != 0 && TryGetSourceTopBaseline(rr.page, abr) is { } srcTop
                && srcTop < abr.URY && srcTop > abr.URY - 3 * rr.fs)
            {
                // Corrected by the descent difference between the source face
                // and the written face, the re-absorbed box edges land where
                // the SOURCE font's box model puts them. On the re-fonted path
                // only the GRID gets that correction; the first line stays on
                // the source baseline, so the box top follows the written face.
                rr.gridFirst = srcTop + (dSrc - rr.descentOff) + shift;
                rr.firstBaseline = rr.arialFace ? srcTop + shift : rr.gridFirst;
            }
            else if (_segments.Count > 0 && _segments[1].Position is { } sp1)
            {
                var srcB1 = sp1.YIndent - dSrc;
                if (srcB1 < rect.URY && srcB1 > rect.URY - 3 * rr.fs)
                {
                    rr.firstBaseline = srcB1 + shift;
                    rr.gridFirst = rr.arialFace ? srcB1 + (dSrc - rr.descentOff) + shift : rr.firstBaseline;
                }
            }
        }
    }

    /// <summary>The measure functions of the written face and the font size that makes the text fit the rectangle.</summary>
    private void SolveReflowFontSize(ReflowRectangleState rr, Rectangle rect, string newText)
    {

        ReadReflowSourceMetrics(rr, newText);

        BuildReflowMeasurers(rr, newText);
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
        {
            double mNew = 0; try { mNew = rr.font.MeasureString(newText, rr.baseFs); } catch { }
            double mSrc = rr.srcMeasure is not null ? rr.srcMeasure(newText, rr.baseFs) : -1;
            Console.Error.WriteLine($"[fit] sx={rr.sx:F6} baseFs={rr.baseFs:F4} rect=({rect.LLX:F2},{rect.LLY:F2},{rect.URX:F2},{rect.URY:F2}) H={rect.Height:F3} wrapWidth={rr.wrapWidth:F3} scale={rr.measureScale:F4} wrapWidthM={rr.wrapWidthM:F3} hmtx={(rr.hmtxMeasure is not null)} measureFont={mNew:F3} measureSrc={mSrc:F3} fit={rr.fit}");
        }
        FitReflowFontSize(rr, rect, newText);
    }

    /// <summary>The wrap borders the replace options adjust, and the source block's own line pitch.</summary>
    /// <returns>The wrap rectangle after the left/right adjustments, unchanged when there are none.</returns>
    private Rectangle ReadReflowGeometry(ReflowRectangleState rr, Rectangle rect)
    {
        rr.leftAdj = _replaceOptions?.LeftAdjustment ?? 0;
        rr.rightAdj = _replaceOptions?.RightAdjustment ?? 0;
        if (rr.leftAdj != 0 || rr.rightAdj != 0)
            rect = new Rectangle(rect.LLX + rr.leftAdj, rect.LLY, rect.URX + rr.rightAdj, rect.URY);
        rr.justify = _replaceOptions is not null
            && (_replaceOptions.ReplaceAdjustmentAction
                & TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth) != 0;

        rr.leadingRatio = 1.2;
        if (_segments.Count >= 2 && _segments[1].Position is { } b1
            && _segments[_segments.Count].Position is { } bLast)
        {
            var l = (b1.YIndent - bLast.YIndent) / (_segments.Count - 1) / rr.baseFs;
            if (l > 0.5 && l < 3.0) rr.leadingRatio = l;
            else if (_segments[2].Position is { } b2)
            {
                l = (b1.YIndent - b2.YIndent) / rr.baseFs;
                if (l > 0.5 && l < 3.0) rr.leadingRatio = l;
            }
        }
        return rect;
    }

    /// <summary>Shrink-to-fit and scale-to-fill solve the size the wrapped text needs inside the rectangle.</summary>
    private void FitReflowFontSize(ReflowRectangleState rr, Rectangle rect, string newText)
    {
        rr.fs = rr.baseFs;
        if (rr.fit is TextReplaceOptions.FontSizeAdjustment.ShrinkToFit
                or TextReplaceOptions.FontSizeAdjustment.Decrease)
            rr.fs = FitFontSize(newText, rr.font, rr.wrapWidthM / rr.sx, rect.Height, 0.0, rr.baseFs, rr.leadingRatio, rr.hmtxMeasure);
        else if (rr.fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                or TextReplaceOptions.FontSizeAdjustment.Increase)
        {
            // ScaleToFill fills the rectangle in whichever direction it must: text
            // that already overruns the height at its own size scales DOWN to fit.
            // Increase only ever grows, so it keeps the one-sided window.
            var overruns = rr.fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                && BlockHeight(newText, rr.font, rr.baseFs, rr.wrapWidthM / rr.sx, rr.leadingRatio, rr.hmtxMeasure) > rect.Height;
            rr.fs = overruns
                ? FitFontSize(newText, rr.font, rr.wrapWidthM / rr.sx, rect.Height, 0.0, rr.baseFs, rr.leadingRatio, rr.hmtxMeasure)
                : FitFontSize(newText, rr.font, rr.wrapWidthM / rr.sx, rect.Height, rr.baseFs, 400.0, rr.leadingRatio, rr.hmtxMeasure);
        }
    }

    /// <summary>The substitute face, its widths and the source-page measurer the wrap will use.</summary>
    private void BuildReflowMeasurers(ReflowRectangleState rr, string newText)
    {
        rr.substFace = ResolveSubstituteFace(rr.font, newText);
        rr.coveringFace = rr.substFace is null ? null : CoveringSubstituteFace(rr.substFace, newText);
        rr.coveringGlyphs = rr.coveringFace is null ? null : CjkFallbackFont.ResolveNamed(rr.coveringFace);
        if (rr.coveringGlyphs is null) rr.coveringFace = null;

        rr.substWidths = rr.substFace is null || rr.font.Subtype != "Type0" || rr.coveringFace is not null
            ? null
            : SystemFaceWidths(rr.substFace);
        rr.substMeasure = rr.substFace is null
            ? null
            : rr.coveringFace is not null
            // The covering face is addressed by CHARACTER, not by byte code: the very
            // glyph that forced the switch lives outside any single-byte encoding.
            ? (str, size) =>
            {
                var upm = rr.coveringGlyphs!.UnitsPerEm > 0 ? rr.coveringGlyphs.UnitsPerEm : 1000;
                double w = 0;
                foreach (var ch in str)
                    w += rr.coveringGlyphs.GetAdvanceWidth(
                        rr.coveringGlyphs.CMap.TryGetValue(ch, out var g) ? g : 0) * size / upm;
                return w;
            }
            : rr.substWidths is null
            ? Standard14Measurer(rr.substFace)
            : (str, size) =>
            {
                double w = 0;
                foreach (var ch in str) w += rr.substWidths[ch < 256 ? ch : '?'] * size / 1000.0;
                return w;
            };

        rr.srcMeasure = BuildSourceMeasurer(rr.page);
        if (rr.substMeasure is not null) rr.srcMeasure = null;
        if (rr.srcMeasure is not null && rr.srcMeasure(newText.Replace(" ", ""), rr.baseFs) < 0)
            rr.srcMeasure = null; // source fonts can't encode the replacement
        // A width table that does not reproduce the widths of the text the page
        // actually drew with it is not the table the replacement should wrap by.
        if (rr.srcMeasure is not null)
        {
            double viaSrc = 0, drawn = 0;
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text) || s.Text.Length < 4) continue;
                var m = rr.srcMeasure(s.Text, rr.baseFs);
                if (m <= 0 || sr.Width <= 0) continue;
                viaSrc += m; drawn += sr.Width;
            }
            if (viaSrc > 1 && drawn > 1 && Math.Abs(viaSrc / drawn - 1.0) > 0.05)
                rr.srcMeasure = null;
        }

        // The fit/wrap measures run through the font; feed them the width the
        // MEASURE scale sees so the drawn result lands in the real rectangle.
        if (rr.substMeasure is not null) rr.hmtxMeasure = rr.substMeasure;
        rr.wrapWidthM = rr.hmtxMeasure is not null ? rr.wrapWidth : rr.wrapWidth / rr.measureScale;
    }

    /// <summary>The written face's own advances, read from the source font program when it has one.</summary>
    private void ReadReflowSourceMetrics(ReflowRectangleState rr, string newText)
    {
        rr.measureScale = 1.0;
        {
            double measured = 0, actual = 0;
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text) || s.Text.Length < 4) continue;
                double m;
                try { m = rr.font.MeasureString(s.Text, rr.baseFs); } catch { continue; }
                if (m <= 0 || sr.Width <= 0) continue;
                measured += m; actual += sr.Width;
            }
            if (measured > 1 && actual > 1)
            {
                var sc = actual / measured;
                // The calibration is there to correct a face measuring at about an em
                // a glyph. A face that already reproduces its own drawn text measures
                // TRUE, and nudging it by the sample's couple of percent only moves
                // the wrap off the break the real metrics put it on.
                if (sc > 0.2 && sc < 5) rr.measureScale = Math.Abs(sc - 1) <= 0.05 ? 1.0 : sc;
            }
        }

        rr.hmtxMeasure = null;
        {
            // Only a program the document actually carries: a font that ships none
            // keeps the calibrated width-table measure, whose scale the surrounding
            // flow is already tuned against.
            var ttfData = (TextState.FontData ?? TextState.Font?.SourceFontData)?.TtfData
                ?? TextState.Font?.GetEmbeddedProgramBytes();
            if (ttfData is not null)
            {
                try
                {
                    var gp = new Aspose.Pdf.Text.GlyphOutlineParser(ttfData);
                    var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
                    rr.hmtxMeasure = (str, size) =>
                    {
                        double w = 0;
                        foreach (var ch in str)
                            w += gp.GetAdvanceWidth(gp.CMap.TryGetValue(ch, out var g) ? g : 0);
                        return w * size / upm;
                    };
                }
                catch { /* unparseable program — fall back to the width tables */ }
            }
            // A subset program often carries only the glyphs it drew, so a cmap
            // lookup for the replacement's characters can come back empty and
            // measure everything at zero. Sanity-check against the width tables
            // and drop the program measure when it disagrees wildly.
            if (rr.hmtxMeasure is not null)
            {
                var probe = newText.Replace(" ", "");
                double viaProgram = rr.hmtxMeasure(probe, rr.baseFs), viaTables = 0;
                try { viaTables = rr.font.MeasureString(probe, rr.baseFs); } catch { }
                if (viaProgram <= 0
                    || (viaTables > 0 && (viaProgram / viaTables is < 0.5 or > 2.0)))
                    rr.hmtxMeasure = null;
            }
        }
    }
}
