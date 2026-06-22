namespace Aspose.Pdf;

/// <summary>
/// Soft-mask handling strategy during PDF/A conversion.
/// </summary>
public enum ConvertSoftMaskAction
{
    /// <summary>Apply the implementation default.</summary>
    Default = 0,
    /// <summary>Convert each soft mask into a 1-bit stencil mask.</summary>
    ConvertToStencilMask = 1,
}

/// <summary>
/// Heading-level recognition tuning for <see cref="AutoTaggingSettings"/>.
/// Each entry is a font-size threshold; the largest size becomes level 1, the next level 2, etc.
/// </summary>
public sealed class HeadingLevels
{
    private readonly List<double> _levels = new();

    public HeadingLevels() { }

    /// <summary>Construct with a single seed font-size threshold.</summary>
    public HeadingLevels(double threshold) { _levels.Add(threshold); }

    /// <summary>All recorded font-size thresholds.</summary>
    public System.Collections.Generic.IList<double> AllLevels => _levels;

    /// <summary>Append every entry from <paramref name="fontSizes"/> to <see cref="AllLevels"/>.</summary>
    public void AddLevels(System.Collections.Generic.ICollection<double> fontSizes)
    {
        if (fontSizes is null) return;
        foreach (var size in fontSizes) _levels.Add(size);
    }
}

/// <summary>
/// Algorithm used to detect headings during auto-tagging.
/// </summary>
public enum HeadingRecognitionStrategy
{
    Default = 0,
    None = 1,
    FontSize = 2,
    FontWeight = 3,
    /// <summary>Try multiple heuristics in order.</summary>
    Auto = 4,
    /// <summary>Text-based heuristic detection.</summary>
    Heuristic = 5,
    /// <summary>Derive heading levels from document outlines.</summary>
    Outlines = 6,
}

/// <summary>
/// Settings controlling automatic structure-tree generation during conversion.
/// </summary>
public sealed class AutoTaggingSettings
{
    /// <summary>Whether auto-tagging runs as part of the conversion pipeline.</summary>
    public bool EnableAutoTagging { get; set; }

    /// <summary>Maximum heading depth captured by the tagger.</summary>
    public HeadingLevels HeadingLevels { get; set; } = new();

    /// <summary>Heuristic used to identify headings.</summary>
    public HeadingRecognitionStrategy HeadingRecognitionStrategy { get; set; }

    /// <summary>Default instance with EnableAutoTagging=false.</summary>
    public static AutoTaggingSettings Default => new();
}

/// <summary>
/// Font-embedding behaviour during conversion.
/// </summary>
public sealed class FontEmbeddingOptions
{
    /// <summary>When true, missing fonts are replaced via the default substitution table.</summary>
    public bool UseDefaultSubstitution { get; set; }
}

/// <summary>
/// PDF/A non-spec compliance toggles. Each flag relaxes one validator rule.
/// </summary>
public sealed class PdfANonSpecificationFlags
{
    /// <summary>When true, the validator skips checking that font-dictionary names match
    /// the embedded font's name.</summary>
    public bool CheckDifferentNamesInFontDictionaries { get; set; }
}

/// <summary>
/// Strategy for re-encoding symbolic fonts during PDF/A conversion.
/// </summary>
public sealed class PdfASymbolicFontEncodingStrategy
{
    public PdfASymbolicFontEncodingStrategy() { }

    public PdfASymbolicFontEncodingStrategy(QueueItem.CMapEncodingTableType preferredEncodingTable)
    {
        PreferredCmapEncodingTable = preferredEncodingTable;
    }

    public PdfASymbolicFontEncodingStrategy(System.Collections.Generic.Queue<QueueItem> priorityQueue)
    {
        CmapEncodingTablesPriorityQueue = priorityQueue;
    }

    /// <summary>Preferred cmap-subtable choice when several are available. Stored only.</summary>
    public QueueItem.CMapEncodingTableType PreferredCmapEncodingTable { get; set; } = QueueItem.CMapEncodingTableType.WindowsUnicodeTable;

    /// <summary>Priority queue of cmap-subtable candidates tried in order. Stored only.</summary>
    public System.Collections.Generic.Queue<QueueItem> CmapEncodingTablesPriorityQueue { get; set; } = new();

    /// <summary>One entry in the priority queue — names a single (platformID, platformSpecificID) cmap subtable.</summary>
    public class QueueItem
    {
        public QueueItem() { }

        public QueueItem(CMapEncodingTableType cmapTable)
        {
            CMapEncodingTable = cmapTable;
        }

        public QueueItem(ushort platformID, ushort platformSpecificID)
        {
            PlatformId = platformID;
            PlatformSpecificId = platformSpecificID;
        }

        public CMapEncodingTableType CMapEncodingTable { get; set; }
        public ushort PlatformId { get; set; }
        public ushort PlatformSpecificId { get; set; }

        /// <summary>OpenType cmap subtable selector.</summary>
        public enum CMapEncodingTableType : short
        {
            MacTable = 0,
            UnicodeTable = 1,
            WindowsSymbolicTable = 2,
            WindowsUnicodeTable = 3,
        }
    }
}

/// <summary>
/// Rules applied when generating ToUnicode CMaps during conversion.
/// </summary>
public sealed class ToUnicodeProcessingRules
{
    public ToUnicodeProcessingRules() { }

    public ToUnicodeProcessingRules(bool removeSpaces)
    {
        RemoveSpacesFromCMapNames = removeSpaces;
    }

    public ToUnicodeProcessingRules(bool removeSpaces, bool mapNonLinkedUnicodesOnSpace)
        : this(removeSpaces)
    {
        MapNonLinkedSymbolsOnSpace = mapNonLinkedUnicodesOnSpace;
    }

    /// <summary>When true, the ToUnicode CMap maps unmapped symbols to the space character.</summary>
    public bool MapNonLinkedSymbolsOnSpace { get; set; }

    /// <summary>When true, whitespace is stripped from CMap names.</summary>
    public bool RemoveSpacesFromCMapNames { get; set; }
}
