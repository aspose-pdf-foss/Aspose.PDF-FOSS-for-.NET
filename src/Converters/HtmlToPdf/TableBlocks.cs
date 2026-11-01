using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Lays out one table block of the escaped-attribute dialect and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutEscapedAttrTable(
        Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Dictionary<string, Dictionary<string, string>> css, Color? dialectButtonFill, string dialectButtonTextRg, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, double lineHeight)
    {
        // Escaped-attr dialect grid — the HTML4 default frame:
        // outer OUTSET border (top/left
        // #555, bottom/right black), every cell INSET (top/left black,
        // bottom/right #555), 0.75 pt lines, 2.25 pt edge spacing and 1.5 pt
        // between cells; Times 12 cells with bold headers, columns sized to
        // the widest cell content + 1.5 pt side padding; form controls occupy
        // their control boxes INSIDE cells.
    flow.afterEscapedRule = false;
        var et = new EscapedTableState();
        if (!TryParseEscapedRows(et, block, flow, css)) return;

        et.nCols = 0;
        SizeEscapedColumns(et, flow);
        PlanEscapedRows(et, flow, profile, doc, docFontDict, marginBottom, marginLeft, marginTop, pageHeight, pageWidth);
        for (int ri = 0; ri < et.trRows.Count; ri++)
        {
            if (!DrawEscapedRow(et, flow, doc, dialectButtonFill, dialectButtonTextRg, lineHeight, ri)) break;
        }
        flow.contentPage = flow.page;
        // Back into baseline space: the next text baseline sits one ascent
        // below the grid's bottom edge (plus its own margins).
        flow.y = et.gridTop - et.tableH - 0.9 * 12;
        flow.lastWasHardBreak = false;
        flow.prevFlowMarginBottom = 0;
        flow.prevFlowLineHeight = 0;
    }

    /// <summary>The paragraph margin the user-agent flow puts between blocks.</summary>
    private const double UaParagraphMarginPt = 13.44;

    // A control-bearing table still renders as a GRID when its visible controls
    // are all radios (plus button-family inputs, which draw nothing in a cell):
    // the radio factory carries the options into the cells as inline glyphs —
    // `◯ ◯Yes ◉ ◉No` on one line, the form-report shape. Any
    // text-like control (text input, select, textarea) keeps its table on the
    // flat path, whose blocks emit the AcroForm fields for it.
    /// <summary>A checkbox grid of the UA serif flow: gridable controls that include a checkbox and no radio - the
    /// radio grids keep their calibrated face and empty-cell heights.</summary>
    private static bool CheckboxGridControls(string markup)
        => RadioGridableControls(markup, checkboxesToo: true)
            && SafeRegex.IsMatch(markup, @"<\s*input\b[^>]*type\s*=\s*[""']?checkbox", RegexOptions.IgnoreCase)
            && !SafeRegex.IsMatch(markup, @"<\s*input\b[^>]*type\s*=\s*[""']?radio", RegexOptions.IgnoreCase);

    /// <summary>Every visible form control of the table lies inside a table NESTED in one of
    /// its cells: the outer table's own cells carry none.</summary>
    private static bool ControlsOnlyInNestedGrids(string markup)
        => HasVisibleFormControl(markup) && !HasVisibleFormControl(OuterTableMarkup(markup));

    /// <summary>The table's markup with every nested table cut out.</summary>
    private static string OuterTableMarkup(string markup)
    {
        var sb = new StringBuilder();
        var depth = 0;
        var last = 0;
        foreach (Match m in SafeRegex.Matches(TableOpenCloseRx, markup))
        {
            if (m.Groups[1].Length == 0)
            {
                // entering a nested grid: the outer markup before it is kept
                depth++;
                if (depth == 2) sb.Append(markup, last, m.Index - last);
            }
            else
            {
                // leaving one: the outer markup resumes after it
                depth--;
                if (depth == 1) last = m.Index + m.Length;
            }
        }
        sb.Append(markup, last, markup.Length - last);
        return sb.ToString();
    }

    private static readonly Regex TableOpenCloseRx = new Regex(@"<(/?)table\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The table states its own column grid as percentages of the box
    /// (<c>&lt;colgroup&gt;&lt;col style="width:12.5%"&gt;</c>). Such a grid belongs to the
    /// column solver that reads a colgroup — the control arm below sizes its columns from
    /// their content alone and would draw every run in them left of its declared share.</summary>
    private static bool ColGroupPercentGrid(string markup)
        => SafeRegex.IsMatch(markup,
            @"<col\b[^>]*style\s*=\s*[""'][^""']*(?<![-\w])width\s*:\s*\d*\.?\d+\s*%",
            RegexOptions.IgnoreCase);

    private static bool RadioGridableControls(string markup, bool checkboxesToo = false)
    {
        var hasRadio = false;
        foreach (Match fim in SafeRegex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            if (SafeRegex.IsMatch(HiddenInlineRx, fim.Value)) continue;
            if (!fim.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase))
                return false;
            var tyM = SafeRegex.Match(fim.Value, @"type\s*=\s*[""']?([A-Za-z]+)",
                RegexOptions.IgnoreCase);
            var ty = tyM.Success ? tyM.Groups[1].Value.ToLowerInvariant() : "text";
            if (ty == "radio" || (checkboxesToo && ty == "checkbox")) hasRadio = true;
            else if (ty is not ("hidden" or "button" or "submit" or "reset" or "image")) return false;
        }
        return hasRadio;
    }

    /// <summary>Lays out one table block and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutTableBlock(Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Dictionary<string, Dictionary<string, string>> css, HtmlLoadOptions? options, List<byte[]> inlineSvgs, List<(Page page, byte[] ops)> floatFirstOps, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, string? bodyCssFace, bool dwFormDoc, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, bool tableAfterSpacer, bool tableAfterText)
    {
        // Bare UA document: whichever arm draws this table, a break
        // paragraph that follows it stands the UA margin the table
        // never read (see breakAfterTable).
        if (profile.uaBareDoc) flow.lastWasMetricTable = true;
        var tb = new TableBlockState();
        if (!TryLayoutMetricTable(tb, block, flow, profile, doc, docFontDict, css, options, bodyCssFace, marginBottom, marginLeft, marginTop, pageHeight, pageWidth, tableAfterSpacer, tableAfterText)) return;
        PrepareTableRow(tb, block, flow, profile, css);
        MeasureTableBox(tb, flow, profile, marginLeft, pageWidth);
        var controls = new GridControlFactory(tb, flow, profile, doc);
        BuildGridTable(tb, block, profile, controls, css, options, inlineSvgs, bodyCssFace, dwFormDoc);
        RebuildOnDeclaredBox(tb, block, profile, controls, css, options, inlineSvgs, bodyCssFace, dwFormDoc, marginLeft, pageWidth);
        if (tb.table is not null)
        {
            LayoutBuiltTable(tb, tb.table, block, flow, profile, doc, docFontDict, floatFirstOps, bandStack, bodyCssFace, dwFormDoc, marginBottom, marginLeft, marginTop, pageHeight, pageWidth);
        }
        flow.lastWasHardBreak = false;
        flow.prevFlowMarginBottom = 0;
        flow.prevFlowLineHeight = 0;
        flow.afterRuleDrop = false;
        flow.afterFhTable = tb.fhRow;
    }

    /// <summary>The box the grid lays out in: a certificate's own declared width, the UA
    /// fieldset's content box, the browser's symmetric body box, or the flow's width.</summary>
    private static void MeasureTableBox(TableBlockState tb, HtmlFlowCursor flow,
        HtmlDocProfile profile, double marginLeft, double pageWidth)
    {
        tb.certTableW = profile.floatBothSidesDoc
            && CertDeclaredTableWidthPt(tb.fhTableHtml) is { } certDw
            && certDw > flow.contentWidth ? certDw : 0;
        if (profile.overDeclaredGridDoc) tb.tableAvailW = flow.contentWidth - UaBodyMarginPt - OverDeclaredHostChromePt;
        else if (tb.certTableW > 0) tb.tableAvailW = tb.certTableW;
        // inside a UA fieldset the frame's content box is the table's box
        else if (flow.fsIndentLive > 0 && profile.uaFieldsetContent && profile.fsBoxW > 0)
            tb.tableAvailW = profile.fsBoxW - 2 * (FsPadLeftPt - UaFieldsetSideInsetPt);
        // A UA-grid document's tables lay out in the browser's symmetric body box (measured on the
        // resume and the royalty statement: every 100% grid spans 96..499 on the 595 sheet).
        else if (profile.uaGridSheet && flow.contentWidth > pageWidth - 2 * marginLeft) tb.tableAvailW = pageWidth - 2 * marginLeft;
        else tb.tableAvailW = flow.contentWidth;
    }

    /// <summary>The form controls a UA grid seats: a checkbox per box cell, and radio options
    /// grouped by name across the document. Both builds of the grid draw from the same
    /// factory, so a group keeps its options across a rebuild, and the rebuild seats the
    /// checkboxes and fields the first build made.</summary>
    private sealed class GridControlFactory
    {
        private readonly TableBlockState _tb;
        private readonly HtmlFlowCursor _flow;
        private readonly HtmlDocProfile _profile;
        private readonly Document _doc;
        // The checkboxes and fields the grid's first build put on the form, in the order its walk met them,
        // and how many of them a rebuild has taken back (-1: no rebuild).
        private readonly List<Aspose.Pdf.Forms.Field> _made = new();
        private int _retaken = -1;

        public GridControlFactory(TableBlockState tb, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc)
        {
            _tb = tb;
            _flow = flow;
            _profile = profile;
            _doc = doc;
        }

        /// <summary>The grid is built again on another box: its controls are the ones the first build made,
        /// not a second set on the form.</summary>
        public void Rebuild() => _retaken = 0;

        private Aspose.Pdf.Forms.Field? Retake() => _retaken >= 0 && _retaken < _made.Count ? _made[_retaken++] : null;

        private T Keep<T>(T control) where T : Aspose.Pdf.Forms.Field
        {
            _made.Add(control);
            if (_retaken >= 0) _retaken = _made.Count;
            return control;
        }

        /// <summary>A UA checkbox grid's box: a form checkbox the row plan seats and the render pass places.</summary>
        public Aspose.Pdf.Forms.CheckboxField MakeCheckbox(bool chk)
        {
            if (Retake() is Aspose.Pdf.Forms.CheckboxField again) return again;
            var cb = new Aspose.Pdf.Forms.CheckboxField(_tb.tablePage, new Rectangle(0, 0, Table.UaCheckboxWidgetPt, Table.UaCheckboxWidgetPt)) { Checked = chk };
            _doc.Form.Add(cb, _tb.tablePage.Number);
            return Keep(cb);
        }

        /// <summary>A field-cell grid's control: a push button for a button, a combo box for a select, else a text
        /// field, on the form with the black frame the flow draws round a control box; the row plan seats it in its cell.</summary>
        public Aspose.Pdf.Forms.Field MakeField(GridFieldSpec spec)
        {
            if (Retake() is { } again and not Aspose.Pdf.Forms.CheckboxField) return again;
            var rect = new Rectangle(0, 0, spec.WidthPt, spec.HeightPt);
            Aspose.Pdf.Forms.Field field;
            if (spec.Tag == "button")
            {
                var button = NewPushButton(_tb.tablePage, rect, spec.Name);
                button.NormalCaption = spec.Value;
                field = button;
            }
            else if (spec.Options is { } options) field = NewComboBox(_tb.tablePage, rect, options, spec.Name);
            else
            {
                var box = new Aspose.Pdf.Forms.TextBoxField(_tb.tablePage, rect)
                {
                    Multiline = spec.Tag == "textarea",
                    DefaultAppearance = new Annotations.DefaultAppearance(spec.Tag == "textarea" ? "Courier" : "Helvetica", GridFieldValuePt),
                };
                box.Characteristics.Border = System.Drawing.Color.Black;
                box.Border = new Annotations.Border(box) { Width = 1 };
                if (spec.Name is not null) box.PartialName = spec.Name;
                box.Width = spec.WidthPt;
                box.Height = spec.HeightPt;
                field = box;
            }
            // (a push button's value is its caption)
            if (spec.Value.Length > 0 && spec.Tag != "button") field.Value = spec.Value;
            field.ReadOnly = spec.ReadOnly;
            field.CellWidthFraction = spec.WidthFraction;
            _doc.Form.Add(field, _tb.tablePage.Number);
            return Keep(field);
        }

        /// <summary>An option of a grid table's radio group, as a radio outside a grid is made: the group answers to
        /// the name its buttons share, an option is named by its button's value (else by the group name and its
        /// place), and the last button marked checked is the group's choice.</summary>
        public Aspose.Pdf.Forms.RadioButtonOptionField MakeRadio(string group, string value, bool chk)
        {
            var key = string.IsNullOrEmpty(group) ? "__gridradio" + _flow.gridRadioAnon++ : group;
            if (!_profile.gridRadioGroups.TryGetValue(key, out var rbf))
            {
                rbf = new Aspose.Pdf.Forms.RadioButtonField(_tb.tablePage);
                if (!string.IsNullOrEmpty(group)) rbf.PartialName = group;
                _profile.gridRadioGroups[key] = rbf;
                _profile.gridRadioPages.Add((rbf, _tb.tablePage));
                _profile.gridRadioValues[key] = new HashSet<string>(StringComparer.Ordinal) { RadioOffState };
            }
            _profile.gridRadioCounts.TryGetValue(key, out var optIdx);
            _profile.gridRadioCounts[key] = optIdx + 1;
            var ropt = new Aspose.Pdf.Forms.RadioButtonOptionField
            {
                Style = Aspose.Pdf.Forms.BoxStyle.Circle,
                OptionName = value.Length > 0 && _profile.gridRadioValues[key].Add(value) ? value : key + "_" + optIdx,
            };
            ropt.Characteristics.Border = System.Drawing.Color.Black;
            rbf.Add(ropt);
            if (chk) rbf.Selected = optIdx + 1;
            return ropt;
        }
    }

    /// <summary>The line box a built grid's cell lines pitch on (0: each line on its own size's normal box).</summary>
    private static double GridCellLineHeightPt(TableBlockState tb, HtmlDocProfile profile)
    {
        if (tb.radioGridTable) return Table.CssLineBoxPt(tb.radioGridFontPt);
        // Sectioned-report rhythm: cell lines pitch on the browser's own
        // line box too, not the flow's legacy em multiple.
        if (profile.sectionedReport && profile.formBodyFontPt > 0) return NormalLineHeightPt(profile.formBodyFontPt);
        // a scaled layout paces cells on the UA 18px line
        if (profile.scaleToPageWidth) return NormalLineHeightPt(DefaultBodyFontPt);
        // …but a quirks grid inherits no more of the sheet's line-height than of its font:
        // its lines pitch on their OWN size's normal box, and one document-wide pitch
        // oversizes every row a small class sets.
        if (SheetDeclaresCellBox(profile.docChainRules)) return 0;
        return profile.bodyLineHeightPt;
    }

    /// <summary>A checkbox grid of the UA serif flow: its builds seat its checkboxes as fields, in the flow's face.</summary>
    private static bool UaCheckboxGrid(TableBlockState tb, HtmlDocProfile profile)
        => tb.radioGridTable && profile.uaStdSerif && CheckboxGridControls(tb.fhTableHtml);

    /// <summary>Builds the grid on the measured box, in the dialect the document profile asks
    /// for.</summary>
    private static void BuildGridTable(TableBlockState tb, Block block,
        HtmlDocProfile profile, GridControlFactory controls,
        Dictionary<string, Dictionary<string, string>> css, HtmlLoadOptions? options,
        List<byte[]> inlineSvgs, string? bodyCssFace, bool dwFormDoc)
    {
        var checkboxGrid = UaCheckboxGrid(tb, profile);
        (tb.table, tb.renderNatW) = BuildTableFromHtml(tb.fhTableHtml, tb.tableAvailW, options, inlineSvgs, css, bandDialect: profile.floatBandDoc, makeRadio: controls.MakeRadio,
            makeCheckbox: checkboxGrid || tb.fieldGrid ? controls.MakeCheckbox : null,
            makeField: tb.fieldGrid ? controls.MakeField : null,
            cellLineHeightPt: GridCellLineHeightPt(tb, profile),
            // The page stylesheet's own base size seeds the grid (see bodyCssFontPt); the
            // probe above must measure the same cells this render builds.
            defaultCellFontPt: (dwFormDoc, tb.radioGridTable, profile.scaleToPageWidth) switch
            {
                (true, _, _) => 12.0,
                (_, true, _) => tb.radioGridFontPt,
                (_, _, true) => DefaultBodyFontPt,
                _ => profile.bodyCssFontPt,
            },
            // the UA serif flow's control grid draws in the flow's own face
            defaultCellFace: (dwFormDoc, checkboxGrid) switch { (true, _) => "Times New Roman", (_, true) => profile.metricFace, _ => null },
            cssRunFace: bodyCssFace, bodyTextColor: profile.bodyCssColor,
            // Sectioned reports lay their grids out on the browser's own cell
            // box: the UA's 1px vertical cell padding and pre-wrap line boxes.
            uaCellBoxes: profile.uaGridBoxes,
            uaLineFactor: profile.bodyLineHeightFactor, uaSheetGrid: profile.uaGridSheet,
            // Nested tables render as real grids, and the chain-selector dialect
            // that rides the same switch is on: a stylesheet's descendant rules
            // reach the cells they address instead of being dropped.
            liftNestedTables: true,
            ptCellWidths: profile.ptStyledFragment,
            redlineCells: profile.redlineDiffDoc,
            dwFormCells: dwFormDoc,
            wordMailCells: profile.wordMailDoc,
            docElementGrid: profile.elementGridDoc,
            pinnedBodyGrid: profile.bodyPinnedW > 0,
            // The over-declared grid document RENDERS on the honest CJK
            // model too — its reference draws full-em ideographs breaking
            // at every ideograph, and the legacy estimates mis-floor its
            // radical/plane-2 columns badly.
            fullWidthCjkMin: profile.overDeclaredGridDoc,
            overDeclaredDraw: profile.overDeclaredGridDoc,
            chainRules: profile.docChainRules,
            cssAncestors: block.CssAncestors, hostChain: block.CssAncestors is null ? block.HostChain : null,
            // (a grid no rule faces takes the body face only where the flow sets the prose in it)
            bodyFaceGrid: profile.sheetTypographyDoc);
    }

    /// <summary>A UA-grid document's grid declaring an ABSOLUTE box wider than the flow, but no
    /// wider than the grown sheet's box (page - margin - 90), lays out on its declared box:
    /// build it again on that box (measured on the quotation: the 650 px grids stand 96..583.5
    /// on their 673.5 sheet).</summary>
    private static void RebuildOnDeclaredBox(TableBlockState tb, Block block,
        HtmlDocProfile profile, GridControlFactory controls,
        Dictionary<string, Dictionary<string, string>> css, HtmlLoadOptions? options,
        List<byte[]> inlineSvgs, string? bodyCssFace, bool dwFormDoc,
        double marginLeft, double pageWidth)
    {
        if (tb.table is { HtmlDeclaredBoxAbs: true } uaDeclTable && profile.uaGridBoxes
            && uaDeclTable.HtmlDeclaredBoxPt > tb.tableAvailW + 0.01
            && uaDeclTable.HtmlDeclaredBoxPt <= pageWidth - marginLeft - UaPageMarginPt + 1e-3)
        {
            tb.tableAvailW = uaDeclTable.HtmlDeclaredBoxPt;
            controls.Rebuild();
            (tb.table, tb.renderNatW) = BuildTableFromHtml(tb.fhTableHtml, tb.tableAvailW, options, inlineSvgs, css, bandDialect: profile.floatBandDoc, makeRadio: controls.MakeRadio,
                makeCheckbox: UaCheckboxGrid(tb, profile) || tb.fieldGrid ? controls.MakeCheckbox : null, makeField: tb.fieldGrid ? controls.MakeField : null,
                cellLineHeightPt: profile.sectionedReport && profile.formBodyFontPt > 0 ? NormalLineHeightPt(profile.formBodyFontPt) : profile.bodyLineHeightPt,
                defaultCellFontPt: dwFormDoc ? 12.0 : profile.bodyCssFontPt, defaultCellFace: dwFormDoc ? "Times New Roman" : null,
                cssRunFace: bodyCssFace, bodyTextColor: profile.bodyCssColor, uaCellBoxes: true, liftNestedTables: true, uaLineFactor: profile.bodyLineHeightFactor, uaSheetGrid: profile.uaGridSheet,
                ptCellWidths: profile.ptStyledFragment, redlineCells: profile.redlineDiffDoc, dwFormCells: dwFormDoc, wordMailCells: profile.wordMailDoc,
                docElementGrid: profile.elementGridDoc, pinnedBodyGrid: profile.bodyPinnedW > 0, fullWidthCjkMin: profile.overDeclaredGridDoc,
                overDeclaredDraw: profile.overDeclaredGridDoc, chainRules: profile.docChainRules, cssAncestors: block.CssAncestors, hostChain: block.CssAncestors is null ? block.HostChain : null);
        }
    }

    /// <summary>The form-horizontal row drop, the pinned-body table gap and the radio-grid flags.</summary>
    private static void PrepareTableRow(TableBlockState tb, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Dictionary<string, Dictionary<string, string>> css)
    {
        tb.fhTableHtml = block.TableHtml ?? "";
        tb.fhRow = profile.formHorizontalDoc
            && tb.fhTableHtml.Contains("class=\"fh-row\"", StringComparison.OrdinalIgnoreCase);
        if (tb.fhRow) flow.y -= 9 * 0.75;
        // The pinned-body report's tables carry real CSS margins: the
        // sheet's `table { margin-top: 5px }` element rule (or a larger
        // inline one) is space above every grid — the first grid included,
        // whose margin offsets it from the page top (measured: the title
        // row's border at 72 + 3.75 + the 1px cellspacing).
        if (profile.bodyPinnedW > 0)
        {
            double pbMtPx = 0;
            if (css.TryGetValue("table", out var pbtR)
                && pbtR.TryGetValue("margin-top", out var pbtV)
                && SafeRegex.Match(pbtV, @"([\d.]+)\s*px") is { Success: true } pbtM)
                double.TryParse(pbtM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out pbMtPx);
            if (SafeRegex.Match(tb.fhTableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase)
                    is { Success: true } pbTag
                && SafeRegex.Match(DivStyleOf(pbTag.Value),
                    @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                    RegexOptions.IgnoreCase) is { Success: true } pbMt
                && double.TryParse(pbMt.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pbInline)
                && pbInline > pbMtPx)
                pbMtPx = pbInline;
            if (pbMtPx > 0) flow.y -= pbMtPx * 0.75;
            // The grid's FIRST border-spacing band sits above row one —
            // the declared `cellspacing="1px"` the generator's rows carry
            // only between and below themselves.
            if (SafeRegex.Match(tb.fhTableHtml, @"cellspacing\s*=\s*[""']?(\d+(?:\.\d+)?)",
                    RegexOptions.IgnoreCase) is { Success: true } pbCs
                && double.TryParse(pbCs.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pbCsPx)
                && pbCsPx > 0)
                flow.y -= pbCsPx * 0.75;
        }
        tb.radioGridTable = profile.metricFlow && RadioGridableControls(tb.fhTableHtml, profile.uaStdSerif);
        tb.radioGridFontPt = profile.uaStdSerif ? 12.0 : 11.0;
        tb.fieldGrid = block.FieldGrid;
        tb.tablePage = flow.page;
    }

    /// <summary>Where a table starts inside a framed wrapper div's content box: centred when the div
    /// centres and the table declares a narrower absolute width, else at the content left.</summary>
    private static double FramedTableOffsetPt(HtmlFlowCursor flow, string tableHtml)
    {
        if (!flow.frameCentred) return 0;
        var tag = SafeRegex.Match(tableHtml, @"<table\b([^>]*)>", RegexOptions.IgnoreCase);
        if (!tag.Success) return 0;
        double declared = 0;
        var wa = SafeRegex.Match(tag.Groups[1].Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)(?:px)?[""'\s>]", RegexOptions.IgnoreCase);
        if (wa.Success) declared = DtpNum(wa.Groups[1].Value) * PxPt;
        else if (SafeRegex.Match(DivStyleOf(tag.Value), @"(?<![-\w])width\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } sw
            && TryParseLength(sw.Groups[1].Value.Trim()) is { } swPt) declared = swPt;
        return declared > 0 && declared < flow.frameContentW ? (flow.frameContentW - declared) / 2 : 0;
    }

    /// <summary>The metric-flow arms: a body-box grid or a win-metric table lays itself out here and leaves the block done.</summary>
    private static bool TryLayoutMetricTable(TableBlockState tb, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Dictionary<string, Dictionary<string, string>> css, HtmlLoadOptions? options, string? bodyCssFace, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, bool tableAfterSpacer, bool tableAfterText)
    {
        tb.quirksRunTable = !profile.metricFlow && profile.quirksCssRun;
        if (profile.metricFlow) tb.tableFace = profile.metricFace;
        else if (tb.quirksRunTable) tb.tableFace = bodyCssFace;
        else tb.tableFace = null;
        // ASPOSE_TRACE_SEAT=1: which layouter takes the table.
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") == "1")
            Console.WriteLine($"[table] y={flow.y:0.##} ytop={pageHeight - flow.y:0.##} cw={flow.contentWidth:0.##} ml={marginLeft:0.##} pw={pageWidth:0.##} prevLh={flow.prevFlowLineHeight:0.##} prevMb={flow.prevFlowMarginBottom:0.##} metricFlow={profile.metricFlow} uaStdSerif={profile.uaStdSerif} quirksCssRun={profile.quirksCssRun} face={tb.tableFace} bodyBoxGrid={profile.bodyBoxGridDoc} overDeclared={profile.overDeclaredGridDoc} html={(block.TableHtml is { Length: > 0 } th ? th.Substring(0, Math.Min(60, th.Length)) : "")}");
        // The inline-body-margin dialect draws its tables as a collapsed
        // 1px grid with mid-row pagination — a shape the metric layouter
        // does not model (it paginates row-at-a-time and knows no
        // border-collapse).
        if (profile.bodyBoxGridDoc && WinMetricsFor(profile.metricFace) is { } bgm)
        {
            RenderBodyBoxGridTable(doc, flow, block.TableHtml ?? "",
                marginLeft, flow.contentWidth, pageWidth, pageHeight, marginBottom,
                profile.metricFace, bgm, profile.metricLineSum, docFontDict);
            flow.lastWasHardBreak = false;
            return false;   // the block is laid out; the loop this came from would continue
        }
        if (tb.tableFace is not null && WinMetricsFor(tb.tableFace) is { } tfm
            && !(RadioGridableControls(block.TableHtml ?? "", profile.uaStdSerif)
                && !ColGroupPercentGrid(block.TableHtml ?? "")
                // A UA table whose only controls sit in the grids NESTED in its cells is a
                // layout grid, not a control grid: the metric layouter draws it and its nested
                // grids as grids (measured: the letter whose reasons' checkboxes live in a
                // nested table keeps its four columns, the control arm flattens them).
                && !(profile.uaStdSerif && ControlsOnlyInNestedGrids(block.TableHtml ?? "")))
            // The over-declared attribute-grid document needs its nested
            // grids drawn as GRIDS — the metric layouter flattens them.
            && !profile.overDeclaredGridDoc)
        {
            LayoutMetricTableBlock(tb, block, flow, profile, doc, docFontDict, css, options, marginLeft, marginTop, marginBottom, pageWidth, pageHeight, tableAfterText, tableAfterSpacer, tfm);
            return false;
        }
        return true;
    }
}
