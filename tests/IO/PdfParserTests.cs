using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PdfParserTests
{
    private static PdfParser Parse(string input) => new(Encoding.ASCII.GetBytes(input));

    [Fact]
    public void ParseObject_Integer()
    {
        var result = Parse("42").ParseObject();
        Assert.IsType<PdfInteger>(result);
        Assert.Equal(42, ((PdfInteger)result).Value);
    }

    [Fact]
    public void ParseObject_Real()
    {
        var result = Parse("3.14").ParseObject();
        Assert.IsType<PdfReal>(result);
        Assert.Equal(3.14, ((PdfReal)result).Value, 0.001);
    }

    [Fact]
    public void ParseObject_Boolean()
    {
        var result = Parse("true").ParseObject();
        Assert.Same(PdfBoolean.True, result);
    }

    [Fact]
    public void ParseObject_Null()
    {
        var result = Parse("null").ParseObject();
        Assert.Same(PdfNull.Instance, result);
    }

    [Fact]
    public void ParseObject_Name()
    {
        var result = Parse("/Type").ParseObject();
        Assert.IsType<PdfName>(result);
        Assert.Equal("Type", ((PdfName)result).Value);
    }

    [Fact]
    public void ParseObject_LiteralString()
    {
        var result = Parse("(Hello)").ParseObject();
        Assert.IsType<PdfString>(result);
        Assert.Equal("Hello", ((PdfString)result).ToText());
    }

    [Fact]
    public void ParseObject_HexString()
    {
        var result = Parse("<48656C6C6F>").ParseObject();
        Assert.IsType<PdfString>(result);
        Assert.True(((PdfString)result).IsHex);
        Assert.Equal("Hello", ((PdfString)result).ToText());
    }

    [Fact]
    public void ParseObject_Array()
    {
        var result = Parse("[1 2 3]").ParseObject();
        var array = Assert.IsType<PdfArray>(result);
        Assert.Equal(3, array.Count);
        Assert.Equal(1, ((PdfInteger)array[0]).Value);
        Assert.Equal(2, ((PdfInteger)array[1]).Value);
        Assert.Equal(3, ((PdfInteger)array[2]).Value);
    }

    [Fact]
    public void ParseObject_NestedArray()
    {
        var result = Parse("[1 [2 3] 4]").ParseObject();
        var array = Assert.IsType<PdfArray>(result);
        Assert.Equal(3, array.Count);
        var inner = Assert.IsType<PdfArray>(array[1]);
        Assert.Equal(2, inner.Count);
    }

    [Fact]
    public void ParseObject_Dictionary()
    {
        var result = Parse("<< /Type /Catalog /Pages 2 0 R >>").ParseObject();
        var dict = Assert.IsType<PdfDictionary>(result);
        Assert.Equal("Catalog", dict.GetName("Type"));
        var pagesRef = Assert.IsType<PdfIndirectRef>(dict.Get("Pages"));
        Assert.Equal(2, pagesRef.ObjectNumber);
    }

    [Fact]
    public void ParseObject_IndirectRef()
    {
        var result = Parse("5 0 R").ParseObject();
        var iref = Assert.IsType<PdfIndirectRef>(result);
        Assert.Equal(5, iref.ObjectNumber);
        Assert.Equal(0, iref.Generation);
    }

    [Fact]
    public void ParseObject_EmptyDictionary()
    {
        var result = Parse("<< >>").ParseObject();
        var dict = Assert.IsType<PdfDictionary>(result);
        Assert.Empty(dict.Keys);
    }

    [Fact]
    public void ParseObject_EmptyArray()
    {
        var result = Parse("[]").ParseObject();
        var array = Assert.IsType<PdfArray>(result);
        Assert.Empty(array);
    }

    [Fact]
    public void ParseIndirectObject()
    {
        var data = Encoding.ASCII.GetBytes("1 0 obj\n<< /Type /Catalog >>\nendobj");
        var parser = new PdfParser(data);
        var obj = parser.ParseIndirectObject();
        Assert.Equal(1, obj.ObjectNumber);
        Assert.Equal(0, obj.Generation);
        var dict = Assert.IsType<PdfDictionary>(obj.Value);
        Assert.Equal("Catalog", dict.GetName("Type"));
    }

    [Fact]
    public void ParseIndirectObject_WithStream()
    {
        var streamBody = "Hello stream content";
        var input = $"4 0 obj\n<< /Length {streamBody.Length} >>\nstream\n{streamBody}\nendstream\nendobj";
        var data = Encoding.ASCII.GetBytes(input);
        var parser = new PdfParser(data);
        var obj = parser.ParseIndirectObject();
        Assert.Equal(4, obj.ObjectNumber);
        var stream = Assert.IsType<PdfStream>(obj.Value);
        Assert.Equal(streamBody, Encoding.ASCII.GetString(stream.RawData));
    }

    [Fact]
    public void ParseObject_DictionaryWithMixedValues()
    {
        var result = Parse("<< /Name (Hello) /Count 42 /Flag true /Sub << /X 1 >> >>").ParseObject();
        var dict = Assert.IsType<PdfDictionary>(result);
        Assert.Equal("Hello", ((PdfString)dict.Get("Name")!).ToText());
        Assert.Equal(42, ((PdfInteger)dict.Get("Count")!).Value);
        Assert.Same(PdfBoolean.True, dict.Get("Flag"));
        var sub = Assert.IsType<PdfDictionary>(dict.Get("Sub"));
        Assert.Equal(1, ((PdfInteger)sub.Get("X")!).Value);
    }
}
