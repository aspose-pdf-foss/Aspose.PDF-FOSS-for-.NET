using System.Linq;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// PDF/UA-1 clause 7.15: a conforming file uses no dynamic XFA form. Returns false - the
    /// conversion refused, the document untouched - for a dynamic form that is XFA alone: its
    /// pages hold only what a reader shows before the form renders (typically a "please wait"
    /// notice), so taking the XFA away would leave a document without its content. Any other
    /// XFA (a static form, or one beside interactive form fields that carry the form) is
    /// logged, and removed when the conversion deletes what it cannot keep.
    /// </summary>
    private bool AdmitXfaForUa(PdfAConvertState pa)
    {
        var acroForm = _reader.ResolveDict(_reader.Catalog.Get("AcroForm"));
        if (acroForm?.Get("XFA") is null) return true;
        var dynamic = Form.Type == Forms.FormType.Dynamic
            || _reader.Resolve(_reader.Catalog.Get("NeedsRendering")) is PdfBoolean { Value: true };
        var hasFields = _reader.Resolve(acroForm.Get("Fields")) is PdfArray { Count: > 0 };
        if (dynamic && !hasFields)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "XFA",
                Clause = "7.15",
                Section = "XFA",
                Description = "The document is a dynamic XFA form with no interactive form fields; its pages hold no form content, so it cannot be converted",
                Convertable = false,
            });
            return false;
        }

        pa.options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "XFA",
            Clause = "7.15",
            Section = "XFA",
            Description = dynamic ? "Dynamic XFA forms shall not be used" : "The document carries an XFA form",
            Convertable = pa.strip,
        });
        if (pa.strip)
        {
            acroForm.Remove("XFA");
            _reader.Catalog.Remove("NeedsRendering");
        }
        return true;
    }

    /// <summary>True when the document names this library (any version) as its /Info producer.</summary>
    /// <remarks>The producer is the only statement a file makes about who wrote it. Every save
    /// by this library stamps it, so a file another producer tagged and this library merely
    /// re-saved reads as its own; a document built in memory and not yet saved names no
    /// producer, and does not.</remarks>
    private bool IsProducedByThisLibrary() =>
        (_reader.Resolve(_reader.ResolveDict(_reader.Trailer.Get("Info"))?.Get("Producer")) as PdfString)
            ?.ToText().StartsWith(BuildVersionInfo.ProducerName, StringComparison.Ordinal) == true;

    /// <summary>Give every link annotation that lacks one an alternate description, taken
    /// from the address it opens. A link's visible text says where it goes to a reader who
    /// can see the page; assistive technology reads /Contents instead, and a link without
    /// one announces nothing (PDF/UA-1 clause 7.18.5).</summary>
    /// <remarks>Only statements of fact are written: the URI a link opens (that address IS
    /// what the link does), otherwise the text the link covers on the page (what a sighted
    /// reader reads as the link), otherwise the file a link into another document opens,
    /// otherwise, for a link into the document itself, the line where it lands. A popup,
    /// which shows its parent's note, is described by that note; a media annotation by the
    /// file it plays.</remarks>
    private void DescribeLinkAnnotations(PdfFormatConversionOptions? options = null)
    {
        var targets = new LinkTargets();
        foreach (var page in Pages)
        {
            if (_reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            List<(Rectangle Box, string Text)>? pageText = null;
            foreach (var entry in annots)
            {
                var annot = _reader.ResolveDict(entry);
                var subtype = annot?.GetName("Subtype");
                if (annot is null || subtype is not ("Link" or "Popup" or "RichMedia" or "Movie")) continue;
                if (_reader.Resolve(annot.Get("Contents")) is PdfString { } existing
                    && existing.ToText().Length > 0) continue;
                if (subtype is "RichMedia" or "Movie")
                {
                    if (MediaFileName(annot) is { } media)
                        annot.Set("Contents", Forms.Field.EncodePdfTextString(media));
                    continue;
                }
                if (subtype == "Popup")
                {
                    if (_reader.ResolveDict(annot.Get("Parent")) is { } parent
                        && _reader.Resolve(parent.Get("Contents")) is PdfString { } note
                        && note.ToText().Length > 0)
                        annot.Set("Contents", note);
                    continue;
                }
                var action = _reader.ResolveDict(annot.Get("A"));
                if (action?.GetName("S") == "URI")
                {
                    if (_reader.Resolve(action.Get("URI")) is PdfString uri && uri.ToText().Length > 0)
                        annot.Set("Contents", new PdfString(Encoding.UTF8.GetBytes(uri.ToText())));
                    continue;
                }
                pageText ??= PageTextBoxes(page);
                var text = LinkCoveredText(annot, pageText) ?? LinkTargetFile(action) ?? LinkTargetLine(annot, targets);
                if (!string.IsNullOrWhiteSpace(text))
                    annot.Set("Contents", Forms.Field.EncodePdfTextString(text!));
                else
                    options?.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "LinkDescription",
                        Clause = "7.18.5",
                        Section = "Annotations",
                        PageNumber = page.Number,
                        Description = "A link has no description (Contents): it covers no text and its target states none, so the author has to describe it",
                        Convertable = false,
                    });
            }
        }
    }

    /// <summary>PDF/UA-1 clause 7.10: every optional-content configuration names itself and
    /// carries no /AS (the automatic state changes the reader, not the user, controls).</summary>
    private void RepairOptionalContentConfigsForUa()
    {
        if (_reader.ResolveDict(_reader.Catalog.Get("OCProperties")) is not { } ocProps) return;
        void Repair(PdfObject? cfgObj, string name)
        {
            if (_reader.ResolveDict(cfgObj) is not { } cfg) return;
            cfg.Remove("AS");
            if (_reader.Resolve(cfg.Get("Name")) is not PdfString { } n || n.ToText().Length == 0)
                cfg.Set("Name", Forms.Field.EncodePdfTextString(name));
        }
        Repair(ocProps.Get("D"), "Default");
        if (_reader.Resolve(ocProps.Get("Configs")) is PdfArray configs)
            for (var i = 0; i < configs.Count; i++)
                Repair(configs[i], $"Configuration {i + 1}");
    }

    /// <summary>PDF/UA-1 clause 7.11: the file specification of an embedded file carries
    /// both /F and /UF. The one the file states is copied to the one it lacks; a file that
    /// names itself in neither takes the key it is filed under in the EmbeddedFiles tree.</summary>
    private void CompleteEmbeddedFileSpecsForUa()
    {
        void Complete(PdfDictionary? fs, string? key)
        {
            if (fs is null || fs.Get("EF") is null) return;
            var uf = (_reader.Resolve(fs.Get("UF")) as PdfString)?.ToText();
            var f = (_reader.Resolve(fs.Get("F")) as PdfString)?.ToText();
            var name = !string.IsNullOrEmpty(uf) ? uf : !string.IsNullOrEmpty(f) ? f : key;
            if (string.IsNullOrEmpty(name)) return;
            if (string.IsNullOrEmpty(uf)) fs.Set("UF", Forms.Field.EncodePdfTextString(name!));
            if (string.IsNullOrEmpty(f)) fs.Set("F", Forms.Field.EncodePdfTextString(name!));
        }

        void Walk(PdfObject? node, int depth)
        {
            if (depth > 32 || _reader.ResolveDict(node) is not { } dict) return;
            if (_reader.Resolve(dict.Get("Names")) is PdfArray names)
                for (var i = 0; i + 1 < names.Count; i += 2)
                    Complete(_reader.ResolveDict(names[i + 1]), (_reader.Resolve(names[i]) as PdfString)?.ToText());
            if (_reader.Resolve(dict.Get("Kids")) is PdfArray kids)
                foreach (var kid in kids) Walk(kid, depth + 1);
        }

        // Direct file specifications nested in any object (an attachment annotation's /FS is
        // usually one). Every object is visited, not only the reachable ones: the file keeps
        // an orphaned attachment, and a validator checks it all the same.
        void Visit(PdfObject? obj, int depth)
        {
            if (depth > 8) return;
            if (obj is PdfDictionary dict)
            {
                Complete(dict, null);
                foreach (var key in dict.Keys.ToList()) Visit(dict.Get(key), depth + 1);
            }
            else if (obj is PdfArray arr)
                foreach (var item in arr) Visit(item, depth + 1);
        }

        var nameTree = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (nameTree is not null) Walk(nameTree.Get("EmbeddedFiles"), 0);
        foreach (var entry in _reader.XRefTable.Entries.Values.ToList())
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;
            PdfObject? obj;
            try { obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation)); }
            catch { continue; }
            Visit(obj is PdfStream s ? s.Dict : obj, 0);
        }
    }

    /// <summary>The page's text fragments with their boxes (empty when the page cannot be read).</summary>
    private static List<(Rectangle Box, string Text)> PageTextBoxes(Page page)
    {
        var absorber = new Text.TextFragmentAbsorber();
        try { page.Accept(absorber); }
        catch { return new List<(Rectangle, string)>(); }
        return absorber.TextFragments.Cast<Text.TextFragment>()
            .Where(f => f.Rectangle is not null && !string.IsNullOrWhiteSpace(f.Text))
            .Select(f => (f.Rectangle!, f.Text)).ToList();
    }

    /// <summary>The text whose fragments are centred inside the link's /Rect, in content order.</summary>
    private string? LinkCoveredText(PdfDictionary annot, List<(Rectangle Box, string Text)> pageText)
    {
        if (_reader.Resolve(annot.Get("Rect")) is not PdfArray { Count: 4 } rectArr) return null;
        var r = Rectangle.FromPdfArray(rectArr);
        double x0 = Math.Min(r.LLX, r.URX), x1 = Math.Max(r.LLX, r.URX);
        double y0 = Math.Min(r.LLY, r.URY), y1 = Math.Max(r.LLY, r.URY);
        var parts = pageText.Where(t =>
        {
            var cx = (t.Box.LLX + t.Box.URX) / 2;
            var cy = (t.Box.LLY + t.Box.URY) / 2;
            return cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1;
        }).Select(t => t.Text.Trim());
        var text = string.Join(" ", parts);
        return text.Length > 0 ? text : null;
    }

    /// <summary>The file a media annotation plays: a RichMedia annotation's first asset (its
    /// name in the assets tree, else its file specification), a Movie annotation's movie file.</summary>
    private string? MediaFileName(PdfDictionary annot)
    {
        string? SpecName(PdfObject? spec) => _reader.Resolve(spec) switch
        {
            PdfString s => s.ToText(),
            PdfDictionary fs => (_reader.Resolve(fs.Get("UF")) as PdfString)?.ToText()
                ?? (_reader.Resolve(fs.Get("F")) as PdfString)?.ToText(),
            _ => null,
        };
        if (_reader.ResolveDict(annot.Get("RichMediaContent")) is { } content
            && _reader.ResolveDict(content.Get("Assets")) is { } assets
            && _reader.Resolve(assets.Get("Names")) is PdfArray { Count: >= 2 } names)
        {
            var name = (_reader.Resolve(names[0]) as PdfString)?.ToText();
            return !string.IsNullOrWhiteSpace(name) ? name : SpecName(names[1]);
        }
        if (_reader.ResolveDict(annot.Get("Movie")) is { } movie) return SpecName(movie.Get("F"));
        return null;
    }

    // A link description taken from its target is cut to this many characters (at a word).
    private const int MaxTargetLineLength = 120;

    /// <summary>What <see cref="LinkTargetLine"/> reads once per document: page numbers and
    /// each target page's text.</summary>
    private sealed class LinkTargets
    {
        public Dictionary<PdfDictionary, int>? PageNumbers;
        public readonly Dictionary<int, List<(Rectangle Box, string Text)>> Text = new();
    }

    /// <summary>For a link into the document itself: the line of text where it lands - the
    /// first line at or below its destination's top (a heading, most often), or the page's
    /// first line when the destination names no position.</summary>
    private string? LinkTargetLine(PdfDictionary annot, LinkTargets targets)
    {
        if (targets.PageNumbers is null)
        {
            targets.PageNumbers = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
            foreach (var p in Pages) targets.PageNumbers[p.Dict] = p.Number;
        }
        if (Converters.PdfToHtmlConverter.TryResolveDestPoint(annot, this, _reader, targets.PageNumbers) is not { } target
            || target.targetPage < 1 || target.targetPage > Pages.Count) return null;
        if (!targets.Text.TryGetValue(target.targetPage, out var text))
            targets.Text[target.targetPage] = text = PageTextBoxes(Pages[target.targetPage]);

        var below = text.Where(t => t.Box.URY <= target.y + 1).OrderByDescending(t => t.Box.URY).ThenBy(t => t.Box.LLX).ToList();
        if (below.Count == 0) below = text.OrderByDescending(t => t.Box.URY).ThenBy(t => t.Box.LLX).ToList();
        if (below.Count == 0) return null;
        var lineBottom = below[0].Box.LLY;
        var line = string.Join(" ", below.Where(t => Math.Abs(t.Box.LLY - lineBottom) < 2).OrderBy(t => t.Box.LLX).Select(t => t.Text.Trim()));
        line = string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length > MaxTargetLineLength)
        {
            var cut = line.LastIndexOf(' ', MaxTargetLineLength);
            line = line[..(cut > 0 ? cut : MaxTargetLineLength)];
        }
        return line.Any(char.IsLetterOrDigit) ? line : null;
    }

    /// <summary>The file a GoToR or Launch action opens, as its file specification names it.</summary>
    private string? LinkTargetFile(PdfDictionary? action)
    {
        if (action?.GetName("S") is not ("GoToR" or "Launch")) return null;
        var spec = _reader.Resolve(action.Get("F"));
        if (spec is PdfString s) return s.ToText();
        if (spec is PdfDictionary fs)
            return (_reader.Resolve(fs.Get("UF")) as PdfString)?.ToText()
                ?? (_reader.Resolve(fs.Get("F")) as PdfString)?.ToText();
        return null;
    }
}
