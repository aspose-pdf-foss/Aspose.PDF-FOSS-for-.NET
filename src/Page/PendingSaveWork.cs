using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>True when a save has something to lay out or write onto this page: paragraphs or a table of
    /// contents, a header or footer, a background, watermark or orientation request, operators, layers, text
    /// decorations or links held unwritten, or the page, its /Annots or one of its annotations edited in the
    /// session. An incremental save lays out these pages only: a page the session did not change costs nothing
    /// and stays exactly as the file has it.</summary>
    internal bool HasPendingSaveWork =>
        Paragraphs.Count > 0 || TocInfo is not null
        || (!HeaderFooterApplied && (Header is not null || Footer is not null))
        || (ExplicitBackground is not null && !BackgroundApplied) || _watermarkSet
        || _pageInfoCache is { LandscapeRequested: true } || HasBeforePageGenerate
        || _contents is { HasUnflushedOperators: true } || _layerFacades is not null
        || _attachedFragments is not null || _attachedParagraphs is not null || _pageInfoAnnotations is not null
        || _bgColorFragments is not null || _underlineFragments is not null || _strikeOutFragments is not null
        || _hyperlinkFragments is not null || _underlineRemovalFragments is not null
        || PruneUnusedFontsOnSave || _dict.Edited || AnnotationsEdited() || HasDeferredStamps;

    /// <summary>An annotation added to or removed from the page's /Annots, or one whose dictionary changed.</summary>
    private bool AnnotationsEdited()
    {
        if (_reader.LoadedValue(_dict.Get("Annots")) is PdfArray { Edited: true }) return true;
        if (_annotations is null) return false;
        foreach (var annotation in _annotations)
            if (annotation.Dict.Edited) return true;
        return false;
    }
}
