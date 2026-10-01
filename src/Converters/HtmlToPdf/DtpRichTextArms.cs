using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The arms of the DTP rich-text token step: a text run, a block open or close, and a span open or close.</summary>
    private static void ApplyDtpSpanTag(DtpRichTextState rt)
    {
        if (rt.close)
        {
            if (rt.spanStack.Count > 0) rt.spanStack.Pop();
        }
        else
        {
            double? size = null;
            string? face = null;
            (double, double, double)? color = null;
            var clsA = DtpAttr("<span " + rt.attrs + ">", "class");
            if (clsA is not null && rt.classRules.TryGetValue(clsA, out var scr))
            {
                size = scr.SizePt;
                face = scr.Family;
                color = scr.Color;
            }
            var st = DtpAttr("<span " + rt.attrs + ">", "style") ?? "";
            var fs = Regex.Match(st, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (fs.Success) size = DtpNum(fs.Groups[1].Value);
            // `font: 7pt "Times New Roman"` shorthand
            var fsh = Regex.Match(st, @"font\s*:\s*([\d.]+)\s*pt\s+[""']?([^;""']+)",
                RegexOptions.IgnoreCase);
            if (fsh.Success)
            {
                size = DtpNum(fsh.Groups[1].Value);
                face = fsh.Groups[2].Value.Trim();
            }
            var ff = Regex.Match(st, @"font-family\s*:\s*[""']?([^;,""']+)", RegexOptions.IgnoreCase);
            if (ff.Success) face = ff.Groups[1].Value.Trim();
            rt.spanStack.Push((size, face is null ? null : DtpNormalizeFace(face), color));
        }
    }

    /// <summary></summary>
    private static void ApplyDtpBlockTag(DtpRichTextState rt)
    {
        FlushDtpLine(rt);
        if (!rt.close)
        {
            var alM = Regex.Match(rt.attrs, @"align\s*=\s*[""']?(left|center|right)",
                RegexOptions.IgnoreCase);
            rt.alignStack.Push(alM.Success
                ? alM.Groups[1].Value.ToLowerInvariant() : rt.alignStack.Peek());
            if (rt.name == "p")
            {
                // Pasted Word bullet paragraph: indent = margin-left
                // + text-indent for the first line, margin-left for
                // continuations.
                var st = DtpAttr("<p " + rt.attrs + ">", "style") ?? "";
                rt.inMso = st.Contains("mso-list", StringComparison.OrdinalIgnoreCase)
                    || rt.attrs.Contains("MsoNormal", StringComparison.OrdinalIgnoreCase);
                // margin shorthand: top right bottom LEFT
                rt.msoHangIndent = DtpStyleLen(st,
                        @"margin\s*:\s*\S+\s+\S+\s+\S+\s+([\-\d.]+(?:in|pt))")
                    ?? DtpStyleLen(st, @"margin-left\s*:\s*([\-\d.]+(?:in|pt))") ?? 0;
                rt.msoFirstIndent = rt.msoHangIndent
                    + (DtpStyleLen(st, @"text-indent\s*:\s*([\-\d.]+(?:in|pt))") ?? 0);
            }
        }
        else
        {
            if (rt.alignStack.Count > 1) rt.alignStack.Pop();
            rt.inMso = false;
        }
    }

    /// <summary></summary>
    private static void AppendDtpText(DtpRichTextState rt)
    {
        var raw = EdgarHtmlRenderer.DecodeEntities(rt.inner[rt.idx..rt.textEnd]);
        raw = Regex.Replace(raw, @"[ \t\r\n\f]+", " ");
        if (raw.Length > 0 && raw != " " || raw == " " && rt.cur is not null && rt.cur.Runs.Count > 0)
        {
            var line = CurDtpLine(rt);
            // strip a collapsible leading space at line start
            if (line.Runs.Count == 0) raw = raw.TrimStart(' ');
            if (raw.Length > 0)
            {
                var (sz, face, color) = DtpEffective(rt.baseClass, rt.spanStack);
                line.Runs.Add(new DtpRun
                {
                    Text = raw,
                    Bold = rt.bold > 0,
                    Italic = rt.ital > 0,
                    Under = rt.under > 0 || rt.link > 0,
                    SizePt = sz,
                    Face = face,
                    Color = rt.link > 0 ? (0, 0, 1.0) : color,
                });
            }
        }
        rt.idx = rt.textEnd;
    }
}
