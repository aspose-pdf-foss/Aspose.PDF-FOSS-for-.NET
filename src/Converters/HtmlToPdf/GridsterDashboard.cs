using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The gridster widget dashboard ───────────────────────────────────────
    //
    // An Aurelia "pdf-widget" export: a .gridster <ul> of absolutely placed
    // <li class="gs-w"> items, each carrying its whole box in an inline style
    // (top / left / width / height, in px). Widget items hold a bordered
    // .pdf-widget-box with a bold caption and a nested gridster of control
    // items; each control is a label (.pdf-field-label, in a 120 px
    // .pdf-field-control-header-left) beside a red .pdf-widget-control-box
    // carrying the value (.pdf-label-control).
    //
    // Measured geometry:
    //  - the canvas is the declared px geometry at 0.75 pt/px, anchored at
    //    the page's own left margin plus the .pdf-widget-container padding
    //    (96 pt + 20 px = 111 pt; the first widget's own left:5px puts its
    //    border box at 114.75, and its stroke centre lands at 115.1);
    //  - a widget BOX hugs its content instead of taking the li's declared
    //    height: caption band (29 px) + the nested control rows + the body's
    //    20 px padding-bottom;
    //  - control rows pace on their own declared tops (5/35/65/95 px → the
    //    22.5 pt ladder drawn at y 146/168.5/191/213.5);
    //  - the red .pdf-widget-control-box fills the VALUE half of the control
    //    box — the control's own width less the 120 px label column;
    //  - label 16 px Arial, value 10.5 pt Arial seated 0.84 pt below it,
    //    caption 18 px Arial Bold.
    private const double GridsterPxPt = 0.75;
    private const double GridsterContainerPadPx = 20.0;
    private const double GridsterCaptionBandPx = 29.0;
    private const double GridsterBodyPadBottomPx = 20.0;
    private const double GridsterLabelColPx = 120.0;
    private const double GridsterLabelPx = 16.0;
    private const double GridsterCaptionPx = 18.0;
    private const double GridsterValuePt = 10.5;
    /// <summary>Value baseline sits this far below the label's (probed: the
    /// label at y 148.31 against the value at 149.15).</summary>
    private const double GridsterValueDropPt = 0.84;
    private const double GridsterBoxBorderPx = 1.0;

    private sealed class GridsterItem
    {
        public double LeftPx, TopPx, WidthPx, HeightPx;
        public string Html = "";
        public bool IsWidget;
    }

    private static Document? TryRenderGridsterDashboard(string html, double pageHeight)
    {
        if (!html.Contains("pdf-widget-container", System.StringComparison.Ordinal)
            || !html.Contains("gs-w", System.StringComparison.Ordinal)) return null;

        var items = ParseGridsterItems(html);
        if (items.Count == 0) return null;
        var widgets = items.FindAll(i => i.IsWidget);
        if (widgets.Count == 0) return null;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Px(double v) => v * GridsterPxPt;

        // Page: the widest widget's right edge plus the container padding, between
        // the document's own margins (the HTML defaults this dialect keeps).
        const double marginLeft = 96.0, marginRight = 72.0;
        var originX = marginLeft + Px(GridsterContainerPadPx);
        double widestPx = 0;
        foreach (var w in widgets) widestPx = System.Math.Max(widestPx, w.LeftPx + w.WidthPx);
        var pageWidth = originX + Px(widestPx + GridsterContainerPadPx) + marginRight;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var sb = new StringBuilder();

        // The gridster's own top: the page margin plus the container's padding and
        // the .pdf-gridster-container padding-top (10 px).
        var originY = 96.0 + Px(GridsterContainerPadPx + 10.0);

        foreach (var w in widgets)
        {
            var boxX = originX + Px(w.LeftPx);
            var boxTop = originY + Px(w.TopPx);
            var boxW = Px(w.WidthPx);

            var controls = ParseGridsterItems(w.Html);
            controls.RemoveAll(c => c.IsWidget);

            // The box hugs its content: caption band + the deepest control row +
            // the body's bottom padding.
            double deepestPx = 0;
            foreach (var c in controls) deepestPx = System.Math.Max(deepestPx, c.TopPx + c.HeightPx);
            var boxH = Px(GridsterCaptionBandPx + deepestPx + GridsterBodyPadBottomPx);

            // Border box (1 px, rgb(28,42,67)), stroked on its centre line.
            var bw = Px(GridsterBoxBorderPx);
            sb.Append(string.Create(inv,
                $"q {28 / 255.0:0.###} {42 / 255.0:0.###} {67 / 255.0:0.###} RG {bw:0.##} w " +
                $"{boxX + bw / 2:F2} {pageHeight - boxTop - bw / 2:F2} " +
                $"{boxW - bw:F2} {-(boxH - bw):F2} re S Q\n"));

            // Caption: 18 px Arial Bold, 5 px in from the box, on the band.
            var caption = FirstClassText(w.Html, "pdf-widget-name");
            if (caption.Length > 0)
            {
                var capSize = Px(GridsterCaptionPx);
                var capX = boxX + Px(GridsterBoxBorderPx + 5.0);
                var capBase = boxTop + Px(GridsterBoxBorderPx + 5.0) + capSize;
                EmitGridsterText(page, resByFace, capSize, capX, pageHeight - capBase,
                    caption, "Arial,Bold");
            }

            var bodyTop = boxTop + Px(GridsterCaptionBandPx);
            foreach (var c in controls)
            {
                var cx = boxX + Px(GridsterBoxBorderPx + c.LeftPx);
                var cTop = bodyTop + Px(c.TopPx);
                var cW = Px(c.WidthPx);

                var label = FirstClassText(c.Html, "pdf-field-label");
                var value = FirstClassText(c.Html, "pdf-label-control");

                // The red control box fills the value half of the control.
                var valX = cx + Px(GridsterLabelColPx);
                var valW = cW - Px(GridsterLabelColPx);
                if (valW > 0)
                    sb.Append(string.Create(inv,
                        $"q 1 0 0 rg {valX:F2} {pageHeight - cTop - Px(c.HeightPx):F2} " +
                        $"{valW:F2} {Px(c.HeightPx):F2} re f Q\n"));

                // Label: 16 px Arial, its baseline at the row's own 3 px pad + ascent.
                var labSize = Px(GridsterLabelPx);
                var labBase = cTop + Px(3.0) + labSize * ArialAscentEm;
                if (label.Length > 0)
                    EmitGridsterText(page, resByFace, labSize, cx, pageHeight - labBase,
                        label, "Arial");

                // Value: 10.5 pt Arial, RIGHT-aligned inside the red box (probed:
                // every value's right edge lands on the box's right inset).
                if (value.Length > 0 && valW > 0)
                {
                    var vw = MeasureFaceText("Arial", value, GridsterValuePt);
                    var vx = valX + valW - vw;
                    EmitGridsterText(page, resByFace, GridsterValuePt, vx,
                        pageHeight - (labBase + GridsterValueDropPt), value, "Arial");
                }
            }
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        PruneUnusedFonts(doc);
        return doc;
    }

    /// <summary>Draw one positioned run in a Standard-14-named face, registering the
    /// resource on the page the way the metric flow does.</summary>
    private static void EmitGridsterText(Page page, Dictionary<string, string> resByFace,
        double size, double x, double y, string text, string face)
    {
        if (!resByFace.TryGetValue(face, out var rn))
        {
            rn = "F" + (20 + resByFace.Count);
            resByFace[face] = rn;
        }
        EnsureFont(page, face.Replace(" ", ""), rn);
        EmitCellLineRuns(page, rn, size, x, y, text, face);
    }

    /// <summary>…in a given ink: the fill colour is set and restored around the run
    /// so the page's default black is untouched.</summary>
    private static void EmitGridsterText(Page page, Dictionary<string, string> resByFace,
        double size, double x, double y, string text, string face,
        (double R, double G, double B) ink)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(string.Create(inv,
            $"q {ink.R:0.###} {ink.G:0.###} {ink.B:0.###} rg\n")));
        EmitGridsterText(page, resByFace, size, x, y, text, face);
        page.AddContentStream(System.Text.Encoding.ASCII.GetBytes("Q\n"));
    }

    /// <summary>Every <c>&lt;li class="gs-w"&gt;</c> at the TOP level of
    /// <paramref name="html"/>, with its declared px box and inner markup.</summary>
    private static List<GridsterItem> ParseGridsterItems(string html)
    {
        var result = new List<GridsterItem>();
        var openRx = new Regex(@"<li\b[^>]*\bclass=""[^""]*\bgs-w\b[^""]*""[^>]*>", RegexOptions.IgnoreCase);
        var anyRx = new Regex(@"<(?<c>/?)li\b[^>]*>", RegexOptions.IgnoreCase);
        var pos = 0;
        while (pos < html.Length)
        {
            var m = openRx.Match(html, pos);
            if (!m.Success) break;
            // The item runs to the close MATCHING its open, so a nested gridster
            // stays inside this item rather than reading as a sibling.
            var depth = 1;
            var end = -1;
            for (var scan = anyRx.Match(html, m.Index + m.Length); scan.Success;
                 scan = anyRx.Match(html, scan.Index + scan.Length))
            {
                depth += scan.Groups["c"].Length > 0 ? -1 : 1;
                if (depth == 0) { end = scan.Index; break; }
            }
            if (end < 0) break;
            var style = Regex.Match(m.Value, @"style=""(?<s>[^""]*)""").Groups["s"].Value;
            double Len(string prop)
            {
                var pm = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)\s*px",
                    RegexOptions.IgnoreCase);
                return pm.Success ? double.Parse(pm.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) : 0;
            }
            var inner = html[(m.Index + m.Length)..end];
            result.Add(new GridsterItem
            {
                LeftPx = Len("left"),
                TopPx = Len("top"),
                WidthPx = Len("width"),
                HeightPx = Len("height"),
                Html = inner,
                // A widget item carries the bordered box; a control item does not.
                IsWidget = inner.Contains("pdf-widget-box", System.StringComparison.Ordinal),
            });
            pos = end;
        }
        return result;
    }

    /// <summary>Text of the first element carrying <paramref name="cls"/>.</summary>
    private static string FirstClassText(string html, string cls)
    {
        var m = Regex.Match(html,
            @"<(?<t>\w[\w-]*)\b[^>]*\bclass=""[^""]*\b" + Regex.Escape(cls) + @"\b[^""]*""[^>]*>(?<b>[\s\S]*?)</\k<t>\s*>",
            RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        var text = Regex.Replace(m.Groups["b"].Value, "<!--[\\s\\S]*?-->", "");
        text = Regex.Replace(text, "<[^>]+>", "");
        return Regex.Replace(DecodeEntities(text), @"\s+", " ").Trim();
    }
}
