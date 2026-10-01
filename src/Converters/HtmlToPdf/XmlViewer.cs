using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The untouched page margins (the converter's caller-facing
    // defaults; the early dialect call sites see the legacy 96/72 pair, so the
    // dialect pins its own).
    private const double XvMarginLeftPt = 90.0;

    private const double XvMarginTopPt = 72.0;

    private const double XvMarginBottomPt = 72.0;

    private static Document? TryRenderXmlViewer(string html, double pageWidth, double pageHeight)
    {
        var xv = new XmlViewerState();
        xv.html = html;
        xv.pageWidth = pageWidth;
        xv.pageHeight = pageHeight;
        if (Regex.IsMatch(xv.html, @"<table\b", RegexOptions.IgnoreCase)) return null;
        xv.styleM = Regex.Match(xv.html, @"<style\b[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase);
        if (!xv.styleM.Success) return null;
        xv.sheet = xv.styleM.Groups[1].Value;

        xv.rootM = Regex.Match(xv.sheet,
            @"\.(\w[\w-]*)\s*\{[^}]*\bfont\s*:\s*(xx-small|x-small|small|medium|large|x-large|xx-large)\s+['""]?([\w ]+)['""]?",
            RegexOptions.IgnoreCase);
        if (!xv.rootM.Success) return null;
        xv.rootCls = xv.rootM.Groups[1].Value;
        xv.face = xv.rootM.Groups[3].Value.Trim();
        if (WinMetricsFor(xv.face) is not { } wm) return null;
        if (TryParseCssFontSize(xv.rootM.Groups[2].Value) is not { } fs || fs <= 0) return null;
        xv.fs = fs;

        xv.starM = Regex.Match(xv.sheet,
            @"\." + Regex.Escape(xv.rootCls) + @"\s+\*\s*\{[^}]*\bdisplay\s*:\s*block[^}]*\bpadding-left\s*:\s*([\d.]+)\s*em",
            RegexOptions.IgnoreCase);
        if (!xv.starM.Success) return null;
        xv.starPad = double.Parse(xv.starM.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * xv.fs;

        // html/body margins zeroed — the fingerprint of the full-bleed dump sheet.
        if (!Regex.IsMatch(xv.sheet, @"(?:html\s*,\s*body|body)\s*(?:[^{}]*)?\{[^}]*\bmargin\s*:\s*0",
                RegexOptions.IgnoreCase)) return null;

        xv.rootDivM = Regex.Match(xv.html,
            @"<div\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(xv.rootCls) + @"\b[^""']*[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!xv.rootDivM.Success) return null;
        xv.rootInner = BalancedInner(xv.html, xv.rootDivM.Index + xv.rootDivM.Length, "div");
        if (xv.rootInner is null) return null;

        xv.rootRule = Regex.Match(xv.sheet, @"\." + Regex.Escape(xv.rootCls) + @"\s*\{([^}]*)\}");
        xv.rootDecl = xv.rootRule.Success ? xv.rootRule.Groups[1].Value : "";
        var rootPadLeft = CssEmLen(xv.rootDecl, "padding-left", xv.fs)
            ?? CssEmLen(xv.rootDecl, "padding", xv.fs) ?? 0;
        xv.rootStyle = AttrValue(xv.rootDivM.Value, "style") ?? "";
        var rootPadTop = CssEmLen(xv.rootStyle, "padding-top", xv.fs)
            ?? CssEmLen(xv.rootDecl, "padding", xv.fs) ?? 0;

        xv.doc = Document.Create();
        xv.docFontDict = new Core.PdfDictionary();
        xv.page = xv.doc.Pages.Add(xv.pageWidth, xv.pageHeight);
        EnsureFonts(xv.page, xv.docFontDict);
        xv.faceRes = xv.face.Replace(" ", "");
        EnsureFont(xv.page, xv.faceRes, "F8");
        EnsureFont(xv.page, xv.faceRes + "Bold", "F9");

        xv.lineBox = MetricLineHeight(xv.fs, wm.sum);
        xv.drop = MetricBaselineDrop(xv.fs, xv.lineBox, wm);
        xv.y = XvMarginTopPt + rootPadTop;
        xv.sb = new StringBuilder();
        xv.inv = System.Globalization.CultureInfo.InvariantCulture;

        Walk(xv, xv.rootInner, XvMarginLeftPt + rootPadLeft);
        xv.page.AddContentStream(Encoding.ASCII.GetBytes(xv.sb.ToString()));
        return xv.doc;
    }
}
