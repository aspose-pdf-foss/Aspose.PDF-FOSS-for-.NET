using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TagTreeBuildState
{
    public int headingCount;
    // Heading hierarchy: a Part holds the top-level heading and one Sect per lower-level
    // heading. The stack tracks the open section at each heading level.
    public LogicalStructure.PartElement part = null!;
    public List<(int level, Aspose.Pdf.LogicalStructure.StructureElement container)> stack = null!;
    public ITaggedContent tc = default!;
    public LogicalStructure.StructureElement root = default!;
    public List<Block> blocks = default!;
    public TaggingRun run = default!;
}
}
