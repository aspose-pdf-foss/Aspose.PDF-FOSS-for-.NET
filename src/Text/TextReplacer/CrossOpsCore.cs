using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>charToOp marker of a space the joiner synthesised between two runs of one
    /// line; a needle space matches it or steps over it.</summary>
    private const int SyntheticSpaceOp = -1;

    /// <summary>charToOp marker of the break between runs on different lines; no needle
    /// character matches it and none steps over it.</summary>
    private const int LineBreakOp = -2;

    private byte[]? TryCrossOperatorReplaceCore(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader, string normalizedSearch, List<CrossTextOp> textOps)
    {
        var cx = new CrossReplaceState();
        cx.streamBytes = streamBytes;
        cx.search = search;
        cx.replacement = replacement;
        cx.pageDict = pageDict;
        cx.reader = reader;
        cx.normalizedSearch = normalizedSearch;
        cx.textOps = textOps;
        cx.metricsCache = new Dictionary<PdfDictionary, FontMetrics?>();
        cx.allText = new StringBuilder();
        cx.charToOp = new List<int>();
        for (var i = 0; i < cx.textOps.Count; i++)
        {
            var cur = cx.textOps[i];
            if (i > 0 && cx.allText.Length > 0)
            {
                var prev = cx.textOps[i - 1];
                bool sameCtm = Math.Abs(cur.CtmA - prev.CtmA) < 1e-6 && Math.Abs(cur.CtmC - prev.CtmC) < 1e-6
                    && Math.Abs(cur.CtmD - prev.CtmD) < 1e-6 && Math.Abs(cur.CtmTx - prev.CtmTx) < 1e-6
                    && Math.Abs(cur.CtmTy - prev.CtmTy) < 1e-6;
                bool horizontal = Math.Abs(cur.TmB) <= Math.Abs(cur.TmA) && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA);
                if (sameCtm && horizontal && Math.Abs(cur.TmTy - prev.TmTy) < 2.0)
                {
                    var gap = cur.TmTx - (prev.TmTx + Adv(cx, prev, prev.Bytes, own: true));
                    var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
                    var lastChar = cx.allText[^1];
                    var nextChar = cur.Text.Length > 0 ? cur.Text[0] : '\0';
                    if (gap > 0.2 * fs && gap <= 3.0 * fs && lastChar != ' ' && nextChar != ' ')
                    {
                        cx.charToOp.Add(SyntheticSpaceOp);
                        cx.allText.Append(' ');
                    }
                }
                else
                {
                    // A run seated on another line (or under another CTM) does not
                    // continue this one: "Lorem " ending a line and "Ipsum" opening the
                    // next never spell the phrase, so a break that no needle can cross
                    // separates them.
                    cx.charToOp.Add(LineBreakOp);
                    cx.allText.Append('\n');
                }
            }
            cur.CharStart = cx.allText.Length;
            foreach (var _ in cur.Text) cx.charToOp.Add(i);
            cx.allText.Append(cur.Text);
        }

        cx.fullText = NormalizeForSearch(cx.allText.ToString());

        (cx.searchIdx, cx.searchLen) = NextMatch(cx, 0);
        if (cx.searchIdx < 0) return null;

        cx.patches = new SortedList<int, (int end, byte[] text)>();
        cx.result = new MemoryStream();
        cx.lastWrite = 0;
        cx.replaced = false;
        while (cx.searchIdx >= 0)
        {
            if (!ReplaceNextMatch(cx)) break;
        }

        if (!cx.replaced) return null;

        CopyRange(cx, cx.streamBytes.Length);

        return cx.result.ToArray();
    }
}
