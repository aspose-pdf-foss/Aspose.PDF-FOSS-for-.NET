using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Cross-operator replace: one match rewritten in place.</summary>
    private bool ReplaceNextMatch(CrossReplaceState cx)
    {
        if (ReplaceFirstOnly && cx.replaced) return false;

        var rm = new ReplaceMatchState();
        rm.msIdx = cx.searchIdx;
        rm.meIdx = cx.searchIdx + cx.searchLen - 1;
        while (rm.msIdx < rm.meIdx && rm.msIdx < cx.charToOp.Count && cx.charToOp[rm.msIdx] < 0) rm.msIdx++;
        while (rm.meIdx > rm.msIdx && rm.meIdx < cx.charToOp.Count && cx.charToOp[rm.meIdx] < 0) rm.meIdx--;
        rm.firstOp = rm.msIdx < cx.charToOp.Count ? cx.charToOp[rm.msIdx] : -1;
        rm.lastOp = rm.meIdx < cx.charToOp.Count ? cx.charToOp[rm.meIdx] : -1;

        rm.inTarget = rm.firstOp < 0 ||
            (IsAtTargetY(cx.textOps[rm.firstOp].TmTx, cx.textOps[rm.firstOp].TmTy,
                         cx.textOps[rm.firstOp].CtmB, cx.textOps[rm.firstOp].CtmD, cx.textOps[rm.firstOp].CtmTy)
             && IsAtTargetX(cx.textOps[rm.firstOp].TmTx, cx.textOps[rm.firstOp].TmTy,
                            cx.textOps[rm.firstOp].CtmA, cx.textOps[rm.firstOp].CtmC, cx.textOps[rm.firstOp].CtmTx));

        // Matches inside ONE operator belong to the per-op pass; cross-op only adds
        // value for spans covering multiple operators.
        if (rm.inTarget && rm.firstOp >= 0 && rm.lastOp >= 0 && rm.firstOp != rm.lastOp)
        {
            RewriteMatchedOps(cx, rm);
        }

        // Advance past the matched span (skipped single-op matches still advance
        // to avoid an infinite loop on regex zero-width corner cases).
        (cx.searchIdx, cx.searchLen) = NextMatch(cx, cx.searchIdx + Math.Max(cx.searchLen, 1));
        return true;
    }
}
