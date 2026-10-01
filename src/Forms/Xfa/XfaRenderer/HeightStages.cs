using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
// The stages of the XFA element height: the field and draw arms.

    /// <summary>The field arm of <see cref="Height"/>: the height of a field element from its
    /// caption, ui and margins, clamped by the declared h / minH.</summary>
    private static double FieldHeight(Ctx ctx, XmlElement e, double? h, double? minH)
    {
        if (h is not null) return h.Value;   // fixed h clamps; content clips
        var (mt, mb, ml, mr) = Margins(e);
        double sv = FontSize(e);
        var (res, plc) = Caption(e);
        var capEl = FirstChild(e, "caption");
        var (capFs, capBold) = CaptionFont(e);
        // Laid-out caption lines: at least one whenever a caption element exists
        // (even with empty text); wraps count. Captions wrap within their reserve
        // (same 10% slack as the paint pass — the width model overestimates the
        // narrow Designer faces).
        int nC = 0;
        if (capEl is not null)
        {
            var capText = InnerText(capEl, "value") ?? "";
            double capAvail = (plc is "left" or "right") && res > 0 ? res * 1.10 : Math.Max(4, BoxW(e) - ml - mr);
            nC = Math.Max(1, string.IsNullOrWhiteSpace(capText) ? 1 : LineCount(capText.Trim(), capAvail, capFs, capBold));
        }
        // Buttons size from their caption alone.
        if (Ui(e) == "button")
        {
            double bhh = Math.Max(minH ?? 0, nC * capFs - 1) + 0.2 * capFs + 1;
            if (System.Environment.GetEnvironmentVariable("XFA_HEIGHTS") is not null)
                System.Console.Error.WriteLine($"FH-BTN\t{e.GetAttribute("name")}\tnC={nC}\tcapFs={capFs}\tH={bhh:F2}");
            return bhh;
        }
        // Laid-out value lines (hard breaks and soft wraps both count).
        var val = FieldValueText(ctx, e);
        double availW = Math.Max(4, BoxW(e) - ml - mr - (plc is "left" or "right" ? res : 0) - 3);
        int nV = string.IsNullOrWhiteSpace(val) ? 0 : LineCount(val!.Trim(), availW, sv, FontBold(e));
        bool capHasText = capEl is not null && !string.IsNullOrWhiteSpace(InnerText(capEl, "value"));
        double borderPad = TextEditBorderPad(e);
        // Top-caption reserve holds its strip and never grows for the caption; the
        // 0.2-caption-size pad rides on top of the minH floor once a value exists.
        double fh;
        if (plc == "top" && capEl is not null)
            fh = mt + mb + res + Math.Max(nV * sv, (minH ?? 0) - mt - mb - res)
                   + (nV >= 1 ? 0.2 * capFs : 0);
        else if (capEl is not null && res <= 0.01)
        {
            // Side caption with a zero reserve: floor-then-pad — the value region
            // floors at minH, and the pads (caption lead, caption lines when it
            // has text, edit-border thickness) ride on top once a value exists.
            fh = mt + mb + Math.Max(nV * sv, (minH ?? 0) - mt - mb)
                 + (nV >= 1 ? 0.2 * capFs + (capHasText ? nC * capFs : 0) + borderPad : 0);
        }
        else
        {
            // Side caption with a reserve — the law (27/27
            // synthetic fits + real-file widget rects): the caption block STACKS
            // on the value line even for a SIDE caption, and the value region
            // contributes exactly ONE line — an EMPTY value still holds a full
            // line, a wrapping multiline value adds nothing — but ONLY when the
            // field declares its own <value> element. A field with no value node
            // is a caption-only strip (a blank field with
            // <value><text maxChars="5"/> measures 22.00 = one line + caption;
            // blank GTC_Number with NO value element measures 12.33 = caption
            // alone). Fixed h wins above; minH clamps from below.
            double capBlock = capEl is not null
                ? (capHasText ? nC * capFs : 0) + 0.2 * capFs + borderPad
                : 0;
            var hasValueNode = FirstChild(e, "value") is not null;
            // A FILLED value keeps its laid-out line count (corpus greens pin
            // multi-line values at nV lines; the synthetic one-line probe result
            // does not hold for these real fields) - the proven part is
            // the EMPTY side: one reserved line iff the value node exists.
            var valueLines = nV > 0 ? nV : (hasValueNode ? 1 : 0);
            fh = Math.Max(minH ?? 0, mt + mb + valueLines * sv + capBlock);
        }
        if (System.Environment.GetEnvironmentVariable("XFA_HEIGHTS") is not null)
        {
            double oldH = Math.Max(minH ?? 0, sv * 1.15 + mt + mb + (plc is "top" or "bottom" ? res : 0));
            if (Math.Abs(oldH - fh) > 0.05)
                System.Console.Error.WriteLine($"FH\t{e.GetAttribute("name")}\tplc={plc}\tnC={nC}\tnV={nV}\tH={fh:F2}\toldH={oldH:F2}\tdelta={fh - oldH:+0.00;-0.00}");
        }
        return fh;
    }

    /// <summary>The draw arm of <see cref="Height"/>: the natural height of a draw element
    /// against its declared h / minH.</summary>
    private static double DrawHeight(Ctx ctx, XmlElement e, double outerW, double? h, double? minH)
    {
        if (h is not null) return h.Value;
        var (mt, mb, ml, mr) = Margins(e);
        var (res, plc) = Caption(e);
        // An auto-height draw is as tall as its laid-out TEXT (probed on the
        // 4506-T instruction columns: the tb flow is the pure sum of content
        // heights — wrapped lines step 1.0 × fontSize, spaceAbove unspent).
        // A non-text draw (rectangle/line/image chrome) keeps the one-line
        // natural height it always had.
        double boxW = BoxW(e); if (boxW <= 0) boxW = outerW;
        double availText = Math.Max(4, boxW - ml - mr);
        double fs = FontSize(e);
        double content = 0;
        var exBody = FirstChild(e, "value") is { } dv
            ? Descendants(dv, "exData").FirstOrDefault()
            : null;
        if (exBody is not null && exBody.SelectNodes(".//*[local-name()='p']")!.Count > 0)
            content = RichContentHeight(ctx, e, availText, exBody);
        else if (InnerText(e, "value") is { } dtxt && !string.IsNullOrWhiteSpace(dtxt))
        {
            double lineH = FirstChild(e, "para") is { } dpel
                && LenN(dpel.GetAttribute("lineHeight")) is { } dplh && dplh > 0 ? dplh : fs;
            content = LineCount(dtxt.Trim(), availText, fs, FontBold(e)) * lineH;
        }
        if (content <= 0) content = fs * 1.15;
        double nat = content + mt + mb + (plc is "top" or "bottom" ? res : 0);
        return Math.Max(minH ?? 0, nat);
    }
}
