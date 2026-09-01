using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Lay a graph-bearing cell out into left-to-right inline rows, wrapping at
    /// the cell's content width. Each row is positioned <see cref="InlineItem"/>s (a text
    /// run or a Graph) with x-offsets from the cell content-left; the render pass draws the
    /// text and blits each graph's content stream at the resolved position.</summary>
    private List<List<InlineItem>> BuildInlineCellLayout(
        Cell cell, double availWidth, double defaultFontSize,
        Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign, out double lineHeight)
    {
        var rows = new List<List<InlineItem>>();
        var current = new List<InlineItem>();
        double x = 0;
        // Generator cells pitch inline rows at the run size (K = 1.0, like every
        // other cell line); the other dialects keep the 1.2 em row.
        var generatorPitch = GeneratorCellModel;
        var maxH = generatorPitch ? 0 : defaultFontSize * 1.2;
        double RowH(double fs) => generatorPitch ? fs : fs * 1.2;
        var contentW = availWidth > 0 ? availWidth : double.MaxValue;
        var faceCache = new Dictionary<string, (byte[]? ttf, string? name)>();

        var lineHasText = false;
        var rowItemSources = 0;
        var rowRightCount = 0;

        void NoteAlign(HorizontalAlignment a)
        {
            rowItemSources++;
            if (a == HorizontalAlignment.Right) rowRightCount++;
        }

        void Flush()
        {
            if (current.Count > 0)
            {
                // Right-aligned inline row: paragraph order packs
                // from the RIGHT content edge (first paragraph rightmost), so an
                // image + joined right-aligned text renders [text][image] against
                // the cell's right padding edge.
                if (rowItemSources > 0 && rowRightCount == rowItemSources && contentW < double.MaxValue)
                {
                    var xr = contentW;
                    foreach (var it in current) { it.X = xr - it.Width; xr -= it.Width; }
                }
                // …otherwise the CELL's own alignment places the row: a centred cell
                // centres its inline line the way it centres a plain wrapped one.
                else if (contentW < double.MaxValue
                         && cellAlign is HorizontalAlignment.Center or HorizontalAlignment.Right)
                {
                    double rowW = 0;
                    foreach (var it in current) rowW = Math.Max(rowW, it.X + it.Width);
                    var slack = contentW - rowW;
                    if (slack > 0)
                    {
                        var shift = cellAlign == HorizontalAlignment.Center ? slack / 2 : slack;
                        foreach (var it in current) it.X += shift;
                    }
                }
                rows.Add(current);
                current = new List<InlineItem>();
            }
            x = 0;
            lineHasText = false;
            rowItemSources = 0;
            rowRightCount = 0;
        }

        static HorizontalAlignment FragAlign(Aspose.Pdf.Text.TextFragment f)
        {
            if (f.TextState.HorizontalAlignment == HorizontalAlignment.Right)
                return HorizontalAlignment.Right;
            foreach (var s in f.Segments)
                if (s.TextState.HorizontalAlignment == HorizontalAlignment.Right)
                    return HorizontalAlignment.Right;
            return f.TextState.HorizontalAlignment;
        }

        foreach (var para in cell.Paragraphs)
        {
            if (para is Aspose.Pdf.Drawing.Graph g)
            {
                if (!g.IsInLineParagraph) Flush();
                var marginL = g.Margin?.Left ?? 0;
                if (current.Count > 0 && x + marginL + g.Width > contentW) Flush();
                x += marginL;
                current.Add(new InlineItem { Graph = g, X = x, Width = g.Width, Height = g.Height });
                x += g.Width;
                if (g.Height > maxH) maxH = g.Height;
                if (!g.IsInLineParagraph) Flush();
            }
            else if (para is Aspose.Pdf.Text.TextFragment tf)
            {
                var marginL = tf.Margin?.Left ?? 0;
                if (!tf.IsInLineParagraph) Flush();

                // A multi-segment fragment lays its segments out as consecutive inline runs on
                // the SAME line, each keeping its own size / colour / baseline (sub-superscript),
                // instead of being flattened to one merged run. Every segment is emitted —
                // including the parameterless TextFragment() ctor's default empty leading segment,
                // which the generator renders as an empty run (a leading empty fragment).
                var segs = System.Linq.Enumerable.ToList(tf.Segments);
                var textCount = System.Linq.Enumerable.Count(segs, s => !string.IsNullOrEmpty(s.Text));
                if (textCount > 1)
                {
                    x += marginL;
                    // A line's first EMBEDDED-font run gets an empty marker run in the
                    // default table font before it (a font-resource
                    // prelude the absorber surfaces as an empty fragment). Standard-font
                    // runs get no marker.
                    void EnsureLineMarker(double fs)
                    {
                        if (lineHasText) return;
                        lineHasText = true;
                        current.Add(new InlineItem { Text = "", Empty = true, FontSize = fs, X = x, Width = 0, Height = RowH(fs) });
                        if (RowH(fs) > maxH) maxH = RowH(fs);
                    }
                    foreach (var seg in segs)
                    {
                        var ss = seg.TextState;
                        var baseFs = ss.FontSizeTouched ? ss.FontSize
                            : (tf.TextState.FontSizeTouched ? tf.TextState.FontSize : defaultFontSize);
                        // The fragment's size is the LINE's: every segment seats against it
                        // (a smaller run bottom-aligns on its own descent). A generator
                        // fragment that never named a size seats its segments on their
                        // own (8 pt Arial segments under an unsized fragment sit on an
                        // 8 pt box, not the cell default's).
                        var lineFs = tf.TextState.FontSizeTouched ? tf.TextState.FontSize
                            : generatorPitch ? baseFs : defaultFontSize;
                        var segColor = ss.ForegroundColor ?? tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
                        // A segment carries its OWN weight, slant and underline: a
                        // multi-segment cell is exactly how a caller mixes them within
                        // one line, and flattening the run to the fragment's style drops
                        // every emphasis the markup asked for.
                        var segBold = ss.IsBold || tf.TextState.IsBold || (cellTextState?.IsBold ?? false);
                        var segItalic = ss.IsItalic || tf.TextState.IsItalic;
                        var segUnderline = ss.Underline || tf.TextState.Underline;
                        if (string.IsNullOrEmpty(seg.Text))
                        {
                            current.Add(new InlineItem { Text = "", Empty = true, FontSize = baseFs, Color = segColor, X = x, Width = 0, Height = RowH(baseFs) });
                            if (RowH(baseFs) > maxH) maxH = RowH(baseFs);
                            continue;
                        }

                        // Newline characters break the inline row: each empty piece is
                        // an empty run at the pen position (before the break, and again
                        // at the new line's start — both are emitted).
                        var segPieces = seg.Text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                        for (var spi = 0; spi < segPieces.Length; spi++)
                        {
                        if (spi > 0) Flush();
                        var segPiece = segPieces[spi];
                        if (segPiece.Length == 0)
                        {
                            current.Add(new InlineItem { Text = "", Empty = true, FontSize = baseFs, Color = segColor, X = x, Width = 0, Height = RowH(baseFs) });
                            if (RowH(baseFs) > maxH) maxH = RowH(baseFs);
                            continue;
                        }

                        // Per-segment embedded font (e.g. NotoSans / NotoSansArabic supplied on the
                        // segment's TextState): the run is drawn with that font embedded as Type0, so
                        // it is measured with the font's real glyph advances. Arabic is shaped
                        // (contextual presentation forms + bidi visual order).
                        var segTtf = ss.Font?.SourceFontData?.TtfData;
                        var segFontName = ss.Font?.FontName;
                        // A NAMED face carries its own bold/italic siblings, and the
                        // generator is what resolves them: TextState stores the flags
                        // only for a fragment that belongs to no page (see
                        // TextState.ApplyFontStyleToSource), leaving the writer to pick
                        // the styled file. Without this a bold-italic Arial segment drew
                        // in plain Arial.
                        if (segTtf is not null && (segBold || segItalic))
                        {
                            var wantStyle = Aspose.Pdf.Text.FontStyles.Regular;
                            if (segBold) wantStyle |= Aspose.Pdf.Text.FontStyles.Bold;
                            if (segItalic) wantStyle |= Aspose.Pdf.Text.FontStyles.Italic;
                            var family = Aspose.Pdf.Text.FontRepository.FamilyOf(
                                ss.Font!.FontName ?? ss.Font.BaseFont);
                            if (!string.IsNullOrEmpty(family))
                            {
                                Aspose.Pdf.Text.Font? styledFace = null;
                                try { styledFace = Aspose.Pdf.Text.FontRepository.TryFindFont(family!, wantStyle, ignoreCase: true); }
                                catch { }
                                if (styledFace?.SourceFontData?.TtfData is { Length: > 12 } styledTtf)
                                {
                                    segTtf = styledTtf;
                                    segFontName = styledFace.FontName;
                                }
                            }
                        }
                        var isArabic = Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(segPiece);
                        var itemH = RowH(baseFs);
                        var sup = ss.Superscript; var sub = ss.Subscript;

                        void AddRun(string runText, double runW, byte[]? runTtf, string? runName,
                            double runFs, double shiftY)
                        {
                            if (runTtf is not null) EnsureLineMarker(baseFs);
                            else lineHasText = true;
                            current.Add(new InlineItem
                            {
                                Text = runText, FontSize = runFs, Color = segColor, X = x, Width = runW,
                                Height = itemH, BaseFontSize = baseFs, LineFontSize = lineFs, BaselineShift = shiftY,
                                Ttf = runTtf, FontName = runName,
                                Bold = segBold, Italic = segItalic, Underline = segUnderline,
                            });
                            x += runW;
                            if (itemH > maxH) maxH = itemH;
                        }

                        // A piece the segment's own face covers and that fits whole at the
                        // pen stays ONE run (one show op per segment).
                        if (segTtf is not null && !NeedsGlyphFallback(segPiece, segTtf))
                        {
                            var whole = isArabic ? Aspose.Pdf.Text.ArabicTextShaper.Shape(segPiece) : segPiece;
                            var fullW = MeasureWidthWithFont(whole, baseFs, segTtf);
                            if (x + fullW <= contentW)
                            {
                                AddRun(whole, fullW, segTtf, segFontName, baseFs, 0);
                                continue;
                            }
                        }
                        // Standard-14 piece (sub/superscript at a reduced size with a
                        // baseline shift) that needs no glyph fallback and fits: one run.
                        if (segTtf is null && !NeedsGlyphFallback(segPiece, null))
                        {
                            var segFs = (sup || sub) ? baseFs * SubSuperScale : baseFs;
                            var shift = sup ? baseFs * SuperscriptRise : sub ? -baseFs * SubscriptRise : 0.0;
                            var sw = segBold ? MeasureFaceExact(segPiece, segFs, true)
                                : MeasureWidthExact(segPiece, segFs);
                            if (x + sw <= contentW || sup || sub)
                            {
                                if (current.Count > 0 && x + sw > contentW) { Flush(); x += marginL; }
                                AddRun(segPiece, sw, null, null, segFs, shift);
                                continue;
                            }
                        }
                        // Otherwise the piece wraps token by token at the cell width. Each
                        // token draws in the face that covers it — the segment's own, or a
                        // glyph-covering substitute when that face lacks its script (Greek
                        // and Arabic in a Standard-14 cell, ideographs in Arial) — and a
                        // token wider than the cell is cut character by character.
                        foreach (var token in SplitKeepingSpaces(segPiece))
                        {
                            // A token is a chain of script runs, each in the face that
                            // covers it (a Greek+ideograph token draws its Greek in the
                            // Latin face and its ideographs in the CJK face).
                            var runs = new List<(string text, byte[]? ttf, string? name)>();
                            foreach (var piece in SplitScriptRuns(token))
                            {
                                var (pTtf, pName) = ResolveTokenFace(piece, ss.Font, segTtf, segFontName, faceCache);
                                var pText = pTtf is not null && Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(piece)
                                    ? Aspose.Pdf.Text.ArabicTextShaper.Shape(piece) : piece;
                                runs.Add((pText, pTtf, pName));
                            }
                            double W(string s, byte[]? ttf) => s.Length == 0 ? 0 : ttf is not null
                                ? MeasureWidthWithFont(s, baseFs, ttf) : MeasureWidthExact(s, baseFs);
                            double tw = 0;
                            foreach (var rn in runs) tw += W(rn.text, rn.ttf);
                            // The fit test ignores the token's trailing space: a word that
                            // fits stays on the line and keeps its space only when the space
                            // fits too (otherwise the space is dropped with the break).
                            var lastRun = runs[^1];
                            var lastCore = lastRun.text.TrimEnd(' ');
                            var coreW = tw - W(lastRun.text, lastRun.ttf) + W(lastCore, lastRun.ttf);
                            if (current.Count > 0 && x + coreW > contentW) { Flush(); x += marginL; }
                            if (coreW <= contentW)
                            {
                                var keepSpace = x + tw <= contentW;
                                for (var rIdx = 0; rIdx < runs.Count; rIdx++)
                                {
                                    var (rt, rTtf, rName) = runs[rIdx];
                                    if (rIdx == runs.Count - 1 && !keepSpace) rt = lastCore;
                                    if (rt.Length == 0) continue;
                                    AddRun(rt, W(rt, rTtf), rTtf, rName, baseFs, 0);
                                }
                                continue;
                            }
                            // Wider than the cell: character fill across the runs.
                            foreach (var (rt, rTtf, rName) in runs)
                            {
                                var rest = rt;
                                while (rest.Length > 0)
                                {
                                    var take = 0;
                                    double pw = 0;
                                    while (take < rest.Length)
                                    {
                                        var len = char.IsHighSurrogate(rest[take]) && take + 1 < rest.Length
                                                  && char.IsLowSurrogate(rest[take + 1]) ? 2 : 1;
                                        var cw = W(rest.Substring(take, len), rTtf);
                                        if (take > 0 && x + pw + cw > contentW) break;
                                        pw += cw;
                                        take += len;
                                    }
                                    // A run whose first glyph no longer fits moves to the next line.
                                    if (current.Count > 0 && x + pw > contentW) { Flush(); x += marginL; continue; }
                                    AddRun(rest.Substring(0, take), pw, rTtf, rName, baseFs, 0);
                                    rest = rest.Substring(take);
                                    if (rest.Length > 0) { Flush(); x += marginL; }
                                }
                            }
                        }
                        }
                    }
                }
                else
                {
                    var text = tf.Text ?? string.Empty;
                    var fs = ResolveFragmentFontSize(tf, defaultFontSize);
                    var color = tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
                    // Fragment-level embedded font AND CJK content: draw it as Type0/CID with the
                    // font's real advances instead of the Standard-14 path (which would emit '?').
                    // Scoped to CJK so an embedded Latin font keeps the existing inline path.
                    var fragTtf = tf.TextState.Font?.SourceFontData?.TtfData;
                    var cjk = fragTtf is not null && CjkCoveredBy(text, fragTtf);
                    var runTtf = cjk ? fragTtf : null;
                    var runName = cjk ? tf.TextState.Font?.FontName : null;
                    double Wid(string t) => t.Length == 0 ? 0
                        : runTtf is not null ? MeasureWidthWithFont(t, fs, runTtf)
                        : MeasureWidthExact(t, fs);
                    var itemH2 = fs * 1.2;
                    void Emit(string t, double w)
                    {
                        current.Add(new InlineItem
                        {
                            Text = t, FontSize = fs, Color = color, X = x, Width = w,
                            Height = itemH2, BaseFontSize = fs, Ttf = runTtf, FontName = runName,
                        });
                        x += w;
                        if (itemH2 > maxH) maxH = itemH2;
                    }
                    var whole = Wid(text);
                    if (x + marginL + whole <= contentW)
                    {
                        // Fits at the pen: one run, as before.
                        if (current.Count > 0 && x + marginL + whole > contentW) Flush();
                        x += marginL;
                        Emit(text, whole);
                    }
                    else
                    {
                        // …otherwise it WRAPS. Emitting the whole paragraph as a single
                        // run drew a 280-character cell as one line and clipped the rest
                        // away at the cell edge; an inline fragment breaks at its tokens
                        // like every other cell paragraph.
                        if (current.Count > 0) Flush();
                        x += marginL;
                        foreach (var token in SplitKeepingSpaces(text))
                        {
                            var core = token.TrimEnd(' ');
                            var coreW = Wid(core);
                            var tokW = Wid(token);
                            if (current.Count > 0 && x + coreW > contentW) { Flush(); x += marginL; }
                            if (coreW <= contentW)
                            {
                                Emit(x + tokW <= contentW ? token : core,
                                     x + tokW <= contentW ? tokW : coreW);
                                continue;
                            }
                            // A token wider than the cell fills character by character.
                            var rest = token;
                            while (rest.Length > 0)
                            {
                                var take = 0; double tw2 = 0;
                                while (take < rest.Length)
                                {
                                    var nw = Wid(rest.Substring(0, take + 1));
                                    if (take > 0 && x + nw > contentW) break;
                                    tw2 = nw; take++;
                                }
                                if (take == 0) { take = 1; tw2 = Wid(rest.Substring(0, 1)); }
                                Emit(rest.Substring(0, take), tw2);
                                rest = rest.Substring(take);
                                if (rest.Length > 0) { Flush(); x += marginL; }
                            }
                        }
                    }
                }
                NoteAlign(FragAlign(tf));
                // The row stays open: a following IsInLineParagraph fragment joins
                // this one's line (probed: a plain " | " fragment followed by an
                // inline "Aspose URL" draws as one line, the link pen-chained at the
                // first run's measured end). A following block paragraph flushes
                // the row itself on entry.
            }
            else if (para is Image inlineImg)
            {
                // An Image among inline paragraphs joins the line as a fixed box;
                // following IsInLineParagraph text continues on the same line
                // (so the row does NOT flush after the image).
                var bytes = ReadImageBytes(inlineImg);
                if (bytes is null) continue;
                if (!inlineImg.IsInLineParagraph) Flush();
                double dispW, dispH;
                if (inlineImg.FixWidth > 0 && inlineImg.FixHeight > 0)
                {
                    dispW = inlineImg.FixWidth;
                    dispH = inlineImg.FixHeight;
                }
                else if (TryGetCellImageSizePt(bytes, out var nw, out var nh) && nw > 0 && nh > 0)
                {
                    dispW = contentW < double.MaxValue && nw > contentW ? contentW : nw;
                    dispH = nh;
                }
                else
                {
                    dispW = dispH = 24;
                }
                if (current.Count > 0 && x + dispW > contentW) Flush();
                current.Add(new InlineItem { ImageData = bytes, X = x, Width = dispW, Height = dispH });
                x += dispW;
                if (dispH > maxH) maxH = dispH;
                NoteAlign(inlineImg.HorizontalAlignment);
            }
            // Other paragraph kinds inside a graph cell are not laid out inline.
        }
        Flush();
        if (rows.Count == 0) rows.Add(new List<InlineItem>());
        lineHeight = maxH > 0 ? maxH : defaultFontSize;
        return rows;
    }
}
