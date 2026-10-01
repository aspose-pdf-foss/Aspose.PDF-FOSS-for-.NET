using Aspose.Pdf.IO;

namespace Aspose.Pdf.Core;

/// <summary>
/// A PDF number tree (ISO 32000-1 §7.9.7): integer keys mapped to objects, held in a
/// balanced tree of nodes that carry either leaf entries (/Nums) or children (/Kids).
/// </summary>
internal static class NumberTree
{
    /// <summary>Flatten a number tree into its key-value pairs, leaves and children alike.</summary>
    public static IEnumerable<(int Key, PdfObject Value)> Entries(PdfDictionary? node, PdfReader reader)
    {
        if (node is null) yield break;
        if (reader.Resolve(node.Get("Nums")) is PdfArray nums)
            for (var i = 0; i + 1 < nums.Count; i += 2)
                if (reader.Resolve(nums[i]) is PdfInteger key)
                    yield return ((int)key.Value, nums[i + 1]);
        if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var kid in kids)
                foreach (var entry in Entries(reader.ResolveDict(kid), reader))
                    yield return entry;
    }
}
