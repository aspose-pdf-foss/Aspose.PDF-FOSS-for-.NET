using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Provides access to a page's resource collections (fonts, images).
/// </summary>
public class PageResources
{
    private readonly Page? _page;
    private readonly XForm? _xform;
    private readonly PdfDictionary? _resDict;
    private readonly PdfReader? _resReader;

    /// <summary>Low-level view of the underlying resource dictionary (the
    /// corpus' <c>EngineDict</c> assert surface — the same canonical view the
    /// annotation/field bridges hand out).</summary>
    internal global::Aspose.Pdf.Forms.FieldDictionaryView EngineDict
    {
        get
        {
            if (_resDict is not null)
                return global::Aspose.Pdf.Forms.FieldDictionaryView.For(_resDict, _resReader ?? Aspose.Pdf.IO.PdfReader.Empty);
            if (_xform is not null && XFormResourcesDict() is { } xr)
                return global::Aspose.Pdf.Forms.FieldDictionaryView.For(xr, _xform.Reader);
            if (_page is not null)
            {
                var pr = _page.Reader.ResolveDict(_page.Dict.Get("Resources"));
                if (pr is not null) return global::Aspose.Pdf.Forms.FieldDictionaryView.For(pr, _page.Reader);
            }
            return global::Aspose.Pdf.Forms.FieldDictionaryView.For(new Core.PdfDictionary(), Aspose.Pdf.IO.PdfReader.Empty);
        }
    }

    internal PageResources(Page page) => _page = page;

    /// <summary>XForm-backed ctor: enumerates resources via the XForm's
    /// stream dictionary (Font / XObject entries on the form, not the
    /// page).</summary>
    internal PageResources(XForm xform) { _xform = xform; }

    /// <summary>Resource-dictionary-backed ctor: enumerates /Font, /XObject etc.
    /// directly from a resource dict (e.g. the AcroForm /DR).</summary>
    internal PageResources(PdfDictionary resourceDict, PdfReader reader)
    {
        _resDict = resourceDict;
        _resReader = reader;
    }

    private Core.PdfDictionary? XFormResourcesDict()
    {
        if (_xform is null) return null;
        return _xform.Reader.ResolveDict(_xform.StreamDict.Get("Resources"));
    }

    /// <summary>Font resources on this page (or the XForm's stream dict
    /// when constructed via the XForm ctor).</summary>
    public FontCollection Fonts
    {
        get
        {
            if (_resDict is not null) return FontCollection.ForResources(_resDict, _resReader!);
            if (_page is not null) return _page.Fonts;
            // An XForm's stream dict carries /Resources directly (a resource dict
            // whose /Font maps names to font dicts), so read it via ForResources —
            // the page-dict ctor would look for a nested /Resources and find none.
            // A form whose resources carry NO /Font yields null (public-API
            // shape — callers probe `GetResources().Fonts == null` for that layout).
            var resDict = XFormResourcesDict();
            if (resDict is null) return null!;
            if (_xform!.Reader.ResolveDict(resDict.Get("Font")) is null
                && resDict.Get("Font") is not Core.PdfDictionary) return null!;
            return FontCollection.ForResources(resDict, _xform!.Reader);
        }
    }

    /// <summary>Image resources on this page.</summary>
    public XImageCollection Images
    {
        get
        {
            if (_page is not null) return _page.Images;
            // The collection ctor discovers images via dict.Get("Resources")/XObject
            // (recursing nested forms). An XForm's stream dict carries /Resources, so
            // pass the stream dict — passing the already-resolved Resources dict would
            // make the ctor look for a nested /Resources that isn't there and yield an
            // empty collection.
            if (_xform is not null) return new XImageCollection(_xform.StreamDict, _xform.Reader);
            if (_resDict is not null)
            {
                var wrap = new Core.PdfDictionary();
                wrap.Set("Resources", _resDict);
                return new XImageCollection(wrap, _resReader!);
            }
            return new XImageCollection(new Core.PdfDictionary(), _resReader!);
        }
    }

    /// <summary>XForm (Form XObject) resources on this page.</summary>
    public XFormCollection Forms
    {
        get
        {
            var reader = _page?.Reader ?? _xform!.Reader;
            var resources = _page is not null
                ? reader.ResolveDict(_page.Dict.Get("Resources"))
                : XFormResourcesDict();
            if (resources is null) return new XFormCollection(new Core.PdfDictionary(), reader, _page);
            var xobjects = reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is null) return new XFormCollection(new Core.PdfDictionary(), reader, _page);
            return new XFormCollection(xobjects, reader, _page);
        }
    }
}

/// <summary>
/// Type alias for PageResources, matching the Resources class name.
/// </summary>
public class Resources : PageResources
{
    internal Resources(Page page) : base(page) { }

    /// <summary>XForm-backed resources accessor: resolves Font / Image /
    /// XObject entries from an XForm's stream dictionary rather than a
    /// page dictionary.</summary>
    internal Resources(XForm xform) : base(xform) { }

    /// <summary>Resource-dictionary-backed accessor (e.g. the AcroForm /DR),
    /// which carries /Font etc. directly.</summary>
    internal Resources(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Font resources (re-declared so reflection sees the member on Resources).</summary>
    public new FontCollection Fonts => base.Fonts;

    /// <summary>Image resources (re-declared so reflection sees the member on Resources).</summary>
    public new XImageCollection Images => base.Images;

    /// <summary>XForm resources (re-declared so reflection sees the member on Resources).</summary>
    public new XFormCollection Forms => base.Forms;

    /// <summary>Font collection accessor — <paramref name="CreateIfAbsent"/> is accepted
    /// for signature compatibility; a live collection is always returned.</summary>
    public FontCollection GetFonts(bool CreateIfAbsent) { _ = CreateIfAbsent; return base.Fonts; }

    /// <summary>Enumerate every /ExtGState entry on this page's resources as a name→value map.</summary>
    public System.Collections.Generic.Dictionary<string, ExtGStateValue> GetExtGStates()
        => new();

    /// <summary>Free resource-cache memory. No-op in this build — the
    /// FOSS resource readers don't cache decoded bytes.</summary>
    public void FreeMemory() { }

    /// <summary>One /ExtGState entry — name plus stroke/fill alpha factors.</summary>
    public class ExtGStateValue
    {
        public ExtGStateValue(string name) { Name = name; }

        /// <summary>Resource name (e.g. "GS1").</summary>
        public string Name { get; }

        /// <summary>Stroking alpha constant (CA).</summary>
        public double CA { get; internal set; } = 1.0;

        /// <summary>Non-stroking alpha constant (ca).</summary>
        public double ca { get; internal set; } = 1.0;
    }
}

/// <summary>
/// Represents a Form XObject (reusable content stream with its own resources).
/// </summary>
public sealed class XForm
{
    private readonly Core.PdfStream _stream;
    private readonly IO.PdfReader _reader;
    private OperatorCollection? _contents;

    internal XForm(Core.PdfStream stream, IO.PdfReader reader)
    {
        _stream = stream;
        _reader = reader;
    }

    /// <summary>The name of this XForm in the page's XObject resources dict.</summary>
    public string? Name { get; set; }

    /// <summary>The Form XObject's content stream as a typed operator collection.
    /// Lazy: parses on first access and caches.</summary>
    public OperatorCollection Contents
        => _contents ??= new OperatorCollection(() => _reader.DecodeStream(_stream));

    /// <summary>The raw decoded content bytes of this XForm. Use
    /// <see cref="Contents"/> for typed-operator iteration.</summary>
    public byte[] DecodedBytes => _reader.DecodeStream(_stream);

    /// <summary>Replace this form's content with raw (decoded) bytes, dropping the
    /// existing filter so the writer re-compresses on save. Used by the text-edit
    /// path when a fragment extracted from this form has its Text changed/removed.</summary>
    internal void SetDecodedContent(byte[] data)
    {
        _stream.Dict.Remove("Filter");
        _stream.Dict.Remove("DecodeParms");
        _stream.ReplaceData(data);
        _contents = null;
    }

    /// <summary>Internal reader for object resolution.</summary>
    internal IO.PdfReader Reader => _reader;

    /// <summary>The XForm's stream dictionary (contains Resources, BBox, etc.).</summary>
    internal Core.PdfDictionary StreamDict => _stream.Dict;

    /// <summary>The bounding box of this XForm. /BBox PDF entry.</summary>
    public Rectangle? BBox
    {
        get
        {
            var arr = _reader.Resolve(_stream.Dict.Get("BBox")) as Core.PdfArray;
            if (arr is null || arr.Count < 4) return null;
            double getN(int i) => arr[i] switch
            {
                Core.PdfInteger pi => pi.Value,
                Core.PdfReal pr => pr.Value,
                _ => 0
            };
            return new Rectangle(getN(0), getN(1), getN(2), getN(3));
        }
        set
        {
            if (value is null) { _stream.Dict.Remove("BBox"); return; }
            var arr = new Core.PdfArray();
            arr.Add(new Core.PdfReal(value.LLX));
            arr.Add(new Core.PdfReal(value.LLY));
            arr.Add(new Core.PdfReal(value.URX));
            arr.Add(new Core.PdfReal(value.URY));
            _stream.Dict.Set("BBox", arr);
        }
    }

    /// <summary>Alias for <see cref="BBox"/>; the public API exposes both names.</summary>
    public Rectangle Rectangle => BBox ?? new Rectangle(0, 0, 0, 0);

    /// <summary>The XObject Subtype (always "Form" for XForm instances).</summary>
    public string Subtype => _stream.Dict.GetName("Subtype") ?? "Form";

    /// <summary>The Form's /Matrix entry (the transformation applied
    /// when the form is painted). Identity matrix when absent.</summary>
    public Matrix Matrix
    {
        get
        {
            var arr = _reader.Resolve(_stream.Dict.Get("Matrix")) as Core.PdfArray;
            if (arr is null || arr.Count < 6) return new Matrix(1, 0, 0, 1, 0, 0);
            double getN(int i) => arr[i] switch
            {
                Core.PdfInteger pi => pi.Value,
                Core.PdfReal pr => pr.Value,
                _ => 0
            };
            return new Matrix(getN(0), getN(1), getN(2), getN(3), getN(4), getN(5));
        }
        set
        {
            if (value is null) { _stream.Dict.Remove("Matrix"); return; }
            var arr = new Core.PdfArray();
            arr.Add(new Core.PdfReal(value.A));
            arr.Add(new Core.PdfReal(value.B));
            arr.Add(new Core.PdfReal(value.C));
            arr.Add(new Core.PdfReal(value.D));
            arr.Add(new Core.PdfReal(value.E));
            arr.Add(new Core.PdfReal(value.F));
            _stream.Dict.Set("Matrix", arr);
        }
    }

    /// <summary>The Form's /Intent (IT) entry; null when absent.</summary>
    public string? IT => _stream.Dict.GetName("IT");

    /// <summary>Open Prepress Interface (OPI) wrapper. Always non-null;
    /// the underlying /OPI entry may be absent (in which case the wrapper
    /// reports defaults).</summary>
    public Opi Opi => new Opi(this);

    /// <summary>Form XObject resources (fonts / images / nested XObjects)
    /// declared on this XForm's stream dict. Aspose.Pdf.Resources-typed;
    /// backed by the XForm-aware
    /// <see cref="PageResources(XForm)"/> ctor.</summary>
    public Resources Resources => new Resources(this);

    /// <summary>Method-style resources accessor, kept for API compatibility.</summary>
    public Resources GetResources() => Resources;

    /// <summary>Method-style resources accessor with create-on-demand.
    /// The FOSS Resources are always materialisable, so the
    /// <paramref name="allowCreate"/> flag is ignored.</summary>
    public Resources GetResources(bool allowCreate) { _ = allowCreate; return Resources; }

    /// <summary>Releases resources held by this XForm. Currently a no-op
    /// — the FOSS XForm reader holds no unmanaged buffers.</summary>
    public void Dispose() { _contents = null; }

    /// <summary>Free decoded-content cache. The FOSS XForm decodes on
    /// demand, so this clears the cached OperatorCollection only.</summary>
    public void FreeMemory() { _contents = null; }

    /// <summary>Construct a new XForm from a source page's content stream
    /// and register it on <paramref name="document"/>. Stored only — the
    /// FOSS XForm-from-page pipeline isn't fully wired; the returned
    /// XForm wraps a freshly created stream with the source page's BBox
    /// and the page's content bytes.</summary>
    public static XForm CreateNewForm(Page source, Document document)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (document is null) throw new ArgumentNullException(nameof(document));
        // Best-effort: build a /Form XObject stream from the source page's
        // /MediaBox + content bytes. Mostly stored: this XForm does not
        // currently get registered in document.Pages or any resource dict.
        var dict = new Core.PdfDictionary();
        dict.Set("Type", new Core.PdfName("XObject"));
        dict.Set("Subtype", new Core.PdfName("Form"));
        var rect = source.Rect ?? new Rectangle(0, 0, 612, 792);
        var bbox = new Core.PdfArray();
        bbox.Add(new Core.PdfReal(rect.LLX));
        bbox.Add(new Core.PdfReal(rect.LLY));
        bbox.Add(new Core.PdfReal(rect.URX));
        bbox.Add(new Core.PdfReal(rect.URY));
        dict.Set("BBox", bbox);
        var stream = new Core.PdfStream(dict, Array.Empty<byte>());
        return new XForm(stream, source.Reader);
    }
}

/// <summary>Open Prepress Interface (OPI) metadata wrapper for an
/// <see cref="XForm"/>. Stored only — the FOSS write path doesn't emit
/// /OPI entries.</summary>
public sealed class Opi
{
    private readonly XForm _xform;

    /// <summary>Construct an OPI wrapper bound to <paramref name="xform"/>.</summary>
    public Opi(XForm xform) { _xform = xform ?? throw new ArgumentNullException(nameof(xform)); }

    /// <summary>OPI dictionary version (1.3 / 2.0 / …). Empty when absent.</summary>
    public string Version => string.Empty;

    /// <summary>External file specification referenced by the OPI entry.</summary>
    public string FileSpecification => string.Empty;

    /// <summary>OPI cropping/positioning rectangle as 4 PDF points.</summary>
    public double[] Position => Array.Empty<double>();
}

/// <summary>Resources (fonts, xobjects) on an XForm's stream dict.</summary>
public sealed class XFormResources
{
    private readonly Core.PdfDictionary _streamDict;
    private readonly IO.PdfReader _reader;

    internal XFormResources(Core.PdfDictionary streamDict, IO.PdfReader reader)
    {
        _streamDict = streamDict;
        _reader = reader;
    }

    /// <summary>Fonts in this XForm's resources dict (null if none).</summary>
    public FontCollection? Fonts
    {
        get
        {
            var resources = _reader.ResolveDict(_streamDict.Get("Resources"));
            if (resources is null) return null;
            var fontDict = _reader.ResolveDict(resources.Get("Font"));
            if (fontDict is null) return null;
            return new FontCollection(_streamDict, _reader);
        }
    }

    /// <summary>XForm (Form XObject) resources on this XForm.</summary>
    public XFormCollection Forms
    {
        get
        {
            var resources = _reader.ResolveDict(_streamDict.Get("Resources"));
            if (resources is null) return new XFormCollection(new Core.PdfDictionary(), _reader);
            var xobjects = _reader.ResolveDict(resources.Get("XObject"));
            if (xobjects is null) return new XFormCollection(new Core.PdfDictionary(), _reader);
            return new XFormCollection(xobjects, _reader);
        }
    }
}

/// <summary>
/// Collection of XForm (Form XObject) resources on a page.
/// Indexed by name (string key in the XObject resources dictionary).
/// </summary>
public sealed class XFormCollection : IEnumerable<XForm>
{
    private readonly Core.PdfDictionary _xobjects;
    private readonly IO.PdfReader _reader;
    private readonly Page? _ownerPage;
    private List<XForm>? _forms;

    internal XFormCollection(Core.PdfDictionary xobjects, IO.PdfReader reader)
        : this(xobjects, reader, null) { }

    internal XFormCollection(Core.PdfDictionary xobjects, IO.PdfReader reader, Page? ownerPage)
    {
        _xobjects = xobjects;
        _reader = reader;
        _ownerPage = ownerPage;
    }

    /// <summary>Number of Form XObjects.</summary>
    public int Count
    {
        get
        {
            EnsureForms();
            return _forms!.Count;
        }
    }

    /// <summary>Get XForm by 1-based index.</summary>
    public XForm this[int index]
    {
        get
        {
            EnsureForms();
            if (index < 1 || index > _forms!.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _forms[index - 1];
        }
    }

    /// <summary>Get XForm by name.</summary>
    public XForm? this[string name]
    {
        get
        {
            var obj = _reader.ResolveStream(_xobjects.Get(name));
            if (obj is null) return null;
            if (_reader.ResolveName(obj.Dict, "Subtype") != "Form") return null;
            return new XForm(obj, _reader) { Name = name };
        }
    }

    /// <summary>Remove an XForm by name from the collection and underlying XObject dict.</summary>
    public void Delete(string name)
    {
        _xobjects.Remove(name);
        if (_forms is not null)
            _forms.RemoveAll(f => f.Name == name);
        StripDoFromOwnerPage(name);
    }

    /// <summary>
    /// Remove every <c>/name Do</c> invocation of a just-deleted Form XObject from the owning
    /// page's content stream. Without this the page keeps drawing (or attempting to draw) a form
    /// whose resource entry is gone, and a reader enumerating <c>page.Contents</c> still finds the
    /// orphaned <c>Do</c> operator. The <c>Do</c> is the only operator removed; surrounding state
    /// (q/Q, cm, gs) is left intact since it may bracket other content.
    /// </summary>
    private void StripDoFromOwnerPage(string name)
    {
        if (_ownerPage is null || string.IsNullOrEmpty(name)) return;
        var bytes = LayerHelper.GetPageContentBytes(_ownerPage);
        if (bytes.Length == 0) return;
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        var pattern = $@"/{System.Text.RegularExpressions.Regex.Escape(name)}\s+Do\b";
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, pattern)) return;
        var newText = System.Text.RegularExpressions.Regex.Replace(text, pattern, string.Empty);
        _ownerPage.SetContentStream(System.Text.Encoding.Latin1.GetBytes(newText));
    }

    /// <summary>Remove an XForm by 1-based index. Resolves to the underlying name then defers to Delete(string).</summary>
    public void Delete(int index)
    {
        EnsureForms();
        if (index < 1 || index > _forms!.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var name = _forms[index - 1].Name;
        if (name is not null) Delete(name);
    }

    public IEnumerator<XForm> GetEnumerator()
    {
        EnsureForms();
        // Enumerate a snapshot so callers can Delete a form inside a foreach
        // over the collection (a common flatten/prune pattern) without a
        // "collection was modified" exception.
        return _forms!.ToList().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public void Add(XForm item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        EnsureForms();
        var name = item.Name ?? $"Fm{_forms!.Count + 1}";
        item.Name = name;
        _forms!.Add(item);
    }

    public void Clear()
    {
        EnsureForms();
        foreach (var f in _forms!.ToList())
            if (f.Name is { } n) _xobjects.Remove(n);
        _forms!.Clear();
    }

    public bool Contains(XForm item)
    {
        EnsureForms();
        return _forms!.Contains(item);
    }

    public void CopyTo(XForm[] array, int index)
    {
        EnsureForms();
        _forms!.CopyTo(array, index);
    }

    public bool Remove(XForm item)
    {
        EnsureForms();
        if (item?.Name is { } n) _xobjects.Remove(n);
        return _forms!.Remove(item!);
    }

    /// <summary>Drop all entries (equivalent to <see cref="Clear"/>).</summary>
    public void Delete() => Clear();

    /// <summary>Discard cached form list so the next access re-reads from the XObject dict.</summary>
    public void FreeMemory() => _forms = null;

    /// <summary>Return the PDF resource name (e.g. "Fm1") under which a Form XObject lives.</summary>
    public string GetFormName(XForm form) => form?.Name ?? string.Empty;

    private void EnsureForms()
    {
        if (_forms is not null) return;
        _forms = new List<XForm>();
        foreach (var key in _xobjects.Keys)
        {
            var stream = _reader.ResolveStream(_xobjects.Get(key));
            if (stream is null) continue;
            if (_reader.ResolveName(stream.Dict, "Subtype") != "Form") continue;
            _forms.Add(new XForm(stream, _reader) { Name = key });
        }
    }
}
