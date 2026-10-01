using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
    /// <summary>Close the run, write or rewrite it on the page, record the fragment's signature and text, and register its underline and strike-out.</summary>
    private void FinishFragment(FragmentWriteState fw, TextFragment fragment, bool addTrailingSpace, Core.PdfStream? rewrite)
    {
        fw.builder.RestoreState();

        fw.runBytes = fw.builder.Build();
        if (rewrite is not null)
        {
            rewrite.ReplaceData(fw.runBytes);
            // A previously materialised operator view of the page would flush the
            // stale operators back over the rewritten segment at save.
            _page.ResetContentsCache();
        }
        else if (ContentSink is { } sink)
        {
            // Handed to the caller: there is no page segment to attach to, and the
            // buffer it goes into is raw bytes with no marked-content merge point.
            sink(fw.runBytes);
        }
        else if (fragment.TextState.MarkedContentTag is { } mcTag)
            _page.AddMarkedContentStream(fw.runBytes, mcTag, fragment.TextState.MarkedContentMcid);
        else
        {
            _page.AddContentStream(fw.runBytes);
            fragment.AttachedSegment = _page.LastContentStreamSegment();
        }
        fragment.AttachedSignature = fragment.AttachedLayoutSignature();
        // Record the fragment's LOGICAL text, not the display form: Arabic input
        // is shaped into presentation forms above, and storing the shaped string
        // makes the save-time sync see a phantom text change — TextReplacer then
        // re-writes the run without shaping.
        fragment.LastWrittenText = fragment.Text + (addTrailingSpace ? " " : "");
        if (rewrite is null) _page.RegisterAttachedFragment(fragment);

        fw.underline = fragment.TextState.UnderlineRequested;
        fw.strikeOut = fragment.TextState.StrikeOut;
        if (fragment.Segments is { } segs)
        {
            foreach (var seg in segs)
            {
                if (seg.TextState.UnderlineRequested) fw.underline = true;
                if (seg.TextState.StrikeOut) fw.strikeOut = true;
            }
        }
        // A rotated run's decorations must rotate with it: the save-time rule
        // writer keys its rotation-aware path on TextDirX/Y, which only the
        // absorber populates — an APPENDED fragment carries its angle in
        // TextState.Rotation, so seed the direction from that (every rule is
        // drawn along the rotated baseline).
        if ((fw.underline || fw.strikeOut) && Math.Abs(fragment.TextState.Rotation % 360.0) > 1e-9)
        {
            var decRad = fragment.TextState.Rotation * Math.PI / 180.0;
            fragment.TextDirX = Math.Cos(decRad);
            fragment.TextDirY = Math.Sin(decRad);
        }
        if (fw.underline) _page.RegisterUnderlineFragment(fragment);
        if (fw.strikeOut) _page.RegisterStrikeOutFragment(fragment);
    }

    /// <summary>Seat the run: the descent compensation, the content builder and the tab stops; a tab-only run is done here.</summary>
    private bool TrySeatFragment(FragmentWriteState fw, TextFragment fragment, Core.PdfStream? rewrite)
    {
        fw.descentComp = fw.needsCid
            ? ComputeCidDescentCompensation(fragment.TextState, fw.fontSize)
            : ComputeDescentCompensation(fragment.TextState, fw.fontSize);

        fw.builder = new ContentStreamBuilder();
        fw.builder.SaveState();

        // Tab-stop line: the text is a sequence of runs separated by #$TAB markers,
        // each run seated against its stop — ending at it, centred on it, or starting
        // from it — with the stop's leader drawn across the gap the tab opened.
        if (fragment.TabStops is { Count: > 0 } stops
            && fragment.Text.Contains(TabMarker, StringComparison.Ordinal))
        {
            AppendTabbedLine(fw.builder, fragment, stops, fw.fontResName, fw.fontSize, fw.x, fw.y, fw.descentComp);
            fw.builder.RestoreState();
            if (rewrite is not null) rewrite.ReplaceData(fw.builder.Build());
            else
            {
                _page.AddContentStream(fw.builder.Build());
                fragment.AttachedSegment = _page.LastContentStreamSegment();
            }
            _page.ResetContentsCache();
            fragment.AttachedSignature = fragment.AttachedLayoutSignature();
            return false;
        }

        // A fragment appended through the public API writes each segment as its OWN
        // run, seated at that segment's own position — segments do NOT flow after one
        // another, and one that was never positioned stays at the origin. The
        // fragment-level background is then ONE box
        // spanning every run. A fragment the LAYOUT ENGINE hands over is the opposite
        // case: its segments are pieces of one flowed line and are chained along it.
        return true;
    }

    /// <summary>Resolve the font resource: an embedded CID subset with its glyph ids, an embeddable face, or the standard face.</summary>
    private bool TryResolveFragmentFont(FragmentWriteState fw, TextFragment fragment)
    {
        // A generated paragraph reads left to right: its Arabic runs flip in place and
        // the segments keep their order (measured: Arabic, ".NET not arabic", Arabic
        // draw at x 90, 199, 296 - not the reverse).
        if (fw.needsCid)
            fw.text = ArabicShaper.ShapeForLtrParagraph(fw.text);

        fw.hexGlyphIds = null;

        if (fw.needsCid)
        {
            var fontData = fw.embeddableFontData!;
            // A font whose cmap lacks glyphs for the text is
            // silently substituted with a covering host face (Thai → Tahoma, Han →
            // SimSun, …) — otherwise every missing char writes glyph 0 and the
            // duplicate ToUnicode entries garble extraction.
            if (!FontRepository.CoversText(fontData.TtfData, fw.text))
            {
                var substitute = FontRepository.SubstituteForMissingGlyphs(fw.text, fragment.TextState.Font);
                // A substitute takes over only when it covers MORE of the text than
                // the face the caller chose: a face missing two Romanian comma-below
                // letters keeps its run (they draw as notdef) instead of being traded
                // for a face that lacks every ideograph of the same run.
                if (substitute?.TtfData is not null
                    && FontRepository.CoverCount(substitute.TtfData, fw.text)
                       > FontRepository.CoverCount(fontData.TtfData, fw.text))
                    fontData = substitute;
            }
            (fw.fontResName, fw.hexGlyphIds) = EnsureEmbeddedCIDFont(fontData, fw.text);
            // Per-LINE covering hand-off: a line the chosen
            // face cannot cover is drawn WHOLE in a covering host face (Times
            // New Roman first) with its own embedded font resource - an Arial
            // Unicode MS paragraph keeps its face while its Romanian comma-below
            // line comes back in Times. Only lines the face genuinely
            // cannot cover switch; the fragment's own face resumes after each.
            _cidLineOverrides = null;
            if (fontData.TtfData is { } baseTtf && fw.text.IndexOf('\n') >= 0)
            {
                var probeLines = fw.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
        else if (fw.embeddableFontData is { TtfData: not null } fontData2)
        {
            fw.fontResName = EnsureEmbeddedTrueTypeFont(fontData2);
        }
        else
        {
            var baseFontName = fragment.TextState.Std14FaceOverride
                ?? MapToStandard14(fragment.TextState);
            fw.fontResName = EnsureFontResource(baseFontName, fragment.TextState.EmitStandard14Descriptor,
                fragment.TextState.Std14Widths);
        }

        fw.fontSize = (float)ScriptSize(fragment.TextState, fragment.TextState.FontSize);
        fw.x = fragment.Position?.XIndent ?? 0;
        fw.y = fragment.Position?.YIndent ?? 0;

        return true;
    }

    /// <summary>The run text and the face it draws with: a styled face for the text state when one applies.</summary>
    private void ResolveFragmentFace(FragmentWriteState fw, TextFragment fragment, bool addTrailingSpace)
    {
        fw.text = fragment.Text + (addTrailingSpace ? " " : "");

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

        fw.embeddableFontData = fragment.TextState.FontData is { TtfData: not null } licenceProbe
            && RefuseUnlicensedEmbedding(licenceProbe, _page) ? null : fragment.TextState.FontData;
        fw.needsCid = fw.embeddableFontData is { TtfData: not null };

        // Arabic is cursive: the embedded-font path resolves each character through the
        // font's cmap to a single glyph, so the base letters must first be replaced with
        // their contextual presentation forms (and lam-alef ligatures) and reordered to
        // visual order. Without this an embedded Arabic font renders disjoint, isolated,
        // logical-order letters. Only the CID path benefits — Standard-14 fonts have no
        // Arabic glyphs regardless.
    }

    /// <summary>Write the fragment's segments run by run: each segment's own face, size, colour, rise and spacing, with the inline attachments.</summary>
    private void WriteMixedSegments(FragmentWriteState fw, TextFragment fragment)
    {
        var mw = new MixedSegmentsState();
        mw.fw = fw;
        mw.fragment = fragment;
        EmitBackgroundRectangles(mw.fw.builder, mw.fragment, mw.fw.fontResName, mw.fw.fontSize, mw.fw.x, mw.fw.y);
        mw.fg = mw.fragment.TextState.ForegroundColor;
        if (mw.fg?.PatternColorSpace is Aspose.Pdf.Drawing.GradientAxialShading grad)
        {
            // A gradient foreground paints the run through a PatternType-2 shading
            // pattern whose matrix spans the run's advance box, axis running in the
            // text's logical direction (an RTL run starts its gradient at the right).
            var patName = EmitTextGradientPattern(grad, mw.fragment, mw.fw.text, mw.fw.hexGlyphIds, mw.fw.fontSize, mw.fw.x, mw.fw.y);
            if (patName is not null) mw.fw.builder.Raw($"/Pattern cs /{patName} scn\n");
            else mw.fw.builder.SetFillColor(0, 0, 0);
        }
        else if (mw.fg is not null)
        {
            var fgGs = TextParagraph.EnsureFillAlphaExtGState(_page, mw.fg.AByte);
            if (fgGs is not null) mw.fw.builder.SetExtGState(fgGs);
            mw.fw.builder.SetFillColor(mw.fg.R / 255.0, mw.fg.G / 255.0, mw.fg.B / 255.0);
        }

        mw.sc = mw.fragment.TextState.StrokingColor;
        if (mw.sc is not null)
            mw.fw.builder.SetStrokeColor(mw.sc.R / 255.0, mw.sc.G / 255.0, mw.sc.B / 255.0);

        mw.mode = mw.fragment.TextState.RenderingMode;
        mw.strokes = mw.mode is Aspose.Pdf.Text.TextRenderingMode.StrokeText
            or Aspose.Pdf.Text.TextRenderingMode.FillThenStrokeText
            or Aspose.Pdf.Text.TextRenderingMode.StrokeTextAndAddPathToClipping
            or Aspose.Pdf.Text.TextRenderingMode.FillThenStrokeTextAndAddPathToClipping;
        // A pen the state names wins over the size-derived one.
        if (mw.strokes) mw.fw.builder.SetLineWidth(mw.fragment.TextState.LineWidth != 1.0 ? mw.fragment.TextState.LineWidth : mw.fw.fontSize / SyntheticBoldPenRatio);

        mw.fw.builder.BeginText();
        mw.fw.builder.SetFont(mw.fw.fontResName, mw.fw.fontSize);
        if (ScriptRise(mw.fragment.TextState, mw.fragment.TextState.FontSize) is not 0 and var rise)
            mw.fw.builder.SetTextRise(rise);
        if (mw.mode != Aspose.Pdf.Text.TextRenderingMode.FillText)
            mw.fw.builder.SetTextRenderingMode((int)mw.mode);
        // Emit Tc/Tw so the requested character/word spacing is actually applied when
        // the page is rendered or re-parsed (without these operators the run renders at
        // default spacing). The fragment's own q/Q scope confines them to this run.
        if (mw.fragment.TextState.CharacterSpacing != 0)
            mw.fw.builder.SetCharSpacing(mw.fragment.TextState.CharacterSpacing);
        // A composite font carries its word spacing glyph by glyph (see
        // CompositeWordSpacing); only a simple font takes it from the Tw operator.
        if (mw.fragment.TextState.WordSpacing != 0 && mw.fw.hexGlyphIds is null)
            mw.fw.builder.SetWordSpacing(mw.fragment.TextState.WordSpacing);
        // Horizontal scaling (Tz): stretch/compress the run's glyph advances. Without
        // this the renderer draws at 100% regardless of TextState.HorizontalScaling.
        if (Math.Abs(mw.fragment.TextState.HorizontalScaling - 100) > 1e-9)
            mw.fw.builder.SetHorizontalScaling(mw.fragment.TextState.HorizontalScaling);

        mw.normalised = mw.fw.text.Replace("\r\n", "\n").Replace('\r', '\n');
        mw.hasNewlines = mw.normalised.IndexOf('\n') >= 0;
        mw.lineHeight = mw.fragment.TextState.FlowLinePitch
            ?? (mw.fragment.TextState.LineSpacing > 0
                ? mw.fw.fontSize + mw.fragment.TextState.LineSpacing
                : mw.fw.fontSize * 1.2);
        if (mw.hasNewlines) mw.fw.builder.SetLeading(mw.lineHeight);
        mw.rotation = mw.fragment.TextState.Rotation;
        if (Math.Abs(mw.rotation % 360.0) > 1e-9)
        {
            var rad = mw.rotation * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            mw.fw.builder.SetTextMatrix(cos, sin, -sin, cos,
                mw.fw.x + mw.fw.descentComp * sin, mw.fw.y - mw.fw.descentComp * cos);
        }
        else if (Math.Abs(mw.fragment.TextState.SourceTmScale - 1.0) > 1e-9)
        {
            // Text drawn under a horizontally scaled matrix: a replacement put in
            // its place carries the same scale, so it occupies the same width.
            mw.fw.builder.SetTextMatrix(mw.fragment.TextState.SourceTmScale, 0, 0, 1, mw.fw.x, mw.fw.y - mw.fw.descentComp);
        }
        else if (ShearOf(mw.fragment.TextState) is var (slope, skew) && (slope != 0 || skew != 0))
        {
            // A sheared upright face: c leans the verticals, b tilts the baseline -- the
            // matrix the flow's own writer sets for a synthetic italic or a skew.
            mw.fw.builder.SetTextMatrix(1, slope, skew, 1, mw.fw.x, mw.fw.y - mw.fw.descentComp);
        }
        else
        {
            mw.fw.builder.MoveTextPosition(mw.fw.x, mw.fw.y - mw.fw.descentComp);
        }

        if (mw.fw.hexGlyphIds is not null)
        {
            // For CID fonts the hex glyph stream is byte-aligned (2 bytes/glyph)
            // but newlines came in as char positions, not byte positions. Re-build
            // per-line hex slices from the original text using the same mapping.
            if (mw.hasNewlines)
                WriteCidLinesWithBreaks(mw.fw.builder, mw.fragment.TextState.FontData!, mw.normalised,
                    mw.fw.fontResName, mw.fw.fontSize, mw.fragment.TextState.WordSpacing);
            else
                ShowComposite(mw.fw.builder, mw.fw.hexGlyphIds,
                    CompositeWordSpacing(mw.fw.text, mw.fw.hexGlyphIds, mw.fragment.TextState.WordSpacing, mw.fw.fontSize));
            _cidLineOverrides = null;
        }
        else
        {
            var lines = mw.normalised.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) mw.fw.builder.NextLine();
                if (lines[i].Length > 0) mw.fw.builder.ShowText(lines[i]);
            }
        }

        mw.fw.builder.EndText();
    }
}
