using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    // How an object is used, gathered over every traversal (Annex F.3 sorts objects by these).
    private const byte UsedByFirstPage = 1;
    private const byte UsedByOpenDocument = 2;
    private const byte UsedByOutlines = 4;

    // The catalog entries a viewer needs to open the document: their objects go with the
    // catalog, ahead of the first page (Annex F.3, part 4).
    private static readonly HashSet<string> OpenDocumentKeys =
        new() { "ViewerPreferences", "PageMode", "Threads", "OpenAction", "AcroForm" };

    private enum UserKind { Page, Thumbnail, Other, OpenDocument, Outlines }

    /// <summary>
    /// Records, for every object, what reaches it: each page (from the page object, never
    /// climbing back up through /Parent nor into another page), each page's thumbnail, each
    /// trailer entry and each catalog entry. A traversal never enters another page's object,
    /// so a link annotation's /P or a destination naming a page leaves that page alone.
    /// </summary>
    private static void MarkUses(LinearizeState lz)
    {
        var size = lz.maxNum + 1;
        lz.visitStamp = new int[size];
        lz.useFlags = new byte[size];
        lz.otherPageUses = new int[size];
        lz.firstOtherPage = new int[size];
        lz.thumbUses = new int[size];
        lz.otherUses = new int[size];
        lz.pageObjects = new List<int>[lz.pages.Count];
        var stack = new Stack<int>();
        var thumbs = new List<int>();
        for (var i = 0; i < lz.pages.Count; i++)
        {
            var page = lz.objs[lz.pages[i]];
            var parent = KeyIndex(page, "Parent");
            var thumb = KeyIndex(page, "Thumb");
            if (i > 0) lz.pageObjects[i] = new List<int>();
            lz.traversal++;
            thumbs.Clear();
            stack.Push(page.Num);
            Visit(lz, stack, UserKind.Page, i, page.Num);
            foreach (var r in page.Refs)
            {
                if (r.Key == parent) continue;
                if (r.Key == thumb) thumbs.Add(r.Target);
                else stack.Push(r.Target);
            }
            Visit(lz, stack, UserKind.Page, i, page.Num);
            foreach (var t in thumbs) stack.Push(t);
            Visit(lz, stack, UserKind.Thumbnail, i, page.Num);
        }

        if (lz.infoNum > 0) MarkFrom(lz, stack, new[] { lz.infoNum }, UserKind.Other);
        var catalog = lz.objs[lz.rootNum];
        for (var k = 0; k < catalog.Keys!.Count; k++)
        {
            var name = catalog.Keys[k].Name;
            var kind = OpenDocumentKeys.Contains(name) ? UserKind.OpenDocument
                : name == "Outlines" ? UserKind.Outlines : UserKind.Other;
            MarkFrom(lz, stack, catalog.Refs.Where(r => r.Key == k).Select(r => r.Target), kind);
        }
    }

    private static void MarkFrom(LinearizeState lz, Stack<int> stack, IEnumerable<int> starts, UserKind kind)
    {
        lz.traversal++;
        foreach (var s in starts) stack.Push(s);
        Visit(lz, stack, kind, -1, -1);
    }

    /// <summary>Drains the stack: every object reached is used by the given user once.</summary>
    private static void Visit(LinearizeState lz, Stack<int> stack, UserKind kind, int pageIndex, int topPage)
    {
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!lz.objs.TryGetValue(n, out var o) || lz.visitStamp[n] == lz.traversal) continue;
            if (n != topPage && (o.TypeIsPage || lz.pageSet.Contains(n))) continue;
            lz.visitStamp[n] = lz.traversal;
            RecordUse(lz, n, kind, pageIndex);
            if (n == topPage) continue; // the page's own entries are pushed by the caller
            foreach (var r in o.Refs) stack.Push(r.Target);
        }
    }

    private static void RecordUse(LinearizeState lz, int n, UserKind kind, int pageIndex)
    {
        switch (kind)
        {
            case UserKind.Page when pageIndex == 0:
                lz.useFlags[n] |= UsedByFirstPage;
                break;
            case UserKind.Page:
                if (lz.otherPageUses[n]++ == 0) lz.firstOtherPage[n] = pageIndex;
                lz.pageObjects[pageIndex].Add(n);
                break;
            case UserKind.Thumbnail: lz.thumbUses[n]++; break;
            case UserKind.OpenDocument: lz.useFlags[n] |= UsedByOpenDocument; break;
            case UserKind.Outlines: lz.useFlags[n] |= UsedByOutlines; break;
            default: lz.otherUses[n]++; break;
        }
    }
}
