using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// A text field that holds a file path and offers a file-select dialog: a
/// <see cref="TextBoxField"/> whose /Ff carries the FileSelect bit. Loading a form hands
/// one back for any text field flagged that way.
/// </summary>
public sealed partial class FileSelectBoxField : TextBoxField
{
    internal FileSelectBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>A file-select field over <paramref name="rect"/> on <paramref name="page"/>.</summary>
    public FileSelectBoxField(Page page, Rectangle rect) : base(page, rect) => SetFieldFlag(FileSelectFlag, true);

    /// <summary>A file-select field in <paramref name="doc"/> over <paramref name="rect"/>.</summary>
    public FileSelectBoxField(Document doc, Rectangle rect) : base(doc, rect) => SetFieldFlag(FileSelectFlag, true);

    /// <summary>A detached file-select field, placed into a form later.</summary>
    public FileSelectBoxField() => SetFieldFlag(FileSelectFlag, true);
}
