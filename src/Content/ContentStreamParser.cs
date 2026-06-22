using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

/// <summary>
/// Parses a PDF content stream and updates a GraphicsState while invoking
/// callbacks for each operator. This provides the foundation for text extraction
/// with position data, image extraction, and content analysis.
/// </summary>
internal sealed class ContentStreamParser
{
    private readonly GraphicsState _state = new();
    private readonly PdfReader _reader;

    private readonly List<PathCommand> _pathSegments = [];
    // True once a valid `m` (or `re`) has opened a subpath. A line/curve segment
    // that arrives without one — e.g. after a malformed `m` whose operands were
    // swallowed by a stray `NaN` token — is dropped rather than stroked from the
    // implicit origin.
    private bool _subpathOpen;

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
                if (resolved is not PdfArray arr || arr.Count < 4) continue;
                var familyName = (arr[0] as PdfName)?.Value;
                if (familyName != "Separation" && familyName != "DeviceN") continue;
                var altSpace = ResolveAltSpaceName(arr[2]);
                if (altSpace is null) continue;
                var tint = Functions.PdfFunction.Parse(arr[3], _reader);
                if (tint is null) continue;
                _tintColorSpaces[name] = (tint, altSpace);
            }
        }

        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontKey = null;
        Dictionary<int, string>? currentToUnicode = toUnicode;
        int tokenCount = 0;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            if (++tokenCount > MaxTokens) break; // safety guard against malformed streams

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

    /// <summary>
    /// Parse an inline image (BI . ID <data> EI).
    /// Abbreviated keys: W=Width, H=Height, BPC=BitsPerComponent, CS=ColorSpace, F=Filter.
    /// </summary>
    private void ParseInlineImage(PdfLexer lexer)
    {
        var dict = new PdfDictionary();

        // Parse key-value pairs until "ID" keyword
        while (true)
        {
            var keyToken = lexer.NextToken();
            if (keyToken.Kind == TokenKind.Eof) return;
            if (keyToken.Kind == TokenKind.Keyword && keyToken.StringValue == "ID") break;
            if (keyToken.Kind != TokenKind.Name) continue;

            var valToken = lexer.NextToken();
            if (valToken.Kind == TokenKind.Keyword && valToken.StringValue == "ID") break;

            // Expand abbreviated key names
            var key = ExpandInlineImageKey(keyToken.StringValue!);

            PdfObject value = valToken.Kind switch
            {
                TokenKind.Integer => new PdfInteger(valToken.IntValue),
                TokenKind.Real => new PdfReal(valToken.RealValue),
                TokenKind.Name => new PdfName(ExpandInlineImageValue(valToken.StringValue!)),
                TokenKind.LiteralString => new PdfString(valToken.BytesValue!),
                TokenKind.HexString => new PdfString(valToken.BytesValue!, isHex: true),
                TokenKind.Boolean => valToken.BoolValue ? PdfBoolean.True : PdfBoolean.False,
                TokenKind.ArrayStart => ParseArray(lexer),
                // /DP (DecodeParms) is a dictionary — e.g. a CCITT glyph image carries
                // <</K -1 /Columns 44>>; without parsing it the filter decodes with the
                // wrong parameters and the image is garbage.
                TokenKind.DictStart => ParseDict(lexer),
                _ => PdfNull.Instance,
            };

            dict.Set(key, value);
        }

        // After ID keyword, skip exactly one whitespace byte, then read raw data until "EI".
        // Prefer an exact payload length over scanning for an EI marker (which can
        // collide with the binary image bytes): unfiltered images compute it from the
        // geometry, RunLengthDecode images from their self-terminating EOD marker.
        var imageData = lexer.ReadInlineImageData(ComputeInlineImageLength(dict), IsRunLengthOnly(dict));
        OnInlineImage?.Invoke(dict, imageData);
    }

    /// <summary>True when the inline image's only filter is RunLengthDecode, whose
    /// stream is self-terminating so its exact length is recoverable.</summary>
    private static bool IsRunLengthOnly(PdfDictionary dict)
    {
        // /Filter values may be abbreviated (/RL) or written in full; array form
        // carries unexpanded names, the direct-name form is already expanded.
        static bool IsRl(string? n) => n is "RunLengthDecode" or "RL";
        return dict.Get("Filter") switch
        {
            PdfName n => IsRl(n.Value),
            PdfArray { Count: 1 } a => IsRl((a[0] as PdfName)?.Value),
            _ => false,
        };
    }

    /// <summary>
    /// Exact byte length of an unfiltered inline image payload, or -1 when it
    /// cannot be determined (filtered data, or a colour space whose component
    /// count is not statically known) — in which case the lexer scans for EI.
    /// </summary>
    private static int ComputeInlineImageLength(PdfDictionary dict)
    {
        // Any filter means the in-stream bytes are encoded; their length is not
        // derivable from the image geometry, so defer to the EI scan.
        if (dict.Get("Filter") != null) return -1;

        var w = dict.GetInt("Width");
        var h = dict.GetInt("Height");
        if (w <= 0 || h <= 0) return -1;

        int components;
        long bpc;
        if (dict.GetBool("ImageMask"))
        {
            components = 1;
            bpc = 1;
        }
        else
        {
            bpc = dict.GetInt("BitsPerComponent");
            if (bpc <= 0) return -1;
            components = InlineImageComponents(dict.Get("ColorSpace"));
            if (components <= 0) return -1;
        }

        long rowBytes = ((long)w * components * bpc + 7) / 8;
        long total = rowBytes * h;
        if (total <= 0 || total > int.MaxValue) return -1;
        return (int)total;
    }

    /// <summary>Component count for inline-image colour spaces we can size statically.</summary>
    private static int InlineImageComponents(PdfObject? cs)
    {
        // Abbreviated device names are already expanded by ExpandInlineImageValue.
        if (cs is PdfName name)
        {
            return name.Value switch
            {
                "DeviceGray" or "CalGray" => 1,
                "DeviceRGB" or "CalRGB" or "Lab" => 3,
                "DeviceCMYK" => 4,
                _ => -1,
            };
        }

        // Indexed colour spaces are 1 component per sample: [/Indexed base hival lookup].
        if (cs is PdfArray { Count: > 0 } arr && arr[0] is PdfName { Value: "Indexed" or "I" })
            return 1;

        // Named resource colour space or anything else: defer to the EI scan.
        return -1;
    }

    private static string ExpandInlineImageKey(string key) => key switch
    {
        "W" => "Width",
        "H" => "Height",
        "BPC" => "BitsPerComponent",
        "CS" => "ColorSpace",
        "D" => "Decode",
        "DP" => "DecodeParms",
        "F" => "Filter",
        "IM" => "ImageMask",
        "I" => "Interpolate",
        _ => key,
    };

    private static string ExpandInlineImageValue(string value) => value switch
    {
        "G" => "DeviceGray",
        "RGB" => "DeviceRGB",
        "CMYK" => "DeviceCMYK",
        "I" => "Indexed",
        "AHx" => "ASCIIHexDecode",
        "A85" => "ASCII85Decode",
        "LZW" => "LZWDecode",
        "Fl" => "FlateDecode",
        "RL" => "RunLengthDecode",
        "CCF" => "CCITTFaxDecode",
        "DCT" => "DCTDecode",
        _ => value,
    };

    private void ProcessOperator(string op, List<PdfObject> operands,
        Dictionary<string, PdfDictionary>? fonts,
        Dictionary<string, PdfDictionary>? extGStates,
        ref string? currentFontKey, ref Dictionary<int, string>? currentToUnicode)
    {
        switch (op)
        {
            // Graphics state
            case "q": _state.Save(); break;
            case "Q": _state.Restore(); break;
            case "gs" when operands.Count >= 1 && operands[0] is PdfName gsName:
                ApplyExtGState(gsName.Value, extGStates);
                break;
            case "cm" when operands.Count >= 6:
                _state.ConcatMatrix(Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3]),
                    Num(operands[4]), Num(operands[5]));
                break;

            // Line attributes
            case "w" when operands.Count >= 1: _state.LineWidth = Num(operands[0]); break;
            case "J" when operands.Count >= 1: _state.LineCap = Int(operands[0]); break;
            case "j" when operands.Count >= 1: _state.LineJoin = Int(operands[0]); break;
            case "M" when operands.Count >= 1: _state.MiterLimit = Num(operands[0]); break;
            case "i" when operands.Count >= 1: _state.Flatness = Num(operands[0]); break;
            case "d" when operands.Count >= 2 && operands[0] is PdfArray dashArr:
                var dash = new double[dashArr.Count];
                for (var di = 0; di < dashArr.Count; di++) dash[di] = Num(dashArr[di]);
                _state.DashArray = dash;
                _state.DashPhase = Num(operands[1]);
                break;

            // Color space operators — changing color space clears any pattern that was
            // pinned for the previous space (PDF 32000 §8.6.8: cs/CS resets the colour).
            case "cs" when operands.Count >= 1 && operands[0] is PdfName csName:
                _state.FillColorSpace = csName.Value;
                _state.FillPatternName = null;
                break;
            case "CS" when operands.Count >= 1 && operands[0] is PdfName csStrokeName:
                _state.StrokeColorSpace = csStrokeName.Value;
                _state.StrokePatternName = null;
                break;

            // Fill color (color space-based). For /Pattern cs the last operand is a
            // pattern resource name (/P5 scn); numeric operands before it are tint
            // values for uncoloured (PaintType 2) patterns and aren't used for fills.
            case "sc" or "scn":
                ApplyPatternOrColor(operands, isFill: true);
                break;
            case "SC" or "SCN":
                ApplyPatternOrColor(operands, isFill: false);
                break;

            // Fill color — these implicitly reset the colour space to Device* and therefore
            // drop any pinned pattern. Clearing FillPatternName here prevents a stale pattern
            // from overriding a subsequent solid fill on the same state scope.
            case "g" when operands.Count >= 1:
                _state.FillR = _state.FillG = _state.FillB = Num(operands[0]);
                _state.FillPatternName = null;
                break;
            case "rg" when operands.Count >= 3:
                _state.FillR = Num(operands[0]);
                _state.FillG = Num(operands[1]);
                _state.FillB = Num(operands[2]);
                _state.FillPatternName = null;
                break;
            case "k" when operands.Count >= 4:
                CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                    out var fr, out var fg, out var fb);
                _state.FillR = fr; _state.FillG = fg; _state.FillB = fb;
                _state.FillPatternName = null;
                break;

            // Stroke color
            case "G" when operands.Count >= 1:
                _state.StrokeR = _state.StrokeG = _state.StrokeB = Num(operands[0]);
                _state.StrokePatternName = null;
                break;
            case "RG" when operands.Count >= 3:
                _state.StrokeR = Num(operands[0]);
                _state.StrokeG = Num(operands[1]);
                _state.StrokeB = Num(operands[2]);
                _state.StrokePatternName = null;
                break;
            case "K" when operands.Count >= 4:
                CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                    out var sr, out var sg, out var sb);
                _state.StrokeR = sr; _state.StrokeG = sg; _state.StrokeB = sb;
                _state.StrokePatternName = null;
                break;

            // Text object
            case "BT":
                _state.InTextObject = true;
                _state.SetTextMatrix(1, 0, 0, 1, 0, 0);
                break;
            case "ET":
                _state.InTextObject = false;
                break;

            // Text state
            case "Tf" when operands.Count >= 2:
                var fontName = (operands[0] as PdfName)?.Value;
                _state.FontName = fontName;
                _state.FontSize = Num(operands[1]);
                if (fontName is not null)
                {
                    currentFontKey = fontName;
                    if (fonts is not null && fonts.TryGetValue(fontName, out var fontDict))
                    {
                        currentToUnicode = Text.TextAbsorber.ParseToUnicodeFromDict(fontDict, _reader)
                            ?? BuildEncodingToUnicode(fontDict, _reader);
                        try { _currentMetrics = Text.FontMetrics.FromFontDict(fontDict, _reader); }
                        catch { _currentMetrics = null; }
                        try { _currentCidInfo = Text.CidFontInfo.TryBuild(fontDict, _reader); }
                        catch { _currentCidInfo = null; }
                    }
                }
                break;
            case "Tc" when operands.Count >= 1: _state.CharSpacing = Num(operands[0]); break;
            case "Tw" when operands.Count >= 1: _state.WordSpacing = Num(operands[0]); break;
            case "Tz" when operands.Count >= 1: _state.HorizontalScaling = Num(operands[0]); break;
            case "TL" when operands.Count >= 1: _state.Leading = Num(operands[0]); break;
            case "Tr" when operands.Count >= 1: _state.RenderingMode = Int(operands[0]); break;
            case "Ts" when operands.Count >= 1: _state.Rise = Num(operands[0]); break;

            // Text positioning
            case "Td" when operands.Count >= 2:
                _state.MoveTextPosition(Num(operands[0]), Num(operands[1]));
                break;
            case "TD" when operands.Count >= 2:
                _state.Leading = -Num(operands[1]);
                _state.MoveTextPosition(Num(operands[0]), Num(operands[1]));
                break;
            case "Tm" when operands.Count >= 6:
                _state.SetTextMatrix(Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3]),
                    Num(operands[4]), Num(operands[5]));
                break;
            case "T*":
                _state.MoveToNextLine();
                break;

            // Text showing
            case "Tj" when operands.Count >= 1 && operands[0] is PdfString s:
                FireTextShown(s.Value, currentToUnicode);
                break;
            case "TJ" when operands.Count >= 1 && operands[0] is PdfArray arr:
                foreach (var item in arr)
                {
                    if (item is PdfString ts)
                        FireTextShown(ts.Value, currentToUnicode);
                    else if (item is PdfInteger pi)
                    {
                        // Numeric adjustment: displaces text position by -value/1000 * fontSize
                        var adj = -pi.Value / 1000.0 * _state.FontSize * (_state.HorizontalScaling / 100.0);
                        _state.AdvanceTextPosition(adj, 0);
                    }
                    else if (item is PdfReal pr)
                    {
                        var adj = -pr.Value / 1000.0 * _state.FontSize * (_state.HorizontalScaling / 100.0);
                        _state.AdvanceTextPosition(adj, 0);
                    }
                }
                break;
            case "'" when operands.Count >= 1 && operands[0] is PdfString qs:
                _state.MoveToNextLine();
                FireTextShown(qs.Value, currentToUnicode);
                break;
            case "\"" when operands.Count >= 3 && operands[2] is PdfString dqs:
                _state.WordSpacing = Num(operands[0]);
                _state.CharSpacing = Num(operands[1]);
                _state.MoveToNextLine();
                FireTextShown(dqs.Value, currentToUnicode);
                break;

            // XObject (images, forms)
            case "Do" when operands.Count >= 1 && operands[0] is PdfName xName:
                OnImageDrawn?.Invoke(xName.Value, _state);
                break;

            // Path construction — accumulate segments
            case "m" when operands.Count >= 2:
                _pathSegments.Add(new PathCommand(PathOp.MoveTo, Num(operands[0]), Num(operands[1])));
                _subpathOpen = true;
                break;
            case "l" when operands.Count >= 2 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.LineTo, Num(operands[0]), Num(operands[1])));
                break;
            case "c" when operands.Count >= 6 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.CurveTo,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3]),
                    Num(operands[4]), Num(operands[5])));
                break;
            case "v" when operands.Count >= 4 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.CurveToV,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3])));
                break;
            case "y" when operands.Count >= 4 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.CurveToY,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3])));
                break;
            case "h":
                _pathSegments.Add(new PathCommand(PathOp.Close));
                break;
            case "re" when operands.Count >= 4:
                _pathSegments.Add(new PathCommand(PathOp.Rect,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3])));
                _subpathOpen = true;
                break;
            case "m" or "l" or "c" or "v" or "y" or "re":
                break; // insufficient operands — ignore

            // Path painting — a W/W* seen since the last `m` gets applied here, after
            // the paint runs (per §8.5.4.2: "the W and W* operators do not actually
            // change the current clipping path until after the painting operator").
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                OnPathPainted?.Invoke(op, _state, _pathSegments);
                if (_pendingClipEvenOdd is { } clipRule)
                {
                    OnPathClipped?.Invoke(clipRule, _state, _pathSegments);
                    _pendingClipEvenOdd = null;
                }
                _pathSegments.Clear();
                _subpathOpen = false;
                break;

            // Shading-paint operator — fills the current clipping region with the
            // named shading. No path is constructed; the shading is clipped by
            // whatever W/W* was installed in an enclosing q/Q frame.
            case "sh" when operands.Count >= 1 && operands[0] is PdfName shName:
                OnShadingPainted?.Invoke(shName.Value, _state);
                break;

            // Clipping — flag the current path; the intersection happens at the
            // next painting operator so the path's still available to hand over.
            case "W":
                _pendingClipEvenOdd = false;
                break;
            case "W*":
                _pendingClipEvenOdd = true;
                break;

            // Marked content
            case "BMC" when operands.Count >= 1 && operands[0] is PdfName bmcTag:
                OnMarkedContentBegin?.Invoke(bmcTag.Value, null);
                break;
            case "BDC" when operands.Count >= 2 && operands[0] is PdfName bdcTag:
                var bdcProps = operands[1] as PdfDictionary;
                // PDF 32000 §14.6.2: BDC's second operand can be an inline
                // properties dict *or* a name that resolves through the page
                // Resources./Properties entry. For /OC marked content the
                // emitter overwhelmingly uses the name form (`/OC /MC0 BDC`)
                // so without resolving it the renderer can't tell which OCG
                // a content range belongs to.
                if (bdcProps is null && operands[1] is PdfName bdcPropName)
                    bdcProps = _reader.ResolveDict(_properties?.Get(bdcPropName.Value));
                _state.MarkedContentTag = bdcTag.Value;
                // Check for ActualText
                if (bdcProps is not null)
                {
                    var actualText = bdcProps.Get("ActualText");
                    if (actualText is PdfString ats)
                        _state.ActualText = System.Text.Encoding.Latin1.GetString(ats.Value);
                }
                OnMarkedContentBegin?.Invoke(bdcTag.Value, bdcProps);
                break;
            case "EMC":
                _state.MarkedContentTag = null;
                _state.ActualText = null;
                OnMarkedContentEnd?.Invoke();
                break;
        }
    }

    private void ApplyExtGState(string name, Dictionary<string, PdfDictionary>? extGStates)
    {
        _state.ExtGStateName = name;

        if (extGStates is null || !extGStates.TryGetValue(name, out var gsDict))
            return;

        // Fill opacity (ca)
        var ca = gsDict.Get("ca");
        if (ca is PdfReal caR) _state.FillAlpha = caR.Value;
        else if (ca is PdfInteger caI) _state.FillAlpha = caI.Value;

        // Stroke opacity (CA)
        var sCA = gsDict.Get("CA");
        if (sCA is PdfReal scaR) _state.StrokeAlpha = scaR.Value;
        else if (sCA is PdfInteger scaI) _state.StrokeAlpha = scaI.Value;

        // Blend mode (BM)
        var bm = gsDict.Get("BM");
        if (bm is PdfName bmName) _state.BlendMode = bmName.Value;

        // Overprint (OP for stroke, op for fill)
        var opStroke = gsDict.Get("OP");
        if (opStroke is PdfBoolean opS) _state.OverprintStroke = opS.Value;

        var opFill = gsDict.Get("op");
        if (opFill is PdfBoolean opF) _state.OverprintFill = opF.Value;

        // Line width (LW)
        var lw = gsDict.Get("LW");
        if (lw is PdfReal lwR) _state.LineWidth = lwR.Value;
        else if (lw is PdfInteger lwI) _state.LineWidth = lwI.Value;

        // Line cap (LC)
        var lc = gsDict.Get("LC");
        if (lc is PdfInteger lcI) _state.LineCap = (int)lcI.Value;

        // Line join (LJ)
        var lj = gsDict.Get("LJ");
        if (lj is PdfInteger ljI) _state.LineJoin = (int)ljI.Value;

        // Miter limit (ML)
        var ml = gsDict.Get("ML");
        if (ml is PdfReal mlR) _state.MiterLimit = mlR.Value;
        else if (ml is PdfInteger mlI) _state.MiterLimit = mlI.Value;

        // Flatness (FL)
        var fl = gsDict.Get("FL");
        if (fl is PdfReal flR) _state.Flatness = flR.Value;
        else if (fl is PdfInteger flI) _state.Flatness = flI.Value;

        // Font (Font array: [fontRef size])
        var font = gsDict.Get("Font") as PdfArray;
        if (font is { Count: >= 2 })
        {
            if (font[1] is PdfReal fSize) _state.FontSize = fSize.Value;
            else if (font[1] is PdfInteger fSizeI) _state.FontSize = fSizeI.Value;
        }

        // Soft mask (/SMask). Per PDF 32000 §11.6.5.4 this is either the name
        // /None (clear the mask) or a soft-mask dictionary {/Type /Mask, /S, /G,
        // /BC?, /TR?}. The mask group is rendered in the CTM that's active when
        // gs runs, NOT at paint-time, so we snapshot Ctm here.
        var smaskObj = gsDict.Get("SMask");
        if (smaskObj is PdfName smaskName && smaskName.Value == "None")
        {
            _state.SoftMask = null;
        }
        else
        {
            var smaskDict = _reader.ResolveDict(smaskObj);
            if (smaskDict is not null && smaskDict.GetName("Type") is null or "Mask")
            {
                _state.SoftMask = new SoftMaskInfo
                {
                    Dict = smaskDict,
                    Subtype = smaskDict.GetName("S") ?? "Luminosity",
                    Ctm = (double[])_state.Ctm.Clone(),
                };
            }
        }
    }

    /// <summary>
    /// Dispatch <c>scn</c>/<c>SCN</c> to pattern or solid-colour handling based on the
    /// current colour space. Pattern operands end in a <see cref="PdfName"/>; solid
    /// colours use 1/3/4 numerics (gray/RGB/CMYK).
    /// </summary>
    private void ApplyPatternOrColor(List<PdfObject> operands, bool isFill)
    {
        var cs = isFill ? _state.FillColorSpace : _state.StrokeColorSpace;
        if ((cs == "Pattern" || (cs is not null && _patternColorSpaces.Contains(cs)))
            && operands.Count >= 1 && operands[^1] is PdfName patName)
        {
            if (isFill) _state.FillPatternName = patName.Value;
            else _state.StrokePatternName = patName.Value;
            return;
        }
        // A bare /Pattern colour space: only route to the pattern renderer when the
        // named pattern is a shading pattern (PatternType 2). Coloured tiling cells
        // FOSS cannot rasterise fall through to the solid-fill path below, preserving
        // the last solid colour rather than rendering blank.
        if (cs is not null && _barePatternColorSpaces.Contains(cs)
            && operands.Count >= 1 && operands[^1] is PdfName barePat
            && IsShadingPattern(barePat.Value))
        {
            if (isFill) _state.FillPatternName = barePat.Value;
            else _state.StrokePatternName = barePat.Value;
            return;
        }
        // Clear any lingering pattern so switching from pattern fill to solid doesn't
        // leave the old pattern name overriding subsequent rgb/g/k operators.
        if (isFill) _state.FillPatternName = null;
        else _state.StrokePatternName = null;

        // /Separation and /DeviceN colorspaces: the scn operands are tint values
        // that the colorspace's tint transform function turns into colour
        // components in the alternate space. Without this, `1 scn` on a
        // /Separation /PANTONE 1805 C space defaults to gray=1.0 (white) and any
        // orange text drawn that way renders invisible against a white background.
        if (cs is not null && _tintColorSpaces.TryGetValue(cs, out var tintInfo))
        {
            var inputs = new double[operands.Count];
            for (var i = 0; i < operands.Count; i++) inputs[i] = Num(operands[i]);
            var altComponents = tintInfo.tint.Evaluate(inputs);
            if (altComponents is null) return;
            ApplyAltSpaceComponents(altComponents, tintInfo.altSpace, isFill);
            return;
        }

        ApplyColorOperands(operands, isFill);
    }

    // True when the named pattern resolves to a shading pattern (PatternType 2),
    // which FOSS rasterises. Tiling patterns (PatternType 1) return false so a
    // bare-pattern fill keeps its solid-colour approximation.
    private bool IsShadingPattern(string patternName)
    {
        if (_patterns is null) return false;
        var pat = _reader.Resolve(_patterns.Get(patternName));
        var dict = pat switch
        {
            PdfStream s => s.Dict,
            PdfDictionary d => d,
            _ => null,
        };
        return dict is not null && (int)dict.GetInt("PatternType") == 2;
    }

    // Map alternate-space output (from a /Separation or /DeviceN tint function)
    // to the renderer's RGB graphics-state slots. /DeviceCMYK and /DeviceRGB
    // are the alternate spaces almost every Pantone spec uses;
    // /DeviceGray covers the few one-component cases. ICCBased alternates
    // fall through to whatever component count the caller passes — best-
    // effort, since we don't run the ICC profile.
    private void ApplyAltSpaceComponents(double[] comp, string altSpace, bool isFill)
    {
        double r, g, b;
        switch (altSpace)
        {
            case "DeviceCMYK" when comp.Length >= 4:
                CmykToRgb(comp[0], comp[1], comp[2], comp[3], out r, out g, out b);
                break;
            case "DeviceRGB" when comp.Length >= 3:
                r = comp[0]; g = comp[1]; b = comp[2];
                break;
            case "DeviceGray" when comp.Length >= 1:
                r = g = b = comp[0];
                break;
            case "Lab" when comp.Length >= 3:
                LabColor.ToRgb(comp[0], comp[1], comp[2], out r, out g, out b);
                break;
            default:
                // Unknown / unsupported alternate: pick whichever interpretation
                // matches the component count so we at least pass something
                // through the pipeline instead of silently dropping the colour.
                if (comp.Length >= 4) { CmykToRgb(comp[0], comp[1], comp[2], comp[3], out r, out g, out b); }
                else if (comp.Length >= 3) { r = comp[0]; g = comp[1]; b = comp[2]; }
                else if (comp.Length >= 1) { r = g = b = comp[0]; }
                else return;
                break;
        }
        if (isFill) { _state.FillR = r; _state.FillG = g; _state.FillB = b; }
        else { _state.StrokeR = r; _state.StrokeG = g; _state.StrokeB = b; }
    }

    // Resolve the alternate colorspace family name from a /Separation or
    // /DeviceN array's third entry: either a direct name (/DeviceCMYK,
    // /DeviceRGB, /DeviceGray, /CalGray, /CalRGB) or an array whose first
    // element is the family name (/ICCBased, /Lab). Returns null when the
    // family is unrecognised, in which case the colorspace is skipped.
    private string? ResolveAltSpaceName(PdfObject? obj)
    {
        var resolved = _reader.Resolve(obj);
        if (resolved is PdfName n)
        {
            if (n.Value == "DeviceCMYK" || n.Value == "DeviceRGB" || n.Value == "DeviceGray")
                return n.Value;
            // CalGray → 1 component; treat as DeviceGray for our renderer.
            if (n.Value == "CalGray") return "DeviceGray";
            if (n.Value == "CalRGB") return "DeviceRGB";
            return null;
        }
        if (resolved is PdfArray a && a.Count > 0 && a[0] is PdfName fam)
        {
            // /ICCBased [/ICCBased <stream>] — the stream's /N entry gives the
            // component count, but we don't run ICC profiles; fall back to
            // CMYK if N=4, RGB if N=3, Gray if N=1.
            if (fam.Value == "ICCBased" && a.Count > 1 && _reader.ResolveStream(a[1]) is { } iccStream)
            {
                var nObj = iccStream.Dict.Get("N");
                var iccN = nObj switch
                {
                    PdfInteger pi => (int)pi.Value,
                    PdfReal pr => (int)pr.Value,
                    _ => 0,
                };
                return iccN switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
            }
            if (fam.Value == "CalGray") return "DeviceGray";
            if (fam.Value == "CalRGB") return "DeviceRGB";
            if (fam.Value == "Lab") return "Lab";
            return null;
        }
        return null;
    }

    private void ApplyColorOperands(List<PdfObject> operands, bool isFill)
    {
        // Map numeric operands to RGB based on operand count:
        // 1 operand = gray, 3 = RGB, 4 = CMYK
        if (operands.Count >= 4)
        {
            CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                out var cr, out var cg, out var cb);
            if (isFill) { _state.FillR = cr; _state.FillG = cg; _state.FillB = cb; }
            else { _state.StrokeR = cr; _state.StrokeG = cg; _state.StrokeB = cb; }
        }
        else if (operands.Count >= 3)
        {
            var r = Num(operands[0]); var g = Num(operands[1]); var b = Num(operands[2]);
            if (isFill) { _state.FillR = r; _state.FillG = g; _state.FillB = b; }
            else { _state.StrokeR = r; _state.StrokeG = g; _state.StrokeB = b; }
        }
        else if (operands.Count >= 1 && operands[0] is not PdfName)
        {
            var gray = Num(operands[0]);
            if (isFill) { _state.FillR = _state.FillG = _state.FillB = gray; }
            else { _state.StrokeR = _state.StrokeG = _state.StrokeB = gray; }
        }
    }

    private Text.FontMetrics? _currentMetrics;
    private Text.CidFontInfo? _currentCidInfo;

    private void FireTextShown(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        var text = DecodeBytes(bytes, toUnicode);
        OnTextShown?.Invoke(text, bytes, _state);

        // Advance the text matrix by the total string displacement.
        // Per PDF spec §9.4.4: tx = ((w0 - Tj/1000) * Tf + Tc + Tw) for each character.
        // For CID fonts bytes pair into 2-byte codes, and widths are CID-keyed — walking the
        // decoded Unicode string would use wrong width entries and miscount character boundaries.
        var fontSize = _state.FontSize;
        var charSpacing = _state.CharSpacing;
        var wordSpacing = _state.WordSpacing;
        var hScaling = _state.HorizontalScaling / 100.0;
        double totalWidth = 0;

        if (_currentCidInfo is not null && _currentCidInfo.LegacyCodepage != 0)
        {
            // Non-embedded predefined national CMap (GBK-EUC-H, …). The /W table is
            // keyed by the Adobe CID we never resolve (so GetWidth returns /DW, often
            // 500), but the renderer draws these full-width; advancing the cursor by
            // 500 would compress every CJK line to half width. Walk the mixed-width
            // run and use nominal full-width (1000) / half-width (500) — matching what
            // DrawLegacyCjkText advances by — so glyphs and cursor stay in lockstep.
            // Vertical writing-mode (-V) advances down the page, one em per full-width
            // glyph, and isn't affected by horizontal scaling.
            var vert = _currentCidInfo.IsVertical;
            var hs = vert ? 1.0 : hScaling;
            var i = 0;
            while (i < bytes.Length)
            {
                var step = _currentCidInfo.LegacyByteLength(bytes[i]);
                if (step == 2 && i + 1 >= bytes.Length) step = 1;
                var code = step == 2 ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
                var w = Text.CjkFallbackFont.AdvanceEm(_currentCidInfo, _currentMetrics, code, step);
                totalWidth += (w / 1000.0 * fontSize + charSpacing) * hs;
                if (!vert && step == 1 && bytes[i] == 32)
                    totalWidth += wordSpacing * hScaling;
                i += step;
            }
        }
        else if (_currentCidInfo is not null && _currentCidInfo.IsTwoByteEncoding)
        {
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var cid = (bytes[i] << 8) | bytes[i + 1];
                var w = _currentMetrics?.GetWidth(cid) ?? 1000;
                totalWidth += (w / 1000.0 * fontSize + charSpacing) * hScaling;
                // Word spacing: PDF spec §9.3.3 — applies to the single-byte code 32 only, which
                // in a 2-byte CID stream means both bytes of the code are 0x00 0x20 (CID 32).
                if (cid == 32)
                    totalWidth += wordSpacing * hScaling;
            }
        }
        else
        {
            // When bytes are 1:1 with decoded text (typical for simple TT fonts), the
            // PDF font dict's /Widths is keyed by the raw byte values — that's how the
            // bytes were paired with widths at embed time. Subset TT fonts with /ToUnicode
            // map byte X to char Y but /Widths still uses X as the key, so a Unicode-char
            // lookup falls through to the MissingWidth/default and the cursor advances
            // wrong (visible as huge letter-spacing on subset-font text). Use byte-keyed
            // lookup when we have a 1:1 mapping; the existing Unicode path remains for
            // multi-byte expansions (rare for simple TT but legal per /ToUnicode spec).
            bool oneToOne = bytes.Length == text.Length;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                int w;
                if (oneToOne)
                    w = _currentMetrics?.GetWidth(bytes[i]) ?? 0;
                else
                    w = _currentMetrics?.GetWidth(ch) ?? 0;
                if (w == 0) w = _currentMetrics?.GetWidth(ch) ?? 500;
                totalWidth += (w / 1000.0 * fontSize + charSpacing) * hScaling;
                if (ch == ' ')
                    totalWidth += wordSpacing * hScaling;
            }
        }

        if (_currentCidInfo is not null && _currentCidInfo.IsVertical)
            _state.AdvanceTextPosition(0, -totalWidth);
        else
            _state.AdvanceTextPosition(totalWidth, 0);
    }

    /// <summary>
    /// Build a code→Unicode map from a simple font's /Encoding (base + /Differences) when
    /// it carries no /ToUnicode. Subset TrueType fonts often number glyphs 1,2,3… and map
    /// them to names via /Differences; without this the bytes decode to control chars
    /// (U+0001…) and the renderer's Unicode-keyed cmap fallback can't find the glyph.
    /// </summary>
    private static Dictionary<int, string>? BuildEncodingToUnicode(PdfDictionary fontDict, IO.PdfReader reader)
    {
        // Only simple fonts carry a byte→name /Encoding; CID fonts use CMaps.
        var enc = reader.Resolve(fontDict.Get("Encoding"));
        if (enc is not PdfDictionary && enc is not PdfName) return null;
        var names = Devices.SoftwarePageRenderer.ResolveEncoding(fontDict, reader);
        var map = new Dictionary<int, string>();
        for (var code = 0; code < 256; code++)
        {
            var name = names[code];
            if (name is null || name == ".notdef") continue;
            var uni = Text.TextAbsorber.ResolveGlyphName(name);
            if (!string.IsNullOrEmpty(uni)) map[code] = uni;
        }
        return map.Count > 0 ? map : null;
    }

    private static string DecodeBytes(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        if (toUnicode is not null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in bytes)
            {
                if (toUnicode.TryGetValue(b, out var mapped))
                    sb.Append(mapped);
                else
                    sb.Append((char)b);
            }
            return sb.ToString();
        }
        return System.Text.Encoding.Latin1.GetString(bytes);
    }

    private static void CmykToRgb(double c, double m, double y, double k,
        out double r, out double g, out double b)
    {
        r = (1 - c) * (1 - k);
        g = (1 - m) * (1 - k);
        b = (1 - y) * (1 - k);
    }

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

/// <summary>Path construction operator type.</summary>
public enum PathOp
{
    MoveTo, LineTo, CurveTo, CurveToV, CurveToY, Close, Rect,
}

/// <summary>A single path construction command with coordinates.</summary>
public readonly struct PathCommand
{
    public PathOp Op { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double X3 { get; }
    public double Y3 { get; }

    public PathCommand(PathOp op, double x1 = 0, double y1 = 0,
        double x2 = 0, double y2 = 0, double x3 = 0, double y3 = 0)
    {
        Op = op; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; X3 = x3; Y3 = y3;
    }
}
