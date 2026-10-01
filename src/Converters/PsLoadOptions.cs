namespace Aspose.Pdf;

/// <summary>
/// Options for loading PostScript (.ps) and Encapsulated PostScript (.eps) files as
/// PDF documents.
/// </summary>
public sealed class PsLoadOptions : LoadOptions
{
    /// <summary>Creates PostScript load options with no extra font folders and PostScript errors suppressed.</summary>
    public PsLoadOptions() { }

    /// <summary>Folders searched for the font programs the PostScript source names.
    /// A face found here is outlined into the page; a face that is one of the
    /// standard PDF base-14 names is referenced by name and never embedded.</summary>
    public string[] FontsFolders { get; set; } = new string[0];

    /// <summary>Convert embedded and resolved font programs to TrueType before
    /// writing them. Stored only: the writer outlines glyphs either way.</summary>
    public bool ConvertFontsToTTF { get; set; } = true;

    /// <summary>Suppress errors raised by the PostScript program itself, so a source
    /// that faults part-way still yields the page it had painted up to that point.
    /// This is the interpreter's default, matching the reference converter.</summary>
    public bool SuppressErrors { get; set; } = true;

    /// <summary>The source format this options instance configures.</summary>
    public override LoadFormat LoadFormat => LoadFormat.PS;
}
