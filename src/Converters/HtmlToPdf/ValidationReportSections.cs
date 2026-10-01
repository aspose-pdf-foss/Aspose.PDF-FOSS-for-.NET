using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The files panel: the path table's header row and one row per source file, framed; the flow then rounds up to the next sheet.</summary>
    private static void DrawFilesPanel(ValidationReportState vr)
    {
        vr.fileTop = vr.y;
        vr.fileHeadTop = vr.fileTop + VrBorderPt + VrPanelPadPt;
        vr.fileX = vr.boxL + VrBorderPt + VrPanelPadPt + VrFileCellInsetPt;
        vr.fileSplit = vr.fileX + VrFileSplitPt;
        Fill(vr, vr.fileHeadTop, vr.fileHeadTop + VrFileHeadPt, vr.boxL + VrBorderPt + VrPanelPadPt,
            vr.boxR - vr.boxL - VrBorderPt - VrPanelPadPt, VrBrand, VrLayerBand);
        Run(vr, vr.fileHeadTop + VrFileCellPadPt, vr.fileX, VrFileTablePt, vr.faceReg, "SegoeUI",
            "File", VrBannerInk);
        Run(vr, vr.fileHeadTop + VrFileCellPadPt, vr.fileSplit, VrFileTablePt, vr.faceReg, "SegoeUI",
            "Path", VrBannerInk);
        vr.fy = vr.fileHeadTop + VrFileHeadPt + VrFileCellPadPt;
        foreach (var (label, path) in VrFileRows(vr.src))
        {
            var rowTop = vr.fy;
            Run(vr, vr.fy, vr.fileX, VrFileTablePt, vr.faceReg, "SegoeUI", label, VrInk);
            var pathY = vr.fy;
            foreach (var ln in Wrap(vr, vr.faceReg, "SegoeUI", path, VrFileTablePt,
                         vr.boxR - VrPanelPadPt - vr.fileSplit))
            {
                Run(vr, pathY, vr.fileSplit, VrFileTablePt, vr.faceReg, "SegoeUI", ln, VrLinkInk);
                pathY += VrLineH(VrFileTablePt);
            }
            vr.fy = Math.Max(pathY, rowTop + VrLineH(VrFileTablePt)) + VrFileCellPadPt;
            VRule(vr, rowTop - VrFileCellPadPt, vr.fy - VrFileCellPadPt,
                vr.fileSplit - VrFileCellInsetPt, VrRuleBorder);
        }
        Box(vr, vr.fileTop, vr.fy + VrPanelPadPt, vr.boxL, vr.boxR, VrWhite, VrFrameBorder, VrLayerContainer);

        // == the Details half, which the print sheet opens on its own page ====
        vr.y = Math.Ceiling((vr.fy + VrPanelPadPt + 1e-6) / vr.contentH) * vr.contentH;
    }

    /// <summary>The administrative envelope: label/value rows in a banded box below its section bar.</summary>
    private static void DrawEnvelopePanel(ValidationReportState vr)
    {
        vr.y = Bar(vr, "header_admin_item", vr.y);
        vr.envL = vr.marginLeft + VrEnvMarginXPt + VrEnvTableMarginPt;
        vr.envW = (vr.pageWidth - vr.marginLeft - vr.marginRight - 2 * VrEnvMarginXPt
            - 2 * VrEnvTableMarginPt) * VrEnvWidthFrac;
        vr.envTop = vr.y;
        vr.envRows = VrEnvelopeRows(vr.src);
        vr.envSplit = vr.envL + VrEnvPadPt + vr.envW * VrEnvLabelFrac;
        for (var i = 0; i < vr.envRows.Count; i++)
        {
            var top = vr.envTop + VrEnvPadPt + i * VrEnvRowPt + VrEnvCellPadPt;
            Run(vr, top, vr.envL + VrEnvPadPt + VrEnvCellPadPt, VrEnvTextPt, vr.faceReg, "SegoeUI",
                vr.envRows[i].Label, VrDarkInk);
            Run(vr, top, vr.envSplit + VrEnvCellPadPt, VrEnvTextPt, vr.faceReg, "SegoeUI",
                vr.envRows[i].Value, VrDarkInk);
            if (i == vr.envRows.Count - 1) continue;
            HRule(vr, top + VrEnvRowPt - VrEnvCellPadPt - VrBorderPt / 2, vr.envL + VrEnvPadPt,
                vr.envL + vr.envW - VrEnvPadPt, VrRuleBorder);
        }
        vr.envBottom = vr.envTop + 2 * VrEnvPadPt + vr.envRows.Count * VrEnvRowPt;
        Box(vr, vr.envTop, vr.envBottom, vr.envL, vr.envL + vr.envW, VrWhite, VrBand, VrLayerContainer);
        vr.y = vr.envBottom + VrEnvTableBottomPt;
    }

    /// <summary>The generals panel: label/value rows separated by rules, framed below its section bar.</summary>
    private static void DrawGeneralsPanel(ValidationReportState vr)
    {
        vr.y = Bar(vr, "header_generals_item", vr.y);
        vr.genTop = vr.y;
        vr.gen = VrPairs(vr.src, "generals_label", "generals_value");
        vr.genContentTop = vr.genTop + VrBorderPt + VrPanelPadPt;
        vr.genLabelX = vr.boxL + VrBorderPt + VrPanelPadPt;
        vr.genValueX = vr.genLabelX + VrGeneralsLabelEm * VrGeneralsPt;
        vr.genRowH = 2 * VrGeneralsPadPt + VrLineH(VrGeneralsPt);
        for (var i = 0; i < vr.gen.Count; i++)
        {
            var top = vr.genContentTop + i * (vr.genRowH + VrBorderPt);
            Run(vr, top + VrGeneralsPadPt, vr.genLabelX, VrGeneralsPt, vr.faceReg, "SegoeUI",
                vr.gen[i].Label, VrInk);
            Run(vr, top + VrGeneralsPadPt, vr.genValueX, VrGeneralsPt, vr.faceReg, "SegoeUI",
                vr.gen[i].Value, VrInk);
            // .last_row drops its rule
            if (i == vr.gen.Count - 1) continue;
            HRule(vr, top + vr.genRowH + VrBorderPt / 2, vr.genLabelX, vr.genValueX, VrRuleBorder);
            HRule(vr, top + vr.genRowH + VrBorderPt / 2, vr.genValueX, vr.boxR - VrGroupGapPt - VrBorderPt,
                VrRuleBorder);
        }
        vr.genBottom = vr.genContentTop + vr.gen.Count * (vr.genRowH + VrBorderPt) - VrBorderPt
            + VrPanelPadPt + VrBorderPt;
        Box(vr, vr.genTop, vr.genBottom, vr.boxL, vr.boxR, VrWhite, VrFrameBorder, VrLayerContainer);
        vr.y = vr.genBottom + VrBarGapPt;
    }

    /// <summary>The info panel: the report's columns wrapped side by side on a white card inside the top panel.</summary>
    private static void DrawInfoPanel(ValidationReportState vr)
    {
        vr.panelL = vr.marginLeft + VrPanelMarginPt;
        vr.panelR = vr.pageWidth - vr.marginRight - VrGroupGapPt;
        vr.panelContentW = vr.panelR - vr.panelL - VrPanelPadPt;
        vr.infoL = vr.panelL + VrPanelPadPt + VrInfoMarginPt;
        vr.infoW = vr.panelContentW * VrInfoWidthFrac + 2 * VrInfoPadXPt;
        vr.infoTop = vr.y + VrPanelPadPt + VrInfoMarginPt;
        vr.colW = (vr.infoW - 2 * VrInfoPadXPt) * VrColWidthFrac;
        vr.colTop = vr.infoTop + VrInfoPadYPt;
        vr.colLines = 1;
        vr.cols = VrColumns(vr.src);
        for (var i = 0; i < vr.cols.Count; i++)
        {
            var cy = vr.colTop;
            var cx = vr.infoL + VrInfoPadXPt + i * vr.colW + VrColPadPt;
            foreach (var ln in Wrap(vr, vr.faceReg, "SegoeUI", vr.cols[i].Label, VrColTextPt,
                         vr.colW - 2 * VrColPadPt))
            { Run(vr, cy, cx, VrColTextPt, vr.faceReg, "SegoeUI", ln, VrInk); cy += VrLineH(VrColTextPt); }
            foreach (var ln in Wrap(vr, vr.faceSemi, "SegoeUISemibold", vr.cols[i].Value, VrColTextPt,
                         vr.colW - 2 * VrColPadPt))
            {
                Run(vr, cy, cx, VrColTextPt, vr.faceSemi, "SegoeUISemibold", ln, VrDarkInk);
                cy += VrLineH(VrColTextPt);
            }
            vr.colLines = Math.Max(vr.colLines, (int)Math.Round((cy - vr.colTop) / VrLineH(VrColTextPt)));
        }
        vr.infoBottom = vr.colTop + vr.colLines * VrLineH(VrColTextPt) + VrInfoPadYPt;
        Fill(vr, vr.infoTop, vr.infoBottom, vr.infoL, vr.infoW, VrWhite, VrLayerContainer);
        vr.y = vr.infoBottom + VrInfoMarginPt + VrPanelPadPt + VrPanelMarginPt;
    }

    /// <summary>The brand banner: the report header lines on the brand fill across the page.</summary>
    private static void DrawBanner(ValidationReportState vr)
    {
        vr.bannerTop = vr.y;
        vr.y += VrBannerPadPt;
        vr.items = VrTexts(vr.src, "header_report_item");
        for (var i = 0; i < vr.items.Count; i++)
        {
            var size = i == 0 ? VrBannerTitlePt : VrColTextPt;
            Run(vr, vr.y, vr.marginLeft + VrBannerPadPt, size, vr.faceReg, "SegoeUI", vr.items[i], VrBannerInk);
            vr.y += VrLineH(size);
        }
        vr.y += VrBannerPadPt;
        Fill(vr, vr.bannerTop, vr.y, vr.marginLeft, vr.pageWidth - vr.marginLeft - vr.marginRight, VrBrand,
            VrLayerBand);
        vr.y += VrBannerGapPt;
    }

    /// <summary>One rule group of the details list: its bubble and name, then each rule's row with status, description and location, framed; the flow rounds up to the next sheet after it.</summary>
    private static bool DrawRuleGroup(ValidationReportState vr, int gi)
    {
        var g = vr.groups[gi];
        var innerR = vr.outerContentR - VrGroupGapPt;
        var innerTop = vr.y + VrGroupGapPt;
        var innerContentL = vr.outerContentL + VrBorderPt + VrFramePadPt;
        var innerContentR = innerR - VrBorderPt - VrFramePadPt;
        var iy = innerTop + VrBorderPt + VrFramePadPt;
        Fill(vr, iy + VrBubbleDropPt, iy + VrBubbleDropPt + VrBubblePt, innerContentL,
            VrBubblePt, g.Bubble, VrLayerBubble);
        Run(vr, iy, innerContentL + VrBubblePt + VrBubbleGapPt, VrGroupNamePt, vr.faceReg,
            "SegoeUI", g.Name, VrInk);
        iy += VrLineH(VrGroupNamePt) + VrNamePadPt;

        foreach (var r in g.Rules)
        {
            var frameTop = iy + VrRuleGapPt;
            var frameL = innerContentL;
            var frameR = innerContentR - VrGroupGapPt;
            var cl = frameL + VrBorderPt + VrFramePadPt;
            var cr = frameR - VrBorderPt - VrFramePadPt;
            var ry = frameTop + VrBorderPt + VrFramePadPt;

            // .rule_header's -14px margin pulls its band back out over the
            // frame's padding, so the band spans the frame edge to edge
            var bandTop = ry - VrFramePadPt;
            var bandBottom = bandTop + 2 * VrHeaderPadPt + VrLineH(VrTitlePt);
            Fill(vr, bandTop, bandBottom, frameL + VrBorderPt,
                frameR - frameL - 2 * VrBorderPt, VrBand, VrLayerBand);
            Fill(vr, ry + VrBubbleDropPt, ry + VrBubbleDropPt + VrBubblePt, cl, VrBubblePt,
                r.Bubble, VrLayerBubble);
            Run(vr, bandTop + VrHeaderPadPt, cl + VrBubblePt + VrBubbleGapPt, VrTitlePt,
                vr.faceReg, "SegoeUI", r.Title, VrInk);
            ry = bandBottom + VrHeaderGapPt + VrHelpPadPt;

            foreach (var ln in Wrap(vr, vr.faceIt, "SegoeUIItalic", r.Comment, VrCommentPt, cr - cl))
            {
                Run(vr, ry, cl, VrCommentPt, vr.faceIt, "SegoeUIItalic", ln, VrInk);
                ry += VrLineH(VrCommentPt);
            }
            ry += VrHelpPadPt;

            if (r.Path.Length > 0)
            {
                foreach (var ln in Wrap(vr, vr.faceReg, "SegoeUI", r.Path, VrFindingPt, cr - cl))
                {
                    Run(vr, ry, cl, VrFindingPt, vr.faceReg, "SegoeUI", ln, VrLinkInk);
                    ry += VrLineH(VrFindingPt);
                }
                ry += VrBr2Pt;
            }
            foreach (var f in r.Findings)
            {
                var slTop = ry;
                var tl = cl + VrBorderPt + VrLinePadXPt;
                var tr = cr - VrBorderPt - VrLinePadXPt;
                var ty = slTop + VrBorderPt + VrLinePadYPt;
                foreach (var ln in Wrap(vr, vr.faceReg, "SegoeUI", f.Text, VrFindingPt, tr - tl))
                {
                    Run(vr, ty, tl, VrFindingPt, vr.faceReg, "SegoeUI", ln, VrInk);
                    ty += VrLineH(VrFindingPt);
                }
                ty += VrBr2Pt + VrXmlTopPt;
                foreach (var ln in Wrap(vr, vr.faceReg, "SegoeUI", f.Xml, VrFindingPt, tr - tl))
                {
                    // .error-xml sets its own 22px line box
                    Run(vr, ty + (VrXmlLinePt - VrLineH(VrFindingPt)) / 2, tl, VrFindingPt,
                        vr.faceReg, "SegoeUI", ln, VrErrorInk);
                    ty += VrXmlLinePt;
                }
                ty += VrXmlBottomPt + VrLinePadYPt + VrBorderPt;
                Box(vr, slTop, ty, cl, cr, null, VrRuleBorder);
                ry = ty;
            }

            var frameBottom = ry + VrFramePadPt + VrBorderPt;
            Box(vr, frameTop, frameBottom, frameL, frameR, VrWhite, VrRuleBorder);
            iy = frameBottom;
        }

        var innerBottom = iy + VrFramePadPt + VrBorderPt;
        Box(vr, innerTop, innerBottom, vr.outerContentL, innerR, null, VrFrameBorder);
        vr.listBottom = innerBottom + VrFramePadPt + VrBorderPt;
        // .details_group_frame { page-break-after: always }
        vr.y = Math.Ceiling((innerBottom + 1e-6) / vr.contentH) * vr.contentH;
        return true;
    }
}
