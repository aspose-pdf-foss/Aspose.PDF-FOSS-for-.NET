using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

/// <summary>
/// Parses a PDF content stream and updates a GraphicsState while invoking
/// callbacks for each operator. This provides the foundation for text extraction
/// with position data, image extraction, and content analysis.
/// </summary>
internal sealed partial class ContentStreamParser
{
    private readonly GraphicsState _state = new();
    private readonly PdfReader _reader;

    private readonly List<PathCommand> _pathSegments = [];
    // True once a valid `m` (or `re`) has opened a subpath. A line/curve segment
    // that arrives without one — e.g. after a malformed `m` whose operands were
    // swallowed by a stray `NaN` token — is dropped rather than stroked from the
    // implicit origin.
    private bool _subpathOpen;
    // True when a path-construction operator since the last paint was dropped for
    // insufficient operands (damaged content streams fuse coordinate separators).
    // A clip built from such a partial path would cut away legitimate content, so
    // the pending W/W* is ignored instead — standard viewer tolerance.
    private bool _pathBroken;

    // PDF 32000 §8.5.4: W / W* do not immediately modify the clipping path; they flag
    // the current path for intersection with the existing clip once the following
    // painting operator has run. Tracked here so we can fire OnPathClipped after
    // OnPathPainted when one of these appeared between `m`/`l` and `S`/`f`/etc.
    private bool? _pendingClipEvenOdd;

    public ContentStreamParser(PdfReader reader)
    {
        _reader = reader;
    }

    /// <summary>The current graphics state.</summary>
    public GraphicsState State => _state;

    /// <summary>Fired when a text string is shown (Tj, TJ, ', ").</summary>
    public event Action<string, byte[], GraphicsState>? OnTextShown;

    /// <summary>Fired when an image XObject is drawn (Do operator with Image subtype).</summary>
    public event Action<string, GraphicsState>? OnImageDrawn;

    /// <summary>Fired for any operator (operator name, operand count, state).</summary>
    public event Action<string, int, GraphicsState>? OnOperator;

    /// <summary>Fired when marked content begins (BMC/BDC). Tag name and optional properties dict.</summary>
    public event Action<string, PdfDictionary?>? OnMarkedContentBegin;

    /// <summary>Fired when marked content ends (EMC).</summary>
    public event Action? OnMarkedContentEnd;

    /// <summary>Fired when an inline image is found (BI/ID/EI). Image dict and data bytes.</summary>
    public event Action<PdfDictionary, byte[]>? OnInlineImage;

    /// <summary>Fired when a path painting operator is encountered (S/s/f/F/f*/B/B*/b/b*/n).
    /// Parameters: operator name, graphics state, accumulated path segments.</summary>
    public event Action<string, GraphicsState, IReadOnlyList<PathCommand>>? OnPathPainted;

    /// <summary>
    /// Fired when a <c>W</c> or <c>W*</c> clipping-path intersection should be
    /// applied — always right after the matching <see cref="OnPathPainted"/> so the
    /// path is painted normally, then the renderer tightens the clip for what comes
    /// next. Parameters: even-odd rule (<c>true</c> for <c>W*</c>, <c>false</c> for
    /// <c>W</c>), current graphics state, and the same path segments that were just
    /// painted. The renderer is expected to AND the built mask with the existing
    /// <see cref="GraphicsState.ClipMask"/> and store the result back on the state.
    /// </summary>
    public event Action<bool, GraphicsState, IReadOnlyList<PathCommand>>? OnPathClipped;

    /// <summary>
    /// Fired for the <c>sh</c> shading-paint operator (PDF 32000 §8.7.4.5). Parameters:
    /// /Shading resource name and the graphics state at the time of the paint. The
    /// shaded region is bounded by the current clipping path; the renderer is expected
    /// to look the name up in its /Shading resource dictionary and rasterise accordingly.
    /// </summary>
    public event Action<string, GraphicsState>? OnShadingPainted;

    /// <summary>
    /// Parse a content stream, updating state and firing events.
    /// </summary>
    /// <summary>
    /// Maximum number of tokens to parse before aborting. This prevents hangs on
    /// malformed PDFs with binary garbage in content streams that the lexer interprets
    /// as an endless sequence of tokens.
    /// </summary>
    internal int MaxTokens { get; set; } = 10_000_000;

    /// <summary>
    /// Pre-resolved /Separation and /DeviceN colorspaces from the page resources,
    /// keyed by colorspace name. Each value carries the tint-transform function
    /// and the alternate colorspace's family so `scn` can convert tint inputs
    /// into RGB. Built once per Parse from the optional colorSpaces argument
    /// rather than chasing the array via _reader on every fill.
    /// </summary>
    private readonly Dictionary<string, (Functions.PdfFunction tint, string altSpace)> _tintColorSpaces = new();
    // Named [/ICCBased] spaces whose profile is a scanner-class Lab-encoded
    // profile — scn components clamp to [0,1] and decode as
    // L = c0·100, a = c1·255−128, b = c2·255−128 (see IsLabEncodedIcc).
    private readonly HashSet<string> _labEncColorSpaces = new();
    // Named DIRECT [/Lab <dict>] spaces: scn/SCN operands are raw L,a,b values
    // (L 0..100, a/b typically ±128) and must go through the Lab→sRGB transform —
    // read as display RGB they clamp into wildly wrong colours.
    private readonly HashSet<string> _labColorSpaces = new();

    /// <summary>
    /// Names of page-resource colour spaces that resolve to a /Pattern space
    /// (either <c>/Pattern</c> or <c>[/Pattern baseColorSpace]</c>). A `cs`/`scn`
    /// fill that names one of these pins a pattern just like the literal
    /// <c>/Pattern cs</c> does — without this, an uncoloured pattern set via a
    /// named space (e.g. <c>/PAT cs 0 /P1 scn</c>) is mistaken for a solid colour
    /// and the shape fills opaque instead of with the tiling pattern.
    /// </summary>
    private readonly HashSet<string> _patternColorSpaces = new();

    /// <summary>
    /// Names of page-resource colour spaces that resolve to a bare <c>/Pattern</c>
    /// or <c>[/Pattern]</c> (a coloured pattern). A <c>scn</c> naming one of these is
    /// only pinned as a pattern fill when the referenced pattern is a shading
    /// pattern (PatternType 2); coloured tiling patterns FOSS cannot rasterise stay
    /// on the legacy solid-fill path. Resolved against <see cref="_patterns"/>.
    /// </summary>
    private readonly HashSet<string> _barePatternColorSpaces = new();

    /// <summary>Page-level /Resources/Pattern dict, captured from <see cref="Parse"/>.</summary>
    private PdfDictionary? _patterns;

    /// <summary>
    /// Page-level /Resources/Properties dict, captured from <see cref="Parse"/>.
    /// Used to resolve the second operand of BDC when it's a name reference
    /// (e.g. <c>/OC /MC0 BDC</c> — MC0 lives in Properties and points to an OCG).
    /// </summary>
    private PdfDictionary? _properties;

    public void Parse(byte[] streamBytes, Dictionary<string, PdfDictionary>? fonts = null,
     Dictionary<int, string>? toUnicode = null,
     Dictionary<string, PdfDictionary>? extGStates = null,
     PdfDictionary? colorSpaces = null,
     PdfDictionary? properties = null,
     PdfDictionary? patterns = null)
    {
        // Pre-resolve any /Separation or /DeviceN colorspaces named in the page
        // resources so `scn` can apply the tint transform without re-walking the
        // resources dictionary on every fill. We accept anything that yields a
        // parseable PdfFunction plus an alternate-space family name; richer
        // alternate spaces (ICCBased) fall through to whatever ColorSpaceFamily
        // the array's third element maps to.
        _properties = properties;
        _patterns = patterns;
        _tintColorSpaces.Clear();
        _patternColorSpaces.Clear();
        _barePatternColorSpaces.Clear();
        _labEncColorSpaces.Clear();
        _labColorSpaces.Clear();
        if (colorSpaces is not null)
        {
            foreach (var name in colorSpaces.Keys)
            {
                var resolved = _reader.Resolve(colorSpaces.Get(name));
                // An UNCOLOURED pattern space — [/Pattern baseColourSpace] — is
                // always pinned: the numeric scn operands supply the colour the
                // renderer paints the tiling cell mask with, and FOSS renders these.
                if (resolved is PdfArray parr && parr.Count >= 2 && (parr[0] as PdfName)?.Value == "Pattern")
                { _patternColorSpaces.Add(name); continue; }
                // A bare /Pattern name or [/Pattern] (a coloured pattern that carries
                // its own colour operators) is recorded separately. A `scn` naming it
                // is only routed to the renderer when the referenced pattern is a
                // shading pattern (PatternType 2), which FOSS rasterises; coloured
                // tiling cells (PatternType 1) stay on the legacy solid-fill path so
                // they keep their last solid colour instead of rendering blank.
                if ((resolved is PdfName pn && pn.Value == "Pattern")
                    || (resolved is PdfArray bp && bp.Count >= 1 && (bp[0] as PdfName)?.Value == "Pattern"))
                { _barePatternColorSpaces.Add(name); continue; }
                if (resolved is not PdfArray arr) continue;
                // A named [/ICCBased <stream>] space whose profile is a scanner-class
                // (Lab-encoded) profile: producers like pdfDocs write RAW Lab-ish scn
                // operands against it (e.g. "100 -1 -1 scn"); record it so scn can
                // clamp + decode instead of misreading the components as display RGB.
                if (arr.Count >= 2 && (arr[0] as PdfName)?.Value == "ICCBased"
                    && IsLabEncodedIcc(arr[1]))
                { _labEncColorSpaces.Add(name); continue; }
                if (arr.Count >= 1 && (arr[0] as PdfName)?.Value == "Lab")
                { _labColorSpaces.Add(name); continue; }
                if (arr.Count < 4) continue;
                var familyName = (arr[0] as PdfName)?.Value;
                if (familyName != "Separation" && familyName != "DeviceN") continue;
                var altSpace = ResolveAltSpaceName(arr[2]);
                var tint = altSpace is null ? null : Functions.PdfFunction.Parse(arr[3], _reader);
                // A scanner-class ICC alternate carries no marker for how the tint
                // output is ENCODED — some producers emit Lab-encoded channels
                // (L/100, (a|b+128)/255), others plain display RGB against the same
                // profile class. The no-ink end disambiguates: tint(0) must be paper
                // white, which is (1, ~0.5, ~0.5) in the Lab encoding but (1, 1, 1)
                // in display RGB. Only keep the LabEnc decode when the function
                // actually lands near the Lab-encoded white.
                if (altSpace == "LabEnc" && tint is not null)
                {
                    var zero = tint.Evaluate(new double[] { 0 });
                    bool labWhite = zero is { Length: >= 3 }
                        && Math.Abs(zero[1] - 0.5) < 0.25 && Math.Abs(zero[2] - 0.5) < 0.25;
                    if (!labWhite) altSpace = "DeviceRGB";
                }
                if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_CSDEBUG") == "1")
                    Console.Error.WriteLine($"[cs] {name} family={familyName} alt={altSpace ?? "NULL"} tint={(tint is null ? "NULL" : tint.GetType().Name)}");
                if (altSpace is null || tint is null) continue;
                _tintColorSpaces[name] = (tint, altSpace);
            }
        }

        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontKey = null;
        Dictionary<int, string>? currentToUnicode = toUnicode;
        int tokenCount = 0;
        // Safety guard against malformed streams (a lexer that stops advancing).
        // A well-formed token consumes at least one input byte, so the byte count
        // is a true upper bound — huge legitimate vector streams (60MB+ CAD maps)
        // must parse to the end rather than stop at an arbitrary op count.
        var maxTokens = Math.Max(MaxTokens, streamBytes.Length);

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            if (++tokenCount > maxTokens) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.Boolean:
                    operands.Add(token.BoolValue ? PdfBoolean.True : PdfBoolean.False);
                    break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.DictStart:
                    operands.Add(ParseDict(lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    if (op == "BI")
                    {
                        // Inline image: parse dict entries until ID, then read binary data until EI
                        ParseInlineImage(lexer);
                        operands.Clear();
                        break;
                    }
                    ProcessOperator(op, operands, fonts, extGStates, ref currentFontKey, ref currentToUnicode);
                    OnOperator?.Invoke(op, operands.Count, _state);
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    // Direct mark-making content operators (PDF 32000 §A): path-painting, text-showing
    // and XObject invocation. `sh` is deliberately excluded — a shading-only cell is
    // handled by the solid-colour fallback (see IsRenderablePattern).
    private static readonly HashSet<string> _directPaintOps = new()
    {
        "f", "F", "f*", "S", "s", "B", "B*", "b", "b*",
        "Tj", "TJ", "'", "\"", "Do",
    };

    private Text.FontMetrics? _currentMetrics;
    private Text.CidFontInfo? _currentCidInfo;

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static int Int(PdfObject obj) => obj switch
    {
        PdfInteger i => (int)i.Value, PdfReal r => (int)r.Value, _ => 0,
    };

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static PdfDictionary ParseDict(PdfLexer lexer)
    {
        var dict = new PdfDictionary();
        while (true)
        {
            var keyToken = lexer.NextToken();
            if (keyToken.Kind == TokenKind.DictEnd || keyToken.Kind == TokenKind.Eof) break;
            if (keyToken.Kind != TokenKind.Name) continue;

            var valToken = lexer.NextToken();
            if (valToken.Kind == TokenKind.DictEnd || valToken.Kind == TokenKind.Eof) break;

            PdfObject value = valToken.Kind switch
            {
                TokenKind.Integer => new PdfInteger(valToken.IntValue),
                TokenKind.Real => new PdfReal(valToken.RealValue),
                TokenKind.Name => new PdfName(valToken.StringValue!),
                TokenKind.LiteralString => new PdfString(valToken.BytesValue!),
                TokenKind.HexString => new PdfString(valToken.BytesValue!, isHex: true),
                TokenKind.Boolean => valToken.BoolValue ? PdfBoolean.True : PdfBoolean.False,
                _ => PdfNull.Instance,
            };

            dict.Set(keyToken.StringValue!, value);
        }
        return dict;
    }
}
