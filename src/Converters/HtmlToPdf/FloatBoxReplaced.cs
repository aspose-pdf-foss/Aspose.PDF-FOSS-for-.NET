using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// THE QUIRKS INLINE-STYLED DOCUMENT: the float-box formatter's second claim - a sheet with no
// doctype and no stylesheet, every block styled inline by an editor (`margin: 0pt 0pt 10pt;
// line-height: 115%; font-size: 12pt` on each paragraph), its pictures remote and unreachable.
// The reference converter lays it out as the same block flow (probed on synthetic fixtures)
// with the quirks differences: a paragraph without a margin of its own has the UA 1.12 em; a
// line holding only replaced boxes has no strut (the block is exactly the box); a sized picture
// that cannot load is a bordered box (its CSS border, else the 1 pt two-tone chrome) holding the
// 32 pt broken-image icon at its content corner and the alt text 34 pt in and 2 pt down, on the
// inherited line; an unsized one is its alt text on the line, or nothing without alt.
internal static partial class HtmlToPdfConverter
{
    private const double FbUaParagraphMarginEm = 1.12;        // the UA <p> margin (probed: 1.12 em at ten sizes)
    private const double FbBrokenIconPt = 32.0;                // the broken-image icon's square
    private const double FbAltTextInsetPt = 34.0;              // the alt text's pen: icon + 2 pt gap
    private const double FbAltTextDropPt = 2.0;                // the alt line box top below the content top
    private const double FbUaChromePt = 1.0;                   // the broken picture's UA frame
    private const string FbChromeDarkRgb = "0.333 0.333 0.333"; // its top and left tone
    private const string FbChromeLightRgb = "0.667 0.667 0.667"; // its bottom and right tone

    /// <summary>A sized picture that could not load, seated in its line as a box: whether the UA
    /// two-tone chrome frames it (no CSS border of its own) and whether the icon draws (an alt text
    /// names a picture; an empty alt draws the frame alone).</summary>
    private sealed class FbReplaced
    {
        public bool UaChrome;
        public bool Icon;
    }

    /// <summary>Why the quirks inline-styled claim declines a document, or null when it claims it:
    /// no doctype, no stylesheet, nothing floated or positioned inline, no controls or frames, at
    /// least one remote picture and none local, paragraphs styled inline with their line height.</summary>
    private static string? FbQuirksDeclines(ConvertState cv)
    {
        var html = cv.html;
        if (cv.marginsExplicit) return "margins";
        if (Regex.IsMatch(html, @"<!DOCTYPE\b", RegexOptions.IgnoreCase)) return "doctype";
        if (Regex.IsMatch(html, @"<(style|link)\b", RegexOptions.IgnoreCase)) return "sheet";
        if (Regex.IsMatch(html, @"style\s*=\s*[""'][^""']*(float\s*:|position\s*:\s*(absolute|fixed))", RegexOptions.IgnoreCase)) return "float or positioned inline";
        if (Regex.IsMatch(html, @"<(iframe|svg|object|embed|frameset|form|input|textarea|select|button)\b", RegexOptions.IgnoreCase)) return "controls or frames";
        var imgs = Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
        if (imgs.Count == 0) return "no pictures";
        foreach (Match m in imgs)
            if (!Regex.IsMatch(m.Value, @"\bsrc\s*=\s*[""']?https?://", RegexOptions.IgnoreCase)) return "local picture";
        if (!Regex.IsMatch(html, @"<p\b[^>]*style\s*=\s*[""'][^""']*line-height\s*:", RegexOptions.IgnoreCase)) return "paragraphs not styled inline";
        return null;
    }

    /// <summary>The declared picture size in px from the attributes or the CSS width / height; zero when unsized.</summary>
    private static (double wPx, double hPx) FbPictureSizePx(FbState fb, HtmlNode el)
    {
        double Dim(string name)
        {
            var css = FbDecl(fb, el, name);
            if (!string.IsNullOrEmpty(css) && FbSizePt(css, 0) is { } pt) return pt / FbPxPt;
            return el.Attrs is not null && el.Attrs.TryGetValue(name, out var a)
                && double.TryParse(a.Trim().TrimEnd('p', 'x'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        return (Dim("width"), Dim("height"));
    }

    /// <summary>A picture in the run: a sized one that cannot load is a replaced box on the line (its
    /// alt text laid inside it), an unsized one is its alt text, and an unsized one without alt is
    /// nothing; a picture aligned left or right by attribute floats with its hspace as side margins.</summary>
    private static void FbAddPicture(FbState fb, FbBox parent, HtmlNode el, FbStyle st, FbBoxProps p, double cx, double cw, ref double cursor, FbBfc bfc, FbFlow flow)
    {
        var alt = el.Attrs is not null && el.Attrs.TryGetValue("alt", out var a) ? DecodeEntities(a).Trim() : "";
        var (wPx, hPx) = FbPictureSizePx(fb, el);
        if (wPx <= 0 || hPx <= 0)
        {
            if (alt.Length == 0) return;
            // (a floated picture shows its alt inside its float box - the alt is the box's one text child)
            if (p.Float != "none")
            {
                if (el.Children.Count == 0) el.Children.Add(new HtmlNode { Text = alt, Parent = el });
                p.HeightAuto = true;
                cursor += flow.PendingMb;
                flow.PendingMb = 0;
                FbPlaceFloat(fb, parent, el, st, p, cx, cw, cursor, bfc);
                return;
            }
            FbAddText(fb, flow.Run, alt, st);
            return;
        }
        p.WidthAuto = false; p.WidthPt = wPx * FbPxPt;
        p.HeightAuto = false; p.HeightPt = hPx * FbPxPt;
        var uaChrome = !p.HasVisibleBorder;
        if (uaChrome) p.Border.T = p.Border.R = p.Border.B = p.Border.L = FbUaChromePt;
        var side = el.Attrs is not null && el.Attrs.TryGetValue("align", out var al) ? al.Trim().ToLowerInvariant() : "";
        if (side is "left" or "right")
        {
            p.Float = side;
            if (el.Attrs!.TryGetValue("hspace", out var hs) && double.TryParse(hs.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hsPx))
                p.Margin.L = p.Margin.R = hsPx * FbPxPt;
            cursor += flow.PendingMb;
            flow.PendingMb = 0;
            FbPlaceFloat(fb, parent, el, st, p, cx, cw, cursor, bfc);
            parent.Kids[^1].Img = new FbReplaced { UaChrome = uaChrome, Icon = alt.Length > 0 };
            return;
        }
        var box = new FbBox { Node = el, St = st, P = p, IsBfc = true, IsInline = true, X = 0, Y = 0, Img = new FbReplaced { UaChrome = uaChrome, Icon = alt.Length > 0 } };
        box.W = p.WidthPt + p.ChromeH;
        box.H = p.HeightPt + p.ChromeV;
        if (alt.Length > 0)
        {
            var run = new List<FbItem>();
            FbAddText(fb, run, alt, st);
            var altCursor = box.ContentY + FbAltTextDropPt;
            FbFlushRun(fb, box, run, box.ContentX + FbAltTextInsetPt, Math.Max(0, p.WidthPt - FbAltTextInsetPt), ref altCursor, new FbBfc());
        }
        parent.Kids.Add(box);
        flow.Run.Add(new FbItem { Box = box, Adv = box.OuterW, Above = box.OuterH, Below = 0, St = st, VAlignTop = true });
    }

    /// <summary>Paint a replaced box's frame and icon: the UA chrome is a 1 pt bevel, dark above and
    /// left, light below and right; the icon stands at the content corner.</summary>
    private static void FbPaintReplaced(FbState fb, FbBox box)
    {
        var img = box.Img!;
        if (img.UaChrome)
        {
            var h = FbUaChromePt / 2;
            FbStrokeH(fb, box.X, box.X + box.W, box.Y + h, FbChromeDarkRgb, FbUaChromePt);
            FbStrokeV(fb, box.X + h, box.Y, box.Y + box.H, FbChromeDarkRgb, FbUaChromePt);
            FbStrokeH(fb, box.X, box.X + box.W, box.Y + box.H - h, FbChromeLightRgb, FbUaChromePt);
            FbStrokeV(fb, box.X + box.W - h, box.Y, box.Y + box.H, FbChromeLightRgb, FbUaChromePt);
        }
        if (!img.Icon) return;
        var (page, py) = FbPageOf(fb, box.ContentY);
        if (page >= fb.pageObjs.Count) return;
        string res;
        (res, fb.iconRef) = RegisterPlaceholderIcon(fb.doc, fb.pageObjs[page], fb.iconRef, masked: true);
        FbOps(fb, page).Append(Compat.Format(fb.invc, $"q {FbBrokenIconPt:0.###} 0 0 {FbBrokenIconPt:0.###} {box.ContentX:0.###} {fb.pageH - py - FbBrokenIconPt:0.###} cm /{res} Do Q\n"));
    }
}
