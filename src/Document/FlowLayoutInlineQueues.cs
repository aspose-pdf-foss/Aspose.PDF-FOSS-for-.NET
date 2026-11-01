namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>Radio controls an inline flow placed: bound as one-option radio groups
        /// once their slots are pages.</summary>
        private readonly List<(int slot, Rectangle rect, string name, bool checkedState)> _pendingRadios = new();

        /// <summary>Text controls a flow paragraph placed: bound as text box or combo box fields once
        /// their slots are pages.</summary>
        private readonly List<(int slot, Rectangle rect, Converters.HtmlToPdfConverter.FlowControl control)> _pendingControls = new();

        /// <summary>Queue a text control's widget at <paramref name="rect"/> on the current page
        /// without moving the cursor.</summary>
        public void QueueControlAt(Rectangle rect, Converters.HtmlToPdfConverter.FlowControl control)
        {
            _pendingControls.Add((_currentSlot, rect, control));
            _overflowBuffer?.Add(Array.Empty<byte>());
        }

        /// <summary>Bind every queued text control: a select as a combo box of its entries, a text
        /// input or text area as a text box, each in a solid one point black border with its value.</summary>
        private void FinaliseInlineControls(IList<Page> overflowPageRefs, Document doc)
        {
            foreach (var (slot, rect, control) in _pendingControls)
            {
                var pg = SlotPage(slot, overflowPageRefs);
                if (pg is null) continue;
                Forms.Field field;
                if (control.IsButton)
                {
                    doc.Form.Add(Converters.HtmlToPdfConverter.NewPushButton(pg, rect, control.Name), pg.Number);
                    continue;
                }
                if (control.Options is { } entries)
                    field = Converters.HtmlToPdfConverter.NewComboBox(pg, rect, entries, control.Name);
                else
                {
                    field = new Forms.TextBoxField(pg, rect) { Multiline = control.Multiline, ReadOnly = control.ReadOnly };
                    if (!string.IsNullOrEmpty(control.Name)) field.PartialName = control.Name;
                    field.Characteristics.Border = System.Drawing.Color.Black;
                }
                doc.Form.Add(field, pg.Number);
                if (control.Value.Length > 0) field.Value = control.Value;
            }
        }

        /// <summary>Draw a picture at <paramref name="rect"/> on the page the cursor is on
        /// without moving the cursor (an inline picture standing on a line).</summary>
        public void QueueImageAt(byte[] data, Rectangle rect)
        {
            if (_overflowBuffer is null) { _startPage.AddLayoutImage(data, rect); return; }
            _pendingImages.Add((_currentSlot, data, rect, false));
            _overflowBuffer.Add(Array.Empty<byte>());
        }

        /// <summary>Queue a checkbox widget at <paramref name="rect"/> on the current page
        /// without moving the cursor.</summary>
        public void QueueCheckboxAt(Rectangle rect, bool checkedState)
        {
            _pendingFormFields.Add((_currentSlot, rect, checkedState));
            _overflowBuffer?.Add(Array.Empty<byte>());
        }

        /// <summary>Queue a radio widget at <paramref name="rect"/> on the current page
        /// without moving the cursor.</summary>
        public void QueueRadioAt(Rectangle rect, string name, bool checkedState)
        {
            _pendingRadios.Add((_currentSlot, rect, name, checkedState));
            _overflowBuffer?.Add(Array.Empty<byte>());
        }

        /// <summary>Bind every queued radio: a group named as queued with one option
        /// "Item0" whose widget sits at the queued rectangle (the reference engine's
        /// shape for an HTML radio control: a 1 pt black ring, a filled dot when on).</summary>
        private void FinaliseInlineRadios(IList<Page> overflowPageRefs, Document doc)
        {
            foreach (var (slot, rect, name, checkedState) in _pendingRadios)
            {
                var pg = SlotPage(slot, overflowPageRefs);
                if (pg is null) continue;
                var radio = new Forms.RadioButtonField(pg) { PartialName = name };
                var option = new Forms.RadioButtonOptionField(pg, rect) { OptionName = "Item0" };
                option.Characteristics.Border = System.Drawing.Color.Black;
                radio.Add(option);
                doc.Form.Add(radio, pg.Number);
                Forms.RadioButtonField.PlaceOptionWidget(option, pg, rect);
                if (checkedState) radio.Selected = 1;
            }
        }
    }
}
