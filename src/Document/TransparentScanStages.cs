using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
// A step of the transparent-paint scan.
    // Blend a colour toward the white backdrop at alpha a, and format the ops
    // that set it and later restore the original.
    private static string ColOps(( int comps, double[] vals) col, double a, bool stroke, bool blendIt)
    {
        // Flattened constant-alpha paint reads slightly lighter than the
        // plain over-white blend (on nested 0.5×0.3 map strokes:
        // ≈ 218–219/255 vs the plain blend's 217/255) — bias the
        // effective alpha to match.
        var ae = a * 0.91;
        var v = new double[col.vals.Length];
        for (var i = 0; i < v.Length; i++)
            v[i] = !blendIt ? col.vals[i]
                : col.comps == 4 ? col.vals[i] * ae          // cmyk: white = 0
                : 1 - (1 - col.vals[i]) * ae;                // gray/rgb: white = 1
        var op = col.comps switch
        {
            1 => stroke ? "G" : "g",
            4 => stroke ? "K" : "k",
            _ => stroke ? "RG" : "rg",
        };
        var sb = new System.Text.StringBuilder();
        foreach (var x in v)
            sb.Append(x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
        return sb.Append(op).ToString();
    }

    private static void AddPoint(TransparentScanState tp, double x, double y)
    {
        var (dx, dy) = tp.ctm.Apply(x, y);
        if (dx < tp.pMinX) tp.pMinX = dx;
        if (dy < tp.pMinY) tp.pMinY = dy;
        if (dx > tp.pMaxX) tp.pMaxX = dx;
        if (dy > tp.pMaxY) tp.pMaxY = dy;
        tp.hasPath = true;
    }

    private static void ClearPath(TransparentScanState tp)
    {
        tp.pMinX = tp.pMinY = double.MaxValue;
        tp.pMaxX = tp.pMaxY = double.MinValue;
        tp.hasPath = false;
    }

    private static bool Transparent(TransparentScanState tp) => (tp.fillA > 0 && tp.fillA < 1) || (tp.strokeA > 0 && tp.strokeA < 1)
                          || (tp.blend != "Normal" && tp.blend != "Compatible")
                          || tp.softMask;

    /// <summary>The state operators of the scan: the q/Q stack, the CTM, the ExtGState alpha and blend, the text font and matrix.</summary>
    private void ScanStateOperator(TransparentScanState tp, double fillScale, double strokeScale, string token)
    {
        switch (token)
        {
            case "q":
                tp.stack.Push((tp.ctm, tp.fillA, tp.strokeA, tp.blend, tp.softMask, tp.fillCol, tp.strokeCol));
                break;
            case "Q":
                if (tp.stack.Count > 0) (tp.ctm, tp.fillA, tp.strokeA, tp.blend, tp.softMask, tp.fillCol, tp.strokeCol) = tp.stack.Pop();
                break;
            case "cm":
                if (tp.nums.Count >= 6)
                    tp.ctm = new SimMatrix(tp.nums[^6], tp.nums[^5], tp.nums[^4], tp.nums[^3], tp.nums[^2], tp.nums[^1]).Concat(tp.ctm);
                break;
            case "gs" when tp.lastName is not null:
                if (tp.gsInfo.TryGetValue(tp.lastName, out var info))
                {
                    if (info.Ca is { } caV) tp.fillA = fillScale * caV;
                    if (info.CA is { } cAV) tp.strokeA = strokeScale * cAV;
                    if (info.Bm is { } bmV) tp.blend = bmV;
                    if (info.Sm is { } smV) tp.softMask = smV;
                }
                break;
            case "Tf":
                if (tp.nums.Count >= 1) tp.fontSize = tp.nums[^1];
                break;
            case "Tm":
                if (tp.nums.Count >= 6) { tp.tmX = tp.nums[^2]; tp.tmY = tp.nums[^1]; }
                break;
        }
    }

    /// <summary>The colour operators: the fill and stroke colour the transparent paint is simulated from.</summary>
    private void ScanColorOperator(TransparentScanState tp, string token)
    {
        switch (token)
        {
            case "g" when tp.nums.Count >= 1:
                tp.fillCol = (1, new[] { tp.nums[^1] });
                break;
            case "G" when tp.nums.Count >= 1:
                tp.strokeCol = (1, new[] { tp.nums[^1] });
                break;
            case "rg" when tp.nums.Count >= 3:
                tp.fillCol = (3, new[] { tp.nums[^3], tp.nums[^2], tp.nums[^1] });
                break;
            case "RG" when tp.nums.Count >= 3:
                tp.strokeCol = (3, new[] { tp.nums[^3], tp.nums[^2], tp.nums[^1] });
                break;
            case "k" when tp.nums.Count >= 4:
                tp.fillCol = (4, new[] { tp.nums[^4], tp.nums[^3], tp.nums[^2], tp.nums[^1] });
                break;
            case "K" when tp.nums.Count >= 4:
                tp.strokeCol = (4, new[] { tp.nums[^4], tp.nums[^3], tp.nums[^2], tp.nums[^1] });
                break;
            // sc/scn operate in the CURRENT colour space — a 1-component
            // Separation TINT is not a gray (tint 1 = full ink, gray 1 =
            // white), and ICC/Lab components aren't device values either.
            // Mark the colour unknown so such paints take the raster-region
            // fallback instead of a mis-recoloured vector.
            case "sc" or "scn":
                tp.fillCol = null;
                break;
            case "SC" or "SCN":
                tp.strokeCol = null;
                break;
        }
    }

    /// <summary>The path operators: points collected, and a painted path replaced by its simulated opaque colour.</summary>
    /// <returns>The operator's replacement text, or null when the token is written through unchanged.</returns>
    private string? ScanPathOperator(TransparentScanState tp, List<double[]> regions, double fillScale, double strokeScale, bool recolor, string token)
    {
        string? replacement = null;
        switch (token)
        {
            case "re":
                if (tp.nums.Count >= 4)
                {
                    var (rx, ry, rw, rh) = (tp.nums[^4], tp.nums[^3], tp.nums[^2], tp.nums[^1]);
                    AddPoint(tp, rx, ry);
                    AddPoint(tp, rx + rw, ry);
                    AddPoint(tp, rx, ry + rh);
                    AddPoint(tp, rx + rw, ry + rh);
                }
                break;
            case "m" or "l":
                if (tp.nums.Count >= 2) AddPoint(tp, tp.nums[^2], tp.nums[^1]);
                break;
            case "c":
                if (tp.nums.Count >= 6)
                {
                    AddPoint(tp, tp.nums[^6], tp.nums[^5]);
                    AddPoint(tp, tp.nums[^4], tp.nums[^3]);
                    AddPoint(tp, tp.nums[^2], tp.nums[^1]);
                }
                break;
            case "v" or "y":
                if (tp.nums.Count >= 4)
                {
                    AddPoint(tp, tp.nums[^4], tp.nums[^3]);
                    AddPoint(tp, tp.nums[^2], tp.nums[^1]);
                }
                break;
            case "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "S" or "s":
                if (Transparent(tp))
                {
                    var usesFill = token is not ("S" or "s");
                    var usesStroke = token is "S" or "s" or "B" or "B*" or "b" or "b*";
                    if (recolor)
                    {
                        // Recolour (Mask) mode applies ONLY to alpha inherited
                        // through a transparency-GROUP form (the Illustrator
                        // map shape) with Normal blend and device colours.
                        // Everything else — page-level alpha, spot colours,
                        // blend modes — keeps the LEGACY Mask behaviour: the
                        // paint is left alone and the neutralisation pass
                        // opaques it, exactly as before this mode existed.
                        var groupScaled = fillScale < 1 || strokeScale < 1;
                        if (groupScaled && tp.blend is "Normal" or "Compatible"
                            && (!usesFill || tp.fillCol is not null)
                            && (!usesStroke || tp.strokeCol is not null))
                        {
                            var pre = new System.Text.StringBuilder();
                            var post = new System.Text.StringBuilder();
                            if (usesFill && tp.fillA < 1)
                            {
                                pre.Append(ColOps(tp.fillCol!.Value, tp.fillA, stroke: false, blendIt: true)).Append(' ');
                                post.Append(' ').Append(ColOps(tp.fillCol.Value, 1, stroke: false, blendIt: false));
                            }
                            if (usesStroke && tp.strokeA < 1)
                            {
                                pre.Append(ColOps(tp.strokeCol!.Value, tp.strokeA, stroke: true, blendIt: true)).Append(' ');
                                post.Append(' ').Append(ColOps(tp.strokeCol.Value, 1, stroke: true, blendIt: false));
                            }
                            if (pre.Length > 0)
                            {
                                replacement = pre.ToString() + token + post;
                                tp.changed = true;
                            }
                        }
                    }
                    else
                    {
                        if (tp.hasPath)
                            regions.Add(new[] { tp.pMinX, tp.pMinY, tp.pMaxX, tp.pMaxY });
                        replacement = "n";
                        tp.changed = true;
                    }
                }
                ClearPath(tp);
                break;
            case "n":
                ClearPath(tp);
                break;
        }
        return replacement;
    }

    /// <summary>A form XObject scanned in its own matrix, its rewrite recorded for the save.</summary>
    private void ScanXObjectOperator(TransparentScanState tp, PdfDictionary resources, List<double[]> regions, List<(PdfStream form, byte[] bytes)> formRewrites, HashSet<PdfStream> visited, double fillScale, double strokeScale, bool recolor, string token)
    {
        switch (token)
        {
            case "Do" when tp.lastName is not null && recolor:
                // Recurse into Form XObjects: page art wrapped in a form keeps
                // its transparent paints in the FORM's stream, invoked either
                // under a page-level alpha or with the alpha in the form's own
                // ExtGState. Regions land in device space via the composed CTM;
                // the rewritten form bytes are deferred to formRewrites.
                {
                    var xoDict = _reader.ResolveDict(resources.Get("XObject"));
                    var xs = xoDict is null ? null : _reader.ResolveStream(xoDict.Get(tp.lastName));
                    if (xs is not null && xs.Dict.GetName("Subtype") == "Form" && visited.Add(xs))
                    {
                        byte[] formData;
                        try { formData = _reader.DecodeStream(xs); }
                        catch { formData = Array.Empty<byte>(); }
                        if (formData.Length > 0)
                        {
                            var fres = _reader.ResolveDict(xs.Dict.Get("Resources")) ?? resources;
                            var fm = tp.ctm;
                            if (_reader.Resolve(xs.Dict.Get("Matrix")) is PdfArray fmArr && fmArr.Count == 6)
                                fm = new SimMatrix(Num(fmArr[0]), Num(fmArr[1]), Num(fmArr[2]),
                                    Num(fmArr[3]), Num(fmArr[4]), Num(fmArr[5])).Concat(tp.ctm);
                            // A /Group form composites its RESULT at the alpha
                            // active here, so its inner gs values scale by it; a
                            // non-group form shares this content's group context.
                            var isGroup = xs.Dict.Get("Group") is not null;
                            var inner = ScanTransparentPaintsCore(formData, fres, regions,
                                formRewrites, fm, tp.fillA, tp.strokeA, tp.blend, visited,
                                isGroup ? tp.fillA : fillScale, isGroup ? tp.strokeA : strokeScale, recolor);
                            if (inner is not null) formRewrites.Add((xs, inner));
                        }
                    }
                }
                break;
        }
    }

    /// <summary>The scan's starting state for one content stream: its text, output, matrices, alphas, colours and path box.</summary>
    private void OpenTransparentScan(TransparentScanState tp, byte[] contentBytes, SimMatrix ctm0, double fillA0, double strokeA0, string blend0)
    {
        tp.text = Compat.Latin1.GetString(contentBytes);
        tp.output = new System.Text.StringBuilder(tp.text.Length);
        tp.stack = new Stack<(SimMatrix Ctm, double FillA, double StrokeA, string Bm, bool Sm,
            (int comps, double[] vals)? FillCol, (int comps, double[] vals)? StrokeCol)>();
        tp.ctm = ctm0;
        tp.fillA = fillA0;
        tp.strokeA = strokeA0;
        tp.blend = blend0;
        tp.softMask = false;
        tp.fillCol = (1, new[] { 0.0 });
        tp.strokeCol = (1, new[] { 0.0 });

        tp.lastName = null;
        tp.nums = new List<double>(8);
        tp.changed = false;
        tp.pos = 0;

        tp.pMinX = double.MaxValue;
        tp.pMinY = double.MaxValue;
        tp.pMaxX = double.MinValue;
        tp.pMaxY = double.MinValue;
        tp.hasPath = false;
        tp.tmX = 0;
        tp.tmY = 0;
        tp.fontSize = 0;
        tp.lastStringLen = 0;
    }

    /// <summary>One token of the content: a number kept, a name remembered, an operator dispatched to its family and its replacement written.</summary>
    private bool ScanTransparentToken(TransparentScanState tp, PdfDictionary resources, List<double[]> regions, List<(PdfStream form, byte[] bytes)> formRewrites, HashSet<PdfStream> visited, double fillScale, double strokeScale, bool recolor)
    {
        var c = tp.text[tp.pos];
        if (char.IsWhiteSpace(c) || c is '[' or ']' or '{' or '}')
        { tp.output.Append(c); tp.pos++; return true; }
        if (c == '%')
        {
            var eol = tp.pos;
            while (eol < tp.text.Length && tp.text[eol] != '\n' && tp.text[eol] != '\r') eol++;
            tp.output.Append(tp.text, tp.pos, eol - tp.pos);
            tp.pos = eol;
            return true;
        }
        if (CopyStringOrHexToken(tp, c)) return true;
        if (c == '>' && tp.pos + 1 < tp.text.Length && tp.text[tp.pos + 1] == '>')
        { tp.output.Append(">>"); tp.pos += 2; return true; }
        if (c == '/')
        {
            var end = tp.pos + 1;
            while (end < tp.text.Length && !char.IsWhiteSpace(tp.text[end])
                   && tp.text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                end++;
            tp.lastName = tp.text[(tp.pos + 1)..end];
            tp.output.Append(tp.text, tp.pos, end - tp.pos);
            tp.pos = end;
            return true;
        }

        {
            var end = tp.pos;
            while (end < tp.text.Length && !char.IsWhiteSpace(tp.text[end])
                   && tp.text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                end++;
            var token = tp.text[tp.pos..end];

            if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                tp.nums.Add(num);
                tp.output.Append(token);
                tp.pos = end;
                return true;
            }

            string? replacement = null;
            switch (token)
            {
                case "BI":
                    tp.bailOut = true; // inline image: bail out, keep the page as-is
                    return false;
                case "q": case "Q": case "cm": case "gs": case "Tf": case "Tm":
                    ScanStateOperator(tp, fillScale, strokeScale, token);
                    break;
                case "g": case "G": case "rg": case "RG": case "k": case "K": case "sc": case "scn": case "SC": case "SCN":
                    ScanColorOperator(tp, token);
                    break;
                case "re": case "m": case "l": case "c": case "v": case "y": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "S": case "s": case "n":
                    replacement = ScanPathOperator(tp, regions, fillScale, strokeScale, recolor, token);
                    break;
                case "Tj" or "TJ" or "'" or "\"":
                    // Recolour (Mask) mode never rasterises: transparent text keeps
                    // the legacy Mask behaviour (neutralised opaque, as always).
                    if (Transparent(tp) && tp.fontSize > 0 && !recolor)
                    {
                        var (dx0, dy0) = tp.ctm.Apply(tp.tmX, tp.tmY - 0.3 * tp.fontSize);
                        var (dx1, dy1) = tp.ctm.Apply(tp.tmX + Math.Max(1, tp.lastStringLen) * 0.55 * tp.fontSize, tp.tmY + tp.fontSize);
                        regions.Add(new[]
                        {
                            Math.Min(dx0, dx1), Math.Min(dy0, dy1),
                            Math.Max(dx0, dx1), Math.Max(dy0, dy1),
                        });
                    }
                    break;
                case "Do":
                    ScanXObjectOperator(tp, resources, regions, formRewrites, visited, fillScale, strokeScale, recolor, token);
                    break;
            }
            tp.nums.Clear();
            tp.output.Append(replacement ?? token);
            tp.pos = end;
        }
        return true;
    }

    /// <summary>A string or hex token is copied through untouched, its length remembered for the text-matrix advance.</summary>
    private bool CopyStringOrHexToken(TransparentScanState tp, char c)
    {
        if (c == '(')
        {
            var end = tp.pos + 1;
            var depth = 1;
            while (end < tp.text.Length && depth > 0)
            {
                var sc = tp.text[end];
                if (sc == '\\') end++;
                else if (sc == '(') depth++;
                else if (sc == ')') depth--;
                end++;
            }
            tp.lastStringLen = Math.Max(0, end - tp.pos - 2);
            tp.output.Append(tp.text, tp.pos, end - tp.pos);
            tp.pos = end;
            return true;
        }
        if (c == '<')
        {
            if (tp.pos + 1 < tp.text.Length && tp.text[tp.pos + 1] == '<')
            { tp.output.Append("<<"); tp.pos += 2; return true; }
            var end = tp.text.IndexOf('>', tp.pos + 1);
            if (end < 0) end = tp.text.Length - 1;
            tp.lastStringLen = Math.Max(0, (end - tp.pos - 1) / 2);
            tp.output.Append(tp.text, tp.pos, end - tp.pos + 1);
            tp.pos = end + 1;
            return true;
        }
        return false;
    }
}
