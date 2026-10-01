using System.Reflection;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using TaggedException = Aspose.Pdf.Tagged.TaggedException;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Standard owners of a tagged-PDF attribute set (the /O entry
/// of an attribute object, ISO 32000-1 Table 348).</summary>
public enum AttributeOwnerStandard
{
    /// <summary>General layout attributes (/O /Layout).</summary>
    Layout,
    /// <summary>List attributes (/O /List).</summary>
    List,
    /// <summary>Print-field attributes (/O /PrintField).</summary>
    PrintField,
    /// <summary>Table attributes (/O /Table).</summary>
    Table,
}

/// <summary>Standard tagged-PDF attribute keys (ISO 32000-1 §14.8.5).</summary>
public enum AttributeKey
{
    Placement,
    WritingMode,
    BackgroundColor,
    BorderColor,
    BorderStyle,
    BorderThickness,
    Color,
    Padding,
    SpaceBefore,
    SpaceAfter,
    StartIndent,
    EndIndent,
    TextIndent,
    TextAlign,
    BBox,
    Width,
    Height,
    BlockAlign,
    InlineAlign,
    LineHeight,
    BaselineShift,
    TextDecorationType,
    TextDecorationColor,
    TextDecorationThickness,
    GlyphOrientationVertical,
    RubyAlign,
    RubyPosition,
    ListNumbering,
    RowSpan,
    ColSpan,
    Headers,
    Scope,
    Summary,
}

/// <summary>
/// A typed value for a standard attribute name (the /Name-valued entries
/// in an attribute object, e.g. /Placement /Block). Implemented as a set
/// of singletons so callers can compare by reference and check for null.
/// </summary>
public sealed class AttributeName
{
    /// <summary>The attribute key this name is valid for.</summary>
    internal AttributeKey Key { get; }

    /// <summary>The PDF name value (the part after the key prefix).</summary>
    internal string Value { get; }

    private AttributeName(AttributeKey key, string value)
    {
        Key = key;
        Value = value;
    }

    public static readonly AttributeName Placement_Block = new(AttributeKey.Placement, "Block");
    public static readonly AttributeName Placement_Inline = new(AttributeKey.Placement, "Inline");
    public static readonly AttributeName Placement_Before = new(AttributeKey.Placement, "Before");
    public static readonly AttributeName Placement_Start = new(AttributeKey.Placement, "Start");
    public static readonly AttributeName Placement_End = new(AttributeKey.Placement, "End");

    public static readonly AttributeName TextAlign_Start = new(AttributeKey.TextAlign, "Start");
    public static readonly AttributeName TextAlign_Center = new(AttributeKey.TextAlign, "Center");
    public static readonly AttributeName TextAlign_End = new(AttributeKey.TextAlign, "End");
    public static readonly AttributeName TextAlign_Justify = new(AttributeKey.TextAlign, "Justify");

    public static readonly AttributeName BlockAlign_Before = new(AttributeKey.BlockAlign, "Before");
    public static readonly AttributeName BlockAlign_Middle = new(AttributeKey.BlockAlign, "Middle");
    public static readonly AttributeName BlockAlign_After = new(AttributeKey.BlockAlign, "After");

    public static readonly AttributeName Height_Auto = new(AttributeKey.Height, "Auto");
    public static readonly AttributeName Width_Auto = new(AttributeKey.Width, "Auto");

    public static readonly AttributeName TextDecorationType_None = new(AttributeKey.TextDecorationType, "None");
    public static readonly AttributeName TextDecorationType_Underline = new(AttributeKey.TextDecorationType, "Underline");
    public static readonly AttributeName TextDecorationType_Overline = new(AttributeKey.TextDecorationType, "Overline");
    public static readonly AttributeName TextDecorationType_LineThrough = new(AttributeKey.TextDecorationType, "LineThrough");

    public static readonly AttributeName Scope_Row = new(AttributeKey.Scope, "Row");
    public static readonly AttributeName Scope_Column = new(AttributeKey.Scope, "Column");
    public static readonly AttributeName Scope_Both = new(AttributeKey.Scope, "Both");

    public static readonly AttributeName BorderStyle_None = new(AttributeKey.BorderStyle, "None");
    public static readonly AttributeName BorderStyle_Hidden = new(AttributeKey.BorderStyle, "Hidden");
    public static readonly AttributeName BorderStyle_Dotted = new(AttributeKey.BorderStyle, "Dotted");
    public static readonly AttributeName BorderStyle_Dashed = new(AttributeKey.BorderStyle, "Dashed");
    public static readonly AttributeName BorderStyle_Solid = new(AttributeKey.BorderStyle, "Solid");
    public static readonly AttributeName BorderStyle_Double = new(AttributeKey.BorderStyle, "Double");
    public static readonly AttributeName BorderStyle_Groove = new(AttributeKey.BorderStyle, "Groove");
    public static readonly AttributeName BorderStyle_Ridge = new(AttributeKey.BorderStyle, "Ridge");
    public static readonly AttributeName BorderStyle_Inset = new(AttributeKey.BorderStyle, "Inset");
    public static readonly AttributeName BorderStyle_Outset = new(AttributeKey.BorderStyle, "Outset");

    /// <summary>The full PDF name (e.g. "Block"). Matches the name written
    /// to the attribute dictionary.</summary>
    public override string ToString() => Value;

    private static readonly Dictionary<(AttributeKey, string), AttributeName> _byKeyValue = BuildLookup();

    private static Dictionary<(AttributeKey, string), AttributeName> BuildLookup()
    {
        var map = new Dictionary<(AttributeKey, string), AttributeName>();
        foreach (var f in typeof(AttributeName).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.GetValue(null) is AttributeName n)
                map[(n.Key, n.Value)] = n;
        return map;
    }

    /// <summary>The predefined name singleton for <paramref name="key"/> with
    /// PDF name <paramref name="value"/>, or null when no such name is known.</summary>
    internal static AttributeName? Find(AttributeKey key, string value)
        => _byKeyValue.TryGetValue((key, value), out var n) ? n : null;
}

/// <summary>Per-key metadata: owner and which value kinds are permitted.</summary>
internal static class AttributeMeta
{
    internal static AttributeOwnerStandard OwnerOf(AttributeKey key) => key switch
    {
        AttributeKey.RowSpan or AttributeKey.ColSpan or AttributeKey.Headers
            or AttributeKey.Scope or AttributeKey.Summary => AttributeOwnerStandard.Table,
        AttributeKey.ListNumbering => AttributeOwnerStandard.List,
        _ => AttributeOwnerStandard.Layout,
    };

    /// <summary>Whether the key accepts a number value.</summary>
    internal static bool AllowsNumber(AttributeKey key) => key switch
    {
        AttributeKey.SpaceBefore or AttributeKey.SpaceAfter or AttributeKey.StartIndent
            or AttributeKey.EndIndent or AttributeKey.TextIndent or AttributeKey.Width
            or AttributeKey.Height or AttributeKey.LineHeight or AttributeKey.BaselineShift
            or AttributeKey.TextDecorationThickness or AttributeKey.BorderThickness
            or AttributeKey.RowSpan or AttributeKey.ColSpan => true,
        _ => false,
    };

    /// <summary>Required fixed length for an array-valued key, or 0 if the
    /// key isn't array-valued / has no fixed length.</summary>
    internal static int RequiredArrayLength(AttributeKey key) => key switch
    {
        AttributeKey.Color or AttributeKey.BackgroundColor or AttributeKey.BorderColor
            or AttributeKey.TextDecorationColor => 3,
        AttributeKey.BBox => 4,
        _ => 0,
    };
}

/// <summary>
/// A single tagged-PDF structure attribute (key plus one typed value).
/// Setting a value of one kind clears any previously set value, so an
/// attribute always carries at most one value. Invalid combinations throw
/// <see cref="TaggedException"/>.
/// </summary>
public sealed class StructureAttribute
{
    private enum Kind { None, Name, Number, Array, Color, String }

    private Kind _kind;
    private AttributeName? _name;
    private double? _number;
    private double?[]? _array;
    private string? _string;

    /// <summary>The attribute key.</summary>
    public AttributeKey Key { get; }

    internal AttributeOwnerStandard Owner => AttributeMeta.OwnerOf(Key);

    internal bool HasValue => _kind != Kind.None;

    /// <summary>Create an attribute for <paramref name="key"/> with no value yet.</summary>
    public StructureAttribute(AttributeKey key)
    {
        Key = key;
    }

    private void Clear()
    {
        _name = null;
        _number = null;
        _array = null;
        _string = null;
    }

    /// <summary>Set a /Name value. Throws if the name isn't valid for this key.</summary>
    public void SetNameValue(AttributeName name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (name.Key != Key)
            throw new TaggedException($"For attribute {Key} doesn't allow to set {name.Value} value");
        Clear();
        _kind = Kind.Name;
        _name = name;
    }

    /// <summary>Set a numeric value. Throws if the key isn't number-valued.</summary>
    public void SetNumberValue(double value)
    {
        if (!AttributeMeta.AllowsNumber(Key))
            throw new TaggedException($"{Key} doesn't allow to set Number value");
        Clear();
        _kind = Kind.Number;
        _number = value;
    }

    /// <summary>Set an array-of-number value. Throws if the key requires a
    /// specific length and <paramref name="value"/> doesn't match it.</summary>
    public void SetArrayNumberValue(double?[] value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        var required = AttributeMeta.RequiredArrayLength(Key);
        if (required > 0 && value.Length != required)
            throw new TaggedException($"Array must contain {required} elements");
        Clear();
        _kind = Kind.Array;
        _array = value;
    }

    /// <summary>Set a colour value (stored as a 3-component RGB array).</summary>
    public void SetColorValue(Aspose.Pdf.Color color)
    {
        if (color is null) throw new ArgumentNullException(nameof(color));
        Clear();
        _kind = Kind.Array;
        _array = ColorToArray(color);
    }

    /// <summary>Set a string value.</summary>
    public void SetStringValue(string value)
    {
        Clear();
        _kind = Kind.String;
        _string = value;
    }

    /// <summary>The /Name value, or null when the attribute holds another kind.</summary>
    public AttributeName? GetNameValue() => _kind == Kind.Name ? _name : null;

    /// <summary>The numeric value, or null when the attribute holds another kind.</summary>
    public double? GetNumberValue() => _kind == Kind.Number ? _number : null;

    /// <summary>The array-of-number value, or null when the attribute holds another kind.</summary>
    public double?[]? GetArrayNumberValue() => _kind == Kind.Array ? _array : null;

    /// <summary>The string value, or null when the attribute holds another kind.</summary>
    public string? GetStringValue() => _kind == Kind.String ? _string : null;

    private static double?[] ColorToArray(Aspose.Pdf.Color color)
    {
        var rgb = color.ToRgb();
        return new double?[] { rgb.R / 255.0, rgb.G / 255.0, rgb.B / 255.0 };
    }

    /// <summary>The value as the object an attribute dictionary carries for this key; null
    /// while no value has been set.</summary>
    internal PdfObject? ToPdf()
    {
        switch (_kind)
        {
            case Kind.Name:
                return new PdfName(_name!.Value);
            case Kind.Number:
                var number = _number!.Value;
                return number == Math.Floor(number) ? new PdfInteger((long)number) : new PdfReal(number);
            case Kind.Array:
                var array = new PdfArray();
                foreach (var item in _array!)
                    array.Add(item is { } n ? new PdfReal(n) : PdfNull.Instance);
                return array;
            case Kind.String:
                return new PdfString(System.Text.Encoding.UTF8.GetBytes(_string ?? string.Empty));
            default:
                return null;
        }
    }

    /// <summary>Build an attribute for <paramref name="key"/> from the raw
    /// PDF value found in an attribute dictionary, or null when the value
    /// can't be interpreted as a known kind. Bypasses the authoring-side
    /// validation since the value already lives in the document.</summary>
    internal static StructureAttribute? FromPdf(AttributeKey key, PdfObject? value)
    {
        var attr = new StructureAttribute(key);
        switch (value)
        {
            case PdfName name:
                var known = AttributeName.Find(key, name.Value);
                if (known is null) return null;
                attr._kind = Kind.Name;
                attr._name = known;
                return attr;
            case PdfInteger i:
                attr._kind = Kind.Number;
                attr._number = i.Value;
                return attr;
            case PdfReal r:
                attr._kind = Kind.Number;
                attr._number = r.Value;
                return attr;
            case PdfArray arr:
                var nums = new double?[arr.Count];
                for (var n = 0; n < arr.Count; n++)
                    nums[n] = AsNumber(arr[n]);
                attr._kind = Kind.Array;
                attr._array = nums;
                return attr;
            case PdfString s:
                attr._kind = Kind.String;
                attr._string = s.ToText();
                return attr;
            default:
                return null;
        }
    }

    private static double? AsNumber(PdfObject? obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };
}

/// <summary>
/// An owner-scoped set of <see cref="StructureAttribute"/> objects attached
/// to a structure element (one PDF attribute dictionary with a fixed /O owner).
/// </summary>
public sealed class StructureAttributes
{
    private readonly Dictionary<AttributeKey, StructureAttribute> _attributes = new();

    /// <summary>The attribute dictionary this set was read from, so the entries it does not
    /// model survive a rewrite; null for a set created by the author.</summary>
    private PdfDictionary? _source;

    /// <summary>Told after every change an author makes to the set.</summary>
    private readonly Action? _changed;

    /// <summary>The owner (/O) all attributes in this set belong to.</summary>
    internal AttributeOwnerStandard Owner { get; }

    internal StructureAttributes(AttributeOwnerStandard owner, Action? changed = null)
    {
        Owner = owner;
        _changed = changed;
    }

    /// <summary>Get the attribute for <paramref name="key"/>, or null.</summary>
    public StructureAttribute? GetAttribute(AttributeKey key)
        => _attributes.TryGetValue(key, out var a) ? a : null;

    /// <summary>Add or replace an attribute. Throws if the attribute has no
    /// value, or its key belongs to a different owner than this set.</summary>
    public void SetAttribute(StructureAttribute attribute)
    {
        if (attribute is null) throw new ArgumentNullException(nameof(attribute));
        if (!attribute.HasValue)
            throw new TaggedException("Attribute value was not initialized");
        var attrOwner = attribute.Owner;
        if (attrOwner != Owner)
            throw new TaggedException($"Attribute owner is '{attrOwner}'. But must be '{Owner}'");
        _attributes[attribute.Key] = attribute;
        _changed?.Invoke();
    }

    /// <summary>Drop the attribute for <paramref name="key"/>, if the set holds one.</summary>
    internal void Remove(AttributeKey key)
    {
        var held = _attributes.Remove(key);
        var stated = _source?.Remove(key.ToString()) ?? false;
        if (held || stated) _changed?.Invoke();
    }

    /// <summary>Add an attribute parsed from the document, without the
    /// owner/value validation the authoring-side <see cref="SetAttribute"/>
    /// applies (the value is already present in the file).</summary>
    internal void AddParsed(StructureAttribute attribute, PdfDictionary source)
    {
        _attributes[attribute.Key] = attribute;
        _source = source;
    }

    /// <summary>The set as one attribute dictionary: the dictionary it was read from, if any,
    /// with the attributes held here written over it. Null when there is nothing to write.</summary>
    internal PdfDictionary? ToPdf()
    {
        if (_attributes.Count == 0 && _source is null) return null;
        var dict = new PdfDictionary();
        if (_source is not null)
            foreach (var key in _source.Keys)
                dict.Set(key, _source.Get(key)!);
        dict.Set("O", new PdfName(Owner.ToString()));
        foreach (var (key, attribute) in _attributes)
            if (attribute.ToPdf() is { } value) dict.Set(key.ToString(), value);
        return dict;
    }
}

/// <summary>
/// The attribute manager exposed by <see cref="StructureElement.Attributes"/>.
/// Holds one <see cref="StructureAttributes"/> set per owner.
/// </summary>
public sealed class StructureElementAttributes
{
    private readonly Dictionary<AttributeOwnerStandard, StructureAttributes> _byOwner = new();

    /// <summary>The element dictionary the sets belong to and are written back to.</summary>
    private readonly PdfDictionary _element;
    private readonly PdfReader? _reader;

    /// <summary>The attribute sets of one structure element, read from its /A entry so
    /// attributes an existing document states are visible through <see cref="GetAttributes"/>.
    /// Every change an author makes to a set is written straight back to that entry: the
    /// element dictionary is the object the document saves, so the entry is kept current
    /// rather than assembled at save time.</summary>
    internal StructureElementAttributes(PdfDictionary element, PdfReader? reader)
    {
        _element = element;
        _reader = reader;
        Parse();
    }

    private PdfObject? Resolve(PdfObject? obj) => _reader?.Resolve(obj) ?? obj;

    private void Parse()
    {
        switch (Resolve(_element.Get("A")))
        {
            case PdfDictionary single:
                ParseAttributeObject(single);
                break;
            case PdfArray arr:
                foreach (var item in arr)
                    if (Resolve(item) is PdfDictionary ad)
                        ParseAttributeObject(ad);
                break;
        }
    }

    private void ParseAttributeObject(PdfDictionary attrDict)
    {
        var ownerName = attrDict.GetName("O");
        if (TryParseOwner(ownerName) is not { } owner) return;
        var set = GetOrCreate(owner);
        foreach (var key in attrDict.Keys)
        {
            if (key == "O") continue;
            if (!Enum.TryParse<AttributeKey>(key, ignoreCase: false, out var attrKey)) continue;
            var attr = StructureAttribute.FromPdf(attrKey, Resolve(attrDict.Get(key)));
            if (attr is not null) set.AddParsed(attr, attrDict);
        }
    }

    /// <summary>Rewrite the element's /A entry from the sets held here. An attribute
    /// dictionary whose owner no set models is carried over as it was.</summary>
    private void Write()
    {
        var entries = new List<PdfObject>();
        var existing = _element.Get("A");
        foreach (var entry in Resolve(existing) is PdfArray array ? array : existing is null ? [] : [existing])
        {
            if (entry is null || Resolve(entry) is not PdfDictionary dict) continue;
            if (TryParseOwner(dict.GetName("O")) is { } owner && _byOwner.ContainsKey(owner)) continue;
            entries.Add(entry);
        }
        foreach (var set in _byOwner.Values)
            if (set.ToPdf() is { } dict) entries.Add(dict);

        switch (entries.Count)
        {
            case 0:
                _element.Remove("A");
                break;
            case 1:
                _element.Set("A", entries[0]);
                break;
            default:
                var all = new PdfArray();
                foreach (var entry in entries) all.Add(entry);
                _element.Set("A", all);
                break;
        }
    }

    private static AttributeOwnerStandard? TryParseOwner(string? name)
        => Enum.TryParse(name, ignoreCase: false, out AttributeOwnerStandard owner)
           && Enum.IsDefined(typeof(AttributeOwnerStandard), owner) ? owner : null;

    private StructureAttributes GetOrCreate(AttributeOwnerStandard owner)
        => _byOwner.TryGetValue(owner, out var a) ? a : CreateAttributes(owner);

    /// <summary>The attribute set for <paramref name="owner"/>, creating
    /// an empty one on first access so callers can add attributes without
    /// an explicit <see cref="CreateAttributes"/> call.</summary>
    public StructureAttributes GetAttributes(AttributeOwnerStandard owner)
        => _byOwner.TryGetValue(owner, out var a) ? a : CreateAttributes(owner);

    /// <summary>Create (and store) an empty attribute set for <paramref name="owner"/>.</summary>
    public StructureAttributes CreateAttributes(AttributeOwnerStandard owner)
    {
        var a = new StructureAttributes(owner, Write);
        _byOwner[owner] = a;
        return a;
    }
}
