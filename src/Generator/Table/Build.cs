using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;
namespace Aspose.Pdf;

public partial class Table : BaseParagraph
{
    /// <summary>
    /// Build the table content stream bytes for the given page.
    /// Registers a Helvetica font in the page resources.
    /// </summary>
    public byte[] Build(Page page)
    {
        var tk = new TableDrawState();
        tk.page = page;
        tk.fontName = RegisterFont(tk.page);
        tk.colWidths = ParseColumnWidths(GetTableUsableWidth(tk.page));
        tk.builder = new ContentStreamBuilder();
        tk.pageHeight = tk.page.LayoutFrameHeight;

        tk.marginLeft = Margin?.Left ?? 0;
        tk.marginTop = Margin?.Top ?? 0;
        tk.tableX = Left + tk.marginLeft;
        tk.tableTopY = tk.pageHeight - Top - tk.marginTop;

        tk.builder.SaveState();

        // Draw table background if specified
        if (BackgroundColor is not null)
        {
            var totalWidth = 0.0;
            foreach (var w in tk.colWidths) totalWidth += w;
            var totalHeight = CalculateTotalHeight(tk.colWidths);

            tk.builder.SetFillColor(BackgroundColor);
            tk.builder.Rectangle(tk.tableX, tk.tableTopY - totalHeight, totalWidth, totalHeight);
            tk.builder.Fill();
        }

        tk.currentY = tk.tableTopY;
        for (var rowIdx = 0; rowIdx < Rows.Count; rowIdx++)
        {
            DrawTableRow(tk, rowIdx);
        }

        // Draw outer table border last so it sits on top of cell backgrounds and borders.
        if (Border is not null)
        {
            var totalWidth = 0.0;
            foreach (var w in tk.colWidths) totalWidth += w;
            var totalHeight = CalculateTotalHeight(tk.colWidths);

            DrawBorder(tk.builder, Border, tk.tableX, tk.tableTopY - totalHeight, totalWidth, totalHeight);
        }

        tk.builder.RestoreState();
        return tk.builder.Build();
    }

    private double[] ParseColumnWidths(double availableWidth = 0, bool clampToAvailable = true)
    {
        // A content-sized grid answers from its cells alone (see ContentSizedColumns).
        if (SizesColumnsToContent) return ContentSizedColumnWidths(availableWidth);
        var pw = new ColumnWidthState();
        pw.contentOverridesWidths = ColumnAdjustment == ColumnAdjustment.AutoFitToContent
            && !string.IsNullOrWhiteSpace(ColumnWidths)
            && RepeatingColumnsCount == 0
            && Broken is not TableBroken.Vertical and not TableBroken.VerticalInSamePage;
        if (ResolveUndeclaredWidths(pw, availableWidth) is { } resolveUndeclaredWidthsResult) return resolveUndeclaredWidthsResult;
        ParseDeclaredWidths(pw, availableWidth);
        FitHtmlColumnWidths(pw, availableWidth);

        pw.neededCols = pw.widths.Length;
        FinishColumnWidths(pw, availableWidth, clampToAvailable);
        return pw.widths;
    }

    /// <summary>Grows the columns that DECLARED a percent toward that percent of the
    /// box, sharing the surplus between them in proportion to their declared shares,
    /// and returns how much it placed. A column already at or past its declared share
    /// takes nothing — CSS treats the percent as the column's target, not a floor.</summary>
    private static double GrowDeclaredPercentColumns(double[] widths, double[] declShare, double surplus)
    {
        if (surplus <= 0.01) return 0;
        double room = 0, shares = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (declShare[i] <= 0) continue;
            room += Math.Max(0, declShare[i] - widths[i]);
            shares += declShare[i];
        }
        if (room <= 0 || shares <= 0) return 0;
        var give = Math.Min(surplus, room);
        double placed = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (declShare[i] <= 0) continue;
            // Proportional to the declared share, but never past this column's own
            // room — with one declared column (the pill's name cell) it simply takes
            // the whole surplus up to its percent.
            var add = Math.Min(give * declShare[i] / shares, Math.Max(0, declShare[i] - widths[i]));
            widths[i] += add;
            placed += add;
        }
        return placed;
    }

    /// <summary>Width available to the table's columns: the page content area inset by
    /// the table's left offset (mirrored on the right). Used to resolve percentage
    /// <see cref="ColumnWidths"/>.</summary>
    /// <summary>When set, the exact content width available to this table — a
    /// float-band COLUMN's width. The symmetric-margin guess below reads a
    /// right-column table (FlowLeftOffset ≈ half the page) as having almost no
    /// room and collapses its columns to per-character wraps.</summary>
    internal double UsableWidthOverride { get; set; }

    private double GetTableUsableWidth(Page page)
    {
        if (UsableWidthOverride > 0) return UsableWidthOverride;
        // A declared Left pins the table at an ABSOLUTE page x, so its band is what
        // is left between the pin and the page's right content margin — a 595 pt page
        // with 90 pt margins gives a table pinned at 440 a 65 pt band, and its
        // declared 85 pt column clamps into it.
        var leftOff = Left > 0
            ? Left + (Margin?.Left ?? 0)
            : (FlowLeftOffset > 0 ? FlowLeftOffset : (Margin?.Left ?? 0));
        // The columns run to the page's RIGHT content margin when the page declares
        // one (a 16.7/13.3 mm margin pair keeps a 510 pt grid whole); a page without
        // an explicit right margin mirrors the flow's left content offset, falling
        // back to the left offset itself. Mirroring the LEFT OFFSET would be wrong
        // for a pinned table, whose offset is a position on the page, not a margin.
        var rightMargin = page.PageInfo?.Margin is { RightTouched: true } pm
            ? pm.Right
            : FlowLeftOffset > 0 ? FlowLeftOffset : leftOff;
        var usable = page.Width - leftOff - rightMargin;
        if (usable <= 0) usable = page.Width - leftOff - 36;
        return usable > 0 ? usable : page.Width;
    }

    /// <summary>Width a MEASUREMENT (<see cref="GetHeight(Page?)"/>) resolves relative
    /// column widths against: the page's content band — its width less both page
    /// margins — because a table asked for its height has not been placed in the flow
    /// yet and so carries no <see cref="FlowLeftOffset"/>. Probed against the generator:
    /// a "50% 50%" table on a 595 pt page wraps a 208.25 pt word at a 90 pt margin
    /// (column 207.5) and keeps it whole at a 20 pt margin (column 277.5), i.e. the base
    /// tracks the margins, not the full page width.</summary>
    private double GetMeasureBandWidth(Page page)
    {
        if (UsableWidthOverride > 0) return UsableWidthOverride;
        if (FlowLeftOffset > 0) return GetTableUsableWidth(page);
        var info = page.PageInfo;
        var band = page.Width - (info?.Margin?.Left ?? 0) - (info?.Margin?.Right ?? 0);
        return band > 0 ? band : GetTableUsableWidth(page);
    }

    private double GetCellWidth(double[] colWidths, int colIndex, int colSpan)
    {
        var span = Math.Max(1, colSpan);
        var width = 0.0;
        var covered = 0;
        for (var i = colIndex; i < colIndex + span && i < colWidths.Length; i++)
        {
            width += colWidths[i];
            covered++;
        }
        // A spanning cell covers the gaps between the columns it spans.
        if (covered > 1) width += (covered - 1) * CellSpacingH;
        return width;
    }

    private double CalculateRowHeight(Row row, double[] colWidths)
    {
        if (row.FixedRowHeight > 0) return row.FixedRowHeight;

        var maxHeight = row.MinRowHeight;
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;

        for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
        {
            var cell = row.Cells.At(colIdx);
            if (cell.SpanContinuation) continue;
            var padding = cell.Margin ?? defaultPad;
            var padTop = padding?.Top ?? 2;
            var padBottom = padding?.Bottom ?? 2;
            var padLeft = padding?.Left ?? 2;
            var padRight = padding?.Right ?? 2;

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var fontSize = ResolveCellFontSize(cell, row);
            var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
            var availWidth = cellWidth - padLeft - padRight;

            var contentHeight = 0.0;
            foreach (var paragraph in cell.Paragraphs)
            {
                if (paragraph is TextFragment tf)
                {
                    var fragFontSize = ResolveCellParagraphFontSize(tf, fontSize, cell, row);
                    // Cell line pitch is exactly the font size (K = 1.0; the
                    // row formula is padTop + padBottom + borders +
                    // lineCount·fontSize) — the old 1.2× leading made every row
                    // ~20% too tall.
                    if (cell.IsWordWrapped && tf.Text.Length > 0)
                    {
                        var lines = WrapText(tf.Text, fragFontSize, availWidth);
                        contentHeight += lines.Count * fragFontSize;
                    }
                    else
                    {
                        contentHeight += fragFontSize;
                    }
                }
                else if (paragraph is HtmlFragment html)
                {
                    var plainText = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    if (plainText.Length > 0)
                        contentHeight += fontSize;
                }
            }

            var cellHeight = contentHeight + padTop + padBottom;
            if (cellHeight > maxHeight) maxHeight = cellHeight;
        }

        return maxHeight > 0 ? maxHeight : 20; // fallback minimum
    }

    private double CalculateTotalHeight(double[] colWidths)
    {
        var total = 0.0;
        for (var i = 0; i < Rows.Count; i++)
            total += CalculateRowHeight(Rows.At(i), colWidths);
        return total;
    }

    /// <summary>Width of the table's own box border — the space it claims outside the
    /// column block on each side. Zero when the table carries no full-box border.</summary>
    /// <summary>Left/right stroke widths the table's <see cref="DefaultCellBorder"/>
    /// adds to every column's pitch (see <see cref="CellBorderInPitch"/>); (0, 0)
    /// when the dialect sizes its own cell boxes or no horizontal side draws.</summary>
    private (double left, double right) CellBorderPitch()
    {
        if (!GeneratorCellModel) return (0, 0);
        // The table's own default border is the usual source of the pitch. A table
        // that instead gives EVERY cell the same border draws the same grid, so it
        // joins the pitch the same way: the column box grows by the strokes and the
        // DECLARED width stays the text box. Reading it as an inset instead shrank
        // every column by a stroke and wrapped its headings a line early.
        var b = DefaultCellBorder ?? UniformAssignedCellBorder();
        if (b is null) return (0, 0);
        // A DOUBLED side claims its clearance and its second rule on top of the stroke.
        var left = OccupiedSideWidth(b, BorderSide.Left, b.LeftAssigned, b.RawLeft);
        var right = OccupiedSideWidth(b, BorderSide.Right, b.RightAssigned, b.RawRight);
        // A COLLAPSED grid shares each boundary's rule between the two cells that
        // meet there, so a column bills half a stroke at each of its ends rather
        // than a whole one — and its DECLARED width already contains them, which
        // is why BuildMultiPage leaves those widths alone. At the grid's outer
        // edge the table's own border is the other party, and the wider rule owns
        // the boundary (see CollapsedBoundaries): a 3 pt table border around 0.5
        // cell rules puts the outermost rule centres 1.5 in, not 0.25.
        if (!IsBordersCollapsed) return (left, right);
        if (Border is { } outer)
        {
            left = Math.Max(left, OccupiedSideWidth(outer, BorderSide.Left, outer.LeftAssigned, outer.RawLeft));
            right = Math.Max(right, OccupiedSideWidth(outer, BorderSide.Right, outer.RightAssigned, outer.RawRight));
        }
        return (left / 2, right / 2);
    }

    private BorderInfo? _uniformCellBorder;
    private bool _uniformCellBorderResolved;

    /// <summary>Descent of the face a cell's text draws in, as a positive fraction of
    /// the em: the hhea descender of a TrueType face, the AFM descender of a
    /// Standard-14 face, Helvetica's 0.207 when nothing names one. The generator seats
    /// every cell baseline one descent above the full-em drop and bounds the text
    /// clip a descent below the last baseline (Helvetica 0.207, Calibri 0.25 —
    /// a header row seats at top − 0.75 × 15).</summary>
    private (double DescentEm, string? Face, bool FragmentFace) CellFontDescentEm(Cell cell, Row row)
    {
        Aspose.Pdf.Text.Font? face = null;
        foreach (var p in cell.Paragraphs)
            if (p is TextFragment tf && NamedFragmentFace(tf) is { } f) { face = f; break; }
        var fragmentFace = face is not null;
        // A fragment naming a Standard-14 face by name alone seats by that face's descent.
        if (face is null)
            foreach (var p in cell.Paragraphs)
                if (p is TextFragment tf && NamedStandard14Face(tf, tf.TextState.IsBold, tf.TextState.IsItalic) is { } named)
                    return (Math.Abs(Standard14Fonts.GetDescent(named)) / 1000.0, named, true);
        face ??= cell.DefaultCellTextState?.Font ?? row.DefaultCellTextState?.Font ?? DefaultCellTextState?.Font;
        // A default state that names a face only by NAME (TextState("Arial")) still
        // counts as a named default face.
        var namedDefault = !fragmentFace && (face is not null
            || !string.IsNullOrEmpty(cell.DefaultCellTextState?.FontName)
            || !string.IsNullOrEmpty(row.DefaultCellTextState?.FontName)
            || !string.IsNullOrEmpty(DefaultCellTextState?.FontName));
        if (face is null) return (HelveticaDescentEm, null, !namedDefault);
        double d = 0;
        try
        {
            if (face.SourceFontData?.TtfData is { } ttf) d = TextBuilder.HheaDescentPerMille(ttf);
            if (d == 0 && face.FontName is { Length: > 0 } name) d = Standard14Fonts.GetDescent(name);
        }
        catch { d = 0; }
        return (d != 0 ? Math.Abs(d) / 1000.0 : HelveticaDescentEm, face.FontName, fragmentFace);
    }

    /// <summary>The face a fragment names itself: on its own state or, when that is
    /// the shared default, on its first segment naming one (a segment-built fragment
    /// carries its face there, and the generator seats it by that face's descent all
    /// the same). TextState.Font is never null (it defaults to the shared Helvetica) —
    /// a state NAMES a face only when it was assigned one.</summary>
    private static Aspose.Pdf.Text.Font? NamedFragmentFace(TextFragment tf)
    {
        if (tf.TextState.Font is { } f && !ReferenceEquals(f, FontInfo.DefaultHelvetica)) return f;
        foreach (var seg in tf.Segments)
            if (seg.TextState.Font is { } sf && !ReferenceEquals(sf, FontInfo.DefaultHelvetica)) return sf;
        return null;
    }

    /// <summary>How far a subscript run extends the clip BELOW the line's descent and
    /// ABOVE its line box, in ems of the line size, per face. Probed at 20 pt on
    /// eight faces (gt2_subclip_probe): the pair is a property of the face that no
    /// ascent/descent metric reproduces (Arial 0.2119 and Helvetica 0.207 descents
    /// give 0.4558 and 0.4214), so the probed values are carried as measured; an
    /// unprobed face takes Helvetica's. The superscript extension (0.14616) and the
    /// sub+superscript coupling (the top grows 1.9135 × the subscript's rise) are the
    /// same on every face.</summary>
    private static readonly Dictionary<string, (double Below, double Above)> SubscriptClipEm = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = (0.42136, 0.02158), ["Arial"] = (0.45580, 0.02606),
        ["Times-Roman"] = (0.42302, 0.02099), ["Times New Roman"] = (0.45709, 0.02582),
        ["Courier"] = (0.36899, 0.01833), ["Calibri"] = (0.45896, 0.02332),
        ["Verdana"] = (0.47103, 0.02834), ["Tahoma"] = (0.46790, 0.02814),
    };
    private static (double Below, double Above) SubscriptClip(string? face)
    {
        if (face is { Length: > 0 })
        {
            if (SubscriptClipEm.TryGetValue(face, out var v)) return v;
            // Styled names ("Helvetica-Bold", "Arial,Bold", "Calibri Bold") share the family's pair.
            foreach (var kv in SubscriptClipEm)
                if (face.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }
        return SubscriptClipEm["Helvetica"];
    }

    /// <summary>Per-side (left, bottom, right, top) doubled clearances of a border.</summary>
    private static (double l, double b, double r, double t) DoubledOutsets(BorderInfo border)
        => (DoubledOutset(border, BorderSide.Left, border.LeftAssigned, border.RawLeft),
            DoubledOutset(border, BorderSide.Bottom, border.BottomAssigned, border.RawBottom),
            DoubledOutset(border, BorderSide.Right, border.RightAssigned, border.RawRight),
            DoubledOutset(border, BorderSide.Top, border.TopAssigned, border.RawTop));

    /// <summary>Per-side (left, bottom, right, top) insets of a pitch-mode cell box.
    /// With <paramref name="half"/> the strokes' insets: half a width on every
    /// flag-enabled side so the stroke's outer edge rides the box edge (an
    /// assignment-enabled side already insets itself in DrawBorder); without it
    /// the full stroke widths, which bound the fill and the text clip.</summary>
    private static (double l, double b, double r, double t) SideInsets(BorderInfo border, bool half)
    {
        double Side(BorderSide flag, bool assigned, GraphInfo? gi)
        {
            var w = DrawnSideWidth(border, flag, assigned, gi);
            if (!half) return w;
            return assigned && !border.Side.HasFlag(flag) ? 0 : w / 2;
        }
        return (Side(BorderSide.Left, border.LeftAssigned, border.RawLeft),
                Side(BorderSide.Bottom, border.BottomAssigned, border.RawBottom),
                Side(BorderSide.Right, border.RightAssigned, border.RawRight),
                Side(BorderSide.Top, border.TopAssigned, border.RawTop));
    }

    // ---- Bold-serif HTML cell path -------------------------------------------------
    // An HtmlFragment whose whole content is a single <b>/<strong> run renders on
    // HTML-engine metrics: embedded Times New Roman Bold at the HTML default
    // 12pt, laid out in a CSS line box (pixel-quantized normal leading over the win
    // content box — computed in BoldSerifTtf) with pair-kerned advances and no cell
    // padding. Gated on exactly this shape so all other HtmlFragment cells keep the
    // legacy plain-text path.

    // Installed-face TTF bytes by (family, bold, italic) for HonorCellTtfFaces cells;
    // a miss is cached too so unavailable faces fall through to the Standard-14 path once.
    private static readonly Dictionary<(string fam, bool bold, bool italic), byte[]?> _cellFaceTtfs = new();

    private static byte[]? _serifTtf;          // Times New Roman (root strut face)

    private static byte[]? _serifBoldTtf;      // Times New Roman Bold

    private static bool _serifTried;

    private static double _serifRootBox;       // pt: root line-box height at 12pt (13.5)

    private static double _serifBaseDrop;      // pt: line-box top → baseline (10.79883)

    private static double _serifDescFrac;      // usWinDescent / upm (descent per pt of size)

    /// <summary>Resolve the serif faces (regular + bold Times New Roman) and the root
    /// CSS line-box metrics: the hhea line height rounds to whole CSS pixels (12pt em =
    /// 16px), the surplus over the win content box splits into half-leading, the baseline
    /// sits winAscent + halfLead below the box top. Every line of an HTML-engine cell
    /// occupies the ROOT 12pt box regardless of its own run sizes; the cell's content
    /// height ends at lastBaseline + winDescent·lastSize. (Exact for
    /// serif/sans faces at 9-13pt.) Null when the faces are unavailable.</summary>
    private static readonly object _serifInit = new();

    // Root-face metrics behind the CSS line box, kept in font units so any size resolves.
    private static double _serifUpm, _serifHheaSum, _serifWinAsc, _serifWinDesc;

    /// <summary>The CSS line box of the root serif face at <paramref name="size"/>:
    /// the hhea line height rounds to whole CSS pixels, the surplus over the win
    /// content box splits into half-leading, and the baseline sits winAscent +
    /// half-leading below the box top. Returns (box height, baseline drop) in points.</summary>
    private static (double Box, double Drop) SerifLineBox(double size)
    {
        if (_serifUpm <= 0 || size <= 0) return (0, 0);
        var pxem = size * 96.0 / 72.0;
        var lpx = Math.Round(_serifHheaSum * pxem / _serifUpm, MidpointRounding.AwayFromZero);
        var lunits = lpx * _serifUpm / pxem;
        var halfLead = (lunits - (_serifWinAsc + _serifWinDesc)) / 2;
        return (lunits * size / _serifUpm, (_serifWinAsc + halfLead) * size / _serifUpm);
    }


    /// <summary>A span's inline style, as the engine dialect reads it: colour (named or
    /// #hex), an own font-size (px scales at 0.75 pt/px; pt and UNITLESS are points -
    /// probed 2026-08-28: font-size:33 with no unit renders 33pt glyphs, font-size:12
    /// renders 12pt), and text-decoration: underline.</summary>
    private static (Color? Color, double Size, bool Underline) ParseSpanStyle(string tag)
    {
        Color? color = null;
        double size = 0;
        var underline = false;
        var css = EngineStyleValue(tag);
        if (css is null) return (null, 0, false);
        var cm = Regex.Match(css, @"(?:^|;)\s*color(?!-)\s*:\s*(?<c>#[0-9a-fA-F]{3,8}|[A-Za-z]+)",
            RegexOptions.IgnoreCase);
        if (cm.Success)
        {
            var cv = cm.Groups["c"].Value;
            try
            {
                var sys = cv.StartsWith('#')
                    ? System.Drawing.ColorTranslator.FromHtml(cv)
                    : System.Drawing.Color.FromName(cv);
                if (Compat.IsKnownColor(sys) || cv.StartsWith('#')) color = Color.FromRgb(sys);
            }
            catch { }
        }
        var fm = Regex.Match(css, @"font-size\s*:\s*(?<n>[\d.]+)\s*(?<u>px|pt)?",
            RegexOptions.IgnoreCase);
        if (fm.Success && double.TryParse(fm.Groups["n"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var fv) && fv > 0)
            size = string.Equals(fm.Groups["u"].Value, "px", StringComparison.OrdinalIgnoreCase)
                ? fv * 0.75 : fv;
        if (Regex.IsMatch(css, @"text-decoration\s*:[^;]*underline", RegexOptions.IgnoreCase))
            underline = true;
        return (color, size, underline);
    }

    // ── The escaped-newline footer fragment ────────────────────────────────
    // A footer HtmlFragment authored as ONE source line whose newlines are the
    // literal two-character "\n" sequences (the author escaped them). Measured
    // directly, the whole rendering falls out of
    // parsing the markup exactly as written:
    //  · the "\n" pairs are TEXT and draw as backslash+n glyph runs;
    //  · every <style> declaration starts with that "\n" junk, so CSS error
    //    recovery drops them ALL — the serif default (Times 12, 13.5 pt boxes)
    //    typesets everything;
    //  · attribute values written as \"…\" are unquoted values starting with a
    //    backslash — invalid, so colspan and text-align are ignored (the title
    //    <th> is confined to column 1 and every th centres, the HTML default);
    //  · stray "\n" text between the table's structural tags foster-parents to
    //    one run ABOVE the table (centred inside <center>, at the band's left
    //    edge outside it);
    //  · columns get the HTML default chrome (cellspacing 2px, cellpadding
    //    1px) across the full band width, distributed min-content plus the
    //    surplus in proportion to (max − min) — all five measured columns
    //    reproduce to 0.01 pt.

    /// <summary>Exact Standard-14 advance width (no wrap-inflation) for positioning inline
    /// cell runs, so a graph/text sequence lands where the generator places it.</summary>
    // Cache of glyph-outline parsers keyed by the raw TTF bytes, so a per-segment embedded
    // font (e.g. NotoSans / NotoSansArabic) is measured with its real advances once.
    private static readonly Dictionary<byte[], Aspose.Pdf.Text.GlyphOutlineParser?> _inlineGlyphParsers =
        new(ReferenceEqualityComparer.Instance);

}
