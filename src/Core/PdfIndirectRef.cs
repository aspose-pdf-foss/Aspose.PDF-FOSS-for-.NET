using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfIndirectRef : PdfObject
{
    public int ObjectNumber { get; }
    public int Generation { get; }

    public PdfIndirectRef(int objectNumber, int generation)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    public override string ToString() => $"{ObjectNumber} {Generation} R";
}

internal readonly record struct IndirectObject(int ObjectNumber, int Generation, PdfObject Value);
