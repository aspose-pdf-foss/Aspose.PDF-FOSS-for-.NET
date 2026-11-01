using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

internal static partial class FontSubsetter
{
    /// <summary>The stages of the CID font subset: one candidate font, one program's subset and one CMap rewrite.</summary>
    private static bool WriteCidSubsetMap(Aspose.Pdf.Core.PdfStream mapStream, HashSet<int> cids, byte[] data)
    {
        var sparse = new byte[data.Length];
        var kept = 0;
        foreach (var cid in cids)
        {
            var off = cid * 2;
            if (off + 1 >= data.Length) continue;
            sparse[off] = data[off];
            sparse[off + 1] = data[off + 1];
            kept++;
        }
        if (kept == 0) return true;
        mapStream.ReplaceData(sparse);
        mapStream.Dict.Remove("Filter");
        mapStream.Dict.Remove("DecodeParms");
        mapStream.Dict.Set("Length", new PdfInteger(sparse.Length));
        return true;
    }

    /// <summary></summary>
    private static bool SubsetCidProgram(CidSubsetState sc, Aspose.Pdf.Core.PdfStream stream, (HashSet<int> gids, List<(Aspose.Pdf.Core.PdfDictionary type0, Aspose.Pdf.Core.PdfDictionary descriptor)> fonts) entry)
    {
        byte[] fontData;
        try { fontData = sc.reader.DecodeStream(stream); } catch { return true; }
        if (fontData.Length < 12) return true;

        byte[] subsetData;
        try
        {
            var parser = new Text.TrueTypeParser(fontData);
            parser.Parse();
            var gids = entry.gids;
            var keepCmap = false;
            // A simple font sharing the program reads its glyphs through the program's cmap:
            // those glyphs, and the cmap, stay.
            if (sc.simpleUsers is not null && sc.simpleUsers.TryGetValue(stream, out var users))
            {
                gids = new HashSet<int>(entry.gids);
                foreach (var code in users.subsetCodes)
                    if (parser.CMap.TryGetValue(code, out var gid)) gids.Add(gid);
                keepCmap = true;
            }
            subsetData = new Text.TrueTypeSubsetter(fontData, parser).SubsetSparse(gids, keepCmap);
        }
        catch { return true; }

        if (subsetData.Length >= fontData.Length) return true;

        stream.ReplaceData(subsetData);
        stream.Dict.Remove("Filter");
        stream.Dict.Remove("DecodeParms");
        stream.Dict.Set("Length", new PdfInteger(subsetData.Length));
        stream.Dict.Set("Length1", new PdfInteger(subsetData.Length));
        foreach (var (type0, descriptor) in entry.fonts)
            TagCompositeSubset(sc.reader, type0, descriptor);
        return true;
    }

    /// <summary>A composite font whose program was just cut is named as a subset (ISO 32000-1 9.6.4), as a
    /// simple font the pass cuts is: its Type0 font, its CIDFont and its descriptor under one tag - the one the
    /// descriptor already carries, else a new one.</summary>
    private static void TagCompositeSubset(PdfReader reader, PdfDictionary type0, PdfDictionary descriptor)
    {
        var cidFont = reader.Resolve(type0.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants
            ? reader.ResolveDict(descendants[0]) : null;
        var held = descriptor.GetName("FontName");
        var tag = held is { Length: > SubsetTagLength } && held[SubsetTagLength - 1] == '+'
            ? held[..(SubsetTagLength - 1)] : GenerateSubsetPrefix();
        string Tagged(string name) =>
            $"{tag}+{(name.Length > SubsetTagLength && name[SubsetTagLength - 1] == '+' ? name[SubsetTagLength..] : name)}";
        if (type0.GetName("BaseFont") is { } typeName) type0.Set("BaseFont", new PdfName(Tagged(typeName)));
        if (cidFont?.GetName("BaseFont") is { } cidName) cidFont.Set("BaseFont", new PdfName(Tagged(cidName)));
        if (held is not null) descriptor.Set("FontName", new PdfName(Tagged(held)));
    }

    /// <summary>A subset tag with its plus sign: six capitals and '+'.</summary>
    private const int SubsetTagLength = 7;

    /// <summary></summary>
    private static bool CollectCidCandidate(CidSubsetState sc, Aspose.Pdf.Core.PdfDictionary fontDict, HashSet<int> charCodes)
    {
        if (fontDict.GetName("Subtype") != "Type0") return true;

        var descendants = sc.reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
        var cidFont = descendants is { Count: > 0 } ? sc.reader.ResolveDict(descendants[0]) : null;
        var descriptor = cidFont is null ? null : sc.reader.ResolveDict(cidFont.Get("FontDescriptor"));
        var fontFileRef = descriptor?.Get("FontFile2");
        if (fontFileRef is null) return true;
        var fontFileStream = sc.reader.ResolveStream(fontFileRef);
        var isPendingProgram = fontFileStream is null;
        if (fontFileStream is null && fontFileRef is PdfIndirectRef nref)
            fontFileStream = sc.resolveNewStream?.Invoke(nref.ObjectNumber);
        if (fontFileStream is null) return true;
        sc.drawnPrograms.Add(fontFileStream);
        // Conversion-time subsetting touches only the programs this conversion
        // just embedded (see the simple-font loop for the rationale).
        if (sc.newlyEmbeddedOnly && !isPendingProgram) return true;
        // The content holds codes; the CMap turns them into the CIDs the map and the
        // program are addressed by.
        var mapRef = cidFont!.Get("CIDToGIDMap");
        if (UsedCids(fontDict, charCodes) is not { } cids)
        {
            // Its glyphs cannot be told from the rest, so neither its program nor its map may be cut to
            // what another font sharing them shows.
            sc.unknownUse.Add(fontFileStream);
            if (mapRef is not null and not PdfName
                && (sc.reader.ResolveStream(mapRef) ?? (mapRef is PdfIndirectRef pending ? sc.resolveNewStream?.Invoke(pending.ObjectNumber) : null)) is { } sharedMap)
                sc.unknownUse.Add(sharedMap);
            return true;
        }

        // CID → GID: identity unless the descendant carries a CIDToGIDMap stream.
        var gids = new HashSet<int>();
        byte[]? cid2gid = null;
        if (mapRef is not null and not PdfName)
        {
            var mapStream = sc.reader.ResolveStream(mapRef);
            if (mapStream is null && mapRef is PdfIndirectRef mref)
                mapStream = sc.resolveNewStream?.Invoke(mref.ObjectNumber);
            if (mapStream is not null)
                try { cid2gid = sc.reader.DecodeStream(mapStream); } catch { /* a stream that does not decode is skipped, as if absent */ }
            if (cid2gid is not null && mapStream is not null)
            {
                if (!sc.byMap.TryGetValue(mapStream, out var me))
                    sc.byMap[mapStream] = me = (new HashSet<int>(), cid2gid);
                me.cids.UnionWith(cids);
            }
        }
        foreach (var cid in cids)
        {
            if (cid2gid is null) { gids.Add(cid); continue; }
            var off = cid * 2;
            if (off + 1 < cid2gid.Length)
                gids.Add((cid2gid[off] << 8) | cid2gid[off + 1]);
        }

        if (!sc.byProgram.TryGetValue(fontFileStream, out var entry))
        {
            entry = (new HashSet<int>(), new List<(PdfDictionary, PdfDictionary)>());
            sc.byProgram[fontFileStream] = entry;
        }
        entry.gids.UnionWith(gids);
        entry.fonts.Add((fontDict, descriptor!));
        return true;
    }
}
