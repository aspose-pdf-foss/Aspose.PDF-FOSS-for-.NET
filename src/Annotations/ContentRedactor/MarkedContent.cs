using Aspose.Pdf.Core;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    // The entries of marked content and of structure elements that restate their content as text:
    // what replaces it (ActualText), describes it (Alt) or spells out an abbreviation in it (E).
    private static readonly string[] TextEntries = ["ActualText", "Alt", "E"];

    /// <summary>A BMC or BDC that is open while the stream is walked: where its operation lies, its
    /// operands, and whether anything shown inside it was removed.</summary>
    private sealed class OpenMark(int start, int end, List<PdfObject> operands)
    {
        public readonly int Start = start;
        public readonly int End = end;
        public readonly List<PdfObject> Operands = operands;
        public bool Redacted;
    }

    /// <summary>Open, close and taint marked content as the operation <paramref name="op"/> ends.</summary>
    private static void TrackMarks(Walk walk, string op, Operation? current, int end, bool edited)
    {
        if (edited)
            foreach (var mark in walk.OpenMarks) mark.Redacted = true;
        switch (op)
        {
            case "BMC" or "BDC" when current is not null:
                walk.OpenMarks.Add(new OpenMark(current.Start, end, current.Operands));
                break;
            case "EMC" when walk.OpenMarks.Count > 0:
                var closed = walk.OpenMarks[walk.OpenMarks.Count - 1];
                walk.OpenMarks.RemoveAt(walk.OpenMarks.Count - 1);
                if (closed.Redacted) ScrubMark(walk, closed);
                break;
        }
    }

    /// <summary>Marked content something was removed from loses the text restating it - in its own
    /// properties, or in the stream's copy of a named property list (under the same name: the list
    /// describes this stream's content) - and its MCID is noted, so its structure element can lose
    /// the same.</summary>
    private static void ScrubMark(Walk walk, OpenMark mark)
    {
        if (mark.Operands.Count < 2 || mark.Operands[0] is not PdfName tag) return;
        var properties = mark.Operands[1] switch
        {
            PdfDictionary inline => inline,
            PdfName name => walk.Resources.Property(name.Value),
            _ => null,
        };
        if (properties is null) return;
        if (properties.Get("MCID") is PdfInteger mcid) walk.RedactedMcids.Add((int)mcid.Value);
        if (!TextEntries.Any(properties.ContainsKey)) return;

        var scrubbed = Shallow(properties);
        foreach (var key in TextEntries) scrubbed.Remove(key);
        if (mark.Operands[1] is PdfName named)
        {
            walk.Resources.SetProperty(named.Value, scrubbed);
            walk.PropertiesChanged = true;
            return;
        }
        PdfObject operand = scrubbed;
        using var output = new MemoryStream();
        var writer = new IO.PdfWriter(output);
        output.WriteByte((byte)'\n');
        writer.WriteObject(tag);
        output.WriteByte((byte)' ');
        writer.WriteObject(operand);
        var tail = System.Text.Encoding.ASCII.GetBytes(" BDC\n");
        output.Write(tail, 0, tail.Length);
        walk.Edits.Add(new Edit(mark.Start, mark.End, output.ToArray()));
    }

    /// <summary>The structure elements owning the page's redacted marked content, and every element
    /// above them, lose the text restating their content: an ActualText or Alt of a paragraph still
    /// held the words removed from it.</summary>
    private static void ScrubStructure(Page page, HashSet<int> mcids)
    {
        if (mcids.Count == 0) return;
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("StructParents")) is not PdfInteger key) return;
        var root = reader.ResolveDict(reader.Catalog.Get("StructTreeRoot"));
        if (root is null || reader.Resolve(NumberTreeValue(reader, reader.ResolveDict(root.Get("ParentTree")),
                (int)key.Value, 0)) is not PdfArray owners)
            return;

        var scrubbed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var mcid in mcids)
        {
            if (mcid < 0 || mcid >= owners.Count) continue;
            for (var element = reader.ResolveDict(owners[mcid]);
                 element is not null && element != root && scrubbed.Add(element);
                 element = reader.ResolveDict(element.Get("P")))
                foreach (var entry in TextEntries) element.Remove(entry);
        }
    }

    // A number tree is followed this deep at most (a malformed one may loop).
    private const int MaxNumberTreeDepth = 32;

    /// <summary>The value a number tree holds for <paramref name="key"/>, or null.</summary>
    private static PdfObject? NumberTreeValue(IO.PdfReader reader, PdfDictionary? node, int key, int depth)
    {
        if (node is null || depth > MaxNumberTreeDepth) return null;
        if (reader.Resolve(node.Get("Nums")) is PdfArray pairs)
            for (var i = 0; i + 1 < pairs.Count; i += 2)
                if (reader.Resolve(pairs[i]) is PdfInteger k && k.Value == key)
                    return pairs[i + 1];
        if (reader.Resolve(node.Get("Kids")) is not PdfArray kids) return null;
        foreach (var kid in kids)
        {
            var child = reader.ResolveDict(kid);
            if (child is null) continue;
            if (reader.Resolve(child.Get("Limits")) is PdfArray { Count: 2 } limits
                && reader.Resolve(limits[0]) is PdfInteger low && reader.Resolve(limits[1]) is PdfInteger high
                && (key < low.Value || key > high.Value))
                continue;
            if (NumberTreeValue(reader, child, key, depth + 1) is { } value) return value;
        }
        return null;
    }
}
