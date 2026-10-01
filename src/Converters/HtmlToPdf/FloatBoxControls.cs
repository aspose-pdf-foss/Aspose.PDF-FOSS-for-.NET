using System;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // Form controls (probed on the WorkflowGen form): a control's layout box is the CSS width by
    // the font's line plus its padding and the UA chrome, seated on the line like an inline
    // block; the page carries an AcroForm widget over that box and no control ink of its own, so
    // the widget's appearance - a 1 pt frame half a point inside the rect, the value in Helvetica
    // at the CSS size, a radio's ring and dot - is drawn here as page content as well.
    private const double FbUaInputPadPx = 2.0;                // a text input's UA padding
    private const double FbUaInputBorderPx = 2.0;             // …and border
    private const double FbUaTextAreaBorderPx = 1.0;          // a textarea's UA border
    private const int FbUaTextAreaRows = 2;                   // a textarea's UA rows
    private const int FbUaTextColumns = 20;                   // a text control's UA size
    private const double FbUaTextColumnEm = 0.5;              // …one column of it
    private const double FbUaCheckBoxPx = 13.0;               // a radio / checkbox box
    private const double FbUaRadioMarginTopPx = 3.0;          // the UA radio margins (top right bottom left)
    private const double FbUaRadioMarginRightPx = 3.0;
    private const double FbUaRadioMarginLeftPx = 5.0;
    private const double FbUaCheckMarginBottomPx = 3.0;
    private const double FbUaCheckMarginLeftPx = 4.0;
    private const double FbInputWidgetOverhangPt = 0.75;      // the widget rect past the box, top and bottom
    private const double FbTextAreaWidgetOverhangPt = 0.75;   // …the same for a textarea
    private const double FbWidgetFrameInsetPt = 0.5;          // the 1 pt appearance frame
    private const double FbWidgetFrameWidthPt = 1.0;
    private const string FbWidgetFontName = "Helvetica";      // the widgets' appearance face
    private const double FbRadioRingInsetPt = 0.5;            // the ring radius is half the box height less this
    private const double FbRadioDotRadiusPt = 2.0;

    private enum FbControlKind { Text, TextArea, Radio, Checkbox }

    private sealed class FbControl
    {
        public FbControlKind Kind;
        public string Name = "", Value = "";
        public bool Checked, ReadOnly;
        public double Overhang;                               // the widget rect past the box, top and bottom
    }

    /// <summary>A control as one item of the run, or null for a hidden or unsupported one.</summary>
    private static FbItem? FbControlItem(FbState fb, FbBox parent, HtmlNode el, FbStyle st, FbBoxProps p)
    {
        var type = el.Tag == "textarea" ? "textarea" : (FbAttr(el, "type") ?? "text").Trim().ToLowerInvariant();
        if (type is "hidden" or "submit" or "button" or "reset" or "image" or "file") return null;
        if (el.Tag is "select" or "button") return null;
        var ctl = new FbControl { Name = FbAttr(el, "name") ?? FbAttr(el, "id") ?? "", Value = FbAttr(el, "value") ?? "" };
        ctl.ReadOnly = el.Attrs is not null && el.Attrs.ContainsKey("readonly");
        ctl.Checked = el.Attrs is not null && el.Attrs.ContainsKey("checked");
        double w, h, above;
        var line = FbLineBoxPx(fb, st) * FbPxPt;
        switch (type)
        {
            case "radio":
            case "checkbox":
                ctl.Kind = type == "radio" ? FbControlKind.Radio : FbControlKind.Checkbox;
                if (!p.MarginDeclared) FbUaCheckMargins(p, type == "radio");
                w = p.WidthAuto ? FbUaCheckBoxPx * FbPxPt : p.WidthPt;
                h = p.HeightAuto ? FbUaCheckBoxPx * FbPxPt : p.HeightPt;
                above = h;
                break;
            case "textarea":
            {
                ctl.Kind = FbControlKind.TextArea;
                ctl.Value = DomText(el, fb.css);
                var rows = FbAttr(el, "rows") is { } rs && int.TryParse(rs.Trim(), out var rn) && rn > 0 ? rn : FbUaTextAreaRows;
                var padV = p.PadDeclared ? p.Pad.V : 2 * FbUaInputPadPx * FbPxPt;
                var borderV = p.HasVisibleBorder ? p.Border.V : 2 * FbUaTextAreaBorderPx * FbPxPt;
                w = p.WidthAuto ? FbUaTextColumns * FbUaTextColumnEm * st.Pt : p.WidthPt;
                h = rows * line + padV + borderV;
                above = h;
                ctl.Overhang = FbTextAreaWidgetOverhangPt;
                break;
            }
            default:
            {
                ctl.Kind = FbControlKind.Text;
                var padT = p.PadDeclared ? p.Pad.T : FbUaInputPadPx * FbPxPt;
                var padB = p.PadDeclared ? p.Pad.B : FbUaInputPadPx * FbPxPt;
                var borderT = p.HasVisibleBorder ? p.Border.T : FbUaInputBorderPx * FbPxPt;
                var borderB = p.HasVisibleBorder ? p.Border.B : FbUaInputBorderPx * FbPxPt;
                w = p.WidthAuto ? FbUaTextColumns * FbUaTextColumnEm * st.Pt : p.WidthPt;
                h = line + padT + padB + borderT + borderB;
                above = borderT + padT + FbAbovePt(fb, st);
                ctl.Overhang = FbInputWidgetOverhangPt;
                break;
            }
        }
        var props = new FbBoxProps { Margin = p.Margin, WidthAuto = false, WidthPt = w, HeightAuto = false, HeightPt = h };
        var box = new FbBox { Node = el, St = st, P = props, X = p.Margin.L, Y = p.Margin.T, W = w, H = h, IsInline = true, IsBfc = true, Ctl = ctl };
        parent.Kids.Add(box);
        var topAligned = ctl.Kind is FbControlKind.Text or FbControlKind.TextArea;
        return new FbItem { Box = box, Adv = box.OuterW, Above = above + p.Margin.T, Below = box.OuterH - above - p.Margin.T, St = st, VAlignTop = topAligned };
    }

    private static string? FbAttr(HtmlNode el, string name) => el.Attrs is not null && el.Attrs.TryGetValue(name, out var v) ? v : null;

    private static void FbUaCheckMargins(FbBoxProps p, bool radio)
    {
        p.Margin.T = FbUaRadioMarginTopPx * FbPxPt;
        p.Margin.R = FbUaRadioMarginRightPx * FbPxPt;
        p.Margin.B = radio ? 0 : FbUaCheckMarginBottomPx * FbPxPt;
        p.Margin.L = (radio ? FbUaRadioMarginLeftPx : FbUaCheckMarginLeftPx) * FbPxPt;
    }

    /// <summary>Paint a control at its seated box: the widget's appearance as page content, and
    /// the AcroForm field over the widget rect.</summary>
    private static void FbPaintControl(FbState fb, FbBox box)
    {
        var ctl = box.Ctl!;
        double wx = box.X, wtop = box.Y - ctl.Overhang, ww = box.W, wh = box.H + 2 * ctl.Overhang;
        var (page, py) = FbPageOf(fb, wtop);
        // (the widget's appearance draws the value at the field's default appearance; the frame
        //  and a radio's ring and dot are page content)
        if (ctl.Kind is FbControlKind.Text or FbControlKind.TextArea)
        {
            FbStrokeRect(fb, wx + FbWidgetFrameInsetPt, wtop + FbWidgetFrameInsetPt, ww - 2 * FbWidgetFrameInsetPt, wh - 2 * FbWidgetFrameInsetPt, "0 0 0", FbWidgetFrameWidthPt);
            FbAddTextField(fb, page, py, ctl, wx, ww, wh, box.St.Pt);
            return;
        }
        var r = box.H / 2 - FbRadioRingInsetPt;
        var cx = box.X + box.H / 2;
        var cy = box.Y + box.H / 2;
        if (ctl.Kind == FbControlKind.Radio)
        {
            FbCircle(fb, cx, cy, r, "0 0 0", false);
            if (ctl.Checked) FbCircle(fb, cx, cy, FbRadioDotRadiusPt, "0 0 0", true);
            FbAddRadioOption(fb, page, py, ctl, wx, ww, wh);
        }
        else FbStrokeRect(fb, box.X + FbWidgetFrameInsetPt, box.Y + FbWidgetFrameInsetPt, box.W - 2 * FbWidgetFrameInsetPt, box.H - 2 * FbWidgetFrameInsetPt, "0 0 0", FbWidgetFrameWidthPt);
    }

    private static Rectangle FbWidgetRect(FbState fb, double py, double wx, double ww, double wh) => new(wx, fb.pageH - py - wh, wx + ww, fb.pageH - py);

    private static void FbAddTextField(FbState fb, int page, double py, FbControl ctl, double wx, double ww, double wh, double fontPt)
    {
        try
        {
            var pg = fb.pageObjs[page];
            var field = new Forms.TextBoxField(pg, FbWidgetRect(fb, py, wx, ww, wh))
            {
                Multiline = ctl.Kind == FbControlKind.TextArea,
                ReadOnly = ctl.ReadOnly && ctl.Kind == FbControlKind.Text,
            };
            if (ctl.Name.Length > 0) field.PartialName = ctl.Name;
            if (ctl.Value.Length > 0) field.Value = ctl.Value;
            field.DefaultAppearance = new Annotations.DefaultAppearance(FbWidgetFontName, fontPt);
            fb.doc.Form.Add(field, pg.Number);
        }
        catch { /* best-effort widget emission: the appearance is already page content */ }
    }

    private static void FbAddRadioOption(FbState fb, int page, double py, FbControl ctl, double wx, double ww, double wh)
    {
        try
        {
            var pg = fb.pageObjs[page];
            var key = ctl.Name.Length > 0 ? ctl.Name : "radio";
            if (!fb.radioGroups.TryGetValue(key, out var group))
            {
                group = (new Forms.RadioButtonField(pg), 0);
                fb.doc.Form.Add(group.field, pg.Number);
            }
            var opt = new Forms.RadioButtonOptionField(pg, FbWidgetRect(fb, py, wx, ww, wh))
            {
                Style = Forms.BoxStyle.Circle,
                OptionName = key + "_" + group.options,
            };
            group.field.Add(opt);
            if (ctl.Checked) group.field.Selected = group.options;
            fb.radioGroups[key] = (group.field, group.options + 1);
        }
        catch { /* best-effort widget emission: the appearance is already page content */ }
    }
}
