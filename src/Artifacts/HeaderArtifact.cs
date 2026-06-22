namespace Aspose.Pdf;

/// <summary>
/// Represents a header artifact — a page-level running head tagged
/// /Artifact /Subtype /Header (PDF 32000 §14.8.2.2). Like
/// <see cref="WatermarkArtifact"/> it is a non-content page element; the
/// inherited <see cref="Artifact"/> members carry its text, position and
/// styling.
/// </summary>
public class HeaderArtifact : Artifact
{
    /// <summary>Creates an empty header artifact (Pagination / Header).</summary>
    public HeaderArtifact() : base(ArtifactType.Pagination, ArtifactSubtype.Header)
    {
    }

    /// <summary>Creates a header artifact carrying the given text.</summary>
    public HeaderArtifact(string text) : this()
    {
        Text = text;
    }
}
