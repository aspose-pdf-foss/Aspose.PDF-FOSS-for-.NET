using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfInteger : PdfObject
{
    public long Value { get; }
    public PdfInteger(long value) => Value = value;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
