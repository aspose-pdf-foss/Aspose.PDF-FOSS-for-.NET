using System;
using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    // The page attributes a page inherits from its ancestors in the page tree (ISO 32000-1 7.7.3.4).
    private static readonly string[] InheritableKeys = ["MediaBox", "CropBox", "Resources", "Rotate"];

    /// <summary>The value text of a top-level key, as written in the object.</summary>
    private sealed class InheritedValue
    {
        public InheritedValue(byte[] text) { Text = text; }
        public byte[] Text { get; }
    }

    /// <summary>
    /// Walks the page tree from the catalog: the pages in document order and the intermediate
    /// nodes. Attributes a node passes down to its pages move onto the pages, as Annex F's page
    /// offset hint table counts each page's objects from the page itself - a resource dictionary
    /// a page only inherits would be no page's object. False when the tree cannot be walked
    /// (a missing or repeated page, a cycle): such a file is written as saved, unlinearized.
    /// </summary>
    private static bool WalkPageTree(LinearizeState lz)
    {
        var catalog = lz.objs[lz.rootNum];
        ScanObject(catalog, wantKeys: true);
        var pagesRoot = FirstRefUnder(catalog, "Pages");
        if (pagesRoot <= 0) return false;

        lz.pages = new List<int>();
        lz.pageSet = new HashSet<int>();
        lz.treeNodes = new List<int>();
        var nodes = new HashSet<int>();
        var pending = new Stack<(int Num, Dictionary<string, InheritedValue> Inherited)>();
        pending.Push((pagesRoot, new Dictionary<string, InheritedValue>()));
        while (pending.Count > 0)
        {
            var (num, inherited) = pending.Pop();
            if (!lz.objs.TryGetValue(num, out var node) || nodes.Contains(num) || lz.pageSet.Contains(num)) return false;
            ScanObject(node, wantKeys: true);
            if (node.DictOpen < 0) return false;
            if (KeyIndex(node, "Kids") < 0)
            {
                lz.pages.Add(num);
                lz.pageSet.Add(num);
                PushInheritedOntoPage(node, inherited);
                continue;
            }
            nodes.Add(num);
            lz.treeNodes.Add(num);
            var passed = TakeInheritableFromNode(node, inherited);
            var kids = RefsUnder(node, "Kids");
            for (var i = kids.Count - 1; i >= 0; i--) pending.Push((kids[i], passed));
        }
        return lz.pages.Count > 0;
    }

    private static int KeyIndex(RawObject o, string name)
    {
        if (o.Keys is null) return -1;
        for (var i = 0; i < o.Keys.Count; i++)
            if (o.Keys[i].Name == name) return i;
        return -1;
    }

    /// <summary>The objects a top-level key's value refers to, in the order written.</summary>
    private static List<int> RefsUnder(RawObject o, string name)
    {
        var key = KeyIndex(o, name);
        var result = new List<int>();
        if (key < 0) return result;
        foreach (var r in o.Refs)
            if (r.Key == key) result.Add(r.Target);
        return result;
    }

    private static int FirstRefUnder(RawObject o, string name)
    {
        var refs = RefsUnder(o, name);
        return refs.Count > 0 ? refs[0] : -1;
    }

    /// <summary>The attributes a node's pages inherit - its own over its ancestors' - taken off
    /// the node, which keeps every other key.</summary>
    private static Dictionary<string, InheritedValue> TakeInheritableFromNode(RawObject node,
        Dictionary<string, InheritedValue> inherited)
    {
        var removed = new List<KeySpan>();
        foreach (var key in InheritableKeys)
        {
            var index = KeyIndex(node, key);
            if (index >= 0) removed.Add(node.Keys![index]);
        }
        if (removed.Count == 0) return inherited;

        var passed = new Dictionary<string, InheritedValue>(inherited);
        foreach (var k in removed)
            passed[k.Name] = new InheritedValue(Slice(node.Buf, k.ValueStart, k.ValueEnd));
        removed.Sort((a, b) => a.NameStart.CompareTo(b.NameStart));
        var edited = new List<byte>(node.End - node.Start);
        var p = node.Start;
        foreach (var k in removed)
        {
            AddRange(edited, node.Buf, p, k.NameStart);
            p = k.ValueEnd;
        }
        AddRange(edited, node.Buf, p, node.End);
        ReplaceBytes(node, edited.ToArray());
        return passed;
    }

    /// <summary>Writes onto a page each inherited attribute it does not set itself.</summary>
    private static void PushInheritedOntoPage(RawObject page, Dictionary<string, InheritedValue> inherited)
    {
        if (inherited.Count == 0) return;
        var insert = new List<byte>();
        foreach (var key in InheritableKeys)
        {
            if (!inherited.TryGetValue(key, out var value) || KeyIndex(page, key) >= 0) continue;
            insert.AddRange(Encoding.ASCII.GetBytes("/" + key));
            insert.AddRange(value.Text);
        }
        if (insert.Count == 0) return;
        var edited = new List<byte>(page.End - page.Start + insert.Count);
        AddRange(edited, page.Buf, page.Start, page.DictOpen);
        edited.AddRange(insert);
        AddRange(edited, page.Buf, page.DictOpen, page.End);
        ReplaceBytes(page, edited.ToArray());
    }

    /// <summary>Gives an object edited bytes and scans them afresh.</summary>
    private static void ReplaceBytes(RawObject o, byte[] bytes)
    {
        o.BodyStart -= o.Start;
        o.Buf = bytes;
        o.Start = 0;
        o.End = bytes.Length;
        ScanObject(o, wantKeys: true);
    }

    private static byte[] Slice(byte[] b, int start, int end)
    {
        var slice = new byte[end - start];
        Array.Copy(b, start, slice, 0, slice.Length);
        return slice;
    }

    private static void AddRange(List<byte> list, byte[] b, int start, int end)
    {
        for (var i = start; i < end; i++) list.Add(b[i]);
    }
}
