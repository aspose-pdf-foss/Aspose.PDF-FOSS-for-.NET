using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // Annotation flag bit 2: hidden (PDF 32000-1 §12.5.3).
    private const int HiddenFlag = 2;

    /// <summary>Give every visible annotation its structure element (PDF/UA-1 §7.18.1, §7.18.4):
    /// a form field's widget a Form, a link no paragraph took a Link, any other annotation an
    /// Annot, each holding an object reference to it. They follow their page's content.
    /// Printer's marks (never shown to a reader) are left out.</summary>
    private static void TagAnnotations(ITaggedContent tc, LS.StructureElement root, TaggingRun run)
    {
        var tagged = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var ls in run.LinkSlots)
            if (run.Pages.FirstOrDefault()?.Page.Reader.ResolveDict(ls.Annot) is { } d) tagged.Add(d);

        foreach (var pw in run.Pages)
        {
            var reader = pw.Page.Reader;
            if (reader.Resolve(pw.Page.Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var annotRef in annots)
            {
                if (reader.ResolveDict(annotRef) is not { } annot || tagged.Contains(annot)) continue;
                var subtype = annot.GetName("Subtype");
                if (subtype is "PrinterMark" || subtype is null) continue;
                if (reader.Resolve(annot.Get("F")) is PdfInteger f && (f.Value & HiddenFlag) != 0) continue;
                LS.StructureElement owner = subtype switch
                {
                    "Widget" => tc.CreateFormElement(),
                    "Link" => tc.CreateLinkElement(),
                    _ => tc.CreateAnnotElement(),
                };
                var objr = new LS.OBJRElement();
                objr.SetObj(annotRef);
                owner.AppendChild(objr);
                root.AppendChild(owner);
                run.LinkSlots.Add(new LinkSlot { Objr = objr, Link = owner, Annot = annotRef, Page = pw.Page });
                tagged.Add(annot);
            }
        }
    }

    /// <summary>Heading levels as a reader walks them (PDF/UA-1 §7.4.2): the first heading is
    /// level 1 and a heading is at most one level below the one before it. Levels ranked from
    /// font sizes can start low or skip (a document whose largest heading size never occurs
    /// before a smaller one).</summary>
    private static void NormalizeHeadingLevels(List<Block> blocks)
    {
        var previous = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != BlockKind.Heading) continue;
            var b = blocks[i];
            b.Level = System.Math.Min(b.Level, previous + 1);
            blocks[i] = b;
            previous = b.Level;
        }
    }
}
