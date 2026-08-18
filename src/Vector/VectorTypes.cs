#nullable disable

namespace Aspose.Pdf.Vector
{
    /// <summary>A Form XObject invocation (<c>Do</c>) found in a content stream.
    /// Child <see cref="Elements"/> are extracted in the form's own coordinate
    /// space; <see cref="Rectangle"/> is the form's /BBox mapped through the
    /// placement CTM into page space.</summary>
    public sealed class XFormPlacement : GraphicElement
    {
        private readonly Rectangle _rectangle;

        internal XFormPlacement(string name, Rectangle rectangle, GraphicElementCollection elements)
        {
            Name = name;
            _rectangle = rectangle;
            Elements = elements;
        }

        /// <summary>The XObject resource name (e.g. "FRM0").</summary>
        public string Name { get; }

        /// <summary>The form's own painted elements, in form-space coordinates.</summary>
        public GraphicElementCollection Elements { get; }

        public override Rectangle Rectangle => _rectangle;

        internal override GraphicElement Clone(XFormPlacement xFormPlacement) => this;

        internal override void AppendSvgContent(System.Text.StringBuilder sb,
            double originX, double originY, double boxHeight)
        {
            foreach (var child in Elements)
                child.AppendSvgContent(sb, originX, originY, boxHeight);
        }
    }
}
