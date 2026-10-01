using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Print ad tag families: skipped heads, the image, the headings, the paragraph, the list, the block and the inline runs.</summary>
    private static void SkipOrImage(PrintAdState pa, AdWalkState wa, AdTagState at, string frag, string tag)
    {
        switch (tag)
        {
            case "style":
            case "script":
                InnerOf(at, wa, frag, at.tag);
                break;
            case "img":
            {
                // A broken image leaves its alt text as one base-size line.
                var src = AttrValue(at.t.Value, "src") ?? "";
                var alt = AttrValue(at.t.Value, "alt") ?? "";
                if (LoadConverterImage(src, pa.options) is null && alt.Length > 0)
                {
                    Spend(pa);
                    var box0 = pa.baseFs * pa.lineFactor;
                    EmitLine(pa, alt, pa.baseFs, false, box0, pa.xText);
                    pa.y += box0;
                }
                break;
            }
        }
    }

    /// <summary></summary>
    private static void Heading(PrintAdState pa, AdWalkState wa, AdTagState at, string frag, string tag)
    {
        switch (tag)
        {
            case "h1":
            {
                var innerH = InnerOf(at, wa, frag, "h1");
                if (at.hidden) break;
                Spend(pa);
                // span-sized first line(s), the remainder at the h1 size;
                // a <br> splits the lines.
                foreach (var seg in Regex.Split(innerH, @"<br\b[^>]*/?>",
                             RegexOptions.IgnoreCase))
                {
                    var segText = PlainText(pa, seg);
                    if (segText.Length == 0) continue;
                    var isSpan = Regex.IsMatch(seg, @"<span\b", RegexOptions.IgnoreCase);
                    var fs2 = isSpan ? pa.h1SpanFs : pa.h1Fs;
                    var box2 = fs2 * pa.lineFactor;
                    EmitLine(pa, segText, fs2, false, box2, pa.xText);
                    pa.y += box2;
                }
                Gap(pa, pa.h1Mb);
                break;
            }
            case "h6":
            {
                var innerH = InnerOf(at, wa, frag, "h6");
                if (at.hidden) break;
                Spend(pa);
                var box2 = pa.h6Fs * 1.28571;
                EmitLine(pa, PlainText(pa, innerH).ToUpperInvariant(), pa.h6Fs, true, box2, pa.xText);
                pa.y += box2 + pa.h6PadBottom;
                if (pa.h6Border is { } hbCol)
                {
                    pa.sb.AppendLine(Compat.Format(pa.inv,
                        $"q {hbCol.R / 255.0:0.###} {hbCol.G / 255.0:0.###} {hbCol.B / 255.0:0.###} RG 0.75 w " +
                        $"{pa.xText:F2} {pa.pageHeight - (pa.y + 0.375):F2} m {pa.xText + pa.contentW:F2} {pa.pageHeight - (pa.y + 0.375):F2} l S Q"));
                    pa.y += 0.75;
                }
                Gap(pa, pa.h6Mb);
                break;
            }
        }
    }

    /// <summary></summary>
    private static void Paragraph(PrintAdState pa, AdWalkState wa, AdTagState at, string frag, string tag)
    {
        switch (tag)
        {
            case "p":
            {
                var innerP = InnerOf(at, wa, frag, "p");
                if (at.hidden) break;
                var fs2 = at.cls.Contains("companyDescription") ? pa.descFs : pa.baseFs;
                var factor = at.cls.Contains("companyDescription") ? 4.0 / 3.0 : pa.lineFactor;
                var box2 = fs2 * factor;
                var any = false;
                foreach (var seg in Regex.Split(innerP, @"<br\b[^>]*/?>",
                             RegexOptions.IgnoreCase))
                {
                    var segText = PlainText(pa, seg);
                    if (segText.Length == 0) continue;
                    if (!any) Spend(pa);
                    any = true;
                    EmitWrapped(pa, segText, fs2, false, box2, pa.xText, pa.contentW);
                }
                if (any)
                    Gap(pa, at.cls.Contains("companyDescription")
                        ? MarginBottomOf(pa, "." + pa.contCls + " .companyDescription", pa.pMb) : pa.pMb);
                break;
            }
        }
    }

    /// <summary></summary>
    private static void ListBlock(PrintAdState pa, AdWalkState wa, AdTagState at, string frag, string tag)
    {
        switch (tag)
        {
            case "ul":
            {
                var innerU = InnerOf(at, wa, frag, "ul");
                if (at.hidden) break;
                Spend(pa);
                var box2 = pa.baseFs * pa.lineFactor;
                foreach (Match li in Regex.Matches(innerU, @"<li\b[^>]*>([\s\S]*?)</li\s*>",
                             RegexOptions.IgnoreCase))
                {
                    var liText = PlainText(pa, li.Groups[1].Value);
                    if (liText.Length == 0) continue;
                    var markerAdv = pa.liMarker.Length > 0
                        ? MeasureFaceText(pa.face!, pa.liMarker, pa.baseFs) + pa.liMarkerGap : 0;
                    if (pa.liMarker.Length > 0) EmitLine(pa, pa.liMarker, pa.baseFs, false, box2, pa.xText);
                    var first = true;
                    foreach (var ln in MeasuredWordWrap(liText, pa.contentW - markerAdv,
                                 pa.face!, pa.baseFs))
                    {
                        if (ln.Length > 0)
                            EmitLine(pa, ln, pa.baseFs, false, box2, first ? pa.xText + markerAdv : pa.xText);
                        pa.y += box2;
                        first = false;
                    }
                }
                Gap(pa, MarginBottomOf(pa, "." + pa.contCls + " ul", 15));
                break;
            }
        }
    }

    /// <summary></summary>
    private static void DivOrAnchor(PrintAdState pa, AdWalkState wa, AdTagState at, string frag, string tag)
    {
        switch (tag)
        {
            case "div":
            {
                var innerD = InnerOf(at, wa, frag, "div");
                if (at.hidden || at.cls.Contains("applyButton")) break;
                WalkAd(pa, innerD);
                if (at.cls.Contains("jobBlock"))
                    Gap(pa, pa.blockMb);
                break;
            }
            case "a":
            {
                var innerA = InnerOf(at, wa, frag, "a");
                // apply buttons are hidden in print
                if (!at.hidden && !at.cls.Contains("applyButton"))
                {
                    var aText = PlainText(pa, innerA);
                    if (aText.Length > 0)
                    {
                        Spend(pa);
                        var box2 = pa.baseFs * pa.lineFactor;
                        EmitWrapped(pa, aText, pa.baseFs, false, box2, pa.xText, pa.contentW);
                    }
                }
                break;
            }
        }
    }

    /// <summary></summary>
    private static void InlineRun(PrintAdState pa, AdWalkState wa, AdTagState at, string frag, string tag)
    {
        switch (tag)
        {
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "span":
            case "strong":
            case "b":
            {
                // flatten inline-ish leftovers as base text
                var innerO = InnerOf(at, wa, frag, at.tag);
                var oText = PlainText(pa, innerO);
                if (!at.hidden && oText.Length > 0)
                {
                    Spend(pa);
                    var box2 = Math.Round(pa.baseFs / 0.75 * pa.lineFactor,
                        MidpointRounding.AwayFromZero) * 0.75;
                    EmitWrapped(pa, oText, pa.baseFs, false, box2, pa.xText, pa.contentW);
                }
                break;
            }
        }
    }
}
