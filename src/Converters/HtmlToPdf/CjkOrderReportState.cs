using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CjkOrderReportState
{
    public byte[]? simsun;
    public byte[]? arial;
    public byte[]? arialBold;
    public System.Globalization.CultureInfo invc = null!;
    public Document doc = null!;
    public Page page = null!;
    public List<(int start, string attrs, string body)> tables = null!;
    // ── page 1: the template's measured layout ──
    // vertical title: the ideographs stack at 14.3 pt pitch
    public System.Text.RegularExpressions.Match titleM = null!;
    public string title = null!;
    public System.Text.RegularExpressions.Match tM2 = null!;
    public double[] rowSeats = null!;
    public string[] leftLabels = null!;
    public Dictionary<string, int> rightRow = null!;
    // the numbered activity tables under their section heading
    public System.Text.RegularExpressions.Match secM = null!;
    public List<string> actHeads = null!;
    public List<int> actIdx = null!;
    // the route line and the infrastructure table
    public System.Text.RegularExpressions.Match routeM = null!;
    public System.Text.RegularExpressions.Match infraHeadM = null!;
    // infra table: 2 columns, measured rows
    public int infraIdx;
    public List<List<string>> infraRows = null!;
    public double[] infraEdges = null!;
    public double yTd;
    public int infraEnd;
    public double MeasureF(byte[] ttf, string name, string s, double fs)
    {
        if (page.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * fs;
        return Text.Type0FontEmbedder.MeasureText(fd, ttf, name, s, fs,
            stripSpacesInBaseFont: true);
    }
    public double MixedW(double fs, string text, bool bold)
    {
        double w = 0;
        var i = 0;
        while (i < text.Length)
        {
            var cjk = text[i] >= 0x2E80;
            var j = i;
            while (j < text.Length && (text[j] >= 0x2E80) == cjk) j++;
            var seg = text[i..j];
            w += cjk ? MeasureF(simsun!, "SimSun", seg, fs)
                : MeasureF(bold ? arialBold! : arial!, bold ? "ArialBold" : "Arial", seg, fs);
            i = j;
        }
        return w;
    }
    public string html = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = null!;
    public double pageHeight = 0;
}
}
