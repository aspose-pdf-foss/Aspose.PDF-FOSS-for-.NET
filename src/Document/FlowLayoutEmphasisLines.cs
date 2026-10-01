using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;
namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>Each wrapped line of an emphasis run is seated on the flow and drawn, its runs keeping the weight, slant and decoration the markup gave them.</summary>
        private void WriteEmphasisLines(Text.TextFragment tf, List<List<(string Text, bool Bold, bool Italic, bool Under, Hyperlink? Link, double W)>> lines, double fontSize, double lineHeight)
        {
            var idx = 0;
            while (idx < lines.Count)
            {
                var availableLines = Math.Max(1, (int)((_curY - EffectiveBottom) / lineHeight));
                var chunkSize = Math.Min(availableLines, lines.Count - idx);
                // Same first-baseline rule as the plain embedded chunk: chain onto the
                // previous body baseline when there is one, else drop by the font size.
                var firstBaseline = _lastBodyBaseline.HasValue
                    ? _lastBodyBaseline.Value - lineHeight
                    : FirstBaselineSeat(tf.TextState, fontSize, lineHeight);
                for (var j = 0; j < chunkSize; j++)
                {
                    var baseline = firstBaseline - j * lineHeight;
                    var x = CurLeft;
                    var line = lines[idx + j];
                    var k = 0;
                    while (k < line.Count)
                    {
                        // Merge the neighbours that share a style into one show.
                        var m = k + 1;
                        while (m < line.Count && line[m].Bold == line[k].Bold
                               && line[m].Italic == line[k].Italic
                               && line[m].Under == line[k].Under
                               && ReferenceEquals(line[m].Link, line[k].Link)) m++;
                        var sb = new System.Text.StringBuilder();
                        var w = 0.0;
                        for (var p = k; p < m; p++) { sb.Append(line[p].Text); w += line[p].W; }
                        var runState = new Text.TextState
                        {
                            Font = tf.TextState.Font,
                            FontData = tf.TextState.FontData,
                            ForegroundColor = tf.TextState.ForegroundColor,
                            IsBold = line[k].Bold,
                            IsItalic = line[k].Italic || tf.TextState.IsItalic,
                            Underline = line[k].Under,
                        };
                        _pendingEmbeddedRenders.Add((_currentSlot, x, _curY,
                            sb.ToString(), runState, fontSize, baseline));
                        // An anchored run carries its own Link annotation, boxed on
                        // this line's baseline. Without this, a block that mixes a
                        // hyperlink with any bold or underlined run reaches the
                        // reader with no clickable link at all.
                        if (line[k].Link is { } emLink && w > 0
                            && sb.ToString().Trim().Length > 0)
                        {
                            var (emAbove, emBelow) = LinkBoxExtent(tf.TextState, fontSize);
                            _pendingLinks.Add((_currentSlot,
                                new Rectangle(x, baseline - emBelow, x + w,
                                    baseline + emAbove), emLink));
                        }
                        x += w;
                        k = m;
                    }
                }
                _lastBodyBaseline = firstBaseline - (chunkSize - 1) * lineHeight;
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY -= lineHeight * chunkSize;
                idx += chunkSize;
                if (idx < lines.Count) FlowToNextRegion();
            }
        }
    }
}
