using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a page in a PDF document.
/// </summary>
public sealed partial class Page : IDisposable
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private int _index;
    private List<Text.TextFragment>? _attachedFragments;
    private HashSet<Text.TextFragment>? _bgColorFragments;
    private HashSet<Text.TextFragment>? _underlineFragments;
    private HashSet<Text.TextFragment>? _strikeOutFragments;
    private HashSet<Text.TextFragment>? _hyperlinkFragments;

    /// <summary>Set when a text edit on this page requested
    /// <see cref="Text.TextEditOptions.FontReplace.RemoveUnusedFonts"/>; the save
    /// pipeline then prunes /Font resources no longer referenced by any content.</summary>
    internal bool PruneUnusedFontsOnSave { get; set; }

    private List<PageInformationAnnotation>? _pageInfoAnnotations;
    private AnnotationCollection? _annotations;
    private XImageCollection? _images;
    private FontCollection? _fonts;
    private Resources? _resources;

    internal Page(PdfDictionary dict, PdfReader reader, int index)
    {
        _dict = dict;
        _reader = reader;
        _index = index;
    }

    /// <summary>0-based page index.</summary>
    internal int Index => _index;

    /// <summary>The object number this page was parsed from in the source
    /// document, or -1 for pages created in memory. Lets the save path write
    /// this page's authoritative in-memory <see cref="Dict"/> back to its
    /// original object number even when the reader's object cache has been
    /// dropped (e.g. by the page renderer), which would otherwise re-parse a
    /// pristine page dict and lose in-memory edits made after rendering.</summary>
    internal int SourceObjectNumber { get; set; } = -1;

    /// <summary>For a page imported from another document: the object number this page's
    /// dictionary must be written at, reserved so GoTo/Link destinations on other imported
    /// pages that target it resolve to this copy instead of deep-importing the source page.
    /// 0 for non-imported pages, which get a writer-allocated number.</summary>
    internal int ImportSlotObjNum { get; set; }

    /// <summary>Update the index without creating a new Page object.</summary>
    internal void SetIndex(int index) => _index = index;

    /// <summary>Register a fragment added via TextBuilder for sync on save.</summary>
    internal void RegisterAttachedFragment(Text.TextFragment fragment)
    {
        _attachedFragments ??= new();
        fragment.SourcePage = this;
        _attachedFragments.Add(fragment);
    }

    /// <summary>Sync attached fragment modifications back to the content stream.</summary>
    internal void SyncAttachedFragments()
    {
        if (_attachedFragments is null) return;
        foreach (var f in _attachedFragments)
        {
            if (f.LastWrittenText is not null && f.Text != f.LastWrittenText)
            {
                var replacer = new Text.TextReplacer();
                replacer.Replace(this, f.LastWrittenText, f.Text);
                f.LastWrittenText = f.Text;
            }
        }
    }

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
    /// Register a text fragment whose <c>Hyperlink</c> was set after absorption, so that a
    /// link annotation is emitted for it on save (mirrors the generator hyperlink path, which
    /// only runs for newly-laid-out paragraphs — not absorber-edited fragments).
    /// </summary>
    internal void RegisterHyperlinkFragment(Text.TextFragment fragment)
    {
        _hyperlinkFragments ??= new();
        _hyperlinkFragments.Add(fragment);
    }

    /// <summary>Register a PageInformationAnnotation so its file-name+date appearance is
    /// generated on save. Enumerating /Annots re-resolves the dict to a generic /PrinterMark
    /// annotation (the C# subtype is lost), so the original typed instance is tracked here.</summary>
    internal void RegisterPageInfoAnnotation(PageInformationAnnotation annot)
    {
        _pageInfoAnnotations ??= new();
        _pageInfoAnnotations.Add(annot);
    }

    /// <summary>Generate the appearance of every registered PageInformationAnnotation with the
    /// supplied output file name. Called during save once the file name is known.</summary>
    internal void FlushPageInfoAnnotations(string fileName, DateTime date)
    {
        if (_pageInfoAnnotations is null) return;
        foreach (var pia in _pageInfoAnnotations)
            pia.GenerateInfoAppearance(fileName, date);
    }

    /// <summary>
    /// Emit a Link annotation for every fragment whose hyperlink was set via the absorber/edit
    /// path. The fragment rectangle is in the page's displayed (rotation-applied) coordinate
    /// frame, so it is mapped back to unrotated page space for the annotation /Rect. Called
    /// during save before the content stream is flushed.
    /// </summary>
    internal void FlushHyperlinkAnnotations()
    {
        if (_hyperlinkFragments is null || _hyperlinkFragments.Count == 0) return;
        foreach (var frag in _hyperlinkFragments)
        {
            var hyperlink = frag.HyperlinkValue;
            if (hyperlink is null || frag.Rectangle is null) continue;
            var rect = MapDisplayedRectToUnrotated(frag.Rectangle, RotateDegrees, MediaBox);
            EmitHyperlinkAnnotation(rect, hyperlink);
        }
        _hyperlinkFragments.Clear();
    }

    /// <summary>Map a rectangle from the page's displayed (rotation-applied) coordinate frame
    /// back to unrotated page space, where annotation /Rect values live.</summary>
    private static Rectangle MapDisplayedRectToUnrotated(Rectangle d, int rotate, Rectangle mb)
    {
        double wu = mb.Width, hu = mb.Height;
        (double x, double y) Map(double x, double y) => (((rotate % 360) + 360) % 360) switch
        {
            90 => (wu - y, x),
            180 => (wu - x, hu - y),
            270 => (y, hu - x),
            _ => (x, y),
        };
        var (x1, y1) = Map(d.LLX, d.LLY);
        var (x2, y2) = Map(d.URX, d.URY);
        return new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    internal void EmitHyperlinkAnnotation(Rectangle rect, Hyperlink hyperlink)
    {
        if (hyperlink is LocalHyperlink lh && lh.TargetPageNumber > 0)
            Annotations.AddLinkAnnotation(rect,
                new Aspose.Pdf.Annotations.GoToAction(
                    new Aspose.Pdf.Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
        else if (hyperlink is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
            Annotations.AddLinkAnnotation(rect, wh.Url);
        else if (hyperlink is FileHyperlink fh && !string.IsNullOrEmpty(fh.FileName))
            Annotations.AddLinkAnnotation(rect,
                new Aspose.Pdf.Annotations.LaunchAction(fh.FileName) { NewWindow = fh.NewWindow });
    }

    /// <summary>
    /// Inject 're'/'f' operators at the start of the content stream for every
    /// registered background-colour fragment. Called during save before the page
    /// content stream is flushed.
    /// </summary>
    internal void FlushBgColorRectangles()
    {
        if (_bgColorFragments is null || _bgColorFragments.Count == 0) return;
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
            if (fragBg is not null && frag.Segments.Count > 0)
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
                if (seg.TextState.BackgroundColor is not null && seg.Position is not null)
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
    /// Computes the background rectangle height for a text segment/fragment.
    /// Uses the system font WinLineHeight for Standard-14 equivalents (most accurate),
    /// falls back to CapHeight+|Descent| from the font descriptor, then BBox, then 1.16×fontSize.
    /// </summary>
    private static double ComputeBgRectHeight(string fontName, Text.FontInfo? font, double rawFs, double tmD)
    {
        // A text-highlight background box is sized at a flat 1.1×
        // the font size, independent of the font's own line-height metrics
        // (a 72pt run yields 79.2, a 12pt run 13.2, an 8pt run 8.8).
        _ = fontName; _ = font;
        return rawFs * tmD * 1.1;
    }

    /// <summary>
    /// Register a fragment whose TextState.Underline was set after extraction.
    /// Called from the Underline setter when the segment traces back to this page.
    /// </summary>
    internal void RegisterUnderlineFragment(Text.TextFragment fragment)
    {
        _underlineFragments ??= new();
        _underlineFragments.Add(fragment);
    }

    private HashSet<Text.TextFragment>? _underlineRemovalFragments;

    /// <summary>
    /// Register a fragment whose captured source underline should be removed from the
    /// content stream, because its TextState.Underline was toggled off after extraction
    /// (ToAttemptGetUnderlineFromSource). Called from the Underline setter.
    /// </summary>
    internal void RegisterUnderlineRemoval(Text.TextFragment fragment)
    {
        _underlineRemovalFragments ??= new();
        _underlineRemovalFragments.Add(fragment);
    }

    /// <summary>
    /// Splice out the source underline rectangles for every registered removal fragment by
    /// matching their captured raw <c>re</c> operands against the page content operators.
    /// Called during save, alongside <see cref="FlushUnderlineRectangles"/>.
    /// </summary>
    internal void FlushUnderlineRemovals()
    {
        if (_underlineRemovalFragments is null || _underlineRemovalFragments.Count == 0) return;
        var targets = new List<(double X, double Y, double W, double H)>();
        foreach (var frag in _underlineRemovalFragments)
        {
            if (frag.CapturedUnderlineSources is { } list) targets.AddRange(list);
            if (frag.CapturedBackgroundSources is { } bgList) targets.AddRange(bgList);
        }
        _underlineRemovalFragments.Clear();
        if (targets.Count == 0) return;

        var ops = Contents;
        var removed = ops.RemoveWhere(op =>
        {
            // Content operators materialize lazily as generic operators, so match by
            // command name + operands rather than only the typed classes.
            double x, y, w, h;
            if (op is Aspose.Pdf.Operators.Re re)
            {
                (x, y, w, h) = (re.X, re.Y, re.Width, re.Height);
            }
            else if (op.CommandName == "re")
            {
                var nums = ParseLeadingNumbers(op.ToString());
                if (nums is not { Length: >= 4 }) return false;
                (x, y, w, h) = (nums[0], nums[1], nums[2], nums[3]);
            }
            else if (op is Aspose.Pdf.Operators.MoveTo || op is Aspose.Pdf.Operators.LineTo
                || op.CommandName is "m" or "l")
            {
                // A stroked-line underline ("x1 y m x2 y l S") was captured with
                // raw X = left end, W = span, Y = the stroke's y. Splice both path
                // points whose coordinates hit either end of a captured target;
                // the leftover S strokes an empty path and paints nothing.
                double px, py;
                if (op is Aspose.Pdf.Operators.MoveTo mv) { px = mv.X; py = mv.Y; }
                else if (op is Aspose.Pdf.Operators.LineTo lt) { px = lt.X; py = lt.Y; }
                else
                {
                    var nums = ParseLeadingNumbers(op.ToString());
                    if (nums is not { Length: >= 2 }) return false;
                    (px, py) = (nums[0], nums[1]);
                }
                foreach (var t in targets)
                {
                    if (Math.Abs(py - t.Y) < 0.75 &&
                        (Math.Abs(px - t.X) < 0.75 || Math.Abs(px - (t.X + t.W)) < 0.75))
                        return true;
                }
                return false;
            }
            else return false;

            foreach (var t in targets)
            {
                if (Math.Abs(x - t.X) < 0.5 && Math.Abs(y - t.Y) < 0.5 &&
                    Math.Abs(w - t.W) < 0.5 && Math.Abs(h - t.H) < 0.5)
                    return true;
            }
            return false;
        });
        // Persist the edited operator list back to the page content stream.
        if (removed > 0) ops.FlushToPage();
    }

    /// <summary>Parse the leading whitespace-separated numeric operands from an operator's
    /// serialized form (e.g. "72 693 84 0.6 re" → [72, 693, 84, 0.6]).</summary>
    private static double[]? ParseLeadingNumbers(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                nums.Add(v);
            else break;
        }
        return nums.ToArray();
    }

    /// <summary>
    /// Emit thin filled rectangles below text for every registered underline fragment.
    /// Called during save after content stream operators are written.
    /// </summary>
    internal void FlushUnderlineRectangles()
    {
        if (_underlineFragments is null || _underlineFragments.Count == 0) return;
        var builder = new Content.ContentStreamBuilder();
        foreach (var frag in _underlineFragments)
        {
            var fragPos = frag.PositionOrNull;
            if (fragPos is null) continue;
            var fs = frag.TextState.FontSize;
            if (fs <= 0) fs = 12;

            // Width: prefer fragment's Rectangle width (computed during absorption),
            // fall back to MeasureString.
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

            // Underline geometry (verified on Arial and Calibri
            // sources): the line's top edge sits a tenth of the font's descent below
            // the fragment's rect bottom (Position.YIndent), and the thickness is 5%
            // of the font size — so the bottom offset is (0.05 + descent/10)·fs.
            // Fonts without a descent metric keep the historical constant, which is
            // this same formula evaluated for a typical 0.269 descent.
            double ulThick = fs * 0.05;
            double ulDescent = 0;
            var ulMetrics = frag.TextState.Font?.GetMetrics();
            if (ulMetrics is not null && ulMetrics.Descent != 0)
                ulDescent = Math.Abs(ulMetrics.Descent) / 1000.0;
            double ulOffset = ulDescent > 0 ? (0.05 + ulDescent / 10) * fs : fs * 0.07691;

            // A fragment whose SOURCE underline was captured (ToAttemptGetUnderlineFromSource,
            // then text-replaced): the new line is anchored to the spliced-out source
            // rectangle — top edge at the source's bottom edge — at the standard thickness
            // and the replacement's advance.
            if (frag.CapturedUnderlineSources is { Count: > 0 } ulSources)
            {
                var src = ulSources[0];
                var afg = frag.TextState.ForegroundColor;
                builder.SaveState();
                builder.SetFillColor(afg?.R / 255.0 ?? 0, afg?.G / 255.0 ?? 0, afg?.B / 255.0 ?? 0);
                builder.Rectangle(src.X, src.Y - ulThick, w, ulThick);
                builder.Fill();
                builder.RestoreState();
                continue;
            }

            // Rotation-aware path: for text drawn under a rotating CTM, emit the
            // underline along the baseline via a cm transform (a perpendicular page-Y
            // offset would leave the line floating off the rotated text). Horizontal
            // text (TextDirX=1, TextDirY=0) is unaffected and takes the path below.
            double ulDirX = frag.TextDirX, ulDirY = frag.TextDirY;
            double ulDirLen = Math.Sqrt(ulDirX * ulDirX + ulDirY * ulDirY);
            if (ulDirLen > 1e-6 && Math.Abs(ulDirY / ulDirLen) > 0.01)
            {
                double ux = ulDirX / ulDirLen, uy = ulDirY / ulDirLen;
                double rw;
                try { rw = frag.TextState.Font?.MeasureString(frag.Text, fs) ?? frag.Text.Length * fs * 0.5; }
                catch { rw = frag.Text.Length * fs * 0.5; }
                var fgr = frag.TextState.ForegroundColor;
                builder.SaveState();
                builder.SetFillColor(fgr?.R / 255.0 ?? 0, fgr?.G / 255.0 ?? 0, fgr?.B / 255.0 ?? 0);
                builder.SetMatrix(ux, uy, -uy, ux, fragPos.XIndent, fragPos.YIndent);
                builder.Rectangle(0, -ulOffset, rw, ulThick);
                builder.Fill();
                builder.RestoreState();
                continue;
            }

            // In page space (Y-up), underline is BELOW baseline = lower Y.
            // But with CTM Y-flip, the offset direction reverses.
            var ctm = frag.ExtractionCtm;
            bool yFlipped = ctm is not null && ctm.D < 0;
            var underlineY = yFlipped
                ? fragPos.YIndent + ulOffset
                : fragPos.YIndent - ulOffset;
            var underlineH = ulThick;

            // Transform from page space to content-stream space using the inverse CTM.
            // Emit raw 're' in content-stream coordinates (no cm prefix) so
            // IsRectanglePresent matches the bare coordinates.
            double rectX = fragPos.XIndent, rectY = underlineY;
            if (ctm is not null)
            {
                (rectX, rectY) = ctm.InverseTransformPoint(fragPos.XIndent, underlineY);
                var (wx, wy) = ctm.InverseTransformPoint(fragPos.XIndent + w, underlineY + underlineH);
                w = Math.Abs(wx - rectX);
                underlineH = Math.Abs(wy - rectY);
            }

            var fg = frag.TextState.ForegroundColor;
            double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b = fg?.B / 255.0 ?? 0;

            builder.SaveState();
            builder.SetFillColor(r, g, b);
            builder.Rectangle(rectX, rectY, w, underlineH);
            builder.Fill();
            builder.RestoreState();
        }
        var bytes = builder.Build();
        if (bytes.Length > 0)
            AddContentStream(bytes);
        _underlineFragments.Clear();
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

    /// <summary>1-based page number.</summary>
    public int Number => _index + 1;

    /// <summary>Form fields whose widgets appear on this page, ordered by their tab order.</summary>
    public IList<Forms.Field> FieldsInTabOrder
    {
        get
        {
            var pageNumber = Number;
            var fields = new List<Forms.Field>();
            var catalog = _reader.Catalog;
            var acroForm = _reader.ResolveDict(catalog.Get("AcroForm"));
            var formFields = acroForm is null
                ? Forms.Form.FromPageWidgets(catalog, _reader)
                : new Forms.Form(acroForm, _reader);
            foreach (var field in formFields.Fields)
            {
                if (field.PageIndex == pageNumber)
                    fields.Add(field);
            }
            fields.Sort((a, b) => a.TabOrder.CompareTo(b.TabOrder));
            return fields;
        }
    }

    /// <summary>
    /// Page rectangle (defaults to MediaBox). Forwarder for parity with the
    /// public API where Page exposes a top-level Rect property. Setting
    /// <c>Rect</c> updates MediaBox, CropBox, BleedBox, TrimBox and ArtBox in
    /// one shot — the property acts as the
    /// primary page-size accessor.
    /// </summary>
    public Rectangle Rect
    {
        get => MediaBox;
        set
        {
            MediaBox = value;
            CropBox = value;
            BleedBox = value;
            TrimBox = value;
            ArtBox = value;
        }
    }

    /// <summary>
    /// The page rectangle. Parity alias of <see cref="Rect"/> for the public API,
    /// which exposes the page size as both <c>Rect</c> and <c>Rectangle</c>.
    /// </summary>
    public Rectangle Rectangle
    {
        get => Rect;
        set => Rect = value;
    }

    private PageInfo? _pageInfoCache;

    /// <summary>
    /// Page info container with width/height/margin properties. Forwarder
    /// for parity with the public API.
    /// </summary>
    public PageInfo PageInfo
    {
        get => _pageInfoCache ??= new PageInfo(this);
        // Assigning a free-standing descriptor SIZES THE PAGE: the bound instance
        // stays bound and takes the assigned values, so `page.PageInfo = new PageInfo
        // { … }` and setting the same properties one by one end in the same place.
        // Swapping the cache for the detached object instead left the media box
        // untouched and every later `page.PageInfo.Width = …` talking to nothing.
        set
        {
            if (value is null) return;
            var bound = _pageInfoCache ??= new PageInfo(this);
            if (!ReferenceEquals(bound, value)) bound.CopyFrom(value);
        }
    }

    /// <summary>Page background colour. When not explicitly set, reading it DETECTS
    /// an existing background from the content stream — a full-page rectangle
    /// filled right at the start of the page paints the page background (so the
    /// getter reports Crimson etc. for such documents).</summary>
    public Color? Background
    {
        get
        {
            if (_background is not null) return _background;
            if (!_backgroundDetected)
            {
                _backgroundDetected = true;
                _detectedBackground = DetectBackgroundColor();
            }
            return _detectedBackground;
        }
        set => _background = value;
    }
    /// <summary>The background the caller explicitly assigned; detection never
    /// leaks into the generator's apply-background pass.</summary>
    internal Color? ExplicitBackground => _background;
    private Color? _background;
    private Color? _detectedBackground;
    private bool _backgroundDetected;

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

    /// <summary>Scan a raw (form XObject) content stream for a full-page fill.</summary>
    private static Color? ScanBytesForBackground(byte[] bytes, Rectangle mb)
    {
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        double r = 0, g = 0, b = 0;
        var colorSet = false;
        double reX = 0, reY = 0, reW = 0, reH = 0;
        var haveRect = false;
        var nums = new System.Collections.Generic.List<double>();
        foreach (var tokRaw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var tok = tokRaw;
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                nums.Add(v);
                continue;
            }
            switch (tok)
            {
                case "rg" when nums.Count >= 3:
                    r = nums[^3]; g = nums[^2]; b = nums[^1]; colorSet = true; break;
                case "g" when nums.Count >= 1:
                    r = g = b = nums[^1]; colorSet = true; break;
                case "re" when nums.Count >= 4:
                    reX = nums[^4]; reY = nums[^3]; reW = nums[^2]; reH = nums[^1];
                    haveRect = true; break;
                case "f" or "f*" or "B" or "b":
                    if (colorSet && haveRect && reW >= mb.Width * 0.9 && reH >= mb.Height * 0.9)
                        return Color.FromRgb(r, g, b);
                    haveRect = false;
                    break;
                case "BT":
                    return null;
            }
            nums.Clear();
        }
        return null;
    }

    /// <summary>The media box for this page (required per spec).</summary>
    public Rectangle MediaBox
    {
        get => GetBox("MediaBox") ?? new Rectangle(0, 0, 612, 792);
        set => SetBox("MediaBox", value);
    }

    /// <summary>The crop box (defaults to media box).</summary>
    public Rectangle CropBox
    {
        get => GetBox("CropBox") ?? MediaBox;
        set => SetBox("CropBox", value);
    }

    /// <summary>The bleed box (defaults to crop box).</summary>
    public Rectangle BleedBox
    {
        get => GetBox("BleedBox") ?? CropBox;
        set => SetBox("BleedBox", value);
    }

    /// <summary>The trim box (defaults to crop box).</summary>
    public Rectangle TrimBox
    {
        get => GetBox("TrimBox") ?? CropBox;
        set => SetBox("TrimBox", value);
    }

    /// <summary>The art box (defaults to crop box).</summary>
    public Rectangle ArtBox
    {
        get => GetBox("ArtBox") ?? CropBox;
        set => SetBox("ArtBox", value);
    }

    /// <summary>Page rotation as Rotation enum.</summary>
    public Rotation Rotate
    {
        get => (Rotation)(int)_dict.GetInt("Rotate");
        set => _dict.Set("Rotate", new PdfInteger((int)value % 360));
    }

    /// <summary>Page rotation in degrees as int (0, 90, 180, 270).</summary>
    public int RotateDegrees
    {
        get => (int)_dict.GetInt("Rotate");
        set => _dict.Set("Rotate", new PdfInteger(value % 360));
    }

    /// <summary>
    /// Set the page rotation in degrees (0, 90, 180, 270).
    /// </summary>
    public void SetRotation(int degrees) => RotateDegrees = degrees;

    /// <summary>
    /// Affine transform that maps the unrotated PDF coordinate system to
    /// the page's user-visible (rotated) one. Composes a rotation about the
    /// MediaBox centre with the translation needed to keep the bottom-left
    /// of the rotated bounds at (0, 0). Identity when rotation is 0.
    /// </summary>
    public Matrix RotationMatrix
    {
        get
        {
            var rotation = ((int)Rotate) % 360;
            if (rotation < 0) rotation += 360;
            var rect = MediaBox;
            var w = rect?.Width ?? 0;
            var h = rect?.Height ?? 0;
            return rotation switch
            {
                90 => new Matrix(0, -1, 1, 0, 0, w),
                180 => new Matrix(-1, 0, 0, -1, w, h),
                270 => new Matrix(0, 1, -1, 0, h, 0),
                _ => Matrix.Identity,
            };
        }
    }

    /// <summary>
    /// Page display duration in seconds for presentation mode.
    /// Returns -1 if no duration is set.
    /// </summary>
    public double Duration
    {
        get
        {
            var val = _reader.Resolve(_dict.Get("Dur"));
            if (val is PdfReal r) return r.Value;
            if (val is PdfInteger i) return i.Value;
            return -1;
        }
        set
        {
            _dict.Set("Dur", new PdfReal(value));
        }
    }

    /// <summary>Set the media box for this page.</summary>
    public void SetMediaBox(Rectangle rect) => SetBox("MediaBox", rect);

    /// <summary>Set the crop box for this page.</summary>
    public void SetCropBox(Rectangle rect) => SetBox("CropBox", rect);

    /// <summary>Set the bleed box for this page.</summary>
    public void SetBleedBox(Rectangle rect) => SetBox("BleedBox", rect);

    /// <summary>Set the trim box for this page.</summary>
    public void SetTrimBox(Rectangle rect) => SetBox("TrimBox", rect);

    /// <summary>Set the art box for this page.</summary>
    public void SetArtBox(Rectangle rect) => SetBox("ArtBox", rect);

    private void SetBox(string name, Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        _dict.Set(name, arr);
    }

    /// <summary>Walk every annotation on this page through
    /// <paramref name="visitor"/>; matches accumulate in
    /// <see cref="Annotations.AnnotationSelector.Selected"/>.</summary>
    public void Accept(Annotations.AnnotationSelector visitor)
    {
        if (visitor is null) return;
        Annotations.Accept(visitor);
    }

    /// <summary>Annotations on this page.</summary>
    public AnnotationCollection Annotations
    {
        get
        {
            // Re-create if the Annots array was modified externally (e.g., by Annotation.Flatten)
            if (_annotations is not null && _annotations.IsDirty(_dict, _reader))
                _annotations = null;
            return _annotations ??= new AnnotationCollection(_dict, _reader, this);
        }
    }

    /// <summary>Image XObjects on this page.</summary>
    public XImageCollection Images =>
        _images ??= new XImageCollection(_dict, _reader) { OwnerPage = this };

    /// <summary>Fonts referenced by this page.</summary>
    public FontCollection Fonts =>
        _fonts ??= new FontCollection(_dict, _reader);

    /// <summary>
    /// Page resources (fonts, images) — provides access via a unified Resources object.
    /// </summary>
    public Resources Resources => _resources ??= new Resources(this);

    /// <summary>Method-style accessor for <see cref="Resources"/> — public-API parity.</summary>
    public Resources GetResources() => Resources;

    /// <summary>
    /// Pattern resources on this page (keyed by pattern name).
    /// </summary>
    public IReadOnlyDictionary<string, Pattern> Patterns
        => _patterns ??= Pattern.ResolvePatterns(_dict, _reader);
    private Dictionary<string, Pattern>? _patterns;

    /// <summary>
    /// Content stream operator collection. Add operators to build page content.
    /// Operators are serialized to the content stream on save.
    /// </summary>
    public OperatorCollection Contents => _contents ??= new OperatorCollection(this);
    private OperatorCollection? _contents;

    /// <summary>Helper that buffers operators to prepend to / append to this page's
    /// content stream, applied on <see cref="ContentsAppender.UpdateData"/>. Commonly
    /// used to wrap existing content in a q…Q save/restore pair.</summary>
    public ContentsAppender ContentsAppender => _contentsAppender ??= new ContentsAppender(this);
    private ContentsAppender? _contentsAppender;

    /// <summary>
    /// Visible page width accounting for rotation.
    /// </summary>
    public double Width
    {
        get
        {
            var mb = MediaBox;
            var rot = RotateDegrees % 360;
            return (rot == 90 || rot == 270) ? mb.Height : mb.Width;
        }
    }

    /// <summary>
    /// Visible page height accounting for rotation.
    /// </summary>
    public double Height
    {
        get
        {
            var mb = MediaBox;
            var rot = RotateDegrees % 360;
            return (rot == 90 || rot == 270) ? mb.Width : mb.Height;
        }
    }

    /// <summary>
    /// Set the page size by updating the MediaBox.
    /// Width and height are in points.
    /// </summary>
    public void SetPageSize(double width, double height)
    {
        var rot = RotateDegrees % 360;
        double boxW, boxH;
        if (rot == 90 || rot == 270)
        {
            boxW = height;
            boxH = width;
        }
        else
        {
            boxW = width;
            boxH = height;
        }

        var arr = new Core.PdfArray();
        arr.Add(new Core.PdfReal(0));
        arr.Add(new Core.PdfReal(0));
        arr.Add(new Core.PdfReal(boxW));
        arr.Add(new Core.PdfReal(boxH));
        _dict.Set("MediaBox", arr);

        // Update CropBox if it exists so it matches
        if (_dict.ContainsKey("CropBox"))
        {
            var cropArr = new Core.PdfArray();
            cropArr.Add(new Core.PdfReal(0));
            cropArr.Add(new Core.PdfReal(0));
            cropArr.Add(new Core.PdfReal(boxW));
            cropArr.Add(new Core.PdfReal(boxH));
            _dict.Set("CropBox", cropArr);
        }
    }

    /// <summary>
    /// Get the page rectangle, optionally considering the CropBox.
    /// </summary>
    /// <param name="considerRotation">Whether to account for page rotation.</param>
    /// <returns>The effective page rectangle.</returns>
    public Rectangle GetPageRect(bool considerRotation)
    {
        var box = _dict.ContainsKey("CropBox") ? CropBox : MediaBox;
        if (!considerRotation)
            return box;

        var rot = RotateDegrees % 360;
        if (rot == 90 || rot == 270)
            return new Rectangle(box.LLX, box.LLY, box.LLX + box.Height, box.LLY + box.Width);
        return box;
    }

    /// <summary>
    /// Gets the color type of this page by analyzing its content stream operators
    /// and image color spaces.
    /// </summary>
    public ColorType ColorType => ColorDetectHelper.GetColorType(this);

    /// <summary>
    /// The page transition effect, or null if none is set.
    /// </summary>
    public PageTransition? Transition => PageTransition.FromPageDict(_dict, _reader);

    /// <summary>
    /// Accept a TextAbsorber visitor (matching the public API).
    /// </summary>
    public void Accept(Text.TextAbsorber visitor) => visitor.Visit(this);

    /// <summary>
    /// Extract all text from the page using a TextAbsorber.
    /// </summary>
    public string GetText()
    {
        var absorber = new Text.TextAbsorber();
        Accept(absorber);
        return absorber.Text;
    }

    /// <summary>
    /// Accept a TextFragmentAbsorber visitor (matching the public API).
    /// </summary>
    public void Accept(Text.TextFragmentAbsorber visitor) => visitor.Visit(this);

    /// <summary>
    /// Tally how many text fragments on this page are drawn at each rotation
    /// angle. The key is the fragment rotation in degrees (CCW from the page
    /// x-axis, 0/90/180/270 for axis-aligned text) and the value is the number
    /// of fragments at that angle.
    /// </summary>
    public System.Collections.Generic.Dictionary<double, int> GetTextRotationStatistic()
    {
        var absorber = new Text.TextFragmentAbsorber();
        absorber.Visit(this);
        var stats = new System.Collections.Generic.Dictionary<double, int>();
        foreach (Text.TextFragment fragment in absorber.TextFragments)
        {
            var rotation = fragment.TextState.Rotation;
            stats[rotation] = stats.TryGetValue(rotation, out var count) ? count + 1 : 1;
        }
        return stats;
    }

    /// <summary>
    /// Accept an ImagePlacementAbsorber visitor (matching the public API).
    /// </summary>
    public void Accept(ImagePlacementAbsorber visitor) => visitor.Visit(this);

    /// <summary>
    /// Add a stamp to this page. The stamp's content stream is appended to the page's content.
    /// </summary>
    /// <summary>
    /// Render this page using the specified device and save to a file.
    /// </summary>
    public void SendTo(Devices.ImageDevice device, string outputFileName)
    {
        device.Process(this, outputFileName);
    }

    /// <summary>
    /// Render this page using the specified device and write to a stream.
    /// </summary>
    public void SendTo(Devices.ImageDevice device, Stream output)
    {
        device.Process(this, output);
    }

    /// <summary>Apply an image stamp to this page. Delegates to
    /// <see cref="ImageStamp.ApplyTo(Page)"/>.</summary>
    public void AddStamp(ImageStamp stamp)
    {
        if (stamp is null) throw new ArgumentNullException(nameof(stamp));
        stamp.ApplyTo(this);
    }

    /// <summary>Apply a page stamp to this page. Delegates to
    /// <see cref="PdfPageStamp.ApplyTo(Page)"/> rather than the generic
    /// <see cref="AddStamp(Aspose.Pdf.Stamps.Stamp)"/> path: a PdfPageStamp registers
    /// its source-page Form XObject in this page's /Resources/XObject and emits a
    /// `… /Fm0 Do …` draw call, but the generic path re-wraps that draw in an inner
    /// Form XObject whose resources deliberately omit /XObject, leaving /Fm0 unresolved
    /// so the stamped content disappears. ApplyTo writes the draw call
    /// straight to the page content where /Fm0 is in scope.</summary>
    public void AddStamp(PdfPageStamp stamp)
    {
        if (stamp is null) throw new ArgumentNullException(nameof(stamp));
        stamp.ApplyTo(this);
    }

    public void AddStamp(Aspose.Pdf.Stamps.Stamp stamp)
    {
        // Register Helvetica in the page resources and pass the resolved
        // resource name into the stamp. If the page already uses "F1" for an
        // embedded subset, RegisterFont returns "F2"/"F3"/etc so the stamp's
        // SetFont op binds to Helvetica rather than the existing subset.
        var fontName = Table.RegisterFont(this);
        var stampBytes = stamp.BuildContentStream(this, fontName);

        // Wrap the stamp content in a Form XObject and reference it from the page
        // content with a Do operator. Emitting the stamp as a form (rather than
        // inline content) keeps the page content stream a simple reference and
        // surfaces the stamp under the page's /Resources/XObject (page.Resources.Forms).
        var formName = AddStampForm(stampBytes, stampId: stamp.StampId);
        // Embed a %StampId comment ahead of the Do reference when the stamp carries an
        // id, so PdfContentEditor.GetStamps / DeleteStampById can identify it on reload.
        var idComment = stamp.StampId != 0 ? $"%StampId={stamp.StampId}\n" : "";
        // Embed a %StampRect comment with the stamp's pre-computed page-space bounds
        // (e.g. header/footer bands), so GetStamps reports the exact geometry on reload.
        var rectComment = stamp.MetaRect is { } mr ? $"%StampRect={Format(mr.LLX)} " +
            $"{Format(mr.LLY)} {Format(mr.URX)} {Format(mr.URY)}\n" : "";
        var refBytes = System.Text.Encoding.ASCII.GetBytes($"{idComment}{rectComment}q /{formName} Do Q\n");

        // Add the stamp reference as its own content stream rather than rewriting
        // /Contents. AddContentStream / PrependContentStream preserve an existing
        // multi-stream /Contents array (common for imported pages); the previous
        // stream-only logic fell through to "no existing content" for an array and
        // erased the page's own content: an imported page whose
        // /Contents was an 8-stream array was reduced to just the stamp reference.
        if (stamp.IsBackground)
            PrependContentStream(refBytes);
        else
            AddContentStream(refBytes);
    }

    /// <summary>Wrap a stamp's content bytes in a Form XObject, register it under a
    /// fresh /FmN name in this page's /Resources/XObject, and return that name. The
    /// form shares the page's font / graphics-state resources so its content resolves
    /// the same resource names it referenced when built.</summary>
    internal string AddStampForm(byte[] content, Rectangle? bboxRect = null, int stampId = 0)
    {
        // Resolve an indirect /Resources in place; a bare cast would miss a
        // PdfReference and replace the real dictionary with an empty one,
        // dropping the page's fonts and content references.
        var resources = _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        // Form-local resources: share the page's font / graphics-state / pattern /
        // colour-space / shading entries. /XObject is deliberately excluded so the
        // form can't end up referencing itself.
        var formResources = new PdfDictionary();
        foreach (var key in new[] { "Font", "ExtGState", "Pattern", "ColorSpace", "Shading" })
        {
            var entry = resources.Get(key);
            if (entry is not null) formResources.Set(key, entry);
        }

        var box = bboxRect ?? MediaBox;
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(box.LLX));
        bbox.Add(new PdfReal(box.LLY));
        bbox.Add(new PdfReal(box.URX));
        bbox.Add(new PdfReal(box.URY));

        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        formDict.Set("FormType", new PdfInteger(1));
        formDict.Set("BBox", bbox);
        formDict.Set("Resources", formResources);
        formDict.Set("StampId", new PdfInteger(stampId));
        var formStream = new PdfStream(formDict, content);

        // Register the form as an indirect object (not inline in /XObject): a full save
        // promotes inline streams, but an incremental (append-only) save writes only the
        // objects registered as new — so a stamp added to a document opened from a
        // writable stream would otherwise vanish on Save().
        var doc = _reader.OwnerDocument;
        PdfObject formEntry = formStream;
        if (doc is not null && doc.HasWritableSourceStream)
        {
            var fnum = doc.AllocateObjectNumber();
            doc.AddNewObject(fnum, formStream, registerOverlay: true);
            formEntry = new PdfIndirectRef(fnum, 0);
        }

        var xobjects = _reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }

        // Stamp form XObjects are numbered from Fm0,
        // so the first stamp added to a page is /Fm0.
        var name = "Fm0";
        var counter = 0;
        while (xobjects.ContainsKey(name)) name = $"Fm{++counter}";
        xobjects.Set(name, formEntry);
        return name;
    }

    /// <summary>
    /// Append a content stream to this page.
    /// If the page has a single content stream, it is converted to an array.
    /// If the page already has a content array, the new stream is appended.
    /// </summary>
    public void AddContentStream(byte[] contentBytes)
    {
        AddContent(contentBytes);
        _trailingMarkedContent = null;
    }

    /// <summary>The trailing <c>BDC … EMC</c> block left at the end of the page
    /// content by <see cref="AddMarkedContentStream"/>: its tag/MCID and the
    /// stream segment holding it. Any other append clears the note, so a merge
    /// only happens across DIRECTLY consecutive same-tag/-MCID runs.</summary>
    private (string Tag, int Mcid, Core.PdfStream Segment)? _trailingMarkedContent;

    /// <summary>Append content wrapped in a <c>/tag &lt;&lt;/MCID mcid&gt;&gt; BDC … EMC</c>
    /// marked-content block. A run appended directly after another with the SAME
    /// tag and MCID continues that block instead of opening a second one (the
    /// previous segment's closing EMC moves to the end of the new segment).</summary>
    internal void AddMarkedContentStream(byte[] contentBytes, string tag, int mcid)
    {
        var close = System.Text.Encoding.ASCII.GetBytes("EMC\n");
        byte[] payload;
        if (_trailingMarkedContent is { } prev && prev.Tag == tag && prev.Mcid == mcid
            && ReferenceEquals(LastContentStreamSegment(), prev.Segment)
            && EndsWith(prev.Segment.RawData, close))
        {
            prev.Segment.ReplaceData(prev.Segment.RawData[..^close.Length]);
            payload = Concat(contentBytes, close);
        }
        else
        {
            var open = System.Text.Encoding.ASCII.GetBytes($"/{tag} <</MCID {mcid}>> BDC\n");
            payload = Concat(Concat(open, contentBytes), close);
        }
        AddContent(payload);
        _trailingMarkedContent = LastContentStreamSegment() is { } seg ? (tag, mcid, seg) : null;

        static bool EndsWith(byte[] data, byte[] suffix)
        {
            if (data.Length < suffix.Length) return false;
            for (var i = 0; i < suffix.Length; i++)
                if (data[data.Length - suffix.Length + i] != suffix[i]) return false;
            return true;
        }
        static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            System.Buffer.BlockCopy(a, 0, r, 0, a.Length);
            System.Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }

    /// <summary>The last stream segment of /Contents (the one the most recent
    /// append landed in), or null when the page has no direct stream content.</summary>
    private Core.PdfStream? LastContentStreamSegment()
    {
        var resolved = _reader.Resolve(_dict.Get("Contents"));
        if (resolved is Core.PdfArray arr)
            return arr.Count > 0 ? _reader.Resolve(arr[arr.Count - 1]) as Core.PdfStream : null;
        return resolved as Core.PdfStream;
    }

    /// <summary>
    /// Prepend content stream bytes before existing page content (for background elements).
    /// </summary>
    public void PrependContentStream(byte[] contentBytes)
    {
        var newStream = new PdfStream(new PdfDictionary(), contentBytes);
        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);

        if (resolved is PdfArray arr)
        {
            arr.Insert(0, newStream);
        }
        else if (resolved is PdfStream existingStream)
        {
            var newArr = new PdfArray();
            newArr.Add(newStream);
            newArr.Add(existingStream);
            _dict.Set("Contents", newArr);
        }
        else
        {
            _dict.Set("Contents", newStream);
        }
    }

    /// <summary>Marked-content tag wrapping a page-background fill emitted by
    /// <see cref="Background"/>. Lets a re-applied background find and remove the
    /// previous one instead of stacking, and lets Color.White act as "remove".</summary>
    internal const string BackgroundMarkerTag = "Background";

    /// <summary>Remove any previously-emitted tagged page-background block from
    /// the content stream(s) so a re-applied background replaces rather than
    /// stacks. Returns true when a block was removed.</summary>
    internal bool RemoveTaggedBackground()
    {
        if (_reader is null) return false;
        var marker = "/" + BackgroundMarkerTag + " BMC";
        var contentsObj = _reader.Resolve(_dict.Get("Contents"));

        if (contentsObj is PdfArray arr)
        {
            var removed = false;
            for (var i = arr.Count - 1; i >= 0; i--)
            {
                var s = _reader.ResolveStream(arr[i]);
                if (s is null) continue;
                var txt = Encoding.ASCII.GetString(_reader.DecodeStream(s));
                if (txt.Contains(marker))
                {
                    arr.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        if (contentsObj is PdfStream stream)
        {
            var txt = Encoding.ASCII.GetString(_reader.DecodeStream(stream));
            var start = txt.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;
            var emc = txt.IndexOf("EMC", start, StringComparison.Ordinal);
            if (emc < 0) return false;
            var end = emc + 3;
            while (end < txt.Length && (txt[end] == '\n' || txt[end] == '\r')) end++;
            SetContentStream(Encoding.ASCII.GetBytes(txt.Remove(start, end - start)));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Append content stream bytes to this page.
    /// If the page has a single content stream, creates an array with both.
    /// If the page has a content array, appends the new stream.
    /// </summary>
    public void AddContent(byte[] contentStreamBytes)
    {
        var doc = _reader.OwnerDocument;
        // Register the new content as an indirect object (not inline in /Contents): a full
        // save promotes inline streams, but an incremental (append-only) save writes only
        // objects registered as new/dirty, so the stream needs its own number to survive
        // Save() on a document opened from a writable stream. registerOverlay exposes it to
        // in-memory _reader.Resolve so reading the page's operators before save still works.
        var newStream = new PdfStream(new PdfDictionary(), contentStreamBytes);
        PdfObject entry = newStream;
        // Only take the indirect path for a document that will be saved incrementally
        // (opened from a writable stream); a full save to a fresh output promotes the inline
        // stream and keeps the compact layout that structural comparisons expect.
        var indirect = doc is not null && doc.HasWritableSourceStream;
        if (indirect)
        {
            var num = doc!.AllocateObjectNumber();
            doc.AddNewObject(num, newStream, registerOverlay: true);
            entry = new PdfIndirectRef(num, 0);
        }

        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);

        if (resolved is PdfArray existingArray)
        {
            existingArray.Add(entry);
            if (indirect && existing is PdfIndirectRef aref)
                doc!.MarkDirty(aref.ObjectNumber, existingArray);
        }
        else if (resolved is PdfStream)
        {
            // Single stream — create an array with both
            var arr = new PdfArray();
            arr.Add(existing!); // keep original ref (may be indirect)
            arr.Add(entry);
            _dict.Set("Contents", arr);
        }
        else
        {
            // No existing content — just set the new stream
            _dict.Set("Contents", entry);
        }

        if (indirect) MarkDirty();
    }

    /// <summary>Number of streams currently making up /Contents (0, 1, or the
    /// array length) — the insertion cursor for <see cref="InsertContentStreamAt"/>.</summary>
    internal int ContentStreamCount
    {
        get
        {
            var resolved = _reader.Resolve(_dict.Get("Contents"));
            return resolved is PdfArray arr ? arr.Count : resolved is PdfStream ? 1 : 0;
        }
    }

    /// <summary>Insert a content stream at <paramref name="index"/> in /Contents —
    /// an UNDERLAY that paints beneath every stream appended after that point
    /// (a table wrapper's background band whose height is only known once its
    /// children have laid out).</summary>
    internal void InsertContentStreamAt(int index, byte[] contentStreamBytes)
    {
        var newStream = new PdfStream(new PdfDictionary(), contentStreamBytes);
        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);
        if (resolved is PdfArray existingArray)
        {
            existingArray.Insert(Math.Clamp(index, 0, existingArray.Count), newStream);
        }
        else if (resolved is PdfStream)
        {
            var arr = new PdfArray();
            if (index <= 0) { arr.Add(newStream); arr.Add(existing!); }
            else { arr.Add(existing!); arr.Add(newStream); }
            _dict.Set("Contents", arr);
        }
        else
        {
            _dict.Set("Contents", newStream);
        }
        _trailingMarkedContent = null;
    }

    /// <summary>Register this page — and any indirect /Resources (and /Resources/XObject)
    /// it owns — as dirty so an incremental (append-only) save re-writes the in-memory
    /// edits. A foreground stamp adds a /Contents stream and an /XObject entry to an
    /// already-existing page; only NEW objects are appended automatically, so the modified
    /// existing objects must be marked explicitly. No-op for a page not loaded from a document.</summary>
    internal void MarkDirty()
    {
        var doc = _reader.OwnerDocument;
        if (doc is null) return;
        var pageNum = doc.FindObjectNumber(_dict);
        if (pageNum > 0) doc.MarkDirty(pageNum, _dict);
        if (_dict.Get("Resources") is PdfIndirectRef rr && _reader.ResolveDict(rr) is { } rdict)
        {
            doc.MarkDirty(rr.ObjectNumber, rdict);
            if (rdict.Get("XObject") is PdfIndirectRef xr && _reader.ResolveDict(xr) is { } xdict)
                doc.MarkDirty(xr.ObjectNumber, xdict);
        }
    }

    /// <summary>
    /// Replace the page content stream with new bytes.
    /// </summary>
    /// <summary>
    /// Returns the decoded content stream bytes for this page.
    /// If the page has multiple content streams (Contents is an array), they are concatenated.
    /// </summary>
    internal byte[]? GetContentStreamBytes()
    {
        if (_reader is null) return null;
        var contents = _reader.Resolve(_dict.Get("Contents"));
        if (contents is PdfStream stream)
            return _reader.DecodeStream(stream);
        if (contents is Core.PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = _reader.ResolveStream(item);
                if (s is null) continue;
                if (ms.Length > 0) ms.WriteByte((byte)'\n');
                var data = _reader.DecodeStream(s);
                ms.Write(data);
            }
            return ms.ToArray();
        }
        // Direct PdfStream on the dict (not via reader)
        if (_dict.Get("Contents") is PdfStream directStream)
            return directStream.RawData;
        return null;
    }

    internal void SetContentStream(byte[] contentBytes)
    {
        _dict.Set("Contents", new PdfStream(new PdfDictionary(), contentBytes));
        _contents?.InvalidateCache();
    }

    /// <summary>Drop the cached typed-operator view of the page content so the
    /// next <see cref="Contents"/> access re-materialises from the current raw
    /// /Contents. Needed after low-level raw edits (SetContentStream /
    /// AddContentStream) when a caller has already materialised the
    /// OperatorCollection: <see cref="SetContentStream"/> only clears the parsed
    /// string cache, so a previously materialised typed-operator list would
    /// otherwise survive stale and win on save.</summary>
    internal void ResetContentsCache() => _contents = null;

    internal void AppendContentBytes(byte[] newBytes)
    {
        // /Contents may be a single stream or an array of streams (PDF 32000-2
        // § 7.7.3.3). GetContentStreamBytes handles both; the previous inline
        // branch silently lost array-content callers' original page data.
        var existingData = GetContentStreamBytes() ?? [];

        var combined = new byte[existingData.Length + 1 + newBytes.Length];
        existingData.CopyTo(combined, 0);
        if (existingData.Length > 0) combined[existingData.Length] = (byte)'\n';
        newBytes.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));
        _dict.Set("Contents", new PdfStream(new PdfDictionary(), combined));
    }

    /// <summary>
    /// Add a table to this page. The table renders itself to a content stream
    /// and registers required font resources.
    /// </summary>
    public void AddTable(Table table)
    {
        var contentBytes = table.Build(this);
        AddContentStream(contentBytes);
    }

    /// <summary>
    /// Add a graph (collection of shapes) to this page.
    /// ExtGState resources for opacity/blend mode are registered automatically.
    /// </summary>
    public void AddGraph(Drawing.Graph graph)
    {
        var contentBytes = graph.Build(this);
        AddContentStream(contentBytes);
    }

    /// <summary>
    /// Add a floating box to this page.
    /// The box is rendered to a content stream and appended to the page content.
    /// </summary>
    public void AddFloatingBox(FloatingBox box)
    {
        var contentBytes = box.Build(this);
        AddContentStream(contentBytes);
    }

    /// <summary>
    /// Add an ExtGState dictionary to this page's resources and return the resource name.
    /// </summary>
    public string AddExtGState(Content.ExtGState extGState)
    {
        // Resolve indirect /Resources and /ExtGState references rather than a
        // bare `as PdfDictionary` cast (which yields null for an indirect ref
        // and would replace the real dictionary, dropping the page's fonts and
        // other resources, with a fresh empty one).
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        var gsDict = resources.Get("ExtGState") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null)
        {
            gsDict = new PdfDictionary();
            resources.Set("ExtGState", gsDict);
        }

        // Find a unique name
        var name = "GS0";
        var counter = 0;
        while (gsDict.ContainsKey(name))
            name = $"GS{++counter}";

        gsDict.Set(name, extGState.ToPdfDictionary());
        return name;
    }

    /// <summary>Register an ExtGState under a lowercase sequential resource name
    /// (<c>gs1, gs2, …</c>) — the naming used for per-paint transparency states
    /// on drawable shapes, distinct from the uppercase <c>GS<i>n</i></c> series.</summary>
    internal string AddExtGStateSequential(Content.ExtGState extGState)
    {
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }
        var gsDict = resources.Get("ExtGState") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null)
        {
            gsDict = new PdfDictionary();
            resources.Set("ExtGState", gsDict);
        }
        var counter = 1;
        var name = "gs1";
        while (gsDict.ContainsKey(name))
            name = $"gs{++counter}";
        gsDict.Set(name, extGState.ToPdfDictionary());
        return name;
    }

    /// <summary>
    /// Add a shading dictionary to this page's /Resources/Shading and return the
    /// resource name (usable with the <c>sh</c> operator).
    /// </summary>
    internal string AddShading(PdfDictionary shadingDict)
    {
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        var shDict = resources.Get("Shading") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("Shading"));
        if (shDict is null)
        {
            shDict = new PdfDictionary();
            resources.Set("Shading", shDict);
        }

        var name = "Sh0";
        var counter = 0;
        while (shDict.ContainsKey(name))
            name = $"Sh{++counter}";

        shDict.Set(name, shadingDict);
        return name;
    }

    /// <summary>
    /// Add a pattern dictionary to this page's /Resources/Pattern and return the
    /// resource name (usable with <c>/Pattern cs /Name scn</c>).
    /// </summary>
    internal string AddPattern(PdfDictionary patternDict)
    {
        var resources = _dict.Get("Resources") as PdfDictionary
            ?? _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        var patDict = resources.Get("Pattern") as PdfDictionary
            ?? _reader.ResolveDict(resources.Get("Pattern"));
        if (patDict is null)
        {
            patDict = new PdfDictionary();
            resources.Set("Pattern", patDict);
        }

        var name = "P0";
        var counter = 0;
        while (patDict.ContainsKey(name))
            name = $"P{++counter}";

        patDict.Set(name, patternDict);
        return name;
    }

    /// <summary>
    /// Add an image to this page at the specified position and size.
    /// Supports JPEG and raw RGB pixel data.
    /// </summary>
    /// <param name="imageData">Image bytes (JPEG format or raw RGB pixels).</param>
    /// <param name="rect">Position and size rectangle (LLX, LLY, URX, URY).</param>
    /// <param name="blackWhite">When true, embed the image as a 1-bit black/white
    /// XObject (<see cref="Image.IsBlackWhite"/>) for a much smaller stream; falls
    /// back to the normal colour embed when the source can't be decoded.</param>
    public void AddImage(byte[] imageData, Rectangle rect, bool blackWhite = false)
        => AddImage(imageData, rect, blackWhite, aspectFit: false);

    // aspectFit: the file-path overload treats the rectangle as a bounding box (the
    // image keeps its own aspect ratio, centred); every other caller — the generator
    // flow, the HTML converter, direct byte[]/stream users — fills the rectangle
    // exactly as given.
    private void AddImage(byte[] imageData, Rectangle rect, bool blackWhite, bool aspectFit)
    {
        if (blackWhite && ImageStamp.FromBlackWhite(imageData) is { } bwStamp)
        {
            bwStamp.X = rect.LLX;
            bwStamp.Y = rect.LLY;
            bwStamp.DisplayWidth = rect.Width;
            bwStamp.DisplayHeight = rect.Height;
            bwStamp.CompensatePageRotation = true;
            bwStamp.ApplyTo(this);
            return;
        }

        // Detect JPEG by FFD8 header
        var isJpeg = imageData.Length >= 2 && imageData[0] == 0xFF && imageData[1] == 0xD8;
        // Detect PNG by 89504E47 header
        var isPng = imageData.Length >= 4 && imageData[0] == 0x89 && imageData[1] == 0x50
                    && imageData[2] == 0x4E && imageData[3] == 0x47;
        // Detect BMP by 'BM' header
        var isBmp = imageData.Length >= 2 && imageData[0] == 0x42 && imageData[1] == 0x4D;
        // Detect JPEG 2000: a JP2/JPX box wrapper (signature box 00000000 0C 6A502020)
        // or a raw codestream (SOC marker FF4F immediately followed by SIZ FF51).
        var isJpx = (imageData.Length >= 12 && imageData[0] == 0x00 && imageData[1] == 0x00
                     && imageData[2] == 0x00 && imageData[3] == 0x0C && imageData[4] == 0x6A
                     && imageData[5] == 0x50 && imageData[6] == 0x20 && imageData[7] == 0x20)
                    || (imageData.Length >= 4 && imageData[0] == 0xFF && imageData[1] == 0x4F
                        && imageData[2] == 0xFF && imageData[3] == 0x51);

        ImageStamp stamp;
        if (isJpeg)
        {
            stamp = ImageStamp.FromJpegStream(new MemoryStream(imageData));
        }
        else if (isPng)
        {
            // Embed PNG as a FlateDecode image with SMask for alpha
            stamp = ImageStamp.FromPngData(imageData);
        }
        else if (isBmp)
        {
            stamp = ImageStampFromBmp(imageData);
        }
        else if (isJpx
                 && Aspose.Pdf.IO.Filters.JpxDecoder.TryDecode(imageData, out var jxPx, out var jxW, out var jxH, out var jxC)
                 && (jxC == 1 || jxC == 3))
        {
            // JPEG 2000 (.jp2/.jpx): GDI+/System.Drawing can't decode it, so decode to raw
            // samples with the built-in JPXDecode decoder and embed as a Flate RGB/Gray image.
            stamp = jxC == 3 ? ImageStamp.FromRgb(jxPx, jxW, jxH) : ImageStamp.FromGrayscale(jxPx, jxW, jxH);
        }
        else
        {
            // Assume raw RGB pixel data — caller must ensure width/height are correct
            var w = (int)rect.Width;
            var h = (int)rect.Height;
            if (imageData.Length == w * h * 3)
            {
                stamp = ImageStamp.FromRgb(imageData, w, h);
            }
            else if (OperatingSystem.IsWindows()
                     && ImageStamp.TryFromGdiPlusDecoder(imageData) is { } gdiStamp)
            {
                // GIF / TIFF / EMF / WMF / ICO and other GDI+-supported formats:
                // decode to raw RGB via System.Drawing. The dimensions are taken
                // from the image header, not the rect — the caller-supplied
                // rect controls the on-page display size below.
                stamp = gdiStamp;
            }
            else
            {
                // Last resort: try treating as PNG anyway (some files lack proper header)
                try { stamp = ImageStamp.FromPngData(imageData); }
                catch { throw new ArgumentException(
                    "Unsupported image format. Supported: JPEG, PNG, BMP, GIF, TIFF, or raw RGB data."); }
            }
        }

        // With aspectFit the rectangle is a bounding box, not a target frame: the image
        // fits INSIDE it at its own aspect ratio, centred on both axes — a square image
        // in a wide rect keeps its shape instead of stretching to fill.
        double dx = rect.LLX, dy = rect.LLY, dw = rect.Width, dh = rect.Height;
        if (aspectFit && stamp.PixelWidth > 0 && stamp.PixelHeight > 0 && dw > 0 && dh > 0)
        {
            var scale = System.Math.Min(dw / stamp.PixelWidth, dh / stamp.PixelHeight);
            var fitW = stamp.PixelWidth * scale;
            var fitH = stamp.PixelHeight * scale;
            dx += (dw - fitW) / 2;
            dy += (dh - fitH) / 2;
            dw = fitW; dh = fitH;
        }
        stamp.X = dx;
        stamp.Y = dy;
        stamp.DisplayWidth = dw;
        stamp.DisplayHeight = dh;
        stamp.CompensatePageRotation = true;
        stamp.ApplyTo(this);
    }

    /// <summary>Place a pre-encoded CCITT Group 4 (1-bit) image at the given rectangle —
    /// the <see cref="Image.IsBlackWhite"/> fast path that embeds a bilevel TIFF's G4
    /// strip without re-encoding.</summary>
    internal void AddCcittImage(byte[] g4Data, int pixelWidth, int pixelHeight, bool blackIs1, Rectangle rect)
    {
        var stamp = ImageStamp.FromCcittG4(g4Data, pixelWidth, pixelHeight, blackIs1);
        stamp.X = rect.LLX;
        stamp.Y = rect.LLY;
        stamp.DisplayWidth = rect.Width;
        stamp.DisplayHeight = rect.Height;
        stamp.ApplyTo(this);
    }

    /// <summary>
    /// Parse a BMP file and return an ImageStamp. Handles the common 24-bit BGR and
    /// 32-bit BGRA Windows BMP variants (BITMAPINFOHEADER, BI_RGB, top-down or bottom-up)
    /// plus 8-bit and 4-bit paletted variants. RLE-compressed and OS/2 v1 fall back to
    /// ArgumentException.
    /// </summary>
    private static ImageStamp ImageStampFromBmp(byte[] bmp)
    {
        if (bmp.Length < 54) throw new ArgumentException("BMP too small.");
        // File header: 'BM' (2) + size (4) + reserved (4) + offBits (4) = 14 bytes
        int offBits = bmp[10] | (bmp[11] << 8) | (bmp[12] << 16) | (bmp[13] << 24);
        // DIB header starts at 14: size (4) + width (4) + height (4) + planes (2) + bpp (2) + comp (4) + …
        int dibSize = bmp[14] | (bmp[15] << 8) | (bmp[16] << 16) | (bmp[17] << 24);
        int width = bmp[18] | (bmp[19] << 8) | (bmp[20] << 16) | (bmp[21] << 24);
        int heightRaw = bmp[22] | (bmp[23] << 8) | (bmp[24] << 16) | (bmp[25] << 24);
        bool topDown = heightRaw < 0;
        int height = topDown ? -heightRaw : heightRaw;
        int bpp = bmp[28] | (bmp[29] << 8);
        int compression = bmp[30] | (bmp[31] << 8) | (bmp[32] << 16) | (bmp[33] << 24);
        int paletteSize = bmp[46] | (bmp[47] << 8) | (bmp[48] << 16) | (bmp[49] << 24);
        if (width <= 0 || height <= 0)
            throw new ArgumentException("BMP has zero or negative dimensions.");
        if (compression != 0)
            throw new ArgumentException("Compressed BMP variants (RLE / BI_BITFIELDS) not supported.");
        if (bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32)
            throw new ArgumentException($"BMP bit-depth {bpp} not supported (4 / 8 / 24 / 32 only).");

        // Paletted BMPs: read color table starting at 14 + dibSize. Each entry is BGRA, 4 bytes.
        byte[]? palette = null;
        if (bpp <= 8)
        {
            int paletteOff = 14 + dibSize;
            int entries = paletteSize > 0 ? paletteSize : (1 << bpp);
            if (paletteOff + entries * 4 > bmp.Length)
                throw new ArgumentException("BMP palette truncated.");
            palette = new byte[entries * 3];
            for (int i = 0; i < entries; i++)
            {
                int s = paletteOff + i * 4;
                // Palette stored as BGRA; we want RGB.
                palette[i * 3 + 0] = bmp[s + 2];
                palette[i * 3 + 1] = bmp[s + 1];
                palette[i * 3 + 2] = bmp[s + 0];
            }
        }

        // BMP rows are padded to a 4-byte boundary.
        int srcStride = ((width * bpp + 31) / 32) * 4;
        if (offBits + srcStride * height > bmp.Length)
            throw new ArgumentException("BMP pixel data truncated.");

        var rgb = new byte[width * height * 3];
        for (int row = 0; row < height; row++)
        {
            // Bottom-up: file row (height-1-row) is what we read for output row (row).
            int srcRow = topDown ? row : (height - 1 - row);
            int srcRowOff = offBits + srcRow * srcStride;
            int dstRowOff = row * width * 3;
            for (int col = 0; col < width; col++)
            {
                int idx;
                if (bpp == 24 || bpp == 32)
                {
                    int s = srcRowOff + col * (bpp / 8);
                    rgb[dstRowOff + col * 3 + 0] = bmp[s + 2];
                    rgb[dstRowOff + col * 3 + 1] = bmp[s + 1];
                    rgb[dstRowOff + col * 3 + 2] = bmp[s + 0];
                    continue;
                }
                else if (bpp == 8)
                {
                    idx = bmp[srcRowOff + col];
                }
                else // bpp == 4
                {
                    int packed = bmp[srcRowOff + col / 2];
                    idx = (col & 1) == 0 ? (packed >> 4) : (packed & 0x0F);
                }
                int p = idx * 3;
                rgb[dstRowOff + col * 3 + 0] = palette![p + 0];
                rgb[dstRowOff + col * 3 + 1] = palette[p + 1];
                rgb[dstRowOff + col * 3 + 2] = palette[p + 2];
            }
        }
        return ImageStamp.FromRgb(rgb, width, height);
    }

    /// <summary>
    /// Add an image from a stream to this page at the specified position and size.
    /// </summary>
    public void AddImage(Stream imageStream, Rectangle rect)
    {
        if (imageStream is null) throw new ArgumentNullException(nameof(imageStream));
        // Callers commonly pass a stream they just wrote to (e.g.
        // 'bitmap.Save(image, Bmp); page.AddImage(image, ...);') — the
        // position sits at end-of-stream after the write, so a naive CopyTo
        // copies zero bytes and the byte[] overload throws 'Unsupported
        // image format'. Rewind seekable streams first.
        if (imageStream.CanSeek) imageStream.Position = 0;
        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        AddImage(ms.ToArray(), rect);
    }

    /// <summary>Add an image from a file path; the rectangle bounds the image, which
    /// keeps its own aspect ratio centred inside it.</summary>
    public void AddImage(string imagePath, Rectangle rectangle)
    {
        if (imagePath is null) throw new ArgumentNullException(nameof(imagePath));
        AddImage(File.ReadAllBytes(imagePath), rectangle, blackWhite: false, aspectFit: true);
    }

    /// <summary>Add an image at <paramref name="imageRect"/> with an explicit bounding-box. Stored only — falls back to <see cref="AddImage(Stream, Rectangle)"/>.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, Rectangle bbox, bool autoAdjustRectangle)
    {
        _ = bbox; _ = autoAdjustRectangle;
        AddImage(imageStream, imageRect);
    }

    /// <summary>Add an image with explicit pixel size + proportion flag (bbox defaults to
    /// the image rectangle). Mirrors the public 5-argument overload used to control
    /// image resolution.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, int imageWidth, int imageHeight, bool saveImageProportions)
    {
        AddImage(imageStream, imageRect, imageWidth, imageHeight, saveImageProportions, imageRect);
    }

    /// <summary>Add an image with explicit pixel size + bbox. Stored only.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, int imageWidth, int imageHeight, bool saveImageProportions, Rectangle bbox)
    {
        _ = imageWidth; _ = imageHeight; _ = saveImageProportions; _ = bbox;
        AddImage(imageStream, imageRect);
    }

    /// <summary>Insert an image and overlay an HOCR (OCR) string as an invisible text
    /// layer (text rendering mode 3), so the page shows the image but its recognised
    /// text is searchable / copy-pasteable. Used to build a searchable image PDF.</summary>
    public void AddImage(string hocr, Stream imageStream, Rectangle imageRect)
    {
        AddImage(imageStream, imageRect);
        if (!string.IsNullOrEmpty(hocr))
            Document.OverlayHocrAsInvisibleText(this, hocr);
    }

    /// <summary>Insert an image and overlay an HOCR (OCR) string as an invisible text
    /// layer; <paramref name="bbox"/> is accepted for API parity.</summary>
    public void AddImage(string hocr, Stream imageStream, Rectangle imageRect, Rectangle bbox)
    {
        _ = bbox;
        AddImage(hocr, imageStream, imageRect);
    }

    /// <summary>Resize this page to <paramref name="targetSize"/> via media-box update.</summary>
    public void Resize(Aspose.Pdf.PageSize targetSize)
    {
        if (targetSize is null) return;
        // Scale the existing content to the new page box, not just the box itself —
        // otherwise the content keeps its original size and appears zoomed relative to
        // the resized page. Prepend a `cm` that maps the current media box
        // onto the target size; it precedes any q/Q so the whole content is scaled.
        var mb = MediaBox;
        double curW = mb.Width, curH = mb.Height;
        if (curW > 0 && curH > 0)
        {
            double sx = targetSize.Width / curW;
            double sy = targetSize.Height / curH;
            Contents.Insert(1, new Aspose.Pdf.Operators.ConcatenateMatrix(new[] { sx, 0, 0, sy, 0, 0 }));
        }
        MediaBox = new Rectangle(0, 0, targetSize.Width, targetSize.Height);
    }

    /// <summary>Render this page into a PNG byte array at the requested resolution.</summary>
    public byte[] AsByteArray(Aspose.Pdf.Devices.Resolution resolution)
    {
        using var ms = new MemoryStream();
        new Aspose.Pdf.Devices.PngDevice(resolution ?? new Aspose.Pdf.Devices.Resolution(150)).Process(this, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Flatten all annotations on this page — render their visual appearance
    /// into the page content stream and remove them from the annotations array.
    /// </summary>
    /// <summary>
    /// Flattens all annotations into the page's content stream.
    /// Each annotation's appearance stream (AP/N) is drawn at the annotation's Rect position
    /// by computing a CTM that maps the appearance's BBox to the Rect. The annotation is then
    /// removed from the page's /Annots array. Popup annotations are skipped (they have no
    /// visual appearance). After flattening, annotations no longer exist as interactive objects.
    /// </summary>
    public void Flatten()
    {
        var annotsObj = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annotsObj is null || annotsObj.Count == 0) return;

        // A page that belongs to a document with a form flattens through the form: each
        // annotation becomes an XObject in the page's own resources (so the flattened
        // widgets are reachable as Resources.Forms) and the fields that lived here leave
        // the AcroForm. Without an owning document there is nothing to retire, and the
        // inline path below still folds the appearances into the content stream.
        if (_reader.OwnerDocument is { } ownerDoc
            && _reader.ResolveDict(_reader.Catalog.Get("AcroForm")) is not null)
        {
            ownerDoc.Form.FlattenSinglePage(ownerDoc, this);
            return;
        }

        var appendContent = new MemoryStream();

        foreach (var annotRef in annotsObj)
        {
            var annotDict = _reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            // Popup annotations are auxiliary UI elements with no drawn appearance
            var subtype = annotDict.GetName("Subtype");
            if (subtype == "Popup") continue;

            var appearanceStream = ResolveAppearanceStream(annotDict);
            if (appearanceStream is null)
            {
                // Shape/markup annotations are often stored without an /AP (the viewer
                // synthesises one). Generate it from the annotation's geometry so the
                // figure is baked into the page instead of vanishing on flatten.
                var typed = Aspose.Pdf.Annotations.Annotation.Create(annotDict, _reader);
                if (typed is Aspose.Pdf.Annotations.SquareAnnotation
                          or Aspose.Pdf.Annotations.CircleAnnotation
                          or Aspose.Pdf.Annotations.TextAnnotation)
                    typed.UpdateAppearances();
                appearanceStream = ResolveAppearanceStream(annotDict);
                if (appearanceStream is null) continue;
            }

            var rectArr = _reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is null || rectArr.Count < 4) continue;

            var rect = Rectangle.FromPdfArray(rectArr);
            var streamData = _reader.DecodeStream(appearanceStream);

            // Compute the CTM that maps the appearance BBox to the annotation Rect.
            // Scale factors map BBox dimensions to Rect dimensions; translation offsets
            // position the appearance at Rect.LLX/LLY, compensating for BBox origin.
            var (sx, sy, tx, ty) = ComputeAppearanceCtm(rect, appearanceStream);

            var writer = new StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write(
                $"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n");
            writer.Flush();
            appendContent.Write(streamData);
            writer.Write("\nQ\n");
            writer.Flush();

            // Merge the appearance stream's Resources into the page's Resources
            // so that fonts/images referenced by the appearance remain available
            Forms.Form.MergeAnnotResources(_dict, appearanceStream.Dict, _reader);
        }

        // Remove all annotations — they are now baked into the content stream
        _dict.Remove("Annots");
        _annotations = null;

        if (appendContent.Length > 0)
            AppendToContentStream(appendContent.ToArray());
    }

    /// <summary>
    /// Resolves the normal appearance stream (AP → N) for an annotation.
    /// Handles both direct streams and state dictionaries (where the current state
    /// is selected by the /AS entry, falling back to the first non-Off state).
    /// </summary>
    private PdfStream? ResolveAppearanceStream(PdfDictionary annotDict)
    {
        var apDict = _reader.ResolveDict(annotDict.Get("AP"));
        if (apDict is null) return null;

        var nResolved = _reader.Resolve(apDict.Get("N"));

        // Direct appearance stream — most common case
        if (nResolved is PdfStream ns)
            return ns;

        // State dictionary — /N is a dict mapping state names (e.g. "Yes"/"Off") to streams.
        // Select the stream for the current state (/AS), or the first non-Off state.
        if (nResolved is PdfDictionary stateDict)
        {
            var asName = annotDict.GetName("AS");
            if (asName is not null)
            {
                var stream = _reader.ResolveStream(stateDict.Get(asName));
                if (stream is not null) return stream;
            }
            foreach (var key in stateDict.Keys)
            {
                if (key == "Off") continue;
                var stream = _reader.ResolveStream(stateDict.Get(key));
                if (stream is not null) return stream;
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the scale and translation needed to map an appearance stream's BBox to
    /// the annotation's Rect on the page (PDF 32000 §12.5.5, Table 168).
    /// </summary>
    private (double sx, double sy, double tx, double ty) ComputeAppearanceCtm(
        Rectangle rect, PdfStream appearanceStream)
    {
        var bboxArr = _reader.Resolve(appearanceStream.Dict.Get("BBox")) as PdfArray;
        double bboxW = rect.Width, bboxH = rect.Height;
        double bboxX = 0, bboxY = 0;
        if (bboxArr is { Count: >= 4 })
        {
            var bbox = Rectangle.FromPdfArray(bboxArr);
            bboxW = bbox.Width;
            bboxH = bbox.Height;
            bboxX = bbox.LLX;
            bboxY = bbox.LLY;
        }

        var sx = bboxW > 0 ? rect.Width / bboxW : 1.0;
        var sy = bboxH > 0 ? rect.Height / bboxH : 1.0;
        var tx = rect.LLX - bboxX * sx;
        var ty = rect.LLY - bboxY * sy;
        return (sx, sy, tx, ty);
    }

    /// <summary>Appends raw content bytes to the existing page content stream.</summary>
    private void AppendToContentStream(byte[] contentToAppend)
    {
        var existing = _reader.Resolve(_dict.Get("Contents"));
        byte[] existingData = existing is PdfStream es ? _reader.DecodeStream(es) : [];

        var combined = new byte[existingData.Length + 1 + contentToAppend.Length];
        existingData.CopyTo(combined, 0);
        if (existingData.Length > 0)
            combined[existingData.Length] = (byte)'\n';
        contentToAppend.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));

        SetContentStream(combined);
    }

    private static string Format(double v) =>
        v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Apply a CTM transform to the page content by wrapping the existing content stream
    /// in <c>q {sx} 0 0 {sy} {tx} {ty} cm … Q</c>.
    /// Annotation Rect arrays are also scaled/translated by the same matrix.
    /// </summary>
    internal void ApplyContentResize(double sx, double sy, double tx, double ty)
    {
        var originalContent = CollectContentBytes();

        // Emit the resize matrix as the FIRST operator, then isolate the original
        // content in q…Q: {sx} 0 0 {sy} {tx} {ty} cm  q  … original content …  Q
        // (the cm comes first, so it is page.Contents.Commands[1]).
        var prefix = System.Text.Encoding.ASCII.GetBytes(
            $"{Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\nq\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\nQ\n");

        var wrapped = new byte[prefix.Length + originalContent.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        originalContent.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + originalContent.Length);

        SetContentStream(wrapped);

        // Transform annotation Rect arrays
        TransformAnnotationRects(sx, sy, tx, ty);
    }

    /// <summary>Like <see cref="ApplyContentResize"/>, but moves the original page
    /// content into a Form XObject and leaves only <c>q … cm /Fm Do Q</c> on the page.
    /// Keeps the page operator stream free of the content's own transforms (so the
    /// applied resize matrix is the single top-level transform) — the
    /// PdfFileEditor.ResizeContents behaviour.</summary>
    internal void ApplyContentResizeAsForm(double sx, double sy, double tx, double ty)
    {
        var originalContent = CollectContentBytes();
        var formName = WrapContentInForm(originalContent);

        var bytes = System.Text.Encoding.ASCII.GetBytes(
            $"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{formName} Do\nQ\n");
        SetContentStream(bytes);

        TransformAnnotationRects(sx, sy, tx, ty);
    }

    /// <summary>Move the page's content into a Form XObject scaled by (sx,sy) and offset
    /// by (tx,ty), keeping the MediaBox. The moved content is bracketed with q/Q INSIDE the
    /// form (so the form's graphics state is self-balanced) and the page invokes it with a
    /// single cm + Do. Used by PdfPageEditor.Zoom.</summary>
    internal void ApplyZoomAsForm(double sx, double sy, double tx, double ty)
    {
        var originalContent = CollectContentBytes();
        var q = System.Text.Encoding.ASCII.GetBytes("q\n");
        var endQ = System.Text.Encoding.ASCII.GetBytes("\nQ\n");
        var bracketed = new byte[q.Length + originalContent.Length + endQ.Length];
        q.CopyTo(bracketed, 0);
        originalContent.CopyTo(bracketed, q.Length);
        endQ.CopyTo(bracketed, q.Length + originalContent.Length);

        var formName = WrapContentInForm(bracketed);
        // q … Q around the invocation: the resulting page stream is
        // q / cm / Do / Q, so cm sits at Contents[2] and Do at Contents[3].
        var bytes = System.Text.Encoding.ASCII.GetBytes(
            $"q\n{Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{formName} Do\nQ\n");
        SetContentStream(bytes);

        TransformAnnotationRects(sx, sy, tx, ty);
    }

    /// <summary>Decode and concatenate the page's content stream(s) into one byte array.</summary>
    private byte[] CollectContentBytes()
    {
        var existing = _reader.Resolve(_dict.Get("Contents"));
        if (existing is PdfStream singleStream)
            return _reader.DecodeStream(singleStream);
        if (existing is PdfArray arr)
        {
            using var buf = new MemoryStream();
            foreach (var item in arr)
            {
                var stream = _reader.ResolveStream(item);
                if (stream is null) continue;
                var data = _reader.DecodeStream(stream);
                if (buf.Length > 0) buf.WriteByte((byte)'\n');
                buf.Write(data);
            }
            return buf.ToArray();
        }
        return [];
    }

    /// <summary>Wrap <paramref name="content"/> in a Form XObject whose resources mirror
    /// the page's (including a snapshot of the existing /XObject entries so the moved
    /// content's images still resolve), register it under a fresh /FmN name in the
    /// page's /Resources/XObject, and return that name.</summary>
    private string WrapContentInForm(byte[] content)
    {
        // Resolve the page /Resources — it is frequently an indirect reference, in which
        // case `as PdfDictionary` would yield null and the moved content would lose every
        // font/image/XObject it references (e.g. a missing /Im1). A page that INHERITS its
        // resources (no /Resources of its own) is seeded from the inherited dict rather
        // than an empty one, so the wrapped content keeps its fonts/images.
        var resources = Forms.Form.EnsureOwnPageResources(_dict, _reader);

        var formResources = new PdfDictionary();
        foreach (var key in new[] { "Font", "ExtGState", "Pattern", "ColorSpace", "Shading", "ProcSet", "Properties" })
        {
            var entry = resources.Get(key);
            if (entry is not null) formResources.Set(key, entry);
        }

        // Snapshot the page's current /XObject entries BEFORE the form is registered,
        // so the moved content can reference them but the form can't see itself.
        var pageXObjects = _reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (pageXObjects is not null)
        {
            var formXObjects = new PdfDictionary();
            foreach (var key in pageXObjects.Keys)
            {
                var entry = pageXObjects.Get(key);
                if (entry is not null) formXObjects.Set(key, entry);
            }
            formResources.Set("XObject", formXObjects);
        }

        var mb = MediaBox;
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(mb.LLX));
        bbox.Add(new PdfReal(mb.LLY));
        bbox.Add(new PdfReal(mb.URX));
        bbox.Add(new PdfReal(mb.URY));

        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        formDict.Set("FormType", new PdfInteger(1));
        formDict.Set("BBox", bbox);
        formDict.Set("Resources", formResources);
        var formStream = new PdfStream(formDict, content);

        var xobjects = _reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }

        // Content wrapped into a form by resize/zoom is numbered from Fm0
        // (PdfFileEditor.ResizeContents yields /Fm0).
        var name = "Fm0";
        var counter = 0;
        while (xobjects.ContainsKey(name)) name = $"Fm{++counter}";
        xobjects.Set(name, formStream);
        return name;
    }

    private void TransformAnnotationRects(double sx, double sy, double tx, double ty)
    {
        var annotsObj = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        foreach (var annotRef in annotsObj)
        {
            var annotDict = _reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            // Sticky-note (/Text) annotations render as a fixed-size icon anchored at
            // their rectangle, so a content resize leaves their rect in place rather
            // than scaling it with the content.
            if (annotDict.GetName("Subtype") == "Text") continue;

            // Transform /Rect
            var rectArr = _reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is { Count: >= 4 })
                TransformCoordArray(rectArr, sx, sy, tx, ty);

            // Transform /QuadPoints (flat array of x,y pairs)
            var qpArr = _reader.Resolve(annotDict.Get("QuadPoints")) as PdfArray;
            if (qpArr is not null)
                TransformCoordArray(qpArr, sx, sy, tx, ty);
        }
    }

    private static double GetNum(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static void TransformCoordArray(PdfArray arr, double sx, double sy, double tx, double ty)
    {
        for (int i = 0; i + 1 < arr.Count; i += 2)
        {
            double xv = GetNum(arr[i]);
            double yv = GetNum(arr[i + 1]);
            arr.ReplaceAt(i,     new PdfReal(sx * xv + tx));
            arr.ReplaceAt(i + 1, new PdfReal(sy * yv + ty));
        }
    }

    /// <summary>
    /// Physically bake a page rotation of <paramref name="degrees"/> (0/90/180/270) into the
    /// page geometry: wrap the content stream in the rotation CTM, map every page box and the
    /// annotation /Rect, /QuadPoints and appearance /Matrix into the rotated space, and clear
    /// the /Rotate viewing flag. Unlike the <see cref="Rotate"/> flag (which leaves the stored
    /// geometry untouched and only rotates the view), this stores the rotation as content
    /// geometry so the annotation rectangles report their rotated positions — the
    /// <c>PdfPageEditor.PageRotations</c> semantics.
    /// </summary>
    internal void BakeRotation(int degrees)
    {
        int rot = ((degrees % 360) + 360) % 360;
        // The baked geometry below is absolute, so the viewing flag is always cleared.
        _dict.Set("Rotate", new PdfInteger(0));
        if (rot == 0) return;

        var mb = MediaBox ?? new Rectangle(0, 0, 612, 792);
        double ox = mb.LLX, oy = mb.LLY, w = mb.Width, h = mb.Height;

        // Affine that maps an old page coordinate (x,y) to the rotated space whose origin is
        // (0,0): x' = a*x + c*y + e, y' = b*x + d*y + f
        // (e.g. 90deg clockwise maps (x,y) -> (y, w - x)).
        double a, b, c, d, e, f;
        switch (rot)
        {
            case 90:  a = 0; b = -1; c = 1; d = 0;  e = -oy;    f = w + ox; break;
            case 180: a = -1; b = 0; c = 0; d = -1; e = w + ox; f = h + oy; break;
            default:  a = 0; b = 1; c = -1; d = 0;  e = h + oy; f = -ox;    break; // 270
        }

        // Wrap the original content in the rotation CTM, isolating it in q…Q just as
        // ApplyContentResize does: {a b c d e f} cm  q  … original …  Q.
        var originalContent = CollectContentBytes();
        var prefix = System.Text.Encoding.ASCII.GetBytes(
            $"{Format(a)} {Format(b)} {Format(c)} {Format(d)} {Format(e)} {Format(f)} cm\nq\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\nQ\n");
        var wrapped = new byte[prefix.Length + originalContent.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        originalContent.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + originalContent.Length);
        SetContentStream(wrapped);

        // Map every defined page box through the same affine (corners then renormalise).
        foreach (var boxName in new[] { "MediaBox", "CropBox", "BleedBox", "TrimBox", "ArtBox" })
        {
            var box = GetBox(boxName);
            if (box is null) continue;
            SetBox(boxName, TransformRect(box, a, b, c, d, e, f));
        }

        TransformAnnotationGeometry(a, b, c, d, e, f);
    }

    /// <summary>Map a rectangle's two corners through the affine and renormalise to LL/UR.</summary>
    private static Rectangle TransformRect(Rectangle r, double a, double b, double c, double d, double e, double f)
    {
        double x0 = a * r.LLX + c * r.LLY + e, y0 = b * r.LLX + d * r.LLY + f;
        double x1 = a * r.URX + c * r.URY + e, y1 = b * r.URX + d * r.URY + f;
        return new Rectangle(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <summary>Transform every annotation's /Rect (renormalised), /QuadPoints and appearance
    /// /Matrix by the page-rotation affine so the annotations move and orient with the content.</summary>
    private void TransformAnnotationGeometry(double a, double b, double c, double d, double e, double f)
    {
        var annots = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annots is null) return;

        foreach (var annotRef in annots)
        {
            var annotDict = _reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            var rectArr = _reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is { Count: >= 4 })
            {
                var nr = TransformRect(
                    new Rectangle(GetNum(rectArr[0]), GetNum(rectArr[1]), GetNum(rectArr[2]), GetNum(rectArr[3])),
                    a, b, c, d, e, f);
                rectArr.ReplaceAt(0, new PdfReal(nr.LLX));
                rectArr.ReplaceAt(1, new PdfReal(nr.LLY));
                rectArr.ReplaceAt(2, new PdfReal(nr.URX));
                rectArr.ReplaceAt(3, new PdfReal(nr.URY));
            }

            var qpArr = _reader.Resolve(annotDict.Get("QuadPoints")) as PdfArray;
            if (qpArr is not null)
            {
                for (int i = 0; i + 1 < qpArr.Count; i += 2)
                {
                    double xv = GetNum(qpArr[i]), yv = GetNum(qpArr[i + 1]);
                    qpArr.ReplaceAt(i,     new PdfReal(a * xv + c * yv + e));
                    qpArr.ReplaceAt(i + 1, new PdfReal(b * xv + d * yv + f));
                }
            }

            // Pre-rotate the normal appearance stream(s) by the linear part of the affine so the
            // annotation's drawn content turns with the page (the viewer fits the appearance BBox
            // into the new /Rect, so only the rotation — not the translation — belongs here).
            RotateAppearanceMatrices(annotDict, a, b, c, d);
        }
    }

    /// <summary>Compose [a b c d 0 0] onto the left of each /AP /N appearance stream's /Matrix
    /// (handling both a single stream and a sub-dictionary of appearance states).</summary>
    private void RotateAppearanceMatrices(PdfDictionary annotDict, double a, double b, double c, double d)
    {
        var ap = _reader.ResolveDict(annotDict.Get("AP"));
        if (ap is null) return;
        var normal = _reader.Resolve(ap.Get("N"));
        if (normal is PdfStream s)
            ComposeStreamMatrix(s, a, b, c, d);
        else if (normal is PdfDictionary states)
        {
            foreach (var key in states.Keys)
                if (_reader.ResolveStream(states.Get(key)) is PdfStream st)
                    ComposeStreamMatrix(st, a, b, c, d);
        }
    }

    private void ComposeStreamMatrix(PdfStream stream, double a, double b, double c, double d)
    {
        // Existing form matrix (default identity).
        double ma = 1, mb = 0, mc = 0, md = 1, me = 0, mf = 0;
        if (_reader.Resolve(stream.Dict.Get("Matrix")) is PdfArray m && m.Count >= 6)
        {
            ma = GetNum(m[0]); mb = GetNum(m[1]); mc = GetNum(m[2]);
            md = GetNum(m[3]); me = GetNum(m[4]); mf = GetNum(m[5]);
        }
        // R * M with R = [a b c d 0 0] (rotation only). Translation of R is intentionally
        // dropped: the viewer maps the transformed BBox onto /Rect, supplying the offset.
        double na = a * ma + c * mb,        nb = b * ma + d * mb;
        double nc = a * mc + c * md,        nd = b * mc + d * md;
        double ne = a * me + c * mf,        nf = b * me + d * mf;
        var arr = new PdfArray();
        arr.Add(new PdfReal(na)); arr.Add(new PdfReal(nb)); arr.Add(new PdfReal(nc));
        arr.Add(new PdfReal(nd)); arr.Add(new PdfReal(ne)); arr.Add(new PdfReal(nf));
        stream.Dict.Set("Matrix", arr);
    }

    /// <summary>
    /// Determines whether the page is blank (has no meaningful content).
    /// A page is considered blank if it has no content stream or an empty/whitespace-only content stream,
    /// and no annotations, images, or form XObjects.
    /// </summary>
    /// <param name="tolerance">Coverage threshold (0..1). Pages whose drawn area
    /// is smaller than <paramref name="tolerance"/> count as blank. The current
    /// implementation does not perform coverage analysis — it returns true only
    /// when the page has zero visible content, matching <c>tolerance == 0</c>.</param>
    /// <summary>
    /// Convenience helper: render this page to a PNG and return the bytes
    /// as a <see cref="MemoryStream"/>. Equivalent to wrapping a
    /// <see cref="Aspose.Pdf.Devices.PngDevice"/> + Process(page, stream).
    /// </summary>
    public MemoryStream ConvertToPNGMemoryStream()
    {
        var ms = new MemoryStream();
        var device = new Aspose.Pdf.Devices.PngDevice();
        device.Process(this, ms);
        ms.Position = 0;
        return ms;
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

    /// <summary>Fraction of the image's pixels that are not pure white (any
    /// channel below max), on the image's own pixel grid — the metric
    /// <see cref="IsBlank"/> uses. An inverting /Decode flips the interpretation.</summary>
    private double NonWhiteImageFraction(PdfDictionary dict, byte[] data, bool inline)
    {
        int W(string a, string b) => (int)(dict.Get(a) is not null ? dict.GetInt(a) : dict.GetInt(b));
        var w = W("Width", "W");
        var h = W("Height", "H");
        if (w <= 0 || h <= 0) return 0;
        var bpc = W("BitsPerComponent", "BPC");
        if (bpc == 0) bpc = 8;
        var filter = dict.GetName("Filter") ?? dict.GetName("F");
        var decodeInverts = (_reader.Resolve(dict.Get("Decode") ?? dict.Get("D")) is PdfArray da)
                            && da.Count >= 2 && CoverageNum(da[0]) > CoverageNum(da[1]);
        long total = (long)w * h;
        if (total <= 0) return 0;

        // Image mask / bilevel: fraction of painting (1-after-Decode) bits.
        var isMask = (dict.Get("ImageMask") ?? dict.Get("IM")) is PdfBoolean im && im.Value;

        long nonWhite = 0;
        switch (filter)
        {
            case "DCTDecode":
            case "DCT":
            {
                var (px, jw, jh, comps) = IO.Filters.JpegDecoder.Decode(data,
                    Devices.SoftwarePageRenderer.CmykDecodeInverts(dict));
                total = (long)jw * jh;
                var n = comps == 1 ? 1 : 3;
                for (long i = 0; i < total; i++)
                {
                    var o = i * n;
                    for (var c = 0; c < n; c++)
                        if (px[o + c] < 255) { nonWhite++; break; }
                }
                break;
            }
            case "JPXDecode":
            {
                if (!IO.Filters.JpxDecoder.TryDecode(data, out var px, out var jw, out var jh, out var comps))
                    return 0;
                total = (long)jw * jh;
                var n = comps >= 3 ? 3 : 1;
                for (long i = 0; i < total; i++)
                {
                    var o = i * n;
                    for (var c = 0; c < n; c++)
                        if (px[o + c] < 255) { nonWhite++; break; }
                }
                break;
            }
            // CCITTFaxDecode and JBIG2Decode are applied by DecodeStream itself and
            // arrive here as 1-bpc DeviceGray rasters (0 = black) — the raw default
            // below counts their ink bits.
            default:
            {
                // Raw samples (any stream-level filters already undone).
                var csObj = _reader.Resolve(dict.Get("ColorSpace") ?? dict.Get("CS"));
                var comps = csObj switch
                {
                    PdfName n2 when n2.Value is "DeviceRGB" or "RGB" or "CalRGB" => 3,
                    PdfName n2 when n2.Value is "DeviceCMYK" or "CMYK" => 4,
                    PdfArray => 1, // Indexed/ICC etc. — treat one component per sample
                    _ => 1,
                };
                if (isMask) comps = 1;
                if (bpc == 8)
                {
                    var rowLen = w * comps;
                    if ((long)rowLen * h > data.Length) return 0;
                    var whiteVal = decodeInverts ? 0 : 255;
                    for (long i = 0; i < total; i++)
                    {
                        var o = i * comps;
                        for (var c = 0; c < comps; c++)
                            if (data[o + c] != whiteVal) { nonWhite++; break; }
                    }
                }
                else if (bpc == 1 && comps == 1)
                {
                    // 1 = white for DeviceGray; an ImageMask's 1 = painted (per
                    // Decode). Count the ink bits.
                    var invert = isMask ? !decodeInverts : decodeInverts;
                    nonWhite = CountBits(data, w, h, invert: !invert);
                }
                else
                {
                    return 0; // exotic depths: no contribution rather than a guess
                }
                break;
            }
        }
        return total > 0 ? (double)nonWhite / total : 0;
    }

    /// <summary>Count "ink" bits in a packed 1-bpp raster (rows padded to bytes).
    /// With <paramref name="invert"/> true a 0 bit is ink, otherwise a 1 bit.</summary>
    private static long CountBits(byte[] bits, int w, int h, bool invert)
    {
        var rowBytes = (w + 7) / 8;
        if ((long)rowBytes * h > bits.Length) return 0;
        long count = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var bit = (bits[y * rowBytes + x / 8] >> (7 - x % 8)) & 1;
                if (invert == (bit == 0)) count++;
            }
        }
        return count;
    }

    private static double CoverageNum(PdfObject o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Header to render at the top of this page.</summary>
    public HeaderFooter? Header { get; set; }

    /// <summary>Footer to render at the bottom of this page.</summary>
    public HeaderFooter? Footer { get; set; }

    /// <summary>
    /// TOC information for this page. When set, the page acts as a Table of Contents.
    /// </summary>
    public TocInfo? TocInfo { get; set; }

    /// <summary>
    /// Collection of paragraph objects to add to this page (TextFragment, HtmlFragment, Table, Heading, etc.).
    /// Paragraphs are rendered on save.
    /// </summary>
    public Paragraphs Paragraphs { get; set; } = new();

    /// <summary>Default stroke/fill style used to draw footnote / endnote separator lines.</summary>
    public GraphInfo NoteLineStyle { get; set; } = new();

    /// <summary>
    /// Tracks whether Document.ApplyPageContent has already laid out this page's
    /// paragraphs and TOC content. Prevents duplicate rendering when both
    /// ProcessParagraphs and Save run the layout pass.
    /// </summary>
    internal bool LayoutApplied { get; set; }

    /// <summary>True when this page's media box was merely INHERITED from the
    /// document (no-size Pages.Insert) rather than set explicitly. A landscape
    /// request on such a page resolves to the A4-landscape default at layout,
    /// replacing the inherited box.</summary>
    internal bool SizeInherited { get; set; }

    /// <summary>Y cursor left behind by the last paragraph-layout pass. Paragraphs
    /// added AFTER a ProcessParagraphs/Save round continue below the earlier
    /// content instead of restarting at the top margin (which overprinted the
    /// first paragraph and read back as one merged line).</summary>
    internal double? LayoutCursorY { get; set; }

    /// <summary>Tracks whether the page's <see cref="Header"/> / <see cref="Footer"/>
    /// have already been rendered, so a second layout pass (ProcessParagraphs then
    /// Save) does not emit them twice.</summary>
    internal bool HeaderFooterApplied { get; set; }

    private bool _contentIsolated;

    /// <summary>Bracket the page's existing content in q/Q before generated
    /// content is appended. An imported page may leave a persistent CTM active
    /// (e.g. a top-level y-flip `1 0 0 -1 0 H cm` outside any q/Q); without the
    /// bracket, appended header/footer/stamp content inherits that matrix and
    /// renders flipped or displaced. Idempotent.</summary>
    internal void IsolateExistingContent()
    {
        if (_contentIsolated) return;
        _contentIsolated = true;
        if (_reader.Resolve(_dict.Get("Contents")) is null) return;
        PrependContentStream(System.Text.Encoding.ASCII.GetBytes("q\n"));
        AddContentStream(System.Text.Encoding.ASCII.GetBytes("Q\n"));
    }

    /// <summary>Tracks whether the page's <see cref="Background"/> fill has already
    /// been prepended to the content, so a second layout pass (ProcessParagraphs
    /// then Save) does not paint it twice.</summary>
    internal bool BackgroundApplied { get; set; }

    /// <summary>
    /// Collection for adding artifacts (watermarks, etc.) to this page.
    /// </summary>
    public ArtifactCollection Artifacts => _artifacts ??= new ArtifactCollection(this);
    private ArtifactCollection? _artifacts;

    /// <summary>
    /// Gets the layers (Optional Content Groups) referenced by this page.
    /// FOSS-extra accessor — exposes the underlying OCG-backed collection
    /// for callers that need the typed OptionalContentGroup API. The public-API
    /// shape goes through <see cref="Layers"/> (List&lt;Layer&gt;) instead.
    /// </summary>
    public LayerCollection OcgLayers
    {
        get
        {
            if (_layers is null)
            {
                _layers = new LayerCollection(LayerHelper.GetPageLayers(this, _reader));
                _layers.SetPage(this);
            }
            return _layers;
        }
    }
    private LayerCollection? _layers;

    /// <summary>
    /// Merge all layers on this page into a single layer with the given name.
    /// </summary>
    public void MergeLayers(string newLayerName)
    {
        LayerHelper.MergeLayersOnPage(this, newLayerName, _reader);
    }

    /// <summary>Merge all layers on this page, assigning the new OCG the given id.</summary>
    public void MergeLayers(string newLayerName, string newOptionalContentGroupId)
    {
        _ = newOptionalContentGroupId;
        LayerHelper.MergeLayersOnPage(this, newLayerName, _reader);
    }

    /// <summary>Whether this page paints any vector graphics, i.e. its content
    /// stream invokes a path-painting operator (stroke / fill / fill-and-stroke).</summary>
    public bool HasVectorGraphics()
    {
        foreach (Operator op in Contents)
        {
            switch (op.ToPdf())
            {
                case "S":   // stroke
                case "s":   // close + stroke
                case "f":   // fill (nonzero winding)
                case "F":   // fill (obsolete, == f)
                case "f*":  // fill (even-odd)
                case "B":   // fill + stroke (nonzero winding)
                case "B*":  // fill + stroke (even-odd)
                case "b":   // close + fill + stroke (nonzero winding)
                case "b*":  // close + fill + stroke (even-odd)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Save vector graphics from this page to <paramref name="pathToSave"/>. Stored only.</summary>
    public bool TrySaveVectorGraphics(string pathToSave) { _ = pathToSave; return false; }

    /// <summary>Diagnostic XML representation of this page. Stored only.</summary>
    public string AsXml() => string.Empty;

    /// <summary>Line-break notifications emitted by the flow layout for this page
    /// when <see cref="Document.EnableNotificationLogging"/> was set before save.</summary>
    public string GetNotifications() => NotificationLog;

    /// <summary>Accumulated line-break notifications for this page.</summary>
    internal string NotificationLog { get; set; } = string.Empty;

    /// <summary>Convert this page's colours to grayscale: content-stream colour operators,
    /// image XObjects, named colour-space resources, and annotation appearances.</summary>
    public void MakeGrayscale() => GrayscaleConverter.ConvertPage(this);

    /// <summary>Convert a degrees-int rotation to <see cref="Rotation"/>.</summary>
    public static Rotation IntToRotation(int rotation) => (((rotation % 360) + 360) % 360) switch
    {
        90 => Rotation.on90,
        180 => Rotation.on180,
        270 => Rotation.on270,
        _ => Rotation.None,
    };

    /// <summary>Convert <see cref="Rotation"/> to degrees as an int.</summary>
    public static int RotationToInt(Rotation rotation) => (int)rotation;

    /// <summary>Per-page additional actions (open / close / etc.), backed by /AA.</summary>
    public PageActionCollection Actions => _actions ??= new PageActionCollection(this);
    private PageActionCollection? _actions;

    /// <summary>User-unit factor (PDF 32000 §14.8.4 /UserUnit entry). Stored only.</summary>
    public double UserUnit { get; set; } = 1.0;

    /// <summary>Whether <see cref="Paragraphs"/> additions append at the end (vs flow position). Stored only.</summary>
    public bool IsAddParagraphsAfterLast { get; set; }

    /// <summary>Page background image. Stored only.</summary>
    public Image? BackgroundImage { get; set; }

    /// <summary>Group / blending colour space dictionary for this page. Stored only.</summary>
    public Group? Group { get; set; }

    /// <summary>Tab order (PDF 32000 §12.5 /Tabs entry). Stored only.</summary>
    public TabOrder TabOrder { get; set; } = TabOrder.None;

    private Watermark? _watermark;
    private bool _watermarkSet;

    /// <summary>The page watermark. The getter detects an existing watermark from
    /// the page content (a /Subtype /Watermark artifact and its image) and returns
    /// an unavailable <see cref="Watermark"/> when there is none, so callers can read
    /// <c>Watermark.Available</c> without a null check. The setter stores a watermark
    /// that is drawn into the page (as a watermark artifact) on save.</summary>
    public Watermark? Watermark
    {
        get => _watermarkSet ? _watermark : DetectWatermark();
        set { _watermark = value; _watermarkSet = true; }
    }

    /// <summary>Watermark stored via the setter and awaiting render-on-save; null
    /// when none was set.</summary>
    internal Watermark? PendingWatermark => _watermarkSet ? _watermark : null;

    /// <summary>Detect an existing watermark from the page content: locate a
    /// /Subtype /Watermark artifact carrying an image (the artifact parser follows a
    /// form wrapper to the image) and surface that image as a <see cref="Watermark"/>.
    /// Returns an unavailable watermark when none is present.</summary>
    private Watermark DetectWatermark()
    {
        if (!OperatingSystem.IsWindows()) return new Watermark();
        foreach (var art in Artifacts)
        {
            if (art is not WatermarkArtifact { Image: { } xi }) continue;
            try
            {
                return new Watermark(LoadWatermarkImage(xi));
            }
            catch
            {
                // Unreadable/undecodable image — treat as no watermark rather than throw.
            }
        }
        return new Watermark();
    }

    /// <summary>Decode a watermark's image XObject into a <see cref="System.Drawing.Image"/>.
    /// The backing stream is kept open (Image.FromStream requires it for the image's
    /// lifetime).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Image LoadWatermarkImage(XImage xi)
    {
        using var ms = new MemoryStream();
        xi.Save(ms);
        return System.Drawing.Image.FromStream(new MemoryStream(ms.ToArray()));
    }

    /// <summary>
    /// The raw page dictionary for power-user access.
    /// </summary>
    internal PdfDictionary Dict => _dict;

    /// <summary>
    /// The internal reader for object resolution.
    /// </summary>
    internal PdfReader Reader => _reader;

    private Rectangle? GetBox(string name)
    {
        var obj = _reader.Resolve(_dict.Get(name));
        if (obj is PdfArray arr && arr.Count >= 4)
        {
            var rect = Rectangle.FromPdfArray(ResolveArrayElements(arr));
            // Normalize inverted coordinates (some XFA PDFs have [0,792,612,0])
            if (rect.Width < 0 || rect.Height < 0)
                return new Rectangle(
                    Math.Min(rect.LLX, rect.URX), Math.Min(rect.LLY, rect.URY),
                    Math.Max(rect.LLX, rect.URX), Math.Max(rect.LLY, rect.URY));
            return rect;
        }
        return InheritBox(name);
    }

    private Rectangle? InheritBox(string name)
    {
        // Walk up /Parent chain for inherited attributes
        var parentObj = _dict.Get("Parent");
        var visited = new HashSet<int>();

        while (parentObj is not null)
        {
            var parent = _reader.ResolveDict(parentObj);
            if (parent is null) break;

            // Prevent infinite loops
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;

            var boxObj = _reader.Resolve(parent.Get(name));
            if (boxObj is PdfArray arr && arr.Count >= 4)
                return Rectangle.FromPdfArray(ResolveArrayElements(arr));

            parentObj = parent.Get("Parent");
        }

        return null;
    }

    private PdfArray ResolveArrayElements(PdfArray arr)
    {
        var result = new PdfArray();
        foreach (var item in arr)
        {
            var resolved = _reader.Resolve(item);
            result.Add(resolved ?? PdfNull.Instance);
        }
        return result;
    }

    /// <summary>Clears cached data.</summary>
    public void FreeMemory()
    {
        // Drop heavyweight wrappers; leave _dict and _attachedFragments
        // intact so pending edits are still flushed on save and properties
        // re-materialize on next access.
        _bgColorFragments = null;
        _underlineFragments = null;
        _underlineRemovalFragments = null;
        _strikeOutFragments = null;
        _annotations = null;
        _images = null;
        _fonts = null;
        _resources = null;
    }

    /// <summary>Frees up memory.</summary>
    public void Dispose() => FreeMemory();
}
