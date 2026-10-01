using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class CidFontInfo
{
    /// <summary>The stages of the CID font build: the embedded CMap read and the descendant font read.</summary>
    private static void ReadDescendantFont(CidFontBuildState cb, PdfArray descArr)
    {
        var cidFontDict = cb.reader.ResolveDict(descArr[0]);
        if (cidFontDict is not null)
        {
            cb.cidToGid = ReadCidToGidMap(cidFontDict, cb.reader);
            // /CIDSystemInfo is a required entry on a CIDFont (§9.7.3).
            var sysInfo = cb.reader.ResolveDict(cidFontDict.Get("CIDSystemInfo"));
            if (sysInfo is not null && sysInfo.Get("Ordering") is PdfString os)
                cb.ordering = os.ToText();
            // Vertical-writing defaults (/DW2 = [vy w1], default [880 -1000],
            // PDF 32000 §9.7.4.3) and the per-CID /W2 overrides.
            if (cb.reader.Resolve(cidFontDict.Get("DW2")) is PdfArray dw2 && dw2.Count >= 2)
            {
                cb.vertOriginY = NumOf(dw2[0]);
                cb.vertAdvance = NumOf(dw2[1]);
            }
            cb.w2 = ReadW2(cidFontDict, cb.reader);
        }
    }

    /// <summary>The stages of the CID font build: the embedded CMap read and the descendant font read.</summary>
    private static void ReadEmbeddedCMap(CidFontBuildState cb)
    {
        try
        {
            var cmapBytes = cb.reader.DecodeStream(cb.encStream!);
            if (!cb.isTwoByte)
                cb.isTwoByte = CMapHasTwoByteCodespace(cmapBytes);
            cb.singleByteCMap = !cb.isTwoByte && CMapHasOnlySingleByteCodespace(cmapBytes);
            // Custom CMaps map byte-codes → CIDs via `cidchar` / `cidrange`.
            // Without this, codes hit `CidToGidMap[code]` directly, which
            // produces wrong glyphs (e.g. 0x0046 looked up as CID 70 instead
            // of CID 4 via the CMap's `<0046>4` entry).
            cb.cmapCodeToCid = ParseCMapCodeToCid(cmapBytes);
        }
        catch { }
    }
}
