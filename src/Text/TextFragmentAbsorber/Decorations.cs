using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>
    /// Maps character offsets in concatenated text back to source RawTextRun entries
    /// to compute bounding rectangles for search matches.
    /// Three phases: (1) build char→run index, (2) find regex/phrase matches, (3) build fragments.
    /// </summary>
    // Detect a horizontal underline drawn as a thin filled rectangle just below the
    // fragment's baseline. Used by SearchForTextRelatedGraphics. PDF producers commonly
    // emit underlines as `x y w h re f*` after the Tj/TJ that placed the text — these
    // rects are short, just below the baseline, and span (approximately) the run's width.
    private static RawFillRect? DetectUnderlineRect(Rectangle rect, double baselineY,
        double fontSize, FillRectIndex fillRects)
    {
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return null;
        var maxThickness = Math.Max(1.5, 0.15 * fontSize);
        var maxGap = Math.Max(2.5, 0.4 * fontSize);
        // A match has Ury in [baselineY - maxGap, baselineY + 0.5]; a thin rect's midpoint
        // sits within Margin of that band.
        return fillRects.FindTopMatch(baselineY - maxGap - FillRectIndex.Margin, baselineY + 0.5, fr =>
        {
            var h = fr.Ury - fr.Lly;
            if (h > maxThickness) return false;
            if (fr.Ury > baselineY + 0.5) return false;
            if (fr.Ury < baselineY - maxGap) return false;
            // A rule far wider than the run AND sitting deep below the baseline (past the
            // descent band) is page graphics — a table border or column rule under the
            // whole line — not this fragment's underline. Width alone can't discriminate
            // (a phrase underline legitimately spans many word fragments), so both
            // conditions must hold.
            if (fr.Urx - fr.Llx > fragWidth * 2 + 4 && fr.Ury < baselineY - Math.Max(1.8, 0.2 * fontSize)) return false;
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) return false;
            return true;
        });
    }

    // Detect a background highlight: a filled rect tall enough to cover the glyph body
    // (not a thin underline/strikeout bar) that spans the fragment horizontally and
    // vertically encloses its baseline band. Captured under ToAttemptGetUnderlineFromSource
    // so a text replacement can splice the old highlight out and re-draw it at the new
    // advance.
    private static RawFillRect? DetectBackgroundRect(Rectangle rect, double baselineY,
        double fontSize, FillRectIndex fillRects)
    {
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return null;
        var minThickness = Math.Max(2.0, 0.5 * fontSize);
        // A match straddles the baseline (Lly ≤ baselineY ≤ Ury − 0.3·fontSize). Taller
        // matches are caught by the index's always-tested tall list; a thin one (rare, only
        // for tiny fonts) has its midpoint within Margin of the band.
        return fillRects.FindTopMatch(baselineY - FillRectIndex.Margin, baselineY + 0.3 * fontSize + FillRectIndex.Margin, fr =>
        {
            if (fr.Ury - fr.Lly < minThickness) return false;      // thin bar = underline/strikeout
            if (fr.Lly > baselineY || fr.Ury < baselineY + 0.3 * fontSize) return false; // must straddle the glyph band
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) return false;
            return true;
        });
    }

    // Detect a strikethrough: a thin filled rect crossing the fragment's glyph body
    // (centre roughly 0.15–0.55·fontSize above the baseline — through the x-height),
    // spanning most of the run's width. Unlike an underline this sits ON the text, not
    // below it. Used to surface TextState.StrikeOut during extraction.
    private static RawFillRect? DetectStrikeoutRect(Rectangle rect, double baselineY,
        double fontSize, FillRectIndex fillRects)
    {
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return null;
        var maxThickness = Math.Max(1.5, 0.15 * fontSize);
        var loY = baselineY + 0.12 * fontSize;
        var hiY = baselineY + 0.58 * fontSize;
        // The predicate keys on the rect's midpoint (cy ∈ [loY, hiY]), which is exactly the
        // index key — the slice bounds are the band itself.
        return fillRects.FindTopMatch(loY, hiY, fr =>
        {
            var h = fr.Ury - fr.Lly;
            if (h > maxThickness) return false;
            var cy = (fr.Lly + fr.Ury) / 2;
            if (cy < loY || cy > hiY) return false;
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) return false;
            return true;
        });
    }

    /// <summary>Text rotation in degrees from the baseline direction vector (the
    /// text-space x-axis mapped through the text matrix and CTM), measured CCW
    /// from the page x-axis and normalised to [0, 360). Axis-aligned text yields
    /// exactly 0/90/180/270; arbitrary text matrices report their true angle.
    /// Returns null for a degenerate (zero-length) direction.</summary>
    private static double? RotationFromDirection(double tdx, double tdy)
    {
        if (Math.Abs(tdx) <= 1e-9 && Math.Abs(tdy) <= 1e-9) return null;
        var rot = Math.Atan2(tdy, tdx) * 180.0 / Math.PI;
        if (rot < 0) rot += 360.0;
        var snapped = Math.Round(rot);
        if (Math.Abs(rot - snapped) < 1e-6) rot = snapped >= 360 ? 0 : snapped;
        return rot;
    }

    /// <summary>Standard symbol faces (Symbol, ZapfDingbats — any subset/style
    /// variant) decode through their built-in encodings, so the strict TrueType
    /// validation must not reject them.</summary>
    private static bool IsStandardSymbolFamily(string? baseFont)
    {
        if (string.IsNullOrEmpty(baseFont)) return false;
        var name = baseFont!;
        var plus = name.IndexOf('+');
        if (plus >= 0 && plus < name.Length - 1) name = name[(plus + 1)..];
        return name.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ZapfDingbats", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the font's embedded TrueType program offers ONLY a
    /// symbolic (3,0) cmap subtable — no Mac (1,0) or Windows (3,1) map that a
    /// text extractor could decode through. Missing/unreadable programs count as
    /// symbol-only (nothing to decode with).</summary>
    private static bool HasOnlySymbolCmap(PdfDictionary fontDict, PdfReader reader)
    {
        try
        {
            var desc = reader.ResolveDict(fontDict.Get("FontDescriptor"));
            var ff = desc is null ? null : reader.ResolveStream(desc.Get("FontFile2"));
            if (ff is null) return true;
            var ttf = reader.DecodeStream(ff);
            if (ttf.Length < 12) return true;
            int numTables = (ttf[4] << 8) | ttf[5];
            for (var i = 0; i < numTables; i++)
            {
                var off = 12 + i * 16;
                if (off + 16 > ttf.Length) break;
                if (ttf[off] != 'c' || ttf[off + 1] != 'm' || ttf[off + 2] != 'a' || ttf[off + 3] != 'p') continue;
                var toff = (ttf[off + 8] << 24) | (ttf[off + 9] << 16) | (ttf[off + 10] << 8) | ttf[off + 11];
                if (toff + 4 > ttf.Length) return true;
                int n = (ttf[toff + 2] << 8) | ttf[toff + 3];
                for (var j = 0; j < n; j++)
                {
                    var rec = toff + 4 + j * 8;
                    if (rec + 8 > ttf.Length) break;
                    int pid = (ttf[rec] << 8) | ttf[rec + 1];
                    int eid = (ttf[rec + 2] << 8) | ttf[rec + 3];
                    if (pid == 1 || (pid == 3 && eid != 0) || pid == 0)
                        return false; // a decodable subtable exists
                }
                return true; // cmap present but symbol-only
            }
            return true; // no cmap at all
        }
        catch { return true; }
    }

    /// <summary>True when consecutive content runs jump UP by more than ~3 inches —
    /// the marker of a stream whose drawing order departs from reading order.</summary>
    private static bool HasMajorUpwardJump(List<RawTextRun> runs)
    {
        var prevY = double.NaN;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0 || run.Text[0] == '\r' || run.Text[0] == '\n') continue;
            var (_, y) = ApplyCtm(run.X, run.Y, run.Ctm);
            if (!double.IsNaN(prevY) && y > prevY + 200.0) return true;
            prevY = y;
        }
        return false;
    }

    /// <summary>Reading-order permutation of the run list for Flatten-mode search:
    /// rows form by viewer-space baseline Y (top first, 2 pt band), runs within a
    /// row order left-to-right, and a line-break sentinel separates rows. Every
    /// downstream consumer indexes the same reordered list, so match→run mapping
    /// and geometry are untouched.</summary>
    private List<RawTextRun> ReorderRunsForFlatten(List<RawTextRun> runs)
    {
        var items = new List<(RawTextRun run, double y, double x)>();
        foreach (var r in runs)
        {
            if (r.Text == "\r\n" || r.Text.Length == 0) continue;
            var (px, py) = ApplyCtm(r.X, r.Y, r.Ctm);
            items.Add((r, py, px));
        }
        if (items.Count == 0) return runs;
        items.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        var result = new List<RawTextRun>(runs.Count);
        var i = 0;
        while (i < items.Count)
        {
            var rowY = items[i].y;
            var row = new List<(RawTextRun run, double y, double x)>();
            while (i < items.Count && rowY - items[i].y <= 2.0) { row.Add(items[i]); i++; }
            row.Sort((a, b) => a.x.CompareTo(b.x));
            if (result.Count > 0)
            {
                var f = row[0].run;
                result.Add(new RawTextRun("\r\n", f.X, f.Y, f.FontSize, f.FontName, 0, f.Ctm, f.Metrics));
            }
            foreach (var t in row) result.Add(t.run);
        }
        return result;
    }

    /// <summary>
    /// Concatenates text from raw runs into a single searchable string, inserting
    /// spaces at detected word gaps, removing false newlines at BT/ET boundaries,
    /// and applying bidi reordering + Arabic normalization for phrase search.
    /// </summary>
    /// <summary>PDF text rendering mode 3 — neither fill nor stroke: invisible
    /// text, the mode OCR overlays draw their recognition layer with.</summary>
    private const int InvisibleTextRenderMode = 3;

    /// <summary>
    /// The visible foreground colour of a run. Stroke-only text rendering modes (1 and 5)
    /// paint the glyph outline in the stroking colour and never use the fill colour, so the
    /// stroking colour is the foreground there; every other mode is fill-based.
    /// </summary>
    private static Color ForegroundColorOf(RawTextRun run)
    {
        if ((run.RenderingMode == 1 || run.RenderingMode == 5) && run.StrokingColor is { } sc)
            return sc;
        return run.FillColor ?? Color.Black;
    }

    /// <summary>Map each nameless Type3 font resource key in <paramref name="resourceDict"/>'s
    /// /Font to its synthesised "T3Font_&lt;n&gt;" handle, indexed by /Font enumeration order —
    /// the same assignment <see cref="FontCollection"/> makes, so the absorber's per-fragment
    /// font name agrees with the resource-collection view.</summary>
    private static Dictionary<string, string> BuildType3SynthesizedNames(
        PdfDictionary? resourceDict, PdfReader reader)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resourceDict is null) return map;
        // resourceDict is a page/form dict (fonts under /Resources/Font, possibly inherited
        // from an ancestor page) or already a /Resources dict (fonts under /Font). Resolve
        // the effective /Font the same way the FontCollection does so the Keys enumeration
        // order — and thus the T3Font_<n> index — matches the resource-collection view.
        var fontDict = ResolveEffectiveFontDict(resourceDict, reader);
        if (fontDict is null) return map;
        var t3 = 0;
        foreach (var key in fontDict.Keys)
        {
            var fd = reader.ResolveDict(fontDict.Get(key));
            if (fd is not null && fd.GetName("Subtype") == "Type3" && fd.GetName("BaseFont") is null)
                map[key] = $"T3Font_{t3++}";
        }
        return map;
    }

    /// <summary>Resolve the effective /Font dictionary for a page/form/resource dict: its own
    /// /Font (already a resource dict), else /Resources/Font, else the nearest ancestor page's
    /// /Resources/Font via the /Parent chain (inheritable per PDF 32000 §7.7.3.4).</summary>
    private static PdfDictionary? ResolveEffectiveFontDict(PdfDictionary dict, PdfReader reader)
    {
        var direct = reader.ResolveDict(dict.Get("Font"));
        if (direct is not null) return direct;
        var res = reader.ResolveDict(dict.Get("Resources"));
        var f = res is null ? null : reader.ResolveDict(res.Get("Font"));
        if (f is not null) return f;
        var parentObj = dict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            var pres = reader.ResolveDict(parent.Get("Resources"));
            var pf = pres is null ? null : reader.ResolveDict(pres.Get("Font"));
            if (pf is not null) return pf;
            parentObj = parent.Get("Parent");
        }
        return null;
    }
}
