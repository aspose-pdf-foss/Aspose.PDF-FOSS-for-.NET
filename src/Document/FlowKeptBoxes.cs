namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>The height a box takes laid whole at the region's current width, its
        /// margins included: what a keep-together rule weighs against the room. Null when
        /// the box holds something only laying it out can measure (a table, a list, a
        /// picture), which leaves the box where it is.</summary>
        internal double? WholeHeightOf(BoxBlock box) => WholeHeightOf(box, CurWidth);

        private static double? WholeHeightOf(BoxBlock box, double regionWidth)
        {
            var (bl, bb, br, bt) = box.Border is { } border ? BorderBands(border) : (0, 0, 0, 0);
            var pad = box.Padding;
            var sides = bl + br + (pad?.Left ?? 0) + (pad?.Right ?? 0);
            var contentWidth = box.Width <= 0
                ? Math.Max(0, regionWidth - box.Margin.Left - box.Margin.Right - sides)
                : box.WidthIsFraction ? box.Width * regionWidth : box.Width;
            double content = 0;
            foreach (var paragraph in box.Paragraphs)
            {
                var height = paragraph switch
                {
                    BoxBlock inner => WholeHeightOf(inner, contentWidth),
                    Text.TextFragment tf => FragmentHeightOf(tf, contentWidth),
                    _ => null,
                };
                if (height is null) return null;
                content += height.Value;
            }
            content = Math.Max(content, box.MinHeight);
            return box.Margin.Top + bt + (pad?.Top ?? 0) + content + (pad?.Bottom ?? 0) + bb + box.Margin.Bottom;
        }

        /// <summary>A fragment's height with its margins, priced as the block writer lays
        /// it: a boxed fragment's border, padding and fixed, capped or floored content
        /// height; a plain one's wrapped lines.</summary>
        private static double? FragmentHeightOf(Text.TextFragment tf, double regionWidth)
        {
            var margins = (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
            if (!tf.HasBlockBox) return margins + CountBlockLines(tf, regionWidth) * BlockLineHeight(tf);

            var (bl, bb, br, bt) = tf.BlockBorder is { } border ? BorderBands(border) : (0, 0, 0, 0);
            var pad = tf.BlockPadding;
            var chrome = (tf.Margin?.Left ?? 0) + (tf.Margin?.Right ?? 0) + bl + br + (pad?.Left ?? 0) + (pad?.Right ?? 0);
            var contentWidth = tf.BlockWidth > 0 ? tf.BlockWidth : Math.Max(0, regionWidth - chrome);
            if (tf.BlockMaxWidth > 0) contentWidth = Math.Min(contentWidth, tf.BlockMaxWidth);
            if (tf.BlockMinWidth > 0) contentWidth = Math.Max(contentWidth, tf.BlockMinWidth);
            var contentHeight = tf.BoxHeight > 0 ? tf.BoxHeight : CountBlockLines(tf, contentWidth) * BlockLineHeight(tf);
            if (tf.BoxMaxHeight > 0) contentHeight = Math.Min(contentHeight, tf.BoxMaxHeight);
            contentHeight = Math.Max(contentHeight, tf.BoxMinHeight);
            return margins + bt + (pad?.Top ?? 0) + contentHeight + (pad?.Bottom ?? 0) + bb;
        }
    }
}
