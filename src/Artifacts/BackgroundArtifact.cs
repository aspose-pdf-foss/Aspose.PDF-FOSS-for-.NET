namespace Aspose.Pdf;

/// <summary>
/// Represents a background artifact (image painted behind page content).
/// Artifacts are marked content sequences tagged /Artifact (PDF 32000
/// §14.8.2.2). Stored only; the renderer does not currently emit the
/// background image.
/// </summary>
public sealed class BackgroundArtifact
{
    /// <summary>Image stream used as the page background.</summary>
    public Stream? BackgroundImage { get; set; }

    /// <summary>Background color (used when no image is set).</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Whether the artifact is drawn behind page content (always
    /// true for a BackgroundArtifact; setter accepted for API parity).</summary>
    public bool IsBackground { get; set; } = true;

    /// <summary>Opacity (0.0 = fully transparent, 1.0 = fully opaque).</summary>
    public double Opacity { get; set; } = 1.0;
}
