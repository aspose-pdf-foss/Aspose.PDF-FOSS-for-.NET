namespace Aspose.Pdf.IO;

internal enum TokenKind : byte
{
    ArrayStart,       // [
    ArrayEnd,         // ]
    DictStart,        // <<
    DictEnd,          // >>
    Integer,
    Real,
    LiteralString,
    HexString,
    Name,
    Boolean,
    Null,
    Keyword,          // obj, endobj, stream, endstream, xref, trailer, startxref, R
    Eof,
}

internal readonly struct Token
{
    public TokenKind Kind { get; }
    public long Position { get; }
    public long IntValue { get; }
    public double RealValue { get; }
    public string? StringValue { get; }
    public byte[]? BytesValue { get; }
    public bool BoolValue { get; }

    private Token(TokenKind kind, long position, long intValue = 0, double realValue = 0,
        string? stringValue = null, byte[]? bytesValue = null, bool boolValue = false)
    {
        Kind = kind;
        Position = position;
        IntValue = intValue;
        RealValue = realValue;
        StringValue = stringValue;
        BytesValue = bytesValue;
        BoolValue = boolValue;
    }

    public static Token Delimiter(TokenKind kind, long position) => new(kind, position);
    public static Token IntegerToken(long value, long position) => new(TokenKind.Integer, position, intValue: value);
    public static Token RealToken(double value, long position) => new(TokenKind.Real, position, realValue: value);
    public static Token LiteralStringToken(byte[] value, long position) => new(TokenKind.LiteralString, position, bytesValue: value);
    public static Token HexStringToken(byte[] value, long position) => new(TokenKind.HexString, position, bytesValue: value);
    public static Token NameToken(string value, long position) => new(TokenKind.Name, position, stringValue: value);
    public static Token BooleanToken(bool value, long position) => new(TokenKind.Boolean, position, boolValue: value);
    public static Token NullToken(long position) => new(TokenKind.Null, position);
    public static Token KeywordToken(string value, long position) => new(TokenKind.Keyword, position, stringValue: value);
    public static Token EofToken(long position) => new(TokenKind.Eof, position);
}
