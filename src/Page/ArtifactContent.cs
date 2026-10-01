using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>
    /// The page content marked as artifacts (<c>/Artifact</c> marked-content sequences, PDF
    /// 32000-1 §14.8.2.2), in content order: each artifact's text as runs of one style, with the
    /// spaces between words and lines restored, and its images. Each item states the type and
    /// subtype of its artifact, so a running header reads as the items whose
    /// <see cref="MarkedContentItem.ArtifactType"/> is Pagination and whose
    /// <see cref="MarkedContentItem.ArtifactSubtype"/> is Header. The items name no structure
    /// element.
    ///
    /// ⚠ FOSS-only: the reference has no styled read of a page's artifacts.
    /// </summary>
    public IList<MarkedContentItem> GetArtifactContent()
    {
        // All artifacts are read in one pass, each starting a run of its own, so the space
        // between two artifacts on one line (a header drawn word by word) is restored.
        var blocks = MarkedContentScan.ArtifactsForPage(Reader, Dict);
        var pieces = new List<(StructureElement?, Page?, MarkedContentScan.Piece)>();
        var starts = new HashSet<int>();
        var blockOf = new List<MarkedContentScan.ArtifactBlock>();
        foreach (var block in blocks)
        {
            starts.Add(pieces.Count);
            foreach (var piece in block.Pieces)
            {
                pieces.Add((null, this, piece));
                blockOf.Add(block);
            }
        }
        var result = new List<MarkedContentItem>();
        var itemStarts = new List<int>();
        StructureElement.BuildItems(Reader, pieces, result, starts, itemStarts);
        for (var i = 0; i < result.Count; i++)
        {
            var block = blockOf[itemStarts[i]];
            result[i].ArtifactType = Enum.TryParse<Artifact.ArtifactType>(block.Type, out var t) && Enum.IsDefined(typeof(Artifact.ArtifactType), t)
                ? t : Artifact.ArtifactType.Undefined;
            result[i].ArtifactSubtype = Enum.TryParse<Artifact.ArtifactSubtype>(block.Subtype, out var s) && Enum.IsDefined(typeof(Artifact.ArtifactSubtype), s)
                ? s : Artifact.ArtifactSubtype.Undefined;
        }
        return result;
    }
}
