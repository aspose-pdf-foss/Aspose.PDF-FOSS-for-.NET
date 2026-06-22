using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Collection of artifacts on a page. Extracts artifacts from the content stream
/// by parsing /Artifact BMC/BDC … EMC marked content sequences (PDF 32000 §14.8.2.2).
/// Supports 1-based indexing to match the public API.
/// </summary>
public sealed class ArtifactCollection : IEnumerable<Artifact>
{
    private readonly Page _page;
    private List<Artifact>? _items;

    internal ArtifactCollection(Page page) => _page = page;

    private readonly List<BackgroundArtifact> _backgrounds = new();

    /// <summary>Add a watermark artifact to the page.</summary>
    public void Add(WatermarkArtifact artifact) => artifact.AddToPage(_page);

    /// <summary>Add a background artifact to the page. Stored only;
    /// the renderer does not currently paint the background image, but the
    /// artifact is recorded so callers can iterate it later.</summary>
    public void Add(BackgroundArtifact artifact) => _backgrounds.Add(artifact);

    /// <summary>Add a generic artifact to the collection. Delegates to the
    /// strongly-typed overload when <paramref name="artifact"/> is a
    /// <see cref="WatermarkArtifact"/>; otherwise the artifact is appended
    /// to the parsed list.</summary>
    public void Add(Artifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (artifact is WatermarkArtifact w) { Add(w); return; }
        EnsureParsed();
        artifact.Page = _page;
        _items!.Add(artifact);
    }

    /// <summary>Copy this collection's artifacts into <paramref name="dest"/> starting at <paramref name="index"/>.</summary>
    public void CopyTo(Artifact[] dest, int index)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        EnsureParsed();
        var items = _items!;
        if (index + items.Count > dest.Length)
            throw new ArgumentException("Destination array is not long enough.", nameof(dest));
        for (int i = 0; i < items.Count; i++)
            dest[index + i] = items[i];
    }

    /// <summary>Find artifacts whose <paramref name="name"/> entry equals <paramref name="expectedValue"/>.</summary>
    public List<Artifact> FindByValue(string name, string expectedValue)
    {
        EnsureParsed();
        var matches = new List<Artifact>();
        foreach (var a in _items!)
        {
            if (string.Equals(a.GetValue(name), expectedValue, StringComparison.Ordinal))
                matches.Add(a);
        }
        return matches;
    }

    /// <summary>Update <paramref name="artifact"/>'s entry on the page (no-op when the artifact is not in the collection).</summary>
    public void Update(Artifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        EnsureParsed();
        if (!_items!.Contains(artifact)) return;
        artifact.Page = _page;
    }

    /// <summary>Whether this collection is read-only (always false).</summary>
    public bool IsReadOnly => false;

    /// <summary>Whether access to this collection is thread-safe (always false).</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronisation root for thread-safe access.</summary>
    public object SyncRoot { get; } = new object();

    /// <summary>Number of artifacts on the page.</summary>
    public int Count
    {
        get
        {
            EnsureParsed();
            return _items!.Count;
        }
    }

    /// <summary>
    /// Get an artifact by 1-based index.
    /// </summary>
    public Artifact this[int index]
    {
        get
        {
            EnsureParsed();
            var items = _items!;
            if (index < 1 || index > items.Count)
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. Must be between 1 and {items.Count}.");
            return items[index - 1];
        }
    }

    public IEnumerator<Artifact> GetEnumerator()
    {
        EnsureParsed();
        // Snapshot so callers can Delete inside a foreach without
        // tripping List<T>'s mutation guard.
        return _items!.ToList().GetEnumerator();
    }

    /// <summary>Delete the artifact at the given 1-based index.</summary>
    public void Delete(int index)
    {
        EnsureParsed();
        var items = _items!;
        if (index < 1 || index > items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        items.RemoveAt(index - 1);
    }

    /// <summary>Remove an artifact by reference; no-op if not in the collection.</summary>
    public void Delete(Artifact artifact)
    {
        EnsureParsed();
        _items!.Remove(artifact);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Parsing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses artifacts from the page's content stream on first access.
    /// Walks through tokens looking for /Artifact BMC or /Artifact «props» BDC
    /// sequences and extracts text from Tj/TJ operators within each artifact block.
    /// </summary>
    private void EnsureParsed()
    {
        if (_items is not null) return;
        _items = new List<Artifact>();

        var contentBytes = _page.GetContentStreamBytes();
        if (contentBytes is null || contentBytes.Length == 0) return;

        var state = new ParseState(_page);
        ParseContentStream(contentBytes, state);

        // Finalize: add any artifact that wasn't closed by EMC (malformed PDF)
        if (state.Current is not null)
        {
            state.Current.Text = state.TextBuilder.ToString();
            _items.Add(state.Current);
        }
    }

    /// <summary>
    /// Tokenizes a content stream and dispatches each operator to the appropriate handler.
    /// </summary>
    private void ParseContentStream(byte[] contentBytes, ParseState state)
    {
        var lexer = new PdfLexer(contentBytes);
        var operands = new List<PdfObject>();

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            if (token.Kind == TokenKind.Keyword)
            {
                DispatchOperator(token.StringValue!, operands, state);
                operands.Clear();
                continue;
            }

            // Accumulate operands until we hit an operator keyword
            var obj = TokenToObject(token, lexer);
            if (obj is not null)
                operands.Add(obj);
            else
                operands.Clear();
        }
    }

    /// <summary>
    /// Converts a lexer token to a PdfObject. Returns null for unexpected token types.
    /// </summary>
    private static PdfObject? TokenToObject(Token token, PdfLexer lexer) => token.Kind switch
    {
        TokenKind.Integer => new PdfInteger(token.IntValue),
        TokenKind.Real => new PdfReal(token.RealValue),
        TokenKind.LiteralString => new PdfString(token.BytesValue!),
        TokenKind.HexString => new PdfString(token.BytesValue!, isHex: true),
        TokenKind.Name => new PdfName(token.StringValue!),
        TokenKind.ArrayStart => ParseArray(lexer),
        TokenKind.DictStart => ParseDict(lexer),
        _ => null,
    };

    // ── Operator dispatch ───────────────────────────────────────────────

    /// <summary>
    /// Routes a PDF operator to the correct handler. Operators fall into three groups:
    /// 1. Marked content (BMC/BDC/EMC) — tracks artifact boundaries
    /// 2. Text positioning (Td/TD/Tm/T*) — detects line breaks within artifacts
    /// 3. Text showing (Tj/TJ/') — extracts text content from artifacts
    /// </summary>
    private void DispatchOperator(string op, List<PdfObject> operands, ParseState state)
    {
        switch (op)
        {
            case "BMC": HandleBMC(operands, state); break;
            case "BDC": HandleBDC(operands, state); break;
            case "EMC": HandleEMC(state); break;
            case "Td" or "TD": HandleTd(operands, state); break;
            case "Tm": HandleTm(operands, state); break;
            case "T*": HandleTStar(state); break;
            case "Tj": HandleTj(operands, state); break;
            case "TJ": HandleTJArray(operands, state); break;
            case "'": HandleQuote(operands, state); break;
            case "Do": HandleDo(operands, state); break;
            // BT/ET are intentionally not handled — we don't insert newlines on BT
            // because many PDFs use separate BT/ET blocks for fragments on the same line.
        }
    }

    // ── Marked content operators ────────────────────────────────────────

    /// <summary>
    /// BMC — Begin Marked Content (no properties).
    /// If the tag is /Artifact, starts capturing a new artifact.
    /// </summary>
    private void HandleBMC(List<PdfObject> operands, ParseState state)
    {
        state.McDepth++;
        var tag = operands.Count > 0 ? (operands[0] as PdfName)?.Value : null;
        if (tag != "Artifact" || state.Current is not null) return;

        state.StartNewArtifact();
    }

    /// <summary>
    /// BDC — Begin Marked Content with properties dictionary.
    /// Same as BMC but also reads /Type, /Subtype, /BBox, /Attached from the properties.
    /// </summary>
    private void HandleBDC(List<PdfObject> operands, ParseState state)
    {
        state.McDepth++;
        var tag = operands.Count > 0 ? (operands[0] as PdfName)?.Value : null;
        if (tag != "Artifact" || state.Current is not null) return;

        state.StartNewArtifact();

        // Apply properties dictionary (Type, Subtype, BBox, Attached)
        if (operands.Count > 1 && operands[1] is PdfDictionary props)
        {
            if (string.Equals(props.GetName("Subtype"), "Watermark", StringComparison.OrdinalIgnoreCase))
                state.UpgradeToWatermark();
            ApplyArtifactProperties(state.Current!, props);
        }
    }

    /// <summary>
    /// EMC — End Marked Content. If we're at the artifact's nesting level,
    /// finalize the artifact's text and add it to the collection.
    /// </summary>
    private void HandleEMC(ParseState state)
    {
        if (state.Current is not null && state.McDepth == state.ArtifactDepth)
        {
            state.Current.Text = state.TextBuilder.ToString();
            _items!.Add(state.Current);
            state.Current = null;
            state.ArtifactDepth = 0;
        }
        state.McDepth--;
    }

    /// <summary>Do — paint an XObject. Inside a watermark artifact, an image
    /// XObject reference supplies the watermark's image: resolve it and surface
    /// its native pixel size as the artifact's Image (dimensions come from the
    /// XObject /Width and /Height).</summary>
    private static void HandleDo(List<PdfObject> operands, ParseState state)
    {
        if (state.Current is not WatermarkArtifact wm) return;
        if (operands.Count < 1 || operands[0] is not PdfName name) return;

        var reader = state.Page.Reader;
        var resources = reader.ResolveDict(state.Page.Dict.Get("Resources"));
        var xobjects = reader.ResolveDict(resources?.Get("XObject"));
        if (xobjects is null) return;
        var stream = reader.ResolveStream(xobjects.Get(name.Value));
        if (stream is null || stream.Dict.GetName("Subtype") != "Image") return;

        // Surface the embedded image as an XImage over the XObject — its Width/Height
        // come straight from the stream dictionary (no raster decode needed).
        wm.Image = new XImage(name.Value, stream, reader);
    }

    // ── Text positioning operators ──────────────────────────────────────

    /// <summary>
    /// Td/TD — move text position. Inserts \r\n only when the vertical
    /// position (dy) changes, since horizontal-only moves (dy=0) stay on the same line.
    /// </summary>
    private static void HandleTd(List<PdfObject> operands, ParseState state)
    {
        if (state.Current is null || operands.Count < 2 || state.TextBuilder.Length == 0) return;

        double dy = GetNumericValue(operands[1]);
        if (Math.Abs(dy) > 0.01)
            state.TextBuilder.Append("\r\n");
    }

    /// <summary>
    /// Tm — set text matrix. Inserts \r\n only when the Y position (operand[5])
    /// actually changes from the previous Tm, avoiding false newlines when
    /// successive BT/ET blocks use the same vertical position.
    /// </summary>
    private static void HandleTm(List<PdfObject> operands, ParseState state)
    {
        if (state.Current is null || operands.Count < 6) return;

        double tmY = GetNumericValue(operands[5]);

        // Insert newline only if Y position changed from the last Tm
        if (state.TextBuilder.Length > 0)
        {
            bool yChanged = double.IsNaN(state.LastTmY) || Math.Abs(tmY - state.LastTmY) > 0.01;
            if (yChanged)
                state.TextBuilder.Append("\r\n");
        }
        state.LastTmY = tmY;
    }

    /// <summary>T* — move to start of next line. Always inserts a newline.</summary>
    private static void HandleTStar(ParseState state)
    {
        if (state.Current is not null && state.TextBuilder.Length > 0)
            state.TextBuilder.Append("\r\n");
    }

    // ── Text showing operators ──────────────────────────────────────────

    /// <summary>Tj — show a single text string.</summary>
    private static void HandleTj(List<PdfObject> operands, ParseState state)
    {
        if (state.Current is null) return;
        if (operands.Count > 0 && operands[0] is PdfString str)
            state.TextBuilder.Append(str.ToText());
    }

    /// <summary>
    /// TJ — show text with individual glyph positioning.
    /// The array contains strings and numeric adjustments; we extract only the strings.
    /// </summary>
    private static void HandleTJArray(List<PdfObject> operands, ParseState state)
    {
        if (state.Current is null) return;
        if (operands.Count == 0 || operands[0] is not PdfArray arr) return;

        foreach (var elem in arr)
        {
            if (elem is PdfString s)
                state.TextBuilder.Append(s.ToText());
        }
    }

    /// <summary>' — move to next line and show text (equivalent to T* followed by Tj).</summary>
    private static void HandleQuote(List<PdfObject> operands, ParseState state)
    {
        if (state.Current is null) return;

        if (state.TextBuilder.Length > 0)
            state.TextBuilder.Append("\r\n");

        if (operands.Count > 0 && operands[0] is PdfString str)
            state.TextBuilder.Append(str.ToText());
    }

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads artifact properties from a BDC properties dictionary.
    /// Properties include /Type, /Subtype, /BBox, /Attached (PDF 32000 §14.8.2.2).
    /// </summary>
    private static void ApplyArtifactProperties(Artifact artifact, PdfDictionary props)
    {
        var type = props.GetName("Type");
        if (type is not null)
        {
            artifact.CustomType = type;
            if (Enum.TryParse<Artifact.ArtifactType>(type, ignoreCase: true, out var parsedType))
                artifact.Type = parsedType;
        }

        var subtype = props.GetName("Subtype");
        if (subtype is not null)
        {
            artifact.CustomSubtype = subtype;
            if (Enum.TryParse<Artifact.ArtifactSubtype>(subtype, ignoreCase: true, out var parsedSubtype))
                artifact.Subtype = parsedSubtype;
        }

        var bbox = props.Get("BBox") as PdfArray;
        if (bbox is { Count: >= 4 })
            artifact.Rectangle = Rectangle.FromPdfArray(bbox);

        // /Attached indicates edge alignment: "Top", "Bottom", "Left", "Right"
        var attached = props.Get("Attached") as PdfArray;
        if (attached is null) return;

        foreach (var att in attached)
        {
            var name = (att as PdfName)?.Value;
            switch (name)
            {
                case "Top": artifact.ArtifactVerticalAlignment = VerticalAlignment.Top; break;
                case "Bottom": artifact.ArtifactVerticalAlignment = VerticalAlignment.Bottom; break;
                case "Left": artifact.ArtifactHorizontalAlignment = HorizontalAlignment.Left; break;
                case "Right": artifact.ArtifactHorizontalAlignment = HorizontalAlignment.Right; break;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Extracts a numeric value from a PdfObject (integer or real).</summary>
    private static double GetNumericValue(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Parses a PDF array from the lexer token stream.</summary>
    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;

            var obj = TokenToObject(t, lexer);
            if (obj is not null) arr.Add(obj);
        }
        return arr;
    }

    /// <summary>Parses a PDF dictionary from the lexer token stream.</summary>
    private static PdfDictionary ParseDict(PdfLexer lexer)
    {
        var dict = new PdfDictionary();
        while (true)
        {
            // Read key (must be a name)
            var keyToken = lexer.NextToken();
            if (keyToken.Kind == TokenKind.DictEnd || keyToken.Kind == TokenKind.Eof) break;
            if (keyToken.Kind != TokenKind.Name) continue;

            // Read value
            var valToken = lexer.NextToken();
            if (valToken.Kind == TokenKind.DictEnd || valToken.Kind == TokenKind.Eof) break;

            var value = TokenToObject(valToken, lexer);
            if (value is not null)
                dict.Set(keyToken.StringValue!, value);
        }
        return dict;
    }

    // ── Parse state ─────────────────────────────────────────────────────

    /// <summary>
    /// Mutable state carried through content stream parsing.
    /// Groups all the tracking variables into a single object to keep method signatures clean.
    /// </summary>
    private sealed class ParseState
    {
        private readonly Page _page;

        public ParseState(Page page) => _page = page;

        /// <summary>The page being parsed (for resolving image XObjects referenced by /Do).</summary>
        public Page Page => _page;

        /// <summary>The artifact currently being built (null when outside an artifact block).</summary>
        public Artifact? Current;

        /// <summary>Current marked content nesting depth.</summary>
        public int McDepth;

        /// <summary>The McDepth at which the current artifact's BMC/BDC was encountered.</summary>
        public int ArtifactDepth;

        /// <summary>Accumulates extracted text for the current artifact.</summary>
        public readonly System.Text.StringBuilder TextBuilder = new();

        /// <summary>
        /// Y position from the last Tm operator. Used to detect vertical position changes
        /// and avoid inserting false newlines when the Y position stays the same.
        /// </summary>
        public double LastTmY = double.NaN;

        /// <summary>Initializes state for a new artifact block.</summary>
        public void StartNewArtifact()
        {
            Current = new Artifact { Page = _page };
            ArtifactDepth = McDepth;
            TextBuilder.Clear();
            LastTmY = double.NaN;
        }

        /// <summary>Replace the current artifact with a typed
        /// <see cref="WatermarkArtifact"/> (when the BDC subtype is /Watermark),
        /// preserving the page and the already-recorded artifact depth.</summary>
        public void UpgradeToWatermark()
        {
            if (Current is WatermarkArtifact) return;
            Current = new WatermarkArtifact { Page = _page };
        }
    }
}
