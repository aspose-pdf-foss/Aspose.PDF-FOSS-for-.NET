using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>Draw the run: CID-keyed text through the CID path, everything else through the simple-font path with its fallback face.</summary>
    private static void DrawTextRuns(TextDrawState dt)
    {
        if (dt.cidInfo is not null && dt.rawBytes is not null
            && (dt.cidInfo.IsTwoByteEncoding || (dt.cidInfo.CMapCodeToCid is not null && dt.cidInfo.HasFixedSingleByteCMap)
                || dt.parser is CffGlyphSource { IsCidKeyed: true }))
        {
            dt.px = DrawCidText(dt.ctx, dt.rawBytes, dt.cidInfo, dt.parser, dt.fontMetrics,
                dt.px, dt.py, dt.effectiveSize, dt.hScale, dt.charSpacingPx, dt.wordSpacingPx, dt.r, dt.g, dt.b, dt.a);
        }
        else
        {
            // A simple-font run whose own program cannot be resolved leaves every glyph id
            // at 0 and draws NOTHING - the advances still walk, so the line silently
            // disappears. Substitute a host face (the /BaseFont's own name, else Arial) and
            // look the glyphs up by Unicode, which is what the GDI+ rasteriser does. A run
            // whose font was re-assigned on a live document, whose program only materialises
            // when the document is saved, is the case that needs it. A /Differences map is
            // for the ORIGINAL program's glyph ids, so it is dropped with the program.
            var simpleParser = dt.parser ?? ResolveSimpleFallback(dt.ctx, dt.state.FontName);
            var substituted = dt.parser is null && simpleParser is not null;
            dt.px = DrawSimpleText(dt.ctx, dt.text, dt.rawBytes, simpleParser, dt.fontMetrics,
                substituted ? null : GetEncodingGidMap(dt.ctx, dt.state.FontName, dt.parser),
                dt.px, dt.py, dt.effectiveSize, dt.hScale, dt.charSpacingPx, dt.wordSpacingPx, dt.r, dt.g, dt.b, dt.a,
                substituted, GetUndefinedCodes(dt.ctx, dt.state.FontName));
        }
    }

    /// <summary>Resolve the glyph source, metrics and CID info of the current font, and the character and word spacing in pixels.</summary>
    private static void ResolveTextFont(TextDrawState dt)
    {
        (dt.parser, dt.hScale) = GetGlyphParser(dt.ctx, dt.state.FontName);
        dt.hScale *= dt.textHScale;
        if (dt.ctx.GlyphEmMatrix is null) dt.hScale *= dt.anisotropy;
        dt.fontMetrics = GetFontMetrics(dt.ctx, dt.state.FontName);
        dt.cidInfo = GetCidFontInfo(dt.ctx, dt.state.FontName);

        dt.textSpaceScale = Math.Sqrt(dt.trm[0] * dt.trm[0] + dt.trm[2] * dt.trm[2]);
        dt.charSpacingPx = dt.state.CharSpacing * dt.textSpaceScale * dt.ctx.Scale * dt.textHScale;
        dt.wordSpacingPx = dt.state.WordSpacing * dt.textSpaceScale * dt.ctx.Scale * dt.textHScale;
    }

    /// <summary>A rotated, skewed or mirrored text matrix draws through the glyph em matrix with its baseline direction; the upright case draws unrotated.</summary>
    private static void SetGlyphEmMatrix(TextDrawState dt)
    {
        if (Math.Abs(dt.trm[1]) > 1e-9 || Math.Abs(dt.trm[2]) > 1e-9 || dt.trm[0] < 0 || dt.trm[3] < 0
            || dt.fsSign < 0 || dt.thSign < 0)
        {
            var kx = dt.fontSize * dt.fsSign * dt.thSign * dt.ctx.Scale;
            var ky = dt.fontSize * dt.fsSign * dt.ctx.Scale;
            dt.ctx.GlyphEmMatrix = new[]
            {
                kx * dt.hScale * dt.trm[0], -kx * dt.hScale * dt.trm[1],
                ky * dt.trm[2], -ky * dt.trm[3],
            };
            // The pen direction is the text-space x axis in device pixels, normalised: the
            // advances the draw loops accumulate are already in device pixels along it. It
            // follows the SIGNED x scale, so a mirrored run walks the other way.
            var penSign = kx < 0 ? -1.0 : 1.0;
            var bx0 = penSign * dt.trm[0] * dt.ctx.Scale;
            var by0 = -penSign * dt.trm[1] * dt.ctx.Scale;
            var blen = Math.Sqrt(bx0 * bx0 + by0 * by0);
            dt.ctx.BaselineUx = blen > 1e-12 ? bx0 / blen : 1;
            dt.ctx.BaselineUy = blen > 1e-12 ? by0 / blen : 0;
            dt.ctx.GlyphOriginX = dt.px;
            dt.ctx.GlyphOriginY = dt.py;
        }
        else
        {
            dt.ctx.GlyphEmMatrix = null;
        }
    }

    /// <summary>The pen origin in pixels, the fill colour bytes and the horizontal scale of the run.</summary>
    private static void ComputeTextPen(TextDrawState dt)
    {
        dt.trmXLen = Math.Sqrt(dt.trm[0] * dt.trm[0] + dt.trm[2] * dt.trm[2]);
        dt.trmYLen = Math.Sqrt(dt.trm[1] * dt.trm[1] + dt.trm[3] * dt.trm[3]);
        dt.anisotropy = dt.trmYLen > 1e-12 ? dt.trmXLen / dt.trmYLen : 1.0;

        dt.x = dt.trm[4];
        dt.y = dt.trm[5];

        dt.px = (double)((dt.x - dt.ctx.MediaBox.LLX) * dt.ctx.Scale);
        dt.py = (double)(dt.ctx.PixelH - (dt.y - dt.ctx.MediaBox.LLY) * dt.ctx.Scale);

        dt.r = (byte)(dt.state.FillR * 255);
        dt.g = (byte)(dt.state.FillG * 255);
        dt.b = (byte)(dt.state.FillB * 255);
        dt.a = (byte)(dt.state.FillAlpha * 255);

        dt.hScale = GetGlyphParser(dt.ctx, dt.state.FontName).hScale;
        dt.textHScale = Math.Abs(dt.state.HorizontalScaling) / 100.0;
        dt.hScale *= dt.textHScale;
    }

    /// <summary>A Type3 font draws through its glyph procedures; true when the run was drawn that way.</summary>
    private static bool TryDrawType3Text(TextDrawState dt)
    {
        if (dt.rawBytes is not null && dt.rawBytes.Length > 0
            && dt.state.FontName is { } fname
            && dt.ctx.FontDicts is not null
            && dt.ctx.FontDicts.TryGetValue(fname, out var fdict)
            && fdict.GetName("Subtype") == "Type3")
        {
            DrawType3Text(dt.ctx, dt.rawBytes, dt.state, fdict);
            return true;
        }
        return false;
    }
}
