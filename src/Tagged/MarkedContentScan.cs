using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Tagged;

/// <summary>Reads what each marked-content sequence of a content stream paints: every shown
/// string with its font, size, colour and baseline span, and every image XObject with the
/// area it covers. A piece belongs to its innermost enclosing MCID; content outside any MCID
/// is not kept. The answer for a page is cached for as long as its content streams stay the
/// same objects holding the same data.</summary>
internal static class MarkedContentScan
{
    /// <summary>One shown string or one image, in content order.</summary>
    internal sealed class Piece
    {
        public int Mcid;
        public bool IsImage;
        /// <summary>A painted path or a form drawn whole: what it covers, in <see cref="Llx"/>..<see cref="Ury"/>.</summary>
        public bool IsDrawing;
        public string Text = string.Empty;
        /// <summary>Resource name and dictionary of the font the string is shown in.</summary>
        public string? FontKey;
        public PdfDictionary? FontDict;
        /// <summary>Effective size: the font size scaled by the text and current matrices.</summary>
        public double Size;
        /// <summary>Baseline start, end and y on the page as it is shown (a form stream: its own space); <see cref="Y1"/>
        /// is the end's y, where a turned string ends further up or down the page.</summary>
        public double X0, X1, Y, Y1;
        public double R, G, B;
        /// <summary>Text rendering mode (Tr).</summary>
        public int RenderMode;
        /// <summary>The baseline's direction on the page, in whole degrees counter-clockwise (0: left to right).</summary>
        public int Angle;
        /// <summary>For images: the XObject's resource name, stream and covered area.</summary>
        public string? ImageName;
        public PdfStream? ImageStream;
        // The matrix the image's unit square is drawn through, onto the page as it is shown.
        public double[]? ImageMatrix;
        public double Llx, Lly, Urx, Ury;
    }

    /// <summary>One artifact sequence of a content stream (<c>/Artifact</c> BMC, or BDC with no
    /// MCID): its /Type and /Subtype, and what it paints.</summary>
    internal sealed class ArtifactBlock
    {
        public string? Type;
        public string? Subtype;
        public List<Piece> Pieces = new();
    }

    /// <summary>What a content stream paints: the pieces under each MCID, and each artifact.</summary>
    internal sealed class ScanResult
    {
        public Dictionary<int, List<Piece>> ByMcid = new();
        public List<ArtifactBlock> Artifacts = new();
        /// <summary>The form XObjects the content draws, each with the matrix at its Do (on the shown page).</summary>
        public List<(PdfStream Form, double[] Ctm)> Forms = new();
    }

    private sealed class CacheEntry
    {
        public object[] Fingerprint = [];
        public double[]? Shown;
        public ScanResult Result = new();
    }

    private static readonly ConditionalWeakTable<PdfDictionary, CacheEntry> PageCache = new();
    private static readonly ConditionalWeakTable<PdfStream, CacheEntry> StreamCache = new();

    /// <summary>The pieces of the page's content, by MCID.</summary>
    public static Dictionary<int, List<Piece>> ForPage(PdfReader reader, PdfDictionary page)
        => ScanPage(reader, page).ByMcid;

    /// <summary>The artifacts of the page's content, in content order.</summary>
    public static List<ArtifactBlock> ArtifactsForPage(PdfReader reader, PdfDictionary page)
        => ScanPage(reader, page).Artifacts;

    private static ScanResult ScanPage(PdfReader reader, PdfDictionary page)
    {
        var streams = new List<PdfStream>();
        switch (reader.Resolve(page.Get("Contents")))
        {
            case PdfStream single:
                streams.Add(single);
                break;
            case PdfArray arr:
                foreach (var item in arr)
                    if (reader.ResolveStream(item) is { } s) streams.Add(s);
                break;
        }
        var fingerprint = Fingerprint(streams);
        lock (PageCache)
        {
            if (PageCache.TryGetValue(page, out var hit) && SameFingerprint(hit.Fingerprint, fingerprint))
                return hit.Result;
        }
        var result = Scan(reader, streams, InheritedResources(reader, page), PageContentScan.ShownMatrix(reader, page));
        lock (PageCache)
        {
            PageCache.Remove(page);
            PageCache.Add(page, new CacheEntry { Fingerprint = fingerprint, Result = result });
        }
        return result;
    }

    /// <summary>The pieces of a content stream named by a marked-content reference's /Stm
    /// (a form XObject), by MCID, placed by <paramref name="shown"/> - the matrix from the
    /// form's space to the shown page (<see cref="FormPlacement"/>) - or in the form's own
    /// space without one.</summary>
    public static Dictionary<int, List<Piece>> ForStream(PdfReader reader, PdfStream stream, double[]? shown = null)
        => ScanStream(reader, stream, shown).ByMcid;

    private static ScanResult ScanStream(PdfReader reader, PdfStream stream, double[]? shown)
    {
        var fingerprint = Fingerprint([stream]);
        lock (StreamCache)
        {
            if (StreamCache.TryGetValue(stream, out var hit) && SameFingerprint(hit.Fingerprint, fingerprint)
                && SameMatrix(hit.Shown, shown))
                return hit.Result;
        }
        var result = Scan(reader, [stream], reader.ResolveDict(stream.Dict.Get("Resources")), shown);
        lock (StreamCache)
        {
            StreamCache.Remove(stream);
            StreamCache.Add(stream, new CacheEntry { Fingerprint = fingerprint, Shown = shown, Result = result });
        }
        return result;
    }

    private static bool SameMatrix(double[]? a, double[]? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);

    /// <summary>The matrix from a form XObject's space to the shown page, when the page draws
    /// the form - directly, or through forms it draws: the form's /Matrix followed by the matrix
    /// at its Do. Null when the page does not draw it.</summary>
    public static double[]? FormPlacement(PdfReader reader, PdfDictionary page, PdfStream form)
        => Placement(reader, ScanPage(reader, page).Forms, form, 0);

    private static double[]? Placement(PdfReader reader, List<(PdfStream Form, double[] Ctm)> drawn, PdfStream form, int depth)
    {
        foreach (var (f, ctm) in drawn)
        {
            var placed = GraphicsState.MultiplyMatrices(PageContentScan.FormMatrix(reader, f), ctm);
            if (ReferenceEquals(f, form)) return placed;
            if (depth < PageContentScan.MaxFormDepth
                && Placement(reader, ScanStream(reader, f, placed).Forms, form, depth + 1) is { } inner)
                return inner;
        }
        return null;
    }

    /// <summary>An image drawn with <paramref name="m"/> as the current matrix: the unit square's
    /// corners through it bound the area it covers.</summary>
    private static Piece ImagePiece(string name, PdfStream stream, double[] m)
    {
        double[] xs = [m[4], m[0] + m[4], m[2] + m[4], m[0] + m[2] + m[4]];
        double[] ys = [m[5], m[1] + m[5], m[3] + m[5], m[1] + m[3] + m[5]];
        return new Piece
        {
            IsImage = true, ImageName = name, ImageStream = stream, ImageMatrix = m,
            Llx = Math.Min(Math.Min(xs[0], xs[1]), Math.Min(xs[2], xs[3])),
            Urx = Math.Max(Math.Max(xs[0], xs[1]), Math.Max(xs[2], xs[3])),
            Lly = Math.Min(Math.Min(ys[0], ys[1]), Math.Min(ys[2], ys[3])),
            Ury = Math.Max(Math.Max(ys[0], ys[1]), Math.Max(ys[2], ys[3])),
        };
    }

    private static object[] Fingerprint(List<PdfStream> streams)
    {
        var result = new object[streams.Count * 2];
        for (var i = 0; i < streams.Count; i++)
        {
            result[2 * i] = streams[i];
            result[2 * i + 1] = streams[i].RawData;
        }
        return result;
    }

    private static bool SameFingerprint(object[] a, object[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    /// <summary>/Resources of the page or, when absent, of the nearest ancestor in the page
    /// tree (PDF 32000-1 §7.7.3.4).</summary>
    private static PdfDictionary? InheritedResources(PdfReader reader, PdfDictionary page)
    {
        for (var node = page; node is not null; node = reader.ResolveDict(node.Get("Parent")))
            if (reader.ResolveDict(node.Get("Resources")) is { } resources)
                return resources;
        return null;
    }

    private static ScanResult Scan(PdfReader reader, List<PdfStream> streams, PdfDictionary? resources, double[]? shown)
    {
        var scan = new ScanResult();
        var result = scan.ByMcid;
        var fonts = new Dictionary<string, PdfDictionary>();
        if (resources is not null && reader.ResolveDict(resources.Get("Font")) is { } fontsDict)
            foreach (var key in fontsDict.Keys)
                if (reader.ResolveDict(fontsDict.Get(key)) is { } fd)
                    fonts[key] = fd;
        var xobjects = resources is not null ? reader.ResolveDict(resources.Get("XObject")) : null;
        var properties = resources is not null ? reader.ResolveDict(resources.Get("Properties")) : null;
        var colorSpaces = resources is not null ? reader.ResolveDict(resources.Get("ColorSpace")) : null;

        // The innermost enclosing MCID or artifact owns a piece; other frames push neither so
        // EMC pops balance.
        var stack = new List<(int? Mcid, ArtifactBlock? Artifact)>();
        (int? Mcid, ArtifactBlock? Artifact) Owner()
        {
            for (var i = stack.Count - 1; i >= 0; i--)
                if (stack[i].Mcid is not null || stack[i].Artifact is not null) return stack[i];
            return (null, null);
        }
        bool Owned() => Owner() is { Mcid: not null } or { Artifact: not null };
        void Add(Piece piece)
        {
            var (mcid, artifact) = Owner();
            if (artifact is not null)
            {
                piece.Mcid = -1;
                artifact.Pieces.Add(piece);
                return;
            }
            piece.Mcid = mcid!.Value;
            if (!result.TryGetValue(piece.Mcid, out var list)) result[piece.Mcid] = list = new List<Piece>();
            list.Add(piece);
        }

        var parser = new ContentStreamParser(reader);
        parser.OnMarkedContentBegin += (tag, props) =>
        {
            int? mcid = props?.Get("MCID") is PdfInteger m ? (int)m.Value : null;
            ArtifactBlock? artifact = null;
            if (tag == "Artifact" && mcid is null)
            {
                artifact = new ArtifactBlock { Type = props?.GetName("Type"), Subtype = props?.GetName("Subtype") };
                scan.Artifacts.Add(artifact);
            }
            stack.Add((mcid, artifact));
        };
        parser.OnMarkedContentEnd += () =>
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        };

        var decoder = new ShownTextDecoder(reader, fonts);
        Piece? open = null;
        parser.OnTextShown += (text, bytes, state) =>
        {
            open = null;
            if (!Owned()) return;
            PdfDictionary? fontDict = null;
            if (state.FontName is { } key) fonts.TryGetValue(key, out fontDict);
            text = decoder.Decode(state.FontName, bytes, text);
            var rm = PageContentScan.Shown(GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm), shown);
            var stroke = state.RenderingMode is 1 or 5;
            open = new Piece
            {
                Text = text, FontKey = state.FontName, FontDict = fontDict,
                Size = state.FontSize * Math.Sqrt(rm[2] * rm[2] + rm[3] * rm[3]),
                X0 = rm[4], X1 = rm[4], Y = rm[5], Y1 = rm[5],
                R = stroke ? state.StrokeR : state.FillR,
                G = stroke ? state.StrokeG : state.FillG,
                B = stroke ? state.StrokeB : state.FillB,
                RenderMode = state.RenderingMode,
                Angle = ((int)Math.Round(Math.Atan2(rm[1], rm[0]) * 180 / Math.PI) + 360) % 360,
            };
            Add(open);
        };
        parser.OnTextShownEnd += state =>
        {
            if (open is null) return;
            var end = PageContentScan.Shown(GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm), shown);
            (open.X1, open.Y1) = (end[4], end[5]);
            open = null;
        };
        parser.OnImageDrawn += (name, state) =>
        {
            var stream = xobjects is not null ? reader.ResolveStream(xobjects.Get(name)) : null;
            var m = PageContentScan.Shown(state.Ctm, shown);
            if (stream?.Dict.GetName("Subtype") == "Form")
            {
                scan.Forms.Add((stream, m));
                // A form drawn inside a marked sequence, not looked into, is a drawing of the sequence's.
                if (Owned() && PageContentScan.FormBox(reader, stream, m) is { } box)
                    Add(new Piece { IsDrawing = true, Llx = box.LLX, Lly = box.LLY, Urx = box.URX, Ury = box.URY });
            }
            else if (Owned() && stream?.Dict.GetName("Subtype") == "Image") Add(ImagePiece(name, stream, m));
        };
        parser.OnPathPainted += (op, state, cmds) =>
        {
            if (!Owned() || PageContentScan.PathBox(PageContentScan.Shown(state.Ctm, shown), cmds) is not { } box) return;
            // (painted in its stroke's colour when only stroked, else in its fill's)
            var stroked = op is "S" or "s";
            Add(new Piece
            {
                IsDrawing = true, Llx = box.LLX, Lly = box.LLY, Urx = box.URX, Ury = box.URY,
                R = stroked ? state.StrokeR : state.FillR, G = stroked ? state.StrokeG : state.FillG, B = stroked ? state.StrokeB : state.FillB,
            });
        };

        // A page's streams are one content stream split anywhere (§7.8.2): parsed joined.
        using var joined = new System.IO.MemoryStream();
        foreach (var s in streams)
        {
            try
            {
                var data = reader.DecodeStream(s);
                if (joined.Length > 0) joined.WriteByte((byte)'\n');
                joined.Write(data, 0, data.Length);
            }
            catch
            {
                // An undecodable stream paints nothing its marked content could report.
            }
        }
        try
        {
            parser.Parse(joined.ToArray(), fonts, colorSpaces: colorSpaces, properties: properties);
        }
        catch
        {
            // A stream the parser gives up on keeps what it read before the fault.
        }
        return scan;
    }
}
