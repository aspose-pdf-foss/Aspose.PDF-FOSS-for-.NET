using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A structure-type role (the /S entry value), exposed via
/// <see cref="StructureElement.S"/>. <see cref="Name"/> is the role tag, e.g. "P", "H1".</summary>
public sealed class StructureType
{
    internal StructureType(string name) => Name = name;

    /// <summary>The structure-type role tag (e.g. "P", "Sect", "H1").</summary>
    public string Name { get; }

    public override string ToString() => Name;
}
