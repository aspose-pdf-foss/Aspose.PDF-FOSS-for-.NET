using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Forms;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class FieldWidgetIndexTests
{
    private static TextBoxField ThreeWidgets(Document document)
    {
        var page = document.Pages.Add();
        var rects = new[]
        {
            new Rectangle(10, 600, 110, 620),
            new Rectangle(10, 630, 110, 650),
            new Rectangle(10, 660, 110, 680),
        };
        return new TextBoxField(page, rects);
    }

    [Fact]
    public void EveryWidgetCountedIsReachableByIndex()
    {
        using var document = new Document();
        var field = ThreeWidgets(document);

        Assert.Equal(3, field.Count);
        for (int index = 1; index <= field.Count; index++)
            Assert.NotNull(field[index]);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => field[field.Count + 1]);
    }

    [Fact]
    public void IndexedWidgetsFollowTheEnumerationOrder()
    {
        using var document = new Document();
        var field = ThreeWidgets(document);

        var walked = field.Cast<WidgetAnnotation>().Select(w => w.Rect!.LLY).ToList();
        var indexed = Enumerable.Range(1, field.Count).Select(i => field[i].Rect!.LLY).ToList();

        Assert.Equal(walked, indexed);
    }

    [Fact]
    public void EachWidgetTakesItsOwnAppearance()
    {
        using var document = new Document();
        var field = ThreeWidgets(document);

        short i = 0;
        var sizes = new double[] { 10, 12, 14 };
        foreach (WidgetAnnotation widget in field)
            widget.DefaultAppearance = new DefaultAppearance("Helvetica", sizes[i++], System.Drawing.Color.DarkBlue);

        Assert.Equal(3, i);
    }
}
