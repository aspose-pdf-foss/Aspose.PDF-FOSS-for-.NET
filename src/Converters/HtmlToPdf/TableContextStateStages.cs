using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The parse state the token loop works on: the sheet's cell-box rules, the table's own classes and the face its cells measure in.</summary>
    private static TableParseState SeedTableParseState(TableStyleConfig cfg, TableColumnModel colModel, bool uaCellBoxes, double uaLineFactor, bool uaSheetGrid, bool wordMailCells)
    {
        var ps = new TableParseState();
        ps.uaCellBoxes = uaCellBoxes;
        ps.uaLineFactor = uaLineFactor;
        ps.uaSheetGrid = uaSheetGrid;
        ps.sheetTdPadZero = uaCellBoxes && ((cfg.docCss is not null && UaSheetZeroesCellPadding(cfg.docCss)) || UaSheetZeroesCellPadding(cfg.css));
        ps.sheetTdBoxRule = SheetDeclaresCellBox(cfg.chainRules);
        ps.sheetFace = ps.sheetTdBoxRule ? SheetBodyFace(cfg.docCss ?? cfg.css) : null;
        ps.minContentGrid = cfg.tblTag.Success
            && cfg.tblTag.Value.IndexOf(BlockRowGridAttr, StringComparison.OrdinalIgnoreCase) >= 0;
        colModel.sheetCollapsed = UaBorderCollapse(cfg);
        ps.uaSheet = cfg.css;
        ps.uaDocSheet = cfg.docCss;
        ps.uaTableClasses = new List<string>(cfg.tblClasses).ToArray();
        ps.cellPadAttrDeclared = cfg.tblCellPadAttr is not null;
        ps.wordMailCells = wordMailCells;
        ps.cellFamily = cfg.defaultCellFace is { Length: > 0 } dcf ? dcf
            // (…and in the browser's default serif when nothing names a face)
            : uaCellBoxes ? "Times New Roman" : null;
        // font-weight inherits: a weight the table declares - inline, or through the class
        return ps;
    }

    /// <summary>The table tag's own style and presentational attributes, the class list it carries, and the chain rules that reach it through its ancestors.</summary>
    private static void ReadTableTagAndChainRules(TableStyleConfig cfg, TableColumnModel colModel, bool uaSheetGrid)
    {
        // cellspacing="0" is a DECLARATION (no spacing), distinct from the absent
        // attribute (which leaves the UA's default border-spacing in force).
        cfg.tblCellSpacingDeclared = false;
        if (cfg.tblTag.Success)
        {
            var sm = Regex.Match(cfg.tblTag.Value, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            // JSON-escaped dialect: the style value is an unquoted token truncated at the
            // first whitespace (see ParseAttributes) — parse the declarations it kept.
            if (!sm.Success && cfg.tblTag.Value.IndexOf("\\\"", StringComparison.Ordinal) >= 0)
                sm = Regex.Match(cfg.tblTag.Value, @"style\s*=\s*(\S+)", RegexOptions.IgnoreCase);
            if (sm.Success)
                foreach (Match d in StyleDeclRx.Matches(sm.Groups[1].Value))
                    cfg.tblStyle[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
            var bm = Regex.Match(cfg.tblTag.Value, @"(?<!\w)border\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bm.Success) cfg.tblBorderAttr = bm.Groups[1].Value;
            var bcAttr = Regex.Match(cfg.tblTag.Value, @"bordercolor\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bcAttr.Success) cfg.tblBorderColorAttr = bcAttr.Groups[1].Value;
            var cm = Regex.Match(cfg.tblTag.Value, @"cellpadding\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (cm.Success) cfg.tblCellPadAttr = cm.Groups[1].Value;
            var csAttr = Regex.Match(cfg.tblTag.Value, @"cellspacing\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (csAttr.Success && PresentationalLengthPt(csAttr.Groups[1].Value) is { } csPt)
            {
                colModel.tblCellSpacingPt = csPt;
                cfg.tblCellSpacingDeclared = true;
                colModel.cellSpacingPt = colModel.tblCellSpacingPt;
            }
            var hm = Regex.Match(cfg.tblTag.Value, @"height\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (hm.Success) double.TryParse(hm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out colModel.tblHeightPx);
        }

        // Stylesheet rules addressed at the table's own class(es). The table-level
        // rule (".listTable { font/width/color }") merges UNDER the inline style;
        // ".cls td" / ".cls th" (the parser collapses ".cls tr td" to these) style
        // the cell grid. Border declarations stay out of the merge — a class border
        // is the table's OUTER frame, not a box on every cell.
        cfg.tblClasses = new List<string>();
        if (cfg.tblTag.Success)
        {
            var clm = Regex.Match(cfg.tblTag.Value, @"class\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (clm.Success)
                foreach (var c in clm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    cfg.tblClasses.Add(c);
        }
        // A collapsed grid (the table class' border-collapse: collapse): the cell borders are the
        // grid's lines - no border spacing, and the outer frame is those same borders.
        cfg.collapsedGrid = TableOwnClassRule(cfg, cfg.tblClasses, "") is { } collapseRule && collapseRule.TryGetValue("border-collapse", out var collapseV)
            && collapseV.Contains("collapse", StringComparison.OrdinalIgnoreCase);
        cfg.tblClassDecl = TableOwnClassRule(cfg, cfg.tblClasses, "");
        if (cfg.tblClassDecl is not null)
            foreach (var kv in cfg.tblClassDecl)
                if (kv.Key is "font-size" or "font-family" or "font-weight" or "color"
                        or "width" or "max-width"
                    && !cfg.tblStyle.ContainsKey(kv.Key))
                    cfg.tblStyle[kv.Key] = kv.Value;
        // …and the sheet's CHAIN rule on the table (`#right_column TABLE { … }`, reached through the
        // containers the grid stands in) merges under those the same way: its typography and box
        // declarations are the table's where nothing closer says otherwise. Its border is the
        // grid's OUTER frame (read below), never a box on every cell.
        cfg.chainBase = null;
        cfg.tblChainDecls = null;
        // (a UA-boxed grid lays its cells out on the browser box model itself: the chain dialect's box runs,
        //  bands and insets stay off - its descendant rules reach the cells through ApplyUaChainRules)
        if (cfg.chainRules is { Count: > 0 } && cfg.liftNestedTables && !uaSheetGrid)
        {
            var te = new CssElem { Tag = "table" };
            if (cfg.tblTag.Success)
            {
                var im2 = Regex.Match(cfg.tblTag.Value, @"\bid\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
                if (im2.Success) te.Id = im2.Groups[1].Value;
                if (cfg.tblClasses.Count > 0) te.Classes = cfg.tblClasses.ToArray();
            }
            cfg.chainBase = cfg.cssAncestors is { Count: > 0 }
                ? new List<CssElem>(cfg.cssAncestors) : new List<CssElem>();
            cfg.chainBase.Add(te);
            cfg.tblChainDecls = MatchChainDecls(cfg.chainRules, cfg.chainBase);
            // (the quirks-mode chain sheet's dialect - a standards-mode document's nested grids
            // keep the calibrated model their greens were measured on)
            colModel.ancestorScopedGrid = _ancestorGridSheet && AncestorScopesGrid(cfg);
            // …and the sheet's two-part `X table` rules the flat map keeps (`.datagrid table
            // { width: 100% }`): the innermost ancestor's rule wins, and a chain rule outranks them.
            if (_ancestorGridSheet && AncestorTableDecls(cfg) is { } ancDecls)
            {
                if (cfg.tblChainDecls is null) cfg.tblChainDecls = ancDecls;
                else foreach (var kv in ancDecls) if (!cfg.tblChainDecls.ContainsKey(kv.Key)) cfg.tblChainDecls[kv.Key] = kv.Value;
            }
            if (cfg.tblChainDecls is not null && _ancestorGridSheet && cfg.cssAncestors is { Count: > 0 })
                foreach (var kv in cfg.tblChainDecls)
                    // (its width keeps the chain arm below, which resolves a percent of the BOX)
                    if (kv.Key is "font-size" or "font-family" or "font-weight" or "color"
                            or "border-spacing" or "border-collapse"
                            or "background" or "background-color"
                        && !cfg.tblStyle.ContainsKey(kv.Key))
                        cfg.tblStyle[kv.Key] = kv.Value;
        }
    }
}
