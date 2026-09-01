// What laying out ONE block needs to know about that block: its font and line box, the
// width and left edge available to it, the lines its text wrapped to, and how far the
// flow had got before they were written. Recomputed for every block, which is why it is
// created inside the loop rather than carried between blocks like the flow cursor.

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The measurements one block is laid out against.</summary>
    private sealed class HtmlBlockMetrics
    {
        /// <summary>The block's font size in points.</summary>
        public double blockFontSize;
        /// <summary>The line box the block's text is laid out on.</summary>
        public double lineHeight;
        public double charW;
        /// <summary>Left edge the block's lines start from.</summary>
        public double lineX;
        /// <summary>The block's text, wrapped to the width available to it.</summary>
        public string[] lines = System.Array.Empty<string>();
        /// <summary>Character offset of the current line's start within the block text.</summary>
        public int cumChar;
        public bool firstLineOfBlock;
        public double floatBoxLeftPt;
        public double floatBoxWidthPt;
        public double floatLabelIndent;
        /// <summary>The block is flowing alongside a left float.</summary>
        public bool besideLeftFloat;
        /// <summary>The face the block is MEASURED in, which is not always the face it is drawn in.</summary>
        public string? bandFace;
        public double metricDrop;
        public string metricMeasureFace = "";
        public double ptLeadExtraPt;
        /// <summary>Where the flow stood before the block's lines were written.</summary>
        public double yBeforeBlockLines;
    }
}
