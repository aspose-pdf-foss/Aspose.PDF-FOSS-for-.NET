using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>A form control a field-cell grid seats in a cell (see HtmlDocProfile.fieldCellGrids).</summary>
internal sealed class GridFieldSpec
{
    public string Tag = "input";
    public string? Name;
    public string Value = "";
    public List<string>? Options;
    public double WidthPt;
    public double HeightPt;
    /// <summary>A percent width: the field takes this share of its cell's content box.</summary>
    public double WidthFraction;
    public bool ReadOnly;
    /// <summary>The style or sheet stated the width / height in pixels (else the control's own box stands in -
    /// under a percent width too, as the most it asks of its column).</summary>
    public bool DeclaredWidth;
    public bool DeclaredHeight;
}

/// <summary>A select or textarea of a field-cell grid while its tokens pass: their text is the
/// control's options or value, never cell text.</summary>
internal sealed class GridFieldCapture
{
    public GridFieldSpec Spec = new();
    public StringBuilder Text = new();
    public string? Chosen;
    public bool OptionSelected;
}

internal static partial class HtmlToPdfConverter
{
    /// <summary>A control's value size, as the flow draws it in a control box.</summary>
    private const double GridFieldValuePt = 10.0;

    /// <summary>A control table a stylesheet form grids with fields in its cells: it holds a text box, a list or
    /// a text area, and every visible control stands in the table's own cells (none in a grid nested in one) and
    /// is one of those or a push button, or a check box or radio button alone in its cell (a table of buttons or
    /// options only is the drawn-control grid's).</summary>
    private static bool FieldGridTable(string tableHtml)
    {
        var (all, boxes, fieldable) = FieldGridControls(tableHtml);
        return boxes > 0 && fieldable && FieldGridControls(OuterTableMarkup(tableHtml)).count == all;
    }

    private static (int count, int boxes, bool fieldable) FieldGridControls(string markup)
    {
        var count = 0;
        var boxes = 0;
        var lastBoxCell = -1;
        foreach (Match m in SafeRegex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (SafeRegex.IsMatch(HiddenInlineRx, m.Value)) continue;
            var box = true;
            if (m.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                var ty = SafeRegex.Match(m.Value, @"\btype\s*=\s*[""']?([A-Za-z-]+)", RegexOptions.IgnoreCase) is { Success: true } tm
                    ? tm.Groups[1].Value.ToLowerInvariant() : "text";
                if (ty == "hidden") continue;
                var boxAlone = ty is "checkbox" or "radio" && AloneInItsCell(markup, m);
                if (!boxAlone && !TextLikeInputTypes.Contains(ty) && ty is not ("submit" or "button" or "reset")) return (count, boxes, false);
                box = TextLikeInputTypes.Contains(ty);
            }
            // a control with no attributes, or one standing between cells, is never seated by the grid's cell arms
            // (the flow draws it); one inside a block of its cell (the sign-in box of a page laid out in a table)
            // is the flow's, not the grid's
            if (SafeRegex.IsMatch(m.Value, @"^<\s*\w+\s*/?\s*>$") || CellInner(markup, m.Index) is not var (start, _)
                || SafeRegex.IsMatch(markup.Substring(start, m.Index - start), CellBlockOpenRx, RegexOptions.IgnoreCase))
                return (count, boxes, false);
            // (a cell's fields are seated a line each: a second box of the cell - a split zip code or phone
            // number - would stand under the first, where the flow lines them up)
            if (box && start == lastBoxCell) return (count, boxes, false);
            count++;
            if (!box) continue;
            boxes++;
            lastBoxCell = start;
        }
        return (count, boxes, true);
    }

    private const string CellBlockOpenRx = @"<(div|p|form|ul|ol|dl|li|fieldset|section|article|header|footer|nav|aside|blockquote|h[1-6])\b";

    /// <summary>Where the content of the cell around an index starts and ends, the cell open at the index's own
    /// table depth - a table nested in that cell is passed over (null outside a cell: before the first one, or
    /// after a cell closed and before the next opens).</summary>
    private static (int start, int end)? CellInner(string markup, int at)
    {
        var open = at;
        for (var depth = 0; ;)
        {
            open = open > 0 ? markup.LastIndexOf('<', open - 1) : -1;
            if (open < 0) return null;
            var tag = TableTagAt(markup, open);
            if (tag == TableTag.TableClose) depth++;
            else if (tag == TableTag.TableOpen && --depth < 0) return null;
            else if (depth == 0 && tag == TableTag.CellClose) return null;
            else if (depth == 0 && tag == TableTag.CellOpen) break;
        }
        var openEnd = markup.IndexOf('>', open);
        if (openEnd < 0 || openEnd > at) return null;
        var close = openEnd;
        for (var depth = 0; ;)
        {
            close = markup.IndexOf('<', close + 1);
            if (close < 0) return null;
            var tag = TableTagAt(markup, close);
            if (tag == TableTag.TableOpen) depth++;
            else if (tag == TableTag.TableClose && --depth < 0) return null;
            else if (depth == 0 && tag == TableTag.CellClose) break;
        }
        return close > at ? (openEnd + 1, close) : null;
    }

    private enum TableTag { None, TableOpen, TableClose, CellOpen, CellClose }

    /// <summary>The table or cell tag the `&lt;` at this index opens or closes, if it is one.</summary>
    private static TableTag TableTagAt(string markup, int at)
    {
        var closing = at + 1 < markup.Length && markup[at + 1] == '/';
        var name = at + (closing ? 2 : 1);
        if (name >= markup.Length || (markup[name] | 0x20) != 't') return TableTag.None;
        if (IsCellTagAt(markup, name + 1)) return closing ? TableTag.CellClose : TableTag.CellOpen;
        var table = string.Compare(markup, name, "table", 0, "table".Length, StringComparison.OrdinalIgnoreCase) == 0
            && (name + "table".Length >= markup.Length || !char.IsLetterOrDigit(markup[name + "table".Length]));
        if (!table) return TableTag.None;
        return closing ? TableTag.TableClose : TableTag.TableOpen;
    }

    /// <summary>A control that is the only content of its cell: no text stands beside it.</summary>
    private static bool AloneInItsCell(string markup, Match control)
    {
        if (CellInner(markup, control.Index) is not var (start, end)) return false;
        var inner = markup.Substring(start, end - start);
        return SafeRegex.Replace(DecodeEntities(SafeRegex.Replace(inner, @"<[^>]*>", "")), @"[\s ]+", "").Length == 0;
    }

    /// <summary>A `d` or `h` closing a cell tag's name at this index (td / th, not tr / table / tbody).</summary>
    private static bool IsCellTagAt(string markup, int at)
        => at + 1 < markup.Length && (markup[at] | 0x20) is 'd' or 'h' && !char.IsLetterOrDigit(markup[at + 1]);

    /// <summary>The box a cell's control asks for: its own style, else the sheet's tag/class/id rules,
    /// else its intrinsic size (size / cols / rows) - the flat path's rules. A percent width is a share of the
    /// cell instead.</summary>
    private static GridFieldSpec GridFieldBox(TableStyleConfig cfg, string tag, Dictionary<string, string> attrs, bool multiline)
    {
        attrs.TryGetValue("style", out var style);
        var (w, h) = ParseInputSize(style);
        var frac = CssPercentFraction(style, "width");
        foreach (var sheet in new[] { cfg.docCss, cfg.css })
        {
            var sheetBox = SheetControlBox(sheet, tag, attrs);
            var (sw, sh) = ParseInputSize(sheetBox);
            if (w <= 0 && frac <= 0) { w = sw; if (w <= 0) frac = CssPercentFraction(sheetBox, "width"); }
            if (h <= 0) h = sh;
        }
        var (iw, ih, _) = IntrinsicControlBox(attrs, multiline);
        attrs.TryGetValue("name", out var name);
        if (string.IsNullOrEmpty(name)) attrs.TryGetValue("id", out name);
        attrs.TryGetValue("value", out var value);
        return new GridFieldSpec
        {
            Tag = tag,
            Name = string.IsNullOrEmpty(name) ? null : name,
            Value = DecodeEntities(value ?? ""),
            WidthPt = w > 0 ? w * PxPt : iw,
            HeightPt = h > 0 ? h * PxPt : ih,
            WidthFraction = w > 0 ? 0 : frac,
            ReadOnly = attrs.ContainsKey("disabled") || attrs.ContainsKey("readonly"),
            DeclaredWidth = w > 0,
            DeclaredHeight = h > 0,
        };
    }

    /// <summary>A field joins its cell: a fixed-width control is replaced content the column must hold.</summary>
    private static void SeatGridField(TableStyleConfig cfg, TableParseState ps, GridFieldSpec spec)
    {
        if (cfg.makeField is not null) (ps.cellFields ??= new List<Forms.Field>()).Add(cfg.makeField(spec));
        if (spec.WidthFraction <= 0) ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, spec.WidthPt);
        // a share of the cell shrinks with its column: its intrinsic box is max-content only
        else ps.cellImgMaxPt = Math.Max(ps.cellImgMaxPt, spec.WidthPt);
    }

    /// <summary>A text-like input in a field-cell grid: a text field in its cell.</summary>
    private static bool OpenGridTextInput(TableStyleConfig cfg, TableParseState ps, Token tok, string inType)
    {
        if (!cfg.fieldCells || ps.cell is null || tok.Attributes is null || !TextLikeInputTypes.Contains(inType)) return false;
        // a control the page hides draws nothing
        if (tok.Attributes.TryGetValue("style", out var style) && SafeRegex.IsMatch(HiddenInlineRx, style)) return true;
        SeatGridField(cfg, ps, GridFieldBox(cfg, "input", tok.Attributes, multiline: false));
        return true;
    }

    /// <summary>A push button in a field-cell grid: a button field round its caption, as wide as the caption
    /// in the control face plus the button chrome unless the style sizes it.</summary>
    private static bool OpenGridPushButton(TableStyleConfig cfg, TableParseState ps, Token tok, string inType)
    {
        if (!cfg.fieldCells || ps.cell is null || tok.Attributes is null || inType is not ("submit" or "button" or "reset")) return false;
        if (tok.Attributes.TryGetValue("style", out var style) && SafeRegex.IsMatch(HiddenInlineRx, style)) return true;
        tok.Attributes.TryGetValue("value", out var value);
        var caption = DecodeEntities(string.IsNullOrWhiteSpace(value) ? UaButtonCaption(inType) : value.Trim());
        if (caption.Length == 0) return false;
        var spec = GridFieldBox(cfg, "input", tok.Attributes, multiline: false);
        spec.Tag = "button";
        spec.Value = caption;
        if (!spec.DeclaredWidth) spec.WidthPt = MeasureStd14(ClButtonFace, caption, ClButtonFontPt) + ButtonChromeWPt;
        if (!spec.DeclaredHeight) spec.HeightPt = ButtonHeightPt;
        SeatGridField(cfg, ps, spec);
        return true;
    }

    /// <summary>A select or textarea opening in a field-cell grid starts its capture.</summary>
    private static void OpenGridFieldCapture(TableStyleConfig cfg, TableParseState ps, Token tok, string tag)
    {
        if (!cfg.fieldCells || ps.cell is null || tok.IsClose || tok.Attributes is null) return;
        var spec = GridFieldBox(cfg, tag, tok.Attributes, multiline: tag == "textarea");
        if (tag == "select") spec.Options = new List<string>();
        ps.fieldCapture = new GridFieldCapture { Spec = spec };
        if (tok.IsSelfClosing) CloseGridFieldTag(cfg, ps, tag);
    }

    /// <summary>An option opening inside a captured select.</summary>
    private static void OpenGridFieldOption(TableParseState ps, Token tok)
    {
        if (ps.fieldCapture is not { Spec.Options: not null } cap || tok.IsClose) return;
        FlushGridOption(cap);
        cap.OptionSelected = tok.Attributes?.ContainsKey("selected") == true;
    }

    private static void FlushGridOption(GridFieldCapture cap)
    {
        var text = CollapseWs(cap.Text.ToString()).Trim();
        cap.Text.Clear();
        if (text.Length == 0 && !cap.OptionSelected) return;
        cap.Spec.Options!.Add(text);
        if (cap.OptionSelected && cap.Chosen is null) cap.Chosen = text;
        cap.OptionSelected = false;
    }

    /// <summary>Text inside a captured control: kept for the control, never flowed into the cell.</summary>
    private static bool CaptureGridFieldText(TableParseState ps, Token tok)
    {
        if (ps.fieldCapture is not { } cap) return false;
        cap.Text.Append(DecodeEntities(tok.Value));
        return true;
    }

    /// <summary>A captured select or textarea closes: its field joins the cell. A select is as wide as
    /// its widest option plus the drop-down chrome unless sized; a textarea's body is its value.</summary>
    private static void CloseGridFieldTag(TableStyleConfig cfg, TableParseState ps, string tag)
    {
        if (ps.fieldCapture is not { } cap || cap.Spec.Tag != tag) return;
        ps.fieldCapture = null;
        if (ps.cell is null) return;
        var spec = cap.Spec;
        if (spec.Options is { } options)
        {
            FlushGridOption(cap);
            spec.Value = cap.Chosen ?? options.FirstOrDefault() ?? "";
            // (a declared width is the box the widget stands a quarter point inside of)
            if (spec.DeclaredWidth) spec.WidthPt -= 2 * ClSelectInsetPt;
            else spec.WidthPt = options.Select(o => MeasureStd14("Helvetica", o, ClSelectFontPt)).DefaultIfEmpty(0).Max() + SelectChromePt;
            if (!spec.DeclaredHeight) spec.HeightPt = SelectBoxHeightPt;
        }
        else spec.Value = cap.Text.ToString().Trim('\r', '\n');
        SeatGridField(cfg, ps, spec);
    }

    /// <summary>The cell controls and radio options of the grid rows a later page carries: the form listed each
    /// on the grid's first page, where its slice was drawn for that page's fonts; it stands on the page its row
    /// landed on (null: the row is not drawn, and the control leaves the form).</summary>
    private static void ReseatSpilledCellWidgets(Document doc, Page startPage, Page? page,
        IReadOnlyList<(Annotations.Annotation annot, Rectangle rect)> draws,
        IReadOnlyList<(Forms.RadioButtonOptionField opt, Rectangle rect)> options)
    {
        if (draws.Count == 0 && options.Count == 0) return;
        var spilled = new HashSet<object?>(draws.Select(d => (object?)d.annot.Dict).Concat(options.Select(o => (object?)o.opt.KidDict)),
            ReferenceEqualityComparer.Instance);
        if (doc.Reader.Resolve(startPage.Dict.Get("Annots")) is Core.PdfArray annots)
            for (var i = annots.Count - 1; i >= 0; i--)
                if (spilled.Contains(doc.Reader.Resolve(annots[i]))) annots.RemoveAt(i);
        foreach (var (annot, rect) in draws)
        {
            if (page is null)
            {
                if (annot is Forms.Field dropped) doc.Form.Delete(dropped);
                continue;
            }
            Table.PlaceCellAnnotation(annot, page, rect);
            annot.Dict.Set("P", page.Dict);
        }
        // (a radio group joins the form after the layout, on the page it opened on: an option on another page
        // names its own)
        foreach (var (opt, rect) in options)
        {
            if (page is null)
            {
                if (opt.OptionName is { } gone) opt.OwnerRadio?.DeleteOption(gone);
                continue;
            }
            Forms.RadioButtonField.PlaceOptionWidget(opt, page, rect);
            opt.KidDict?.Set(PlacePageHint, new Core.PdfInteger(page.Number));
        }
    }

    /// <summary>The page a widget of a field not yet on the form is placed on when the field joins it.</summary>
    private const string PlacePageHint = "_PlacePage";
}
