using System.Linq;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>One cell of a sheet-styled table in a UA-flow fragment: its inline pieces and
    /// the box rules its tag, class rules and style resolved.</summary>
    private sealed class UaCssCell
    {
        public List<HtmlInlineItem> Items = new();
        public bool NoWrap;
        public bool FullWidth;
        /// <summary>A CSS width in points the cell's rules declare (0 = none).</summary>
        public double DeclaredW;
        public double PadTop, PadBottom, PadLeft, PadRight;
        public HorizontalAlignment Align = HorizontalAlignment.Left;
        public string VAlign = "middle";
        public Color? Back;
        public double MinW, MaxW;
        public List<HtmlInlineLine> Lines = new();
        public double ContentH;
    }

    /// <summary>The table's box rules from its sheet and style: face, size, spacing, margins,
    /// border bands and background.</summary>
    private sealed class UaCssTable
    {
        public List<List<UaCssCell>> Rows = new();
        public int Columns;
        public double Spacing = UaTableCellSpacingPx * CssPxToPt;
        public double MarginTop, MarginBottom;
        public double BorderTop, BorderBottom, BorderLeft, BorderRight;
        public Color? BorderColor;
        public Color? Back;
        public double WidthPt;
        public double[] ColW = System.Array.Empty<double>();
        public double[] RowH = System.Array.Empty<double>();
    }

    /// <summary>UA defaults of a table with no attributes: 2 px between cells, 1 px inside them.</summary>
    private const double UaTableCellSpacingPx = 2.0;
    private const double UaTableCellPaddingPx = 1.0;

    /// <summary>Lay a sheet-styled table of a UA-flow fragment out on the UA table model
    /// (probed on the reference: a 100 % table spans the content box; its CSS margin, border
    /// bands and background frame the cell grid; cells sit 2 px apart with their CSS padding;
    /// a cell's line box is its face's hhea line rounded to pixels (Tahoma 11 px = 13 px), the
    /// row as tall as its tallest cell, shorter cells centred; a no-wrap column takes its
    /// content width, a width:100 % column the rest). False when the table holds anything the
    /// model has no rule for - the calibrated engines keep it.</summary>
    private bool TryLayoutUaCssTable(HtmlFragmentLayoutState hl, string chunk)
    {
        if (!hl.uaFlowProse) return false;
        var sheet = TableSheetForChunk(hl.htmlContent, chunk);
        if (sheet is null) return false;
        var open = Regex.Match(chunk, @"<table\b([^>]*)>", RegexOptions.IgnoreCase);
        var tableClasses = InlineAttr(open.Groups[0].Value, "class");
        // The sheet must style THIS table (an element or class rule); a plain table keeps the calibrated engines.
        sheet.TryGetValue("table", out var tableDecls);
        if (tableDecls is null && !HasClassRule(sheet, "table", tableClasses)) return false;
        // A nested table, a spanning cell, a picture or a control: no rule here yet.
        if (Regex.Matches(chunk, @"<\s*table\b", RegexOptions.IgnoreCase).Count > 1
            || Regex.IsMatch(chunk, @"\b(colspan|rowspan)\s*=|<\s*(img|input)\b", RegexOptions.IgnoreCase))
            return false;
        var decls = MergeCssDecls(tableDecls, "");
        MergeClassRules(decls, sheet, "table", tableClasses);
        decls = MergeCssDecls(decls, InlineAttr(open.Groups[0].Value, "style") ?? "");
        // The face and size inherit from the sheet's body rule when the table names none.
        if (sheet.TryGetValue("body", out var bodyRule))
            foreach (var prop in new[] { "font-family", "font-size" })
                if (!decls.ContainsKey(prop) && bodyRule.TryGetValue(prop, out var bv)) decls[prop] = bv;
        var st = new HtmlInlineFlowState { hl = hl, flow = hl.flow, html = chunk, lastPiece = false, defaultColor = hl.htmlColor };
        if (!ResolveUaTableBase(st, decls)) return false;
        var t = new UaCssTable();
        var padAttr = InlineAttr(open.Groups[0].Value, "cellpadding");
        var defaultPad = padAttr is { Length: > 0 } && double.TryParse(padAttr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var padPx)
            ? padPx * CssPxToPt : UaTableCellPaddingPx * CssPxToPt;
        if (!ParseUaCssTableCells(hl, st, sheet, chunk, t, defaultPad)) return false;
        ApplyUaTableBox(t, decls, open.Groups[1].Value, st.basePt, st.flow.CurWidth);
        MeasureUaCssColumns(st, t);
        MeasureUaCssRows(st, t);
        EmitUaCssTable(st, t);
        return true;
    }

    /// <summary>The table's face: the first family of its font-family list that loads (else
    /// the UA body face) at its font-size (else the UA body size).</summary>
    private static bool ResolveUaTableBase(HtmlInlineFlowState st, Dictionary<string, string> decls)
    {
        UaFace? face = null;
        if (decls.TryGetValue("font-family", out var fams))
            foreach (var fam in fams.Split(','))
            {
                var name = fam.Trim().Trim('\'', '"');
                if (name.Length == 0) continue;
                face = TryLoadInlineFace(st, name, false, false);
                if (face is not null) { st.baseFamily = name; break; }
            }
        face ??= TryLoadInlineFace(st, UaInlineBaseFamily, false, false);
        if (face is null) return false;
        if (decls.TryGetValue("font-size", out var fs) && CssMarginPt(fs.Trim(), UaInlineBasePt) is var pt && !double.IsNaN(pt) && pt > 0)
            st.basePt = pt;
        st.strut = face;
        st.faces[st.baseFamily.ToLowerInvariant()] = face;
        st.fontDict = Table.ResolvePageFontDict(st.flow.CurrentPage);
        st.csb = new Content.ContentStreamBuilder();
        return true;
    }

    /// <summary>A rule's declarations with an element's own style applied over them.</summary>
    private static Dictionary<string, string> MergeCssDecls(Dictionary<string, string>? rule, string style)
    {
        var d = rule is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(rule, StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(style, @"([-\w]+)\s*:\s*([^;]+)"))
            d[m.Groups[1].Value.Trim().ToLowerInvariant()] = m.Groups[2].Value.Trim();
        return d;
    }

    /// <summary>Whether the sheet holds a rule for any of the element's classes (".cls" or "tag.cls").</summary>
    private static bool HasClassRule(IReadOnlyDictionary<string, Dictionary<string, string>> sheet, string tag, string? classAttr)
    {
        foreach (var cls in (classAttr ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (sheet.ContainsKey(tag + "." + cls) || sheet.ContainsKey("." + cls)) return true;
        return false;
    }

    /// <summary>Apply the sheet's class rules of the element ("tag.cls" over ".cls") onto its declarations.</summary>
    private static void MergeClassRules(Dictionary<string, string> decls, IReadOnlyDictionary<string, Dictionary<string, string>> sheet, string tag, string? classAttr)
    {
        foreach (var cls in (classAttr ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (sheet.TryGetValue("." + cls, out var r2)) foreach (var kv in r2) decls[kv.Key] = kv.Value;
            if (sheet.TryGetValue(tag + "." + cls, out var r1)) foreach (var kv in r1) decls[kv.Key] = kv.Value;
        }
    }

    /// <summary>Rows and cells: each cell's inline pieces in the table face (bold when its
    /// rules say so), its padding, wrap, width and alignment rules. A cell rule declaring a
    /// border keeps the calibrated engines.</summary>
    private static bool ParseUaCssTableCells(HtmlFragmentLayoutState hl, HtmlInlineFlowState st, IReadOnlyDictionary<string, Dictionary<string, string>> sheet, string chunk, UaCssTable t, double defaultPad)
    {
        sheet.TryGetValue("td", out var tdRule);
        foreach (Match rm in Regex.Matches(chunk, @"<tr\b[^>]*>(.*?)</tr\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var row = new List<UaCssCell>();
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value, @"<t([dh])\b([^>]*)>(.*?)</t\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tag = cm.Groups[2].Value;
                var decls = MergeCssDecls(tdRule, "");
                MergeClassRules(decls, sheet, "td", InlineAttr("<td " + tag + ">", "class"));
                decls = MergeCssDecls(decls, InlineAttr("<td " + tag + ">", "style") ?? "");
                if (decls.Keys.Any(k => k.StartsWith("border", StringComparison.OrdinalIgnoreCase))) return false;
                var cell = new UaCssCell();
                var isHeader = cm.Groups[1].Value.Equals("h", StringComparison.OrdinalIgnoreCase);
                var bold = isHeader || Regex.IsMatch(decls.TryGetValue("font-weight", out var fw) ? fw : "", @"bold|[6-9]00", RegexOptions.IgnoreCase);
                foreach (var b in ParseHtmlFlowBlocks(cm.Groups[3].Value, hl, st.basePt, out _, out _))
                    foreach (var it in b.Items) { if (bold) it.Bold = true; cell.Items.Add(it); }
                cell.NoWrap = Regex.IsMatch(tag, @"\bnowrap\b", RegexOptions.IgnoreCase)
                    || (decls.TryGetValue("white-space", out var ws) && ws.Contains("nowrap", StringComparison.OrdinalIgnoreCase));
                cell.FullWidth = decls.TryGetValue("width", out var cw) && Regex.IsMatch(cw, @"^\s*100\s*%");
                if (cw is not null && !cell.FullWidth && CssMarginPt(cw.Trim(), st.basePt) is var dw && !double.IsNaN(dw) && dw > 0) cell.DeclaredW = dw;
                (cell.PadTop, cell.PadBottom, cell.PadLeft, cell.PadRight) = CssPaddingSides(decls, st.basePt, defaultPad);
                if (decls.TryGetValue("text-align", out var ta) || isHeader)
                    cell.Align = (ta ?? "center").Trim().ToLowerInvariant() switch { "right" => HorizontalAlignment.Right, "center" => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
                if (decls.TryGetValue("vertical-align", out var va)) cell.VAlign = va.Trim().ToLowerInvariant();
                if (decls.TryGetValue("background-color", out var bg) || decls.TryGetValue("background", out bg)) cell.Back = Converters.HtmlToPdfConverter.ParseCssColor(bg);
                row.Add(cell);
            }
            if (row.Count == 0) continue;
            t.Rows.Add(row);
            t.Columns = System.Math.Max(t.Columns, row.Count);
        }
        return t.Rows.Count > 0 && t.Rows.All(r => r.Count == t.Columns);
    }

    /// <summary>A cell's padding (top, bottom, left, right) in points: the table's cellpadding
    /// (the UA 1 px when it names none) unless the shorthand or a longhand says otherwise.</summary>
    private static (double Top, double Bottom, double Left, double Right) CssPaddingSides(Dictionary<string, string> decls, double fontPt, double defaultPad)
    {
        double top = defaultPad, bottom = defaultPad, left = defaultPad, right = defaultPad;
        if (decls.TryGetValue("padding", out var sh))
        {
            var v = sh.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(x => CssMarginPt(x, fontPt)).Select(x => double.IsNaN(x) ? 0 : x).ToArray();
            if (v.Length >= 1)
            {
                top = v[0]; right = v[v.Length > 1 ? 1 : 0]; bottom = v[v.Length > 2 ? 2 : 0]; left = v[v.Length > 3 ? 3 : v.Length > 1 ? 1 : 0];
            }
        }
        foreach (var (name, side) in new[] { ("top", 0), ("bottom", 1), ("left", 2), ("right", 3) })
        {
            if (!decls.TryGetValue("padding-" + name, out var lv)) continue;
            var pt = CssMarginPt(lv.Trim(), fontPt);
            if (double.IsNaN(pt)) continue;
            switch (side) { case 0: top = pt; break; case 1: bottom = pt; break; case 2: left = pt; break; default: right = pt; break; }
        }
        return (top, bottom, left, right);
    }

    /// <summary>The table box: its width (a percent of the content box, else the content box),
    /// cell spacing attribute, CSS margins, border sides and background.</summary>
    private static void ApplyUaTableBox(UaCssTable t, Dictionary<string, string> decls, string openAttrs, double fontPt, double contentWidth)
    {
        t.WidthPt = contentWidth;
        var wAttr = InlineAttr("<table " + openAttrs + ">", "width");
        if (decls.TryGetValue("width", out var cssW)) wAttr = cssW;
        if (wAttr is { Length: > 0 })
        {
            var pm = Regex.Match(wAttr, @"^\s*([\d.]+)\s*(%|px|pt)?");
            if (pm.Success && double.TryParse(pm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wv))
                t.WidthPt = pm.Groups[2].Value switch { "%" => contentWidth * wv / 100.0, "pt" => wv, _ => wv * CssPxToPt };
        }
        if (InlineAttr("<table " + openAttrs + ">", "cellspacing") is { Length: > 0 } cs
            && double.TryParse(cs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var csv))
            t.Spacing = csv * CssPxToPt;
        var style = string.Join(";", decls.Select(kv => kv.Key + ":" + kv.Value));
        var (mt, mb, _, _) = BlockCssMargins(style, fontPt);
        t.MarginTop = double.IsNaN(mt) ? 0 : mt;
        t.MarginBottom = double.IsNaN(mb) ? 0 : mb;
        if (decls.TryGetValue("border", out var all)) { var (w, c) = CssBorderSide(all); t.BorderTop = t.BorderBottom = t.BorderLeft = t.BorderRight = w; t.BorderColor = c; }
        foreach (var (name, side) in new[] { ("top", 0), ("bottom", 1), ("left", 2), ("right", 3) })
        {
            if (!decls.TryGetValue("border-" + name, out var bv)) continue;
            var (w, c) = CssBorderSide(bv);
            t.BorderColor ??= c;
            switch (side) { case 0: t.BorderTop = w; break; case 1: t.BorderBottom = w; break; case 2: t.BorderLeft = w; break; default: t.BorderRight = w; break; }
        }
        if (decls.TryGetValue("background-color", out var bg) || decls.TryGetValue("background", out bg)) t.Back = Converters.HtmlToPdfConverter.ParseCssColor(bg);
    }

    /// <summary>A border shorthand's width (0 for none/hidden) and colour.</summary>
    private static (double WidthPt, Color? Color) CssBorderSide(string value)
    {
        if (Regex.IsMatch(value, @"\b(none|hidden)\b", RegexOptions.IgnoreCase)) return (0, null);
        var wm = Regex.Match(value, @"([\d.]+)\s*(px|pt)");
        var w = wm.Success ? CssMarginPt(wm.Value.Replace(" ", ""), UaInlineBasePt) : CssPxToPt * UaTableMediumBorderPx;
        return (double.IsNaN(w) ? 0 : w, Converters.HtmlToPdfConverter.ParseCssColor(value));
    }

    /// <summary>A border with no width word is medium: 3 px.</summary>
    private const double UaTableMediumBorderPx = 3.0;

    /// <summary>Column widths from the cells' content: a no-wrap column its widest content
    /// plus padding; a width:100 % column the rest of the table; otherwise every column its
    /// widest content when that fits (the surplus shared in proportion), else shrunk toward
    /// its widest word in proportion to its slack.</summary>
    private static void MeasureUaCssColumns(HtmlInlineFlowState st, UaCssTable t)
    {
        var n = t.Columns;
        var min = new double[n];
        var max = new double[n];
        var full = new bool[n];
        var declared = new double[n];
        foreach (var row in t.Rows)
            for (var i = 0; i < n; i++)
            {
                var c = row[i];
                var one = c.Items.Count == 0 ? new List<HtmlInlineLine>() : BuildInlineLines(st, c.Items, double.MaxValue / 4);
                c.MaxW = (one.Count > 0 ? one[0].Pen : 0) + c.PadLeft + c.PadRight;
                c.MinW = c.NoWrap ? c.MaxW : WidestInlineToken(st, c.Items) + c.PadLeft + c.PadRight;
                max[i] = System.Math.Max(max[i], c.MaxW);
                min[i] = System.Math.Max(min[i], c.MinW);
                full[i] |= c.FullWidth;
                declared[i] = System.Math.Max(declared[i], c.DeclaredW);
            }
        // A declared column width is the column's preferred width (its content still sets
        // the floor it cannot shrink under).
        for (var i = 0; i < n; i++)
            if (declared[i] > 0) max[i] = System.Math.Max(min[i], declared[i]);
        var avail = t.WidthPt - t.Spacing * (n + 1) - t.BorderLeft - t.BorderRight;
        var w = new double[n];
        var fullIndex = System.Array.IndexOf(full, true);
        if (fullIndex >= 0)
        {
            var others = 0.0;
            for (var i = 0; i < n; i++) if (i != fullIndex) { w[i] = max[i]; others += max[i]; }
            w[fullIndex] = System.Math.Max(min[fullIndex], avail - others);
        }
        else if (max.Sum() <= avail)
        {
            var sum = max.Sum();
            for (var i = 0; i < n; i++) w[i] = sum > 0 ? max[i] + (avail - sum) * max[i] / sum : avail / n;
        }
        else
        {
            var slack = max.Sum() - min.Sum();
            var room = System.Math.Max(0, avail - min.Sum());
            for (var i = 0; i < n; i++) w[i] = min[i] + (slack > 0 ? (max[i] - min[i]) * room / slack : 0);
        }
        t.ColW = w;
    }

    /// <summary>The widest single wrap token of the pieces, in their faces.</summary>
    private static double WidestInlineToken(HtmlInlineFlowState st, List<HtmlInlineItem> items)
    {
        var widest = 0.0;
        foreach (var it in items)
        {
            if (it.Kind != HtmlInlineKind.Text) continue;
            foreach (Match tm in HtmlFlowTokenRegex.Matches(it.Text))
                if (tm.Value.Trim().Length > 0) widest = System.Math.Max(widest, InlineTextWidth(st, it, tm.Value.Trim()));
        }
        return widest;
    }

    /// <summary>Wrap every cell on its column and take each row as tall as its tallest cell.</summary>
    private static void MeasureUaCssRows(HtmlInlineFlowState st, UaCssTable t)
    {
        t.RowH = new double[t.Rows.Count];
        for (var r = 0; r < t.Rows.Count; r++)
            for (var i = 0; i < t.Columns; i++)
            {
                var c = t.Rows[r][i];
                // An empty cell opens no line box: its padding is all the height it asks for.
                c.Lines = c.Items.Count == 0 ? new List<HtmlInlineLine>() : BuildInlineLines(st, c.Items, c.NoWrap ? double.MaxValue / 4 : t.ColW[i] - c.PadLeft - c.PadRight);
                c.ContentH = 0;
                foreach (var line in c.Lines) { MeasureInlineLine(st, line); c.ContentH += line.Above + line.Below; }
                t.RowH[r] = System.Math.Max(t.RowH[r], c.ContentH + c.PadTop + c.PadBottom);
            }
    }

    /// <summary>Draw the table at the cursor (on the next page when it does not fit): margin,
    /// background over the border box, border bands, then each cell's lines seated on their
    /// line boxes, vertically aligned in the row; advance by the box and its margins.</summary>
    private void EmitUaCssTable(HtmlInlineFlowState st, UaCssTable t)
    {
        var flow = st.flow;
        var gridH = t.Spacing * (t.Rows.Count + 1) + t.RowH.Sum();
        var boxH = t.BorderTop + gridH + t.BorderBottom;
        var fitsPage = boxH + t.MarginTop <= flow.ContentTop - flow.BottomMargin;
        // A table that fits a page opens on the next one when it does not fit here; a
        // taller one continues its rows overleaf, each row whole.
        if (fitsPage && flow.CurrentY - t.MarginTop - boxH < flow.BottomMargin && flow.CurrentY < flow.ContentTop) flow.ForceNewPage();
        var x0 = flow.CurrentLeft;
        flow.AdvanceY(t.MarginTop);
        if (fitsPage) EmitUaTableFrame(st, t, x0, flow.CurrentY, boxH);
        flow.AdvanceY(t.BorderTop + t.Spacing);
        for (var r = 0; r < t.Rows.Count; r++)
        {
            if (flow.CurrentY - t.RowH[r] < flow.BottomMargin && flow.CurrentY < flow.ContentTop)
            {
                flow.InjectContentAtCursor(st.csb.Build());
                st.csb = new Content.ContentStreamBuilder();
                flow.ForceNewPage();
                flow.AdvanceY(t.Spacing);
            }
            var x = x0 + t.BorderLeft + t.Spacing;
            for (var i = 0; i < t.Columns; i++)
            {
                EmitUaCssCell(st, t.Rows[r][i], x, flow.CurrentY, t.ColW[i], t.RowH[r]);
                x += t.ColW[i] + t.Spacing;
            }
            flow.AdvanceY(t.RowH[r] + t.Spacing);
        }
        flow.InjectContentAtCursor(st.csb.Build());
        flow.AdvanceY(t.BorderBottom + t.MarginBottom);
    }

    /// <summary>The table's background over its border box and its border bands.</summary>
    private static void EmitUaTableFrame(HtmlInlineFlowState st, UaCssTable t, double x0, double top, double boxH)
    {
        var csb = st.csb;
        if (t.Back is { } back) csb.SaveState().SetFillColor(back).Rectangle(x0, top - boxH, t.WidthPt, boxH).Fill().RestoreState();
        if (t.BorderColor is not { } bc) return;
        csb.SaveState().SetFillColor(bc);
        if (t.BorderTop > 0) csb.Rectangle(x0, top - t.BorderTop, t.WidthPt, t.BorderTop).Fill();
        if (t.BorderBottom > 0) csb.Rectangle(x0, top - boxH, t.WidthPt, t.BorderBottom).Fill();
        if (t.BorderLeft > 0) csb.Rectangle(x0, top - boxH, t.BorderLeft, boxH).Fill();
        if (t.BorderRight > 0) csb.Rectangle(x0 + t.WidthPt - t.BorderRight, top - boxH, t.BorderRight, boxH).Fill();
        csb.RestoreState();
    }

    /// <summary>One cell: its background over its box, its lines seated from its padded top,
    /// centred (or top / bottom aligned) in the row and aligned in its content width.</summary>
    private void EmitUaCssCell(HtmlInlineFlowState st, UaCssCell c, double x, double top, double w, double rowH)
    {
        if (c.Back is { } back) st.csb.SaveState().SetFillColor(back).Rectangle(x, top - rowH, w, rowH).Fill().RestoreState();
        var cellH = c.ContentH + c.PadTop + c.PadBottom;
        var offset = c.VAlign switch { "top" => 0, "bottom" => rowH - cellH, _ => (rowH - cellH) / 2 };
        var contentW = w - c.PadLeft - c.PadRight;
        var lineTop = top - offset - c.PadTop;
        foreach (var line in c.Lines)
        {
            var baseline = lineTop - line.Above;
            var slack = c.Align switch { HorizontalAlignment.Right => contentW - line.Pen, HorizontalAlignment.Center => (contentW - line.Pen) / 2, _ => 0 };
            foreach (var cell in line.Cells)
                if (cell.Item.Kind == HtmlInlineKind.Text) EmitInlineText(st, cell, x + c.PadLeft + System.Math.Max(0, slack) + cell.X, baseline);
            lineTop -= line.Above + line.Below;
        }
    }
}
