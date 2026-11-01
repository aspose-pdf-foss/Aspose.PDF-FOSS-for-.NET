using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The &lt;base href&gt; of the page being converted, or null. Per conversion.</summary>
    [ThreadStatic] private static string? DocumentBaseHref;

    private static readonly Regex BaseHrefElement = new(@"<base\b[^>]*?\bhref\s*=\s*(?:""(?<h>[^""]*)""|'(?<h>[^']*)'|(?<h>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The &lt;base href&gt; a page declares, or null.</summary>
    private static string? ReadBaseHref(string html)
    {
        var match = SafeRegex.Match(BaseHrefElement, html);
        return match.Success ? DecodeEntities(match.Groups["h"].Value.Trim()) : null;
    }

    /// <summary>A relative resource reference resolved as a URL against the page's web base - its
    /// &lt;base href&gt;, else an http(s) HtmlLoadOptions base path - the way a browser resolves it:
    /// document-, root- and parent-relative references and protocol-relative ones alike: the URL a
    /// custom resource loader is asked for. A reference with a scheme of its own, or one with no web
    /// base to resolve against, comes back unchanged (a local base path resolves on disk).</summary>
    private static string ResolveAgainstWebBase(string src, HtmlLoadOptions? options)
    {
        if (WebBase(options) is not { } webBase) return src;
        // "data:", "http:", "file:" and the like name the resource themselves; a drive letter
        // ("C:") parses as a one-letter scheme and is a local path.
        if (Uri.TryCreate(src, UriKind.Absolute, out var absolute) && absolute.Scheme.Length > 1
            && !src.StartsWith("/", StringComparison.Ordinal))
            return src;
        return Uri.TryCreate(webBase, src, out var resolved) ? resolved.AbsoluteUri : src;
    }

    /// <summary>The http(s) URL a relative reference names on the site of an http(s) base path - the one the
    /// converter fetches it from - or null when the base path is not a web address or the reference names a
    /// resource of another kind (a file URI, a drive path). The page's &lt;base href&gt; plays no part: the
    /// converter requests only what the application's own base path points it at.</summary>
    private static string? ResolveOnBasePathSite(string src, HtmlLoadOptions? options)
        => AsWebUri(options?.BasePath) is { } site && Uri.TryCreate(site, UrlSeparators(src), out var url) && IsWebUri(url)
            ? url.AbsoluteUri
            : null;

    /// <summary>A reference with its backslashes read as the path separators a browser's URL parser reads
    /// them as (the file and web schemes): a page saved on Windows writes "Resources\x.png", and a loader
    /// is asked for "Resources/x.png", the name that resolves on any file system.</summary>
    private static string UrlSeparators(string src) => src.Replace('\\', '/');

    /// <summary>The http(s) URL relative references resolve against, or null when the page has none: an
    /// http(s) base path, else the page's &lt;base href&gt; when no base path was given. A page read from a
    /// folder keeps resolving on disk whatever &lt;base href&gt; it carries from where it was saved.</summary>
    private static Uri? WebBase(HtmlLoadOptions? options)
    {
        var basePath = options?.BasePath;
        return AsWebUri(basePath is { Length: > 0 } ? basePath : DocumentBaseHref);
    }

    /// <summary>The text as an absolute http(s) URL, or null.</summary>
    private static Uri? AsWebUri(string? text)
        => text is { Length: > 0 } && Uri.TryCreate(text, UriKind.Absolute, out var uri) && IsWebUri(uri) ? uri : null;

    private static bool IsWebUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
