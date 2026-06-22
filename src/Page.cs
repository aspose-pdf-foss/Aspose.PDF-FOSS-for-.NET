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
    /// Inject 're'/'f' operators at the start of the content stream for every
    /// registered background-colour fragment. Called during save before the page
    /// content stream is flushed.
    /// </summary>
    internal void FlushBgColorRectangles()
    {
        if (_bgColorFragments is null || _bgColorFragments.Count == 0) return;
        var builder = new Content.ContentStreamBuilder();
        foreach (var frag in _bgColorFragments)
        {
            var fragBg = frag.TextState.BackgroundColor;

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
                    double ox = frag.Position?.XIndent ?? frag.Rectangle?.LLX ?? 0;
                    double oy = frag.Position?.YIndent ?? frag.Rectangle?.LLY ?? 0;

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

                // Check if the fragment was extracted under a non-trivial CTM.
                var ctm = frag.ExtractionCtm;
                double ctmScaleX = ctm is not null ? Math.Sqrt(ctm.A * ctm.A + ctm.B * ctm.B) : 1.0;
                bool hasCtm = ctmScaleX > 1.5; // significant scaling (>1.5x)

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
                    fragX = frag.Rectangle?.LLX ?? frag.Position?.XIndent ?? 0;
                    fragY = frag.Rectangle?.LLY ?? frag.Position?.YIndent ?? 0;
                    (fragX, fragY) = ctm!.InverseTransformPoint(fragX, fragY);
                }
                else
                {
                    // Standard path (no significant CTM): use fragment rectangle
                    // for position/width, compute height from rawFs/TmD metrics.
                    fragW = (frag.Rectangle?.Width ?? 0) - frag.TrailingTcPageSpace;
                    fragX = frag.Rectangle?.LLX ?? frag.Position?.XIndent ?? 0;
                    fragY = frag.Rectangle?.LLY ?? frag.Position?.YIndent ?? 0;

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

                builder.SaveState();
                builder.SetFillColor(fragBg.R / 255.0, fragBg.G / 255.0, fragBg.B / 255.0);
                builder.Rectangle(fragX, fragY, fragW, fragH);
                builder.Fill();
                builder.RestoreState();
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
                        w = frag.Rectangle.URX - startX;
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

                builder.SaveState();
                builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                builder.Rectangle(startX, startY, w, h);
                builder.Fill();
                builder.RestoreState();
                si = lastMerged + 1;
            }
        }
        var bytes = builder.Build();
        if (bytes.Length > 0)
        {
            PrependContentStream(bytes);
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
        int sysWinLH = Text.Standard14Fonts.GetSystemWinLineHeight(fontName);
        if (sysWinLH > 0)
            return sysWinLH / 1000.0 * rawFs * tmD;

        var metrics = font?.GetMetrics();
        if (metrics is not null && metrics.WinLineHeight > 0)
            return metrics.WinLineHeight / 1000.0 * rawFs * tmD;
        if (metrics is not null && (metrics.Ascent != 0 || metrics.Descent != 0))
            return (metrics.Ascent - metrics.Descent) / 1000.0 * rawFs * tmD;

        int bboxH = Text.Standard14Fonts.GetFontBBoxHeight(fontName);
        return bboxH > 0 ? bboxH / 1000.0 * rawFs * tmD : rawFs * tmD * 1.16;
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
            var fragPos = frag.Position;
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

            // Underline offset: 7.7% of font size below baseline, matching typical
            // .NET GDI+ underline positioning for Latin fonts.
            // Thickness: 5% of font size (standard thin-line convention).
            double ulOffset = fs * 0.07691;
            double ulThick = fs * 0.05;

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
            var fragPos = frag.Position;
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
    /// one shot — matches how the Aspose.PDF for .NET API treats the property as the
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
    /// for parity with the public Aspose.Pdf API.
    /// </summary>
    public PageInfo PageInfo
    {
        get => _pageInfoCache ??= new PageInfo(this);
        set => _pageInfoCache = value;
    }

    public Color? Background { get; set; }

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
                90 => new Matrix(0, 1, -1, 0, h, 0),
                180 => new Matrix(-1, 0, 0, -1, w, h),
                270 => new Matrix(0, -1, 1, 0, 0, w),
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
        _images ??= new XImageCollection(_dict, _reader);

    /// <summary>Fonts referenced by this page.</summary>
    public FontCollection Fonts =>
        _fonts ??= new FontCollection(_dict, _reader);

    /// <summary>
    /// Page resources (fonts, images) — provides access via a unified Resources object.
    /// </summary>
    public Resources Resources => _resources ??= new Resources(this);

    /// <summary>Method-style accessor for <see cref="Resources"/> — Aspose.PDF for .NET parity.</summary>
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
        var formName = AddStampForm(stampBytes);
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
    private string AddStampForm(byte[] content)
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

        var name = "Fm1";
        var counter = 1;
        while (xobjects.ContainsKey(name)) name = $"Fm{++counter}";
        xobjects.Set(name, formStream);
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
        var newStream = new PdfStream(new PdfDictionary(), contentStreamBytes);
        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);

        if (resolved is PdfArray existingArray)
        {
            // Already an array — append the new stream
            existingArray.Add(newStream);
        }
        else if (resolved is PdfStream)
        {
            // Single stream — create an array with both
            var arr = new PdfArray();
            arr.Add(existing!); // keep original ref (may be indirect)
            arr.Add(newStream);
            _dict.Set("Contents", arr);
        }
        else
        {
            // No existing content — just set the new stream
            _dict.Set("Contents", newStream);
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
    {
        if (blackWhite && ImageStamp.FromBlackWhite(imageData) is { } bwStamp)
        {
            bwStamp.X = rect.LLX;
            bwStamp.Y = rect.LLY;
            bwStamp.DisplayWidth = rect.Width;
            bwStamp.DisplayHeight = rect.Height;
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

        stamp.X = rect.LLX;
        stamp.Y = rect.LLY;
        stamp.DisplayWidth = rect.Width;
        stamp.DisplayHeight = rect.Height;
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

    /// <summary>Add an image from a file path at the specified rectangle.</summary>
    public void AddImage(string imagePath, Rectangle rectangle)
    {
        if (imagePath is null) throw new ArgumentNullException(nameof(imagePath));
        AddImage(File.ReadAllBytes(imagePath), rectangle);
    }

    /// <summary>Add an image at <paramref name="imageRect"/> with an explicit bounding-box. Stored only — falls back to <see cref="AddImage(Stream, Rectangle)"/>.</summary>
    public void AddImage(Stream imageStream, Rectangle imageRect, Rectangle bbox, bool autoAdjustRectangle)
    {
        _ = bbox; _ = autoAdjustRectangle;
        AddImage(imageStream, imageRect);
    }

    /// <summary>Add an image with explicit pixel size + proportion flag (bbox defaults to
    /// the image rectangle). Mirrors the Aspose.PDF for .NET 5-argument overload used to control
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

    /// <summary>Add an image accompanied by an HOCR (OCR overlay) string. Stored only.</summary>
    public void AddImage(string hocr, Stream imageStream, Rectangle imageRect, Rectangle bbox)
    {
        _ = hocr; _ = bbox;
        AddImage(imageStream, imageRect);
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
        // (Aspose.PDF for .NET places the cm first, so it is page.Contents.Commands[1]).
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
    /// applied resize matrix is the single top-level transform), matching the
    /// Aspose.PDF for .NET PdfFileEditor.ResizeContents behaviour.</summary>
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
        var bytes = System.Text.Encoding.ASCII.GetBytes(
            $"{Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{formName} Do\n");
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
        // font/image/XObject it references (e.g. a missing /Im1).
        var resources = _reader.ResolveDict(_dict.Get("Resources")) ?? new PdfDictionary();
        _dict.Set("Resources", resources);

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

        var name = "Fm1";
        var counter = 1;
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
        // fillThresholdFactor is accepted for API parity; coverage-aware blankness is not
        // implemented yet, so any visible content makes the page non-blank.
        _ = fillThresholdFactor;
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

            // Check content stream
            var contentsObj = _reader.Resolve(_dict.Get("Contents"));
            if (contentsObj is PdfStream contentStream)
            {
                var data = _reader.DecodeStream(contentStream);
                if (HasVisibleContent(data)) return false;
            }
            else if (contentsObj is PdfArray contentArr)
            {
                foreach (var item in contentArr)
                {
                    var stream = _reader.ResolveStream(item);
                    if (stream is not null)
                    {
                        var data = _reader.DecodeStream(stream);
                        if (HasVisibleContent(data)) return false;
                    }
                }
            }

            // Check for image XObjects in resources
            var resources = _reader.ResolveDict(_dict.Get("Resources"));
            if (resources is not null)
            {
                var xobjects = _reader.ResolveDict(resources.Get("XObject"));
                if (xobjects is not null && xobjects.Keys.Any())
                    return false;
            }

            return true;
        }
    }

    private static bool HasVisibleContent(byte[] data)
    {
        if (data.Length == 0) return false;
        // Check if content stream has any visible operators
        // Whitespace/comments only → blank
        var text = System.Text.Encoding.ASCII.GetString(data).Trim();
        return text.Length > 0;
    }

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

    /// <summary>Tracks whether the page's <see cref="Header"/> / <see cref="Footer"/>
    /// have already been rendered, so a second layout pass (ProcessParagraphs then
    /// Save) does not emit them twice.</summary>
    internal bool HeaderFooterApplied { get; set; }

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
    /// for callers that need the typed OptionalContentGroup API. The Aspose.PDF for .NET
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

    /// <summary>Per-page additional actions (open / close / etc.). Stored only.</summary>
    public PageActionCollection Actions => _actions ??= new PageActionCollection();
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

    /// <summary>Watermark applied to this page. Stored only.</summary>
    public Watermark? Watermark { get; set; }

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
        _strikeOutFragments = null;
        _annotations = null;
        _images = null;
        _fonts = null;
        _resources = null;
    }

    /// <summary>Frees up memory.</summary>
    public void Dispose() => FreeMemory();
}

/// <summary>
/// Provides access to a page's resource collections (fonts, images).
/// </summary>
public class PageResources
{
    private readonly Page? _page;
    private readonly XForm? _xform;
    private readonly PdfDictionary? _resDict;
    private readonly PdfReader? _resReader;

    internal PageResources(Page page) => _page = page;

    /// <summary>XForm-backed ctor: enumerates resources via the XForm's
    /// stream dictionary (Font / XObject entries on the form, not the
    /// page).</summary>
    internal PageResources(XForm xform) { _xform = xform; }

    /// <summary>Resource-dictionary-backed ctor: enumerates /Font, /XObject etc.
    /// directly from a resource dict (e.g. the AcroForm /DR).</summary>
    internal PageResources(PdfDictionary resourceDict, PdfReader reader)
    {
        _resDict = resourceDict;
        _resReader = reader;
    }

    private Core.PdfDictionary? XFormResourcesDict()
    {
        if (_xform is null) return null;
        return _xform.Reader.ResolveDict(_xform.StreamDict.Get("Resources"));
    }

    /// <summary>Font resources on this page (or the XForm's stream dict
    /// when constructed via the XForm ctor).</summary>
    public FontCollection Fonts
    {
        get
        {
            if (_resDict is not null) return FontCollection.ForResources(_resDict, _resReader!);
            if (_page is not null) return _page.Fonts;
            // An XForm's stream dict carries /Resources directly (a resource dict
            // whose /Font maps names to font dicts), so read it via ForResources —
            // the page-dict ctor would look for a nested /Resources and find none.
            var resDict = XFormResourcesDict();
            if (resDict is null) return new FontCollection(new Core.PdfDictionary(), _xform!.Reader);
            return FontCollection.ForResources(resDict, _xform!.Reader);
        }
    }

    /// <summary>Image resources on this page.</summary>
    public XImageCollection Images
    {
        get
        {
            if (_page is not null) return _page.Images;
            // The collection ctor discovers images via dict.Get("Resources")/XObject
            // (recursing nested forms). An XForm's stream dict carries /Resources, so
            // pass the stream dict — passing the already-resolved Resources dict would
            // make the ctor look for a nested /Resources that isn't there and yield an
            // empty collection.
            if (_xform is not null) return new XImageCollection(_xform.StreamDict, _xform.Reader);
            if (_resDict is not null)
            {
                var wrap = new Core.PdfDictionary();
                wrap.Set("Resources", _resDict);
                return new XImageCollection(wrap, _resReader!);
            }
            return new XImageCollection(new Core.PdfDictionary(), _resReader!);
        }
    }

    /// <summary>XForm (Form XObject) resources on this page.</summary>
    public XFormCollection Forms
    {
        get
        {
            var reader = _page?.Reader ?? _xform!.Reader;
            var resources = _page is not null
                ? reader.ResolveDict(_page.Dict.Get("Resources"))
                : XFormResourcesDict();
            if (resources is null) return new XFormCollection(new Core.PdfDictionary(), reader);
            var xobjects = reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is null) return new XFormCollection(new Core.PdfDictionary(), reader);
            return new XFormCollection(xobjects, reader);
        }
    }
}

/// <summary>
/// Type alias for PageResources, matching the Resources class name.
/// </summary>
public class Resources : PageResources
{
    internal Resources(Page page) : base(page) { }

    /// <summary>XForm-backed resources accessor: resolves Font / Image /
    /// XObject entries from an XForm's stream dictionary rather than a
    /// page dictionary.</summary>
    internal Resources(XForm xform) : base(xform) { }

    /// <summary>Resource-dictionary-backed accessor (e.g. the AcroForm /DR),
    /// which carries /Font etc. directly.</summary>
    internal Resources(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Font resources (re-declared so reflection sees the member on Resources).</summary>
    public new FontCollection Fonts => base.Fonts;

    /// <summary>Image resources (re-declared so reflection sees the member on Resources).</summary>
    public new XImageCollection Images => base.Images;

    /// <summary>XForm resources (re-declared so reflection sees the member on Resources).</summary>
    public new XFormCollection Forms => base.Forms;

    /// <summary>Font collection accessor — <paramref name="CreateIfAbsent"/> is honoured by
    /// matching Aspose.PDF for .NET-shape semantics; FOSS always returns a live collection.</summary>
    public FontCollection GetFonts(bool CreateIfAbsent) { _ = CreateIfAbsent; return base.Fonts; }

    /// <summary>Enumerate every /ExtGState entry on this page's resources as a name→value map.</summary>
    public System.Collections.Generic.Dictionary<string, ExtGStateValue> GetExtGStates()
        => new();

    /// <summary>Free resource-cache memory. No-op in this build — the
    /// FOSS resource readers don't cache decoded bytes.</summary>
    public void FreeMemory() { }

    /// <summary>One /ExtGState entry — name plus stroke/fill alpha factors.</summary>
    public class ExtGStateValue
    {
        public ExtGStateValue(string name) { Name = name; }

        /// <summary>Resource name (e.g. "GS1").</summary>
        public string Name { get; }

        /// <summary>Stroking alpha constant (CA).</summary>
        public double CA { get; internal set; } = 1.0;

        /// <summary>Non-stroking alpha constant (ca).</summary>
        public double ca { get; internal set; } = 1.0;
    }
}

/// <summary>
/// Represents a Form XObject (reusable content stream with its own resources).
/// </summary>
public sealed class XForm
{
    private readonly Core.PdfStream _stream;
    private readonly IO.PdfReader _reader;
    private OperatorCollection? _contents;

    internal XForm(Core.PdfStream stream, IO.PdfReader reader)
    {
        _stream = stream;
        _reader = reader;
    }

    /// <summary>The name of this XForm in the page's XObject resources dict.</summary>
    public string? Name { get; set; }

    /// <summary>The Form XObject's content stream as a typed operator collection.
    /// Lazy: parses on first access and caches.</summary>
    public OperatorCollection Contents
        => _contents ??= new OperatorCollection(() => _reader.DecodeStream(_stream));

    /// <summary>The raw decoded content bytes of this XForm. Use
    /// <see cref="Contents"/> for typed-operator iteration.</summary>
    public byte[] DecodedBytes => _reader.DecodeStream(_stream);

    /// <summary>Replace this form's content with raw (decoded) bytes, dropping the
    /// existing filter so the writer re-compresses on save. Used by the text-edit
    /// path when a fragment extracted from this form has its Text changed/removed.</summary>
    internal void SetDecodedContent(byte[] data)
    {
        _stream.Dict.Remove("Filter");
        _stream.Dict.Remove("DecodeParms");
        _stream.ReplaceData(data);
        _contents = null;
    }

    /// <summary>Internal reader for object resolution.</summary>
    internal IO.PdfReader Reader => _reader;

    /// <summary>The XForm's stream dictionary (contains Resources, BBox, etc.).</summary>
    internal Core.PdfDictionary StreamDict => _stream.Dict;

    /// <summary>The bounding box of this XForm. /BBox PDF entry.</summary>
    public Rectangle? BBox
    {
        get
        {
            var arr = _reader.Resolve(_stream.Dict.Get("BBox")) as Core.PdfArray;
            if (arr is null || arr.Count < 4) return null;
            double getN(int i) => arr[i] switch
            {
                Core.PdfInteger pi => pi.Value,
                Core.PdfReal pr => pr.Value,
                _ => 0
            };
            return new Rectangle(getN(0), getN(1), getN(2), getN(3));
        }
        set
        {
            if (value is null) { _stream.Dict.Remove("BBox"); return; }
            var arr = new Core.PdfArray();
            arr.Add(new Core.PdfReal(value.LLX));
            arr.Add(new Core.PdfReal(value.LLY));
            arr.Add(new Core.PdfReal(value.URX));
            arr.Add(new Core.PdfReal(value.URY));
            _stream.Dict.Set("BBox", arr);
        }
    }

    /// <summary>Alias for <see cref="BBox"/>; Aspose.PDF for .NET exposes both names.</summary>
    public Rectangle Rectangle => BBox ?? new Rectangle(0, 0, 0, 0);

    /// <summary>The XObject Subtype (always "Form" for XForm instances).</summary>
    public string Subtype => _stream.Dict.GetName("Subtype") ?? "Form";

    /// <summary>The Form's /Matrix entry (the transformation applied
    /// when the form is painted). Identity matrix when absent.</summary>
    public Matrix Matrix
    {
        get
        {
            var arr = _reader.Resolve(_stream.Dict.Get("Matrix")) as Core.PdfArray;
            if (arr is null || arr.Count < 6) return new Matrix(1, 0, 0, 1, 0, 0);
            double getN(int i) => arr[i] switch
            {
                Core.PdfInteger pi => pi.Value,
                Core.PdfReal pr => pr.Value,
                _ => 0
            };
            return new Matrix(getN(0), getN(1), getN(2), getN(3), getN(4), getN(5));
        }
        set
        {
            if (value is null) { _stream.Dict.Remove("Matrix"); return; }
            var arr = new Core.PdfArray();
            arr.Add(new Core.PdfReal(value.A));
            arr.Add(new Core.PdfReal(value.B));
            arr.Add(new Core.PdfReal(value.C));
            arr.Add(new Core.PdfReal(value.D));
            arr.Add(new Core.PdfReal(value.E));
            arr.Add(new Core.PdfReal(value.F));
            _stream.Dict.Set("Matrix", arr);
        }
    }

    /// <summary>The Form's /Intent (IT) entry; null when absent.</summary>
    public string? IT => _stream.Dict.GetName("IT");

    /// <summary>Open Prepress Interface (OPI) wrapper. Always non-null;
    /// the underlying /OPI entry may be absent (in which case the wrapper
    /// reports defaults).</summary>
    public Opi Opi => new Opi(this);

    /// <summary>Form XObject resources (fonts / images / nested XObjects)
    /// declared on this XForm's stream dict. Aspose.Pdf.Resources-typed
    /// to match Aspose.PDF for .NET; backed by the XForm-aware
    /// <see cref="PageResources(XForm)"/> ctor.</summary>
    public Resources Resources => new Resources(this);

    /// <summary>Method-style resources accessor, parity with the .NET API.</summary>
    public Resources GetResources() => Resources;

    /// <summary>Method-style resources accessor with create-on-demand.
    /// The FOSS Resources are always materialisable, so the
    /// <paramref name="allowCreate"/> flag is ignored.</summary>
    public Resources GetResources(bool allowCreate) { _ = allowCreate; return Resources; }

    /// <summary>Releases resources held by this XForm. Currently a no-op
    /// — the FOSS XForm reader holds no unmanaged buffers.</summary>
    public void Dispose() { _contents = null; }

    /// <summary>Free decoded-content cache. The FOSS XForm decodes on
    /// demand, so this clears the cached OperatorCollection only.</summary>
    public void FreeMemory() { _contents = null; }

    /// <summary>Construct a new XForm from a source page's content stream
    /// and register it on <paramref name="document"/>. Stored only — the
    /// FOSS XForm-from-page pipeline isn't fully wired; the returned
    /// XForm wraps a freshly created stream with the source page's BBox
    /// and the page's content bytes.</summary>
    public static XForm CreateNewForm(Page source, Document document)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (document is null) throw new ArgumentNullException(nameof(document));
        // Best-effort: build a /Form XObject stream from the source page's
        // /MediaBox + content bytes. Mostly stored: this XForm does not
        // currently get registered in document.Pages or any resource dict.
        var dict = new Core.PdfDictionary();
        dict.Set("Type", new Core.PdfName("XObject"));
        dict.Set("Subtype", new Core.PdfName("Form"));
        var rect = source.Rect ?? new Rectangle(0, 0, 612, 792);
        var bbox = new Core.PdfArray();
        bbox.Add(new Core.PdfReal(rect.LLX));
        bbox.Add(new Core.PdfReal(rect.LLY));
        bbox.Add(new Core.PdfReal(rect.URX));
        bbox.Add(new Core.PdfReal(rect.URY));
        dict.Set("BBox", bbox);
        var stream = new Core.PdfStream(dict, Array.Empty<byte>());
        return new XForm(stream, source.Reader);
    }
}

/// <summary>Open Prepress Interface (OPI) metadata wrapper for an
/// <see cref="XForm"/>. Stored only — the FOSS write path doesn't emit
/// /OPI entries.</summary>
public sealed class Opi
{
    private readonly XForm _xform;

    /// <summary>Construct an OPI wrapper bound to <paramref name="xform"/>.</summary>
    public Opi(XForm xform) { _xform = xform ?? throw new ArgumentNullException(nameof(xform)); }

    /// <summary>OPI dictionary version (1.3 / 2.0 / …). Empty when absent.</summary>
    public string Version => string.Empty;

    /// <summary>External file specification referenced by the OPI entry.</summary>
    public string FileSpecification => string.Empty;

    /// <summary>OPI cropping/positioning rectangle as 4 PDF points.</summary>
    public double[] Position => Array.Empty<double>();
}

/// <summary>Resources (fonts, xobjects) on an XForm's stream dict.</summary>
public sealed class XFormResources
{
    private readonly Core.PdfDictionary _streamDict;
    private readonly IO.PdfReader _reader;

    internal XFormResources(Core.PdfDictionary streamDict, IO.PdfReader reader)
    {
        _streamDict = streamDict;
        _reader = reader;
    }

    /// <summary>Fonts in this XForm's resources dict (null if none).</summary>
    public FontCollection? Fonts
    {
        get
        {
            var resources = _reader.ResolveDict(_streamDict.Get("Resources"));
            if (resources is null) return null;
            var fontDict = _reader.ResolveDict(resources.Get("Font"));
            if (fontDict is null) return null;
            return new FontCollection(_streamDict, _reader);
        }
    }

    /// <summary>XForm (Form XObject) resources on this XForm.</summary>
    public XFormCollection Forms
    {
        get
        {
            var resources = _reader.ResolveDict(_streamDict.Get("Resources"));
            if (resources is null) return new XFormCollection(new Core.PdfDictionary(), _reader);
            var xobjects = _reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is null) return new XFormCollection(new Core.PdfDictionary(), _reader);
            return new XFormCollection(xobjects, _reader);
        }
    }
}

/// <summary>
/// Collection of XForm (Form XObject) resources on a page.
/// Indexed by name (string key in the XObject resources dictionary).
/// </summary>
public sealed class XFormCollection : IEnumerable<XForm>
{
    private readonly Core.PdfDictionary _xobjects;
    private readonly IO.PdfReader _reader;
    private List<XForm>? _forms;

    internal XFormCollection(Core.PdfDictionary xobjects, IO.PdfReader reader)
    {
        _xobjects = xobjects;
        _reader = reader;
    }

    /// <summary>Number of Form XObjects.</summary>
    public int Count
    {
        get
        {
            EnsureForms();
            return _forms!.Count;
        }
    }

    /// <summary>Get XForm by 1-based index.</summary>
    public XForm this[int index]
    {
        get
        {
            EnsureForms();
            if (index < 1 || index > _forms!.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _forms[index - 1];
        }
    }

    /// <summary>Get XForm by name.</summary>
    public XForm? this[string name]
    {
        get
        {
            var obj = _reader.ResolveStream(_xobjects.Get(name));
            if (obj is null) return null;
            if (obj.Dict.GetName("Subtype") != "Form") return null;
            return new XForm(obj, _reader) { Name = name };
        }
    }

    /// <summary>Remove an XForm by name from the collection and underlying XObject dict.</summary>
    public void Delete(string name)
    {
        _xobjects.Remove(name);
        if (_forms is not null)
            _forms.RemoveAll(f => f.Name == name);
    }

    /// <summary>Remove an XForm by 1-based index. Resolves to the underlying name then defers to Delete(string).</summary>
    public void Delete(int index)
    {
        EnsureForms();
        if (index < 1 || index > _forms!.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var name = _forms[index - 1].Name;
        if (name is not null) Delete(name);
    }

    public IEnumerator<XForm> GetEnumerator()
    {
        EnsureForms();
        return _forms!.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public void Add(XForm item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        EnsureForms();
        var name = item.Name ?? $"Fm{_forms!.Count + 1}";
        item.Name = name;
        _forms!.Add(item);
    }

    public void Clear()
    {
        EnsureForms();
        foreach (var f in _forms!.ToList())
            if (f.Name is { } n) _xobjects.Remove(n);
        _forms!.Clear();
    }

    public bool Contains(XForm item)
    {
        EnsureForms();
        return _forms!.Contains(item);
    }

    public void CopyTo(XForm[] array, int index)
    {
        EnsureForms();
        _forms!.CopyTo(array, index);
    }

    public bool Remove(XForm item)
    {
        EnsureForms();
        if (item?.Name is { } n) _xobjects.Remove(n);
        return _forms!.Remove(item!);
    }

    /// <summary>Drop all entries (equivalent to <see cref="Clear"/>).</summary>
    public void Delete() => Clear();

    /// <summary>Discard cached form list so the next access re-reads from the XObject dict.</summary>
    public void FreeMemory() => _forms = null;

    /// <summary>Return the PDF resource name (e.g. "Fm1") under which a Form XObject lives.</summary>
    public string GetFormName(XForm form) => form?.Name ?? string.Empty;

    private void EnsureForms()
    {
        if (_forms is not null) return;
        _forms = new List<XForm>();
        foreach (var key in _xobjects.Keys)
        {
            var stream = _reader.ResolveStream(_xobjects.Get(key));
            if (stream is null) continue;
            if (stream.Dict.GetName("Subtype") != "Form") continue;
            _forms.Add(new XForm(stream, _reader) { Name = key });
        }
    }
}

/// <summary>
/// Standalone operator-list class — surface mirrors <see cref="OperatorCollection"/>
/// but is detached from any page. Used by callers that want to construct operators
/// outside the page-binding flow (e.g. TextBuilder before-page-binding).
/// </summary>
public class BaseOperatorCollection : System.Collections.Generic.IEnumerable<Operator>
{
    private readonly List<Operator> _ops = new();

    public int Count => _ops.Count;

    public bool IsReadOnly => false;

    /// <summary>Whether the absorber is operating in fast-text-extraction mode (stored only).</summary>
    public bool IsFastTextExtractionMode { get; internal set; }

    // Operator access is 1-based: index 1 is the first operator. Matches the
    // public collection convention used across the form/content APIs.
    public Operator this[int index]
    {
        get => _ops[index - 1];
        set => _ops[index - 1] = value;
    }

    public void Add(Operator op) => _ops.Add(op);
    public void Clear() => _ops.Clear();
    public bool Contains(Operator item) => _ops.Contains(item);
    public void CopyTo(Operator[] array, int index) => _ops.CopyTo(array, index);
    public void Insert(int index, Operator op) => _ops.Insert(index, op);
    public bool Remove(Operator item) => _ops.Remove(item);

    /// <summary>Suspend any deferred-update bookkeeping. No-op.</summary>
    public void SuppressUpdate() { }
    /// <summary>Resume deferred-update bookkeeping. No-op.</summary>
    public void ResumeUpdate() { }
    /// <summary>Cancel any pending deferred update. No-op.</summary>
    public void CancelUpdate() { }

    public System.Collections.Generic.IEnumerator<Operator> GetEnumerator() => _ops.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Collection of content stream operators for a page.
/// Operators added here are appended to the page's content stream on save.
/// </summary>
public sealed class OperatorCollection : IEnumerable<Operator>, IDisposable
{
    private readonly Page? _page;
    private readonly Func<byte[]>? _bytesProvider;
    private readonly List<Operator> _operators = [];
    private List<string>? _parsed;
    private bool _suppressed;
    private bool _materialized;

    internal OperatorCollection(Page page) => _page = page;

    /// <summary>Backed by an arbitrary content-bytes producer (e.g. a
    /// Form XObject's decoded /Contents stream). Used when the operators
    /// don't live on a <see cref="Page"/> — Field.NormalAppearance,
    /// XForm.Operators, etc.</summary>
    internal OperatorCollection(Func<byte[]> bytesProvider) => _bytesProvider = bytesProvider;

    /// <summary>Aspose.PDF for .NET alias that returns this collection itself
    /// (callers do <c>page.Contents.Commands[i]</c>).</summary>
    public OperatorCollection Commands => this;

    /// <summary>Add an operator to the collection.</summary>
    public void Add(Operator op) { Materialize(); _operators.Add(op); }

    /// <summary>Add several operators in one call.</summary>
    public void Add(Operator[] ops)
    {
        if (ops is null) return;
        Materialize();
        foreach (var op in ops) _operators.Add(op);
    }

    /// <summary>Add several operators from any collection in one call.</summary>
    public void Add(System.Collections.Generic.ICollection<Operator> ops)
    {
        if (ops is null) return;
        Materialize();
        foreach (var op in ops) _operators.Add(op);
    }

    /// <summary>Visit every operator with the given selector. Materialises the
    /// collection first so the operators handed to the visitor are the same
    /// stable instances held by this collection — a selector that collects them
    /// (e.g. <see cref="OperatorSelector.Selected"/>) can then be passed back to
    /// <see cref="Delete(System.Collections.Generic.IList{Operator})"/> and the
    /// reference-equality removal will find them. Each operator dispatches to the
    /// matching typed <c>Visit</c> overload via its own <see cref="Operator.Accept"/>.</summary>
    public void Accept(IOperatorSelector visitor)
    {
        if (visitor is null) return;
        Materialize();
        foreach (var op in _operators)
            op.Accept(visitor);
    }

    /// <summary>Cancel a suppressed-update window (started by
    /// <see cref="SuppressUpdate"/>) without flushing pending changes.
    /// No-op in this build because mutations are already deferred to save.</summary>
    public void CancelUpdate() { _suppressed = false; }

    /// <summary>Remove every operator from the collection.</summary>
    public void Clear()
    {
        _operators.Clear();
        _parsed?.Clear();
    }

    /// <summary>True when <paramref name="op"/> is currently in the collection.</summary>
    public bool Contains(Operator op)
        => op is not null && _operators.Contains(op);

    /// <summary>Copy the live operators (the in-memory mutable list, not the
    /// parsed cache) into <paramref name="array"/> starting at <paramref name="index"/>.</summary>
    public void CopyTo(Operator[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        _operators.CopyTo(array, index);
    }

    /// <summary>Remove every occurrence of each operator in <paramref name="ops"/>.</summary>
    public void Delete(Operator[] ops)
    {
        if (ops is null) return;
        Materialize();
        foreach (var op in ops) _operators.Remove(op);
    }

    /// <summary>Remove every operator in <paramref name="list"/>.</summary>
    public void Delete(System.Collections.Generic.IList<Operator> list)
    {
        if (list is null) return;
        Materialize();
        foreach (var op in list) _operators.Remove(op);
    }

    /// <summary>Releases resources held by the collection. Currently a no-op —
    /// operators are pure value objects in this build.</summary>
    public void Dispose() { _operators.Clear(); _parsed?.Clear(); _ = _suppressed; }

    /// <summary>Insert one operator at the given 1-based index.</summary>
    public void Insert(int index, Operator op)
    {
        if (op is null) return;
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        Materialize();
        _operators.Insert(Math.Min(index - 1, _operators.Count), op);
    }

    /// <summary>Insert several operators at <paramref name="at"/> (1-based).</summary>
    public void Insert(int at, Operator[] ops)
    {
        if (ops is null) return;
        if (at < 1) throw new ArgumentOutOfRangeException(nameof(at));
        Materialize();
        _operators.InsertRange(Math.Min(at - 1, _operators.Count), ops);
    }

    /// <summary>Insert several operators (any IList) at <paramref name="at"/> (1-based).</summary>
    public void Insert(int at, System.Collections.Generic.IList<Operator> ops)
    {
        if (ops is null) return;
        if (at < 1) throw new ArgumentOutOfRangeException(nameof(at));
        Materialize();
        _operators.InsertRange(Math.Min(at - 1, _operators.Count), ops);
    }

    /// <summary>Whether the absorber/parser is in fast-text-extraction mode
    /// (no glyph-width metrics, character-position approximations only).
    /// Always false in this build — we always parse precisely.</summary>
    public bool IsFastTextExtractionMode => false;

    /// <summary>Always false: callers may add and remove operators.</summary>
    public bool IsReadOnly => false;

    /// <summary>Remove the first occurrence of <paramref name="op"/>; returns
    /// true when an operator was removed.</summary>
    public bool Remove(Operator op)
        => op is not null && _operators.Remove(op);

    /// <summary>Replace operators in place: each operator in
    /// <paramref name="operators"/> overwrites the existing operator at its
    /// 1-based <see cref="Operator.Index"/>. Operators whose index falls
    /// outside the current range are ignored.</summary>
    public void Replace(System.Collections.Generic.IList<Operator> operators)
    {
        if (operators is null) return;
        Materialize();
        foreach (var op in operators)
        {
            if (op is null) continue;
            if (op.Index >= 1 && op.Index <= _operators.Count)
                _operators[op.Index - 1] = op;
        }
    }

    /// <summary>
    /// Suspend automatic content-stream re-serialization while a batch of
    /// operator mutations is performed. Paired with <see cref="ResumeUpdate()"/>.
    /// The implementation works in-memory and re-serializes lazily on save, so
    /// this is a no-op kept for public API parity.
    /// </summary>
    public void SuppressUpdate() { _suppressed = true; }

    /// <summary>Resume automatic content-stream re-serialization. See <see cref="SuppressUpdate"/>.</summary>
    public void ResumeUpdate() { _suppressed = false; }

    /// <summary>Resume automatic re-serialization with optional full-flush
    /// semantics (the <paramref name="updateAll"/> flag is stored only).</summary>
    public void ResumeUpdate(bool updateAll) { _suppressed = false; _ = updateAll; }

    /// <summary>Persist pending operator mutations to the backing content stream.
    /// In this build the collection re-serializes its operators lazily on save, so
    /// this is a no-op kept for public API parity (mirrors <see cref="SuppressUpdate"/>
    /// / <see cref="ResumeUpdate()"/> / <see cref="CancelUpdate"/>).</summary>
    public void UpdateData() { }

    /// <summary>
    /// Number of operators in the page content stream.
    /// Parses the content stream on first access.
    /// </summary>
    public int Count
    {
        get
        {
            if (_operators.Count > 0) return _operators.Count;
            EnsureParsed();
            return _parsed!.Count;
        }
    }

    /// <summary>Access (or replace) operator at 1-based index. Returns a
    /// typed <see cref="Operator"/> subclass (BT, ET, GSave, GRestore,
    /// SelectFont, SetRGBColor, MoveTo, LineTo, …) when the operator name
    /// is recognised; otherwise a <see cref="RawOperator"/> wrapping the
    /// original token. The setter overwrites the in-memory operator at the
    /// given position.</summary>
    public Operator this[int index]
    {
        get
        {
            Materialize();
            if (index < 1 || index > _operators.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _operators[index - 1];
        }
        set
        {
            Materialize();
            if (index < 1 || index > _operators.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _operators[index - 1] = value;
        }
    }

    /// <summary>Promote the parsed content into the live <see cref="_operators"/>
    /// list so callers receive stable operator instances whose mutations persist.
    /// Marks the list as representing the full content, so <see cref="FlushToPage"/>
    /// replaces (rather than appends to) the page stream on save. A no-op once the
    /// list already holds operators — whether materialised here or added directly.</summary>
    private void Materialize()
    {
        if (_materialized || _operators.Count > 0) return;
        EnsureParsed();
        foreach (var s in _parsed!)
            _operators.Add(TypedOperatorParser.Parse(s));
        _materialized = true;
    }

    /// <summary>Remove operator at the given 1-based index.</summary>
    public void Delete(int index)
    {
        Materialize();
        if (index < 1 || index > _operators.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _operators.RemoveAt(index - 1);
    }

    /// <summary>Enumerate all operators in the content stream as typed
    /// <see cref="Operator"/> instances (with <see cref="RawOperator"/>
    /// fallback for unrecognised commands).</summary>
    public IEnumerator<Operator> GetEnumerator()
    {
        if (_operators.Count > 0)
        {
            foreach (var op in _operators) yield return op;
            yield break;
        }
        EnsureParsed();
        for (int i = 0; i < _parsed!.Count; i++)
            yield return TypedOperatorParser.Parse(_parsed[i]);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns all operators as a single string.</summary>
    public override string ToString()
    {
        EnsureParsed();
        return string.Join("\n", _parsed!);
    }

    private void EnsureParsed()
    {
        if (_parsed is not null) return;
        _parsed = [];
        var bytes = GetContentBytes();
        if (bytes.Length == 0) return;
        _parsed = ContentStreamOperatorParser.ParseOperators(bytes);
    }

    private byte[] GetContentBytes()
    {
        if (_bytesProvider is not null) return _bytesProvider() ?? [];
        if (_page is null) return [];
        var contentsObj = _page.Reader.Resolve(_page.Dict.Get("Contents"));
        if (contentsObj is Core.PdfStream stream)
            return _page.Reader.DecodeStream(stream);
        if (contentsObj is Core.PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = _page.Reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = _page.Reader.DecodeStream(s);
                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    /// <summary>Serialize all operators and append to the page's content stream.
    /// No-op for non-page-backed instances (Field.NormalAppearance etc.).</summary>
    internal void FlushToPage()
    {
        if (_page is null) return;
        // Materialised operators are the page's complete content (the caller read
        // or edited existing operators), so the stream is replaced. Non-materialised
        // operators were added on top of existing content, so they are appended.
        if (!_materialized && _operators.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var op in _operators)
        {
            sb.Append(op.ToPdf());
            sb.Append('\n');
        }
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        if (_materialized)
            _page.SetContentStream(bytes);
        else
            _page.AppendContentBytes(bytes);
        _operators.Clear();
        _parsed = null;
        _materialized = false;
    }

    /// <summary>Invalidate cached parse results (after content stream modification).</summary>
    internal void InvalidateCache() => _parsed = null;
}

/// <summary>
/// Buffers operators to prepend to / append to a page's content stream. Operators added
/// via <see cref="AppendToBegin"/> are inserted (in call order) before the existing
/// content; those added via <see cref="AppendToEnd"/> are appended after it. Nothing is
/// applied until <see cref="UpdateData"/> is called. Typical use is wrapping a page's
/// content in a q…Q graphics-state save/restore pair before drawing extra overlay content.
/// </summary>
public sealed class ContentsAppender
{
    private readonly Page _page;
    private readonly System.Collections.Generic.List<Aspose.Pdf.Operator> _begin = new();
    private readonly System.Collections.Generic.List<Aspose.Pdf.Operator> _end = new();

    internal ContentsAppender(Page page) => _page = page;

    /// <summary>Queue an operator to be inserted before the existing page content.</summary>
    public void AppendToBegin(Aspose.Pdf.Operator op)
    {
        if (op is not null) _begin.Add(op);
    }

    /// <summary>Queue an operator to be appended after the existing page content.</summary>
    public void AppendToEnd(Aspose.Pdf.Operator op)
    {
        if (op is not null) _end.Add(op);
    }

    /// <summary>Apply the queued begin/end operators to the page's content stream.</summary>
    public void UpdateData()
    {
        var contents = _page.Contents;
        if (_begin.Count > 0)
            contents.Insert(1, _begin); // 1-based insert at the front, preserving call order
        foreach (var op in _end)
            contents.Add(op);
        _begin.Clear();
        _end.Clear();
    }
}

/// <summary>An unparsed operator token from a content stream — used as a fallback
/// for operators not covered by the typed <see cref="Aspose.Pdf.Operators"/>
/// hierarchy. Inherits <see cref="Aspose.Pdf.Operators.Operator"/> so that
/// <see cref="OperatorCollection"/> can yield a uniform typed sequence.</summary>
public sealed class RawOperator : Aspose.Pdf.Operators.Operator
{
    private readonly string _text;

    internal RawOperator(string text) => _text = text;

    /// <summary>The operator command name (last token).</summary>
    public override string CommandName
    {
        get
        {
            var trimmed = _text.TrimEnd();
            var lastSpace = trimmed.LastIndexOf(' ');
            return lastSpace >= 0 ? trimmed[(lastSpace + 1)..] : trimmed;
        }
    }

    /// <inheritdoc />
    public override string ToPdf() => _text;
}

/// <summary>
/// Dispatches a parsed operator token (e.g. <c>"BT"</c>, <c>"0.5 0.3 0.1 rg"</c>,
/// <c>"/F1 12 Tf"</c>) to the matching typed <see cref="Operator"/> subclass,
/// falling back to <see cref="RawOperator"/> for commands we don't yet model
/// or whose operands fail to parse cleanly.
/// </summary>
internal static class TypedOperatorParser
{
    internal static Operator Parse(string text)
    {
        var trimmed = text.TrimEnd();
        var lastSpace = trimmed.LastIndexOf(' ');
        var op = lastSpace >= 0 ? trimmed[(lastSpace + 1)..] : trimmed;
        var operandText = lastSpace >= 0 ? trimmed[..lastSpace] : "";

        try
        {
            switch (op)
            {
                case "q":  return new Aspose.Pdf.Operators.GSave();
                case "Q":  return new Aspose.Pdf.Operators.GRestore();
                case "BT": return new Aspose.Pdf.Operators.BT();
                case "ET": return new Aspose.Pdf.Operators.ET();
                case "W":  return new Aspose.Pdf.Operators.Clip();
                case "W*": return new Aspose.Pdf.Operators.EOClip();
                case "EMC": return new Aspose.Pdf.Operators.EMC();
                case "T*": return new Aspose.Pdf.Operators.MoveToNextLine();
                case "Tf":
                {
                    // /Name size Tf
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && ops[0].StartsWith('/')
                        && double.TryParse(ops[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var size))
                        return new Aspose.Pdf.Operators.SelectFont(ops[0][1..], size);
                    break;
                }
                case "rg":
                case "RG":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 3
                        && TryD(ops[0], out var r) && TryD(ops[1], out var g) && TryD(ops[2], out var b))
                        return new Aspose.Pdf.Operators.SetRGBColor(r, g, b);
                    break;
                }
                case "Td":
                case "TD":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && TryD(ops[0], out var x) && TryD(ops[1], out var y))
                        return op == "Td"
                            ? new Aspose.Pdf.Operators.MoveTextPosition(x, y)
                            : new Aspose.Pdf.Operators.MoveTextPositionSetLeading(x, y);
                    break;
                }
                case "Tm":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 6
                        && TryD(ops[0], out var a) && TryD(ops[1], out var b)
                        && TryD(ops[2], out var c) && TryD(ops[3], out var d)
                        && TryD(ops[4], out var e) && TryD(ops[5], out var f))
                        return new Aspose.Pdf.Operators.SetTextMatrix(a, b, c, d, e, f);
                    break;
                }
                case "cm":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 6
                        && TryD(ops[0], out var a) && TryD(ops[1], out var b)
                        && TryD(ops[2], out var c) && TryD(ops[3], out var d)
                        && TryD(ops[4], out var e) && TryD(ops[5], out var f))
                        return new Aspose.Pdf.Operators.ConcatenateMatrix(a, b, c, d, e, f);
                    break;
                }
                case "Do":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && ops[0].StartsWith('/'))
                        return new Aspose.Pdf.Operators.Do(ops[0][1..]);
                    break;
                }
                case "gs":
                {
                    // /Name gs — apply parameters from a named ExtGState resource.
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && ops[0].StartsWith('/'))
                        return new Aspose.Pdf.Operators.GS(ops[0][1..]);
                    break;
                }
                case "Tj":
                {
                    // (text) Tj — operand is a single literal/hex string
                    var s = ParseSingleStringOperand(operandText);
                    if (s is not null) return new Aspose.Pdf.Operators.ShowText(s);
                    break;
                }
                case "'":
                {
                    // (text) '
                    var s = ParseSingleStringOperand(operandText);
                    if (s is not null) return new Aspose.Pdf.Operators.MoveToNextLineShowText(s);
                    break;
                }
                case "\"":
                {
                    // wordSpace charSpace (text) "
                    var lastParenStart = operandText.LastIndexOf('(');
                    var lastParenEnd = operandText.LastIndexOf(')');
                    if (lastParenStart >= 0 && lastParenEnd > lastParenStart)
                    {
                        var nums = SplitOperands(operandText[..lastParenStart]);
                        var s = ParseSingleStringOperand(operandText[lastParenStart..(lastParenEnd + 1)]);
                        if (s is not null && nums.Length == 2
                            && TryD(nums[0], out var ws) && TryD(nums[1], out var cs))
                            return new Aspose.Pdf.Operators.SetSpacingMoveToNextLineShowText(ws, cs, s);
                    }
                    break;
                }
                case "TJ":
                {
                    // [ (str) num (str) num ... ] TJ — array of strings + numeric kerning
                    var items = ParseTJArrayOperands(operandText);
                    if (items is not null) return new Aspose.Pdf.Operators.SetGlyphsPositionShowText(items);
                    break;
                }
                case "i":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var flat))
                        return new Aspose.Pdf.Operators.SetFlat(flat);
                    break;
                }
                case "m":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && TryD(ops[0], out var x) && TryD(ops[1], out var y))
                        return new Aspose.Pdf.Operators.MoveTo(x, y);
                    break;
                }
                case "l":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 2 && TryD(ops[0], out var x) && TryD(ops[1], out var y))
                        return new Aspose.Pdf.Operators.LineTo(x, y);
                    break;
                }
                case "re":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 4 && TryD(ops[0], out var x) && TryD(ops[1], out var y)
                        && TryD(ops[2], out var rw) && TryD(ops[3], out var rh))
                        return new Aspose.Pdf.Operators.Re(x, y, rw, rh);
                    break;
                }
                case "w":
                {
                    var ops = SplitOperands(operandText);
                    if (ops.Length == 1 && TryD(ops[0], out var lw))
                        return new Aspose.Pdf.Operators.SetLineWidth(lw);
                    break;
                }
                // Path-painting and path-close operators (no operands).
                case "h":  return new Aspose.Pdf.Operators.ClosePath();
                case "S":  return new Aspose.Pdf.Operators.Stroke();
                case "s":  return new Aspose.Pdf.Operators.ClosePathStroke();
                case "f":
                case "F":  return new Aspose.Pdf.Operators.Fill();
                case "f*": return new Aspose.Pdf.Operators.EOFill();
                case "B":  return new Aspose.Pdf.Operators.FillStroke();
                case "B*": return new Aspose.Pdf.Operators.EOFillStroke();
                case "b":  return new Aspose.Pdf.Operators.ClosePathFillStroke();
                case "b*": return new Aspose.Pdf.Operators.ClosePathEOFillStroke();
                case "n":  return new Aspose.Pdf.Operators.EndPath();
            }
        }
        catch
        {
            // Operand parse failed — fall through to RawOperator.
        }

        return new RawOperator(text);
    }

    private static bool TryD(string s, out double v) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>Parse a single literal `(text)` or hex `&lt;...&gt;` operand.
    /// Returns null on parse failure; PDF escape sequences inside literal
    /// strings (\\, \(, \)) are unescaped.</summary>
    private static string? ParseSingleStringOperand(string s)
    {
        s = s.Trim();
        if (s.StartsWith('(') && s.EndsWith(')'))
        {
            var body = s.Substring(1, s.Length - 2);
            var sb = new System.Text.StringBuilder(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '\\' && i + 1 < body.Length)
                {
                    sb.Append(body[++i]);
                }
                else sb.Append(body[i]);
            }
            return sb.ToString();
        }
        if (s.StartsWith('<') && s.EndsWith('>'))
        {
            var hex = s.Substring(1, s.Length - 2).Replace(" ", "");
            if (hex.Length % 2 != 0) hex += "0";
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return System.Text.Encoding.Latin1.GetString(bytes);
        }
        return null;
    }

    /// <summary>Parse a `[ (str) num (str) num ... ]` TJ-array operand into
    /// the mixed object[] expected by SetGlyphsPositionShowText.Items.</summary>
    private static object[]? ParseTJArrayOperands(string s)
    {
        s = s.Trim();
        if (!s.StartsWith('[') || !s.EndsWith(']')) return null;
        var inner = s.Substring(1, s.Length - 2);
        var items = new List<object>();
        int i = 0;
        while (i < inner.Length)
        {
            while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
            if (i >= inner.Length) break;
            if (inner[i] == '(')
            {
                int start = i;
                i++;
                while (i < inner.Length && inner[i] != ')')
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length) i += 2;
                    else i++;
                }
                if (i >= inner.Length) return null;
                i++; // past ')'
                var parsed = ParseSingleStringOperand(inner[start..i]);
                if (parsed is null) return null;
                items.Add(parsed);
            }
            else if (inner[i] == '<')
            {
                int start = i;
                while (i < inner.Length && inner[i] != '>') i++;
                if (i >= inner.Length) return null;
                i++;
                var parsed = ParseSingleStringOperand(inner[start..i]);
                if (parsed is null) return null;
                items.Add(parsed);
            }
            else
            {
                int start = i;
                while (i < inner.Length && !char.IsWhiteSpace(inner[i]) && inner[i] != '(' && inner[i] != '<') i++;
                var token = inner[start..i];
                if (TryD(token, out var d)) items.Add(d);
                else return null;
            }
        }
        return items.ToArray();
    }

    private static string[] SplitOperands(string s)
    {
        var trimmed = s.Trim();
        return trimmed.Length == 0
            ? Array.Empty<string>()
            : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>Parses PDF content stream bytes into individual operator strings.</summary>
internal static class ContentStreamOperatorParser
{
    internal static List<string> ParseOperators(byte[] data)
    {
        var result = new List<string>();
        var text = Encoding.Latin1.GetString(data);
        int pos = 0;
        int len = text.Length;
        var operands = new List<string>();

        while (pos < len)
        {
            SkipWhitespaceAndComments(text, ref pos, len);
            if (pos >= len) break;

            char c = text[pos];

            if (c == '(')
            {
                operands.Add(ReadParenString(text, ref pos, len));
            }
            else if (c == '<' && pos + 1 < len && text[pos + 1] == '<')
            {
                operands.Add(ReadDictionary(text, ref pos, len));
            }
            else if (c == '<')
            {
                operands.Add(ReadHexString(text, ref pos, len));
            }
            else if (c == '[')
            {
                operands.Add(ReadArray(text, ref pos, len));
            }
            else if (c == '/')
            {
                operands.Add(ReadName(text, ref pos, len));
            }
            else if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
            {
                operands.Add(ReadNumber(text, ref pos, len));
            }
            else if (c == 'B' && pos + 1 < len && text[pos + 1] == 'I' &&
                     (pos + 2 >= len || IsDelimiter(text[pos + 2])))
            {
                // BI . ID . EI — inline image: count as 3 operators (BI, ID, EI)
                // to match .NET Aspose.PDF for .NET OperatorCollection behavior
                var start = pos;
                var eiPos = FindInlineImageEnd(text, ref pos, len);
                var fullText = text[start..eiPos].TrimEnd();
                // BI with its key-value pairs
                var idIdx = fullText.IndexOf("\nID", StringComparison.Ordinal);
                if (idIdx < 0) idIdx = fullText.IndexOf(" ID", StringComparison.Ordinal);
                if (idIdx >= 0)
                {
                    result.Add(fullText[..idIdx].TrimEnd()); // BI + parameters
                    result.Add("ID"); // ID operator
                    result.Add("EI"); // EI operator
                }
                else
                {
                    result.Add(fullText);
                }
                operands.Clear();
            }
            else if (IsOperatorChar(c))
            {
                var opName = ReadOperatorName(text, ref pos, len);
                // Check for true/false/null which are operands
                if (opName == "true" || opName == "false" || opName == "null")
                {
                    operands.Add(opName);
                }
                else
                {
                    // Handle concatenated single-letter operators like "QQQQQ" (5× Q)
                    // Some corrupt PDFs omit whitespace between operators.
                    bool isConcatenated = opName.Length > 2 && !IsKnownOperator(opName)
                        && opName.All(ch => IsKnownSingleCharOp(ch));
                    if (isConcatenated)
                    {
                        // First operator gets any pending operands
                        if (operands.Count > 0)
                        {
                            result.Add(string.Join(" ", operands) + " " + opName[0]);
                            operands.Clear();
                        }
                        else
                        {
                            result.Add(opName[0].ToString());
                        }
                        // Remaining characters are individual operators
                        for (int ci = 1; ci < opName.Length; ci++)
                            result.Add(opName[ci].ToString());
                    }
                    else
                    {
                        // This is an operator — emit with operands
                        if (operands.Count > 0)
                        {
                            result.Add(string.Join(" ", operands) + " " + opName);
                            operands.Clear();
                        }
                        else
                        {
                            result.Add(opName);
                        }
                    }
                }
            }
            else
            {
                pos++; // skip unexpected chars
            }
        }

        return result;
    }

    private static bool IsOperatorChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '\'' || c == '"' || c == '*';

    private static bool IsKnownSingleCharOp(char c) =>
        c == 'q' || c == 'Q' || c == 'n' || c == 'f' || c == 'h' || c == 'W' || c == 'S' ||
        c == 'B' || c == 'b' || c == 's' || c == 'F';

    private static readonly HashSet<string> _knownOps = new(StringComparer.Ordinal)
    {
        "q","Q","cm","m","l","c","v","y","h","re","S","s","f","F","B","b","n","W",
        "BT","ET","Tf","Td","TD","Tm","TJ","Tj","TL","Tc","Tw","Tz","Tr","Ts",
        "d0","d1","CS","cs","SC","SCN","sc","scn","G","g","RG","rg","K","k",
        "gs","ri","i","Do","BI","ID","EI","sh","BX","EX","MP","DP","BMC","BDC","EMC",
        "w","J","j","M","d","T*","'","\"","W*","f*","b*","B*",
    };
    private static bool IsKnownOperator(string op) => _knownOps.Contains(op);

    private static bool IsDelimiter(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '(' || c == ')' ||
        c == '<' || c == '>' || c == '[' || c == ']' || c == '/' || c == '%';

    private static void SkipWhitespaceAndComments(string text, ref int pos, int len)
    {
        while (pos < len)
        {
            char c = text[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\0')
            {
                pos++;
            }
            else if (c == '%')
            {
                while (pos < len && text[pos] != '\n' && text[pos] != '\r') pos++;
            }
            else break;
        }
    }

    private static string ReadParenString(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip (
        int depth = 1;
        while (pos < len && depth > 0)
        {
            if (text[pos] == '\\') { pos += 2; continue; }
            if (text[pos] == '(') depth++;
            else if (text[pos] == ')') depth--;
            pos++;
        }
        return text[start..pos];
    }

    private static string ReadHexString(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip <
        while (pos < len && text[pos] != '>') pos++;
        if (pos < len) pos++; // skip >
        return text[start..pos];
    }

    private static string ReadDictionary(string text, ref int pos, int len)
    {
        int start = pos;
        pos += 2; // skip <<
        int depth = 1;
        while (pos < len && depth > 0)
        {
            if (pos + 1 < len && text[pos] == '<' && text[pos + 1] == '<') { depth++; pos += 2; }
            else if (pos + 1 < len && text[pos] == '>' && text[pos + 1] == '>') { depth--; pos += 2; }
            else pos++;
        }
        return text[start..pos];
    }

    private static string ReadArray(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip [
        int depth = 1;
        while (pos < len && depth > 0)
        {
            if (text[pos] == '[') depth++;
            else if (text[pos] == ']') depth--;
            pos++;
        }
        return text[start..pos];
    }

    private static string ReadName(string text, ref int pos, int len)
    {
        int start = pos;
        pos++; // skip /
        while (pos < len && !IsDelimiter(text[pos]) && text[pos] != '/' &&
               text[pos] != '(' && text[pos] != '<' && text[pos] != '[') pos++;
        return text[start..pos];
    }

    private static string ReadNumber(string text, ref int pos, int len)
    {
        int start = pos;
        if (text[pos] == '+' || text[pos] == '-') pos++;
        while (pos < len && ((text[pos] >= '0' && text[pos] <= '9') || text[pos] == '.')) pos++;
        return text[start..pos];
    }

    private static string ReadOperatorName(string text, ref int pos, int len)
    {
        int start = pos;
        while (pos < len && IsOperatorChar(text[pos])) pos++;
        return text[start..pos];
    }

    private static int FindInlineImageEnd(string text, ref int pos, int len)
    {
        // Skip past BI, then find ID, then find EI
        pos += 2; // skip BI
        // Find ID
        while (pos + 1 < len)
        {
            if (text[pos] == 'I' && text[pos + 1] == 'D' &&
                (pos == 0 || text[pos - 1] == ' ' || text[pos - 1] == '\n' || text[pos - 1] == '\r'))
            {
                pos += 2;
                if (pos < len && text[pos] == ' ') pos++; // skip single space after ID
                break;
            }
            pos++;
        }
        // Find EI — must be preceded by whitespace
        while (pos + 2 < len)
        {
            if ((text[pos] == '\n' || text[pos] == '\r' || text[pos] == ' ') &&
                text[pos + 1] == 'E' && text[pos + 2] == 'I' &&
                (pos + 3 >= len || IsDelimiter(text[pos + 3])))
            {
                pos += 3;
                return pos;
            }
            pos++;
        }
        pos = len;
        return pos;
    }
}
