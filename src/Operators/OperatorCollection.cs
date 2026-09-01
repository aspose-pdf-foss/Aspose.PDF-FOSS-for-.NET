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
/// Standalone operator-list class — surface mirrors <see cref="OperatorCollection"/>
/// but is detached from any page. Used by callers that want to construct operators
/// outside the page-binding flow (e.g. TextBuilder before-page-binding).
/// </summary>
public class BaseOperatorCollection : System.Collections.Generic.IEnumerable<Operator>
{
    private readonly List<Operator> _ops = new();

    public int Count => _ops.Count;

    public bool IsReadOnly => false;

    /// <summary>Whether the absorber is operating in fast-text-extraction mode (stored only).</summary>
    public bool IsFastTextExtractionMode { get; internal set; }

    // Operator access is 1-based: index 1 is the first operator. Matches the
    // public collection convention used across the form/content APIs.
    public Operator this[int index]
    {
        get => _ops[index - 1];
        set => _ops[index - 1] = value;
    }

    public void Add(Operator op) => _ops.Add(op);
    public void Clear() => _ops.Clear();
    public bool Contains(Operator item) => _ops.Contains(item);
    public void CopyTo(Operator[] array, int index) => _ops.CopyTo(array, index);
    public void Insert(int index, Operator op) => _ops.Insert(index, op);
    public bool Remove(Operator item) => _ops.Remove(item);

    /// <summary>Suspend any deferred-update bookkeeping. No-op.</summary>
    public void SuppressUpdate() { }
    /// <summary>Resume deferred-update bookkeeping. No-op.</summary>
    public void ResumeUpdate() { }
    /// <summary>Cancel any pending deferred update. No-op.</summary>
    public void CancelUpdate() { }

    public System.Collections.Generic.IEnumerator<Operator> GetEnumerator() => _ops.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Collection of content stream operators for a page.
/// Operators added here are appended to the page's content stream on save.
/// </summary>
public sealed class OperatorCollection : IEnumerable<Operator>, IDisposable
{
    private readonly Page? _page;
    private readonly Func<byte[]>? _bytesProvider;
    private readonly List<Operator> _operators = [];
    private List<string>? _parsed;
    private bool _suppressed;
    // True once any operator was added inside a SuppressUpdate/ResumeUpdate batch.
    // Such a batch is deferred until SAVE: the live render does not
    // show it (a batch-added overlay is absent from the live render), while
    // ops added OUTSIDE a batch are live-visible (an image Do renders unsaved).
    private bool _suspendBatched;
    private bool _materialized;

    internal OperatorCollection(Page page) => _page = page;

    /// <summary>Backed by an arbitrary content-bytes producer (e.g. a
    /// Form XObject's decoded /Contents stream). Used when the operators
    /// don't live on a <see cref="Page"/> — Field.NormalAppearance,
    /// XForm.Operators, etc.</summary>
    internal OperatorCollection(Func<byte[]> bytesProvider) => _bytesProvider = bytesProvider;

    /// <summary>Public-API alias that returns this collection itself
    /// (callers do <c>page.Contents.Commands[i]</c>).</summary>
    public OperatorCollection Commands => this;

    /// <summary>Read-only glance at the current operators WITHOUT materialising
    /// or caching: a peek must never freeze the collection's state, because the
    /// backing stream may still be written later (generator pages before save).</summary>
    internal IEnumerable<string> PeekOps()
    {
        if (_operators.Count > 0)
        {
            foreach (var op in _operators) yield return op.ToPdf();
            yield break;
        }
        if (_parsed is not null)
        {
            foreach (var s in _parsed) yield return s;
            yield break;
        }
        var bytes = GetContentBytes();
        if (bytes.Length == 0) yield break;
        foreach (var s in ContentStreamOperatorParser.ParseOperators(bytes))
            yield return s;
    }

    /// <summary>Add an operator to the collection.</summary>
    public void Add(Operator op) { Materialize(); _operators.Add(op); if (_suppressed) _suspendBatched = true; Reindex(); }

    /// <summary>Add several operators in one call.</summary>
    public void Add(Operator[] ops)
    {
        if (ops is null) return;
        Materialize();
        foreach (var op in ops) _operators.Add(op);
        Reindex();
    }

    /// <summary>Add several operators from any collection in one call.</summary>
    public void Add(System.Collections.Generic.ICollection<Operator> ops)
    {
        if (ops is null) return;
        Materialize();
        foreach (var op in ops) _operators.Add(op);
        Reindex();
    }

    /// <summary>Re-stamp every live operator's 1-based <see cref="Operator.Index"/>.
    /// Called after materialisation and after every mutation so the property always
    /// reflects the operator's current position. An operator removed from the
    /// collection keeps the index it had at removal time — callers use it to
    /// re-insert a replacement at the same position.</summary>
    private void Reindex()
    {
        for (int i = 0; i < _operators.Count; i++)
            _operators[i].Index = i + 1;
    }

    /// <summary>Visit every operator with the given selector. Materialises the
    /// collection first so the operators handed to the visitor are the same
    /// stable instances held by this collection — a selector that collects them
    /// (e.g. <see cref="OperatorSelector.Selected"/>) can then be passed back to
    /// <see cref="Delete(System.Collections.Generic.IList{Operator})"/> and the
    /// reference-equality removal will find them. Each operator dispatches to the
    /// matching typed <c>Visit</c> overload via its own <see cref="Operator.Accept"/>.</summary>
    public void Accept(IOperatorSelector visitor)
    {
        if (visitor is null) return;
        Materialize();
        foreach (var op in _operators)
            op.Accept(visitor);
    }

    /// <summary>Cancel a suppressed-update window (started by
    /// <see cref="SuppressUpdate"/>) without flushing pending changes.
    /// No-op in this build because mutations are already deferred to save.</summary>
    public void CancelUpdate() { _suppressed = false; }

    /// <summary>Remove every operator from the collection.</summary>
    public void Clear()
    {
        _operators.Clear();
        _parsed?.Clear();
    }

    /// <summary>True when <paramref name="op"/> is currently in the collection.</summary>
    public bool Contains(Operator op)
        => op is not null && _operators.Contains(op);

    /// <summary>Copy the live operators (the in-memory mutable list, not the
    /// parsed cache) into <paramref name="array"/> starting at <paramref name="index"/>.</summary>
    public void CopyTo(Operator[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        _operators.CopyTo(array, index);
    }

    /// <summary>Remove every occurrence of each operator in <paramref name="ops"/>.</summary>
    public void Delete(Operator[] ops)
    {
        if (ops is null) return;
        Materialize();
        foreach (var op in ops) _operators.Remove(op);
        Reindex();
    }

    /// <summary>Remove every operator in <paramref name="list"/>.</summary>
    public void Delete(System.Collections.Generic.IList<Operator> list)
    {
        if (list is null) return;
        Materialize();
        foreach (var op in list) _operators.Remove(op);
        Reindex();
    }

    /// <summary>Releases resources held by the collection. Currently a no-op —
    /// operators are pure value objects in this build.</summary>
    public void Dispose() { _operators.Clear(); _parsed?.Clear(); _ = _suppressed; }

    /// <summary>Insert one operator at the given 1-based index.</summary>
    public void Insert(int index, Operator op)
    {
        if (op is null) return;
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        Materialize();
        _operators.Insert(Math.Min(index - 1, _operators.Count), op);
        Reindex();
    }

    /// <summary>Insert several operators at <paramref name="at"/> (1-based).</summary>
    public void Insert(int at, Operator[] ops)
    {
        if (ops is null) return;
        if (at < 1) throw new ArgumentOutOfRangeException(nameof(at));
        Materialize();
        _operators.InsertRange(Math.Min(at - 1, _operators.Count), ops);
        Reindex();
    }

    /// <summary>Insert several operators (any IList) at <paramref name="at"/> (1-based).</summary>
    public void Insert(int at, System.Collections.Generic.IList<Operator> ops)
    {
        if (ops is null) return;
        if (at < 1) throw new ArgumentOutOfRangeException(nameof(at));
        Materialize();
        _operators.InsertRange(Math.Min(at - 1, _operators.Count), ops);
        Reindex();
    }

    /// <summary>Whether the absorber/parser is in fast-text-extraction mode
    /// (no glyph-width metrics, character-position approximations only).
    /// Always false in this build — we always parse precisely.</summary>
    public bool IsFastTextExtractionMode => false;

    /// <summary>Always false: callers may add and remove operators.</summary>
    public bool IsReadOnly => false;

    /// <summary>Remove the first occurrence of <paramref name="op"/>; returns
    /// true when an operator was removed.</summary>
    public bool Remove(Operator op)
    {
        if (op is null || !_operators.Remove(op)) return false;
        Reindex();
        return true;
    }

    /// <summary>Replace operators in place: each operator in
    /// <paramref name="operators"/> overwrites the existing operator at its
    /// 1-based <see cref="Operator.Index"/>. Operators whose index falls
    /// outside the current range are ignored.</summary>
    public void Replace(System.Collections.Generic.IList<Operator> operators)
    {
        if (operators is null) return;
        Materialize();
        foreach (var op in operators)
        {
            if (op is null) continue;
            if (op.Index >= 1 && op.Index <= _operators.Count)
                _operators[op.Index - 1] = op;
        }
    }

    /// <summary>
    /// Suspend automatic content-stream re-serialization while a batch of
    /// operator mutations is performed. Paired with <see cref="ResumeUpdate()"/>.
    /// The implementation works in-memory and re-serializes lazily on save, so
    /// this is a no-op kept for public API compatibility.
    /// </summary>
    public void SuppressUpdate() { _suppressed = true; }

    /// <summary>Resume automatic content-stream re-serialization. See <see cref="SuppressUpdate"/>.</summary>
    public void ResumeUpdate() { _suppressed = false; }

    /// <summary>Resume automatic re-serialization with optional full-flush
    /// semantics (the <paramref name="updateAll"/> flag is stored only).</summary>
    public void ResumeUpdate(bool updateAll) { _suppressed = false; _ = updateAll; }

    /// <summary>Persist pending operator mutations to the backing content stream.
    /// In this build the collection re-serializes its operators lazily on save, so
    /// this is a no-op kept for public API compatibility (mirrors <see cref="SuppressUpdate"/>
    /// / <see cref="ResumeUpdate()"/> / <see cref="CancelUpdate"/>).</summary>
    public void UpdateData() { }

    /// <summary>
    /// Number of operators in the page content stream.
    /// Parses the content stream on first access.
    /// </summary>
    public int Count
    {
        get
        {
            if (_operators.Count > 0) return _operators.Count;
            EnsureParsed();
            return _parsed!.Count;
        }
    }

    /// <summary>Access (or replace) operator at 1-based index. Returns a
    /// typed <see cref="Operator"/> subclass (BT, ET, GSave, GRestore,
    /// SelectFont, SetRGBColor, MoveTo, LineTo, …) when the operator name
    /// is recognised; otherwise a <see cref="RawOperator"/> wrapping the
    /// original token. The setter overwrites the in-memory operator at the
    /// given position.</summary>
    public Operator this[int index]
    {
        get
        {
            Materialize();
            if (index < 1 || index > _operators.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _operators[index - 1];
        }
        set
        {
            Materialize();
            if (index < 1 || index > _operators.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _operators[index - 1] = value;
        }
    }

    /// <summary>Promote the parsed content into the live <see cref="_operators"/>
    /// list so callers receive stable operator instances whose mutations persist.
    /// Marks the list as representing the full content, so <see cref="FlushToPage"/>
    /// replaces (rather than appends to) the page stream on save. A no-op once the
    /// list already holds operators — whether materialised here or added directly.</summary>
    private void Materialize()
    {
        if (_materialized || _operators.Count > 0) return;
        EnsureParsed();
        foreach (var s in _parsed!)
            _operators.Add(TypedOperatorParser.Parse(s));
        _materialized = true;
        Reindex();
    }

    /// <summary>Materialize the parsed content into live operator instances so
    /// enumeration hands out stable objects whose property mutations persist on
    /// the next <see cref="FlushToPage"/> (a non-materialised enumeration yields
    /// throw-away parses).</summary>
    internal void EnsureMaterialized() => Materialize();

    /// <summary>Remove operator at the given 1-based index.</summary>
    public void Delete(int index)
    {
        Materialize();
        if (index < 1 || index > _operators.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _operators.RemoveAt(index - 1);
        Reindex();
    }

    /// <summary>Materialize the content and remove every operator matching the predicate.
    /// Operating on the materialized list (rather than enumerator-yielded instances) keeps
    /// the removal stable so a subsequent <see cref="FlushToPage"/> persists it.</summary>
    internal int RemoveWhere(Predicate<Operator> match)
    {
        Materialize();
        var removed = _operators.RemoveAll(match);
        if (removed > 0) Reindex();
        return removed;
    }

    /// <summary>Enumerate all operators in the content stream as typed
    /// <see cref="Operator"/> instances (with <see cref="RawOperator"/>
    /// fallback for unrecognised commands).</summary>
    public IEnumerator<Operator> GetEnumerator()
    {
        if (_operators.Count > 0)
        {
            foreach (var op in _operators) yield return op;
            yield break;
        }
        EnsureParsed();
        for (int i = 0; i < _parsed!.Count; i++)
            yield return TypedOperatorParser.Parse(_parsed[i]);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Snapshot of the operators as a list. Materialises the collection so
    /// callers receive the same stable instances the collection holds.</summary>
    internal List<Operator> ToList()
    {
        Materialize();
        return new List<Operator>(_operators);
    }

    /// <summary>Returns all operators as a single string. Operators are joined
    /// with "\r\n " (CRLF + space) — the exact layout
    /// tests match with multi-operator Contains() literals.</summary>
    public override string ToString()
    {
        EnsureParsed();
        return string.Join("\r\n ", _parsed!);
    }

    private void EnsureParsed()
    {
        if (_parsed is not null) return;
        _parsed = [];
        var bytes = GetContentBytes();
        if (bytes.Length == 0) return;
        _parsed = ContentStreamOperatorParser.ParseOperators(bytes);
    }

    private byte[] GetContentBytes()
    {
        if (_bytesProvider is not null) return _bytesProvider() ?? [];
        if (_page is null) return [];
        var contentsObj = _page.Reader.Resolve(_page.Dict.Get("Contents"));
        if (contentsObj is Core.PdfStream stream)
            return _page.Reader.DecodeStream(stream);
        if (contentsObj is Core.PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = _page.Reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = _page.Reader.DecodeStream(s);
                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    /// <summary>Serialize all operators and append to the page's content stream.
    /// No-op for non-page-backed instances (Field.NormalAppearance etc.).</summary>
    internal void FlushToPage() => FlushToPage(fromRender: false);

    internal void FlushToPage(bool fromRender)
    {
        if (_page is null) return;
        // A render-time flush must not materialise a suspended batch - it is
        // deferred until save (see _suspendBatched).
        if (fromRender && _suspendBatched) return;
        // Materialised operators are the page's complete content (the caller read
        // or edited existing operators), so the stream is replaced. Non-materialised
        // operators were added on top of existing content, so they are appended.
        if (!_materialized && _operators.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var op in _operators)
        {
            sb.Append(op.ToPdf());
            sb.Append('\n');
        }
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        if (_materialized)
            _page.SetContentStream(bytes);
        else
            _page.AppendContentBytes(bytes);
        _operators.Clear();
        _parsed = null;
        _materialized = false;
        _suspendBatched = false;
    }

    /// <summary>Invalidate cached parse results (after content stream modification).</summary>
    internal void InvalidateCache() => _parsed = null;
}

/// <summary>
/// Buffers operators to prepend to / append to a page's content stream. Operators added
/// via <see cref="AppendToBegin"/> are inserted (in call order) before the existing
/// content; those added via <see cref="AppendToEnd"/> are appended after it. Nothing is
/// applied until <see cref="UpdateData"/> is called. Typical use is wrapping a page's
/// content in a q…Q graphics-state save/restore pair before drawing extra overlay content.
/// </summary>
public sealed class ContentsAppender
{
    private readonly Page _page;
    private readonly System.Collections.Generic.List<Aspose.Pdf.Operator> _begin = new();
    private readonly System.Collections.Generic.List<Aspose.Pdf.Operator> _end = new();

    internal ContentsAppender(Page page) => _page = page;

    /// <summary>Queue an operator to be inserted before the existing page content.</summary>
    public void AppendToBegin(Aspose.Pdf.Operator op)
    {
        if (op is not null) _begin.Add(op);
    }

    /// <summary>Queue an operator to be appended after the existing page content.</summary>
    public void AppendToEnd(Aspose.Pdf.Operator op)
    {
        if (op is not null) _end.Add(op);
    }

    /// <summary>Apply the queued begin/end operators to the page's content stream.</summary>
    public void UpdateData()
    {
        var contents = _page.Contents;
        if (_begin.Count > 0)
            contents.Insert(1, _begin); // 1-based insert at the front, preserving call order
        foreach (var op in _end)
            contents.Add(op);
        _begin.Clear();
        _end.Clear();
    }
}

/// <summary>An unparsed operator token from a content stream — used as a fallback
/// for operators not covered by the typed <see cref="Aspose.Pdf.Operators"/>
/// hierarchy. Inherits <see cref="Aspose.Pdf.Operators.Operator"/> so that
/// <see cref="OperatorCollection"/> can yield a uniform typed sequence.</summary>
public sealed class RawOperator : Aspose.Pdf.Operators.Operator
{
    private readonly string _text;

    internal RawOperator(string text) => _text = text;

    /// <summary>The operator command name (last token).</summary>
    public override string CommandName
    {
        get
        {
            var trimmed = _text.TrimEnd();
            var lastSpace = trimmed.LastIndexOf(' ');
            return lastSpace >= 0 ? trimmed[(lastSpace + 1)..] : trimmed;
        }
    }

    /// <inheritdoc />
    public override string ToPdf() => _text;
}
