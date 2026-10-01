using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the STL run render: one segment at a time.</summary>
    private static bool RenderStlSegment(StlRunRenderState rr, int si)
    {
        var segText = rr.segments[si].Text;
        if (segText.Length > 0)
        {
            var pieces = rr.runUnion is null
                ? null
                : UnionStlSegments(rr.sp.htmlFaces, rr.r.Family, segText);
            foreach (var (pieceTtf, pieceParser, pieceUpm, pieceText) in
                pieces is null
                    ? new[] { ((byte[])rr.faceTtf!, rr.faceParser, rr.faceUpm, segText) }
                    : pieces.Select(p => (p.face.Ttf, p.face.Parser, p.face.Upm, p.text)))
            {
                if (pieceText.Length == 0) continue;
                var (rn, hex) = Text.Type0FontEmbedder.Embed(rr.fontDict, pieceTtf,
                    rr.faceName, pieceText, stripSpacesInBaseFont: true);
                rr.sb.Append($"/{rn} {rr.r.FontSizePt.ToString("F1", rr.sp.inv)} Tf ");
                if (rr.r.LetterSpacingPt != 0)
                    rr.sb.Append($"{rr.r.LetterSpacingPt.ToString("F3", rr.sp.inv)} Tc ");
                rr.sb.Append($"1 0 0 1 {rr.segX.ToString("F2", rr.sp.inv)} {rr.y.ToString("F2", rr.sp.inv)} Tm ");
                rr.sb.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ");
                if (rr.r.LetterSpacingPt != 0) rr.sb.Append("0 Tc ");
                rr.segX += MeasureParsedExact(pieceParser, pieceUpm, pieceText, rr.r.FontSizePt)
                      + rr.r.LetterSpacingPt * pieceText.Length;
            }
        }
        if (rr.segments[si].Sep == ' ')
            rr.segX += MeasureParsedExact(rr.faceParser, rr.faceUpm, " ", rr.r.FontSizePt)
                  + rr.r.LetterSpacingPt + rr.r.WordSpacingPt;
        else if (rr.segments[si].Sep == 'c')
            rr.segX += rr.r.WordSpacingPt;
        return true;
    }
}
