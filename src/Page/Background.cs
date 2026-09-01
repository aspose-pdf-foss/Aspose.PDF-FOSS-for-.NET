using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Sync attached fragment modifications back to the content stream.</summary>
    internal void SyncAttachedFragments()
    {
        if (_attachedFragments is null) return;
        foreach (var f in _attachedFragments.ToArray())
        {
            if (f.AttachedSegment is not null)
            {
                // The fragment owns its content-stream segment: when anything the
                // written run depends on changed, write the segment again from the
                // current state (an unchanged fragment costs nothing).
                if (f.AttachedSignature != f.AttachedLayoutSignature())
                    new Text.TextBuilder(this).RewriteAttachedFragment(f);
                continue;
            }
            if (f.LastWrittenText is not null && f.Text != f.LastWrittenText)
            {
                var replacer = new Text.TextReplacer();
                replacer.Replace(this, f.LastWrittenText, f.Text);
                f.LastWrittenText = f.Text;
            }
        }
    }

    /// <summary>Place a decoration block INLINE, immediately before the text object that
    /// draws at <paramref name="pageX"/>/<paramref name="pageY"/>. A regenerated rule or
    /// highlight belongs where the run it dresses is drawn, not at either end of the page:
    /// prepending puts it under content the source already painted (a highlight vanished
    /// behind the source's own), and appending puts it over the text it is supposed to sit
    /// behind. Returns false when no text object draws there, so the caller can fall back to
    /// its historic placement.</summary>
    internal bool InsertBeforeTextObjectAt(System.Collections.Generic.IList<Operator> block,
        double pageX, double pageY, double tolX = 2.0, double tolY = 3.0)
    {
        if (block.Count == 0) return false;
        var ops = Contents;
        var list = ops.ToList();
        double ctmA = 1, ctmB = 0, ctmC = 0, ctmD = 1, ctmE = 0, ctmF = 0;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmE = 0, tmF = 0;
        int btIndex = -1;
        for (var i = 0; i < list.Count; i++)
        {
            // Operators materialise lazily as GENERIC operators, so the walk keys on the
            // command name and reads the operands back off the serialised form - the same
            // reading the splice above uses.
            var cmd = list[i].CommandName;
            switch (cmd)
            {
                case "q":
                    ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmE, ctmF));
                    continue;
                case "Q":
                    if (ctmStack.Count > 0) (ctmA, ctmB, ctmC, ctmD, ctmE, ctmF) = ctmStack.Pop();
                    continue;
                case "cm":
                {
                    var n = ParseLeadingNumbers(list[i].ToString());
                    if (n is not { Length: >= 6 }) continue;
                    double nA = n[0] * ctmA + n[1] * ctmC;
                    double nB = n[0] * ctmB + n[1] * ctmD;
                    double nC = n[2] * ctmA + n[3] * ctmC;
                    double nD = n[2] * ctmB + n[3] * ctmD;
                    double nE = n[4] * ctmA + n[5] * ctmC + ctmE;
                    double nF = n[4] * ctmB + n[5] * ctmD + ctmF;
                    ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmE = nE; ctmF = nF;
                    continue;
                }
                case "BT":
                    btIndex = i; tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmE = 0; tmF = 0;
                    continue;
                case "ET":
                    btIndex = -1;
                    continue;
                case "Tm":
                {
                    var n = ParseLeadingNumbers(list[i].ToString());
                    if (n is not { Length: >= 6 }) continue;
                    tmA = n[0]; tmB = n[1]; tmC = n[2]; tmD = n[3]; tmE = n[4]; tmF = n[5];
                    continue;
                }
                case "Td":
                case "TD":
                {
                    var n = ParseLeadingNumbers(list[i].ToString());
                    if (n is not { Length: >= 2 }) continue;
                    tmE = n[0] * tmA + n[1] * tmC + tmE;
                    tmF = n[0] * tmB + n[1] * tmD + tmF;
                    continue;
                }
                case "Tj":
                case "TJ":
                case "'":
                case "\"":
                {
                    if (btIndex < 0) continue;
                    double px = ctmA * tmE + ctmC * tmF + ctmE;
                    double py = ctmB * tmE + ctmD * tmF + ctmF;
                    if (Math.Abs(px - pageX) <= tolX && Math.Abs(py - pageY) <= tolY)
                    {
                        ops.Insert(btIndex + 1, block);   // Insert is 1-based
                        ops.FlushToPage();
                        return true;
                    }
                    btIndex = -1;   // this text object is not the one; do not re-test it
                    continue;
                }
            }
        }
        return false;
    }

    /// <summary>Scan a raw (form XObject) content stream for a full-page fill.</summary>
    private static Color? ScanBytesForBackground(byte[] bytes, Rectangle mb)
    {
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        double r = 0, g = 0, b = 0;
        var colorSet = false;
        double reX = 0, reY = 0, reW = 0, reH = 0;
        var haveRect = false;
        var nums = new System.Collections.Generic.List<double>();
        foreach (var tokRaw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var tok = tokRaw;
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                nums.Add(v);
                continue;
            }
            switch (tok)
            {
                case "rg" when nums.Count >= 3:
                    r = nums[^3]; g = nums[^2]; b = nums[^1]; colorSet = true; break;
                case "g" when nums.Count >= 1:
                    r = g = b = nums[^1]; colorSet = true; break;
                case "re" when nums.Count >= 4:
                    reX = nums[^4]; reY = nums[^3]; reW = nums[^2]; reH = nums[^1];
                    haveRect = true; break;
                case "f" or "f*" or "B" or "b":
                    if (colorSet && haveRect && reW >= mb.Width * 0.9 && reH >= mb.Height * 0.9)
                        return Color.FromRgb(r, g, b);
                    haveRect = false;
                    break;
                case "BT":
                    return null;
            }
            nums.Clear();
        }
        return null;
    }

    /// <summary>Remove any previously-emitted tagged page-background block from
    /// the content stream(s) so a re-applied background replaces rather than
    /// stacks. Returns true when a block was removed.</summary>
    internal bool RemoveTaggedBackground()
    {
        if (_reader is null) return false;
        var marker = "/" + BackgroundMarkerTag + " BMC";
        var contentsObj = _reader.Resolve(_dict.Get("Contents"));

        if (contentsObj is PdfArray arr)
        {
            var removed = false;
            for (var i = arr.Count - 1; i >= 0; i--)
            {
                var s = _reader.ResolveStream(arr[i]);
                if (s is null) continue;
                var txt = Encoding.ASCII.GetString(_reader.DecodeStream(s));
                if (txt.Contains(marker))
                {
                    arr.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        if (contentsObj is PdfStream stream)
        {
            var txt = Encoding.ASCII.GetString(_reader.DecodeStream(stream));
            var start = txt.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;
            var emc = txt.IndexOf("EMC", start, StringComparison.Ordinal);
            if (emc < 0) return false;
            var end = emc + 3;
            while (end < txt.Length && (txt[end] == '\n' || txt[end] == '\r')) end++;
            SetContentStream(Encoding.ASCII.GetBytes(txt.Remove(start, end - start)));
            return true;
        }

        return false;
    }

    /// <summary>Whether this page paints any vector graphics, i.e. its content
    /// stream invokes a path-painting operator (stroke / fill / fill-and-stroke).</summary>
    public bool HasVectorGraphics()
    {
        foreach (Operator op in Contents)
        {
            switch (op.ToPdf())
            {
                case "S":   // stroke
                case "s":   // close + stroke
                case "f":   // fill (nonzero winding)
                case "F":   // fill (obsolete, == f)
                case "f*":  // fill (even-odd)
                case "B":   // fill + stroke (nonzero winding)
                case "B*":  // fill + stroke (even-odd)
                case "b":   // close + fill + stroke (nonzero winding)
                case "b*":  // close + fill + stroke (even-odd)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Detect an existing watermark from the page content: locate a
    /// /Subtype /Watermark artifact carrying an image (the artifact parser follows a
    /// form wrapper to the image) and surface that image as a <see cref="Watermark"/>.
    /// Returns an unavailable watermark when none is present.</summary>
    private Watermark DetectWatermark()
    {
        foreach (var art in Artifacts)
        {
            if (art is not WatermarkArtifact { Image: { } xi }) continue;
            // Finding the artifact needs no platform support; only turning it into a
            // System.Drawing.Image does. Say the watermark is THERE and let the image
            // itself report what this host cannot do.
            if (!OperatingSystem.IsWindows()) return new Watermark(imageNeedsPlatform: true);
            try
            {
                return new Watermark(LoadWatermarkImage(xi));
            }
            catch
            {
                // Unreadable/undecodable image — treat as no watermark rather than throw.
            }
        }
        return new Watermark();
    }
}
