using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    /// <summary>True when the document already carries a complete structure: a structure tree
    /// with elements, /MarkInfo /Marked true, and every painting operation of every page inside
    /// marked content (tagged, or an artifact). One that is untagged or tagged only in part is
    /// tagged afresh.</summary>
    internal static bool HasCompleteTagging(Document document)
    {
        var reader = document.Reader;
        var root = reader.ResolveDict(reader.Catalog.Get("StructTreeRoot"));
        var kids = root is not null ? reader.Resolve(root.Get("K")) : null;
        if (kids is null || kids is PdfArray { Count: 0 }) return false;
        if (reader.ResolveDict(reader.Catalog.Get("MarkInfo")) is not { } markInfo
            || reader.Resolve(markInfo.Get("Marked")) is not PdfBoolean { Value: true })
            return false;

        foreach (var page in document.Pages)
        {
            var (_, ops) = PageContentScan.Scan(page);
            var depth = 0;
            foreach (var op in ops)
            {
                if (op.Kind == ContentOpKind.BeginMark) depth++;
                else if (op.Kind == ContentOpKind.EndMark) depth = System.Math.Max(0, depth - 1);
                else if (depth == 0 && ContentMarker.Paints(op)) return false;
            }
        }
        return true;
    }
}
