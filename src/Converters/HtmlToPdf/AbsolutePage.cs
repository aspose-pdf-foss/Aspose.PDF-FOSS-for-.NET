using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The absolutely positioned page ──────────────────────────────────────
    // A print-oriented export whose body holds ONE absolutely positioned, sized container
    // div (centred by `left: 50%; margin-left: -half`) and, inside it, nothing but
    // absolutely positioned lines (`<div style="position:absolute;left:Xpx;top:Ypx"
    // class=cls_N>text</div>`) whose typography lives in their class rules, plus the odd
    // positioned image. Nothing flows: every line stands at its declared offset inside the
    // container's content box, in its class face and size, and the container's outset
    // border draws as a bevel round the box (measured on the benefits summary: the box
    // stands at 83.75..532 x 72..705.5 on a 613 pt sheet, every line at 84.75 + left·0.75,
    // its baseline at 73 + top·0.75 + the face's ascent).

    /// <summary>The sheet is laid out at the default width first; a box that then ends past the
    /// page margin grows the sheet to one page margin past its right edge, and the box is
    /// re-centred on the grown sheet (measured: 595 → 613 for a 595 px box centred with a
    /// −297 px margin).</summary>
    private const double AbsPageMarginSidePt = 90.0;
    private const double AbsPageMarginTopPt = 72.0;
    /// <summary>The UA body margin the container's percent offset resolves inside.</summary>
    private const double AbsPageBodyInsetPt = UaBodyMarginPt;
    /// <summary>An outset border with no declared width draws as a 1 pt frame: a dark grey top rule
    /// over black right and bottom rules (the reference strokes a grey left edge and a second, lighter
    /// bevel line one width inside too; its raster shows neither, so they are not drawn).</summary>
    private const double AbsPageBevelPt = 1.0;
    private const double AbsPageBevelDark = 0.333;
    /// <summary>The broken-image frame's lighter bottom+right tone.</summary>
    private const double AbsPageBevelLight = 0.667;
    /// <summary>A missing picture inside the box is the 32 px broken-image frame at its own bevel
    /// inside the picture's box origin.</summary>
    private const double AbsPageBrokenImagePt = 32.0;

    private sealed class AbsPageLine
    {
        public double LeftPt, TopPt, FontPt;
        public string Face = "Arial";
        public bool Bold, Italic;
        public (double r, double g, double b) Color = (0, 0, 0);
        public string Text = "";
        public bool IsImage;
        public byte[]? ImageBytes;
        public double ImageWPt, ImageHPt;
    }

    private sealed class AbsPageBox
    {
        public double WidthPt, HeightPt, MarginLeftPt, LeftFrac, TopPt;
        public bool Outset;
        public List<AbsPageLine> Lines = new();
    }

    private static readonly Regex AbsPageContainerRx = new Regex(
        @"<div\b(?<attrs>[^>]*\bstyle\s*=\s*(?<q>[""'])(?<st>[^""']*position\s*:\s*absolute[^""']*)\k<q>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True for the absolutely positioned page shape: one sized absolute container after
    /// the body open holding only absolute children, with no table and no flowing text.</summary>
    private static bool IsAbsolutePageHtml(string html)
    {
        if (Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase)) return false;
        return ReadAbsolutePage(html, null) is not null;
    }

    /// <summary>The container box and its lines, or null when the markup is not this shape.</summary>
    private static AbsPageBox? ReadAbsolutePage(string html, HtmlLoadOptions? options)
    {
        var trace = Environment.GetEnvironmentVariable("ASPOSE_TRACE_ABSPAGE") == "1";
        void Why(string reason) { if (trace) Console.Error.WriteLine("[abspage] no: " + reason); }
        var bodyM = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) { Why("no body"); return null; }
        var body = html[(bodyM.Index + bodyM.Length)..];
        var bodyEnd = body.IndexOf("</body", StringComparison.OrdinalIgnoreCase);
        if (bodyEnd >= 0) body = body[..bodyEnd];
        body = HtmlCommentRx.Replace(body, "");
        var open = AbsPageContainerRx.Match(body);
        if (!open.Success || body[..open.Index].Trim().Length > 0) { Why("no leading absolute container"); return null; }
        var st = open.Groups["st"].Value;
        if (!(StylePxPt(st, "width") is { } wPt && wPt > 0) || !(StylePxPt(st, "height") is { } hPt && hPt > 0)) { Why("container unsized"); return null; }
        var (afterClose, divEnd) = FindDivEnd(body, open.Index + open.Length);
        if (afterClose < 0 || body[afterClose..].Trim().Length > 0) { Why($"container close {afterClose}, trailing '{(afterClose < 0 ? "" : body[afterClose..].Trim())}'"); return null; }
        var box = new AbsPageBox
        {
            WidthPt = wPt, HeightPt = hPt,
            MarginLeftPt = StylePxPt(st, "margin-left") ?? 0,
            TopPt = StylePxPt(st, "top") ?? 0,
            Outset = Regex.IsMatch(st, @"border-style\s*:\s*outset", RegexOptions.IgnoreCase),
        };
        var leftM = Regex.Match(st, @"(?<![-\w])left\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        if (leftM.Success) box.LeftFrac = double.Parse(leftM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        var css = ParseStyleSheet(StripStyleCommentMarkers(html));
        var inner = body[(open.Index + open.Length)..divEnd];
        var pos = 0;
        foreach (Match child in Regex.Matches(inner, @"<div\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase))
        {
            if (child.Index < pos) continue;   // inside the previous child
            if (inner[pos..child.Index].Trim().Length > 0) { Why("text between children: " + inner[pos..child.Index].Trim()); return null; }
            var cst = Regex.Match(child.Groups["attrs"].Value, @"\bstyle\s*=\s*([""'])(?<v>[^""']*)\1", RegexOptions.IgnoreCase).Groups["v"].Value;
            if (!Regex.IsMatch(cst, @"position\s*:\s*absolute", RegexOptions.IgnoreCase)) { Why("child not absolute: " + child.Value); return null; }
            var (cAfterClose, cEnd) = FindDivEnd(inner, child.Index + child.Length);
            if (cAfterClose < 0) { Why("child unclosed: " + child.Value); return null; }
            var content = inner[(child.Index + child.Length)..cEnd];
            pos = cAfterClose;
            var line = new AbsPageLine { LeftPt = StylePxPt(cst, "left") ?? 0, TopPt = StylePxPt(cst, "top") ?? 0 };
            var img = Regex.Match(content, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
            if (img.Success)
            {
                line.IsImage = true;
                var wa = Regex.Match(img.Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
                var ha = Regex.Match(img.Value, @"\bheight\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
                line.ImageWPt = wa.Success ? DtpNum(wa.Groups[1].Value) * PxPt : 0;
                line.ImageHPt = ha.Success ? DtpNum(ha.Groups[1].Value) * PxPt : 0;
                var src = Regex.Match(img.Value, @"\bsrc\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
                if (options is not null && src.Success) line.ImageBytes = LoadConverterImage(src.Groups[1].Value, options);
                box.Lines.Add(line);
                continue;
            }
            if (Regex.IsMatch(content, @"<(?!/?(b|i|strong|em|span|br)\b)[a-z]", RegexOptions.IgnoreCase)) { Why("child holds markup: " + content); return null; }
            line.Text = CollapseWs(DecodeEntities(Regex.Replace(content, "<[^>]*>", ""))).Trim();
            var cls = Regex.Match(child.Groups["attrs"].Value, @"\bclass\s*=\s*(?:[""']([^""']*)[""']|([\w-]+))", RegexOptions.IgnoreCase);
            var clsName = cls.Success ? (cls.Groups[1].Success ? cls.Groups[1].Value : cls.Groups[2].Value).Trim() : "";
            ApplyAbsPageClass(line, css, clsName);
            if (Regex.IsMatch(content, @"<(b|strong)\b", RegexOptions.IgnoreCase)) line.Bold = true;
            if (Regex.IsMatch(content, @"<(i|em)\b", RegexOptions.IgnoreCase)) line.Italic = true;
            if (line.Text.Length > 0) box.Lines.Add(line);
        }
        if (inner[pos..].Trim().Length > 0) { Why("trailing text: " + inner[pos..].Trim()); return null; }
        if (box.Lines.Count == 0) Why("no lines");
        return box.Lines.Count > 0 ? box : null;
    }

    /// <summary>A px (or pt) length of a style declaration in points, or null.</summary>
    private static double? StylePxPt(string style, string prop)
    {
        var m = Regex.Match(style, @"(?<![-\w])" + Regex.Escape(prop) + @"\s*:\s*(-?[\d.]+)\s*(px|pt)?", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        return m.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? v : v * PxPt;
    }

    /// <summary>The line's class rule (`div.cls_N`, then `.cls_N`): face, size, weight, style, colour.</summary>
    private static void ApplyAbsPageClass(AbsPageLine line, Dictionary<string, Dictionary<string, string>> css, string clsName)
    {
        line.FontPt = DefaultBodyFontPt;
        if (clsName.Length == 0) return;
        foreach (var key in new[] { "." + clsName, "div." + clsName })
        {
            if (!css.TryGetValue(key, out var rule)) continue;
            if (rule.TryGetValue("font-size", out var fs) && TryParseLength(fs) is { } fsPt && fsPt > 0) line.FontPt = fsPt;
            if (rule.TryGetValue("font-family", out var ff) && ResolveFixedFace(ff) is { Length: > 0 } face) line.Face = face;
            if (rule.TryGetValue("font-weight", out var fw) && Regex.IsMatch(fw, @"bold|[6-9]00", RegexOptions.IgnoreCase)) line.Bold = true;
            if (rule.TryGetValue("font-style", out var fst) && Regex.IsMatch(fst, @"italic|oblique", RegexOptions.IgnoreCase)) line.Italic = true;
            if (rule.TryGetValue("color", out var col) && ParseCssColorRgb(col) is { } rgb) line.Color = rgb;
        }
    }

    /// <summary>Lays the absolutely positioned page out: the sheet, the container's bevel, its lines.</summary>
    private static Document? TryConvertAbsolutePage(string html, HtmlLoadOptions? options)
    {
        if (Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase)) return null;
        if (ReadAbsolutePage(html, options) is not { } box) return null;
        var pageInfo = options?.PageInfo;
        var pageW = pageInfo?.Width is > 0 ? pageInfo.Width : 595.0;
        var pageH = pageInfo?.Height is > 0 ? pageInfo.Height : 842.0;
        var widthAuthored = pageInfo?.WidthAssigned ?? false;
        var bevel = box.Outset ? AbsPageBevelPt : 0;
        // the box left on a sheet: page margin + body inset + its percent of the body box + its margin
        double BoxLeft(double sheetW) => AbsPageMarginSidePt + AbsPageBodyInsetPt
            + box.LeftFrac * (sheetW - 2 * (AbsPageMarginSidePt + AbsPageBodyInsetPt)) + box.MarginLeftPt;
        var outerW = box.WidthPt + 2 * bevel;
        var rightAtDefault = BoxLeft(pageW) + outerW;
        if (!widthAuthored && rightAtDefault + AbsPageMarginSidePt > pageW)
            pageW = rightAtDefault + AbsPageMarginSidePt;
        var boxX = BoxLeft(pageW);
        var boxTop = AbsPageMarginTopPt + box.TopPt;   // from the page top
        var outerH = box.HeightPt + 2 * bevel;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageW, pageH);
        EnsureFonts(page, docFontDict);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        if (bevel > 0) AppendAbsPageBevel(sb, inv, pageH, boxX, boxTop, outerW, outerH);
        var originX = boxX + bevel;
        var originTop = boxTop + bevel;
        foreach (var line in box.Lines)
        {
            var x = originX + line.LeftPt;
            var top = originTop + line.TopPt;
            if (line.IsImage)
            {
                if (line.ImageBytes is { Length: > 0 } && line.ImageWPt > 0 && line.ImageHPt > 0)
                {
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString())); sb.Clear();
                    page.AddImage(line.ImageBytes, new Rectangle(x, pageH - top - line.ImageHPt, x + line.ImageWPt, pageH - top));
                }
                else AppendAbsPageBrokenImage(sb, inv, pageH, x + AbsPageBevelPt, top + AbsPageBevelPt);
                continue;
            }
            AppendAbsPageLine(sb, inv, page, docFontDict, line, x, pageH - top);
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        PruneUnusedFonts(doc);
        return doc;
    }

    /// <summary>The container's outset bevel: two 1 pt strokes per side, the outer dark-grey top+left over
    /// black bottom+right, the inner dark-grey top+left over light-grey bottom+right.</summary>
    private static void AppendAbsPageBevel(StringBuilder sb, System.Globalization.CultureInfo inv, double pageH, double x, double top, double w, double h)
    {
        var half = AbsPageBevelPt / 2;
        var y0 = pageH - top;          // box top (bottom-up)
        var y1 = pageH - top - h;      // box bottom
        void Line(double g, double lx0, double ly0, double lx1, double ly1)
            => sb.Append(Compat.Format(inv, $"{g:0.###} G {lx0:F2} {ly0:F2} m {lx1:F2} {ly1:F2} l S "));
        // (measured on the reference raster: the top rule and the black right and bottom rules
        // show; the left grey hairline does not, so it is not drawn)
        sb.Append(Compat.Format(inv, $"q {AbsPageBevelPt:0.##} w "));
        Line(AbsPageBevelDark, x, y0 - half, x + w, y0 - half);
        Line(0, x, y1 + half, x + w, y1 + half);
        Line(0, x + w - half, y0, x + w - half, y1);
        sb.Append("Q\n");
    }

    /// <summary>The 32 px broken-image frame: dark grey top+left, light grey bottom+right, 1 pt.</summary>
    private static void AppendAbsPageBrokenImage(StringBuilder sb, System.Globalization.CultureInfo inv, double pageH, double x, double top)
    {
        var h = AbsPageBevelPt / 2;
        var bTop = pageH - top; var bBot = bTop - AbsPageBrokenImagePt; var bR = x + AbsPageBrokenImagePt;
        sb.Append(Compat.Format(inv,
            $"q {AbsPageBevelPt:0.##} w {AbsPageBevelDark:0.###} G " +
            $"{x:F2} {bTop - h:F2} m {bR:F2} {bTop - h:F2} l S {x + h:F2} {bTop:F2} m {x + h:F2} {bBot:F2} l S " +
            $"{AbsPageBevelLight:0.###} G {x:F2} {bBot + h:F2} m {bR:F2} {bBot + h:F2} l S {bR - h:F2} {bTop:F2} m {bR - h:F2} {bBot:F2} l S Q\n"));
    }

    /// <summary>One line in its class face: the baseline stands the face's ascent under the line's top.</summary>
    private static void AppendAbsPageLine(StringBuilder sb, System.Globalization.CultureInfo inv, Page page, Core.PdfDictionary docFontDict, AbsPageLine line, double x, double topY)
    {
        var variant = (line.Bold ? " Bold" : "") + (line.Italic ? " Italic" : "");
        var ttf = PosFace(line.Face + variant).ttf;
        if (ttf is null && variant.Length > 0)
        {
            try
            {
                ttf = Text.FontRepository.FindFont(line.Face,
                        line.Bold && line.Italic ? Text.FontStyles.Bold | Text.FontStyles.Italic
                        : line.Bold ? Text.FontStyles.Bold : Text.FontStyles.Italic, ignoreCase: true)
                    ?.SourceFontData?.TtfData;
            }
            catch { ttf = null; }
            ttf ??= PosFace(line.Face).ttf;
        }
        var asc = (WinMetricsFor(line.Face) ?? (0.905, 1.15)).asc;
        var y = topY - asc * line.FontPt;
        var res = page.Dict.Get("Resources") as Core.PdfDictionary;
        var fontDict = res?.Get("Font") as Core.PdfDictionary ?? docFontDict;
        sb.Append(Compat.Format(inv, $"BT {line.Color.r:F3} {line.Color.g:F3} {line.Color.b:F3} rg "));
        if (ttf is not null)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, line.Face + variant, line.Text, stripSpacesInBaseFont: true);
            sb.Append(Compat.Format(inv, $"/{rn} {line.FontPt:F2} Tf 1 0 0 1 {x:F2} {y:F2} Tm <"))
              .Append(Compat.ToHexString(hex)).Append("> Tj ");
        }
        else
            sb.Append(Compat.Format(inv, $"/F1 {line.FontPt:F2} Tf 1 0 0 1 {x:F2} {y:F2} Tm ({EscapePdfString(line.Text)}) Tj "));
        sb.Append("ET\n");
    }
}
