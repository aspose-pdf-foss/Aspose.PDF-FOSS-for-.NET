using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Intent of a polygon or polyline annotation (/IT entry).</summary>
public enum PolyIntent
{
    /// <summary>Intent is missing or undefined.</summary>
    Undefined,
    /// <summary>Cloud-shaped polygon (PolygonCloud).</summary>
    PolygonCloud,
    /// <summary>Polygon used as a dimension (PolygonDimension).</summary>
    PolygonDimension,
    /// <summary>Polyline used as a dimension (PolyLineDimension).</summary>
    PolyLineDimension,
}
