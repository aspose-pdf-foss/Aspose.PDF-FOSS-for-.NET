using System.Collections.Generic;

namespace Aspose.Pdf.Vector;

/// <summary>The clipping region in force while an element was painted: the paths one W or W*
/// clipped to, each with the CTM it was built under, the rule that fills them, and the region
/// in force when it was set - a clip only ever narrows the one before it.</summary>
internal sealed class ClipRegion
{
    internal readonly IReadOnlyList<(Aspose.Pdf.Matrix Ctm, IReadOnlyList<Aspose.Pdf.Operator> Ops)> Paths;
    internal readonly bool EvenOdd;
    internal readonly ClipRegion? Outer;

    internal ClipRegion(IReadOnlyList<(Aspose.Pdf.Matrix Ctm, IReadOnlyList<Aspose.Pdf.Operator> Ops)> paths,
        bool evenOdd, ClipRegion? outer)
    {
        Paths = paths;
        EvenOdd = evenOdd;
        Outer = outer;
    }

    /// <summary>This region and the ones it narrows, the outermost first.</summary>
    internal List<ClipRegion> Chain()
    {
        var chain = new List<ClipRegion>();
        for (var region = this; region is not null; region = region.Outer)
            chain.Insert(0, region);
        return chain;
    }
}
