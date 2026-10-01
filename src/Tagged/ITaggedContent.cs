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
    /// <summary>Creates a figure element (<c>/Figure</c>). It is not in the tree until appended to a parent element.</summary>
    LS.FigureElement CreateFigureElement();
    /// <summary>Creates a form element (<c>/Form</c>). It is not in the tree until appended to a parent element.</summary>
    LS.FormElement CreateFormElement();
    LS.FormulaElement CreateFormulaElement();
    /// <summary>Creates a heading element without a level (<c>/H</c>). It is not in the tree until appended to a parent element.</summary>
    LS.HeaderElement CreateHeaderElement();
    /// <summary>Creates a heading element of the given level (<c>/H1</c>, <c>/H2</c> ...); a level of 0 or less
    /// gives <c>/H</c>. It is not in the tree until appended to a parent element.</summary>
    LS.HeaderElement CreateHeaderElement(int level);
    LS.IndexElement CreateIndexElement();
    /// <summary>Creates a link element (<c>/Link</c>). It is not in the tree until appended to a parent element.</summary>
    LS.LinkElement CreateLinkElement();
    /// <summary>Creates a list element (<c>/L</c>). It is not in the tree until appended to a parent element.</summary>
    LS.ListElement CreateListElement();
    LS.ListLBodyElement CreateListLBodyElement();
    /// <summary>Creates a list-item element (<c>/LI</c>). It is not in the tree until appended to a parent element.</summary>
    LS.ListLIElement CreateListLIElement();
    LS.ListLblElement CreateListLblElement();
    LS.NonStructElement CreateNonStructElement();
    /// <summary>Creates a note element (<c>/Note</c>). It is not in the tree until appended to a parent element.</summary>
    LS.NoteElement CreateNoteElement();
    /// <summary>Creates a paragraph element (<c>/P</c>). It is not in the tree until appended to a parent element.</summary>
    LS.ParagraphElement CreateParagraphElement();
    LS.PartElement CreatePartElement();
    LS.PrivateElement CreatePrivateElement();
    /// <summary>Creates an inline quotation element (<c>/Quote</c>). It is not in the tree until appended to a parent element.</summary>
    LS.QuoteElement CreateQuoteElement();
    LS.ReferenceElement CreateReferenceElement();
    LS.RubyElement CreateRubyElement();
    /// <summary>Creates a section element (<c>/Sect</c>). It is not in the tree until appended to a parent element.</summary>
    LS.SectElement CreateSectElement();
    /// <summary>Creates a span element (<c>/Span</c>). It is not in the tree until appended to a parent element.</summary>
    LS.SpanElement CreateSpanElement();
    /// <summary>Creates a table-of-contents element (<c>/TOC</c>). It is not in the tree until appended to a parent element.</summary>
    LS.TOCElement CreateTOCElement();
    /// <summary>Creates a table-of-contents item element (<c>/TOCI</c>). It is not in the tree until appended to a parent element.</summary>
    LS.TOCIElement CreateTOCIElement();
    /// <summary>Creates a table element (<c>/Table</c>). It is not in the tree until appended to a parent element.</summary>
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
