namespace Aspose.Pdf;

/// <summary>How a rule -- a side of a border -- is built across its width: the
/// CSS <c>border-style</c> structures. A dash pattern is not a structure but a
/// pattern along the rule, and rides on <see cref="GraphInfo.DashLengths"/> (or
/// <see cref="GraphInfo.DashArray"/>) beside it.</summary>
public enum RuleStyle
{
    /// <summary>One band the whole width.</summary>
    Solid = 0,

    /// <summary>Two bands, each a third of the width, a third apart -- the outer
    /// at the box edge and the inner at the rule's inner edge.</summary>
    Double,

    /// <summary>Two halves, the outer half of the top and left sides in the darker
    /// tone and the inner half in the colour itself; the bottom and right sides the
    /// other way round -- a channel cut into the surface.</summary>
    Groove,

    /// <summary>The mirror of <see cref="Groove"/>: a raised rim.</summary>
    Ridge,

    /// <summary>Both halves of the top and left sides in the darker tone, both
    /// halves of the bottom and right in the colour itself -- a sunken box.</summary>
    Inset,

    /// <summary>The mirror of <see cref="Inset"/>: a raised box.</summary>
    Outset,
}
