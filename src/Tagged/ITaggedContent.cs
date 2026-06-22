using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// The author-facing surface for a tagged PDF document. Exposes the
/// structure-tree root, factories for each typed structure element,
/// and the save hook that flushes the in-memory tree to
/// /StructTreeRoot.
/// </summary>
public interface ITaggedContent
{
    /// <summary>The first top-level structure element under
    /// <see cref="StructTreeRootElement"/> (typically the auto-generated
    /// /Document element).</summary>
    LS.StructureElement RootElement { get; }

    /// <summary>The /StructTreeRoot wrapper hosting the document's
    /// structure tree.</summary>
    LS.StructTreeRootElement StructTreeRootElement { get; }

    /// <summary>Default text-state used by inline structure elements
    /// that don't specify their own.</summary>
    LS.StructureTextState StructureTextState { get; }

    LS.AnnotElement CreateAnnotElement();
    LS.ArtElement CreateArtElement();
    LS.BibEntryElement CreateBibEntryElement();
    LS.BlockQuoteElement CreateBlockQuoteElement();
    LS.CaptionElement CreateCaptionElement();
    LS.CodeElement CreateCodeElement();
    LS.DivElement CreateDivElement();
    LS.FigureElement CreateFigureElement();
    LS.FormElement CreateFormElement();
    LS.FormulaElement CreateFormulaElement();
    LS.HeaderElement CreateHeaderElement();
    LS.HeaderElement CreateHeaderElement(int level);
    LS.IndexElement CreateIndexElement();
    LS.LinkElement CreateLinkElement();
    LS.ListElement CreateListElement();
    LS.ListLBodyElement CreateListLBodyElement();
    LS.ListLIElement CreateListLIElement();
    LS.ListLblElement CreateListLblElement();
    LS.NonStructElement CreateNonStructElement();
    LS.NoteElement CreateNoteElement();
    LS.ParagraphElement CreateParagraphElement();
    LS.PartElement CreatePartElement();
    LS.PrivateElement CreatePrivateElement();
    LS.QuoteElement CreateQuoteElement();
    LS.ReferenceElement CreateReferenceElement();
    LS.RubyElement CreateRubyElement();
    LS.SectElement CreateSectElement();
    LS.SpanElement CreateSpanElement();
    LS.TOCElement CreateTOCElement();
    LS.TOCIElement CreateTOCIElement();
    LS.TableElement CreateTableElement();
    LS.TableTBodyElement CreateTableTBodyElement();
    LS.TableTDElement CreateTableTDElement();
    LS.TableTFootElement CreateTableTFootElement();
    LS.TableTHElement CreateTableTHElement();
    LS.TableTHeadElement CreateTableTHeadElement();
    LS.TableTRElement CreateTableTRElement();
    LS.WarichuElement CreateWarichuElement();

    /// <summary>Prepare the in-memory tree for serialisation. Called
    /// before <see cref="Save"/> by the FOSS save pipeline so callers
    /// who insert nodes post-construction can flush them.</summary>
    void PreSave();

    /// <summary>Flush the in-memory tree to the document's
    /// /StructTreeRoot dictionary so the next file save embeds the
    /// updated structure.</summary>
    void Save();

    /// <summary>Set the document language (BCP-47 tag, e.g. "en-US").</summary>
    void SetLanguage(string lang);

    /// <summary>Set the document title in the /Info dictionary.</summary>
    void SetTitle(string title);
}
