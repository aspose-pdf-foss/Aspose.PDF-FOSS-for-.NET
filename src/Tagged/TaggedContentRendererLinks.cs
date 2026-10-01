using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;
using LS = Aspose.Pdf.LogicalStructure;
namespace Aspose.Pdf.Tagged;

internal static partial class TaggedContentRenderer
{
    private sealed partial class Engine
    {
        /// <summary>Every rendered link becomes a Link annotation with an OBJR child under its structure element, and takes its own parent-tree key.</summary>
        private int WireLinkStructure(PdfArray nums, int nextKey,
            Dictionary<PdfDictionary, PdfIndirectRef> refs)
        {

            // Link annotations + OBJR children.
            foreach (var (link, page, llx, lly, urx, ury) in _links)
            {
                var annot = new PdfDictionary();
                annot.Set("Type", new PdfName("Annot"));
                annot.Set("Subtype", new PdfName("Link"));
                var rect = new PdfArray();
                rect.Add(new PdfReal(llx));
                rect.Add(new PdfReal(lly));
                rect.Add(new PdfReal(urx));
                rect.Add(new PdfReal(ury));
                annot.Set("Rect", rect);
                annot.Set("F", new PdfInteger(4));
                var bs = new PdfDictionary();
                bs.Set("W", new PdfInteger(0));
                annot.Set("BS", bs);
                annot.Set("BE", new PdfDictionary());
                if (link.Hyperlink?.Url is { } url)
                {
                    var action = new PdfDictionary();
                    action.Set("S", new PdfName("URI"));
                    action.Set("URI", new PdfString(System.Text.Encoding.ASCII.GetBytes(url)));
                    annot.Set("A", action);
                }
                if (!string.IsNullOrEmpty(link.AlternateDescriptions))
                    annot.Set("Contents", new PdfString(System.Text.Encoding.UTF8.GetBytes(link.AlternateDescriptions!)));

                var annotNum = _doc.AllocateObjectNumber();
                _doc.AddNewObject(annotNum, annot);
                var annotRef = new PdfIndirectRef(annotNum, 0);

                if (_doc.Reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots)
                {
                    annots = new PdfArray();
                    page.Dict.Set("Annots", annots);
                }
                annots.Add(annotRef);

                var key = nextKey++;
                annot.Set("StructParent", new PdfInteger(key));
                if (refs.TryGetValue(link._dict, out var linkRef))
                {
                    nums.Add(new PdfInteger(key));
                    nums.Add(linkRef);
                }

                var objr = new PdfDictionary();
                objr.Set("Type", new PdfName("OBJR"));
                objr.Set("Obj", annotRef);
                if (link._dict.Get("K") is PdfArray linkK) linkK.Add(objr);
            }
            return nextKey;
        }
    }
}
