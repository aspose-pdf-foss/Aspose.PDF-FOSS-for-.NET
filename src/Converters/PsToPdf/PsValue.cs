using System;
using System.Globalization;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>The PostScript object types the interpreter distinguishes. The order is
/// the one <c>type</c> reports, so the enum doubles as that operator's table.</summary>
internal enum PsType
{
    /// <summary>The null object, and the value of an unfilled array slot.</summary>
    Null,
    /// <summary>A boolean.</summary>
    Boolean,
    /// <summary>An integer.</summary>
    Integer,
    /// <summary>A real number.</summary>
    Real,
    /// <summary>A name, literal or executable.</summary>
    Name,
    /// <summary>A string body, shared and mutable.</summary>
    String,
    /// <summary>An array or a procedure, shared and mutable.</summary>
    Array,
    /// <summary>A dictionary, shared and mutable.</summary>
    Dictionary,
    /// <summary>A built-in operator.</summary>
    Operator,
    /// <summary>The mark object left by <c>mark</c> and <c>[</c>.</summary>
    Mark,
    /// <summary>A file object.</summary>
    File,
    /// <summary>The token left by <c>save</c>.</summary>
    Save,
    /// <summary>A font dictionary handed out by <c>findfont</c>.</summary>
    Font,
}

/// <summary>
/// One PostScript object. Numbers live in <see cref="Number"/>; every composite type
/// keeps its shared body in <see cref="Body"/>, so copying a value copies the
/// reference exactly as PostScript's composite-object semantics require.
/// </summary>
internal readonly struct PsValue : IEquatable<PsValue>
{
    /// <summary>The null object.</summary>
    public static readonly PsValue Null = default;

    /// <summary>The mark object.</summary>
    public static readonly PsValue Mark = new(PsType.Mark, 0, null, false);

    /// <summary>True.</summary>
    public static readonly PsValue True = new(PsType.Boolean, 1, null, false);

    /// <summary>False.</summary>
    public static readonly PsValue False = new(PsType.Boolean, 0, null, false);

    private PsValue(PsType type, double number, object? body, bool executable)
    {
        Type = type;
        Number = number;
        Body = body;
        IsExecutable = executable;
    }

    /// <summary>This object's type.</summary>
    public PsType Type { get; }

    /// <summary>The numeric payload: the value of an integer, real or boolean.</summary>
    public double Number { get; }

    /// <summary>The shared body of a composite object, or the text of a name.</summary>
    public object? Body { get; }

    /// <summary>Whether the interpreter executes this object rather than pushing it.</summary>
    public bool IsExecutable { get; }

    /// <summary>Whether this object is an integer or a real.</summary>
    public bool IsNumber => Type == PsType.Integer || Type == PsType.Real;

    /// <summary>Whether this object is the null object.</summary>
    public bool IsNull => Type == PsType.Null;

    /// <summary>Make an integer.</summary>
    public static PsValue Int(double value) => new(PsType.Integer, value, null, false);

    /// <summary>Make a real.</summary>
    public static PsValue Real(double value) => new(PsType.Real, value, null, false);

    /// <summary>Make a number, integral when it has no fractional part and fits.</summary>
    public static PsValue Num(double value) =>
        value == Math.Floor(value) && Math.Abs(value) <= int.MaxValue ? Int(value) : Real(value);

    /// <summary>Make a boolean.</summary>
    public static PsValue Bool(bool value) => value ? True : False;

    /// <summary>Make a literal name.</summary>
    public static PsValue LiteralName(string text) => new(PsType.Name, 0, text, false);

    /// <summary>Make an executable name — the form the scanner produces for a bare
    /// word, and the form the interpreter looks up.</summary>
    public static PsValue ExecName(string text) => new(PsType.Name, 0, text, true);

    /// <summary>Wrap an existing string body.</summary>
    public static PsValue Str(PsString body) => new(PsType.String, 0, body, false);

    /// <summary>Wrap an existing array body as a literal array.</summary>
    public static PsValue Arr(PsArray body) => new(PsType.Array, 0, body, false);

    /// <summary>Wrap an existing array body as a procedure.</summary>
    public static PsValue Proc(PsArray body) => new(PsType.Array, 0, body, true);

    /// <summary>Wrap an existing dictionary body.</summary>
    public static PsValue Dict(PsDictionary body) => new(PsType.Dictionary, 0, body, false);

    /// <summary>Wrap a dictionary body as a font, the object <c>findfont</c> hands out.</summary>
    public static PsValue Font(PsDictionary body) => new(PsType.Font, 0, body, false);

    /// <summary>Wrap a built-in operator.</summary>
    public static PsValue Op(PsOperator body) => new(PsType.Operator, 0, body, true);

    /// <summary>Wrap a file body.</summary>
    public static PsValue File(PsFile body) => new(PsType.File, 0, body, false);

    /// <summary>Make the token a <c>save</c> hands out.</summary>
    public static PsValue SaveToken(int level) => new(PsType.Save, level, null, false);

    /// <summary>This object with the executable flag set as asked.</summary>
    public PsValue WithExecutable(bool executable) =>
        new(Type, Number, Body, executable);

    /// <summary>The text of a name, or null when this is not a name.</summary>
    public string? Text => Body as string;

    /// <summary>The string body, or null when this is not a string.</summary>
    public PsString? AsString => Body as PsString;

    /// <summary>The array body, or null when this is not an array.</summary>
    public PsArray? AsArray => Body as PsArray;

    /// <summary>The dictionary body, or null when this is neither dict nor font.</summary>
    public PsDictionary? AsDictionary => Body as PsDictionary;

    /// <summary>The operator body, or null when this is not an operator.</summary>
    public PsOperator? AsOperator => Body as PsOperator;

    /// <summary>The file body, or null when this is not a file.</summary>
    public PsFile? AsFile => Body as PsFile;

    /// <summary>The numeric value as an integer, truncating a real.</summary>
    public int ToInt() => (int)Number;

    /// <summary>Whether a boolean object is true.</summary>
    public bool ToBool() => Number != 0;

    /// <summary>The PostScript type name <c>type</c> reports.</summary>
    public string TypeName => Type switch
    {
        PsType.Null => "nulltype",
        PsType.Boolean => "booleantype",
        PsType.Integer => "integertype",
        PsType.Real => "realtype",
        PsType.Name => "nametype",
        PsType.String => "stringtype",
        PsType.Array => "arraytype",
        PsType.Dictionary => "dicttype",
        PsType.Operator => "operatortype",
        PsType.Mark => "marktype",
        PsType.File => "filetype",
        PsType.Save => "savetype",
        _ => "fonttype",
    };

    /// <summary>Composite objects compare by identity, simple ones by value — the
    /// rule <c>eq</c> follows, except that <c>eq</c> also equates a name with the
    /// string of the same characters.</summary>
    public bool Equals(PsValue other)
    {
        if (IsNumber && other.IsNumber) return Number == other.Number;
        if (Type != other.Type) return false;
        return Type switch
        {
            PsType.Null or PsType.Mark => true,
            PsType.Boolean or PsType.Save => Number == other.Number,
            PsType.Name => Text == other.Text,
            _ => ReferenceEquals(Body, other.Body),
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PsValue v && Equals(v);

    /// <inheritdoc/>
    public override int GetHashCode() => Type switch
    {
        PsType.Integer or PsType.Real => Number.GetHashCode(),
        PsType.Name => Text?.GetHashCode() ?? 0,
        PsType.Null or PsType.Mark => (int)Type,
        PsType.Boolean or PsType.Save => (int)Type ^ Number.GetHashCode(),
        _ => Body is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Body),
    };

    /// <summary>The text <c>=</c> would print, used for diagnostics.</summary>
    public override string ToString() => Type switch
    {
        PsType.Null => "null",
        PsType.Boolean => ToBool() ? "true" : "false",
        PsType.Integer => ((long)Number).ToString(CultureInfo.InvariantCulture),
        PsType.Real => Number.ToString("0.######", CultureInfo.InvariantCulture),
        PsType.Name => (IsExecutable ? string.Empty : "/") + Text,
        PsType.String => "(" + AsString + ")",
        PsType.Array => IsExecutable ? "{...}" : "[...]",
        PsType.Operator => "--" + AsOperator!.Name + "--",
        PsType.Mark => "-mark-",
        _ => "-" + TypeName + "-",
    };
}
