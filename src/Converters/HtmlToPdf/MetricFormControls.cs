using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The form controls a UA form cell holds (see HtmlDocProfile.uaFormCells): a text input as a
// replaced box with its AcroForm widget, a leading checkbox with its own inline margin, and an
// absolutely positioned div whose text draws at the page margin box's offset.
internal static partial class HtmlToPdfConverter
{
    /// <summary>The hard line break the cell text carries for a `br`.</summary>
    private const char MetricHardBreakChar = '\u0001';

    /// <summary>The 1 px border of the inline box a remote image's alt text stands in (measured
    /// on the test request: the logo row is 15 px tall around a 13 px line).</summary>
    private const double UaAltBoxBorderPt = 0.75;

    private static readonly HashSet<string> TextLikeInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "password", "email", "tel", "url", "number", "search", "date", "datetime-local", "month", "week", "time",
    };

    /// <summary>An input in a cell. A checkbox that opens the cell is an inline REPLACED box: a
    /// 13 px box on the line's baseline, its AcroForm widget one point inside it, and nothing
    /// drawn in the page content - only a checkbox that opens the cell is modelled, the corpus
    /// states no other shape on this path; in a UA form cell its own inline `margin-left` is the
    /// lead before the box (measured: `margin-left: 20px` seats the box 15 pt into the cell). A
    /// text-like input in a UA form cell is a replaced box too (see <see cref="MetricInputBox"/>).</summary>
    private static void OpenMetricInput(MetricTableState mt, Token tok)
    {
        if (mt.mps.cell is not { } cell || tok.Attributes is not { } attrs) return;
        var type = attrs.TryGetValue("type", out var t) && t is not null ? t.Trim().ToLowerInvariant() : "text";
        if (type == "checkbox")
        {
            if (cell.LeadCheckboxName is not null || !MetricTextIsBlank(mt.text)) return;
            cell.LeadCheckboxName = attrs.TryGetValue("name", out var cbName) && cbName.Trim().Length > 0 ? cbName.Trim() : "";
            cell.LeadCheckboxChecked = attrs.ContainsKey("checked");
            if (mt.mps.uaFormCells && attrs.TryGetValue("style", out var cbSt) && cbSt is not null
                && Regex.Match(cbSt, @"(?<![-\w])margin-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mlM
                && TryParseLength(mlM.Groups[1].Value.Trim()) is { } mlPt && mlPt >= 0)
                cell.LeadCheckboxMarginLeftPt = mlPt;
            return;
        }
        // A button-family input in a UA grid cell is its LABEL: the value in the control face (Arial) at
        // the size and weight its class rule states (probed: `.cssInput { font-size: 11px; font-weight:
        // bold }` draws 'Col1' ArialBold 8.25 centred in its th, no chrome, no fill).
        if (mt.stdSerif && type is "submit" or "button" or "reset")
        {
            if (!attrs.TryGetValue("value", out var label) || label is null || label.Trim().Length == 0) return;
            var lbag = MetricControlStyleBag(mt, "input", attrs);
            if (MetricTextIsBlank(mt.text))
            {
                cell.Face = "Arial";
                if (lbag.TryGetValue("font-size", out var lfs) && TryParseCssFontSize(lfs.Trim()) is { } lfsPt && lfsPt > 0)
                { cell.FontSize = lfsPt; cell.FontFromClass = true; }
                if (lbag.TryGetValue("font-weight", out var lfw)) cell.Bold = IsBoldFontWeight(lfw);
            }
            mt.text.Append(DecodeEntities(label));
            return;
        }
        if (!mt.mps.uaFormCells || !TextLikeInputTypes.Contains(type)) return;
        var box = new MetricInputBox
        {
            Name = attrs.TryGetValue("name", out var n) && n is not null ? n.Trim() : "",
            Value = attrs.TryGetValue("value", out var v) && v is not null ? DecodeEntities(v) : "",
            AfterText = !MetricTextIsBlank(mt.text),
        };
        var bag = MetricControlStyleBag(mt, "input", attrs);
        ReadMetricControlWidth(box, bag);
        SizeMetricControlBox(box, bag, attrs, cell.Face ?? mt.face);
        (cell.InputBoxes ??= new List<MetricInputBox>()).Add(box);
    }

    /// <summary>A textarea in a UA form cell: a replaced box like a text input, its width and
    /// height the sheet's (measured on the test request: a `width: 95%; height: 60px` comment
    /// box draws a 356.25 × 46.5 widget in a 375 pt cell), its markup body no cell text.</summary>
    private static void OpenMetricTextArea(MetricTableState mt, Token tok)
    {
        if (!mt.mps.uaFormCells || mt.mps.cell is not { } cell || tok.Attributes is not { } attrs) return;
        var box = new MetricInputBox
        {
            Name = attrs.TryGetValue("name", out var n) && n is not null ? n.Trim() : "",
            AfterText = !MetricTextIsBlank(mt.text),
            Multiline = true,
        };
        var bag = MetricControlStyleBag(mt, "textarea", attrs);
        ReadMetricControlWidth(box, bag);
        SizeMetricControlBox(box, bag, attrs, cell.Face ?? mt.face);
        (cell.InputBoxes ??= new List<MetricInputBox>()).Add(box);
        // the body between the tags is the control's value, never the cell's text
        if (!tok.IsSelfClosing) { mt.mps.hiddenTag = "textarea"; mt.mps.hiddenDepth = 1; }
    }

    /// <summary>A select in a pt form cell: a replaced box like a text input - its width the sheet's
    /// (140 px -> 105), its box the face's UNROUNDED line plus the select chrome, advancing its width
    /// plus the select border and its class's horizontal padding, its text the chosen option (probed:
    /// a `width:140px` class select lays out 110.25 x 12.656 in an 8 pt Tahoma cell; 106.5 unclassed).</summary>
    private static void OpenMetricSelect(MetricTableState mt, Token tok)
    {
        if (!mt.mps.ptFormCells || mt.mps.cell is not { } cell || tok.Attributes is not { } attrs) return;
        var box = new MetricInputBox
        {
            Name = attrs.TryGetValue("name", out var n) && n is not null ? n.Trim() : "",
            AfterText = !MetricTextIsBlank(mt.text),
            Select = true,
        };
        var bag = MetricControlStyleBag(mt, "select", attrs);
        ReadMetricControlWidth(box, bag);
        SizeMetricControlBox(box, bag, attrs, cell.Face ?? mt.face);
        var selFace = box.Face ?? cell.Face ?? mt.face;
        box.BoxHPt = box.FontSize * (HheaLineSumFor(selFace) ?? WinMetricsFor(selFace)?.sum ?? Table.CssNormalLineHeight) + PtFormSelectBoxChromePt;
        box.WidgetHPt = box.BoxHPt + PtFormSelectFillOverhangPt;
        var padH = 0.0;
        if (bag.TryGetValue("padding-left", out var pdl) && TryParseLength(pdl.Trim()) is { } pdlPt && pdlPt > 0) padH += pdlPt;
        if (bag.TryGetValue("padding-right", out var pdr) && TryParseLength(pdr.Trim()) is { } pdrPt && pdrPt > 0) padH += pdrPt;
        box.AdvanceExtraPt = PtFormSelectBorderPt + padH;
        box.Value = SelectedOptionText(mt.tableHtml, box.Name);
        (cell.InputBoxes ??= new List<MetricInputBox>()).Add(box);
        // the options between the tags are the control's, never the cell's text
        if (!tok.IsSelfClosing) { mt.mps.hiddenTag = "select"; mt.mps.hiddenDepth = 1; }
    }

    /// <summary>The select's box past its unrounded line: 1 px border and 1 px padding above and below.</summary>
    private const double PtFormSelectBoxChromePt = 3.0;
    /// <summary>The select's white fill stands one px above and below its layout box.</summary>
    private const double PtFormSelectFillOverhangPt = 2.0;
    /// <summary>What the select advances past its width for its border, both sides.</summary>
    private const double PtFormSelectBorderPt = 1.5;
    /// <summary>The select's fill starts this much inside its layout left and is this much narrower
    /// than the layout width on the right too (probed: fill 104.5 wide at +0.25 for a 105 box).</summary>
    private const double PtFormSelectFillInsetPt = 0.25;
    /// <summary>The chosen option's text starts this far inside the fill; its line box seats this far
    /// under the fill's top (probed: baseline 9.98 under the fill top at 8 pt Tahoma).</summary>
    private const double PtFormSelectTextInsetPt = 2.0;

    /// <summary>The text of the option a select shows: its `selected` option, else its first.</summary>
    private static string SelectedOptionText(string tableHtml, string name)
    {
        var sel = name.Length > 0
            ? Regex.Match(tableHtml, @"<select\b[^>]*\bname\s*=\s*[""']?" + Regex.Escape(name) + @"[""'\s>][^>]*>(?<b>.*?)</select\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            : Regex.Match(tableHtml, @"<select\b[^>]*>(?<b>.*?)</select\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!sel.Success) return "";
        var options = Regex.Matches(sel.Groups["b"].Value, @"<option\b(?<a>[^>]*)>(?<t>[^<]*)", RegexOptions.IgnoreCase);
        if (options.Count == 0) return "";
        foreach (Match o in options)
            if (Regex.IsMatch(o.Groups["a"].Value, @"\bselected\b", RegexOptions.IgnoreCase))
                return CollapseWs(DecodeEntities(o.Groups["t"].Value)).Trim();
        return CollapseWs(DecodeEntities(options[0].Groups["t"].Value)).Trim();
    }

    /// <summary>The style a form control resolves to: the sheet's element rule, its class rules
    /// (bare and element-qualified) and its inline style, each later declaration winning.</summary>
    private static Dictionary<string, string> MetricControlStyleBag(MetricTableState mt, string tag, Dictionary<string, string> attrs)
    {
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Merge(Dictionary<string, string>? rule)
        {
            if (rule is null) return;
            foreach (var kv in rule) bag[kv.Key] = kv.Value;
        }
        if (mt.css.TryGetValue(tag, out var tagRule)) Merge(tagRule);
        if (attrs.TryGetValue("class", out var cls) && cls is not null)
            foreach (var cn in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (mt.css.TryGetValue("." + cn, out var bare)) Merge(bare);
                if (mt.css.TryGetValue(tag + "." + cn, out var qualified)) Merge(qualified);
            }
        if (attrs.TryGetValue("style", out var st) && st is not null)
            foreach (Match d in StyleDeclRx.Matches(st))
                bag[d.Groups[1].Value.Trim()] = d.Groups[2].Value.Trim();
        return bag;
    }

    /// <summary>A control's declared width: a percent of the cell's content box, or an absolute
    /// length (the widget is exactly that wide - measured: 284 px inputs draw 213.0 widgets).</summary>
    private static void ReadMetricControlWidth(MetricInputBox box, Dictionary<string, string> bag)
    {
        if (!bag.TryGetValue("width", out var w)) return;
        w = w.Trim();
        var frac = PercentFraction(w);
        if (frac > 0) box.WidthFrac = frac;
        else if (TryParseLength(w) is { } wPt && wPt > 0) box.WidthPt = wPt;
    }

    /// <summary>The UA text control's chrome, in px: a 2 px inset border on every side, a 1 px
    /// padding above and below and none beside (probed: Verdana 8 pt inputs draw 21 px widgets
    /// on 13 px lines; `padding: 0` narrows the box by 2 px and widens nothing).</summary>
    private const double UaControlBorderPx = 2.0;
    private const double UaControlPaddingVPx = 1.0;
    private const double UaControlPaddingHPx = 0.0;
    /// <summary>The widget rect overhangs the control's box by one px above and below (probed:
    /// the widget spans the cell's padding box exactly where the box spans its content).</summary>
    private const double UaControlWidgetOverhangPt = 0.75;
    /// <summary>What a percent-wide control's column ask adds to its intrinsic box before the
    /// percentage (probed: pct x 133.6 for the 118.54 box - the control's border, padding and
    /// margin on both sides).</summary>
    private const double UaPercentControlChromePt = 15.0;

    /// <summary>The UA default control font where the sheet states none: 10 pt in the sans face
    /// (probed: a body-only font is not inherited; the widget is 118.54 × 17.25, DA 10).</summary>
    private const double UaControlDefaultFontPt = 10.0;
    private const string UaControlDefaultFace = "Arial";
    /// <summary>The UA text control's `size` where the markup states none.</summary>
    private const int UaControlDefaultColumns = 20;

    /// <summary>A control's intrinsic column, in em of its own font (probed on the licensed
    /// converter: `size` × this per face, glyph-independent - 4.952 / 4.622 / 4.457 pt per
    /// column at 8 pt; no font metric names it, so it is the per-face constant it measures).</summary>
    private static double UaControlColumnEm(string face) => face.ToLowerInvariant() switch
    {
        "verdana" => 0.6190,
        "tahoma" => 0.54383,
        "times new roman" or "times" => 0.5571,
        _ => 0.5777,
    };

    /// <summary>A control's box from its style: its font (the sheet's, else the UA control
    /// font), its line box (a declared line-height, else the face's px-rounded line), the
    /// border and paddings around it (a declared `height` is the whole box), its margins, the
    /// widget one px larger above and below, and its advance in the line - the widget plus its
    /// border and horizontal padding again plus the margins (probed: a 284 px inset input
    /// advances 288 px; `padding: 0 10px` adds 3 + 15; a 10 px margin adds 15). A width-less
    /// control's widget is its `size` columns at the face's column em plus that chrome.</summary>
    private static void SizeMetricControlBox(MetricInputBox box, Dictionary<string, string> bag, Dictionary<string, string> attrs, string cellFace)
    {
        var fs = bag.TryGetValue("font-size", out var fsDecl) && TryParseCssFontSize(fsDecl.Trim()) is { } fsPt && fsPt > 0
            ? fsPt : UaControlDefaultFontPt;
        var face = bag.TryGetValue("font-family", out var famDecl) && FirstFontFamily(famDecl) is { } fam
            && WinMetricsFor(fam) is not null ? fam
            : bag.ContainsKey("font-family") ? cellFace : UaControlDefaultFace;
        box.FontSize = fs;
        box.Face = face;
        double lineBox;
        if (bag.TryGetValue("line-height", out var lh) && MetricControlLineHeightPt(lh.Trim(), fs) is { } lhPt) lineBox = lhPt;
        else lineBox = MetricLineHeight(fs, HheaLineSumFor(face) ?? WinMetricsFor(face)?.sum ?? Table.CssNormalLineHeight);
        var border = UaControlBorderPx * PxPt;
        if (bag.TryGetValue("border-style", out var bst) && Regex.IsMatch(bst, @"^\s*(none|hidden)\s*$", RegexOptions.IgnoreCase)) border = 0;
        else if (bag.TryGetValue("border", out var bsh) && CssBorderShorthand(bsh.Trim()) is { } side) border = side.W;
        else if (bag.TryGetValue("border-width", out var bwd) && CssBorderWidthOf(bwd.Trim()) is { } bwPt) border = bwPt;
        var padV = UaControlPaddingVPx * PxPt;
        var padH = UaControlPaddingHPx * PxPt;
        if (bag.TryGetValue("padding", out var pd) && CssSideValues(pd.Trim()) is { } pads)
        {
            padV = IsZeroLength(pads[0]) ? 0 : TryParseLength(pads[0]) ?? padV;
            padH = IsZeroLength(pads[1]) ? 0 : TryParseLength(pads[1]) ?? padH;
        }
        // A one-sided padding (the request form's `padding-left: 5px` class on its inputs) is
        // spent on that side alone: half of it in the symmetric chrome below (probed: the size=30
        // input paints 130.518 + 3 + 3.75 and advances 3 + 3.75 past that again).
        if (bag.TryGetValue("padding-left", out var pdl) && TryParseLength(pdl.Trim()) is { } pdlPt && pdlPt > 0) padH += pdlPt / 2;
        if (bag.TryGetValue("padding-right", out var pdr) && TryParseLength(pdr.Trim()) is { } pdrPt && pdrPt > 0) padH += pdrPt / 2;
        box.BoxHPt = bag.TryGetValue("height", out var hd) && TryParseLength(hd.Trim()) is { } hPt && hPt > 0
            ? hPt : lineBox + 2 * (border + padV);
        box.WidgetHPt = box.BoxHPt + 2 * UaControlWidgetOverhangPt;
        if (bag.TryGetValue("margin", out var mg) && TryParseLength(mg.Trim()) is { } mgPt && mgPt > 0) box.MarginPt = mgPt;
        box.AdvanceExtraPt = 2 * (border + padH + box.MarginPt);
        var columns = attrs.TryGetValue("size", out var sz) && int.TryParse(sz?.Trim(), out var szN) && szN > 0 ? szN : UaControlDefaultColumns;
        box.IntrinsicWidthPt = columns * UaControlColumnEm(face) * fs + 2 * (border + padH);
        if (box.WidthPt <= 0 && box.WidthFrac <= 0) box.WidthPt = box.IntrinsicWidthPt;
    }

    /// <summary>The advance of a UA text control the sheet leaves unstyled: `size` columns (20
    /// where the markup states none) in the UA control face and size plus the control's border
    /// and padding, and that chrome once more past the box (probed: 118.54 + 3 per input in the
    /// verification row - the next cell's text starts 3 past the widget).</summary>
    private static double UaTextControlAdvancePt(Dictionary<string, string> attrs)
    {
        var columns = attrs.TryGetValue("size", out var sz) && int.TryParse(sz?.Trim(), out var n) && n > 0 ? n : UaControlDefaultColumns;
        return columns * UaControlColumnEm(UaControlDefaultFace) * UaControlDefaultFontPt + 2 * UaTextControlChromePt;
    }

    /// <summary>The UA text control's border and padding, both sides - what its box adds to its
    /// columns and what its advance adds again past the box.</summary>
    private static double UaTextControlChromePt => 2 * (UaControlBorderPx + UaControlPaddingHPx) * PxPt;

    /// <summary>A nested grid's own declared width in absolute units - its width attribute, an
    /// inline `width`, or a class rule's - in pt; null when it declares none (or a percent).</summary>
    private static double? NestedGridDeclaredWidthPt(MetricTableState mt, string tableHtml)
    {
        var tag = Regex.Match(tableHtml, @"<table\b([^>]*)>", RegexOptions.IgnoreCase);
        if (!tag.Success) return null;
        var attrs = tag.Groups[1].Value;
        var st = DivStyleOf(tag.Value);
        if (st.Length > 0 && Regex.Match(st, @"(?<![-\w])width\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } sw)
            return TryParseLength(sw.Groups[1].Value.Trim());
        var wa = Regex.Match(attrs, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)(?:px)?[""'\s>]", RegexOptions.IgnoreCase);
        if (wa.Success) return DtpNum(wa.Groups[1].Value) * PxPt;
        var cls = Regex.Match(attrs, @"\bclass\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (cls.Success)
            foreach (var cn in cls.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (mt.css.TryGetValue("." + cn, out var rule) && rule.TryGetValue("width", out var cw)
                    && TryParseLength(cw.Trim()) is { } cwPt)
                    return cwPt;
        return null;
    }

    /// <summary>A control's `line-height`: a percent or plain number of its font size, or a length.</summary>
    private static double? MetricControlLineHeightPt(string decl, double fs)
    {
        if (decl.EndsWith('%') && double.TryParse(decl.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return pct / 100.0 * fs;
        if (double.TryParse(decl, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return n * fs;
        return TryParseLength(decl);
    }

    /// <summary>The line box a cell's controls stand in: the tallest control box with its margins.</summary>
    private static double MetricInputsLineBoxPt(MetricCell mc)
    {
        var h = 0.0;
        if (mc.InputBoxes is null) return h;
        foreach (var box in mc.InputBoxes) h = Math.Max(h, box.BoxHPt + 2 * box.MarginPt);
        return h;
    }

    /// <summary>A leading checkbox's margin box: its lead (the UA 3 pt, or the box's own inline
    /// margin-left), the 13 px box and the block margin after it.</summary>
    private static double MetricCheckboxMarginBoxPt(MetricCell mc)
        => (mc.LeadCheckboxMarginLeftPt ?? Table.UaCheckboxLeadPt) + Table.UaCheckboxBoxPt + Table.UaCheckboxMarginBlockPt;

    /// <summary>The unwrapped width a cell's text inputs ask for: the intrinsic 20-column box
    /// each (a percent width resolves against the column once it is known), after the last
    /// line's text and a space when ink precedes the input on its line.</summary>
    private static double MetricInputsMaxContentPt(MetricTableState mt, MetricCell mc, List<MetricInputBox> inputs)
    {
        var fs = mc.FontSize ?? mt.mps.fontSize;
        var w = 0.0;
        foreach (var box in inputs)
        {
            // A percent-wide control asks its column for that share of its intrinsic box plus the
            // control chrome (probed: 50 % → 66.8, 25 % → 33.4, 100 % → 133.6 of max-content for
            // the 118.54 intrinsic box); an absolute or plain one for its box and margins.
            var bw = box.WidthFrac > 0
                ? box.WidthFrac * (box.IntrinsicWidthPt + UaPercentControlChromePt)
                : (box.WidthPt > 0 ? box.WidthPt : box.IntrinsicWidthPt) + box.AdvanceExtraPt;
            if (box.AfterText && mc.Text.Length > 0)
                bw += MeasureFaceText(CellFaceName(mt.face, mt.boldFace, mc), BrLineMeasureText(LastBrSegment(mc.Text)) + " ", fs);
            w = Math.Max(w, bw);
        }
        return w + CellPadLeftExtra(mc, mt.p) + CellPadRightExtra(mc, mt.p);
    }

    /// <summary>The cell text after its last hard line break.</summary>
    private static string LastBrSegment(string text)
    {
        var i = text.LastIndexOf(MetricHardBreakChar);
        return i < 0 ? text : text[(i + 1)..];
    }

    /// <summary>The cell's text inputs: each a box <c>UaTextInputBoxEm</c> tall, centred in
    /// the cell's own content band, its width the inline percent of the cell's content box or
    /// the intrinsic <c>UaTextInputIntrinsicEm</c>; the AcroForm text field is the widget,
    /// and its 1 pt black rule runs half a point inside the widget rect (measured on the
    /// worksheet: 90 % inputs 131.74 wide in 146.38 content boxes, 17.24 tall, centred in the
    /// 27 pt band of their two-line label row; the stroke 291.69..422.43 around a 291.19..422.93
    /// widget).</summary>
    private static void RenderMetricCellInputs(MetricRowsState mr, double boxW, Page page, double cellTop)
    {
        if (mr.mc.InputBoxes is not { Count: > 0 } boxes) return;
        var padL = mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p;
        var padR = mr.mc.PadRight >= 0 ? mr.mc.PadRight : mr.p;
        var contentW = boxW - padL - padR;
        var x = mr.colX + mr.mc.BorderLeftW + padL;
        var innerH = mr.mc.ContentH - mr.mc.PadTopPt - mr.mc.PadBottomPt;
        foreach (var box in boxes)
        {
            // a sized control's widget is one px taller than its box on each side, centred on
            // the cell's content band (its box, when that is what paces the row: the widget
            // then overhangs the band by that px - measured: widgets at 75.0 under a 75.75 row)
            var h = box.WidgetHPt;
            var top = cellTop - (innerH - h) / 2;
            var w = box.WidthFrac > 0 ? box.WidthFrac * contentW : box.WidthPt;
            var bx = x + box.MarginPt;
            // (a right- or centre-aligned cell seats the box against its content edge / midway -
            // probed: the 50 % input under align=right ends at the cell's content right)
            if (mr.mc.Align == HorizontalAlignment.Right) bx = x + contentW - w;
            else if (mr.mc.Align == HorizontalAlignment.Center) bx = x + (contentW - w) / 2;
            if (box.AfterText && mr.mc.Lines.Length > 0)
                bx += MeasureFaceText(mr.mFace, mr.mc.Lines[^1] + " ", mr.cellFs);
            if (box.Select)
            {
                // the pt form's select: a white fill one px above and below its layout box, a
                // quarter-point inside its left, a 1 pt black rule half a point inside the fill,
                // the chosen option's text two points in (probed on the request form)
                var fillX = bx + PtFormSelectFillInsetPt;
                var fillW = w - 2 * PtFormSelectFillInsetPt;
                DrawBox(page, fillX, top - h, fillW, h, border: null, borderWidth: 0, fill: Color.FromArgb(255, 255, 255));
                DrawBox(page, fillX + UaInputBorderPt / 2, top - h + UaInputBorderPt / 2,
                    fillW - UaInputBorderPt, h - UaInputBorderPt,
                    border: Color.Black, borderWidth: UaInputBorderPt, fill: null);
                if (box.Value.Length > 0 && box.FontSize > 0)
                {
                    var selFace = box.Face ?? mr.mFace;
                    var selAsc = WinMetricsFor(selFace) is { } selFm ? selFm.asc : mr.fm.asc;
                    EmitCellLineRuns(page, mr.fontRes, box.FontSize, fillX + PtFormSelectTextInsetPt,
                        top - PtFormSelectTextInsetPt - selAsc * box.FontSize, box.Value, selFace);
                }
                x += w + box.AdvanceExtraPt;
                continue;
            }
            var field = new Forms.TextBoxField(page, new Rectangle(bx, top - h, bx + w, top));
            if (box.Name.Length > 0) field.PartialName = box.Name;
            if (box.Multiline) field.Multiline = true;
            if (box.Value.Length > 0) field.Value = box.Value;
            mr.doc.Form.Add(field, page.Number);
            // the value sets in the control's own font at its own size
            if (box.FontSize > 0)
                field.DefaultAppearance = new Annotations.DefaultAppearance(box.Face ?? mr.mFace, box.FontSize, System.Drawing.Color.Black);
            DrawBox(page, bx + UaInputBorderPt / 2, top - h + UaInputBorderPt / 2,
                w - UaInputBorderPt, h - UaInputBorderPt,
                border: Color.Black, borderWidth: UaInputBorderPt, fill: null);
            x += w + box.AdvanceExtraPt;
        }
    }

    /// <summary>A cell's absolutely positioned div (`position: absolute` with pixel `left`/`top`,
    /// its margins added) is out of the cell's flow: its text is captured for the page, and
    /// nothing of it - text, breaks, the divs nested in it - reaches the cell. True when the
    /// div opened such a capture.</summary>
    private static bool TryOpenMetricAbsText(MetricTableState mt, Token tok)
    {
        if (tok.Attributes is not { } a || !a.TryGetValue("style", out var st) || st is null) return false;
        if (!Regex.IsMatch(st, @"(?<![-\w])position\s*:\s*absolute", RegexOptions.IgnoreCase)) return false;
        double? Px(string prop)
        {
            var m = Regex.Match(st, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)\s*px", RegexOptions.IgnoreCase);
            return m.Success ? DtpNum(m.Groups[1].Value) * PxPt : null;
        }
        var left = Px("left");
        var top = Px("top");
        if (left is null && top is null) return false;
        mt.mps.absCapture = new MetricAbsText
        {
            LeftPt = (left ?? 0) + (Px("margin-left") ?? 0),
            TopPt = (top ?? 0) + (Px("margin-top") ?? 0),
        };
        mt.mps.absDepth = 1;
        return true;
    }

    /// <summary>A div closes inside a captured absolute div: the outermost close ends the
    /// capture and hands the text to the cell.</summary>
    private static void CloseMetricAbsText(MetricTableState mt)
    {
        if (--mt.mps.absDepth > 0) return;
        var abs = mt.mps.absCapture!;
        mt.mps.absCapture = null;
        if (mt.mps.cell is { } cell && abs.Text.ToString().Trim().Length > 0)
            (cell.AbsTexts ??= new List<MetricAbsText>()).Add(abs);
    }

    /// <summary>The cell's absolutely positioned texts draw at their offsets from the page
    /// margin box - the sheet's margin corner, inside which the UA body inset sits - in the
    /// cell's face and size (measured on the worksheet: `left: 414.38px; top: 112.72px;
    /// margin-top: -34px` seats its line at 90 + 310.79 across and 72 + 59.04 down).</summary>
    private static void RenderMetricCellAbsTexts(MetricRowsState mr, Page page)
    {
        if (mr.mc.AbsTexts is not { Count: > 0 } texts) return;
        var drop = CellDropOf(mr.mps, mr.stdSerif, mr.fm, mr.mc, mr.cellFs, mr.cellLineH);
        foreach (var abs in texts)
        {
            var x = mr.mps.pageMarginLeft + abs.LeftPt;
            var top = mr.pageHeight - (mr.mps.pageMarginTop + abs.TopPt);
            var text = CollapseWs(abs.Text.ToString()).Trim();
            if (text.Length > 0) EmitCellLineRuns(page, mr.fontRes, mr.cellFs, x, top - drop, text, mr.mFace);
        }
    }

    /// <summary>A table tag's `border` shorthand, spelled out into the longhands the frame
    /// reads: its length is the width, its style keyword the style, the rest its colour
    /// (a colour's own spaces - `rgb(204, 204, 204)` - stay inside it).</summary>
    private static void ExpandBorderShorthand(string shorthand, Dictionary<string, string> decl)
    {
        var rest = shorthand;
        var styleM = Regex.Match(rest, @"(?<![-\w])(none|hidden|solid|dashed|dotted|double|groove|ridge|inset|outset)\b", RegexOptions.IgnoreCase);
        if (!styleM.Success) return;
        decl["border-style"] = styleM.Value;
        rest = rest.Remove(styleM.Index, styleM.Length);
        var widthM = Regex.Match(rest, @"(?<![-\w.])\d*\.?\d+\s*(px|pt|em|mm|cm|in)\b", RegexOptions.IgnoreCase);
        if (widthM.Success)
        {
            decl["border-width"] = widthM.Value;
            rest = rest.Remove(widthM.Index, widthM.Length);
        }
        var colour = rest.Trim();
        if (colour.Length > 0) decl["border-color"] = colour;
    }

    /// <summary>A column declared at least as wide as the table's usable span cannot have it: it
    /// takes what the other columns leave (measured on the worksheet: a 936 px column in a 600 pt
    /// grid draws 530.84 beside the 60.16 `Completed` column, and an 800 px title column beside
    /// a 200 px image column leaves the image column whole). True when that settled the
    /// overflow.</summary>
    private static bool YieldOverDeclaredColumns(MetricTableState mt, double fitW)
    {
        var usable = fitW - (mt.nCols + 1) * mt.s - mt.nCols * 2 * mt.p;
        var yielded = false;
        for (var c = 0; c < mt.nCols; c++)
        {
            if (!mt.colFixed[c] || mt.colPx[c] <= 0 || mt.colPx[c] < usable) continue;
            var others = 0.0;
            for (var o = 0; o < mt.nCols; o++) if (o != c) others += mt.colW[o];
            mt.colW[c] = Math.Max(0, usable - others);
            yielded = true;
        }
        if (!yielded) return false;
        mt.total = (mt.nCols + 1) * mt.s;
        foreach (var w in mt.colW) mt.total += w + 2 * mt.p;
        return mt.total <= fitW;
    }

    /// <summary>A data URI whose payload is a PNG (base64 `iVBORw0KGgo`) under another image
    /// type: the bytes decide what the picture is, not the label (the worksheet's `image/gif`
    /// logo is a PNG, drawn at its own pixel size).</summary>
    private static bool MislabelledPngDataUri(string src)
        => src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
           && !src.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase)
           && src.Contains("base64,iVBORw0KGgo", StringComparison.Ordinal);

    /// <summary>A text input in a UA form cell: a replaced inline box, its width a percent of the
    /// cell's content box or the intrinsic 20-column box, its widget the AcroForm text field.</summary>
    private sealed class MetricInputBox
    {
        public string Name = "";
        public string Value = "";
        /// <summary>An inline `width: N%` of the cell's content width (0 = none).</summary>
        public double WidthFrac;
        /// <summary>An inline absolute width, pt (0 = none).</summary>
        public double WidthPt;
        /// <summary>Ink precedes it on its line: it seats after the last line's text.</summary>
        public bool AfterText;
        /// <summary>A textarea: a multi-line text field.</summary>
        public bool Multiline;
        /// <summary>A select: a white box with its chosen option drawn as page text (pt form grids).</summary>
        public bool Select;
        /// <summary>The control's box in its line, pt (0 = the calibrated em box), and the
        /// widget one px taller above and below it.</summary>
        public double BoxHPt;
        public double WidgetHPt;
        /// <summary>The control's own margin on every side, pt (0 = none).</summary>
        public double MarginPt;
        /// <summary>What the control's advance in its line exceeds its widget by: its border and
        /// horizontal padding again, and its margins.</summary>
        public double AdvanceExtraPt;
        /// <summary>The widget a width-less control draws: its `size` columns at the face's column em
        /// plus its border and horizontal padding (a percent-wide control's max-content too).</summary>
        public double IntrinsicWidthPt;
        /// <summary>The control's font: the sheet's, else the UA control font (0 = unset).</summary>
        public double FontSize;
        public string? Face;
    }

    /// <summary>Text an absolutely positioned div holds: drawn at the page margin box's offset,
    /// out of the cell's flow.</summary>
    private sealed class MetricAbsText
    {
        public StringBuilder Text = new();
        public double LeftPt;
        public double TopPt;
    }
}
