using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Collect text ops: a TJ array's strings and kerns collected into one op.</summary>
    private static void ApplyTjArrayOp(CollectTextOpsState ct, PdfArray tjArr, int ep)
    {
        var sb = new StringBuilder();
        var byteBuf = new MemoryStream();
        double kernSum = 0;
        bool isHex = false; bool firstStr = true;
        // Where each kern falls WITHIN the run, so a wrap can measure a
        // prefix at its true width. A justified line carries most of its
        // inter-word space in these numbers (this corpus has -700-odd per
        // gap), and a prefix measured from glyph advances alone comes out
        // far too narrow — narrow enough to keep a word that must
        // wrap.
        var kernAt = new List<(int byteIndex, double amount)>();
        foreach (var item in tjArr)
        {
            if (item is PdfString ps)
            {
                sb.Append(DecodeString(ps.Value, ct.curToUnicode, ct.curFontDict, ct.reader));
                byteBuf.Write(ps.Value, 0, ps.Value.Length);
                if (firstStr) { isHex = ps.IsHex; firstStr = false; }
            }
            else if (item is PdfInteger ki)
            {
                kernSum += ki.Value;
                kernAt.Add(((int)byteBuf.Length, ki.Value));
            }
            else if (item is PdfReal kr)
            {
                kernSum += kr.Value;
                kernAt.Add(((int)byteBuf.Length, kr.Value));
            }
        }
        ct.textOps.Add(new CrossTextOp
        {
            Text = sb.ToString(), Bytes = byteBuf.ToArray(), IsHex = isHex,
            OpStart = ct.ops2[0].startPos, OpEnd = ep,
            TmA = ct.tmA, TmB = ct.tmB, TmC = ct.tmC, TmD = ct.tmD, TmTx = ct.tmTx, TmTy = ct.tmTy,
            CtmA = ct.ctmA, CtmB = ct.ctmB, CtmC = ct.ctmC, CtmD = ct.ctmD, CtmTx = ct.ctmTx, CtmTy = ct.ctmTy,
            FontDict = ct.curFontDict, FontName = ct.curFontName, ToUnicode = ct.curToUnicode,
            FontSize = ct.curFontSize, Tc = ct.curTc, KernSum = kernSum, KernAt = kernAt,
            BtStart = ct.curBtStart,
            TmPositioned = ct.pendingTm.has, TmXTokStart = ct.pendingTm.xStart,
            TmXTokEnd = ct.pendingTm.xEnd, TmXVal = ct.pendingTm.xVal,
            TdPositioned = ct.pendingTd.has, TdXTokStart = ct.pendingTd.xStart,
            TdXTokEnd = ct.pendingTd.xEnd, TdXVal = ct.pendingTd.xVal,
        });
        ct.pendingTm.has = false;
        ct.pendingTd.has = false;
        AdvanceText(ct, ct.textOps[^1].Bytes, kernSum);
    }
}
