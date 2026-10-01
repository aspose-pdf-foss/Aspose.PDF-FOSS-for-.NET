using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Print ad helpers: font sizes, margins, hidden classes, drops, line and wrapped emits, plain text and the tag walk.
    private static double FsOf(PrintAdState pa, string sel, double dflt)
        => pa.css.TryGetValue(sel, out var r) && r.TryGetValue("font-size", out var v)
           && TryParseLength(v.Trim()) is { } pt && pt > 0 ? pt : dflt;

    private static double MarginBottomOf(PrintAdState pa, string sel, double dflt)
    {
        if (!pa.css.TryGetValue(sel, out var r)) return dflt;
        if (r.TryGetValue("margin-bottom", out var mbv)
            && TryParseLength(mbv.Trim().Replace("!important", "").Trim()) is { } mbPt)
            return mbPt;
        if (r.TryGetValue("margin", out var mv)
            && TryParseCssMarginBox(Regex.Replace(mv, @"!important", "").Trim(), out var mBox))
            return mBox.bottom;
        return dflt;
    }

    private static bool HiddenCls(PrintAdState pa, string cls)
        => pa.css.TryGetValue("." + pa.contCls + " ." + cls, out var hr)
           && hr.TryGetValue("display", out var hd)
           && hd.Contains("none", StringComparison.OrdinalIgnoreCase);

    private static double Drop(PrintAdState pa, double fs, double box) => (box - fs * pa.wm.sum) / 2 + fs * pa.wm.asc;

    private static void Gap(PrintAdState pa, double g) => pa.pendingGap = Math.Max(pa.pendingGap, g);

    private static void Spend(PrintAdState pa) { pa.y += pa.pendingGap; pa.pendingGap = 0; }

    private static void EmitLine(PrintAdState pa, string text, double fs, bool bold, double box, double x)
    {
        var (rn, hex) = Text.Type0FontEmbedder.Embed(
            (pa.page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                ?? throw new InvalidOperationException(),
            PosFace(pa.face + (bold ? " Bold" : "")).ttf ?? PosFace(pa.face!).ttf!,
            pa.face!.Replace(" ", "") + (bold ? "Bold" : ""), text, stripSpacesInBaseFont: true);
        pa.sb.AppendLine(Compat.Format(pa.inv,
            $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {pa.pageHeight - (pa.y + Drop(pa, fs, box)):F2} Tm <{Compat.ToHexString(hex)}> Tj ET"));
    }

    private static void EmitWrapped(PrintAdState pa, string text, double fs, bool bold, double box, double x, double w)
    {
        foreach (var ln in MeasuredWordWrap(text, w, pa.face + (bold ? " Bold" : ""), fs))
        {
            if (ln.Length > 0) EmitLine(pa, ln, fs, bold, box, x);
            pa.y += box;
        }
    }

    private static string PlainText(PrintAdState pa, string s2) => CollapseWs(DecodeEntities(
        Regex.Replace(s2, @"<[^>]+>", ""))).Trim();

    private static void WalkAd(PrintAdState pa, string frag)
    {
        var wa = new AdWalkState();
        wa.p2 = 0;
        while (true)
        {
            if (!WalkAdTag(pa, wa, frag)) break;
        }
    }
}
