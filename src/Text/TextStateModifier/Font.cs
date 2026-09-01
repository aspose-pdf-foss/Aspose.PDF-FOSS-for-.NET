using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    public void ModifyFont(Page page, string text, Font newFont, double? targetY = null,
        bool segmentScoped = false, OverflowRelay? relay = null)
    {
        var reader = page.Reader;
        if (reader is null) return;
        var doc = reader.OwnerDocument;
        if (doc is null || newFont is null) return;

        // A Standard-14 font is referenced by name only (no embedded program); any
        // other real font needs a TrueType program to embed. Bail only when we have
        // neither.
        var isCore = Standard14Fonts.IsCoreName(newFont.BaseFont)
            || Standard14Fonts.IsCoreName(newFont.FontName);
        var ttf = newFont.SourceFontData?.TtfData;
        if (!isCore && (ttf is null || ttf.Length == 0)) return;

        // Standard-14 base names are written verbatim (e.g. "Times-Roman"); embedded
        // fonts drop spaces/style separators from their /BaseFont name.
        var baseName = isCore
            ? (newFont.FontName ?? "Helvetica")
            : (newFont.FontName ?? "Font").Replace(" ", "").Replace("-", "");

        // The replacement font's resource key is deterministic, so a run already showing
        // in it can be recognised before the resource is (re-)registered.
        var targetRes = "AsRp" + SanitizeResName(baseName);

        var contentStreams = GetContentStreams(page, reader);
        var combined = contentStreams.Count > 0 ? CombineStreams(contentStreams) : [];

        // Look for the fragment's OWN run — one whose text is exactly the fragment's —
        // across both scopes before settling for a run that merely contains the text.
        // Text frequently lives inside a Form XObject (e.g. `q /Fm0 Do Q`), and a
        // one-character fragment matches a substring of half the runs on the page, so
        // searching the forms first and taking any hit restyles the wrong run and leaves
        // the fragment's own showing the original font.
        var range = FindTfNameRange(combined, text, page.Dict, reader, targetRes, exactOnly: true);
        if (range is null)
        {
            if (ModifyFontInFormXObjects(page.Dict, reader, doc, text, isCore, ttf, baseName,
                    newFont, segmentScoped, exactOnly: true))
                return;
            range = FindTfNameRange(combined, text, page.Dict, reader, targetRes, exactOnly: false);
        }
        if (range is null)
        {
            if (ModifyFontInFormXObjects(page.Dict, reader, doc, text, isCore, ttf, baseName,
                    newFont, segmentScoped, exactOnly: false))
                return;
            // The fragment's glyphs may be spread over several show operators.
            if (FindSpannedTfSpans(combined, text, page.Dict, reader, targetRes) is { Count: > 0 } spans)
            {
                var resNm = RegisterFontResource(page.Dict, reader, doc, isCore, ttf, baseName, newFont);
                page.SetContentStream(PatchNames(combined, spans, resNm));
            }
            return;
        }

        var site = range.Value;
        var origName = ExtractResName(combined, site.NameStart, site.NameEnd);
        if (site.Composite)
        {
            if (ReencodeCompositeRun(combined, site, page.Dict, reader, doc,
                    isCore, ttf, baseName, newFont) is { } recoded)
                page.SetContentStream(recoded);
            return;
        }
        var resName = RegisterFontResource(page.Dict, reader, doc, isCore, ttf, baseName, newFont);
        // A match inside a longer run switches font for those glyphs only. ⚠ Segment-scoped
        // ONLY: a FRAGMENT-level assignment repoints the whole covering Tf — the
        // same distinction the size path draws, and the corpus pins it both ways.
        if (segmentScoped && site.LitStart >= 0 && origName is not null
            && SplitFontRun(combined, site.LitStart, site.LitEnd, text, origName, resName, site.Size) is { } split)
        {
            page.SetContentStream(split);
            return;
        }
        // Same rule for a run drawn as a TJ array: the phrase there is spread over several
        // kerned pieces, so the literal splitter above cannot see it and the run was being
        // re-faced whole even for a segment-scoped change.
        if (segmentScoped && site.LitStart < 0 && origName is not null && site.ShowStart >= 0
            && SplitShowRunTJ(combined, site.ShowStart, site.ShowEnd, text,
                   TfOps(resName, site.Size), TfOps(origName, site.Size), null) is { } tjSplit)
        {
            page.SetContentStream(tjSplit);
            return;
        }
        // Read the matched run's seat off the PRE-patch bytes (offsets are still
        // valid there); the numeric anchor survives the name patches below.
        (double x, double y)? relayAnchor = null;
        if (relay is not null && site.ShowStart >= 0)
            relayAnchor = FindSeatBefore(combined, site.ShowStart);
        var modified = PatchName(combined, site.NameStart, site.NameEnd, resName);
        if (origName is not null)
            modified = RepointRedundantTfs(modified, origName, resName);
        if (relay is { } r && relayAnchor is { } anchor)
            modified = RelayOverflowLine(modified, anchor.x, anchor.y, r);
        page.SetContentStream(modified);
    }

    private bool ModifyFontInFormXObjects(PdfDictionary dict, PdfReader reader,
        Document doc, string text, bool isCore, byte[]? ttf, string baseFontName,
        Font? newFont = null, bool segmentScoped = false, bool exactOnly = false)
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
            var range = FindTfNameRange(streamData, text, xobjStream.Dict, reader,
                "AsRp" + SanitizeResName(baseFontName), exactOnly);
            if (range is not null)
            {
                var site = range.Value;
                var origName = ExtractResName(streamData, site.NameStart, site.NameEnd);
                if (site.Composite)
                {
                    if (ReencodeCompositeRun(streamData, site, xobjStream.Dict, reader, doc,
                            isCore, ttf, baseFontName, newFont) is { } recoded)
                        ReplaceFormStream(xobjStream, recoded);
                    return true;
                }
                var resName = RegisterFontResource(xobjStream.Dict, reader, doc, isCore, ttf, baseFontName, newFont);
                byte[]? xsplit = null;
                if (segmentScoped && origName is not null)
                    xsplit = site.LitStart >= 0
                        ? SplitFontRun(streamData, site.LitStart, site.LitEnd, text, origName, resName, site.Size)
                        : site.ShowStart >= 0
                            ? SplitShowRunTJ(streamData, site.ShowStart, site.ShowEnd, text,
                                  TfOps(resName, site.Size), TfOps(origName, site.Size), null)
                            : null;
                var modified = xsplit ?? PatchName(streamData, site.NameStart, site.NameEnd, resName);
                // A run's font is often re-selected by a redundant `/F Tf` that shows no
                // text (immediately overridden). Repoint those to the replacement too, so
                // the original font is left fully unreferenced and prunes cleanly instead
                // of surviving as a dangling /Tf.
                // Only when the whole Tf was repointed: a SPLIT deliberately leaves the
                // original font selected for the glyphs either side of the match.
                if (origName is not null && xsplit is null)
                    modified = RepointRedundantTfs(modified, origName, resName);
                ReplaceFormStream(xobjStream, modified);
                return true;
            }

            if (!exactOnly && FindSpannedTfSpans(streamData, text, xobjStream.Dict, reader,
                    "AsRp" + SanitizeResName(baseFontName)) is { Count: > 0 } spannedSpans)
            {
                var spannedRes = RegisterFontResource(xobjStream.Dict, reader, doc, isCore, ttf, baseFontName, newFont);
                ReplaceFormStream(xobjStream, PatchNames(streamData, spannedSpans, spannedRes));
                return true;
            }

            if (ModifyFontInFormXObjects(xobjStream.Dict, reader, doc, text, isCore, ttf, baseFontName,
                    newFont, segmentScoped, exactOnly))
                return true;
        }
        return false;
    }

    /// <summary>Patch each name span to <c>/<paramref name="resName"/></c>, applying
    /// right-to-left so the earlier offsets stay valid.</summary>
    private static byte[] PatchNames(byte[] data, List<(int start, int end)> spans, string resName)
    {
        var replacement = Encoding.ASCII.GetBytes("/" + resName);
        foreach (var (s, e) in spans.OrderByDescending(sp => sp.start))
            data = Splice(data, s, e, replacement);
        return data;
    }

    /// <summary>Write <paramref name="data"/> back into a Form XObject as its raw,
    /// unfiltered content.</summary>
    private static void ReplaceFormStream(PdfStream xobjStream, byte[] data)
    {
        xobjStream.Dict.Remove("Filter");
        xobjStream.Dict.Remove("DecodeParms");
        xobjStream.Dict.Set("Length", new PdfInteger(data.Length));
        xobjStream.ReplaceData(data);
    }

    /// <summary>Show a composite (Type0/CID) run with a simple replacement font. The run's
    /// 2-byte codes mean nothing under a single-byte font, so the show operand is replaced
    /// by a self-contained sequence that selects the replacement font, shows the text the
    /// run decodes to re-encoded as single bytes, and selects the original font back — the
    /// governing Tf is NOT repointed, because one composite Tf typically governs many runs
    /// and the others still carry 2-byte codes. The operator that follows the operand is
    /// left to show an empty string. Returns null when the run carries characters the
    /// replacement's WinAnsi encoding cannot show (a Latin font genuinely cannot stand in
    /// for a CJK run) — the run is then left as it was.</summary>
    private byte[]? ReencodeCompositeRun(byte[] content, FontSwapSite site,
        PdfDictionary container, PdfReader reader, Document doc,
        bool isCore, byte[]? ttf, string baseName, Font? newFont)
    {
        if (site.ShowStart < 0 || site.ShowEnd <= site.ShowStart) return null;
        var origRes = ExtractResName(content, site.NameStart, site.NameEnd);
        if (origRes is null) return null;
        if (!TryEncodeWinAnsiLiteral(site.Decoded, out var literal)) return null;

        var resName = RegisterFontResource(container, reader, doc, isCore, ttf, baseName, newFont);

        // The trailing operand keeps the following operator's arity: an array for TJ,
        // an empty literal for Tj / ' / ".
        var isArray = FirstNonSpace(content, site.ShowStart, site.ShowEnd) == (byte)'[';
        string Tf(string res) => string.Format(CultureInfo.InvariantCulture, "/{0} {1} Tf ",
            res, site.Size.ToString("0.####", CultureInfo.InvariantCulture));
        var replacement = Encoding.ASCII.GetBytes(
            " " + Tf(resName) + literal + " Tj " + Tf(origRes) + (isArray ? "[]" : "()"));
        return Splice(content, site.ShowStart, site.ShowEnd, replacement);
    }

    private static byte FirstNonSpace(byte[] data, int start, int end)
    {
        while (start < end && (data[start] == ' ' || data[start] == '\t'
            || data[start] == '\r' || data[start] == '\n')) start++;
        return start < end ? data[start] : (byte)0;
    }

    /// <summary>Replace <c>data[start..end)</c> with <paramref name="replacement"/>.</summary>
    private static byte[] Splice(byte[] data, int start, int end, byte[] replacement)
    {
        var result = new byte[data.Length - (end - start) + replacement.Length];
        Array.Copy(data, 0, result, 0, start);
        Array.Copy(replacement, 0, result, start, replacement.Length);
        Array.Copy(data, end, result, start + replacement.Length, data.Length - end);
        return result;
    }

    /// <summary>Render <paramref name="text"/> as a PDF literal string in the WinAnsi
    /// encoding a replacement simple font is written with, escaping the delimiters.
    /// False when any character has no WinAnsi code point.</summary>
    private static bool TryEncodeWinAnsiLiteral(string text, out string literal)
    {
        literal = string.Empty;
        var sb = new StringBuilder(text.Length + 2).Append('(');
        foreach (var ch in text)
        {
            var bytes = Cp1252.GetBytes(ch.ToString());
            // CP1252 maps an unrepresentable character to '?'; only a genuine '?' is one.
            if (bytes.Length != 1 || (bytes[0] == (byte)'?' && ch != '?')) return false;
            if (ch is '(' or ')' or '\\') sb.Append('\\');
            sb.Append((char)bytes[0]);
        }
        literal = sb.Append(')').ToString();
        return true;
    }

    /// <summary>Register the replacement font as a resource on <paramref name="container"/>
    /// (a page or Form XObject dict) and return its new resource name. A Standard-14 font
    /// becomes a plain Type1 dictionary (no descriptor / font file); any other font is
    /// embedded as a WinAnsi TrueType via <see cref="FontEmbedder"/>.</summary>
    private string RegisterFontResource(PdfDictionary container, Aspose.Pdf.IO.PdfReader reader,
        Document doc, bool isCore, byte[]? ttf, string baseName, Font? newFont = null)
    {
        // Consolidate: use a deterministic resource key per replacement font so that
        // replacing every run of a page with the same font reuses ONE /Font entry
        // instead of adding a duplicate per run. Keyed off the font's base name (the
        // resource-dict keys are always readable, unlike a just-allocated font object's
        // /BaseFont, which the reader can't yet resolve back).
        var resName = "AsRp" + SanitizeResName(baseName);
        if (FontResKeyExists(container, reader, resName)) return resName;
        if (isCore)
        {
            var objNum = doc.AllocateObjectNumber();
            var font = new PdfDictionary();
            font.Set("Type", new PdfName("Font"));
            font.Set("Subtype", new PdfName("Type1"));
            font.Set("BaseFont", new PdfName(baseName));
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
            // Overlay-registered so re-absorbing the SAME document instance resolves the
            // replacement font before any save — a replace-then-verify flow reads the
            // rewritten runs in memory.
            doc.AddNewObject(objNum, font, registerOverlay: true);
            AddFontRefToResources(container, reader, resName, objNum);
        }
        else
        {
            var embedder = FontEmbedder.Embed(doc, ttf!, resName, baseName);
            embedder.AddToResources(container, reader);
            // The caller may still clear IsEmbedded/IsSubset on the font after this
            // assignment; record what was written so that choice can be applied to it.
            if (newFont is not null && embedder.FontDict is not null)
                newFont.TrackMaterialised(doc, embedder.FontDict, baseName,
                    embedder.DescriptorObjNum, embedder.FontFileObjNum);
        }
        return resName;
    }

    /// <summary>Point <paramref name="container"/>'s /Resources/Font/<paramref name="resName"/>
    /// at the indirect font object <paramref name="objNum"/>, creating the resource
    /// sub-dictionaries as needed (mirrors <see cref="FontEmbedder.AddToResources"/>).</summary>
    private static void AddFontRefToResources(PdfDictionary container, Aspose.Pdf.IO.PdfReader reader,
        string resName, int objNum)
    {
        var resources = container.Get("Resources") as PdfDictionary
            ?? reader.ResolveDict(container.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            container.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as PdfDictionary
            ?? reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        fontDict.Set(resName, new PdfIndirectRef(objNum, 0));
    }

    /// <summary>Walk a content stream and return the byte range of the font-name
    /// operand of the Tf that is active when the first text-showing operator whose
    /// decoded string contains <paramref name="text"/> is reached.</summary>
    /// <summary>Rewrite a literal show operator so only the matched substring is shown
    /// in <paramref name="newRes"/>: the prefix keeps the original font, the match switches
    /// font, and the suffix switches back. The pen advances through the new glyphs, so
    /// trailing text moves by the width difference alone — the surrounding text keeps its
    /// own metrics. Returns null when the operand isn't a plain 1:1 literal or the match
    /// covers the whole run (the caller then repoints the Tf wholesale).</summary>
    private static byte[]? SplitFontRun(byte[] original, int litStart, int litEnd,
        string text, string origRes, string newRes, double size)
    {
        // The operand span starts where the lexer resumed, so it can carry leading
        // whitespace ahead of the literal's '('.
        // space, tab, CR or LF ahead of the literal's opening parenthesis
        while (litStart < litEnd && original[litStart] is 0x20 or 0x09 or 0x0D or 0x0A)
            litStart++;
        if (litEnd - litStart < 2 || string.IsNullOrEmpty(origRes)
            || original[litStart] != (byte)'(' || original[litEnd - 1] != (byte)')')
            return null;

        int innerStart = litStart + 1;
        int innerLen = litEnd - 1 - innerStart;
        if (innerLen <= 0) return null;
        for (int i = innerStart; i < innerStart + innerLen; i++)
        {
            byte b = original[i];
            if (b == (byte)'\\' || b == (byte)'(' || b == (byte)')') return null;
        }

        var innerBytes = new byte[innerLen];
        Array.Copy(original, innerStart, innerBytes, 0, innerLen);
        var inner = Cp1252.GetString(innerBytes);
        int idx = inner.IndexOf(text, StringComparison.Ordinal);
        if (idx < 0) return null;
        // Whole-run match → the caller's Tf repoint is both correct and cheaper.
        if (idx == 0 && text.Length == inner.Length) return null;

        var prefix = inner.Substring(0, idx);
        var suffix = inner.Substring(idx + text.Length);
        string Tf(string res) => string.Format(CultureInfo.InvariantCulture,
            "/{0} {1} Tf ", res, size.ToString("0.####", CultureInfo.InvariantCulture));

        // Lead with a space so the first token never abuts the preceding operator.
        var sb = new StringBuilder(" ");
        if (prefix.Length > 0) sb.Append('(').Append(prefix).Append(") Tj ");
        sb.Append(Tf(newRes)).Append('(').Append(text).Append(')');
        if (suffix.Length > 0)
        {
            // The original Tj keyword after this operand shows the suffix.
            sb.Append(" Tj ").Append(Tf(origRes)).Append('(').Append(suffix).Append(')');
        }
        else
        {
            sb.Append(" Tj ").Append(Tf(origRes)).Append("()");
        }

        var replacement = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[original.Length - (litEnd - litStart) + replacement.Length];
        Array.Copy(original, 0, result, 0, litStart);
        Array.Copy(replacement, 0, result, litStart, replacement.Length);
        Array.Copy(original, litEnd, result, litStart + replacement.Length, original.Length - litEnd);
        return result;
    }

    /// <summary>Where a font swap can be applied: the `/Name` operand span of the
    /// governing Tf, plus — when the match sits inside a plain single-byte literal
    /// show operand — that operand's span and the active Tf size, which let the
    /// caller split the run instead of repointing the whole Tf.
    /// <paramref name="ShowStart"/>/<paramref name="ShowEnd"/> span the show operator's
    /// whole operand (literal, hex or TJ array); <paramref name="Composite"/> marks a
    /// run whose font is Type0, whose 2-byte codes must be re-encoded before a simple
    /// font can show it.</summary>
    private readonly record struct FontSwapSite(int NameStart, int NameEnd,
        int LitStart, int LitEnd, double Size, string Decoded,
        int ShowStart = -1, int ShowEnd = -1, bool Composite = false);

    /// <param name="alreadyReplacedRes">Resource name the replacement font is registered
    /// under. A run whose active Tf already names it has been converted by an earlier call
    /// and is skipped, so replacing several fragments that share the same text (a page of
    /// single-space runs, say) walks forward instead of re-matching the first one and
    /// leaving the rest showing the original font.</param>
    /// <param name="exactOnly">Accept only a run whose text IS the fragment's, so the
    /// caller can look for the fragment's own run in every scope before settling for one
    /// that merely contains the text.</param>
    private FontSwapSite? FindTfNameRange(byte[] streamBytes, string text,
        PdfDictionary pageDict, PdfReader reader, string? alreadyReplacedRes = null,
        bool exactOnly = false)
    {
        if (streamBytes.Length == 0) return null;
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        int lastTfNameStart = -1, lastTfNameEnd = -1;
        double lastTfSize = 0;
        Dictionary<int, string>? currentToUnicode = null;
        // A simple (single-byte) font is swapped for our simple WinAnsi embedded font by
        // repointing Tf alone: the shown bytes are reinterpreted under the new font's
        // encoding. A Type0/CID font shows 2-byte codes that a simple font cannot
        // represent, so its show operand has to be re-encoded as well — the caller does
        // that when the site reports Composite. A font the resource dict doesn't resolve
        // is left alone entirely: its codes decode to nothing reliable.
        bool currentFontIsSimple = false;
        bool currentFontResolved = false;
        string? currentFontRes = null;
        // A run whose text IS the fragment's is the fragment's own run; one that merely
        // contains it may belong to a different fragment. Preferring the exact run keeps
        // a short fragment ("c" out of a split word) from claiming a long run and leaving
        // its own showing the original font.
        FontSwapSite? containing = null;

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
                    var arrTexts = new StringBuilder();
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                        {
                            var strBytes = t.BytesValue;
                            if (strBytes is not null)
                                arrTexts.Append(DecodeTextString(strBytes, currentToUnicode));
                        }
                    }
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
                            if (operands.Count >= 2 && operands[0].obj is PdfName fn)
                            {
                                currentFontRes = fn.Value;
                                if (fonts.TryGetValue(fn.Value, out var fontDict))
                                {
                                    currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                    currentFontIsSimple = fontDict.GetName("Subtype") != "Type0";
                                    currentFontResolved = true;
                                }
                                else
                                {
                                    currentToUnicode = null;
                                    currentFontIsSimple = false;
                                    currentFontResolved = false;
                                }
                                lastTfNameStart = operands[0].startPos;
                                lastTfNameEnd = operands[0].endPos;
                                lastTfSize = operands[1].obj is PdfInteger ti ? ti.Value
                                    : operands[1].obj is PdfReal tr ? tr.Value : 0;
                            }
                            break;
                        case "Tj":
                        case "'":
                        case "\"":
                        case "TJ":
                            if (operands.Count >= 1 && operands[^1].obj is PdfString showStr)
                            {
                                var decoded = DecodeTextString(showStr.Value, currentToUnicode);
                                var alreadyDone = alreadyReplacedRes is not null
                                    && string.Equals(currentFontRes, alreadyReplacedRes, StringComparison.Ordinal);
                                if (decoded.Contains(text) && lastTfNameStart >= 0 && currentFontResolved
                                    && !alreadyDone)
                                {
                                    var litOk = op == "Tj" && operands[^1].kind == TokenKind.LiteralString;
                                    var site = new FontSwapSite(lastTfNameStart, lastTfNameEnd,
                                        litOk ? operands[^1].startPos : -1,
                                        litOk ? operands[^1].endPos : -1,
                                        lastTfSize, decoded,
                                        operands[^1].startPos, operands[^1].endPos,
                                        Composite: !currentFontIsSimple);
                                    if (decoded.Length == text.Length) return site;
                                    containing ??= site;
                                }
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
        return exactOnly ? null : containing;
    }

    /// <summary>Read the resource name (without the leading '/') from a Tf name
    /// operand span, or null if it isn't a name token.</summary>
    private static string? ExtractResName(byte[] data, int start, int end)
    {
        while (start < end && (data[start] == ' ' || data[start] == '\t'
            || data[start] == '\r' || data[start] == '\n')) start++;
        if (start >= end || data[start] != (byte)'/') return null;
        return Encoding.ASCII.GetString(data, start + 1, end - start - 1).Trim();
    }

    /// <summary>Repoint every `/<paramref name="origName"/> … Tf` that selects a font but
    /// shows no text before the next Tf (a redundant selection) to
    /// <paramref name="newResName"/>. Runs that DO show text are left untouched — only the
    /// no-op selections are rewritten, so this never changes visible text.</summary>
    private byte[] RepointRedundantTfs(byte[] data, string origName, string newResName)
    {
        var lexer = new PdfLexer(data);
        var operands = new List<(int start, int end, byte[]? str)>();
        var patches = new List<(int start, int end)>(); // name spans to repoint
        int pendingNameStart = -1, pendingNameEnd = -1; string? pendingFontName = null;
        bool sawGlyphs = false; bool haveOpenTf = false;

        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            var endPos = (int)lexer.Position;
            switch (token.Kind)
            {
                case TokenKind.Name:
                case TokenKind.Integer:
                case TokenKind.Real:
                    operands.Add((startPos, endPos, null));
                    break;
                case TokenKind.LiteralString:
                case TokenKind.HexString:
                    if (token.BytesValue is { Length: > 0 }) sawGlyphs = true;
                    operands.Add((startPos, endPos, token.BytesValue));
                    break;
                case TokenKind.ArrayStart:
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto finish;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if ((t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                            && t.BytesValue is { Length: > 0 }) sawGlyphs = true;
                    }
                    operands.Clear();
                    break;
                case TokenKind.Keyword:
                    var op = token.StringValue!;
                    if (op == "Tf")
                    {
                        // Close out the previous Tf: if it selected origName and showed no
                        // glyphs, it was redundant — repoint it.
                        if (haveOpenTf && pendingFontName == origName && !sawGlyphs)
                            patches.Add((pendingNameStart, pendingNameEnd));
                        // Open this Tf.
                        if (operands.Count >= 1)
                        {
                            var nameOp = operands[0];
                            pendingNameStart = nameOp.start; pendingNameEnd = nameOp.end;
                            pendingFontName = ExtractResName(data, nameOp.start, nameOp.end);
                            haveOpenTf = true;
                        }
                        sawGlyphs = false;
                    }
                    else if (op == "ET")
                    {
                        if (haveOpenTf && pendingFontName == origName && !sawGlyphs)
                            patches.Add((pendingNameStart, pendingNameEnd));
                        haveOpenTf = false; sawGlyphs = false;
                    }
                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
        }
        finish:
        if (patches.Count == 0) return data;

        // Apply right-to-left so earlier offsets stay valid.
        patches.Sort((a, b) => b.start.CompareTo(a.start));
        foreach (var (s, e) in patches)
            data = PatchName(data, s, e, newResName);
        return data;
    }

    /// <summary>Whether the container's /Resources/Font already has an entry named
    /// <paramref name="resName"/> (checked by key, which is always readable).</summary>
    private static bool FontResKeyExists(PdfDictionary containerDict, PdfReader reader, string resName)
    {
        var resources = containerDict.Get("Resources") as PdfDictionary
            ?? reader.ResolveDict(containerDict.Get("Resources"));
        var fontDict = resources is null ? null
            : (resources.Get("Font") as PdfDictionary ?? reader.ResolveDict(resources.Get("Font")));
        return fontDict is not null && fontDict.ContainsKey(resName);
    }

    /// <summary>Reduce a font base name to a PDF-name-safe token for use as a
    /// resource key (letters and digits only).</summary>
    private static string SanitizeResName(string baseName)
    {
        var sb = new System.Text.StringBuilder(baseName.Length);
        foreach (var c in baseName)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : "Font";
    }

    private static byte[] PatchName(byte[] original, int nameStart, int nameEnd, string resName)
    {
        while (nameStart < nameEnd && (original[nameStart] == ' ' || original[nameStart] == '\t'
            || original[nameStart] == '\r' || original[nameStart] == '\n'))
            nameStart++;
        var nameBytes = Encoding.ASCII.GetBytes("/" + resName);
        var result = new byte[original.Length - (nameEnd - nameStart) + nameBytes.Length];
        Array.Copy(original, 0, result, 0, nameStart);
        Array.Copy(nameBytes, 0, result, nameStart, nameBytes.Length);
        Array.Copy(original, nameEnd, result, nameStart + nameBytes.Length, original.Length - nameEnd);
        return result;
    }
}
