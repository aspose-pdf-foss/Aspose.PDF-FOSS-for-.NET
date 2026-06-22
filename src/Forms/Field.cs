using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Represents a form field. Implements IEnumerable&lt;Field&gt; to allow
/// iterating over child fields (Kids array in the PDF dictionary).
/// </summary>
public class Field : Aspose.Pdf.Annotations.WidgetAnnotation, IEnumerable<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;

    internal Field(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Create a field bound to a document. The field is not yet attached to the document's form — use <see cref="Form.Add(Field)"/> after configuring.</summary>
    public Field(Document doc) : base(doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        _dict = Dict;
        _reader = InternalReader;
        OwnerDocument = doc;
    }

    /// <summary>The PDF object number for this field's dictionary (-1 if unknown).</summary>
    internal int ObjectNumber { get; set; } = -1;

    /// <summary>The owning document (for dirty tracking during incremental save).</summary>
    internal Document? OwnerDocument { get; set; }

    /// <summary>The field type.</summary>
    public FieldType Type => DetermineType();

    /// <summary>The fully qualified field name.</summary>
    public new string? FullName => BuildFullName();

    /// <summary>Text-specific horizontal alignment override applied to
    /// the field's widget appearance. Stored only — parity
    /// with Annotation.TextHorizontalAlignment.</summary>
    public new Aspose.Pdf.HorizontalAlignment TextHorizontalAlignment { get; set; } = Aspose.Pdf.HorizontalAlignment.Left;

    /// <summary>
    /// 1-based index of the page that owns this field's widget. Resolved by
    /// walking the document's page tree and matching the dict referenced by
    /// the field's (or a parent's) /P entry, or by scanning each page's
    /// /Annots when /P is absent. Returns -1 when no owning page is found.
    /// </summary>
    public new int PageIndex
    {
        get
        {
            var idx = ResolvePageIndexFor(_dict);
            if (idx > 0) return idx;
            // Multi-widget terminal fields (e.g. a checkbox whose options live in
            // separate widget kids) keep /P on each widget, not on the field dict.
            // Fall back to the first widget kid's owning page.
            foreach (var kidDict in AllKids())
            {
                idx = ResolvePageIndexFor(kidDict);
                if (idx > 0) return idx;
            }
            return -1;
        }
    }

    /// <summary>1-based page index resolved from this field's OWN dict only
    /// (its /P or /Annots membership), without the multi-widget kid fallback that
    /// <see cref="PageIndex"/> applies. Returns -1 for a group/non-terminal field
    /// that has no widget of its own. Used to distinguish "on a page" from
    /// "merely has children on a page" in the form JSON export.</summary>
    internal int OwnPageIndex => ResolvePageIndexFor(_dict);

    /// <summary>Resolve the 1-based owning page index for a widget/field dict by
    /// its /P entry (walking /Parent), then by scanning each page's /Annots.
    /// Returns -1 when no owning page is found.</summary>
    private int ResolvePageIndexFor(PdfDictionary dict)
    {
        var pageDict = _reader.ResolveDict(dict.Get("P"));
        if (pageDict is null)
        {
            var parent = _reader.ResolveDict(dict.Get("Parent"));
            while (parent is not null && pageDict is null)
            {
                pageDict = _reader.ResolveDict(parent.Get("P"));
                parent = _reader.ResolveDict(parent.Get("Parent"));
            }
        }
        var pages = new PageCollection(_reader);
        if (pageDict is not null)
        {
            for (var i = 1; i <= pages.Count; i++)
                if (ReferenceEquals(pages[i].Dict, pageDict)) return i;
        }
        for (var i = 1; i <= pages.Count; i++)
        {
            var annots = _reader.Resolve(pages[i].Dict.Get("Annots")) as Core.PdfArray;
            if (annots is null) continue;
            foreach (var item in annots)
            {
                if (ReferenceEquals(_reader.ResolveDict(item), dict))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>The partial field name (/T).</summary>
    public string? PartialName
    {
        get => GetString("T");
        set
        {
            if (value is null)
                _dict.Remove("T");
            else
                _dict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
        }
    }

    /// <summary>
    /// The field's "Normal" appearance (the /AP /N entry). Returns an
    /// <see cref="XForm"/> wrapping the appearance stream so callers can
    /// inspect content-stream operators via <c>NormalAppearance.Contents</c>.
    ///
    /// Resolution order: this field's /AP/N first; if absent, walk into the
    /// single widget kid's /AP/N (terminal fields with one widget). When
    /// /AP/N is a state-keyed dict (checkbox /Yes vs /Off), the first
    /// non-Off stream entry is returned.
    /// </summary>
    public new XForm? NormalAppearance
    {
        get
        {
            var stream = ResolveNormalAppearanceStream(_dict);
            if (stream is null)
            {
                var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
                if (kids is { Count: 1 })
                {
                    var kidDict = _reader.ResolveDict(kids[0]);
                    if (kidDict is not null)
                        stream = ResolveNormalAppearanceStream(kidDict);
                }
            }
            return stream is null ? null : new XForm(stream, _reader);
        }
    }

    /// <summary>The field widget's appearance streams (the /AP entry), keyed by
    /// appearance name. State-keyed sub-dictionaries (e.g. checkbox /N with
    /// /Yes vs /Off) are flattened to compound keys like <c>"N.Yes"</c> /
    /// <c>"D.Off"</c>. Resolves this field's own /AP, falling back to a single
    /// widget kid's, matching <see cref="Aspose.Pdf.Annotations.Annotation.Appearance"/>.</summary>
    public new Aspose.Pdf.Annotations.AppearanceDictionary Appearance
    {
        get
        {
            var result = new Aspose.Pdf.Annotations.AppearanceDictionary();
            var ap = _reader.ResolveDict(_dict.Get("AP"));
            if (ap is null)
            {
                var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
                if (kids is { Count: 1 } && _reader.ResolveDict(kids[0]) is { } kidDict)
                    ap = _reader.ResolveDict(kidDict.Get("AP"));
            }
            if (ap is null) return result;

            foreach (var key in ap.Keys)
            {
                var obj = _reader.Resolve(ap.Get(key));
                if (obj is PdfStream stream)
                {
                    result[key] = new XForm(stream, _reader);
                }
                else if (obj is PdfDictionary stateDict)
                {
                    foreach (var stateKey in stateDict.Keys)
                        if (_reader.ResolveStream(stateDict.Get(stateKey)) is { } stStream)
                            result[key + "." + stateKey] = new XForm(stStream, _reader);
                }
            }
            return result;
        }
    }

    private PdfStream? ResolveNormalAppearanceStream(PdfDictionary dict)
    {
        var ap = _reader.ResolveDict(dict.Get("AP"));
        if (ap is null) return null;
        var nObj = _reader.Resolve(ap.Get("N"));
        if (nObj is PdfStream direct) return direct;
        if (nObj is PdfDictionary stateDict)
        {
            // /AP/N is a state-keyed dict (e.g. /Yes /Off for checkboxes).
            // Pick the on-state stream — anything that isn't /Off — falling
            // back to the first stream we find.
            PdfStream? firstAny = null;
            foreach (var key in stateDict.Keys)
            {
                var resolved = _reader.ResolveStream(stateDict.Get(key));
                if (resolved is null) continue;
                firstAny ??= resolved;
                if (key != "Off") return resolved;
            }
            return firstAny;
        }
        return null;
    }

    /// <summary>The action triggered when this field is activated (/A entry).
    /// Reads from the field dict; if absent and the field has a single
    /// widget kid, falls back to the kid's /A.</summary>
    public new Annotations.PdfAction? OnActivated
    {
        get
        {
            var actionDict = _reader.ResolveDict(_dict.Get("A"));
            if (actionDict is null)
            {
                // Fall back to the single widget kid (common case for non-radio fields)
                var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
                if (kids is { Count: 1 })
                {
                    var kid = _reader.ResolveDict(kids[0]);
                    actionDict = _reader.ResolveDict(kid?.Get("A"));
                }
            }
            return actionDict is not null ? Annotations.PdfAction.Create(actionDict, _reader) : null;
        }
        set
        {
            if (value is null) _dict.Remove("A");
            else _dict.Set("A", value.Dict);
        }
    }

    /// <summary>The field value (/V). Inherits from parent if not set on this dict.</summary>
    public virtual string? Value
    {
        get
        {
            var v = _reader.Resolve(_dict.Get("V"));
            if (v is null or PdfNull)
            {
                // Inherit /V from parent (e.g. radio button widget kids)
                var parent = _reader.ResolveDict(_dict.Get("Parent"));
                while (parent is not null)
                {
                    v = _reader.Resolve(parent.Get("V"));
                    if (v is not null and not PdfNull) break;
                    parent = _reader.ResolveDict(parent.Get("Parent"));
                }
            }
            return v switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
        }
        set
        {
            SetValue(value);
        }
    }

    /// <summary>
    /// Set the field value. For text fields, sets /V as a PdfString.
    /// For checkboxes, sets /V and /AS as a PdfName.
    /// </summary>
    protected virtual void SetValue(string? value)
    {
        if (value is null)
        {
            _dict.Set("V", PdfNull.Instance);
        }
        else
        {
            _dict.Set("V", EncodePdfTextString(value));
        }
        // Mark dirty for incremental save
        if (OwnerDocument is not null && ObjectNumber >= 0)
            OwnerDocument.MarkDirty(ObjectNumber, _dict);
    }

    /// <summary>
    /// Encode a string as a PDF text string: Latin1 for ASCII-safe text,
    /// UTF-16BE with BOM for text containing non-Latin1 characters.
    /// </summary>
    internal static PdfString EncodePdfTextString(string value)
    {
        // Check if all characters fit in Latin1 (0x00–0xFF)
        bool needsUnicode = false;
        foreach (char c in value)
        {
            if (c > 0xFF) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return new PdfString(System.Text.Encoding.Latin1.GetBytes(value));

        // UTF-16BE with BOM prefix (0xFE 0xFF)
        byte[] utf16 = System.Text.Encoding.BigEndianUnicode.GetBytes(value);
        byte[] withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        utf16.CopyTo(withBom, 2);
        return new PdfString(withBom);
    }

    // ── Child field iteration (Kids array) ────────────────────────────

    /// <summary>
    /// Number of kids in this field's /Kids array — either child fields
    /// (hierarchy nodes) or widget annotations (a terminal field with
    /// multiple visual widgets, such as a grouped checkbox/radio). Returns
    /// 0 if this is a single-widget leaf field with no /Kids.
    /// </summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (var _ in AllKids()) count++;
            return count;
        }
    }

    /// <summary>
    /// Internal: enumerate every kid in the /Kids array that resolves to a
    /// dictionary, in array order. Unlike <see cref="FieldKids"/>, this does
    /// not filter out pure widget annotations (kids without /T or /FT), so it
    /// surfaces the per-option widgets of a grouped checkbox/radio field.
    /// </summary>
    internal IEnumerable<PdfDictionary> AllKids()
    {
        var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
        if (kids is null) yield break;
        foreach (var kid in kids)
        {
            if (_reader.Resolve(kid) is PdfDictionary kidDict)
                yield return kidDict;
        }
    }

    /// <summary>
    /// Iterates widget annotations for this field's kids in the /Kids array.
    /// </summary>
    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetEnumerator()
    {
        // Yield the typed child field (a Field is itself a WidgetAnnotation) with
        // its Parent wired back to this group, so callers can cast to the concrete
        // field type and walk the parent relationship.
        foreach (var child in FieldKids())
        {
            child.Parent = this;
            yield return child;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Field-typed child kids of this field as a snapshot
    /// array — convenience accessor mirroring
    /// <see cref="Form.Fields"/> on the form. Each entry is a freshly
    /// materialised <see cref="Field"/> over the matching /Kids entry.</summary>
    public Field[] Fields => FieldKids().ToArray();

    /// <summary>
    /// Internal: enumerate the Field-typed child kids of this field. Skips pure
    /// widget annotations (kids without /T or /FT) and recreates each child via
    /// <see cref="Field.Create"/>.
    /// </summary>
    internal IEnumerable<Field> FieldKids()
    {
        var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
        if (kids is null) yield break;

        foreach (var kid in kids)
        {
            var kidDict = _reader.Resolve(kid) as PdfDictionary;
            if (kidDict is null) continue;
            if (!kidDict.ContainsKey("T") && !kidDict.ContainsKey("FT")) continue;

            var childField = Field.Create(kidDict, _reader);
            childField.OwnerDocument = OwnerDocument;
            yield return childField;
        }
    }

    // A Field is now itself a WidgetAnnotation, so the former implicit
    // Field -> WidgetAnnotation conversion is unnecessary (and illegal to a
    // base type); the inheritance relationship supplies it directly.

    /// <summary>
    /// Gets the widget annotation for the kid at the 1-based index.
    /// </summary>
    public Aspose.Pdf.Annotations.WidgetAnnotation this[int index]
    {
        get
        {
            int i = 1;
            foreach (var kidDict in AllKids())
            {
                if (i == index)
                    return new Aspose.Pdf.Annotations.WidgetAnnotation(kidDict, _reader);
                i++;
            }
            // A terminal field with no /Kids is merged with its single widget: the
            // field dict itself is that widget, addressable at index 1.
            if (i == 1 && index == 1 &&
                (_dict.GetName("Subtype") == "Widget" || _dict.ContainsKey("Rect")))
                return new Aspose.Pdf.Annotations.WidgetAnnotation(_dict, _reader);
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Index {index} is out of range. Must be between 1 and {Count}.");
        }
    }

    /// <summary>
    /// Gets the widget annotation for the kid whose partial name matches.
    /// Throws ArgumentException if not found.
    /// </summary>
    public Aspose.Pdf.Annotations.WidgetAnnotation this[string name]
    {
        get
        {
            foreach (var child in FieldKids())
            {
                if (string.Equals(child.PartialName, name, StringComparison.Ordinal))
                    return new Aspose.Pdf.Annotations.WidgetAnnotation(child.Dict, child.Reader);
            }
            throw new ArgumentException($"Kid field not found: {name}");
        }
    }

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>The default value (/DV).</summary>
    public string? DefaultValue => GetString("DV");

    /// <summary>The alternate field name / tooltip (/TU entry).</summary>
    public string? AlternateName
    {
        get => GetString("TU");
        set
        {
            if (value is null)
                Dict.Remove("TU");
            else
                Dict.Set("TU", EncodePdfTextString(value));
        }
    }



    /// <summary>The widget annotation flags (/F entry), e.g. Print / Hidden /
    /// ReadOnly. Matches the Aspose.PDF for .NET <c>Field.Flags</c> surface (which exposes
    /// the annotation flags, not the field /Ff flags).</summary>
    public new Aspose.Pdf.Annotations.AnnotationFlags Flags
    {
        get => (Aspose.Pdf.Annotations.AnnotationFlags)(int)_dict.GetInt("F");
        set => _dict.Set("F", new Aspose.Pdf.Core.PdfInteger((int)value));
    }

    /// <summary>The form-field flags (/Ff entry) — Required, Multiline, Combo,
    /// etc. Used internally to derive the typed Is* properties.</summary>
    internal int FieldFlags => (int)_dict.GetInt("Ff");

    /// <summary>
    /// Tab order index for this field. Returns -1 if not specified.
    /// The tab order is derived from the field's position in the page's /Annots array
    /// combined with the /Tabs entry on the page dictionary.
    /// When no page association can be determined, falls back to the /StructParent
    /// or the internal form index.
    /// </summary>
    public int TabOrder
    {
        set => _dict.Set("TI", new PdfInteger(value));
        get
        {
            // /TI (Tab Index) is a non-standard but common extension
            if (_dict.ContainsKey("TI"))
                return (int)_dict.GetInt("TI");

            // /StructParent gives a unique ordering index per annotation
            if (_dict.ContainsKey("StructParent"))
                return (int)_dict.GetInt("StructParent");

            // Walk the parent page's Annots array to find position
            var pageDict = FindPageDict();
            if (pageDict is not null)
            {
                var annotsObj = _reader.Resolve(pageDict.Get("Annots"));
                if (annotsObj is PdfArray annots)
                {
                    // Try by reference identity (1-based to match the public API contract).
                    for (int i = 0; i < annots.Count; i++)
                    {
                        var resolved = _reader.ResolveDict(annots[i]);
                        if (ReferenceEquals(resolved, _dict))
                            return i + 1;
                    }

                    // Fallback: match by partial name
                    var partialName = PartialName;
                    if (partialName is not null)
                    {
                        for (int i = 0; i < annots.Count; i++)
                        {
                            var resolved = _reader.ResolveDict(annots[i]);
                            if (resolved is null) continue;
                            var t = resolved.Get("T");
                            string? name = t switch
                            {
                                PdfString s => s.ToText(),
                                PdfName n => n.Value,
                                _ => null,
                            };
                            if (name == partialName)
                                return i + 1;
                        }
                    }
                }
            }

            return -1;
        }
    }

    private PdfDictionary? FindPageDict()
    {
        // The field's /P entry points to the page
        var pageRef = _reader.ResolveDict(_dict.Get("P"));
        if (pageRef is not null) return pageRef;

        // Walk parent chain looking for /P
        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            pageRef = _reader.ResolveDict(parent.Get("P"));
            if (pageRef is not null) return pageRef;
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }

        return null;
    }

    /// <summary>Whether the field is read-only.</summary>
    public bool IsReadOnly => (FieldFlags & 1) != 0;

    /// <summary>Whether the field is read-only (settable alias for API parity).</summary>
    public new bool ReadOnly
    {
        get => IsReadOnly;
        set
        {
            var flags = (int)_dict.GetInt("Ff");
            if (value)
                flags |= 1;
            else
                flags &= ~1;
            _dict.Set("Ff", new PdfInteger(flags));
        }
    }

    /// <summary>Whether the field is required.</summary>
    public bool IsRequired => (FieldFlags & 2) != 0;

    private Aspose.Pdf.Annotations.Characteristics? _characteristics;
    /// <summary>
    /// Appearance characteristics (border / background colors, widget rotation).
    /// Per published reference; backed by an <see cref="Aspose.Pdf.Annotations.Characteristics"/>
    /// instance lazily allocated on first access.
    /// </summary>
    public new Aspose.Pdf.Annotations.Characteristics Characteristics
        => _characteristics ??= new Aspose.Pdf.Annotations.Characteristics();

    private Aspose.Pdf.Annotations.DefaultAppearance? _defaultAppearance;
    /// <summary>
    /// Default appearance (font, size, colour) of the field — per published
    /// reference on <see cref="Aspose.Pdf.Annotations.WidgetAnnotation"/>.
    /// Lazily allocated; mutations on the returned instance are visible to
    /// subsequent reads through the same Field.
    /// </summary>
    public new Aspose.Pdf.Annotations.DefaultAppearance DefaultAppearance
    {
        get => _defaultAppearance ??= new Aspose.Pdf.Annotations.DefaultAppearance();
        set
        {
            _defaultAppearance = value;
            // Persist the /DA string immediately so an appearance regenerated
            // before the field is attached to a form (e.g. setting Value first)
            // already reflects this font/size/colour.
            if (value is not null)
                Dict.Set("DA", new PdfString(
                    System.Text.Encoding.Latin1.GetBytes(value.ToAppearanceString())));
            // When the field is already part of a form, embed an embeddable font
            // immediately (the font may be set after Form.Add); this re-points /DA
            // at the embedded resource. A no-op otherwise.
            Form.EmbedDefaultAppearanceFont(this);
        }
    }

    /// <summary>The field rectangle.</summary>
    public new Rectangle? Rect
    {
        get
        {
            var arr = _reader.Resolve(_dict.Get("Rect")) as PdfArray;
            if (arr is { Count: >= 4 }) return Rectangle.FromPdfArray(arr, _reader);
            // Field whose geometry lives on widget kids: return the bounding box (union)
            // of all descendant widget /Rects, so a multi-widget field reports its full
            // extent (matches Aspose.PDF for .NET Field.Rect).
            return UnionKidRect(_dict, 0);
        }
        set
        {
            if (value is null)
            {
                _dict.Remove("Rect");
            }
            else
            {
                var arr = new PdfArray();
                arr.Add(new PdfReal(value.LLX));
                arr.Add(new PdfReal(value.LLY));
                arr.Add(new PdfReal(value.URX));
                arr.Add(new PdfReal(value.URY));
                _dict.Set("Rect", arr);
            }
        }
    }

    /// <summary>Bounding box (union) of all descendant widget /Rects (depth-limited), or null.</summary>
    private Rectangle? UnionKidRect(PdfDictionary dict, int depth)
    {
        if (depth > 2) return null;
        var kids = _reader.Resolve(dict.Get("Kids")) as PdfArray;
        if (kids is null) return null;
        Rectangle? acc = null;
        foreach (var k in kids)
        {
            var kid = _reader.ResolveDict(k);
            if (kid is null) continue;
            Rectangle? r = _reader.Resolve(kid.Get("Rect")) is PdfArray { Count: >= 4 } ra
                ? Rectangle.FromPdfArray(ra, _reader)
                : UnionKidRect(kid, depth + 1);
            if (r is null) continue;
            acc = acc is null
                ? r
                : new Rectangle(
                    System.Math.Min(acc.LLX, r.LLX), System.Math.Min(acc.LLY, r.LLY),
                    System.Math.Max(acc.URX, r.URX), System.Math.Max(acc.URY, r.URY));
        }
        return acc;
    }

    // Field border is inherited from WidgetAnnotation.Border, which lazily
    // resolves a non-null Border from the widget's /BS dictionary (and writes
    // back through the setter). The previous `new Border? { get; set; }` shadow
    // here defaulted to null, so `field.Border.Width = 1` threw NRE.

    /// <summary>The field color (stored in /DA default appearance string as RGB).</summary>
    public new Color Color
    {
        get
        {
            var da = GetString("DA");
            if (da is null) return Color.Black;
            // Parse "r g b rg" or "r g b RG" from DA string. The colour operator
            // is normally the last token, so the scan must include the final index
            // (the match only reads tokens before i, so there's no overrun).
            var parts = da.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if ((parts[i] == "rg" || parts[i] == "RG") && i >= 3)
                {
                    if (double.TryParse(parts[i - 3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) &&
                        double.TryParse(parts[i - 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) &&
                        double.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
                        return Color.FromRgb(r, g, b);
                }
            }
            return Color.Black;
        }
        set
        {
            // Update /DA string with new color, preserving font info
            var da = GetString("DA") ?? "";
            var r = (value.R / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var g = (value.G / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var b = (value.B / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            // Remove existing color operators from DA
            var parts = da.Split(' ').ToList();
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if ((parts[i] == "rg" || parts[i] == "RG" || parts[i] == "g" || parts[i] == "G") && i >= 1)
                {
                    int numArgs = parts[i] == "rg" || parts[i] == "RG" ? 3 : 1;
                    int start = Math.Max(0, i - numArgs);
                    parts.RemoveRange(start, i - start + 1);
                    i = start;
                }
            }
            var newDa = string.Join(" ", parts).Trim();
            if (newDa.Length > 0) newDa += " ";
            newDa += $"{r} {g} {b} rg";
            _dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(newDa)));
        }
    }

    internal new PdfDictionary Dict => _dict;
    internal PdfReader Reader => _reader;

    /// <summary>Set the partial name (/T entry) of this field.</summary>
    internal void SetPartialName(string name)
    {
        _dict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
    }

    private FieldType DetermineType()
    {
        var ft = GetInheritedName("FT");
        var flags = (int)GetInheritedInt("Ff");

        // NB: Field now inherits Annotation.FieldType (a string /FT accessor), which
        // shadows the FieldType enum in expression position — so qualify the enum.
        return ft switch
        {
            "Tx" => Aspose.Pdf.Forms.FieldType.Text,
            "Btn" => (flags & (1 << 16)) != 0 ? Aspose.Pdf.Forms.FieldType.Button      // bit 17: Pushbutton
                   : (flags & (1 << 15)) != 0 ? Aspose.Pdf.Forms.FieldType.RadioButton // bit 16: Radio
                   : Aspose.Pdf.Forms.FieldType.CheckBox,
            // The /Ff combo bit (bit 18, 0-based 17) splits Ch into combo / list.
            // The form-JSON round-trip and facade callers rely on this so a re-imported
            // combo rebuilds as ComboBoxField, not ListBoxField.
            "Ch" => (flags & (1 << 17)) != 0 ? Aspose.Pdf.Forms.FieldType.ComboBox
                                              : Aspose.Pdf.Forms.FieldType.ListBox,
            "Sig" => Aspose.Pdf.Forms.FieldType.Signature,
            _ => Aspose.Pdf.Forms.FieldType.Unknown,
        };
    }

    private string? GetInheritedName(string key)
    {
        var name = _dict.GetName(key);
        if (name is not null) return name;

        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            name = parent.GetName(key);
            if (name is not null) return name;
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }
        return null;
    }

    private long GetInheritedInt(string key)
    {
        if (_dict.ContainsKey(key)) return _dict.GetInt(key);

        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            if (parent.ContainsKey(key)) return parent.GetInt(key);
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }
        return 0;
    }

    private string? BuildFullName()
    {
        var parts = new List<string>();
        var current = _dict;
        while (current is not null)
        {
            var t = current.Get("T");
            if (t is PdfString s)
                parts.Add(s.ToText());
            else if (t is PdfName n)
                parts.Add(n.Value);
            current = _reader.ResolveDict(current.Get("Parent"));
        }
        parts.Reverse();
        return parts.Count > 0 ? string.Join(".", parts) : null;
    }

    private new string? GetString(string key)
    {
        var obj = _reader.Resolve(_dict.Get(key));
        return obj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null,
        };
    }

    /// <summary>Build a PdfArray for a rectangle.</summary>
    internal static PdfArray MakeRectArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    internal static Field Create(PdfDictionary dict, PdfReader reader)
    {
        var ft = dict.GetName("FT");
        // Check parent for inherited FT
        if (ft is null)
        {
            var parent = reader.ResolveDict(dict.Get("Parent"));
            while (parent is not null && ft is null)
            {
                ft = parent.GetName("FT");
                parent = reader.ResolveDict(parent.Get("Parent"));
            }
        }

        return ft switch
        {
            "Tx" => CreateTextField(dict, reader),
            "Btn" => CreateButtonField(dict, reader),
            "Ch" => CreateChoiceField(dict, reader),
            "Sig" => new SignatureField(dict, reader),
            _ => new Field(dict, reader),
        };
    }

    private static TextBoxField CreateTextField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        return (flags & (1 << 25)) != 0
            ? new RichTextBoxField(dict, reader)
            : new TextBoxField(dict, reader);
    }

    private static Field CreateButtonField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        if ((flags & (1 << 16)) != 0) return new ButtonField(dict, reader); // bit 17: Pushbutton
        if ((flags & (1 << 15)) != 0) return new RadioButtonField(dict, reader); // bit 16: Radio
        return new CheckboxField(dict, reader);
    }

    private static ChoiceField CreateChoiceField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        if ((flags & (1 << 17)) != 0) return new ComboBoxField(dict, reader); // bit 18: Combo
        return new ListBoxField(dict, reader);
    }

    private static long GetInheritedInt(PdfDictionary dict, PdfReader reader, string key)
    {
        if (dict.ContainsKey(key)) return dict.GetInt(key);
        var parent = reader.ResolveDict(dict.Get("Parent"));
        while (parent is not null)
        {
            if (parent.ContainsKey(key)) return parent.GetInt(key);
            parent = reader.ResolveDict(parent.Get("Parent"));
        }
        return 0;
    }

    // ── Aspose.PDF for .NET shape additions ───────────────────────────────

    /// <summary>1-based index of the widget annotation used by AcroForm rendering when the field has multiple widgets. Stored only.</summary>
    public int AnnotationIndex { get; set; } = 1;

    /// <summary>Global flag: when true, a field's value is auto-resized to fit its
    /// widget rectangle. Static to match the Aspose.PDF for .NET surface (a process-wide default).
    /// Stored only.</summary>
    public static bool FitIntoRectangle { get; set; }

    /// <summary>Whether this field is a container/group of child fields.</summary>
    public bool IsGroup => Count > 0;

    private List<Field> GetKids()
    {
        var list = new List<Field>();
        foreach (var k in FieldKids()) list.Add(k);
        return list;
    }

    /// <summary>Whether this field belongs to a shared-fields group. Stored only.</summary>
    public bool IsSharedField { get; set; }

    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronization root; returns this instance.</summary>
    public object SyncRoot => this;

    /// <summary>Maximum font size used by the field's appearance auto-fit. Stored only.</summary>
    public double MaxFontSize { get; set; }

    /// <summary>Minimum font size used by the field's appearance auto-fit. Stored only.</summary>
    public double MinFontSize { get; set; }

    /// <summary>Mapping name used by external scripts to identify the field. Stored only.</summary>
    public string? MappingName
    {
        get
        {
            var obj = Dict.Get("TM");
            return obj is PdfString s ? s.ToText() : null;
        }
        set
        {
            if (value is null) Dict.Remove("TM");
            else Dict.Set("TM", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Copy widget annotations into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Aspose.Pdf.Annotations.WidgetAnnotation[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (OwnerDocument is null) return;
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            array[index + i] = new Aspose.Pdf.Annotations.WidgetAnnotation(OwnerDocument);
    }

    /// <summary>Copy this field's kids (as Field instances) into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Field[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            array[index + i] = kids[i];
    }

    /// <summary>Execute the supplied JavaScript action against this field. Stored intent only — the JS interpreter is not invoked.</summary>
    public void ExecuteFieldJavaScript(Aspose.Pdf.Annotations.JavascriptAction javaScriptAction)
    {
        _ = javaScriptAction;
    }

    /// <summary>Serialise this field to JSON (a single <see cref="FieldExportingData"/>
    /// object) via the supplied stream.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(Stream stream)
        => ExportToJson(stream, null);

    /// <summary>Serialise this field to a JSON file.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(string fileName)
        => ExportToJson(fileName, null);

    /// <summary>Serialise this field to JSON via the supplied stream.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(Stream stream, ExportFieldsToJsonOptions? options)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var data = FieldJsonExporter.BuildField(this);
        FieldJsonExporter.Write(stream, data, options?.WriteIndented ?? false);
        return new[]
        {
            new FieldSerializationResult
            {
                FieldFullName = FullName ?? PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            },
        };
    }

    /// <summary>Serialise this field to a JSON file.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(string fileName, ExportFieldsToJsonOptions? options)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        return ExportToJson(fs, options);
    }

    /// <summary>Write the field's name, flags and value to a JSON stream. A
    /// dotted field name (an XFA path such as <c>form1[0].P1[0].Field[0]</c>)
    /// is emitted as nested <c>{ "Name", "ChildFields" }</c> objects, one per
    /// path segment, with the leaf carrying <c>Flags</c> and <c>Value</c>.</summary>
    public void ExportValueToJson(Stream outputJsonStream, bool indented)
    {
        if (outputJsonStream is null) throw new ArgumentNullException(nameof(outputJsonStream));
        var fullName = FullName ?? PartialName ?? string.Empty;
        var form = OwnerDocument?.Form;
        var value = (form is { IsXfa: true } ? form.GetXfaFieldValue(fullName) : Value) ?? Value ?? string.Empty;

        var segments = fullName.Length == 0 ? new[] { string.Empty } : fullName.Split('.');
        var node = new FieldValueNode { Name = segments[segments.Length - 1], Flags = (int)Flags, Value = value };
        for (var i = segments.Length - 2; i >= 0; i--)
            node = new FieldValueNode { Name = segments[i], ChildFields = new List<FieldValueNode> { node } };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node, options);
        outputJsonStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Nested field node used when (de)serialising a field-value path
    /// to/from JSON. Property order matters: <c>Name, Flags, Value, ChildFields</c>.</summary>
    private sealed class FieldValueNode
    {
        public string? Name { get; set; }
        public int? Flags { get; set; }
        public string? Value { get; set; }
        public List<FieldValueNode>? ChildFields { get; set; }
    }

    private static string JsonEscape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    /// <summary>Flatten this field — turn its value into static page content. Currently no-op; per-field flattening is not implemented.</summary>
    public new void Flatten()
    {
        // Per-field flatten not implemented; document-level Form.Flatten handles the bulk path.
    }

    /// <summary>Enumerator typed to <see cref="Aspose.Pdf.Annotations.WidgetAnnotation"/>.</summary>
    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetWidgetEnumerator()
    {
        if (OwnerDocument is null) yield break;
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            yield return new Aspose.Pdf.Annotations.WidgetAnnotation(OwnerDocument);
    }

    /// <summary>Read this field's value from a JSON stream. Returns true on success.</summary>
    public bool ImportValueFromJson(Stream inputJsonStream)
        => ImportValueFromJson(inputJsonStream, FullName ?? PartialName ?? string.Empty);

    /// <summary>Read a named field's value from a JSON stream — a single field
    /// object (possibly nested via <c>ChildFields</c>) or an array of them — and
    /// apply the value of the entry whose dotted path matches
    /// <paramref name="fieldFullNameInJSON"/>. For an XFA-backed form the value
    /// is written into the XFA datasets as well as the AcroForm field. Returns
    /// true on success.</summary>
    public bool ImportValueFromJson(Stream inputJsonStream, string fieldFullNameInJSON)
    {
        if (inputJsonStream is null) return false;
        try
        {
            using var reader = new StreamReader(inputJsonStream, System.Text.Encoding.UTF8, leaveOpen: true);
            using var doc = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
            var root = doc.RootElement;
            string? value = null;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                    if (TryFindValueByPath(element, string.Empty, fieldFullNameInJSON, out value))
                        break;
            }
            else
            {
                TryFindValueByPath(root, string.Empty, fieldFullNameInJSON, out value);
            }
            if (value is null) return false;
            ApplyImportedValue(value);
            return true;
        }
        catch
        {
            // parse failure → false
        }
        return false;
    }

    /// <summary>Search a field node (recursing through <c>ChildFields</c>) for a
    /// leaf whose accumulated dotted path equals <paramref name="target"/>.</summary>
    private static bool TryFindValueByPath(
        System.Text.Json.JsonElement element, string prefix, string target, out string? value)
    {
        value = null;
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        var name = element.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var full = prefix.Length == 0 ? name : prefix + "." + name;
        // Children are nested under "ChildFields" (field-level export) or "Fields"
        // (form-level export); recurse whichever is present.
        if ((element.TryGetProperty("ChildFields", out var kids) ||
             element.TryGetProperty("Fields", out kids)) &&
            kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var kid in kids.EnumerateArray())
                if (TryFindValueByPath(kid, full, target, out value))
                    return true;
            return false;
        }
        if (full == target &&
            element.TryGetProperty("Value", out var v) &&
            v.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        return false;
    }

    private void ApplyImportedValue(string value)
    {
        Value = value;
        var form = OwnerDocument?.Form;
        if (form is { IsXfa: true })
            form.SetXfaFieldValue(FullName ?? string.Empty, value);
    }

    /// <summary>Recalculate the field's value from its calculation script. Currently no-op; returns false.</summary>
    public bool Recalculate() => false;

    /// <summary>
    /// Generate a default <c>/AP /N</c> appearance stream for this field's widget
    /// so it renders without relying on a viewer's NeedAppearances pass. Overridden
    /// per field type; the base implementation is a no-op (fields with no visible
    /// representation, or types that build their appearance elsewhere).
    /// </summary>
    internal virtual void GenerateAppearance() { }

    /// <summary>Format a coordinate for a content stream, invariant culture.</summary>
    private protected static string FmtNum(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Escape a string for use as a PDF literal-string operand.</summary>
    private protected static string EscapePdfText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>Resolve the widget rectangle's width/height. Returns false when
    /// there is no usable (positive-area) rectangle.</summary>
    private protected bool TryWidgetSize(out double w, out double h)
    {
        w = h = 0;
        if (Reader.Resolve(Dict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4)
            return false;
        var r = Rectangle.FromPdfArray(rectArr);
        w = r.Width; h = r.Height;
        return w > 0 && h > 0;
    }

    /// <summary>Parse the field's <c>/DA</c> default-appearance string for a font
    /// resource name and size, defaulting to Helvetica 12. A size of 0 in /DA
    /// means auto-size — the caller decides what size to substitute.</summary>
    private protected void ParseDefaultAppearance(out string fontName, out double fontSize)
        => ParseDefaultAppearance(out fontName, out fontSize, defaultSize: 12);

    private protected void ParseDefaultAppearance(out string fontName, out double fontSize, double defaultSize)
    {
        fontName = "Helv";
        fontSize = defaultSize;
        if (Reader.Resolve(Dict.Get("DA")) is not PdfString daStr) return;
        var parts = daStr.ToText().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] != "Tf" || i < 2) continue;
            fontName = parts[i - 2].TrimStart('/');
            double.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s);
            if (s > 0) fontSize = s;
        }
    }

    /// <summary>Wrap a content string in a Form XObject appearance stream sized
    /// to the supplied bounding box.</summary>
    private protected static PdfStream MakeApXObject(string content, double w, double h,
        PdfDictionary? resources = null)
    {
        var stream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(content));
        stream.Dict.Set("Type", new PdfName("XObject"));
        stream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        stream.Dict.Set("BBox", bbox);
        stream.Dict.Set("Resources", (PdfObject?)resources ?? new PdfDictionary());
        return stream;
    }

    /// <summary>Build a <c>/Font</c> resource dictionary mapping the given resource
    /// name to a Standard-14 base font, so an appearance stream that references it
    /// renders text (the renderer resolves a font only from declared resources).
    /// The /DR aliases written by Acrobat ("Helv" / "HeBo" / "ZaDb" / "TiRo" / ...)
    /// are translated to their PostScript base-font names; an unknown alias falls
    /// back to Helvetica.</summary>
    private protected static PdfDictionary MakeStandardFontResources(string fontName)
    {
        var alias = string.IsNullOrEmpty(fontName) ? "Helv" : fontName;
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(StandardBaseFont(alias)));
        var fonts = new PdfDictionary();
        fonts.Set(alias, font);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        return res;
    }

    /// <summary>The Acrobat /DR font aliases that name a Standard-14 face.</summary>
    private static readonly HashSet<string> StandardFontAliases = new(StringComparer.Ordinal)
    {
        "Helv", "HeBo", "HeOb", "HeBO", "TiRo", "TiBo", "TiIt", "TiBI",
        "Cour", "CoBo", "CoOb", "CoBO", "ZaDb", "Symb",
    };

    /// <summary>Build the <c>/Resources</c> dictionary for a regenerated text-field
    /// appearance so the <c>/{fontName} Tf</c> operator in the content stream
    /// resolves. Sibling fonts and non-font resources from
    /// <paramref name="existingRes"/> are carried over unchanged; when the
    /// <c>/DA</c> font name isn't already declared there, a Standard-14 entry under
    /// that name is synthesised. The AcroForm <c>/DR</c> face is deliberately not
    /// pulled in: a /DR font is often an embedded subset that only carries the
    /// field's original glyphs, so rendering a freshly-set value through it would
    /// drop characters the subset never included.</summary>
    private protected PdfDictionary BuildTextAppearanceResources(string fontName, PdfDictionary? existingRes)
    {
        var existingFonts = existingRes is null ? null : Reader.ResolveDict(existingRes.Get("Font"));

        var fonts = new PdfDictionary();
        if (existingFonts is not null)
            foreach (var key in existingFonts.Keys)
                fonts.Set(key, existingFonts.Get(key)!);

        if (!fonts.ContainsKey(fontName))
            fonts.Set(fontName, MakeAppearanceFont(fontName));

        var res = new PdfDictionary();
        res.Set("Font", fonts);
        if (existingRes is not null)
            foreach (var key in existingRes.Keys)
                if (key != "Font")
                    res.Set(key, existingRes.Get(key)!);
        return res;
    }

    /// <summary>Synthesise a <c>/Font</c> dictionary for an appearance resource.
    /// A known Acrobat alias (Helv, TiRo, ...) maps to its Standard-14 PostScript
    /// base font. Any other name that resolves to an installed face is kept verbatim
    /// as the <c>/BaseFont</c> so a <c>/DA</c> naming a real font (e.g. "Arial")
    /// round-trips that name; a name that resolves to nothing — typically a bare
    /// subset tag whose <c>/DA</c> font isn't a usable family — falls back to
    /// Helvetica so the value still renders instead of vanishing.</summary>
    private static PdfDictionary MakeAppearanceFont(string fontName)
    {
        string baseFont;
        if (StandardFontAliases.Contains(fontName))
            baseFont = StandardBaseFont(fontName);
        else if (Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName) is not null)
            baseFont = fontName;
        else
            baseFont = "Helvetica";
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        return font;
    }

    /// <summary>Map an Acrobat /DR alias to its Standard-14 PostScript base-font name.</summary>
    private protected static string StandardBaseFont(string alias) => alias switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "HeBO" => "Helvetica-BoldOblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "CoOb" => "Courier-Oblique",
        "CoBO" => "Courier-BoldOblique",
        "ZaDb" => "ZapfDingbats",
        "Symb" => "Symbol",
        _ => "Helvetica",
    };

    /// <summary>Coerce a resolved numeric PDF object (integer or real) to a double.</summary>
    private protected static double? AsNumber(PdfObject? obj) => obj switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => null,
    };

    /// <summary>Emit background-fill and/or border-stroke operators from the
    /// widget's <c>/MK</c> appearance-characteristics dictionary (<c>/BG</c>,
    /// <c>/BC</c>). Empty when neither colour is present.</summary>
    private protected string BuildMkBackgroundAndBorder(double w, double h)
    {
        var mk = Reader.ResolveDict(Dict.Get("MK"));
        if (mk is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        if (MkColorOperator(mk.Get("BG"), fill: true) is { } bg)
            sb.Append($"{bg} 0 0 {FmtNum(w)} {FmtNum(h)} re f ");
        if (MkColorOperator(mk.Get("BC"), fill: false) is { } bc)
            sb.Append($"{bc} 1 w 0.5 0.5 {FmtNum(w - 1)} {FmtNum(h - 1)} re S ");
        return sb.ToString();
    }

    private string? MkColorOperator(PdfObject? entry, bool fill)
    {
        if (Reader.Resolve(entry) is not PdfArray arr) return null;
        var vals = new System.Collections.Generic.List<double>();
        foreach (var it in arr)
            if (AsNumber(Reader.Resolve(it)) is { } d) vals.Add(d);
        if (vals.Count == 0) return null; // empty array = transparent / no paint
        var nums = string.Join(" ", vals.ConvertAll(FmtNum));
        return vals.Count switch
        {
            1 => $"{nums} {(fill ? "g" : "G")}",
            3 => $"{nums} {(fill ? "rg" : "RG")}",
            4 => $"{nums} {(fill ? "k" : "K")}",
            _ => null,
        };
    }

    /// <summary>Move the field's first widget rectangle to the supplied point (top-left corner).</summary>
    public void SetPosition(Aspose.Pdf.Point point)
    {
        if (point is null) return;
        var rect = Rect;
        if (rect is null) return;
        var width = rect.URX - rect.LLX;
        var height = rect.URY - rect.LLY;
        var newRect = new PdfArray();
        newRect.Add(new PdfReal(point.X));
        newRect.Add(new PdfReal(point.Y - height));
        newRect.Add(new PdfReal(point.X + width));
        newRect.Add(new PdfReal(point.Y));
        Dict.Set("Rect", newRect);
    }
}

public class TextBoxField : Field
{
    internal TextBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// auto-fit appearance. Stored only; tests set this to clamp the
    /// max font size for the whole form.</summary>
    public static new double MaxFontSize { get; set; }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// auto-fit appearance — the lower bound for the auto-sized font. Stored
    /// only; together with <see cref="MaxFontSize"/> a caller can pin the
    /// appearance font size for the whole form.</summary>
    public static new double MinFontSize { get; set; }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// FitIntoRectangle flag. Stored only.</summary>
    public static new bool FitIntoRectangle { get; set; }

    protected override void SetValue(string? value)
    {
        // A value that the field's built-in format action cannot accept (e.g. a
        // non-date string assigned to an AFDate-formatted field) is rejected,
        // leaving the previous value in place — matching Aspose.PDF for .NET behaviour.
        if (!FieldFormatScript.IsValueValid(Dict, Reader, value ?? string.Empty))
            return;
        base.SetValue(value);
        // /V holds the raw typed value (per PDF convention). The visible
        // appearance uses what the field's /AA/F Format JavaScript would
        // produce — for the small set of built-in Acrobat formatters
        // (AFDate_FormatEx, AFNumber_Format, AFPercent_Format, ...) we
        // pattern-match the call and apply the equivalent transform.
        // Falls through to the raw value when no Format action is set or
        // when the script isn't a recognised built-in.
        var displayValue = FieldFormatScript.Apply(Dict, Reader, value ?? "");
        RegenerateAppearance(displayValue);
    }

    /// <summary>
    /// Rebuild the /AP/N appearance stream to display the current value text.
    /// Uses the font/size from the /DA (default appearance) string.
    /// </summary>
    private void RegenerateAppearance(string text)
    {
        // Parse DA for font name and size
        var da = Dict.Get("DA") is PdfString daStr ? daStr.ToText() : "/Helv 12 Tf 0 g";
        string fontName = "Helv";
        double fontSize = 12;
        var daParts = da.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < daParts.Length; i++)
        {
            if (daParts[i] == "Tf" && i >= 2)
            {
                fontName = daParts[i - 2].TrimStart('/');
                double.TryParse(daParts[i - 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out fontSize);
            }
        }

        // Get the widget rect for BBox
        var rectArr = Reader.Resolve(Dict.Get("Rect")) as PdfArray;
        double llx = 0, lly = 0, urx = 100, ury = 20;
        if (rectArr is { Count: >= 4 })
        {
            var r = Rectangle.FromPdfArray(rectArr);
            llx = r.LLX; lly = r.LLY; urx = r.URX; ury = r.URY;
        }
        double w = urx - llx, h = ury - lly;

        // /DA size 0 means auto-size: pick the largest size whose glyph run still
        // fits the box width (~0.5em average advance) AND leaves a little vertical
        // padding (Acrobat caps at ~0.83 of inner height for single-line text).
        if (fontSize <= 0)
        {
            var charCount = System.Math.Max(1, text.Length);
            var widthCap = (w - 4) / (charCount * 0.5);
            var heightCap = h * 0.83;
            fontSize = System.Math.Max(4, System.Math.Min(widthCap, heightCap));
        }

        // Honour the global TextBoxField auto-fit clamps: when a
        // caller pins MinFontSize / MaxFontSize the effective appearance font size
        // is bounded to that range — e.g. Min==Max==15 forces 15 regardless of the
        // /DA size. Unset (0) bounds leave the size untouched.
        if (TextBoxField.MinFontSize > 0) fontSize = System.Math.Max(fontSize, TextBoxField.MinFontSize);
        if (TextBoxField.MaxFontSize > 0) fontSize = System.Math.Min(fontSize, TextBoxField.MaxFontSize);

        // Build the BT…ET body. Multiline fields lay the value out top-down,
        // one line per visual line, with a fixed 1.2× line pitch; single-line
        // fields vertical-centre the whole value on one baseline.
        string textBody;
        if (IsMultiline)
        {
            var lines = text.Split('\n');
            for (int li = 0; li < lines.Length; li++)
                lines[li] = lines[li].TrimEnd('\r');

            // Line pitch is 1.2× the font size (Acrobat default leading).
            var lineHeight = fontSize * 1.2;
            // First baseline measured from the top of the box: a full line box
            // sits at the top, and the baseline lies the typographic descent
            // above that line box's bottom edge. typoDescent is negative.
            var typoDescentEm = ReadTypoDescentEm(fontName);
            var firstY = h - lineHeight - typoDescentEm * fontSize;

            var bt = new System.Text.StringBuilder();
            bt.Append($"2 {Format(firstY)} Td\n");
            for (int li = 0; li < lines.Length; li++)
            {
                if (li > 0) bt.Append($"0 {Format(-lineHeight)} Td\n");
                var esc = lines[li].Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                bt.Append($"({esc}) Tj\n");
            }
            textBody = bt.ToString();
        }
        else
        {
            var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            textBody = $"2 {Format(h / 2 - fontSize * 0.3)} Td\n({escaped}) Tj\n";
        }

        // Build the appearance content stream
        var content = $"/Tx BMC\nq\nBT\n/{fontName} {Format(fontSize)} Tf\n0 g\n" +
                      textBody + "ET\nQ\nEMC\n";
        var contentBytes = System.Text.Encoding.Latin1.GetBytes(content);

        // Create the appearance stream
        var apStream = new PdfStream(new PdfDictionary(), contentBytes);
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bboxArr = new PdfArray();
        bboxArr.Add(new PdfReal(0)); bboxArr.Add(new PdfReal(0));
        bboxArr.Add(new PdfReal(w)); bboxArr.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bboxArr);

        // Carry font resources into the appearance so the value text renders.
        // Prefer reusing the resources from a previously-built /AP/N (preserves any
        // embedded font); otherwise declare the /DA font as standard-14 Helvetica —
        // the renderer resolves a font only from the appearance's own resources.
        PdfDictionary? resolvedRes = null;
        var existingAp = Reader.ResolveDict(Dict.Get("AP"));
        if (existingAp is not null)
        {
            var existingN = Reader.ResolveStream(existingAp.Get("N"));
            if (existingN is not null)
                resolvedRes = Reader.ResolveDict(existingN.Dict.Get("Resources"));
        }
        apStream.Dict.Set("Resources", BuildTextAppearanceResources(fontName, resolvedRes));

        // Build a fresh /AP dict. Modifying the resolved (possibly-indirect)
        // /AP dict in place leaves the writer with no signal to re-emit it on
        // incremental save — the field's own dict is dirty-tracked but the
        // separate /AP indirect object isn't. Inlining a new direct dict means
        // the field's re-serialised body carries the updated /N reference, and
        // the new appearance stream gets promoted to its own indirect object
        // by PdfWriter.WriteDictionary.
        var newApDict = new PdfDictionary();
        var oldApDict = Reader.ResolveDict(Dict.Get("AP"));
        if (oldApDict is not null)
        {
            foreach (var key in oldApDict.Keys)
            {
                if (key == "N") continue; // we're replacing /N
                newApDict.Set(key, oldApDict.Get(key)!);
            }
        }
        newApDict.Set("N", apStream);
        Dict.Set("AP", newApDict);
    }

    /// <summary>Build the appearance for the current value when none exists yet,
    /// so a freshly-added text field renders its (possibly empty) value box.</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        var displayValue = FieldFormatScript.Apply(Dict, Reader, Value ?? "");
        RegenerateAppearance(displayValue);
    }

    private static string Format(double v) =>
        v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Resolve the typographic descender (signed em ratio) used to place
    /// the first multiline baseline. Prefers the font embedded via the field's
    /// DefaultAppearance (its /DA name may already be an embedded-resource alias
    /// that no longer resolves by family name); otherwise loads the named system
    /// face; falls back to a typical Latin descent.</summary>
    private double ReadTypoDescentEm(string fontName)
    {
        var ttf = DefaultAppearance?.EmbeddedFont?.SourceFontData?.TtfData;
        if (ttf is { Length: > 0 })
        {
            var d = Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(ttf);
            if (d != 0) return d;
        }
        var resolved = Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName);
        if (resolved is not null)
        {
            var d = Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(resolved);
            if (d != 0) return d;
        }
        return -0.207; // Helvetica-class default
    }

    /// <summary>
    /// Creates a new text box field on the specified page with the given rectangle.
    /// </summary>
    public TextBoxField(Page page, Rectangle rect)
        : base(BuildTextFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Creates a new text box field associated with the document (page assigned via Form.Add).
    /// </summary>
    public TextBoxField(Document doc, Rectangle rect)
        : base(BuildTextFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>Document-bound text box without a rectangle. Caller sets
    /// <see cref="Width"/> / <see cref="Height"/> or attaches via Form.Add.</summary>
    public TextBoxField(Document doc)
        : base(BuildTextFieldDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader)
    {
    }

    /// <summary>Multi-widget text field — first rectangle is used for the widget
    /// dictionary; additional rectangles are stored on /Kids for visual parity
    /// with the Aspose.PDF for .NET multi-widget contract.</summary>
    public TextBoxField(Page page, Rectangle[] rects)
        : base(BuildTextFieldDict(rects is { Length: > 0 } ? rects[0] : new Rectangle(0, 0, 0, 0)),
               page.Reader)
    {
        if (rects is null || rects.Length <= 1) return;
        var kids = new Aspose.Pdf.Core.PdfArray();
        for (var i = 1; i < rects.Length; i++)
        {
            var kid = new PdfDictionary();
            kid.Set("Subtype", new PdfName("Widget"));
            kid.Set("Rect", MakeRectArray(rects[i]));
            kids.Add(kid);
        }
        Dict.Set("Kids", kids);
    }

    /// <summary>
    /// Parameterless ctor — creates an unbound text box field. Layout
    /// dimensions come from <see cref="Width"/> / <see cref="Height"/>;
    /// the field is later attached to a page (via Form.Add) or to a layout
    /// container that places it (via <c>cell.Paragraphs.Add(field)</c>).
    /// </summary>
    public TextBoxField()
        : base(BuildTextFieldDict(new Rectangle(0, 0, 0, 0)), IO.PdfReader.Empty)
    {
    }

    private static PdfDictionary BuildTextFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Tx"));
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }

    /// <summary>Layout width when the field is placed inside a layout
    /// container (table cell, paragraph). Stored only; the actual widget
    /// rectangle is set when the field is attached to a page.</summary>
    public new double Width { get; set; }

    /// <summary>Layout height when the field is placed inside a layout
    /// container.</summary>
    public new double Height { get; set; }

    /// <summary>Multiline flag (bit 13 of /Ff). Setter mirrors the
    /// existing read-only <see cref="IsMultiline"/>.</summary>
    public bool Multiline
    {
        get => IsMultiline;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 12);
            else f &= ~(1 << 12);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    public int MaxLen
    {
        get => (int)Dict.GetInt("MaxLen");
        set => Dict.Set("MaxLen", new Aspose.Pdf.Core.PdfInteger(value));
    }
    public bool IsMultiline => (FieldFlags & (1 << 12)) != 0;
    public bool IsPassword => (FieldFlags & (1 << 13)) != 0;

    /// <summary>
    /// Vertical alignment of the text inside the field's appearance — per
    /// published reference. Stored in-memory; persisted to /AP regeneration
    /// when the appearance pipeline reads this hint.
    /// </summary>
    public VerticalAlignment TextVerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>True when the Tx field has the rich-text flag (bit 26 of /Ff) set.</summary>
    public bool IsRichText => (FieldFlags & (1 << 25)) != 0;

    /// <summary>
    /// Text justification: 0 = Left, 1 = Center, 2 = Right.
    /// </summary>
    public int Justification => (int)Dict.GetInt("Q");

    /// <summary>Bit 25 of /Ff (Comb): when set, the field is divided into
    /// MaxLen equal-width combs. Requires Multiline=false and MaxLen>0.</summary>
    public bool ForceCombs
    {
        get => (FieldFlags & (1 << 24)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 24);
            else f &= ~(1 << 24);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Inverse of bit 24 of /Ff (DoNotScroll). When false the field
    /// will not scroll horizontally / vertically beyond its visible region.</summary>
    public bool Scrollable
    {
        get => (FieldFlags & (1 << 23)) == 0;
        set
        {
            var f = FieldFlags;
            if (value) f &= ~(1 << 23);
            else f |= (1 << 23);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Inverse of bit 23 of /Ff (DoNotSpellCheck).</summary>
    public bool SpellCheck
    {
        get => (FieldFlags & (1 << 22)) == 0;
        set
        {
            var f = FieldFlags;
            if (value) f &= ~(1 << 22);
            else f |= (1 << 22);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>The text value stored in /V.</summary>
    public override string? Value
    {
        get => Dict.Get("V") is PdfString s ? s.ToText() : null;
        set => SetValue(value);
    }

    /// <summary>Encode <paramref name="code"/> as a placeholder barcode in the
    /// field value. The FOSS pipeline does not currently render a glyph form;
    /// the string is stored verbatim so callers can round-trip the value.</summary>
    public void AddBarcode(string code) => SetValue(code);

    /// <summary>Stub for image overlay support. Cross-platform — no GDI
    /// rasterization happens.</summary>
    public void AddImage(System.Drawing.Image image) { _ = image; }
}

/// <summary>
/// A Tx form field that carries the rich-text flag (bit 26 of /Ff). Stores rich-text
/// content via /RV and exposes <see cref="Value"/> as the plain-text representation
/// from /V. Mirrors the public Aspose.PDF for .NET type.
/// </summary>
public sealed class RichTextBoxField : TextBoxField
{
    internal RichTextBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct a rich-text field on the given page rectangle.</summary>
    public RichTextBoxField(Page page, Rectangle rect) : base(page, rect)
    {
        // Set the rich-text bit (PDF table-228 bit 26).
        Dict.Set("Ff", new PdfInteger(FieldFlags | (1 << 25)));
    }

    /// <summary>The rich-text representation of the field value (PDF /RV entry).</summary>
    public string? RichTextValue
    {
        get => Dict.Get("RV") is PdfString s ? s.ToText() : null;
        set => Dict.Set("RV", value is null ? null! : new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>The formatted (rich-text) representation; alias for <see cref="RichTextValue"/>.</summary>
    public string? FormattedValue
    {
        get => RichTextValue;
        set => RichTextValue = value;
    }

    /// <summary>Plain-text value rendered by this field (/V entry).</summary>
    public new string? Value
    {
        get => Dict.Get("V") is PdfString s ? s.ToText() : null;
        set => Dict.Set("V", new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>CSS-style style string applied to the rich-text fragments. Stored only.</summary>
    public string Style { get; set; } = string.Empty;

    /// <summary>Text justification.</summary>
    public Aspose.Pdf.Annotations.Justification Justify { get; set; } = Aspose.Pdf.Annotations.Justification.Left;
}

/// <summary>Visual style of the checkbox glyph (/MK /CA mapping).</summary>
public enum BoxStyle
{
    Circle,
    Check,
    Cross,
    Diamond,
    Square,
    Star,
}

/// <summary>Outer shape of a checkbox / radio-button widget border.</summary>
public enum BoxShape
{
    Square,
    Circle,
}

public class CheckboxField : Field
{
    internal CheckboxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Default-construct an unbound checkbox field. Add it to a
    /// document later via <c>form.Add(field, page)</c>.</summary>
    public CheckboxField() : base(BuildCheckboxDict(), PdfReader.Empty) { }

    /// <summary>Construct a checkbox bound to <paramref name="doc"/>.</summary>
    public CheckboxField(Document doc) : base(BuildCheckboxDict(), doc?.Reader ?? PdfReader.Empty) { }

    /// <summary>Construct a checkbox bound to <paramref name="doc"/> with
    /// the given widget <paramref name="rect"/>.</summary>
    public CheckboxField(Document doc, Rectangle rect)
        : base(BuildCheckboxDict(rect), doc?.Reader ?? PdfReader.Empty) { }

    /// <summary>Construct a checkbox on <paramref name="page"/> at the
    /// given <paramref name="rect"/>.</summary>
    public CheckboxField(Page page, Rectangle rect)
        : base(BuildCheckboxDict(rect), page?.Reader ?? PdfReader.Empty) { }

    private static PdfDictionary BuildCheckboxDict(Rectangle? rect = null)
    {
        var d = new PdfDictionary();
        d.Set("FT", new PdfName("Btn"));
        if (rect is not null)
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(rect.LLX));
            arr.Add(new PdfReal(rect.LLY));
            arr.Add(new PdfReal(rect.URX));
            arr.Add(new PdfReal(rect.URY));
            d.Set("Rect", arr);
        }
        return d;
    }

    /// <summary>Visual style of the rendered checkbox glyph (/MK /CA):
    /// <see cref="BoxStyle.Cross"/> draws an X, <see cref="BoxStyle.Check"/> a
    /// check mark; other styles fall back to an X. Honoured by
    /// <see cref="GenerateAppearance"/> when the checkbox is added to a form.</summary>
    public BoxStyle Style { get; set; } = BoxStyle.Check;

    /// <summary>Move this checkbox's widget to <paramref name="rect"/> (page space) and
    /// ensure it is registered in the page /Annots. Used by the generator to position a
    /// checkbox laid out inside a table cell; the widget's /AP draws the box and check.</summary>
    internal void PlaceWidget(Page page, Rectangle rect)
    {
        Dict.Set("Rect", MakeRectArray(rect));
        var annots = Reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annots is null) { annots = new PdfArray(); page.Dict.Set("Annots", annots); }
        foreach (var a in annots)
            if (ReferenceEquals(Reader.Resolve(a), Dict)) return; // already present
        annots.Add(Dict);
    }

    /// <summary>Generate the /AP Normal + Down appearance streams for this
    /// checkbox: the glyph matching <see cref="Style"/> for the on state
    /// (<see cref="OnValue"/>) and an empty box for "Off". No-op when the field
    /// has no /Rect or already carries an /AP (e.g. loaded from an existing PDF).</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        if (Reader.Resolve(Dict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4) return;
        var r = Rectangle.FromPdfArray(rectArr);
        double w = r.Width, h = r.Height;
        if (w <= 0 || h <= 0) return;

        var on = OnValue;
        var ap = new PdfDictionary();
        ap.Set("N", BuildStateDict(on, w, h));
        ap.Set("D", BuildStateDict(on, w, h));
        Dict.Set("AP", ap);
        if (Dict.GetName("AS") is null) Dict.Set("AS", new PdfName("Off"));
    }

    private PdfDictionary BuildStateDict(string onValue, double w, double h)
    {
        var states = new PdfDictionary();
        states.Set(onValue, BuildBoxStream(w, h, drawGlyph: true));
        states.Set("Off", BuildBoxStream(w, h, drawGlyph: false));
        return states;
    }

    private static string Fmt(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private PdfStream BuildBoxStream(double w, double h, bool drawGlyph)
    {
        var sb = new System.Text.StringBuilder();
        // Wrap in q/Q so the drawing is graphics-state isolated; the trailing Q
        // is the final operator, with the glyph strokes just before it.
        sb.Append("q ");
        // White interior + black border box (a plain check box, not a grey button).
        sb.Append($"1 w 1 1 1 rg 0 0 {Fmt(w)} {Fmt(h)} re f ");
        sb.Append($"0 0 0 RG 0 0 {Fmt(w)} {Fmt(h)} re S ");
        if (drawGlyph)
        {
            var inset = System.Math.Min(w, h) * 0.2;
            sb.Append("2 w ");
            if (Style == BoxStyle.Check)
            {
                // Check mark: down-stroke then up-stroke.
                sb.Append($"{Fmt(w * 0.2)} {Fmt(h * 0.5)} m {Fmt(w * 0.4)} {Fmt(h * 0.25)} l {Fmt(w * 0.8)} {Fmt(h * 0.78)} l S ");
            }
            else
            {
                // Cross (and fallback for other styles): an X of two diagonals.
                sb.Append($"{Fmt(inset)} {Fmt(inset)} m {Fmt(w - inset)} {Fmt(h - inset)} l S ");
                sb.Append($"{Fmt(inset)} {Fmt(h - inset)} m {Fmt(w - inset)} {Fmt(inset)} l S ");
            }
        }
        sb.Append("Q ");

        var stream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(sb.ToString()));
        stream.Dict.Set("Type", new PdfName("XObject"));
        stream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        stream.Dict.Set("BBox", bbox);
        stream.Dict.Set("Resources", new PdfDictionary());
        return stream;
    }

    /// <summary>The export value emitted into FDF/XFDF on form submit.
    /// Defaults to <see cref="OnValue"/>.</summary>
    public string ExportValue
    {
        get => OnValue;
        set
        {
            // Setter wires /AP/N/{value} as a clone of the existing /AP/N/{OnValue}
            // entry; left as a stored-only no-op here because the FOSS appearance
            // generator doesn't currently honour custom export values at write.
            _ = value;
        }
    }

    /// <summary>True when this checkbox is a grouped field with explicit widget
    /// kids (each kid carrying its own /AP/N on-state), as opposed to a single
    /// leaf widget. Grouped checkboxes track their selection per kid.</summary>
    private bool HasWidgetKids => AllKids().Any();

    /// <summary>The /AP/N on-state name (the non-"Off" key) of a widget kid,
    /// or null when the kid declares no appearance states.</summary>
    private string? KidOnValue(PdfDictionary kid)
    {
        var ap = Reader.ResolveDict(kid.Get("AP"));
        var n = ap is null ? null : Reader.ResolveDict(ap.Get("N"));
        if (n is not null)
            foreach (var k in n.Keys)
                if (k != "Off") return k;
        return null;
    }

    /// <summary>The currently-selected export value of a grouped checkbox: the
    /// /AS of the first kid that is not "Off", or "Off" when none is selected.</summary>
    private string SelectedKidState()
    {
        foreach (var kid in AllKids())
        {
            var asName = kid.GetName("AS");
            if (asName is not null && asName != "Off") return asName;
        }
        return "Off";
    }

    /// <summary>Distinct on-state values declared across all widget kids.</summary>
    private System.Collections.Generic.List<string> KidOnValues()
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (var kid in AllKids())
        {
            var ov = KidOnValue(kid);
            if (ov is not null && !result.Contains(ov)) result.Add(ov);
        }
        return result;
    }

    /// <summary>Select <paramref name="value"/> on a grouped checkbox: set the
    /// field /V and /AS, and set each kid's /AS to its own on-value when it
    /// matches, otherwise "Off".</summary>
    private void ApplyGroupedValue(string value)
    {
        Dict.Set("V", new PdfName(value));
        Dict.Set("AS", new PdfName(value));
        foreach (var kid in AllKids())
        {
            var ov = KidOnValue(kid);
            kid.Set("AS", new PdfName(ov == value ? value : "Off"));
        }
        MarkCheckboxDirty();
    }

    /// <summary>Mark the field dict and every widget-kid dict dirty so an
    /// incremental save persists a checkbox value/state change.</summary>
    private void MarkCheckboxDirty()
    {
        if (OwnerDocument is null) return;
        if (ObjectNumber >= 0) OwnerDocument.MarkDirty(ObjectNumber, Dict);
        foreach (var kid in AllKids())
        {
            var n = OwnerDocument.FindObjectNumber(kid);
            if (n >= 0) OwnerDocument.MarkDirty(n, kid);
        }
    }

    /// <summary>Currently selected appearance state. For a grouped checkbox this
    /// is the on-value of the selected kid; for a single checkbox the
    /// <see cref="OnValue"/> or "Off".</summary>
    public new string ActiveState
    {
        get => HasWidgetKids ? SelectedKidState() : (Dict.GetName("AS") ?? "Off");
        set
        {
            if (value is null) { Dict.Remove("AS"); return; }
            if (HasWidgetKids) ApplyGroupedValue(value);
            else { Dict.Set("AS", new PdfName(value)); MarkCheckboxDirty(); }
        }
    }

    /// <summary>All appearance-state keys declared under /AP/N. Includes
    /// "Off" plus the <see cref="OnValue"/>.</summary>
    public System.Collections.Generic.List<string> AllowedStates
    {
        get
        {
            var result = new System.Collections.Generic.List<string>();
            var ap = Reader.ResolveDict(Dict.Get("AP"));
            var n = ap is null ? null : Reader.ResolveDict(ap.Get("N"));
            if (n is not null) foreach (var k in n.Keys) result.Add(k);
            return result;
        }
    }

    /// <summary>The checkbox value (/V entry, name-typed). For a grouped
    /// checkbox, reflects the selected kid's on-value.</summary>
    public new string Value
    {
        get => HasWidgetKids ? SelectedKidState() : (Dict.GetName("V") ?? "Off");
        set => SetValue(value);
    }

    /// <summary>Map an incoming value to a legal appearance-state name: null or
    /// "Off" (case-insensitive) → "Off"; an exact declared on-state (field /AP/N
    /// key or widget-kid on-value) → itself; any other non-empty value → the
    /// field's <see cref="OnValue"/>.</summary>
    private string NormalizeOnValue(string? value)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return "Off";
        if (AllowedStates.Contains(value)) return value;
        if (HasWidgetKids && KidOnValues().Contains(value)) return value;
        // A recognised "on" alias selects the field's real on-state; any other
        // unrecognised value (e.g. an FDF "No"/"false"/"0" for an unchecked box) is
        // treated as Off rather than forced on — fixes FDF imports that carry a
        // non-on string for a checkbox that should be cleared.
        if (IsOnAlias(value)) return OnValue;
        return "Off";
    }

    /// <summary>True when <paramref name="value"/> names one of this checkbox's
    /// declared "on" states (an /AP/N key other than "Off", or a grouped widget
    /// kid's on-value) — i.e. selecting it would check the box. "Off", empty, and
    /// any value matching no declared state return false. Used by data import
    /// (FDF/XFDF) where the incoming value is an export value to be matched
    /// against the real states, not coerced to the on-state.</summary>
    internal bool IsDeclaredOnState(string? value)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var s in AllowedStates)
            if (s != "Off" && string.Equals(s, value, StringComparison.Ordinal)) return true;
        return HasWidgetKids && KidOnValues().Contains(value);
    }

    private static bool IsOnAlias(string value) =>
        value.Equals("Yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("On", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value == "1";

    /// <summary>Append <paramref name="optionName"/> as a new selectable
    /// state on this checkbox (used for grouped checkboxes
    /// that behave like radio buttons). Stored only; FOSS treats every
    /// checkbox as a single-state Yes/Off toggle.</summary>
    public void AddOption(string optionName) { _ = optionName; }

    /// <summary>Append an option with an explicit widget rectangle.</summary>
    public void AddOption(string optionName, Rectangle rect) { _ = optionName; _ = rect; }

    /// <summary>Append an option targeting the given <paramref name="page"/>
    /// number plus widget rectangle.</summary>
    public void AddOption(string optionName, int page, Rectangle rect) { _ = optionName; _ = page; _ = rect; }

    /// <summary>Shallow clone of the field. Returns a new
    /// <see cref="CheckboxField"/> wrapping the same backing dictionary
    /// (matching the <c>object Clone()</c> contract).</summary>
    public new object Clone() => new CheckboxField(Dict, Reader);

    /// <summary>
    /// Gets the "On" state name for this checkbox (the appearance state key that is not "Off").
    /// Typically "Yes" but can be any custom name.
    /// </summary>
    public string OnValue
    {
        get
        {
            // Grouped checkbox: the on-value is taken from the kids. Prefer the
            // conventional "Yes" when present, otherwise the first kid on-value.
            if (HasWidgetKids)
            {
                var vals = KidOnValues();
                if (vals.Contains("Yes")) return "Yes";
                if (vals.Count > 0) return vals[0];
            }
            // Look at AP/N dictionary keys for the non-Off state
            var apDict = Reader.ResolveDict(Dict.Get("AP"));
            if (apDict is not null)
            {
                var nObj = Reader.Resolve(apDict.Get("N"));
                if (nObj is PdfDictionary nDict)
                {
                    foreach (var key in nDict.Keys)
                    {
                        if (key != "Off") return key;
                    }
                }
            }
            return "Yes"; // default
        }
    }

    public bool IsChecked
    {
        get
        {
            if (HasWidgetKids) return SelectedKidState() == OnValue;
            var v = Value;
            return v is not null && v != "Off";
        }
        set
        {
            if (HasWidgetKids)
            {
                string target;
                if (value) target = OnValue;
                else
                {
                    // Unchecking a grouped checkbox selects the single remaining
                    // (non-on) export value when there is exactly one — e.g. a
                    // "Yes"/"No" pair unchecks to "No"; otherwise plain "Off".
                    var others = KidOnValues();
                    others.Remove(OnValue);
                    target = others.Count == 1 ? others[0] : "Off";
                }
                ApplyGroupedValue(target);
                return;
            }
            var newValue = value ? OnValue : "Off";
            Dict.Set("V", new PdfName(newValue));
            Dict.Set("AS", new PdfName(newValue));
            MarkCheckboxDirty();
        }
    }

    public bool Checked
    {
        get => IsChecked;
        set => IsChecked = value;
    }

    protected override void SetValue(string? value)
    {
        // For checkboxes, /V is a name (not a string), and its only legal values
        // are "Off" and the field's declared on-state(s). Normalise generic
        // on-values ("Yes"/"On"/…) to the real on-state so importing e.g. "Yes"
        // into a checkbox whose on-state is "Y" stores "Y" (parity).
        var name = NormalizeOnValue(value);
        if (HasWidgetKids) { ApplyGroupedValue(name); return; }
        Dict.Set("V", new PdfName(name));
        Dict.Set("AS", new PdfName(name));
        MarkCheckboxDirty();
    }
}

public sealed class RadioButtonField : ChoiceField
{
    internal RadioButtonField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Creates a new radio-button field bound to the supplied page's reader.
    /// Use <see cref="AddOption(string, Rectangle)"/> to populate kid widgets,
    /// then add the field via <c>Form.Add</c>.
    /// </summary>
    public RadioButtonField(Page page) : base(BuildRadioFieldDict(), page.Reader) { }

    /// <summary>Construct with the field-level /Rect set. Callers that
    /// build a radio group via <c>new RadioButtonField(page, rect)</c> followed
    /// by per-option <c>RadioButtonOptionField</c> kids expect the parent dict
    /// to carry a /Rect; the kids each carry their own widget rects.</summary>
    public RadioButtonField(Page page, Rectangle rect) : base(BuildRadioFieldDict(), page.Reader)
    {
        if (rect is null) return;
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        Dict.Set("Rect", arr);
    }

    public RadioButtonField(Document doc) : base(BuildRadioFieldDict(), doc?.Reader ?? PdfReader.Empty) { }

    private static PdfDictionary BuildRadioFieldDict()
    {
        var dict = new PdfDictionary();
        dict.Set("FT", new PdfName("Btn"));
        dict.Set("Ff", new PdfInteger(1 << 15)); // bit 16: Radio flag
        return dict;
    }

    /// <summary>
    /// Adds an option with the given appearance-state name at the supplied
    /// rectangle. Writes both an /Opt entry (so <see cref="Options"/> reflects
    /// it) and a kid widget annotation under /Kids (so <c>this[i].Rect</c>
    /// surfaces the option's position).
    /// </summary>
    public void AddOption(string optionName, Rectangle rect)
    {
        AddOption(optionName); // /Opt entry (inherited from ChoiceField)

        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("Annot"));
        kid.Set("Subtype", new PdfName("Widget"));
        kid.Set("Rect", MakeRectArray(rect));
        kid.Set("AS", new PdfName("Off"));

        var ap = new PdfDictionary();
        var n = new PdfDictionary();
        n.Set(optionName, new PdfDictionary());
        n.Set("Off", new PdfDictionary());
        ap.Set("N", n);
        kid.Set("AP", ap);

        var kids = Dict.Get("Kids") as PdfArray;
        if (kids is null)
        {
            kids = new PdfArray();
            Dict.Set("Kids", kids);
        }
        kids.Add(kid);
    }

    /// <summary>Add a radio option by name only (no widget rectangle yet); shadows
    /// the inherited ChoiceField version so reflection surfaces it on this type.</summary>
    public new void AddOption(string optionName) => base.AddOption(optionName);

    /// <summary>
    /// Add a configured <see cref="RadioButtonOptionField"/> as a kid widget of this
    /// radio group. Builds the widget annotation from the option's rectangle, name,
    /// and border characteristics, registers it under /Kids (so <c>Form.Add</c>
    /// places it on the page's /Annots), and links the option to the new widget so
    /// <see cref="RadioButtonOptionField.Rect"/> tracks it.
    /// </summary>
    public void Add(RadioButtonOptionField option)
    {
        if (option is null) throw new ArgumentNullException(nameof(option));

        var rect = option.PendingRect
            ?? new Rectangle(0, 0, option.Width, option.Height);
        var kidCount = 0;
        if (Dict.Get("Kids") is PdfArray existing) kidCount = existing.Count;
        var onState = string.IsNullOrEmpty(option.OptionName)
            ? $"Option{kidCount + 1}" : option.OptionName!;

        base.AddOption(onState); // /Opt entry on the parent field

        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("Annot"));
        kid.Set("Subtype", new PdfName("Widget"));
        kid.Set("Rect", MakeRectArray(rect));
        kid.Set("AS", new PdfName("Off"));
        kid.Set("Parent", Dict);

        var ap = new PdfDictionary();
        var n = new PdfDictionary();
        n.Set(onState, new PdfDictionary());
        n.Set("Off", new PdfDictionary());
        ap.Set("N", n);
        kid.Set("AP", ap);

        // /MK appearance characteristics: border colour (and background when set).
        var mk = new PdfDictionary();
        mk.Set("BC", ColorComponents(option.Characteristics.Border));
        if (option.Characteristics.Background.A != 0)
            mk.Set("BG", ColorComponents(option.Characteristics.Background));
        kid.Set("MK", mk);

        // /BS border width when the option carries a border.
        if (option.Border is { Width: > 0 } border)
        {
            var bs = new PdfDictionary();
            bs.Set("W", new PdfReal(border.Width));
            kid.Set("BS", bs);
        }

        var kids = Dict.Get("Kids") as PdfArray;
        if (kids is null) { kids = new PdfArray(); Dict.Set("Kids", kids); }
        kids.Add(kid);

        option.KidDict = kid;
        option.KidReader = Reader;
        option.OwnerRadio = this;
    }

    /// <summary>Position <paramref name="option"/>'s widget at <paramref name="rect"/> (page
    /// space) and register it in <paramref name="page"/>'s /Annots, so a radio option laid out
    /// by the generator round-trips as an interactive widget rather than just a drawn glyph.
    /// Refreshes the option's on/off appearance using its border and marker colours.</summary>
    internal void PlaceOptionWidget(RadioButtonOptionField option, Page page, Rectangle rect)
    {
        var kid = option.KidDict;
        if (kid is null) return;
        kid.Set("Rect", MakeRectArray(rect));
        WriteOptionAppearance(option, kid, rect.Width, rect.Height);

        var annots = page.Dict.Get("Annots") as PdfArray;
        if (annots is null) { annots = new PdfArray(); page.Dict.Set("Annots", annots); }
        foreach (var a in annots) if (ReferenceEquals(a, kid)) return; // already placed
        annots.Add(kid);
    }

    /// <summary>Build the option widget's /AP /N appearance for its on-state and "Off": the
    /// border-coloured disc outline followed by the marker-coloured centre, emitted as two
    /// non-stroking <c>rg</c> colour operators (border first, marker second).</summary>
    private static void WriteOptionAppearance(RadioButtonOptionField option, PdfDictionary kid, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        var onName = "Off";
        if (kid.Get("AP") is PdfDictionary apd && apd.Get("N") is PdfDictionary nd0)
            foreach (var key in nd0.Keys) if (key != "Off") { onName = key; break; }
        if (onName == "Off") onName = string.IsNullOrEmpty(option.OptionName) ? "On" : option.OptionName!;

        var border = option.Characteristics.Border;
        var marker = option.DefaultAppearance?.TextColor ?? System.Drawing.Color.Black;
        double cx = w / 2, cy = h / 2, rad = System.Math.Min(w, h) / 2 - 1;
        if (rad <= 0) rad = System.Math.Min(w, h) / 2;

        var on = new System.Text.StringBuilder();
        on.Append("q ");
        on.Append($"{FmtNum(border.R / 255.0)} {FmtNum(border.G / 255.0)} {FmtNum(border.B / 255.0)} rg ");
        on.Append(CirclePath(cx, cy, rad)).Append("f ");
        on.Append($"{FmtNum(marker.R / 255.0)} {FmtNum(marker.G / 255.0)} {FmtNum(marker.B / 255.0)} rg ");
        on.Append(CirclePath(cx, cy, rad * 0.5)).Append("f Q ");

        var off = new System.Text.StringBuilder();
        off.Append("q ");
        off.Append($"{FmtNum(border.R / 255.0)} {FmtNum(border.G / 255.0)} {FmtNum(border.B / 255.0)} rg ");
        off.Append(CirclePath(cx, cy, rad)).Append("S Q ");

        var n = new PdfDictionary();
        n.Set(onName, MakeApXObject(on.ToString(), w, h));
        n.Set("Off", MakeApXObject(off.ToString(), w, h));
        var ap = new PdfDictionary();
        ap.Set("N", n);
        kid.Set("AP", ap);
        if (kid.GetName("AS") is null) kid.Set("AS", new PdfName("Off"));
    }

    /// <summary>Convert a System.Drawing colour to a PDF DeviceRGB component array.</summary>
    private static PdfArray ColorComponents(System.Drawing.Color color)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        return arr;
    }

    /// <summary>Build a filled-disc "on" glyph and an empty "off" glyph for every
    /// kid widget, keyed by the kid's existing on-state name. Lets a freshly-built
    /// radio group render without a viewer's NeedAppearances pass.</summary>
    internal override void GenerateAppearance()
    {
        // Targets are the kid widgets carrying a /Rect; with none, the field's
        // own dict is the widget (a single-widget radio, e.g. one rebuilt from a
        // flat import).
        var targets = new System.Collections.Generic.List<PdfDictionary>();
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
            foreach (var k in kids)
                if (Reader.Resolve(k) is PdfDictionary kd && Reader.Resolve(kd.Get("Rect")) is PdfArray)
                    targets.Add(kd);
        if (targets.Count == 0 && Reader.Resolve(Dict.Get("Rect")) is PdfArray)
            targets.Add(Dict);

        // The field-level /V selects which widget is shown filled. Without it
        // the appearance is uniformly "off" — that's how a single-widget radio
        // gets toggled on on a flat import.
        var fieldOn = Reader.Resolve(Dict.Get("V")) switch
        {
            PdfName pn => pn.Value,
            PdfString ps => ps.ToText(),
            _ => null,
        };

        foreach (var kid in targets)
        {
            if (Reader.Resolve(kid.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var r = Rectangle.FromPdfArray(ra);
            double w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) continue;

            // The on-state name is the non-"Off" key already present in /AP/N;
            // failing that, /AS itself (if not "Off"); failing that, the field's
            // /V (so a flat-imported single-widget radio adopts the selected
            // name); finally fall back to "On".
            var onName = "On";
            if (Reader.ResolveDict(kid.Get("AP")) is { } apd &&
                Reader.ResolveDict(apd.Get("N")) is { } nd)
            {
                foreach (var key in nd.Keys)
                    if (key != "Off") { onName = key; break; }
            }
            else if (kid.GetName("AS") is { } existingAs && existingAs != "Off")
                onName = existingAs;
            else if (!string.IsNullOrEmpty(fieldOn) && fieldOn != "Off")
                onName = fieldOn!;

            var n = new PdfDictionary();
            n.Set(onName, BuildRadioStream(w, h, filled: true));
            n.Set("Off", BuildRadioStream(w, h, filled: false));
            var ap = new PdfDictionary();
            ap.Set("N", n);
            kid.Set("AP", ap);
            // /AS picks the visible state. Honor an existing /AS, otherwise
            // turn this widget on iff its on-name matches the field's /V.
            if (kid.GetName("AS") is null)
            {
                var asName = !string.IsNullOrEmpty(fieldOn) && fieldOn == onName ? onName : "Off";
                kid.Set("AS", new PdfName(asName));
            }
        }
    }

    private static PdfStream BuildRadioStream(double w, double h, bool filled)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("q 0.5 w 0 0 0 RG ");
        double cx = w / 2, cy = h / 2, rad = System.Math.Min(w, h) / 2 - 1;
        if (rad <= 0) rad = System.Math.Min(w, h) / 2;
        sb.Append(CirclePath(cx, cy, rad)).Append("S ");
        if (filled)
        {
            sb.Append("0 0 0 rg ");
            sb.Append(CirclePath(cx, cy, rad * 0.5)).Append("f ");
        }
        sb.Append("Q ");
        return MakeApXObject(sb.ToString(), w, h);
    }

    /// <summary>Approximate a circle with four cubic Béziers (kappa = 0.5523).</summary>
    private static string CirclePath(double cx, double cy, double r)
    {
        var kr = 0.5523 * r;
        return $"{FmtNum(cx + r)} {FmtNum(cy)} m " +
               $"{FmtNum(cx + r)} {FmtNum(cy + kr)} {FmtNum(cx + kr)} {FmtNum(cy + r)} {FmtNum(cx)} {FmtNum(cy + r)} c " +
               $"{FmtNum(cx - kr)} {FmtNum(cy + r)} {FmtNum(cx - r)} {FmtNum(cy + kr)} {FmtNum(cx - r)} {FmtNum(cy)} c " +
               $"{FmtNum(cx - r)} {FmtNum(cy - kr)} {FmtNum(cx - kr)} {FmtNum(cy - r)} {FmtNum(cx)} {FmtNum(cy - r)} c " +
               $"{FmtNum(cx + kr)} {FmtNum(cy - r)} {FmtNum(cx + r)} {FmtNum(cy - kr)} {FmtNum(cx + r)} {FmtNum(cy)} c ";
    }

    /// <summary>Move the field's widget rectangle to <paramref name="point"/>, preserving the
    /// current width/height. Stored on the field dictionary's /Rect.</summary>
    public new void SetPosition(Aspose.Pdf.Point point)
    {
        if (point is null) throw new ArgumentNullException(nameof(point));
        var rectArr = Reader.Resolve(Dict.Get("Rect")) as PdfArray;
        double w = 0, h = 0;
        if (rectArr is { Count: >= 4 })
        {
            var r = Rectangle.FromPdfArray(rectArr);
            w = r.URX - r.LLX;
            h = r.URY - r.LLY;
        }
        Dict.Set("Rect", MakeRectArray(new Rectangle(point.X, point.Y, point.X + w, point.Y + h)));
    }

    /// <summary>/Ff bit 15 (NoToggleToOff): when true the user can't clear the
    /// selected radio button by clicking it again.</summary>
    public bool NoToggleToOff
    {
        get => (FieldFlags & (1 << 14)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 14);
            else f &= ~(1 << 14);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Visual style of the radio glyph (/MK /CA). Stored only — the FOSS
    /// appearance generator emits a filled disc regardless of this value.</summary>
    public BoxStyle Style { get; set; } = BoxStyle.Circle;

    /// <summary>Outer border shape of the radio-button widgets. Persisted on the field so
    /// it survives a save/reload round-trip; defaults to <see cref="BoxShape.Circle"/>.</summary>
    public BoxShape Shape
    {
        get => Dict.Get("AsposeBoxShape") is Aspose.Pdf.Core.PdfInteger pi
            ? (BoxShape)pi.Value
            : BoxShape.Circle;
        set => Dict.Set("AsposeBoxShape", new Aspose.Pdf.Core.PdfInteger((int)value));
    }

    /// <summary>1-based page index of the field's first widget. Shadows the inherited
    /// <see cref="Field.PageIndex"/> with a get-only Aspose.PDF for .NET shape signature.</summary>
    public new int PageIndex => base.PageIndex;

    /// <summary>The radio-button option collection. Shadows the inherited
    /// <see cref="ChoiceField.Options"/> so DeclaredOnly reflection surfaces the
    /// Aspose.PDF for .NET shape return type directly on RadioButtonField.</summary>
    public new OptionCollection Options => base.Options;

    /// <summary>
    /// 1-based access to the kid widget annotations on this radio field.
    /// Each entry exposes the per-option /Rect.
    /// </summary>
    public new Field this[int index]
    {
        get
        {
            var kids = Reader.Resolve(Dict.Get("Kids")) as PdfArray;
            if (kids is null || index < 1 || index > kids.Count)
                throw new System.ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. Must be between 1 and {kids?.Count ?? 0}.");
            var kidDict = Reader.ResolveDict(kids[index - 1]);
            if (kidDict is null)
                throw new System.InvalidOperationException("Kid widget reference does not resolve to a dictionary.");
            return new Field(kidDict, Reader);
        }
    }

    /// <summary>Whether this is a radio button (always true for RadioButtonField).</summary>
    public bool IsRadio => true;

    /// <summary>Whether this is a pushbutton (always false for RadioButtonField).</summary>
    public bool IsPushbutton => false;

    /// <summary>
    /// Currently selected value, or null if nothing is selected.
    /// For radio buttons, the value is the /V entry of the field or its parent.
    /// </summary>
    public string? SelectedValue
    {
        get
        {
            var v = Value;
            return v is not null && v != "Off" ? v : null;
        }
    }

    /// <summary>
    /// Gets or sets the 1-based index of the selected radio button option among
    /// the kid widget /AP/N appearance states (excluding the universal "Off"
    /// state). Setting a value outside [1..N] (or -1) writes /V = "Off",
    /// matching the PDF radio-button "no selection" convention.
    /// </summary>
    public override int Selected
    {
        get
        {
            var sel = SelectedValue;
            if (sel is null) return -1;
            var states = CollectKidStates();
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == sel) return i + 1;
            }
            return -1;
        }
        set
        {
            var states = CollectKidStates();
            var optValue = value >= 1 && value <= states.Count ? states[value - 1] : "Off";
            // The selection of a radio group is carried by the widget kids' /AS
            // (the chosen kid shows its on-state, every other shows "Off") and the
            // group /V (a name). Drive both so it renders and round-trips.
            Dict.Set("V", new PdfName(optValue));
            // Track each touched kid with its object number (taken from the /Kids
            // indirect ref — robust, unlike a reference-equality xref scan).
            var dirtyKids = new List<(int num, PdfDictionary dict)>();
            if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
            {
                foreach (var kidRef in kids)
                {
                    if (Reader.ResolveDict(kidRef) is not PdfDictionary kid) continue;
                    var on = RadioKidOnState(kid);
                    kid.Set("AS", new PdfName(on is not null && on == optValue ? on : "Off"));
                    var kn = kidRef is PdfIndirectRef kr ? kr.ObjectNumber
                        : OwnerDocument?.FindObjectNumber(kid) ?? -1;
                    dirtyKids.Add((kn, kid));
                }
            }
            else
            {
                Dict.Set("AS", new PdfName(optValue));
            }
            var parentRef = Dict.Get("Parent");
            var parent = Reader.ResolveDict(parentRef);
            parent?.Set("V", new PdfName(optValue));
            // Mark the group (and kids/parent) dirty so an incremental save persists it.
            if (OwnerDocument is not null)
            {
                var gnum = ObjectNumber > 0 ? ObjectNumber : OwnerDocument.FindObjectNumber(Dict);
                if (gnum > 0) OwnerDocument.MarkDirty(gnum, Dict);
                foreach (var (kn, kid) in dirtyKids)
                    if (kn > 0) OwnerDocument.MarkDirty(kn, kid);
                if (parent is not null)
                {
                    var pn = parentRef is PdfIndirectRef pr ? pr.ObjectNumber
                        : OwnerDocument.FindObjectNumber(parent);
                    if (pn > 0) OwnerDocument.MarkDirty(pn, parent);
                }
            }
        }
    }

    /// <summary>The /AP/N on-state name (the non-"Off" key) of a radio widget kid.</summary>
    private string? RadioKidOnState(PdfDictionary kid)
    {
        var ap = Reader.ResolveDict(kid.Get("AP"));
        var n = ap is null ? null : Reader.ResolveDict(ap.Get("N"));
        if (n is not null)
            foreach (var k in n.Keys)
                if (k != "Off") return k;
        return null;
    }

    /// <summary>
    /// Override of <see cref="Field.Value"/>. Setting a value that does not
    /// correspond to one of this field's /AP/N appearance states resolves to
    /// "Off" — the canonical unselected sentinel for radio buttons.
    /// </summary>
    public override string? Value
    {
        get => base.Value;
        set
        {
            if (value is not null && CollectKidStates().Contains(value))
            {
                Dict.Set("V", new PdfName(value));
                Dict.Set("AS", new PdfName(value));
                var parent = Reader.ResolveDict(Dict.Get("Parent"));
                if (parent is not null)
                {
                    parent.Set("V", new PdfName(value));
                }
            }
            else
            {
                Dict.Set("V", new PdfName("Off"));
                Dict.Set("AS", new PdfName("Off"));
                var parent = Reader.ResolveDict(Dict.Get("Parent"));
                if (parent is not null)
                {
                    parent.Set("V", new PdfName("Off"));
                }
            }
        }
    }

    private List<string> CollectKidStates()
    {
        var values = new List<string>();
        var seen = new HashSet<string>();

        void AddFromDict(PdfDictionary dict)
        {
            var apDict = Reader.ResolveDict(dict.Get("AP"));
            if (apDict is null) return;
            var nObj = Reader.Resolve(apDict.Get("N"));
            if (nObj is PdfDictionary nDict)
            {
                foreach (var key in nDict.Keys)
                {
                    if (key != "Off" && seen.Add(key))
                        values.Add(key);
                }
            }
        }

        var kidsObj = Reader.Resolve(Dict.Get("Kids"));
        if (kidsObj is PdfArray kids)
        {
            foreach (var kidRef in kids)
            {
                var kidDict = Reader.ResolveDict(kidRef);
                if (kidDict is not null)
                    AddFromDict(kidDict);
            }
        }

        if (values.Count == 0)
        {
            var parentDict = Reader.ResolveDict(Dict.Get("Parent"));
            if (parentDict is not null)
            {
                var parentKids = Reader.Resolve(parentDict.Get("Kids")) as PdfArray;
                if (parentKids is not null)
                {
                    foreach (var kidRef in parentKids)
                    {
                        var kidDict = Reader.ResolveDict(kidRef);
                        if (kidDict is not null)
                            AddFromDict(kidDict);
                    }
                }
            }
        }

        if (values.Count == 0)
            AddFromDict(Dict);

        return values;
    }
}

public class ChoiceField : Field
{
    internal ChoiceField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public ChoiceField(Document doc)
        : base(BuildChoiceDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader)
    {
    }

    public ChoiceField(Document doc, Rectangle rect)
        : base(BuildChoiceDict(rect), doc.Pages[1].Reader)
    {
    }

    public ChoiceField(Page page, Rectangle rect)
        : base(BuildChoiceDict(rect), page.Reader)
    {
    }

    private static PdfDictionary BuildChoiceDict(Rectangle rect)
    {
        var d = new PdfDictionary();
        d.Set("Type", new PdfName("Annot"));
        d.Set("Subtype", new PdfName("Widget"));
        d.Set("FT", new PdfName("Ch"));
        d.Set("Rect", MakeRectArray(rect));
        return d;
    }

    /// <summary>Draw the field appearance — the selected value for a combo box,
    /// or the full /Opt list (with a highlight rect on the selected entry) for a
    /// list box, clipped to the field rectangle, with any /MK background and
    /// border. Radio buttons override this with a per-option disc.</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        if (!TryWidgetSize(out var w, out var h)) return;
        ParseDefaultAppearance(out var fontName, out var fontSize);

        var sb = new System.Text.StringBuilder();
        sb.Append("/Tx BMC q ");
        sb.Append(BuildMkBackgroundAndBorder(w, h));
        // Clip to the interior so long values don't overflow the widget box.
        sb.Append($"1 1 {FmtNum(w - 2)} {FmtNum(h - 2)} re W n ");

        if (IsCombo)
        {
            // Combo box: render only the selected value (single visible line).
            var text = Value ?? string.Empty;
            if (text.Length > 0)
            {
                var escaped = EscapePdfText(text);
                sb.Append($"BT /{fontName} {FmtNum(fontSize)} Tf 0 g " +
                          $"2 {FmtNum(h / 2 - fontSize * 0.3)} Td ({escaped}) Tj ET ");
            }
        }
        else
        {
            // List box: each /Opt entry on its own line, top-to-bottom; selected
            // entries get a light-blue highlight rectangle behind their text.
            var opts = ReadOptionDisplayValues();
            if (opts.Count > 0)
            {
                var selected = new System.Collections.Generic.HashSet<string>(SelectedValues);
                // Line height tracks the Acrobat default — slightly more than the
                // glyph nominal so descenders don't clip on the row below.
                var lineHeight = fontSize * 1.15;
                // Acrobat draws the first option at the top of the box.
                for (var i = 0; i < opts.Count; i++)
                {
                    var top = h - 1 - i * lineHeight;
                    var rowY = top - lineHeight;
                    if (rowY < 1) break; // stop once we're past the bottom edge
                    if (selected.Contains(opts[i]))
                    {
                        // Acrobat's selection-highlight blue (#5BA9E1-ish at 50%).
                        sb.Append($"0.6 0.74 0.86 rg 1 {FmtNum(rowY)} {FmtNum(w - 2)} {FmtNum(lineHeight)} re f ");
                    }
                    var escaped = EscapePdfText(opts[i]);
                    var textY = rowY + (lineHeight - fontSize) / 2 + fontSize * 0.25;
                    sb.Append($"BT /{fontName} {FmtNum(fontSize)} Tf 0 g " +
                              $"2 {FmtNum(textY)} Td ({escaped}) Tj ET ");
                }
            }
        }
        sb.Append("Q EMC");

        var ap = new PdfDictionary();
        ap.Set("N", MakeApXObject(sb.ToString(), w, h, MakeStandardFontResources(fontName)));
        Dict.Set("AP", ap);
    }

    /// <summary>Read the /Opt entries as a flat list of display strings.
    /// Each entry may be a string (display = export) or a [export, display] pair.</summary>
    private System.Collections.Generic.List<string> ReadOptionDisplayValues()
    {
        var list = new System.Collections.Generic.List<string>();
        if (Reader.Resolve(Dict.Get("Opt")) is not PdfArray arr) return list;
        foreach (var item in arr)
        {
            var resolved = Reader.Resolve(item);
            if (resolved is PdfString s) list.Add(s.ToText());
            else if (resolved is PdfArray pair && pair.Count >= 2
                     && Reader.Resolve(pair[1]) is PdfString display)
                list.Add(display.ToText());
        }
        return list;
    }

    public bool IsCombo => (FieldFlags & (1 << 17)) != 0;
    public bool IsEditable => (FieldFlags & (1 << 18)) != 0;
    public bool IsMultiSelect => (FieldFlags & (1 << 21)) != 0;
    public bool IsSorted => (FieldFlags & (1 << 19)) != 0;

    /// <summary>Get/set MultiSelect (/Ff bit 22 — 0-based 21).</summary>
    public bool MultiSelect
    {
        get => IsMultiSelect;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 21);
            else f &= ~(1 << 21);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Get/set CommitOnSelChange (/Ff bit 27 — 0-based 26). When true,
    /// each selection change immediately commits / fires events.</summary>
    public bool CommitImmediately
    {
        get => (FieldFlags & (1 << 26)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 26);
            else f &= ~(1 << 26);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>0-based selected-option indices (Aspose.PDF for .NET shape int[] sibling
    /// of <see cref="SelectedIndices"/>; setter rewrites /V from the matching
    /// options).</summary>
    public int[] SelectedItems
    {
        get => SelectedIndices.ToArray();
        set
        {
            if (value is null || value.Length == 0)
            {
                SelectedValues = [];
                return;
            }
            var opts = Options;
            var picked = new List<string>();
            foreach (var i in value)
                if (i >= 0 && i < opts.Count) picked.Add(opts[i + 1].Value);
            SelectedValues = picked;
        }
    }

    /// <summary>
    /// The currently selected values. For single-select, returns an array with one element.
    /// For multi-select (when /V is an array), returns all selected values.
    /// Returns empty array if no selection.
    /// Setter writes the new selection to the field's /V entry.
    /// </summary>
    public IReadOnlyList<string> SelectedValues
    {
        get
        {
            var v = Reader.Resolve(Dict.Get("V"));
            if (v is null or PdfNull) return [];

            if (v is PdfArray arr)
            {
                var result = new List<string>();
                foreach (var item in arr)
                {
                    var resolved = Reader.Resolve(item);
                    var text = resolved switch
                    {
                        PdfString s => s.ToText(),
                        PdfName n => n.Value,
                        _ => null,
                    };
                    if (text is not null)
                        result.Add(text);
                }
                return result;
            }

            var single = v switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
            return single is not null ? [single] : [];
        }
        set
        {
            if (value is null || value.Count == 0)
                Dict.Set("V", PdfNull.Instance);
            else if (value.Count == 1)
                Dict.Set("V", new PdfString(System.Text.Encoding.UTF8.GetBytes(value[0])));
            else
            {
                // Multiple selected values → /V array
                var arr = new PdfArray();
                foreach (var val in value)
                    arr.Add(new PdfString(System.Text.Encoding.UTF8.GetBytes(val)));
                Dict.Set("V", arr);
            }
            // Mark the field dirty so an incremental save persists the selection change.
            if (OwnerDocument is not null && ObjectNumber >= 0)
                OwnerDocument.MarkDirty(ObjectNumber, Dict);
        }
    }

    /// <summary>
    /// The 0-based indices of selected values within the Options list.
    /// </summary>
    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            var selected = new HashSet<string>(SelectedValues);
            if (selected.Count == 0) return [];

            var result = new List<int>();
            int i = 0;
            foreach (var opt in Options)
            {
                if (selected.Contains(opt.ExportValue))
                    result.Add(i);
                i++;
            }
            return result;
        }
    }

    /// <summary>
    /// The collection of choice options. Each option has an export value and display name.
    /// Mutations on the returned collection (Add / Remove / Clear) write through to
    /// the field's /Opt array.
    /// </summary>
    public virtual OptionCollection Options => new(this);

    /// <summary>
    /// 1-based index of the currently selected option, or -1 when no option is selected
    /// or the selection cannot be matched against the option list.
    /// Setting -1 (or any value outside [1..Options.Count]) clears the selection.
    /// </summary>
    public virtual int Selected
    {
        get
        {
            var values = SelectedValues;
            if (values.Count == 0) return -1;
            int i = 1;
            foreach (var opt in Options)
            {
                if (opt.Value == values[0]) return i;
                i++;
            }
            return -1;
        }
        set
        {
            var opts = Options;
            if (value >= 1 && value <= opts.Count)
                SelectedValues = [opts[value].Value];
            else
                SelectedValues = [];
        }
    }

    /// <summary>
    /// The field value (/V). Override declared per published reference; behavior delegates
    /// to <see cref="Field.Value"/>.
    /// </summary>
    public override string? Value
    {
        get => base.Value;
        set => base.Value = value;
    }

    /// <summary>Add an option to the choice field.</summary>
    public void AddOption(string optionName)
    {
        var opt = Dict.Get("Opt") as PdfArray;
        if (opt is null)
        {
            opt = new PdfArray();
            Dict.Set("Opt", opt);
        }
        opt.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(optionName)));
    }

    /// <summary>Add an option with separate export and display values.</summary>
    public void AddOption(string export, string name)
    {
        var opt = Dict.Get("Opt") as PdfArray;
        if (opt is null)
        {
            opt = new PdfArray();
            Dict.Set("Opt", opt);
        }
        var pair = new PdfArray();
        pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(export)));
        pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
        opt.Add(pair);
    }

    /// <summary>Remove the first option matching <paramref name="optionName"/>
    /// (either as a bare PdfString or as the display half of an [export, name] pair).</summary>
    public void DeleteOption(string optionName)
    {
        // Remove the /Opt list entry (display/export pair).
        if (Dict.Get("Opt") is PdfArray opt)
        {
            for (var i = 0; i < opt.Count; i++)
            {
                var resolved = Reader.Resolve(opt[i]);
                var match = resolved switch
                {
                    PdfString s => s.ToText() == optionName,
                    PdfArray pair when pair.Count >= 2 => GetText(pair[1]) == optionName
                                                         || GetText(pair[0]) == optionName,
                    _ => false,
                };
                if (match)
                {
                    opt.RemoveAt(i);
                    break;
                }
            }
        }

        // Radio-button options also carry a kid widget annotation keyed by the
        // option's on-state name (AP/N key); without removing it the field's kid
        // count — which is what Count reports — keeps the deleted option.
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
        {
            for (var i = 0; i < kids.Count; i++)
            {
                if (Reader.Resolve(kids[i]) is PdfDictionary kid
                    && KidOnStateName(kid) == optionName)
                {
                    kids.RemoveAt(i);
                    break;
                }
            }
        }
    }

    /// <summary>The on (non-"Off") appearance-state name of a widget kid — the
    /// key under /AP/N that isn't "Off", falling back to /AS. Identifies which
    /// radio option the kid renders.</summary>
    private string? KidOnStateName(PdfDictionary kid)
    {
        if (Reader.ResolveDict(kid.Get("AP")) is { } ap
            && Reader.ResolveDict(ap.Get("N")) is { } n)
        {
            foreach (var key in n.Keys)
                if (key != "Off") return key;
        }
        var asName = kid.GetName("AS");
        return asName != "Off" ? asName : null;
    }

    internal static string GetText(PdfObject obj) => obj switch
    {
        PdfString s => s.ToText(),
        PdfName n => n.Value,
        _ => "",
    };

    /// <summary>
    /// Materialise the option list backing the <see cref="Options"/> collection.
    /// Default implementation reads the /Opt array. Subclasses (e.g. RadioButtonField)
    /// override to derive options from the field's appearance state dictionaries.
    /// </summary>
    protected internal virtual List<Option> MaterializeOptions()
    {
        var arr = Reader.Resolve(Dict.Get("Opt")) as PdfArray;
        if (arr is null) return new List<Option>();

        var selectedValues = new HashSet<string>(SelectedValues);
        var result = new List<Option>();
        foreach (var item in arr)
        {
            var resolved = Reader.Resolve(item);
            Option? option = null;
            if (resolved is PdfArray pair && pair.Count >= 2)
            {
                option = new Option(GetText(pair[0]), GetText(pair[1]));
            }
            else if (resolved is PdfString s)
            {
                var text = s.ToText();
                option = new Option(text, text);
            }
            else if (resolved is PdfName n)
            {
                option = new Option(n.Value, n.Value);
            }

            if (option is not null)
            {
                option.Index = result.Count + 1;
                option.Selected = selectedValues.Contains(option.Value);
                result.Add(option);
            }
        }
        return result;
    }
}

/// <summary>
/// Class represents option of choice field.
/// </summary>
public sealed class Option
{
    public Option(string value, string name)
    {
        ExportValue = value;
        DisplayValue = name;
    }

    /// <summary>1-based position of this option within its owning collection.</summary>
    public int Index { get; internal set; }

    /// <summary>Display name shown in the choice UI.</summary>
    public string Name { get => DisplayValue; set => DisplayValue = value; }

    /// <summary>Whether this option is currently selected on the owning field.</summary>
    public bool Selected { get; set; }

    /// <summary>Export value written to the PDF when this option is selected.</summary>
    public string Value { get => ExportValue; set => ExportValue = value; }

    internal string ExportValue { get; set; }
    internal string DisplayValue { get; set; }
}

/// <summary>
/// Mutable collection of <see cref="Option"/> values backed by the owning
/// <see cref="ChoiceField"/>'s /Opt entry. Implements ICollection&lt;Option&gt;
/// per published reference.
/// </summary>
public sealed class OptionCollection : ICollection<Option>
{
    private readonly ChoiceField _field;

    internal OptionCollection(ChoiceField field) => _field = field;

    /// <summary>Number of options on the owning field.</summary>
    public int Count => Materialize().Count;

    /// <summary>Always false — the collection is mutable.</summary>
    public bool IsReadOnly => false;

    /// <summary>Always false — the collection is not thread-safe.</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronization root for the collection.</summary>
    public object SyncRoot => this;

    /// <summary>
    /// Gets the option at the given 1-based index.
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the index falls outside [1..Count].
    /// </summary>
    public Option this[int index]
    {
        get
        {
            var list = Materialize();
            if (index < 1 || index > list.Count)
                throw new System.ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. Must be between 1 and {list.Count}.");
            return list[index - 1];
        }
    }

    /// <summary>Gets the option whose display name matches the given key, or null when not found.</summary>
    public Option? this[string name]
    {
        get
        {
            foreach (var opt in Materialize())
            {
                if (opt.Name == name) return opt;
            }
            return null;
        }
    }

    /// <summary>Method-form indexer (the public surface uses a lowercase `get` method).</summary>
    public Option get(int index) => this[index];

    /// <summary>Method-form lookup by display name.</summary>
    public Option? get(string name) => this[name];


    /// <summary>Appends an option to the field's /Opt array.</summary>
    public void Add(Option item)
    {
        if (item is null) throw new System.ArgumentNullException(nameof(item));
        var arr = _field.Dict.Get("Opt") as PdfArray;
        if (arr is null)
        {
            arr = new PdfArray();
            _field.Dict.Set("Opt", arr);
        }
        if (item.Value == item.Name)
        {
            arr.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(item.Value)));
        }
        else
        {
            var pair = new PdfArray();
            pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(item.Value)));
            pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(item.Name)));
            arr.Add(pair);
        }
    }

    /// <summary>Removes all options from the field.</summary>
    public void Clear() => _field.Dict.Set("Opt", new PdfArray());

    /// <summary>Returns true when an option with the same Value+Name exists in the collection.</summary>
    public bool Contains(Option item)
    {
        if (item is null) return false;
        foreach (var opt in Materialize())
        {
            if (opt.Value == item.Value && opt.Name == item.Name) return true;
        }
        return false;
    }

    /// <summary>Copies the materialised options into the supplied array starting at the given offset.</summary>
    public void CopyTo(Option[] array, int index) => Materialize().CopyTo(array, index);

    /// <summary>
    /// Removes the first option whose Value+Name match the supplied option. Returns true if a
    /// matching entry was removed.
    /// </summary>
    public bool Remove(Option item)
    {
        if (item is null) return false;
        var arr = _field.Reader.Resolve(_field.Dict.Get("Opt")) as PdfArray;
        if (arr is null) return false;
        for (int i = 0; i < arr.Count; i++)
        {
            var resolved = _field.Reader.Resolve(arr[i]);
            string ev, dv;
            if (resolved is PdfArray pair && pair.Count >= 2)
            {
                ev = ChoiceField.GetText(pair[0]);
                dv = ChoiceField.GetText(pair[1]);
            }
            else if (resolved is PdfString s)
            {
                ev = dv = s.ToText();
            }
            else if (resolved is PdfName n)
            {
                ev = dv = n.Value;
            }
            else continue;

            if (ev == item.Value && dv == item.Name)
            {
                arr.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Enumerates the options on the field at call time.</summary>
    public IEnumerator<Option> GetEnumerator() => Materialize().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<Option> Materialize() => _field.MaterializeOptions();
}

/// <summary>Combo box (drop-down) form field — a ChoiceField with the Combo flag set.</summary>
public class ComboBoxField : ChoiceField
{
    internal ComboBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Parameterless ctor (caller binds to a page later).</summary>
    public ComboBoxField() : base(BuildComboFieldDict(new Rectangle(0, 0, 0, 0)), PdfReader.Empty) { }

    /// <summary>Document-bound ctor; rectangle defaults to empty.</summary>
    public ComboBoxField(Document doc) : base(BuildComboFieldDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader) { }

    /// <summary>Document-bound ctor with an explicit rectangle.</summary>
    public ComboBoxField(Document doc, Rectangle rect)
        : base(BuildComboFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>
    /// Creates a new combo box field on the specified page with the given rectangle.
    /// </summary>
    public ComboBoxField(Page page, Rectangle rect)
        : base(BuildComboFieldDict(rect), page.Reader)
    {
    }

    /// <summary>Whether the user can type a free-form value (/Ff bit 19, Edit).</summary>
    public bool Editable
    {
        get => (FieldFlags & (1 << 18)) != 0;
        set => Dict.Set("Ff", new PdfInteger(value ? FieldFlags | (1 << 18) : FieldFlags & ~(1 << 18)));
    }

    /// <summary>Whether spell-check is enabled (/Ff bit 23, DoNotSpellCheck inverted).</summary>
    public bool SpellCheck
    {
        get => (FieldFlags & (1 << 22)) == 0;
        set => Dict.Set("Ff", new PdfInteger(value ? FieldFlags & ~(1 << 22) : FieldFlags | (1 << 22)));
    }

    private static PdfDictionary BuildComboFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Ch"));
        dict.Set("Ff", new PdfInteger(1 << 17)); // Combo flag
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }
}

/// <summary>List box form field — a ChoiceField without the Combo flag.</summary>
public class ListBoxField : ChoiceField
{
    internal ListBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Parameterless ctor (caller binds to a page later).</summary>
    public ListBoxField() : base(BuildListFieldDict(new Rectangle(0, 0, 0, 0)), PdfReader.Empty) { }

    /// <summary>
    /// Creates a new list box field on the specified page with the given rectangle.
    /// </summary>
    public ListBoxField(Page page, Rectangle rect)
        : base(BuildListFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Creates a new list box field associated with the document (page assigned via Form.Add).
    /// </summary>
    public ListBoxField(Document doc, Rectangle rect)
        : base(BuildListFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>Set-only selected-items array.</summary>
    public new int[] SelectedItems
    {
        set => base.SelectedItems = value;
    }

    /// <summary>1-based index of the selected option (-1 if none); setting it
    /// selects that single option. Delegates to the 1-based base setter so it
    /// stays consistent with the getter and <see cref="ChoiceField.Options"/>
    /// (the int[] SelectedItems API is index-based and handled separately).</summary>
    public new int Selected
    {
        get => base.Selected;
        set => base.Selected = value;
    }

    /// <summary>0-based index of the first visible option when the list is scrolled. Stored only.</summary>
    public int TopIndex { get; set; }

    private static PdfDictionary BuildListFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Ch"));
        // No Combo flag — list box
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }
}

/// <summary>Push-button form field (FT=Btn with Pushbutton flag set).</summary>
public class ButtonField : Field
{
    internal ButtonField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public ButtonField() : base(BuildButtonDict(), PdfReader.Empty) { }

    public ButtonField(Document doc, Rectangle rect)
        : base(BuildButtonDict(rect), doc?.Reader ?? PdfReader.Empty) { }

    public ButtonField(Page page, Rectangle rect)
        : base(BuildButtonDict(rect), page?.Reader ?? PdfReader.Empty) { }

    private static PdfDictionary BuildButtonDict(Rectangle? rect = null)
    {
        var d = new PdfDictionary();
        d.Set("FT", new PdfName("Btn"));
        d.Set("Ff", new PdfInteger(1 << 16));
        if (rect is not null)
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(rect.LLX));
            arr.Add(new PdfReal(rect.LLY));
            arr.Add(new PdfReal(rect.URX));
            arr.Add(new PdfReal(rect.URY));
            d.Set("Rect", arr);
        }
        return d;
    }

    /// <summary>
    /// Caption shown on the button in its normal state (MK dict /CA entry).
    /// Setting null or empty removes the entry.
    /// </summary>
    public string? NormalCaption
    {
        get
        {
            // MK dict lives on the widget; try own dict first, fallback to first kid (widget)
            var mk = GetMK(create: false);
            return (mk?.Get("CA") as PdfString)?.ToText();
        }
        set
        {
            var mk = GetMK(create: value is not null);
            if (mk is null) return;
            if (value is null)
                mk.Remove("CA");
            else
                mk.Set("CA", new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
        }
    }

    /// <summary>Draw the push-button face (/MK background + border, or default
    /// grey chrome) and centre the normal caption.</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        if (!TryWidgetSize(out var w, out var h)) return;
        ParseDefaultAppearance(out var fontName, out var fontSize);

        var sb = new System.Text.StringBuilder();
        sb.Append("q ");
        var mk = BuildMkBackgroundAndBorder(w, h);
        if (mk.Length == 0)
            // Default push-button chrome: light-grey face with a grey border.
            sb.Append($"0.75 0.75 0.75 rg 0 0 {FmtNum(w)} {FmtNum(h)} re f " +
                      $"0.5 0.5 0.5 RG 1 w 0.5 0.5 {FmtNum(w - 1)} {FmtNum(h - 1)} re S ");
        else
            sb.Append(mk);

        var caption = NormalCaption ?? string.Empty;
        if (caption.Length > 0)
        {
            var escaped = EscapePdfText(caption);
            // Rough centring: average glyph advance ~0.5em.
            var textW = caption.Length * fontSize * 0.5;
            var tx = System.Math.Max(2, (w - textW) / 2);
            var ty = h / 2 - fontSize * 0.35;
            sb.Append($"BT /{fontName} {FmtNum(fontSize)} Tf 0 g {FmtNum(tx)} {FmtNum(ty)} Td ({escaped}) Tj ET ");
        }
        sb.Append("Q");

        var ap = new PdfDictionary();
        ap.Set("N", MakeApXObject(sb.ToString(), w, h, MakeStandardFontResources(fontName)));
        Dict.Set("AP", ap);
    }

    private PdfDictionary? GetMK(bool create)
    {
        // Button widget MK dict: walk through own dict, kids, to find the widget carrying MK
        var target = LocateWidgetDict();
        if (target is null) return null;
        if (target.Get("MK") is PdfDictionary existing) return existing;
        if (!create) return null;
        var mk = new PdfDictionary();
        target.Set("MK", mk);
        return mk;
    }

    private PdfDictionary LocateWidgetDict()
    {
        // Prefer this field's dict (single widget) if it has Subtype=Widget
        if (Dict.Get("Subtype") is PdfName sn && sn.Value == "Widget")
            return Dict;
        // Otherwise look at Kids[0]
        var kids = Reader.Resolve(Dict.Get("Kids")) as PdfArray;
        if (kids is null || kids.Count == 0) return Dict;
        return Reader.Resolve(kids[0]) as PdfDictionary ?? Dict;
    }

    private string? GetMkString(string key)
    {
        var mk = GetMK(create: false);
        return (mk?.Get(key) as PdfString)?.ToText();
    }

    private void SetMkString(string key, string? value)
    {
        var mk = GetMK(create: value is not null);
        if (mk is null) return;
        if (value is null)
            mk.Remove(key);
        else
            mk.Set(key, new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
    }

    /// <summary>Caption shown when the user holds the mouse button down (/MK /AC).</summary>
    public string? AlternateCaption
    {
        get => GetMkString("AC");
        set => SetMkString("AC", value);
    }

    /// <summary>Caption shown when the cursor hovers (/MK /RC).</summary>
    public string? RolloverCaption
    {
        get => GetMkString("RC");
        set => SetMkString("RC", value);
    }

    /// <summary>Form XObject used as the normal-state icon (/MK /I). Stored only.</summary>
    public XForm? NormalIcon { get; set; }

    /// <summary>Form XObject used as the rollover icon (/MK /RI). Stored only.</summary>
    public XForm? RolloverIcon { get; set; }

    /// <summary>Form XObject used as the down/alternate icon (/MK /IX). Stored only.</summary>
    public XForm? AlternateIcon { get; set; }

    /// <summary>Position of the caption relative to the icon (/MK /TP).</summary>
    public IconCaptionPosition ICPosition
    {
        get
        {
            var mk = GetMK(create: false);
            return (IconCaptionPosition)(int)((mk?.Get("TP") as PdfInteger)?.Value ?? 0);
        }
        set
        {
            var mk = GetMK(create: true);
            mk?.Set("TP", new PdfInteger((int)value));
        }
    }

    /// <summary>Icon scaling parameters (/MK /IF). Always returns a fresh wrapper; not persisted.</summary>
    public IconFit IconFit { get; } = new IconFit();

    /// <summary>
    /// Attach an image as the button's normal icon. Cross-platform-friendly stub:
    /// the image is stored opaquely (advanced GDI rendering not supported).
    /// </summary>
    public void AddImage(System.Drawing.Image image) { _ = image; }
}

/// <summary>
/// PDF Annotation Handler "TP" entry — caption / icon layout for push-button widgets.
/// PDF 32000-1 §12.5.6.19 Table 189.
/// </summary>
public enum IconCaptionPosition
{
    NoIcon = 0,
    NoCaption = 1,
    CaptionBelowIcon = 2,
    CaptionAboveIcon = 3,
    CaptionToTheRight = 4,
    CaptionToTheLeft = 5,
    CaptionOverlaid = 6,
}

/// <summary>
/// PDF Annotation Handler "IF" entry — icon-fit dictionary for push-button widgets.
/// Stored-only wrapper; values are not currently emitted into /MK /IF.
/// </summary>
public class IconFit
{
    public ScalingMode ScalingMode { get; set; } = ScalingMode.Proportional;
    public ScalingReason ScalingReason { get; set; } = ScalingReason.Always;
    public double LeftoverLeft { get; set; } = 0.5;
    public double LeftoverBottom { get; set; } = 0.5;
    public bool SpreadOnBorder { get; set; }

    public static ScalingMode NameToScalingMode(string mode) => mode switch
    {
        "A" => ScalingMode.Anamorphic,
        "P" => ScalingMode.Proportional,
        _ => ScalingMode.Proportional,
    };

    public static ScalingReason NameToScalingReason(string reason) => reason switch
    {
        "A" => ScalingReason.Always,
        "B" => ScalingReason.IconIsBigger,
        "S" => ScalingReason.IconIsSmaller,
        "N" => ScalingReason.Never,
        _ => ScalingReason.Always,
    };

    public static string ScalingModeToName(ScalingMode mode) => mode switch
    {
        ScalingMode.Anamorphic => "A",
        ScalingMode.Proportional => "P",
        _ => "P",
    };

    public static string ScalingReasonToName(ScalingReason reason) => reason switch
    {
        ScalingReason.Always => "A",
        ScalingReason.IconIsBigger => "B",
        ScalingReason.IconIsSmaller => "S",
        ScalingReason.Never => "N",
        _ => "A",
    };
}

/// <summary>How a button icon is scaled to fit its rectangle (/MK /IF /SW).</summary>
public enum ScalingMode
{
    Anamorphic = 0,
    Proportional = 1,
}

/// <summary>When to scale the icon to fit its rectangle (/MK /IF /S).</summary>
public enum ScalingReason
{
    Always = 0,
    IconIsBigger = 1,
    IconIsSmaller = 2,
    Never = 3,
}

public class SignatureField : Field
{
    /// <summary>Latest signed PDF bytes produced by <see cref="Sign(Signature)"/>.
    /// The caller retrieves these via the <see cref="OwnerDocument"/>'s
    /// document-level signing flow — the field itself can't mutate its
    /// owner Document's underlying byte buffer in the FOSS build.</summary>
    private byte[]? _signedBytes;

    /// <summary>Returns the latest signed bytes, or null when this field
    /// hasn't been signed via <see cref="Sign(Signature)"/>.</summary>
    public byte[]? SignedBytes => _signedBytes;

    internal SignatureField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create an empty signature field on <paramref name="doc"/>'s
    /// first page sized to <paramref name="rect"/>. Add the field to
    /// <c>doc.Form</c> to expose it on the form, then sign via
    /// <see cref="Sign(Signature)"/>.</summary>
    public SignatureField(Document doc, Rectangle rect)
        : base(BuildSignatureFieldDict(rect), doc.Pages[1].Reader)
    {
        OwnerDocument = doc;
    }

    /// <summary>Create an empty signature field on <paramref name="page"/>
    /// sized to <paramref name="rect"/>. <see cref="Field.OwnerDocument"/>
    /// is set when the field is later added to a Form.</summary>
    public SignatureField(Page page, Rectangle rect)
        : base(BuildSignatureFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Information about the digital signature in this field. Returns
    /// null when the field's /V is not set (the field has not been
    /// signed). Reads /Reason, /Location, /Name (signer), /M (date) and
    /// /SubFilter (signature handler) from the signature dictionary.
    /// </summary>
    public Signature? Signature
    {
        get
        {
            var sigDict = Reader.ResolveDict(Dict.Get("V"));
            if (sigDict is null) return null;
            return Forms.Signature.FromDict(sigDict, Reader, FullName);
        }
    }

    /// <summary>Sign this field with the supplied <paramref name="signature"/>'s
    /// embedded certificate. The signed PDF bytes land on
    /// <see cref="SignedBytes"/> — read those and persist via the owner
    /// Document's own Save path. (Direct write-back into Document._data
    /// requires invasive Document API changes deferred for now.)</summary>
    public void Sign(Signature signature)
    {
        if (signature is null) throw new System.ArgumentNullException(nameof(signature));
        var doc = OwnerDocument
            ?? throw new System.InvalidOperationException("SignatureField has no OwnerDocument. Add the field to doc.Form before signing.");
        var facade = new Facades.PdfFileSignature(doc);
        facade.Sign(FullName ?? string.Empty, signature);
        _signedBytes = facade.ToByteArray();
    }

    /// <summary>Sign with a PFX from a stream + password — convenience over
    /// <see cref="Sign(Signature)"/>.</summary>
    public void Sign(Signature signature, Stream pfx, string pass)
    {
        if (signature is null) throw new System.ArgumentNullException(nameof(signature));
        if (pfx is not null && signature.Certificate is null)
        {
            using var ms = new MemoryStream();
            pfx.CopyTo(ms);
            signature.Certificate = Security.PdfCertificate.FromPfx(ms.ToArray(), pass ?? string.Empty);
        }
        Sign(signature);
    }

    /// <summary>Extract the signing certificate of this field's /V as a DER
    /// byte stream (.cer).</summary>
    public Stream? ExtractCertificate()
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        return new Facades.PdfFileSignature(doc).ExtractCertificate(FullName ?? string.Empty);
    }

    /// <summary>Extract the signing certificate as an X509Certificate2.</summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? ExtractCertificateObject()
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        var facade = new Facades.PdfFileSignature(doc);
        var sigName = new Facades.SignatureName(FullName ?? string.Empty, PartialName ?? string.Empty, hasSignature: true);
        return facade.TryExtractCertificate(sigName, out System.Security.Cryptography.X509Certificates.X509Certificate2 cert)
            ? cert : null;
    }

    /// <summary>Extract the visible-signature appearance (the /AP /N stream)
    /// as a Stream. Returns null when the field has no appearance.</summary>
    public Stream? ExtractImage()
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        return new Facades.PdfFileSignature(doc).ExtractImage(FullName ?? string.Empty);
    }

    /// <summary>Extract the visible-signature appearance and re-encode it as
    /// <paramref name="format"/>. The raw /AP /N stream is decoded then
    /// re-saved through System.Drawing.Image — Windows-only at runtime.</summary>
    public Stream? ExtractImage(System.Drawing.Imaging.ImageFormat format)
    {
        var raw = ExtractImage();
        if (raw is null || format is null) return raw;
#pragma warning disable CA1416
        try
        {
            using var img = System.Drawing.Image.FromStream(raw);
            var output = new MemoryStream();
            img.Save(output, format);
            output.Position = 0;
            return output;
        }
        catch { return null; }
#pragma warning restore CA1416
    }

    private static PdfDictionary BuildSignatureFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        var r = new Aspose.Pdf.Core.PdfArray();
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.LLX));
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.LLY));
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.URX));
        r.Add(new Aspose.Pdf.Core.PdfReal(rect.URY));
        dict.Set("Rect", r);
        return dict;
    }
}

/// <summary>One option of a <see cref="RadioButtonField"/>. Stored-only wrapper
/// emitted via <see cref="RadioButtonField.Add(RadioButtonOptionField)"/>.</summary>
public sealed class RadioButtonOptionField : BaseParagraph
{
    public RadioButtonOptionField() { }

    public RadioButtonOptionField(Page page, Rectangle rect)
    {
        _ = page;
        PendingRect = rect;
    }

    /// <summary>Default-appearance settings (/DA) applied to the option's
    /// widget. Auto-initialized so callers can set TextColor/Font directly.</summary>
    public DefaultAppearance DefaultAppearance { get; } = new DefaultAppearance();

    /// <summary>Caption text shown next to the radio glyph. Stored only.</summary>
    public Aspose.Pdf.Text.TextFragment? Caption { get; set; }

    /// <summary>The /Opt name written into the parent field when this option is added.</summary>
    public string? OptionName { get; set; }

    /// <summary>Visual style of the radio glyph. Stored only.</summary>
    public BoxStyle Style { get; set; } = BoxStyle.Circle;

    /// <summary>Width of the option's widget rectangle in points. Stored only.</summary>
    public double Width { get; set; }

    /// <summary>Height of the option's widget rectangle in points. Stored only.</summary>
    public double Height { get; set; }

    /// <summary>Border styling applied to the option's widget. Stored only.</summary>
    public Border? Border { get; set; }

    /// <summary>Visual-characteristics dictionary (/MK) applied to the option's
    /// widget. Auto-initialized so callers can set
    /// <c>option.Characteristics.Border = Color.Black</c> on a fresh instance.</summary>
    public Aspose.Pdf.Annotations.Characteristics Characteristics { get; } =
        new Aspose.Pdf.Annotations.Characteristics();

    /// <summary>Pending widget rectangle, used by Add to size the kid annotation.</summary>
    internal Rectangle? PendingRect { get; }

    /// <summary>The kid widget dictionary created for this option by
    /// <see cref="RadioButtonField.Add(RadioButtonOptionField)"/>, and the reader
    /// that resolves it. Set once the option is added to a field.</summary>
    internal PdfDictionary? KidDict { get; set; }
    internal Aspose.Pdf.IO.PdfReader? KidReader { get; set; }

    /// <summary>The radio-button group this option was added to (via
    /// <see cref="RadioButtonField.Add(RadioButtonOptionField)"/>). Lets the
    /// generator register the parent group in the AcroForm when only the options
    /// are placed into the page's paragraph tree.</summary>
    internal RadioButtonField? OwnerRadio { get; set; }

    /// <summary>The option's widget rectangle. Once the option has been added to a
    /// field this reads the live kid annotation's /Rect (so it reflects later
    /// transforms, e.g. <c>PdfFileEditor.ResizeContents</c>); before that it returns
    /// the rectangle supplied to the constructor.</summary>
    public Rectangle? Rect
    {
        get
        {
            if (KidDict is not null && KidReader is not null
                && KidReader.Resolve(KidDict.Get("Rect")) is PdfArray arr && arr.Count >= 4)
                return Rectangle.FromPdfArray(arr);
            return PendingRect;
        }
    }
}
