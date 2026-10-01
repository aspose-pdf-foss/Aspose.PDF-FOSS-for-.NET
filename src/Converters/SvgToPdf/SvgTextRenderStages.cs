using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
// The stages of the SVG text render: one run's emit and the tspan walk.
    private static void EmitSvgTextRun(SvgTextRenderState sx, string text, XmlElement source, Dictionary<string, string> runStyle)
    {
        if (text.Length == 0) return;
        // U+A880 is the exporter's PUA stand-in for a space-like glyph slot
        // (see SvgDevice.ShowText); map it back to a plain space on import.
        text = text.Replace('ꢀ', ' ');
        if (text.Trim().Length == 0) return;

        sx.fontSize = ParseLength(Prop(runStyle, "font-size"));
        if (sx.fontSize <= 0) sx.fontSize = 16;

        sx.uniTtf = ResolveDeclaredFace(sx.ctx, Prop(runStyle, "font-family"));
        sx.declaredFaceName = sx.uniTtf is null ? null : DeclaredFaceName(sx.ctx, Prop(runStyle, "font-family"));
        if (sx.uniTtf is null && NeedsUnicodeSvg(text)) sx.uniTtf = ResolveSvgUnicodeTtf(text);
        sx.display = text;
        if (sx.uniTtf is not null)
            sx.display = IsPureRtlSvg(text) ? ToVisualRtlSvg(text)
                : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtlSvg(text) : text;

        sx.baseFont = MapFont(runStyle);
        sx.fontDict = GetOrCreate(sx.ctx.Surface.Resources, "Font");
        sx.fontRes = sx.uniTtf is null ? EnsureFontResource(sx.ctx, sx.baseFont) : "";

        sx.embedName = sx.declaredFaceName ?? "SvgUni";
        sx.width = sx.uniTtf is null
            ? MeasureText(text, sx.baseFont, sx.fontSize)
            : Text.Type0FontEmbedder.MeasureText(sx.fontDict, sx.uniTtf, sx.embedName, sx.display, sx.fontSize,
                stripSpacesInBaseFont: true, resNameHint: NextCompositeResName(sx.fontDict));
        sx.anchor = Prop(runStyle, "text-anchor");
        sx.x = sx.curX;
        if (sx.anchor == "middle") sx.x -= sx.width / 2;
        else if (sx.anchor == "end") sx.x -= sx.width;

        sx.visible = Prop(runStyle, "visibility") != "hidden";
        if (sx.visible)
        {
            DrawSvgTextRun(sx, text, runStyle);
        }
        sx.curX += sx.width;
    }

    private static void WalkSvgTspans(SvgTextRenderState sx, XmlNode node, Dictionary<string, string> nodeStyle)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                var text = CollapseWs(child.Value ?? "");
                EmitSvgTextRun(sx, text, (XmlElement)node, nodeStyle);
            }
            else if (child is XmlElement tspan && child.LocalName is "tspan" or "textPath" or "a")
            {
                var childStyle = ResolveStyle(tspan, nodeStyle, sx.ctx);
                if (tspan.HasAttribute("x")) sx.curX = GetFirstLen(tspan, "x", sx.ctx.VpW);
                if (tspan.HasAttribute("y")) sx.curY = GetFirstLen(tspan, "y", sx.ctx.VpH);
                sx.curX += GetFirstLen(tspan, "dx", sx.ctx.VpW);
                sx.curY += GetFirstLen(tspan, "dy", sx.ctx.VpH);
                WalkSvgTspans(sx, tspan, childStyle);
            }
        }
    }

    /// <summary></summary>
    private static void DrawSvgTextRun(SvgTextRenderState sx, string text, Dictionary<string, string> runStyle)
    {
        var fillVal = Prop(runStyle, "fill");
        double fr = 0, fg = 0, fb = 0;
        var noFill = IsNoPaint(fillVal);
        if (!noFill && ParseUrlRef(fillVal) is null)
            (fr, fg, fb) = ParseColor(fillVal);

        var opacity = ParseOpacity(runStyle.GetValueOrDefault("opacity"))
                      * ParseOpacity(Prop(runStyle, "fill-opacity"));

        sx.sb.Append("q\n");
        if (opacity < 0.999)
            sx.sb.Append($"/{RegisterAlphaGs(sx.ctx, opacity, opacity)} gs\n");
        if (!noFill)
            sx.sb.Append($"{F(fr)} {F(fg)} {F(fb)} rg ");

        // The glyph payload: WinAnsi (escaped) string, or 2-byte Type0 hex codes.
        string glyphOp;
        string useFontRes;
        if (sx.uniTtf is not null)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(sx.fontDict, sx.uniTtf, sx.embedName, sx.display,
                stripSpacesInBaseFont: true, resNameHint: NextCompositeResName(sx.fontDict));
            useFontRes = rn;
            glyphOp = $"<{Compat.ToHexString(hex)}>";
        }
        else
        {
            useFontRes = sx.fontRes;
            glyphOp = $"({EscapePdfString(text)})";
        }

        if (sx.tmMatrix is not null)
        {
            // FOSS-generated SVG (round-trip): a matrix() transform on <text> is the
            // PDF text matrix with its y-column negated (see SvgDevice) — negate it
            // back to recover the text matrix and place the run with Tm.
            sx.sb.Append($"BT /{useFontRes} {F(sx.fontSize)} Tf " +
                $"{F(sx.tmMatrix[0])} {F(sx.tmMatrix[1])} {F(-sx.tmMatrix[2])} {F(-sx.tmMatrix[3])} {F(sx.tmMatrix[4])} {F(sx.tmMatrix[5])} Tm ");
        }
        else
        {
            // Draw with a LOCAL y-flip (1 0 0 -1) so the glyphs are upright —
            // cancelling the page's scale(1,-1); without the flip the text
            // renders mirrored/upside-down.
            sx.sb.Append($"BT /{useFontRes} {F(sx.fontSize)} Tf 1 0 0 -1 {F(sx.x)} {F(sx.curY)} Tm ");
        }
        sx.sb.Append($"{glyphOp} Tj ET\n");

        // text-decoration: draw the line as a filled rect in user space.
        var deco = Prop(runStyle, "text-decoration");
        if (deco.Contains("line-through") || deco.Contains("underline"))
        {
            var t = Math.Max(sx.fontSize * 0.06, 0.5);
            if (deco.Contains("line-through"))
                sx.sb.Append($"{F(sx.x)} {F(sx.curY - sx.fontSize * 0.30 - t / 2)} {F(sx.width)} {F(t)} re f\n");
            if (deco.Contains("underline"))
                sx.sb.Append($"{F(sx.x)} {F(sx.curY + sx.fontSize * 0.11 - t / 2)} {F(sx.width)} {F(t)} re f\n");
        }
        sx.sb.Append("Q\n");
    }
}
