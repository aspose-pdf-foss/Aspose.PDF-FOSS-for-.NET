using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Bytes a rewrite is expected to grow by at most: the buffer starts at the
    /// source length plus this, so a page-sized rewrite does not double its way up.</summary>
    private const int RewriteHeadroom = 256;

    private byte[] ReplaceInContentStream(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader,
        HashSet<int>? processedXObjects = null,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        processedXObjects ??= new HashSet<int>();
        var rr = new ReflowReplaceState();
        rr.countBefore = _replacementCount;
        rr.fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        rr.normalizedSearch = NormalizeForSearch(search);
        rr.lexer = new PdfLexer(streamBytes);
        rr.result = new MemoryStream(streamBytes.Length + RewriteHeadroom);
        rr.operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        rr.currentFontName = null;
        rr.currentToUnicode = null;
        rr.currentFontDict = null;
        rr.currentFontSize = 12.0;
        rr.lastWritePos = 0;

        rr.ctmA = initCtmA;
        rr.ctmB = initCtmB;
        rr.ctmC = initCtmC;
        rr.ctmD = initCtmD;
        rr.ctmTx = initCtmTx;
        rr.ctmTy = initCtmTy;
        rr.ctmStack = new Stack<(double, double, double, double, double, double)>();
        rr.tmA = 1;
        rr.tmB = 0;
        rr.tmC = 0;
        rr.tmD = 1;
        rr.tmTx = 0;
        rr.tmTy = 0;
        rr.tlLeading = 0;
        rr.renderMode = 0;
        rr.trStack = new Stack<int>();
        rr.tcSpacing = 0;
        rr.twSpacing = 0;
        rr.spacingStack = new Stack<(double tc, double tw)>();

        while (true)
        {
            var startPos = (int)rr.lexer.Position;
            var token = rr.lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            if (!ConsumeReplaceToken(rr, token, startPos, streamBytes, search, replacement, pageDict, reader, processedXObjects)) break;
        }

        // Write remaining bytes
        if (rr.lastWritePos < streamBytes.Length)
            rr.result.Write(streamBytes, rr.lastWritePos, streamBytes.Length - rr.lastWritePos);

        rr.output = rr.result.ToArray();
        _walkSawText = rr.sawText;

        // Always run cross-operator pass when enabled, even after per-op
        // replacements: per-op handles within-Tj matches, cross-op picks up
        // matches whose decoded text spans separate Tj/TJ operators (e.g.
        // "Page " in one Tj followed by "5 of 10" in another after a Td/Tm).
        // The cross-op routine itself skips single-operator matches so we
        // don't double-process spans the per-op pass already replaced.
        if (_allowCrossOperator)
        {
            var crossResult = TryCrossOperatorReplace(rr.output, search, replacement, pageDict, reader,
                initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
            if (crossResult is not null)
                rr.output = crossResult;
        }

        return rr.output;
    }

    /// <summary>
    /// Cross-operator text replacement: collects text across consecutive Tj/TJ operators,
    /// finds the search string (literal or regex per <see cref="_isRegex"/>) in the
    /// concatenated text, and rewrites the operators. Used to catch matches whose
    /// decoded text spans positioned glyphs across separate Tj/TJ operators —
    /// invisible to the per-operator matcher.
    /// </summary>
    private byte[]? TryCrossOperatorReplace(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);

        // Collect text operators with everything needed to (a) build a gap-aware
        // concatenation for matching, (b) split a partially-matched first/last operator,
        // and (c) re-anchor / shift following same-line runs: decoded text + raw string
        // bytes, byte span, text matrix + CTM, font state (dict/ToUnicode/size/Tc), TJ
        // kern total, and the byte span of the op's positioning Tm x-operand (when the
        // op is Tm-positioned) so a follower's Tm can be rewritten in place.
        var textOps = CollectTextOps(streamBytes, fonts, reader,
            initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
        return TryCrossOperatorReplaceCore(streamBytes, search, replacement, pageDict, reader,
            normalizedSearch, textOps);
    }
}
