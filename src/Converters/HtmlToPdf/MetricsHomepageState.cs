using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MetricsHomepageState
{
    public byte[] segoe = null!;
    // ── parse the metric grids ───────────────────────────────────────────
    public List<List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.MhCell>>> tables = null!;
    // the CTA label (the hero button's underlined anchor)
    public System.Text.RegularExpressions.Match ctaM = null!;
    public string ctaText = null!;
    public System.Globalization.CultureInfo inv = null!;
    public Aspose.Pdf.Core.PdfDictionary measureDict = null!;
    public Document doc = null!;
    public Page page = null!;
    public System.Text.StringBuilder shapes = null!;
    public List<(byte[] Ttf, string Face, double Fs, double X, double BaseTd, string Text, string Col)> runs = null!;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public byte[] segoeSemi = null!;
    public byte[] arialBold = null!;
}
}
