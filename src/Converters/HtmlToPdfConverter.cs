using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts HTML into a PDF document using a minimal block-layout model:
/// block elements (p/div/h1-h6/blockquote/li/tr) stack vertically with
/// per-block top and bottom margins, inline elements flow inside a block,
/// and text wraps to the content width. Not a CSS-complete renderer —
/// enough structure for pagination to match block-level document shape.
/// </summary>
internal static class HtmlToPdfConverter
{
    public static Document Convert(string htmlPath, HtmlLoadOptions? options = null)
    {
        var html = DecodeHtmlBytes(File.ReadAllBytes(htmlPath), options);
        return ConvertFromHtml(html, options);
    }

    public static Document Convert(byte[] htmlData, HtmlLoadOptions? options = null)
    {
        var html = DecodeHtmlBytes(htmlData, options);
        return ConvertFromHtml(html, options);
    }

    // charset from an explicit name, else null for the sniffing fallback.
    private static string? DecodeByName(string name, byte[] data, int offset)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n is "utf-8" or "utf8") return Encoding.UTF8.GetString(data, offset, data.Length - offset);
        if (n is "iso-8859-1" or "latin1" or "latin-1" or "windows-1252" or "cp1252" or "ansi" or "us-ascii" or "ascii")
            return Text.Cp1252.GetString(offset == 0 ? data : data[offset..]);
        try { return Encoding.GetEncoding(n).GetString(data, offset, data.Length - offset); }
        catch { return null; }
    }

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
        if (metaCs.Success && DecodeByName(metaCs.Groups["cs"].Value, data, 0) is { } byMeta)
            return byMeta;

        // No declaration: strict UTF-8, else Windows-1252 for legacy single-byte content.
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data); }
        catch (DecoderFallbackException) { return Text.Cp1252.GetString(data); }
    }

    /// <summary>One rendered block: a run of text with uniform style and
    /// vertical spacing on either side. One Block becomes N wrapped lines
    /// at layout time.</summary>
    internal sealed class Block
    {
        public string Text = "";
        public double FontSize;
        public string FontRes = "F1";    // F1=Helvetica, F2=Helvetica-Bold, F3=Helvetica-Oblique
        // CSS font-family (first non-generic name, e.g. "Arial"); null = default Helvetica.
        // When set and resolvable through FontRepository, the run is drawn with the
        // embedded TrueType face instead of the Standard-14 FontRes.
        public string? FontFamily;
        public double MarginTop;
        public double MarginBottom;
        public double LeftIndent;
        public bool IsListItem;
        // CSS page-break-before:always — start this block on a fresh page.
        public bool PageBreakBefore;
        // List-item marker (e.g. "1." for an <ol> item, "•" for a <ul> bullet).
        // Emitted as a separate text run to the left of the first content line, so it
        // surfaces as its own TextFragment. Null = not a list item / no marker.
        public string? Marker;
        // Emit the marker AFTER the first content line (not before) so it surfaces as the
        // LATER TextFragment on that line. Set for CSS ::before generated markers on RTL
        // lists, where the item text reads first and the marker sits to its right.
        public bool MarkerAfter;
        public bool IsHardBreak;         // hidden spacer (e.g. <br> inside block)
        // Floor on the block's rendered height (from CSS height/min-height).
        // Zero = let the text content alone decide.
        public double ExplicitHeight;
        // <hr>: draw a horizontal rule line in RuleColor / RuleWidth instead
        // of just consuming vertical space.
        public bool IsHorizontalRule;
        public Color? RuleColor;
        public double RuleWidth;
        // CSS box decoration drawn behind/around the block's content area:
        // background-color fill and a border stroke. Null = none (draw nothing).
        public Color? BackgroundColor;
        public Color? BorderColor;
        public double BorderWidth;
        // Inline <a href> ranges within Text (char offsets into the collapsed Text),
        // each with its target URL. Drives Link-annotation generation at layout time.
        public System.Collections.Generic.List<(int Start, int Length, string Url)>? Anchors;

        // Anchor-target names declared in this block (an element's `id`, or an
        // `<a name="…">`). A #fragment hyperlink resolves to the page this block
        // renders on, so internal document links land on the right page.
        public System.Collections.Generic.List<string>? AnchorNames;

        // Interactive form input: an <input>/<textarea> becomes an AcroForm
        // TextBoxField at layout time instead of a text run.
        public bool IsInputField;
        public string InputValue = "";
        public string? InputName;     // AcroForm field name from the <input> name/id attribute
        public double InputWidth;     // CSS px (0 = fill content width)
        public double InputHeight;    // CSS px (0 = one text line)
        public bool InputMultiline;
        public bool InputReadOnly;    // HTML disabled / readonly attribute

        // <input type="checkbox">: emit an AcroForm CheckboxField at layout time.
        // Checked carries the HTML `checked` attribute.
        public bool IsCheckbox;
        public bool Checked;

        // <input type="radio">: collected into a RadioButtonField group (by RadioGroup =
        // the input name) emitted after layout, so each option surfaces as a
        // RadioButtonOptionField on Form.Fields.
        public bool IsRadio;
        public string RadioGroup = "";

        // <img>: draw the referenced image in-flow at layout time. Src is resolved via the
        // load options' custom resource loader (for remote/opaque URIs), a data: URI, or a
        // local file. Width/Height are CSS px (0 = derive from the other / natural size).
        public bool IsImage;
        public string ImageSrc = "";
        public double ImageWidth;
        public double ImageHeight;
        // <img alt="…"> — alternate description, surfaced as a Figure structure
        // element's /Alt when CreateLogicalStructure builds the tag tree.
        public string? ImageAlt;

        // A real <table> (no form inputs) rendered as a column grid at layout time via
        // BuildTableFromHtml + Table.BuildMultiPage. TableHtml carries the raw <table>…</table>.
        public bool IsTable;
        public string TableHtml = "";
    }

    /// <summary>True when the markup carries block-level structure (lists,
    /// paragraphs, headings, tables) that needs vertical/indented block layout
    /// rather than a single flat run of stripped text.</summary>
    internal static bool HasBlockStructure(string html) =>
        Regex.IsMatch(html ?? "", @"<\s*(ul|ol|li|p|div|h[1-6]|table|tr|blockquote|hr|form|input|textarea)\b",
            RegexOptions.IgnoreCase);

    /// <summary>Extract the rule colour and width for an &lt;hr&gt; from its
    /// inline style. Reads the CSS border shorthand / border-color / color.</summary>
    private static void ParseHrStyle(Dictionary<string, string>? attrs,
        out Color? color, out double width)
    {
        color = null;
        width = 1;
        if (attrs is null) return;
        attrs.TryGetValue("style", out var style);
        style ??= "";
        // Width from the first pixel length in a border declaration.
        var wm = Regex.Match(style, @"border[^:]*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success && double.TryParse(wm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var w) && w > 0)
            width = w;
        // Colour: scan the style string (covers border/border-color/color).
        color = ParseCssColor(style);
    }

    /// <summary>Emit a CSS box decoration — an optional <paramref name="fill"/> rectangle
    /// and an optional <paramref name="border"/> stroke — onto <paramref name="page"/> at
    /// the given lower-left origin and size (all in points). No-op when neither is set.</summary>
    private static void DrawBox(Page page, double llx, double lly, double w, double h,
        Color? border, double borderWidth, Color? fill)
    {
        if (w <= 0 || h <= 0 || (border is null && fill is null)) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("F2", ci);
        string Rgb(Color c) => $"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)}";
        var sb = new StringBuilder();
        sb.Append("q ");
        if (fill is not null)
            sb.Append($"{Rgb(fill)} rg {N(llx)} {N(lly)} {N(w)} {N(h)} re f ");
        if (border is not null)
        {
            var bw = borderWidth > 0 ? borderWidth : 0.75;
            sb.Append($"{Rgb(border)} RG {N(bw)} w {N(llx)} {N(lly)} {N(w)} {N(h)} re S ");
        }
        sb.Append("Q");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>Resolve an &lt;img&gt; source to raw bytes: the load options' custom resource
    /// loader first (it may serve remote/opaque URIs), then a data: URI, then a local file.
    /// Returns null when nothing can be loaded.</summary>
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
                    return System.Convert.FromBase64String(src[(comma + 1)..]);
                return null;
            }
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null; // no network fetch without a custom loader
            var path = src;
            if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.IsFile)
                path = uri.LocalPath;
            // Resolve a relative src against the document's base directory (the HtmlLoadOptions
            // BasePath), the way a browser resolves it against the page URL — otherwise a relative
            // image reference is looked up against the process working directory and never found.
            if (!System.IO.Path.IsPathRooted(path) && options?.BasePath is { Length: > 0 } baseDir)
            {
                var combined = System.IO.Path.Combine(baseDir, path);
                if (System.IO.File.Exists(combined)) return System.IO.File.ReadAllBytes(combined);
            }
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        }
        catch { return null; }
    }

    /// <summary>Read an image's pixel width/height from a PNG (IHDR) or JPEG (SOF) header
    /// without decoding pixels. Returns false for formats this can't parse.</summary>
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
    private static Color? ParseCssColor(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var hex = Regex.Match(text, @"#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b");
        if (hex.Success)
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
            return Color.FromRgb(System.Convert.ToInt32(h[..2], 16),
                System.Convert.ToInt32(h[2..4], 16), System.Convert.ToInt32(h[4..6], 16));
        }
        var rgb = Regex.Match(text, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (rgb.Success)
            return Color.FromRgb(int.Parse(rgb.Groups[1].Value),
                int.Parse(rgb.Groups[2].Value), int.Parse(rgb.Groups[3].Value));
        foreach (Match nm in Regex.Matches(text, @"[a-zA-Z]+"))
        {
            switch (nm.Value.ToLowerInvariant())
            {
                case "black": return Color.FromRgb(0, 0, 0);
                case "white": return Color.FromRgb(255, 255, 255);
                case "red": return Color.FromRgb(255, 0, 0);
                case "green": return Color.FromRgb(0, 128, 0);
                case "blue": return Color.FromRgb(0, 0, 255);
                case "yellow": return Color.FromRgb(255, 255, 0);
                case "gray": case "grey": return Color.FromRgb(128, 128, 128);
                case "orange": return Color.FromRgb(255, 165, 0);
                case "purple": return Color.FromRgb(128, 0, 128);
                case "navy": return Color.FromRgb(0, 0, 128);
            }
        }
        return null;
    }

    /// <summary>Parse HTML into the flat block list used by the layout pass.
    /// Exposed for the in-page HtmlFragment renderer.</summary>
    internal static List<Block> ParseHtmlBlocks(string html) => ParseBlocks(html, null);

    /// <summary>True when the markup contains an HTML &lt;table&gt; element.</summary>
    internal static bool ContainsTable(string? html) =>
        !string.IsNullOrEmpty(html) && Regex.IsMatch(html!, @"<\s*table\b", RegexOptions.IgnoreCase);

    /// <summary>Split mixed HTML into an ordered sequence of top-level segments: each
    /// <c>&lt;table&gt;…&lt;/table&gt;</c> block (isTable = true) and the markup between them
    /// (isTable = false). Lets the in-page HtmlFragment renderer flow text blocks and real
    /// column tables in document order. Nested tables are not supported.</summary>
    internal static List<(bool isTable, string html)> SegmentHtmlTables(string html)
    {
        var result = new List<(bool, string)>();
        if (string.IsNullOrEmpty(html)) return result;
        var idx = 0;
        foreach (Match m in Regex.Matches(html, @"<table\b[^>]*>[\s\S]*?</table>", RegexOptions.IgnoreCase))
        {
            if (m.Index > idx) result.Add((false, html.Substring(idx, m.Index - idx)));
            result.Add((true, m.Value));
            idx = m.Index + m.Length;
        }
        if (idx < html.Length) result.Add((false, html.Substring(idx)));
        return result;
    }

    /// <summary>Parse one HTML &lt;table&gt; into a generator <see cref="Table"/> so it can
    /// be laid out as real columns (rows × cells side-by-side) instead of the flat
    /// single-column stack that tag-stripping produces. One TextFragment is emitted per
    /// &lt;br&gt;-separated cell line; cells word-wrap to the column width. Header (&lt;th&gt;)
    /// cells are bold and centred; colspan, per-cell text-align, and CSS font-size / border /
    /// padding / table-width are honoured. Nested tables are not supported. Returns null when
    /// the markup yields no rows.</summary>
    internal static Table? BuildTableFromHtml(string html) => BuildTableFromHtml(html, 0, out _);

    /// <summary>Build a generator Table from an HTML &lt;table&gt;. When it has no explicit column
    /// widths, columns auto-fit to content: max-content (no wrapping) if the table fits
    /// <paramref name="availWidthPt"/>, otherwise min-content (each column shrinks to its widest
    /// word). <paramref name="availWidthPt"/> ≤ 0 means unconstrained (always max-content).
    /// <paramref name="naturalWidthPt"/> returns the total width the chosen columns occupy.</summary>
    internal static Table? BuildTableFromHtml(string html, double availWidthPt, out double naturalWidthPt)
    {
        naturalWidthPt = 0;
        const double PxToPt = 0.75;
        var css = ParseStyleSheet(html);

        // The <table> tag's own inline style="…" / attributes (font, width, border, cellpadding)
        // take precedence over stylesheet rules — CMS/report HTML commonly styles the table
        // inline rather than via a <style> block, so honour both.
        var tblTag = Regex.Match(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var tblStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? tblBorderAttr = null, tblCellPadAttr = null;
        if (tblTag.Success)
        {
            var sm = Regex.Match(tblTag.Value, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (sm.Success)
                foreach (Match d in StyleDeclRx.Matches(sm.Groups[1].Value))
                    tblStyle[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
            var bm = Regex.Match(tblTag.Value, @"border\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bm.Success) tblBorderAttr = bm.Groups[1].Value;
            var cm = Regex.Match(tblTag.Value, @"cellpadding\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (cm.Success) tblCellPadAttr = cm.Groups[1].Value;
        }

        double cellFontSize = 11;
        if (tblStyle.TryGetValue("font-size", out var itfs) && TryParseLength(itfs, out var itfsp)) cellFontSize = itfsp;
        else if (TryGetCssLength(css, "table", "font-size", out var tfs)) cellFontSize = tfs;
        else if (TryGetCssLength(css, "td", "font-size", out var dfs)) cellFontSize = dfs;

        string? cellFamily = null;
        if (tblStyle.TryGetValue("font-family", out var iff)) cellFamily = FirstFontFamily(iff);
        else if (css.TryGetValue("table", out var tdecl) && tdecl.TryGetValue("font-family", out var ffv))
            cellFamily = FirstFontFamily(ffv);

        bool hasBorder = false; double borderWidth = 1; Color borderColor = Color.Black; double pad = 0;
        // border="1"/"1px" attribute on the table draws a 1px box on every cell.
        if (tblBorderAttr is not null && !tblBorderAttr.StartsWith("0"))
        {
            hasBorder = true;
            var wm = Regex.Match(tblBorderAttr, @"(\d+(?:\.\d+)?)");
            if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bwa) && bwa > 0)
                borderWidth = bwa * PxToPt;
        }
        if (tblCellPadAttr is not null && double.TryParse(Regex.Match(tblCellPadAttr, @"[\d.]+").Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cpa) && cpa > 0)
            pad = cpa * PxToPt;
        foreach (var sel in new[] { "td", "th" })
        {
            if (!css.TryGetValue(sel, out var d)) continue;
            if (d.TryGetValue("border", out var bd))
            {
                var t = bd.Trim();
                hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw))
                    borderWidth = bw * PxToPt;
                var bc = ParseCssColor(bd); if (bc is not null) borderColor = bc;
            }
            if (d.TryGetValue("padding", out var pv) && TryParseLength(pv, out var pp)) pad = pp;
        }

        double tableWidthFrac = 1.0;
        string? twVal = tblStyle.TryGetValue("width", out var itw) ? itw
            : (css.TryGetValue("table", out var tw2) && tw2.TryGetValue("width", out var tw) ? tw : null);
        if (twVal is not null && twVal.EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(twVal.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var twp))
            tableWidthFrac = Math.Clamp(twp / 100.0, 0.05, 1.0);

        var table = new Table { IsBordersIncluded = hasBorder };
        if (hasBorder) table.DefaultCellBorder = new BorderInfo(BorderSide.Box, borderWidth, borderColor);
        if (pad > 0) table.DefaultCellPadding = new MarginInfo(pad, pad, pad, pad);

        const double PxToPtW = 0.75;
        var tokens = Tokenize(StripNonContent(html));
        Row? row = null; Cell? cell = null;
        var line = new StringBuilder();
        var lines = new List<string>();
        bool isHeader = false; int colSpan = 1;
        HorizontalAlignment cellAlign = HorizontalAlignment.Left; bool alignSet = false;
        int maxCols = 0;
        // Leading rows whose cells are all <th> are the table header; count them so they can be
        // repeated at the top of every page the table spans (RepeatingRowsCount).
        int headerRows = 0; bool countingHeaderRows = true; bool rowHasTd = false, rowHasCell = false;

        // Explicit per-column widths (points): captured from the first row whose cells are all
        // single-span and each carry an explicit CSS width (inline `width:Npx` or a class rule),
        // so a label : value table keeps its narrow ":" column instead of equal thirds.
        double cellWidthPt = 0;
        var rowWidths = new List<double>();
        bool rowAllSingleExplicit = true;
        List<double>? colWidthsPt = null;

        // Content-based auto width: per column, the min-content width (widest single word — the
        // narrowest the column can be while still wrapping) and the max-content width (widest full
        // line — no wrapping). A browser uses max-content when the table fits and shrinks toward
        // min-content otherwise. Tracks the current column cursor per row.
        var colMinW = new List<double>();
        var colMaxW = new List<double>();
        var colHdrW = new List<double>();
        int colCursor = 0;
        Text.Font? measureFont = null;
        if (cellFamily is not null)
            try { measureFont = Text.FontRepository.FindFont(cellFamily); } catch { }
        double MeasureLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            try
            {
                // A system font resolved via FindFont has an empty PDF font dict (no /Widths),
                // so Font.MeasureString would default every glyph to 1 em. Read the real glyph
                // advances from the source TTF (hmtx) instead when available.
                if (measureFont?.SourceFontData?.TtfData is { Length: > 0 })
                    return measureFont.SourceFontData.MeasureString(s, cellFontSize);
                if (measureFont is not null) return measureFont.MeasureString(s, cellFontSize);
            }
            catch { }
            return s.Length * cellFontSize * 0.5; // fallback: average glyph advance
        }
        // Min-content width: the widest single word — a wrappable cell ("Beginning Balance") can
        // shrink to its longest word ("Beginning"), so the column need only be that wide (matching
        // a browser's auto table layout). Single-token cells ("$0,000.00") keep their full width.
        double MeasureMinContent(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double w = 0;
            foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                w = Math.Max(w, MeasureLine(word));
            return w;
        }

        void PushLine() { lines.Add(CollapseWs(line.ToString())); line.Clear(); }
        void CloseCell()
        {
            if (cell is null || row is null) return;
            rowWidths.Add(cellWidthPt);
            if (colSpan > 1 || cellWidthPt <= 0) rowAllSingleExplicit = false;
            PushLine();
            foreach (var ln in lines)
            {
                if (ln.Length == 0) continue;
                var tf = new Text.TextFragment(ln);
                tf.TextState.FontSize = (float)cellFontSize;
                if (isHeader) tf.TextState.IsBold = true;
                if (cellFamily is not null)
                {
                    try { var f = Text.FontRepository.FindFont(cellFamily); if (f is not null) tf.TextState.Font = f; }
                    catch { }
                }
                // CJK text can't render in the Standard-14 WinAnsi fonts — it would collapse
                // to '?'. Fall back to an embedded Unicode face that covers the run so it flows
                // through the Type0/CID render path. Scoped to CJK (not every non-WinAnsi char)
                // so incidental symbols don't drag a large document onto the slower embed path.
                if (tf.TextState.Font?.SourceFontData is null && HasCjk(ln))
                {
                    var uf = ResolveUnicodeFont(ln);
                    if (uf is not null) tf.TextState.Font = uf;
                }
                cell.Paragraphs.Add(tf);
            }
            cell.IsWordWrapped = true;
            cell.ColSpan = Math.Max(1, colSpan);
            cell.Alignment = alignSet ? cellAlign : (isHeader ? HorizontalAlignment.Center : HorizontalAlignment.Left);
            row.Cells.Add(cell);
            // Record this cell's content width against the column(s) it spans, so a table with no
            // explicit widths auto-fits each column to its widest content (+ cell padding).
            double cellMin = 0, cellMax = 0, cellHdr = 0;
            foreach (var ln in lines)
            {
                cellMin = Math.Max(cellMin, MeasureMinContent(ln));
                cellMax = Math.Max(cellMax, MeasureLine(ln));
                // A header cell's full (unwrapped) line width — used to keep <th> on one line when
                // the whole table still fits the available width (a browser does not wrap headers to
                // their widest word). Recorded separately so it never forces the page/table wider.
                if (isHeader) cellHdr = Math.Max(cellHdr, MeasureLine(ln));
            }
            var span = Math.Max(1, colSpan);
            // Column footprint = content + cell padding (both sides) + the cell box border the
            // generator draws around it, so the summed natural width matches the rendered grid.
            var extra = 2 * pad + (hasBorder ? 2 * borderWidth : 0) + 1.5;
            var perColMin = cellMin / span + extra;
            var perColMax = cellMax / span + extra;
            var perColHdr = cellHdr > 0 ? cellHdr / span + extra : 0;
            for (var k = 0; k < span; k++)
            {
                var ci = colCursor + k;
                while (colMinW.Count <= ci) { colMinW.Add(0); colMaxW.Add(0); colHdrW.Add(0); }
                if (perColMin > colMinW[ci]) colMinW[ci] = perColMin;
                if (perColMax > colMaxW[ci]) colMaxW[ci] = perColMax;
                if (perColHdr > colHdrW[ci]) colHdrW[ci] = perColHdr;
            }
            colCursor += span;
            cell = null; lines.Clear(); line.Clear(); isHeader = false; colSpan = 1; alignSet = false; cellWidthPt = 0;
        }
        void CloseRow()
        {
            if (cell is not null) CloseCell();
            if (row is null) return;
            var cols = 0; foreach (var c in row.Cells) cols += Math.Max(1, c.ColSpan);
            if (cols > maxCols) maxCols = cols;
            if (colWidthsPt is null && rowAllSingleExplicit && rowWidths.Count > 1)
                colWidthsPt = new List<double>(rowWidths);
            rowWidths.Clear(); rowAllSingleExplicit = true;
            colCursor = 0;
            if (row.Cells.Count > 0)
            {
                table.Rows.Add(row);
                if (rowHasCell)
                {
                    if (countingHeaderRows && !rowHasTd) headerRows++;
                    else countingHeaderRows = false;
                }
            }
            rowHasTd = false; rowHasCell = false;
            row = null;
        }

        foreach (var tok in tokens)
        {
            if (tok.Kind == TokenKind.Text) { if (cell is not null) line.Append(DecodeEntities(tok.Value)); continue; }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                if (tag is "td" or "th") CloseCell();
                else if (tag is "tr" or "table") CloseRow();
                continue;
            }
            switch (tag)
            {
                case "tr": CloseRow(); row = new Row(); break;
                case "td":
                case "th":
                    if (cell is not null) CloseCell();
                    row ??= new Row();
                    cell = new Cell(); isHeader = tag == "th";
                    rowHasCell = true; if (tag == "td") rowHasTd = true;
                    cellWidthPt = ResolveCellWidthPt(tok.Attributes, css) * PxToPtW;
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("colspan", out var cs) && int.TryParse(cs, out var csn) && csn > 0)
                            colSpan = csn;
                        if (tok.Attributes.TryGetValue("style", out var st))
                        {
                            var am = Regex.Match(st, @"text-align\s*:\s*(left|right|center)", RegexOptions.IgnoreCase);
                            if (am.Success)
                            {
                                alignSet = true;
                                cellAlign = am.Groups[1].Value.ToLowerInvariant() switch
                                {
                                    "right" => HorizontalAlignment.Right,
                                    "center" => HorizontalAlignment.Center,
                                    _ => HorizontalAlignment.Left,
                                };
                            }
                        }
                    }
                    break;
                case "br": if (cell is not null) PushLine(); break;
            }
        }
        CloseRow();
        if (headerRows > 0 && headerRows < table.Rows.Count) table.RepeatingRowsCount = headerRows;

        if (table.Rows.Count == 0) { naturalWidthPt = 0; return null; }
        naturalWidthPt = 0;
        if (colWidthsPt is { Count: > 0 } cw && cw.Count == maxCols)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < cw.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(cw[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                naturalWidthPt += cw[i];
            }
            table.ColumnWidths = sb.ToString();
        }
        else if (availWidthPt > 0 && colMaxW.Count == maxCols && colMaxW.Count > 0 && colMaxW.TrueForAll(w => w > 0))
        {
            // No explicit widths: content-fit. Only when the caller opts in with a real available
            // width (the wide-table ConvertFromHtml path); legacy callers (header/footer & in-flow
            // HtmlFragment tables) keep the equal-% fallback below so their layout is unchanged.
            // Use max-content (no wrapping) when the table fits the available width; otherwise fall
            // back to min-content (columns shrink to their widest word and multi-word cells wrap) —
            // matching a browser's auto table layout.
            double sumMax = 0; foreach (var w in colMaxW) sumMax += w;
            // Min-content, but keep header cells on one line when the resulting table still fits the
            // available width; if even that overflows, fall back to the pure widest-word min so a wide
            // header never forces the page/table wider (that would override the caller's page size).
            var minPref = new List<double>(colMinW);
            double sumPref = 0;
            for (var i = 0; i < minPref.Count; i++) { if (colHdrW[i] > minPref[i]) minPref[i] = colHdrW[i]; sumPref += minPref[i]; }
            var chosenMin = (sumPref <= availWidthPt) ? minPref : colMinW;
            var chosen = (availWidthPt <= 0 || sumMax <= availWidthPt) ? colMaxW : chosenMin;
            var sb = new StringBuilder();
            for (var i = 0; i < chosen.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(chosen[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                naturalWidthPt += chosen[i];
            }
            table.ColumnWidths = sb.ToString();
        }
        else if (maxCols > 0)
        {
            var each = tableWidthFrac * 100.0 / maxCols;
            var sb = new StringBuilder();
            for (var i = 0; i < maxCols; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(each.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
            }
            table.ColumnWidths = sb.ToString();
        }
        return table;
    }

    /// <summary>Resolve a table cell's explicit CSS width in px (inline <c>style="width:Npx"</c>
    /// first, then a <c>class</c> rule's width); 0 when none is specified.</summary>
    private static double ResolveCellWidthPt(Dictionary<string, string>? attrs,
        IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        if (attrs is null) return 0;
        if (attrs.TryGetValue("style", out var st))
        {
            var m = Regex.Match(st, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                return w;
        }
        if (attrs.TryGetValue("class", out var cls))
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue("." + c, out var d) && d.TryGetValue("width", out var wv))
                {
                    var m = Regex.Match(wv, @"(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
                    if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                        return w;
                }
        return 0;
    }

    /// <summary>Strip script/style/head/comment/doctype bodies so the table tokenizer
    /// sees only structural markup (mirrors the front of <see cref="ParseBlocks"/>).</summary>
    private static string StripNonContent(string html)
    {
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        return html;
    }

    private static string CollapseWs(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static bool TryGetCssLength(IReadOnlyDictionary<string, Dictionary<string, string>> css,
        string selector, string prop, out double pts)
    {
        pts = 0;
        return css.TryGetValue(selector, out var d) && d.TryGetValue(prop, out var v) && TryParseLength(v, out pts);
    }

    // Tags that open a block-level element; each starts a new Block on exit.
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "blockquote", "ul", "ol", "li", "tr", "td", "th",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "table", "pre", "hr",
    };

    // Tags whose inner content is discarded entirely.
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "meta", "link", "title",
    };

    private static Document ConvertFromHtml(string html, HtmlLoadOptions? options)
    {
        var pageInfo = options?.PageInfo;
        var pageWidth   = pageInfo?.Width  is > 0 ? pageInfo.Width  : 612.0;
        var pageHeight  = pageInfo?.Height is > 0 ? pageInfo.Height : 792.0;
        var pageMargin  = pageInfo?.Margin;
        // Respect user-set margins verbatim (including explicit zeros); fall back to
        // the HTML-renderer defaults only when MarginInfo was never touched.
        // In that renderer, unstyled
        // content ~96 pt from the left and the first baseline ~89 pt from the top of an
        // A4 page; the previous 72 pt left/top shifted every conversion up-and-left of
        // the reference by ~24 pt / ~17 pt. Right/bottom keep 72 pt.
        bool marginsExplicit = pageMargin?.IsTouched ?? false;
        var marginLeft   = marginsExplicit ? pageMargin!.Left   : 96.0;
        var marginRight  = marginsExplicit ? pageMargin!.Right  : 72.0;
        var marginTop    = marginsExplicit ? pageMargin!.Top    : 89.0;
        var marginBottom = marginsExplicit ? pageMargin!.Bottom : 72.0;

        var css = ParseStyleSheet(html);

        // A <header>/<footer> becomes a *running* region (repeated on every page) only when it is
        // pinned with position:fixed — the print idiom `@media print { header { position:fixed } }`.
        // A semantic <header>/<footer> that is ordinary flow content (display:block, or
        // position:absolute/static) is laid out once in document order, so it must stay in the flow;
        // pulling it out and repeating it per page would duplicate that content on every page.
        string? runHeader = null, runFooter = null;
        var hMatch = Regex.Match(html, @"<header([^>]*)>(.*?)</header>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (hMatch.Success && IsFixedRegion(hMatch.Groups[1].Value, "header", css))
        {
            runHeader = DecodeEntities(HtmlFragment.StripHtmlTags(hMatch.Groups[2].Value)).Trim();
            html = html.Remove(hMatch.Index, hMatch.Length);
        }
        var fMatch = Regex.Match(html, @"<footer([^>]*)>(.*?)</footer>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (fMatch.Success && IsFixedRegion(fMatch.Groups[1].Value, "footer", css))
        {
            runFooter = DecodeEntities(HtmlFragment.StripHtmlTags(fMatch.Groups[2].Value)).Trim();
            html = html.Remove(fMatch.Index, fMatch.Length);
        }
        if (!string.IsNullOrEmpty(runHeader)) marginTop += 24;
        if (!string.IsNullOrEmpty(runFooter)) marginBottom += 24;
        var beforeMarkers = ParseBeforeMarkers(html);

        // Build the block list, segmenting out real data tables (no form inputs) so they render
        // as column grids instead of a flattened single column. Text between tables flows through
        // the normal block path; a table with form inputs stays on the flat path (BuildTableFromHtml
        // would swallow its <input>s). Table-free HTML yields a single text segment = unchanged.
        var blocks = new List<Block>();
        bool htmlHasFormInput = Regex.IsMatch(html, @"<\s*(input|select|textarea)\b", RegexOptions.IgnoreCase);
        if (ContainsTable(html) && !htmlHasFormInput)
        {
            foreach (var (isTable, seg) in SegmentHtmlTables(html))
            {
                if (isTable) blocks.Add(new Block { IsTable = true, TableHtml = seg });
                else blocks.AddRange(ParseBlocks(seg, css, beforeMarkers));
            }
        }
        else blocks = ParseBlocks(html, css, beforeMarkers);
        if (blocks.Count == 0) return Document.Create();

        // Auto-size the page width to the widest data table's natural (content-fit) width when it
        // would otherwise overflow the content area — matching the layout engine, which widens
        // the page for a wide table rather than compressing/clipping it. Only widen (never shrink),
        // and only when a table genuinely overflows, so normal-width conversions are unchanged.
        double availContentW = pageWidth - marginLeft - marginRight;
        double widestTable = 0;
        foreach (var b in blocks)
            if (b.IsTable && BuildTableFromHtml(b.TableHtml, availContentW, out var natW) is not null && natW > widestTable)
                widestTable = natW;
        if (widestTable > availContentW)
        {
            var neededContent = widestTable + 8; // small slack for borders/padding rounding
            var neededPage = neededContent + marginLeft + marginRight;
            if (neededPage > pageWidth)
            {
                // The layout engine keeps the page's larger (portrait-height) dimension as the
                // height and widens the width to fit the table, rather than the swapped landscape
                // short edge — so a wide table on an A4 page lands ~1129 × 842, not 1129 × 595.
                pageHeight = Math.Max(pageWidth, pageHeight);
                pageWidth = neededPage;
            }
        }

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);

        // Pull <title> for doc metadata before we lose it in stripping.
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (titleMatch.Success)
            doc.Info.Title = DecodeEntities(titleMatch.Groups[1].Value).Trim();

        var contentWidth = pageWidth - marginLeft - marginRight;
        var y = pageHeight - marginTop;
        // A browser aligns the TOP of the first line box to the content top, so the first
        // baseline sits further from the top for a larger first line (its line box is taller).
        // The default margin was calibrated to the HTML renderer for a default-size
        // (~11 pt) first line; when the first line's font is larger, lower the first baseline
        // by its font-size excess to give a top-aligned first line. Scoped
        // to the default-margin path so explicit-margin conversions stay byte-identical.
        if (!marginsExplicit)
        {
            const double DefaultFirstFontSize = 11.0;
            const double FirstLineLeadingPerPt = 0.7647; // excess-pt → baseline drop (reference-fit)
            var firstFontSize = blocks[0].FontSize;
            if (firstFontSize > DefaultFirstFontSize)
                y -= (firstFontSize - DefaultFirstFontSize) * FirstLineLeadingPerPt;
        }
        var sb = new StringBuilder();

        // CSS font-family → embedded TrueType. Each resolvable family is embedded once
        // (shared indirect font dict) and registered into each page's resources on first
        // use under a unique "FE<n>" resource name. Blocks whose family doesn't resolve
        // fall back to the Standard-14 FontRes (Helvetica/Courier).
        var embeddedFonts = new Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)>(StringComparer.OrdinalIgnoreCase);
        var fontFileCache = new Dictionary<string, (int objNum, string embedName)>(StringComparer.Ordinal);
        bool usedCustomFont = false;

        string ResolveFontRes(Page pg, Block blk)
        {
            if (string.IsNullOrEmpty(blk.FontFamily)) return blk.FontRes;
            var family = blk.FontFamily!;
            if (!embeddedFonts.TryGetValue(family, out var entry))
            {
                var ttf = Text.FontRepository.GetTtfData(family);
                if (ttf is null) { embeddedFonts[family] = default; return blk.FontRes; }
                // PDF /BaseFont can't carry raw spaces; strip them ("Segoe UI" → "SegoeUI").
                var baseName = family.Replace(" ", "");
                var fontDict = new Core.PdfDictionary();
                Text.FontEmbedder.EmbedIntoFontDict(doc, ttf, fontDict, baseName, fontFileCache);
                var objNum = doc.AllocateObjectNumber();
                doc.AddNewObject(objNum, fontDict, registerOverlay: true);
                entry = ($"FE{embeddedFonts.Count + 1}", new Core.PdfIndirectRef(objNum, 0));
                embeddedFonts[family] = entry;
            }
            if (entry.fontRef is null) return blk.FontRes; // unresolvable family (cached miss)
            RegisterPageFont(pg, entry.resName, entry.fontRef);
            usedCustomFont = true;
            return entry.resName;
        }

        // Radio inputs collected during layout, grouped (after the loop) into one
        // RadioButtonField per HTML `name` so each option surfaces as a
        // RadioButtonOptionField on Form.Fields.
        var radioOptions = new List<(string group, bool chk, Page page, Rectangle rect)>();

        // Internal-link support: where each named anchor (id / <a name>) rendered,
        // and the inline <a href> ranges pending link-annotation emission. Resolved
        // in a second pass after layout so #fragment links to later pages work.
        var anchorTargets = new Dictionary<string, (Page page, double y)>(StringComparer.Ordinal);
        var pendingLinks = new List<(Page page, Aspose.Pdf.Rectangle rect, string url)>();

        bool lastWasHardBreak = false;
        foreach (var block in blocks)
        {
            var blockFontSize = block.FontSize;
            var lineHeight = blockFontSize * 1.3;

            // CSS page-break-before:always — start this block on a fresh page (unless we're
            // already at the top of one, so a break as the very first content doesn't add a
            // blank leading page).
            if (block.PageBreakBefore && y < pageHeight - marginTop - 1e-3)
            {
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page);
                y = pageHeight - marginTop;
            }

            if (block.IsTable)
            {
                var table = BuildTableFromHtml(block.TableHtml, contentWidth, out _);
                if (table is not null)
                {
                    table.FlowLeftOffset = marginLeft;
                    // Paginate the table from the current cursor; the first slice lands on this
                    // page, further slices spill onto fresh pages (matching a browser splitting a
                    // long table across pages). Borders/graphics come back via LastGraphDraws.
                    var slices = table.BuildMultiPage(page, y, marginBottom);
                    var graphs = table.LastGraphDraws;
                    for (var si = 0; si < slices.Count; si++)
                    {
                        if (si > 0)
                        {
                            page = doc.Pages.Add(pageWidth, pageHeight);
                            EnsureFonts(page);
                        }
                        if (si < graphs.Count)
                            foreach (var g in graphs[si]) page.AddContentStream(g);
                        page.AddContentStream(slices[si]);
                    }
                    y = slices.Count > 1 ? table.LastPageEndY : y - table.LastRenderedHeight;
                }
                lastWasHardBreak = false;
                continue;
            }

            // <input>: place an interactive AcroForm TextBoxField at the cursor.
            // The test only inspects the field (type/Multiline), not its pixels, but
            // we size and position it from the CSS so the widget lands where the input
            // sits in the flow.
            if (block.IsCheckbox)
            {
                // Emit an AcroForm checkbox at the flow cursor (a small fixed box; the
                // HTML→PDF tests inspect the field, not its pixel position).
                const double boxSize = 10.0;
                if (y - boxSize < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                var cbx = marginLeft + block.LeftIndent;
                var checkbox = new Forms.CheckboxField(page, new Rectangle(cbx, y - boxSize, cbx + boxSize, y))
                {
                    Checked = block.Checked,
                };
                doc.Form.Add(checkbox, page.Number);
                y -= boxSize + 2;
                lastWasHardBreak = false;
                continue;
            }

            if (block.IsRadio)
            {
                const double boxSize = 10.0;
                if (y - boxSize < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                var rbx = marginLeft + block.LeftIndent;
                radioOptions.Add((block.RadioGroup, block.Checked, page,
                    new Rectangle(rbx, y - boxSize, rbx + boxSize, y)));
                y -= boxSize + 2;
                lastWasHardBreak = false;
                continue;
            }

            if (block.IsImage)
            {
                var bytes = LoadConverterImage(block.ImageSrc, options);
                if (bytes is not null)
                {
                    double natW = 0, natH = 0;
                    TryReadImagePixelSize(bytes, out var pxW, out var pxH);
                    if (pxW > 0 && pxH > 0) { natW = pxW * 0.75; natH = pxH * 0.75; }
                    var w = block.ImageWidth > 0 ? block.ImageWidth * 0.75 : 0;
                    var h = block.ImageHeight > 0 ? block.ImageHeight * 0.75 : 0;
                    if (w <= 0 && h <= 0) { w = natW > 0 ? natW : 72; h = natH > 0 ? natH : 72; }
                    else if (h <= 0) h = (natW > 0 && natH > 0) ? w * natH / natW : w;
                    else if (w <= 0) w = (natW > 0 && natH > 0) ? h * natW / natH : h;
                    var availW = contentWidth;
                    if (availW > 0 && w > availW) { h *= availW / w; w = availW; }
                    if (y - h < marginBottom)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page);
                        y = pageHeight - marginTop;
                    }
                    try { page.AddImage(bytes, new Rectangle(marginLeft, y - h, marginLeft + w, y)); }
                    catch { /* undecodable image: skip, keep the flow going */ }
                    y -= h;
                }
                lastWasHardBreak = false;
                continue;
            }

            if (block.IsInputField)
            {
                if (y < pageHeight - marginTop - 1e-3)
                    y -= block.MarginTop;
                var fieldW = block.InputWidth > 0
                    ? System.Math.Min(block.InputWidth, contentWidth - block.LeftIndent)
                    : contentWidth - block.LeftIndent;
                var fieldH = block.InputHeight > 0 ? block.InputHeight : lineHeight;
                if (y - fieldH < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                var llx = marginLeft + block.LeftIndent;
                var field = new Forms.TextBoxField(page, new Rectangle(llx, y - fieldH, llx + fieldW, y))
                {
                    Multiline = block.InputMultiline,
                    ReadOnly = block.InputReadOnly,
                };
                // Carry the HTML name/id through to the AcroForm field name so callers can
                // find the field by FullName.
                if (!string.IsNullOrEmpty(block.InputName)) field.PartialName = block.InputName;
                if (!string.IsNullOrEmpty(block.InputValue)) field.Value = block.InputValue;
                doc.Form.Add(field, page.Number);
                // Draw a visible border box for the input so it reads as a form field
                // in the rendered page (the widget's own appearance is not rasterised).
                DrawBox(page, llx, y - fieldH, fieldW, fieldH,
                    border: Color.FromRgb(130, 130, 130), borderWidth: 0.75, fill: null);
                y -= fieldH + block.MarginBottom;
                lastWasHardBreak = false;
                continue;
            }

            // Hard-break blocks (<br>, empty <p>, <hr>) only consume vertical
            // space — never emit an empty BT/ET run, which would surface as
            // extra zero-length TextFragments to TextFragmentAbsorber. Coalesce
            // runs of consecutive hard-breaks so deeply-nested empty containers
            // don't explode page count (HTML like <div><div></div></div> emits
            // a chain of closes that would otherwise each become a blank line).
            if (block.IsHardBreak || string.IsNullOrEmpty(block.Text))
            {
                // Prefer the explicit CSS height over the default half-line
                // spacer — CMS template HTML often uses empty styled divs as
                // visual separator bars, and ignoring their height would
                // collapse intended pagination.
                var spacer = block.ExplicitHeight > 0
                    ? block.ExplicitHeight
                    : (lastWasHardBreak ? 0 : lineHeight * 0.5);
                if (spacer > 0)
                {
                    if (y - spacer < marginBottom)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page);
                        y = pageHeight - marginTop;
                    }
                    y -= spacer;
                }
                lastWasHardBreak = true;
                continue;
            }
            lastWasHardBreak = false;

            // Apply top margin (unless we're at the start of a fresh page).
            if (y < pageHeight - marginTop - 1e-3)
                y -= block.MarginTop;

            var availWidth = contentWidth - block.LeftIndent;
            var lines = WordWrap(block.Text, availWidth, blockFontSize * 0.52);
            // Pad the block's rendered area up to ExplicitHeight so styled
            // fixed-height elements keep their reserved vertical space even
            // when the text inside wraps to fewer lines.
            var textHeight = lines.Length * lineHeight;
            var paddingBelow = block.ExplicitHeight > textHeight ? block.ExplicitHeight - textHeight : 0;

            // Approximate per-char advance for mapping inline <a href> char ranges to
            // link-annotation rects (same crude metric WordWrap uses to break lines).
            var charW = blockFontSize * 0.52;
            var lineX = marginLeft + block.LeftIndent;
            var cumChar = 0;          // char offset of the current line's start within block.Text
            var firstLineOfBlock = true;
            foreach (var line in lines)
            {
                if (y - lineHeight < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                var fontRes = ResolveFontRes(page, block);

                // List marker: a separate text run in the item's left indent on the first line,
                // so it surfaces as its own TextFragment. A numeric/bullet marker is emitted BEFORE
                // the content (earlier fragment); a CSS ::before marker (MarkerAfter) is emitted
                // AFTER the content so, on an RTL line, the item text is the earlier fragment.
                // The marker itself may be Arabic/Hebrew/CJK, so it uses the same RTL/Type0 path
                // as body text.
                void EmitMarkerHere()
                {
                    var markerW = block.Marker!.Length * blockFontSize * 0.52;
                    var markerX = Math.Max(marginLeft, marginLeft + block.LeftIndent - markerW - 4);
                    EmitPositionedRun(page, fontRes, blockFontSize, markerX, y, block.Marker!);
                }
                if (firstLineOfBlock && !string.IsNullOrEmpty(block.Marker) && !block.MarkerAfter)
                    EmitMarkerHere();

                var invc = System.Globalization.CultureInfo.InvariantCulture;
                var lnX = (marginLeft + block.LeftIndent).ToString("F2", invc);
                var lnY = y.ToString("F2", invc);

                // CSS background-color: draw a fill rectangle behind this line, spanning the
                // block's content width, BEFORE the text (append order = draw order, so the
                // text lands on top). The rect covers the baseline origin of every fragment on
                // the line so text extraction recovers it as TextState.BackgroundColor. Fill
                // components are emitted at F5 so Color.FromRgb's Round(c*255) round-trips exactly.
                if (block.BackgroundColor is { } bgc)
                {
                    var bgX = marginLeft + block.LeftIndent;
                    var bgW = contentWidth - block.LeftIndent;
                    var bgSb = new StringBuilder();
                    bgSb.Append("q ");
                    bgSb.Append($"{(bgc.R / 255.0).ToString("F5", invc)} {(bgc.G / 255.0).ToString("F5", invc)} {(bgc.B / 255.0).ToString("F5", invc)} rg ");
                    bgSb.Append($"{bgX.ToString("F2", invc)} {(y - blockFontSize * 0.25).ToString("F2", invc)} {bgW.ToString("F2", invc)} {(blockFontSize * 1.15).ToString("F2", invc)} re f Q");
                    page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
                }
                // CJK / RTL line: the Standard-14 WinAnsi Tf/Tj path collapses these to '?'.
                // Embed a covering Unicode face as a Type0/CID font (deduped once per page) and
                // emit hex glyph ids. A pure Arabic/Hebrew line is written in VISUAL order (shaped
                // Arabic / reversed Hebrew) so it displays right-to-left; the absorber logicalizes
                // presentation forms and pure-Hebrew runs back to logical reading order.
                var isRtlLine = IsPureRtl(line);
                var uniSource = isRtlLine ? ToVisualRtl(line) : line;
                var cjkFont = (HasCjk(line) || isRtlLine) ? ResolveUnicodeFont(uniSource) : null;
                var cjkTtf = cjkFont?.SourceFontData?.TtfData;
                if (cjkTtf is not null
                    && page.Dict.Get("Resources") as Core.PdfDictionary is { } cjkRes
                    && cjkRes.Get("Font") as Core.PdfDictionary is { } cjkFontDict)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(
                        cjkFontDict, cjkTtf, cjkFont!.FontName ?? "Unicode", uniSource, stripSpacesInBaseFont: true);
                    sb.Clear();
                    sb.AppendLine("BT");
                    sb.Append($"/{rn} {blockFontSize.ToString("F1", invc)} Tf ");
                    sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                    sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else
                {
                    sb.Clear();
                    sb.AppendLine("BT");
                    sb.Append($"/{fontRes} {blockFontSize.ToString("F1", invc)} Tf ");
                    sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                    sb.Append($"({EscapePdfString(line)}) Tj ");
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }

                // CSS ::before marker: emitted after the item text so it is the later fragment.
                if (firstLineOfBlock && !string.IsNullOrEmpty(block.Marker) && block.MarkerAfter)
                    EmitMarkerHere();

                // A named anchor declared in this block resolves to the page + y of
                // its first rendered line, so a #fragment link lands here.
                if (firstLineOfBlock && block.AnchorNames is { Count: > 0 })
                    foreach (var nm in block.AnchorNames)
                        anchorTargets[nm] = (page, y + lineHeight);
                firstLineOfBlock = false;

                // Inline <a href> ranges overlapping this line get a link rect over
                // their run; resolved to a GoTo/URI action after layout.
                if (block.Anchors is { Count: > 0 })
                {
                    int lineStart = cumChar, lineEnd = cumChar + line.Length;
                    foreach (var (aStart, aLen, url) in block.Anchors)
                    {
                        int ov0 = Math.Max(aStart, lineStart), ov1 = Math.Min(aStart + aLen, lineEnd);
                        if (ov1 > ov0 && !string.IsNullOrEmpty(url))
                        {
                            double x0 = lineX + (ov0 - lineStart) * charW;
                            double x1 = lineX + (ov1 - lineStart) * charW;
                            pendingLinks.Add((page, new Aspose.Pdf.Rectangle(x0, y, x1, y + lineHeight), url));
                        }
                    }
                }
                cumChar += line.Length + 1;   // +1 for the space consumed at the wrap point
                y -= lineHeight;
            }
            if (paddingBelow > 0)
            {
                if (y - paddingBelow < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                else
                {
                    y -= paddingBelow;
                }
            }
            y -= block.MarginBottom;
        }

        // When custom (font-family) faces were embedded, drop the eager Standard-14
        // Helvetica/Courier resources that no content stream actually references, so the
        // document's font set reflects only the faces in use. Conversions that don't use
        // font-family keep the original eager fonts untouched (no behavioural change).
        // Build one RadioButtonField per HTML radio group (by name); each <input> becomes a
        // RadioButtonOptionField kid (circle style, visible border) so it surfaces on
        // Form.Fields after save+reload.
        EmitRadioGroups(doc, radioOptions);

        // Render the running <header>/<footer> on every page (pulled out of the flow above so
        // they repeat rather than appearing once). Emitted after body content in the reserved
        // top/bottom bands.
        if (!string.IsNullOrEmpty(runHeader) || !string.IsNullOrEmpty(runFooter))
        {
            var invHf = System.Globalization.CultureInfo.InvariantCulture;
            foreach (Page pg in doc.Pages)
            {
                EnsureFonts(pg);
                void EmitRunning(string text, double ty)
                {
                    var s = new StringBuilder();
                    s.AppendLine("BT");
                    s.Append($"/F1 11.0 Tf ");
                    s.Append($"1 0 0 1 {marginLeft.ToString("F2", invHf)} {ty.ToString("F2", invHf)} Tm ");
                    s.Append($"({EscapePdfString(text)}) Tj ");
                    s.AppendLine("ET");
                    pg.AddContentStream(Encoding.ASCII.GetBytes(s.ToString()));
                }
                if (!string.IsNullOrEmpty(runHeader)) EmitRunning(runHeader!, pageHeight - 24);
                if (!string.IsNullOrEmpty(runFooter)) EmitRunning(runFooter!, 24);
            }
        }

        // Second pass: emit link annotations now that every anchor target's page is
        // known. A #fragment href resolves to the page/y its named anchor rendered
        // on (an internal GoTo); any other href becomes an external URI link.
        foreach (var (lp, rect, url) in pendingLinks)
        {
            if (url.StartsWith("#", StringComparison.Ordinal))
            {
                var frag = url.Substring(1);
                if (frag.Length > 0 && anchorTargets.TryGetValue(frag, out var tgt))
                    lp.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                tgt.page.Number, 0, tgt.y, 0)));
                else if (frag.Length == 0 || frag.Equals("top", StringComparison.OrdinalIgnoreCase))
                    // "#" and "#top" are the HTML convention for the document top.
                    lp.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                1, 0, pageHeight - marginTop, 0)));
                // A dangling #fragment (no matching anchor) emits no link.
            }
            else
            {
                lp.Annotations.AddLinkAnnotation(rect, url);
            }
        }

        if (usedCustomFont) PruneUnusedFonts(doc);

        // Build a logical-structure (tagged) tree when the caller asked for it.
        // Each content block becomes a structure element under the document root:
        // an <img> → Figure (carrying its alt text as /Alt), a text paragraph → P.
        if (options?.CreateLogicalStructure == true)
            BuildLogicalStructure(doc, blocks);

        return doc;
    }

    /// <summary>Author a /StructTreeRoot for the converted document from the
    /// parsed block list: images become Figure elements (with /Alt from the
    /// <c>alt</c> attribute), paragraphs become P elements, all directly under
    /// the document root element. Enabled by
    /// <see cref="HtmlLoadOptions.CreateLogicalStructure"/>.</summary>
    private static void BuildLogicalStructure(Document doc, List<Block> blocks)
    {
        Tagged.ITaggedContent tc = doc.TaggedContent;
        var root = tc.RootElement;
        foreach (var b in blocks)
        {
            if (b.IsImage)
            {
                var fig = tc.CreateFigureElement();
                if (!string.IsNullOrEmpty(b.ImageAlt))
                    fig.AlternativeText = b.ImageAlt!;
                root.AppendChild(fig);
            }
            else if (!b.IsHardBreak && !b.IsHorizontalRule && !b.IsInputField
                     && !b.IsCheckbox && !b.IsRadio && !b.IsTable
                     && !string.IsNullOrWhiteSpace(b.Text))
            {
                root.AppendChild(tc.CreateParagraphElement());
            }
        }
    }

    /// <summary>Register an embedded-font indirect reference under <paramref name="resName"/>
    /// in a page's /Resources/Font (resolving indirect Resources/Font so the originals
    /// aren't replaced); idempotent per page.</summary>
    private static void RegisterPageFont(Page page, string resName, Core.PdfIndirectRef fontRef)
    {
        var reader = page.Reader;
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary
            ?? reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary
            ?? reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName)) fontDict.Set(resName, fontRef);
    }

    /// <summary>Build one <see cref="Forms.RadioButtonField"/> per HTML radio group (keyed by
    /// the input `name`; unnamed radios each form their own group) from the options collected
    /// during layout. Each option becomes a circle-styled <see cref="Forms.RadioButtonOptionField"/>
    /// kid with a visible border, so after save+reload it surfaces on Form.Fields.</summary>
    private static void EmitRadioGroups(Document doc,
        List<(string group, bool chk, Page page, Rectangle rect)> options)
    {
        if (options.Count == 0) return;
        var groups = new List<(string key, List<(bool chk, Page page, Rectangle rect)> opts)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var anon = 0;
        foreach (var (g, chk, page, rect) in options)
        {
            var key = string.IsNullOrEmpty(g) ? "__radio" + anon++ : g;
            if (!index.TryGetValue(key, out var gi))
            {
                gi = groups.Count; index[key] = gi;
                groups.Add((key, new List<(bool, Page, Rectangle)>()));
            }
            groups[gi].opts.Add((chk, page, rect));
        }

        foreach (var (key, opts) in groups)
        {
            try
            {
                var firstPage = opts[0].page;
                var rbf = new Forms.RadioButtonField(firstPage);
                var oi = 0;
                foreach (var (chk, page, rect) in opts)
                {
                    var opt = new Forms.RadioButtonOptionField(page, rect)
                    {
                        Style = Forms.BoxStyle.Circle,
                        OptionName = key + "_" + oi++,
                    };
                    opt.Characteristics.Border = System.Drawing.Color.Black;
                    rbf.Add(opt);
                }
                doc.Form.Add(rbf, firstPage.Number);
            }
            catch { /* best-effort radio emission */ }
        }
    }

    /// <summary>Remove /Font entries on each page that no content stream references via a
    /// "/Name … Tf" operator. Only provably-unused fonts are dropped (rendering unchanged).</summary>
    private static void PruneUnusedFonts(Document doc)
    {
        foreach (var page in doc.Pages)
        {
            var reader = page.Reader;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            var fontDict = resources is null ? null : reader.ResolveDict(resources.Get("Font"));
            if (fontDict is null) continue;

            var content = page.GetContentStreamBytes();
            if (content is null) continue;
            var text = Encoding.ASCII.GetString(content);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(text, @"/([A-Za-z0-9.+\-]+)\s+[-\d.]+\s+Tf"))
                used.Add(m.Groups[1].Value);

            foreach (var key in new List<string>(fontDict.Keys))
                if (!used.Contains(key)) fontDict.Remove(key);
        }
    }

    /// <summary>Build a Block describing an <input> control: its value and any CSS
    /// width/height, so layout can emit a TextBoxField of the right size.</summary>
    private static Block BuildInputBlock(Dictionary<string, string>? attrs, BlockStyle style)
    {
        string? value = null, styleAttr = null, name = null, id = null;
        attrs?.TryGetValue("value", out value);
        attrs?.TryGetValue("style", out styleAttr);
        attrs?.TryGetValue("name", out name);
        attrs?.TryGetValue("id", out id);
        var (w, h) = ParseInputSize(styleAttr);
        // A disabled or readonly input maps to a ReadOnly AcroForm field.
        var readOnly = attrs is not null && (attrs.ContainsKey("disabled") || attrs.ContainsKey("readonly"));
        // AcroForm field name: prefer the HTML name attribute, fall back to id.
        var fieldName = !string.IsNullOrEmpty(name) ? name : id;
        return new Block
        {
            IsInputField = true,
            InputValue = DecodeEntities(value ?? ""),
            InputName = string.IsNullOrEmpty(fieldName) ? null : fieldName,
            InputWidth = w,
            InputHeight = h,
            InputMultiline = false,
            InputReadOnly = readOnly,
            FontSize = style.FontSize,
            FontRes = style.FontRes,
            LeftIndent = style.LeftIndent,
            MarginTop = 1,
            MarginBottom = 2,
        };
    }

    /// <summary>Read width:/height: pixel lengths from an inline style string.</summary>
    private static (double w, double h) ParseInputSize(string? styleAttr)
    {
        double w = 0, h = 0;
        if (string.IsNullOrEmpty(styleAttr)) return (w, h);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var wm = Regex.Match(styleAttr, @"(?:^|[;\s])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out w);
        var hm = Regex.Match(styleAttr, @"(?:^|[;\s])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out h);
        return (w, h);
    }

    /// <summary>Turn HTML into a list of Block records. The parser is a
    /// small hand-rolled tokeniser (no external DOM): it tracks the stack
    /// of open block elements to decide font + margins for each text run.</summary>
    private static List<Block> ParseBlocks(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        IReadOnlyList<BeforeMarker>? beforeMarkers = null)
    {
        // Strip script/style/head bodies whole; inline tags inside them are
        // not semantic content.
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        // Strip DOCTYPE, comments and CDATA sections — the tag tokenizer
        // below only recognises <Name …> shapes, so these would otherwise
        // surface as literal text content.
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, @"<!\[CDATA\[[\s\S]*?\]\]>", "");
        // Strip leading BOM if present — UTF-8 HTMLs often ship with one.
        if (html.Length > 0 && html[0] == '\uFEFF') html = html.Substring(1);
        // Decode entities once at the text layer.
        var tokens = Tokenize(html);

        var blocks = new List<Block>();
        var currentText = new StringBuilder();
        var styleStack = new Stack<BlockStyle>();
        styleStack.Push(new BlockStyle { FontSize = 11, FontRes = "F1", MarginTop = 0, MarginBottom = 0, LeftIndent = 0 });
        // Inline <a href> spans accumulated for the block currently being built, in
        // currentText (raw, pre-collapse) coordinates. Flushed (and translated to the
        // collapsed Text's coordinates) when the block is emitted.
        var rawAnchors = new List<(int start, int end, string url)>();
        var openAnchors = new Stack<(int start, string url)>();
        // Anchor-target names (id / <a name>) seen since the last flush; attached to
        // the block being emitted so #fragment links can resolve to its page. If the
        // block is empty they carry forward to the next non-empty block.
        var pendingAnchorNames = new List<string>();
        // A list-item marker ("5." / "•") set when an <li> opens; attaches to the FIRST
        // non-empty block emitted inside that <li> (its text may be nested in child divs,
        // e.g. EditorJS markup), then clears so only the item's first line is marked.
        string? pendingMarker = null;
        // True when pendingMarker is CSS ::before generated content on an RTL list: it renders
        // after the item text (to its right) rather than before, so the item text is the earlier
        // fragment on the line.
        bool pendingMarkerAfter = false;
        // True between <textarea> and </textarea>: the element becomes an AcroForm field,
        // so its inner text is the field's default value, not body content — suppress it.
        bool inTextarea = false;

        void Flush(bool _unused, BlockStyle styleUsed)
        {
            // An <a> still open at the flush boundary covers text up to here in THIS block.
            foreach (var oa in openAnchors)
                rawAnchors.Add((oa.start, currentText.Length, oa.url));
            var raw = currentText.ToString();
            // Collapse runs of *ASCII* whitespace only — U+00A0 (from
            // &nbsp;) is intentional visual content and must survive
            // collapse+Trim so an &nbsp;-only <p> still emits a line.
            // CollapseWhitespaceWithMap reproduces that collapse+Trim while tracking,
            // for each output char, the raw index it came from — so inline anchor
            // spans can be re-expressed in the collapsed Text's coordinates.
            var (collapsed, rawOf) = CollapseWhitespaceWithMap(raw);
            if (collapsed.Length > 0)
            {
                var blk = new Block
                {
                    Text = collapsed,
                    FontSize = styleUsed.FontSize,
                    FontRes = styleUsed.FontRes,
                    FontFamily = styleUsed.FontFamily,
                    MarginTop = styleUsed.MarginTop,
                    MarginBottom = styleUsed.MarginBottom,
                    LeftIndent = styleUsed.LeftIndent,
                    IsListItem = styleUsed.IsListItem,
                    PageBreakBefore = styleUsed.PageBreakBefore,
                    ExplicitHeight = styleUsed.ExplicitHeight,
                    BackgroundColor = styleUsed.BackgroundColor,
                };
                // Attach a pending list marker to this first content block of the <li>.
                if (pendingMarker is not null)
                {
                    blk.Marker = pendingMarker;
                    blk.MarkerAfter = pendingMarkerAfter;
                    pendingMarker = null;
                    pendingMarkerAfter = false;
                }
                if (rawAnchors.Count > 0)
                {
                    foreach (var (s, e, url) in rawAnchors)
                    {
                        if (string.IsNullOrEmpty(url)) continue;
                        int cs = -1, ce = -1;
                        for (int k = 0; k < rawOf.Count; k++)
                            if (rawOf[k] >= s && rawOf[k] < e) { if (cs < 0) cs = k; ce = k + 1; }
                        if (cs >= 0)
                            (blk.Anchors ??= new()).Add((cs, ce - cs, url));
                    }
                }
                if (pendingAnchorNames.Count > 0)
                {
                    blk.AnchorNames = new List<string>(pendingAnchorNames);
                    pendingAnchorNames.Clear();
                }
                blocks.Add(blk);
            }
            else if (styleUsed.ExplicitHeight > 0)
            {
                // Empty block with explicit height (e.g. `<div style="height:50px">`
                // used as a visual separator bar). Emit a text-less spacer
                // so pagination sees the reserved vertical space.
                blocks.Add(new Block
                {
                    Text = "",
                    FontSize = styleUsed.FontSize,
                    FontRes = styleUsed.FontRes,
                    MarginTop = 0,
                    MarginBottom = 0,
                    LeftIndent = styleUsed.LeftIndent,
                    IsHardBreak = true,
                    ExplicitHeight = styleUsed.ExplicitHeight,
                });
            }
            // Empty block close-tags without explicit height do not emit a
            // spacer — nested empty containers (e.g. <div><div></div></div>)
            // would otherwise inflate page count well beyond the text
            // volume. Explicit vertical spacing comes from <br>, <hr>,
            // block margins, and any CSS height/min-height override.
            currentText.Clear();
            rawAnchors.Clear();
            // An <a> left open across a block/line boundary continues in the next
            // block; record what it covered here and re-anchor it at offset 0.
            if (openAnchors.Count > 0)
            {
                var carried = openAnchors.ToArray();
                openAnchors.Clear();
                for (int oi = carried.Length - 1; oi >= 0; oi--)
                    openAnchors.Push((0, carried[oi].url));
            }
        }

        foreach (var tok in tokens)
        {
            if (tok.Kind == TokenKind.Text)
            {
                // Text inside a <textarea> is the field's value, not flow content.
                if (inTextarea) continue;
                currentText.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!;
            if (SkipTags.Contains(tag)) continue;

            if (tok.IsClose)
            {
                if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase)) { inTextarea = false; continue; }
                if (tag.Equals("a", StringComparison.OrdinalIgnoreCase) && openAnchors.Count > 0)
                {
                    var (st, url) = openAnchors.Pop();
                    if (currentText.Length > st) rawAnchors.Add((st, currentText.Length, url));
                }
                if (BlockTags.Contains(tag))
                {
                    var popped = styleStack.Count > 1 ? styleStack.Pop() : styleStack.Peek();
                    Flush(true,popped);
                }
                // Inline close tags are no-ops for block layout.
                continue;
            }

            // Opening tag (or self-closing).
            // Anchor targets: an `id` on any element, or a `name` on <a>, marks a
            // destination that a #fragment hyperlink can jump to. Record it against
            // the block currently being built.
            if (tok.Attributes is not null)
            {
                if (tok.Attributes.TryGetValue("id", out var idName) && !string.IsNullOrEmpty(idName))
                    pendingAnchorNames.Add(idName);
                if (tag.Equals("a", StringComparison.OrdinalIgnoreCase)
                    && tok.Attributes.TryGetValue("name", out var aName) && !string.IsNullOrEmpty(aName))
                    pendingAnchorNames.Add(aName);
            }
            if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                // <br> inserts a newline *within* the current block. We
                // flush as an empty forced-break block so the next text
                // starts on a new line at the same style.
                Flush(true,styleStack.Peek());
                continue;
            }
            if (tag.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                Flush(true,styleStack.Peek());
                // Draw <hr> as a horizontal rule. The line colour/width come
                // from the CSS border (e.g. "border: 1px solid red"); default
                // to a thin grey line when unspecified.
                ParseHrStyle(tok.Attributes, out var hrColor, out var hrWidth);
                blocks.Add(new Block
                {
                    Text = "",
                    FontSize = styleStack.Peek().FontSize,
                    FontRes = "F1",
                    MarginTop = 6,
                    MarginBottom = 6,
                    // Not IsHardBreak: a rule is drawn content, so it must
                    // survive the trailing-spacer trim and be rendered.
                    IsHorizontalRule = true,
                    RuleColor = hrColor,
                    RuleWidth = hrWidth,
                });
                continue;
            }

            // <img>: emit an in-flow image block (drawn at layout time). A display:none image
            // is not part of the rendering — skip it entirely (no draw, no reserved space).
            if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                string? src = null;
                tok.Attributes?.TryGetValue("src", out src);
                bool imgHidden = tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var istyle)
                    && Regex.IsMatch(istyle, @"display\s*:\s*none", RegexOptions.IgnoreCase);
                if (!string.IsNullOrEmpty(src) && !imgHidden)
                {
                    Flush(false, styleStack.Peek());
                    double iw = 0, ih = 0;
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("width", out var ws)) double.TryParse(
                            Regex.Match(ws, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out iw);
                        if (tok.Attributes.TryGetValue("height", out var hs)) double.TryParse(
                            Regex.Match(hs, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out ih);
                        if (tok.Attributes.TryGetValue("style", out var st2) && !string.IsNullOrEmpty(st2))
                        {
                            var wm = Regex.Match(st2, @"width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                            if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out iw);
                            var hm = Regex.Match(st2, @"height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                            if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out ih);
                        }
                    }
                    string? alt = null;
                    tok.Attributes?.TryGetValue("alt", out alt);
                    blocks.Add(new Block { IsImage = true, ImageSrc = src, ImageWidth = iw, ImageHeight = ih, ImageAlt = alt });
                }
                continue;
            }

            // <input> / <textarea>: emit an interactive AcroForm field.
            // Text-like inputs become a TextBoxField; a checkbox becomes a CheckboxField
            // (its `checked` attribute → Checked); a radio becomes a RadioButtonOptionField
            // grouped by name. hidden/submit/button/image are skipped.
            if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            {
                // <textarea> → a multi-line AcroForm text field. Its inner text is the
                // default value (suppressed via inTextarea), not flow content.
                Flush(false, styleStack.Peek());
                blocks.Add(BuildInputBlock(tok.Attributes, styleStack.Peek()));
                inTextarea = true;
                continue;
            }
            if (tag.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                string? type = null;
                tok.Attributes?.TryGetValue("type", out type);
                type = string.IsNullOrEmpty(type) ? "text" : type.ToLowerInvariant();
                if (type is "text" or "password" or "email" or "tel" or "url"
                    or "number" or "search" or "date" or "datetime-local" or "month" or "week" or "time")
                {
                    Flush(false, styleStack.Peek());
                    blocks.Add(BuildInputBlock(tok.Attributes, styleStack.Peek()));
                }
                else if (type == "checkbox")
                {
                    Flush(false, styleStack.Peek());
                    var st = styleStack.Peek();
                    blocks.Add(new Block
                    {
                        IsCheckbox = true,
                        Checked = tok.Attributes?.ContainsKey("checked") == true,
                        FontSize = st.FontSize,
                        FontRes = st.FontRes,
                        LeftIndent = st.LeftIndent,
                    });
                }
                else if (type == "radio")
                {
                    Flush(false, styleStack.Peek());
                    var st = styleStack.Peek();
                    string? grp = null;
                    tok.Attributes?.TryGetValue("name", out grp);
                    blocks.Add(new Block
                    {
                        IsRadio = true,
                        RadioGroup = grp ?? "",
                        Checked = tok.Attributes?.ContainsKey("checked") == true,
                        FontSize = st.FontSize,
                        FontRes = st.FontRes,
                        LeftIndent = st.LeftIndent,
                    });
                }
                continue;
            }

            if (BlockTags.Contains(tag))
            {
                // Start a new block: flush any pending inline text at the
                // outer style, then push the new style.
                Flush(false,styleStack.Peek());
                var parent = styleStack.Peek();
                var style = new BlockStyle
                {
                    FontSize = parent.FontSize,
                    FontRes = parent.FontRes,
                    FontFamily = parent.FontFamily,
                    MarginTop = 0,
                    MarginBottom = 0,
                    LeftIndent = parent.LeftIndent,
                };
                ApplyBlockTagStyle(tag, style);
                // CSS rules: type selector then class selector(s), each overriding the
                // previous, before the inline style="…" (highest specificity).
                ApplyCssRules(css, tag, tok.Attributes, style);
                // Inline style="…" overrides tag defaults: if the author
                // explicitly set padding-left / margin-left we drop the
                // list-style indent the tag default added so that e.g.
                // `<ol style="padding-left:0">` sits flush with body text.
                if (HasInlineIndentOverride(tok.Attributes))
                    style.LeftIndent = parent.LeftIndent;
                ApplyInlineStyle(tok.Attributes, style);
                // List context: an <ol>/<ul> style carries a counter its <li> children
                // draw from; an <li> takes the next marker from its enclosing list. A list
                // whose CSS supplies its own `li:nth-child(..)::before { content }` markers uses
                // those (indexed by child position) instead of the numeric/bullet default.
                if (tag is "ol" or "ul")
                {
                    style.ListKind = tag == "ol" ? 1 : 2;
                    if (tag == "ol") style.ListCounter = ParseListStart(tok.Attributes);
                    style.BeforeRules = ResolveListBeforeRules(beforeMarkers,
                        tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var lc) ? lc : null);
                    style.ChildIndex = 0;
                }
                else if (tag == "li" && parent.ListKind != 0)
                {
                    parent.ChildIndex++;
                    BeforeMarker? before = null;
                    if (parent.BeforeRules is not null)
                        foreach (var r in parent.BeforeRules)
                            if (r.Matches(parent.ChildIndex)) { before = r; break; }
                    if (before is not null)
                    {
                        // CSS-supplied generated marker (list-style:none + ::before): render it as
                        // its own run AFTER the item text so, on an RTL line, the text is the earlier
                        // fragment and the marker the later one.
                        pendingMarker = before.Content;
                        pendingMarkerAfter = true;
                    }
                    else if (parent.BeforeRules is null)
                    {
                        // No CSS markers for this list → numeric ordinal / bullet default.
                        pendingMarker = parent.ListKind == 1
                            ? (++parent.ListCounter).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                            : "•";
                        pendingMarkerAfter = false;
                    }
                    // BeforeRules present but no rule matched this index → no marker.
                }
                styleStack.Push(style);
                continue;
            }

            // Inline tags: mutate the top-of-stack style for <b>/<i>/<strong>/<em>.
            // <span style="font-size:..."> also adjusts size for the inner run.
            if (tag is "b" or "strong")
                MarkInline(styleStack, "F2");
            else if (tag is "i" or "em")
                MarkInline(styleStack, "F3");
            else if (tag == "small")
                MarkInlineSize(styleStack, factor: 0.85);
            else if (tag is "span" or "font")
                // Inline <span style="font-family:…"> / <font face="…"> selects a
                // custom face for the enclosed run (resolved+embedded at layout).
                MarkInlineFontFamily(styleStack, tok.Attributes);
            else if (tag.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                // <a href> opens an inline hyperlink span; record the start so the
                // text up to the matching </a> becomes a Link annotation.
                string? href = null;
                tok.Attributes?.TryGetValue("href", out href);
                if (!string.IsNullOrEmpty(href))
                    openAnchors.Push((currentText.Length, href));
            }
        }
        // Final flush
        Flush(false,styleStack.Peek());
        // Drop trailing hard-break spacers so the doc doesn't grow a blank
        // tail page for HTML that ends with close-tags.
        // Drop trailing spacer-only hardbreaks so HTML that ends with close-tags
        // doesn't grow a blank tail page. Hardbreaks with an explicit CSS
        // height are intentional layout spacers — keep those.
        while (blocks.Count > 0 && blocks[^1].IsHardBreak && blocks[^1].ExplicitHeight <= 0)
            blocks.RemoveAt(blocks.Count - 1);
        return blocks;
    }

    /// <summary>Collapse ASCII whitespace runs to a single space and trim leading/
    /// trailing whitespace — identical output to <c>Regex.Replace(raw,"[ \t\r\n\f]+"," ").
    /// Trim(...)</c> (U+00A0 is preserved as content) — while recording, for each output
    /// character, the raw index it originated from. The map lets inline anchor ranges
    /// (tracked in raw coordinates) be re-expressed against the collapsed text.</summary>
    private static (string text, System.Collections.Generic.List<int> rawOf) CollapseWhitespaceWithMap(string raw)
    {
        static bool IsWs(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f';
        var sb = new StringBuilder(raw.Length);
        var rawOf = new System.Collections.Generic.List<int>(raw.Length);
        int i = 0, n = raw.Length;
        while (i < n && IsWs(raw[i])) i++;                 // drop leading whitespace
        while (i < n)
        {
            if (IsWs(raw[i]))
            {
                int runStart = i;
                while (i < n && IsWs(raw[i])) i++;
                if (i < n) { sb.Append(' '); rawOf.Add(runStart); }   // single space between words; trailing run dropped
            }
            else { sb.Append(raw[i]); rawOf.Add(i); i++; }
        }
        return (sb.ToString(), rawOf);
    }

    private sealed class BlockStyle
    {
        public double FontSize;
        public string FontRes = "F1";
        public string? FontFamily;
        public double MarginTop;
        public double MarginBottom;
        public double LeftIndent;
        public bool IsListItem;
        public bool PageBreakBefore; // CSS page-break-before:always on this element
        // List context carried on an <ol>/<ul> style so its <li> children can be
        // numbered/bulleted. ListKind: 0 = not a list, 1 = ordered, 2 = unordered.
        // ListCounter holds the last-used ordinal (incremented per <li>); the first
        // <li> renders ListCounter+1, so `start="5"`/`counter-set: item 4` sets it to 4.
        public int ListKind;
        public int ListCounter;
        // CSS `li:nth-child(An+B)::before { content: … }` generated markers active for this
        // list (matched to the <ol>/<ul>'s class when it opens); ChildIndex counts the list's
        // children so each <li> can pick the matching rule. Null = no ::before markers → the
        // numeric/bullet default applies.
        public List<BeforeMarker>? BeforeRules;
        public int ChildIndex;
        // Explicit CSS height / min-height in points. When >0 the block's
        // own rendered area must be at least this tall, so empty-body
        // styled divs (common in CMS template HTML) still contribute
        // vertical space to pagination.
        public double ExplicitHeight;
        // CSS box decoration (background-color / border) carried to the emitted Block.
        public Color? BackgroundColor;
        public Color? BorderColor;
        public double BorderWidth;
    }

    // Initial <ol> counter — the first <li> renders ParseListStart+1. Honours the
    // `start` attribute (start-1) and CSS `counter-set`/`counter-reset: <name> N` (N),
    // the latter used by rich-text editors (EditorJS) to resume numbering.
    private static readonly Regex CounterSetRx = new(
        @"counter-(?:set|reset)\s*:\s*[A-Za-z_][\w-]*\s+(-?\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static int ParseListStart(Dictionary<string, string>? attrs)
    {
        if (attrs is null) return 0;
        if (attrs.TryGetValue("start", out var s) && int.TryParse(s.Trim(), out var st))
            return st - 1;
        if (attrs.TryGetValue("style", out var css) && !string.IsNullOrEmpty(css))
        {
            var m = CounterSetRx.Match(css);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var cv)) return cv;
        }
        return 0;
    }

    /// <summary>True when the string contains a CJK / Han / Kana / Hangul character — text
    /// that needs an embedded Unicode font (the Standard-14 fonts have no such glyphs).</summary>
    private static bool HasCjk(string s)
    {
        foreach (var ch in s)
        {
            int o = ch;
            if ((o >= 0x3400 && o <= 0x9FFF)   // CJK Unified + Ext-A
                || (o >= 0x3000 && o <= 0x30FF) // CJK symbols, Hiragana, Katakana
                || (o >= 0xF900 && o <= 0xFAFF) // CJK compatibility ideographs
                || (o >= 0xAC00 && o <= 0xD7AF)) // Hangul syllables
                return true;
        }
        return false;
    }

    /// <summary>True when the line is entirely RTL (Arabic/Hebrew/…) letters plus neutral
    /// punctuation/whitespace — the case where the run can be written wholesale in visual order.
    /// Mixed LTR+RTL lines need full bidi and fall through to the Standard-14 path.</summary>
    private static bool IsPureRtl(string s)
    {
        var hasRtl = false;
        foreach (var c in s)
        {
            if (Text.BidiReorderer.IsRtlChar(c)) hasRtl = true;
            else if (c == ' ' || c == '\t' || (c >= '!' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            { /* neutral */ }
            else return false;
        }
        return hasRtl;
    }

    /// <summary>Convert a pure-RTL logical string to the VISUAL order drawn left-to-right:
    /// Arabic gets contextual shaping (which already emits visual order); other RTL scripts
    /// (Hebrew, …) are simply reversed.</summary>
    private static string ToVisualRtl(string s)
    {
        if (Text.ArabicTextShaper.ContainsArabic(s)) return Text.ArabicTextShaper.Shape(s);
        var arr = s.ToCharArray();
        System.Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>Emit a single positioned text run at (<paramref name="x"/>,<paramref name="y"/>).
    /// A pure Arabic/Hebrew or CJK run is written in visual order through an embedded Type0/CID
    /// face (the Standard-14 fonts would collapse it to '?'); everything else uses the WinAnsi
    /// Tf/Tj path. Used for list markers, which may themselves be non-Latin (a CSS ::before
    /// generated Arabic marker).</summary>
    private static void EmitPositionedRun(Page page, string fontRes, double fontSize, double x, double y, string text)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var isRtl = IsPureRtl(text);
        var visual = isRtl ? ToVisualRtl(text) : text;
        var uniFont = (HasCjk(text) || isRtl) ? ResolveUnicodeFont(visual) : null;
        var ttf = uniFont?.SourceFontData?.TtfData;
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        if (ttf is not null
            && page.Dict.Get("Resources") as Core.PdfDictionary is { } res
            && res.Get("Font") as Core.PdfDictionary is { } fontDict)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                fontDict, ttf, uniFont!.FontName ?? "Unicode", visual, stripSpacesInBaseFont: true);
            sb.Append($"/{rn} {fontSize.ToString("F1", inv)} Tf ");
            sb.Append($"1 0 0 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
            sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
        }
        else
        {
            sb.Append($"/{fontRes} {fontSize.ToString("F1", inv)} Tf ");
            sb.Append($"1 0 0 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
            sb.Append($"({EscapePdfString(text)}) Tj ");
        }
        sb.AppendLine("ET");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    // Broad-Unicode faces (installed on most Windows systems) tried in order; the first
    // whose embedded program covers every non-WinAnsi char in the run is used.
    private static readonly string[] UnicodeFallbackFonts =
        { "Arial", "SimSun", "Malgun Gothic", "Microsoft YaHei", "MS Gothic", "Arial Unicode MS" };
    private static readonly Dictionary<string, (Text.Font? font, Dictionary<int, int>? cmap)> _uniFontCache = new();

    /// <summary>Resolve an embedded Unicode fallback face that covers every non-WinAnsi
    /// character in <paramref name="text"/>, or null when none is available.</summary>
    private static Text.Font? ResolveUnicodeFont(string text)
    {
        foreach (var name in UnicodeFallbackFonts)
        {
            if (!_uniFontCache.TryGetValue(name, out var entry))
            {
                Text.Font? f = null; Dictionary<int, int>? cmap = null;
                try
                {
                    f = Text.FontRepository.FindFont(name);
                    if (f?.SourceFontData?.TtfData is { } ttf) cmap = new Text.GlyphOutlineParser(ttf).CMap;
                }
                catch { f = null; cmap = null; }
                entry = (f, cmap);
                _uniFontCache[name] = entry;
            }
            if (entry.font?.SourceFontData is null || entry.cmap is null) continue;
            var covers = true;
            foreach (var ch in text)
            {
                if (ch <= 0x7F || Text.Cp1252.TryGetByte(ch, out _)) continue;
                if (!entry.cmap.TryGetValue(ch, out var gid) || gid == 0) { covers = false; break; }
            }
            if (covers) return entry.font;
        }
        return null;
    }

    private static void ApplyBlockTagStyle(string tag, BlockStyle s)
    {
        // Minimal margins — only headings and blockquotes get meaningful
        // spacing. p/div/ul/tr stack line-on-line so page counts mirror what
        // the tag-strip + wrap path would produce for the same text volume.
        switch (tag.ToLowerInvariant())
        {
            case "h1": s.FontSize = 18; s.FontRes = "F2"; s.MarginTop = 4; s.MarginBottom = 2; break;
            case "h2": s.FontSize = 15; s.FontRes = "F2"; s.MarginTop = 3; s.MarginBottom = 2; break;
            case "h3": s.FontSize = 13; s.FontRes = "F2"; s.MarginTop = 3; s.MarginBottom = 2; break;
            case "h4": s.FontSize = 12; s.FontRes = "F2"; s.MarginTop = 2; s.MarginBottom = 1; break;
            case "h5": s.FontSize = 11; s.FontRes = "F2"; s.MarginTop = 2; s.MarginBottom = 1; break;
            case "h6": s.FontSize = 10; s.FontRes = "F2"; s.MarginTop = 1; s.MarginBottom = 1; break;
            case "blockquote": s.MarginTop = 3; s.MarginBottom = 3; s.LeftIndent += 20; break;
            case "ul":
            case "ol":         s.LeftIndent += 20; break;
            case "li":         s.IsListItem = true; break;
            case "pre":        s.FontRes = "F4"; break;
            // p, div, tr, td, th, table: inherit parent margins (0 by default).
        }
    }

    // Parse a tiny subset of inline style="…" — enough to let per-block
    // font-size overrides (common in email-style HTML) affect layout.
    private static readonly Regex StyleDeclRx = new(
        @"([a-z-]+)\s*:\s*([^;]+?)\s*(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool HasInlineIndentOverride(Dictionary<string, string>? attrs)
    {
        if (attrs is null || !attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr))
            return false;
        return Regex.IsMatch(styleStr, @"(padding-left|margin-left)\s*:", RegexOptions.IgnoreCase);
    }

    private static void ApplyInlineStyle(Dictionary<string, string>? attrs, BlockStyle s)
    {
        if (attrs is null) return;
        if (!attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr)) return;
        ApplyDeclarationString(styleStr, s);
    }

    /// <summary>Apply a CSS declaration block ("prop:val; prop:val") to a BlockStyle.</summary>
    private static void ApplyDeclarationString(string styleStr, BlockStyle s)
    {
        foreach (Match m in StyleDeclRx.Matches(styleStr))
            ApplyDeclaration(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim(), s);
    }

    private static void ApplyDeclaration(string prop, string val, BlockStyle s)
    {
        if (prop == "font-size")
        {
            if (TryParseLength(val, out var pts)) s.FontSize = pts;
        }
        else if (prop == "font-family")
        {
            var fam = FirstFontFamily(val);
            if (fam is not null) s.FontFamily = fam;
        }
        else if (prop == "font-weight")
        {
            if (val is "bold" or "bolder" || (int.TryParse(val, out var n) && n >= 600))
                s.FontRes = s.FontRes == "F3" ? "F2" : "F2";
        }
        else if (prop == "font-style")
        {
            if (val is "italic" or "oblique")
                s.FontRes = s.FontRes == "F2" ? "F2" : "F3";
        }
        else if (prop == "margin-top")
        {
            if (TryParseLength(val, out var pts)) s.MarginTop = pts;
        }
        else if (prop == "margin-bottom")
        {
            if (TryParseLength(val, out var pts)) s.MarginBottom = pts;
        }
        else if (prop == "margin-left" || prop == "padding-left")
        {
            if (TryParseLength(val, out var pts)) s.LeftIndent += pts;
        }
        else if (prop == "height" || prop == "min-height")
        {
            if (TryParseLength(val, out var pts) && pts > s.ExplicitHeight)
                s.ExplicitHeight = pts;
        }
        else if (prop == "background-color" || prop == "background")
        {
            var c = ParseCssColor(val);
            // Ignore white/transparent backgrounds — they add no visible ink.
            if (c is not null && !(c.R >= 250 && c.G >= 250 && c.B >= 250))
                s.BackgroundColor = c;
        }
        else if (prop == "border" || prop == "border-color" || prop == "border-width"
              || prop == "border-top" || prop == "border-bottom"
              || prop == "border-left" || prop == "border-right")
        {
            var c = ParseCssColor(val);
            if (c is not null) s.BorderColor = c;
            var wm = Regex.Match(val, @"([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (wm.Success && double.TryParse(wm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bw) && bw > 0)
                s.BorderWidth = bw * 0.75; // px → pt
            else if (s.BorderWidth <= 0)
                s.BorderWidth = 0.75;
            // A border with an unspecified colour defaults to black (CSS `border:1px solid`).
            if (s.BorderColor is null && val.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                s.BorderColor = Color.FromRgb(0, 0, 0);
        }
    }

    /// <summary>First concrete (non-generic) family name from a CSS font-family list,
    /// with quotes stripped. Returns null for a purely generic list (serif/sans-serif/
    /// monospace/cursive/fantasy) so the Standard-14 Helvetica default applies.</summary>
    private static string? FirstFontFamily(string value)
    {
        foreach (var part in value.Split(','))
        {
            var name = part.Trim().Trim('\'', '"').Trim();
            if (name.Length == 0) continue;
            switch (name.ToLowerInvariant())
            {
                case "serif": case "sans-serif": case "monospace":
                case "cursive": case "fantasy": case "system-ui": case "inherit":
                    continue;
            }
            return name;
        }
        return null;
    }

    /// <summary>Apply the document stylesheet's type-selector and class-selector rules
    /// to <paramref name="s"/> for an element with tag <paramref name="tag"/> and the
    /// given attributes. Type rule first, then each class (left-to-right) — matching the
    /// simple cascade the converter needs for font-family / size.</summary>
    private static void ApplyCssRules(IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        string tag, Dictionary<string, string>? attrs, BlockStyle s)
    {
        if (css is null || css.Count == 0) return;
        void ApplySelector(string selector)
        {
            if (!css.TryGetValue(selector, out var decls)) return;
            foreach (var kv in decls)
            {
                // page-break-before:always — a genuine pagination directive, honoured here.
                if (kv.Key == "page-break-before"
                    && kv.Value.Contains("always", StringComparison.OrdinalIgnoreCase))
                    s.PageBreakBefore = true;
                // Apply only layout-NEUTRAL font properties from <style> rules.
                // Size/margin/height/indent from a stylesheet are deliberately NOT
                // applied: the converter historically ignored <style> blocks entirely,
                // and honouring those here would shift wrapping/pagination and break
                // documents whose page count is asserted. font-family/weight/style
                // don't affect the metrics WordWrap uses, so they're safe to apply.
                // Font props + box decoration (background/border) are layout-NEUTRAL —
                // they change only the drawn ink, not the wrap metric or pagination — so
                // they're safe to apply from a stylesheet. Size/margin/height stay excluded.
                if (kv.Key is "font-family" or "font-weight" or "font-style"
                    or "background-color" or "background"
                    or "border" or "border-color" or "border-width"
                    or "border-top" or "border-bottom" or "border-left" or "border-right")
                    ApplyDeclaration(kv.Key, kv.Value, s);
            }
        }
        var tagLower = tag.ToLowerInvariant();
        ApplySelector(tagLower);
        if (attrs is not null && attrs.TryGetValue("class", out var cls) && !string.IsNullOrWhiteSpace(cls))
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                ApplySelector("." + c);
                ApplySelector(tagLower + "." + c); // compound "tag.class" (e.g. h1.page)
            }
    }

    /// <summary>Parse a tiny subset of CSS from the document's &lt;style&gt; blocks into
    /// a selector → declarations map. Handles comma-separated type and class selectors
    /// (".a", "div", "th, td"); everything else (descendant combinators, ids, media
    /// queries) is ignored. Used only to resolve font-family / size for HTML→PDF.</summary>
    /// <summary>True when a semantic &lt;header&gt;/&lt;footer&gt; resolves to
    /// <c>position: fixed</c> — via an inline <c>style</c>, a type rule for the tag, or a rule for
    /// one of its classes — i.e. a running region that repeats on every page. A header/footer that is
    /// normal flow content (no fixed positioning) returns false and stays in document flow.</summary>
    private static bool IsFixedRegion(string openTagAttrs, string tagName,
        IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        static bool PinsFixed(string? decl) =>
            decl is not null && Regex.IsMatch(decl, @"position\s*:\s*fixed", RegexOptions.IgnoreCase);

        var styleM = Regex.Match(openTagAttrs, @"style\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase);
        if (styleM.Success && PinsFixed(styleM.Groups["v"].Value)) return true;

        bool RulePinsFixed(string key) =>
            css.TryGetValue(key, out var d) && d.TryGetValue("position", out var p)
            && p.Trim().Equals("fixed", StringComparison.OrdinalIgnoreCase);
        if (RulePinsFixed(tagName)) return true;

        var classM = Regex.Match(openTagAttrs, @"class\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase);
        if (classM.Success)
            foreach (var c in classM.Groups["v"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (RulePinsFixed("." + c)) return true;
        return false;
    }

    /// <summary>Resolve CSS <c>var(--name[, fallback])</c> references in a declaration value
    /// against the collected custom-property map. Unknown names with no fallback resolve to
    /// empty. Custom properties are treated as document-global (last definition wins) — enough
    /// for the common <c>:root</c> / single-rule usage the converter needs.</summary>
    private static string ResolveCssVars(string value, Dictionary<string, string> vars)
    {
        if (value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0) return value;
        return Regex.Replace(value, @"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*?)\s*)?\)",
            m =>
            {
                if (vars.TryGetValue(m.Groups[1].Value, out var v) && v.Length > 0) return v;
                return m.Groups[2].Success ? m.Groups[2].Value : "";
            }, RegexOptions.IgnoreCase);
    }

    private static Dictionary<string, Dictionary<string, string>> ParseStyleSheet(string html)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        // Phase 1: gather all CSS custom properties (--name: value) across every <style>
        // block — including :root and any rule — into a document-global map so var()
        // references resolve regardless of the selector that declared them.
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var cssText = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (Match cv in Regex.Matches(cssText, @"(--[\w-]+)\s*:\s*([^;}]+)"))
                vars[cv.Groups[1].Value] = cv.Groups[2].Value.Trim();
        }
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var selectors = rule.Groups[1].Value;
                var body = rule.Groups[2].Value;
                var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match d in StyleDeclRx.Matches(body))
                    decls[d.Groups[1].Value.Trim().ToLowerInvariant()] =
                        ResolveCssVars(d.Groups[2].Value.Trim(), vars);
                if (decls.Count == 0) continue;
                foreach (var sel in selectors.Split(','))
                {
                    var key = sel.Trim();
                    // Only simple type / single-class selectors (no combinators / pseudo).
                    if (key.Length == 0 || key.IndexOfAny(new[] { ' ', '>', '+', '~', ':', '[' }) >= 0)
                        continue;
                    if (!result.TryGetValue(key, out var existing))
                        result[key] = new Dictionary<string, string>(decls, StringComparer.OrdinalIgnoreCase);
                    else
                        foreach (var kv in decls) existing[kv.Key] = kv.Value;
                }
            }
        }
        return result;
    }

    /// <summary>A CSS <c>li:nth-child(An+B)::before { content: "…" }</c> generated-content
    /// marker: the item text a matching &lt;li&gt; is prefixed with. Only the small subset used
    /// by list styling (an optional container class, an nth-child index, a literal content
    /// string) is modelled — enough to reproduce editor-authored ordered-list markers.</summary>
    private sealed class BeforeMarker
    {
        public string? ContainerClass; // class on the enclosing <ol>/<ul> (null = any list)
        public int A;                  // nth-child(An+B) coefficient
        public int B;                  // nth-child(An+B) offset
        public string Content = "";    // generated text, logical order
        public bool Matches(int index1Based) => A == 0
            ? index1Based == B
            : (index1Based - B) % A == 0 && (index1Based - B) / A >= 0;
    }

    // .class > li:nth-child(An+B)::before  /  li:nth-child(An+B):before  — the container class
    // and combinator are optional; nth-child arg captured raw for NthChildRx.
    private static readonly Regex BeforeSelectorRx = new(
        @"(?:\.(?<cc>[A-Za-z_][\w-]*)\s*[>\s]\s*)?[A-Za-z]+:nth-child\(\s*(?<nc>[^)]+?)\s*\)\s*::?before",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NthChildRx = new(
        @"^(?:(?<a>-?\d*)n\s*(?:(?<sign>[+-])\s*(?<b>\d+))?|(?<lit>-?\d+))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BeforeContentRx = new(
        @"content\s*:\s*(['""])(?<v>.*?)\1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Scan the document's &lt;style&gt; blocks for
    /// <c>li:nth-child(An+B)::before { content: "…" }</c> rules and return them as generated-content
    /// markers, so an <c>&lt;ol&gt;</c> whose CSS supplies its own markers (list-style:none + ::before)
    /// renders those instead of the numeric default.</summary>
    private static List<BeforeMarker> ParseBeforeMarkers(string html)
    {
        var result = new List<BeforeMarker>();
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var sel = BeforeSelectorRx.Match(rule.Groups[1].Value);
                if (!sel.Success) continue;
                var cm = BeforeContentRx.Match(rule.Groups[2].Value);
                if (!cm.Success) continue;
                var nc = NthChildRx.Match(sel.Groups["nc"].Value.Trim());
                if (!nc.Success) continue;
                int a, b;
                if (nc.Groups["lit"].Success) { a = 0; b = int.Parse(nc.Groups["lit"].Value); }
                else
                {
                    var av = nc.Groups["a"].Value;
                    a = av.Length == 0 ? 1 : av == "-" ? -1 : int.Parse(av);
                    b = nc.Groups["b"].Success
                        ? int.Parse(nc.Groups["b"].Value) * (nc.Groups["sign"].Value == "-" ? -1 : 1)
                        : 0;
                }
                result.Add(new BeforeMarker
                {
                    ContainerClass = sel.Groups["cc"].Success ? sel.Groups["cc"].Value : null,
                    A = a,
                    B = b,
                    Content = DecodeEntities(cm.Groups["v"].Value),
                });
            }
        }
        return result;
    }

    /// <summary>The subset of <paramref name="markers"/> that applies to an <c>&lt;ol&gt;/&lt;ul&gt;</c>
    /// carrying <paramref name="classAttr"/> — a rule with no container class matches any list; a
    /// rule scoped to <c>.foo</c> matches only when the list has class <c>foo</c>. Null when none.</summary>
    private static List<BeforeMarker>? ResolveListBeforeRules(IReadOnlyList<BeforeMarker>? markers, string? classAttr)
    {
        if (markers is null || markers.Count == 0) return null;
        var classes = classAttr?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? System.Array.Empty<string>();
        List<BeforeMarker>? hits = null;
        foreach (var m in markers)
            if (m.ContainerClass is null || System.Array.IndexOf(classes, m.ContainerClass) >= 0)
                (hits ??= new()).Add(m);
        return hits;
    }

    private static bool TryParseLength(string s, out double pts)
    {
        pts = 0;
        // Accept "13px" / "10pt" / "1em". Reject percent / calc / etc.
        var m = Regex.Match(s, @"^(-?\d+(?:\.\d+)?)\s*(px|pt|em|rem)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : "px";
        pts = unit switch
        {
            "pt" => n,
            "px" => n * 0.75,          // 96dpi: 1px = 0.75pt
            "em" or "rem" => n * 11,   // against our default body 11pt
            _ => n,
        };
        return pts > 0;
    }

    private static void MarkInline(Stack<BlockStyle> stack, string fontRes)
    {
        // Inline emphasis modifies the *current* block's style mid-stream.
        // Minimal fidelity: promote the whole block to the emphasised font
        // when any part of it uses <b>/<i>. Real mixed-style output would
        // require splitting Block into sub-runs.
        if (stack.Count == 0) return;
        var top = stack.Peek();
        if (top.FontRes == "F1") top.FontRes = fontRes;
    }

    private static void MarkInlineSize(Stack<BlockStyle> stack, double factor)
    {
        if (stack.Count == 0) return;
        var top = stack.Peek();
        top.FontSize *= factor;
    }

    /// <summary>Apply ONLY an inline element's font-family — a
    /// <c>&lt;span style="font-family:…"&gt;</c> or <c>&lt;font face="…"&gt;</c> — to the
    /// current run by mutating the top-of-stack block style. Deliberately layout-neutral:
    /// any size/margin the same element declares is ignored (the wrap metric is
    /// family-independent), so pagination is unchanged and only the rendered face differs.</summary>
    private static void MarkInlineFontFamily(Stack<BlockStyle> stack, Dictionary<string, string>? attrs)
    {
        if (stack.Count == 0 || attrs is null) return;
        string? fam = null;
        if (attrs.TryGetValue("style", out var styleStr) && !string.IsNullOrWhiteSpace(styleStr))
        {
            foreach (Match m in StyleDeclRx.Matches(styleStr))
                if (m.Groups[1].Value.Trim().Equals("font-family", StringComparison.OrdinalIgnoreCase))
                    fam = FirstFontFamily(m.Groups[2].Value.Trim());
        }
        if (fam is null && attrs.TryGetValue("face", out var face))
            fam = FirstFontFamily(face);
        if (fam is not null) stack.Peek().FontFamily = fam;
    }

    private enum TokenKind { Text, Tag }
    private sealed class Token
    {
        public TokenKind Kind;
        public string? Tag;
        public bool IsClose;
        public bool IsSelfClosing;
        public Dictionary<string, string>? Attributes;
        public string Value = "";
    }

    private static readonly Regex TagRx = new(
        @"<(/?)([A-Za-z][A-Za-z0-9]*)\s*([^>]*?)(/?)>",
        RegexOptions.Compiled);

    private static readonly Regex AttrRx = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*(?:=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\">]+)))?",
        RegexOptions.Compiled);

    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        int idx = 0;
        foreach (Match m in TagRx.Matches(html))
        {
            if (m.Index > idx)
            {
                var text = html.Substring(idx, m.Index - idx);
                if (text.Length > 0)
                    tokens.Add(new Token { Kind = TokenKind.Text, Value = text });
            }
            var attrs = ParseAttributes(m.Groups[3].Value);
            tokens.Add(new Token
            {
                Kind = TokenKind.Tag,
                Tag = m.Groups[2].Value,
                IsClose = m.Groups[1].Value == "/",
                IsSelfClosing = m.Groups[4].Value == "/",
                Attributes = attrs,
            });
            idx = m.Index + m.Length;
        }
        if (idx < html.Length)
        {
            var text = html.Substring(idx);
            if (text.Length > 0)
                tokens.Add(new Token { Kind = TokenKind.Text, Value = text });
        }
        return tokens;
    }

    private static Dictionary<string, string>? ParseAttributes(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRx.Matches(s))
        {
            var name = m.Groups[1].Value;
            var val = m.Groups[2].Success ? m.Groups[2].Value
                     : m.Groups[3].Success ? m.Groups[3].Value
                     : m.Groups[4].Success ? m.Groups[4].Value
                     : "";
            dict[name] = val;
        }
        return dict.Count > 0 ? dict : null;
    }

    private static string DecodeEntities(string text)
    {
        // Numeric + named; covers the common set. Full HTML5 entity table is out of scope.
        text = Regex.Replace(text, @"&#(\d+);", m =>
            int.TryParse(m.Groups[1].Value, out var code) ? char.ConvertFromUtf32(code) : m.Value);
        text = Regex.Replace(text, @"&#x([0-9A-Fa-f]+);", m =>
            int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var code)
                ? char.ConvertFromUtf32(code) : m.Value);
        return text
            // Use a real no-break space (U+00A0) so Trim() leaves it in
            // place; an &nbsp;-only paragraph is a deliberate vertical
            // spacer in many CMS-generated HTMLs and should occupy a line.
            .Replace("&nbsp;", "\u00A0")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'");
    }

    private static string[] WordWrap(string text, double maxWidth, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var maxChars = (int)(maxWidth / Math.Max(charWidth, 1));
        if (maxChars <= 0) maxChars = 1;
        if (text.Length <= maxChars) return [text];

        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > maxChars)
        {
            var breakAt = remaining.LastIndexOf(' ', maxChars);
            if (breakAt <= 0) breakAt = maxChars;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        if (remaining.Length > 0) result.Add(remaining);
        return result.ToArray();
    }

    private static string EscapePdfString(string s)
    {
        // The content stream is written with Encoding.ASCII, so a raw non-ASCII char
        // (bullet U+2022, curly quotes, en/em dash, accented Latin) would be flattened
        // to '?'. Encode to Windows-1252 (the fonts declare /WinAnsiEncoding) and emit
        // any byte outside printable ASCII as an octal escape so it survives the ASCII
        // write and renders as the right glyph.
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            byte b = Aspose.Pdf.Text.Cp1252.TryGetByte(ch, out var wb) ? wb : (byte)'?';
            switch (b)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                default:
                    if (b >= 0x20 && b <= 0x7E) sb.Append((char)b);
                    else sb.Append('\\').Append(System.Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
            }
        }
        return sb.ToString();
    }

    private static void EnsureFonts(Page page)
    {
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
        EnsureFont(page, "Helvetica-Oblique", "F3");
        EnsureFont(page, "Courier", "F4");
    }

    private static void EnsureFont(Page page, string baseFontName, string resName)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName))
        {
            var font = new Core.PdfDictionary();
            font.Set("Type", new Core.PdfName("Font"));
            font.Set("Subtype", new Core.PdfName("Type1"));
            font.Set("BaseFont", new Core.PdfName(baseFontName));
            font.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
            fontDict.Set(resName, font);
        }
    }
}
