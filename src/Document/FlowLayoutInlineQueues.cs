namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>Radio controls an inline flow placed: bound as one-option radio groups
        /// once their slots are pages.</summary>
        private readonly List<(int slot, Rectangle rect, string name, bool checkedState)> _pendingRadios = new();

        /// <summary>Draw a picture at <paramref name="rect"/> on the page the cursor is on
        /// without moving the cursor (an inline picture standing on a line).</summary>
        public void QueueImageAt(byte[] data, Rectangle rect)
        {
            if (_overflowBuffer is null) { _startPage.AddImage(data, rect); return; }
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
                radio.PlaceOptionWidget(option, pg, rect);
                if (checkedState) radio.Selected = 1;
            }
        }
    }
}
