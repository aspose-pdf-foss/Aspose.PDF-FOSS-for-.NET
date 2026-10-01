using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The stages of the PUA symbolic font conversion: one font resource.</summary>
    private bool ConvertPuaSymbolicFont(PuaSymbolicConvertState pu, string key)
    {
        pu.fontDict = _reader.ResolveDict(pu.fonts!.Get(key));
        if (pu.fontDict is null || pu.fontDict.GetName("Subtype") != "TrueType") return true;
        pu.descriptor = _reader.ResolveDict(pu.fontDict.Get("FontDescriptor"));
        pu.ff2 = pu.descriptor is not null ? _reader.ResolveStream(pu.descriptor.Get("FontFile2")) : null;
        if (pu.descriptor is null || pu.ff2 is null) return true;
        pu.flags = (_reader.Resolve(pu.descriptor.Get("Flags")) as PdfInteger)?.Value ?? 0;
        if ((pu.flags & 4) == 0) return true; // symbolic flag

        try { pu.cmap = new Text.GlyphOutlineParser(_reader.DecodeStream(pu.ff2)).CMap; }
        catch { return true; }

        pu.tfPattern = new System.Text.RegularExpressions.Regex(
            @"/" + System.Text.RegularExpressions.Regex.Escape(key) + @"\s+([\d.]+)\s+Tf");
        if (!pu.tfPattern.IsMatch(pu.content)) return true;

        pu.firstChar = (int)((_reader.Resolve(pu.fontDict.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
        pu.widths = _reader.Resolve(pu.fontDict.Get("Widths")) as PdfArray;
        pu.usedGidWidths = new SortedDictionary<int, double>();
        pu.anyPua = false;

        pu.newKey = $"C{pu.converted}_0";
        pu.rewritten = new StringBuilder();
        pu.pos = 0;
        foreach (System.Text.RegularExpressions.Match m in pu.tfPattern.Matches(pu.content))
        {
            if (!RewritePuaShowRun(pu, m)) break;
        }
        pu.rewritten.Append(pu.content, pu.pos, pu.content.Length - pu.pos);

        if (!pu.anyPua) return true; // not a PUA usage — leave the font/content untouched

        pu.content = pu.rewritten.ToString();
        pu.converted++;

        pu.cidSystemInfo = new PdfDictionary();
        pu.cidSystemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
        pu.cidSystemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
        pu.cidSystemInfo.Set("Supplement", new PdfInteger(0));

        pu.wArr = new PdfArray();
        foreach (var (gid, w) in pu.usedGidWidths)
        {
            pu.wArr.Add(new PdfInteger(gid));
            var inner = new PdfArray();
            inner.Add(new PdfReal(w));
            pu.wArr.Add(inner);
        }

        pu.baseFontName = pu.fontDict.GetName("BaseFont") ?? "Unknown";
        pu.cidFont = new PdfDictionary();
        pu.cidFont.Set("Type", new PdfName("Font"));
        pu.cidFont.Set("Subtype", new PdfName("CIDFontType2"));
        pu.cidFont.Set("BaseFont", new PdfName(pu.baseFontName));
        pu.cidFont.Set("CIDSystemInfo", pu.cidSystemInfo);
        pu.cidFont.Set("FontDescriptor", pu.fontDict.Get("FontDescriptor")!);
        pu.cidFont.Set("DW", new PdfInteger(1000));
        if (pu.wArr.Count > 0) pu.cidFont.Set("W", pu.wArr);
        pu.cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

        pu.type0 = new PdfDictionary();
        pu.type0.Set("Type", new PdfName("Font"));
        pu.type0.Set("Subtype", new PdfName("Type0"));
        pu.type0.Set("BaseFont", new PdfName(pu.baseFontName));
        pu.type0.Set("Encoding", new PdfName("Identity-H"));
        pu.descendants = new PdfArray();
        pu.descendants.Add(pu.cidFont);
        pu.type0.Set("DescendantFonts", pu.descendants);
        pu.fonts.Set(pu.newKey, pu.type0);
        return true;
    }

    /// <summary></summary>
    private bool RewritePuaShowRun(PuaSymbolicConvertState pu, System.Text.RegularExpressions.Match m)
    {
        if (m.Index < pu.pos) return true;
        pu.rewritten.Append(pu.content, pu.pos, m.Index - pu.pos);
        pu.rewritten.Append('/').Append(pu.newKey).Append(' ').Append(m.Groups[1].Value).Append(" Tf");
        pu.pos = m.Index + m.Length;

        // Until the NEXT Tf or ET, re-encode literal show strings.
        var segEnd = pu.content.Length;
        var nextTf = pu.content.IndexOf(" Tf", pu.pos, StringComparison.Ordinal);
        var nextEt = pu.content.IndexOf("ET", pu.pos, StringComparison.Ordinal);
        if (nextTf >= 0)
        {
            // back up to the start of the /Name that belongs to that Tf
            var nameStart = pu.content.LastIndexOf('/', nextTf);
            if (nameStart > pu.pos) segEnd = Math.Min(segEnd, nameStart);
        }
        if (nextEt >= 0) segEnd = Math.Min(segEnd, nextEt);

        var segment = pu.content[pu.pos..segEnd];
        segment = System.Text.RegularExpressions.Regex.Replace(segment,
            @"\(((?:[^()\\]|\\.)*)\)\s*(Tj|'\s*)",
            sm =>
            {
                var raw = UnescapePdfLiteral(sm.Groups[1].Value);
                var hex = new StringBuilder();
                var actual = new StringBuilder();
                foreach (var ch in raw)
                {
                    var code = (int)ch;
                    if (CodeIsPua(pu, code)) pu.anyPua = true;
                    var gid = CodeToGid(pu, code);
                    hex.Append(gid.ToString("X4"));
                    double w = 0;
                    if (pu.widths is not null && code - pu.firstChar >= 0 && code - pu.firstChar < pu.widths.Count)
                        w = _reader.Resolve(pu.widths[code - pu.firstChar]) switch
                        {
                            PdfInteger wi => wi.Value,
                            PdfReal wr => wr.Value,
                            _ => 0,
                        };
                    if (gid > 0) pu.usedGidWidths[gid] = w;
                    actual.Append(' '); // PUA glyph: no Unicode meaning; marked as a space
                }
                var op = sm.Groups[2].Value.TrimEnd();
                return $"/Span <</ActualText ({actual})>> BDC\n<{hex}> {op}\nEMC";
            });
        pu.rewritten.Append(segment);
        pu.pos = segEnd;
        return true;
    }
    private static int CodeToGid(PuaSymbolicConvertState pu, int code) =>
        pu.cmap.TryGetValue(0xF000 | code, out var g) ? g
        : pu.cmap.TryGetValue(code, out var g2) ? g2 : 0;

    private static bool CodeIsPua(PuaSymbolicConvertState pu, int code) => pu.cmap.ContainsKey(0xF000 | code);
}
