using System.Collections;
using System.Collections.Generic;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Annotations;

/// <summary>Caption-position within a <see cref="LineAnnotation"/>.</summary>
public enum CaptionPosition
{
    Inline,
    Top,
}

/// <summary>Measure-units metadata attached to a <see cref="LineAnnotation"/>
/// (PDF 32000 §12.5.6.13 measure dictionaries). Stored only — the FOSS
/// renderer does not interpret measure entries at write time.</summary>
public class Measure
{
    /// <summary>Nested number-format list (Aspose.PDF for .NET shape:
    /// <c>Measure+NumberFormatList</c>). Backed by an in-memory list.</summary>
    public class NumberFormatList
    {
        private readonly List<NumberFormat> _items = new();
        private readonly Measure? _measure;

        public NumberFormatList() { }
        public NumberFormatList(Measure measure) { _measure = measure; }

        public int Count => _items.Count;

        // 1-based indexer to match the Aspose.Pdf collection convention.
        public NumberFormat this[int index]
        {
            get => _items[index - 1];
            set => _items[index - 1] = value;
        }

        public void Add(NumberFormat value) => _items.Add(value);
        public void Insert(int index, NumberFormat value) => _items.Insert(index, value);
        public void RemoveAt(int index) => _items.RemoveAt(index);
    }

    /// <summary>One number-format entry — describes how a measurement value
    /// is rendered (precision, separators, before/after text). Stored only.</summary>
    public class NumberFormat
    {
        public NumberFormat(Measure measure) { Owner = measure; }

        internal Measure? Owner { get; }

        public string AfterText { get; set; } = string.Empty;
        public string BeforeText { get; set; } = string.Empty;
        /// <summary>Conversion factor; legacy spelling preserved (Convresion, not Conversion).</summary>
        public double ConvresionFactor { get; set; } = 1.0;
        public int Denominator { get; set; } = 1;
        public bool ForceDenominator { get; set; }
        public FractionStyle FractionDisplayment { get; set; } = FractionStyle.ShowAsDecimal;
        public string FractionSeparator { get; set; } = "/";
        public int Precision { get; set; } = 2;
        public string ThousandsSeparator { get; set; } = ",";
        public string UnitLabel { get; set; } = string.Empty;

        /// <summary>How fractional values are rendered (decimal / fraction / round / truncate).</summary>
        public enum FractionStyle
        {
            ShowAsDecimal = 0,
            ShowAsFraction = 1,
            Round = 2,
            Truncate = 3,
        }
    }

    /// <summary>Construct a Measure bound to <paramref name="annotation"/>,
    /// populating the scale ratio and number-format lists from its /Measure
    /// dictionary (PDF 32000 §12.5.6.10) when present.</summary>
    public Measure(Annotation annotation)
    {
        Annotation = annotation;
        ParseFromAnnotation();
    }

    private void ParseFromAnnotation()
    {
        if (Annotation is null) return;
        var reader = Annotation.InternalReader;
        var annotDict = Annotation.Dict;
        if (reader is null || annotDict is null) return;
        if (reader.Resolve(annotDict.Get("Measure")) is not PdfDictionary m) return;

        if (reader.Resolve(m.Get("R")) is PdfString r) ScaleRatio = r.ToText();

        NumberFormatList ReadFormats(PdfObject? arrObj)
        {
            var list = new NumberFormatList(this);
            if (reader.Resolve(arrObj) is PdfArray arr)
                foreach (var item in arr)
                    if (reader.Resolve(item) is PdfDictionary nf)
                    {
                        var f = new NumberFormat(this);
                        if (reader.Resolve(nf.Get("U")) is PdfString u) f.UnitLabel = u.ToText();
                        f.ConvresionFactor = reader.Resolve(nf.Get("C")) switch
                        {
                            PdfInteger ci => ci.Value,
                            PdfReal cr => cr.Value,
                            _ => f.ConvresionFactor,
                        };
                        if (reader.Resolve(nf.Get("RD")) is PdfString rd) f.FractionSeparator = rd.ToText();
                        if (reader.Resolve(nf.Get("SS")) is PdfString ss) f.ThousandsSeparator = ss.ToText();
                        if (reader.Resolve(nf.Get("RT")) is PdfString rt) f.BeforeText = rt.ToText();
                        list.Add(f);
                    }
            return list;
        }

        DistanceFormat = ReadFormats(m.Get("D"));
        AreaFormat = ReadFormats(m.Get("A"));
        AngleFormat = ReadFormats(m.Get("T"));
        SlopeFormat = ReadFormats(m.Get("S"));
        XFormat = ReadFormats(m.Get("X"));
        YFormat = ReadFormats(m.Get("Y"));
    }

    /// <summary>The annotation this Measure decorates.</summary>
    public Annotation? Annotation { get; }

    /// <summary>Scale-ratio string (e.g. <c>"1 in = 10 ft"</c>).</summary>
    public string ScaleRatio { get; set; } = string.Empty;

    /// <summary>X/Y aspect-ratio factor (non-isotropic measure systems).</summary>
    public double XYFactor { get; set; } = 1.0;

    /// <summary>Origin point in user space for measure calculations.</summary>
    public Aspose.Pdf.Point Origin { get; set; } = new Aspose.Pdf.Point(0, 0);

    /// <summary>Linear-distance number-format list.</summary>
    public NumberFormatList DistanceFormat { get; set; } = new NumberFormatList();

    /// <summary>Area number-format list.</summary>
    public NumberFormatList AreaFormat { get; set; } = new NumberFormatList();

    /// <summary>Angle number-format list.</summary>
    public NumberFormatList AngleFormat { get; set; } = new NumberFormatList();

    /// <summary>Slope number-format list.</summary>
    public NumberFormatList SlopeFormat { get; set; } = new NumberFormatList();

    /// <summary>X-coordinate number-format list.</summary>
    public NumberFormatList XFormat { get; set; } = new NumberFormatList();

    /// <summary>Y-coordinate number-format list.</summary>
    public NumberFormatList YFormat { get; set; } = new NumberFormatList();
}

/// <summary>Per-event PDF action slots for a widget annotation
/// (matches the Aspose.PDF for .NET /AA-tree entry shape: 14 named events).
/// Each slot is stored only in this build — the FOSS renderer doesn't
/// dispatch widget actions.</summary>
public class AnnotationActionCollection
{
    public PdfAction? OnActivated { get; set; }
    public PdfAction? OnCalculate { get; set; }
    public PdfAction? OnClosePage { get; set; }
    public PdfAction? OnEnter { get; set; }
    public PdfAction? OnExit { get; set; }
    public PdfAction? OnFormat { get; set; }
    public PdfAction? OnHidePage { get; set; }
    public PdfAction? OnLostFocus { get; set; }
    public PdfAction? OnModifyCharacter { get; set; }
    public PdfAction? OnOpenPage { get; set; }
    public PdfAction? OnPressMouseBtn { get; set; }
    public PdfAction? OnReceiveFocus { get; set; }
    public PdfAction? OnReleaseMouseBtn { get; set; }
    public PdfAction? OnShowPage { get; set; }
    public PdfAction? OnValidate { get; set; }
}

/// <summary>Collection of <see cref="PdfAction"/> entries attached to an
/// annotation (or any other action-bearing PDF object). Indexed by
/// 1-based position to match Aspose.PDF for .NET.</summary>
public class PdfActionCollection : IEnumerable<PdfAction>
{
    private readonly List<PdfAction> _actions = new();

    /// <summary>Number of actions in the collection.</summary>
    public int Count => _actions.Count;

    /// <summary>1-based indexer for the action at <paramref name="index"/>.</summary>
    public PdfAction this[int index]
    {
        get
        {
            if (index < 1 || index > _actions.Count)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            return _actions[index - 1];
        }
    }

    /// <summary>Append <paramref name="action"/> to the collection.</summary>
    public void Add(PdfAction action)
    {
        if (action is null) throw new System.ArgumentNullException(nameof(action));
        _actions.Add(action);
    }

    /// <summary>Remove the action at the given 1-based index.</summary>
    public void Delete(int index)
    {
        if (index < 1 || index > _actions.Count) return;
        _actions.RemoveAt(index - 1);
    }

    /// <inheritdoc />
    public IEnumerator<PdfAction> GetEnumerator() => _actions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Appearance-stream dictionary on an annotation (/AP entry):
/// maps appearance-state name -> <see cref="XForm"/>. Implements the full
/// <see cref="IDictionary{TKey,TValue}"/> shape for Aspose.PDF for .NET parity.
/// The FOSS implementation backs the dictionary in memory; round-trip
/// to the /AP entry is not currently emitted at save time.</summary>
public class AppearanceDictionary : IDictionary<string, XForm>
{
    private readonly Dictionary<string, XForm> _entries = new(System.StringComparer.Ordinal);

    /// <summary>Number of appearance states.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <summary>Always false (the FOSS dict is mutable).</summary>
    public bool IsFixedSize => false;

    /// <summary>Always false: callers serialise their own access.</summary>
    public bool IsSynchronized => false;

    /// <summary>Sentinel object for ICollection.SyncRoot-style locking.</summary>
    public object SyncRoot { get; } = new();

    /// <inheritdoc />
    public ICollection<string> Keys => _entries.Keys;

    /// <inheritdoc />
    public ICollection<XForm> Values => _entries.Values;

    /// <inheritdoc />
    public XForm this[string key]
    {
        get => _entries[key];
        set => _entries[key] = value;
    }

    /// <inheritdoc />
    public void Add(string key, XForm value) => _entries.Add(key, value);

    /// <summary>Add via key/value pair (IDictionary contract).</summary>
    public void Add(KeyValuePair<string, XForm> item) => _entries.Add(item.Key, item.Value);

    /// <summary>Add via object/object (loose-typed). Throws when the
    /// arguments aren't a (string, XForm) pair.</summary>
    public void Add(object key, object value)
    {
        if (key is not string s) throw new System.ArgumentException("Key must be a string.", nameof(key));
        if (value is not XForm x) throw new System.ArgumentException("Value must be an XForm.", nameof(value));
        _entries.Add(s, x);
    }

    /// <inheritdoc />
    public void Clear() => _entries.Clear();

    /// <inheritdoc />
    public bool Contains(KeyValuePair<string, XForm> item)
        => _entries.TryGetValue(item.Key, out var v)
           && ReferenceEquals(v, item.Value);

    /// <inheritdoc />
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<string, XForm>[] array, int arrayIndex)
    {
        foreach (var kv in _entries) array[arrayIndex++] = kv;
    }

    /// <summary>Copy just the appearance XForms into <paramref name="array"/>
    /// (the value-only CopyTo overload).</summary>
    public void CopyTo(XForm[] array, int index)
    {
        foreach (var kv in _entries) array[index++] = kv.Value;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, XForm>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Remove(string key) => _entries.Remove(key);

    /// <inheritdoc />
    public bool Remove(KeyValuePair<string, XForm> item)
        => Contains(item) && _entries.Remove(item.Key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out XForm value)
    {
        if (_entries.TryGetValue(key, out var v)) { value = v; return true; }
        value = null!;
        return false;
    }
}
