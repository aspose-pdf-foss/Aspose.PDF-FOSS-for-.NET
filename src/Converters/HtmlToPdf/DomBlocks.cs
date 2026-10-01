using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Build the styled-run row for a nav-bar container: tabs from the
    /// left-pinned cluster, sign-in group from the right-pinned one, colors/weights
    /// resolved per element (including two-part descendant rules), the active tab's
    /// top strip from its border-top-color.</summary>
    private static Block? BuildNavRowBlock(HtmlNode container, HtmlNode barEl, Color barColor, double barHeightPx, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var nv = new NavRowBlockState();
        nv.container = container;
        nv.barEl = barEl;
        nv.barColor = barColor;
        nv.barHeightPx = barHeightPx;
        nv.css = css;
        nv.fontPx = DomFontPx(nv.container, 13, nv.css);
        nv.rowHeightPx = Math.Max(ParsePxValue(DomDecl(nv.container, "height", nv.css)), nv.barHeightPx);
        nv.barBorder = null;
        nv.bb = DomDecl(nv.barEl, "border-bottom", nv.css);
        if (!string.IsNullOrEmpty(nv.bb) && !nv.bb.Contains("none", StringComparison.OrdinalIgnoreCase))
            nv.barBorder = ParseCssColor(nv.bb) ?? Color.FromArgb(0, 0, 0);

        nv.runs = new List<RowRun>();
        nv.leftPad = 0;
        nv.rightPad = 0;

        foreach (var d in nv.container.Descendants())
        {
            if (d.Tag.Length == 0 || IsHiddenElement(d.Tag, d.Attrs, nv.css)) continue;
            if (DomDecl(d, "position", nv.css)?.Equals("absolute", StringComparison.OrdinalIgnoreCase) != true) continue;
            var isLeft = DomDecl(d, "left", nv.css)?.Trim() == "0";
            var isRight = DomDecl(d, "right", nv.css)?.Trim() == "0";
            if (!isLeft && !isRight) continue;
            var (pl, _) = DomBoxLR(d, "padding", nv.css);
            var (_, pr) = DomBoxLR(d, "padding", nv.css);
            if (isLeft) { nv.leftPad = pl; CollectNavCluster(nv, d, rightGroup: false); }
            else { nv.rightPad = pr; CollectNavCluster(nv, d, rightGroup: true); }
        }
        if (nv.runs.Count == 0) return null;

        return new Block
        {
            Text = "",
            RowRuns = nv.runs,
            RowHeightPx = nv.rowHeightPx,
            RowBarColor = nv.barColor,
            RowBarHeightPx = nv.barHeightPx,
            RowBarBorderColor = nv.barBorder,
            RowFontPx = nv.fontPx,
            RowLeftPadPx = nv.leftPad,
            RowRightPadPx = nv.rightPad,
        };
    }

    /// <summary>Build an RTL diagram-table block: requires an explicit table width, a
    /// full-width inline-svg placeholder row, and a trailing legend row whose every
    /// cell holds one svg placeholder plus label text. Returns null when the table
    /// doesn't match (it then flows through the normal table pipeline).</summary>
    private static Block? BuildRtlSvgTableBlock(HtmlNode table, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var rt = new RtlSvgTableState();
        rt.table = table;
        rt.css = css;
        rt.widthPx = ParsePxValue(DomDecl(rt.table, "width", rt.css));
        if (rt.widthPx <= 0) return null;

        rt.rows = new List<HtmlNode>();
        foreach (var d in rt.table.Descendants())
            if (d.Tag == "tr" && !IsHiddenElement(d.Tag, d.Attrs, rt.css)) rt.rows.Add(d);
        if (rt.rows.Count < 2) return null;

        rt.dt = new RtlSvgTable { WidthPx = rt.widthPx };

        rt.legendCells = Cells(rt.rows[^1]);
        if (rt.legendCells.Count < 2) return null;
        foreach (var cell in rt.legendCells)
        {
            var svg = SvgPlaceholder(cell);
            if (svg is null) return null;
            rt.dt.Legend.Add((svg.Value.idx, DomText(cell, rt.css)));
        }

        // main figure: the largest svg placeholder in the earlier rows
        for (var ri = 0; ri < rt.rows.Count - 1; ri++)
        {
            var svg = SvgPlaceholder(rt.rows[ri]);
            if (svg is not null && svg.Value.w > rt.dt.MainSvgWPx)
            {
                rt.dt.MainSvgIdx = svg.Value.idx;
                rt.dt.MainSvgWPx = svg.Value.w;
                rt.dt.MainSvgHPx = svg.Value.h;
            }
        }
        if (rt.dt.MainSvgIdx < 0 || rt.dt.MainSvgWPx <= 0) return null;

        // caption: first non-figure row's text; axis labels: the row of plain-text cells
        for (var ri = 0; ri < rt.rows.Count - 1; ri++)
        {
            ReadRtlSvgDataRow(rt, ri);
        }

        // The calibrated fractions describe the 3-legend auto-layout;
        // other shapes fall back to equal right-to-left columns.
        if (rt.dt.Legend.Count != 3)
        {
            ReadRtlSvgLegend(rt);
        }

        return new Block { Text = "", Diagram = rt.dt };
    }

    /// <summary>Build a flex-grid block from the waybill container: each
    /// display:flex row's percent-width column divs become cells — a dt/dd
    /// label-value pair, plain wrapping text, or the signature composite (a dt
    /// with a float:right span plus a padded full-width dd of float halves).</summary>
    private static Block? BuildFlexGridBlock(HtmlNode host,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var fg = new FlexGrid();
        foreach (var h in host.Descendants())
            if (h.Tag == "h1") { fg.Title = DomText(h, css); break; }


        foreach (var row in host.Descendants())
        {
            if (row.Tag is not ("div" or "tr") || DomDecl(row, "display", css)?.Trim()
                    .Equals("flex", StringComparison.OrdinalIgnoreCase) != true) continue;
            var fr = new FlexGridRow();
            if (row.Tag == "tr") fg.TableFlavor = true;
            foreach (var cell in row.Children)
            {
                if (!ParseFlexGridCell(fr, cell, css)) break;
            }
            if (fr.Cells.Count > 0) fg.Rows.Add(fr);
        }
        return fg.Rows.Count >= 4 ? new Block { Text = "", Flex = fg } : null;
    }

    /// <summary>Build a centered search-form block from a &lt;form&gt; with a text
    /// input and submit buttons. All geometry comes from the stylesheet: the centered
    /// cell takes the WIDEST width among the input's class rules, the input's outer
    /// width its narrowest class width + the inline padding shorthand + borders +
    /// the wrapper's margin-left, buttons their container height/background and
    /// button font, the side link its own color/size/margins.</summary>
    private static Block? BuildSearchFormBlock(HtmlNode form, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var fb = new SearchFormBlockState();
        fb.form = form;
        fb.css = css;
        fb.textInput = null;
        fb.buttons = new List<(string Label, string Name)>();
        fb.icon = null;
        fb.link = null;
        ScanSearchFormDescendants(fb);
        if (fb.textInput is null || fb.buttons.Count == 0) return null;

        fb.sf = new SearchForm { Buttons = fb.buttons };
        fb.inputName = null;
        fb.textInput.Attrs?.TryGetValue("name", out fb.inputName);
        fb.sf.InputName = fb.inputName;

        fb.cellW = 0;
        fb.inputContentW = 0;
        fb.inputH = 25;
        ReadSearchInputStyle(fb);
        fb.wrapMargin = 4.0; // .ds margin-left on the input wrapper
        if (fb.textInput.Parent is not null)
        {
            var (wl, _) = DomBoxLR(fb.textInput.Parent.Parent ?? fb.textInput.Parent, "margin", fb.css);
            if (wl > 0) fb.wrapMargin = wl;
        }
        fb.sf.CellWidthPx = fb.cellW;
        fb.sf.InputContentPx = fb.inputContentW;
        fb.sf.InputWidthPx = fb.inputContentW + fb.padL + fb.padR + 2 + fb.wrapMargin;
        fb.sf.InputHeightPx = fb.inputH + 2;

        if (fb.icon is not null && fb.icon.Attrs is not null)
        {
            fb.icon.Attrs.TryGetValue("src", out var isrc);
            fb.sf.IconSrc = isrc;
            if (fb.icon.Attrs.TryGetValue("width", out var iw)) fb.sf.IconWPx = ParsePxValue(iw + "px");
            if (fb.icon.Attrs.TryGetValue("height", out var ih)) fb.sf.IconHPx = ParsePxValue(ih + "px");
            if (fb.icon.Attrs.TryGetValue("style", out var isty))
            {
                var rm = Regex.Match(isty, @"right\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (rm.Success) fb.sf.IconRightPx = double.Parse(rm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var tm = Regex.Match(isty, @"top\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (tm.Success) fb.sf.IconTopPx = double.Parse(tm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // button box: wrapper height + bg, label font from the button class
        ReadSearchFormButtons(fb);

        if (fb.link is not null)
        {
            fb.sf.LinkText = DomText(fb.link, fb.css);
            fb.link.Attrs!.TryGetValue("href", out var href);
            fb.sf.LinkUrl = href;
            var lc = DomColor(fb.link, fb.css);
            if (lc is not null) fb.sf.LinkColor = lc;
            fb.sf.LinkFontPx = DomFontPx(fb.link, 11, fb.css);
            // The side cell sits flush against the centered cell;
            // the link's own margin-left is not part of the rendered offset.
            fb.sf.LinkMarginLeftPx = 0;
        }

        var (_, formMb) = DomBoxTB(fb.form, "margin", fb.css);
        if (formMb > 0) fb.sf.MarginBottomPx = formMb;

        return new Block { Text = "", Form = fb.sf };
    }
}
