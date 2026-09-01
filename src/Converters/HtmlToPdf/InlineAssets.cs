using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // charset from an explicit name, else null for the sniffing fallback.
    private static string? DecodeByName(string name, byte[] data, int offset)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n is "utf-8" or "utf8") return Encoding.UTF8.GetString(data, offset, data.Length - offset);
        if (n is "iso-8859-1" or "latin1" or "latin-1" or "windows-1252" or "cp1252" or "ansi" or "us-ascii" or "ascii")
            return Text.Cp1252.GetString(offset == 0 ? data : data[offset..]);
        // .NET Core ships the legacy code pages (windows-1251, shift_jis, …) behind
        // CodePagesEncodingProvider — without registering it GetEncoding throws and
        // an explicit InputEncoding silently fell through to the meta/UTF-8 sniff,
        // mojibaking every high byte.
        if (!CodePagesRegistered)
        {
            try { Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); }
            catch { /* provider unavailable: GetEncoding below still covers the built-ins */ }
            CodePagesRegistered = true;
        }
        try { return Encoding.GetEncoding(n).GetString(data, offset, data.Length - offset); }
        catch { return null; }
    }

    private static bool CodePagesRegistered;

    /// <summary>Decode raw HTML bytes to text, resolving the character encoding the way a browser
    /// does when converting a legacy document: an explicit <see cref="HtmlLoadOptions.InputEncoding"/>
    /// wins, then a BOM, then a <c>&lt;meta charset&gt;</c> declaration; with none of those, valid
    /// UTF-8 is decoded as UTF-8 but non-UTF-8 single-byte bytes fall back to Windows-1252 (the
    /// de-facto legacy default) instead of turning every high byte into a U+FFFD that later renders
    /// as '?'.</summary>
    private static string DecodeHtmlBytes(byte[] data, HtmlLoadOptions? options)
    {
        if (data is null || data.Length == 0) return string.Empty;

        if (options?.InputEncoding is { Length: > 0 } declaredOpt
            && DecodeByName(declaredOpt, data, 0) is { } byOpt)
            return byOpt;

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);

        // <meta charset="…"> / <meta http-equiv="Content-Type" content="…; charset=…">, scanned
        // over the document prologue (ASCII-safe) before the encoding is known.
        var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 2048));
        var metaCs = Regex.Match(head, @"charset\s*=\s*[""']?\s*(?<cs>[\w-]+)", RegexOptions.IgnoreCase);
        if (metaCs.Success)
        {
            var metaName = metaCs.Groups["cs"].Value.Trim().ToLowerInvariant();
            // A meta claiming UTF-16 that was READ OUT OF an ASCII prologue scan
            // is lying about its own bytes — real UTF-16 (NUL every other byte,
            // and BOM-less at that) could never have matched the scan. Fall to
            // the sniff (the résumé corpus ships such utf-8 files).
            if (!metaName.StartsWith("utf-16", StringComparison.Ordinal)
                && !metaName.StartsWith("utf16", StringComparison.Ordinal)
                && DecodeByName(metaName, data, 0) is { } byMeta)
                return byMeta;
        }

        // No declaration: strict UTF-8, else Windows-1252 for legacy single-byte content.
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data); }
        catch (DecoderFallbackException) { return Text.Cp1252.GetString(data); }
    }

    /// <summary>Resolve an &lt;img&gt; source to raw bytes: the load options' custom resource
    /// loader first (it may serve remote/opaque URIs), then a data: URI, then a local file.
    /// Returns null when nothing can be loaded.</summary>
    /// <summary>Replace each inline <c>&lt;svg&gt;…&lt;/svg&gt;</c> element with an
    /// <c>&lt;img src="inline-svg:i" width="W" height="H"&gt;</c> placeholder (W/H taken
    /// from the root attributes when present) and collect the extracted markup. The
    /// placeholders flow through the normal image-block layout and rasterize through the
    /// SVG engine at draw time.</summary>
    internal static string ExtractInlineSvgs(string html, out List<byte[]> svgs)
    {
        svgs = new List<byte[]>();
        if (string.IsNullOrEmpty(html) || html.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
            return html;
        var list = svgs;
        return Regex.Replace(html, @"<svg\b[\s\S]*?</svg\s*>", m =>
        {
            var idx = list.Count;
            // Repair an UNTERMINATED attribute value on the root element
            // (`xmlns="http://…/svg width="184"` — the closing quote never
            // typed): a quoted value running into whitespace + another
            // attribute name is malformed (URIs and lengths carry no spaces),
            // and the XML load would otherwise reject the whole drawing.
            var svgText = Regex.Replace(m.Value,
                @"([A-Za-z-]+\s*=\s*""[^""\s>]*)\s+(?=[A-Za-z-]+\s*=)", "$1\" ");
            list.Add(Encoding.UTF8.GetBytes(svgText));
            var rootEnd = svgText.IndexOf('>');
            var root = rootEnd > 0 ? svgText[..(rootEnd + 1)] : svgText;
            // Element size resolution (all in CSS px):
            // 1. an inline style width/height wins;
            // 2. else width/height presentation attributes;
            // 3. else a viewBox alone sizes the element to 150px high, width from the
            //    viewBox aspect ratio;
            // 4. else (no viewBox, no size) leave unsized — the rasterizer's natural
            //    content extent decides.
            double w = 0, h = 0;
            var stA = Regex.Match(root, @"style\s*=\s*['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
            if (stA.Success)
            {
                var sw = Regex.Match(stA.Groups[1].Value, @"(?:^|[;\s])width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                var sh = Regex.Match(stA.Groups[1].Value, @"(?:^|[;\s])height\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (sw.Success) double.TryParse(sw.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out w);
                if (sh.Success) double.TryParse(sh.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out h);
            }
            if (w <= 0 && h <= 0)
            {
                var wA = Regex.Match(root, @"\bwidth\s*=\s*['""]?([\d.]+)", RegexOptions.IgnoreCase);
                var hA = Regex.Match(root, @"\bheight\s*=\s*['""]?([\d.]+)", RegexOptions.IgnoreCase);
                if (wA.Success) double.TryParse(wA.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out w);
                if (hA.Success) double.TryParse(hA.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out h);
            }
            if (w <= 0 && h <= 0)
            {
                var vb = Regex.Match(root, @"viewBox\s*=\s*['""]\s*[-\d.]+[,\s]+[-\d.]+[,\s]+([\d.]+)[,\s]+([\d.]+)", RegexOptions.IgnoreCase);
                if (vb.Success
                    && double.TryParse(vb.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var vbW)
                    && double.TryParse(vb.Groups[2].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var vbH)
                    && vbW > 0 && vbH > 0)
                {
                    h = 150.0;
                    w = 150.0 * vbW / vbH;
                }
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var attrs = (w > 0 ? $" width=\"{w.ToString("0.##", inv)}\"" : "")
                      + (h > 0 ? $" height=\"{h.ToString("0.##", inv)}\"" : "");
            return $"<img src=\"inline-svg:{idx}\"{attrs} />";
        }, RegexOptions.IgnoreCase);
    }

    /// <summary>True when the bytes are an SVG document (optionally behind a BOM,
    /// XML declaration, comments, or a DOCTYPE).</summary>
    internal static bool IsSvgBytes(byte[]? d)
    {
        if (d is null || d.Length < 5) return false;
        var head = Encoding.UTF8.GetString(d, 0, Math.Min(d.Length, 1024));
        var i = 0;
        while (true)
        {
            while (i < head.Length && (char.IsWhiteSpace(head[i]) || head[i] == '\uFEFF')) i++;
            if (i + 4 >= head.Length || head[i] != '<') return false;
            if (head[i + 1] == '?')
            { var e = head.IndexOf("?>", i, StringComparison.Ordinal); if (e < 0) return false; i = e + 2; continue; }
            if (string.CompareOrdinal(head, i, "<!--", 0, 4) == 0)
            { var e = head.IndexOf("-->", i, StringComparison.Ordinal); if (e < 0) return false; i = e + 3; continue; }
            if (head[i + 1] == '!')
            { var e = head.IndexOf('>', i); if (e < 0) return false; i = e + 1; continue; }
            return string.Compare(head, i, "<svg", 0, 4, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }

    /// <summary>Replace every <c>&lt;link rel="stylesheet" href="…"&gt;</c> with an inline
    /// <c>&lt;style&gt;…&lt;/style&gt;</c> carrying the fetched CSS text, so the legacy flow's
    /// <c>&lt;style&gt;</c>-scanning CSS collectors apply linked rules the same as inline ones.
    /// The stylesheet is fetched through <see cref="LoadConverterImage"/> (the custom loader,
    /// then the BasePath); a tag whose target can't be read is left in place unchanged.</summary>
    private static string InlineLinkedStylesheets(string html, HtmlLoadOptions? options)
    {
        if (html.IndexOf("<link", StringComparison.OrdinalIgnoreCase) < 0) return html;
        return Regex.Replace(html,
            @"<link(?=[^>]*\brel\s*=\s*[""']?stylesheet)[^>]*>",
            m =>
            {
                var hrefM = Regex.Match(m.Value, @"\bhref\s*=\s*(?:""(?<h>[^""]*)""|'(?<h>[^']*)'|(?<h>[^\s>]+))",
                    RegexOptions.IgnoreCase);
                if (!hrefM.Success) return m.Value;
                var bytes = LoadConverterImage(DecodeEntities(hrefM.Groups["h"].Value), options);
                if (bytes is null || bytes.Length == 0) return m.Value;
                // Strip a UTF-8 BOM so the first rule parses.
                var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                var cssText = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
                // Guard against a </style> in the stylesheet prematurely closing the block.
                cssText = cssText.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase);
                // Carry the link's own media list onto the generated block. The CSS
                // collectors read every block regardless — a screen sheet still supplies
                // the flow's rules — but a scan that must respect the medium (the page's
                // own size, which only a print-applicable sheet may set) can now tell.
                var mediaM = Regex.Match(m.Value,
                    @"\bmedia\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
                    RegexOptions.IgnoreCase);
                var media = mediaM.Success
                    ? " media=\"" + mediaM.Groups["v"].Value.Trim() + "\"" : "";
                return "<style" + media + ">" + cssText + "</style>";
            }, RegexOptions.IgnoreCase);
    }

    /// <summary>Fetch a remote &lt;img&gt; source the way a browser does. Null on any
    /// failure (timeout, non-success, non-image) — the caller then falls back to the
    /// alt-text/placeholder path exactly as for an unreadable local file. Shared with
    /// the markdown converter (an HTML image block inside a .md file).</summary>
    internal static byte[]? FetchRemoteImage(string url) =>
        RemoteImageCache.GetOrAdd(url, static u =>
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                // Some CDNs refuse requests without a UA.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                var bytes = http.GetByteArrayAsync(u).GetAwaiter().GetResult();
                return bytes.Length > 0 ? bytes : null;
            }
            catch { return null; }
        });

    /// <summary>A local resource path, matched exactly and then case-insensitively
    /// within its directory. Windows resolves every href case-blind for free (NTFS),
    /// so a saved page referencing COREV15.css next to a COREV15.CSS on disk loads its
    /// stylesheet there and silently loses it on a case-sensitive file system - probed:
    /// the SharePoint export converted with NO stylesheet on Linux (unstyled uniform
    /// cells, the page widened past the expected width and the size gate failed) and with
    /// the full 308 KB sheet on Windows. The retry runs only after an exact miss and
    /// only off Windows, so Windows resolution is byte-identical.</summary>
    private static string? ResolveLocalResource(string path)
    {
        if (System.IO.File.Exists(path)) return path;
        if (OperatingSystem.IsWindows()) return null;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || name.Length == 0) return null;
            foreach (var e in System.IO.Directory.GetFiles(dir))
                if (string.Equals(System.IO.Path.GetFileName(e), name,
                        StringComparison.OrdinalIgnoreCase))
                    return e;
        }
        catch { /* unreadable directory: unresolved, like the exact miss */ }
        return null;
    }

    private static byte[]? LoadConverterImage(string src, HtmlLoadOptions? options)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        var loader = options?.CustomLoaderOfExternalResources;
        if (loader is not null)
        {
            try
            {
                var result = loader(src);
                if (result?.Data is { Length: > 0 } data) return data;
            }
            catch { /* fall through to the built-in resolution */ }
        }
        try
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma > 0 && src.IndexOf("base64", 0, comma, StringComparison.OrdinalIgnoreCase) >= 0)
                    return GifToPng(System.Convert.FromBase64String(src[(comma + 1)..]));
                return null;
            }
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return FetchRemoteImage(src); // browsers fetch; an unreachable URL falls back to alt text
            var path = IO.CallerPaths.FileUriToPath(src);
            // A page saved on Windows references its assets with backslash separators
            // ("Images\logo.png"). A browser treats the backslash as a path separator
            // in a file URL and so does NTFS; a POSIX file system reads it as part of
            // the file NAME, so every such reference silently misses (the email
            // newsletter lost all 42 of its images and collapsed to one page).
            if (!OperatingSystem.IsWindows()) path = path.Replace('\\', '/');
            // Resolve a relative src against the document's base directory (the HtmlLoadOptions
            // BasePath), the way a browser resolves it against the page URL — otherwise a relative
            // image reference is looked up against the process working directory and never found.
            if (!System.IO.Path.IsPathRooted(path) && options?.BasePath is { Length: > 0 } baseDir)
            {
                var combined = System.IO.Path.Combine(baseDir, path);
                if (ResolveLocalResource(combined) is { } hit) return BmpToPng(System.IO.File.ReadAllBytes(hit));
                // Callers commonly pass the page FILE (or its URL) as the base path — resolve
                // against its containing directory, like a browser resolves against the page.
                if (System.IO.Path.GetDirectoryName(baseDir) is { Length: > 0 } parentDir)
                {
                    combined = System.IO.Path.Combine(parentDir, path);
                    if (ResolveLocalResource(combined) is { } hit2) return BmpToPng(System.IO.File.ReadAllBytes(hit2));
                }
            }
            return ResolveLocalResource(path) is { } hit3
                ? BmpToPng(System.IO.File.ReadAllBytes(hit3)) : null;
        }
        catch { return null; }
    }

    /// <summary>Read an image's pixel width/height from a PNG (IHDR) or JPEG (SOF) header
    /// without decoding pixels. Returns false for formats this can't parse.</summary>
    /// <summary>A GIF (whatever MIME the data URI claims — the corpus ships GIF89a
    /// bytes labelled image/png) converts to PNG here: the drawing pipeline decodes
    /// PNG/JPEG only, while a browser renders the REAL format regardless of the
    /// declared one. Transparency survives the 32bpp round-trip. Non-GIF bytes
    /// pass through untouched.</summary>
    /// <summary>BMP rides the same System.Drawing round-trip as GIF data URIs:
    /// the drawing pipeline decodes PNG/JPEG only, while a browser renders
    /// a <c>&lt;img src="x.bmp"&gt;</c> like any other bitmap. Non-BMP bytes
    /// pass through untouched (a local .gif keeps its calibrated placeholder
    /// path).</summary>
    private static byte[] BmpToPng(byte[] data)
        => data.Length >= 10 && data[0] == (byte)'B' && data[1] == (byte)'M'
            ? RasterToPng(data) : data;

    private static byte[] GifToPng(byte[] data)
    {
        if (data.Length < 10
            || data[0] != (byte)'G' || data[1] != (byte)'I' || data[2] != (byte)'F')
            return data;
        return RasterToPng(data);
    }

    /// <summary>Re-encode any System.Drawing-decodable raster as 32bpp PNG;
    /// the input bytes come back unchanged when decoding fails.</summary>
    private static byte[] RasterToPng(byte[] data)
    {
        // The re-encode runs through the System.Drawing image codecs, which exist only
        // on Windows. Off Windows the bytes pass through untouched - the same answer the
        // catch below produces, without paying for a PlatformNotSupportedException.
        if (!OperatingSystem.IsWindows()) return data;
#pragma warning disable CA1416
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var src = System.Drawing.Image.FromStream(ms);
            using var bmp = new System.Drawing.Bitmap(src.Width, src.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            using var oms = new System.IO.MemoryStream();
            bmp.Save(oms, System.Drawing.Imaging.ImageFormat.Png);
            return oms.ToArray();
        }
        catch { return data; }
#pragma warning restore CA1416
    }

    private static bool TryReadImagePixelSize(byte[] d, out int w, out int h)
    {
        w = 0; h = 0;
        if (d is null || d.Length < 24) return false;
        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
            h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
            return w > 0 && h > 0;
        }
        // GIF87a/89a: logical screen size at offsets 6..9, little-endian.
        if (d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F')
        {
            w = d[6] | (d[7] << 8);
            h = d[8] | (d[9] << 8);
            return w > 0 && h > 0;
        }
        if (d[0] == 0xFF && d[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) { i++; continue; }
                int m = d[i + 1];
                if (m is 0xD8 or 0xD9 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                int seg = (d[i + 2] << 8) | d[i + 3];
                if ((m >= 0xC0 && m <= 0xCF) && m != 0xC4 && m != 0xC8 && m != 0xCC)
                {
                    h = (d[i + 5] << 8) | d[i + 6];
                    w = (d[i + 7] << 8) | d[i + 8];
                    return w > 0 && h > 0;
                }
                i += 2 + seg;
            }
        }
        return false;
    }

    /// <summary>Parse the first CSS colour token (hex, rgb(), or a common
    /// named colour) found in <paramref name="text"/>. Null when none.</summary>
    internal static Color? ParseCssColor(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var hex = Regex.Match(text, @"#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b");
        if (hex.Success)
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
            return Color.FromRgbBytes(System.Convert.ToInt32(h[..2], 16),
                System.Convert.ToInt32(h[2..4], 16), System.Convert.ToInt32(h[4..6], 16));
        }
        var rgb = Regex.Match(text, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (rgb.Success)
            return Color.FromRgbBytes(int.Parse(rgb.Groups[1].Value),
                int.Parse(rgb.Groups[2].Value), int.Parse(rgb.Groups[3].Value));
        // rgba(): the expected render fills the base colour through a fill-alpha
        // graphics state; over the white page that composites to
        // c·a + 255·(1−a) per channel, which a flat fill reproduces ink-exactly.
        var rgba = Regex.Match(text,
            @"rgba\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)");
        if (rgba.Success && double.TryParse(rgba.Groups[4].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var a))
        {
            a = Math.Clamp(a, 0, 1);
            int Comp(string v) => (int)Math.Round(int.Parse(v) * a + 255 * (1 - a));
            return Color.FromRgbBytes(Comp(rgba.Groups[1].Value),
                Comp(rgba.Groups[2].Value), Comp(rgba.Groups[3].Value));
        }
        foreach (Match nm in Regex.Matches(text, @"[a-zA-Z]+"))
        {
            switch (nm.Value.ToLowerInvariant())
            {
                case "black": return Color.FromArgb(0, 0, 0);
                case "white": return Color.FromArgb(255, 255, 255);
                case "red": return Color.FromArgb(255, 0, 0);
                case "green": return Color.FromArgb(0, 128, 0);
                case "blue": return Color.FromArgb(0, 0, 255);
                case "yellow": return Color.FromArgb(255, 255, 0);
                case "gray": case "grey": return Color.FromArgb(128, 128, 128);
                case "darkgray": case "darkgrey": return Color.FromArgb(169, 169, 169);
                case "dimgray": case "dimgrey": return Color.FromArgb(105, 105, 105);
                case "orange": return Color.FromArgb(255, 165, 0);
                case "purple": return Color.FromArgb(128, 0, 128);
                case "navy": return Color.FromArgb(0, 0, 128);
                case "gainsboro": return Color.FromArgb(220, 220, 220);
                case "silver": return Color.FromArgb(192, 192, 192);
                case "lightgray": case "lightgrey": return Color.FromArgb(211, 211, 211);
                case "lightgreen": return Color.FromArgb(144, 238, 144);
                case "lightblue": return Color.FromArgb(173, 216, 230);
                case "lightyellow": return Color.FromArgb(255, 255, 224);
                case "whitesmoke": return Color.FromArgb(245, 245, 245);
                case "beige": return Color.FromArgb(245, 245, 220);
                case "pink": return Color.FromArgb(255, 192, 203);
                case "brown": return Color.FromArgb(165, 42, 42);
                case "maroon": return Color.FromArgb(128, 0, 0);
                case "olive": return Color.FromArgb(128, 128, 0);
                case "teal": return Color.FromArgb(0, 128, 128);
                case "aqua": case "cyan": return Color.FromArgb(0, 255, 255);
                case "fuchsia": case "magenta": return Color.FromArgb(255, 0, 255);
                case "lime": return Color.FromArgb(0, 255, 0);
            }
        }
        return null;
    }
}
