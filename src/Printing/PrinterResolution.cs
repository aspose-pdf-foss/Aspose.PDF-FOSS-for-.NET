namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// A printer resolution: either one of the driver's named quality bands, or a
    /// <see cref="PrinterResolutionKind.Custom"/> dpi pair.
    /// </summary>
    public class PrinterResolution
    {
        /// <summary>Creates a custom resolution of zero dpi.</summary>
        public PrinterResolution() { }

        /// <summary>Creates a resolution of the given kind and dpi.</summary>
        internal PrinterResolution(PrinterResolutionKind kind, int x, int y)
        {
            Kind = kind;
            X = x;
            Y = y;
        }

        /// <summary>The quality band, or <see cref="PrinterResolutionKind.Custom"/> when
        /// <see cref="X"/> and <see cref="Y"/> carry the resolution.</summary>
        public PrinterResolutionKind Kind { get; set; }

        /// <summary>Horizontal resolution in dots per inch.</summary>
        public int X { get; set; }

        /// <summary>Vertical resolution in dots per inch.</summary>
        public int Y { get; set; }

        /// <inheritdoc/>
        public override string ToString()
            => Kind == PrinterResolutionKind.Custom
                ? "[PrinterResolution X=" + X + " Y=" + Y + "]"
                : "[PrinterResolution " + Kind + "]";
    }
}
