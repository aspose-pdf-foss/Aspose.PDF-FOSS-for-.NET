using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The stages of a page visit: resetting the per-page extraction state before the content walk, and finishing the page after it.</summary>
    private void FinishPageVisit(PageVisitState pv)
    {
        if (pv.pureLayout)
            InsertLeadingGridSpaces(pv.textStart);

        // The page end closes the final line the way a streaming break would:
        // a single trailing space GLYPH in its show is typographic content and
        // gets the same sentinel protection from the trailing trim below.
        MaskTrailingShowSpaces();

        // Sort this page's text lines by visual order (Y coordinate, top to bottom)
        SortLinesByY(pv.textStart, pv.yStart);

        // Pure mode lays lines on the character grid but never leaves padding at a
        // line's right edge: Pure output has no trailing spaces on
        // any line (a trailing space fragment drawn at the row's far right would
        // otherwise leave one). Raw mode keeps source whitespace verbatim, and
        // rect-clipped extraction keeps clipped-run edges (the windowed
        // output ends lines with the source spaces).
        if (pv.pureLayout && TextSearchOptions?.Rectangle is null)
            TrimTrailingLineSpaces(pv.textStart);
        // Unmask the real glyph spaces the trim was steered around, and record
        // the page's trailing textless advances (a show op after the last break
        // means the page ends in glyphs — nothing to replay).
        RestoreEolShowSpaces(pv.textStart);
        TrailingBlankRows = _lastShowEnd < 0 || _lastShowEnd > _lastBreakPos
            ? 0 : _breaksAfterLastGlyph;

        // Diagnostic: a page that draws only images/graphics has no text-showing
        // operators. When the caller opted into error logging, surface this as a
        // recorded extraction error.
        if ((TextSearchOptions?.LogTextExtractionErrors ?? false) && _textShowingOpCount == 0)
        {
            const string msg = "Text showing operators aren't found on the page.";
            Errors.Add(new TextExtractionError
            {
                PageIndex = pv.page.Number,
                Message = msg,
                Description = msg,
                Summary = msg,
                Location = new TextExtractionErrorLocation { PageNumber = pv.page.Number },
            });
        }
    }

    /// <summary></summary>
    private void ResetPageVisitState(PageVisitState pv)
    {
        pv.textStart = _text.Length;
        pv.yStart = _lineYPositions.Count;
        _currentLineY = double.NaN;
        _currentLineCmTy = 0;
        _currentLineEffFs = double.NaN;
        _currentLineIsRotated = false;
        _currentLineDescent = 0.2;
        _currentLineDevY = double.NaN;
        _currentLineRowX = double.NaN;
        _rowXLineOffset = -1;
        _textShowingOpCount = 0;
        _currentPageNumber = pv.page.Number;

        pv.pureLayout = ExtractionOptions?.FormattingMode is not TextExtractionOptions.TextFormattingMode.Raw
            and not TextExtractionOptions.TextFormattingMode.MemorySaving;
        // Line-end glyph-space masking only applies where TrimTrailingLineSpaces
        // runs (full-page Pure); Raw keeps whitespace verbatim and rect-clipped
        // extraction keeps clipped-run edges untouched.
        _maskEolShowSpaces = pv.pureLayout && TextSearchOptions?.Rectangle is null;
        _breaksAfterLastGlyph = 0;
        _lastBreakPos = _text.Length;
        _lastShowStart = -1;
        _lastShowEnd = -1;
        _pageHeightForRows = pv.page.Height;
        _pageHasRotatedText = false;
        // The grid anchors at coordinate x = 0 regardless of the MediaBox
        // (a shifted MediaBox does not move the column
        // boundaries); content at negative X can't occupy a column at all
        // and is dropped from Pure output (see the show-op guard).
        _pageGridOriginX = 0;
        (_pageCellWidth, _pageCellCeil, _pageMinX, _pageDominantFs, _pageRotDominant) = pv.pureLayout
            ? EstimatePageGrid(pv.contentStreams, pv.page.Dict, pv.reader,
                ExtractionOptions?.ScaleFactor ?? 1.0)
            : (0, 0, double.NaN, 0, false);
        // A clip rectangle re-anchors extraction to the window, not the page:
        // page-absolute columns/leading pads don't apply (the
        // rect-clipped output starts lines at the window edge).
        if (TextSearchOptions?.Rectangle is not null)
        {
            _pageMinX = double.NaN;
            // Rect-clipped extraction uses the exact ceiled-bucket cell (see
            // EstimatePageGrid note).
            if (_pageCellCeil > 0) _pageCellWidth = _pageCellCeil;
        }
        // The caller's rectangle is in VIEWER coordinates (the page as displayed,
        // after /Rotate); content-stream positions are in media coordinates. Map
        // the window through the inverse page rotation so the filters compare
        // like with like.
        _effectiveSearchRect = MapViewerRectToMedia(TextSearchOptions?.Rectangle, pv.page);
        _lineStartPageX = double.NaN;
        _lineStartTextOffset = _text.Length;
        _sawIntraLineGapSpaces = false;
        _collectOcrRuns = pv.pureLayout;
        _ocrRuns.Clear();
        _type3SpanRuns = 0;
        _pageLineStarts.Clear();
        _pageRunSpans.Clear();
    }
}
