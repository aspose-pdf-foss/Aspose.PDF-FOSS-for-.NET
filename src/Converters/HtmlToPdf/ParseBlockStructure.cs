using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Recursively map one HTML node (and its subtree) onto structure elements
    /// appended under <paramref name="parent"/>, per the mapping in
    /// <see cref="BuildLogicalStructure(Document, string)"/>.</summary>
    private static void EmitStructureElement(HtmlNode node,
        Aspose.Pdf.LogicalStructure.StructureElement parent, Tagged.ITaggedContent tc)
    {
        switch (node.Tag)
        {
            case "div":
            {
                var d = tc.CreateDivElement();
                parent.AppendChild(d);
                foreach (var c in node.Children) EmitStructureElement(c, d, tc);
                break;
            }
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                parent.AppendChild(tc.CreateHeaderElement(node.Tag[1] - '0'));
                break;
            case "p":
                parent.AppendChild(tc.CreateParagraphElement());
                break;
            case "ul": case "ol":
            {
                var l = tc.CreateListElement();
                parent.AppendChild(l);
                foreach (var c in node.Children) EmitStructureElement(c, l, tc);
                break;
            }
            case "li":
            {
                // A list item expands to LI → { Lbl (bullet/label), [Link], LBody (text) };
                // its inline children are represented by these, not walked further.
                var li = tc.CreateListLIElement();
                parent.AppendChild(li);
                li.AppendChild(tc.CreateListLblElement());
                if (HasDescendant(node, "a"))
                    li.AppendChild(tc.CreateLinkElement());
                li.AppendChild(tc.CreateListLBodyElement());
                break;
            }
            case "img":
            {
                var fig = tc.CreateFigureElement();
                if (node.Attrs is not null && node.Attrs.TryGetValue("alt", out var alt)
                    && !string.IsNullOrEmpty(alt))
                    fig.AlternativeText = alt;
                parent.AppendChild(fig);
                break;
            }
            case "input":
            {
                // Each rendered interactive control becomes a Form structure element
                // (wrapping the widget's object reference). A type="hidden" input has no
                // widget and produces nothing.
                var type = node.Attrs is not null && node.Attrs.TryGetValue("type", out var ty)
                    ? ty.Trim().ToLowerInvariant() : "";
                if (type != "hidden")
                    parent.AppendChild(tc.CreateFormElement());
                break;
            }
            case "textarea":
            case "select":
            case "button":
                parent.AppendChild(tc.CreateFormElement());
                break;
            case "b": case "strong": case "u": case "i": case "em":
            {
                // Inline emphasis inside a TABLE CELL becomes a Span element — the
                // cell's content model is structured per run. Free-flow emphasis
                // (and empty icon elements) melts into its paragraph's text and
                // produces no element of its own.
                var inCell = false;
                for (var a = node.Parent; a is not null; a = a.Parent)
                    if (a.Tag is "td" or "th") { inCell = true; break; }
                var hasText = !string.IsNullOrWhiteSpace(node.Text);
                if (!hasText)
                    foreach (var dnode in node.Descendants())
                        if (!string.IsNullOrWhiteSpace(dnode.Text)) { hasText = true; break; }
                if (!inCell || !hasText) break;
                var sp = tc.CreateSpanElement();
                parent.AppendChild(sp);
                foreach (var c in node.Children) EmitStructureElement(c, sp, tc);
                break;
            }
            case "table":
            {
                var tbl = tc.CreateTableElement();
                parent.AppendChild(tbl);
                foreach (var c in node.Children) EmitStructureElement(c, tbl, tc);
                break;
            }
            case "thead":
            {
                var th = tc.CreateTableTHeadElement();
                parent.AppendChild(th);
                foreach (var c in node.Children) EmitStructureElement(c, th, tc);
                break;
            }
            case "tbody":
            {
                var tb = tc.CreateTableTBodyElement();
                parent.AppendChild(tb);
                foreach (var c in node.Children) EmitStructureElement(c, tb, tc);
                break;
            }
            case "tfoot":
            {
                var tf = tc.CreateTableTFootElement();
                parent.AppendChild(tf);
                foreach (var c in node.Children) EmitStructureElement(c, tf, tc);
                break;
            }
            case "tr":
            {
                var tr = tc.CreateTableTRElement();
                parent.AppendChild(tr);
                foreach (var c in node.Children) EmitStructureElement(c, tr, tc);
                break;
            }
            case "th":
            {
                var thc = tc.CreateTableTHElement();
                parent.AppendChild(thc);
                foreach (var c in node.Children) EmitStructureElement(c, thc, tc);
                break;
            }
            case "td":
            {
                var td = tc.CreateTableTDElement();
                parent.AppendChild(td);
                foreach (var c in node.Children) EmitStructureElement(c, td, tc);
                break;
            }
            // Transparent wrappers: descend without emitting an element of their own.
            case "html": case "body": case "#root": case "section": case "article": case "main":
                foreach (var c in node.Children) EmitStructureElement(c, parent, tc);
                break;
            // Everything else (inline runs, text nodes, br, label, span, a-in-flow, select,
            // button, …) produces no structure element of its own, but may CONTAIN block,
            // table or form descendants (e.g. an <input> wrapped in a <label> or <span>), so
            // descend into it without emitting.
            default:
                foreach (var c in node.Children) EmitStructureElement(c, parent, tc);
                break;
        }
    }

    /// <summary>Register an embedded-font indirect reference under <paramref name="resName"/>
    /// in a page's /Resources/Font (resolving indirect Resources/Font so the originals
    /// aren't replaced); idempotent per page.</summary>
    private static void RegisterPageFont(Page page, string resName, Core.PdfIndirectRef fontRef)
    {
        var reader = page.Reader;
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary
            ?? reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary
            ?? reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName)) fontDict.Set(resName, fontRef);
    }

    /// <summary>Build one <see cref="Forms.RadioButtonField"/> per HTML radio group (keyed by
    /// the input `name`; unnamed radios each form their own group) from the options collected
    /// during layout. Each option becomes a circle-styled <see cref="Forms.RadioButtonOptionField"/>
    /// kid with a visible border, so after save+reload it surfaces on Form.Fields.</summary>
    private static void EmitRadioGroups(Document doc,
        List<(string group, bool chk, Page page, Rectangle rect)> options)
    {
        if (options.Count == 0) return;
        var groups = new List<(string key, List<(bool chk, Page page, Rectangle rect)> opts)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var anon = 0;
        foreach (var (g, chk, page, rect) in options)
        {
            var key = string.IsNullOrEmpty(g) ? "__radio" + anon++ : g;
            if (!index.TryGetValue(key, out var gi))
            {
                gi = groups.Count; index[key] = gi;
                groups.Add((key, new List<(bool, Page, Rectangle)>()));
            }
            groups[gi].opts.Add((chk, page, rect));
        }

        foreach (var (key, opts) in groups)
        {
            try
            {
                var firstPage = opts[0].page;
                var rbf = new Forms.RadioButtonField(firstPage);
                var oi = 0;
                foreach (var (chk, page, rect) in opts)
                {
                    var opt = new Forms.RadioButtonOptionField(page, rect)
                    {
                        Style = Forms.BoxStyle.Circle,
                        OptionName = key + "_" + oi++,
                    };
                    opt.Characteristics.Border = System.Drawing.Color.Black;
                    rbf.Add(opt);
                }
                doc.Form.Add(rbf, firstPage.Number);
            }
            catch { /* best-effort radio emission */ }
        }
    }

    /// <summary>Remove /Font entries on each page that no content stream references via a
    /// "/Name … Tf" operator. Only provably-unused fonts are dropped (rendering unchanged).</summary>
    private static void PruneUnusedFonts(Document doc)
    {
        // Every page of one conversion shares a single /Font dictionary (EnsureFonts),
        // so the used-name set must be the UNION across all pages sharing that dict —
        // pruning per page would let a page with no text wipe the fonts of the others.
        var usedByDict = new Dictionary<Core.PdfDictionary, HashSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var page in doc.Pages)
        {
            var reader = page.Reader;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            var fontDict = resources is null ? null : reader.ResolveDict(resources.Get("Font"));
            if (fontDict is null) continue;

            if (!usedByDict.TryGetValue(fontDict, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByDict[fontDict] = used;
            }
            var content = page.GetContentStreamBytes();
            if (content is null) continue;
            var text = Encoding.ASCII.GetString(content);
            foreach (Match m in Regex.Matches(text, @"/([A-Za-z0-9.+\-]+)\s+[-\d.]+\s+Tf"))
                used.Add(m.Groups[1].Value);
        }

        foreach (var (fontDict, used) in usedByDict)
            foreach (var key in new List<string>(fontDict.Keys))
                if (!used.Contains(key)) fontDict.Remove(key);
    }
}
