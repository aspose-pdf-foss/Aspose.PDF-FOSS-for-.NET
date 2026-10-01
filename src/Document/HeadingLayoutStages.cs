using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary>The stages of the heading layout: the destination link and the outline copy.</summary>
    private void CopyHeadingToOutlines(HeadingLayoutState hl)
    {
        var headingPageIdx = 0;
        for (int pi = 1; pi <= PageCount; pi++)
        {
            if (Pages.At(pi) == hl.headingPage) { headingPageIdx = pi; break; }
        }
        if (headingPageIdx > 0)
        {
            var item = new OutlineItemCollection(Outlines)
            {
                Title = hl.heading.Segments[1].Text ?? string.Empty,
            };
            item.Action = new Aspose.Pdf.Annotations.GoToAction(
                new Aspose.Pdf.Annotations.XYZExplicitDestination(
                    headingPageIdx, hl.marginLeft, hl.headingY, 0));
            Outlines.Add(item);
        }
    }

    /// <summary>The stages of the heading layout: the destination link and the outline copy.</summary>
    private void LinkHeadingDestination(HeadingLayoutState hl, double height)
    {
        var linkRect = new Rectangle(hl.marginLeft, hl.headingY - height, hl.headingPage.Width - hl.marginRight, hl.headingY);
        var destPageIdx = 0;
        for (int pi = 1; pi <= PageCount; pi++)
        {
            if (Pages.At(pi) == hl.destPage) { destPageIdx = pi; break; }
        }
        if (destPageIdx > 0)
        {
            // Link via a GoTo action with an explicit XYZ destination at
            // the target page's upper-left corner, so Annotation.Action
            // resolves to a GoToAction whose Destination exposes the page
            // and coordinates (a /Dest [page /Fit] form leaves Action null).
            // Destination coordinates are in unrotated page space; map the
            // visual top-left (0, rotated-height) back through the page's
            // rotation so it lands correctly on rotated pages too.
            var destRect = hl.destPage!.GetPageRect(true);
            var (destLeft, destTop) = hl.destPage.RotationMatrix
                .InverseTransformPoint(0, destRect.Height);
            hl.headingPage.Annotations.AddLinkAnnotation(linkRect,
                new Aspose.Pdf.Annotations.GoToAction(
                    new Aspose.Pdf.Annotations.XYZExplicitDestination(
                        destPageIdx, destLeft, destTop, 0)));
        }
    }
}
