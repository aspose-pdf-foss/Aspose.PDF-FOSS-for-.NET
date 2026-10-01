using System.Text;
using Aspose.Pdf.Core;
namespace Aspose.Pdf.Tagged;

public sealed partial class StructureTreeBuilder
{
    /// <summary>Each page's marked-content owners become a parent-tree entry, one key per page.</summary>
    private int WireMcidOwners(SortedDictionary<int, Dictionary<int, int>> owners, PdfArray parentTreeNums, int nextKey)
    {
        foreach (var (key, byMcid) in owners)
        {
            var perPage = new PdfArray();
            var last = -1;
            foreach (var mcid in byMcid.Keys)
                if (mcid > last) last = mcid;

            for (var mcid = 0; mcid <= last; mcid++)
            {
                perPage.Add(byMcid.TryGetValue(mcid, out var owner)
                    ? new PdfIndirectRef(owner, 0)
                    : PdfNull.Instance);
            }

            parentTreeNums.Add(new PdfInteger(key));
            parentTreeNums.Add(perPage);
            if (key >= nextKey) nextKey = key + 1;
        }
        return nextKey;
    }
}
