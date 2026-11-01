using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The text controls of the margin-flow fragment renderer: a text input, a text area or a
// select inside a flow paragraph is a run of its own - it takes its box on the line and
// becomes a form field there, like the controls of a control line (ControlLineLayout.cs).
internal static partial class HtmlToPdfConverter
{
    /// <summary>A text control standing in a flow paragraph.</summary>
    internal sealed class FlowControl
    {
        /// <summary>What the control is: a text control (a text box, text area or select), a check
        /// box, a radio button or a push button whose caption is its value.</summary>
        public bool IsCheckBox, IsRadio, IsButton, Checked;
        public string? Name;
        public string Value = "";
        public bool ReadOnly;
        public bool Multiline;
        /// <summary>A text input's size attribute (0 = the UA 20 columns).</summary>
        public int Columns;
        /// <summary>A text area's box, from its rows and columns.</summary>
        public double AreaWidthPt, AreaHeightPt;
        /// <summary>A select's entries; null for a text control.</summary>
        public List<string>? Options;
    }

    private static readonly Regex FlowOptionRx = new(@"<option\b([^>]*)>([^<]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    private static string? FlowAttr(string tagStr, string name)
    {
        var m = SafeRegex.Match(tagStr, @"\b" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }

    private static int FlowIntAttr(string tagStr, string name) =>
        int.TryParse(FlowAttr(tagStr, name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;

    /// <summary>The control an input, textarea or select tag opens at <paramref name="i"/> (just past
    /// the tag), or null for an input that is no text box or a hidden control; a textarea's and a
    /// select's content is consumed.</summary>
    private static FlowControl? ParseFlowControl(string s, ref int i, string tag, string tagStr)
    {
        var hidden = SafeRegex.IsMatch(tagStr, @"display\s*:\s*none", RegexOptions.IgnoreCase);
        var control = new FlowControl
        {
            Name = FlowAttr(tagStr, "name") ?? FlowAttr(tagStr, "id"),
            ReadOnly = SafeRegex.IsMatch(tagStr, @"\s(disabled|readonly)\b", RegexOptions.IgnoreCase),
        };
        if (tag == "input")
        {
            var type = (FlowAttr(tagStr, "type") ?? "text").ToLowerInvariant();
            if (hidden) return null;
            if (type is "checkbox" or "radio")
            {
                control.IsCheckBox = type == "checkbox";
                control.IsRadio = !control.IsCheckBox;
                control.Checked = SafeRegex.IsMatch(tagStr, @"\schecked\b", RegexOptions.IgnoreCase);
                return control;
            }
            if (type is "submit" or "button" or "reset")
            {
                control.IsButton = true;
                control.Value = DecodeEntities(FlowAttr(tagStr, "value") ?? UaButtonCaption(type));
                return control;
            }
            if (NonTextInputTypes.Contains(type)) return null;
            control.Value = DecodeEntities(FlowAttr(tagStr, "value") ?? "");
            control.Columns = FlowIntAttr(tagStr, "size");
            return control;
        }
        var close = SafeRegex.Match(s[i..], @"</\s*" + tag + @"\s*>", RegexOptions.IgnoreCase);
        var inner = close.Success ? s.Substring(i, close.Index) : s[i..];
        i = close.Success ? i + close.Index + close.Length : s.Length;
        if (hidden) return null;
        if (tag == "textarea")
        {
            var cols = FlowIntAttr(tagStr, "cols");
            var rows = FlowIntAttr(tagStr, "rows");
            control.Multiline = true;
            control.Value = DecodeEntities(inner).Trim();
            control.AreaWidthPt = ((cols > 0 ? cols : ClInputColumns) + 1) * ControlColWidthPt;
            control.AreaHeightPt = ControlFirstRowPt + ((rows > 0 ? rows : FbUaTextAreaRows) - 1) * ControlNextRowPt
                + 2 * ClWidgetOverhangPt;
            return control;
        }
        control.Options = new List<string>();
        foreach (Match option in SafeRegex.Matches(FlowOptionRx, inner))
        {
            var entry = CollapseWs(DecodeEntities(option.Groups[2].Value));
            if (entry.Length == 0) continue;
            control.Options.Add(entry);
            if (control.Value.Length == 0 && SafeRegex.IsMatch(option.Groups[1].Value, @"\bselected\b", RegexOptions.IgnoreCase))
                control.Value = entry;
        }
        if (control.Value.Length == 0 && control.Options.Count > 0) control.Value = control.Options[0];
        return control;
    }

    /// <summary>The control's place on its line from the pen and the baseline (y up): its advance,
    /// the widget's offset and size, and how far its box reaches above and below the baseline.</summary>
    internal static (double Adv, double Dx, double Dy, double W, double H, double Above, double Below) FlowControlBox(
        FlowControl control, double availPt, double descentPt)
    {
        var block = new Block
        {
            IsCheckbox = control.IsCheckBox,
            IsRadio = control.IsRadio,
            IsButton = control.IsButton,
            ButtonCaption = control.IsButton ? control.Value : "",
            IsInputField = !(control.IsCheckBox || control.IsRadio || control.IsButton),
            IsSelectBox = control.Options is not null,
            SelectOptions = control.Options,
            InputMultiline = control.Multiline,
            InputColumns = control.Columns,
            InputWidth = control.AreaWidthPt,
            InputHeight = control.AreaHeightPt,
        };
        var box = ControlLineBox(block, availPt, descentPt);
        return (box.Adv, box.Dx, box.Dy, box.W, box.H, box.Above, box.Below);
    }

    /// <summary>A push button's field: its widget in a solid one point black border.</summary>
    internal static Forms.ButtonField NewPushButton(Page page, Rectangle rect, string? name)
    {
        var button = new Forms.ButtonField(page, rect);
        var look = new Core.PdfDictionary();
        look.Set("BC", new Core.PdfArray(new List<Core.PdfObject> { new Core.PdfInteger(0), new Core.PdfInteger(0), new Core.PdfInteger(0) }));
        button.Dict.Set("MK", look);
        var border = new Core.PdfDictionary();
        border.Set("W", new Core.PdfInteger(1));
        button.Dict.Set("BS", border);
        if (!string.IsNullOrEmpty(name)) button.PartialName = name;
        return button;
    }

    /// <summary>The face and size a push button's caption is drawn in, and its advance.</summary>
    internal const string PushButtonFace = ClButtonFace;
    internal const double PushButtonFontPt = ClButtonFontPt;
    internal static double PushButtonCaptionWidth(string caption) => MeasureStd14(ClButtonFace, caption, ClButtonFontPt);

    /// <summary>A select's combo box: its entries, in the UA control face, with its own look - a
    /// white box in a solid one point black border, drawn by its appearance.</summary>
    internal static Forms.ComboBoxField NewComboBox(Page page, Rectangle rect, IEnumerable<string> entries, string? name)
    {
        var combo = new Forms.ComboBoxField(page, rect);
        combo.Dict.Set("DA", new Core.PdfString(System.Text.Encoding.ASCII.GetBytes(
            Compat.Format(System.Globalization.CultureInfo.InvariantCulture, $"0 0 0 rg 0 0 0 RG /Helv {ClSelectFontPt:0.##} Tf"))));
        var look = new Core.PdfDictionary();
        look.Set("BG", new Core.PdfArray(new List<Core.PdfObject> { new Core.PdfInteger(1) }));
        look.Set("BC", new Core.PdfArray(new List<Core.PdfObject> { new Core.PdfInteger(0), new Core.PdfInteger(0), new Core.PdfInteger(0) }));
        combo.Dict.Set("MK", look);
        var border = new Core.PdfDictionary();
        border.Set("S", new Core.PdfName("S"));
        border.Set("W", new Core.PdfInteger(1));
        combo.Dict.Set("BS", border);
        foreach (var entry in entries) combo.AddOption(entry);
        if (!string.IsNullOrEmpty(name)) combo.PartialName = name;
        return combo;
    }
}
