using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
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
        var bf = new BgColorFlushState();
        bf.pageBuilder = new Content.ContentStreamBuilder();
        bf.formBuilders = new Dictionary<PdfStream, Content.ContentStreamBuilder>();
        foreach (var frag in _bgColorFragments)
        {
            if (!FlushFragmentBackground(bf, frag)) break;
        }
        bf.bytes = bf.pageBuilder.Build();
        if (bf.bytes.Length > 0)
        {
            PrependContentStream(bf.bytes);
        }
        foreach (var (formStream, formBuilder) in bf.formBuilders)
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
                        // An Acrobat-style background is a Form XObject (OCG
                        // "Background") invoked at the stream head — look for the
                        // full-page fill inside it.
                        return BackgroundFromFormXObject(parts, mb);
                }
            }
        }
        catch { /* malformed content — report no background */ }
        return null;
    }

    /// <summary>An Acrobat-style background is a Form XObject (OCG "Background") invoked at
    /// the stream head - look for the full-page fill inside it.</summary>
    private Color? BackgroundFromFormXObject(string[] parts, Rectangle mb)
    {
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

    /// <summary>
    /// Determines whether the page is blank. Annotations other than form widgets and
    /// printer marks make the page non-blank; any visible text, shading, or non-white
    /// vector fill or stroke does too. Otherwise the non-white pixel fractions of the
    /// drawn images are summed and compared with <paramref name="fillThresholdFactor"/>.
    /// </summary>
    /// <param name="fillThresholdFactor">The largest image coverage (0..1) that still
    /// counts as blank. The default 0 means any non-white image pixel makes the page
    /// non-blank.</param>
    /// <returns><c>true</c> when the page's coverage does not exceed the threshold.</returns>
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
        var bk = new BlankCoverageState();
        bk.tolerance = tolerance;
        bk.crop = Devices.SoftwarePageRenderer.EffectiveCropRect(this);
        bk.sum = 0;
        bk.hard = false;
        bk.counted = new HashSet<string>(StringComparer.Ordinal);

        bk.resources0 = _reader.ResolveDict(_dict.Get("Resources"));
        using var contentMs = new MemoryStream();
        bk.contentsObj = _reader.Resolve(_dict.Get("Contents"));
        if (bk.contentsObj is PdfStream single)
        {
            try { var d = _reader.DecodeStream(single); contentMs.Write(d, 0, d.Length); } catch { }
        }
        else if (bk.contentsObj is PdfArray contentArr)
        {
            foreach (var item in contentArr)
            {
                if (_reader.ResolveStream(item) is not { } cs) continue;
                try { var d = _reader.DecodeStream(cs); contentMs.Write(d, 0, d.Length); contentMs.WriteByte((byte)'\n'); }
                catch { }
            }
        }
        bk.contents = contentMs.ToArray();
        if (bk.contents.Length > 0) WalkBlankCoverage(bk, bk.contents, bk.resources0, 0);
        return bk.hard ? double.PositiveInfinity : bk.sum;
    }
}
