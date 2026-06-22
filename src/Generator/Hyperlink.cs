namespace Aspose.Pdf;

/// <summary>Base type for hyperlinks attached to text fragments or
/// annotations. Concrete subclasses (LocalHyperlink, WebHyperlink) carry
/// the destination — this class is a marker
/// (no public members).</summary>
public class Hyperlink
{
}

/// <summary>Hyperlink that opens an external URL.</summary>
public class WebHyperlink : Hyperlink
{
    public WebHyperlink() { }
    public WebHyperlink(string url) { Url = url; }

    /// <summary>Destination URL.</summary>
    public string? Url { get; set; }
}

/// <summary>Hyperlink that launches an external file (e.g. another PDF) via a
/// /Launch action.</summary>
public class FileHyperlink : Hyperlink
{
    public FileHyperlink() { }
    public FileHyperlink(string fileName) { FileName = fileName; }

    /// <summary>Path of the file to open.</summary>
    public string? FileName { get; set; }

    /// <summary>Whether the file should open in a new viewer window.</summary>
    public ExtendedBoolean NewWindow { get; set; } = ExtendedBoolean.Undefined;
}

/// <summary>Hyperlink that jumps to another paragraph or page in the
/// same document.</summary>
public class LocalHyperlink : Hyperlink
{
    public LocalHyperlink() { }
    public LocalHyperlink(BaseParagraph target) { Target = target; }

    /// <summary>Target paragraph within the document.</summary>
    public BaseParagraph? Target { get; set; }

    /// <summary>1-based page number to jump to; takes effect when
    /// <see cref="Target"/> is null.</summary>
    public int TargetPageNumber { get; set; }
}
