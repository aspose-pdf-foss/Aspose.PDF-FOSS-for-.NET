using System.Globalization;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.DataEditor;

/// <summary>A value in a PDF dictionary as the data editor shows it: a name, a string, a
/// number, a boolean or a nested dictionary. The typed views return null when the value
/// is of another kind.</summary>
public interface ICosPdfPrimitive
{
    /// <summary>The value as a name, or null.</summary>
    CosPdfName? ToCosPdfName();

    /// <summary>The value as a string, or null.</summary>
    CosPdfString? ToCosPdfString();

    /// <summary>The value as a boolean, or null.</summary>
    CosPdfBoolean? ToCosPdfBoolean();

    /// <summary>The value as a number, or null.</summary>
    CosPdfNumber? ToCosPdfNumber();

    /// <summary>The value as a dictionary, or null.</summary>
    CosPdfDictionary? ToCosPdfDictionary();

    /// <summary>The value in PDF syntax.</summary>
    string ToString();
}

/// <summary>Base of the editor's values. Each subtype overrides its own typed view.</summary>
public abstract class CosPdfPrimitive : ICosPdfPrimitive
{
    /// <inheritdoc/>
    public virtual CosPdfNumber? ToCosPdfNumber() => null;

    /// <inheritdoc/>
    public virtual CosPdfName? ToCosPdfName() => null;

    /// <inheritdoc/>
    public virtual CosPdfString? ToCosPdfString() => null;

    /// <inheritdoc/>
    public virtual CosPdfBoolean? ToCosPdfBoolean() => null;

    /// <inheritdoc/>
    public virtual CosPdfDictionary? ToCosPdfDictionary() => null;

    /// <summary>The value in PDF syntax.</summary>
    public abstract override string ToString();

    /// <summary>The core object this value writes into a dictionary.</summary>
    internal abstract PdfObject ToPdfObject();

    /// <summary>The editor's view of a core object: null for a kind the editor does not
    /// show (an array, a stream, a reference that did not resolve).</summary>
    internal static CosPdfPrimitive? From(PdfObject? value) => value switch
    {
        PdfName n => new CosPdfName(n.Value),
        PdfString s => new CosPdfString(s.ToText(), s.IsHex),
        PdfInteger i => new CosPdfNumber(i.Value),
        PdfReal r => new CosPdfNumber(r.Value),
        PdfBoolean b => new CosPdfBoolean(b.Value),
        PdfDictionary d => new CosPdfDictionary(d, null),
        _ => null,
    };
}

/// <summary>A name value, written with its leading slash.</summary>
public sealed class CosPdfName : CosPdfPrimitive
{
    /// <summary>A name from its text (without the slash).</summary>
    public CosPdfName(string value) => Value = value ?? string.Empty;

    /// <summary>The name without its slash.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override CosPdfName ToCosPdfName() => this;

    /// <inheritdoc/>
    public override string ToString() => "/" + Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CosPdfName other && other.Value == Value;

    internal override PdfObject ToPdfObject() => new PdfName(Value);
}

/// <summary>A string value, literal or hexadecimal.</summary>
public sealed class CosPdfString : CosPdfPrimitive
{
    /// <summary>A literal string.</summary>
    public CosPdfString(string value) : this(value, false) { }

    /// <summary>A string, written as hexadecimal when asked.</summary>
    public CosPdfString(string value, bool isHexadecimal)
    {
        Value = value ?? string.Empty;
        IsHexadecimal = isHexadecimal;
    }

    /// <summary>Whether the string is written in hexadecimal form.</summary>
    public bool IsHexadecimal { get; }

    /// <summary>The string's text.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override CosPdfString ToCosPdfString() => this;

    /// <inheritdoc/>
    public override string ToString() => IsHexadecimal ? "<" + Hex(Value) + ">" : "(" + Value + ")";

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CosPdfString other && other.Value == Value;

    internal override PdfObject ToPdfObject() => new PdfString(System.Text.Encoding.UTF8.GetBytes(Value), IsHexadecimal);

    private static string Hex(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text)) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

/// <summary>A numeric value.</summary>
public sealed class CosPdfNumber : CosPdfPrimitive
{
    /// <summary>Zero.</summary>
    public CosPdfNumber() : this(0) { }

    /// <summary>A number.</summary>
    public CosPdfNumber(double value) => Value = value;

    /// <summary>The number.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public override CosPdfNumber ToCosPdfNumber() => this;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CosPdfNumber other && other.Value == Value;

    internal override PdfObject ToPdfObject()
        => Value == System.Math.Floor(Value) && System.Math.Abs(Value) < long.MaxValue
            ? new PdfInteger((long)Value)
            : new PdfReal(Value);
}

/// <summary>A boolean value.</summary>
public sealed class CosPdfBoolean : CosPdfPrimitive
{
    /// <summary>A boolean.</summary>
    public CosPdfBoolean(bool value) => Value = value;

    /// <summary>The boolean.</summary>
    public bool Value { get; }

    /// <inheritdoc/>
    public override CosPdfBoolean ToCosPdfBoolean() => this;

    /// <inheritdoc/>
    public override string ToString() => Value ? "true" : "false";

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CosPdfBoolean other && other.Value == Value;

    internal override PdfObject ToPdfObject() => Value ? PdfBoolean.True : PdfBoolean.False;
}
