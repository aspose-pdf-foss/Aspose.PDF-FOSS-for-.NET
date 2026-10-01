namespace Aspose.Pdf.Optimization;

/// <summary>
/// One entry of the PDF/UA-1 (ISO 14289-1) validation vocabulary: where a problem is
/// filed in the log, the conformance clause and the finer check code it carries, how
/// severe it is, and whether a conversion can repair it.
/// </summary>
/// <remarks>Code is finer than Clause and the two are separate log attributes: the
/// clause names the ISO requirement (<c>7.1</c>), the code names the individual check
/// inside it (<c>7.1:7.2(12.2)</c>). A few codes sit under a shorter clause than their
/// own prefix suggests — <c>7.18.5.2:2</c> is filed under clause <c>7.18.5</c>.</remarks>
internal readonly record struct UaProblemKind(
    string Section,
    string Clause,
    string Code,
    PdfAProblemSeverity Severity,
    bool Convertable);

/// <summary>
/// The PDF/UA-1 log vocabulary: the fixed section list every UA log carries and the
/// kinds of problem this validator can report.
/// </summary>
internal static class UaProblems
{
    public const string General = "General";
    public const string Text = "Text";
    public const string Fonts = "Fonts";
    public const string Graphics = "Graphics";
    public const string Tables = "Tables";
    public const string Annotations = "Annotations";
    public const string VersionIdentification = "VersionIdentification";

    /// <summary>The nineteen UA sections, in the order the log writes them. Every one is
    /// present in every UA log, empty sections included — a reader looks a section up by
    /// name and must find it whether or not the document has problems of that kind.</summary>
    public static readonly string[] SectionOrder =
    [
        "Security", General, Text, Fonts, Graphics, "Headings", Tables, "Lists",
        "NotesAndReferences", "OptionalContent", "EmbeddedFiles", "DigitalSignatures",
        "NonInteractiveForms", "XFA", "Navigation", Annotations, "Actions", "XObjects",
        VersionIdentification,
    ];

    // Clause 5 — version identification.
    public static readonly UaProblemKind PdfUaIdentifierMissing =
        new(VersionIdentification, "5", "5:1", PdfAProblemSeverity.Error, true);

    // Clause 6.2 — the structural parent tree.
    public static readonly UaProblemKind ParentTreeInconsistentEntry =
        new(General, "6.2", "6.2:1.1", PdfAProblemSeverity.Error, false);

    // Clause 7.1 — general document requirements.
    public static readonly UaProblemKind DocumentNotMarkedAsTagged =
        new(General, "7.1", "7.1:1.1(14.8.1)", PdfAProblemSeverity.Error, true);
    public static readonly UaProblemKind ObjectNotTagged =
        new(General, "7.1", "7.1:1.1(14.8)", PdfAProblemSeverity.Error, false);
    public static readonly UaProblemKind StructureTreeMissing =
        new(General, "7.1", "7.1:2.1", PdfAProblemSeverity.Warning, false);
    /// <summary>An inline-level structure element directly inside a grouping element.
    /// Figures are filed under Graphics, every other inline element under General.</summary>
    public static readonly UaProblemKind InappropriateStructureElement =
        new(General, "7.1", "7.1:2.4.1", PdfAProblemSeverity.Warning, false);
    public static readonly UaProblemKind InappropriateFigureElement =
        new(Graphics, "7.1", "7.1:2.4.1", PdfAProblemSeverity.Warning, false);
    public static readonly UaProblemKind XmpMetadataMissing =
        new(General, "7.1", "7.1:6.1", PdfAProblemSeverity.Error, true);
    public static readonly UaProblemKind TitleMissingInXmp =
        new(General, "7.1", "7.1:6.2", PdfAProblemSeverity.Error, true);
    public static readonly UaProblemKind TitleEmptyInXmp =
        new(General, "7.1", "7.1:6.3", PdfAProblemSeverity.Warning, true);
    public static readonly UaProblemKind ViewerPreferencesMissing =
        new(General, "7.1", "7.1:7.1(12.2)", PdfAProblemSeverity.Warning, true);
    public static readonly UaProblemKind DisplayDocTitleNotSet =
        new(General, "7.1", "7.1:7.2(12.2)", PdfAProblemSeverity.Error, true);
    public static readonly UaProblemKind SuspectsIsSet =
        new(General, "7.1", "7.1:8(14.7.1)", PdfAProblemSeverity.Error, true);
    /// <summary>Colour contrast cannot be judged mechanically, so every UA log carries
    /// this note whatever the document contains.</summary>
    public static readonly UaProblemKind ColorContrast =
        new(General, "7.1", "7.1:5", PdfAProblemSeverity.NeedManualCheck, false);

    // Clause 7.2 — text.
    /// <summary>Reading order cannot be judged mechanically; always present, like
    /// <see cref="ColorContrast"/>.</summary>
    public static readonly UaProblemKind LogicalReadingOrder =
        new(Text, "7.2", "7.2:1", PdfAProblemSeverity.NeedManualCheck, false);

    // Clause 7.5 — tables.
    public static readonly UaProblemKind IrregularTableRow =
        new(Tables, "7.5", "7.5:1", PdfAProblemSeverity.Error, false);

    // Clause 7.18 — annotations.
    public static readonly UaProblemKind LinkAnnotationMissingContents =
        new(Annotations, "7.18.5", "7.18.5.2:2", PdfAProblemSeverity.Error, false);

    // Clause 7.21 — fonts.
    public static readonly UaProblemKind FontNotEmbedded =
        new(Fonts, "7.21.4.1", "7.21.4.1", PdfAProblemSeverity.Error, true);
    public static readonly UaProblemKind CidSetIncomplete =
        new(Fonts, "7.21.4.2", "7.21.4.2", PdfAProblemSeverity.Error, true);
}
