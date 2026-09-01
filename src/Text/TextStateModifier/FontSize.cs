using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>
    /// Change the font size of the Tf operator that immediately precedes the first
    /// occurrence of <paramref name="text"/> in the page's content stream(s).
    /// </summary>
    public void ModifyFontSize(Page page, string text, double oldSize, double newSize, bool allowCollateral = true)
    {
        var reader = page.Reader;
        if (reader is null) return;

        // First try Form XObjects (text is often inside XObjects, not page content directly)
        if (ModifyInFormXObjects(page.Dict, reader, text, oldSize, newSize, allowCollateral))
            return;

        // Then try the page's own content stream
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;

        var combined = CombineStreams(contentStreams);
        var modified = ModifyFontSizeInStream(combined, text, oldSize, newSize, page.Dict, reader, allowCollateral);
        if (modified is not null)
        {
            page.SetContentStream(modified);
        }
    }

    private bool ModifyInFormXObjects(PdfDictionary dict, PdfReader reader,
        string text, double oldSize, double newSize, bool allowCollateral)
    {
        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return false;
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return false;

        foreach (var key in xobjects.Keys)
        {
            var xobjStream = reader.ResolveStream(xobjects.Get(key));
            if (xobjStream is null) continue;
            if (xobjStream.Dict.GetName("Subtype") != "Form") continue;

            var streamData = reader.DecodeStream(xobjStream);
            var modified = ModifyFontSizeInStream(streamData, text, oldSize, newSize, xobjStream.Dict, reader, allowCollateral);
            if (modified is not null)
            {
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            // Recurse into nested Form XObjects
            if (ModifyInFormXObjects(xobjStream.Dict, reader, text, oldSize, newSize, allowCollateral))
                return true;
        }
        return false;
    }

    private byte[]? ModifyFontSizeInStream(byte[] streamBytes, string text, double oldSize,
        double newSize, PdfDictionary pageDict, PdfReader reader, bool allowCollateral)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();

        // Track the position of the most recent Tf operator
        int lastTfSizeStart = -1;
        int lastTfSizeEnd = -1;
        double lastTfSize = 0;
        double tmScaleY = 1; // text matrix vertical scale factor
        // The CTM scale in force, tracked through q/Q/cm. A producer that lays a page out in
        // its own space — `0.8625 0 0 -0.8625 50 700 cm`, then `16 Tf` — draws text at an
        // EFFECTIVE 13.8 pt, which is the size the absorber reports and therefore the size a
        // caller passes as oldSize. Reading the raw 16 off the Tf made every such run fail the
        // oldSize test, so the resize silently did nothing. The raw value written back is
        // recovered through the same combined scale below, so it stays in the page's space.
        double ctmScale = 1;
        var ctmStack = new Stack<double>();
        string? currentFontName = null;
        Dictionary<int, string>? currentToUnicode = null;
        // Every text show with the Tf that governs it. A fragment's phrase is
        // often split over several consecutive shows, each re-issuing its own
        // Tf (accented glyphs, kerned words), so the match must run over the
        // concatenated show text and then patch EVERY Tf covering the match.
        var shows = new List<(string decoded, int tfStart, int tfEnd, double effSize,
            int showStart, int showEnd, string? fontRes)>();

        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            var endPos = (int)lexer.Position;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                    break;
                case TokenKind.Real:
                    operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                    break;
                case TokenKind.LiteralString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                    break;
                case TokenKind.HexString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                    break;
                case TokenKind.Name:
                    operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                    break;
                case TokenKind.ArrayStart:
                {
                    // Collect array elements (for TJ operator)
                    var arrTexts = new StringBuilder();
                    int arrStringCount = 0;
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                        {
                            var strBytes = t.BytesValue;
                            if (strBytes is not null)
                            {
                                arrTexts.Append(DecodeTextString(strBytes, currentToUnicode));
                                arrStringCount++;
                            }
                        }
                    }
                    // Store the concatenated text from the array as an operand
                    operands.Add((TokenKind.ArrayStart, new PdfString(
                        Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)lexer.Position));
                    break;
                }
                case TokenKind.DictStart:
                {
                    int depth = 1;
                    while (depth > 0)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.DictStart) depth++;
                        if (t.Kind == TokenKind.DictEnd) depth--;
                    }
                    operands.Clear();
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "Tf":
                            if (operands.Count >= 2)
                            {
                                if (operands[0].obj is PdfName fn)
                                {
                                    currentFontName = fn.Value;
                                    if (fonts.TryGetValue(currentFontName, out var fontDict))
                                        currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                    else
                                        currentToUnicode = null;
                                }
                                // Record position of the size operand
                                lastTfSizeStart = operands[1].startPos;
                                lastTfSizeEnd = operands[1].endPos;
                                if (operands[1].obj is PdfInteger pi)
                                    lastTfSize = pi.Value;
                                else if (operands[1].obj is PdfReal pr)
                                    lastTfSize = pr.Value;
                            }
                            break;

                        case "Tm":
                            // Tm: a b c d e f — text matrix; effective font size = Tf_size * sqrt(c² + d²)
                            if (operands.Count >= 6)
                            {
                                double c = 0, d = 0;
                                if (operands[2].obj is PdfReal cr2) c = cr2.Value;
                                else if (operands[2].obj is PdfInteger ci2) c = ci2.Value;
                                if (operands[3].obj is PdfReal dr2) d = dr2.Value;
                                else if (operands[3].obj is PdfInteger di2) d = di2.Value;
                                tmScaleY = Math.Sqrt(c * c + d * d);
                                if (tmScaleY < 0.001) tmScaleY = 1;
                            }
                            break;

                        case "q":
                            ctmStack.Push(ctmScale);
                            break;

                        case "Q":
                            if (ctmStack.Count > 0) ctmScale = ctmStack.Pop();
                            break;

                        case "cm":
                            if (operands.Count >= 6)
                            {
                                static double Num((TokenKind kind, PdfObject obj, int startPos, int endPos) o)
                                    => o.obj is PdfReal r ? r.Value : o.obj is PdfInteger i ? i.Value : 0;
                                var det = Math.Abs(Num(operands[0]) * Num(operands[3])
                                    - Num(operands[1]) * Num(operands[2]));
                                if (det > 1e-9) ctmScale *= Math.Sqrt(det);
                            }
                            break;

                        case "Tj":
                        case "'":
                        case "\"":
                            if (operands.Count >= 1 && operands[^1].obj is PdfString textStr)
                            {
                                var decoded = DecodeTextString(textStr.Value, currentToUnicode);
                                if (decoded.Length > 0 && lastTfSizeStart >= 0)
                                    shows.Add((decoded, lastTfSizeStart, lastTfSizeEnd,
                                        lastTfSize * tmScaleY * ctmScale,
                                        operands[^1].startPos, endPos, currentFontName));
                            }
                            break;

                        case "TJ":
                            // TJ array: text was decoded during array parsing
                            if (operands.Count >= 1 && operands[^1].obj is PdfString tjText)
                            {
                                var decoded = DecodeTextString(tjText.Value, currentToUnicode);
                                if (decoded.Length > 0 && lastTfSizeStart >= 0)
                                    shows.Add((decoded, lastTfSizeStart, lastTfSizeEnd,
                                        lastTfSize * tmScaleY * ctmScale,
                                        operands[^1].startPos, endPos, currentFontName));
                            }
                            break;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
        done:
        // Match the phrase over the concatenated show text, then patch every
        // Tf site (with the expected old size) that governs a show overlapping
        // the first match. Single-show matches reduce to one patch; phrases
        // split across shows/Tf re-issues patch each covering Tf once.
        if (shows.Count == 0) return null;
        var concat = new StringBuilder();
        var spans = new (int start, int end)[shows.Count];
        for (var si = 0; si < shows.Count; si++)
        {
            spans[si] = (concat.Length, concat.Length + shows[si].decoded.Length);
            concat.Append(shows[si].decoded);
        }
        // Walk occurrences until one is drawn at the expected old size — the
        // same text can appear elsewhere at other sizes (the caller resizes a
        // specific absorbed fragment, identified by its size).
        var concatStr = concat.ToString();

        var patches = new SortedDictionary<int, (int end, double newTf)>();
        for (var idx = concatStr.IndexOf(text, StringComparison.Ordinal); idx >= 0;
             idx = concatStr.IndexOf(text, idx + 1, StringComparison.Ordinal))
        {
            var matchEnd = idx + text.Length;
            var sizeMatched = false;
            var collateral = false;
            for (var si = 0; si < shows.Count; si++)
            {
                if (spans[si].end <= idx || spans[si].start >= matchEnd) continue;
                var s = shows[si];
                if (Math.Abs(s.effSize - oldSize) >= 0.5) continue;
                sizeMatched = true;
                // A Tf may be shared with shows OUTSIDE the match (e.g. the
                // whole paragraph under one Tf): patching it would resize
                // unrelated text. Resize only when every show governed by the
                // candidate Tf lies inside the match — otherwise the scoped
                // insertion below gives the match its own Tf instead.
                for (var sj = 0; allowCollateral == false && sj < shows.Count; sj++)
                {
                    if (shows[sj].tfStart != s.tfStart) continue;
                    if (spans[sj].start < idx || spans[sj].end > matchEnd) { collateral = true; break; }
                }
                if (collateral) break;
                // newSize is the desired effective size; recover the raw Tf value
                // through the same Tm scale that produced this show's effective size.
                var tmScale = s.effSize / Math.Max(0.0001, RawTfFor(streamBytes, s));
                patches[s.tfStart] = (s.tfEnd, newSize / Math.Max(0.0001, tmScale));
            }
            if (sizeMatched && !collateral && patches.Count > 0) break;
            patches.Clear();
            // The covering Tf also governs text outside the match, so it cannot be
            // rewritten in place. Give the match its OWN Tf instead: open the new size
            // just before its first show and restore the old one just after its last.
            // That is the expected shape — a resized replacement carries
            // its own Tf rather than resizing the line it sits on. Only for a run of
            // shows that lies WHOLLY inside the match and is contiguous in the stream,
            // so nothing outside the match can fall inside the new scope.
            if (sizeMatched && collateral
                && ScopedTfInsertion(streamBytes, shows, spans, idx, matchEnd, oldSize, newSize)
                    is { } scoped)
                return scoped;
        }
        if (patches.Count == 0) return null;

        var result = streamBytes;
        foreach (var kv in patches.Reverse())
            result = PatchFontSize(result, kv.Key, kv.Value.end, kv.Value.newTf);
        return result;
    }

    /// <summary>Wrap the shows covering a match in their OWN <c>Tf</c>, leaving the Tf that
    /// governs the rest of the line alone: <c>/F newSize Tf</c> before the first covered show
    /// and <c>/F oldSize Tf</c> after the last. Returns null (leave the stream untouched) when
    /// the covered shows do not form one contiguous run wholly inside the match, when they do
    /// not share one Tf, or when the font resource name is unknown — in any of those cases a
    /// scope would reach text the caller did not name.</summary>
    private static byte[]? ScopedTfInsertion(byte[] streamBytes,
        List<(string decoded, int tfStart, int tfEnd, double effSize, int showStart, int showEnd, string? fontRes)> shows,
        (int start, int end)[] spans, int matchStart, int matchEnd, double oldSize, double newSize)
    {
        int first = -1, last = -1;
        for (var si = 0; si < shows.Count; si++)
        {
            if (spans[si].end <= matchStart || spans[si].start >= matchEnd) continue;
            if (spans[si].start < matchStart || spans[si].end > matchEnd) return null; // partial show
            if (Math.Abs(shows[si].effSize - oldSize) >= 0.5) return null;
            if (first < 0) first = si;
            last = si;
        }
        if (first < 0 || last < first) return null;
        for (var si = first; si <= last; si++)
        {
            if (spans[si].start < matchStart || spans[si].end > matchEnd) return null; // interleaved
            if (shows[si].tfStart != shows[first].tfStart) return null;                // mixed Tf
        }
        var res = shows[first].fontRes;
        if (string.IsNullOrEmpty(res)) return null;
        var rawOld = RawTfFor(streamBytes, shows[first]);
        var scale = shows[first].effSize / Math.Max(0.0001, rawOld);
        var rawNew = newSize / Math.Max(0.0001, scale);
        var open = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
            " /{0} {1:0.####} Tf ", res, rawNew));
        var close = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
            " /{0} {1:0.####} Tf ", res, rawOld));
        var at1 = shows[first].showStart;
        var at2 = shows[last].showEnd;
        if (at1 < 0 || at2 > streamBytes.Length || at2 < at1) return null;
        var result = new byte[streamBytes.Length + open.Length + close.Length];
        var w = 0;
        Array.Copy(streamBytes, 0, result, w, at1); w += at1;
        open.CopyTo(result, w); w += open.Length;
        Array.Copy(streamBytes, at1, result, w, at2 - at1); w += at2 - at1;
        close.CopyTo(result, w); w += close.Length;
        Array.Copy(streamBytes, at2, result, w, streamBytes.Length - at2);
        return result;
    }

    /// <summary>Parse the raw numeric Tf size at the recorded operand span.</summary>
    private static double RawTfFor(byte[] streamBytes,
        (string decoded, int tfStart, int tfEnd, double effSize, int showStart, int showEnd, string? fontRes) show)
    {
        var s = Encoding.ASCII.GetString(streamBytes, show.tfStart,
            Math.Max(0, show.tfEnd - show.tfStart)).Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v != 0 ? v : show.effSize;
    }

    private static byte[] PatchFontSize(byte[] original, int sizeStart, int sizeEnd, double newSize)
    {
        // Skip leading whitespace in the size range to preserve it
        while (sizeStart < sizeEnd && (original[sizeStart] == ' ' || original[sizeStart] == '\t'
            || original[sizeStart] == '\r' || original[sizeStart] == '\n'))
            sizeStart++;

        // Use enough precision so size * tmScale rounds back to the intended value
        string sizeStr;
        if (newSize == Math.Floor(newSize))
            sizeStr = ((int)newSize).ToString(CultureInfo.InvariantCulture);
        else
            sizeStr = newSize.ToString("R", CultureInfo.InvariantCulture); // round-trip format
        var sizeBytes = Encoding.ASCII.GetBytes(sizeStr);

        var result = new byte[original.Length - (sizeEnd - sizeStart) + sizeBytes.Length];
        Array.Copy(original, 0, result, 0, sizeStart);
        Array.Copy(sizeBytes, 0, result, sizeStart, sizeBytes.Length);
        Array.Copy(original, sizeEnd, result, sizeStart + sizeBytes.Length, original.Length - sizeEnd);
        return result;
    }

    /// <summary>
    /// Rewrite the page (or a Form XObject) content so the text run matching
    /// <paramref name="text"/> is shown with <paramref name="newFont"/>: a subset
    /// of the new font is embedded into the document and the run's active Tf
    /// operator is repointed at the freshly registered resource. Mirrors the
    /// match-by-decoded-text approach used by ModifyFontSize / ModifyForegroundColor.
    /// </summary>
    /// <param name="segmentScoped">The caller is restyling ONE SEGMENT of a run, so
    /// only those glyphs change font and the run is split around them. A
    /// fragment-scoped change restyles the whole matched run by repointing its Tf,
    /// which leaves the show operators intact for a text replacement that follows.</param>
    /// <summary>Geometry for the overflow re-lay a fragment-level font assignment can
    /// request (see <see cref="TextState.Font"/>): the line's baseline drops one
    /// re-flow band and the tail runs re-seat at the match x plus one source-face
    /// space, each keeping its own face. Probed on a
    /// replaced run whose assigned face cannot fit the sheet (the overflow family: source
    /// baseline 364.27 → 346.71 at fs 15.96 = 1.10 em; the ': ' and CID tail runs
    /// both re-seat at 66.96 + 3.99, one source-face space right of the match).
    /// The match run's own seat anchors the edit — it is read off the run's
    /// preceding Tm, so no caller-side position is involved.</summary>
    internal readonly record struct OverflowRelay(double SourceSpaceW, double Drop);

    private static readonly System.Text.RegularExpressions.Regex SimpleTm =
        new(@"1 0 0 1 (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Tm");

    /// <summary>Re-seat the source line after a font assignment left its replaced run
    /// wider than the sheet: the matched run (seated at the anchor) keeps its x and
    /// drops by <see cref="OverflowRelay.Drop"/>; every later run on the same baseline
    /// re-seats one source-face space right of the match on the dropped baseline,
    /// keeping its own face. Only simple `1 0 0 1 x y Tm` seats are touched — a line
    /// positioned any other way is left exactly where it was.</summary>
    private static byte[] RelayOverflowLine(byte[] content, double matchX, double baselineY,
        OverflowRelay r)
    {
        var s = Encoding.Latin1.GetString(content);
        var newY = baselineY - r.Drop;
        var tailX = matchX + r.SourceSpaceW;
        static string Fmt(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
        var patched = SimpleTm.Replace(s, m =>
        {
            var x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (System.Math.Abs(y - baselineY) > 0.02) return m.Value;
            if (System.Math.Abs(x - matchX) <= 0.02)
                return "1 0 0 1 " + Fmt(x) + " " + Fmt(newY) + " Tm";
            if (x > matchX)
                return "1 0 0 1 " + Fmt(tailX) + " " + Fmt(newY) + " Tm";
            return m.Value;
        });
        return Encoding.Latin1.GetBytes(patched);
    }
}
