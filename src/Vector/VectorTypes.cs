#nullable disable

namespace Aspose.Pdf.Vector
{
    /// <summary>A Form XObject invocation (<c>Do</c>) found in a content stream.
    /// Child <see cref="Elements"/> are extracted in the form's own coordinate
    /// space; <see cref="Rectangle"/> is the form's /BBox mapped through the
    /// placement CTM into page space. A top-level placement can be moved
    /// (<see cref="Position"/>) or removed on its source page; replaying it onto
    /// another page imports the form object into that page's document.</summary>
    public sealed class XFormPlacement : GraphicElement
    {
        private readonly Rectangle _rectangle;
        private double _dx, _dy;

        internal XFormPlacement(string name, Rectangle rectangle, GraphicElementCollection elements,
            Aspose.Pdf.Matrix placementCtm, Core.PdfStream formStream, IO.PdfReader sourceReader,
            Core.PdfIndirectRef formRef)
        {
            Name = name;
            _rectangle = rectangle;
            Elements = elements;
            PlacementCtm = placementCtm;
            FormStream = formStream;
            SourceReader = sourceReader;
            FormRef = formRef;
        }

        /// <summary>The XObject resource name (e.g. "FRM0").</summary>
        public string Name { get; }

        /// <summary>The form's own painted elements, in form-space coordinates.</summary>
        public GraphicElementCollection Elements { get; }

        /// <summary>The CTM in force at the <c>Do</c> invocation (placement into page space).</summary>
        internal Aspose.Pdf.Matrix PlacementCtm { get; }

        /// <summary>The form's stream object in its source document.</summary>
        internal Core.PdfStream FormStream { get; }

        /// <summary>The indirect reference the source page's /XObject entry held
        /// (a resolved <see cref="Core.PdfStream"/> does not carry its own object
        /// number in unencrypted files, so replays reference the form by this).
        /// Null when the resource entry was a direct object.</summary>
        internal Core.PdfIndirectRef FormRef { get; }

        /// <summary>The source document's reader (for cross-document import).</summary>
        internal IO.PdfReader SourceReader { get; }

        public override Rectangle Rectangle => _rectangle;

        /// <summary>The placement's page-space anchor. Assigning a new point moves
        /// the whole form invocation by the delta and rewrites the source page.</summary>
        public override Point Position
        {
            get => new Point(_rectangle.LLX + _dx, _rectangle.LLY + _dy);
            set
            {
                _dx = value.X - _rectangle.LLX;
                _dy = value.Y - _rectangle.LLY;
                EditState?.MarkDirty();
            }
        }

        internal override (double Dx, double Dy) SourceTranslation => (_dx, _dy);

        internal override Aspose.Pdf.Matrix SourceCtm => PlacementCtm;

        internal override GraphicElement Clone(XFormPlacement xFormPlacement) => this;

        internal override void AppendSvgContent(System.Text.StringBuilder sb,
            double originX, double originY, double boxHeight)
        {
            foreach (var child in Elements)
                child.AppendSvgContent(sb, originX, originY, boxHeight);
        }
    }
}
