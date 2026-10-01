using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ShapeRenderState
{
    public System.Text.StringBuilder sb = null!;
    public double[] newCtm = null!;
    public bool visible;
    public string fillVal = null!;
    public string strokeVal = null!;
    public bool hasFill;
    public bool fillIsPattern;
    // A <pattern> paint server becomes a PDF tiling pattern; a tile with
    // no area disables the fill (SVG's none-rendering rule).
    public string? tilingName;
    public double opacity;
    public double fillOpacity;
    public double strokeOpacity;
    public bool evenOdd;
    public XmlElement elem = default!;
    public Ctx ctx = default!;
    public Dictionary<string, string> style = default!;
    public double[] ctm = default!;
}
}
