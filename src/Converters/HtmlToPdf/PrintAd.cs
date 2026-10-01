using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderPrintAd(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight, HtmlLoadOptions? options)
    {
        var pa = new PrintAdState();
        pa.html = html;
        pa.css = css;
        pa.pageHeight = pageHeight;
        pa.options = options;
        // Gate: an explicit zero-margin conversion of a document carrying an
        // @media print block and a container class with max-width + padding +
        // a px font-size + a unitless line-height.
        if (!Regex.IsMatch(pa.html, @"@media\s+print", RegexOptions.IgnoreCase)) return null;
        pa.contCls = null;
        pa.contRule = null;
        foreach (var (sel, props) in pa.css)
            if (sel.StartsWith('.') && !sel.Contains(' ')
                && props.ContainsKey("max-width") && props.ContainsKey("padding")
                && props.TryGetValue("font-size", out var cfs)
                && cfs.Contains("px", StringComparison.OrdinalIgnoreCase)
                && props.ContainsKey("line-height"))
            { pa.contCls = sel[1..]; pa.contRule = props; break; }
        if (pa.contCls is null || pa.contRule is null) return null;
        pa.bodyM = Regex.Match(pa.html, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        pa.face = pa.bodyM.Success
            && Regex.Match(pa.bodyM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } bfm
            ? FirstFontFamily(bfm.Groups[1].Value) : null;
        if (pa.face is null || WinMetricsFor(pa.face) is not { } wm) return null;
        pa.wm = wm;
        if (TryParseLength(pa.contRule["max-width"].Trim()) is not { } maxW || maxW <= 0) return null;
        if (TryParseLength(pa.contRule["padding"].Trim()) is not { } pad || pad < 0) return null;
        pa.baseFs = TryParseLength(pa.contRule["font-size"].Trim()) is { } bfs ? bfs : 11.25;
        pa.lineFactor = double.TryParse(pa.contRule["line-height"].Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var lf) && lf > 0 ? lf : 1.25;

        pa.contM = Regex.Match(pa.html,
            @"<div\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(pa.contCls) + @"\b[^""']*[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!pa.contM.Success) return null;
        pa.inner = BalancedInner(pa.html, pa.contM.Index + pa.contM.Length, "div");
        if (pa.inner is null) return null;

        pa.pageWidth = UaBodyMarginPt + maxW + 2 * pad;
        pa.xText = UaBodyMarginPt + pad;
        pa.contentW = maxW;

        pa.beforeM = Regex.Match(pa.html,
            @"li:before\s*\{[^}]*content\s*:\s*[""']([^""']*)[""'][^}]*margin-right\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        pa.liMarker = pa.beforeM.Success ? pa.beforeM.Groups[1].Value : "";
        pa.liMarkerGap = pa.beforeM.Success
            ? double.Parse(pa.beforeM.Groups[2].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75 : 0;

        pa.h6Fs = FsOf(pa, "." + pa.contCls + " h6", 10.5);
        pa.h1Fs = FsOf(pa, "." + pa.contCls + " h1", 17.25);
        pa.h1SpanFs = FsOf(pa, "." + pa.contCls + " h1 span", 24);
        pa.descFs = FsOf(pa, "." + pa.contCls + " .companyDescription", pa.baseFs);
        pa.pMb = MarginBottomOf(pa, "." + pa.contCls + " p", 15);
        pa.h1Mb = MarginBottomOf(pa, "." + pa.contCls + " h1", 11.25);
        pa.h6Rule = pa.css.TryGetValue("." + pa.contCls + " h6", out var h6R) ? h6R : null;
        pa.h6PadBottom = pa.h6Rule is not null && pa.h6Rule.TryGetValue("padding-bottom", out var h6pb)
            && TryParseLength(h6pb.Trim()) is { } h6pbPt ? h6pbPt : 3.75;
        pa.h6Mb = MarginBottomOf(pa, "." + pa.contCls + " h6", 7.5);
        pa.h6Border = pa.h6Rule is not null && pa.h6Rule.TryGetValue("border-bottom", out var h6bb)
            ? ParseCssColor(h6bb) : null;
        pa.blockMb = MarginBottomOf(pa, "." + pa.contCls + " .jobBlock", 22.5);

        pa.doc = Document.Create();
        pa.docFontDict = new Core.PdfDictionary();
        pa.page = pa.doc.Pages.Add(pa.pageWidth, pa.pageHeight);
        EnsureFonts(pa.page, pa.docFontDict);
        pa.sb = new StringBuilder();
        pa.inv = System.Globalization.CultureInfo.InvariantCulture;
        pa.y = UaBodyMarginPt + pad;
        pa.pendingGap = 0.0;
        pa.tagRx = new Regex(@"<(/?)(\w+)([^>]*?)(/?)>|<!--[\s\S]*?-->", RegexOptions.Singleline);
        WalkAd(pa, pa.inner);
        pa.page.AddContentStream(Encoding.ASCII.GetBytes(pa.sb.ToString()));
        return pa.doc;
    }
}
