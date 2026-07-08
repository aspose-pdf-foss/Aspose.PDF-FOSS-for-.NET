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

    /// <summary>Add a watermark artifact to the page.</summary>
    public void Add(WatermarkArtifact artifact) => artifact.AddToPage(_page);

    /// <summary>Add a background artifact to the page: it is rendered into the
    /// page content as a prepended /Artifact /Subtype /Background block and
    /// recorded in the collection so it round-trips on reload.</summary>
    public void Add(BackgroundArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        EnsureParsed();
        artifact.RenderToPage(_page);
        artifact.Page = _page;
        _items!.Add(artifact);
    }

    /// <summary>Add a generic artifact to the collection. Delegates to the
    /// strongly-typed overloads for <see cref="WatermarkArtifact"/> /
    /// <see cref="BackgroundArtifact"/>; otherwise the artifact is rendered
    /// into the page content as an /Artifact marked-content block and recorded
    /// in the collection so it round-trips on reload.</summary>
    public void Add(Artifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (artifact is WatermarkArtifact w) { Add(w); return; }
        if (artifact is BackgroundArtifact b) { Add(b); return; }
        EnsureParsed();
        artifact.RenderToPage(_page);
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

    /// <summary>Delete the artifact at the given 1-based index. The artifact's
    /// /Artifact marked-content block is also spliced out of the page content stream
    /// so the deletion survives save + reopen.</summary>
    public void Delete(int index)
    {
        EnsureParsed();
        var items = _items!;
        if (index < 1 || index > items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        RemoveArtifactBlockFromContent(index - 1);
        items.RemoveAt(index - 1);
    }

    /// <summary>Remove an artifact by reference; no-op if not in the collection.
    /// Also splices its marked-content block out of the page content.</summary>
    public void Delete(Artifact artifact)
    {
        EnsureParsed();
        var ordinal = _items!.IndexOf(artifact);
        if (ordinal < 0) return;
        RemoveArtifactBlockFromContent(ordinal);
        _items.RemoveAt(ordinal);
    }

    /// <summary>Splice the (0-based) <paramref name="ordinal"/>-th top-level
    /// /Artifact marked-content block out of the page content stream, so a deleted
    /// artifact does not reappear on reload. The ordinal counts artifact blocks in
    /// content order, which matches the parsed <c>_items</c> order.</summary>
    private void RemoveArtifactBlockFromContent(int ordinal)
    {
        var content = _page.GetContentStreamBytes();
        if (content is null || content.Length == 0) return;
        var range = FindArtifactBlockRange(content, ordinal);
        if (range is not var (start, end)) return;
        var result = new byte[content.Length - (end - start)];
        Array.Copy(content, 0, result, 0, start);
        Array.Copy(content, end, result, start, content.Length - end);
        _page.SetContentStream(result);
    }

    /// <summary>Rewrite the marked-content block of an artifact parsed from this page,
    /// replacing it with a freshly emitted /Artifact … BDC … EMC block that carries the
    /// artifact's current (mutated) properties and text. Called by
    /// <see cref="Artifact.SaveUpdates"/> so in-place edits survive save + reopen. No-op
    /// when the artifact is not in the collection or its block can't be located.</summary>
    internal void RewriteArtifactBlockFor(Artifact artifact)
    {
        EnsureParsed();
        var ordinal = _items!.IndexOf(artifact);
        if (ordinal < 0) return;
        var content = _page.GetContentStreamBytes();
        if (content is null || content.Length == 0) return;
        var range = FindArtifactBlockRange(content, ordinal);
        if (range is not var (start, end)) return;

        var replacement = System.Text.Encoding.ASCII.GetBytes(artifact.BuildInPlaceBlock(_page));
        var result = new byte[start + replacement.Length + (content.Length - end)];
        Array.Copy(content, 0, result, 0, start);
        Array.Copy(replacement, 0, result, start, replacement.Length);
        Array.Copy(content, end, result, start + replacement.Length, content.Length - end);
        _page.SetContentStream(result);
    }

    /// <summary>Find the byte range [start, end) of the <paramref name="ordinal"/>-th
    /// (0-based) top-level /Artifact BMC/BDC … EMC block in <paramref name="content"/>,
    /// mirroring the marked-content nesting logic of the parser. Returns null when not
    /// found.</summary>
    private static (int start, int end)? FindArtifactBlockRange(byte[] content, int ordinal)
    {
        var lexer = new PdfLexer(content);
        long operandRunStart = 0;
        string? firstOperandName = null;
        var firstOperandSeen = false;
        var mcDepth = 0;
        var inArtifact = false;
        var artifactDepth = 0;
        var blockStart = 0;
        var count = 0;

        while (true)
        {
            var tokenStart = lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            if (token.Kind == TokenKind.Keyword)
            {
                var op = token.StringValue;
                if (op is "BMC" or "BDC")
                {
                    mcDepth++;
                    if (!inArtifact && firstOperandName == "Artifact")
                    {
                        inArtifact = true;
                        artifactDepth = mcDepth;
                        blockStart = (int)operandRunStart;
                    }
                }
                else if (op == "EMC")
                {
                    if (inArtifact && mcDepth == artifactDepth)
                    {
                        if (count == ordinal) return (blockStart, (int)lexer.Position);
                        count++;
                        inArtifact = false;
                        artifactDepth = 0;
                    }
                    if (mcDepth > 0) mcDepth--;
                }

                operandRunStart = lexer.Position;
                firstOperandName = null;
                firstOperandSeen = false;
            }
            else if (!firstOperandSeen)
            {
                firstOperandSeen = true;
                operandRunStart = tokenStart;
                firstOperandName = token.Kind == TokenKind.Name ? token.StringValue : null;
            }
        }
        return null;
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

        // Surface each artifact's marked-content block as typed operators via
        // Artifact.Contents — from "/Artifact BMC|BDC" through the closing "EMC"
        // inclusive, matching the reference operator count.
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Contents.Count > 0) continue;
            if (FindArtifactBlockRange(contentBytes, i) is not var (start, end) || end <= start) continue;
            var slice = new byte[end - start];
            Array.Copy(contentBytes, start, slice, 0, end - start);
            foreach (var opText in ContentStreamOperatorParser.ParseOperators(slice))
                _items[i].Contents.Add(TypedOperatorParser.Parse(opText));
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
            case "q": state.PushGs(); break;
            case "Q": state.PopGs(); break;
            case "cm" when operands.Count >= 6:
                state.ConcatCm(GetNumericValue(operands[0]), GetNumericValue(operands[1]),
                    GetNumericValue(operands[2]), GetNumericValue(operands[3]),
                    GetNumericValue(operands[4]), GetNumericValue(operands[5]));
                break;
            case "gs": HandleGs(operands, state); break;
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
            var sub = props.GetName("Subtype");
            if (string.Equals(sub, "Watermark", StringComparison.OrdinalIgnoreCase))
                state.UpgradeToWatermark();
            else if (string.Equals(sub, "Background", StringComparison.OrdinalIgnoreCase))
                state.UpgradeToBackground();
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
        if (state.Current is null) return;
        if (operands.Count < 1 || operands[0] is not PdfName name) return;

        var reader = state.Page.Reader;
        var resources = reader.ResolveDict(state.Page.Dict.Get("Resources"));
        var xobjects = reader.ResolveDict(resources?.Get("XObject"));
        if (xobjects is null) return;
        var stream = reader.ResolveStream(xobjects.Get(name.Value));
        if (stream is null) return;

        var wm = state.Current as WatermarkArtifact;
        var subtype = stream.Dict.GetName("Subtype");
        if (subtype == "Image" && wm is not null)
        {
            // Surface the embedded image as an XImage over the XObject — its Width/Height
            // come straight from the stream dictionary (no raster decode needed).
            wm.Image = new XImage(name.Value, stream, reader);
        }
        else if (subtype == "Form")
        {
            // Any artifact (watermark, header/footer) that draws its caption inside a
            // Form XObject (BT … Tj … ET) — pull the text out so the artifact reports it.
            var formText = ExtractFormText(reader, stream);
            if (formText.Length > 0) state.TextBuilder.Append(formText);

            if (wm is not null)
            {
                // An image watermark draws its image one level down (q … cm /Im0 Do Q).
                var nested = FindFirstImageInForm(reader, stream);
                if (nested is not null)
                {
                    wm.Image = nested;
                    // The image artifact's rectangle is the form's /BBox mapped into page
                    // space by the CTM active at this /Do.
                    if (reader.Resolve(stream.Dict.Get("BBox")) is PdfArray bbox && bbox.Count >= 4)
                        wm.Rectangle = TransformBBox(bbox, state.Ctm);
                }
            }
        }
        // Record the rotation (from the CTM) and opacity (from the active ExtGState)
        // in effect when the watermark XObject is painted.
        if (wm is not null)
        {
            // The watermark's reported rotation is its CTM rotation measured against the
            // page's *displayed* orientation, so a page /Rotate subtracts from it.
            if (wm.Rotation == 0) wm.Rotation = RotationDegrees(state.Ctm) - state.Page.RotateDegrees;
            if (state.Ca < 1.0) wm.Opacity = state.Ca;
        }
    }

    /// <summary>Map a form /BBox [llx lly urx ury] into page space by transforming
    /// its four corners with <paramref name="ctm"/> and taking the axis-aligned bounds.</summary>
    private static Rectangle TransformBBox(PdfArray bbox, double[] ctm)
    {
        double x0 = GetNumericValue(bbox[0]), y0 = GetNumericValue(bbox[1]);
        double x1 = GetNumericValue(bbox[2]), y1 = GetNumericValue(bbox[3]);
        var xs = new double[4];
        var ys = new double[4];
        var corners = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = corners[i];
            xs[i] = ctm[0] * x + ctm[2] * y + ctm[4];
            ys[i] = ctm[1] * x + ctm[3] * y + ctm[5];
        }
        return new Rectangle(Math.Min(Math.Min(xs[0], xs[1]), Math.Min(xs[2], xs[3])),
                             Math.Min(Math.Min(ys[0], ys[1]), Math.Min(ys[2], ys[3])),
                             Math.Max(Math.Max(xs[0], xs[1]), Math.Max(xs[2], xs[3])),
                             Math.Max(Math.Max(ys[0], ys[1]), Math.Max(ys[2], ys[3])));
    }

    /// <summary>Rotation angle (degrees) encoded by a transformation matrix's
    /// (a, b) column: atan2(b, a). Rounded to the nearest degree.</summary>
    private static double RotationDegrees(double[] m)
    {
        var deg = Math.Atan2(m[1], m[0]) * 180.0 / Math.PI;
        return Math.Round(deg, MidpointRounding.AwayFromZero);
    }

    /// <summary>Concatenate the text shown by a Form XObject's Tj/TJ operators
    /// (used to recover a text watermark's caption drawn one level down).</summary>
    private static string ExtractFormText(Aspose.Pdf.IO.PdfReader reader, PdfStream form)
    {
        var sb = new System.Text.StringBuilder();
        var content = reader.DecodeStream(form);
        if (content is null || content.Length == 0) return string.Empty;
        var lexer = new PdfLexer(content);
        var operands = new List<PdfObject>();
        while (true)
        {
            var tok = lexer.NextToken();
            if (tok.Kind == TokenKind.Eof) break;
            if (tok.Kind == TokenKind.Keyword)
            {
                var op = tok.StringValue!;
                if (op == "Tj" && operands.Count > 0 && operands[^1] is PdfString s)
                    sb.Append(s.ToText());
                else if ((op == "TJ") && operands.Count > 0 && operands[^1] is PdfArray arr)
                    foreach (var el in arr) if (el is PdfString ps) sb.Append(ps.ToText());
                operands.Clear();
                continue;
            }
            var o = TokenToObject(tok, lexer);
            if (o is not null) operands.Add(o); else operands.Clear();
        }
        return sb.ToString();
    }

    /// <summary>gs — apply an ExtGState by name; pull its fill opacity (/ca) into
    /// the current graphics state so a watermark records its transparency.</summary>
    private static void HandleGs(List<PdfObject> operands, ParseState state)
    {
        if (operands.Count < 1 || operands[0] is not PdfName name) return;
        var reader = state.Page.Reader;
        var resources = reader.ResolveDict(state.Page.Dict.Get("Resources"));
        var egs = reader.ResolveDict(resources?.Get("ExtGState"));
        var gs = reader.ResolveDict(egs?.Get(name.Value));
        if (gs?.Get("ca") is PdfReal or PdfInteger)
            state.Ca = GetNumericValue(gs.Get("ca")!);
    }

    /// <summary>Scan a Form XObject's content for the first image it paints (a
    /// <c>/Name Do</c> resolving to an /Image XObject in the form's own resources),
    /// descending through nested forms. Used to surface the image of a watermark
    /// drawn through a form wrapper.</summary>
    private static XImage? FindFirstImageInForm(PdfReader reader, PdfStream formStream)
    {
        var formRes = reader.ResolveDict(formStream.Dict.Get("Resources"));
        var formXObjects = reader.ResolveDict(formRes?.Get("XObject"));
        if (formXObjects is null) return null;

        var content = reader.DecodeStream(formStream);
        var lexer = new PdfLexer(content);
        PdfName? lastName = null;
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) break;
            if (t.Kind == TokenKind.Name) { lastName = new PdfName(t.StringValue!); continue; }
            if (t.Kind == TokenKind.Keyword && t.StringValue == "Do" && lastName is not null)
            {
                var s = reader.ResolveStream(formXObjects.Get(lastName.Value));
                if (s is not null)
                {
                    var sub = s.Dict.GetName("Subtype");
                    if (sub == "Image") return new XImage(lastName.Value, s, reader);
                    if (sub == "Form")
                    {
                        var nested = FindFirstImageInForm(reader, s);
                        if (nested is not null) return nested;
                    }
                }
            }
            lastName = null;
        }
        return null;
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

        // Round-trip metadata written by Artifact.BuildPropsDict.
        if (props.Get("Opacity") is PdfReal or PdfInteger)
            artifact.Opacity = GetNumericValue(props.Get("Opacity")!);
        if (props.Get("Rotation") is PdfReal or PdfInteger)
            artifact.Rotation = GetNumericValue(props.Get("Rotation")!);
        if (props.Get("Position") is PdfArray pos && pos.Count >= 2)
            artifact.Position = new Point(GetNumericValue(pos[0]), GetNumericValue(pos[1]));

        // Any remaining string-valued key is a custom name/value pair (SetValue).
        foreach (var key in props.Keys)
        {
            if (Artifact.ReservedPropertyKeys.Contains(key)) continue;
            if (props.Get(key) is PdfString s)
                artifact.SetValue(key, s.ToText());
        }

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

        /// <summary>Current transformation matrix (a b c d e f), and the current fill
        /// opacity (/ca from the active ExtGState). Tracked across cm/q/Q/gs so an
        /// XObject-drawn watermark can record its rotation and opacity.</summary>
        public double[] Ctm = { 1, 0, 0, 1, 0, 0 };
        public double Ca = 1.0;
        private readonly System.Collections.Generic.Stack<(double[] ctm, double ca)> _gsStack = new();

        public void PushGs() => _gsStack.Push(((double[])Ctm.Clone(), Ca));

        public void PopGs()
        {
            if (_gsStack.Count == 0) return;
            (Ctm, Ca) = _gsStack.Pop();
        }

        /// <summary>Pre-multiply the CTM by a cm operand matrix (CTM' = cm · CTM).</summary>
        public void ConcatCm(double a, double b, double c, double d, double e, double f)
        {
            var m = Ctm;
            Ctm = new[]
            {
                a * m[0] + b * m[2],
                a * m[1] + b * m[3],
                c * m[0] + d * m[2],
                c * m[1] + d * m[3],
                e * m[0] + f * m[2] + m[4],
                e * m[1] + f * m[3] + m[5],
            };
        }

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

        /// <summary>Replace the current artifact with a typed
        /// <see cref="BackgroundArtifact"/> (when the BDC subtype is /Background),
        /// preserving the page.</summary>
        public void UpgradeToBackground()
        {
            if (Current is BackgroundArtifact) return;
            Current = new BackgroundArtifact { Page = _page };
        }
    }
}
