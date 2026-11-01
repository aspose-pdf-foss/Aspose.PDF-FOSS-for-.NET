using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class TextStamp
{
    /// <summary>The page's name for a graphics state equal to <paramref name="state"/>: the one an earlier
    /// stamp of the same opacity registered, else a new one. Stamps tiled over a page then draw through one
    /// state; a new state per stamp grew the page's /ExtGState with every stamp, and each stamp form
    /// carries a copy of that dictionary, so the file grew with the square of the stamp count.</summary>
    private static string SharedGraphicsState(Page page, Content.ExtGState state)
    {
        var wanted = state.ToPdfDictionary();
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (page.Reader.ResolveDict(resources?.Get("ExtGState")) is { } states)
        {
            foreach (var key in states.Keys)
                if (page.Reader.ResolveDict(states.Get(key)) is { } held && SameEntries(held, wanted))
                    return key;
        }
        return page.AddExtGStateDict(wanted);
    }

    /// <summary>True when <paramref name="held"/> has exactly the entries of <paramref name="wanted"/>,
    /// whose values are names, numbers and booleans.</summary>
    private static bool SameEntries(PdfDictionary held, PdfDictionary wanted)
    {
        if (held.Count != wanted.Count) return false;
        foreach (var key in wanted.Keys)
        {
            var same = (held.Get(key), wanted.Get(key)) switch
            {
                (PdfName a, PdfName b) => a.Value == b.Value,
                (PdfBoolean a, PdfBoolean b) => a.Value == b.Value,
#pragma warning disable S1244 // the very values a stamp wrote: equal only when exactly equal
                (PdfInteger or PdfReal, PdfInteger or PdfReal) => Number(held.Get(key)!) == Number(wanted.Get(key)!),
#pragma warning restore S1244
                _ => false,
            };
            if (!same) return false;
        }
        return true;
    }

    private static double Number(PdfObject value) => value is PdfInteger i ? i.Value : ((PdfReal)value).Value;
}
