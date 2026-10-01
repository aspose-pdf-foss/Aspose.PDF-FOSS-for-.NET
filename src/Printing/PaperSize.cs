namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// The size of a sheet of paper, in hundredths of an inch - the unit
    /// <see cref="System.Drawing.Printing.PaperSize"/> uses, so the two convert without
    /// scaling.
    /// </summary>
    public class PaperSize
    {
        /// <summary>Creates an unnamed custom size of zero extent.</summary>
        public PaperSize()
        {
            PaperName = string.Empty;
        }

        /// <summary>Creates a custom size with the given name and dimensions in
        /// hundredths of an inch.</summary>
        public PaperSize(string name, int width, int height)
        {
            PaperName = name;
            Width = width;
            Height = height;
            Kind = PaperKind.Custom;
        }

        /// <summary>Creates one of the standard sizes. Standard sizes carry no name of
        /// their own: the printer driver names them from <see cref="Kind"/>.</summary>
        internal PaperSize(PaperKind kind, int width, int height)
        {
            PaperName = string.Empty;
            Width = width;
            Height = height;
            Kind = kind;
        }

        /// <summary>The name of this size, empty for the standard sizes.</summary>
        public string PaperName { get; set; }

        /// <summary>Width in hundredths of an inch.</summary>
        public int Width { get; set; }

        /// <summary>Height in hundredths of an inch.</summary>
        public int Height { get; set; }

        /// <summary>Which standard size this is, or <see cref="PaperKind.Custom"/>.</summary>
        public PaperKind Kind { get; set; }

        /// <inheritdoc/>
        public override string ToString()
            => "[PaperSize " + PaperName + " Kind=" + Kind + " Height=" + Height + " Width=" + Width + "]";
    }
}
