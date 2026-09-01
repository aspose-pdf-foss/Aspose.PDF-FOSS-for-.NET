using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    public string Value { get; }
    public PdfName(string value) => Value = value;

    public bool Equals(PdfName? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is PdfName other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => $"/{Value}";

    public static bool operator ==(PdfName? left, PdfName? right) =>
        left is null ? right is null : left.Equals(right);
    public static bool operator !=(PdfName? left, PdfName? right) => !(left == right);
}
