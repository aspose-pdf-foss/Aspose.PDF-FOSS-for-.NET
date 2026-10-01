using System.Globalization;

namespace Aspose.Pdf.Comparison
{
    /// <summary>How one kind of edit is drawn in a comparison output: its text colour and
    /// the colour behind it. A null colour leaves that aspect to the output's own default.</summary>
    public class TextStyle
    {
        /// <summary>The text colour.</summary>
        public Color? Color { get; set; }

        /// <summary>The colour drawn behind the text.</summary>
        public Color? BackgroundColor { get; set; }

        /// <summary>Create a style that sets neither colour.</summary>
        public TextStyle() { }

        /// <summary>The class body the HTML output writes for this style: one declaration per
        /// line, colours as upper-case hex, as the reference sheet has them.</summary>
        internal string ToCssStyle()
        {
            var css = string.Empty;
            if (Color is { } c) css += "color: " + Hex(c) + ";" + "\n";
            if (BackgroundColor is { } b) css += "background-color: " + Hex(b) + ";" + "\n";
            return css;
        }

        private static string Hex(Color c)
        {
            var rgb = c.ToRgb();
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", rgb.R, rgb.G, rgb.B);
        }
    }

    /// <summary>The three styles a comparison output draws with - one per edit kind - plus
    /// whether deleted text is struck through as well as coloured.</summary>
    public class OutputTextStyle
    {
        /// <summary>Style for text present only in the second document.</summary>
        public TextStyle? InsertedStyle { get; set; }

        /// <summary>Style for text present only in the first document.</summary>
        public TextStyle? DeletedStyle { get; set; }

        /// <summary>Style for text present in both.</summary>
        public TextStyle? EqualStyle { get; set; }

        /// <summary>Draw deleted text with a line through it, on top of its colours.</summary>
        public bool StrikethroughDeleted { get; set; }

        /// <summary>Create an output style that sets nothing, so every kind takes the
        /// output's own default.</summary>
        public OutputTextStyle() { }
    }
}
