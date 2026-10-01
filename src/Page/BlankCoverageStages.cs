using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
// The stages of the blank-coverage measure: the recursive content walk.
    private void WalkBlankCoverage(BlankCoverageState bk, byte[] content, PdfDictionary? resources, int depth)
    {
        if (bk.hard || depth > 16) return;
        var xobjects = _reader.ResolveDict(resources?.Get("XObject"));
        var parser = new Content.ContentStreamParser(_reader);

        parser.OnShadingPainted += (_, _) => bk.hard = true;

        parser.OnTextShown += (text, _, state) =>
        {
            if (bk.hard || state.RenderingMode == 3) return;
            foreach (var ch in text)
                if (!char.IsWhiteSpace(ch) && ch != '\0') { bk.hard = true; return; }
        };

        parser.OnPathPainted += (op, state, segments) =>
        {
            if (bk.hard || segments.Count == 0) return;
            var fills = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
            var strokes = op is "S" or "s" or "B" or "B*" or "b" or "b*";
            var nonWhiteFill = state.FillPatternName is not null
                || state.FillR < 0.995 || state.FillG < 0.995 || state.FillB < 0.995;
            var nonWhiteStroke = state.StrokePatternName is not null
                || state.StrokeR < 0.995 || state.StrokeG < 0.995 || state.StrokeB < 0.995;
            if (!(fills && nonWhiteFill) && !(strokes && nonWhiteStroke)) return;

            // Device-space bbox of the path; a mark fully outside the crop box
            // is invisible and doesn't count.
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            void Grow(double px, double py)
            {
                var (dx, dy) = state.TransformPoint(px, py);
                if (dx < minX) minX = dx;
                if (dy < minY) minY = dy;
                if (dx > maxX) maxX = dx;
                if (dy > maxY) maxY = dy;
            }
            foreach (var seg in segments)
            {
                switch (seg.Op)
                {
                    case Content.PathOp.MoveTo:
                    case Content.PathOp.LineTo:
                        Grow(seg.X1, seg.Y1);
                        break;
                    case Content.PathOp.CurveTo:
                        Grow(seg.X1, seg.Y1); Grow(seg.X2, seg.Y2); Grow(seg.X3, seg.Y3);
                        break;
                    case Content.PathOp.CurveToV:
                    case Content.PathOp.CurveToY:
                        Grow(seg.X1, seg.Y1); Grow(seg.X2, seg.Y2);
                        break;
                    case Content.PathOp.Rect:
                        Grow(seg.X1, seg.Y1); Grow(seg.X1 + seg.X2, seg.Y1 + seg.Y2);
                        break;
                }
            }
            if (minX > maxX) return;
            if (maxX < bk.crop.LLX || minX > bk.crop.URX || maxY < bk.crop.LLY || minY > bk.crop.URY) return;
            bk.hard = true;
        };

        parser.OnInlineImage += (dict, raw) =>
        {
            // Once the summed coverage exceeds the caller's tolerance the verdict
            // can't change (coverage only grows) — skip further image decodes.
            if (bk.hard || bk.sum > bk.tolerance) return;
            try { bk.sum += NonWhiteImageFraction(dict, IO.Filters.StreamFilter.Decode(raw, dict), inline: true); }
            catch { }
        };

        parser.OnImageDrawn += (name, _) =>
        {
            if (bk.hard || bk.sum > bk.tolerance || xobjects is null) return;
            var xobj = _reader.ResolveStream(xobjects.Get(name));
            if (xobj is null) return;
            if (xobj.Dict.GetName("Subtype") == "Form")
            {
                byte[] formContent;
                try { formContent = _reader.DecodeStream(xobj); }
                catch { return; }
                var formRes = _reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;
                WalkBlankCoverage(bk, formContent, formRes, depth + 1);
                return;
            }
            // Distinct image resources sum; the same name drawn twice counts once.
            if (!bk.counted.Add(depth + ":" + name)) return;
            try
            {
                byte[] decoded;
                try { decoded = _reader.DecodeStream(xobj); } catch { return; }
                bk.sum += NonWhiteImageFraction(xobj.Dict, decoded, inline: false);
            }
            catch { }
        };

        parser.Parse(content, null, null, null, null, null, null);
    }
}
