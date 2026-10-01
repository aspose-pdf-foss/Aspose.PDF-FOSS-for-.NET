using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

internal static partial class LayerContentFilter
{
// The stages of the layer content filter: the operator emit, the target scan and one operator's filter decision.
    private static void Emit(LayerFilterState lf, string operands, string op)
    {
        if (CollapseOps.Contains(op) && lf.output.Count > 0 && lf.output[^1].Op == op)
        {
            lf.output[^1] = (operands, op);
            return;
        }
        lf.output.Add((operands, op));
    }

    /// <summary></summary>
    private static bool FilterLayerOperator(LayerFilterState lf, int i)
    {
        var (operands, op) = lf.ops[i];
        if (op is "BDC" or "BMC")
        {
            var isTarget = op == "BDC" && IsTargetOcOperand(operands, lf.layerId);
            var insideTarget = InTarget(lf.ocStack);
            lf.ocStack.Add(op == "BDC" ? isTarget : null);
            // Flatten/Delete keep other layers' markers when they sit OUTSIDE
            // the target's blocks; Save drops every marker.
            if (lf.mode != LayerFilterMode.Save && !isTarget && !insideTarget && i < lf.tailStart)
                Emit(lf, operands, op);
            return true;
        }
        if (op == "EMC")
        {
            bool? wasTarget = null;
            if (lf.ocStack.Count > 0)
            {
                wasTarget = lf.ocStack[^1];
                lf.ocStack.RemoveAt(lf.ocStack.Count - 1);
            }
            if (lf.mode != LayerFilterMode.Save && wasTarget != true && !InTarget(lf.ocStack) && i < lf.tailStart)
                Emit(lf, string.Empty, "EMC");
            return true;
        }
        if (i >= lf.tailStart)
        {
            // Save mode, after the target's last block: only the Qs that
            // close groups opened before the tail survive.
            if (op == "q") lf.tailDepth++;
            else if (op == "Q")
            {
                if (lf.tailDepth > 0) lf.tailDepth--;
                else Emit(lf, string.Empty, "Q");
            }
            return true;
        }
        var inTargetBlock = InTarget(lf.ocStack);
        var reduceToSkeleton = lf.mode switch
        {
            LayerFilterMode.Save => !inTargetBlock,
            LayerFilterMode.Delete => inTargetBlock,
            _ => false, // Flatten keeps everything verbatim
        };
        if (!reduceToSkeleton)
        {
            if (lf.mode == LayerFilterMode.Delete && op == "Do" && IsTargetDo(operands, lf.layerXObjects))
                return true; // Delete drops the layer's own XObject draw
            Emit(lf, operands, op);
            return true;
        }
        // Skeleton region.
        if (PathConstructionOps.Contains(op) || ClipOps.Contains(op))
        {
            lf.pathBuffer.Add((operands, op));
            return true;
        }
        if (PaintOps.Contains(op))
        {
            var hadClip = false;
            foreach (var b in lf.pathBuffer)
                if (ClipOps.Contains(b.Op)) { hadClip = true; break; }
            if (hadClip)
            {
                foreach (var b in lf.pathBuffer) Emit(lf, b.Operands, b.Op);
                Emit(lf, string.Empty, "n");
            }
            else if (op == "n" && lf.pathBuffer.Count == 0)
            {
                Emit(lf, string.Empty, "n"); // a bare no-op n survives
            }
            lf.pathBuffer.Clear();
            return true;
        }
        if (op == "Do" && lf.mode == LayerFilterMode.Save && IsTargetDo(operands, lf.layerXObjects))
        {
            Emit(lf, operands, op);
            return true;
        }
        if (StateOps.Contains(op))
            Emit(lf, operands, op);
        // everything else (text showing, BT/ET, foreign Do, sh, inline images) drops
        return true;
    }

    /// <summary></summary>
    private static void ScanLayerTargets(LayerFilterState lf)
    {
        for (var i = 0; i < lf.ops.Count; i++)
        {
            var (operands, op) = lf.ops[i];
            if (op is "BDC" or "BMC")
            {
                lf.ocStack.Add(op == "BDC" ? IsTargetOcOperand(operands, lf.layerId) : null);
                continue;
            }
            if (op == "EMC")
            {
                if (InTarget(lf.ocStack)) lf.lastTarget = i;
                if (lf.ocStack.Count > 0) lf.ocStack.RemoveAt(lf.ocStack.Count - 1);
                continue;
            }
            if (InTarget(lf.ocStack)) lf.lastTarget = i;
            if (op == "Do" && IsTargetDo(operands, lf.layerXObjects)) lf.hasDoContribution = true;
        }
    }
}
