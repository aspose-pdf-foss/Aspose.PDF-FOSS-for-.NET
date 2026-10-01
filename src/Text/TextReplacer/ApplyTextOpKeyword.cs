using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Collect text ops: one content operator applied to the collection state.</summary>
    private static void ApplyTextOpKeyword(CollectTextOpsState ct, Token tok, int sp, int ep)
    {
        var op = tok.StringValue!;
        if (op == "Tf" && ct.ops2.Count >= 2 && ct.ops2[0].obj is PdfName fn)
        {
            ct.curFontName = fn.Value;
            if (ct.ops2[1].obj is PdfInteger fsi) ct.curFontSize = fsi.Value;
            else if (ct.ops2[1].obj is PdfReal fsr) ct.curFontSize = fsr.Value;
            if (ct.fonts.TryGetValue(ct.curFontName, out var fd))
            { ct.curFontDict = fd; ct.curToUnicode = TextAbsorber.ParseToUnicodeFromDict(fd, ct.reader); }
            else { ct.curFontDict = null; ct.curToUnicode = null; }
        }
        else if (op == "Tc" && ct.ops2.Count >= 1)
            ct.curTc = ToDouble(ct.ops2[0].obj);
        else if (op == "Tw" && ct.ops2.Count >= 1)
            ct.curTw = ToDouble(ct.ops2[0].obj);
        else if (op is "Tj" or "'" && ct.ops2.Count >= 1 && ct.ops2[0].obj is PdfString s)
        {
            if (op == "'") { ct.tlmTx = -ct.tlLeading * ct.tmC + ct.tlmTx; ct.tlmTy = -ct.tlLeading * ct.tmD + ct.tlmTy; ct.tmTx = ct.tlmTx; ct.tmTy = ct.tlmTy; ct.pendingTm.has = false; }
            var decoded = DecodeString(s.Value, ct.curToUnicode, ct.curFontDict, ct.reader);
            ct.textOps.Add(new CrossTextOp
            {
                Text = decoded, Bytes = s.Value, IsHex = s.IsHex,
                OpStart = ct.ops2[0].startPos, OpEnd = ep,
                TmA = ct.tmA, TmB = ct.tmB, TmC = ct.tmC, TmD = ct.tmD, TmTx = ct.tmTx, TmTy = ct.tmTy,
                CtmA = ct.ctmA, CtmB = ct.ctmB, CtmC = ct.ctmC, CtmD = ct.ctmD, CtmTx = ct.ctmTx, CtmTy = ct.ctmTy,
                FontDict = ct.curFontDict, FontName = ct.curFontName, ToUnicode = ct.curToUnicode,
                FontSize = ct.curFontSize, Tc = ct.curTc, BtStart = ct.curBtStart,
                TmPositioned = ct.pendingTm.has, TmXTokStart = ct.pendingTm.xStart,
                TmXTokEnd = ct.pendingTm.xEnd, TmXVal = ct.pendingTm.xVal,
                TdPositioned = ct.pendingTd.has, TdXTokStart = ct.pendingTd.xStart,
                TdXTokEnd = ct.pendingTd.xEnd, TdXVal = ct.pendingTd.xVal,
            });
            ct.pendingTm.has = false;
            ct.pendingTd.has = false;
            AdvanceText(ct, s.Value, 0);
        }
        else if (op == "TJ" && ct.ops2.Count >= 1 && ct.ops2[0].obj is PdfArray tjArr)
        {
            ApplyTjArrayOp(ct, tjArr, ep);
        }
        else if (op == "BT") { ct.tmA = 1; ct.tmB = 0; ct.tmC = 0; ct.tmD = 1; ct.tmTx = 0; ct.tmTy = 0; ct.tlmTx = 0; ct.tlmTy = 0; ct.tlLeading = 0; ct.pendingTm.has = false; ct.pendingTd.has = false; ct.curBtStart = sp; }
        else if ((op == "Td" || op == "TD") && ct.ops2.Count >= 2)
        {
            double dx = ToDouble(ct.ops2[0].obj), dy = ToDouble(ct.ops2[1].obj);
            ct.tlmTx = dx * ct.tmA + dy * ct.tmC + ct.tlmTx;
            ct.tlmTy = dx * ct.tmB + dy * ct.tmD + ct.tlmTy;
            ct.tmTx = ct.tlmTx; ct.tmTy = ct.tlmTy;
            if (op == "TD") ct.tlLeading = -dy;
            ct.pendingTm.has = false; // Td-positioned: inherits the line chain, no Tm patch
            ct.pendingTd = (true, ct.ops2[0].startPos, ct.ops2[0].endPos, dx);
        }
        else if (op == "Tm" && ct.ops2.Count >= 6)
        {
            ct.tmA = ToDouble(ct.ops2[0].obj); ct.tmB = ToDouble(ct.ops2[1].obj);
            ct.tmC = ToDouble(ct.ops2[2].obj); ct.tmD = ToDouble(ct.ops2[3].obj);
            ct.tmTx = ToDouble(ct.ops2[4].obj); ct.tmTy = ToDouble(ct.ops2[5].obj);
            ct.tlmTx = ct.tmTx; ct.tlmTy = ct.tmTy;
            ct.pendingTm = (true, ct.ops2[4].startPos, ct.ops2[4].endPos, ct.tmTx);
            ct.pendingTd.has = false;
        }
        else if (op == "TL" && ct.ops2.Count >= 1) ct.tlLeading = ToDouble(ct.ops2[0].obj);
        else if (op == "T*") { ct.tlmTx = -ct.tlLeading * ct.tmC + ct.tlmTx; ct.tlmTy = -ct.tlLeading * ct.tmD + ct.tlmTy; ct.tmTx = ct.tlmTx; ct.tmTy = ct.tlmTy; ct.pendingTm.has = false; }
        // The text state rides in the graphics state (PDF 32000-1 Table 52), so
        // `q`/`Q` save and restore the font, its SIZE and the spacing parameters
        // as well as the CTM. A `q /F 1 Tf ... Q` block that leaks its size out
        // makes every later run measure at 1 pt, and a re-flow then never wraps.
        else if (op == "q")
        {
            ct.ctmStack.Push((ct.ctmA, ct.ctmB, ct.ctmC, ct.ctmD, ct.ctmTx, ct.ctmTy));
            ct.tsStack.Push((ct.curFontSize, ct.curFontName, ct.curFontDict, ct.curToUnicode, ct.curTc, ct.curTw, ct.tlLeading));
        }
        else if (op == "Q")
        {
            if (ct.ctmStack.Count > 0) (ct.ctmA, ct.ctmB, ct.ctmC, ct.ctmD, ct.ctmTx, ct.ctmTy) = ct.ctmStack.Pop();
            if (ct.tsStack.Count > 0)
                (ct.curFontSize, ct.curFontName, ct.curFontDict, ct.curToUnicode, ct.curTc, ct.curTw, ct.tlLeading) = ct.tsStack.Pop();
        }
        else if (op == "cm" && ct.ops2.Count >= 6)
        {
            double a = ToDouble(ct.ops2[0].obj), b = ToDouble(ct.ops2[1].obj), c = ToDouble(ct.ops2[2].obj);
            double dd = ToDouble(ct.ops2[3].obj), tx = ToDouble(ct.ops2[4].obj), ty = ToDouble(ct.ops2[5].obj);
            double nA = a * ct.ctmA + b * ct.ctmC, nB = a * ct.ctmB + b * ct.ctmD;
            double nC = c * ct.ctmA + dd * ct.ctmC, nD = c * ct.ctmB + dd * ct.ctmD;
            double nTx = tx * ct.ctmA + ty * ct.ctmC + ct.ctmTx, nTy = tx * ct.ctmB + ty * ct.ctmD + ct.ctmTy;
            ct.ctmA = nA; ct.ctmB = nB; ct.ctmC = nC; ct.ctmD = nD; ct.ctmTx = nTx; ct.ctmTy = nTy;
        }
        ct.ops2.Clear();
    }
}
