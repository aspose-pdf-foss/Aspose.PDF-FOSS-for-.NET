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
    private RowPlan BuildRowPlan(Row row, double[] colWidths, int[] cellMap,
        int[]? gridToCell = null, int[]? effRowSpan = null, double svgFillHeight = 0)
    {
        var rp = new RowPlanState();
        rp.plan = new RowPlan { Row = row, GridToCell = gridToCell, EffRowSpan = effRowSpan };
        // Non-grid rows walk cells by GRID position: a ColSpan cell consumes span
        // columns, so a cell after it starts past the span (not at the next column
        // index). Only with an identity cellMap — column-band chunking (non-identity)
        // keeps its own mapping.
        if (gridToCell is null)
        {
            var identityMap = true;
            for (var i = 0; i < cellMap.Length; i++)
                if (cellMap[i] != i) { identityMap = false; break; }
            if (identityMap)
            {
                var colToCell = new int[colWidths.Length];
                for (var i = 0; i < colToCell.Length; i++) colToCell[i] = -1;
                var gc = 0;
                for (var ci = 0; ci < row.Cells.Count && gc < colToCell.Length; ci++)
                {
                    // Continuation copies published by a previous layout pass stand for
                    // columns their originating cell already consumed — walking them
                    // again would advance the grid once per spanned column.
                    if (row.Cells.At(ci).SpanContinuation) continue;
                    colToCell[gc] = ci;
                    var sp = Math.Max(1, Math.Min(row.Cells.At(ci).ColSpan, colToCell.Length - gc));
                    for (var s = 1; s < sp; s++) colToCell[gc + s] = -2;
                    gc += sp;
                }
                rp.plan.ColToCell = colToCell;
            }
        }
        rp.defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;
        rp.maxLineHeight = 0;
        rp.tightForMax = 0;
        rp.maxVertPad = 0;
        rp.maxTopPad = 0;
        rp.cellTotals = new List<(double padV, int lineCount, double tight, double exact, double ownStack)>();

        for (var col = 0; col < colWidths.Length; col++)
            if (!BuildRowPlanColumn(col, rp, row, colWidths, cellMap, gridToCell, effRowSpan, svgFillHeight)) break;
        foreach (var cellLines2 in rp.plan.CellLines)
            foreach (var cl2 in cellLines2)
                if (cl2.Leading > rp.plan.Leading) rp.plan.Leading = cl2.Leading;
        rp.plan.LineHeight = rp.maxLineHeight > 0 ? rp.maxLineHeight : DefaultLineHeightPt;
        rp.plan.TightLine = rp.tightForMax > 0 ? rp.tightForMax : rp.plan.LineHeight;
        // Band-doc tables (HonorCellFontFaces): unstyled text rows advance at the CSS
        // line box of their font size — round(pt·(4/3)·1.15)px·0.75, e.g. 9 pt for an
        // 8 pt line — not at the bare font size, which packs multi-line cells ~1 pt/line
        // tighter than a browser lays them out.
        // The lifted HTML render lays its text out on the same browser line box (its
        // 8 pt body columns pitch at 9 pt, line for line with a browser).
        // …and so does a grid the stylesheet styles: its 8 pt body columns pitch at 9,
        // line for line with a browser. A table the stylesheet never addresses
        // keeps the calibrated bare-em pitch — re-pitching it grows every row ~12 %
        // and walks the whole table down the page.
        if ((HonorCellFontFaces || (NestedTableRender && HtmlChainStyledCells))
            && rp.plan.CssContentH <= 0 && rp.maxLineHeight > 0)
        {
            rp.plan.LineHeight = CssLineBoxPt(rp.plan.LineHeight);
            rp.plan.TightLine = CssLineBoxPt(rp.plan.TightLine);
        }
        // An inline-styled grid pitches at ITS face's CSS line box (Verdana's
        // 2489/2048 em puts a 9 px cell on an 11 px = 8.25 pt line, the pitch the
        // reference rows step at).
        else if (InlineFaceGridRatio > 0 && rp.plan.CssContentH <= 0 && rp.maxLineHeight > 0)
        {
            rp.plan.LineHeight = FaceCssLineBoxPt(rp.plan.LineHeight, InlineFaceGridRatio);
            rp.plan.TightLine = FaceCssLineBoxPt(rp.plan.TightLine, InlineFaceGridRatio);
        }
        rp.rowContentH = rp.plan.LineCount == 0 ? 0.0
            : (rp.plan.LineCount - 1) * rp.plan.LineHeight + rp.plan.TightLine;
        rp.maxCellTotal = 0.0;
        rp.anyExactCell = false;
        rp.maxOwnTotal = 0.0;
        foreach (var (cpv, cn, ctight, cexact, cown) in rp.cellTotals)
        {
            var ch = cexact > 0 ? cexact : cn == 0 ? 0 : (cn - 1) * rp.plan.LineHeight + ctight;
            if (cexact > 0) rp.anyExactCell = true;
            if (cpv + ch > rp.maxCellTotal) rp.maxCellTotal = cpv + ch;
            if (cn > 0 && cpv + cown > rp.maxOwnTotal) rp.maxOwnTotal = cpv + cown;
        }
        // A row holding an exact-stack control cell sizes to the max over cells
        // of each cell's OWN stacked height (every text line at its own font
        // size, boxes at their box height) — the uniform grid would price a
        // 7pt side label at the row's 10pt pitch and oversize the row.
        rp.plan.ExactTotalH = rp.anyExactCell ? rp.maxOwnTotal : 0;
        // UA cell boxes: the row's content already stacks on the CSS line-box grid, so
        // the padding is simply the widest cell's own — deriving it from
        // maxCellTotal−rowContentH would net off the difference between the per-cell
        // tight line (the glyph height) and the row's line box and swallow most of it.
        // CSS run boxes stack on the same CSS line-box grid, so the padding is likewise
        // the widest cell's own: netting maxCellTotal against rowContentH would cancel
        // the difference between a cell's TIGHT line (its glyph height) and the row's
        // line box and swallow most of the padding.
        // A grid drawing in the document's own face stacks on that face's CSS line box
        // as well, so its padding is the widest cell's own for the same reason. The
        // lifted nested-table render keeps the cells' own padding too: its rows stack
        // reserve lines/css boxes exactly, and the net-off would swallow the
        // cellspacing bands the outer table declares.
        rp.plan.VertPadding = UaCellBoxes || CssRunBoxes || HonorCellTtfFaces || NestedTableRender
            ? rp.maxVertPad
            : rp.plan.LineCount == 0 || rp.maxCellTotal <= 0
            ? rp.maxVertPad
            : Math.Max(0, rp.maxCellTotal - rp.rowContentH);
        rp.plan.CellPadV = rp.maxVertPad;
        rp.plan.TopPad = rp.maxTopPad;
        rp.plan.MinBlankHeight = Math.Max(row.FixedRowHeight, row.MinRowHeight);
        // A content-less row reserves a single line (no padding, see the slice loop) —
        // matching the generator's tight spacer rows rather than a full row. Band-doc
        // tables (HonorCellFontFaces) collapse it to nothing instead: their all-empty
        // rows are CSS column-width definitions (<td width="6%"></td>…), which browsers
        // lay out at zero height.
        if (rp.plan.MinBlankHeight <= 0)
            // A row with NO CELLS reserves nothing: it draws nothing and the generator
            // gives it no height (a `Rows.Add()` with only FixedRowHeight = 0 in front of
            // a fixed-height row leaves that row at the content top, not a line below it).
            rp.plan.MinBlankHeight = row.Cells.Count == 0 ? 0
                : rp.plan.LineCount != 0 ? 20
                : HonorCellFontFaces ? 0
                // redline grids: a hidden-content row collapses to the tight
                // spacer drawn for it (3.2 pt — measured net of the next
                // row's own billed margin-top)
                : RedlineCellSeat ? 3.2
                // A column-pagination slice: this row's text lives in another
                // slice; here it is one line of the row's cell font (the height
                // the row has where its text renders), not the generic blank slot.
                : ColumnSliceChild && row.Cells.Count > 0
                    ? ResolveFragmentDrawSize(row.Cells.At(0), row)
                : rp.plan.LineHeight;
        // A whitespace-only row (e.g. a " " spacer) is likewise a tight spacer drawn
        // without cell padding so it reserves just its line.
        // …but under CSS run boxes a row of empty cells is an ORDINARY row: the browser
        // gives it its cells' padding plus one line box (the invisible character those
        // cells hold is still a line), which is what draws its rules.
        // Under the lifted nested-table render, a row whose "blank" lines are
        // nested-table or image RESERVES is content, not a spacer — dropping its
        // padding would strip the cellspacing bands and jam the nested grid
        // against the row border. Legacy dialects keep the historical rule.
        rp.plan.IsBlankRow = !CssRunBoxes && rp.plan.CellInline is null && rp.plan.LineCount > 0
            && row.FixedRowHeight <= 0
            && !((NestedTableRender || GeneratorCellModel) && rp.plan.CellTables is not null)
            && System.Linq.Enumerable.All(rp.plan.CellLines,
                cl => System.Linq.Enumerable.All(cl,
                    l => string.IsNullOrWhiteSpace(l.Text) && !(NestedTableRender && l.ImgReserve)));
        // XML-generator dialect: an all-empty row is an
        // ORDINARY row — its cells' padding plus one line at the default cell
        // font size (5+10+5 = 20 for the padded report rows), never the tight
        // spacer or the 1.2-em default line.
        if (XmlGeneratorModel)
        {
            rp.plan.IsBlankRow = false;
            if (rp.plan.LineCount == 0)
            {
                var xmlFs = DefaultCellTextState?.FontSize > 0 ? (double)DefaultCellTextState.FontSize : 10.0;
                rp.plan.MinBlankHeight = Math.Max(rp.plan.MinBlankHeight, rp.maxVertPad + xmlFs + XmlLineSpacing);
            }
            // Every line advances by its OWN font size (a 14/10/14 pt paragraph
            // stack is 38 pt of content, not 3 × 14) — the row sizes to the exact
            // per-cell stacks, like the control-cell rule.
            else if (rp.maxOwnTotal > 0)
                rp.plan.ExactTotalH = rp.maxOwnTotal;
        }
        return rp.plan;
    }

    /// <summary>Effective font size for a cell text fragment: the fragment's own size when
    /// set, else the first segment that carries one (callers commonly set size on the
    /// TextSegment rather than the TextFragment), else the cell default.</summary>
    /// <summary>Font size a content-less cell's row DRAWS at: the declared
    /// default state's size when one exists, else the TextFragment ctor's 10 pt
    /// placeholder (what an undeclared cell fragment actually renders with).</summary>
    private double ResolveFragmentDrawSize(Cell cell, Row row)
    {
        if (cell.DefaultCellTextState is null && row.DefaultCellTextState is null
            && DefaultCellTextState is null)
            return new Aspose.Pdf.Text.TextState().FontSize;
        return ResolveCellFontSize(cell, row);
    }

    /// <summary>A cell's font size when a DefaultCellTextState declares one: the
    /// cell's, else the row's, else the table's. Probed against the generator — a cell
    /// paragraph is drawn with the declared default even when the fragment carries its
    /// own size, whether the size was set before or after the fragment's text (a 9 pt
    /// fragment in a table declaring 12 pt draws at 12). Null when nothing declares
    /// one, in which case the fragment's own state sizes it.</summary>
    private double? DeclaredCellFontSize(Cell? cell, Row? row)
    {
        if (cell?.DefaultCellTextState is { FontSizeTouched: true } c) return c.FontSize;
        if (row?.DefaultCellTextState is { FontSizeTouched: true } r) return r.FontSize;
        if (DefaultCellTextState is { FontSizeTouched: true } t) return t.FontSize;
        return null;
    }

    /// <summary>Bold requested by a DefaultCellTextState (cell, else row, else table).
    /// Same contract as <see cref="DeclaredCellFontSize"/>: the declared default styles
    /// the cell's paragraphs.</summary>
    private bool DeclaredCellBold(Cell? cell, Row? row) =>
        (cell?.DefaultCellTextState?.IsBold ?? false)
        || (row?.DefaultCellTextState?.IsBold ?? false)
        || (DefaultCellTextState?.IsBold ?? false);

    /// <summary>The leading a CALLER declared for a cell paragraph, in points.
    /// A <see cref="Aspose.Pdf.Text.TextState.LineSpacing"/> an internal layout path
    /// assigned (the HTML block renderer's 1.2× pitch) is not the caller's and carries
    /// no leading here. Segment states are consulted when the fragment's own is bare —
    /// a fragment built from segments declares its spacing on them.</summary>
    private static double CallerLineSpacing(BaseParagraph paragraph)
    {
        if (paragraph is not Aspose.Pdf.Text.TextFragment tf) return 0;
        // A TEXTLESS fragment is a deliberate spacer, and its box is one bare font
        // size: leading is the gap a line of glyphs opens above itself, and this
        // fragment draws none (a spacer segment declaring LineSpacing 10 opens 10 pt,
        // not 10 plus a line).
        if (string.IsNullOrEmpty(tf.Text)) return 0;
        var st = tf.TextState;
        if (!st.LineSpacingSynthetic && st.LineSpacing > 0) return st.LineSpacing;
        foreach (var seg in tf.Segments)
            if (!seg.TextState.LineSpacingSynthetic && seg.TextState.LineSpacing > 0)
                return seg.TextState.LineSpacing;
        return 0;
    }

    /// <summary>Foreground colour of the first non-empty segment that declares one —
    /// what a fragment built from styled segments actually draws in.</summary>
    private static Color? FirstSegmentForegroundColor(Aspose.Pdf.Text.TextFragment tf)
    {
        foreach (var seg in tf.Segments)
            if (!string.IsNullOrEmpty(seg.Text) && seg.TextState.ForegroundColor is { } c)
                return c;
        return null;
    }

    /// <summary>Records <paramref name="leading"/> on every line from
    /// <paramref name="start"/> onwards — the lines one cell paragraph produced.</summary>
    private static void StampLeading(List<CellLine> lines, int start, double leading)
    {
        if (leading <= 0) return;
        for (var i = start; i < lines.Count; i++) lines[i].Leading = leading;
    }

    /// <summary>Font size a cell paragraph draws at: a declared cell/row/table default
    /// wins over the fragment's own state; otherwise the fragment's.</summary>
    private double ResolveCellParagraphFontSize(
        Aspose.Pdf.Text.TextFragment tf, double fallback, Cell? cell, Row? row)
        => DeclaredCellFontSize(cell, row) ?? ResolveFragmentFontSize(tf, fallback);

    private static double ResolveFragmentFontSize(Aspose.Pdf.Text.TextFragment tf, double fallback)
    {
        // A TextFragment built via the parameterless ctor + Segments.Add carries a
        // default empty leading segment, so prefer the size of a segment that actually
        // has text (where callers set an explicit per-segment size) over the fragment's
        // own default state.
        if (tf.Segments is { Count: > 0 })
            foreach (var s in tf.Segments)
                if (s.TextState.FontSizeTouched && !string.IsNullOrEmpty(s.Text))
                    return s.TextState.FontSize;
        if (tf.TextState.FontSizeTouched) return tf.TextState.FontSize;
        return fallback;
    }

    /// <summary>Read the (type-shadowed) IsInLineParagraph flag — TextFragment redeclares
    /// it with <c>new</c>, so a BaseParagraph-typed read would miss the value callers set.</summary>
    /// <summary>The installed face a generator cell fragment draws in — the fragment's
    /// own named font, else the cell's, row's or table's DefaultCellTextState font —
    /// or null when the effective face is the default Helvetica / a Core-14 name
    /// (those keep the Standard-14 path).</summary>
    private Aspose.Pdf.Text.Font? ResolveGeneratorCellFont(TextFragment tf, Cell cell, Row row)
    {
        var f = tf.TextState.Font;
        if (f is null || ReferenceEquals(f, Aspose.Pdf.Text.FontInfo.DefaultHelvetica))
            f = cell.DefaultCellTextState?.Font ?? row.DefaultCellTextState?.Font ?? DefaultCellTextState?.Font;
        return f;
    }

    private string? ResolveGeneratorCellFace(TextFragment tf, Cell cell, Row row)
    {
        var f = ResolveGeneratorCellFont(tf, cell, row);
        if (f is null || ReferenceEquals(f, Aspose.Pdf.Text.FontInfo.DefaultHelvetica)) return null;
        var name = f.FontName;
        if (string.IsNullOrEmpty(name) || Aspose.Pdf.Text.Standard14Fonts.IsCoreName(name)) return null;
        return name;
    }

    /// <summary>Greedy word wrap on an embedded face's plain advances (no kerning —
    /// the generator draws unkerned shows).</summary>
    private static List<string> WrapLinesWithFont(string s, double size, byte[] ttf, double avail)
    {
        var res = new List<string>();
        var cur = "";
        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var cand = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && avail > 0 && MeasureWidthWithFont(cand, size, ttf) > avail + 1e-6)
            { res.Add(cur); cur = word; }
            else cur = cand;
        }
        if (cur.Length > 0) res.Add(cur);
        return res;
    }

    private static bool IsInlineParagraph(BaseParagraph p) => p switch
    {
        Aspose.Pdf.Text.TextFragment tf => tf.IsInLineParagraph,
        _ => p.IsInLineParagraph,
    };

    /// <summary>Read the (type-shadowed) Margin — TextFragment redeclares it with
    /// <c>new</c>, so a BaseParagraph-typed read would miss the value callers set.</summary>
    /// <summary>The margin a cell paragraph actually lays out with. ⚠ Several DOM
    /// paragraph types re-declare <c>Margin</c> with <c>new</c>, so reading it through
    /// <see cref="BaseParagraph"/> reaches a field nothing ever sets — each shadowing
    /// type has to be named here.</summary>
    private static MarginInfo? ParagraphMargin(BaseParagraph p) => p switch
    {
        Aspose.Pdf.Text.TextFragment tf => tf.Margin,
        Table t => t.Margin,
        _ => p.Margin,
    };

    /// <summary>A TextFragment carrying more than one non-empty segment — each segment has its own
    /// TextState (size/colour/super-subscript) and is laid out as a distinct inline run.</summary>
    private static bool IsMultiSegmentFragment(BaseParagraph p) =>
        p is Aspose.Pdf.Text.TextFragment tf
        && System.Linq.Enumerable.Count(tf.Segments, s => !string.IsNullOrEmpty(s.Text)) > 1;

    /// <summary>The vertical alignment a row's cells share in the generator model:
    /// the last cell of the row that set one, else None.</summary>
    private static VerticalAlignment RowCellVerticalAlignment(Row row)
    {
        var va = VerticalAlignment.None;
        for (var ci = 0; ci < row.Cells.Count; ci++)
            if (row.Cells.At(ci).VerticalAlignment != VerticalAlignment.None) va = row.Cells.At(ci).VerticalAlignment;
        return va;
    }

    /// <summary>Script class of a codepoint for face resolution: runs of one class
    /// share a face; ASCII/Latin punctuation joins the run around it.</summary>
    private static int ScriptClassOf(int cp)
    {
        if (cp < 0x0250) return 0;                                   // Latin / common
        if (cp >= 0x0370 && cp <= 0x03FF) return 1;                  // Greek
        if (cp >= 0x0400 && cp <= 0x052F) return 2;                  // Cyrillic
        if (cp >= 0x0590 && cp <= 0x05FF) return 3;                  // Hebrew
        if ((cp >= 0x0600 && cp <= 0x077F) || (cp >= 0xFB50 && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFF)) return 4; // Arabic
        if (cp >= 0x0900 && cp <= 0x0DFF) return 5;                  // Indic
        if (cp >= 0x0E00 && cp <= 0x0E7F) return 6;                  // Thai
        if ((cp >= 0x2E80 && cp <= 0x9FFF) || (cp >= 0xAC00 && cp <= 0xD7AF) || (cp >= 0xF900 && cp <= 0xFAFF)
            || (cp >= 0xFF00 && cp <= 0xFFEF) || cp >= 0x20000) return 7; // CJK
        return 0;
    }

    /// <summary>Cut a token into maximal runs of one script class; common characters
    /// (Latin letters, digits, punctuation) extend the run they follow.</summary>
    private static List<string> SplitScriptRuns(string token)
    {
        var runs = new List<string>();
        if (token.Length == 0) return runs;
        var start = 0;
        var cls = -1;
        for (var i = 0; i < token.Length; i++)
        {
            int cp = token[i];
            var len = 1;
            if (char.IsHighSurrogate(token[i]) && i + 1 < token.Length && char.IsLowSurrogate(token[i + 1]))
            {
                cp = char.ConvertToUtf32(token[i], token[i + 1]);
                len = 2;
            }
            var c = ScriptClassOf(cp);
            if (c != 0 && cls != -1 && c != cls)
            {
                runs.Add(token.Substring(start, i - start));
                start = i;
            }
            if (c != 0) cls = c;
            i += len - 1;
        }
        runs.Add(token.Substring(start));
        return runs;
    }

    /// <summary>Height of one inline row: the tallest item on it (a text run's
    /// pitch, an image's box), or <paramref name="fallback"/> for an empty row.</summary>
    /// <summary>Row height in a cell that went inline ONLY because it holds a Graph:
    /// a graph row is exactly the graph's declared box (zero for a Graph(0, 0)) and a
    /// text row is the cell's own resolved size, as the plain cell path prices it.</summary>
    /// <summary>Whether a cell's inline layout was forced by a Graph alone — the case
    /// that keeps the plain cell's line model and baseline seat.</summary>
    private static bool CellInlineFromGraphOnly(Cell cell)
    {
        var any = false;
        foreach (var gp in cell.Paragraphs)
        {
            if (gp is Aspose.Pdf.Drawing.Graph) { any = true; continue; }
            if (IsInlineParagraph(gp) || IsMultiSegmentFragment(gp)) return false;
        }
        return any;
    }

    private static double GraphOnlyRowHeight(List<InlineItem> row, double cellFontSize)
    {
        double h = 0;
        var allGraphs = row.Count > 0;
        foreach (var it in row)
        {
            if (it.Graph is null) { allGraphs = false; continue; }
            if (it.Height > h) h = it.Height;
        }
        return allGraphs ? h : cellFontSize;
    }

    private static double InlineRowHeight(List<InlineItem> row, double fallback)
    {
        double h = 0;
        var allGraphs = row.Count > 0;
        foreach (var it in row)
        {
            if (it.Height > h) h = it.Height;
            if (it.Graph is null) allGraphs = false;
        }
        // A Graph occupies exactly the box it DECLARES, zero included: a Graph(0, 0)
        // in a cell takes no room at all and its shapes overhang whatever follows,
        // so the cell's own text stays on the row's first line (probed 2026-08-26).
        // Every other empty row falls back to one text line.
        if (allGraphs) return h;
        return h > 0 ? h : fallback;
    }

    /// <summary>True when <paramref name="text"/> holds a character the face cannot
    /// show: for a Standard-14 face anything outside Latin-1, for an embedded face a
    /// codepoint its cmap lacks.</summary>
    private static bool NeedsGlyphFallback(string text, byte[]? ttf)
    {
        var gp = ttf is null ? null : GetInlineGlyphParser(ttf);
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            if (cp < 0x80 || char.IsControl((char)Math.Min(cp, 0xFFFF))) continue;
            if (gp is null) { if (cp > 0xFF) return true; continue; }
            if (!gp.CMap.TryGetValue(cp, out var g) || g <= 0) return true;
        }
        return false;
    }

    /// <summary>The face a token draws in: the segment's own when it covers the token,
    /// else the first glyph-covering substitute (host surrogate, registered sources,
    /// Arial, the script's system CJK face), else the segment's face. Cached per
    /// face + distinct non-ASCII character set so a long cell resolves each script once.</summary>
    private static (byte[]? ttf, string? name) ResolveTokenFace(string token, Aspose.Pdf.Text.Font? font,
        byte[]? segTtf, string? segName, Dictionary<string, (byte[]? ttf, string? name)> cache)
    {
        if (!NeedsGlyphFallback(token, segTtf)) return (segTtf, segName);
        var probe = new SortedSet<char>();
        foreach (var c in token) if (c > 0x7F) probe.Add(c);
        var key = (font?.FontName ?? string.Empty) + "|" + string.Concat(probe);
        if (cache.TryGetValue(key, out var hit)) return hit;
        (byte[]? ttf, string? name) result = (segTtf, segName);
        try
        {
            var sub = Aspose.Pdf.Text.FontRepository.SubstituteForMissingGlyphs(token, font);
            if (sub?.TtfData is { Length: > 0 } subTtf) result = (subTtf, sub.FontName);
        }
        catch { }
        cache[key] = result;
        return result;
    }

    /// <summary>Resolve an image's natural size in points for in-cell layout. On Windows
    /// the platform decoder is used so images without explicit density (JFIF units=0)
    /// resolve at the 96-DPI default the generator assumes; elsewhere it falls back to the
    /// header parser (which defaults such images to 72 DPI).</summary>
    private static bool TryGetCellImageSizePt(byte[] data, out double widthPt, out double heightPt)
    {
        widthPt = 0; heightPt = 0;
        if (OperatingSystem.IsWindows())
        {
            try
            {
#pragma warning disable CA1416
                using var ms = new MemoryStream(data);
                using var img = System.Drawing.Image.FromStream(ms, false, false);
                var dpiX = img.HorizontalResolution > 0 ? img.HorizontalResolution : 96;
                var dpiY = img.VerticalResolution > 0 ? img.VerticalResolution : 96;
                widthPt = img.Width * 72.0 / dpiX;
                heightPt = img.Height * 72.0 / dpiY;
                if (widthPt > 0 && heightPt > 0) return true;
#pragma warning restore CA1416
            }
            catch { /* fall through to the header parser */ }
        }
        return Document.TryGetImageNaturalSizePt(data, out widthPt, out heightPt);
    }

    /// <summary>Read an <see cref="Image"/> paragraph's bytes from its stream or file,
    /// rewinding a seekable stream so a second build pass still sees the data.</summary>
    private static byte[]? ReadImageBytes(Image img)
    {
        var raw = ReadRawImageBytes(img);

        // Page.AddImage only accepts raster formats. An SVG source (FileType=Svg or
        // detected from the bytes) is rasterised first so a vector image embedded in
        // a cell renders instead of throwing "Unsupported image format".
        if (raw is { Length: > 0 } && IsSvg(img, raw))
            return ImageRasterizer.RasterizeSvg(raw) ?? raw;
        return raw;
    }

    /// <summary>The source bytes of an <see cref="Image"/> paragraph as authored —
    /// an SVG stays vector text here (no rasterisation), so callers can inspect
    /// its root attributes before deciding a raster size.</summary>
    private static byte[]? ReadRawImageBytes(Image img) => img.ReadSourceBytes();

    /// <summary>True when an SVG root declares no width/height (viewBox-only or bare):
    /// the artwork has no intrinsic size, so a cell placement sizes it to the space
    /// it sits in rather than to the raster's natural dimensions.</summary>
    private static bool SvgLacksIntrinsicSize(byte[] svgData)
    {
        try
        {
            var head = System.Text.Encoding.UTF8.GetString(
                svgData, 0, System.Math.Min(2048, svgData.Length));
            var m = System.Text.RegularExpressions.Regex.Match(head, "<svg\\b[^>]*>");
            if (!m.Success) return false;
            return !System.Text.RegularExpressions.Regex.IsMatch(
                m.Value, "\\s(width|height)\\s*=");
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSvg(Image img, byte[] data)
    {
        if (img.FileType == ImageFileType.Svg) return true;
        // Sniff: an SVG file starts with an XML prolog or the <svg root, possibly
        // after a UTF-8 BOM / leading whitespace.
        int i = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) i = 3;
        while (i < data.Length && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n')) i++;
        var head = System.Text.Encoding.ASCII.GetString(data, i, System.Math.Min(512, data.Length - i));
        return head.StartsWith("<?xml") ? head.Contains("<svg") : head.StartsWith("<svg");
    }

    /// <summary>Pages of this table already emitted — the offset from the table's start
    /// page to the page the slice list now being built lands on.</summary>
    private int _emittedPages;

    /// <summary>Resolve the page-label macros <c>$p</c> and <c>$P</c> in the lines about
    /// to be drawn. They work in an ORDINARY page table exactly as they do in a
    /// header/footer band, and they resolve PER PAGE: measured, the
    /// page-2 rows of an 80-row table read "row 70 page 2 of 2". The swap is undone
    /// before returning because the same <see cref="CellLine"/> objects are re-emitted
    /// for the pages that follow.</summary>
    private List<(CellLine Line, string Original)>? SubstitutePageMacros(List<RowSlice> slices)
    {
        var doc = _buildPage?.OwnerDocument;
        if (doc is null) return null;
        var startNumber = doc.Pages.IndexOf(_buildPage);
        if (startNumber <= 0) return null;
        var pageNumber = startNumber + _emittedPages;
        List<(CellLine, string)>? swaps = null;
        foreach (var slice in slices)
            foreach (var cellLines in slice.Plan.CellLines)
                foreach (var cl in cellLines)
                {
                    if (cl.Text is not { Length: > 0 } t) continue;
                    if (t.IndexOf("$p", StringComparison.Ordinal) < 0
                        && t.IndexOf("$P", StringComparison.Ordinal) < 0) continue;
                    (swaps ??= new()).Add((cl, t));
                    cl.Text = HeaderFooter.ApplyPageLabelMacros(t, doc, pageNumber);
                }
        return swaps;
    }

    private void RestorePageMacros(List<(CellLine Line, string Original)>? swaps)
    {
        _emittedPages++;
        if (swaps is null) return;
        foreach (var (line, original) in swaps) line.Text = original;
    }

    private void RenderRowSlice(ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, double tableX, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links = null,
        List<(byte[] data, Rectangle rect)>? imageSink = null,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<byte[]>? graphSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null,
        Page? page = null,
        List<(Note note, double x, double baseline, double size)>? footnoteSink = null)
    {
        var row = slice.Plan.Row;
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;
        var cellX = tableX;

        // Table background: the table-level BackgroundColor paints the whole row band
        // (cell/row colours draw over it); under the XML dialect a header band bleeds
        // edge-to-edge from x = 0 (the era template dialect). The plain generator
        // paints it too — five LightYellow grids fill their whole column block.
        if ((XmlGeneratorModel || GeneratorDialect) && BackgroundColor is not null)
        {
            double bgX = tableX, bgW = 0;
            foreach (var w in colWidths) bgW += w;
            if (XmlBandBleedWidth > 0) { bgX = 0; bgW = XmlBandBleedWidth; }
            builder.SetFillColor(BackgroundColor);
            builder.Rectangle(bgX, slice.TopY - slice.Height, bgW, slice.Height);
            builder.Fill();
        }

        var gridToCell = slice.Plan.GridToCell;
        for (var col = 0; col < colWidths.Length; col++)
            RenderRowSliceColumn(col, ref cellX, builder, slice, colWidths, fontName, cellMap,
                links, imageSink, optionSink, graphSink, checkboxSink, page, footnoteSink);
    }

    // Anchor styling: link text draws pure
    // blue (#0000FF) with a 1.2 pt blue underline 1.24 pt below the baseline,
    // one bar per word (the bars break at the word gaps). Values scale with the
    // line's font size from the 12 pt base.
    private const double LinkUnderlineDropPt = 1.24;

    private const double LinkUnderlineWPt = 1.2;

    private const double LinkProbeBasePt = 12.0;

    /// <summary>Draw a line whose text carries hyperlink runs: non-link segments in
    /// the line's own colour, each anchor run in link blue with per-word underlines.
    /// Segment boundaries are recovered from the runs' pre-measured x-offsets by
    /// accumulating glyph advances.</summary>
    private void ShowLineWithLinks(ContentStreamBuilder builder, CellLine line,
        string resolvedFont, double lineX, double lineBase)
    {
        var text = line.Text;
        var fs = line.FontSize;
        var lScale = fs / LinkProbeBasePt;
        var runs = new List<(double XOff, double W)>();
        if (line.LinkRuns is { Count: > 0 })
            foreach (var (xo, rw, _) in line.LinkRuns) runs.Add((xo, rw));
        else if (line.Hyperlink is not null) runs.Add((0, MeasureWidth(text, fs)));
        runs.Sort((a, b) => a.XOff.CompareTo(b.XOff));

        void ShowSeg(string seg, double atX, bool blue)
        {
            if (seg.Length == 0) return;
            builder.BeginText();
            builder.SetFont(resolvedFont, fs);
            if (blue) builder.SetFillColor(0, 0, 1);
            else ApplyColor(builder, line.ForegroundColor);
            builder.MoveTextPosition(lineX + atX, lineBase);
            builder.ShowText(seg);
            builder.EndText();
            if (!blue) return;
            // Per-word underline bars.
            builder.SetStrokeColor(0, 0, 1);
            builder.SetLineWidth(LinkUnderlineWPt * lScale);
            var wx = atX;
            var wi = 0;
            while (wi < seg.Length)
            {
                if (seg[wi] == ' ') { wx += MeasureWidth(" ", fs); wi++; continue; }
                var we = wi;
                while (we < seg.Length && seg[we] != ' ') we++;
                var wordW = MeasureWidth(seg[wi..we], fs);
                var uy = lineBase - LinkUnderlineDropPt * lScale;
                builder.MoveTo(lineX + wx, uy).LineTo(lineX + wx + wordW, uy).Stroke();
                wx += wordW;
                wi = we;
            }
        }

        var ci = 0;
        var cum = 0.0;
        foreach (var (xo, rw) in runs)
        {
            var segStart = ci;
            var segX = cum;
            while (ci < text.Length
                   && cum + MeasureWidth(text[ci].ToString(), fs) / 2 < xo)
            { cum += MeasureWidth(text[ci].ToString(), fs); ci++; }
            ShowSeg(text[segStart..ci], segX, blue: false);
            var runStart = ci;
            var runX = cum;
            while (ci < text.Length
                   && cum + MeasureWidth(text[ci].ToString(), fs) / 2 <= xo + rw)
            { cum += MeasureWidth(text[ci].ToString(), fs); ci++; }
            ShowSeg(text[runStart..ci], runX, blue: true);
        }
        if (ci < text.Length) ShowSeg(text[ci..], cum, blue: false);
    }

    /// <summary>Render a cell's visible lines one at a time, drawing a form-control
    /// glyph (currently the radio-button circle) ahead of any option line's caption.</summary>
    // DataWorks input-box value size: 10 pt sans, the value clipping just
    // inside its 177 px box; the era layout draws the values in Helvetica 10.
    private const double DwValuePt = 10.0;

    /// <summary>Width ONE hidden inline widget occupies in a DataWorks form
    /// cell — a borderless unchecked checkbox or a dead image with no declared
    /// box (derived: the results-row text pen sits 13.7pt past its
    /// column origin = checkbox + broken folder icon, two reserves).</summary>
    internal const double DwHiddenInlinePt = 6.85;

    /// <summary>Lift of a control box above its centred-minus-2 legacy seat
    /// (measured: the first input box's top sits at its cell's content top).</summary>
    internal const double DwBoxSeatLiftPt = 0.4;

    /// <summary>Draw-time footprint of a VISIBLE checkbox widget in a DataWorks
    /// control line — the upload filename and List labels both start
    /// 17.3 pt past the pen (widget box + its margins). The width model keeps
    /// the smaller DwHiddenInlinePt reserve; only the draw pen advances by this.</summary>
    internal const double DwCheckboxDrawWPt = 17.3;
    /// <summary>Checked-checkbox glyph seat inside that footprint: the check
    /// strokes start 7.7 pt in, scaled 1.35× from the legacy glyph, and ride
    /// 1.3 pt higher (reference glyph ink x +7.3..+14.0, y −1.0..+5.9 around
    /// the baseline).</summary>
    internal const double DwCheckIndentPt = 7.7;
    internal const double DwCheckScale = 1.35;
    internal const double DwCheckRisePt = 1.3;
    /// <summary>DataWorks radio glyph: a 12 pt circle 4.8 pt past the pen with
    /// no trailing gap (measured: circle 160.3..172.3 with the caption at its
    /// right edge).</summary>
    internal const double DwRadioLeadPt = 4.8;
    internal const double DwRadioGlyphDPt = 12.0;

    /// <summary>Std14 Times-Roman advance for a WinAnsi string (per-char AFM
    /// widths, 1/1000 em) — the measure twin of the serif control-cell text.</summary>
    private static double MeasureTimesRoman(string s, double fontSize)
    {
        double w = 0;
        foreach (var ch in s)
        {
            var cw = Standard14Fonts.GetWidth("Times-Roman", ch);
            w += cw > 0 ? cw : Standard14Fonts.GetDefaultWidth("Times-Roman");
        }
        return w * fontSize / 1000.0;
    }

    /// <summary>Std14 Helvetica advance — the measure twin of the control-box
    /// values, which draw in the UI sans (the generic MeasureWidth over-measures
    /// them and clips the visible tail).</summary>
    private static double MeasureHelvetica(string s, double fontSize)
    {
        double w = 0;
        foreach (var ch in s)
        {
            var cw = Standard14Fonts.GetWidth("Helvetica", ch);
            w += cw > 0 ? cw : Standard14Fonts.GetDefaultWidth("Helvetica");
        }
        return w * fontSize / 1000.0;
    }

    /// <summary>Fill a rounded rectangle (radius clamped so corner arcs never overlap).</summary>
    private static void FillRoundedRect(ContentStreamBuilder builder, double x, double y,
        double w, double h, double radius)
    {
        if (w <= 0 || h <= 0) return;
        var r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        var k = r * RoundCornerKappa;
        builder.MoveTo(x + r, y)
            .LineTo(x + w - r, y)
            .CurveTo(x + w - r + k, y, x + w, y + r - k, x + w, y + r)
            .LineTo(x + w, y + h - r)
            .CurveTo(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h)
            .LineTo(x + r, y + h)
            .CurveTo(x + r - k, y + h, x, y + h - r + k, x, y + h - r)
            .LineTo(x, y + r)
            .CurveTo(x, y + r - k, x + r - k, y, x + r, y)
            .ClosePath();
        builder.Fill();
    }

    /// <summary>Fill an axis-aligned ellipse centred at (cx, cy) — the checked
    /// radio's inner dot (the dot draws taller than wide).</summary>
    private static void FillEllipse(ContentStreamBuilder builder, double cx, double cy,
        double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return;
        const double k = 0.5522847498;
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Fill();
    }

    /// <summary>Fill a circle centred at (cx, cy), four cubic Béziers.</summary>
    private static void FillCircle(ContentStreamBuilder builder, double cx, double cy, double radius)
    {
        if (radius <= 0) return;
        const double k = 0.5522847498;
        var rx = radius; var ry = radius;
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Fill();
    }

    /// <summary>Stroke an axis-aligned ellipse centred at (cx, cy), approximated with
    /// four cubic Béziers.</summary>
    private static void DrawEllipse(ContentStreamBuilder builder, double cx, double cy,
        double rx, double ry, double r, double g, double b)
    {
        if (rx <= 0 || ry <= 0) return;
        const double k = 0.5522847498;
        builder.SetLineWidth(1);
        builder.SetStrokeColor(r, g, b);
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Stroke();
    }

    private static void ApplyColor(ContentStreamBuilder builder, Color? color)
    {
        if (color is { } c)
            builder.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);
    }
}
