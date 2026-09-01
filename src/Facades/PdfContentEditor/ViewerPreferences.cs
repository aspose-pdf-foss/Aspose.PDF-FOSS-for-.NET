using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
    /// <summary>
    /// Get the current viewer preference flags as an integer bitmask.
    /// Use bitwise AND with ViewerPreference constants to check individual flags.
    /// </summary>
    public int GetViewerPreference()
    {
        var doc = EnsureBound();
        int result = 0;

        // PageMode (from catalog /PageMode)
        var pageMode = doc.Reader.Catalog.GetName("PageMode");
        result |= pageMode switch
        {
            "UseOutlines" => ViewerPreference.PageModeUseOutlines,
            "UseThumbs" => ViewerPreference.PageModeUseThumbs,
            "FullScreen" => ViewerPreference.PageModeFullScreen,
            "UseOC" => ViewerPreference.PageModeUseOC,
            "UseAttachments" => ViewerPreference.PageModeUseAttachment,
            _ => ViewerPreference.PageModeUseNone,
        };

        // PageLayout (from catalog /PageLayout)
        var pageLayout = doc.Reader.Catalog.GetName("PageLayout");
        result |= pageLayout switch
        {
            "SinglePage" => ViewerPreference.PageLayoutSinglePage,
            "OneColumn" => ViewerPreference.PageLayoutOneColumn,
            "TwoColumnLeft" => ViewerPreference.PageLayoutTwoColumnLeft,
            "TwoColumnRight" => ViewerPreference.PageLayoutTwoColumnRight,
            "TwoPageLeft" => ViewerPreference.PageLayoutTwoPageLeft,
            "TwoPageRight" => ViewerPreference.PageLayoutTwoPageRight,
            _ => ViewerPreference.PageLayoutSinglePage,
        };

        // ViewerPreferences dictionary flags
        var vpDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("ViewerPreferences"));
        if (vpDict is not null)
        {
            if (vpDict.Get("HideMenubar") is PdfBoolean hm && hm.Value)
                result |= ViewerPreference.HideMenubar;
            if (vpDict.Get("HideToolbar") is PdfBoolean ht && ht.Value)
                result |= ViewerPreference.HideToolbar;
            if (vpDict.Get("HideWindowUI") is PdfBoolean hw && hw.Value)
                result |= ViewerPreference.HideWindowUI;
            if (vpDict.Get("FitWindow") is PdfBoolean fw && fw.Value)
                result |= ViewerPreference.FitWindow;
            if (vpDict.Get("CenterWindow") is PdfBoolean cw && cw.Value)
                result |= ViewerPreference.CenterWindow;
            if (vpDict.Get("DisplayDocTitle") is PdfBoolean dt && dt.Value)
                result |= ViewerPreference.DisplayDocTitle;

            result |= vpDict.GetName("Duplex") switch
            {
                "Simplex" => ViewerPreference.Simplex,
                "DuplexFlipLongEdge" => ViewerPreference.DuplexFlipLongEdge,
                "DuplexFlipShortEdge" => ViewerPreference.DuplexFlipShortEdge,
                _ => 0,
            };
            if (vpDict.Get("PickTrayByPDFSize") is PdfBoolean pt && pt.Value)
                result |= ViewerPreference.PickTrayByPDFSize;
        }

        return result;
    }

    /// <summary>
    /// Change the viewer preference. Sets the specified flags (replaces all previous settings).
    /// Parameter is named <c>viewerAttribution</c> per the published signature.
    /// </summary>
    public void ChangeViewerPreference(int viewerAttribution)
    {
        var preference = viewerAttribution;
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;

        // PageMode
        string? pageModeVal = null;
        if ((preference & ViewerPreference.PageModeUseOutlines) != 0) pageModeVal = "UseOutlines";
        else if ((preference & ViewerPreference.PageModeUseThumbs) != 0) pageModeVal = "UseThumbs";
        else if ((preference & ViewerPreference.PageModeFullScreen) != 0) pageModeVal = "FullScreen";
        else if ((preference & ViewerPreference.PageModeUseOC) != 0) pageModeVal = "UseOC";
        else if ((preference & ViewerPreference.PageModeUseAttachment) != 0) pageModeVal = "UseAttachments";
        else if ((preference & ViewerPreference.PageModeUseNone) != 0) pageModeVal = "UseNone";

        if (pageModeVal is not null)
            catalog.Set("PageMode", new PdfName(pageModeVal));
        else
            catalog.Remove("PageMode");

        // PageLayout
        string? pageLayoutVal = null;
        if ((preference & ViewerPreference.PageLayoutOneColumn) != 0) pageLayoutVal = "OneColumn";
        else if ((preference & ViewerPreference.PageLayoutTwoColumnLeft) != 0) pageLayoutVal = "TwoColumnLeft";
        else if ((preference & ViewerPreference.PageLayoutTwoColumnRight) != 0) pageLayoutVal = "TwoColumnRight";
        else if ((preference & ViewerPreference.PageLayoutTwoPageLeft) != 0) pageLayoutVal = "TwoPageLeft";
        else if ((preference & ViewerPreference.PageLayoutTwoPageRight) != 0) pageLayoutVal = "TwoPageRight";
        else if ((preference & ViewerPreference.PageLayoutSinglePage) != 0) pageLayoutVal = "SinglePage";

        if (pageLayoutVal is not null)
            catalog.Set("PageLayout", new PdfName(pageLayoutVal));
        else
            catalog.Remove("PageLayout");

        // ViewerPreferences dictionary flags
        var vpDict = doc.Reader.ResolveDict(catalog.Get("ViewerPreferences")) ?? new PdfDictionary();
        catalog.Set("ViewerPreferences", vpDict);

        SetOrRemoveBool(vpDict, "HideMenubar", (preference & ViewerPreference.HideMenubar) != 0);
        SetOrRemoveBool(vpDict, "HideToolbar", (preference & ViewerPreference.HideToolbar) != 0);
        SetOrRemoveBool(vpDict, "HideWindowUI", (preference & ViewerPreference.HideWindowUI) != 0);
        SetOrRemoveBool(vpDict, "FitWindow", (preference & ViewerPreference.FitWindow) != 0);
        SetOrRemoveBool(vpDict, "CenterWindow", (preference & ViewerPreference.CenterWindow) != 0);
        SetOrRemoveBool(vpDict, "DisplayDocTitle", (preference & ViewerPreference.DisplayDocTitle) != 0);

        // Duplex (/Duplex name entry, alongside Document.Duplex)
        string? duplexVal = null;
        if ((preference & ViewerPreference.DuplexFlipShortEdge) != 0) duplexVal = "DuplexFlipShortEdge";
        else if ((preference & ViewerPreference.DuplexFlipLongEdge) != 0) duplexVal = "DuplexFlipLongEdge";
        else if ((preference & ViewerPreference.Simplex) != 0) duplexVal = "Simplex";

        if (duplexVal is not null)
            vpDict.Set("Duplex", new PdfName(duplexVal));
        else
            vpDict.Remove("Duplex");

        SetOrRemoveBool(vpDict, "PickTrayByPDFSize", (preference & ViewerPreference.PickTrayByPDFSize) != 0);
    }

    private static void SetOrRemoveBool(PdfDictionary dict, string key, bool value)
    {
        if (value)
            dict.Set(key, PdfBoolean.True);
        else
            dict.Remove(key);
    }
}
