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
public sealed class SvgDevice
{
    /// <summary>
    /// Convert a page to SVG string.
    /// </summary>
    public string Process(Page page)
    {
        var mb = page.MediaBox;
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

        public GState Clone()
        {
            return new GState
            {
                FillR = FillR, FillG = FillG, FillB = FillB,
                StrokeR = StrokeR, StrokeG = StrokeG, StrokeB = StrokeB,
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
    private static List<LinkRect> ResolveLinkRects(Page page, Rectangle mb)
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
            if (action is null || action.GetName("S") != "URI") continue;
            var uriObj = reader.Resolve(action.Get("URI"));
            var uri = uriObj switch
            {
                PdfString us => us.ToText(),
                PdfName un => un.Value,
                _ => null,
            };
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

    /// <summary>The link area covering the given device point, if any.</summary>
    private static LinkRect? LinkAt(List<LinkRect>? links, double x, double y)
    {
        if (links is null) return null;
        foreach (var l in links)
            if (l.Contains(x, y)) return l;
        return null;
    }

    private static void RenderToSvg(byte[] streamBytes, PdfDictionary? resources,
        PdfReader reader, StringBuilder sb, int depth, GState gs, ISet<string> usedBlendModes,
        List<LinkRect>? links)
    {
        var fonts = ResolveFonts(resources, reader);
        var extGStates = ResolveExtGStates(resources, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        // tm is the text matrix, tlm the text line matrix (PDF row-vector convention).
        double[] tm = { 1, 0, 0, 1, 0, 0 };
        double[] tlm = { 1, 0, 0, 1, 0, 0 };
        var gsStack = new Stack<GState>();
        var pathData = new StringBuilder();
        // Path current point in USER coordinates (needed by the v operator).
        double curX = 0, curY = 0;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        // --- Graphics state stack ---
                        case "q":
                            gsStack.Push(gs.Clone());
                            break;
                        case "Q":
                            if (gsStack.Count > 0)
                                gs = gsStack.Pop();
                            break;

                        // --- CTM ---
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                var m = new[]
                                {
                                    Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                    Num(operands[3]), Num(operands[4]), Num(operands[5]),
                                };
                                gs.Ctm = MulAffine(m, gs.Ctm);
                            }
                            break;

                        // --- ExtGState ---
                        case "gs":
                            if (operands.Count >= 1 && operands[0] is PdfName gsName)
                            {
                                if (extGStates.TryGetValue(gsName.Value, out var gsDict))
                                {
                                    var caObj = gsDict.Get("ca");
                                    if (caObj is PdfReal caR) gs.FillAlpha = caR.Value;
                                    else if (caObj is PdfInteger caI) gs.FillAlpha = caI.Value;

                                    var scaObj = gsDict.Get("CA");
                                    if (scaObj is PdfReal scaR) gs.StrokeAlpha = scaR.Value;
                                    else if (scaObj is PdfInteger scaI) gs.StrokeAlpha = scaI.Value;

                                    var bmObj = gsDict.GetName("BM");
                                    if (bmObj is not null)
                                    {
                                        gs.BlendMode = bmObj;
                                        if (bmObj != "Normal") usedBlendModes.Add(bmObj);
                                    }
                                }
                            }
                            break;

                        // --- Line width ---
                        case "w":
                            if (operands.Count >= 1)
                                gs.LineWidth = Num(operands[0]);
                            break;

                        // --- Line cap ---
                        case "J":
                            if (operands.Count >= 1)
                                gs.LineCap = (int)Num(operands[0]);
                            break;

                        // --- Line join ---
                        case "j":
                            if (operands.Count >= 1)
                                gs.LineJoin = (int)Num(operands[0]);
                            break;

                        // --- Dash pattern ---
                        case "d":
                            if (operands.Count >= 2 && operands[0] is PdfArray dashArr)
                            {
                                gs.DashArray = new double[dashArr.Count];
                                for (int i = 0; i < dashArr.Count; i++)
                                    gs.DashArray[i] = Num(dashArr[i]);
                                gs.DashPhase = Num(operands[1]);
                            }
                            break;

                        // --- Font ---
                        case "Tf":
                            if (operands.Count >= 2)
                            {
                                if (operands[0] is PdfName fn)
                                {
                                    if (fonts.TryGetValue(fn.Value, out var fd))
                                    {
                                        gs.FontDict = fd;
                                        gs.ToUnicode = Text.TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                                        var baseFont = fd.GetName("BaseFont") ?? "sans-serif";
                                        gs.FontName = MapFontName(baseFont);
                                        try { gs.Metrics = Text.FontMetrics.FromFontDict(fd, reader); }
                                        catch { gs.Metrics = null; }
                                    }
                                    else
                                    {
                                        gs.FontDict = null;
                                        gs.ToUnicode = null;
                                        gs.Metrics = null;
                                        gs.FontName = fn.Value;
                                    }
                                }
                                gs.FontSize = Num(operands[1]);
                            }
                            break;

                        // --- Text state ---
                        case "Tc":
                            if (operands.Count >= 1) gs.CharSpacing = Num(operands[0]);
                            break;
                        case "Tw":
                            if (operands.Count >= 1) gs.WordSpacing = Num(operands[0]);
                            break;
                        case "Tz":
                            if (operands.Count >= 1) gs.HorizScale = Num(operands[0]) / 100.0;
                            break;
                        case "Ts":
                            if (operands.Count >= 1) gs.TextRise = Num(operands[0]);
                            break;
                        case "Tr":
                            if (operands.Count >= 1) gs.RenderMode = (int)Num(operands[0]);
                            break;

                        // --- Text object begin: reset text + line matrices ---
                        case "BT":
                            Array.Copy(Identity, tm, 6);
                            Array.Copy(Identity, tlm, 6);
                            break;

                        // --- Text positioning (operate on the text line matrix) ---
                        case "Td":
                            if (operands.Count >= 2)
                            {
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(operands[0]), Num(operands[1]) }, tlm);
                                Array.Copy(tlm, tm, 6);
                            }
                            break;
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                gs.TextLeading = -Num(operands[1]);
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(operands[0]), Num(operands[1]) }, tlm);
                                Array.Copy(tlm, tm, 6);
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                var m = new[] { Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                    Num(operands[3]), Num(operands[4]), Num(operands[5]) };
                                Array.Copy(m, tm, 6); Array.Copy(m, tlm, 6);
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                gs.TextLeading = Num(operands[0]);
                            break;
                        case "T*":
                            tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                            Array.Copy(tlm, tm, 6);
                            break;

                        // --- Fill color (RGB) ---
                        case "rg":
                            if (operands.Count >= 3)
                            { gs.FillR = Num(operands[0]); gs.FillG = Num(operands[1]); gs.FillB = Num(operands[2]); }
                            break;

                        // --- Stroke color (RGB) ---
                        case "RG":
                            if (operands.Count >= 3)
                            { gs.StrokeR = Num(operands[0]); gs.StrokeG = Num(operands[1]); gs.StrokeB = Num(operands[2]); }
                            break;

                        // --- Grayscale fill ---
                        case "g":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                gs.FillR = gray; gs.FillG = gray; gs.FillB = gray;
                            }
                            break;

                        // --- Grayscale stroke ---
                        case "G":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                gs.StrokeR = gray; gs.StrokeG = gray; gs.StrokeB = gray;
                            }
                            break;

                        // --- CMYK fill ---
                        case "k":
                            if (operands.Count >= 4)
                            {
                                CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                                    out gs.FillR, out gs.FillG, out gs.FillB);
                            }
                            break;

                        // --- CMYK stroke ---
                        case "K":
                            if (operands.Count >= 4)
                            {
                                CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                                    out gs.StrokeR, out gs.StrokeG, out gs.StrokeB);
                            }
                            break;

                        // --- Colorspace-relative fill/stroke colour (sc/scn, SC/SCN) ---
                        // The colorspace is set via cs/CS to a named space; rather than
                        // resolve it, infer from the numeric operand count (1=gray,
                        // 3=rgb, 4=cmyk). Without this, sc-coloured content (e.g. white
                        // 1 1 1 sc interiors) all defaulted to black.
                        case "sc":
                        case "scn":
                            SetColorFromComponents(operands, ref gs.FillR, ref gs.FillG, ref gs.FillB);
                            break;
                        case "SC":
                        case "SCN":
                            SetColorFromComponents(operands, ref gs.StrokeR, ref gs.StrokeG, ref gs.StrokeB);
                            break;

                        // --- Text show: ' (move to next line then show) ---
                        case "'":
                            tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                            Array.Copy(tlm, tm, 6);
                            if (operands.Count >= 1 && operands[0] is PdfString qs)
                                ShowText(sb, gs, tm, new PdfObject[] { qs }, reader, links);
                            break;

                        // --- Text show: " (set word+char spacing, next line, show) ---
                        case "\"":
                            if (operands.Count >= 3 && operands[2] is PdfString dqs)
                            {
                                gs.WordSpacing = Num(operands[0]);
                                gs.CharSpacing = Num(operands[1]);
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                                Array.Copy(tlm, tm, 6);
                                ShowText(sb, gs, tm, new PdfObject[] { dqs }, reader, links);
                            }
                            break;

                        // --- Text show ---
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                                ShowText(sb, gs, tm, new PdfObject[] { s }, reader, links);
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray tja)
                                ShowText(sb, gs, tm, tja.ToArray(), reader, links);
                            break;

                        // --- Path construction (coordinates transformed by the CTM) ---
                        case "m": // moveto
                            if (operands.Count >= 2)
                            {
                                curX = Num(operands[0]); curY = Num(operands[1]);
                                var (px, py) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"M{F(px)} {F(py)} ");
                            }
                            break;
                        case "l": // lineto
                            if (operands.Count >= 2)
                            {
                                curX = Num(operands[0]); curY = Num(operands[1]);
                                var (px, py) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"L{F(px)} {F(py)} ");
                            }
                            break;
                        case "c": // curveto
                            if (operands.Count >= 6)
                            {
                                var (x1, y1) = Apply(gs.Ctm, Num(operands[0]), Num(operands[1]));
                                var (x2, y2) = Apply(gs.Ctm, Num(operands[2]), Num(operands[3]));
                                curX = Num(operands[4]); curY = Num(operands[5]);
                                var (x3, y3) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} ");
                            }
                            break;
                        case "v": // curveto (initial point replicated)
                            if (operands.Count >= 4)
                            {
                                var (x1, y1) = Apply(gs.Ctm, curX, curY);
                                var (x2, y2) = Apply(gs.Ctm, Num(operands[0]), Num(operands[1]));
                                curX = Num(operands[2]); curY = Num(operands[3]);
                                var (x3, y3) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} ");
                            }
                            break;
                        case "y": // curveto (final point replicated)
                            if (operands.Count >= 4)
                            {
                                var (x1, y1) = Apply(gs.Ctm, Num(operands[0]), Num(operands[1]));
                                curX = Num(operands[2]); curY = Num(operands[3]);
                                var (x3, y3) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"C{F(x1)} {F(y1)} {F(x3)} {F(y3)} {F(x3)} {F(y3)} ");
                            }
                            break;
                        case "h": // closepath
                            pathData.Append("Z ");
                            break;
                        case "re":
                            if (operands.Count >= 4)
                            {
                                var rx = Num(operands[0]); var ry = Num(operands[1]);
                                var rw = Num(operands[2]); var rh = Num(operands[3]);
                                var (p1x, p1y) = Apply(gs.Ctm, rx, ry);
                                var (p2x, p2y) = Apply(gs.Ctm, rx + rw, ry);
                                var (p3x, p3y) = Apply(gs.Ctm, rx + rw, ry + rh);
                                var (p4x, p4y) = Apply(gs.Ctm, rx, ry + rh);
                                pathData.Append($"M{F(p1x)} {F(p1y)} L{F(p2x)} {F(p2y)} L{F(p3x)} {F(p3y)} L{F(p4x)} {F(p4y)} Z ");
                                curX = rx; curY = ry;
                            }
                            break;

                        // --- Path painting ---
                        case "S": // stroke
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: false, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "s": // close and stroke
                            pathData.Append("Z ");
                            goto case "S";
                        case "f" or "F": // fill (nonzero)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: false, fill: true, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "f*": // fill (even-odd)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: false, fill: true, evenOdd: true);
                                pathData.Clear();
                            }
                            break;
                        case "B": // fill and stroke (nonzero)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "B*": // fill and stroke (even-odd)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: true);
                                pathData.Clear();
                            }
                            break;
                        case "b": // close, fill and stroke (nonzero)
                            pathData.Append("Z ");
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "b*": // close, fill and stroke (even-odd)
                            pathData.Append("Z ");
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: true);
                                pathData.Clear();
                            }
                            break;
                        case "n": // end path (no fill, no stroke)
                            pathData.Clear();
                            break;
                        // --- XObject invocation (Form recursion) ---
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xn)
                                RenderXObject(xn.Value, resources, reader, sb, depth, gs, usedBlendModes, links);
                            break;

                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    /// <summary>
    /// Show one run of text (a Tj/'/"-string or a full TJ array). Decodes the bytes
    /// glyph-by-glyph through the font, advances the text matrix per glyph (width,
    /// char/word spacing, TJ adjustments), and emits a single &lt;text&gt; element.
    /// Horizontal upright runs get per-glyph absolute x positions; rotated runs get a
    /// matrix() transform (y-column negated so glyphs render upright; the importer
    /// negates it back).
    /// </summary>
    private static void ShowText(StringBuilder sb, GState gs, double[] tm,
        PdfObject[] items, PdfReader reader, List<LinkRect>? links)
    {
        var fs = gs.FontSize;
        var th = gs.HorizScale;
        var isCid = gs.Metrics?.IsCid ?? (gs.FontDict?.GetName("Subtype") == "Type0");
        var step = isCid ? 2 : 1;

        // One <text> element is emitted per TJ string segment — kerning
        // adjustments between segments only move the pen.
        foreach (var item in items)
        {
            if (item is PdfInteger or PdfReal)
            {
                var adj = -Num(item) / 1000.0 * fs * th;
                ApplyPen(tm, adj);
                continue;
            }
            if (item is not PdfString ps) continue;
            var bytes = ps.Value;

            var text = new StringBuilder();
            var xs = new List<double>();
            double[]? firstDevice = null;

            for (var i = 0; i + step - 1 < bytes.Length; i += step)
            {
                var code = isCid ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
                var seg = isCid ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
                var glyph = gs.FontDict is not null
                    ? Text.TextAbsorber.DecodeStringPublic(seg, gs.ToUnicode, gs.FontDict, reader)
                    : Encoding.Latin1.GetString(seg);
                // A glyph whose decode is empty or XML-invalid (unmapped codes often
                // carry U+FFFF from a bfrange to FFFF) still occupies a slot on the
                // line. Substitute the PUA char U+A880 — the "SVG space" — so the
                // character count stays aligned with the x list.
                glyph = EscapeXml(glyph);
                if (glyph.Length == 0 && gs.FontDict is not null)
                    glyph = "ꢀ";

                // Device matrix for this glyph: [fs*th 0 0 fs 0 rise] × tm × ctm
                var trm = MulAffine(new[] { fs * th, 0, 0, fs, 0, gs.TextRise },
                    MulAffine(tm, gs.Ctm));
                firstDevice ??= trm;
                if (glyph.Length > 0)
                {
                    // Multi-char decodes (ligature expansions) get one position for the
                    // first char; SVG flows the rest after it with natural advances.
                    xs.Add(trm[4]);
                    text.Append(glyph);
                }

                var w = gs.Metrics is not null
                    ? gs.Metrics.GetWidth(code) / 1000.0 * fs
                    : fs * 0.5;
                var adv = (w + gs.CharSpacing + (step == 1 && code == 32 ? gs.WordSpacing : 0)) * th;
                ApplyPen(tm, adv);
            }

            if (text.Length == 0 || firstDevice is null) continue;
            if (gs.RenderMode == 3) continue; // invisible text: pen already advanced

            EmitTextRun(sb, gs, tm, firstDevice, xs, text.ToString(), links);
        }
    }

    /// <summary>Emit one decoded, positioned text run.</summary>
    private static void EmitTextRun(StringBuilder sb, GState gs, double[] tm,
        double[] firstDevice, List<double> xs, string text, List<LinkRect>? links)
    {
        var fs = gs.FontSize;
        var th = gs.HorizScale;
        var fill = FormatHex(gs.FillR, gs.FillG, gs.FillB);
        var style = new StringBuilder();
        style.Append($"fill:{fill};font-family:{gs.FontName};");
        if (gs.FillAlpha < 1.0)
            style.Append($"fill-opacity:{F(gs.FillAlpha)};");
        if (gs.BlendMode != "Normal")
            style.Append($"mix-blend-mode:{MapBlendMode(gs.BlendMode)};");

        var d = firstDevice;
        const double eps = 1e-6;
        if (Math.Abs(d[1]) < eps && Math.Abs(d[2]) < eps && d[0] > 0 && d[3] < 0)
        {
            // Horizontal upright text: absolute per-glyph x positions, single y.
            var size = Math.Abs(d[3]);
            // The run's approximate centre (half a glyph beyond the last x, half a
            // font-size above the baseline) decides link-anchor coverage.
            // Whitespace-only runs draw nothing clickable and stay unwrapped.
            var link = IsBlankRun(text) ? null : LinkAt(links,
                (xs.Min() + xs.Max() + size * 0.5) / 2.0, d[5] - size / 2.0);
            style.Append($"font-size:{FD(size)}px;");
            var xList = string.Join(" ", xs.Select(FD));
            if (link is not null) sb.AppendLine($"<a xlink:href=\"{EscapeXml(link.Uri)}\" target=\"_blank\" >");
            sb.AppendLine($"<text x=\"{xList}\" y=\"{FD(d[5])}\" style=\"{style}\">{text}</text>");
            if (link is not null) sb.AppendLine("</a>");
        }
        else
        {
            // Rotated/sheared: place by matrix (tm×ctm without the font-size factor),
            // y-column negated so the glyphs render upright under the flipped page.
            var t2 = MulAffine(new[] { th, 0, 0, 1.0, 0, gs.TextRise }, MulAffine(tm, gs.Ctm));
            // Anchor the matrix at the start of the run: translation from firstDevice.
            t2[4] = d[4]; t2[5] = d[5];
            var link = IsBlankRun(text) ? null : LinkAt(links, t2[4], t2[5]);
            style.Append($"font-size:{FD(fs)}px;");
            var transform = $"matrix({F(t2[0])},{F(t2[1])},{F(Neg(t2[2]))},{F(Neg(t2[3]))},{F(t2[4])},{F(t2[5])})";
            if (link is not null) sb.AppendLine($"<a xlink:href=\"{EscapeXml(link.Uri)}\" target=\"_blank\" >");
            sb.AppendLine($"<text transform=\"{transform}\" style=\"{style}\">{text}</text>");
            if (link is not null) sb.AppendLine("</a>");
        }
    }

    /// <summary>True when a decoded run has no visible glyphs (whitespace and the
    /// U+A880 blank-glyph placeholder only).</summary>
    private static bool IsBlankRun(string text)
    {
        foreach (var ch in text)
            if (!char.IsWhiteSpace(ch) && ch != 'ꢀ') return false;
        return true;
    }

    /// <summary>Advance the text matrix pen: tm := [1 0 0 1 adv 0] × tm.</summary>
    private static void ApplyPen(double[] tm, double adv)
    {
        tm[4] += adv * tm[0];
        tm[5] += adv * tm[1];
    }

    /// <summary>Negate, flushing -0 to 0 for tidy output.</summary>
    private static double Neg(double v) => v == 0.0 ? 0.0 : -v;

    /// <summary>Transform a point by an affine matrix.</summary>
    private static (double x, double y) Apply(double[] m, double x, double y) =>
        (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);

    /// <summary>
    /// Emit a path element with current graphics state. Path data is already in
    /// top-down page coordinates; stroke widths/dashes are scaled by the CTM.
    /// </summary>
    private static void EmitPath(StringBuilder sb, GState gs, string d, bool stroke, bool fill, bool evenOdd)
    {
        var attrs = new StringBuilder();
        attrs.Append($"d=\"{d}\"");

        if (fill)
        {
            var fillColor = FormatHex(gs.FillR, gs.FillG, gs.FillB);
            attrs.Append($" fill=\"{fillColor}\"");
            if (gs.FillAlpha < 1.0)
            {
                attrs.Append($" fill-opacity=\"{F(gs.FillAlpha)}\"");
                // A fully transparent fill is invisible content; keep it from
                // intercepting clicks meant for link anchors beneath it.
                if (gs.FillAlpha == 0.0)
                    attrs.Append(" pointer-events=\"none\"");
            }
            if (evenOdd)
                attrs.Append(" fill-rule=\"evenodd\"");
        }
        else
        {
            attrs.Append(" fill=\"none\"");
        }

        if (stroke)
        {
            var scale = gs.CtmScale;
            var strokeColor = FormatHex(gs.StrokeR, gs.StrokeG, gs.StrokeB);
            attrs.Append($" stroke=\"{strokeColor}\"");
            if (gs.StrokeAlpha < 1.0)
                attrs.Append($" stroke-opacity=\"{F(gs.StrokeAlpha)}\"");
            var sw = gs.LineWidth * scale;
            if (sw != 1.0)
                attrs.Append($" stroke-width=\"{F(sw)}\"");
            if (gs.LineCap != 0)
                attrs.Append($" stroke-linecap=\"{MapLineCap(gs.LineCap)}\"");
            if (gs.LineJoin != 0)
                attrs.Append($" stroke-linejoin=\"{MapLineJoin(gs.LineJoin)}\"");
            if (gs.DashArray.Length > 0)
            {
                attrs.Append($" stroke-dasharray=\"{string.Join(",", gs.DashArray.Select(v => F(v * scale)))}\"");
                if (gs.DashPhase != 0)
                    attrs.Append($" stroke-dashoffset=\"{F(gs.DashPhase * scale)}\"");
            }
        }
        else
        {
            attrs.Append(" stroke=\"none\"");
        }

        if (gs.BlendMode != "Normal")
            attrs.Append($" style=\"mix-blend-mode:{MapBlendMode(gs.BlendMode)}\"");

        sb.AppendLine($"<path {attrs} />");
    }

    private static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

    /// <summary>Compose two affine matrices (PDF row-vector convention): m1 × m2.</summary>
    private static double[] MulAffine(double[] m1, double[] m2) => new[]
    {
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
    };

    private static string FormatHex(double r, double g, double b) =>
        $"#{ClampByte(r):x2}{ClampByte(g):x2}{ClampByte(b):x2}";

    private static int ClampByte(double v) => Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>
    /// Set an RGB colour from the numeric operands of an <c>sc</c>/<c>scn</c>
    /// operator, inferring the model from the component count. A trailing pattern
    /// name (scn) or zero numeric operands leaves the colour unchanged.
    /// </summary>
    private static void SetColorFromComponents(List<PdfObject> operands, ref double r, ref double g, ref double b)
    {
        var nums = operands.Where(o => o is PdfInteger or PdfReal).Select(Num).ToList();
        switch (nums.Count)
        {
            case 1:
                r = g = b = nums[0];
                break;
            case 3:
                r = nums[0]; g = nums[1]; b = nums[2];
                break;
            case 4:
                CmykToRgb(nums[0], nums[1], nums[2], nums[3], out r, out g, out b);
                break;
        }
    }

    private static void CmykToRgb(double c, double m, double y, double k,
        out double r, out double g, out double b)
    {
        r = (1 - c) * (1 - k);
        g = (1 - m) * (1 - k);
        b = (1 - y) * (1 - k);
    }

    private static string MapLineCap(int cap) => cap switch
    {
        1 => "round",
        2 => "square",
        _ => "butt",
    };

    private static string MapLineJoin(int join) => join switch
    {
        1 => "round",
        2 => "bevel",
        _ => "miter",
    };

    private static string MapFontName(string baseFont) => baseFont switch
    {
        var n when n.Contains("Helvetica") => "Helvetica, Arial, sans-serif",
        var n when n.Contains("Times") => "Times New Roman, serif",
        var n when n.Contains("Courier") => "Courier New, monospace",
        _ => "sans-serif",
    };

    private static string MapBlendMode(string pdfMode) => pdfMode switch
    {
        "Multiply" => "multiply",
        "Screen" => "screen",
        "Overlay" => "overlay",
        "Darken" => "darken",
        "Lighten" => "lighten",
        "ColorDodge" => "color-dodge",
        "ColorBurn" => "color-burn",
        "HardLight" => "hard-light",
        "SoftLight" => "soft-light",
        "Difference" => "difference",
        "Exclusion" => "exclusion",
        "Hue" => "hue",
        "Saturation" => "saturation",
        "Color" => "color",
        "Luminosity" => "luminosity",
        _ => "normal",
    };

    /// <summary>
    /// Render a named XObject. Form XObjects are decoded and recursed into with their
    /// own /Resources (falling back to the parent's) and optional /Matrix composed
    /// into the CTM. Image XObjects are not yet emitted.
    /// </summary>
    private static void RenderXObject(string name, PdfDictionary? resources, PdfReader reader,
        StringBuilder sb, int depth, GState gs, ISet<string> usedBlendModes,
        List<LinkRect>? links)
    {
        if (depth >= MaxXObjectDepth || resources is null) return;

        var xobjDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjDict is null) return;

        var xobj = reader.ResolveStream(xobjDict.Get(name));
        if (xobj is null) return;

        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype == "Image")
        {
            EmitImage(name, xobj, reader, sb, gs, links);
            return;
        }
        if (subtype != "Form") return;

        byte[] formBytes;
        try { formBytes = reader.DecodeStream(xobj); }
        catch { return; }

        var formResources = reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;

        // Optional /Matrix maps form space into the current user space.
        var formGs = gs.Clone();
        var matrix = reader.Resolve(xobj.Dict.Get("Matrix")) as PdfArray;
        if (matrix is not null && matrix.Count >= 6)
        {
            var m = new[]
            {
                Num(matrix[0]), Num(matrix[1]), Num(matrix[2]),
                Num(matrix[3]), Num(matrix[4]), Num(matrix[5]),
            };
            formGs.Ctm = MulAffine(m, formGs.Ctm);
        }

        RenderToSvg(formBytes, formResources, reader, sb, depth + 1, formGs, usedBlendModes, links);
    }

    /// <summary>Emit an image XObject as an inline data-URI PNG. The image's unit
    /// square is mapped through the CTM; axis-aligned placements use x/y/width/height,
    /// anything else keeps the full matrix.</summary>
    private static void EmitImage(string name, PdfStream xobj, PdfReader reader,
        StringBuilder sb, GState gs, List<LinkRect>? links)
    {
        byte[] png;
        try
        {
            png = new ImageXObject(name, xobj, reader).ToPng();
        }
        catch
        {
            return; // undecodable image: skip rather than fail the page
        }

        // The image occupies the unit square (0,0)-(1,1) in user space.
        var m = gs.Ctm;
        var (x0, y0) = Apply(m, 0, 0);
        var (x1, y1) = Apply(m, 1, 1);
        var link = LinkAt(links, (x0 + x1) / 2.0, (y0 + y1) / 2.0);
        if (link is not null) sb.AppendLine($"<a xlink:href=\"{EscapeXml(link.Uri)}\" target=\"_blank\" >");

        var href = $"data:image/png;base64,{Convert.ToBase64String(png)}";
        const double eps = 1e-6;
        if (Math.Abs(m[1]) < eps && Math.Abs(m[2]) < eps)
        {
            var lx = Math.Min(x0, x1);
            var ly = Math.Min(y0, y1);
            var w = Math.Abs(x1 - x0);
            var h = Math.Abs(y1 - y0);
            sb.AppendLine($"<image x=\"{F(lx)}\" y=\"{F(ly)}\" width=\"{F(w)}\" height=\"{F(h)}\" " +
                $"preserveAspectRatio=\"none\" xlink:href=\"{href}\" />");
        }
        else
        {
            // General placement: the unit square flips vertically under the page
            // transform, so fold the flip into the matrix and draw at (0,-1).
            var transform = $"matrix({F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(m[4])} {F(m[5])})";
            sb.AppendLine($"<image x=\"0\" y=\"-1\" width=\"1\" height=\"1\" " +
                $"preserveAspectRatio=\"none\" transform=\"{transform} scale(1,-1)\" xlink:href=\"{href}\" />");
        }
        if (link is not null) sb.AppendLine("</a>");
    }

    private static string EscapeXml(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Drop characters that are illegal in XML 1.0: control chars below 0x20
            // (except tab/LF/CR) and the non-characters U+FFFE/U+FFFF.
            if (ch < 0x20 && ch is not ('\t' or '\n' or '\r')) continue;
            if (ch is '￾' or '￿') continue;
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;
        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is not null) result[key] = font;
        }
        return result;
    }

    private static Dictionary<string, PdfDictionary> ResolveExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return result;
        foreach (var key in gsDict.Keys)
        {
            var entryDict = reader.ResolveDict(gsDict.Get(key));
            if (entryDict is not null) result[key] = entryDict;
        }
        return result;
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    /// <summary>Format with a forced decimal part (e.g. <c>266.16667</c>, <c>173.0</c>),
    /// the shape used for text positions.</summary>
    private static string FD(double v) => v.ToString("0.0#####", CultureInfo.InvariantCulture);
}
