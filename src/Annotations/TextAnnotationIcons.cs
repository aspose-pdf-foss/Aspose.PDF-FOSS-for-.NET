using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Standard note-icon appearance streams for /Text annotations
/// (PDF 32000 §12.5.6.4). The icon paths span a 20x20 unit box; the first
/// fill-colour op after the header (`1 g`) is the slot where the annotation's
/// /C colour goes — the icon body. Later `1 g` ops are genuine white (donut
/// holes, the question mark). Strokes stay black.</summary>
internal static class TextAnnotationIcons
{
    /// <summary>Side of the square BBox the icon streams are drawn in.</summary>
    internal const double BoxSize = 20;

    /// <summary>Stream text for the named icon with the annotation's /C colour
    /// substituted into the body-fill slot. Unknown/missing names fall back to
    /// the Note icon; a missing colour keeps the white body.</summary>
    internal static string ContentFor(string? name, (double R, double G, double B)? color)
    {
        if (name is null || !Streams.TryGetValue(name, out var content))
            content = Streams["Note"];
        if (color is { } c)
        {
            string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var slot = content.IndexOf("1 g", System.StringComparison.Ordinal);
            if (slot >= 0)
                content = content.Substring(0, slot)
                    + $"{F(c.R)} {F(c.G)} {F(c.B)} rg"
                    + content.Substring(slot + "1 g".Length);
        }
        return content;
    }

    /// <summary>Build the icon appearance as a Form XObject for a /Text
    /// annotation dictionary that carries no /AP — renderers map it onto the
    /// annotation /Rect like any other appearance form.</summary>
    internal static PdfStream BuildIconForm(PdfDictionary annot, PdfReader reader)
    {
        var content = ContentFor(annot.GetName("Name") ?? "Note", ReadColor(reader, annot.Get("C")));
        var form = new PdfStream(new PdfDictionary(), System.Text.Encoding.ASCII.GetBytes(content));
        form.Dict.Set("Type", new PdfName("XObject"));
        form.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(BoxSize)); bbox.Add(new PdfReal(BoxSize));
        form.Dict.Set("BBox", bbox);
        return form;
    }

    private static (double R, double G, double B)? ReadColor(PdfReader reader, PdfObject? o)
    {
        if (reader.Resolve(o) is not PdfArray a) return null;
        double Num(PdfObject? v) => v switch { PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0 };
        switch (a.Count)
        {
            case 1: { double v = Num(a[0]); return (v, v, v); }
            case 3: return (Num(a[0]), Num(a[1]), Num(a[2]));
            case 4:
                double c = Num(a[0]), m = Num(a[1]), y = Num(a[2]), k = Num(a[3]);
                return ((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k));
            default: return null;
        }
    }

    public static readonly System.Collections.Generic.Dictionary<string, string> Streams = new()
    {
        ["Check"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
0 i
0.59 w
4 M
0 J
1 0 0 1 7.1836 1.2061 cm
0 0 m
6.691 11.152 11.31 14.196 v
10.773 15.201 9.626 16.892 8.155 17.587 c
2.293 10.706 -0.255 4.205 y
-4.525 9.177 l
-6.883 5.608 l
h
b
Q",
        ["Circle"] = @"q
0 0 0 RG
0 0 0 rg
1 g
1 0 0 1 9.999 3.6387 cm
0 G
4 M
0.59 w
q
0 J
0 16.119 m
-5.388 16.119 -9.756 11.751 -9.756 6.363 c
-9.756 0.973 -5.388 -3.395 0 -3.395 c
5.391 -3.395 9.757 0.973 9.757 6.363 c
9.757 11.751 5.391 16.119 0 16.119 c
b
Q
1 g
0 J
0 0 m
-3.513 0 -6.36 2.85 -6.36 6.363 c
-6.36 9.875 -3.513 12.724 0 12.724 c
3.514 12.724 6.363 9.875 6.363 6.363 c
6.363 2.85 3.514 0 0 0 c
b
Q",
        ["Comment"] = @"q
0 0 0 RG
0 0 0 rg
1 g
20 0 0 20 0 0 cm
0.0625 0.0625 0.875 0.875 re
f
q
1 1 1 rg
0.2 0.3 0.6 0.5 re
f
0.5 0.3 m
0.35 0.15 l
0.35 0.3 l
h
f
Q
0.3 0.6 0.4 0.04 re
0.3 0.5 0.3 0.04 re
f
Q",
        ["Cross"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
0 i
0.59 w
4 M
0 J
1 0 0 1 18.6924 3.1357 cm
0 0 m
-6.363 6.364 l
0 12.728 l
-2.828 15.556 l
-9.192 9.192 l
-15.556 15.556 l
-18.384 12.728 l
-12.02 6.364 l
-18.384 0 l
-15.556 -2.828 l
-9.192 3.535 l
-2.828 -2.828 l
h
b
Q",
        ["Help"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0.59 w
q
0 G
0 i
4 M
0 J
1 0 0 1 12.1465 10.5137 cm
-2.146 9.403 m
-7.589 9.403 -12.001 4.99 -12.001 -0.453 c
-12.001 -5.895 -7.589 -10.309 -2.146 -10.309 c
3.296 -10.309 7.709 -5.895 7.709 -0.453 c
7.709 4.99 3.296 9.403 -2.146 9.403 c
h
B
Q
1 g
0 G
0 i
4 M
0 J
1 0 0 1 12.1465 10.5137 cm
0 0 m
-0.682 -0.756 -0.958 -1.472 -0.938 -2.302 c
-0.938 -2.632 l
-3.385 -2.632 l
-3.403 -2.154 l
-3.459 -1.216 -3.147 -0.259 -2.316 0.716 c
-1.729 1.433 -1.251 2.022 -1.251 2.647 c
-1.251 3.291 -1.674 3.715 -2.594 3.751 c
-3.202 3.751 -3.937 3.531 -4.417 3.2 c
-5.041 5.205 l
-4.361 5.591 -3.274 5.959 -1.968 5.959 c
0.46 5.959 1.563 4.616 1.563 3.089 c
1.563 1.691 0.699 0.771 0 0 c
-2.227 -6.863 m
-2.245 -6.863 l
-3.202 -6.863 -3.864 -6.146 -3.864 -5.189 c
-3.864 -4.196 -3.182 -3.516 -2.227 -3.516 c
-1.233 -3.516 -0.589 -4.196 -0.57 -5.189 c
-0.57 -6.146 -1.233 -6.863 -2.227 -6.863 c
b
Q",
        ["Insert"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
0 i
0.59 w
4 M
0 J
1 0 0 1 8.5386 19.8545 cm
0 0 m
-8.39 -19.719 l
8.388 -19.719 l
h
b
Q",
        ["Key"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
4 M
0 J
0 i
1 0 0 1 6.5 12.6729 cm
0.59 w
q
0.001 5.138 m
-2.543 5.138 -4.604 3.077 -4.604 0.534 c
-4.604 -1.368 -3.449 -3.001 -1.802 -3.702 c
-1.802 -4.712 l
-0.795 -5.719 l
-1.896 -6.82 l
-0.677 -8.039 l
-1.595 -8.958 l
-0.602 -9.949 l
-1.479 -10.829 l
-0.085 -12.483 l
1.728 -10.931 l
1.728 -3.732 l
1.737 -3.728 1.75 -3.724 1.76 -3.721 c
3.429 -3.03 4.604 -1.385 4.604 0.534 c
4.604 3.077 2.542 5.138 0.001 5.138 c
B
Q
1 g
0 0 m
-1.076 0 -1.95 0.874 -1.95 1.95 c
-1.95 3.028 -1.076 3.306 0 3.306 c
1.077 3.306 1.95 3.028 1.95 1.95 c
1.95 0.874 1.077 0 0 0 c
b
Q",
        ["NewParagraph"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
0 i
0.58 w
4 M
0 J
q
1 0 0 1 6.4995 20 cm
0 0 m
-6.205 -12.713 l
6.205 -12.713 l
h
b
Q
q
1 0 0 1 1.1909 6.2949 cm
0 0 m
1.278 0 l
1.353 0 1.362 -0.02 1.391 -0.066 c
2.128 -1.363 3.78 -4.275 3.966 -4.713 c
3.985 -4.713 l
3.976 -4.453 3.957 -3.91 3.957 -3.137 c
3.957 -0.076 l
3.957 -0.02 3.976 0 4.041 0 c
4.956 0 l
5.021 0 5.04 -0.029 5.04 -0.084 c
5.04 -6.049 l
5.04 -6.113 5.021 -6.133 4.947 -6.133 c
3.695 -6.133 l
3.621 -6.133 3.611 -6.113 3.574 -6.066 c
3.052 -4.955 1.353 -2.063 0.971 -1.186 c
0.961 -1.186 l
0.999 -1.68 0.999 -2.146 1.008 -3.025 c
1.008 -6.049 l
1.008 -6.104 0.989 -6.133 0.933 -6.133 c
0.009 -6.133 l
-0.046 -6.133 -0.075 -6.123 -0.075 -6.049 c
-0.075 -0.066 l
-0.075 -0.02 -0.056 0 0 0 c
f
Q
q
1 0 0 1 9.1367 3.0273 cm
0 0 m
0.075 0 0.215 -0.008 0.645 -0.008 c
1.4 -0.008 2.119 0.281 2.119 1.213 c
2.119 1.969 1.633 2.381 0.737 2.381 c
0.354 2.381 0.075 2.371 0 2.361 c
h
-1.146 3.201 m
-1.146 3.238 -1.129 3.268 -1.082 3.268 c
-0.709 3.275 0.02 3.285 0.729 3.285 c
2.613 3.285 3.248 2.314 3.258 1.232 c
3.258 -0.27 2.007 -0.914 0.607 -0.914 c
0.327 -0.914 0.057 -0.914 0 -0.904 c
0 -2.789 l
0 -2.836 -0.019 -2.865 -0.074 -2.865 c
-1.082 -2.865 l
-1.119 -2.865 -1.146 -2.846 -1.146 -2.799 c
h
f
Q
Q",
        ["Note"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
0 i
0.61 w
4 M
0 J
q
1 0 0 1 16.959 1.3672 cm
0 0 m
0 -0.434 -0.352 -0.785 -0.784 -0.785 c
-14.911 -0.785 l
-15.345 -0.785 -15.696 -0.434 -15.696 0 c
-15.696 17.266 l
-15.696 17.699 -15.345 18.051 -14.911 18.051 c
-0.784 18.051 l
-0.352 18.051 0 17.699 0 17.266 c
h
b
Q
q
1 0 0 1 4.4023 13.9243 cm
0 0 m
9.418 0 l
S
Q
q
1 0 0 1 4.4019 11.2207 cm
0 0 m
9.418 0 l
S
Q
q
1 0 0 1 4.4023 8.5176 cm
0 0 m
9.418 0 l
S
Q
q
1 0 0 1 4.4023 5.8135 cm
0 0 m
9.418 0 l
S
Q
Q",
        ["Paragraph"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 i
4 M
0 J
0 G
0.59 w
q
1 0 0 1 11.6787 2.6582 cm
0 0 m
-1.141 0 l
-1.227 0 -1.244 0.052 -1.227 0.139 c
-0.656 1.157 -0.52 2.505 -0.52 3.317 c
-0.52 3.594 l
-2.833 3.783 -5.441 4.838 -5.441 8.309 c
-5.441 10.778 -3.714 12.626 -0.57 13.024 c
-0.535 13.508 -0.381 14.129 -0.242 14.389 c
-0.207 14.44 -0.174 14.475 -0.104 14.475 c
1.088 14.475 l
1.156 14.475 1.191 14.458 1.175 14.372 c
1.105 14.095 0.881 13.127 0.881 12.402 c
0.881 9.431 0.932 7.324 0.95 4.06 c
0.95 2.298 0.708 0.813 0.189 0.07 c
0.155 0.034 0.103 0 0 0 c
b
Q
1 g
1 0 0 1 19.6973 10.0005 cm
0 0 m
0 -5.336 -4.326 -9.662 -9.663 -9.662 c
-14.998 -9.662 -19.324 -5.336 -19.324 0 c
-19.324 5.335 -14.998 9.662 -9.663 9.662 c
-4.326 9.662 0 5.335 0 0 c
h
S
Q",
        ["Star"] = @"q
0 0 0 RG
0 0 0 rg
1 g
0 G
0 i
0.59 w
4 M
0 J
1 0 0 1 9.999 18.8838 cm
0 0 m
3.051 -6.178 l
9.867 -7.168 l
4.934 -11.978 l
6.099 -18.768 l
0 -15.562 l
-6.097 -18.768 l
-4.933 -11.978 l
-9.866 -7.168 l
-3.048 -6.178 l
b
Q",
    };
}
