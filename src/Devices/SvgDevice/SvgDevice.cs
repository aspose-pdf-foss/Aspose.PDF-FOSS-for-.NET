using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Converts a PDF page to SVG markup.
///
/// Output shape (SVG 1.1): all geometry is emitted flattened into top-down page
/// coordinates (no transform groups). Horizontal text runs are emitted as
/// <c>&lt;text x="x0 x1 …" y="y" style="fill:#rrggbb;…"&gt;</c> with one absolute x
/// position per glyph computed from the font's width table. Rotated/sheared
/// runs fall back to a
/// <c>transform="matrix(…)"</c> placement whose y-column is negated so the glyphs
/// render upright; SvgToPdfConverter negates it back on import.
/// </summary>
public sealed partial class SvgDevice
{
    /// <summary>Save options in force for this conversion (the custom
    /// embedded-image saving strategy lives here). Null for plain use.</summary>
    internal Aspose.Pdf.SvgSaveOptions? SaveOptions;

    /// <summary>Maps a 1-based page number to the file name its SVG is
    /// saved under, so internal GoTo links can point at the sibling page
    /// file. Null when pages aren't being saved as files.</summary>
    internal Func<int, string>? PageLinkTarget;

    // Numbers the images handed to the custom saving strategy across all
    // pages of one conversion.
    private int _imgCounter;

    /// <summary>
    /// Convert a page to SVG string.
    /// </summary>
    public string Process(Page page)
    {
        // The canvas is the VISIBLE page: a distinct /CropBox sizes the SVG and
        // anchors its coordinates (a cropped page round-trips at its
        // crop size; sizing from the MediaBox inflated the canvas by the cropped-away
        // margins and shifted every coordinate by the crop offset).
        var mb = page.CropBox;
        var body = new StringBuilder();

        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        var resources = SoftwarePageRenderer.ResolveInheritedPageResources(page.Dict, reader);

        // Map PDF user space (origin bottom-left, y up) onto SVG page space
        // (origin top-left, y down): x' = x - LLX, y' = URY - y.
        var gs = new GState { Ctm = new[] { 1.0, 0, 0, -1, -mb.LLX, mb.URY } };
        var usedBlendModes = new SortedSet<string>(StringComparer.Ordinal);
        var links = ResolveLinkRects(page, mb);

        foreach (var stream in contentStreams)
        {
            RenderToSvg(stream, resources, reader, body, 0, gs.Clone(), usedBlendModes, links);
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        sb.AppendLine("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">");
        // width/height are CSS pixels (1px = 0.75pt) so an importer applying the
        // standard px→pt factor recovers the original page size; the viewBox keeps
        // the content coordinates in points.
        sb.AppendLine($"<svg version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\" " +
            $"xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
            $"width=\"{F(mb.Width / 0.75)}\" height=\"{F(mb.Height / 0.75)}\" " +
            $"viewBox=\"0 0 {F(mb.Width)} {F(mb.Height)}\">");
        if (usedBlendModes.Count > 0)
        {
            sb.Append("<style type=\"text/css\">");
            foreach (var bm in usedBlendModes)
            {
                var css = MapBlendMode(bm);
                if (css == "normal") continue;
                var cls = css.Replace("-", "");
                sb.Append($".{cls}{{ mix-blend-mode:{css}; }}");
            }
            sb.AppendLine("</style>");
        }
        sb.Append(body);
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Convert a page to SVG and write to stream.
    /// </summary>
    public void Process(Page page, Stream output)
    {
        var svg = Process(page);
        output.Write(Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>
    /// Convert a page to SVG and write to a file.
    /// </summary>
    public void Process(Page page, string outputFileName)
    {
        using var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write);
        Process(page, fs);
    }

    /// <summary>
    /// Holds the mutable graphics state for rendering.
    /// </summary>
    private sealed class GState
    {
        public double FillR, FillG, FillB;
        public double StrokeR, StrokeG, StrokeB;
        public double FillAlpha = 1.0;
        public double StrokeAlpha = 1.0;
        public string BlendMode = "Normal";
        public double FontSize = 12;
        public string FontName = "sans-serif";
        // Font descriptor + ToUnicode CMap for the current font, so show-text
        // byte strings (which for embedded/subset fonts are glyph codes, not
        // Latin1 text) are decoded to real Unicode instead of garbage.
        public PdfDictionary? FontDict;
        public Dictionary<int, string>? ToUnicode;
        public Text.FontMetrics? Metrics;
        public double LineWidth = 1.0;
        public int LineCap; // 0=butt, 1=round, 2=square
        public int LineJoin; // 0=miter, 1=round, 2=bevel
        public double[] DashArray = Array.Empty<double>();
        public double DashPhase;
        public double TextLeading;
        public double CharSpacing;   // Tc
        public double WordSpacing;   // Tw
        public double HorizScale = 1.0; // Tz / 100
        public double TextRise;      // Ts
        public int RenderMode;       // Tr

        // CTM as 6-element matrix [a b c d e f]; includes the page's PDF→SVG flip.
        public double[] Ctm = { 1, 0, 0, 1, 0, 0 };

        // The colorspaces cs/CS selected, resolved to their tint machinery — a
        // /Separation spot colour must go through its tint transform, not the
        // operand-count gray inference (a "1 scn" full tint otherwise paints WHITE).
        public SoftwarePageRenderer.ImageColorSpaceInfo? FillCs;
        public SoftwarePageRenderer.ImageColorSpaceInfo? StrokeCs;

        public GState Clone()
        {
            return new GState
            {
                FillR = FillR, FillG = FillG, FillB = FillB,
                StrokeR = StrokeR, StrokeG = StrokeG, StrokeB = StrokeB,
                FillCs = FillCs, StrokeCs = StrokeCs,
                FillAlpha = FillAlpha, StrokeAlpha = StrokeAlpha,
                BlendMode = BlendMode,
                FontSize = FontSize, FontName = FontName,
                FontDict = FontDict, ToUnicode = ToUnicode, Metrics = Metrics,
                LineWidth = LineWidth,
                LineCap = LineCap, LineJoin = LineJoin,
                DashArray = (double[])DashArray.Clone(),
                DashPhase = DashPhase,
                TextLeading = TextLeading,
                CharSpacing = CharSpacing, WordSpacing = WordSpacing,
                HorizScale = HorizScale, TextRise = TextRise, RenderMode = RenderMode,
                Ctm = (double[])Ctm.Clone(),
            };
        }

        /// <summary>Uniform scale factor of the CTM, for stroke widths and dashes.</summary>
        public double CtmScale
        {
            get
            {
                var det = Math.Abs(Ctm[0] * Ctm[3] - Ctm[1] * Ctm[2]);
                return det > 0 ? Math.Sqrt(det) : 1.0;
            }
        }
    }

    /// <summary>Guard against pathological or self-referential Form XObject nesting.</summary>
    private const int MaxXObjectDepth = 12;

    /// <summary>A URI-link annotation's active area in top-down page coordinates.
    /// Content elements whose bounding-box centre falls inside the area are
    /// emitted wrapped in an <c>&lt;a xlink:href&gt;</c> anchor.</summary>
    private sealed record LinkRect(double X0, double Y0, double X1, double Y1, string Uri)
    {
        public bool Contains(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
    }

    /// <summary>Collect the page's URI-link annotation rectangles, mapped into
    /// top-down page coordinates.</summary>
    private List<LinkRect> ResolveLinkRects(Page page, Rectangle mb)
    {
        var links = new List<LinkRect>();
        var reader = page.Reader;
        var annots = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annots is null) return links;
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null || annot.GetName("Subtype") != "Link") continue;
            var action = reader.ResolveDict(annot.Get("A"));
            string? uri = null;
            if (action is not null && action.GetName("S") == "URI")
            {
                uri = reader.Resolve(action.Get("URI")) switch
                {
                    PdfString us => us.ToText(),
                    PdfName un => un.Value,
                    _ => null,
                };
            }
            else if (PageLinkTarget is not null)
            {
                // Internal GoTo links point at the sibling page's SVG file.
                var dest = action is not null && action.GetName("S") == "GoTo"
                    ? action.Get("D")
                    : annot.Get("Dest");
                var target = ResolveDestPage(reader, dest);
                if (target > 0) uri = PageLinkTarget(target);
            }
            if (string.IsNullOrEmpty(uri)) continue;
            if (reader.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4) continue;
            var x0 = Math.Min(Num(rect[0]), Num(rect[2])) - mb.LLX;
            var x1 = Math.Max(Num(rect[0]), Num(rect[2])) - mb.LLX;
            var y0 = mb.URY - Math.Max(Num(rect[1]), Num(rect[3]));
            var y1 = mb.URY - Math.Min(Num(rect[1]), Num(rect[3]));
            links.Add(new LinkRect(x0, y0, x1, y1, uri));
        }
        return links;
    }

    /// <summary>The 1-based page number an explicit destination points at, or
    /// 0 when it cannot be resolved. Handles a direct destination array, a
    /// named destination, and a page-object first element.</summary>
    private static int ResolveDestPage(PdfReader reader, PdfObject? dest)
    {
        var resolved = reader.Resolve(dest);
        // Named destination: look it up through the document.
        if (resolved is PdfString or PdfName)
        {
            var name = resolved is PdfString ps ? ps.ToText() : ((PdfName)resolved).Value;
            var doc = reader.OwnerDocument;
            if (doc is null || string.IsNullOrEmpty(name)) return 0;
            foreach (var nd in doc.NamedDestinations)
                if (nd.Name == name)
                    return nd.PageNumber;
            return 0;
        }
        if (resolved is not PdfArray arr || arr.Count == 0) return 0;
        if (dest is null) return 0;
        var pageObj = reader.Resolve(arr[0]);
        if (reader.Resolve(arr[0]) is PdfInteger pi) return (int)pi.Value + 1;
        if (pageObj is not PdfDictionary pageDict) return 0;
        var ownerDoc = reader.OwnerDocument;
        if (ownerDoc is null) return 0;
        var pages = ownerDoc.Pages;
        for (var i = 1; i <= pages.Count; i++)
            if (ReferenceEquals(pages[i].Dict, pageDict)) return i;
        return 0;
    }

    /// <summary>The link area covering the given device point, if any.</summary>
    private static LinkRect? LinkAt(List<LinkRect>? links, double x, double y)
    {
        if (links is null) return null;
        foreach (var l in links)
            if (l.Contains(x, y)) return l;
        return null;
    }

    /// <summary>Transform a point by an affine matrix.</summary>
    private static (double x, double y) Apply(double[] m, double x, double y) =>
        (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);

}
