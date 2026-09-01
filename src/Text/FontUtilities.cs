using Aspose.Pdf.Optimization;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Strategy for font subsetting when saving a document.
/// </summary>
public enum FontSubsetStrategy : byte
{
    /// <summary>No font subsetting.</summary>
    None,
    /// <summary>Subset all fonts used in the document.</summary>
    SubsetAllFonts,
    /// <summary>Subset only embedded fonts.</summary>
    SubsetEmbeddedFontsOnly,
}

/// <summary>
/// Provides font management utilities for a document.
/// </summary>
public sealed class FontUtilities : Document.IDocumentFontUtilities
{
    private readonly Document _document;

    internal FontUtilities(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// Returns all fonts referenced in the document (across all pages).
    /// </summary>
    public global::Aspose.Pdf.Text.Font[] GetAllFonts()
    {
        var reader = _document.Reader;
        // Dedup by physical font-object identity (object number), NOT by /BaseFont
        // name: two distinct font dictionaries that happen to share a base name
        // (e.g. a subset and a full face, or two subsets) are both real fonts and
        // must both be reported, while the same font reused across many pages is
        // reported once. Direct (inline) font dicts have no object number, so they
        // get a unique negative id and are always kept.
        var seenFonts = new HashSet<int>();
        var visitedXObjects = new HashSet<int>(); // cycle guard for nested form XObjects
        var result = new List<global::Aspose.Pdf.Text.Font>();

        void Collect(Aspose.Pdf.Core.PdfDictionary? resources)
        {
            if (resources is null) return;

            var fontDict = reader.ResolveDict(resources.Get("Font"));
            if (fontDict is not null)
            {
                foreach (var key in fontDict.Keys)
                {
                    var entry = fontDict.Get(key);
                    var fd = reader.ResolveDict(entry);
                    if (fd is null) continue;
                    var id = entry is Aspose.Pdf.Core.PdfIndirectRef r ? r.ObjectNumber : -(result.Count + 1);
                    if (seenFonts.Add(id))
                        result.Add(new global::Aspose.Pdf.Text.Font(key, fd, reader));
                }
            }

            // Form XObjects carry their own /Resources/Font — fonts used only
            // inside a form (e.g. a stamp or appearance stream) live there and
            // would otherwise be missed.
            var xobjDict = reader.ResolveDict(resources.Get("XObject"));
            if (xobjDict is not null)
            {
                foreach (var key in xobjDict.Keys)
                {
                    var entry = xobjDict.Get(key);
                    if (entry is Aspose.Pdf.Core.PdfIndirectRef xr && !visitedXObjects.Add(xr.ObjectNumber))
                        continue;
                    var xstream = reader.ResolveStream(entry);
                    if (xstream is null) continue;
                    Collect(reader.ResolveDict(xstream.Dict.Get("Resources")));
                }
            }
        }

        // Page content fonts first (this is the order callers expect), then the
        // AcroForm default resources (/AcroForm /DR /Font): form-field fonts such
        // as the base Helvetica/ZapfDingbats used by widget appearances live only
        // in /DR and are appended after the page fonts. Identity dedup keeps a font
        // that appears in both places single.
        foreach (var page in _document.Pages)
            Collect(reader.ResolveDict(page.Dict.Get("Resources")));

        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is not null)
            Collect(reader.ResolveDict(acroForm.Get("DR")));

        return result.ToArray();
    }

    /// <summary>
    /// Apply a font subsetting strategy to the document.
    /// Scans all pages for font usage and removes unused glyph data from embedded fonts.
    /// </summary>
    public void SubsetFonts(FontSubsetStrategy subsetStrategy)
    {
        if (subsetStrategy == FontSubsetStrategy.None) return;

        if (subsetStrategy == FontSubsetStrategy.SubsetAllFonts)
        {
            // SubsetAllFonts covers fonts the document does NOT embed as well: give each
            // used non-embedded face (including the Standard-14, resolved to a system
            // face) a real program first, then subset. The fresh programs are pending
            // objects until save, so the subsetter needs the pending-stream resolver;
            // stripStandard14 stays off or the optimizer would undo the embed.
            _document.EmbedAllFontsForSubsetting();
            FontSubsetter.SubsetFonts(_document.Reader, subsetEmbedded: true,
                resolveNewStream: _document.ResolvePendingStreamInternal, stripStandard14: false);
            return;
        }

        FontSubsetter.SubsetFonts(_document.Reader, subsetEmbedded: true);
    }
}
