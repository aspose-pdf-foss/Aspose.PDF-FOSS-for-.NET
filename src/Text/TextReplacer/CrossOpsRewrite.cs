using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Cross-operator replace: the matched operators rewritten with the replacement and the follower shift.</summary>
    private void RewriteMatchedOps(CrossReplaceState cx, ReplaceMatchState rm)
    {
        var mw = new MatchRewriteState();
        mw.fo = cx.textOps[rm.firstOp];
        mw.lo = cx.textOps[rm.lastOp];
        mw.prefixText = mw.fo.Text.Substring(0, Compat.Clamp(rm.msIdx - mw.fo.CharStart, 0, mw.fo.Text.Length));
        mw.matchedLastLen = Compat.Clamp(rm.meIdx - mw.lo.CharStart + 1, 0, mw.lo.Text.Length);
        mw.suffixText = mw.lo.Text.Substring(mw.matchedLastLen);

        mw.prefixBytes = mw.prefixText.Length > 0 ? EncodeString(mw.prefixText, mw.fo.ToUnicode, mw.fo.FontDict) : Array.Empty<byte>();
        mw.suffixBytes = mw.suffixText.Length > 0 ? EncodeString(mw.suffixText, mw.lo.ToUnicode, mw.lo.FontDict) : Array.Empty<byte>();
        mw.matchedLastBytes = mw.matchedLastLen > 0 ? EncodeString(mw.lo.Text.Substring(0, mw.matchedLastLen), mw.lo.ToUnicode, mw.lo.FontDict) : Array.Empty<byte>();

        mw.advMatched = 0.0;
        if (ShiftFollowersByAdvance)
        {
            var matchedFirstText = mw.fo.Text.Substring(Math.Min(mw.prefixText.Length, mw.fo.Text.Length));
            if (matchedFirstText.Length > 0)
                mw.advMatched += Adv(cx, mw.fo, EncodeString(matchedFirstText, mw.fo.ToUnicode, mw.fo.FontDict), own: false);
            for (var oi = rm.firstOp + 1; oi < rm.lastOp; oi++)
                mw.advMatched += Adv(cx, cx.textOps[oi], cx.textOps[oi].Bytes, own: true);
            if (mw.matchedLastBytes.Length > 0)
                mw.advMatched += Adv(cx, mw.lo, mw.matchedLastBytes, own: false);
        }

        // Copy everything before the first matched operator.
        CopyRange(cx, mw.fo.OpStart);

        // Prefix (kept head of the first op) stays in the original font at the
        // original pen position.
        WriteReplacementRun(cx, rm, mw);

        // Last operator: keep the tail after the match, re-anchored with an
        // absolute Tm. Reflow mode puts it at match-start + replacement width
        // (the same-line reflow); otherwise it keeps its original X.
        cx.lastWrite = cx.textOps[rm.lastOp - 1 >= rm.firstOp ? rm.lastOp - 1 : rm.firstOp].OpEnd;
        CopyRange(cx, mw.lo.OpStart);
        mw.advPrefix = mw.prefixBytes.Length > 0 ? Adv(cx, mw.fo, mw.prefixBytes, own: false) : 0;
        mw.advMatchedLast = mw.matchedLastBytes.Length > 0 ? Adv(cx, mw.lo, mw.matchedLastBytes, own: false) : 0;
        mw.oldSuffixTmX = mw.lo.TmTx + mw.advMatchedLast;
        mw.newSuffixTmX = ReflowLineOnReplace ? mw.fo.TmTx + mw.advPrefix + mw.advRepl : mw.oldSuffixTmX;
        if (mw.suffixBytes.Length > 0)
        {
            // Leading space: the copied gap can end in a keyword ("… Tc") with no
            // trailing delimiter, and "Tc1 0 0 …" would lex as an unknown keyword.
            cx.result.Write(Encoding.ASCII.GetBytes(
                $" {FormatNum(cx, mw.lo.TmA)} {FormatNum(cx, mw.lo.TmB)} {FormatNum(cx, mw.lo.TmC)} {FormatNum(cx, mw.lo.TmD)} {FormatNum(cx, mw.newSuffixTmX)} {FormatNum(cx, mw.lo.TmTy)} Tm "));
            WriteStringOperand(cx.result, mw.suffixBytes, mw.lo.IsHex);
            cx.result.Write(Encoding.ASCII.GetBytes(" Tj"));
        }
        else
            cx.result.Write(Encoding.ASCII.GetBytes("() Tj"));

        cx.lastWrite = mw.lo.OpEnd;
        _replacementCount++;
        cx.replaced = true;

        mw.delta = mw.oldSuffixTmX - mw.newSuffixTmX;
        ShiftFollowers(cx, rm, mw);
    }

    /// <summary></summary>
    private void ShiftFollowers(CrossReplaceState cx, ReplaceMatchState rm, MatchRewriteState mw)
    {
        if (Math.Abs(mw.delta) > 0.01)
        {
            for (var j = rm.lastOp + 1; j < cx.textOps.Count; j++)
            {
                var fl = cx.textOps[j];
                if (!fl.TmPositioned) continue;
                if (fl.TmXTokStart < cx.lastWrite) continue;
                bool sameCtm = Math.Abs(fl.CtmA - mw.lo.CtmA) < 1e-6 && Math.Abs(fl.CtmC - mw.lo.CtmC) < 1e-6
                    && Math.Abs(fl.CtmD - mw.lo.CtmD) < 1e-6 && Math.Abs(fl.CtmTx - mw.lo.CtmTx) < 1e-6
                    && Math.Abs(fl.CtmTy - mw.lo.CtmTy) < 1e-6;
                if (!sameCtm || Math.Abs(fl.TmTy - mw.lo.TmTy) >= 2.0) continue;
                if (fl.TmXVal <= mw.fo.TmTx) continue;
                cx.patches[fl.TmXTokStart] = (fl.TmXTokEnd,
                    Encoding.ASCII.GetBytes(FormatNum(cx, fl.TmXVal - mw.delta)));
            }
        }

        // AdjustSpaceWidth: the words AFTER the replacement sit behind its new
        // advance even when the line is not reflowed. A Tm-positioned line re-states
        // an absolute Tm at the shifted x before every following glyph; on a
        // Td-chained line one relative move carries them all, so nudge the first
        // Td after the match and let the chain do the rest.
        if (ShiftFollowersByAdvance && !ReflowLineOnReplace)
        {
            var advDelta = mw.advRepl - mw.advMatched;
            if (Math.Abs(advDelta) > 0.01)
                for (var j = rm.lastOp + 1; j < cx.textOps.Count; j++)
                {
                    var fl = cx.textOps[j];
                    if (Math.Abs(fl.TmTy - mw.lo.TmTy) >= 2.0) continue;
                    if (!fl.TdPositioned || fl.TdXTokStart < cx.lastWrite) continue;
                    cx.patches[fl.TdXTokStart] = (fl.TdXTokEnd,
                        Encoding.ASCII.GetBytes(FormatNum(cx, fl.TdXVal + advDelta)));
                    _shiftedFollowers = true;
                    break;
                }
        }
    }

    /// <summary></summary>
    private void WriteReplacementRun(CrossReplaceState cx, ReplaceMatchState rm, MatchRewriteState mw)
    {
        if (mw.prefixBytes.Length > 0)
        {
            WriteStringOperand(cx.result, mw.prefixBytes, mw.fo.IsHex);
            cx.result.Write(Encoding.ASCII.GetBytes(" Tj "));
        }

        if (cx.replacement.Length > 0 && CanEncodeInFont(cx, mw.fo, cx.replacement))
        {
            var replBytes = EncodeString(cx.replacement, mw.fo.ToUnicode, mw.fo.FontDict);
            WriteStringOperand(cx.result, replBytes, mw.fo.IsHex);
            cx.result.Write(Encoding.ASCII.GetBytes(" Tj "));
            mw.advRepl = Adv(cx, mw.fo, replBytes, own: false);
        }
        else if (cx.replacement.Length > 0)
        {
            WriteFontSwitchedReplacement(cx.result, cx.replacement, mw.fo.FontDict,
                mw.fo.FontName, mw.fo.FontSize, cx.pageDict, cx.reader, "Tj", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
            cx.result.WriteByte((byte)' ');
            double est = 0;
            foreach (var ch in cx.replacement)
            {
                var cw = ch <= 0xFF ? Standard14Fonts.GetWidth("Helvetica", ch) : 0;
                est += cw > 0 ? cw : 500;
            }
            mw.advRepl = est / 1000.0 * mw.fo.FontSize;
        }
        else
            mw.advRepl = 0;

        // Middle operators: keep their positioning/state gaps, blank the text.
        for (var oi = rm.firstOp + 1; oi < rm.lastOp; oi++)
        {
            cx.lastWrite = cx.textOps[oi - 1].OpEnd;
            CopyRange(cx, cx.textOps[oi].OpStart);
            cx.result.Write(Encoding.ASCII.GetBytes("() Tj "));
        }
    }
}
