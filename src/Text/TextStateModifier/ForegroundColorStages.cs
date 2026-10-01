using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
// A helper of the foreground-colour rewrite.
    private static bool MatchesY(ForegroundColorState fc) => !fc.targetY.HasValue
        || Math.Abs(fc.ctmD * fc.tmTy + fc.ctmTy - fc.targetY.Value) <= fc.yTolerance;

    private static bool MatchesX(ForegroundColorState fc) => !fc.targetX.HasValue
        || Math.Abs(fc.ctmA * fc.penTx + fc.ctmC * fc.tmTy + fc.ctmTx - fc.targetX.Value) <= fc.xTolerance;

    // A show operator whose start X coincides with the target segment's X to
    // half a point IS that segment's operator — the segment position was
    // measured from it. Trusted over the decoded-text containment check,
    // whose ToUnicode interpretation can disagree with the absorber's for
    // exotic CID maps (observed: a space run decoding as '=').
    private static bool GeometricallyExact(ForegroundColorState fc) => fc.targetX.HasValue
        && Math.Abs(fc.ctmA * fc.penTx + fc.ctmC * fc.tmTy + fc.ctmTx - fc.targetX.Value) <= 0.5;

    // ★ Which OCCURRENCE of the text inside this show operator the target segment is.
    // A line that draws the same replacement twice ("… 12345.  The 12345 …") is one
    // show operator with two matches, and each carries its own segment — anchoring on
    // the operator's start X alone would give them both the first one, so the second
    // recolour lands on glyphs that already have it and the real second occurrence is
    // never reached. The pen at each occurrence is the operator's pen plus the advance
    // of everything drawn before it (kerns included).
    // Returns the char offset to split at, or -1 when none of them is the target.
    private static int PickOccurrence(ForegroundColorState fc, string decoded, List<object>? arrayItems, byte[]? singleRun)
    {
        var first = decoded.IndexOf(fc.text, StringComparison.Ordinal);
        if (first < 0 || !fc.targetX.HasValue) return first;
        var nearestAt = -1;
        var nearestGap = double.MaxValue;
        for (var at = first; at >= 0; at = decoded.IndexOf(fc.text, at + 1, StringComparison.Ordinal))
        {
            var pen = fc.penTx + PrefixAdvance(fc, arrayItems, singleRun, at) * fc.tmA;
            var gap = Math.Abs(fc.ctmA * pen + fc.ctmC * fc.tmTy + fc.ctmTx - fc.targetX.Value);
            if (gap < nearestGap) { nearestGap = gap; nearestAt = at; }
        }
        if (nearestAt < 0) return -1;
        fc.lastOccurrenceGap = nearestGap;
        return fc.nearestX || nearestGap <= fc.xTolerance ? nearestAt : -1;
    }

    // Advance of the first `chars` shown characters of this operator. Only a 1:1
    // byte↔char run can be measured this way, which is the same restriction the
    // colour split itself works under; anything else reports 0 so the caller keeps
    // its whole-operator behaviour.
    private static double PrefixAdvance(ForegroundColorState fc, List<object>? arrayItems, byte[]? singleRun, int chars)
    {
        if (chars <= 0) return 0;
        double total = 0;
        var left = chars;
        if (arrayItems is null)
        {
            if (singleRun is null || singleRun.Length < chars) return 0;
            var head = new byte[chars];
            Array.Copy(singleRun, head, chars);
            return StringAdvance(fc, head);
        }
        foreach (var item in arrayItems)
        {
            if (left <= 0) break;
            if (item is byte[] runBytes)
            {
                var take = Math.Min(left, runBytes.Length);
                var head = new byte[take];
                Array.Copy(runBytes, head, take);
                total += StringAdvance(fc, head);
                left -= take;
            }
            else if (item is double kern)
            {
                total -= kern / 1000.0 * fc.fontSize * fc.hScaling;
            }
        }
        return total;
    }

    // Advance of one shown string in fc.text-space units (mirrors
    // ContentStreamParser's cursor math: per-code width + Tc, + Tw on the
    // single-byte space code, scaled by Tz). CID (2-byte) fonts consume the
    // bytes pairwise through the /W-keyed metrics.
    private static double StringAdvance(ForegroundColorState fc, byte[] bytes)
    {
        if (bytes.Length == 0 || fc.fontSize <= 0) return 0;
        double total = 0;
        if (fc.currentMetrics is { IsCid: true })
        {
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var cid = (bytes[i] << 8) | bytes[i + 1];
                total += (fc.currentMetrics.GetWidth(cid) / 1000.0 * fc.fontSize + fc.charSpacing) * fc.hScaling;
            }
        }
        else
        {
            foreach (var b in bytes)
            {
                var w = fc.currentMetrics?.GetWidth(b) ?? 500;
                total += (w / 1000.0 * fc.fontSize + fc.charSpacing
                    + (b == 0x20 ? fc.wordSpacing : 0)) * fc.hScaling;
            }
        }
        return total;
    }

    /// <summary>Seed the rewrite's state: the inputs, the page's fonts, the lexer and the initial CTM and text state.</summary>
    private void InitForegroundColorState(ForegroundColorState fc, byte[] streamBytes, string text, double? targetY, double? targetX, PdfDictionary pageDict, PdfReader reader, double initCtmA, double initCtmB, double initCtmC, double initCtmD, double initCtmTx, double initCtmTy, bool nearestX)
    {
        fc.text = text;
        fc.targetY = targetY;
        fc.targetX = targetX;
        fc.nearestX = nearestX;
        fc.fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        fc.lexer = new PdfLexer(streamBytes);
        fc.operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        fc.currentToUnicode = null;
        fc.currentFontName = null;
        fc.currentMetrics = null;
        fc.fontSize = 0;
        fc.charSpacing = 0;
        fc.wordSpacing = 0;
        fc.hScaling = 1.0;
        fc.tjItems = null;

        fc.ctmA = initCtmA;
        fc.ctmB = initCtmB;
        fc.ctmC = initCtmC;
        fc.ctmD = initCtmD;
        fc.ctmTx = initCtmTx;
        fc.ctmTy = initCtmTy;
        fc.ctmStack = new Stack<(double, double, double, double, double, double)>();
        fc.tmA = 1;
        fc.tmB = 0;
        fc.tmC = 0;
        fc.tmD = 1;
        fc.tmTx = 0;
        fc.tmTy = 0;
        fc.tlLeading = 0;
        fc.yTolerance = 6.0;
        fc.penTx = 0;

        fc.fillR = 0;
        fc.fillG = 0;
        fc.fillB = 0;
        fc.fillOpText = null;
        fc.trMode = 0;

        fc.xTolerance = 4.0;
        fc.bestResult = null;
        fc.bestGap = double.MaxValue;

        fc.lastOccurrenceGap = double.MaxValue;

    }
}
