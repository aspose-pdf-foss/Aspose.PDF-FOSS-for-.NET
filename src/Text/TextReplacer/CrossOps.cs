using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Collect every single-rectangle fill — a lone `re` painted by
    /// f/F/f*/B/B*/b/b* with no other path segment — with its page-space rect
    /// (axis-aligned CTM) and the byte span covering `re`'s first operand through
    /// the painting operator. Underline bars drawn by word processors take exactly
    /// this shape; multi-segment paths are skipped so a real outline never
    /// qualifies for deletion.</summary>
    private static List<FillRectOp> CollectFillRects(byte[] streamBytes)
    {
        var rects = new List<FillRectOp>();
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(double val, int startPos)>();
        double ctmA = 1, ctmB = 0, ctmC = 0, ctmD = 1, ctmTx = 0, ctmTy = 0;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        // Pending path: the current subpath ops since the last paint/clear.
        (bool has, double x, double y, double w, double h, int spanStart) pending = default;
        bool pathDirty = false;

        while (true)
        {
            var sp = (int)lexer.Position;
            var tok = lexer.NextToken();
            if (tok.Kind == TokenKind.Eof) break;
            var ep = (int)lexer.Position;
            switch (tok.Kind)
            {
                case TokenKind.Integer:
                    operands.Add((tok.IntValue, sp));
                    break;
                case TokenKind.Real:
                    operands.Add((tok.RealValue, sp));
                    break;
                case TokenKind.Keyword:
                {
                    var op = tok.StringValue!;
                    switch (op)
                    {
                        case "q":
                            ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
                            break;
                        case "cm" when operands.Count >= 6:
                        {
                            double a = operands[0].val, b = operands[1].val, c = operands[2].val;
                            double d = operands[3].val, tx = operands[4].val, ty = operands[5].val;
                            double nA = a * ctmA + b * ctmC, nB = a * ctmB + b * ctmD;
                            double nC = c * ctmA + d * ctmC, nD = c * ctmB + d * ctmD;
                            double nTx = tx * ctmA + ty * ctmC + ctmTx, nTy = tx * ctmB + ty * ctmD + ctmTy;
                            ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmTx = nTx; ctmTy = nTy;
                            break;
                        }
                        case "re" when operands.Count >= 4:
                            if (pending.has) pathDirty = true; // second rect in one path
                            pending = (true, operands[^4].val, operands[^3].val,
                                operands[^2].val, operands[^1].val, operands[^4].startPos);
                            break;
                        case "m" or "l" or "c" or "v" or "y" or "h":
                            pathDirty = true;
                            break;
                        case "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            if (pending.has && !pathDirty
                                && Math.Abs(ctmB) < 1e-9 && Math.Abs(ctmC) < 1e-9)
                                rects.Add(MakeFillRect(pending, ctmA, ctmD, ctmTx, ctmTy, ep));
                            pending = default; pathDirty = false;
                            break;
                        case "n" or "S" or "s":
                            pending = default; pathDirty = false;
                            break;
                        // W/W* leave the path pending for the following paint/n op.
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
        return rects;
    }

    /// <summary>The pending `re` painted under an axis-aligned CTM, as a device-space fill
    /// rectangle spanning the path's first operand to the paint operator.</summary>
    private static FillRectOp MakeFillRect((bool has, double x, double y, double w, double h, int spanStart) pending,
        double ctmA, double ctmD, double ctmTx, double ctmTy, int ep)
    {
        double x0 = ctmA * pending.x + ctmTx, y0 = ctmD * pending.y + ctmTy;
        double x1 = ctmA * (pending.x + pending.w) + ctmTx;
        double y1 = ctmD * (pending.y + pending.h) + ctmTy;
        return new FillRectOp
        {
            X = Math.Min(x0, x1), Y = Math.Min(y0, y1),
            W = Math.Abs(x1 - x0), H = Math.Abs(y1 - y0),
            SpanStart = pending.spanStart, SpanEnd = ep,
        };
    }

    /// <summary>Per-text-operator record for <see cref="TryCrossOperatorReplace"/>.</summary>
    private sealed class CrossTextOp
    {
        public string Text = "";
        public byte[] Bytes = Array.Empty<byte>();
        public bool IsHex;
        public int OpStart, OpEnd;
        public double TmA = 1, TmB, TmC, TmD = 1, TmTx, TmTy;
        public double CtmA = 1, CtmB, CtmC, CtmD = 1, CtmTx, CtmTy;
        public PdfDictionary? FontDict;
        public string? FontName;
        public Dictionary<int, string>? ToUnicode;
        public double FontSize = 12, Tc, KernSum;
        /// <summary>Each TJ kern paired with the byte offset it applies at, so a prefix
        /// of the run can be measured at the width it actually occupies.</summary>
        public List<(int byteIndex, double amount)>? KernAt;
        public int CharStart = -1;
        public bool TmPositioned;
        public int TmXTokStart, TmXTokEnd;
        public double TmXVal;
        /// <summary>The <c>tx</c> operand of the <c>Td</c>/<c>TD</c> that placed this op,
        /// when one did. A Td-chained line states each glyph RELATIVE to the last, so
        /// adding to this single number carries every later glyph with it - which is how
        /// a follower shift reaches a line that states no absolute Tm of its own.</summary>
        public bool TdPositioned;
        public int TdXTokStart, TdXTokEnd;
        public double TdXVal;
        /// <summary>Byte offset of the enclosing BT keyword (-1 when none seen);
        /// graphics injected for this op (regenerated underlines) go BEFORE it,
        /// since path operators are illegal inside a text object.</summary>
        public int BtStart = -1;
        /// <summary>Set when <see cref="Bytes"/> has been REWRITTEN and no longer spells
        /// what the source operator holds - the cross-break trim is the one edit that does
        /// this to a run other than the head. A run the flow merely MOVES is re-emitted by
        /// copying its original operator bytes verbatim, which would silently discard the
        /// trim, so such a run has to be re-encoded from <see cref="Bytes"/> instead.</summary>
        public bool BytesRewritten;
    }

    /// <summary>A single-rectangle fill (`re` immediately painted by f/f*/F/B/B*/b/b*)
    /// found by <see cref="CollectFillRects"/>: the page-space rect (axis-aligned CTM
    /// assumed) plus the byte span from the `re`'s first operand through the painting
    /// operator, so the whole construct can be deleted from the stream.</summary>
    private sealed class FillRectOp
    {
        public double X, Y, W, H;
        public int SpanStart, SpanEnd;
    }

    /// <summary>
    /// Check that the page-space Y of the current text matrix
    /// (Tm.ty × CTM[3] + CTM[5]) is within tolerance of <see cref="TargetY"/>.
    /// Returns true unconditionally when TargetY is unset (page-wide replace,
    /// the default behaviour). Only handles axis-aligned scale+translate CTMs;
    /// rotation/skew degrades to "no replacement" which is safer than
    /// page-wide for the per-fragment use case.
    /// </summary>
    private bool IsAtTargetY(double tmTx, double tmTy, double ctmB, double ctmD, double ctmTy)
    {
        if (TargetY is not double targetY) return true;
        // Full Y row of the CTM: the ctmB×tmTx cross-term matters on rotated pages
        // (page /Rotate seeds a 90°/270° CTM where Y comes from the text-space X).
        var pageY = ctmB * tmTx + ctmD * tmTy + ctmTy;
        return Math.Abs(pageY - targetY) <= TargetYTolerance;
    }

    /// <summary>
    /// True when <see cref="RequiredRenderMode"/> is unset (mode-agnostic, the
    /// default), or the current text render mode equals it. Lets invisible-fragment
    /// deletion target only the Tr-3 copy of overlapping visible/invisible text.
    /// </summary>
    private bool RenderModeMatches(int renderMode)
        => RequiredRenderMode is not int required || renderMode == required;

    /// <summary>
    /// Check that the page-space X of the current text-matrix origin
    /// (Tm.tx × CTM[0] + Tm.ty × CTM[2] + CTM[4]) is within tolerance of
    /// <see cref="TargetX"/>. Returns true unconditionally when TargetX is unset
    /// (the default — X is not scoped). Companion to <see cref="IsAtTargetY"/>.
    /// </summary>
    private bool IsAtTargetX(double tmTx, double tmTy, double ctmA, double ctmC, double ctmTx)
    {
        if (TargetX is not double targetX) return true;
        var pageX = ctmA * tmTx + ctmC * tmTy + ctmTx;
        return Math.Abs(pageX - targetX) <= TargetXTolerance;
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0
    };

    /// <summary>Replace matches in <paramref name="text"/> for the current search.
    /// Honours <see cref="ReplaceFirstOnly"/>.</summary>
    private string ApplyReplace(string text, string normalizedSearch, string replacement)
    {
        if (MatchAnyOperator)
        {
            _replacementCount++;
            return replacement;
        }
        if (_isRegex && _regexPattern is not null)
        {
            if (ReplaceFirstOnly)
            {
                var match = _regexPattern.Match(text);
                if (!match.Success) return text;
                _replacementCount++;
                return Compat.Concat(text.AsSpan(0, match.Index), replacement.AsSpan(),
                    text.AsSpan(match.Index + match.Length));
            }
            _replacementCount += _regexPattern.Matches(text).Count;
            return _regexPattern.Replace(text, replacement);
        }
        if (ReplaceFirstOnly)
        {
            int idx = text.IndexOf(normalizedSearch, StringComparison.Ordinal);
            if (idx < 0) return text;
            _replacementCount++;
            return Compat.Concat(text.AsSpan(0, idx), replacement.AsSpan(),
                text.AsSpan(idx + normalizedSearch.Length));
        }
        _replacementCount += CountOccurrences(text, normalizedSearch);
        return text.Replace(normalizedSearch, replacement, StringComparison.Ordinal);
    }

    /// <summary>
    /// Embed a Type0/CID fallback font — from the source font's own family when
    /// installed, else a script-appropriate face — that contains the glyphs for
    /// <paramref name="text"/>, and return its resource name plus the 2-byte
    /// glyph-id string. Used when the source font can't encode a non-Latin1
    /// replacement (Cyrillic/CJK not in its subset) so the run renders AND stays
    /// searchable via the embedder's /ToUnicode CMap. Re-embeds the source font's
    /// own family when installed (e.g. TimesNewRoman / FangSong / SimHei), else a
    /// script-appropriate face. Returns null when no suitable TTF is available
    /// (caller keeps the Standard-14 Latin path).
    /// </summary>
    private static (string resName, byte[] hexIds)? TryEmbedCidFallback(
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict,
        string? forcedFamily = null)
    {
        var srcFamily = SourceFontFamily(sourceFontDict);
        // A forced family (the explicit-ReplaceFonts assignment redress) replaces the
        // whole family-preserving walk: the substitution scan already chose the face.
        var candidates = forcedFamily is { Length: > 0 }
            ? new List<string?> { forcedFamily }
            : new List<string?> { srcFamily };
        if (forcedFamily is { Length: > 0 })
        {
            candidates.Add("TimesNewRoman");
            candidates.Add("Arial");
            return EmbedFirstCovering(pageDict, reader, text, candidates);
        }
        // A legacy-codepage family name carries a charset suffix the installed face
        // does not ("FangSong_GB2312" names the same family as "FangSong") - the
        // reference preserves the FAMILY: its replacement reads back as FangSong.
        var suffixCut = srcFamily?.IndexOf('_') ?? -1;
        if (suffixCut > 0) candidates.Add(srcFamily!.Substring(0, suffixCut));
        // SimSun is the default Han substitute (even with
        // FangSong available in the SYSTEM, CJK replacements read back as SimSun); a
        // FangSong result comes from the source family above or from a
        // CALLER-REGISTERED source, which outranks the system default (measured:
        // with the test-data Fonts folder registered, a SimHei-sourced replacement
        // whose family is not installed reads back as the folder's FangSong).
        if (ContainsCjk(text))
        {
            if (FontRepository.FindRegisteredCoveringFamily(text) is { } regFamily)
                candidates.Add(regFamily);
            candidates.Add("SimSun");
            candidates.Add("FangSong");
            candidates.Add("MS Gothic");
        }
        // The standard substitute is Times New Roman (probed: a Cyrillic and a Latin-1
        // replacement into an unresolvable CID face both come back in Times); Arial is the
        // last resort for glyphs Times lacks.
        candidates.Add("TimesNewRoman");
        candidates.Add("Arial");

        return EmbedFirstCovering(pageDict, reader, text, candidates);
    }

    /// <summary>Embed the first candidate family that resolves and covers every
    /// non-ASCII glyph of <paramref name="text"/> (first resolvable as best-effort
    /// when none covers), as a Type0/CID subset in the page's font dict.</summary>
    private static (string resName, byte[] hexIds)? EmbedFirstCovering(
        PdfDictionary pageDict, PdfReader reader, string text, List<string?> candidates)
    {
        // The non-ASCII characters that actually need a glyph in the fallback face.
        var need = text.Where(c => c > 0x7F).Distinct().ToArray();

        byte[]? ttf = null;
        var family = "Arial";
        byte[]? firstAvail = null;
        var firstFamily = "";
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            byte[]? t;
            try { t = FontRepository.GetTtfDataForSubstitution(c!); } catch { t = null; }
            if (t is not { Length: > 12 }) continue;
            if (firstAvail is null) { firstAvail = t; firstFamily = c!; }
            // Prefer a face that actually covers every needed non-ASCII glyph —
            // the source family may be a Latin-only subset with no Hebrew/CJK.
            try
            {
                var gp = new GlyphOutlineParser(t);
                if (need.All(ch => gp.CMap.TryGetValue(ch, out var g) && g != 0))
                { ttf = t; family = c!; break; }
            }
            catch { /* unparseable — skip */ }
        }
        if (ttf is null) { ttf = firstAvail; family = firstFamily; } // best-effort
        if (ttf is null) return null;

        try
        {
            var fonts = GetOrCreatePageFontDict(pageDict, reader);
            var (resName, hexIds) = Type0FontEmbedder.Embed(fonts, ttf, family, text, stripSpacesInBaseFont: true);
            return (resName, hexIds);
        }
        catch { return null; }
    }

    /// <summary>
    /// Embed Times New Roman as a Type0/Identity-H subset covering <paramref name="text"/>
    /// and return its resource name + 2-byte glyph-id string. Used to font-switch a run
    /// whose source subset lacks glyphs for (Latin) replacement chars by substituting
    /// the whole run in Times. Returns null when Times isn't resolvable.
    /// </summary>
    private static (string resName, byte[] hexIds)? EmbedTimesCidForRun(
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict)
    {
        // Prefer re-embedding the SOURCE font's own family when it's installed (keep the
        // family, e.g. an Arial subset → Arial), else fall back to Times New Roman
        // (the source family isn't available to expand, e.g. Bookman/Folio not installed).
        byte[]? ttf = null;
        string family = "TimesNewRoman";
        // The stand-in keeps the replaced run's weight and slope, so a bold run does not
        // come back regular; the unstyled names close the list for the faces that do not
        // resolve a styled variant.
        var styled = "TimesNewRoman" + TimesStyleSuffix(sourceFontDict, reader);
        foreach (var fam in new[] { SourceFontFamily(sourceFontDict), styled, "TimesNewRoman", "Times New Roman", "Times" })
        {
            if (string.IsNullOrEmpty(fam)) continue;
            byte[]? t;
            try { t = FontRepository.GetTtfData(fam!); } catch { t = null; }
            if (t is { Length: > 12 }) { ttf = t; family = fam!; break; }
        }
        if (ttf is null) return null;
        try
        {
            var fonts = GetOrCreatePageFontDict(pageDict, reader);
            var emb = Type0FontEmbedder.Embed(fonts, ttf, family, text, stripSpacesInBaseFont: true);
            RecordSwitchedFont(family);
            return emb;
        }
        catch { return null; }
    }

    /// <summary>
    /// Make ResolveFonts accessible for text replacement.
    /// </summary>
    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);
}
