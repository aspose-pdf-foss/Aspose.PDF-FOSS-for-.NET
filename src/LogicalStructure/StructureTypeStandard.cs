namespace Aspose.Pdf.LogicalStructure;

/// <summary>
/// Category of a standard structure type per ISO 32000-1 §14.8.4: grouping
/// elements, block-level structure elements (BLSEs), inline-level structure
/// elements (ILSEs) and illustration elements.
/// </summary>
public sealed class StructureTypeCategory
{
    private StructureTypeCategory(string name) => Name = name;

    internal string Name { get; }

    /// <summary>Grouping elements (Document, Part, Sect, Div, …).</summary>
    public static readonly StructureTypeCategory GroupingElements = new("GroupingElements");
    /// <summary>Block-level structure elements (P, H1…H6, L, Table, …).</summary>
    public static readonly StructureTypeCategory BLSEs = new("BLSEs");
    /// <summary>Inline-level structure elements (Span, Quote, Link, Ruby, …).</summary>
    public static readonly StructureTypeCategory ILSEs = new("ILSEs");
    /// <summary>Illustration elements (Figure, Formula, Form).</summary>
    public static readonly StructureTypeCategory IllustrationElements = new("IllustrationElements");

    /// <summary>Resolve a category from its name.</summary>
    public static explicit operator StructureTypeCategory(string name) => name switch
    {
        "GroupingElements" => GroupingElements,
        "BLSEs" => BLSEs,
        "ILSEs" => ILSEs,
        "IllustrationElements" => IllustrationElements,
        _ => throw new ArgumentException($"Unknown structure type category: {name}"),
    };

    public override string ToString() => Name;
}

/// <summary>
/// The PDF standard structure types (ISO 32000-1 Tables 333–337), exposed as
/// singletons so <see cref="StructureElement.StructureType"/> reads compare by
/// identity. <see cref="Tag"/> is the role name written to the /S entry.
/// </summary>
public sealed class StructureTypeStandard
{
    private StructureTypeStandard(string tag, StructureTypeCategory category)
    {
        Tag = tag;
        Category = category;
    }

    /// <summary>The role tag (the /S entry value, e.g. "P", "H1", "Ruby").</summary>
    public string Tag { get; }

    /// <summary>The type's category (grouping / BLSE / ILSE / illustration).</summary>
    public StructureTypeCategory Category { get; }

    public override string ToString() => Tag;

    // Grouping elements (Table 333)
    public static readonly StructureTypeStandard Document = new("Document", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Part = new("Part", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Art = new("Art", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Sect = new("Sect", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Div = new("Div", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard BlockQuote = new("BlockQuote", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Caption = new("Caption", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard TOC = new("TOC", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard TOCI = new("TOCI", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Index = new("Index", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard NonStruct = new("NonStruct", StructureTypeCategory.GroupingElements);
    public static readonly StructureTypeStandard Private = new("Private", StructureTypeCategory.GroupingElements);

    // Block-level structure elements (Tables 334–335)
    public static readonly StructureTypeStandard P = new("P", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H = new("H", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H1 = new("H1", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H2 = new("H2", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H3 = new("H3", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H4 = new("H4", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H5 = new("H5", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard H6 = new("H6", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard L = new("L", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard LI = new("LI", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard Lbl = new("Lbl", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard LBody = new("LBody", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard Table = new("Table", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard TR = new("TR", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard TH = new("TH", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard TD = new("TD", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard THead = new("THead", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard TBody = new("TBody", StructureTypeCategory.BLSEs);
    public static readonly StructureTypeStandard TFoot = new("TFoot", StructureTypeCategory.BLSEs);

    // Inline-level structure elements (Table 336)
    public static readonly StructureTypeStandard Span = new("Span", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Quote = new("Quote", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Note = new("Note", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Reference = new("Reference", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard BibEntry = new("BibEntry", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Code = new("Code", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Link = new("Link", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Annot = new("Annot", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Ruby = new("Ruby", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard RB = new("RB", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard RT = new("RT", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard RP = new("RP", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard Warichu = new("Warichu", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard WT = new("WT", StructureTypeCategory.ILSEs);
    public static readonly StructureTypeStandard WP = new("WP", StructureTypeCategory.ILSEs);

    // Illustration elements (Table 337)
    public static readonly StructureTypeStandard Figure = new("Figure", StructureTypeCategory.IllustrationElements);
    public static readonly StructureTypeStandard Formula = new("Formula", StructureTypeCategory.IllustrationElements);
    public static readonly StructureTypeStandard Form = new("Form", StructureTypeCategory.IllustrationElements);

    private static readonly Dictionary<string, StructureTypeStandard> ByTag = BuildIndex();

    private static Dictionary<string, StructureTypeStandard> BuildIndex()
    {
        var map = new Dictionary<string, StructureTypeStandard>(StringComparer.Ordinal);
        foreach (var field in typeof(StructureTypeStandard).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.GetValue(null) is StructureTypeStandard t)
                map[t.Tag] = t;
        }
        return map;
    }

    /// <summary>Resolve a standard type from its role tag; throws on an
    /// unknown tag.</summary>
    public static explicit operator StructureTypeStandard(string tag)
        => FromTag(tag) ?? throw new ArgumentException($"Unknown standard structure type: {tag}");

    /// <summary>The singleton for <paramref name="tag"/>, or null when the
    /// tag is not a standard structure type.</summary>
    internal static StructureTypeStandard? FromTag(string? tag)
        => tag is not null && ByTag.TryGetValue(tag, out var t) ? t : null;
}
