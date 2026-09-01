
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
        var page = SourcePage!;
        var font = TextState.Font!;
        double baseFs = TextState.FontSize;
        if (rect.Width <= 1 || rect.Height <= 1 || string.IsNullOrEmpty(newText)) return false;

        // Left/RightAdjustment extend the wrap borders (a negative left
        // adjustment widens leftward, a positive right one rightward).
        var leftAdj = _replaceOptions?.LeftAdjustment ?? 0;
        var rightAdj = _replaceOptions?.RightAdjustment ?? 0;
        if (leftAdj != 0 || rightAdj != 0)
            rect = new Rectangle(rect.LLX + leftAdj, rect.LLY, rect.URX + rightAdj, rect.URY);
        // AdjustSpaceWidth justifies every wrapped line but the last to the
        // wrap width by widening the inter-word gaps.
        var justify = _replaceOptions is not null
            && (_replaceOptions.ReplaceAdjustmentAction
                & TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth) != 0;

        // Derive the line pitch (baseline-to-baseline) from the fragment's current
        // multi-line layout so the reflowed block keeps the same leading —
        // averaged over the whole first-to-last span, so the per-segment
        // position quantization doesn't accumulate over the reflowed lines.
        double leadingRatio = 1.2;
        if (_segments.Count >= 2 && _segments[1].Position is { } b1
            && _segments[_segments.Count].Position is { } bLast)
        {
            var l = (b1.YIndent - bLast.YIndent) / (_segments.Count - 1) / baseFs;
            if (l > 0.5 && l < 3.0) leadingRatio = l;
            else if (_segments[2].Position is { } b2)
            {
                l = (b1.YIndent - b2.YIndent) / baseFs;
                if (l > 0.5 && l < 3.0) leadingRatio = l;
            }
        }

        double wrapWidth = rect.Width;
        // The source run's text matrix scales every advance it draws, so the width a
        // line occupies on the page is its advance sum times that scale. Measuring
        // and wrapping happen in the font's own space; only the page-space results
        // carry the factor.
        var sx = TextState.SourceTmScale is > 0.01 and < 100 ? TextState.SourceTmScale : 1.0;
        var wrapWidthT = wrapWidth / sx;
        var fit = _replaceOptions?.FontSizeAdjustmentAction ?? TextReplaceOptions.FontSizeAdjustment.None;

        // Same text into a same-size rectangle is a pure TRANSLATION: keep the
        // original line structure (strings, pitch, per-line widths) and move it.
        // Re-wrapping would put every measurement error of a metrics-less font
        // into the block's shape; translation cancels them all.
        // The matched text carries the source line breaks; the replacement has
        // plain spaces — compare whitespace-folded.
        static string FoldWs(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        if (fit is TextReplaceOptions.FontSizeAdjustment.None
            && string.Equals(FoldWs(oldText), FoldWs(newText), StringComparison.Ordinal)
            && _segments.Count > 0
            && _segments.All(s => s.Position is not null && s.Rectangle is not null
                && !string.IsNullOrEmpty(s.Text))
            && TryTranslateBlock(page, oldText, newText, rect, baseFs))
        {
            return true;
        }

        // A font resolved without its real width table measures every glyph near
        // an em wide; the original segments carry their TRUE drawn widths, so
        // calibrate the measure against them before wrapping.
        double measureScale = 1.0;
        {
            double measured = 0, actual = 0;
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text) || s.Text.Length < 4) continue;
                double m;
                try { m = font.MeasureString(s.Text, baseFs); } catch { continue; }
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
                if (sc > 0.2 && sc < 5) measureScale = Math.Abs(sc - 1) <= 0.05 ? 1.0 : sc;
            }
        }

        // An embedded font program measures through its OWN advance table (hmtx at
        // its own units-per-em) — the width table the wrap measurement works from.
        // The PDF /Widths array carries the same advances rounded to integer
        // 1000ths of an em, and that rounding is enough to move a fitted size off
        // by a tenth of a point.
        Func<string, double, double>? hmtxMeasure = null;
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
                    hmtxMeasure = (str, size) =>
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
            if (hmtxMeasure is not null)
            {
                var probe = newText.Replace(" ", "");
                double viaProgram = hmtxMeasure(probe, baseFs), viaTables = 0;
                try { viaTables = font.MeasureString(probe, baseFs); } catch { }
                if (viaProgram <= 0
                    || (viaTables > 0 && (viaProgram / viaTables is < 0.5 or > 2.0)))
                    hmtxMeasure = null;
            }
        }

        // A source font that cannot encode the replacement (a Japanese face asked to
        // show Latin, a subset carrying only its own glyphs) is SUBSTITUTED, and the
        // substitute is the serif default — the replacement measures and writes in
        // Times, not in the Helvetica the family map falls back to. Detected by the
        // width table itself: a face with no widths for these characters reports
        // about a full em each.
        var substFace = ResolveSubstituteFace(font, newText);
        // A stand-in has to be able to SHOW the replacement. The serif default carries
        // Latin, Greek, Cyrillic and the common symbols, but not a dingbat like '★', a
        // circled numeral, kana or han — and ONE character it has no glyph for re-dresses
        // the WHOLE run in a face that does. That is why a line of ordinary English can
        // come back measured at half-width advances: the covering face sets its Latin at
        // half an em, so a run led by one star fits a size well below the serif one.
        var coveringFace = substFace is null ? null : CoveringSubstituteFace(substFace, newText);
        var coveringGlyphs = coveringFace is null ? null : CjkFallbackFont.ResolveNamed(coveringFace);
        if (coveringGlyphs is null) coveringFace = null;

        // The stand-in follows the SHAPE of the font it stands in for. Replacing a CID
        // font's run produces a composite stand-in carrying the installed face's own
        // advances; standing in for a simple font keeps a simple one, whose widths are
        // the core table's. The extent the page reports back follows that choice, so
        // the measure has to make it too.
        var substWidths = substFace is null || font.Subtype != "Type0" || coveringFace is not null
            ? null
            : SystemFaceWidths(substFace);
        var substMeasure = substFace is null
            ? null
            : coveringFace is not null
            // The covering face is addressed by CHARACTER, not by byte code: the very
            // glyph that forced the switch lives outside any single-byte encoding.
            ? (str, size) =>
            {
                var upm = coveringGlyphs!.UnitsPerEm > 0 ? coveringGlyphs.UnitsPerEm : 1000;
                double w = 0;
                foreach (var ch in str)
                    w += coveringGlyphs.GetAdvanceWidth(
                        coveringGlyphs.CMap.TryGetValue(ch, out var g) ? g : 0) * size / upm;
                return w;
            }
            : substWidths is null
            ? Standard14Measurer(substFace)
            : (str, size) =>
            {
                double w = 0;
                foreach (var ch in str) w += substWidths[ch < 256 ? ch : '?'] * size / 1000.0;
                return w;
            };

        // Prefer measuring with the source resources' own width tables; the
        // calibrated host-face measure (wrapWidthM compensates its scale) is
        // the fallback.
        var srcMeasure = BuildSourceMeasurer(page);
        if (substMeasure is not null) srcMeasure = null;
        if (srcMeasure is not null && srcMeasure(newText.Replace(" ", ""), baseFs) < 0)
            srcMeasure = null; // source fonts can't encode the replacement
        // A width table that does not reproduce the widths of the text the page
        // actually drew with it is not the table the replacement should wrap by.
        if (srcMeasure is not null)
        {
            double viaSrc = 0, drawn = 0;
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text) || s.Text.Length < 4) continue;
                var m = srcMeasure(s.Text, baseFs);
                if (m <= 0 || sr.Width <= 0) continue;
                viaSrc += m; drawn += sr.Width;
            }
            if (viaSrc > 1 && drawn > 1 && Math.Abs(viaSrc / drawn - 1.0) > 0.05)
                srcMeasure = null;
        }

        // The fit/wrap measures run through the font; feed them the width the
        // MEASURE scale sees so the drawn result lands in the real rectangle.
        if (substMeasure is not null) hmtxMeasure = substMeasure;
        var wrapWidthM = hmtxMeasure is not null ? wrapWidth : wrapWidth / measureScale;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
        {
            double mNew = 0; try { mNew = font.MeasureString(newText, baseFs); } catch { }
            double mSrc = srcMeasure is not null ? srcMeasure(newText, baseFs) : -1;
            Console.Error.WriteLine($"[fit] sx={sx:F6} baseFs={baseFs:F4} rect=({rect.LLX:F2},{rect.LLY:F2},{rect.URX:F2},{rect.URY:F2}) H={rect.Height:F3} wrapWidth={wrapWidth:F3} scale={measureScale:F4} wrapWidthM={wrapWidthM:F3} hmtx={(hmtxMeasure is not null)} measureFont={mNew:F3} measureSrc={mSrc:F3} fit={fit}");
        }
        double fs = baseFs;
        if (fit is TextReplaceOptions.FontSizeAdjustment.ShrinkToFit
                or TextReplaceOptions.FontSizeAdjustment.Decrease)
            fs = FitFontSize(newText, font, wrapWidthM / sx, rect.Height, 0.0, baseFs, leadingRatio, hmtxMeasure);
        else if (fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                or TextReplaceOptions.FontSizeAdjustment.Increase)
        {
            // ScaleToFill fills the rectangle in whichever direction it must: text
            // that already overruns the height at its own size scales DOWN to fit.
            // Increase only ever grows, so it keeps the one-sided window.
            var overruns = fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                && BlockHeight(newText, font, baseFs, wrapWidthM / sx, leadingRatio, hmtxMeasure) > rect.Height;
            fs = overruns
                ? FitFontSize(newText, font, wrapWidthM / sx, rect.Height, 0.0, baseFs, leadingRatio, hmtxMeasure)
                : FitFontSize(newText, font, wrapWidthM / sx, rect.Height, baseFs, 400.0, leadingRatio, hmtxMeasure);
        }

        // Wrapped lines keep the break's inter-word space (the last line ends at
        // its final word), so each re-absorbed line extent includes the trailing
        // space advance — the same extent the source lines report.
        var lines = hmtxMeasure is not null
            ? WrapToWidth(newText, font, fs, wrapWidthT, trailingSpace: true, measure: s => hmtxMeasure(s, fs))
            : srcMeasure is not null
            ? WrapToWidth(newText, font, fs, wrapWidthT, trailingSpace: true, measure: s => srcMeasure(s, fs))
            : WrapToWidth(newText, font, fs, wrapWidthM / sx, trailingSpace: true);
        if (lines.Count == 0) return false;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[fit2] fs={fs:F6} lines={lines.Count} subst={substFace ?? "none"} sysW={(substWidths is not null)}");

        double leading = leadingRatio * fs;
        // A block whose size the fitter actually CHANGED distributes its lines over
        // the FULL rectangle — in both directions: the baseline pitch becomes the
        // rect height over the line count. A size the fitter left alone (the text
        // already fits, so nothing was shrunk) keeps the glyph size's own leading.
        // A FILL mode always paces its lines over the whole rectangle, and so does any
        // fit that actually changed the size. A shrink that found the text already
        // fitting leaves both the size and the block's own leading alone.
        if (lines.Count > 1 && rect.Height > 0
            && (fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                    or TextReplaceOptions.FontSizeAdjustment.Increase
                || (fit is not TextReplaceOptions.FontSizeAdjustment.None
                    && Math.Abs(fs - baseFs) > 1e-6)))
            leading = rect.Height / lines.Count;
        // Anchor the block so its re-absorbed top matches rect.URY. The wrapped
        // lines are written through TextBuilder, which maps a non-embedded font
        // to a Standard-14 face; TextFragmentAbsorber then reconstructs that
        // run's box as URY = baseline + (1.1·fs + descentOff) and
        // LLY = baseline + descentOff (descentOff negative). Use the WRITTEN
        // font's descent (not the original run's ascent) so the anchor lines up.
        var writtenFontName = substFace ?? TextBuilder.MapToStandard14Public(TextState);
        // An Arial-family source is re-fonted with the system Arial face rather
        // than the Helvetica AFM face: the written resource carries Arial's own
        // vertical metrics (hhea descender -434 in a 2048 em, truncated to -211)
        // in a FontDescriptor, and the anchor math below runs on that descent.
        var arialFace = font.SourceFontData is null && writtenFontName == "Helvetica"
            && IsArialFamily(TextState.FontName);
        if (arialFace) writtenFontName = "Arial";
        double descentOff = font.SourceFontData is null
            ? Standard14Fonts.GetWrittenFaceDescent(writtenFontName) * fs / 1000.0
            : (font.GetMetrics()?.Descent ?? -212) * fs / 1000.0;
        double ascentH = fs * 1.1 + descentOff;
        double firstBaseline = rect.URY - ascentH;
        // Baseline grid for the SECOND and later lines. On the re-fonted path the
        // first line keeps the source baseline itself while the rest of the block
        // sits on the descent-corrected grid — the two differ by exactly the
        // source-vs-written descent delta, so the re-absorbed box top follows the
        // written face while the bottom stays on the source box model.
        double gridFirst = firstBaseline;
        // When the ORIGINAL baseline grid is recoverable, continue from it (plus
        // whatever vertical shift the target rectangle carries relative to the
        // absorbed box) — the reflowed text keeps the source's exact first
        // baseline and pitch instead of re-derived approximations of them.
        // Read both straight from the block's own positioning operators; fall
        // back to the segment-position reconstruction.
        if (fs == baseFs && AbsorbedRectangle is { } abr)
        {
            var shift = rect.URY - abr.URY;
            var dSrc = SourceDescentUnits(page) * baseFs / 1000.0;
            if (dSrc != 0 && TryGetSourceTopBaseline(page, abr, out var srcTop)
                && srcTop < abr.URY && srcTop > abr.URY - 3 * fs)
            {
                // Corrected by the descent difference between the source face
                // and the written face, the re-absorbed box edges land where
                // the SOURCE font's box model puts them. On the re-fonted path
                // only the GRID gets that correction; the first line stays on
                // the source baseline, so the box top follows the written face.
                gridFirst = srcTop + (dSrc - descentOff) + shift;
                firstBaseline = arialFace ? srcTop + shift : gridFirst;
            }
            else if (_segments.Count > 0 && _segments[1].Position is { } sp1)
            {
                var srcB1 = sp1.YIndent - dSrc;
                if (srcB1 < rect.URY && srcB1 > rect.URY - 3 * fs)
                {
                    firstBaseline = srcB1 + shift;
                    gridFirst = arialFace ? srcB1 + (dSrc - descentOff) + shift : firstBaseline;
                }
            }
        }

        // Remove the original paragraph: delete each source line operator at its
        // own baseline Y so a repeated substring elsewhere on the page is
        // untouched — then sweep the absorbed region for whatever a fragmented
        // multi-run line left behind (a paragraph drawn as dozens of kerned
        // sub-runs never deletes cleanly by text matching alone).
        DeleteReflowSource(page, oldText);
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
                    TableAbsorber.RemoveTableContent(page, sweep, textOnly: true);

                // A rule the source drew UNDER this text belongs to it and goes with it —
                // left behind, it underscores whatever words now occupy that space, at the
                // old text's width. The band is the replaced text's OWN extent, and only a
                // path lying wholly inside it is claimed, so a page rule running beneath
                // the line (wider than the line) is not mistaken for its underline.
                var ulBand = new Rectangle(sLLX - 0.5, sLLY - 0.30 * baseFs, sURX + 0.5, sLLY + 0.06 * baseFs);
                TableAbsorber.RemoveTableContent(page, ulBand, decorationOnly: true);
            }
        }

        // Write the wrapped lines as positioned fragments.
        var tb = new TextBuilder(page);
        double maxW = 0;
        TextFragment MakeLine(string text, double x, double y)
        {
            var lf = new TextFragment(text);
            lf.TextState.Font = font;
            lf.TextState.FontName = TextState.FontName;
            lf.TextState.FontSize = (float)fs;
            lf.TextState.IsBold = TextState.IsBold;
            lf.TextState.IsItalic = TextState.IsItalic;
            // Weight the source got by STROKING its own outline travels with the
            // replacement: the stand-in is the regular face either way, so dropping the
            // mode is what turns a bold heading into a light one.
            lf.TextState.RenderingMode = TextState.RenderingMode;
            if (TextState.StrokingColor is { } strokeCol) lf.TextState.StrokingColor = strokeCol;
            if (TextState.ForegroundColor is { } fg) lf.TextState.ForegroundColor = fg;
            if (arialFace)
            {
                lf.TextState.Std14FaceOverride = writtenFontName;
                lf.TextState.EmitStandard14Descriptor = true;
                // Written in the system face, the resource carries that face's own
                // advances — the same choice already made for its vertical metrics.
                // Arial is drawn on a 2048-unit em, so its advances are not whole
                // 1000ths, and the core table's rounded ones make the extent read
                // back a twentieth of a point short over a paragraph.
                lf.TextState.Std14Widths = SystemFaceWidths(writtenFontName);
            }
            else if (substFace is not null)
            {
                lf.TextState.Std14FaceOverride = substFace;
                lf.TextState.Std14Widths = substWidths;
            }
            lf.TextState.SourceTmScale = sx;
            lf.Position = new Position(x, y);
            return lf;
        }
        double Measure(string s)
        {
            // The written face measures the result: a substituted run reports the
            // stand-in's advances, not the source font's.
            if (hmtxMeasure is not null) return hmtxMeasure(s, fs);
            if (srcMeasure is not null)
            {
                var v = srcMeasure(s, fs);
                if (v >= 0) return v;
            }
            try { return font.MeasureString(s, fs) * measureScale; }
            catch { return s.Length * fs * 0.5; }
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var lineY = i == 0 ? firstBaseline : gridFirst - i * leading;
            var words = justify
                ? lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries)
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
                for (var wi = 0; wi < words.Length; wi++) { wordW[wi] = Measure(words[wi]); inkW += wordW[wi]; }
                var lastLine = i == lines.Count - 1;
                var gap = lastLine
                    ? Measure(" ")
                    : (wrapWidthT - inkW) / (words.Length - 1);
                if (gap > 0)
                {
                    // Justification seats the last NON-SPACE character on the wrap
                    // edge; the break's own space then hangs past it at its natural
                    // advance. The last line is not justified and ends at its final
                    // word. The final run's reported right edge is its start plus
                    // the WRITTEN face's advance, so anchor it by that.
                    var lineEnd = lastLine
                        ? rect.LLX + (inkW + gap * (words.Length - 1)) * sx
                        : rect.LLX + wrapWidth + Measure(" ") * sx;
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
                            if (gw > 0) w0 += gw * fs / 1000.0;
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
                        if (wWritten <= 0) { wWritten = wordW[^1] + Measure(" "); break; }
                        if (wWritten >= fs || lastCount >= words.Length) break;
                        lastCount++;
                    }

                    var x = 0.0;
                    for (var wi = 0; wi < words.Length - lastCount; wi++)
                    {
                        tb.AppendText(MakeLine(words[wi], rect.LLX + x * sx, lineY));
                        x += wordW[wi] + gap;
                    }
                    tb.AppendText(MakeLine(lastText, lineEnd - wWritten * sx, lineY));
                    if (lineEnd - rect.LLX > maxW) maxW = lineEnd - rect.LLX;
                    continue;
                }
            }
            tb.AppendText(MakeLine(lines[i], rect.LLX, lineY));
            var w = Measure(lines[i]) * sx;
            if (w > maxW) maxW = w;
        }

        // The delete (SetContentStream) + line writes (AddContentStream) edited the
        // raw /Contents; drop any materialised typed-operator view so a later
        // page.Contents use (and save) re-reads the reflowed content.
        page.ResetContentsCache();

        // Update this fragment to the laid-out block (matching the absorber's
        // baseline+descentOff floor and baseline+ascentH top).
        double lastBaseline = lines.Count == 1
            ? firstBaseline
            : gridFirst - (lines.Count - 1) * leading;
        // ★ The reported box is ONE LINE deep whatever the block wraps to: the extent a
        // replaced paragraph reports back is its top edge less a single line's height,
        // not the height of the lines it actually drew. A block that fills its rectangle
        // reports the same box as one that half-fills it, which is why every case here
        // wants h = leading·fs exactly. The DRAWN lines are untouched — this is what the
        // paragraph reports, not where its text sits.
        var reportedURY = arialFace ? firstBaseline + ascentH : rect.URY;
        // The reported line box is 1.1 em — the face-independent line height a replaced
        // paragraph reports back, NOT the flow's own leading (which is 1.2 here and is
        // what spaces the drawn lines apart).
        const double reportedLineHeightEm = 1.1;
        var singleLineLLY = reportedURY - reportedLineHeightEm * fs;
        // Keep the drawn-block floor when it is the SHALLOWER of the two: a one-line
        // block already reports its own line, and a source whose descent puts the floor
        // above that must not be pushed down by the nominal line height.
        var reportedLLY = lines.Count == 1
            ? Math.Max(lastBaseline + descentOff, singleLineLLY)
            : singleLineLLY;
        _rectangle = new Rectangle(rect.LLX, reportedLLY, rect.LLX + maxW, reportedURY);
        _text = newText;
        _segments.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var seg = new TextSegment(lines[i]);
            seg.TextState.FontSize = (float)fs;
            seg.TextState.FontName = TextState.FontName;
            seg.TextState.Font = font;
            seg.Owner = this;
            seg.Position = new Position(rect.LLX, i == 0 ? firstBaseline : gridFirst - i * leading);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
        return true;
    }

    /// <summary>Pure translation of an unchanged paragraph into a same-size
    /// rectangle: shift the block's own positioning operators (Tm, and a
    /// leading Td straight after BT) by the move delta, leaving the show
    /// strings, fonts, and kerning untouched — the re-absorbed block reproduces
    /// the original geometry exactly, moved by the shift.</summary>
    private bool TryTranslateBlock(Page page, string oldText, string newText, Rectangle rect, double baseFs)
    {
        // The caller may have handed the fragment's own Rectangle instance back
        // as the target (mutated in place) — recover the PRE-shift geometry from
        // the absorbed-box snapshot (exact), falling back to the segment union.
        double obLLX = double.MaxValue, obLLY = double.MaxValue;
        double obURX = double.MinValue, obURY = double.MinValue;
        if (AbsorbedRectangle is { } ab)
        {
            obLLX = ab.LLX; obLLY = ab.LLY; obURX = ab.URX; obURY = ab.URY;
        }
        else
        {
            foreach (var s in _segments)
            {
                var sr = s.Rectangle!;
                if (sr.LLX < obLLX) obLLX = sr.LLX;
                if (sr.LLY < obLLY) obLLY = sr.LLY;
                if (sr.URX > obURX) obURX = sr.URX;
                if (sr.URY > obURY) obURY = sr.URY;
            }
        }
        var dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_GRIDDEBUG") == "1";
        if (Math.Abs(rect.Width - (obURX - obLLX)) >= 0.5
            || Math.Abs(rect.Height - (obURY - obLLY)) >= 0.5)
        {
            if (dbg) Console.Error.WriteLine($"[xlate] size mismatch rect={rect.Width:F2}x{rect.Height:F2} ob={(obURX - obLLX):F2}x{(obURY - obLLY):F2}");
            return false;
        }

        var dx = rect.LLX - obLLX;
        var dy = rect.URY - obURY;

        // Region gate for positioning ops: the block's own coordinates, padded
        // for the baseline-vs-rect-bottom offset. Anything else on the page
        // stays untouched.
        var padX = 2.0;
        var padY = Math.Max(4.0, baseFs * 0.5);
        bool InRegion(double x, double y) =>
            x >= obLLX - padX && x <= obURX + padX
            && y >= obLLY - padY && y <= obURY + padY;

        var shifted = 0;
        var afterBt = false;
        var toReplace = new List<Aspose.Pdf.Operators.SetTextMatrix>();
        // Materialize so the enumerated instances are the collection's own
        // (mutations persist and Index is stamped for the delete/insert swap).
        page.Contents.EnsureMaterialized();
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case Aspose.Pdf.Operators.BT:
                    afterBt = true;
                    break;
                case Aspose.Pdf.Operators.SetTextMatrix tm
                    when Math.Abs(tm.A - 1) < 1e-6 && Math.Abs(tm.B) < 1e-6
                        && Math.Abs(tm.C) < 1e-6 && Math.Abs(tm.D - 1) < 1e-6
                        && InRegion(tm.E, tm.F):
                    toReplace.Add(tm);
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.MoveTextPosition td
                    when afterBt && InRegion(td.X, td.Y):
                    // Td straight after BT is absolute (line matrix = identity).
                    td.X += dx;
                    td.Y += dy;
                    shifted++;
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.TextPlaceOperator:
                    afterBt = false;
                    break;
            }
        }
        foreach (var tm in toReplace)
        {
            var at = tm.Index;
            page.Contents.Delete(new Aspose.Pdf.Operator[] { tm });
            page.Contents.Insert(at, new Aspose.Pdf.Operators.SetTextMatrix(
                tm.A, tm.B, tm.C, tm.D, tm.E + dx, tm.F + dy));
            shifted++;
        }
        if (shifted == 0)
        {
            if (dbg) Console.Error.WriteLine("[xlate] no positioning ops found in region");
            return false;
        }
        page.Contents.FlushToPage();
        if (dbg) Console.Error.WriteLine($"[xlate] shifted {shifted} positioning ops by ({dx:F2},{dy:F2})");

        _rectangle = new Rectangle(obLLX + dx, obLLY + dy, obURX + dx, obURY + dy);
        _text = newText;
        foreach (var s in _segments)
        {
            s.Position = new Position(s.Position!.XIndent + dx, s.Position.YIndent + dy);
            s.Rectangle = new Rectangle(s.Rectangle!.LLX + dx, s.Rectangle.LLY + dy,
                s.Rectangle.URX + dx, s.Rectangle.URY + dy);
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
        if (_rectangle is null || _position is null || string.IsNullOrEmpty(newText))
            return false;
        var font = TextState.Font;
        double fs = TextState.FontSize;
        if (font is null || fs <= 0) return false;
        double width = _rectangle.Width;
        if (width < 2) return false;

        // Pick the face the lines are WRITTEN in: the source font when it covers the
        // replacement, else the same family's system face, else Times New Roman.
        static bool Covers(Aspose.Pdf.Text.Font f, string s)
        {
            foreach (var ch in s)
                if (!char.IsWhiteSpace(ch) && !f.CanRepresent(ch)) return false;
            return true;
        }
        var writeFont = font;
        if (!Covers(writeFont, newText))
        {
            var fam = font.FontName ?? string.Empty;
            int plus = fam.IndexOf('+');
            if (plus >= 0 && plus + 1 < fam.Length) fam = fam[(plus + 1)..];
            int comma = fam.IndexOf(',');
            if (comma > 0) fam = fam[..comma];
            Aspose.Pdf.Text.Font? sys = null;
            if (fam.Length > 0)
                try { sys = FontRepository.TryFindFont(fam, ignoreCase: true); } catch { }
            if (sys is null || !Covers(sys, newText))
                try { sys = FontRepository.TryFindFont("Times New Roman", ignoreCase: true); } catch { }
            if (sys is null || !Covers(sys, newText)) return false;
            writeFont = sys;
        }

        // Greedy wrap at spaces against the fragment width, measuring each candidate
        // line WITH its trailing separator space (which non-final lines keep). A
        // FindFont-produced face has stub FontInfo metrics (no font dict); its REAL
        // advances live on the attached FontData's raw TTF widths.
        double Measure(string s)
        {
            try
            {
                if (writeFont.SourceFontData is { } fd) return fd.MeasureString(s, fs);
                return writeFont.MeasureString(s, fs);
            }
            catch { return s.Length * fs * 0.5; }
        }
        var wrapped = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var word in newText.Split(' '))
        {
            if (word.Length == 0) continue;
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || Measure(trial + " ") <= width)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
                continue;
            }
            wrapped.Add(cur.ToString() + " ");
            cur.Clear();
            cur.Append(word);
        }
        if (cur.Length > 0) wrapped.Add(cur.ToString());
        if (wrapped.Count <= 1) return false; // fits: ordinary in-place replace

        // Delete the source operator(s) by REGION — every text op starting inside the
        // fragment's own rect span at its baseline. (Trailing padding spaces in the
        // same operator go with it; nothing observable asserts them.)
        var del = new TextReplacer
        {
            MatchAnyOperator = true,
            TargetX = (_rectangle.LLX + _rectangle.URX) / 2,
            TargetXTolerance = width / 2 + 1.0,
        };
        var targetYs = new System.Collections.Generic.List<double>();
        var baseY = (BaselinePosition ?? _position).YIndent;
        targetYs.Add(baseY);
        if (System.Math.Abs(_position.YIndent - baseY) > 0.01) targetYs.Add(_position.YIndent);
        foreach (var ty in targetYs)
        {
            del.TargetY = ty;
            del.Replace(page, string.Empty, string.Empty);
            if (del.ReplacementCount > 0) break;
        }
        if (del.ReplacementCount == 0) return false;

        // Emit the wrapped lines.
        var tb = new TextBuilder(page);
        double x0 = _rectangle.LLX;
        double y0 = _position.YIndent;
        double step = 1.2 * fs;
        var laidOut = new System.Collections.Generic.List<(string text, double by, double w)>();
        double maxLineW = 0;
        for (int i = 0; i < wrapped.Count; i++)
        {
            double by = y0 - i * step;
            var frag = new TextFragment(wrapped[i]);
            frag.TextState.Font = writeFont;
            frag.TextState.FontSize = (float)fs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(x0, by);
            tb.AppendText(frag);
            double lw = Measure(wrapped[i]);
            laidOut.Add((wrapped[i], by, lw));
            if (lw > maxLineW) maxLineW = lw;
        }
        page.ResetContentsCache();

        // Re-point this fragment at the laid-out block (mirrors the whole-paragraph
        // reflow): box LLY = last baseline, URY = first baseline + 1.1·fs.
        _rectangle = new Rectangle(x0, laidOut[^1].by, x0 + maxLineW, laidOut[0].by + 1.1 * fs);
        _segments.Clear();
        foreach (var ln in laidOut)
        {
            var seg = new TextSegment(ln.text);
            seg.TextState.FontSize = (float)fs;
            seg.TextState.FontName = writeFont.FontName;
            seg.TextState.Font = writeFont;
            seg.Owner = this;
            seg.Position = new Position(x0, ln.by);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
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
