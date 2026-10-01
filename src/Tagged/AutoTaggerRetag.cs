using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    /// <summary>Per page, per MCID: what a previous structure tree said about that content.</summary>
    private sealed class CarriedProperties
    {
        public readonly Dictionary<PdfDictionary, Dictionary<int, Dictionary<string, PdfString>>> ByPage =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<string, PdfString> At(PdfDictionary page, int mcid)
        {
            if (!ByPage.TryGetValue(page, out var byMcid)) ByPage[page] = byMcid = new();
            if (!byMcid.TryGetValue(mcid, out var props)) byMcid[mcid] = props = new(StringComparer.Ordinal);
            return props;
        }

        public Dictionary<string, PdfString>? Find(PdfDictionary page, int mcid) =>
            ByPage.TryGetValue(page, out var byMcid) && byMcid.TryGetValue(mcid, out var props) ? props : null;
    }

    // An element's replacement text or description stands for ALL of its content: the first
    // piece carries it and the rest are replaced by nothing. An expansion belongs to the first
    // piece only. A language holds for every piece, the innermost element's winning.
    private static readonly string[] ReplacingKeys = ["ActualText", "Alt"];

    /// <summary>Read, before retagging replaces it, what the existing structure tree says about
    /// its content: an element's /ActualText, /Alt, /E and /Lang, keyed by the marked content
    /// they cover. Retagging rebuilds the structure from the layout, but these are an author's
    /// statements the layout cannot recover.</summary>
    private static CarriedProperties CollectCarriedProperties(Document document)
    {
        var reader = document.Reader;
        var carried = new CarriedProperties();
        if (reader.ResolveDict(reader.Catalog.Get("StructTreeRoot")) is not { } root) return carried;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        List<(PdfDictionary Page, int Mcid)> Visit(PdfObject? node, PdfDictionary? page, int depth)
        {
            var content = new List<(PdfDictionary, int)>();
            if (depth > 128) return content;
            var resolved = reader.Resolve(node);
            if (resolved is PdfArray kids)
            {
                foreach (var kid in kids) content.AddRange(Visit(kid, page, depth + 1));
                return content;
            }
            if (resolved is PdfInteger mcid)
            {
                if (page is not null) content.Add((page, (int)mcid.Value));
                return content;
            }
            if (resolved is not PdfDictionary elem || !seen.Add(elem)) return content;
            page = reader.ResolveDict(elem.Get("Pg")) ?? page;
            var type = elem.GetName("Type");
            if (type == "MCR")
            {
                var stm = elem.Get("Stm");
                if (stm is null && page is not null && reader.Resolve(elem.Get("MCID")) is PdfInteger m)
                    content.Add((page, (int)m.Value));
                return content;
            }
            if (type == "OBJR") return content;

            content.AddRange(Visit(elem.Get("K"), page, depth + 1));
            if (content.Count == 0) return content;
            // The element is done after its descendants: an outer replacement overrides an inner
            // one, an inner language stays.
            foreach (var key in ReplacingKeys)
                if (reader.Resolve(elem.Get(key)) is PdfString text)
                    for (var i = 0; i < content.Count; i++)
                        carried.At(content[i].Item1, content[i].Item2)[key] =
                            i == 0 ? text : new PdfString([]);
            if (reader.Resolve(elem.Get("E")) is PdfString expansion)
                carried.At(content[0].Item1, content[0].Item2)["E"] = expansion;
            if (reader.Resolve(elem.Get("Lang")) is PdfString lang && lang.Value.Length > 0)
                foreach (var (p, m) in content)
                {
                    var props = carried.At(p, m);
                    if (!props.ContainsKey("Lang")) props["Lang"] = lang;
                }
            return content;
        }

        Visit(root.Get("K"), null, 0);
        return carried;
    }

    /// <summary>The properties a mark of the previous tree hands on: its own property list's,
    /// else those its structure element gave.</summary>
    private static IReadOnlyDictionary<string, PdfString>? CarriedFor(
        ContentOp op, PdfDictionary page, CarriedProperties carried)
    {
        if (op.MarkMcid < 0) return null;
        Dictionary<string, PdfString>? result = null;
        if (carried.Find(page, op.MarkMcid) is { } fromTree)
            result = new Dictionary<string, PdfString>(fromTree, StringComparer.Ordinal);
        if (op.MarkProps is { } own)
            foreach (var key in new[] { "ActualText", "Alt", "E", "Lang" })
                if (own.Get(key) is PdfString s)
                    (result ??= new Dictionary<string, PdfString>(StringComparer.Ordinal))[key] = s;
        return result;
    }

    /// <summary>A Figure the old tree described keeps its description: the first /Alt (or
    /// /ActualText) in force over one of the figure's images becomes the new element's /Alt.</summary>
    private static void CarryFigureAlternates(PageWork pw, int[] targets, TaggingRun run, CarriedProperties carried)
    {
        var stack = new Stack<IReadOnlyDictionary<string, PdfString>?>();
        for (var i = 0; i < pw.Ops.Count; i++)
        {
            var op = pw.Ops[i];
            // The old tree is keyed by the page's MCIDs: a mark inside a form said nothing it kept.
            if (op.Kind == ContentOpKind.BeginMark) { stack.Push(op.Stream == 0 ? CarriedFor(op, pw.Page.Dict, carried) : null); continue; }
            if (op.Kind == ContentOpKind.EndMark) { if (stack.Count > 0) stack.Pop(); continue; }
            if (op.Kind is not (ContentOpKind.Image or ContentOpKind.InlineImage or ContentOpKind.Form)) continue;
            var t = targets[i];
            if (t < 0 || t >= run.Slots.Count || run.Slots[t].Kind != SlotKind.Figure) continue;
            if (run.Slots[t].El.ParentElement is not LS.StructureElement figure
                || !string.IsNullOrEmpty(figure.AlternativeText)) continue;
            foreach (var props in stack)
            {
                if (props is null) continue;
                var text = props.TryGetValue("Alt", out var alt) && alt.Value.Length > 0 ? alt
                    : props.TryGetValue("ActualText", out var at) && at.Value.Length > 0 ? at : null;
                if (text is null) continue;
                figure.AlternativeText = text.ToText();
                break;
            }
        }
    }
}
