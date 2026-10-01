using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CMapParseState
{
    public Dictionary<int, string> map = null!;
    public string[] lines = null!;
    public bool inBfChar;
    public bool inBfRange;
    /// <summary>The lines of the bfrange section being read: an array-form entry may wrap over
    /// several of them, so the section is parsed whole at its end.</summary>
    public StringBuilder bfRange = new();
    public string cmapText = default!;
    public string line = null!;
}
}
