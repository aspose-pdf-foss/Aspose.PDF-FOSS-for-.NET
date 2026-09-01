using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class Annotation
{
    /// <summary>Read the border width as an int for the write-through <see cref="Border.Width"/>
    /// accessor: /BS /W, else /Border[2], else the default 1.</summary>
    internal int GetBorderWidthValue()
    {
        var bs = _reader.ResolveDict(_dict.Get("BS"));
        if (bs?.Get("W") is PdfInteger bi) return (int)bi.Value;
        if (bs?.Get("W") is PdfReal br) return (int)System.Math.Round(br.Value);
        if (_reader.Resolve(_dict.Get("Border")) is PdfArray arr && arr.Count >= 3)
        {
            if (arr[2] is PdfInteger ai) return (int)ai.Value;
            if (arr[2] is PdfReal ar) return (int)System.Math.Round(ar.Value);
        }
        return 1;
    }

    /// <summary>Persist the border width to both /Border ([0 0 W]) and /BS (/W) so a later read
    /// or appearance generation sees the explicit value — including an explicit 0.</summary>
    internal void SetBorderWidthValue(int width)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(0)); arr.Add(new PdfInteger(0)); arr.Add(new PdfInteger(width));
        _dict.Set("Border", arr);
        var bs = _reader.ResolveDict(_dict.Get("BS")) ?? new PdfDictionary();
        bs.Set("W", new PdfInteger(width));
        _dict.Set("BS", bs);
    }

    /// <summary>Read the border style (/BS /S) for the write-through
    /// <see cref="Border.Style"/> accessor. Defaults to Solid.</summary>
    internal Aspose.Pdf.Annotations.BorderStyle GetBorderStyleValue()
    {
        var bs = _reader.ResolveDict(_dict.Get("BS"));
        return bs?.GetName("S") switch
        {
            "D" => Aspose.Pdf.Annotations.BorderStyle.Dashed,
            "B" => Aspose.Pdf.Annotations.BorderStyle.Beveled,
            "I" => Aspose.Pdf.Annotations.BorderStyle.Inset,
            "U" => Aspose.Pdf.Annotations.BorderStyle.Underline,
            _ => Aspose.Pdf.Annotations.BorderStyle.Solid,
        };
    }

    /// <summary>Persist the border style to /BS /S for the write-through
    /// <see cref="Border.Style"/> accessor.</summary>
    internal void SetBorderStyleValue(Aspose.Pdf.Annotations.BorderStyle style)
    {
        var bs = _reader.ResolveDict(_dict.Get("BS")) ?? new PdfDictionary();
        bs.Set("S", new PdfName(style switch
        {
            Aspose.Pdf.Annotations.BorderStyle.Dashed => "D",
            Aspose.Pdf.Annotations.BorderStyle.Beveled => "B",
            Aspose.Pdf.Annotations.BorderStyle.Inset => "I",
            Aspose.Pdf.Annotations.BorderStyle.Underline => "U",
            _ => "S",
        }));
        _dict.Set("BS", bs);
    }

    /// <summary>Read the border dash pattern (/BS /D) for the write-through
    /// <see cref="Border.Dash"/> accessor, or null when none.</summary>
    internal int[]? GetBorderDashValue()
    {
        var bs = _reader.ResolveDict(_dict.Get("BS"));
        if (_reader.Resolve(bs?.Get("D")) is PdfArray d && d.Count > 0)
        {
            var res = new int[d.Count];
            for (int i = 0; i < d.Count; i++)
                res[i] = d[i] switch
                {
                    PdfInteger pi => (int)pi.Value,
                    PdfReal pr => (int)System.Math.Round(pr.Value),
                    _ => 0,
                };
            return res;
        }
        return null;
    }

    /// <summary>Persist the border dash pattern to /BS /D for the write-through
    /// <see cref="Border.Dash"/> accessor (removes /D when null/empty).</summary>
    internal void SetBorderDashValue(int[]? pattern)
    {
        var bs = _reader.ResolveDict(_dict.Get("BS")) ?? new PdfDictionary();
        if (pattern is { Length: > 0 })
        {
            var d = new PdfArray();
            foreach (var seg in pattern) d.Add(new PdfInteger(seg));
            bs.Set("D", d);
        }
        else bs.Remove("D");
        _dict.Set("BS", bs);
    }
}
