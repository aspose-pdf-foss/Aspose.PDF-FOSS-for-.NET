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

public sealed partial class Document
{
    /// <summary>HTML block stages: the special blocks, the text preparation and the text emit.</summary>
    private void EmitBlockText(HtmlBlockRenderState hb, HtmlBlockState rb, Aspose.Pdf.Converters.HtmlToPdfConverter.Block b)
    {
        if (rb.bandColor is not null)
        {
            hb.flow.EnsureRoomFor(b.BgPadTopPt + rb.bf.TextState.FontSize
                + rb.bf.TextState.LineSpacing + b.BgPadBottomPt);
            rb.bandStartSlot = hb.flow.CurrentSlot;
            rb.bandTop = hb.flow.CurrentY;
            if (b.BgPadTopPt > 0) hb.flow.AdvanceY(b.BgPadTopPt);
        }
        hb.flow.LeftIndent = b.LeftIndent + hb.htmlFrameIndent
            + (rb.bandColor is not null ? b.BgPadLeftPt : 0);
        rb.emphRuns = HtmlEmphasisRuns(b);
        if (rb.emphRuns is not null)
        {
            rb.bf.TextState.IsBold = false;
            rb.wrote = hb.flow.WriteEmphasisRuns(rb.bf, rb.emphRuns);
        }
        else rb.wrote = hb.flow.WriteTextFragment(rb.bf);
        hb.flow.LeftIndent = 0;
        if (!rb.wrote)
        {
            rb.bf.Position = new Text.Position(hb.marginLeft + b.LeftIndent,
                hb.page.Height - hb.marginTop - rb.bf.TextState.FontSize);
            hb.tb.AppendTextInline(rb.bf);
        }
        if (rb.bandColor is not null)
        {
            if (b.BgPadBottomPt > 0) hb.flow.AdvanceY(b.BgPadBottomPt);
            hb.flow.QueueBandFill(rb.bandStartSlot, rb.bandTop, hb.flow.CurrentY, rb.bandColor);
        }
    }

    /// <summary></summary>
    private void PrepareBlockText(HtmlBlockRenderState hb, HtmlBlockState rb, Aspose.Pdf.Converters.HtmlToPdfConverter.Block b)
    {
        rb.htmlBlockLead = rb.htmlCallerLs > 0
            ? rb.htmlCallerLs
            : hb.legacyDialect ? rb.legacyLead : rb.fontSize * 0.2;
        rb.bf.TextState.LineSpacing = (float)rb.htmlBlockLead;
        rb.bf.TextState.IsBold = b.FontRes == "F2";
        rb.bf.TextState.IsItalic = b.FontRes == "F3";
        // Emphasis title: draw with the embedded bold-italic face on the
        // CSS "normal" line height (pixel-quantized win-metric
        // leading), overriding the Standard-14 bold/italic flags.
        if (rb.styledFace is not null)
        {
            rb.bf.TextState.Font = rb.styledFace;
            rb.bf.TextState.IsBold = false;
            rb.bf.TextState.IsItalic = false;
            var pitch = HtmlNormalLineHeightPt(rb.styledFace.SourceFontData?.TtfData, rb.fontSize);
            rb.bf.TextState.LineSpacing = (float)(pitch > 0 ? pitch - rb.fontSize : rb.fontSize * 0.2);
        }
        rb.callerBodyFace = hb.html.TextState?.Font ?? DefaultTextStateFace(hb.page) ?? hb.uaDefaultFace;
        if (rb.callerBodyFace is not null && rb.styledFace is null && !hb.legacyDialect)
            rb.bf.TextState.Font = rb.callerBodyFace;
        rb.cssLineBox = false;
        // The body's own absolute line-height is the fragment's CSS line box when the
        // caller's TextState declares no leading: every line pitches on it and the
        // first baseline seats half its surplus leading plus the ascent below the top.
        if (rb.htmlCallerLs <= 0 && rb.styledFace is null && hb.bodyCss.LineHeightPt > rb.fontSize)
        {
            rb.bf.TextState.LineSpacing = (float)(hb.bodyCss.LineHeightPt - rb.fontSize);
            rb.cssLineBox = true;
        }
        if (hb.bodyCssFace is not null && rb.styledFace is null && !hb.legacyDialect)
        {
            rb.bf.TextState.Font = hb.bodyCssFace;
            var bodyPitch = HtmlNormalLineHeightPt(
                hb.bodyCssFace.SourceFontData?.TtfData, rb.fontSize);
            if (bodyPitch > 0)
            {
                rb.bf.TextState.LineSpacing = (float)(bodyPitch - rb.fontSize);
                // That pitch IS the CSS `line-height: normal` box, so the first
                // baseline seats inside the box — half its leading below the box
                // top, plus the face's ascent — not on the legacy first-line drop.
                rb.cssLineBox = true;
            }
        }
        // Dialect: draw with the embedded face and the run's CSS colour.
        if (hb.legacyDialect)
        {
            var face = b.FontFamily is { Length: > 0 } fam1 ? SafeFindFont(fam1) : null;
            if (face?.SourceFontData?.TtfData is { Length: > 0 }) rb.bf.TextState.Font = face;
            else if (hb.legacyFace is not null) rb.bf.TextState.Font = hb.legacyFace;
            if (b.ForeColor is { } fc) rb.bf.TextState.ForegroundColor = fc;
        }
        // A CSS `color` on the block draws its text — on a painted band the
        // declared ink is the only thing that makes the band's text legible.
        if (!hb.legacyDialect && b.ForeColor is { } blockFore)
            rb.bf.TextState.ForegroundColor = blockFore;
        if (hb.htmlColor is not null) rb.bf.TextState.ForegroundColor = hb.htmlColor;
        // Split the block into segments so inline <a href> ranges carry a
        // WebHyperlink — the layout engine turns hyperlinked segments into
        // Link annotations over their rendered run.
        if (b.Anchors is { Count: > 0 })
            ApplyHtmlAnchorSegments(rb.bf, b.Text, b.Anchors);
        // A Hyperlink set on the HtmlFragment ITSELF covers the fragment:
        // ONE Link annotation goes over the rendered
        // block (the first when the HTML splits into several).
        if (hb.html.Hyperlink is not null && !hb.htmlFragmentLinkEmitted)
        {
            rb.bf.Hyperlink = hb.html.Hyperlink!;
            hb.htmlFragmentLinkEmitted = true;
        }
    }

    /// <summary></summary>
    private bool TryRenderSpecialBlock(HtmlBlockRenderState hb, HtmlBlockState rb, Aspose.Pdf.Converters.HtmlToPdfConverter.Block b)
    {
        if (b.IsImage
            && !(b.ImageSrc?.StartsWith("inline-svg:", StringComparison.Ordinal) ?? false)
            && (b.ImageSrc is null || LoadHtmlImageBytes(b.ImageSrc) is null))
        {
            // A broken/missing <img> still occupies the CSS default
            // replaced-element box (300x150 px, width capped by the
            // stylesheet), so following content flows below it — reserve
            // that box inline at the image's document position.
            var imgH = (b.ImageHeight > 0 ? b.ImageHeight : 150.0) * 0.75;
            if (hb.flow.CurrentY - imgH < hb.flow.BottomMargin) hb.flow.ForceNewPage();
            hb.flow.AdvanceY(imgH);
            return true;
        }
        if (b.IsCheckbox)
        {
            // <input type="checkbox"> inside an in-page HtmlFragment:
            // reserve a small AcroForm CheckboxField at the flow cursor,
            // queued with the current overflow slot so it binds to the page
            // it actually flows onto (registered on Form by FinaliseFormFields).
            hb.flow.QueueCheckbox(10.0, b.LeftIndent, b.Checked);
            return true;
        }
        if (b.IsInputField)
        {
            // <input>/<textarea> inside an in-page HtmlFragment: place an
            // interactive AcroForm TextBoxField at the flow cursor, named
            // from the HTML name/id so callers can find it by FullName.
            var ifPage = hb.flow.CurrentPage;
            var ifLlx = hb.marginLeft + b.LeftIndent;
            var ifContentW = ifPage.Width - hb.marginLeft - hb.marginRight - b.LeftIndent;
            var ifW = b.InputWidth > 0 ? System.Math.Min(b.InputWidth, ifContentW) : ifContentW;
            var ifH = b.InputHeight > 0 ? b.InputHeight : rb.fontSize * 1.3;
            var ifTop = hb.flow.CurrentY;
            var ifField = new Aspose.Pdf.Forms.TextBoxField(ifPage,
                new Aspose.Pdf.Rectangle(ifLlx, ifTop - ifH, ifLlx + ifW, ifTop))
            {
                Multiline = b.InputMultiline,
                ReadOnly = b.InputReadOnly,
            };
            if (!string.IsNullOrEmpty(b.InputName)) ifField.PartialName = b.InputName;
            if (!string.IsNullOrEmpty(b.InputValue)) ifField.Value = b.InputValue;
            Form.Add(ifField, ifPage.Number);
            hb.flow.AdvanceY(ifH + b.MarginBottom);
            return true;
        }
        if (b.IsHorizontalRule)
        {
            // Draw the <hr> as a thin filled bar across the
            // content width in its CSS border colour.
            var hrPage = hb.flow.CurrentPage;
            var lineW = hrPage.Width - hb.marginLeft - hb.marginRight;
            var th = b.RuleWidth > 0 ? b.RuleWidth : 1.0;
            var hrY = hb.flow.CurrentY;
            var csb = new Content.ContentStreamBuilder();
            csb.SaveState();
            csb.SetFillColor(b.RuleColor ?? Color.FromArgb(128, 128, 128));
            csb.Rectangle(hb.marginLeft, hrY - th, lineW, th);
            csb.Fill();
            csb.RestoreState();
            hrPage.AddContentStream(csb.Build());
            hb.flow.AdvanceY(th + 2);
            return true;
        }
        if (string.IsNullOrEmpty(b.Text))
        {
            // Dialect blank line (<p><br></p>) occupies a full 1.25×em grid
            // row; a caller-set line spacing steps blank rows on the same
            // pitch as text rows.
            var blankLs = (double)(hb.html.TextState?.LineSpacing ?? 0f);
            hb.flow.AdvanceLineBox(b.ExplicitHeight > 0 ? b.ExplicitHeight
                : blankLs > 0 ? rb.fontSize + blankLs
                : hb.legacyDialect ? rb.fontSize + rb.legacyLead : rb.fontSize);
            return true;
        }
        return false;
    }
}
