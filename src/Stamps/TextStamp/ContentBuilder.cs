using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Stamps;

public partial class TextStamp
{
    /// <summary>Draw the whole stamp with an embedded Type0 replacement font (Identity-H +
    /// ToUnicode), so Unicode/CJK text both renders and round-trips through text extraction.
    /// Handles multi-line text: rows stack downward one em apart, each aligned within the
    /// block per <see cref="TextAlignment"/>, the block placed per the stamp alignments
    /// (margins honoured) — measured with the embedded font's /W advances so extraction
    /// re-measures the exact same widths.</summary>
    private byte[] BuildCidStamp(Page page, byte[] ttf, string fontName, double fontSize, Color color)
    {
        var fontDict = GetPageFontDict(page);
        var wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;
        var x0 = XIndent > 0 ? XIndent : LeftMargin;

        // Wrap budget: the stamp Width when declared, else the page's own width
        // (probed: a wrapped stamp with no Width fills 49 × 12 pt rows in a 595 page).
        var budget = WrapWidth > 0 ? WrapWidth : Math.Max(0.0, page.Width - x0);

        // Display rows: the explicit '\n' lines, each re-filled against the budget when
        // wrapping is on. The fill is GREEDY AT NATURAL ADVANCES with no kinsoku —
        // DiscretionaryHyphenation breaks anywhere (mid-word), the word modes at spaces
        // (probed; see the CID wrap law).
        var charLevel = WordWrapMode == TextFormattingOptions.WordWrapMode.DiscretionaryHyphenation;
        var displayRows = new System.Collections.Generic.List<System.Collections.Generic.List<(byte[] ttf, string name, string text)>>();
        foreach (var sourceRow in Text.Replace("\r\n", "\n").Split('\n'))
        {
            var runs = SplitFontRuns(sourceRow, ttf, fontName);
            if (wrapping && budget > 0)
                displayRows.AddRange(FillCidRows(fontDict, runs, fontSize, budget, charLevel));
            else
                displayRows.Add(runs);
        }

        var n = displayRows.Count;
        var rowRuns = new System.Collections.Generic.List<(string res, byte[] hex, double width)>[n];
        var widths = new double[n];
        for (var i = 0; i < n; i++)
        {
            rowRuns[i] = new();
            foreach (var (runTtf, runName, runText) in displayRows[i])
            {
                var (res, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, runTtf, runName, runText, stripSpacesInBaseFont: true);
                var w = Aspose.Pdf.Text.Type0FontEmbedder.MeasureText(
                    fontDict, runTtf, runName, runText, fontSize, stripSpacesInBaseFont: true);
                rowRuns[i].Add((res, hex, w));
                widths[i] += w;
            }
        }
        var naturalBlockW = n > 0 ? widths.Max() : 0.0;

        // Probed block scaling: sx = Width / widest row (squeezes as well as stretches),
        // on BY DEFAULT and off only under an explicit Scale=false; sy = Height /
        // ((rows + 0.1) · fs) — the rows never re-flow at the scaled size.
        var sx = Width > 0 && naturalBlockW > 0 && CidScaleEnabled ? Width / naturalBlockW : 1.0;
        var sy = Height > 0 && CidScaleEnabled ? Height / ((n + 0.1) * fontSize) : 1.0;
        var pitch = fontSize * sy;
        var blockW = naturalBlockW * sx;

        // Block placement: alignment when set (margins honoured), else XIndent/YIndent
        // with the block BOTTOM-anchored at YIndent (probed: the bottom row's baseline
        // sits exactly on YIndent — 0 included, the page's own bottom edge — and the
        // rows grow upward).
        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (page.Width - blockW) / 2,
            HorizontalAlignment.Right => page.Width - blockW - XIndent - RightMargin,
            _ => x0,
        };
        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => page.Height - TopMargin - YIndent - pitch,
            VerticalAlignment.Center => (page.Height + n * pitch) / 2 - pitch,
            VerticalAlignment.Bottom => YIndent + BottomMargin + n * pitch - pitch,
            _ => YIndent + (n - 1) * pitch,
        };

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        for (var i = 0; i < n; i++)
        {
            var rowW = widths[i] * sx;
            var rowX = TextAlignment switch
            {
                HorizontalAlignment.Right => x + blockW - rowW,
                HorizontalAlignment.Center => x + (blockW - rowW) / 2,
                _ => x,
            };
            builder.SetTextMatrix(sx, 0, 0, sy, rowX, y - i * pitch);
            foreach (var (res, hex, _) in rowRuns[i])
                builder.SetFont(res, fontSize).ShowTextHex(hex);
        }
        builder
            .EndText();
        builder.RestoreState();
        return builder.Build();
    }

    internal override byte[] BuildContentStream(Page page)
    {
        // Pull effective font/size/colour from TextState first (setting
        // TextState.* on a stamp wins over the
        // bare TextStamp.FontSize/Color), falling back to the stamp's own
        // properties for callers that don't touch TextState.
        var baseFontName = ResolveBaseFontName();
        var fontSize = TextState?.FontSize > 0 ? (double)TextState.FontSize : FontSize;
        var color = TextState?.ForegroundColor ?? Color;

        // Encode Text into single-byte PDF string bytes against WinAnsi, and
        // collect any code-point / glyph-name pairs the resulting font must
        // declare via /Differences so non-WinAnsi chars (Polish ę/ą/ś/ł/ń/ź/ż/ć,
        // Czech č, etc.) render instead of falling back to '?'.
        var encoded = EncodeForWinAnsi(Text, out var diffMap);

        // Auto-fit: pick the largest font size at which the word-wrapped text still
        // fits the Width×Height box (bisection to AutoFitPrecision). Do this before
        // any layout so the chosen size flows into the render below and is readable
        // via the FontSize property once the stamp has been added.
        if (AutoFitToBox && Width > 0 && Height > 0)
        {
            fontSize = ComputeAutoFitFontSize(baseFontName, encoded);
            FontSize = (float)fontSize;
        }

        // When the primary font can't represent some glyphs (e.g. CJK/Unicode collapses to
        // '?') and a replacement font program is configured, embed it as a Type0/CIDFontType2
        // font and draw the whole stamp with it so the text renders and round-trips through
        // extraction (the recurring non-Latin1 stamp-text path).
        var replacement = ReplacementFontProgram;
        if (replacement is { } rf && HasUnencodableGlyphs(Text, encoded))
            return BuildCidStamp(page, rf.ttf, rf.name, fontSize, color);

        // CJK stamp text with no explicit replacement font: the configured Latin face
        // has no such glyphs (they collapsed to '?'), so embed a system CJK face as a
        // Type0 font — mirroring the generator's CJK fallback — so the text renders
        // and round-trips through extraction.
        if (replacement is null && HasUnencodableGlyphs(Text, encoded)
            && TryResolveCjkTtf(Text) is { } cjk)
            return BuildCidStamp(page, cjk.ttf, cjk.name, fontSize, color);

        // Supplementary-plane text (CJK Ext-B, Egyptian hieroglyphs, emoji …) with no
        // BMP-CJK face matched above: the Latin base face's TrueType substitute anchors
        // the stamp and each supplementary run brings its own script face (resolved per
        // code point inside BuildCidStamp), so the text renders where a face exists and
        // always round-trips through extraction via per-CID ToUnicode.
        if (replacement is null && HasUnencodableGlyphs(Text, encoded)
            && HasSupplementaryChars(Text) && TryResolveUnicodeFallback() is { } ufb)
            return BuildCidStamp(page, ufb.ttf, ufb.name, fontSize, color);

        // No explicit replacement font, but the text carries glyphs outside WinAnsi (Polish
        // ę/ą/ś/…, etc.) that a non-embedded Standard-14 base font can't display: embed the
        // matching TrueType substitute as a Type0 font so the glyphs render and round-trip
        // through extraction. Gated to a plain single-line stamp (BuildCidStamp's shape).
        if (replacement is null && diffMap.Count > 0 && IsPlainBlockStamp()
            && TryResolveUnicodeFallback() is { } auto)
            return BuildCidStamp(page, auto.ttf, auto.name, fontSize, color);

        var fontResName = EnsureFontResource(page, baseFontName, diffMap);

        // Wrapping is enabled by the WordWrap bool OR a non-NoWrap WordWrapMode
        // (both are exposed; this ctor path sets only the bool).
        var wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;

        // Scale-to-fit: a stamp with Scale=true and an explicit Width×Height box
        // lays its text out at the base font, then non-uniformly scales that block
        // to exactly fill the box, anchored at (XIndent, YIndent) —
        // emitting `sx 0 0 sy XIndent YIndent cm` over a natural-size
        // form. Wrapped text fills width at scale ~1 and stretches
        // vertically; un-wrapped text is laid as a single line and squished to width.
        if (Scale && Width > 0 && Height > 0)
            return BuildScaledToBox(page, encoded, baseFontName, fontResName, fontSize, color, wrapping);

        // Rotated stamp with an explicit Width×Height box: the text scales
        // non-uniformly to fill the box (sx=Width/textW, sy=Height/textH), the box is
        // centred per Horizontal/VerticalAlignment, then rotated about the box centre.
        // The plain path below only applies the horizontal scale and
        // rotates about the block anchor, which mis-sizes/positions the result.
        double rot = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        if (Math.Abs(rot) > 0.01 && Width > 0 && Height > 0)
            return BuildBoxRotated(page, encoded, baseFontName, fontResName, fontSize, color, wrapping, rot);

        // Word-wrapped stamp with a background box and Scale=false: wrap the text to the
        // inner width (Width minus L/R margins), grow the box to the widest wrapped line,
        // and emit the box as the leading `q / x y w h re / rg / RG / f*`
        // block — the text follows inside it.
        var bgEarly = TextState?.BackgroundColor;
        if (!Scale && wrapping && Width > 0 && bgEarly is { IsEmpty: false })
            return BuildWrappedBackgroundBox(page, encoded, baseFontName, fontResName, fontSize, color, bgEarly!);

        // Break the text into display rows: wrap to the stamp width when wrapping
        // is on, otherwise split on the explicit '\n' line breaks that a
        // FormattedText (AddNewLineText) or a multi-line Value carries. The old
        // no-wrap path emitted the raw '\n' byte inside a single Tj string, which
        // is not a line break in PDF — every line collapsed onto one row.
        // When wrapping is requested but no explicit stamp width is set, wrap to the
        // page's available extent along the text's advance axis so a WordWrap stamp lays
        // out as multiple on-page lines instead of one line running off the edge — which
        // page-bounds text extraction would then crop. The extent is the UNROTATED
        // MediaBox dimension: the stamp's own Rotate turns the advance vertical at 90/270
        // (Rotation values are degrees, %180==90 catches both), while page /Rotate is a
        // view transform only — extraction bounds are the unrotated MediaBox, so page.Width
        // /Height (which swap for a rotated page) must not choose the wrap axis.
        var stampDeg = (int)Math.Round(Math.Abs(RotateAngle != 0 ? RotateAngle : (double)Rotate));
        var wrapVertical = stampDeg % 180 == 90;
        var mb = page.MediaBox;
        var advanceDim = wrapVertical ? mb.Height : mb.Width;
        var wrapLead = wrapVertical ? (YIndent > 0 ? YIndent : BottomMargin) : (XIndent > 0 ? XIndent : LeftMargin);
        var wrapTrail = wrapVertical ? TopMargin : RightMargin;
        var wrapWidth = WrapWidth > 0
            ? WrapWidth
            : (WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap
                ? Math.Max(0.0, advanceDim - wrapLead - wrapTrail)
                : 0.0);
        var rows = (wrapWidth > 0 && WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap)
            ? WrapEncoded(encoded, baseFontName, fontSize, wrapWidth)
            : SplitRows(encoded);

        // Natural (un-scaled) row widths and the block width (the widest row).
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;

        // A stamp with an explicit Width stretches/condenses its text horizontally
        // to fill that width (the whole stamp form scales
        // by Width / naturalWidth). No Width ⇒ draw at natural size.
        var scaleX = (Width > 0 && blockWidth > 0) ? Width / blockWidth : 1.0;
        var scaledBlockWidth = blockWidth * scaleX;

        // Leading of one em (stamp lines are spaced by exactly the font size).
        var lineHeight = fontSize;

        // Position the block on the page. The block's left/top is derived from the
        // SCALED width so Right/Center alignment lands the right/centre at the page
        // edge/centre, and the first baseline sits one line below the top edge.
        var (originX, topBaseline) = ComputeBlockOrigin(page, scaledBlockWidth, fontSize, lineHeight, rows.Count, baseFontName);

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        if (Opacity < 1.0)
        {
            var gs = new Content.ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = page.AddExtGState(gs);
            builder.SetExtGState(gsName);
        }

        // Place + scale the block with a single cm: rotate about the block anchor
        // when requested, then apply the horizontal fill-scale. Drawing happens in
        // block-local coordinates (top line baseline at y=0, growing downward).
        double rotateDeg = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        var cos = Math.Cos(rotateDeg * Math.PI / 180);
        var sin = Math.Sin(rotateDeg * Math.PI / 180);

        var bgColor = TextState?.BackgroundColor;
        var hasBg = bgColor is { IsEmpty: false };

        // A multi-line block without a background box anchors
        // at its BOTTOM — the last row's baseline sits one font-descent above
        // the block origin and each row's Tm carries the absolute in-block Y
        // ((N-1-li)·lineHeight + descent). The cm translation is lowered by the same
        // amount, so the net page placement is unchanged; only the Tm/cm split moves.
        var bottomAnchor = 0.0;
        if (rows.Count > 1 && !hasBg)
        {
            var d = Aspose.Pdf.Text.Standard14Fonts.GetDescent(baseFontName);
            var descentInset = (d < 0 ? -d / 1000.0 : 0.2) * fontSize;
            bottomAnchor = (rows.Count - 1) * lineHeight + descentInset;
        }
        var cmY = topBaseline - bottomAnchor;
        // A 90/270-rotated stamp advances along page Y; the corner-anchored matrix below
        // would swing the block off the page (advance one way off the baseline, rows into
        // −X). Re-anchor so the block's rotated page-space bounding box honours
        // Horizontal/VerticalAlignment inside the unrotated MediaBox, keeping every glyph
        // on-page. Upright 0/180 stamps keep their existing anchor.
        var rotQuarter = ((int)Math.Round(Math.Abs(rotateDeg))) % 360;
        if (rotQuarter == 90 || rotQuarter == 270)
        {
            var advExtent = scaledBlockWidth;                                                // page-Y span
            var crossExtent = (rows.Count - 1) * lineHeight + (hasBg ? 1.1 : 1.0) * fontSize; // page-X span
            var pageXMin = HorizontalAlignment == HorizontalAlignment.Right
                ? mb.Width - RightMargin - crossExtent
                : HorizontalAlignment == HorizontalAlignment.Center
                    ? (mb.Width - crossExtent) / 2
                    : (XIndent > 0 ? XIndent : LeftMargin);
            var pageYMin = VerticalAlignment == VerticalAlignment.Top
                ? mb.Height - TopMargin - advExtent
                : VerticalAlignment == VerticalAlignment.Center
                    ? (mb.Height - advExtent) / 2
                    : (YIndent > 0 ? YIndent : BottomMargin);
            originX = pageXMin + crossExtent;                          // rows map to X in [pageXMin, pageXMin+cross]
            cmY = rotQuarter == 90 ? pageYMin : pageYMin + advExtent;  // advance spans [pageYMin, pageYMin+adv]
        }
        else if (rotQuarter == 180)
        {
            // A 180-flipped block runs its (horizontal) advance and rows in the negative
            // direction from the corner anchor — HAlign.Left would push the text off the
            // left/top edge. Re-anchor so the flipped page-space box honours alignment
            // inside the unrotated MediaBox (advance along page X, rows down page Y).
            var advExtent = scaledBlockWidth;                                                // page-X span
            var crossExtent = (rows.Count - 1) * lineHeight + (hasBg ? 1.1 : 1.0) * fontSize; // page-Y span
            var pageXMin = HorizontalAlignment == HorizontalAlignment.Right
                ? mb.Width - RightMargin - advExtent
                : HorizontalAlignment == HorizontalAlignment.Center
                    ? (mb.Width - advExtent) / 2
                    : (XIndent > 0 ? XIndent : LeftMargin);
            var pageYMin = VerticalAlignment == VerticalAlignment.Top
                ? mb.Height - TopMargin - crossExtent
                : VerticalAlignment == VerticalAlignment.Center
                    ? (mb.Height - crossExtent) / 2
                    : (YIndent > 0 ? YIndent : BottomMargin);
            originX = pageXMin + advExtent;    // page X spans [pageXMin, pageXMin+adv]
            cmY = pageYMin + crossExtent;      // page Y spans [pageYMin, pageYMin+cross]
        }
        // An OFF-AXIS rotated stamp (e.g. 45°) is anchored by its ROTATED BOUNDING BOX,
        // not by the baseline start: the stamp's content box rotates about
        // the box origin and translates so the rotated box's min corner lands at
        // (XIndent, YIndent) (the matrix composes size·scale·rotation·shift(point);
        // the anchor is offset by the rotated box extents). Pinning the baseline start
        // (originX, cmY) leaves the stamp shifted by the rotated box's overhang. Applied
        // only to the SIMPLE case it is derived for — a single-line, XIndent/YIndent-placed
        // stamp with no alignment override, background box, wrap or width; quarter rotations
        // (90/180/270) use the alignment re-anchor above instead.
        var rotAnchorSimple = Math.Abs(rotateDeg) > 0.01
            && rotQuarter != 90 && rotQuarter != 180 && rotQuarter != 270
            && (HorizontalAlignment == HorizontalAlignment.Left
                || HorizontalAlignment == HorizontalAlignment.None)
            && !hasBg
            && rows.Count == 1
            && Width <= 0;
        if (rotAnchorSimple)
        {
            var boxW = scaledBlockWidth;
            var descF = Aspose.Pdf.Text.Standard14Fonts.GetDescent(baseFontName);
            var descent = (descF < 0 ? -descF / 1000.0 : 0.2) * fontSize;
            // Content box in block-local space: x∈[0,boxW]; y spans one line box above
            // the top baseline (≈1.13 em, the Position.YIndent plus the fragment
            // height) down to a font descent below the bottom baseline.
            var yTop = 1.13 * fontSize;
            var yBot = -bottomAnchor - descent;
            double minX = double.MaxValue;
            foreach (var (lx, ly) in new[] { (0.0, yBot), (boxW, yBot), (boxW, yTop), (0.0, yTop) })
            {
                var rx = lx * cos - ly * sin;
                if (rx < minX) minX = rx;
            }
            // X is anchored by the rotated box overhang; Y stays on the baseline
            // (TreatYIndentAsBaseLine), which needs no re-anchoring.
            builder.SetMatrix(cos * scaleX, sin * scaleX, -sin, cos, originX - minX, cmY);
        }
        else if (Math.Abs(rotateDeg) > 0.01)
        {
            builder.SetMatrix(cos * scaleX, sin * scaleX, -sin, cos, originX, cmY);
        }
        else
        {
            builder.SetMatrix(scaleX, 0, 0, 1, originX, cmY);
        }

        // Optional background box: when TextState.BackgroundColor is set, fill a
        // rectangle behind the text in the block-local (already rotated/placed)
        // space. The box spans the block width and one 1.1-em line box per row;
        // the text baseline is raised by the descent so the glyphs sit inside it.
        var bgYOffset = 0.0;
        if (hasBg)
        {
            const double descentFactor = 0.211; // baseline inset from the box bottom
            const double boxHeightFactor = 1.1; // one line box = 1.1 em
            bgYOffset = (rows.Count - 1) * lineHeight + descentFactor * fontSize;
            var boxHeight = (rows.Count - 1) * lineHeight + boxHeightFactor * fontSize;
            // Inner save so the rectangle's preceding operator is `q`, not the cm.
            builder.SaveState();
            builder.Rectangle(0, 0, blockWidth, boxHeight);
            builder.SetFillColor(bgColor!);
            builder.SetStrokeColor(bgColor!);
            builder.FillEvenOdd();
        }

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);

        // Apply the stamp's character/word spacing (Tc/Tw) so letter-spaced stamps
        // render spaced; guarded so default spacing keeps byte-identical output.
        if (TextState?.CharacterSpacing is { } cs and not 0f)
            builder.SetCharSpacing(cs);
        if (TextState?.WordSpacing is { } ws and not 0f)
            builder.SetWordSpacing(ws);

        for (var li = 0; li < rows.Count; li++)
        {
            // Align each row within the (un-scaled) block per TextAlignment; the cm
            // scale above stretches these local offsets to the scaled block width.
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            builder.SetTextMatrix(1, 0, 0, 1, localX, -li * lineHeight + bgYOffset + bottomAnchor)
                   .ShowTextBytes(rows[li]);
        }

        builder.EndText();
        if (hasBg) builder.RestoreState(); // close the inner save
        builder.RestoreState();

        return builder.Build();
    }

    // Scale=true layout: lay the text out at the base font in a natural-size block,
    // then emit one cm that non-uniformly scales that block to fill the Width×Height
    // box at (XIndent, YIndent). Wrapped text breaks to Width (so scaleX ≈ 1 and only
    // the height stretches); un-wrapped text is a single line (newlines → spaces) that
    // is squished horizontally to Width and stretched to Height.
    private byte[] BuildScaledToBox(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, bool wrapping)
    {
        var rows = wrapping
            ? WrapEncoded(encoded, baseFontName, fontSize, Width)
            : new List<byte[]> { JoinToOneLine(encoded) };
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        var lineHeight = fontSize;
        var blockHeight = Math.Max(1, rows.Count) * lineHeight;
        if (blockWidth <= 0) blockWidth = Width;

        var sX = Width / blockWidth;
        var sY = Height / blockHeight;
        // Baseline of the bottom row inside the natural block (leave the font's
        // descent below it); rows stack upward from there.
        var descent = fontSize * 0.2;

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.SetMatrix(sX, 0, 0, sY, XIndent, YIndent);

        // Background box: fills the whole natural block in block-local space, so the
        // cm scale above stretches it to exactly the Width×Height box.
        if (TextState?.BackgroundColor is { IsEmpty: false } bg)
        {
            builder.SaveState();
            builder.Rectangle(0, 0, blockWidth, blockHeight);
            builder.SetFillColor(bg);
            builder.SetStrokeColor(bg);
            builder.FillEvenOdd();
            builder.RestoreState();
        }

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            // Row 0 is the top line; the bottom line sits at `descent`.
            var localY = (rows.Count - 1 - li) * lineHeight + descent;
            builder.SetTextMatrix(1, 0, 0, 1, localX, localY).ShowTextBytes(rows[li]);
        }
        builder.EndText().RestoreState();
        return builder.Build();
    }

    // Rotated, box-fitted stamp: scale the natural text block to fill
    // the Width×Height box (sx=Width/blockWidth, sy=Height/blockHeight), place the box
    // per Horizontal/VerticalAlignment (or XIndent/YIndent when alignment is unset),
    // and rotate about the box centre. The emitted cm is
    //   [sx·cosθ, sx·sinθ, -sy·sinθ, sy·cosθ, tx, ty]
    // with (tx,ty) chosen so the (scaled) box centre maps to the target page centre.
    private byte[] BuildBoxRotated(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, bool wrapping, double rotateDeg)
    {
        // An auto-fitted stamp must render the SAME wrap the fit was computed
        // against (trailing-space-trimmed rows) or the scales below distort what
        // the fit sized to be exact.
        var rows = wrapping
            ? (AutoFitToBox
                ? AutoFitWrap(encoded, baseFontName, fontSize, Width)
                : WrapEncoded(encoded, baseFontName, fontSize, Width))
            : new List<byte[]> { JoinToOneLine(encoded) };
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        if (blockWidth <= 0) blockWidth = Width;
        var lineHeight = fontSize;
        // Vertical box-fit law (probed, see the scaled-box path): the block is
        // (N + 0.1) lines tall - one line of leading per row plus a tenth of a
        // line for the last row's descender band.
        var blockHeight = (Math.Max(1, rows.Count) + 0.1) * lineHeight;

        var sX = Width / blockWidth;
        var sY = Height / blockHeight;
        double sw = Width, sh = Height; // scaled box dimensions

        // Alignment places the box's ROTATED footprint, not the unrotated box: a
        // 90-degree stamp whose Width x Height mirrors the page (the swap the
        // caller makes so the rotated text fills it) must land ON the page.
        // Anchoring the unrotated dimensions hung 123 pt of a page-filling
        // rotated stamp off both edges (measured against the expected render,
        // which spans the full page). The footprint's axis-aligned bounds:
        var rotRad = rotateDeg * Math.PI / 180;
        var fw = Math.Abs(Math.Cos(rotRad)) * sw + Math.Abs(Math.Sin(rotRad)) * sh;
        var fh = Math.Abs(Math.Sin(rotRad)) * sw + Math.Abs(Math.Cos(rotRad)) * sh;

        // Box centre on the page: alignment when set, else XIndent/YIndent corner.
        // Anchoring is within the MEDIA BOX rectangle - a page whose box has a
        // non-zero origin (the mediabox-swap pattern leaves y running 247..842)
        // otherwise draws the stamp a full origin-offset below the visible page.
        var pmb = page.MediaBox;
        double cx = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => pmb.LLX + pmb.Width / 2.0,
            HorizontalAlignment.Right => pmb.URX - fw / 2.0 - XIndent,
            _ => pmb.LLX + XIndent + fw / 2.0,
        };
        double cy = VerticalAlignment switch
        {
            VerticalAlignment.Center => pmb.LLY + pmb.Height / 2.0,
            VerticalAlignment.Top => pmb.URY - fh / 2.0 - YIndent,
            _ => pmb.LLY + YIndent + fh / 2.0,
        };

        var cos = Math.Cos(rotateDeg * Math.PI / 180);
        var sin = Math.Sin(rotateDeg * Math.PI / 180);
        // tx,ty: map the scaled box centre (sw/2, sh/2) through the rotation to (cx,cy).
        var tx = cx - (cos * (sw / 2.0) - sin * (sh / 2.0));
        var ty = cy - (sin * (sw / 2.0) + cos * (sh / 2.0));

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.SetMatrix(sX * cos, sX * sin, -sY * sin, sY * cos, tx, ty);
        // The stamp's background fills the whole box (a FormattedText backdrop
        // colour paints behind every row, edge to edge).
        var boxBg = TextState?.BackgroundColor;
        if (boxBg is { IsEmpty: false })
        {
            builder.SetFillColor(boxBg.R / 255.0, boxBg.G / 255.0, boxBg.B / 255.0);
            builder.Rectangle(0, 0, blockWidth, blockHeight);
            builder.Fill();
        }
        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        var descent = fontSize * 0.2;
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            var localY = (rows.Count - 1 - li) * lineHeight + descent;
            builder.SetTextMatrix(1, 0, 0, 1, localX, localY).ShowTextBytes(rows[li]);
        }
        builder.EndText().RestoreState();
        return builder.Build();
    }

    // Word-wrapped stamp with a background box (Scale=false). Wrap to the inner width
    // (Width minus left/right margins), grow the box to the widest wrapped line, and emit:
    //   q  x y w h re  r g b rg  r g b RG  f*  BT ... ET  Q
    // so the rectangle is the first painted operator. The box
    // is placed per the stamp's Horizontal/Vertical alignment; the text fills it top-down.
    private byte[] BuildWrappedBackgroundBox(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, Color bgColor)
    {
        var innerW = Width - LeftMargin - RightMargin;
        if (innerW <= 0) innerW = Width;

        // Break at spaces only: a word wider than the inner width is NOT hyphenated — it sits on
        // its own line and overflows, which (with Scale=false) is what grows the box width.
        var rows = WrapAtSpaces(encoded, baseFontName, fontSize, innerW);
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        // With Scale=false a word wider than the inner width grows the box rather than being
        // squeezed, so the box width is the widest wrapped line.
        var boxW = Math.Max(innerW, blockWidth);
        var lineHeight = fontSize;
        var descent = fontSize * 0.1;                 // baseline inset below the last line
        var boxH = rows.Count * lineHeight + descent;

        double boxX = HorizontalAlignment switch
        {
            HorizontalAlignment.Right => page.Width - RightMargin - boxW,
            HorizontalAlignment.Center => (page.Width - boxW) / 2.0,
            _ => LeftMargin,
        };
        double boxY = VerticalAlignment switch
        {
            VerticalAlignment.Bottom => BottomMargin,
            VerticalAlignment.Center => (page.Height - boxH) / 2.0,
            _ => page.Height - TopMargin - boxH,       // Top (default)
        };

        var builder = new ContentStreamBuilder();
        builder.SaveState();                            // q
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.Rectangle(boxX, boxY, boxW, boxH);      // re
        builder.SetFillColor(bgColor);                  // rg
        builder.SetStrokeColor(bgColor);                // RG
        builder.FillEvenOdd();                          // f*

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        var topBaseline = boxY + boxH - fontSize;       // first line near the box top
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = boxW - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            builder.SetTextMatrix(1, 0, 0, 1, boxX + localX, topBaseline - li * lineHeight)
                   .ShowTextBytes(rows[li]);
        }
        builder.EndText();
        builder.RestoreState();                         // Q
        return builder.Build();
    }
}
