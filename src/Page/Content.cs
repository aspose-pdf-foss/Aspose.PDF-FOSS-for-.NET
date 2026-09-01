using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>
    /// Content stream operator collection. Add operators to build page content.
    /// Operators are serialized to the content stream on save.
    /// </summary>
    public OperatorCollection Contents => _contents ??= new OperatorCollection(this);

    /// <summary>Helper that buffers operators to prepend to / append to this page's
    /// content stream, applied on <see cref="ContentsAppender.UpdateData"/>. Commonly
    /// used to wrap existing content in a q…Q save/restore pair.</summary>
    public ContentsAppender ContentsAppender => _contentsAppender ??= new ContentsAppender(this);

    /// <summary>
    /// Append a content stream to this page.
    /// If the page has a single content stream, it is converted to an array.
    /// If the page already has a content array, the new stream is appended.
    /// </summary>
    public void AddContentStream(byte[] contentBytes)
    {
        AddContent(contentBytes);
        _trailingMarkedContent = null;
    }

    /// <summary>Wrap the page's existing content in <c>q … Q</c> so a stream
    /// appended afterwards starts from the identity CTM. A printout's content
    /// often opens with a TOP-LEVEL flip matrix (<c>0.24 0 0 -0.24 0 612 cm</c>)
    /// outside any q/Q; without the sandwich an appended watermark inherits it
    /// and draws mirrored at a quarter scale. A clean state is guaranteed by
    /// rewriting the page around its artifact. Idempotent per page.</summary>
    internal void WrapExistingContentInGraphicsState()
    {
        if (_contentStateWrapped) return;
        if (ContentStreamCount == 0) { _contentStateWrapped = true; return; }
        InsertContentStreamAt(0, System.Text.Encoding.ASCII.GetBytes("q\n"));
        AddContent(System.Text.Encoding.ASCII.GetBytes("Q\n"));
        _trailingMarkedContent = null;
        _contentStateWrapped = true;
    }

    /// <summary>Append content wrapped in a <c>/tag &lt;&lt;/MCID mcid&gt;&gt; BDC … EMC</c>
    /// marked-content block. A run appended directly after another with the SAME
    /// tag and MCID continues that block instead of opening a second one (the
    /// previous segment's closing EMC moves to the end of the new segment).</summary>
    internal void AddMarkedContentStream(byte[] contentBytes, string tag, int mcid)
    {
        var close = System.Text.Encoding.ASCII.GetBytes("EMC\n");
        byte[] payload;
        if (_trailingMarkedContent is { } prev && prev.Tag == tag && prev.Mcid == mcid
            && ReferenceEquals(LastContentStreamSegment(), prev.Segment)
            && EndsWith(prev.Segment.RawData, close))
        {
            prev.Segment.ReplaceData(prev.Segment.RawData[..^close.Length]);
            payload = Concat(contentBytes, close);
        }
        else
        {
            var open = System.Text.Encoding.ASCII.GetBytes($"/{tag} <</MCID {mcid}>> BDC\n");
            payload = Concat(Concat(open, contentBytes), close);
        }
        AddContent(payload);
        _trailingMarkedContent = LastContentStreamSegment() is { } seg ? (tag, mcid, seg) : null;

        static bool EndsWith(byte[] data, byte[] suffix)
        {
            if (data.Length < suffix.Length) return false;
            for (var i = 0; i < suffix.Length; i++)
                if (data[data.Length - suffix.Length + i] != suffix[i]) return false;
            return true;
        }
        static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            System.Buffer.BlockCopy(a, 0, r, 0, a.Length);
            System.Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }

    /// <summary>The last stream segment of /Contents (the one the most recent
    /// append landed in), or null when the page has no direct stream content.</summary>
    internal Core.PdfStream? LastContentStreamSegment()
    {
        var resolved = _reader.Resolve(_dict.Get("Contents"));
        if (resolved is Core.PdfArray arr)
            return arr.Count > 0 ? _reader.Resolve(arr[arr.Count - 1]) as Core.PdfStream : null;
        return resolved as Core.PdfStream;
    }

    /// <summary>
    /// Prepend content stream bytes before existing page content (for background elements).
    /// </summary>
    public void PrependContentStream(byte[] contentBytes)
    {
        var newStream = new PdfStream(new PdfDictionary(), contentBytes);
        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);

        if (resolved is PdfArray arr)
        {
            arr.Insert(0, newStream);
        }
        else if (resolved is PdfStream existingStream)
        {
            var newArr = new PdfArray();
            newArr.Add(newStream);
            newArr.Add(existingStream);
            _dict.Set("Contents", newArr);
        }
        else
        {
            _dict.Set("Contents", newStream);
        }
    }

    /// <summary>
    /// Append content stream bytes to this page.
    /// If the page has a single content stream, creates an array with both.
    /// If the page has a content array, appends the new stream.
    /// </summary>
    public void AddContent(byte[] contentStreamBytes)
    {
        var doc = _reader.OwnerDocument;
        // Register the new content as an indirect object (not inline in /Contents): a full
        // save promotes inline streams, but an incremental (append-only) save writes only
        // objects registered as new/dirty, so the stream needs its own number to survive
        // Save() on a document opened from a writable stream. registerOverlay exposes it to
        // in-memory _reader.Resolve so reading the page's operators before save still works.
        var newStream = new PdfStream(new PdfDictionary(), contentStreamBytes);
        PdfObject entry = newStream;
        // Only take the indirect path for a document that will be saved incrementally
        // (opened from a writable stream); a full save to a fresh output promotes the inline
        // stream and keeps the compact layout that structural comparisons expect.
        var indirect = doc is not null && doc.HasWritableSourceStream;
        if (indirect)
        {
            var num = doc!.AllocateObjectNumber();
            doc.AddNewObject(num, newStream, registerOverlay: true);
            entry = new PdfIndirectRef(num, 0);
        }

        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);

        if (resolved is PdfArray existingArray)
        {
            existingArray.Add(entry);
            if (indirect && existing is PdfIndirectRef aref)
                doc!.MarkDirty(aref.ObjectNumber, existingArray);
        }
        else if (resolved is PdfStream)
        {
            // Single stream — create an array with both
            var arr = new PdfArray();
            arr.Add(existing!); // keep original ref (may be indirect)
            arr.Add(entry);
            _dict.Set("Contents", arr);
        }
        else
        {
            // No existing content — just set the new stream
            _dict.Set("Contents", entry);
        }

        if (indirect) MarkDirty();
    }

    /// <summary>Insert a content stream at <paramref name="index"/> in /Contents —
    /// an UNDERLAY that paints beneath every stream appended after that point
    /// (a table wrapper's background band whose height is only known once its
    /// children have laid out).</summary>
    internal void InsertContentStreamAt(int index, byte[] contentStreamBytes)
    {
        var newStream = new PdfStream(new PdfDictionary(), contentStreamBytes);
        var existing = _dict.Get("Contents");
        var resolved = _reader.Resolve(existing);
        if (resolved is PdfArray existingArray)
        {
            existingArray.Insert(Math.Clamp(index, 0, existingArray.Count), newStream);
        }
        else if (resolved is PdfStream)
        {
            var arr = new PdfArray();
            if (index <= 0) { arr.Add(newStream); arr.Add(existing!); }
            else { arr.Add(existing!); arr.Add(newStream); }
            _dict.Set("Contents", arr);
        }
        else
        {
            _dict.Set("Contents", newStream);
        }
        _trailingMarkedContent = null;
    }

    /// <summary>
    /// Replace the page content stream with new bytes.
    /// </summary>
    /// <summary>
    /// Returns the decoded content stream bytes for this page.
    /// If the page has multiple content streams (Contents is an array), they are concatenated.
    /// </summary>
    internal byte[]? GetContentStreamBytes()
    {
        if (_reader is null) return null;
        var contents = _reader.Resolve(_dict.Get("Contents"));
        if (contents is PdfStream stream)
            return _reader.DecodeStream(stream);
        if (contents is Core.PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = _reader.ResolveStream(item);
                if (s is null) continue;
                if (ms.Length > 0) ms.WriteByte((byte)'\n');
                var data = _reader.DecodeStream(s);
                ms.Write(data);
            }
            return ms.ToArray();
        }
        // Direct PdfStream on the dict (not via reader)
        if (_dict.Get("Contents") is PdfStream directStream)
            return directStream.RawData;
        return null;
    }

    internal void SetContentStream(byte[] contentBytes)
    {
        _dict.Set("Contents", new PdfStream(new PdfDictionary(), contentBytes));
        _contents?.InvalidateCache();
    }

    /// <summary>Drop the cached typed-operator view of the page content so the
    /// next <see cref="Contents"/> access re-materialises from the current raw
    /// /Contents. Needed after low-level raw edits (SetContentStream /
    /// AddContentStream) when a caller has already materialised the
    /// OperatorCollection: <see cref="SetContentStream"/> only clears the parsed
    /// string cache, so a previously materialised typed-operator list would
    /// otherwise survive stale and win on save.</summary>
    internal void ResetContentsCache() => _contents = null;

    /// <summary>Persist operators added or edited through <see cref="Contents"/> into
    /// the page's real /Contents stream. Renderers call this before reading the
    /// stream so a LIVE document renders exactly what its edited DOM holds -
    /// unsaved edits render (an image added to resources plus its Do
    /// drawn via Contents.Add shows up without a save/reload round-trip).</summary>
    internal void FlushPendingContents() => _contents?.FlushToPage(fromRender: true);

    internal void AppendContentBytes(byte[] newBytes)
    {
        // /Contents may be a single stream or an array of streams (PDF 32000-2
        // § 7.7.3.3). GetContentStreamBytes handles both; the previous inline
        // branch silently lost array-content callers' original page data.
        var existingData = GetContentStreamBytes() ?? [];

        var combined = new byte[existingData.Length + 1 + newBytes.Length];
        existingData.CopyTo(combined, 0);
        if (existingData.Length > 0) combined[existingData.Length] = (byte)'\n';
        newBytes.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));
        _dict.Set("Contents", new PdfStream(new PdfDictionary(), combined));
    }

    /// <summary>Appends raw content bytes to the existing page content stream.</summary>
    private void AppendToContentStream(byte[] contentToAppend)
    {
        var existing = _reader.Resolve(_dict.Get("Contents"));
        byte[] existingData = existing is PdfStream es ? _reader.DecodeStream(es) : [];

        var combined = new byte[existingData.Length + 1 + contentToAppend.Length];
        existingData.CopyTo(combined, 0);
        if (existingData.Length > 0)
            combined[existingData.Length] = (byte)'\n';
        contentToAppend.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));

        SetContentStream(combined);
    }

    /// <summary>
    /// Apply a CTM transform to the page content by wrapping the existing content stream
    /// in <c>q {sx} 0 0 {sy} {tx} {ty} cm … Q</c>.
    /// Annotation Rect arrays are also scaled/translated by the same matrix.
    /// </summary>
    internal void ApplyContentResize(double sx, double sy, double tx, double ty)
    {
        var originalContent = CollectContentBytes();

        // Emit the resize matrix as the FIRST operator, then isolate the original
        // content in q…Q: {sx} 0 0 {sy} {tx} {ty} cm  q  … original content …  Q
        // (the cm comes first, so it is page.Contents.Commands[1]).
        var prefix = System.Text.Encoding.ASCII.GetBytes(
            $"{Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\nq\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\nQ\n");

        var wrapped = new byte[prefix.Length + originalContent.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        originalContent.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + originalContent.Length);

        SetContentStream(wrapped);

        // Transform annotation Rect arrays
        TransformAnnotationRects(sx, sy, tx, ty);
    }

    /// <summary>Like <see cref="ApplyContentResize"/>, but moves the original page
    /// content into a Form XObject and leaves only <c>q … cm /Fm Do Q</c> on the page.
    /// Keeps the page operator stream free of the content's own transforms (so the
    /// applied resize matrix is the single top-level transform) — the
    /// PdfFileEditor.ResizeContents behaviour.</summary>
    internal void ApplyContentResizeAsForm(double sx, double sy, double tx, double ty)
    {
        var originalContent = CollectContentBytes();
        var formName = WrapContentInForm(originalContent);

        var bytes = System.Text.Encoding.ASCII.GetBytes(
            $"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{formName} Do\nQ\n");
        SetContentStream(bytes);

        TransformAnnotationRects(sx, sy, tx, ty);
    }

    /// <summary>The page's decoded content, for read-only scans by other subsystems
    /// (e.g. the tagged-caption lookup of the form facade).</summary>
    internal byte[] GetDecodedContentBytes() => CollectContentBytes();

    /// <summary>Decode and concatenate the page's content stream(s) into one byte array.</summary>
    private byte[] CollectContentBytes()
    {
        var existing = _reader.Resolve(_dict.Get("Contents"));
        if (existing is PdfStream singleStream)
            return _reader.DecodeStream(singleStream);
        if (existing is PdfArray arr)
        {
            using var buf = new MemoryStream();
            foreach (var item in arr)
            {
                var stream = _reader.ResolveStream(item);
                if (stream is null) continue;
                var data = _reader.DecodeStream(stream);
                if (buf.Length > 0) buf.WriteByte((byte)'\n');
                buf.Write(data);
            }
            return buf.ToArray();
        }
        return [];
    }

    /// <summary>Wrap <paramref name="content"/> in a Form XObject whose resources mirror
    /// the page's (including a snapshot of the existing /XObject entries so the moved
    /// content's images still resolve), register it under a fresh /FmN name in the
    /// page's /Resources/XObject, and return that name.</summary>
    private string WrapContentInForm(byte[] content)
    {
        // Resolve the page /Resources — it is frequently an indirect reference, in which
        // case `as PdfDictionary` would yield null and the moved content would lose every
        // font/image/XObject it references (e.g. a missing /Im1). A page that INHERITS its
        // resources (no /Resources of its own) is seeded from the inherited dict rather
        // than an empty one, so the wrapped content keeps its fonts/images.
        var resources = Forms.Form.EnsureOwnPageResources(_dict, _reader);

        var formResources = new PdfDictionary();
        foreach (var key in new[] { "Font", "ExtGState", "Pattern", "ColorSpace", "Shading", "ProcSet", "Properties" })
        {
            var entry = resources.Get(key);
            if (entry is not null) formResources.Set(key, entry);
        }

        // Snapshot the page's current /XObject entries BEFORE the form is registered,
        // so the moved content can reference them but the form can't see itself.
        var pageXObjects = _reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (pageXObjects is not null)
        {
            var formXObjects = new PdfDictionary();
            foreach (var key in pageXObjects.Keys)
            {
                var entry = pageXObjects.Get(key);
                if (entry is not null) formXObjects.Set(key, entry);
            }
            formResources.Set("XObject", formXObjects);
        }

        var mb = MediaBox;
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(mb.LLX));
        bbox.Add(new PdfReal(mb.LLY));
        bbox.Add(new PdfReal(mb.URX));
        bbox.Add(new PdfReal(mb.URY));

        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        formDict.Set("FormType", new PdfInteger(1));
        formDict.Set("BBox", bbox);
        formDict.Set("Resources", formResources);
        var formStream = new PdfStream(formDict, content);

        var xobjects = _reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }

        // Content wrapped into a form by resize/zoom is numbered from Fm0
        // (PdfFileEditor.ResizeContents yields /Fm0).
        var name = "Fm0";
        var counter = 0;
        while (xobjects.ContainsKey(name)) name = $"Fm{++counter}";
        xobjects.Set(name, formStream);
        return name;
    }

    /// <summary>Bracket the page's existing content in q/Q before generated
    /// content is appended. An imported page may leave a persistent CTM active
    /// (e.g. a top-level y-flip `1 0 0 -1 0 H cm` outside any q/Q); without the
    /// bracket, appended header/footer/stamp content inherits that matrix and
    /// renders flipped or displaced. Idempotent.</summary>
    internal void IsolateExistingContent()
    {
        if (_contentIsolated) return;
        _contentIsolated = true;
        if (_reader.Resolve(_dict.Get("Contents")) is null) return;
        PrependContentStream(System.Text.Encoding.ASCII.GetBytes("q\n"));
        AddContentStream(System.Text.Encoding.ASCII.GetBytes("Q\n"));
    }
}
