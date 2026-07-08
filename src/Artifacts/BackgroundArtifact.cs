namespace Aspose.Pdf;

/// <summary>
/// Represents a background artifact (a coloured block or image painted behind
/// page content). Artifacts are marked content sequences tagged /Artifact
/// (PDF 32000 §14.8.2.2). Added via <see cref="ArtifactCollection.Add(BackgroundArtifact)"/>,
/// emitted as a prepended /Artifact /Subtype /Background block, and re-surfaced
/// as a <see cref="BackgroundArtifact"/> when the page is reopened.
/// </summary>
public sealed class BackgroundArtifact : Artifact
{
    /// <summary>Creates a background artifact (Pagination / Background, drawn behind content).</summary>
    public BackgroundArtifact() : base(ArtifactType.Pagination, ArtifactSubtype.Background)
    {
        IsBackground = true;
    }

    /// <summary>Image stream used as the page background. When set, the artifact
    /// paints this image (scaled to the page) instead of a solid colour.</summary>
    public Stream? BackgroundImage { get; set; }
}
