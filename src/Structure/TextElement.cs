using System.Collections;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Structure;

/// <summary>A text-bearing structure element (Span / P / Quote /
/// Note / Reference / BibEntry).</summary>
public class TextElement : Element
{
    internal TextElement(PdfDictionary dict, PdfReader reader, Element? parent)
        : base(dict, reader, parent) { }

    /// <summary>The text content of this element. Returns the
    /// /ActualText entry when present; otherwise the value of /T (the
    /// element title) when set; otherwise an empty string.</summary>
    public string Text
    {
        get
        {
            if (!string.IsNullOrEmpty(ActualText)) return ActualText;
            var obj = _reader?.Resolve(_dict.Get("T")) ?? _dict.Get("T");
            return obj is PdfString s ? s.ToText() : string.Empty;
        }
    }
}
