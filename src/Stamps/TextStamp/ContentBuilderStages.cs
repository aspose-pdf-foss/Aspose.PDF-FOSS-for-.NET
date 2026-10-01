using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class TextStamp
{
    /// <summary>Text stamp content stages: the special-form heads, row layout, rotation placement and the text emit.</summary>
    private void EmitStampText(StampContentState cb)
    {
        if (cb.hasBg)
        {
            const double descentFactor = 0.211; // baseline inset from the box bottom
            const double boxHeightFactor = 1.1; // one line box = 1.1 em
            cb.bgYOffset = (cb.rows.Count - 1) * cb.lineHeight + descentFactor * cb.fontSize;
            var boxHeight = (cb.rows.Count - 1) * cb.lineHeight + boxHeightFactor * cb.fontSize;
            // Inner save so the rectangle's preceding operator is `q`, not the cm.
            cb.builder.SaveState();
            cb.builder.Rectangle(0, 0, cb.blockWidth, boxHeight);
            cb.builder.SetFillColor(cb.bgColor!);
            cb.builder.SetStrokeColor(cb.bgColor!);
            cb.builder.FillEvenOdd();
        }

        if (cb.wrapping && !Scale)
        {
            EmitWrappedWords(cb);
        }
        else
        {
            cb.builder.BeginText()
                .SetFillColor(cb.color.R / 255.0, cb.color.G / 255.0, cb.color.B / 255.0)
                .SetFont(cb.fontResName, cb.fontSize);
            ApplyStampSpacing(cb);
            for (var li = 0; li < cb.rows.Count; li++)
                cb.builder.SetTextMatrix(1, 0, 0, 1, RowLocalX(cb, li), RowLocalY(cb, li))
                       .ShowTextBytes(cb.rows[li]);
            cb.builder.EndText();
        }
        if (cb.hasBg) cb.builder.RestoreState(); // close the inner save
        cb.builder.RestoreState();

    }

    /// <summary>The stamp's character/word spacing (Tc/Tw) so letter-spaced stamps render
    /// spaced; guarded so default spacing keeps byte-identical output.</summary>
    private void ApplyStampSpacing(StampContentState cb)
    {
        if (TextState?.CharacterSpacing is { } cs and not 0f)
            cb.builder.SetCharSpacing(cs);
        if (TextState?.WordSpacing is { } ws and not 0f)
            cb.builder.SetWordSpacing(ws);
    }

    /// <summary>A row's left edge within the (un-scaled) block per TextAlignment; the cm
    /// scale above stretches these local offsets to the scaled block width.</summary>
    private double RowLocalX(StampContentState cb, int li)
    {
        var pad = cb.blockWidth - cb.rowWidths[li];
        return TextAlignment switch
        {
            HorizontalAlignment.Right => pad,
            HorizontalAlignment.Center => pad / 2,
            _ => 0.0,
        };
    }

    private static double RowLocalY(StampContentState cb, int li) =>
        -li * cb.lineHeight + cb.bgYOffset + cb.bottomAnchor;

    private const byte SpaceCode = 0x20;

    /// <summary>LAW (the wrapped stamp's shape): the reference writes a word-wrapped,
    /// unscaled text stamp one WORD per text object, seated at its own advance along the
    /// row, and opens one saved state per word up front, closing one after each word - so
    /// a 500-word stamp nests its q operators 500 deep, which is what its own PDF/A
    /// implementation-limit check then reports (clause 6.1.12/13, nesting over 28).
    /// Measured 2026-09-07 on the reference writer: "a a a" emits q q q BT..ET Q BT..ET Q
    /// BT..ET Q with the words at 0, 10.008, 20.016 (glyph + space advances), a single
    /// word emits one q and one text object.</summary>
    private void EmitWrappedWords(StampContentState cb)
    {
        var spaceAdvance = MeasureRow(new[] { SpaceCode }, cb.baseFontName, cb.fontSize);
        var words = new List<(byte[] bytes, double x, double y)>();
        for (var li = 0; li < cb.rows.Count; li++)
        {
            var x = RowLocalX(cb, li);
            var y = RowLocalY(cb, li);
            var row = cb.rows[li];
            var start = 0;
            for (var i = 0; i <= row.Length; i++)
            {
                if (i < row.Length && row[i] != SpaceCode) continue;
                if (i > start)
                {
                    var word = new byte[i - start];
                    Array.Copy(row, start, word, 0, word.Length);
                    words.Add((word, x, y));
                    x += MeasureRow(word, cb.baseFontName, cb.fontSize);
                }
                if (i < row.Length) x += spaceAdvance;
                start = i + 1;
            }
        }
        for (var k = 0; k < words.Count; k++) cb.builder.SaveState();
        foreach (var (bytes, x, y) in words)
        {
            cb.builder.BeginText()
                .SetFont(cb.fontResName, cb.fontSize)
                .SetFillColor(cb.color.R / 255.0, cb.color.G / 255.0, cb.color.B / 255.0);
            ApplyStampSpacing(cb);
            cb.builder.SetTextMatrix(1, 0, 0, 1, x, y).ShowTextBytes(bytes).EndText();
            cb.builder.RestoreState();
        }
    }

    /// <summary></summary>
    private void PlaceRotated(StampContentState cb)
    {
        if (cb.rotQuarter == 90 || cb.rotQuarter == 270)
        {
            var advExtent = cb.scaledBlockWidth;                                                // page-Y span
            var crossExtent = (cb.rows.Count - 1) * cb.lineHeight + (cb.hasBg ? 1.1 : 1.0) * cb.fontSize; // page-X span
            var pageXMin = HorizontalAlignment == HorizontalAlignment.Right
                ? cb.mb.Width - RightMargin - crossExtent
                : HorizontalAlignment == HorizontalAlignment.Center
                    ? (cb.mb.Width - crossExtent) / 2
                    : (XIndent > 0 ? XIndent : LeftMargin);
            var pageYMin = VerticalAlignment == VerticalAlignment.Top
                ? cb.mb.Height - TopMargin - advExtent
                : VerticalAlignment == VerticalAlignment.Center
                    ? (cb.mb.Height - advExtent) / 2
                    : (YIndent > 0 ? YIndent : BottomMargin);
            cb.originX = pageXMin + crossExtent;                          // rows map to X in [pageXMin, pageXMin+cross]
            cb.cmY = cb.rotQuarter == 90 ? pageYMin : pageYMin + advExtent;  // advance spans [pageYMin, pageYMin+adv]
        }
        else if (cb.rotQuarter == 180)
        {
            // A 180-flipped block runs its (horizontal) advance and rows in the negative
            // direction from the corner anchor — HAlign.Left would push the text off the
            // left/top edge. Re-anchor so the flipped page-space box honours alignment
            // inside the unrotated MediaBox (advance along page X, rows down page Y).
            var advExtent = cb.scaledBlockWidth;                                                // page-X span
            var crossExtent = (cb.rows.Count - 1) * cb.lineHeight + (cb.hasBg ? 1.1 : 1.0) * cb.fontSize; // page-Y span
            var pageXMin = HorizontalAlignment == HorizontalAlignment.Right
                ? cb.mb.Width - RightMargin - advExtent
                : HorizontalAlignment == HorizontalAlignment.Center
                    ? (cb.mb.Width - advExtent) / 2
                    : (XIndent > 0 ? XIndent : LeftMargin);
            var pageYMin = VerticalAlignment == VerticalAlignment.Top
                ? cb.mb.Height - TopMargin - crossExtent
                : VerticalAlignment == VerticalAlignment.Center
                    ? (cb.mb.Height - crossExtent) / 2
                    : (YIndent > 0 ? YIndent : BottomMargin);
            cb.originX = pageXMin + advExtent;    // page X spans [pageXMin, pageXMin+adv]
            cb.cmY = pageYMin + crossExtent;      // page Y spans [pageYMin, pageYMin+cross]
        }
        cb.rotAnchorSimple = Math.Abs(cb.rotateDeg) > 0.01
            && cb.rotQuarter != 90 && cb.rotQuarter != 180 && cb.rotQuarter != 270
            && (HorizontalAlignment == HorizontalAlignment.Left
                || HorizontalAlignment == HorizontalAlignment.None)
            && !cb.hasBg
            && cb.rows.Count == 1
            && Width <= 0;
        if (cb.rotAnchorSimple)
        {
            var boxW = cb.scaledBlockWidth;
            var descF = Aspose.Pdf.Text.Standard14Fonts.GetDescent(cb.baseFontName);
            var descent = (descF < 0 ? -descF / 1000.0 : 0.2) * cb.fontSize;
            // Content box in block-local space: x∈[0,boxW]; y spans one line box above
            // the top baseline (≈1.13 em, the Position.YIndent plus the fragment
            // height) down to a font descent below the bottom baseline.
            var yTop = 1.13 * cb.fontSize;
            var yBot = -cb.bottomAnchor - descent;
            double minX = double.MaxValue;
            foreach (var (lx, ly) in new[] { (0.0, yBot), (boxW, yBot), (boxW, yTop), (0.0, yTop) })
            {
                var rx = lx * cb.cos - ly * cb.sin;
                if (rx < minX) minX = rx;
            }
            // X is anchored by the rotated box overhang; Y stays on the baseline
            // (TreatYIndentAsBaseLine), which needs no re-anchoring.
            cb.builder.SetMatrix(cb.cos * cb.scaleX, cb.sin * cb.scaleX, -cb.sin, cb.cos, cb.originX - minX, cb.cmY);
        }
        else if (Math.Abs(cb.rotateDeg) > 0.01)
        {
            cb.builder.SetMatrix(cb.cos * cb.scaleX, cb.sin * cb.scaleX, -cb.sin, cb.cos, cb.originX, cb.cmY);
        }
        else
        {
            cb.builder.SetMatrix(cb.scaleX, 0, 0, 1, cb.originX, cb.cmY);
        }
    }

    /// <summary></summary>
    private void LayoutRows(StampContentState cb)
    {
        cb.wrapWidth = WrapWidth > 0
            ? WrapWidth
            : (WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap
                ? Math.Max(0.0, cb.advanceDim - cb.wrapLead - cb.wrapTrail)
                : 0.0);
        cb.rows = (cb.wrapWidth > 0 && WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap)
            ? WrapEncoded(cb.encoded, cb.baseFontName, cb.fontSize, cb.wrapWidth)
            : SplitRows(cb.encoded);

        cb.rowWidths = cb.rows.Select(r => MeasureRow(r, cb.baseFontName, cb.fontSize)).ToList();
        cb.blockWidth = cb.rowWidths.Count > 0 ? cb.rowWidths.Max() : 0.0;
        // LAW: an unscaled wrapped stamp whose widest row runs past the page keeps that row's
        // natural width as its form's /BBox width (the reference writes 218634.61 for a
        // 32760-glyph word on a 595 pt page - a fractional real past 32767, which its own
        // PDF/A-1 implementation-limit check then reports); a block that fits keeps the page box.
        FormBBox = !Scale && cb.wrapping && cb.blockWidth > cb.mb.Width
            ? new Rectangle(0, 0, cb.blockWidth, cb.mb.Height)
            : null;

        cb.scaleX = (Width > 0 && cb.blockWidth > 0) ? Width / cb.blockWidth : 1.0;
        cb.scaledBlockWidth = cb.blockWidth * cb.scaleX;

        cb.lineHeight = cb.fontSize;

        // Position the block on the page. The block's left/top is derived from the
        // SCALED width so Right/Center alignment lands the right/centre at the page
        // edge/centre, and the first baseline sits one line below the top edge.
        (cb.originX, cb.topBaseline) = ComputeBlockOrigin(cb.page, cb.scaledBlockWidth, cb.fontSize, cb.lineHeight, cb.rows.Count, cb.baseFontName);

        cb.builder = new ContentStreamBuilder();
        cb.builder.SaveState();

        if (Opacity < 1.0)
        {
            var gs = new Content.ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = cb.page.AddExtGState(gs);
            cb.builder.SetExtGState(gsName);
        }

        cb.rotateDeg = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        cb.cos = Math.Cos(cb.rotateDeg * Math.PI / 180);
        cb.sin = Math.Sin(cb.rotateDeg * Math.PI / 180);

        cb.bgColor = TextState?.BackgroundColor;
        cb.hasBg = cb.bgColor is { IsEmpty: false };

        cb.bottomAnchor = 0.0;
        if (cb.rows.Count > 1 && !cb.hasBg)
        {
            var d = Aspose.Pdf.Text.Standard14Fonts.GetDescent(cb.baseFontName);
            var descentInset = (d < 0 ? -d / 1000.0 : 0.2) * cb.fontSize;
            cb.bottomAnchor = (cb.rows.Count - 1) * cb.lineHeight + descentInset;
        }
        cb.cmY = cb.topBaseline - cb.bottomAnchor;
    }

    /// <summary></summary>
    private byte[]? TryBuildSpecialForm(StampContentState cb)
    {
        if (AutoFitToBox && Width > 0 && Height > 0)
        {
            cb.fontSize = ComputeAutoFitFontSize(cb.baseFontName, cb.encoded);
            FontSize = (float)cb.fontSize;
        }

        cb.replacement = ReplacementFontProgram;
        if (cb.replacement is { } rf && HasUnencodableGlyphs(Text, cb.encoded))
            return BuildCidStamp(cb.page, rf.ttf, rf.name, cb.fontSize, cb.color);

        // CJK stamp text with no explicit replacement font: the configured Latin face
        // has no such glyphs (they collapsed to '?'), so embed a system CJK face as a
        // Type0 font — mirroring the generator's CJK fallback — so the text renders
        // and round-trips through extraction.
        if (cb.replacement is null && HasUnencodableGlyphs(Text, cb.encoded)
            && TryResolveCjkTtf(Text) is { } cjk)
            return BuildCidStamp(cb.page, cjk.ttf, cjk.name, cb.fontSize, cb.color);

        // Supplementary-plane text (CJK Ext-B, Egyptian hieroglyphs, emoji …) with no
        // BMP-CJK face matched above: the Latin base face's TrueType substitute anchors
        // the stamp and each supplementary run brings its own script face (resolved per
        // code point inside BuildCidStamp), so the text renders where a face exists and
        // always round-trips through extraction via per-CID ToUnicode.
        if (cb.replacement is null && HasUnencodableGlyphs(Text, cb.encoded)
            && HasSupplementaryChars(Text) && TryResolveUnicodeFallback() is { } ufb)
            return BuildCidStamp(cb.page, ufb.ttf, ufb.name, cb.fontSize, cb.color);

        // No explicit replacement font, but the text carries glyphs outside WinAnsi (Polish
        // ę/ą/ś/…, etc.) that a non-embedded Standard-14 base font can't display: embed the
        // matching TrueType substitute as a Type0 font so the glyphs render and round-trip
        // through extraction. Gated to a plain single-line stamp (BuildCidStamp's shape).
        if (cb.replacement is null && cb.diffMap.Count > 0 && IsPlainBlockStamp()
            && TryResolveUnicodeFallback() is { } auto)
            return BuildCidStamp(cb.page, auto.ttf, auto.name, cb.fontSize, cb.color);

        cb.fontResName = EnsureFontResource(cb.page, cb.baseFontName, cb.diffMap);

        cb.wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;

        // Scale-to-fit: a stamp with Scale=true and an explicit Width×Height box
        // lays its text out at the base font, then non-uniformly scales that block
        // to exactly fill the box, anchored at (XIndent, YIndent) —
        // emitting `sx 0 0 sy XIndent YIndent cm` over a natural-size
        // form. Wrapped text fills width at scale ~1 and stretches
        // vertically; un-wrapped text is laid as a single line and squished to width.
        if (Scale && Width > 0 && Height > 0)
            return BuildScaledToBox(cb.page, cb.encoded, cb.baseFontName, cb.fontResName, cb.fontSize, cb.color, cb.wrapping);

        cb.rot = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        if (Math.Abs(cb.rot) > 0.01 && Width > 0 && Height > 0)
            return BuildBoxRotated(cb.page, cb.encoded, cb.baseFontName, cb.fontResName, cb.fontSize, cb.color, cb.wrapping, cb.rot);

        cb.bgEarly = TextState?.BackgroundColor;
        if (!Scale && cb.wrapping && Width > 0 && cb.bgEarly is { IsEmpty: false })
            return BuildWrappedBackgroundBox(cb.page, cb.encoded, cb.baseFontName, cb.fontResName, cb.fontSize, cb.color, cb.bgEarly!);
        return null;
    }
}
