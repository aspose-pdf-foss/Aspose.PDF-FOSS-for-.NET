using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfBoolean : PdfObject
{
    public static readonly PdfBoolean True = new(true);
    public static readonly PdfBoolean False = new(false);

    public bool Value { get; }
    private PdfBoolean(bool value) => Value = value;
    public override string ToString() => Value ? "true" : "false";
}
