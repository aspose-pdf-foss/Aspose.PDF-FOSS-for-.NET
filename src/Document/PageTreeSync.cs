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
    private void RebuildPagesTree(PdfWriter writer)
    {
        // Determine the actual /Pages object number from the catalog
        var pagesRef = _reader.Catalog.Get("Pages");
        var pagesObjNum = pagesRef is PdfIndirectRef pr ? pr.ObjectNumber : 2;

        // Reserve the writer's number space above every cross-document import slot so a
        // writer-allocated page number can't collide with a slot — including slots for
        // destination-only pages that are referenced but never written.
        if (_pages is not null)
            writer.ReserveObjectNumber(_pages.ImportSlotHighWater);

        // Build a new /Pages dict with all current pages as kids
        var kids = new PdfArray();
        foreach (var page in Pages)
        {
            // Choose each page's object number:
            //  - an imported page is written at its reserved slot so GoTo/Link destinations
            //    that target it (and point at that slot) resolve to this copy;
            //  - a page loaded from THIS document keeps its original object number, so the
            //    document's own internal links (bookmarks, link annotations, named
            //    destinations) — which reference pages by object number — still resolve
            //    after pages are deleted or reordered (imported pages carry
            //    SourceObjectNumber = -1, so they never take this branch);
            //  - a newly created page takes a fresh writer-allocated number.
            int objNum;
            if (page.ImportSlotObjNum > 0) objNum = page.ImportSlotObjNum;
            else if (page.SourceObjectNumber > 0) objNum = page.SourceObjectNumber;
            else objNum = writer.AllocateObjectNumber();

            page.Dict.Set("Parent", new PdfIndirectRef(pagesObjNum, 0));
            writer.WriteIndirectObject(objNum, page.Dict);
            kids.Add(new PdfIndirectRef(objNum, 0));
        }

        var pagesDict = new PdfDictionary();
        pagesDict.Set("Type", new PdfName("Pages"));
        pagesDict.Set("Kids", kids);
        pagesDict.Set("Count", new PdfInteger(Pages.Count));

        // Write at the original /Pages object number
        writer.WriteIndirectObject(pagesObjNum, pagesDict);

        // Keep the in-memory reader's page tree consistent with the current page
        // order (Insert/Delete only updated the Pages list, not the underlying
        // /Kids). Without this, page-number lookups that walk the reader tree after
        // a save — e.g. resolving a GoTo destination's target page — see the stale
        // pre-edit order. The kids are the live page dicts in their current order.
        var inMemPages = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (inMemPages is not null)
        {
            var inMemKids = new PdfArray();
            foreach (var page in Pages) inMemKids.Add(page.Dict);
            inMemPages.Set("Kids", inMemKids);
            inMemPages.Set("Count", new PdfInteger(Pages.Count));
        }
    }

    /// <summary>
    /// Rebuild the in-memory catalog /Pages tree (/Kids and /Count) so it matches
    /// the current page order. <see cref="PageCollection.Insert"/> / Delete update
    /// only the Pages list, not the underlying /Kids, so any reader-tree walk before
    /// save — e.g. resolving a GoTo or bookmark destination's target page number —
    /// would otherwise see the stale pre-edit order (off-by-one after a page is
    /// inserted). Safe to call repeatedly; a no-op when no page was added or removed.
    /// </summary>
    internal void SyncInMemoryPageTree()
    {
        if (_pages is null || !_pages.IsModified) return;
        var inMemPages = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (inMemPages is null) return;

        // Preserve each already-loaded page's original indirect reference so the
        // rebuilt /Kids keeps page object-number identity: named-destination
        // resolution maps a page's object number to its index, so flattening to
        // bare dicts would make named destinations unresolvable. Newly inserted
        // (pending) pages have no object number yet and go in as direct dicts.
        var dictToRef = new Dictionary<PdfDictionary, PdfObject>(
            System.Collections.Generic.ReferenceEqualityComparer.Instance);
        CollectKidRefs(inMemPages, dictToRef);

        var inMemKids = new PdfArray();
        foreach (var page in Pages)
            inMemKids.Add(dictToRef.TryGetValue(page.Dict, out var r) ? r : page.Dict);
        inMemPages.Set("Kids", inMemKids);
        inMemPages.Set("Count", new PdfInteger(Pages.Count));
    }

    /// <summary>
    /// After pages have been removed (e.g. by <see cref="Facades.PdfFileEditor.Extract(byte[],int,int)"/>,
    /// which deletes every page outside the requested range), drop the objects that only the
    /// removed pages kept alive. A plain save writes every object still reachable from the
    /// trailer, and an outline bookmark, article thread, or link annotation that pointed at a
    /// removed page keeps that page — and its (often large) images — reachable, so an
    /// extracted file stays as big as the whole source. Recompute reachability treating each
    /// removed page as a cut point so the save writes only what the surviving pages still use.
    /// </summary>
    internal void CompactAfterPageRemoval()
    {
        if (_pages is null || !_pages.IsModified) return;

        // Flatten /Kids to the surviving pages so a removed page is no longer reachable
        // through the page tree itself.
        SyncInMemoryPageTree();

        var survivingPages = new HashSet<int>();
        foreach (var page in Pages)
            if (page.SourceObjectNumber > 0) survivingPages.Add(page.SourceObjectNumber);

        // Compute reachability but treat every removed page as a cut point: don't traverse a
        // /Type /Page object that isn't one of the survivors. This drops each removed page
        // and everything only it references (its images can be the bulk of the file) no
        // matter what still points at it — a bookmark, an article-thread bead's /P, a link
        // annotation on a surviving page. Those references simply dangle, resolving to
        // "no page" on reopen, which never keeps the page.
        var reachable = new HashSet<int>();
        CollectReachableExcludingRemovedPages(_reader.Trailer, reachable, survivingPages);
        if (reachable.Count > 0) _reachableObjects = reachable;
    }

    /// <summary>Reachability variant used after page removal: identical to
    /// <see cref="CollectReachable"/> except a <c>/Type /Page</c> object whose number is not
    /// in <paramref name="survivingPages"/> is neither marked reachable nor traversed, so a
    /// removed page (and any object only it kept alive) falls out of the saved file.</summary>
    private void CollectReachableExcludingRemovedPages(PdfObject? root, HashSet<int> visited, HashSet<int> survivingPages)
    {
        if (root is null or PdfNull) return;
        var stack = new Stack<PdfObject>();
        stack.Push(root);
        var seenDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null or PdfNull) continue;

            if (obj is PdfIndirectRef iref)
            {
                if (visited.Contains(iref.ObjectNumber)) continue;
                var resolved = _reader.Resolve(iref);
                // Cut removed pages: don't record or traverse them.
                if (resolved is PdfDictionary pd && pd.GetName("Type") == "Page"
                    && !survivingPages.Contains(iref.ObjectNumber))
                    continue;
                visited.Add(iref.ObjectNumber);
                if (resolved is not null) stack.Push(resolved);
                continue;
            }
            if (obj is PdfStream stream) { stack.Push(stream.Dict); continue; }
            if (obj is PdfDictionary dict)
            {
                if (!seenDicts.Add(dict)) continue;
                foreach (var key in dict.Keys)
                {
                    var val = dict.Get(key);
                    if (val is not null) stack.Push(val);
                }
                continue;
            }
            if (obj is PdfArray arr)
                foreach (var item in arr)
                    if (item is not null) stack.Push(item);
        }
    }

    /// <summary>Map each leaf page dictionary in a /Pages subtree to the indirect
    /// reference that points at it, so <see cref="SyncInMemoryPageTree"/> can keep
    /// those references when it rebuilds a flat /Kids array.</summary>
    private void CollectKidRefs(PdfDictionary node, Dictionary<PdfDictionary, PdfObject> map)
    {
        if (_reader.Resolve(node.Get("Kids")) is not PdfArray kids) return;
        foreach (var kid in kids)
        {
            var kidDict = _reader.ResolveDict(kid);
            if (kidDict is null) continue;
            if (kidDict.GetName("Type") == "Page")
            {
                if (kid is PdfIndirectRef) map[kidDict] = kid;
            }
            else
            {
                CollectKidRefs(kidDict, map);
            }
        }
    }

    private static void CopyTrailerEntry(PdfDictionary source, PdfDictionary dest, string key)
    {
        var val = source.Get(key);
        if (val is not null)
            dest.Set(key, val);
    }
}
