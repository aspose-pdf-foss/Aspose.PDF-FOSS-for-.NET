using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The stages of the span spacing resolve: the slot-based spacing.</summary>
    private static void ResolveSlotSpacing(StlDivState sd, List<StlItem> its, double? inheritWs, int s, int slots)
    {
        // THE EM-COMPENSATION SOLVE:
        //   ws = R2( (S·ΣE − S·(S−1)·Σface − n·lsFloor) / (D·1000) )
        // · The solve runs at the css size TRUNCATED to the
        //   0.01-em grid (0.91em·12 = 10.92 pt for a drawn 11) while
        //   the markup emits the ROUNDED size (0.92em); S is the
        //   drawn/solve ratio, and the face side scales by S again
        //   (the ws deliberately lands short of its own ink).
        // · n = every region item (interior slots included; the
        //   trailing inter-span slot of a title span included — that
        //   slot extends the region to the next span's start, which
        //   is what makes the solve track the following span rather
        //   than the title's own ink).
        // · D counts only KERN-CARRYING slots: a bare drawn space
        //   (pen gap = its own advance) contributes no divisor. The
        //   membership floor lies in the (0.1, 48.8) milli-em
        //   bracket; 20 sits mid-bracket.
        const double EmGridSlotKernFloorMilli = 20.0;
        var fsEm = sd.st.FontSize / 12.0;
        var cssEm = Math.Floor(fsEm * 100.0) / 100.0;
        var scale = cssEm > 0 ? fsEm / cssEm : 1.0;
        double sumE = 0, faceSum = 0;
        foreach (var x in its) { sumE += x.E; faceSum += x.FaceMilli; }
        var dKern = its.Count(x => x.IsSlot
            && Math.Abs(x.E) >= EmGridSlotKernFloorMilli);
        if (dKern == 0) dKern = slots;   // all-bare span: every slot carries
        var lsTerms = its.Count;
        sd.wsEm = Math.Round(
            (scale * sumE - scale * (scale - 1.0) * faceSum - lsTerms * sd.lsMilli)
            / dKern / 1000.0,
            2, MidpointRounding.AwayFromZero);
        var fitEnv = Environment.GetEnvironmentVariable("ASPOSE_PH2_FIT");
        if (fitEnv is "1" or "2")
        {
            var head = new StringBuilder();
            foreach (var x in its)
            {
                if (head.Length >= 28) break;
                head.Append(x.IsSlot ? ' ' : x.Ch);
            }
            Console.Error.WriteLine(
                $"[fit] n={its.Count} D={dKern}/{slots} S={scale:F5} " +
                $"sumE={sumE:F2} face={faceSum:F1} ls={sd.lsMilli:F1} " +
                $"ws={sd.wsEm:F2} |{head}|");
            if (fitEnv == "2")
                foreach (var x in its)
                    Console.Error.WriteLine(
                        $"  [it] {(x.IsSlot ? "SLOT" : (x.Text ?? x.Ch.ToString())),-4} " +
                        $"E={x.E:F2} face={x.FaceMilli:F1} x={x.StartX:F2}");
        }
    }
}
