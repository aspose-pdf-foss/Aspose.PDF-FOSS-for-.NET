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
    /// <summary>Nested number-format list (public-API shape:
    /// <c>Measure+NumberFormatList</c>). Backed by an in-memory list.</summary>
    public class NumberFormatList
    {
        private readonly List<NumberFormat> _items = new();
        private readonly Measure? _measure;

        public NumberFormatList() { }
        public NumberFormatList(Measure measure) { _measure = measure; }

        public int Count => _items.Count;

        // 1-based indexer to match the public collection convention.
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
/// (matches the public /AA-tree entry shape: 14 named events plus
/// the direct /A activation action). When bound to an annotation dictionary
/// (the normal case) each setter writes through to the live /AA tree (or /A for
/// <see cref="OnActivated"/>), so assignments survive save/reload. The FOSS
/// renderer does not dispatch widget actions, but the dictionary round-trips.</summary>
public class AnnotationActionCollection
{
    private readonly Dictionary<string, PdfAction?> _slots = new(System.StringComparer.Ordinal);
    private Aspose.Pdf.Core.PdfDictionary? _owner;
    private Aspose.Pdf.IO.PdfReader? _reader;

    // /AA event key for each property (OnActivated is the direct /A, handled separately).
    private const string ActivatedKey = "A";

    /// <summary>Bind this collection to the owning annotation dict so setters
    /// write through to /A and /AA (reader may be null for freshly created annotations).</summary>
    internal void Bind(Aspose.Pdf.Core.PdfDictionary owner, Aspose.Pdf.IO.PdfReader? reader)
    {
        _owner = owner;
        _reader = reader;
    }

    /// <summary>Populate a slot during read-back WITHOUT writing through (the
    /// value already lives in the dictionary).</summary>
    internal void Load(string key, PdfAction? action) => _slots[key] = action;

    private PdfAction? Get(string key) => _slots.TryGetValue(key, out var v) ? v : null;

    private void Set(string key, PdfAction? value)
    {
        _slots[key] = value;
        if (_owner is null) return;

        if (key == ActivatedKey)
        {
            if (value is null) _owner.Remove("A");
            else _owner.Set("A", value.Dict);
            return;
        }

        var aa = _reader is not null
            ? _reader.ResolveDict(_owner.Get("AA"))
            : _owner.Get("AA") as Aspose.Pdf.Core.PdfDictionary;
        if (value is null)
        {
            aa?.Remove(key);
            if (aa is not null && aa.Count == 0) _owner.Remove("AA");
            return;
        }
        if (aa is null)
        {
            aa = new Aspose.Pdf.Core.PdfDictionary();
            _owner.Set("AA", aa);
        }
        aa.Set(key, value.Dict);
    }

    public PdfAction? OnActivated { get => Get(ActivatedKey); set => Set(ActivatedKey, value); }
    public PdfAction? OnCalculate { get => Get("C"); set => Set("C", value); }
    public PdfAction? OnClosePage { get => Get("PC"); set => Set("PC", value); }
    public PdfAction? OnEnter { get => Get("E"); set => Set("E", value); }
    public PdfAction? OnExit { get => Get("X"); set => Set("X", value); }
    public PdfAction? OnFormat { get => Get("F"); set => Set("F", value); }
    public PdfAction? OnHidePage { get => Get("PI"); set => Set("PI", value); }
    public PdfAction? OnLostFocus { get => Get("Bl"); set => Set("Bl", value); }
    public PdfAction? OnModifyCharacter { get => Get("K"); set => Set("K", value); }
    public PdfAction? OnOpenPage { get => Get("PO"); set => Set("PO", value); }
    public PdfAction? OnPressMouseBtn { get => Get("D"); set => Set("D", value); }
    public PdfAction? OnReceiveFocus { get => Get("Fo"); set => Set("Fo", value); }
    public PdfAction? OnReleaseMouseBtn { get => Get("U"); set => Set("U", value); }
    public PdfAction? OnShowPage { get => Get("PV"); set => Set("PV", value); }
    public PdfAction? OnValidate { get => Get("V"); set => Set("V", value); }
}

/// <summary>Collection of <see cref="PdfAction"/> entries attached to an
/// annotation (or any other action-bearing PDF object). Indexed by
/// 1-based position to match the public API.</summary>
public class PdfActionCollection : IEnumerable<PdfAction>
{
    private readonly List<PdfAction> _actions = new();
    private Aspose.Pdf.Core.PdfDictionary? _owner;

    /// <summary>Number of actions in the collection.</summary>
    public int Count => _actions.Count;

    /// <summary>Bind this collection to the owning annotation dict so that <see cref="Add"/> /
    /// <see cref="Delete"/> write the action through to the dict's /A (+ /Next chain), and thus
    /// survive save. Called after the collection is populated from the dict, so read-back does not
    /// re-persist.</summary>
    internal void Bind(Aspose.Pdf.Core.PdfDictionary owner) => _owner = owner;

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
        Persist();
    }

    /// <summary>Remove the action at the given 1-based index.</summary>
    public void Delete(int index)
    {
        if (index < 1 || index > _actions.Count) return;
        _actions.RemoveAt(index - 1);
        Persist();
    }

    // Write the action list onto the bound annotation dict: first action as /A, the rest chained
    // via /Next (mirroring ActionCollection), so the actions round-trip through save/reload.
    private void Persist()
    {
        if (_owner is null) return;
        if (_actions.Count == 0) { _owner.Remove("A"); return; }
        _owner.Set("A", _actions[0].Dict);
        for (int i = 0; i < _actions.Count; i++)
        {
            if (i < _actions.Count - 1) _actions[i].Dict.Set("Next", _actions[i + 1].Dict);
            else _actions[i].Dict.Remove("Next");
        }
    }

    /// <inheritdoc />
    public IEnumerator<PdfAction> GetEnumerator() => _actions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Appearance-stream dictionary on an annotation (/AP entry):
/// maps appearance-state name -> <see cref="XForm"/>. Implements the full
/// <see cref="IDictionary{TKey,TValue}"/> shape for public-API parity.
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

    /// <summary>Appearance-state lookup. Returns null for an absent state (e.g.
    /// a field with no /N appearance) rather than throwing, matching the public
    /// API where callers null-check the result.</summary>
    public XForm this[string key]
    {
        get => _entries.TryGetValue(key, out var v) ? v : null!;
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
