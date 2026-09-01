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
    /// <summary>
    /// Resolve a page's /Resources by walking up the /Parent chain. PDF 32000 §7.7.3.4
    /// lists /Resources as one of the inheritable page attributes — pages routinely
    /// omit their own /Resources when the parent /Pages dict carries the shared font /
    /// pattern / XObject table. The depth cap protects against malformed PDFs whose
    /// /Parent chain loops back on itself.
    /// </summary>
    internal static PdfDictionary? ResolveInheritedPageResources(PdfDictionary pageDict, PdfReader reader)
    {
        var dict = pageDict;
        for (var depth = 0; dict is not null && depth < 32; depth++)
        {
            var res = reader.ResolveDict(dict.Get("Resources"));
            if (res is not null) return res;
            dict = reader.ResolveDict(dict.Get("Parent"));
        }
        return null;
    }

    internal static Dictionary<string, PdfDictionary> ResolveFontDicts(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;
        foreach (var key in fontDict.Keys)
        {
            var fd = reader.ResolveDict(fontDict.Get(key));
            if (fd is not null) result[key] = fd;
        }
        return result;
    }

    internal static Dictionary<string, PdfDictionary>? ResolveExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        if (resources is null) return null;
        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return null;
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        foreach (var key in gsDict.Keys)
        {
            var d = reader.ResolveDict(gsDict.Get(key));
            if (d is not null) result[key] = d;
        }
        return result;
    }

    internal static Dictionary<string, PdfStream> ResolveAllXObjects(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfStream>(StringComparer.Ordinal);
        if (resources is null) return result;
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return result;
        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is not null)
                result[key] = obj;
        }
        return result;
    }

    internal static byte[] GetPageContent(PdfDictionary pageDict, PdfReader reader)
    {
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) return reader.DecodeStream(stream);
        if (obj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    private sealed class RenderContext(byte[] pixels, int pixelW, int pixelH,
        double scale, Rectangle mediaBox, PdfReader reader)
    {
        public byte[] Pixels => pixels;
        public int PixelW => pixelW;
        public int PixelH => pixelH;
        public double Scale => scale;
        public Rectangle MediaBox => mediaBox;
        public PdfReader Reader => reader;
        public Dictionary<string, PdfStream>? AllXObjects { get; set; }
        public Dictionary<string, PdfDictionary>? FontDicts { get; set; }
        public Dictionary<string, (IGlyphOutlineSource? parser, double hScale)> FontParsers { get; } = new(StringComparer.Ordinal);

        /// <summary>Render charstring-outline embedded fonts through a converted
        /// TrueType sfnt (<see cref="RenderingOptions.ConvertFontsToUnicodeTTF"/>).</summary>
        public bool ConvertFontsToUnicodeTtf { get; set; }

        /// <summary>The document IDENTIFIES as PDF/X, so overprint is SIMULATED rather than
        /// composited (PDF 32000 §8.6.7). Resolved once per render - the marker is a
        /// document-level key, and probing it per image draw would reparse the info dict.</summary>
        public bool PdfXOverprintSim { get; set; }
        public Dictionary<string, CidFontInfo?> CidFontInfos { get; } = new(StringComparer.Ordinal);
        // Per-font byte→GID map built from the PDF /Encoding /Differences glyph names
        // (resolved through the embedded font's name table). null = no usable map.
        public Dictionary<PdfDictionary, int[]?> EncodingGidMaps { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Page's /Resources/Pattern dict — looked up when resolving a <c>scn</c> pattern name.
        /// Propagated into child contexts (Form XObjects, pattern tiles) so nested fills resolve
        /// correctly. Null when the page has no Pattern resources.
        /// </summary>
        public PdfDictionary? Patterns { get; set; }

        /// <summary>
        /// Page / Form-XObject / Pattern /Resources/Shading dict — looked up by the
        /// <c>sh</c> operator to paint a smooth-gradient fill inside the current clip.
        /// Null means this scope has no shading resources.
        /// </summary>
        public PdfDictionary? Shadings { get; set; }

        /// <summary>
        /// Page resources /ColorSpace dictionary — Separation / DeviceN array
        /// references named in `cs`/`CS` operators. Passed through to the
        /// content stream parser so tint transforms can convert named-spot
        /// colors into the renderer's RGB pipeline. Null when the page has
        /// no named colorspaces (the common case).
        /// </summary>
        public PdfDictionary? ColorSpaces { get; set; }

        /// <summary>
        /// Page resources /Properties dict — referenced by name in BDC's
        /// second operand (<c>/OC /MC0 BDC</c>). Stored on the context so
        /// child renders (Form XObjects, patterns) can pass it through to
        /// their nested content-stream parser as well.
        /// </summary>
        public PdfDictionary? Properties { get; set; }

        /// <summary>
        /// OCG dicts (resolved instances) that the current OC config marks as
        /// hidden — i.e. content inside their <c>/OC /Name BDC … EMC</c> ranges
        /// must NOT be drawn. Built once per page from /OCProperties/D.
        /// Reference equality is fine because PdfReader caches resolved dicts.
        /// </summary>
        public HashSet<PdfDictionary>? OcgHidden { get; set; }

        /// <summary>
        /// Stack of "did this marked-content frame hide its content?". Pushed
        /// on every BMC/BDC, popped on every EMC. <see cref="IsContentHidden"/>
        /// reports true while any frame in the stack is true.
        /// </summary>
        public Stack<bool> OcgHiddenStack { get; } = new();

        /// <summary>True when the current draw operation lies inside a marked-content
        /// range belonging to an OCG flagged invisible by the OC config.</summary>
        public bool IsContentHidden
        {
            get
            {
                foreach (var hidden in OcgHiddenStack)
                    if (hidden) return true;
                return false;
            }
        }

        /// <summary>
        /// Optional 1-byte-per-pixel stencil: non-zero pixels are paintable, zero pixels are
        /// masked out. Used during tiling-pattern fill so the pattern only paints inside the
        /// current path. Null means no clipping (the normal unmasked case).
        /// </summary>
        public byte[]? ClipMask { get; set; }

        /// <summary>
        /// Coverage being accumulated for a TEXT clip (Tr 4-7, PDF 32000 §9.3.6): the glyphs
        /// shown inside the current BT…ET add their shapes to the clipping path, which takes
        /// effect at ET and lasts until the enclosing Q. Non-null only while such a text
        /// object is open; <see cref="BlitAlphaMask"/> writes glyph coverage here instead of
        /// (or as well as) painting it. One page-sized plane, allocated on first use.
        /// </summary>
        public byte[]? TextClipAccum { get; set; }

        /// <summary>Whether the open text clip also PAINTS its glyphs: Tr 4/5/6 fill or stroke
        /// and clip, Tr 7 only clips. Meaningless while <see cref="TextClipAccum"/> is null.</summary>
        public bool TextClipPaints { get; set; }

        /// <summary>
        /// Font units to device pixels for the run being drawn, as (a, b, c, d), when the
        /// text matrix ROTATES or skews the glyphs. Null for the upright case, which keeps
        /// the cheaper axis-aligned rasterisation. Set per text-showing operator.
        /// </summary>
        public double[]? GlyphEmMatrix { get; set; }

        /// <summary>Device-pixel position of the run’s first glyph origin, and the unit
        /// vector the pen travels along. The pen itself stays a scalar distance (px), so a
        /// rotated run advances along its own baseline rather than along the raster’s x.
        /// Only read while <see cref="GlyphEmMatrix"/> is set.</summary>
        public double GlyphOriginX { get; set; }
        public double GlyphOriginY { get; set; }
        public double BaselineUx { get; set; } = 1;
        public double BaselineUy { get; set; }

        /// <summary>
        /// The page’s DEFAULT user space as a CTM (the /Rotate compensation, identity for
        /// an unrotated page). A pattern’s /Matrix is defined against this and NOT against
        /// whatever CTM happens to be in force at the fill (PDF 32000 §8.7.3.1), so it has
        /// to travel with the context. Null means identity.
        /// </summary>
        public double[]? PageCtm { get; set; }


        /// <summary>
        /// Active PDF 32000 §11.3.5 blend mode for the next pixel write — set by callers
        /// (DrawText / DrawPath / DrawImage / etc.) from <c>state.BlendMode</c> before each
        /// blit, read by SetPixel. "Normal" means straight Porter-Duff source-over alpha;
        /// "Multiply" applies the multiplicative blend separable formula. Other modes fall
        /// back to Normal until they're implemented.
        /// </summary>
        public string CurrentBlendMode { get; set; } = "Normal";

        /// <summary>
        /// True while this context is the scratch buffer of a knockout transparency group
        /// (PDF 32000 §11.4.4 / §11.6.6, /Group dict with /K true). Each new draw inside a
        /// knockout group composites against the group's ORIGINAL backdrop (transparent for
        /// /S /Transparency groups), not against accumulated prior draws — so overlapping
        /// elements show only the topmost. We implement that by switching pixel writes to
        /// "replace with src·alpha" instead of source-over, and by skipping blend-mode
        /// dispatch (a non-Normal blend against a transparent backdrop reduces to src*α
        /// per the spec's compositing equation). Strokes don't currently honour this flag —
        /// rare in practice, parallel to the existing stroke/blend-mode gap.
        /// </summary>
        public bool IsKnockoutGroup { get; set; }

        /// <summary>
        /// Per-pixel soft-mask alpha (PDF 32000 §11.6.5.4) installed by paint sites
        /// from the active <c>state.SoftMask</c>. One byte per pixel (page-sized);
        /// each fragment's effective alpha is multiplied by <c>SoftMaskAlpha[idx]</c>
        /// before blending. Null means no soft mask. Resolved lazily and cached per
        /// page in <see cref="SoftMaskCache"/>.
        /// </summary>
        public byte[]? SoftMaskAlpha { get; set; }

        /// <summary>
        /// Per-page cache keyed by the soft-mask group dict's object number, mapping
        /// to the rendered alpha buffer. A single PDF often references one SMask in
        /// dozens of paint operations; resolving (rendering the group) on each is
        /// prohibitive. Page-scoped because the same SMask dict can be referenced
        /// from multiple paint sites without re-rendering.
        /// </summary>
        public Dictionary<int, byte[]> SoftMaskCache { get; } = new();
    }
}
