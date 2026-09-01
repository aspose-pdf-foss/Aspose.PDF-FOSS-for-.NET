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
    /// Register a fragment whose segment(s) had BackgroundColor set after extraction.
    /// Called from <see cref="Text.TextState.BackgroundColor"/> setter when the
    /// segment traces back to this page.
    /// </summary>
    internal void RegisterBgColorFragment(Text.TextFragment fragment)
    {
        _bgColorFragments ??= new();
        _bgColorFragments.Add(fragment);
    }

    /// <summary>
    /// Inject 're'/'f' operators at the start of the content stream for every
    /// registered background-colour fragment. Called during save before the page
    /// content stream is flushed.
    /// </summary>
    internal void FlushBgColorRectangles()
    {
        if (_bgColorFragments is null || _bgColorFragments.Count == 0) return;
        // Start from what the page ACTUALLY holds. An earlier save-time pass may have
        // appended or prepended a stream, and the cached operator view is a snapshot from
        // before it existed - flushing that view back would restore the page as it was and
        // drop the append. Only these passes need it: resetting inside the append itself
        // pulls the view out from under a caller that is still building with it.
        ResetContentsCache();
        var pageBuilder = new Content.ContentStreamBuilder();
        // A fragment extracted from a Form XObject gets its highlight drawn INTO
        // that form's stream (before its text), not onto the page: the rectangle
        // must live where the text lives, both for the paint order under nested
        // content and for consumers reading the form's own operator list.
        var formBuilders = new Dictionary<PdfStream, Content.ContentStreamBuilder>();
        foreach (var frag in _bgColorFragments)
        {
            Content.ContentStreamBuilder builder;
            if (frag.SourceXObjStream is { } sourceForm)
            {
                if (!formBuilders.TryGetValue(sourceForm, out builder!))
                    formBuilders[sourceForm] = builder = new Content.ContentStreamBuilder();
            }
            else
                builder = pageBuilder;

            var fragBg = frag.TextState.BackgroundColor;

            // A fragment whose SOURCE decorations were captured had them spliced out, so its
            // highlight is a REPLACEMENT for the rect that stood there and belongs where that
            // rect stood - inline, immediately before the run it backs. Prepended to the page
            // it lands under everything the source draws, including the very highlight it
            // replaces, and is simply not seen.
            if (fragBg is { IsEmpty: false } && frag.SourceXObjStream is null
                && (frag.CapturedUnderlineSources is { Count: > 0 }
                    || frag.CapturedBackgroundSources is { Count: > 0 })
                && frag.PositionOrNull is { } inlinePos && frag.Rectangle is { } inlineRect)
            {
                var inlineFs = frag.TextState.RawFontSize > 0
                    ? (double)frag.TextState.RawFontSize
                    : (frag.TextState.FontSize > 0 ? frag.TextState.FontSize : 12);
                var inlineH = ComputeBgRectHeight(frag.TextState.FontName ?? "",
                    frag.TextState.Font, inlineFs,
                    Math.Abs(frag.TextState.TmD) > 0.001 ? Math.Abs(frag.TextState.TmD) : 1.0);
                if (InsertBeforeTextObjectAt(
                        DecorationBlock(fragBg, inlinePos.XIndent, inlinePos.YIndent,
                            inlineRect.Width, inlineH),
                        inlinePos.XIndent, inlinePos.YIndent))
                    continue;
            }

            // The run's own transform context, shared by the fragment- and
            // segment-level emitters below.
            var ctm = frag.ExtractionCtm;
            double ctmScaleX = ctm is not null ? Math.Sqrt(ctm.A * ctm.A + ctm.B * ctm.B) : 1.0;
            bool hasCtm = ctmScaleX > 1.5; // significant scaling (>1.5x)
            bool ctmNonIdentity = ctm is not null
                && (Math.Abs(ctm.A - 1) + Math.Abs(ctm.B) + Math.Abs(ctm.C)
                    + Math.Abs(ctm.D - 1) + Math.Abs(ctm.E) + Math.Abs(ctm.F)) > 1e-6;
            // An axis-aligned translated/scaled frame — including a y-DOWN
            // (flipped, D < 0) one, where the local rect anchors one height below
            // the inverse-mapped page bottom edge and the flip renders it back.
            Matrix? frame = !hasCtm && ctmNonIdentity && ctm is not null
                && Math.Abs(ctm.B) < 1e-6 && Math.Abs(ctm.C) < 1e-6
                && ctm.A > 1e-6 && Math.Abs(ctm.D) > 1e-6 ? ctm : null;
            // A quarter-turn frame (axis-swapping, |B|,|C| carry the scale — the
            // page-rotation composition for /Rotate content).
            bool quarterTurn = !hasCtm && ctm is not null
                && Math.Abs(ctm.A) < 1e-6 && Math.Abs(ctm.D) < 1e-6
                && Math.Abs(ctm.B) > 1e-6 && Math.Abs(ctm.C) > 1e-6;

            // Draw one highlight box. The caller supplies the PAGE-space anchor,
            // width and box height plus the metric height (raw-Tf units, which is
            // what a local frame measures in); the framing decides where the
            // numbers actually land.
            void EmitBg(Aspose.Pdf.Color color, double pageX, double pageY,
                double pageW, double metricH, double pageH)
            {
                builder.SaveState();
                if (quarterTurn)
                {
                    // The box in the CONTENT STREAM's own device space.
                    var (qx1, qy1) = ctm!.InverseTransformPoint(pageX, pageY);
                    var (qx2, qy2) = ctm.InverseTransformPoint(pageX + pageW, pageY + pageH);
                    builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
                    builder.Rectangle(Math.Min(qx1, qx2), Math.Min(qy1, qy2),
                        Math.Abs(qx2 - qx1), Math.Abs(qy2 - qy1));
                }
                else if (frame is not null)
                {
                    // Replay the run's frame around the rectangle and write the rect
                    // in that local space. cm FIRST, colour second, so the cm stays
                    // immediately before the rectangle operands.
                    var (lx, ly) = frame.InverseTransformPoint(pageX, pageY);
                    if (frame.D < 0) ly -= metricH;
                    builder.SetMatrix(frame.A, frame.B, frame.C, frame.D, frame.E, frame.F);
                    builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
                    builder.Rectangle(lx, ly, pageW / frame.A, metricH);
                }
                else
                {
                    builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
                    builder.Rectangle(pageX, pageY, pageW, metricH);
                }
                builder.Fill();
                builder.RestoreState();
            }

            // Fragment-level BackgroundColor: emit ONE rectangle.
            // Use fragment rectangle for X/width but compute height from font metrics
            // using the MAXIMUM rawFs across all segments (the tallest glyph determines height).
            if (fragBg is { IsEmpty: false } && frag.Segments.Count > 0)
            {
                // Rotation-aware path: when the text direction is not horizontal
                // (text drawn under a rotating CTM), emit the highlight as a
                // rectangle oriented along the baseline via a cm transform, so it
                // follows the rotated text instead of being an axis-aligned box.
                // Horizontal text (the default TextDirX=1, TextDirY=0) is unaffected.
                double dirX = frag.TextDirX, dirY = frag.TextDirY;
                double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (dirLen > 1e-6 && Math.Abs(dirY / dirLen) > 0.01)
                {
                    double ux = dirX / dirLen, uy = dirY / dirLen;
                    double ox = frag.PositionOrNull?.XIndent ?? frag.Rectangle?.LLX ?? 0;
                    double oy = frag.PositionOrNull?.YIndent ?? frag.Rectangle?.LLY ?? 0;

                    double rRawFs = 0, rTmD = 1.0;
                    string rFontName = frag.TextState.FontName ?? "";
                    Text.FontInfo? rFont = frag.TextState.Font;
                    foreach (var seg in frag.Segments)
                    {
                        var rfs = seg.TextState.RawFontSize > 0 ? (double)seg.TextState.RawFontSize : (double)seg.TextState.FontSize;
                        if (rfs > rRawFs)
                        {
                            rRawFs = rfs;
                            rTmD = Math.Abs(seg.TextState.TmD) > 0.001 ? Math.Abs(seg.TextState.TmD) : 1.0;
                            rFontName = seg.TextState.FontName ?? rFontName;
                            rFont = seg.TextState.Font ?? rFont;
                        }
                    }
                    if (rRawFs <= 0) rRawFs = frag.TextState.FontSize;
                    double rFs = frag.TextState.FontSize > 0 ? frag.TextState.FontSize : rRawFs;
                    double rotW = rFont?.MeasureString(frag.Text, rFs) ?? frag.Text.Length * rFs * 0.5;
                    double rotH = ComputeBgRectHeight(rFontName, rFont, rRawFs, rTmD);
                    double rotDescent = rotH * 0.21;

                    builder.SaveState();
                    builder.SetFillColor(fragBg.R / 255.0, fragBg.G / 255.0, fragBg.B / 255.0);
                    builder.SetMatrix(ux, uy, -uy, ux, ox, oy);
                    builder.Rectangle(0, -rotDescent, rotW, rotH);
                    builder.Fill();
                    builder.RestoreState();
                    continue;
                }

                double fragW, fragH, fragX, fragY;

                if (hasCtm)
                {
                    // CTM path: compute width/height from current FontSize in
                    // Tm (content-stream) space, then inverse-CTM the position.
                    double localFs = frag.TextState.FontSize / ctmScaleX;
                    Text.FontInfo? fragFont = frag.TextState.Font;
                    foreach (var seg in frag.Segments)
                        if (seg.TextState.Font is not null) { fragFont = seg.TextState.Font; break; }
                    fragW = fragFont?.MeasureString(frag.Text, localFs)
                        ?? (frag.Text.Length * localFs * 0.5);
                    fragH = localFs * 1.1;
                    fragX = frag.Rectangle?.LLX ?? frag.PositionOrNull?.XIndent ?? 0;
                    fragY = frag.Rectangle?.LLY ?? frag.PositionOrNull?.YIndent ?? 0;
                    (fragX, fragY) = ctm!.InverseTransformPoint(fragX, fragY);
                }
                else
                {
                    // Standard path: use the fragment rectangle for position/width,
                    // compute height from rawFs/TmD metrics.
                    fragW = (frag.Rectangle?.Width ?? 0) - frag.TrailingTcPageSpace;
                    fragX = (frag.Rectangle?.LLX ?? frag.PositionOrNull?.XIndent ?? 0) + frag.PostAbsorbDx;
                    fragY = (frag.Rectangle?.LLY ?? frag.PositionOrNull?.YIndent ?? 0) + frag.PostAbsorbDy;

                    double maxRawFs = 0;
                    double maxTmD = 1.0;
                    string maxFontName = frag.TextState.FontName ?? "";
                    Text.FontInfo? maxFont = frag.TextState.Font;
                    foreach (var seg in frag.Segments)
                    {
                        var rfs = seg.TextState.RawFontSize > 0 ? (double)seg.TextState.RawFontSize : (double)seg.TextState.FontSize;
                        if (rfs > maxRawFs)
                        {
                            maxRawFs = rfs;
                            maxTmD = Math.Abs(seg.TextState.TmD) > 0.001 ? Math.Abs(seg.TextState.TmD) : 1.0;
                            maxFontName = seg.TextState.FontName ?? maxFontName;
                            maxFont = seg.TextState.Font ?? maxFont;
                        }
                    }
                    if (maxRawFs <= 0) maxRawFs = frag.TextState.FontSize;

                    fragH = ComputeBgRectHeight(maxFontName, maxFont, maxRawFs, maxTmD);
                }

                EmitBg(fragBg, fragX, fragY, fragW, fragH,
                    frag.Rectangle?.Height ?? fragH);
                continue;
            }

            // Segment-level: collect segments with their own bg colour
            var segList = new List<Text.TextSegment>();
            foreach (var seg in frag.Segments)
            {
                if (seg.TextState.BackgroundColor is { IsEmpty: false } && seg.Position is not null)
                    segList.Add(seg);
            }

            // Merge consecutive segments with the same font size into single rectangles.
            // The the public API emits one rect per font-size group on the same line.
            int si = 0;
            while (si < segList.Count)
            {
                var seg = segList[si];
                var bg = seg.TextState.BackgroundColor!;
                var segPos = seg.Position!;
                var fs = seg.TextState.FontSize > 0 ? seg.TextState.FontSize : frag.TextState.FontSize;
                double startX = segPos.XIndent;
                double startY = segPos.YIndent;

                // Scan forward to merge consecutive segments from the same source run.
                // This groups segments by physical Tj/TJ operator, matching the the public API's
                // per-run background rectangles.
                int lastMerged = si;
                while (lastMerged + 1 < segList.Count)
                {
                    var nextSeg = segList[lastMerged + 1];
                    if (nextSeg.SourceRunIndex == seg.SourceRunIndex)
                        lastMerged++;
                    else
                        break;
                }

                // Width spans from first segment start to last merged segment end
                double w;
                if (lastMerged + 1 < segList.Count && segList[lastMerged + 1].Position is not null)
                {
                    w = segList[lastMerged + 1].Position!.XIndent - startX;
                }
                else
                {
                    // Last merged group: check if it's also the last segment of the fragment.
                    // If all segments have bg color, use frag.Rectangle.URX.
                    // Otherwise, compute width from font metrics for the covered segments.
                    bool isLastFragSeg = (segList.Count == frag.Segments.Count);
                    if (isLastFragSeg && frag.Rectangle is not null)
                    {
                        // A segment moved after absorption carries its box with it:
                        // the fragment rectangle still describes the ORIGINAL span,
                        // so shift its right edge by the segment's own displacement.
                        var segDx = seg.Rectangle is { } segRect ? startX - segRect.LLX : 0;
                        w = frag.Rectangle.URX + segDx - startX;
                    }
                    else
                    {
                        // Compute width from font metrics for segments si.lastMerged
                        w = 0;
                        for (int k = si; k <= lastMerged; k++)
                        {
                            var s = segList[k];
                            var font = s.TextState.Font ?? frag.TextState.Font;
                            if (font is not null)
                            {
                                try { w += font.MeasureString(s.Text, fs); }
                                catch { w += s.Text.Length * fs * 0.5; }
                            }
                            else
                                w += s.Text.Length * fs * 0.5;
                        }
                    }
                }

                var rawFs = seg.TextState.RawFontSize > 0 ? (double)seg.TextState.RawFontSize : fs;
                var tmD = Math.Abs(seg.TextState.TmD) > 0.001 ? Math.Abs(seg.TextState.TmD) : 1.0;
                var fontName2 = seg.TextState.FontName ?? frag.TextState.FontName ?? "";
                var font2 = seg.TextState.Font ?? frag.TextState.Font;
                double h = ComputeBgRectHeight(fontName2, font2, rawFs, tmD);

                EmitBg(bg, startX, startY, w, h, h);
                si = lastMerged + 1;
            }
        }
        var bytes = pageBuilder.Build();
        if (bytes.Length > 0)
        {
            PrependContentStream(bytes);
        }
        foreach (var (formStream, formBuilder) in formBuilders)
        {
            var formBytes = formBuilder.Build();
            if (formBytes.Length == 0) continue;
            var existing = _reader.DecodeStream(formStream);
            var merged = new byte[formBytes.Length + 1 + existing.Length];
            formBytes.CopyTo(merged, 0);
            merged[formBytes.Length] = (byte)'\n';
            existing.CopyTo(merged, formBytes.Length + 1);
            formStream.Dict.Remove("Filter");
            formStream.Dict.Remove("DecodeParms");
            formStream.ReplaceData(merged);
            if (formStream.ObjectNumber > 0)
                _reader.OwnerDocument?.MarkDirty(formStream.ObjectNumber, formStream);
        }
        _bgColorFragments.Clear();
    }

    /// <summary>
    /// Register a fragment whose TextState.StrikeOut was set, so a strikethrough
    /// rectangle is emitted at save time.
    /// </summary>
    internal void RegisterStrikeOutFragment(Text.TextFragment fragment)
    {
        _strikeOutFragments ??= new();
        _strikeOutFragments.Add(fragment);
    }

    /// <summary>
    /// Emit thin filled rectangles through the middle of each registered
    /// strike-through fragment. Mirrors <see cref="FlushUnderlineRectangles"/>
    /// with a baseline-relative Y offset that places the line at ~30% of the
    /// ascent above the baseline.
    /// </summary>
    internal void FlushStrikeOutRectangles()
    {
        if (_strikeOutFragments is null || _strikeOutFragments.Count == 0) return;
        var builder = new Content.ContentStreamBuilder();
        foreach (var frag in _strikeOutFragments)
        {
            var fragPos = frag.PositionOrNull;
            if (fragPos is null) continue;
            var fs = frag.TextState.FontSize;
            if (fs <= 0) fs = 12;

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

            // Strike-through offset: ~30% of font size above baseline (i.e. through
            // the visual centre of the x-height). Thickness: 5% of font size.
            double soOffset = fs * 0.30;
            double soThick = fs * 0.05;

            // Rotation-aware path (see FlushUnderlineRectangles): emit the strike
            // line along the rotated baseline via a cm transform; horizontal text
            // is unaffected.
            double soDirX = frag.TextDirX, soDirY = frag.TextDirY;
            double soDirLen = Math.Sqrt(soDirX * soDirX + soDirY * soDirY);
            if (soDirLen > 1e-6 && Math.Abs(soDirY / soDirLen) > 0.01)
            {
                double ux = soDirX / soDirLen, uy = soDirY / soDirLen;
                double rw;
                try { rw = frag.TextState.Font?.MeasureString(frag.Text, fs) ?? frag.Text.Length * fs * 0.5; }
                catch { rw = frag.Text.Length * fs * 0.5; }
                var fgr = frag.TextState.ForegroundColor;
                builder.SaveState();
                builder.SetFillColor(fgr?.R / 255.0 ?? 0, fgr?.G / 255.0 ?? 0, fgr?.B / 255.0 ?? 0);
                builder.SetMatrix(ux, uy, -uy, ux, fragPos.XIndent, fragPos.YIndent);
                builder.Rectangle(0, soOffset, rw, soThick);
                builder.Fill();
                builder.RestoreState();
                continue;
            }

            var ctm = frag.ExtractionCtm;
            bool yFlipped = ctm is not null && ctm.D < 0;
            var strikeoutY = yFlipped
                ? fragPos.YIndent - soOffset
                : fragPos.YIndent + soOffset;
            var strikeoutH = soThick;

            double rectX = fragPos.XIndent, rectY = strikeoutY;
            if (ctm is not null)
            {
                (rectX, rectY) = ctm.InverseTransformPoint(fragPos.XIndent, strikeoutY);
                var (wx, wy) = ctm.InverseTransformPoint(fragPos.XIndent + w, strikeoutY + strikeoutH);
                w = Math.Abs(wx - rectX);
                strikeoutH = Math.Abs(wy - rectY);
            }

            var fg = frag.TextState.ForegroundColor;
            double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b = fg?.B / 255.0 ?? 0;

            builder.SaveState();
            builder.SetFillColor(r, g, b);
            builder.Rectangle(rectX, rectY, w, strikeoutH);
            builder.Fill();
            builder.RestoreState();
        }
        var bytes = builder.Build();
        if (bytes.Length > 0)
            AddContentStream(bytes);
        _strikeOutFragments.Clear();
    }

    /// <summary>Scan the leading painting operators for a fill covering (almost)
    /// the whole page and return its colour; null when the page has no painted
    /// background. Only the stream prefix is examined — a background is by
    /// definition painted before the content above it.</summary>
    private Color? DetectBackgroundColor()
    {
        try
        {
            var mb = MediaBox;
            double r = 0, g = 0, b = 0;
            bool colorSet = false;
            double reX = 0, reY = 0, reW = 0, reH = 0;
            bool haveRect = false;
            // Path points for m/l-drawn rectangles.
            var pts = new System.Collections.Generic.List<(double x, double y)>();
            var opsSeen = 0;
            var nums = new System.Collections.Generic.List<double>();
            // Peek, don't enumerate Contents: materialising here would freeze an
            // empty snapshot on a not-yet-generated page and hide content written
            // to it later (generator pages read as op-less after Save).
            foreach (var s in Contents.PeekOps())
            {
                if (++opsSeen > 60) break; // background lives at the stream head
                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var name = parts[^1];
                nums.Clear();
                for (var i = 0; i < parts.Length - 1; i++)
                    if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
                        nums.Add(v);
                switch (name)
                {
                    case "rg" when nums.Count >= 3:
                        r = nums[0]; g = nums[1]; b = nums[2]; colorSet = true; break;
                    case "g" when nums.Count >= 1:
                        r = g = b = nums[0]; colorSet = true; break;
                    case "k" when nums.Count >= 4:
                        r = (1 - nums[0]) * (1 - nums[3]);
                        g = (1 - nums[1]) * (1 - nums[3]);
                        b = (1 - nums[2]) * (1 - nums[3]);
                        colorSet = true; break;
                    case "scn" or "sc" when nums.Count >= 3:
                        r = nums[0]; g = nums[1]; b = nums[2]; colorSet = true; break;
                    case "re" when nums.Count >= 4:
                        reX = nums[0]; reY = nums[1]; reW = nums[2]; reH = nums[3];
                        haveRect = true; break;
                    case "m" or "l" when nums.Count >= 2:
                        pts.Add((nums[0], nums[1])); break;
                    case "f" or "f*" or "b" or "B" or "b*" or "B*":
                    {
                        if (!colorSet) { pts.Clear(); haveRect = false; break; }
                        if (!haveRect && pts.Count >= 4)
                        {
                            reX = pts.Min(p => p.x); reY = pts.Min(p => p.y);
                            reW = pts.Max(p => p.x) - reX; reH = pts.Max(p => p.y) - reY;
                            haveRect = true;
                        }
                        if (haveRect && reW >= mb.Width * 0.9 && reH >= mb.Height * 0.9)
                            return Color.FromRgb(r, g, b);
                        pts.Clear(); haveRect = false;
                        break;
                    }
                    case "BT":
                        return null; // real content started — no background fill found
                    case "Do":
                    {
                        // An Acrobat-style background is a Form XObject (OCG
                        // "Background") invoked at the stream head — look for the
                        // full-page fill inside it.
                        if (parts.Length >= 2 && parts[0].StartsWith('/'))
                        {
                            var xname = parts[0][1..];
                            var res = _reader.ResolveDict(Dict.Get("Resources"));
                            var xobjs = res is null ? null : _reader.ResolveDict(res.Get("XObject"));
                            var xstr = xobjs is null ? null : _reader.ResolveStream(xobjs.Get(xname));
                            if (xstr is not null && xstr.Dict.GetName("Subtype") == "Form")
                            {
                                var inner = ScanBytesForBackground(_reader.DecodeStream(xstr), mb);
                                if (inner is not null) return inner;
                            }
                        }
                        return null;
                    }
                }
            }
        }
        catch { /* malformed content — report no background */ }
        return null;
    }

    public bool IsBlank(double fillThresholdFactor = 0)
    {
        // Check if there are annotations (excluding Widget annotations for form fields)
        var annots = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annots is not null)
        {
            foreach (var annotRef in annots)
            {
                var annotDict = _reader.ResolveDict(annotRef);
                if (annotDict is null) continue;
                var subtype = annotDict.GetName("Subtype");
                // Widget (form fields) and PrinterMark (pre-press marks) are not
                // page content, so they don't make the page non-blank.
                if (subtype != "Widget" && subtype != "PrinterMark") return false;
            }
        }

        // Coverage model: the page's coverage is +infinity when any
        // "hard mark" paints — a non-white vector fill/stroke intersecting the
        // crop box, a visible non-space text op (any colour, including white),
        // or a shading, recursing through form XObjects — otherwise the SUM over
        // distinct drawn image resources of each image's non-white pixel
        // fraction (computed on the image's own pixel grid; page placement and
        // crop are ignored for images). IsBlank(tol) is coverage <= tol.
        return ComputeBlankCoverage(fillThresholdFactor) <= fillThresholdFactor;
    }

    private double ComputeBlankCoverage(double tolerance)
    {
        var crop = Devices.SoftwarePageRenderer.EffectiveCropRect(this);
        double sum = 0;
        var hard = false;
        var counted = new HashSet<string>(StringComparer.Ordinal);

        void Walk(byte[] content, PdfDictionary? resources, int depth)
        {
            if (hard || depth > 16) return;
            var xobjects = _reader.ResolveDict(resources?.Get("XObject"));
            var parser = new Content.ContentStreamParser(_reader);

            parser.OnShadingPainted += (_, _) => hard = true;

            parser.OnTextShown += (text, _, state) =>
            {
                if (hard || state.RenderingMode == 3) return;
                foreach (var ch in text)
                    if (!char.IsWhiteSpace(ch) && ch != '\0') { hard = true; return; }
            };

            parser.OnPathPainted += (op, state, segments) =>
            {
                if (hard || segments.Count == 0) return;
                var fills = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
                var strokes = op is "S" or "s" or "B" or "B*" or "b" or "b*";
                var nonWhiteFill = state.FillPatternName is not null
                    || state.FillR < 0.995 || state.FillG < 0.995 || state.FillB < 0.995;
                var nonWhiteStroke = state.StrokePatternName is not null
                    || state.StrokeR < 0.995 || state.StrokeG < 0.995 || state.StrokeB < 0.995;
                if (!(fills && nonWhiteFill) && !(strokes && nonWhiteStroke)) return;

                // Device-space bbox of the path; a mark fully outside the crop box
                // is invisible and doesn't count.
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                void Grow(double px, double py)
                {
                    var (dx, dy) = state.TransformPoint(px, py);
                    if (dx < minX) minX = dx;
                    if (dy < minY) minY = dy;
                    if (dx > maxX) maxX = dx;
                    if (dy > maxY) maxY = dy;
                }
                foreach (var seg in segments)
                {
                    switch (seg.Op)
                    {
                        case Content.PathOp.MoveTo:
                        case Content.PathOp.LineTo:
                            Grow(seg.X1, seg.Y1);
                            break;
                        case Content.PathOp.CurveTo:
                            Grow(seg.X1, seg.Y1); Grow(seg.X2, seg.Y2); Grow(seg.X3, seg.Y3);
                            break;
                        case Content.PathOp.CurveToV:
                        case Content.PathOp.CurveToY:
                            Grow(seg.X1, seg.Y1); Grow(seg.X2, seg.Y2);
                            break;
                        case Content.PathOp.Rect:
                            Grow(seg.X1, seg.Y1); Grow(seg.X1 + seg.X2, seg.Y1 + seg.Y2);
                            break;
                    }
                }
                if (minX > maxX) return;
                if (maxX < crop.LLX || minX > crop.URX || maxY < crop.LLY || minY > crop.URY) return;
                hard = true;
            };

            parser.OnInlineImage += (dict, raw) =>
            {
                // Once the summed coverage exceeds the caller's tolerance the verdict
                // can't change (coverage only grows) — skip further image decodes.
                if (hard || sum > tolerance) return;
                try { sum += NonWhiteImageFraction(dict, IO.Filters.StreamFilter.Decode(raw, dict), inline: true); }
                catch { }
            };

            parser.OnImageDrawn += (name, _) =>
            {
                if (hard || sum > tolerance || xobjects is null) return;
                var xobj = _reader.ResolveStream(xobjects.Get(name));
                if (xobj is null) return;
                if (xobj.Dict.GetName("Subtype") == "Form")
                {
                    byte[] formContent;
                    try { formContent = _reader.DecodeStream(xobj); }
                    catch { return; }
                    var formRes = _reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;
                    Walk(formContent, formRes, depth + 1);
                    return;
                }
                // Distinct image resources sum; the same name drawn twice counts once.
                if (!counted.Add(depth + ":" + name)) return;
                try
                {
                    byte[] decoded;
                    try { decoded = _reader.DecodeStream(xobj); } catch { return; }
                    sum += NonWhiteImageFraction(xobj.Dict, decoded, inline: false);
                }
                catch { }
            };

            parser.Parse(content, null, null, null, null, null, null);
        }

        var resources0 = _reader.ResolveDict(_dict.Get("Resources"));
        using var contentMs = new MemoryStream();
        var contentsObj = _reader.Resolve(_dict.Get("Contents"));
        if (contentsObj is PdfStream single)
        {
            try { var d = _reader.DecodeStream(single); contentMs.Write(d, 0, d.Length); } catch { }
        }
        else if (contentsObj is PdfArray contentArr)
        {
            foreach (var item in contentArr)
            {
                if (_reader.ResolveStream(item) is not { } cs) continue;
                try { var d = _reader.DecodeStream(cs); contentMs.Write(d, 0, d.Length); contentMs.WriteByte((byte)'\n'); }
                catch { }
            }
        }
        var contents = contentMs.ToArray();
        if (contents.Length > 0) Walk(contents, resources0, 0);
        return hard ? double.PositiveInfinity : sum;
    }
}
