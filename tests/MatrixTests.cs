using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public class MatrixTests
{
    [Fact]
    public void Identity_DoesNotTransform()
    {
        var m = Matrix.Identity;
        var (x, y) = m.TransformPoint(10, 20);
        Assert.Equal(10, x, 1e-10);
        Assert.Equal(20, y, 1e-10);
    }

    [Fact]
    public void Translate_MovesPoint()
    {
        var m = Matrix.Translate(100, 200);
        var (x, y) = m.TransformPoint(10, 20);
        Assert.Equal(110, x, 1e-10);
        Assert.Equal(220, y, 1e-10);
    }

    [Fact]
    public void Scale_ScalesPoint()
    {
        var m = Matrix.Scale(2, 3);
        var (x, y) = m.TransformPoint(10, 20);
        Assert.Equal(20, x, 1e-10);
        Assert.Equal(60, y, 1e-10);
    }

    [Fact]
    public void Rotate_90Degrees()
    {
        var m = Matrix.Rotate(90);
        var (x, y) = m.TransformPoint(1, 0);
        Assert.Equal(0, x, 1e-10);
        Assert.Equal(1, y, 1e-10);
    }

    [Fact]
    public void Multiply_ChainsTransforms()
    {
        var scale = Matrix.Scale(2, 2);
        var translate = Matrix.Translate(10, 10);
        var combined = scale.Multiply(translate);
        var (x, y) = combined.TransformPoint(5, 5);
        Assert.Equal(20, x, 1e-10);
        Assert.Equal(20, y, 1e-10);
    }

    [Fact]
    public void Inverse_UndoesTransform()
    {
        var m = Matrix.Translate(100, 200);
        var inv = m.Inverse();
        var combined = m.Multiply(inv);
        var (x, y) = combined.TransformPoint(42, 99);
        Assert.Equal(42, x, 1e-8);
        Assert.Equal(99, y, 1e-8);
    }
}
