using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests.Stamps;

public class PaginationArtifactTests
{
    private static Point Stamped(BatesNArtifact bates)
    {
        using var document = new Document();
        document.Pages.Add();
        document.Pages.AddBatesNumbering(bates);
        return document.Pages[1].Artifacts[1].Position!;
    }

    [Fact]
    public void Margins_DefaultToAnInchAtTheSidesAndHalfAnInchAtTheEdges()
    {
        var bates = new BatesNArtifact();
        Assert.Equal(72, bates.LeftMargin);
        Assert.Equal(72, bates.RightMargin);
        Assert.Equal(36, bates.TopMargin);
        Assert.Equal(36, bates.BottomMargin);
    }

    [Fact]
    public void RightAndBottomMargins_MoveTheNumber()
    {
        var plain = Stamped(new BatesNArtifact());
        var moved = Stamped(new BatesNArtifact { RightMargin = 100, BottomMargin = 50 });
        Assert.Equal(plain.X - 28, moved.X, 6);
        Assert.Equal(plain.Y + 14, moved.Y, 6);
    }

    [Fact]
    public void LeftAndTopMargins_PlaceLeftAndTopAlignedNumbers()
    {
        var placed = Stamped(new BatesNArtifact
        {
            ArtifactHorizontalAlignment = HorizontalAlignment.Left,
            ArtifactVerticalAlignment = VerticalAlignment.Top,
            LeftMargin = 50,
            TopMargin = 20,
        });
        Assert.Equal(50, placed.X, 6);
        Assert.Equal(PageSize.A4.Height - 20, placed.Y, 6);
    }
}
