using Aspose.Pdf.Content;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

public class GraphicsStateTests
{
    [Fact]
    public void Default_CtmIsIdentity()
    {
        var gs = new GraphicsState();
        Assert.Equal(new double[] { 1, 0, 0, 1, 0, 0 }, gs.Ctm);
    }

    [Fact]
    public void Default_ColorsAreBlack()
    {
        var gs = new GraphicsState();
        Assert.Equal(0, gs.FillR);
        Assert.Equal(0, gs.FillG);
        Assert.Equal(0, gs.FillB);
        Assert.Equal(0, gs.StrokeR);
    }

    [Fact]
    public void Default_LineWidthIsOne()
    {
        var gs = new GraphicsState();
        Assert.Equal(1.0, gs.LineWidth);
    }

    [Fact]
    public void Default_TextState()
    {
        var gs = new GraphicsState();
        Assert.Equal(100, gs.HorizontalScaling);
        Assert.Equal(0, gs.CharSpacing);
        Assert.Equal(0, gs.WordSpacing);
        Assert.Equal(0, gs.Leading);
        Assert.Equal(0, gs.RenderingMode);
        Assert.Equal(0, gs.Rise);
    }

    [Fact]
    public void SaveRestore_PreservesLineWidth()
    {
        var gs = new GraphicsState();
        gs.LineWidth = 5.0;
        gs.Save();
        gs.LineWidth = 10.0;
        Assert.Equal(10.0, gs.LineWidth);
        gs.Restore();
        Assert.Equal(5.0, gs.LineWidth);
    }

    [Fact]
    public void SaveRestore_PreservesColors()
    {
        var gs = new GraphicsState();
        gs.FillR = 1.0; gs.FillG = 0; gs.FillB = 0;
        gs.Save();
        gs.FillR = 0; gs.FillG = 1.0; gs.FillB = 0;
        gs.Restore();
        Assert.Equal(1.0, gs.FillR);
        Assert.Equal(0, gs.FillG);
    }

    [Fact]
    public void SaveRestore_Nested()
    {
        var gs = new GraphicsState();
        gs.LineWidth = 1.0;
        gs.Save();
        gs.LineWidth = 2.0;
        gs.Save();
        gs.LineWidth = 3.0;
        gs.Restore();
        Assert.Equal(2.0, gs.LineWidth);
        gs.Restore();
        Assert.Equal(1.0, gs.LineWidth);
    }

    [Fact]
    public void Restore_WithEmptyStack_IsNoOp()
    {
        var gs = new GraphicsState();
        gs.LineWidth = 5.0;
        gs.Restore(); // should not crash
        Assert.Equal(5.0, gs.LineWidth);
    }

    [Fact]
    public void ConcatMatrix_Translation()
    {
        var gs = new GraphicsState();
        gs.ConcatMatrix(1, 0, 0, 1, 100, 200);
        Assert.Equal(100, gs.Ctm[4], 0.001);
        Assert.Equal(200, gs.Ctm[5], 0.001);
    }

    [Fact]
    public void ConcatMatrix_Scale()
    {
        var gs = new GraphicsState();
        gs.ConcatMatrix(2, 0, 0, 3, 0, 0);
        Assert.Equal(2, gs.Ctm[0], 0.001);
        Assert.Equal(3, gs.Ctm[3], 0.001);
    }

    [Fact]
    public void ConcatMatrix_Chained()
    {
        var gs = new GraphicsState();
        // Translate by (10, 20) then scale by (2, 2)
        gs.ConcatMatrix(1, 0, 0, 1, 10, 20);
        gs.ConcatMatrix(2, 0, 0, 2, 0, 0);
        var (x, y) = gs.TransformPoint(0, 0);
        Assert.Equal(10, x, 0.001);
        Assert.Equal(20, y, 0.001);
    }

    [Fact]
    public void TransformPoint_Identity()
    {
        var gs = new GraphicsState();
        var (x, y) = gs.TransformPoint(5, 10);
        Assert.Equal(5, x);
        Assert.Equal(10, y);
    }

    [Fact]
    public void TransformPoint_WithTranslation()
    {
        var gs = new GraphicsState();
        gs.ConcatMatrix(1, 0, 0, 1, 100, 200);
        var (x, y) = gs.TransformPoint(5, 10);
        Assert.Equal(105, x, 0.001);
        Assert.Equal(210, y, 0.001);
    }

    [Fact]
    public void SetTextMatrix_ResetsTextMatrix()
    {
        var gs = new GraphicsState();
        gs.SetTextMatrix(2, 0, 0, 2, 72, 720);
        Assert.Equal(72, gs.TextMatrix[4]);
        Assert.Equal(720, gs.TextMatrix[5]);
    }

    [Fact]
    public void MoveTextPosition_OffsetsFromLineMatrix()
    {
        var gs = new GraphicsState();
        gs.SetTextMatrix(1, 0, 0, 1, 72, 720);
        gs.MoveTextPosition(10, -15);
        Assert.Equal(82, gs.TextMatrix[4], 0.001);
        Assert.Equal(705, gs.TextMatrix[5], 0.001);
    }

    [Fact]
    public void MoveToNextLine_UsesLeading()
    {
        var gs = new GraphicsState();
        gs.Leading = 14;
        gs.SetTextMatrix(1, 0, 0, 1, 72, 720);
        gs.MoveToNextLine();
        Assert.Equal(72, gs.TextMatrix[4], 0.001);
        Assert.Equal(706, gs.TextMatrix[5], 0.001);
    }

    [Fact]
    public void MultiplyMatrices_Identity()
    {
        var id = new double[] { 1, 0, 0, 1, 0, 0 };
        var m = new double[] { 2, 0, 0, 3, 10, 20 };
        var result = GraphicsState.MultiplyMatrices(m, id);
        Assert.Equal(m, result);
    }

    [Fact]
    public void MultiplyMatrices_Translation()
    {
        var t1 = new double[] { 1, 0, 0, 1, 10, 0 };
        var t2 = new double[] { 1, 0, 0, 1, 0, 20 };
        var result = GraphicsState.MultiplyMatrices(t1, t2);
        Assert.Equal(10, result[4], 0.001);
        Assert.Equal(20, result[5], 0.001);
    }

    [Fact]
    public void GetTextPosition_ReturnsCompositePosition()
    {
        var gs = new GraphicsState();
        gs.ConcatMatrix(1, 0, 0, 1, 0, 0); // identity CTM
        gs.SetTextMatrix(1, 0, 0, 1, 72, 720);
        var (x, y) = gs.GetTextPosition();
        Assert.Equal(72, x, 0.001);
        Assert.Equal(720, y, 0.001);
    }

    [Fact]
    public void GetEffectiveFontSize_WithUnityMatrix()
    {
        var gs = new GraphicsState();
        gs.FontSize = 12;
        Assert.Equal(12, gs.GetEffectiveFontSize(), 0.001);
    }

    [Fact]
    public void GetEffectiveFontSize_WithScaledMatrix()
    {
        var gs = new GraphicsState();
        gs.FontSize = 12;
        gs.SetTextMatrix(1, 0, 0, 2, 0, 0); // scale Y by 2
        Assert.Equal(24, gs.GetEffectiveFontSize(), 0.001);
    }

    [Fact]
    public void SaveRestore_PreservesCtm()
    {
        var gs = new GraphicsState();
        gs.ConcatMatrix(1, 0, 0, 1, 50, 50);
        gs.Save();
        gs.ConcatMatrix(2, 0, 0, 2, 0, 0);
        gs.Restore();
        Assert.Equal(1, gs.Ctm[0], 0.001);
        Assert.Equal(50, gs.Ctm[4], 0.001);
    }

    [Fact]
    public void FillAlpha_Default_IsOpaque()
    {
        var gs = new GraphicsState();
        Assert.Equal(1.0, gs.FillAlpha);
        Assert.Equal(1.0, gs.StrokeAlpha);
    }
}
