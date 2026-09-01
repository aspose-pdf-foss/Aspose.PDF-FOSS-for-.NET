using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void RestoreStateOp(ExtractRunsState xr)
    {
        if (xr.ctmStack.Count > 0)
            xr.ctm = xr.ctmStack.Pop();
        if (xr.clipStack.Count > 0)
            xr.currentClip = xr.clipStack.Pop();
        if (xr.gsStack.Count > 0)
        {
            var saved = xr.gsStack.Pop();
            xr.leading = saved.leading;
            xr.charSpacing = saved.charSpacing;
            xr.wordSpacing = saved.wordSpacing;
            xr.hScaling = saved.hScaling;
            xr.textRise = saved.textRise;
            xr.renderMode = saved.renderMode;
            xr.currentFillColor = saved.fillColor;
            xr.currentStrokeColor = saved.strokeColor;
            xr.currentFontName = saved.fontName;
            xr.currentFontNameForGuard = saved.fontNameGuard;
            xr.fontSize = saved.fontSize;
            xr.fontDict = saved.fontDict;
            xr.toUnicode = saved.toUnicode;
            xr.metrics = saved.metrics;
            xr.currentFontInfo = saved.fontInfo;
            xr.currentIsBold = saved.isBold;
            xr.currentIsItalic = saved.isItalic;
            xr.fontIsBold = saved.fontBold;
            xr.currentFontMissing = saved.fontMissing;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void FillPathOp(ExtractRunsState xr)
    {
        if ((xr.fillRects is not null || xr.coverRects is not null)
            && !xr.currentPathHasNonRect && xr.pendingPathRects.Count > 0)
        {
            // In default mode (no graphics/underline option) only thin rects —
            // underline/strikeout candidates — are retained, so always-on
            // collection stays cheap on graphics-heavy pages. When a consumer
            // that needs full fill geometry (background capture) is enabled,
            // keep every rect.
            bool keepAll = xr.keepAllFillRects;
            foreach (var (x, y, w, h, ctmAtRe) in xr.pendingPathRects)
            {
                // Transform the four corners by the CTM at the time of re,
                // then take the axis-aligned bounding box (handles rotation).
                var (x1, y1) = ApplyCtm(x, y, ctmAtRe);
                var (x2, y2) = ApplyCtm(x + w, y, ctmAtRe);
                var (x3, y3) = ApplyCtm(x + w, y + h, ctmAtRe);
                var (x4, y4) = ApplyCtm(x, y + h, ctmAtRe);
                var llx = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
                var lly = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
                var urx = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
                var ury = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
                // Occlusion candidates: a body-sized opaque fill (both
                // dimensions above glyph-bar size) that paints AFTER the
                // runs collected so far covers them (redaction-style
                // hidden text). RunsBefore = the run count at paint time.
                if (xr.coverRects is not null && (ury - lly) >= 6.0 && (urx - llx) >= 6.0)
                    AddCoverRect(xr.coverRects, llx, lly, urx, ury, xr.currentClip, xr.result.Count);
                if (xr.fillRects is null) continue;
                if (!keepAll && (ury - lly) >= 6.0) continue;
                xr.fillRects.Add(new RawFillRect(llx, lly, urx, ury, xr.currentFillColor, x, y, w, h));
            }
        }
        // Non-rect fills also occlude: a rounded rect (m/c/…/h) or a
        // closed polygon painted over text hides it just like a `re`
        // fill. Trust the union bbox only for a SINGLE subpath — a
        // multi-subpath even-odd fill is typically a hollow frame
        // whose bbox interior stays visible.
        else if (xr.coverRects is not null && xr.currentPathHasNonRect
            && xr.pathSubpaths == 1 && !double.IsInfinity(xr.pathMinX)
            && (xr.pathMaxY - xr.pathMinY) >= 6.0 && (xr.pathMaxX - xr.pathMinX) >= 6.0)
        {
            AddCoverRect(xr.coverRects, xr.pathMinX, xr.pathMinY, xr.pathMaxX, xr.pathMaxY, xr.currentClip, xr.result.Count);
        }
        ApplyPendingClip(xr);
        ResetPathBbox(xr);
        xr.pendingPathRects.Clear();
        xr.currentPathHasNonRect = false;
        xr.strokePts.Clear();
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void StrokePathOp(ExtractRunsState xr)
    {
        // A horizontal stroked segment is an underline/strikeout rule:
        // record it as a thin decoration rect (height = line width).
        if (xr.fillRects is not null && xr.strokePts.Count == 2)
        {
            var (ax, ay, actm) = xr.strokePts[0];
            var (bx, by, _) = xr.strokePts[1];
            if (Math.Abs(ay - by) < 1e-3)
            {
                var half = Math.Max(xr.currentLineWidth, 0.1) / 2.0;
                var (lx, lyC) = ApplyCtm(Math.Min(ax, bx), ay, actm);
                var (rx, ryC) = ApplyCtm(Math.Max(ax, bx), ay, actm);
                var lineY = (lyC + ryC) / 2;
                var (_, hTop) = ApplyCtm(Math.Min(ax, bx), ay + half, actm);
                var thick = Math.Abs(hTop - lineY) * 2;
                if (thick < 0.2) thick = Math.Max(xr.currentLineWidth, 0.5);
                var lly = lineY - thick / 2;
                var ury = lineY + thick / 2;
                if (xr.keepAllFillRects || (ury - lly) < 6.0)
                    xr.fillRects.Add(new RawFillRect(
                        Math.Min(lx, rx), lly, Math.Max(lx, rx), ury,
                        xr.currentStrokeColor ?? Color.Black, Math.Min(ax, bx), ay,
                        Math.Abs(bx - ax), thick));
            }
        }
        ApplyPendingClip(xr);
        ResetPathBbox(xr);
        xr.pendingPathRects.Clear();
        xr.currentPathHasNonRect = false;
        xr.strokePts.Clear();
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void BeginTextOp(ExtractRunsState xr)
    {
        // PDF spec: BT resets the text matrix and text line matrix to identity.
        // Reset text position and matrix components so subsequent Td/TD/Tm start fresh.
        // Do NOT reset lastEmittedY — it tracks cross-BT-block Y position
        // to prevent spurious newline sentinels between adjacent BT blocks.
        xr.tx = xr.txLine = 0;
        xr.ty = xr.tyLine = 0;
        xr.tmA = 1.0; xr.tmB = 0.0; xr.tmC = 0.0; xr.tmD = 1.0;
        xr.tmBaseTy = 0;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetFontOp(ExtractRunsState xr)
    {
        if (xr.operands.Count >= 2 && xr.operands[0] is PdfName fn)
        {
            xr.currentFontName = fn.Value;
            xr.currentFontNameForGuard = fn.Value;
            xr.fontSize = GetNum(xr.operands[1]);
            xr.currentIsBold = false;
            xr.fontIsBold = false;
            xr.currentIsItalic = false;
            xr.currentFontMissing = false;
            if (xr.fonts.TryGetValue(xr.currentFontName, out var fd))
            {
                xr.fontDict = fd;
                // Strict validation: a TrueType subset addressed by raw glyph
                // indices (FirstChar < 32) with NEITHER /Encoding NOR /ToUnicode
                // NOR a decodable cmap in its embedded program (only a symbolic
                // (3,0) subtable) cannot be decoded — decoding throws
                // rather than emitting garbage (a (1,0) Mac subtable
                // keeps such a subset extractable). IgnoreResourceFontErrors
                // opts out.
                if (xr.strictFonts && xr.depth == 0
                    && fd.GetName("Subtype") == "TrueType"
                    && fd.Get("Encoding") is null
                    && fd.Get("ToUnicode") is null
                    && (int)fd.GetInt("FirstChar") is > 0 and < 32
                    && !IsStandardSymbolFamily(fd.GetName("BaseFont"))
                    && HasOnlySymbolCmap(fd, xr.reader))
                    throw new IncorrectFontUsageException(
                        $"Font {fn.Value} cannot be used for text extraction: no encoding or Unicode mapping is available.");
                // Prefer BaseFont name (e.g. "ArialMT") over resource key (e.g. "TT2")
                var baseFontName = fd.GetName("BaseFont");
                if (baseFontName is not null)
                    xr.currentFontName = baseFontName;
                // UseFontEngineEncoding: ignore /ToUnicode and decode via
                // the font program's own encoding/cmap instead (recovers
                // text when the ToUnicode map is wrong or absent).
                xr.toUnicode = xr.useFontEngineEncoding
                    ? null
                    : TextAbsorber.ParseToUnicodeFromDict(fd, xr.reader);
                xr.metrics = FontMetrics.FromFontDict(fd, xr.reader);

                // Create FontInfo from the resolved font dictionary
                xr.currentFontInfo = new Font(fn.Value, fd, xr.reader);
                // Nameless Type3 fonts report the collection's synthesised
                // "T3Font_<n>" handle rather than the "Unknown" BaseFont.
                if (xr.currentFontInfo.IsNamelessType3
                    && xr.t3Names.TryGetValue(fn.Value, out var t3n))
                    xr.currentFontInfo.SynthesizedFontName = t3n;

                // Resolve bold/italic from font descriptor flags
                var descriptor = xr.reader.ResolveDict(fd.Get("FontDescriptor"));
                if (descriptor is not null)
                {
                    var flagsVal = (int)descriptor.GetInt("Flags");
                    xr.currentIsItalic = (flagsVal & 64) != 0;
                    xr.currentIsBold = (flagsVal & (1 << 18)) != 0;
                }
                // Also check BaseFont name for bold/italic hints
                if (baseFontName is not null)
                {
                    var upper = baseFontName.ToUpperInvariant();
                    if (!xr.currentIsBold && (upper.Contains("BOLD") || upper.Contains(",BOLD")))
                        xr.currentIsBold = true;
                    if (!xr.currentIsItalic && (upper.Contains("ITALIC") || upper.Contains("OBLIQUE") || upper.Contains(",ITALIC")))
                        xr.currentIsItalic = true;
                }
                xr.fontIsBold = xr.currentIsBold;
                // Apply Tr-based bold if current render mode is fill+stroke
                if (xr.renderMode == 2)
                    xr.currentIsBold = true;
            }
            else if (xr.depth == 0
                && !TextAbsorber.FontResourceKeyExists(xr.resourceDict, xr.reader, fn.Value))
            {
                // Only a key genuinely ABSENT from the Resources
                // hierarchy drops its text and gets reported. A key
                // that is present but unresolvable in-memory (a
                // just-registered replacement font awaiting save)
                // keeps the legacy carry-over decode.
                xr.currentFontMissing = true;
                if (xr.missingFontKeys is not null
                    && !xr.missingFontKeys.Contains(fn.Value))
                    xr.missingFontKeys.Add(fn.Value);
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void MoveTextLineOp(ExtractRunsState xr)
    {
        if (xr.operands.Count >= 2)
        {
            var tdxVal = GetNum(xr.operands[0]);
            var tdyVal = GetNum(xr.operands[1]);
            // Td values are in unscaled text space; apply the text matrix to convert
            // to content-stream space: new_line = Tm(a,b,c,d) × (tdx, tdy) + old_line.
            xr.txLine = xr.tmA * tdxVal + xr.tmC * tdyVal + xr.txLine;
            xr.tyLine = xr.tmB * tdxVal + xr.tmD * tdyVal + xr.tyLine;
            xr.tx = xr.txLine;
            xr.ty = xr.tyLine;
            // Insert newline sentinel for significant vertical displacement.
            var pageDisp = Math.Abs(xr.tmB * tdxVal + xr.tmD * tdyVal);
            if (pageDisp > 0.5 && xr.result.Count > 0 && xr.result[^1].Text != "\r\n")
                xr.result.Add(new RawTextRun("\r\n", xr.tx, xr.ty, xr.fontSize, xr.currentFontName, 0, xr.ctm, xr.metrics));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void MoveTextLineSetLeadingOp(ExtractRunsState xr)
    {
        if (xr.operands.Count >= 2)
        {
            var tdxD = GetNum(xr.operands[0]);
            var tdyD = GetNum(xr.operands[1]);
            xr.txLine = xr.tmA * tdxD + xr.tmC * tdyD + xr.txLine;
            xr.tyLine = xr.tmB * tdxD + xr.tmD * tdyD + xr.tyLine;
            xr.tx = xr.txLine;
            xr.ty = xr.tyLine;
            xr.leading = -tdyD; // TD sets TL = -ty (in unscaled text space)
            var pageDispD = Math.Abs(xr.tmB * tdxD + xr.tmD * tdyD);
            if (pageDispD > 0.5 && xr.result.Count > 0 && xr.result[^1].Text != "\r\n")
                xr.result.Add(new RawTextRun("\r\n", xr.tx, xr.ty, xr.fontSize, xr.currentFontName, 0, xr.ctm, xr.metrics));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void SetTextMatrixOp(ExtractRunsState xr)
    {
        if (xr.operands.Count >= 6)
        {
            var newTmTx = GetNum(xr.operands[4]);
            var newTmTy = GetNum(xr.operands[5]);
            // Track all Tm components so Td/TD/T* can scale displacements correctly.
            xr.tmA = GetNum(xr.operands[0]);
            xr.tmB = GetNum(xr.operands[1]);
            xr.tmC = GetNum(xr.operands[2]);
            xr.tmD = GetNum(xr.operands[3]); // raw value; use Math.Abs where needed for thresholds
            // Emit newline sentinel when Tm repositions to a different Y line.
            // Compare against lastEmittedY (not ty) so that BT resets (ty=0)
            // don't cause false newlines when consecutive BT blocks are on the same line.
            var tmRefY = !double.IsNaN(xr.lastEmittedY) ? xr.lastEmittedY : xr.ty;
            bool tmLineBreak = Math.Abs(newTmTy - tmRefY) > Math.Max(1.0, xr.fontSize * 0.3);
            // Producers that carry the line position in each block's cm
            // (Tm y stays 0 across all lines) defeat the text-space test:
            // compare page-space Y as well, thresholded by the EFFECTIVE
            // page-space font size, so their line breaks are still seen.
            // STRICTLY axis-aligned geometry only: both the Tm and the CTM
            // must be rotation-free. Under a rotated CTM the page-Y of a Tm
            // position varies with text-X, so stacked rotated labels (Tm
            // identity, rotation in cm) would get a false sentinel per label;
            // a rotated/curved Tm moves page-Y along the line the same way.
            // With both rotation-free, page-Y depends on text-Y and the cm
            // translation alone — exactly the per-block-cm producer shape.
            if (!tmLineBreak && !double.IsNaN(xr.lastEmittedPageY)
                && Math.Abs(xr.tmB) <= 1e-4 * Math.Abs(xr.tmA)
                && Math.Abs(xr.ctm.B) <= 1e-4 * Math.Abs(xr.ctm.A)
                && Math.Abs(xr.ctm.C) <= 1e-4 * Math.Abs(xr.ctm.D))
            {
                var (_, newPageY) = ApplyCtm(newTmTx, newTmTy, xr.ctm);
                var refFs = double.IsNaN(xr.lastEmittedFs) ? xr.fontSize
                    : Math.Min(xr.fontSize, xr.lastEmittedFs);
                var effTmFs = refFs * Math.Max(Math.Abs(xr.tmD), 0.001)
                    * Math.Sqrt(Math.Abs(xr.ctm.A * xr.ctm.D - xr.ctm.B * xr.ctm.C));
                tmLineBreak = Math.Abs(newPageY - xr.lastEmittedPageY)
                    > Math.Max(1.0, effTmFs * 0.3);
            }
            if (tmLineBreak
                && xr.result.Count > 0 && xr.result[^1].Text != "\r\n")
                xr.result.Add(new RawTextRun("\r\n", newTmTx, newTmTy, xr.fontSize, xr.currentFontName, 0, xr.ctm, xr.metrics));
            xr.tx = xr.txLine = newTmTx;
            xr.ty = xr.tyLine = newTmTy;
            xr.tmBaseTy = newTmTy;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void NextTextLineOp(ExtractRunsState xr)
    {
        // T* = Td(0, -TL): move to the start of the next line.
        // Apply the text matrix scale to the leading displacement.
        xr.txLine = xr.tmA * 0 + xr.tmC * (-xr.leading) + xr.txLine;
        xr.tyLine = xr.tmB * 0 + xr.tmD * (-xr.leading) + xr.tyLine;
        xr.tx = xr.txLine;
        xr.ty = xr.tyLine;
        {
            // Unlike Td/Tm (where a sentinel after a sentinel usually means the
            // producer re-stated the same move), each T* is an explicit one-line
            // advance, so consecutive T* = genuinely blank lines. Emit a sentinel
            // per advance; sentinel consumers already skip runs of them.
            var pageDispStar = Math.Abs(Math.Abs(xr.tmD) * xr.leading);
            if (pageDispStar > 0.5 && xr.result.Count > 0)
                xr.result.Add(new RawTextRun("\r\n", xr.tx, xr.ty, xr.fontSize, xr.currentFontName, 0, xr.ctm, xr.metrics));
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void DrawXObjectOp(ExtractRunsState xr)
    {
        if (xr.operands.Count >= 1 && xr.operands[0] is PdfName xobjName)
        {
            var xobjects = TextAbsorber.ResolveXObjects(xr.resourceDict, xr.reader);
            if (xobjects is not null)
            {
                var xobjStream = xr.reader.ResolveStream(xobjects.Get(xobjName.Value));
                if (xobjStream is not null &&
                    xr.reader.ResolveName(xobjStream.Dict, "Subtype") == "Form")
                {
                    // Within one absorber run a Form XObject INDIRECT OBJECT is
                    // absorbed at most once — the first Do wins, every later Do of
                    // the same object (same page or a later page of a document
                    // walk) contributes nothing. Keyed by object identity: two
                    // distinct objects with identical bytes are both absorbed, a
                    // different placement matrix does not defeat the dedup.
                    if (xr.seenForms is not null && !xr.seenForms.Add(xobjStream))
                        return;
                    var xobjBytes = xr.reader.DecodeStream(xobjStream);
                    var xobjDict = xobjStream.Dict;

                    // Compute the CTM for the XObject: current CTM × form's own /Matrix
                    var xobjCtm = xr.ctm;
                    var matrixArr = xr.reader.ResolveArray(xobjDict.Get("Matrix"));
                    if (matrixArr is { Count: >= 6 })
                    {
                        var fm = new Matrix(
                            GetNum(matrixArr[0]), GetNum(matrixArr[1]),
                            GetNum(matrixArr[2]), GetNum(matrixArr[3]),
                            GetNum(matrixArr[4]), GetNum(matrixArr[5]));
                        xobjCtm = fm.Multiply(xr.ctm);
                    }

                    var runCountBefore = xr.result.Count;
                    ExtractRuns(xobjBytes, xobjDict, xr.reader, xr.result, xr.depth + 1, xobjCtm, xr.fillRects, xr.useFontEngineEncoding, xr.keepAllFillRects, xr.coverRects, xr.currentClip, seenForms: xr.seenForms, missingFontKeys: xr.missingFontKeys);
                    // Stamp the runs this form produced with their source
                    // stream (innermost wins for nested forms — inner
                    // recursion stamped its own runs first).
                    for (var ri = runCountBefore; ri < xr.result.Count; ri++)
                        if (xr.result[ri].SourceXObj is null)
                            xr.result[ri] = xr.result[ri] with { SourceXObj = xobjStream };
                }
            }
        }
    }
}
