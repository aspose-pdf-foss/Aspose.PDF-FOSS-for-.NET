using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    /// <summary>Where Annex F puts an object, from what uses it.</summary>
    private enum Placement
    {
        Unused, Catalog, OpenDocument, Outlines, FirstPagePrivate, FirstPageShared,
        OtherPagePrivate, OtherPageShared, Other,
    }

    private static Placement Classify(LinearizeState lz, int n)
    {
        if (n == lz.rootNum) return Placement.Catalog;
        var flags = lz.useFlags[n];
        if ((flags & UsedByOutlines) != 0) return Placement.Outlines;
        if ((flags & UsedByOpenDocument) != 0) return Placement.OpenDocument;
        var others = lz.otherUses[n];
        var otherPages = lz.otherPageUses[n];
        var thumbs = lz.thumbUses[n];
        if ((flags & UsedByFirstPage) != 0)
            return others == 0 && otherPages == 0 && thumbs == 0 ? Placement.FirstPagePrivate : Placement.FirstPageShared;
        if (otherPages == 1 && others == 0 && thumbs == 0) return Placement.OtherPagePrivate;
        if (otherPages > 1) return Placement.OtherPageShared;
        return others > 0 || thumbs > 0 || otherPages > 0 ? Placement.Other : Placement.Unused;
    }

    /// <summary>
    /// Sorts the objects into the sections of a linearized file (Annex F.3) and numbers them so
    /// the hint tables can name them by count: the catalog and the objects that open the
    /// document (part 4); the first page with every object it uses (part 6); each other page
    /// followed by the objects only it uses (part 7); the objects several other pages share
    /// (part 8); everything else (part 9), the outline objects in one run headed by the outline
    /// dictionary. Parts 7 to 9 take the numbers from 1, then the linearization dictionary,
    /// part 4, part 6 and the hint stream follow, so the first-page cross-reference section
    /// is one run of numbers.
    /// </summary>
    private static void OrderSections(LinearizeState lz)
    {
        var groups = new Dictionary<Placement, List<RawObject>>();
        foreach (var o in lz.objs.Values.OrderBy(o => o.Num))
        {
            var placement = Classify(lz, o.Num);
            if (!groups.TryGetValue(placement, out var list)) groups[placement] = list = new List<RawObject>();
            list.Add(o);
        }
        List<RawObject> Group(Placement p) => groups.TryGetValue(p, out var list) ? list : new List<RawObject>();

        var firstPage = lz.objs[lz.pages[0]];
        lz.part4 = new List<RawObject> { lz.objs[lz.rootNum] };
        lz.part4.AddRange(Group(Placement.OpenDocument));
        var part6 = new List<RawObject> { firstPage };
        part6.AddRange(Group(Placement.FirstPagePrivate).Where(o => o != firstPage));
        part6.AddRange(Group(Placement.FirstPageShared));
        var outlines = OutlineRun(lz, Group(Placement.Outlines));
        var outlinesFirst = CatalogNameIs(lz, "PageMode", "UseOutlines");
        var pageRuns = OtherPageRuns(lz);
        var part8 = Group(Placement.OtherPageShared);
        var part9 = new List<RawObject>();
        if (!outlinesFirst) part9.AddRange(outlines);
        part9.AddRange(Group(Placement.Other));
        part9.AddRange(Group(Placement.Unused));

        if (lz.packObjects)
        {
            part6 = PackSection(lz, part6, PackableWithPage, lz.packedFirst);
            for (var i = 0; i < pageRuns.Count; i++) pageRuns[i] = PackSection(lz, pageRuns[i], PackableWithPage, lz.packed);
            part8 = PackSection(lz, part8, PackableWithPage, lz.packed);
            part9 = PackSection(lz, part9, PackableAside, lz.packed);
        }
        if (outlinesFirst) part6.AddRange(outlines);
        lz.part6 = part6;
        lz.part7 = pageRuns.SelectMany(run => run).ToList();
        lz.part8 = part8;
        lz.part9 = part9;
        lz.pageObjectCounts = new int[lz.pages.Count];
        lz.pageObjectCounts[0] = part6.Count;
        for (var i = 1; i < lz.pages.Count; i++) lz.pageObjectCounts[i] = pageRuns[i - 1].Count;
        NumberObjects(lz);
    }

    /// <summary>
    /// Parts 7 to 9 take the numbers from 1, then the linearization dictionary, part 4, part 6
    /// and the hint stream follow, so each cross-reference section is one run of numbers. A
    /// cross-reference stream lists a section's packed objects after all the others, so they
    /// take their section's last numbers.
    /// </summary>
    private static void NumberObjects(LinearizeState lz)
    {
        var packs = lz.packed.Count + lz.packedFirst.Count > 0;
        var next = 1;
        foreach (var part in new[] { lz.part7, lz.part8, lz.part9 })
            foreach (var o in part) o.NewNum = next++;
        if (packs) lz.mainXrefNum = next++;
        foreach (var o in lz.packed) o.NewNum = next++;
        lz.secondHalfCount = next - 1;
        lz.linObjNum = next++;
        if (packs) lz.firstXrefNum = next++;
        foreach (var part in new[] { lz.part4, lz.part6 })
            foreach (var o in part) o.NewNum = next++;
        lz.hintObjNum = next++;
        foreach (var o in lz.packedFirst) o.NewNum = next++;
        lz.size = next;
    }

    /// <summary>Each page after the first, followed by the objects only it uses.</summary>
    private static List<List<RawObject>> OtherPageRuns(LinearizeState lz)
    {
        var runs = new List<List<RawObject>>();
        for (var i = 1; i < lz.pages.Count; i++)
        {
            var page = lz.pages[i];
            var run = new List<RawObject> { lz.objs[page] };
            foreach (var n in lz.pageObjects[i])
                if (n != page && Classify(lz, n) == Placement.OtherPagePrivate) run.Add(lz.objs[n]);
            runs.Add(run);
        }
        return runs;
    }

    /// <summary>The outline objects with the outline dictionary first, as the outline hint table
    /// names them by the dictionary and a count.</summary>
    private static List<RawObject> OutlineRun(LinearizeState lz, List<RawObject> outlines)
    {
        var root = FirstRefUnder(lz.objs[lz.rootNum], "Outlines");
        var head = outlines.FirstOrDefault(o => o.Num == root);
        if (head is null) return outlines;
        lz.outlinesRoot = head;
        lz.outlineObjectCount = outlines.Count;
        var run = new List<RawObject>(outlines.Count) { head };
        run.AddRange(outlines.Where(o => o != head));
        return run;
    }

    /// <summary>Whether a catalog entry is the given name.</summary>
    private static bool CatalogNameIs(LinearizeState lz, string key, string name)
    {
        var catalog = lz.objs[lz.rootNum];
        var index = KeyIndex(catalog, key);
        if (index < 0) return false;
        var k = catalog.Keys![index];
        var text = Encoding.ASCII.GetString(catalog.Buf, k.ValueStart, k.ValueEnd - k.ValueStart).Trim();
        return text == "/" + name;
    }
}
