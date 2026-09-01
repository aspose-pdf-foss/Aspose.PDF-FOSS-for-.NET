using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A structure element that can carry inline text content
/// (written to the element's /ActualText entry).</summary>
public interface ITextElement
{
    /// <summary>Set the element's text content.</summary>
    void SetText(string text);
}
