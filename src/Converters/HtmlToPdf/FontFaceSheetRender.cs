using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderFontFaceSheet(string html, HtmlLoadOptions? options,
        double defaultPageHeight)
    {
        var ff = new FontFaceSheetState();
        ff.html = html;
        ff.options = options;
        ff.defaultPageHeight = defaultPageHeight;
        ff.styleText = new StringBuilder();
        foreach (Match sm in SafeRegex.Matches(ff.html, @"<style[^>]*>(?<b>[\s\S]*?)</style\s*>", RegexOptions.IgnoreCase))
            ff.styleText.Append(sm.Groups["b"].Value).Append('\n');
        ff.css = ff.styleText.ToString();
        if (!ff.css.Contains("@font-face", System.StringComparison.OrdinalIgnoreCase)) return null;
        // data-URI faces belong to the styled-class data-font flow (StyledDoc.cs) —
        // this arm takes only the FILE-sourced variant of the shape.
        if (SafeRegex.IsMatch(ff.css, @"@font-face[^}]*url\(\s*[""']?data:", RegexOptions.Singleline)) return null;
        ff.bodyW = SafeRegex.Match(ff.css, @"body\s*\{[^}]*?width:\s*(?<w>[\d.]+)pt", RegexOptions.Singleline);
        if (!ff.bodyW.Success) return null;
        if (SafeRegex.IsMatch(ff.html, @"<(table|img|input|ul|ol)\b", RegexOptions.IgnoreCase)) return null;

        ff.faces = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<SheetFace>>(
            System.StringComparer.OrdinalIgnoreCase);
        ParseFontFaceRules(ff);
        if (ff.faces.Count == 0) return null;

        ff.rules = new System.Collections.Generic.List<(string Selector, string Body)>();
        foreach (var groups in SafeRegex.Matches(ff.css, @"(?<sel>[^{}@]+)\{(?<body>[^}]*)\}", RegexOptions.Singleline).Select(m => m.Groups))
            foreach (var sel in groups["sel"].Value.Split(','))
                ff.rules.Add((sel.Trim(), groups["body"].Value));

        ff.bodyM = SafeRegex.Match(ff.html, @"<body[^>]*>(?<b>[\s\S]*?)</body\s*>", RegexOptions.IgnoreCase);
        if (!ff.bodyM.Success) return null;
        ff.bodyInner = ff.bodyM.Groups["b"].Value;

        ff.paras = new System.Collections.Generic.List<(string Text, string PClass, string DivClasses, bool BoldTag)>();
        foreach (var dmGroups in SafeRegex.Matches(ff.bodyInner, @"<div(?<dattrs>[^>]*)>(?<dbody>[\s\S]*?)</div\s*>", RegexOptions.IgnoreCase).Select(dm => dm.Groups))
        {
            var divCls = SafeRegex.Match(dmGroups["dattrs"].Value, @"class=""(?<c>[^""]*)""").Groups["c"].Value;
            foreach (var pmGroups in SafeRegex.Matches(dmGroups["dbody"].Value, @"<p(?<pattrs>[^>]*)>(?<pbody>[\s\S]*?)</p\s*>", RegexOptions.IgnoreCase).Select(pm => pm.Groups))
            {
                var pCls = SafeRegex.Match(pmGroups["pattrs"].Value, @"class=""(?<c>[^""]*)""").Groups["c"].Value;
                var inner = pmGroups["pbody"].Value;
                var boldTag = SafeRegex.IsMatch(inner, @"^\s*<(b|strong)\b", RegexOptions.IgnoreCase);
                var text = SafeRegex.Replace(inner, "<[^>]+>", "");
                text = SafeRegex.Replace(DecodeEntities(text), @"\s+", " ").Trim();
                if (text.Length == 0) continue;
                ff.paras.Add((text, pCls, divCls, boldTag));
            }
        }
        if (ff.paras.Count == 0) return null;


        ff.firstChildMt = 0.0;
        ff.fcm = SafeRegex.Match(ff.css, @"body\s*>\s*:first-child\s*\{[^}]*margin-top:\s*(?<v>[\d.]+)pt", RegexOptions.Singleline);
        if (ff.fcm.Success) ff.firstChildMt = double.Parse(ff.fcm.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);

        ff.contentW = double.Parse(ff.bodyW.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture);
        ff.pageW = FontFaceSheetSideMargin * 2 + ff.contentW;
        ff.pageH = ff.defaultPageHeight;
        ff.left = FontFaceSheetSideMargin;

        ff.doc = new Document();
        ff.fontDict = new Core.PdfDictionary();
        ff.pg = ff.doc.Pages.Add(ff.pageW, ff.pageH);
        EnsureFonts(ff.pg, ff.fontDict);
        ff.sb = new StringBuilder();
        ff.inv = System.Globalization.CultureInfo.InvariantCulture;

        ff.flowTop = FontFaceSheetTopMargin;   // top-down flow position
        ff.pendingMargin = ff.firstChildMt;       // collapsed margin awaiting the next block

        foreach (var (text, pCls, divCls, boldTag) in ff.paras)
        {
            if (!LayoutFontFaceParagraph(ff, text, pCls, divCls, boldTag)) return null;
        }

        if (ff.sb.Length > 0) ff.pg.AddContentStream(Encoding.ASCII.GetBytes(ff.sb.ToString()));
        PruneUnusedFonts(ff.doc);
        return ff.doc;
    }
}
