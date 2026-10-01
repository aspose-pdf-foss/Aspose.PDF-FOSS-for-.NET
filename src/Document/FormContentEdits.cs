namespace Aspose.Pdf;

public sealed partial class Document
{
    // Every form whose operators were opened for editing through XForm.Contents; a save
    // writes their edits back into the streams before it reads them.
    private readonly HashSet<XForm> _formsWithContents = new();

    internal void TrackContentEdits(XForm form) => _formsWithContents.Add(form);

    /// <summary>Write every tracked form's operator edits back into its stream, so both a
    /// full and an incremental save serialise what the caller added.</summary>
    internal void FlushFormContentEdits()
    {
        foreach (var form in _formsWithContents)
            form.FlushContentEdits();
    }
}
