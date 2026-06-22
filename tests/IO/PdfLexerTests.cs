using System.Text;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PdfLexerTests
{
    private static PdfLexer Lex(string input) => new(Encoding.ASCII.GetBytes(input));

    [Fact]
    public void NextToken_Integer()
    {
        var lexer = Lex("42");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Integer, token.Kind);
        Assert.Equal(42, token.IntValue);
    }

    [Fact]
    public void NextToken_NegativeInteger()
    {
        var lexer = Lex("-17");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Integer, token.Kind);
        Assert.Equal(-17, token.IntValue);
    }

    [Fact]
    public void NextToken_Real()
    {
        var lexer = Lex("3.14");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Real, token.Kind);
        Assert.Equal(3.14, token.RealValue, 0.001);
    }

    [Fact]
    public void NextToken_RealStartingWithDot()
    {
        var lexer = Lex(".5");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Real, token.Kind);
        Assert.Equal(0.5, token.RealValue, 0.001);
    }

    [Fact]
    public void NextToken_Boolean_True()
    {
        var lexer = Lex("true");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Boolean, token.Kind);
        Assert.True(token.BoolValue);
    }

    [Fact]
    public void NextToken_Boolean_False()
    {
        var lexer = Lex("false");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Boolean, token.Kind);
        Assert.False(token.BoolValue);
    }

    [Fact]
    public void NextToken_Null()
    {
        var lexer = Lex("null");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Null, token.Kind);
    }

    [Fact]
    public void NextToken_Name()
    {
        var lexer = Lex("/Type");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Name, token.Kind);
        Assert.Equal("Type", token.StringValue);
    }

    [Fact]
    public void NextToken_NameWithHexEscape()
    {
        var lexer = Lex("/A#42");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Name, token.Kind);
        Assert.Equal("AB", token.StringValue);
    }

    [Fact]
    public void NextToken_LiteralString()
    {
        var lexer = Lex("(Hello World)");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.LiteralString, token.Kind);
        Assert.Equal("Hello World", Encoding.ASCII.GetString(token.BytesValue!));
    }

    [Fact]
    public void NextToken_LiteralStringWithNestedParens()
    {
        var lexer = Lex("(Hello (World))");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.LiteralString, token.Kind);
        Assert.Equal("Hello (World)", Encoding.ASCII.GetString(token.BytesValue!));
    }

    [Fact]
    public void NextToken_LiteralStringWithEscapes()
    {
        var lexer = Lex(@"(line1\nline2)");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.LiteralString, token.Kind);
        Assert.Equal("line1\nline2", Encoding.ASCII.GetString(token.BytesValue!));
    }

    [Fact]
    public void NextToken_LiteralStringWithOctalEscape()
    {
        var lexer = Lex("(\\101)"); // \101 = 'A'
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.LiteralString, token.Kind);
        Assert.Equal("A", Encoding.ASCII.GetString(token.BytesValue!));
    }

    [Fact]
    public void NextToken_HexString()
    {
        var lexer = Lex("<48656C6C6F>"); // "Hello"
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.HexString, token.Kind);
        Assert.Equal("Hello", Encoding.ASCII.GetString(token.BytesValue!));
    }

    [Fact]
    public void NextToken_HexStringWithWhitespace()
    {
        var lexer = Lex("<48 65 6C 6C 6F>");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.HexString, token.Kind);
        Assert.Equal("Hello", Encoding.ASCII.GetString(token.BytesValue!));
    }

    [Fact]
    public void NextToken_HexStringOddNibble()
    {
        var lexer = Lex("<ABC>"); // AB C0
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.HexString, token.Kind);
        Assert.Equal(2, token.BytesValue!.Length);
        Assert.Equal(0xAB, token.BytesValue[0]);
        Assert.Equal(0xC0, token.BytesValue[1]);
    }

    [Fact]
    public void NextToken_ArrayDelimiters()
    {
        var lexer = Lex("[1 2]");
        Assert.Equal(TokenKind.ArrayStart, lexer.NextToken().Kind);
        Assert.Equal(TokenKind.Integer, lexer.NextToken().Kind);
        Assert.Equal(TokenKind.Integer, lexer.NextToken().Kind);
        Assert.Equal(TokenKind.ArrayEnd, lexer.NextToken().Kind);
    }

    [Fact]
    public void NextToken_DictDelimiters()
    {
        var lexer = Lex("<< /Key /Value >>");
        Assert.Equal(TokenKind.DictStart, lexer.NextToken().Kind);
        Assert.Equal(TokenKind.Name, lexer.NextToken().Kind);
        Assert.Equal(TokenKind.Name, lexer.NextToken().Kind);
        Assert.Equal(TokenKind.DictEnd, lexer.NextToken().Kind);
    }

    [Fact]
    public void NextToken_Keyword()
    {
        var lexer = Lex("obj");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Keyword, token.Kind);
        Assert.Equal("obj", token.StringValue);
    }

    [Fact]
    public void NextToken_SkipsComments()
    {
        var lexer = Lex("% this is a comment\n42");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Integer, token.Kind);
        Assert.Equal(42, token.IntValue);
    }

    [Fact]
    public void NextToken_SkipsWhitespace()
    {
        var lexer = Lex("  \t\r\n  42");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Integer, token.Kind);
        Assert.Equal(42, token.IntValue);
    }

    [Fact]
    public void NextToken_Eof()
    {
        var lexer = Lex("");
        var token = lexer.NextToken();
        Assert.Equal(TokenKind.Eof, token.Kind);
    }

    [Fact]
    public void NextToken_MultipleTokens()
    {
        var lexer = Lex("/Type /Catalog");
        var t1 = lexer.NextToken();
        var t2 = lexer.NextToken();
        Assert.Equal("Type", t1.StringValue);
        Assert.Equal("Catalog", t2.StringValue);
    }

    [Fact]
    public void PeekToken_DoesNotAdvance()
    {
        var lexer = Lex("42 43");
        var peeked = lexer.PeekToken();
        var next = lexer.NextToken();
        Assert.Equal(peeked.Kind, next.Kind);
        Assert.Equal(peeked.IntValue, next.IntValue);
    }

    [Fact]
    public void NextToken_PositionTracking()
    {
        var lexer = Lex("42 43");
        var t1 = lexer.NextToken();
        Assert.Equal(0, t1.Position);
        var t2 = lexer.NextToken();
        Assert.Equal(3, t2.Position);
    }
}
