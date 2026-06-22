namespace Aspose.Pdf;

/// <summary>PDF specification version targeted by the document header.</summary>
public enum PdfVersion
{
    v_1_0,
    v_1_1,
    v_1_2,
    v_1_3,
    v_1_4,
    v_1_5,
    v_1_6,
    v_1_7,
    v_2_0,
}

/// <summary>Predominant text-flow direction recorded in the viewer-preferences dictionary.</summary>
public enum Direction
{
    L2R,
    R2L,
}

/// <summary>Page-layout entry written to the catalog (PDF32000 Table 28).</summary>
public enum PageLayout
{
    Default,
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight,
    TwoPageLeft,
    TwoPageRight,
}

/// <summary>Page-mode entry written to the catalog (PDF32000 Table 28).</summary>
public enum PageMode
{
    UseNone,
    UseOutlines,
    UseThumbs,
    FullScreen,
    UseOC,
    UseAttachments,
}

/// <summary>Print-duplex value recorded in viewer preferences (PDF32000 Table 150).</summary>
public enum PrintDuplex
{
    Simplex,
    DuplexFlipShortEdge,
    DuplexFlipLongEdge,
}

/// <summary>
/// Pair of byte strings that make up the /ID array in the PDF trailer.
/// Original = first /ID entry (set at file creation); Modified = second
/// entry (rewritten on every save).
/// </summary>
public sealed class Id
{
    public Id(string original, string modified)
    {
        Original = original ?? string.Empty;
        Modified = modified ?? string.Empty;
    }

    public string Original { get; }
    public string Modified { get; }
}
