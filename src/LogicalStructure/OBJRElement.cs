using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>An object reference (role "OBJR") — links a structure element to a PDF object on a
/// page (typically an annotation, e.g. a Link's widget). <see cref="Obj"/> resolves the
/// referenced object so callers can wrap it (e.g. <c>new LinkAnnotation(objr.Obj, doc)</c>).</summary>
public sealed class OBJRElement : StructureElement
{
    internal OBJRElement() : base("OBJR") { }
    internal OBJRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>Record the referenced object's indirect reference under /Obj.</summary>
    internal void SetObj(PdfObject objRef) => _dict.Set("Obj", objRef);

    /// <summary>The referenced PDF object (resolved from /Obj), or null.</summary>
    public object? Obj => _reader is not null ? _reader.Resolve(_dict.Get("Obj")) : _dict.Get("Obj");
}
