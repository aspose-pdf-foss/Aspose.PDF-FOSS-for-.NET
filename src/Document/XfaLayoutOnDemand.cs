using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    // The pages a layout replaced, by dictionary, under the numbers they had: a caller still
    // holding one afterwards is answered with the form's page of that number.
    private Dictionary<PdfDictionary, int>? _xfaReplacedPages;

    private bool _xfaPagesLaidOut;

    /// <summary>True when the catalog asks a viewer to build the pages from the XFA packets: the
    /// pages the file came with only carry a notice to wait.</summary>
    private bool AsksForXfaRender
        => _reader.Catalog.Get("NeedsRendering") is { } needsRendering
            && _reader.Resolve(needsRendering) is PdfBoolean { Value: true }
            && _reader.ResolveDict(_reader.Catalog.Get("AcroForm")) is { } acroForm
            && acroForm.ContainsKey("XFA");

    /// <summary>True while such a form has not been laid out onto the pages, or its packets have
    /// changed since it was.</summary>
    internal bool AwaitsXfaLayout => !_xfaPagesLaidOut && AsksForXfaRender;

    /// <summary>The form's packets changed: the pages laid out from them are behind.</summary>
    internal void XfaPacketsChanged() => _xfaPagesLaidOut = false;

    /// <summary>Lays a form that awaits it out onto pages of its own, once, when a reader of page
    /// content starts; the XFA packets and the catalog stay as they are, so the document is still
    /// the form it was. With <paramref name="convert"/> the form becomes a standard one as well -
    /// for a caller that leaves no XFA behind - whether or not its pages were laid out before.
    /// A document nobody reads the pages of is never laid out. Returns whether it ran.</summary>
    internal bool LayOutXfaForm(bool convert = false)
    {
        if (convert ? !AsksForXfaRender : !AwaitsXfaLayout) return false;
        _xfaPagesLaidOut = true;
        _xfaReplacedPages ??= new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        for (var number = 1; number <= Pages.Count; number++)
            _xfaReplacedPages[Pages[number].Dict] = number;
        if (convert) Form.ConvertXfaAwaitingRender();
        else Form.LayOutXfaPages();
        // Whatever the layout itself wrote into the packets, the pages are those of the packets.
        _xfaPagesLaidOut = true;
        return true;
    }

    /// <summary>The page a reader of page content works on: the page itself, or - for a page a
    /// form's layout has replaced - the form's page of the same number.</summary>
    internal static Page PageToRead(Page page)
    {
        var document = page.OwnerDocument;
        if (document is null) return page;
        document.LayOutXfaForm();
        return document._xfaReplacedPages is { } replaced
            && replaced.TryGetValue(page.Dict, out var number)
            && number <= document.Pages.Count
                ? document.Pages[number]
                : page;
    }
}
