using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Table of contents information for a page.
/// When set on a page via <see cref="Page.TocInfo"/>, the page acts as a TOC page.
/// </summary>
public sealed class TocInfo
{
    public TocInfo()
    {
        ColumnInfo = new ColumnInfo();
        IsShowPageNumbers = true;
        IsCountTocPages = true;
        LineDash = TabLeaderType.Dot;
    }

    /// <summary>Title displayed at the top of the TOC page.</summary>
    public TextFragment? Title { get; set; }

    /// <summary>Whether page numbers are shown next to each TOC entry.</summary>
    public bool IsShowPageNumbers { get; set; }

    /// <summary>Multi-column layout descriptor for the TOC page. Auto-initialised
    /// so callers can write <c>tocInfo.ColumnInfo.ColumnCount = 2</c> directly.</summary>
    public ColumnInfo? ColumnInfo { get; set; }

    /// <summary>True when the TOC headings should also be appended to the
    /// document's outline tree. Stored only — the FOSS TOC pipeline does not
    /// emit /Outlines entries.</summary>
    public bool CopyToOutlines { get; set; }

    /// <summary>Length of <see cref="FormatArray"/>; setter resizes the array
    /// padding with default <see cref="LevelFormat"/> instances.</summary>
    public int FormatArrayLength
    {
        get => FormatArray?.Length ?? 0;
        set
        {
            if (value <= 0) { FormatArray = Array.Empty<LevelFormat>(); return; }
            var src = FormatArray ?? Array.Empty<LevelFormat>();
            var dst = new LevelFormat[value];
            for (var i = 0; i < value; i++)
                dst[i] = i < src.Length ? src[i] : new LevelFormat();
            FormatArray = dst;
        }
    }

    /// <summary>Per-level format descriptors. Stored only.</summary>
    public LevelFormat[]? FormatArray { get; set; }

    /// <summary>True when the TOC's own page count is included in the
    /// destination page numbers it prints. Stored only.</summary>
    public bool IsCountTocPages { get; set; }

    /// <summary>Tab-leader style between heading text and page number.</summary>
    public TabLeaderType LineDash { get; set; } = TabLeaderType.None;

    /// <summary>String inserted before each page number ("p. ", "Page ", …).</summary>
    public string? PageNumbersPrefix { get; set; }
}

/// <summary>Per-heading-level TOC formatting descriptor (line-dash style,
/// margins, indent, text state). Stored only by the FOSS TOC pipeline.</summary>
public sealed class LevelFormat
{
    /// <summary>Leader style for this level's entries. The per-level default
    /// is Dot: once a FormatArray is in play, each level's own LineDash governs
    /// its leader (an explicit None suppresses it) and the TocInfo-level
    /// LineDash is consulted only when no level format exists.</summary>
    public TabLeaderType LineDash { get; set; } = TabLeaderType.Dot;

    /// <summary>Margin for this level's TOC entry. Auto-initialized so callers
    /// can write <c>level.Margin.Left = 0</c> on a fresh instance.</summary>
    public MarginInfo Margin { get; set; } = new MarginInfo();

    public float SubsequentLinesIndent { get; set; }

    /// <summary>Text state for this level's TOC entry. Auto-initialized so
    /// callers can write <c>level.TextState.FontSize = 10</c> directly.</summary>
    public Aspose.Pdf.Text.TextState TextState { get; set; } = new Aspose.Pdf.Text.TextState();
}

/// <summary>
/// Represents a TOC heading entry that links to a destination page.
/// Added to a page's Paragraphs collection.
/// </summary>
public class Heading : BaseParagraph
{
    /// <summary>Heading level (1-based).</summary>
    public int Level { get; set; }

    /// <summary>Numbering style used for auto-numbered headings.</summary>
    public NumberingStyle Style { get; set; }

    /// <summary>Text segments of this heading entry.</summary>
    public TextSegmentCollection Segments { get; } = new();

    /// <summary>The TOC page this heading belongs to.</summary>
    public Page? TocPage { get; set; }

    /// <summary>The destination page this heading links to.</summary>
    public Page? DestinationPage { get; set; }

    /// <summary>
    /// Y coordinate (in points) of the destination on
    /// <see cref="DestinationPage"/> that this TOC entry's link points
    /// to. Used by the layout engine when emitting the TOC's link
    /// annotations so the click jumps to the right vertical position.
    /// Stored only; per-entry destination links are not currently emitted.
    /// </summary>
    public double Top { get; set; }

    /// <summary>Whether the heading is inserted into the TOC list.</summary>
    public bool IsInList { get; set; }

    /// <summary>Whether the heading number is auto-incremented.</summary>
    public bool IsAutoSequence { get; set; }

    /// <summary>Default text state applied to the heading text. Stored
    /// only; the heading layout reads font/size from the first segment's
    /// TextState.</summary>
    public Aspose.Pdf.Text.TextState TextState { get; set; } = new();

    /// <summary>Convenience text accessor — sets a single TextSegment as the heading text.</summary>
    public string Text
    {
        get => Segments.Count > 0 ? Segments[1].Text : string.Empty;
        set
        {
            Segments.Clear();
            Segments.Add(new TextSegment(value));
        }
    }

    public Heading(int level) => Level = level;

    /// <summary>Heading auto-sequence start number. Stored only.</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>Custom user label shown in place of the auto number. Stored only.</summary>
    public TextSegment? UserLabel { get; set; }

    /// <summary>Shallow clone of this heading. Segments list is empty; configure as needed.</summary>
    public override object Clone()
    {
        var copy = new Heading(Level)
        {
            Style = Style,
            TocPage = TocPage,
            DestinationPage = DestinationPage,
            Top = Top,
            IsInList = IsInList,
            IsAutoSequence = IsAutoSequence,
            TextState = TextState,
            StartNumber = StartNumber,
            UserLabel = UserLabel,
        };
        return copy;
    }

    /// <summary>Clone the heading and copy its segments by reference.</summary>
    public object CloneWithSegments()
    {
        var copy = (Heading)Clone();
        foreach (var s in Segments) copy.Segments.Add(s);
        return copy;
    }

    /// <summary>
    /// Build the content stream for this heading entry on the TOC page.
    /// Returns the rendered height in points.
    /// </summary>
    /// <summary>Format <paramref name="n"/> (1-based) for an auto-sequenced
    /// heading according to <paramref name="style"/>. Returns the bare token
    /// (e.g. "iv", "C", "3") with no trailing separator.</summary>
    internal static string FormatNumber(NumberingStyle style, int n)
    {
        switch (style)
        {
            case NumberingStyle.LowerRoman: return ToRoman(n).ToLowerInvariant();
            case NumberingStyle.UpperRoman: return ToRoman(n);
            case NumberingStyle.LowerAlpha: return ToAlpha(n).ToLowerInvariant();
            case NumberingStyle.UpperAlpha: return ToAlpha(n);
            case NumberingStyle.None: return string.Empty;
            default: return n.ToString();
        }
    }

    private static string ToRoman(int n)
    {
        if (n <= 0) return n.ToString();
        var map = new (int v, string s)[]
        {
            (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),(100,"C"),(90,"XC"),
            (50,"L"),(40,"XL"),(10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I"),
        };
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in map) { while (n >= v) { sb.Append(s); n -= v; } }
        return sb.ToString();
    }

    private static string ToAlpha(int n)
    {
        // 1->A, 26->Z, 27->AA, ...
        var sb = new System.Text.StringBuilder();
        while (n > 0) { n--; sb.Insert(0, (char)('A' + n % 26)); n /= 26; }
        return sb.ToString();
    }

    internal (byte[] content, double height) Build(Page page, double x, double y, string fontName)
        => Build(page, x, y, fontName, "");

    internal (byte[] content, double height) Build(Page page, double x, double y,
        string fontName, string numberPrefix)
    {
        var builder = new Content.ContentStreamBuilder();
        var totalText = string.Join("", Segments.Select(s => s.Text));
        // Real Helvetica advances for the wrap: the crude half-em estimate
        // under-fills typical lines (breaking ~10 chars early); each line
        // must fill to the real measured width.
        static double MeasureHelv(string s, double fs)
        {
            double w = 0;
            foreach (var c in s)
            {
                var cw = Aspose.Pdf.Text.Standard14Fonts.GetWidth("Helvetica", c < 256 ? c : '?');
                if (cw < 0) cw = 500;
                w += cw * fs / 1000.0;
            }
            return w;
        }
        // The heading's OWN TextState wins when the caller set it (a content
        // heading with TextState.FontSize = 12 renders 12 pt even though its
        // segment was created at the 10 pt default); an explicitly-sized
        // segment comes next; the legacy segment fallback stays for untouched
        // headings so their metrics don't shift.
        double fontSize = TextState.FontSizeTouched ? TextState.FontSize
            : Segments.FirstOrDefault(s => s.TextState.FontSizeTouched)?.TextState.FontSize
            ?? (Segments.Count > 0 ? Segments[1].TextState.FontSize : 12);
        var lineSpacing = Segments.Count > 0 && Segments[1].TextState.LineSpacing > 0
            ? Segments[1].TextState.LineSpacing
            : fontSize * 1.2;

        // Word-wrap the text to fit page width, filling each line to the REAL
        // measured width ("…under the plan onaccount" /
        // "of each allowed" break exactly where the Helvetica advances run out).
        var availWidth = page.Width - x - 72; // right margin

        var lines = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var word in totalText.Split(' '))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (MeasureHelv(trial, fontSize) <= availWidth || cur.Length == 0)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
            }
            else
            {
                lines.Add(cur.ToString());
                cur.Clear();
                cur.Append(word);
            }
        }
        if (cur.Length > 0 || lines.Count == 0) lines.Add(cur.ToString());

        // First baseline drops by the cap-height ascent from the band top (the
        // same placement the flow's plain-fragment writer uses), so a heading
        // line chains bottoms with its neighbours by exactly its own font size
        // — stepping 758 → 748 → … → next heading at −12.
        var capHeight = Aspose.Pdf.Text.Standard14Fonts.GetCapHeight("Helvetica");
        var ascent = capHeight > 0 ? capHeight / 1000.0 * fontSize : fontSize * 0.7;
        var baseline = y - ascent;

        // Every content heading opens with an EMPTY text show at
        // the line start in the auto-created first segment's own size (so
        // extraction reports an empty 10 pt fragment before a 12 pt heading).
        if (Segments.Count > 1 && string.IsNullOrEmpty(Segments[0].Text))
            builder.BeginText().SetFont(fontName, Segments[0].TextState.FontSize > 0
                    ? (double)Segments[0].TextState.FontSize : 10)
                .SetFillColor(0, 0, 0)
                .MoveTextPosition(x, baseline).ShowText(string.Empty).EndText();

        // The auto-sequence number is its OWN show at the margin and the
        // heading text starts at a fixed 20 pt tab stop after it
        // ("1  " at x=40, "Heading 0" at x=60 regardless of the number width).
        var textX = x;
        if (numberPrefix.Length > 0)
        {
            builder.BeginText().SetFont(fontName, fontSize).SetFillColor(0, 0, 0)
                .MoveTextPosition(x, baseline).ShowText(numberPrefix).EndText();
            textX = x + 20;
        }

        builder.BeginText();
        builder.SetFont(fontName, fontSize);
        builder.SetFillColor(0, 0, 0);
        builder.MoveTextPosition(textX, baseline);

        for (var i = 0; i < lines.Count; i++)
        {
            // Continuation lines return to the heading's left edge (the
            // number-tab indent applies to the FIRST line only —
            // "b.a  the value…" wraps back to the margin).
            if (i == 1)
                builder.MoveTextPosition(x - textX, -lineSpacing);
            else if (i > 1)
                builder.MoveTextPosition(0, -lineSpacing);
            builder.ShowText(lines[i]);
        }
        builder.EndText();

        var height = fontSize + (lines.Count - 1) * lineSpacing;
        return (builder.Build(), height);
    }
}
