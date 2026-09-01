using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
    /// <summary>Write <paramref name="fragment"/> as page content - appended as a new
    /// content-stream segment, or, with <paramref name="rewrite"/>, into that existing
    /// segment in place. On a rewrite every segment of a multi-segment fragment is
    /// written at its own position, a positionless one at the fragment origin: that
    /// is where a segment added after the append seats (its run then
    /// overlaps the first, and extraction reads the two back as one fragment of two
    /// segments with a space between them).</summary>
    private void WriteFragment(TextFragment fragment, bool addTrailingSpace, Core.PdfStream? rewrite)
    {
        var text = fragment.Text + (addTrailingSpace ? " " : "");

        // If FontData was set via implicit FontData→FontInfo conversion on TextState.Font,
        // propagate it to FontData so the font gets embedded properly.
        if (fragment.TextState.FontData is null && fragment.TextState.Font?.SourceFontData is { } srcFd)
            fragment.TextState.FontData = srcFd;

        // Route every embedded-TTF fragment through the CIDFont (Type0 /
        // Identity-H) path so glyph advances align with the font's own metrics.
        // Pitfall: the multi-segment branch below originally emitted
        // ShowText(literal) against the Identity-H font, producing
        // nonsense glyph IDs from each
        // pair of ASCII bytes. The branch now encodes each segment's text as
        // 2-byte glyph IDs via the same parser the fragment-level path uses,
        // so the CID route is safe for both single- and multi-segment
        // fragments. TextAbsorber.DecodeWithToUnicode round-trips the text
        // for extraction via the emitted /ToUnicode CMap.
        // A Bold/Italic FontStyle on a repository-resolved (non-core) face selects
        // the styled family member (Times New Roman + Bold|Italic → the Bold Italic
        // face); the embedded /BaseFont then reports
        // family+styles. Genuine Core-14 names keep the Standard-14 mapping below.
        if (ResolveStyledFace(fragment.TextState, fragment.TextState.FontData) is { } styledFace)
            fragment.TextState.FontData = styledFace;

        // A face whose licence does not cover embedding is not written into the file at
        // all. Settle that before a writer is chosen: reporting is raised here, and a
        // caller that switched reporting off keeps its save, the run falling to the
        // by-name Standard-14 path with the reason left on the face.
        var embeddableFontData = fragment.TextState.FontData is { TtfData: not null } licenceProbe
            && RefuseUnlicensedEmbedding(licenceProbe, _page) ? null : fragment.TextState.FontData;
        var needsCid = embeddableFontData is { TtfData: not null };

        // Arabic is cursive: the embedded-font path resolves each character through the
        // font's cmap to a single glyph, so the base letters must first be replaced with
        // their contextual presentation forms (and lam-alef ligatures) and reordered to
        // visual order. Without this an embedded Arabic font renders disjoint, isolated,
        // logical-order letters. Only the CID path benefits — Standard-14 fonts have no
        // Arabic glyphs regardless.
        if (needsCid)
            text = ArabicShaper.ShapeForDisplay(text);

        string fontResName;
        byte[]? hexGlyphIds = null;

        if (needsCid)
        {
            var fontData = embeddableFontData!;
            // A font whose cmap lacks glyphs for the text is
            // silently substituted with a covering host face (Thai → Tahoma, Han →
            // SimSun, …) — otherwise every missing char writes glyph 0 and the
            // duplicate ToUnicode entries garble extraction.
            if (!FontRepository.CoversText(fontData.TtfData, text))
            {
                var substitute = FontRepository.SubstituteForMissingGlyphs(text, fragment.TextState.Font);
                // A substitute takes over only when it covers MORE of the text than
                // the face the caller chose: a face missing two Romanian comma-below
                // letters keeps its run (they draw as notdef) instead of being traded
                // for a face that lacks every ideograph of the same run.
                if (substitute?.TtfData is not null
                    && FontRepository.CoverCount(substitute.TtfData, text)
                       > FontRepository.CoverCount(fontData.TtfData, text))
                    fontData = substitute;
            }
            (fontResName, hexGlyphIds) = EnsureEmbeddedCIDFont(fontData, text);
            // Per-LINE covering hand-off: a line the chosen
            // face cannot cover is drawn WHOLE in a covering host face (Times
            // New Roman first) with its own embedded font resource - an Arial
            // Unicode MS paragraph keeps its face while its Romanian comma-below
            // line comes back in Times. Only lines the face genuinely
            // cannot cover switch; the fragment's own face resumes after each.
            _cidLineOverrides = null;
            if (fontData.TtfData is { } baseTtf && text.IndexOf('\n') >= 0)
            {
                var probeLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (var li = 0; li < probeLines.Length; li++)
                {
                    var lineText = probeLines[li];
                    if (lineText.Length == 0 || FontRepository.CoversText(baseTtf, lineText)) continue;
                    if (FontRepository.ResolveCoveringFont(baseTtf, lineText) is not { SourceFontData.TtfData: not null } cover)
                        continue;
                    var (lineRes, lineHex) = EnsureEmbeddedCIDFont(cover.SourceFontData, lineText);
                    if (lineHex is not null)
                        (_cidLineOverrides ??= new())[li] = (lineRes, lineHex);
                }
            }
        }
        else if (embeddableFontData is { TtfData: not null } fontData2)
        {
            fontResName = EnsureEmbeddedTrueTypeFont(fontData2);
        }
        else
        {
            var baseFontName = fragment.TextState.Std14FaceOverride
                ?? MapToStandard14(fragment.TextState);
            fontResName = EnsureFontResource(baseFontName, fragment.TextState.EmitStandard14Descriptor,
                fragment.TextState.Std14Widths);
        }

        var fontSize = fragment.TextState.FontSize;
        var x = fragment.Position?.XIndent ?? 0;
        var y = fragment.Position?.YIndent ?? 0;

        // Compute descent compensation for embedded fonts.
        // The absorber's Position.Y = Td.Y + descent*fs/1000 (descent is negative),
        // so we must write Td.Y = user_y - descent*fs/1000 to round-trip correctly.
        double descentComp = needsCid
            ? ComputeCidDescentCompensation(fragment.TextState, fontSize)
            : ComputeDescentCompensation(fragment.TextState, fontSize);

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        // Tab-stop line: the text is a sequence of runs separated by #$TAB markers,
        // each run seated against its stop — ending at it, centred on it, or starting
        // from it — with the stop's leader drawn across the gap the tab opened.
        if (fragment.TabStops is { Count: > 0 } stops
            && fragment.Text.Contains(TabMarker, StringComparison.Ordinal))
        {
            AppendTabbedLine(builder, fragment, stops, fontResName, fontSize, x, y, descentComp);
            builder.RestoreState();
            if (rewrite is not null) rewrite.ReplaceData(builder.Build());
            else
            {
                _page.AddContentStream(builder.Build());
                fragment.AttachedSegment = _page.LastContentStreamSegment();
            }
            _page.ResetContentsCache();
            fragment.AttachedSignature = fragment.AttachedLayoutSignature();
            return;
        }

        // A fragment appended through the public API writes each segment as its OWN
        // run, seated at that segment's own position — segments do NOT flow after one
        // another, and one that was never positioned stays at the origin. The
        // fragment-level background is then ONE box
        // spanning every run. A fragment the LAYOUT ENGINE hands over is the opposite
        // case: its segments are pieces of one flowed line and are chained along it.
        if (fragment.Segments.Count > 1 && !fragment.AttachedInline)
        {
            var seats = BuildSegmentSeats(fragment, fontSize, x, y);
            EmitFragmentBackground(builder, fragment, seats);
            AppendSegmentRuns(builder, fragment, seats, fontResName, fontSize,
                descentComp, x, y, addTrailingSpace);
        }
        else if (fragment.Segments.Count > 1 && SegmentStylesDiffer(fragment, fontSize))
        {
            EmitBackgroundRectangles(builder, fragment, fontResName, fontSize, x, y);
            AppendStyledSegments(fragment, builder, fontResName, fontSize, descentComp, x, y);
        }
        else
        {
            EmitBackgroundRectangles(builder, fragment, fontResName, fontSize, x, y);
            var fg = fragment.TextState.ForegroundColor;
            if (fg?.PatternColorSpace is Aspose.Pdf.Drawing.GradientAxialShading grad)
            {
                // A gradient foreground paints the run through a PatternType-2 shading
                // pattern whose matrix spans the run's advance box, axis running in the
                // text's logical direction (an RTL run starts its gradient at the right).
                var patName = EmitTextGradientPattern(grad, fragment, text, hexGlyphIds, fontSize, x, y);
                if (patName is not null) builder.Raw($"/Pattern cs /{patName} scn\n");
                else builder.SetFillColor(0, 0, 0);
            }
            else if (fg is not null)
            {
                var fgGs = TextParagraph.EnsureFillAlphaExtGState(_page, fg.AByte);
                if (fgGs is not null) builder.SetExtGState(fgGs);
                builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            }

            var sc = fragment.TextState.StrokingColor;
            if (sc is not null)
                builder.SetStrokeColor(sc.R / 255.0, sc.G / 255.0, sc.B / 255.0);

            // A STROKING render mode also needs its pen: the default line width is a
            // full point, which at text sizes floods the glyphs into blobs. Producers
            // that fake a bold weight this way — filling and stroking the same regular
            // face rather than switching to a bold one — set the pen to a fraction of
            // the size, and the replacement has to keep that relationship for the run to
            // come back the same weight.
            var mode = fragment.TextState.RenderingMode;
            var strokes = mode is Aspose.Pdf.Text.TextRenderingMode.StrokeText
                or Aspose.Pdf.Text.TextRenderingMode.FillThenStrokeText
                or Aspose.Pdf.Text.TextRenderingMode.StrokeTextAndAddPathToClipping
                or Aspose.Pdf.Text.TextRenderingMode.FillThenStrokeTextAndAddPathToClipping;
            if (strokes) builder.SetLineWidth(fontSize / SyntheticBoldPenRatio);

            builder.BeginText();
            builder.SetFont(fontResName, fontSize);
            if (mode != Aspose.Pdf.Text.TextRenderingMode.FillText)
                builder.SetTextRenderingMode((int)mode);
            // Emit Tc/Tw so the requested character/word spacing is actually applied when
            // the page is rendered or re-parsed (without these operators the run renders at
            // default spacing). The fragment's own q/Q scope confines them to this run.
            if (fragment.TextState.CharacterSpacing != 0)
                builder.SetCharSpacing(fragment.TextState.CharacterSpacing);
            if (fragment.TextState.WordSpacing != 0)
                builder.SetWordSpacing(fragment.TextState.WordSpacing);
            // Horizontal scaling (Tz): stretch/compress the run's glyph advances. Without
            // this the renderer draws at 100% regardless of TextState.HorizontalScaling.
            if (Math.Abs(fragment.TextState.HorizontalScaling - 100) > 1e-9)
                builder.SetHorizontalScaling(fragment.TextState.HorizontalScaling);

            // \n inside the fragment text needs explicit T* breaks: PDF's Tj
            // operator renders the whole string on one line (newline chars are
            // either dropped or drawn as .notdef). Without splitting, a multi-
            // line input collapses to a single overflowing line.
            var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var hasNewlines = normalised.IndexOf('\n') >= 0;
            // An explicit LineSpacing is extra leading on top of the glyph height
            // (line pitch = fontSize + LineSpacing, matching the generator paginator);
            // otherwise fall back to the default 1.2x leading.
            var lineHeight = fragment.TextState.FlowLinePitch
                ?? (fragment.TextState.LineSpacing > 0
                    ? fontSize + fragment.TextState.LineSpacing
                    : fontSize * 1.2);
            if (hasNewlines) builder.SetLeading(lineHeight);
            // A non-zero TextState.Rotation rotates the run about its position via a
            // text matrix; the descent shift (applied straight down in the unrotated
            // case) is rotated to stay perpendicular to the rotated baseline.
            var rotation = fragment.TextState.Rotation;
            if (Math.Abs(rotation % 360.0) > 1e-9)
            {
                var rad = rotation * Math.PI / 180.0;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                builder.SetTextMatrix(cos, sin, -sin, cos,
                    x + descentComp * sin, y - descentComp * cos);
            }
            else if (Math.Abs(fragment.TextState.SourceTmScale - 1.0) > 1e-9)
            {
                // Text drawn under a horizontally scaled matrix: a replacement put in
                // its place carries the same scale, so it occupies the same width.
                builder.SetTextMatrix(fragment.TextState.SourceTmScale, 0, 0, 1, x, y - descentComp);
            }
            else
            {
                builder.MoveTextPosition(x, y - descentComp);
            }

            if (hexGlyphIds is not null)
            {
                // For CID fonts the hex glyph stream is byte-aligned (2 bytes/glyph)
                // but newlines came in as char positions, not byte positions. Re-build
                // per-line hex slices from the original text using the same mapping.
                if (hasNewlines)
                    WriteCidLinesWithBreaks(builder, fragment.TextState.FontData!, normalised,
                        fontResName, fontSize);
                else
                    builder.ShowTextHex(hexGlyphIds);
                _cidLineOverrides = null;
            }
            else
            {
                var lines = normalised.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) builder.NextLine();
                    if (lines[i].Length > 0) builder.ShowText(lines[i]);
                }
            }

            builder.EndText();
        }

        builder.RestoreState();

        var runBytes = builder.Build();
        if (rewrite is not null)
        {
            rewrite.ReplaceData(runBytes);
            // A previously materialised operator view of the page would flush the
            // stale operators back over the rewritten segment at save.
            _page.ResetContentsCache();
        }
        else if (fragment.TextState.MarkedContentTag is { } mcTag)
            _page.AddMarkedContentStream(runBytes, mcTag, fragment.TextState.MarkedContentMcid);
        else
        {
            _page.AddContentStream(runBytes);
            fragment.AttachedSegment = _page.LastContentStreamSegment();
        }
        fragment.AttachedSignature = fragment.AttachedLayoutSignature();
        // Record the fragment's LOGICAL text, not the display form: Arabic input
        // is shaped into presentation forms above, and storing the shaped string
        // makes the save-time sync see a phantom text change — TextReplacer then
        // re-writes the run without shaping.
        fragment.LastWrittenText = fragment.Text + (addTrailingSpace ? " " : "");
        if (rewrite is null) _page.RegisterAttachedFragment(fragment);

        // Underline/strikeout are drawn as thin rectangles at save time. The
        // TextState flags are typically set before the fragment is attached to a
        // page, so the property setters' own registration (which needs a SourcePage)
        // is skipped — register here now that the fragment lives on this page. The
        // flag may sit on the fragment's TextState or on any of its segments.
        // Only a REQUESTED underline is drawn. One the absorber merely observed under the
        // source describes the page as it already is — re-emitting it for a replacement
        // lays a second copy over the original rule.
        bool underline = fragment.TextState.UnderlineRequested;
        bool strikeOut = fragment.TextState.StrikeOut;
        if (fragment.Segments is { } segs)
        {
            foreach (var seg in segs)
            {
                if (seg.TextState.UnderlineRequested) underline = true;
                if (seg.TextState.StrikeOut) strikeOut = true;
            }
        }
        // A rotated run's decorations must rotate with it: the save-time rule
        // writer keys its rotation-aware path on TextDirX/Y, which only the
        // absorber populates — an APPENDED fragment carries its angle in
        // TextState.Rotation, so seed the direction from that (every rule is
        // drawn along the rotated baseline).
        if ((underline || strikeOut) && Math.Abs(fragment.TextState.Rotation % 360.0) > 1e-9)
        {
            var decRad = fragment.TextState.Rotation * Math.PI / 180.0;
            fragment.TextDirX = Math.Cos(decRad);
            fragment.TextDirY = Math.Sin(decRad);
        }
        if (underline) _page.RegisterUnderlineFragment(fragment);
        if (strikeOut) _page.RegisterStrikeOutFragment(fragment);
    }

    /// <summary>One appended segment's seat. <see cref="SegmentSeat.TmX"/>/<see cref="SegmentSeat.TmY"/>
    /// is where the run's text matrix is written: the segment's position plus the seat
    /// lift FROZEN when the segment was seated (a later font or size change patches
    /// the run's face, never its matrix — a run is re-seated only when its position is
    /// assigned again). Everything else is measured from the segment's CURRENT state,
    /// so the background box follows post-append edits.</summary>
    private readonly record struct SegmentSeat(
        TextSegment Segment, string Text, double TmX, double TmY,
        double FontSize, double CurrentLift, FontData? FontData, Color? Fill);

    /// <summary>A text highlight is this many ems tall, seated on the descender line.</summary>
    private const double BackgroundEmHeight = 1.1;

    /// <summary>The seat lift of <paramref name="segment"/> in
    /// <paramref name="fragment"/>: the descriptor descent the writer adds to the
    /// segment's position so the absorber reads that position back unchanged.
    /// Captured when the segment is seated — appended, added to an attached fragment,
    /// or repositioned — and frozen until it is seated again.</summary>
    internal static double SeatLiftFor(TextFragment fragment, TextSegment segment)
    {
        var (fs, fontData) = ResolveSegmentFace(fragment, segment);
        return SeatLift(fontData, fs);
    }

    /// <summary>Descriptor descent (as a positive lift) for a face at a size; zero for
    /// a Standard-14 face, which is written with no descriptor at all.</summary>
    private static double SeatLift(FontData? fontData, double fontSize)
    {
        if (fontData?.TtfData is null) return 0;
        var d = HheaDescentPerMille(fontData.TtfData);
        if (d == 0) d = FontRepository.ReadTtfMetrics(fontData.TtfData).descent;
        return -d * fontSize / 1000.0;
    }

    /// <summary>Seat every segment of <paramref name="fragment"/>. The first segment
    /// falls back to the fragment's own position; a later segment that was never
    /// positioned sits at the origin.</summary>
    private static List<SegmentSeat> BuildSegmentSeats(TextFragment fragment,
        double fragFontSize, double fragX, double fragY)
    {
        var seats = new List<SegmentSeat>(fragment.Segments.Count);
        var index = 0;
        foreach (var seg in fragment.Segments)
        {
            var (fs, fontData) = ResolveSegmentFace(fragment, seg);
            var lift = seg.SeatLift ??= SeatLift(fontData, fs);
            var px = seg.Position?.XIndent ?? (index == 0 ? fragX : 0);
            var py = seg.Position?.YIndent ?? (index == 0 ? fragY : 0);
            var fill = seg.TextState?.ForegroundColor ?? fragment.TextState.ForegroundColor;
            seats.Add(new SegmentSeat(seg, seg.Text ?? string.Empty, px, py + lift, fs,
                SeatLift(fontData, fs), fontData, fill));
            index++;
        }
        return seats;
    }

    /// <summary>Advance width of a seat's text in its own face.</summary>
    private static double SeatWidth(in SegmentSeat seat, TextFragment fragment, string text)
    {
        if (text.Length == 0) return 0;
        try
        {
            if (seat.FontData is { TtfData: not null } fd) return fd.MeasureString(text, seat.FontSize);
            var name = seat.Segment.TextState?.FontName ?? fragment.TextState.FontName;
            if (!string.IsNullOrEmpty(name) && Standard14Fonts.IsStandard14(name!))
            {
                double w = 0;
                foreach (var ch in text)
                {
                    var cw = Standard14Fonts.GetWidth(name!, ch < 256 ? ch : '?');
                    w += (cw >= 0 ? cw : 500) * seat.FontSize / 1000.0;
                }
                return w;
            }
        }
        catch { }
        return text.Length * seat.FontSize * 0.5;
    }

    /// <summary>The fragment-level background of a multi-segment fragment: ONE box
    /// spanning every seated run (their boxes unioned), written before the runs so the
    /// glyphs sit on top of it. Per-segment backgrounds are written by
    /// <see cref="AppendSegmentRuns"/>, each just before its own run.</summary>
    private void EmitFragmentBackground(ContentStreamBuilder builder, TextFragment fragment,
        List<SegmentSeat> seats)
    {
        var bg = fragment.TextState.BackgroundColor;
        // Color.Empty is a COLOUR, not a null: it is new(0,0,0,0), so a null check alone lets
        // it through and paints a fully opaque BLACK box behind the text. A caller assigning
        // Empty is asking for no background at all.
        if (bg is null || bg.IsEmpty) return;

        double llx = double.MaxValue, lly = double.MaxValue, urx = double.MinValue, ury = double.MinValue;
        var any = false;
        foreach (var seat in seats)
        {
            if (seat.Text.Length == 0) continue;
            var (bx, by, bw, bh) = SeatBox(seat, fragment);
            if (bw <= 0) continue;
            any = true;
            if (bx < llx) llx = bx;
            if (by < lly) lly = by;
            if (bx + bw > urx) urx = bx + bw;
            if (by + bh > ury) ury = by + bh;
        }
        if (!any) return;

        builder.SaveState();
        builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
        builder.SetMatrix(1, 0, 0, 1, llx, lly);
        builder.Rectangle(0, 0, urx - llx, ury - lly);
        builder.Fill();
        builder.RestoreState();
    }

    /// <summary>Write one run per segment, each in its own face and colour at its own
    /// seat, each preceded by its own background box when it carries one.</summary>
    private void AppendSegmentRuns(ContentStreamBuilder builder, TextFragment fragment,
        List<SegmentSeat> seats, string fragResName, double fragFontSize,
        double fragDescentComp, double fragX, double fragY, bool addTrailingSpace)
    {
        foreach (var seat in seats)
        {
            var segBg = seat.Segment.TextState?.BackgroundColor;
            if (segBg is { IsEmpty: false } && seat.Text.Length > 0)
            {
                var (bx, by, bw, bh) = SeatBox(seat, fragment);
                if (bw > 0)
                {
                    builder.SaveState();
                    builder.SetFillColor(segBg.R / 255.0, segBg.G / 255.0, segBg.B / 255.0);
                    builder.SetMatrix(1, 0, 0, 1, bx, by);
                    builder.Rectangle(0, 0, bw, bh);
                    builder.Fill();
                    builder.RestoreState();
                }
            }

            string resName;
            byte[]? hexIds = null;
            // Arabic is cursive: shape to contextual presentation forms in visual
            // order before the glyph lookup (a no-op for every other script).
            var segText = ArabicShaper.ShapeForDisplay(seat.Text);
            if (seat.FontData is { TtfData: not null })
            {
                (resName, hexIds) = EnsureEmbeddedCIDFont(seat.FontData, segText);
            }
            else if (seat.Segment.TextState is { } segState
                && (segState.FontName is not null || segState.Font is not null))
            {
                resName = EnsureFontResource(MapToStandard14(segState));
            }
            else
            {
                resName = fragResName;
            }

            if (seat.Fill is not null)
                builder.SetFillColor(seat.Fill.R / 255.0, seat.Fill.G / 255.0, seat.Fill.B / 255.0);
            var stroke = seat.Segment.TextState?.StrokingColor ?? fragment.TextState.StrokingColor;
            if (stroke is not null)
                builder.SetStrokeColor(stroke.R / 255.0, stroke.G / 255.0, stroke.B / 255.0);

            builder.BeginText();
            builder.SetFont(resName, seat.FontSize);
            // Character/word spacing, horizontal scaling and the rendering mode are
            // fragment-wide: every run carries them, or the page renders at defaults.
            var runState = seat.Segment.TextState ?? fragment.TextState;
            var charSpacing = runState.CharacterSpacing != 0
                ? runState.CharacterSpacing : fragment.TextState.CharacterSpacing;
            if (charSpacing != 0) builder.SetCharSpacing(charSpacing);
            var wordSpacing = runState.WordSpacing != 0
                ? runState.WordSpacing : fragment.TextState.WordSpacing;
            if (wordSpacing != 0) builder.SetWordSpacing(wordSpacing);
            var scaling = Math.Abs(runState.HorizontalScaling - 100) > 1e-9
                ? runState.HorizontalScaling : fragment.TextState.HorizontalScaling;
            if (Math.Abs(scaling - 100) > 1e-9) builder.SetHorizontalScaling(scaling);
            var renderMode = runState.RenderingMode != Aspose.Pdf.Text.TextRenderingMode.FillText
                ? runState.RenderingMode : fragment.TextState.RenderingMode;
            if (renderMode != Aspose.Pdf.Text.TextRenderingMode.FillText)
                builder.SetTextRenderingMode((int)renderMode);
            builder.MoveTextPosition(seat.TmX, seat.TmY);
            if (hexIds is not null) builder.ShowTextHex(hexIds);
            else builder.ShowText(segText);
            builder.EndText();
        }

        // Per-segment fragments write each segment separately, so a trailing word
        // space (added when copied fragments abut) needs its own glyph past the
        // fragment's right edge to survive re-extraction as a word boundary.
        if (!addTrailingSpace) return;
        var endX = fragment.Rectangle is { } rr ? rr.URX : fragX;
        builder.BeginText();
        builder.SetFont(fragResName, fragFontSize);
        builder.MoveTextPosition(endX, fragY - fragDescentComp);
        builder.ShowText(" ");
        builder.EndText();
    }

    /// <summary>The styled family member a Bold/Italic <paramref name="state"/>
    /// selects for a repository-resolved (non-core) face — Times New Roman +
    /// Bold|Italic is the Bold Italic face — or null when the state asks for no
    /// style, the family is a Core-14 name, the face is already styled, or no
    /// file really carrying the requested style exists (the resolver falls back
    /// to the regular face; that is not accepted). <paramref name="current"/> is
    /// the face the state resolves to now.</summary>
    internal static FontData? ResolveStyledFace(TextState state, FontData? current)
    {
        if (!state.IsBold && !state.IsItalic) return null;
        var family = current?.FontName ?? state.Font?.FontName ?? state.FontName;
        if (string.IsNullOrEmpty(family) || Standard14Fonts.IsCoreName(family)
            || family.Contains("Bold", StringComparison.OrdinalIgnoreCase)
            || family.Contains("Italic", StringComparison.OrdinalIgnoreCase))
            return null;
        var styleSuffix = (state.IsBold ? " Bold" : string.Empty)
            + (state.IsItalic ? " Italic" : string.Empty);
        var spaced = System.Text.RegularExpressions.Regex.Replace(family, "(?<=[a-z])(?=[A-Z])", " ");
        var styled = FontRepository.FindFontData(family + styleSuffix)
            ?? (spaced != family ? FontRepository.FindFontData(spaced + styleSuffix) : null);
        var wantTag = state.IsBold ? "Bold" : "Italic";
        return styled?.TtfData is not null
               && styled.FontName?.Contains(wantTag, StringComparison.OrdinalIgnoreCase) == true
            ? styled : null;
    }

    /// <summary>Register a PatternType-2 (axial shading) pattern spanning the run's
    /// advance box and return its resource name. The gradient axis runs in the text's
    /// logical direction, so an RTL run's gradient starts at its right edge.</summary>
    private string? EmitTextGradientPattern(Aspose.Pdf.Drawing.GradientAxialShading grad,
        TextFragment fragment, string shapedText, byte[]? hexGlyphIds, double fontSize, double x, double y)
    {
        var width = MeasureRunWidth(fragment, shapedText, hexGlyphIds, fontSize);
        if (width <= 0) return null;

        var rtl = ArabicShaper.ContainsArabic(fragment.Text);
        var pattern = new PdfDictionary();
        pattern.Set("Type", new PdfName("Pattern"));
        pattern.Set("PatternType", new PdfInteger(2));
        pattern.Set("Shading", Aspose.Pdf.Drawing.Shape.BuildAxialShadingDict(grad));
        var matrix = new PdfArray();
        foreach (var v in new[] { rtl ? -width : width, 0, 0, fontSize, rtl ? x + width : x, y })
            matrix.Add(new PdfReal(v));
        pattern.Set("Matrix", matrix);
        return _page.AddPattern(pattern);
    }

    /// <summary>Advance width of the run in points: embedded-font glyph advances for
    /// the CID path, Standard-14 AFM widths otherwise.</summary>
    private static double MeasureRunWidth(TextFragment fragment, string shapedText,
        byte[]? hexGlyphIds, double fontSize)
    {
        if (hexGlyphIds is not null && fragment.TextState.FontData?.TtfData is { } ttf)
        {
            try
            {
                var parser = new GlyphOutlineParser(ttf);
                double units = 0;
                for (var i = 0; i + 1 < hexGlyphIds.Length; i += 2)
                    units += parser.GetAdvanceWidth((hexGlyphIds[i] << 8) | hexGlyphIds[i + 1]);
                if (parser.UnitsPerEm > 0 && units > 0)
                    return units * fontSize / parser.UnitsPerEm;
            }
            catch
            {
                // Unparseable program — fall through to the AFM estimate.
            }
        }

        var name = fragment.TextState.FontName ?? "Helvetica";
        double sum = 0;
        foreach (var ch in shapedText)
        {
            var w = ch <= 255 ? Standard14Fonts.GetWidth(name, ch) : -1;
            if (w <= 0) w = 500;
            sum += w;
        }
        return sum * fontSize / 1000.0;
    }

    /// <summary>
    /// Emit a multi-line CID-encoded text run, breaking lines on '\n'. For each
    /// non-empty line, look up the per-character glyph IDs from the embedded TTF
    /// and write them as a 2-byte hex stream; emit a `T*` (next-line) between
    /// lines so the page-level leading set on the builder advances the cursor.
    /// </summary>
    /// <summary>The shaped glyph run for one CID line, or null when the line needs no
    /// shaping. Glyphs a substitution introduced (a conjunct, a reph) are registered with
    /// the embedder so they keep their /W advance and survive subsetting — nothing maps
    /// to them through a character.</summary>
    private static ushort[]? ShapeCidLine(FontData fontData, GlyphOutlineParser glyphParser, string line)
    {
        try
        {
            var shaped = OpenType.TextShaper.Shape(fontData.TtfData!, line,
                cp => (ushort)(glyphParser.CMap.TryGetValue(cp, out var g) ? g : 0));
            if (shaped is not null) Type0FontEmbedder.RegisterShapedGlyphs(fontData.TtfData!, shaped);
            return shaped;
        }
        catch
        {
            return null;
        }
    }

    private void WriteCidLinesWithBreaks(ContentStreamBuilder builder, FontData fontData, string normalised,
        string baseResName, double fontSize)
    {
        var glyphParser = new GlyphOutlineParser(fontData.TtfData!);
        var lines = normalised.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) builder.NextLine();
            if (lines[i].Length == 0) continue;
            if (_cidLineOverrides is not null && _cidLineOverrides.TryGetValue(i, out var over))
            {
                // The covering face draws this whole line; the fragment's own
                // face resumes on the next.
                builder.SetFont(over.res, fontSize);
                builder.ShowTextHex(over.hex);
                builder.SetFont(baseResName, fontSize);
                continue;
            }
            // Iterate by codepoint so surrogate pairs (emoji, CJK Ext-B) map to ONE glyph.
            var line = lines[i];
            var bytes = new System.Collections.Generic.List<byte>(line.Length * 2);
            // A complex script is not one glyph per codepoint — its conjuncts and reph
            // are substitutions the font supplies, and some marks precede the consonant
            // they follow in memory. Shape the line first when the face has rules for it.
            var shapedLine = ShapeCidLine(fontData, glyphParser, line);
            if (shapedLine is not null)
            {
                foreach (var gid in shapedLine)
                {
                    bytes.Add((byte)(gid >> 8));
                    bytes.Add((byte)(gid & 0xFF));
                }
                builder.ShowTextHex(bytes.ToArray());
                continue;
            }
            for (var k = 0; k < line.Length; k++)
            {
                int cp = line[k];
                if (char.IsHighSurrogate(line[k]) && k + 1 < line.Length && char.IsLowSurrogate(line[k + 1]))
                {
                    cp = char.ConvertToUtf32(line[k], line[k + 1]);
                    k++;
                }
                int gid = 0;
                if (glyphParser.CMap.TryGetValue(cp, out var mapped)) gid = mapped;
                bytes.Add((byte)(gid >> 8));
                bytes.Add((byte)(gid & 0xFF));
            }
            builder.ShowTextHex(bytes.ToArray());
        }
    }

    /// <summary>
    /// For each segment whose TextState.BackgroundColor is set, emit a filled
    /// rectangle behind the text. The rectangle's position comes from the
    /// segment's Position (or falls back to the fragment's position) and its
    /// width is estimated from glyph metrics. This must be called BEFORE
    /// BT so the rectangles render behind the glyphs.
    /// </summary>
    private void EmitBackgroundRectangles(ContentStreamBuilder builder, TextFragment fragment,
        string fontResName, double fontSize, double defaultX, double defaultY)
    {
        // Check fragment-level background first, then per-segment.
        // Segment-level overrides fragment-level.
        var fragBg = fragment.TextState.BackgroundColor;
        double curX = defaultX;
        double curY = defaultY;

        // Whether the render takes the per-segment styled path (each segment its
        // own font/size) or the uniform fragment path — the highlight measures
        // with the same state the glyphs are drawn with.
        var styledSegs = SegmentStylesDiffer(fragment, fontSize);

        foreach (var seg in fragment.Segments)
        {
            var bg = seg.TextState.BackgroundColor ?? fragBg;
            // Color.Empty is a colour, not a null - see EmitFragmentBackground.
            if (bg is { IsEmpty: false })
            {
                var segX = seg.Position?.XIndent ?? curX;
                var segY = seg.Position?.YIndent ?? curY;
                var segFs = styledSegs && seg.TextState.FontSize > 0 ? seg.TextState.FontSize : fontSize;

                var fontName = (styledSegs ? seg.TextState.FontName : null) ?? fragment.TextState.FontName;
                var fd = (styledSegs ? seg.TextState.FontData ?? seg.TextState.Font?.SourceFontData : null)
                    ?? fragment.TextState.FontData ?? fragment.TextState.Font?.SourceFontData;
                double LineWidth(string s)
                {
                    if (fd is { TtfData: not null })
                        return fd.MeasureString(s, segFs);
                    if (!string.IsNullOrEmpty(fontName) && Standard14Fonts.IsStandard14(fontName!))
                    {
                        double w = 0;
                        foreach (var ch in s)
                        {
                            var cw = Standard14Fonts.GetWidth(fontName!, ch < 256 ? ch : '?');
                            w += (cw >= 0 ? cw : 500) * segFs / 1000.0;
                        }
                        return w;
                    }
                    return s.Length * segFs * 0.5; // proportional fallback
                }

                // The box: 1.1 em tall, bottom at the fragment's anchor Y (the
                // descent line the position round-trip is anchored to) — the same
                // shape the paragraph flow emits. The rect is written in a LOCAL
                // frame placed by a cm (rotation folded in), colour set before the
                // cm so the cm immediately precedes the rectangle operands.
                var rectH = segFs * 1.1;
                // Line pitch for a multi-line (\n-joined) chunk — matches the AppendText render.
                var bgLineHeight = fragment.TextState.FlowLinePitch
                    ?? (seg.TextState.LineSpacing > 0 ? segFs + seg.TextState.LineSpacing
                    : fragment.TextState.LineSpacing > 0 ? segFs + fragment.TextState.LineSpacing
                    : segFs * 1.2);

                var rotation = seg.TextState.Rotation != 0 ? seg.TextState.Rotation : fragment.TextState.Rotation;
                var rad = rotation * Math.PI / 180.0;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                // One filled rectangle behind each rendered line (a chunk from the
                // paginator arrives as its wrapped lines joined by \n). A multi-line
                // block tiles its rectangles at the full line pitch so the highlight
                // is continuous; a single line uses the 1.1-em box.
                var segLines = seg.Text.Replace("\r\n", "\n").Split('\n');
                var rh = segLines.Length > 1 ? bgLineHeight : rectH;
                // DrawTextRectangleBorder strokes the same box in the state's
                // stroking colour — the colour pair is written before the matrix,
                // and the box is painted with one fill-and-stroke.
                var borderColor = fragment.TextState.DrawTextRectangleBorder
                    ? seg.TextState.StrokingColor ?? fragment.TextState.StrokingColor
                    : null;
                for (var li = 0; li < segLines.Length; li++)
                {
                    var lw = LineWidth(segLines[li]);
                    if (lw <= 0) continue;
                    builder.SaveState();
                    builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                    if (borderColor is not null)
                        builder.SetStrokeColor(borderColor.R / 255.0, borderColor.G / 255.0, borderColor.B / 255.0);
                    builder.SetMatrix(cos, sin, -sin, cos, segX, segY - li * bgLineHeight);
                    builder.Rectangle(0, 0, lw, rh);
                    if (borderColor is not null) builder.FillAndStrokeEvenOdd();
                    else builder.Fill();
                    builder.RestoreState();
                }
            }
            // Advance curX/curY for next segment (basic horizontal flow)
            curX += seg.Text.Length * fontSize * 0.5;
        }
    }

    /// <summary>
    /// Compute the descent offset (in points) that the absorber will add to Position.Y.
    /// Returns descentOffset = descent * fontSize / 1000 (negative for typical fonts).
    /// For Standard14 fonts (no font descriptor in PDF), the absorber does not apply
    /// descent to Position, so this returns 0.
    /// </summary>
    /// <summary>Descent compensation for the CID (Type0) embed path. The Type0
    /// descriptor writes the hhea descender, so the writer must
    /// compensate with the SAME value the absorber will read back — using the OS/2
    /// typographic descent here would shift every round-tripped Y by their delta.</summary>
    internal static double ComputeCidDescentCompensation(TextState state, double fontSize)
    {
        if (state.FontData is { TtfData: not null } fd)
        {
            var d = HheaDescentPerMille(fd.TtfData);
            if (d != 0) return d * fontSize / 1000.0;
        }
        return ComputeDescentCompensation(state, fontSize);
    }

    /// <summary>hhea descender normalized to a 1000-unit em (0 when unparsable).</summary>
    internal static double HheaDescentPerMille(byte[] ttf)
    {
        try
        {
            var ttp = new TrueTypeParser(ttf);
            ttp.Parse();
            if (ttp.Descent != 0 && ttp.UnitsPerEm > 0)
                // Truncate exactly like the Type0 descriptor writer does — the
                // absorber reads the INTEGER /Descent back, and the compensation
                // must cancel it to the last hundredth of a point.
                return (int)((double)ttp.Descent * 1000 / ttp.UnitsPerEm);
        }
        catch { }
        return 0;
    }

    private static double ComputeDescentCompensation(TextState state, double fontSize)
    {
        // Embedded TrueType fonts have a descriptor with Descent — absorber uses it.
        if (state.FontData is { TtfData: not null } fontData)
        {
            var (_, descent, _, _) = FontRepository.ReadTtfMetrics(fontData.TtfData);
            if (descent != 0)
                return descent * fontSize / 1000.0;
        }
        // Standard14 fonts: absorber only applies descent when run.Metrics is not null
        // and Descent != 0. For bare Type1 Standard14 font dicts (no descriptor),
        // run.Metrics comes from the font descriptor — which we don't emit for Standard14.
        // So no compensation needed. (The hOCR overlay opts into descriptor emission
        // via TextState.EmitStandard14Descriptor and lifts its baselines itself.)
        return 0;
    }
}
