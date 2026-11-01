using System.Globalization;
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
        var metaCs = SafeRegex.Match(head, @"charset\s*=\s*[""']?\s*(?<cs>[\w-]+)", RegexOptions.IgnoreCase);
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
    internal static (string result, List<byte[]> svgs) ExtractInlineSvgs(string html)
    {
        var svgs = new List<byte[]>();
        if (string.IsNullOrEmpty(html) || html.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
            return (html, svgs);
        var list = svgs;
        return (SafeRegex.Replace(html, @"<svg\b[\s\S]*?</svg\s*>", m =>
        {
            var idx = list.Count;
            // Repair an UNTERMINATED attribute value on the root element
            // (`xmlns="http://…/svg width="184"` — the closing quote never
            // typed): a quoted value running into whitespace + another
            // attribute name is malformed (URIs and lengths carry no spaces),
            // and the XML load would otherwise reject the whole drawing.
            var svgText = SafeRegex.Replace(m.Value,
                @"([A-Za-z-]+\s*=\s*""[^""\s>]*)\s+(?=[A-Za-z-]+\s*=)", "$1\" ");
            list.Add(Encoding.UTF8.GetBytes(svgText));
            var rootEnd = svgText.IndexOf('>');
            var root = rootEnd > 0 ? svgText[..(rootEnd + 1)] : svgText;
            // Element size resolution (all in CSS px):
            // 1. an inline style width/height wins,
            // 2. else width/height presentation attributes,
            // 3. else a viewBox alone sizes the element to 150px high, width from the
            //    viewBox aspect ratio,
            // 4. else (no viewBox, no size) leave unsized — the rasterizer's natural
            //    content extent decides.
            double w = 0, h = 0;
            var stA = SafeRegex.Match(root, @"style\s*=\s*['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
            if (stA.Success)
            {
                w = SvgRootLengthPx(SafeRegex.Match(stA.Groups[1].Value, @"(?:^|[;\s])width\s*:\s*([^;]+)", RegexOptions.IgnoreCase));
                h = SvgRootLengthPx(SafeRegex.Match(stA.Groups[1].Value, @"(?:^|[;\s])height\s*:\s*([^;]+)", RegexOptions.IgnoreCase));
            }
            if (w <= 0 && h <= 0)
            {
                // (a length in any CSS unit: 10in is 960 px, not 10)
                w = SvgRootLengthPx(SafeRegex.Match(root, @"\bwidth\s*=\s*['""]?([^'""\s>]+)", RegexOptions.IgnoreCase));
                h = SvgRootLengthPx(SafeRegex.Match(root, @"\bheight\s*=\s*['""]?([^'""\s>]+)", RegexOptions.IgnoreCase));
            }
            if (w <= 0 && h <= 0)
            {
                var vb = SafeRegex.Match(root, @"viewBox\s*=\s*['""]\s*[-\d.]+[,\s]+[-\d.]+[,\s]+([\d.]+)[,\s]+([\d.]+)", RegexOptions.IgnoreCase);
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
        }, RegexOptions.IgnoreCase), svgs);
    }

    /// <summary>An svg root's width or height in CSS px, any unit (em of the 16px root); 0 when absent or relative.</summary>
    private static double SvgRootLengthPx(Match m) =>
        m.Success && CssLengthPx(m.Groups[1].Value, ControlRemPx) is { } px ? px : 0;

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
            { var e = head.IndexOf("?>", i, StringComparison.Ordinal); if (e < 0) { return false; } i = e + 2; continue; }
            if (string.CompareOrdinal(head, i, "<!--", 0, 4) == 0)
            { var e = head.IndexOf("-->", i, StringComparison.Ordinal); if (e < 0) { return false; } i = e + 3; continue; }
            if (head[i + 1] == '!')
            { var e = head.IndexOf('>', i); if (e < 0) { return false; } i = e + 1; continue; }
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
        return SafeRegex.Replace(html,
            @"<link(?=[^>]*\brel\s*=\s*[""']?stylesheet)[^>]*>",
            m =>
            {
                var hrefM = SafeRegex.Match(m.Value, @"\bhref\s*=\s*(?:""(?<h>[^""]*)""|'(?<h>[^']*)'|(?<h>[^\s>]+))",
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
                var mediaM = SafeRegex.Match(m.Value,
                    @"\bmedia\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
                    RegexOptions.IgnoreCase);
                var media = mediaM.Success
                    ? " media=\"" + mediaM.Groups["v"].Value.Trim() + "\"" : "";
                return "<style" + media + ">" + cssText + "</style>";
            }, RegexOptions.IgnoreCase);
    }

    /// <summary>The shortest base64 payload hoisted out of the markup: a picture or a font of a
    /// kilobyte or more. Smaller payloads (icons, bullets, tiles) stay where they are.</summary>
    private const int HoistedPayloadMinChars = 1024;

    /// <summary>The run of a hoisted payload its token keeps, so a format sniff over the base64 text
    /// (<c>iVBORw0KGgo</c> for a PNG, <c>R0lGOD</c> for a GIF) still reads the real bytes' head.</summary>
    private const int HoistedPayloadKeptChars = 16;

    private const string HoistedPayloadMark = "HOISTED";

    /// <summary>A token's length is never a multiple of four, which every base64 text's is: a reader
    /// that takes a token for a payload fails the way it fails on malformed base64, instead of
    /// decoding it to a picture of noise.</summary>
    private const int Base64QuantumChars = 4;

    private const string Base64Marker = ";base64,";

    /// <summary>How far ahead of its payload a data URI's <c>data:</c> may begin: the media type
    /// and its parameters.</summary>
    private const int DataUriHeadMaxChars = 64;

    /// <summary>The payloads hoisted out of the markup under conversion, by token
    /// (<see cref="HoistBase64Payloads"/>); null outside a conversion.</summary>
    [ThreadStatic] private static Dictionary<string, string>? HoistedPayloads;

    /// <summary>The markup with every long base64 picture or font payload replaced by a short token,
    /// the payload kept in <see cref="HoistedPayloads"/>. The converter reads the document many times
    /// over - every heuristic is a scan of the whole text - and with a megabyte photo inline each scan
    /// walked the photo too, which on the .NET Framework's regex interpreter multiplied into seconds for
    /// a two-page letter. Every reader of a payload goes through <see cref="Base64Payload"/>, which
    /// hands back the real bytes.</summary>
    private static string HoistBase64Payloads(string html)
    {
        StringBuilder? sb = null;
        var pos = 0;
        var at = html.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            var start = at + Base64Marker.Length;
            var end = start;
            while (end < html.Length && IsBase64Char(html[end])) end++;
            if (end - start >= HoistedPayloadMinChars && IsPictureOrFontDataUri(html, at))
            {
                sb ??= new StringBuilder();
                sb.Append(html, pos, start - pos).Append(HoistedToken(html.Substring(start, end - start)));
                pos = end;
            }
            at = html.IndexOf(Base64Marker, end, StringComparison.OrdinalIgnoreCase);
        }
        return sb is null ? html : sb.Append(html, pos, html.Length - pos).ToString();
    }

    private static bool IsBase64Char(char c)
        => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '=';

    /// <summary>Whether the data URI whose base64 marker sits at <paramref name="markerAt"/> carries a
    /// picture or a font: those are read through <see cref="Base64Payload"/>. Any other payload (a
    /// download link's file, a page's own HTML) stays in the markup, where it is copied out as it is.</summary>
    private static bool IsPictureOrFontDataUri(string html, int markerAt)
    {
        const string scheme = "data:";
        var d = html.LastIndexOf(scheme, markerAt, StringComparison.OrdinalIgnoreCase);
        if (d < 0 || markerAt - d > DataUriHeadMaxChars) return false;
        var mime = html.Substring(d + scheme.Length, markerAt - d - scheme.Length);
        return mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mime.StartsWith("font/", StringComparison.OrdinalIgnoreCase)
            || (mime.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                && mime.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>The token standing in for <paramref name="payload"/>: its first characters, the mark
    /// and the payload's number, padded to a length no base64 text has. The same payload placed
    /// again (a logo on every page) gets the token it already has.</summary>
    private static string HoistedToken(string payload)
    {
        var table = HoistedPayloads ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in table)
            if (kv.Value == payload) return kv.Key;
        var token = new StringBuilder(payload, 0, HoistedPayloadKeptChars, HoistedPayloadKeptChars + HoistedPayloadMark.Length + Base64QuantumChars * 2)
            .Append(HoistedPayloadMark).Append(table.Count.ToString(CultureInfo.InvariantCulture));
        while (token.Length % Base64QuantumChars != 1) token.Append('X');
        var key = token.ToString();
        table[key] = payload;
        return key;
    }

    /// <summary>The bytes of a base64 payload read out of the markup: a hoisted one through its token,
    /// any other decoded as it stands. Throws what <see cref="System.Convert.FromBase64String(string)"/>
    /// throws on malformed text.</summary>
    internal static byte[] Base64Payload(string text)
        => System.Convert.FromBase64String(
            HoistedPayloads is { } table && table.TryGetValue(text, out var payload) ? payload : text);

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
        if (Compat.IsWindows()) return null;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || name.Length == 0) return null;
            var e = System.IO.Directory.GetFiles(dir).FirstOrDefault(e => string.Equals(System.IO.Path.GetFileName(e), name,
                        StringComparison.OrdinalIgnoreCase));
            if (e is not null)
                return e;
        }
        catch { /* unreadable directory: unresolved, like the exact miss */ }
        return null;
    }

    private static byte[]? LoadConverterImage(string src, HtmlLoadOptions? options)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        // A URL-valued attribute is stripped of leading and trailing ASCII whitespace before it
        // is resolved, so `src=' Sample+Image.jpg'` names the file the directory holds; without
        // the strip the space becomes part of the name, the image never loads, and the page that
        // should have grown to hold it stays at the default sheet.
        src = src.Trim();
        var loader = options?.CustomLoaderOfExternalResources;
        if (loader is not null)
        {
            // A result the loader hands back is its answer: the data, or a refusal - cancelled, or no
            // data - that keeps the resource unloaded, so an application can stop the converter from
            // requesting what untrusted markup names. A loader that throws refused it too. Only a loader
            // that returns no result at all leaves the resource to the built-in resolution, and so does a
            // refused data: URI, which carries its payload and requests nothing.
            var isData = src.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
            try
            {
                // The loader is asked for the resource a browser would request: a relative reference
                // resolved against the page's web base. It is asked first, so an application decides
                // what the converter requests; only a loader with no answer leaves the fetch below.
                var result = loader(isData ? src : ResolveAgainstWebBase(UrlSeparators(src), options));
                if (result is not null && (result.Data is { Length: > 0 } || !isData))
                    return result.LoadingCancelled || result.Data is not { Length: > 0 } data ? null : data;
            }
            catch when (!isData) { return null; }
            catch { /* a data: URI decodes below */ }
        }
        try
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma <= 0) return null;
                if (src.IndexOf("base64", 0, comma, StringComparison.OrdinalIgnoreCase) >= 0)
                    return GifToPng(Base64Payload(src[(comma + 1)..]));
                // A data URI without base64 carries its payload percent-encoded (the way
                // an inline SVG chart is commonly written); the decoded text is the picture.
                return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(src[(comma + 1)..]));
            }
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return FetchRemoteImage(src); // browsers fetch; an unreachable URL falls back to alt text
            // Under a base path that is a site address, a relative reference names a resource on that
            // site - document-, root- and parent-relative alike - never a file on this disk.
            if (ResolveOnBasePathSite(src, options) is { } siteUrl)
                return FetchRemoteImage(siteUrl);
            // A protocol-relative URL ("//host/path") is a web address, as browsers read it; taken
            // as a path it names a network share, and every look probed that host and waited.
            if (src.StartsWith("//", StringComparison.Ordinal))
                return FetchRemoteImage("https:" + src);
            var path = IO.CallerPaths.FileUriToPath(src);
            // A local reference is a URL: its percent-escapes name the file decoded
            // (the Word mail's `test%20Mail_files/image002.gif` lives in "test Mail_files").
            if (path.IndexOf('%') >= 0) path = Uri.UnescapeDataString(path);
            // A page saved on Windows references its assets with backslash separators
            // ("Images\logo.png"). A browser treats the backslash as a path separator
            // in a file URL and so does NTFS; a POSIX file system reads it as part of
            // the file NAME, so every such reference silently misses (the email
            // newsletter lost all 42 of its images and collapsed to one page).
            if (!Compat.IsWindows()) path = path.Replace('\\', '/');
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
        if (!Compat.IsWindows()) return data;
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

    private static (int w, int h)? TryReadImagePixelSize(byte[] d)
    {
        int w = default;
        int h = default;

        if (d is null || d.Length < 24) return null;
        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
            h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
            return (w > 0 && h > 0) ? (w, h) : null;
        }
        // GIF87a/89a: logical screen size at offsets 6..9, little-endian.
        if (d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F')
        {
            w = d[6] | (d[7] << 8);
            h = d[8] | (d[9] << 8);
            return (w > 0 && h > 0) ? (w, h) : null;
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
                    return (w > 0 && h > 0) ? (w, h) : null;
                }
                i += 2 + seg;
            }
        }
        return null;
    }

    /// <summary>The EXIF orientation tag.</summary>
    private const int ExifOrientationTag = 0x0112;

    /// <summary>The clockwise turn, in degrees, a JPEG's EXIF orientation asks of a viewer
    /// (3 = 180, 6 = 90, 8 = 270); 0 when the picture is upright, carries no EXIF or is
    /// mirrored. Read from the APP1 segment's first image directory only: no pixel is decoded.</summary>
    private static int ReadJpegExifRotation(byte[] d)
    {
        if (d is null || d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return 0;
        var i = 2;
        while (i + 4 <= d.Length && d[i] == 0xFF)
        {
            var marker = d[i + 1];
            if (marker == 0xDA) return 0;                      // the scan: no EXIF ahead of it
            var seg = (d[i + 2] << 8) | d[i + 3];
            if (marker == 0xE1 && i + 2 + seg <= d.Length) return ExifRotation(d, i + 4, seg - 2);
            i += 2 + seg;
        }
        return 0;
    }

    private static int ExifRotation(byte[] d, int at, int length)
    {
        // "Exif\0\0", then a TIFF header: byte order, 42, the offset of the first directory.
        if (length < 14 || d[at] != 'E' || d[at + 1] != 'x' || d[at + 2] != 'i' || d[at + 3] != 'f') return 0;
        var tiff = at + 6;
        var little = d[tiff] == 'I';
        int U16(int p) => little ? d[p] | (d[p + 1] << 8) : (d[p] << 8) | d[p + 1];
        int U32(int p) => little ? U16(p) | (U16(p + 2) << 16) : (U16(p) << 16) | U16(p + 2);
        var dir = tiff + U32(tiff + 4);
        if (dir + 2 > at + length) return 0;
        var entries = U16(dir);
        for (var e = 0; e < entries; e++)
        {
            var entry = dir + 2 + e * 12;
            if (entry + 12 > at + length) return 0;
            if (U16(entry) != ExifOrientationTag) continue;
            return U16(entry + 8) switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        }
        return 0;
    }

    /// <summary>Parse the first CSS colour token (hex, rgb(), or a common
    /// named colour) found in <paramref name="text"/>. Null when none.</summary>
    internal static Color? ParseCssColor(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // A `url(...)` token names an image, never a colour - a data URI's base64 payload
        // spells any colour name (and any hex digit run) by chance, so the scan skips it.
        if (text.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0)
            text = SafeRegex.Replace(text, @"url\([^)]*\)", " ", RegexOptions.IgnoreCase);
        var hex = SafeRegex.Match(text, @"#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b");
        if (hex.Success)
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
            return Color.FromRgbBytes(System.Convert.ToInt32(h[..2], 16),
                System.Convert.ToInt32(h[2..4], 16), System.Convert.ToInt32(h[4..6], 16));
        }
        var rgb = SafeRegex.Match(text, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (rgb.Success)
            return Color.FromRgbBytes(int.Parse(rgb.Groups[1].Value),
                int.Parse(rgb.Groups[2].Value), int.Parse(rgb.Groups[3].Value));
        // rgba(): the expected render fills the base colour through a fill-alpha
        // graphics state; over the white page that composites to
        // c·a + 255·(1−a) per channel, which a flat fill reproduces ink-exactly.
        var rgba = SafeRegex.Match(text,
            @"rgba\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)");
        if (rgba.Success && double.TryParse(rgba.Groups[4].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var a))
        {
            a = Compat.Clamp(a, 0, 1);
            int Comp(string v) => (int)Math.Round(int.Parse(v) * a + 255 * (1 - a));
            return Color.FromRgbBytes(Comp(rgba.Groups[1].Value),
                Comp(rgba.Groups[2].Value), Comp(rgba.Groups[3].Value));
        }
        foreach (Match nm in SafeRegex.Matches(text, @"[a-zA-Z]+"))
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
                case "aliceblue": return Color.FromArgb(240, 248, 255);
                case "firebrick": return Color.FromArgb(178, 34, 34);
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

    /// <summary>An HTML5 canvas element with a straight-line 2D-context script becomes the inline SVG
    /// its calls paint (probed: a red fillRect with a cleared window draws as the filled box with a
    /// white window; the fallback text inside the element is not drawn). fillStyle/strokeStyle/
    /// lineWidth state, fillRect/clearRect/strokeRect, and beginPath/moveTo/lineTo/closePath/arc with
    /// fill/stroke are read; anything else in the script is skipped. The element's own CSS border
    /// draws as a frame inside the box.</summary>
    internal static string ReplaceCanvasesWithSvg(string html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("<canvas", StringComparison.OrdinalIgnoreCase) < 0)
            return html;
        var scripts = string.Join("\n", SafeRegex.Matches(html, @"<script\b[^>]*>([\s\S]*?)</script\s*>", RegexOptions.IgnoreCase)
            .Cast<Match>().Select(m => m.Groups[1].Value));
        return SafeRegex.Replace(html, @"<canvas\b([^>]*)>[\s\S]*?</canvas\s*>", m =>
        {
            var attrs = m.Groups[1].Value;
            string Attr(string name) => SafeRegex.Match(attrs, name + @"\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase)
                is { Success: true } am ? am.Groups[1].Value : "";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(Attr("width"), System.Globalization.NumberStyles.Float, inv, out var w) || w <= 0) w = 300;
            if (!double.TryParse(Attr("height"), System.Globalization.NumberStyles.Float, inv, out var h) || h <= 0) h = 150;
            var id = Attr("id");
            var sb = new StringBuilder();
            sb.Append(Compat.Format(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w:0.##}\" height=\"{h:0.##}\">"));
            var style = SafeRegex.Match(attrs, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            var border = style.Success
                ? SafeRegex.Match(style.Groups[1].Value, @"border\s*:\s*([\d.]+)px\s+\w+\s+([#\w(),.\s]+)", RegexOptions.IgnoreCase) : null;
            if (border is { Success: true })
                sb.Append(Compat.Format(CultureInfo.InvariantCulture, $"<rect x=\"{double.Parse(border.Groups[1].Value, inv) / 2:0.##}\" y=\"{double.Parse(border.Groups[1].Value, inv) / 2:0.##}\" width=\"{w - double.Parse(border.Groups[1].Value, inv):0.##}\" height=\"{h - double.Parse(border.Groups[1].Value, inv):0.##}\" fill=\"none\" stroke=\"{border.Groups[2].Value.Trim()}\" stroke-width=\"{border.Groups[1].Value}\"/>"));
            if (id.Length > 0) AppendCanvasScript(sb, scripts, id);
            sb.Append("</svg>");
            return sb.ToString();
        }, RegexOptions.IgnoreCase);
    }

    /// <summary>The 2D-context calls of the script that took the canvas by id, as SVG elements.</summary>
    private static void AppendCanvasScript(StringBuilder sb, string scripts, string canvasId)
    {
        var got = SafeRegex.Match(scripts, @"(\w+)\s*=\s*document\.getElementById\(\s*[""']" + Regex.Escape(canvasId) + @"[""']\s*\)");
        if (!got.Success) return;
        var ctxM = SafeRegex.Match(scripts[got.Index..], @"(\w+)\s*=\s*" + got.Groups[1].Value + @"\.getContext\(\s*[""']2d[""']\s*\)");
        if (!ctxM.Success) return;
        var ctx = ctxM.Groups[1].Value;
        var body = scripts[(got.Index + ctxM.Index + ctxM.Length)..];
        var fill = "#000"; var stroke = "#000"; var lineWidth = "1";
        var path = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string Num(string v) => double.TryParse(v.Trim(), System.Globalization.NumberStyles.Float, inv, out var d) ? d.ToString("0.##", inv) : "0";
        foreach (var groups in SafeRegex.Matches(body, ctx + @"\.(\w+)\s*(?:=\s*([""']?)([^;""']*)\2|\(([^)]*)\))\s*;").Select(st => st.Groups))
        {
            var name = groups[1].Value;
            var value = groups[3].Value.Trim();
            var args = groups[4].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(Num).ToArray();
            switch (name)
            {
                case "fillStyle": fill = value; break;
                case "strokeStyle": stroke = value; break;
                case "lineWidth": lineWidth = Num(value); break;
                case "fillRect" when args.Length == 4:
                    sb.Append($"<rect x=\"{args[0]}\" y=\"{args[1]}\" width=\"{args[2]}\" height=\"{args[3]}\" fill=\"{fill}\"/>"); break;
                case "clearRect" when args.Length == 4:
                    sb.Append($"<rect x=\"{args[0]}\" y=\"{args[1]}\" width=\"{args[2]}\" height=\"{args[3]}\" fill=\"#fff\"/>"); break;
                case "strokeRect" when args.Length == 4:
                    sb.Append($"<rect x=\"{args[0]}\" y=\"{args[1]}\" width=\"{args[2]}\" height=\"{args[3]}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{lineWidth}\"/>"); break;
                case "beginPath": path.Clear(); break;
                case "moveTo" when args.Length == 2: path.Append($"M{args[0]} {args[1]} "); break;
                case "lineTo" when args.Length == 2: path.Append($"L{args[0]} {args[1]} "); break;
                case "closePath": path.Append("Z "); break;
                case "arc" when args.Length >= 5:
                    sb.Append($"<circle cx=\"{args[0]}\" cy=\"{args[1]}\" r=\"{args[2]}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{lineWidth}\" data-pending=\"1\"/>"); break;
                case "stroke" when path.Length > 0:
                    sb.Append($"<path d=\"{path.ToString().Trim()}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{lineWidth}\"/>"); break;
                case "fill" when path.Length > 0:
                    sb.Append($"<path d=\"{path.ToString().Trim()}\" fill=\"{fill}\"/>"); break;
            }
        }
    }
}
