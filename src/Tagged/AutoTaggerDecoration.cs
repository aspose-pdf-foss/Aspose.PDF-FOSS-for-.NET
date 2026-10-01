using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // An image covering at least this share of the page is its background.
    private const double BackgroundShare = 0.8;
    // An image thinner than this (points) in either direction is a spacer or a rule.
    private const double SpacerSize = 3.0;
    // Logos repeat at the same place: positions and sizes agree within this many points.
    private const double RepeatTolerance = 2.0;

    /// <summary>Mark images that are decoration rather than content: a page background, a
    /// spacer or rule, and a logo repeated in the running header or footer band of most pages.
    /// They become artifacts, which carry no alternative text (PDF/UA-1 §7.1, §7.3); every
    /// other image stays a Figure, whose alternative text only the caller can supply
    /// (<see cref="LogicalStructure.StructureElement.AlternativeText"/>).</summary>
    private static void MarkDecorativeImages(List<PageWork> pages)
    {
        var banded = new List<(PageWork Page, (double y, double x, double w, double h, int op) Fig)>();
        foreach (var pw in pages)
        {
            var rect = pw.Page.GetPageRect(true); // figures stand as the page is shown
            var pageArea = Math.Max(1, rect.Width * rect.Height);
            var band = rect.Height * RunningBand;
            foreach (var f in pw.Figures)
            {
                if (f.w < SpacerSize || f.h < SpacerSize || f.w * f.h >= BackgroundShare * pageArea)
                    pw.Decorative.Add(f.op);
                else if (f.y > rect.URY - band - f.h || f.y + f.h < rect.LLY + band + f.h)
                    banded.Add((pw, f));
            }
        }

        // A band image recurring at one place on at least half the pages is a running logo.
        if (pages.Count < 2) return;
        var needed = Math.Max(2, (pages.Count + 1) / 2);
        foreach (var (pw, f) in banded)
        {
            var onPages = banded.Where(o => Math.Abs(o.Fig.x - f.x) <= RepeatTolerance && Math.Abs(o.Fig.y - f.y) <= RepeatTolerance
                                            && Math.Abs(o.Fig.w - f.w) <= RepeatTolerance && Math.Abs(o.Fig.h - f.h) <= RepeatTolerance)
                .Select(o => o.Page).Distinct().Count();
            if (onPages >= needed) pw.Decorative.Add(f.op);
        }
    }
}
