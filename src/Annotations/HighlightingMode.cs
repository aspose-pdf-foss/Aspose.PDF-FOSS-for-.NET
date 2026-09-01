using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Highlighting mode for link annotations.</summary>
public enum HighlightingMode
{
    /// <summary>No highlighting.</summary>
    None,
    /// <summary>Invert the contents of the annotation rectangle.</summary>
    Invert,
    /// <summary>Invert the annotation border.</summary>
    Outline,
    /// <summary>Push the annotation appearance.</summary>
    Push,
    /// <summary>Toggle the annotation's appearance on/off.</summary>
    Toggle,
}
