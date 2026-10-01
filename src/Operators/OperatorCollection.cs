using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
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

    /// <summary>Creates an empty operator list that is not attached to any page.</summary>
    public BaseOperatorCollection() { }

    /// <summary>Gets the number of operators in the list.</summary>
    public int Count => _ops.Count;

    public bool IsReadOnly => false;

    /// <summary>Whether the absorber is operating in fast-text-extraction mode (stored only).</summary>
    public bool IsFastTextExtractionMode { get; internal set; }

    // Operator access is 1-based: index 1 is the first operator. Matches the
    // public collection convention used across the form/content APIs.
    /// <summary>Gets or sets the operator at the given 1-based position (1 is the first operator).</summary>
    public Operator this[int index]
    {
        get => _ops[index - 1];
        set => _ops[index - 1] = value;
    }

    /// <summary>Appends an operator to the end of the list.</summary>
    public void Add(Operator op) => _ops.Add(op);
    /// <summary>Removes all operators from the list.</summary>
    public void Clear() => _ops.Clear();
    /// <summary>Returns <c>true</c> if the list contains the given operator.</summary>
    public bool Contains(Operator item) => _ops.Contains(item);
    /// <summary>Copies the operators into <c>array</c>, starting at the 0-based position <c>index</c> in that array.</summary>
    public void CopyTo(Operator[] array, int index) => _ops.CopyTo(array, index);
    /// <summary>Inserts an operator at the given 0-based position in the list.</summary>
    public void Insert(int index, Operator op) => _ops.Insert(index, op);
    /// <summary>Removes the first occurrence of the operator; returns <c>true</c> if it was found.</summary>
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
    // The page's content streams the operators were read from; streams added to the page after
    // that (text a TextBuilder writes) are not in the list and are kept when it is written back.
    private List<Core.PdfStream>? _parsedStreams;
    private List<Core.PdfStream>? _materializedStreams;
    // Streams added to the page while the operators were held here, each after the operator it
    // followed (null: before them all), so written back they keep their place among them.
    private readonly List<(Operator? After, Core.PdfStream Stream)> _addedStreams = [];
    // Where a stream-backed collection writes its operators back (an XForm's stream);
    // null for a page (which flushes through the page) or a read-only view.
    private readonly Action<byte[]>? _streamSink;
    // True once a mutator ran since the last materialisation or flush: only an edited
    // collection is written back, so a stream that was merely inspected keeps its bytes.
    private bool _edited;

    /// <summary>An operator already handed out was changed in place (a BDC given its marked-content
    /// id): the content is written afresh on save, as after an insertion.</summary>
    internal void MarkEdited() => _edited = true;

    internal OperatorCollection(Page page) => _page = page;

    /// <summary>Backed by an arbitrary content-bytes producer (e.g. a
    /// Form XObject's decoded /Contents stream). Used when the operators
    /// don't live on a <see cref="Page"/> — Field.NormalAppearance,
    /// XForm.Operators, etc.</summary>
    internal OperatorCollection(Func<byte[]> bytesProvider) => _bytesProvider = bytesProvider;

    /// <summary>Backed by a stream that also takes the operators back: <paramref name="streamSink"/>
    /// receives the serialised content on <see cref="FlushToStream"/>.</summary>
    internal OperatorCollection(Func<byte[]> bytesProvider, Action<byte[]> streamSink)
        : this(bytesProvider) => _streamSink = streamSink;

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
    public void Add(Operator op)
    {
        Materialize();
        ResolveAgainstFont(op);
        _operators.Add(op);
        if (_suppressed) _suspendBatched = true;
        Reindex();
    }

    /// <summary>
    /// Encode a run that was given a font against that font's glyph ids.
    ///
    /// A run shown in an embedded font cannot be written as a literal string:
    /// the codes in the content stream are the font's own glyph ids. Resolving
    /// here is what makes the page's resources and its content agree, and it
    /// also grows the face's widths and its character map to cover the run, so
    /// the text remains extractable.
    /// </summary>
    private void ResolveAgainstFont(Operator op)
    {
        if (_page is null) return;

        switch (op)
        {
            case Aspose.Pdf.Operators.ShowText show when Usable(show.Font, show.Text):
                show.UseGlyphIds(Encode(show.Font!, show.Text, show.Features));
                break;

            case Aspose.Pdf.Operators.SetGlyphsPositionShowText positioned when Usable(positioned.Font, "x"):
                positioned.UseGlyphIds(EncodeEach(positioned));
                break;
        }
    }

    private static bool Usable(Aspose.Pdf.Text.FontInfo? font, string text) =>
        font is not null && text.Length > 0 && font.ProgramBytes() is not null;

    /// <summary>Each string element of a positioned run, encoded; adjustments left alone.</summary>
    private byte[]?[] EncodeEach(Aspose.Pdf.Operators.SetGlyphsPositionShowText positioned)
    {
        var resolved = new byte[]?[positioned.Items.Length];
        for (var index = 0; index < positioned.Items.Length; index++)
            if (positioned.Items[index] is string run && run.Length > 0)
                resolved[index] = Encode(positioned.Font!, run, positioned.Features);
        return resolved;
    }

    private byte[] Encode(Aspose.Pdf.Text.FontInfo font, string text,
                          System.Collections.Generic.IReadOnlyList<string>? features = null)
    {
        var fontDict = Aspose.Pdf.Text.FontCollection.GetOrCreateFontResources(_page!.Dict, _page.Reader);
        var (_, glyphIds) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
            fontDict, font.ProgramBytes()!, font.EmbeddedBaseFontName ?? font.FontName ?? "Font",
            text, resNameHint: font.PageResourceName, features: features);
        return glyphIds;
    }

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
        _edited = true;
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
        _edited = true;
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
        RemoveEach(ops);
    }

    /// <summary>Remove every operator in <paramref name="list"/>.</summary>
    public void Delete(System.Collections.Generic.IList<Operator> list)
    {
        if (list is null) return;
        RemoveEach(list);
    }

    /// <summary>Remove the given operators from the materialised list. An operator a caller
    /// collected from a plain enumeration is a throw-away parse, not the materialised
    /// instance, so one that is not found by reference is matched by its 1-based index and
    /// its text (the way a caller identifies it: "delete every Do I saw").</summary>
    private void RemoveEach(System.Collections.Generic.IEnumerable<Operator> ops)
    {
        Materialize();
        var byIndex = new System.Collections.Generic.List<Operator>();
        foreach (var op in ops)
            if (op is not null && !_operators.Remove(op)) byIndex.Add(op);
        byIndex.Sort((a, b) => b.Index.CompareTo(a.Index));
        foreach (var op in byIndex)
        {
            var at = op.Index - 1;
            if (at >= 0 && at < _operators.Count && _operators[at].ToString() == op.ToString())
                _operators.RemoveAt(at);
        }
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
        _edited = true;
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
    /// Marks the list as representing the full content, so <c>FlushToPage</c>
    /// replaces (rather than appends to) the page stream on save. A no-op once the
    /// list already holds operators — whether materialised here or added directly.</summary>
    private void Materialize()
    {
        if (_materialized || _operators.Count > 0) return;
        EnsureParsed();
        foreach (var s in _parsed!)
            _operators.Add(TypedOperatorParser.Parse(s));
        _materialized = true;
        _materializedStreams = _parsedStreams;
        _addedStreams.Clear();
        Reindex();
        _edited = false;
    }

    /// <summary>Materialize the parsed content into live operator instances so
    /// enumeration hands out stable objects whose property mutations persist on
    /// the next <c>FlushToPage</c> (a non-materialised enumeration yields
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
    /// the removal stable so a subsequent <c>FlushToPage</c> persists it.</summary>
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
        // A throw-away parse still reports the position it came from: a caller
        // that collects operators from a plain enumeration names them back to the
        // collection by that index (Delete by list, Insert at an operator's Index).
        for (int i = 0; i < _parsed!.Count; i++)
        {
            var op = TypedOperatorParser.Parse(_parsed[i]);
            op.Index = i + 1;
            yield return op;
        }
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
        _parsedStreams = _page.ContentStreamObjects();
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
    /// <summary>Note a content stream the page appended while these operators are held here: it
    /// comes after the last of them.</summary>
    internal void NoteStreamAdded(Core.PdfStream stream)
    {
        if (_materialized) _addedStreams.Add((_operators.Count > 0 ? _operators[^1] : null, stream));
    }

    /// <summary>Note that the page's content was replaced whole (not added to): the operators held
    /// here now stand for the new content, which they replace when written back - no stream of it
    /// is kept beside them.</summary>
    internal void NoteContentReplaced()
    {
        if (!_materialized || _page is null) return;
        _materializedStreams = _page.ContentStreamObjects();
        _addedStreams.Clear();
    }

    internal void FlushToPage() => FlushToPage(fromRender: false);

    /// <summary>The operators as content-stream bytes. Every operand byte survives: the parser
    /// read the stream as Latin-1 and the writer gives it back the same way (an ASCII pass
    /// turned each code above 127 in a shown string into a question mark).</summary>
    private static byte[] Serialize(IEnumerable<Operator> operators)
    {
        var sb = new StringBuilder();
        foreach (var op in operators)
        {
            sb.Append(op.ToPdf());
            sb.Append('\n');
        }
        return Compat.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>The page's new content, in order: runs of these operators, and between them the
    /// streams the page added while they were held here, each after the operator it followed (a
    /// stream whose operator is gone comes at the end).</summary>
    private List<(byte[]? Bytes, Core.PdfStream? Stream)> Parts()
    {
        var parts = new List<(byte[]? Bytes, Core.PdfStream? Stream)>();
        var at = new Dictionary<int, List<Core.PdfStream>>();
        var orphans = new List<Core.PdfStream>();
        foreach (var (after, stream) in _addedStreams)
        {
            var index = after is null ? 0 : _operators.FindIndex(o => ReferenceEquals(o, after)) + 1;
            if (after is not null && index == 0) { orphans.Add(stream); continue; }
            if (!at.TryGetValue(index, out var list)) at[index] = list = [];
            list.Add(stream);
        }
        var start = 0;
        foreach (var index in at.Keys.OrderBy(i => i))
        {
            if (index > start) parts.Add((Serialize(_operators.GetRange(start, index - start)), null));
            foreach (var stream in at[index]) parts.Add((null, stream));
            start = index;
        }
        if (start < _operators.Count || parts.Count == 0) parts.Add((Serialize(_operators.GetRange(start, _operators.Count - start)), null));
        foreach (var stream in orphans) parts.Add((null, stream));
        return parts;
    }

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
        if (_materialized)
            _page.SetContentStream(Parts(), keepAddedSince: _materializedStreams);
        else
            _page.AppendContentBytes(Serialize(_operators));
        _addedStreams.Clear();
        _operators.Clear();
        _parsed = null;
        _materialized = false;
        _suspendBatched = false;
    }

    /// <summary>Write the operators back into the backing stream through the sink the
    /// owner supplied. Every mutator materialises the list first, so an edited list is the
    /// stream's whole content and replaces it. A collection that was only read, or has no
    /// sink, is left alone.</summary>
    internal void FlushToStream()
    {
        if (_streamSink is null || !_edited || !_materialized) return;
        var sb = new StringBuilder();
        foreach (var op in _operators)
        {
            sb.Append(op.ToPdf());
            sb.Append('\n');
        }
        _streamSink(Compat.Latin1.GetBytes(sb.ToString()));
        _operators.Clear();
        _parsed = null;
        _materialized = false;
        _edited = false;
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
