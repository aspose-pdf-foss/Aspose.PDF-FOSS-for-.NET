using System.Text.RegularExpressions;

namespace Aspose.Pdf;

/// <summary>
/// Represents an HTML content fragment that can be added to PDF pages
/// via HeaderFooter.Paragraphs or Page.Paragraphs.
/// The HTML is converted to plain text for PDF content stream rendering.
/// </summary>
public class HtmlFragment : BaseParagraph
{
    /// <summary>
    /// Create an HtmlFragment from an HTML string.
    /// </summary>
    public HtmlFragment(string text)
    {
        HtmlContent = text;
    }

    /// <summary>Whether the HTML loader is allowed to break words at line ends. Stored only.</summary>
    public bool IsBreakWords { get; set; }

    /// <summary>Whether the resulting paragraph carries the fragment's own margin. Stored only.</summary>
    public bool IsParagraphHasMargin { get; set; }

    /// <summary>Shallow clone — the HTML string and reference-typed properties are shared.</summary>
    public override object Clone() => new HtmlFragment(HtmlContent ?? string.Empty)
    {
        HtmlLoadOptions = HtmlLoadOptions,
        Margin = Margin,
        IsInNewPage = IsInNewPage,
        IsKeptWithNext = IsKeptWithNext,
        TextState = TextState,
        IsBreakWords = IsBreakWords,
        IsParagraphHasMargin = IsParagraphHasMargin,
    };

    /// <summary>The raw HTML content.</summary>
    public string? HtmlContent { get; set; }

    /// <summary>Options for HTML loading (font embedding, encoding, etc.).</summary>
    public HtmlLoadOptions? HtmlLoadOptions { get; set; }

    /// <summary>Margins for the fragment. Auto-initialized so callers can write
    /// <c>fragment.Margin.Top = 10</c> on a freshly-constructed HtmlFragment
    /// without a NullReferenceException.</summary>
    public new MarginInfo Margin { get; set; } = new MarginInfo();

    /// <summary>Whether this fragment should start on a new page.</summary>
    public new bool IsInNewPage { get; set; }

    /// <summary>Keep this fragment together with the next paragraph on the same page.</summary>
    public new bool IsKeptWithNext { get; set; }

    /// <summary>Default text state applied to the converted HTML output.</summary>
    public Aspose.Pdf.Text.TextState? TextState { get; set; }

    /// <summary>
    /// Bounding rectangle of the rendered fragment on the page. Populated by
    /// the layout engine when the document is saved (or when ProcessParagraphs
    /// runs the layout pass). Empty before layout has executed.
    /// </summary>
    public System.Drawing.RectangleF Rectangle { get; internal set; }

    /// <summary>
    /// Strip HTML tags and return plain text.
    /// Handles common entities and normalizes whitespace.
    /// </summary>
    internal static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        // Remove script and style blocks
        var result = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);

        // Replace <br>, <p>, <div>, <li> with newlines
        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</(p|div|li|tr|h[1-6])>", "\n", RegexOptions.IgnoreCase);

        // Replace <img> tags with their alt text
        result = Regex.Replace(result, @"<img\b[^>]*\balt=['""]([^'""]*)['""][^>]*>",
            "$1", RegexOptions.IgnoreCase);

        // Remove all remaining tags
        result = Regex.Replace(result, @"<[^>]+>", "");

        // Decode common HTML entities
        result = result.Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");

        // Normalize whitespace
        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");

        return result.Trim();
    }
}

/// <summary>
/// Options for loading HTML content into PDF.
/// </summary>
public sealed class HtmlLoadOptions : LoadOptions
{
    public HtmlLoadOptions() { }

    /// <summary>
    /// Base path used to resolve relative URLs in the HTML (usually the
    /// directory of the HTML file being loaded).
    /// </summary>
    public HtmlLoadOptions(string basePath) { BasePath = basePath; }

    /// <summary>Base path for resolving relative resource URLs.</summary>
    public string? BasePath { get; internal set; }

    /// <summary>True when <see cref="BasePath"/> was derived from the loaded file's own
    /// directory rather than supplied by the caller. Resources resolve against an
    /// auto base, but the converter-HTML re-import routing (fixed layout vs plain
    /// reflow) depends on whether the stylesheet is reachable from CALLER-supplied
    /// context only.</summary>
    internal bool BasePathAutoDerived { get; set; }

    /// <summary>Whether to embed fonts used in the HTML.</summary>
    public bool IsEmbedFonts { get; set; }

    /// <summary>Render the entire HTML onto a single PDF page (canonical option).</summary>
    public bool IsRenderToSinglePage { get; set; }

    /// <summary>Input encoding for the HTML content (e.g., "utf-8").</summary>
    public string? InputEncoding { get; set; }

    /// <summary>Whether to disable font license verification checks
    /// (shadows the LoadOptions base-class flag for legacy callers that
    /// read HtmlLoadOptions.DisableFontLicenseVerifications directly).</summary>
    public new bool DisableFontLicenseVerifications { get; set; }

    /// <summary>HTML media type for rendering (Screen or Print).</summary>
    public HtmlMediaType HtmlMediaType { get; set; } = HtmlMediaType.Print;

    /// <summary>Whether to emit a logical-structure tree (tagged PDF) from the HTML structure. Stored only.</summary>
    public bool CreateLogicalStructure { get; set; }

    /// <summary>Whether @page CSS rules take precedence over the page size declared in <see cref="PageInfo"/>. Stored only.</summary>
    public bool IsPriorityCssPageRule { get; set; }

    /// <summary>How the generated PDF page layout reacts to wide HTML content. Stored only.</summary>
    public HtmlPageLayoutOption PageLayoutOption { get; set; } = HtmlPageLayoutOption.None;

    /// <summary>Caller-supplied delegate that resolves external resources referenced by the HTML.
    /// Stored only — the FOSS HTML loader does not currently dispatch through this hook.</summary>
    public LoadOptions.ResourceLoadingStrategy? CustomLoaderOfExternalResources;

    /// <summary>Credentials used when fetching external resources over the network. Stored only.</summary>
    public System.Net.ICredentials? ExternalResourcesCredentials;

    /// <summary>
    /// Page descriptor (size, margins) applied to the generated PDF. Tests use
    /// this to set margins or page size before loading HTML.
    /// </summary>
    public PageInfo PageInfo { get; set; } = CreateDefaultPageInfo();

    // The default descriptor's EXISTING margin is marked for per-side resolution
    // (see MarginInfo.HtmlPerSideDefaults). Assigning a new MarginInfo instead
    // would flag PageInfo.MarginAssigned — the "authored as a whole" signal — and
    // turn every default conversion's margins into explicit zeros.
    private static PageInfo CreateDefaultPageInfo()
    {
        var pi = new PageInfo();
        pi.Margin.HtmlPerSideDefaults = true;
        return pi;
    }

    /// <summary>Path to an APS intermediate file (debug artifact). Stored only.</summary>
    public string? ApsIntermediateFileIfAny { get; set; }

    /// <summary>Path to an XPS intermediate file (debug artifact). Stored only.</summary>
    public string? XpsIntermediateFileIfAny { get; set; }

    /// <summary>Render form-field borders in the converted PDF. Stored only.</summary>
    public bool ShowFieldsBorders { get; set; }

    /// <summary>Clip content to its declared area (overflow:hidden semantics). Stored only.</summary>
    public bool UseAreaClipping { get; set; }
}

/// <summary>
/// Specifies the media type for HTML rendering.
/// </summary>
public enum HtmlMediaType
{
    /// <summary>Screen media type.</summary>
    Screen,
    /// <summary>Print media type.</summary>
    Print,
}

/// <summary>How the generated PDF page layout reacts to wide HTML content.</summary>
public enum HtmlPageLayoutOption
{
    /// <summary>Use the size declared by <see cref="HtmlLoadOptions.PageInfo"/>.</summary>
    None = 0,
    /// <summary>Grow the page width to match the widest content block.</summary>
    FitToWidestContentWidth = 1,
    /// <summary>Scale content to the declared page width.</summary>
    ScaleToPageWidth = 2,
}
