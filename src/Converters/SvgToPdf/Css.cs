using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
    /// <summary>Inheritable presentation properties and their initial values.</summary>
    private static readonly Dictionary<string, string> InitialStyle = new(StringComparer.Ordinal)
    {
        ["fill"] = "black",
        ["stroke"] = "none",
        ["stroke-width"] = "1",
        ["stroke-linecap"] = "butt",
        ["stroke-linejoin"] = "miter",
        ["stroke-dasharray"] = "none",
        ["fill-rule"] = "nonzero",
        ["fill-opacity"] = "1",
        ["stroke-opacity"] = "1",
        ["font-family"] = "sans-serif",
        ["font-size"] = "16",
        ["font-weight"] = "normal",
        ["font-style"] = "normal",
        ["text-anchor"] = "start",
        ["text-decoration"] = "",
        ["color"] = "black",
        ["visibility"] = "visible",
    };

    // Properties consulted from attributes/CSS that are NOT inherited.
    private static readonly string[] NonInherited = { "opacity", "display", "clip-path", "mask" };

    private static void CollectDefsAndCss(XmlElement root, Ctx ctx)
    {
        foreach (var node in root.SelectNodes(".//*")!.Cast<XmlNode>().Prepend(root))
        {
            if (node is not XmlElement el) continue;
            var id = el.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && !ctx.Defs.ContainsKey(id))
                ctx.Defs[id] = el;
            if (el.LocalName == "style")
                ParseCss(ResolveImports(el.InnerText, ctx), ctx);
            // A stylesheet the document LINKS to is part of its style, and its
            // @font-face blocks are how an SVG ships a web font (one links a
            // Google Fonts sheet from <defs>).
            else if (el.LocalName == "link"
                     && el.GetAttribute("rel").Trim().Equals("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                var css = FetchCss(el.GetAttribute("href"), ctx);
                if (css is not null) ParseCss(ResolveImports(css, ctx), ctx);
            }
        }
    }

    /// <summary>Inline every <c>@import url(…)</c> the sheet names, so the imported
    /// rules (notably @font-face blocks) are parsed with the sheet's own.</summary>
    private static string ResolveImports(string css, Ctx ctx)
    {
        if (css.IndexOf("@import", StringComparison.OrdinalIgnoreCase) < 0) return css;
        return Regex.Replace(css,
            @"@import\s+(?:url\(\s*[""']?(?<u>[^""')]+)[""']?\s*\)|[""'](?<u>[^""']+)[""'])\s*;?",
            m => FetchCss(m.Groups["u"].Value, ctx) ?? "", RegexOptions.IgnoreCase);
    }

    /// <summary>Read a stylesheet named by an href/@import: an http(s) URL is fetched,
    /// a relative path resolves against the document's base directory. Null when the
    /// sheet cannot be read — the document then renders without those rules.</summary>
    private static string? FetchCss(string href, Ctx ctx)
    {
        href = href.Trim();
        if (href.Length == 0) return null;
        try
        {
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = HtmlToPdfConverter.FetchRemoteImage(href);
                return bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : null;
            }
            var path = href;
            if (!System.IO.Path.IsPathRooted(path) && ctx.BaseDir is { Length: > 0 } dir)
                path = System.IO.Path.Combine(dir, path);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    /// <summary>Record every @font-face program the sheet declares under its family
    /// name, fetching the first readable <c>src</c> url. TrueType/OpenType only — a
    /// WOFF payload the TrueType parser cannot read is skipped.</summary>
    private static void CollectFontFaces(string css, Ctx ctx)
    {
        foreach (Match m in Regex.Matches(css, @"@font-face\s*\{(?<b>[^{}]*)\}",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var body = m.Groups["b"].Value;
            var famM = Regex.Match(body, @"font-family\s*:\s*(?<f>[^;]+)", RegexOptions.IgnoreCase);
            if (!famM.Success) continue;
            var family = famM.Groups["f"].Value.Trim().Trim('"', '\'').Trim();
            if (family.Length == 0 || ctx.DeclaredFaces.ContainsKey(family)) continue;
            foreach (Match u in Regex.Matches(body, @"url\(\s*[""']?(?<u>[^""')]+)[""']?\s*\)",
                         RegexOptions.IgnoreCase))
            {
                var bytes = LoadFontProgram(u.Groups["u"].Value, ctx);
                if (bytes is null) continue;
                ctx.DeclaredFaces[family] = bytes;
                break;
            }
        }
    }

    /// <summary>Fetch a @font-face source and keep it only when it really parses as a
    /// TrueType/OpenType program (the format the embedder can subset).</summary>
    private static byte[]? LoadFontProgram(string url, Ctx ctx)
    {
        url = url.Trim();
        if (url.Length == 0) return null;
        byte[]? bytes = null;
        try
        {
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = url.IndexOf(',');
                if (comma > 0 && url.IndexOf("base64", 0, comma, StringComparison.OrdinalIgnoreCase) >= 0)
                    bytes = System.Convert.FromBase64String(url[(comma + 1)..]);
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                bytes = HtmlToPdfConverter.FetchRemoteImage(url);
            else
            {
                var path = url;
                if (!System.IO.Path.IsPathRooted(path) && ctx.BaseDir is { Length: > 0 } dir)
                    path = System.IO.Path.Combine(dir, path);
                if (System.IO.File.Exists(path)) bytes = System.IO.File.ReadAllBytes(path);
            }
        }
        catch { return null; }
        if (bytes is null || bytes.Length < 12) return null;
        try { new Text.GlyphOutlineParser(bytes); } catch { return null; }
        return bytes;
    }

    private static void ParseCss(string css, Ctx ctx)
    {
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        CollectFontFaces(css, ctx);
        var order = ctx.Css.Count;
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
        {
            var props = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var decl in rule.Groups[2].Value.Split(';'))
            {
                var idx = decl.IndexOf(':');
                if (idx <= 0) continue;
                var name = decl[..idx].Trim().ToLowerInvariant();
                var value = decl[(idx + 1)..].Trim();
                if (name.Length > 0 && value.Length > 0) props[name] = value;
            }
            if (props.Count == 0) continue;
            foreach (var sel in rule.Groups[1].Value.Split(','))
            {
                var s = sel.Trim();
                if (s.Length == 0) continue;
                var spec = s.StartsWith('#') ? 100 : s.Contains('.') ? 10 : 1;
                ctx.Css.Add((s, spec, order++, props));
            }
        }
    }

    /// <summary>Match simple selectors: <c>tag</c>, <c>.class</c>, <c>tag.class</c>, <c>#id</c>, <c>*</c>.</summary>
    private static bool SelectorMatches(string selector, XmlElement el)
    {
        if (selector == "*") return true;
        if (selector.StartsWith('#'))
            return el.GetAttribute("id") == selector[1..];
        string tagPart, classPart = "";
        var dot = selector.IndexOf('.');
        if (dot >= 0) { tagPart = selector[..dot]; classPart = selector[(dot + 1)..]; }
        else tagPart = selector;
        if (tagPart.Length > 0 && !string.Equals(tagPart, el.LocalName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (classPart.Length > 0)
        {
            var classes = el.GetAttribute("class").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // handle multi-class selectors a.b.c
            foreach (var cls in classPart.Split('.'))
                if (Array.IndexOf(classes, cls) < 0) return false;
        }
        return true;
    }

    private static readonly string[] PresentationAttrs =
    {
        "fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin",
        "stroke-dasharray", "fill-rule", "fill-opacity", "stroke-opacity", "opacity",
        "font-family", "font-size", "font-weight", "font-style", "text-anchor",
        "text-decoration", "color", "visibility", "display", "clip-path", "mask",
    };

    /// <summary>
    /// Compute the effective style for an element: inherited values, then CSS rules
    /// (by specificity), then presentation attributes, then the inline style attribute.
    /// Presentation attributes rank below CSS per the SVG spec, but above inherited.
    /// </summary>
    private static Dictionary<string, string> ResolveStyle(XmlElement el, Dictionary<string, string> parent, Ctx ctx)
    {
        var style = new Dictionary<string, string>(parent, StringComparer.Ordinal);
        foreach (var ni in NonInherited) style.Remove(ni);

        // 1. presentation attributes
        foreach (var name in PresentationAttrs)
        {
            var v = el.GetAttribute(name);
            if (!string.IsNullOrEmpty(v)) style[name] = v.Trim();
        }

        // 2. CSS rules (override presentation attributes)
        List<(int Spec, int Order, Dictionary<string, string> Props)>? matched = null;
        foreach (var (sel, spec, order, props) in ctx.Css)
        {
            if (SelectorMatches(sel, el))
                (matched ??= new()).Add((spec, order, props));
        }
        if (matched is not null)
        {
            matched.Sort((a, b) => a.Spec != b.Spec ? a.Spec - b.Spec : a.Order - b.Order);
            foreach (var (_, _, props) in matched)
                foreach (var kv in props)
                    style[kv.Key] = kv.Value;
        }

        // 3. inline style attribute (highest)
        var inline = el.GetAttribute("style");
        if (!string.IsNullOrEmpty(inline))
        {
            foreach (var decl in inline.Split(';'))
            {
                var idx = decl.IndexOf(':');
                if (idx <= 0) continue;
                var name = decl[..idx].Trim().ToLowerInvariant();
                var value = decl[(idx + 1)..].Trim();
                if (name.Length > 0 && value.Length > 0) style[name] = value;
            }
        }

        // currentColor indirection
        if (style.TryGetValue("fill", out var f) && f == "currentColor")
            style["fill"] = style.GetValueOrDefault("color", "black");
        if (style.TryGetValue("stroke", out var st) && st == "currentColor")
            style["stroke"] = style.GetValueOrDefault("color", "black");

        return style;
    }

    private static string Prop(Dictionary<string, string> style, string name) =>
        style.GetValueOrDefault(name, InitialStyle.GetValueOrDefault(name, ""));
}
