using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

internal static partial class LayerHelper
{
    /// <summary>The stages of the page-layer collection: the layers reachable through the page's resources.</summary>
    private static void CollectResourceLayers(PageLayersState lh)
    {
        var props = lh.reader.ResolveDict(lh.resources!.Get("Properties"));
        if (props is not null)
        {
            foreach (var key in props.Keys)
            {
                var propDict = lh.reader.ResolveDict(props.Get(key));
                if (propDict is null) continue;

                var type = propDict.GetName("Type");
                if (type != "OCG") continue;
                if (!lh.seen.Add(propDict)) continue;

                // Reuse the document-level group instance so state changes propagate
                OptionalContentGroup ocg;
                if (lh.ocgLookup.TryGetValue(propDict, out var existing))
                {
                    ocg = existing;
                    ocg.Id = key;
                    ocg._page = lh.page;
                }
                else
                {
                    ocg = new OptionalContentGroup(propDict) { Id = key, _page = lh.page };
                    ocg.SetRegisteredReader(lh.reader);
                    ApplyDocLevelState(ocg, propDict, lh.reader);
                }
                lh.result.Add(ocg);
            }
        }

        // 2. Check XObject resources for /OC references (XForm-level layers)
        var xobjects = lh.reader.ResolveDict(lh.resources.Get("XObject"));
        if (xobjects is not null)
        {
            foreach (var key in xobjects.Keys)
            {
                var xobj = lh.reader.ResolveStream(xobjects.Get(key));
                if (xobj is null) continue;

                var ocRef = xobj.Dict.Get("OC");
                if (ocRef is null) continue;

                var ocDict = lh.reader.ResolveDict(ocRef);
                if (ocDict is null) continue;

                // /OC can point directly to an OCG dict or to an OCMD
                var ocType = ocDict.GetName("Type");
                PdfDictionary? actualOcgDict = null;
                string? propId = null;

                if (ocType == "OCG")
                {
                    actualOcgDict = ocDict;
                }
                else if (ocType == "OCMD")
                {
                    // OCMD — resolve the first OCG in its /OCGs
                    var ocmdOcgs = lh.reader.Resolve(ocDict.Get("OCGs"));
                    if (ocmdOcgs is PdfArray arr && arr.Count > 0)
                        actualOcgDict = lh.reader.ResolveDict(arr[0]);
                    else if (ocmdOcgs is PdfDictionary d)
                        actualOcgDict = d;
                }
                else
                {
                    // No /Type — assume it's an OCG
                    actualOcgDict = ocDict;
                }

                if (actualOcgDict is null) continue;

                // Don't dedup XObject-level layers — each XObject with /OC
                // is a separate layer entry (matches the public behavior).
                propId = key;
                // XObject layers: always create new instances since multiple
                // XObjects may share the same OCG but need separate Id/page refs.
                // Copy visibility state from the document-level group if available.
                var ocg = new OptionalContentGroup(actualOcgDict) { Id = propId, _page = lh.page };
                ocg.SetRegisteredReader(lh.reader);
                if (lh.ocgLookup.TryGetValue(actualOcgDict, out var docGroup))
                {
                    ocg.IsVisible = docGroup.IsVisible;
                    ocg.IsLocked = docGroup.IsLocked;
                    ocg.SetOwner(lh.ocProps!);
                    ocg._docTwin = docGroup;
                }
                else
                {
                    ApplyDocLevelState(ocg, actualOcgDict, lh.reader);
                }
                lh.result.Add(ocg);
            }

        }
    }
}
