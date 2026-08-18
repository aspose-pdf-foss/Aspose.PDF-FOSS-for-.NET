namespace Aspose.Pdf.Text;

/// <summary>
/// Collects all fonts used in a PDF document or a single page.
/// After calling <see cref="Visit(Document)"/> or <see cref="Visit(Page)"/>,
/// the <see cref="Fonts"/> collection contains one <see cref="Font"/> entry per
/// distinct font OBJECT encountered — two page resources referencing the same
/// indirect font dictionary yield one entry, while two separate objects with the
/// same /BaseFont (a per-section duplicate embed) stay separate entries.
/// </summary>
public sealed class FontAbsorber
{
    private readonly List<Font> _fonts = new();
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the fonts collected after the last <c>Visit</c> call.</summary>
    public IReadOnlyList<FontInfo> FontInfos => _fonts;

    /// <summary>Fonts collected — exposed through the <see cref="FontCollection"/> surface.</summary>
    public FontCollection Fonts
    {
        get
        {
            var col = new FontCollection();
            foreach (var f in _fonts)
                col.Add(f, out _);
            return col;
        }
    }

    /// <summary>
    /// Visit all pages of a document and collect their fonts.
    /// Clears any previously collected fonts before visiting.
    /// </summary>
    public void Visit(Document pdf)
    {
        _fonts.Clear();
        _seen.Clear();
        foreach (var page in pdf.Pages)
            VisitPage(page);
    }

    /// <summary>Visit a slice of pages (<paramref name="startPage"/> 1-based, <paramref name="pageCount"/> consecutive pages).</summary>
    public void Visit(Document pdf, int startPage, int pageCount)
    {
        if (pdf is null) throw new ArgumentNullException(nameof(pdf));
        _fonts.Clear();
        _seen.Clear();
        var end = Math.Min(pdf.PageCount, startPage + pageCount - 1);
        for (int i = Math.Max(1, startPage); i <= end; i++)
            VisitPage(pdf.Pages.At(i));
    }

    /// <summary>
    /// Visit a single page and add its fonts to the collection.
    /// Does not clear previously collected fonts.
    /// </summary>
    public void Visit(Page page)
    {
        VisitPage(page);
    }

    private void VisitPage(Page page)
    {
        foreach (var font in page.Fonts)
        {
            // Dedupe by the underlying font DICTIONARY object: the reader resolves an
            // indirect object to one instance, so a font shared across pages registers
            // once while same-name duplicate objects register separately.
            if (_seen.Add(font.FontDict))
                _fonts.Add(font);
        }
    }
}
