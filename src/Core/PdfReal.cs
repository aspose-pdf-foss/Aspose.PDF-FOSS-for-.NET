using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfReal : PdfObject
{
    public double Value { get; }
    public PdfReal(double value) => Value = value;

    // PDF real numbers (PDF 32000-1 §7.3.3) are written as a plain decimal —
    // exponential notation is NOT permitted. Default "G" formatting emits
    // exponent form for magnitudes below 1e-4 or very large (e.g. an ArtBox
    // coordinate 0.0000610352 became "6.10352E-05"), which downstream PDF
    // parsers — including this library's own — reject, derailing the whole
    // object and falling back to defaults (a blank, mis-sized page). Keep the
    // exact "G" text for every value it already renders without an exponent
    // (so existing byte-for-byte output is unchanged), and only expand the
    // exponent cases to an equivalent plain decimal.
    public override string ToString()
    {
        var g = Value.ToString("G", CultureInfo.InvariantCulture);
        if (g.IndexOf('E') < 0 && g.IndexOf('e') < 0)
            return g;
        // "0.################" never uses an exponent and trims trailing zeros;
        // 16 fractional digits cover the sub-1e-4 magnitudes that triggered the
        // exponent without introducing floating-point noise.
        var plain = Value.ToString("0.################", CultureInfo.InvariantCulture);
        return plain.Length == 0 || plain == "-0" ? "0" : plain;
    }
}
